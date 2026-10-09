// NvdaDirect.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace IKMA
{
    // ======================================================================
    // NVDA TELLS IKMA WHEN A LINE HAS FINISHED. (Session 50, 0.7.461.)
    //
    // Zamar, 2026-10-06, on knowing when NVDA has finished a line instead of
    // estimating it: "I want that for v.5 I think that sounds important."
    //
    // THE PROBLEM. UniversalSpeech hands NVDA a line with
    // nvdaController_speakText, which returns at once and never says when the
    // line ended. So under NVDA, SpeechPump.EngineBusy answered "cannot tell"
    // and Speech.cs fell back to three words a second. Under the Windows
    // voice the engine answers exactly - and that exact path is the one every
    // playtest so far has exercised.
    //
    // THE CALL. NVDA 2024.1 added nvdaController_speakSsml. Asked to speak
    // SYNCHRONOUSLY it does not return until the line "is completed or
    // canceled" (NVDA's readme), and its return value says which: 0 for
    // completed, 1223 (ERROR_CANCELLED) for cancelled. Read from NVDA's own
    // source, NVDAHelper nvdaController_speakSsml, releases 2024.1 and
    // 2026.2. Notes: IKM Access\reference\nvda-controller-notes.md.
    //
    // THE SHAPE. That call blocks for as long as the line takes, so it
    // cannot run on SpeechPump's worker: an interrupting line queued behind
    // it could not be delivered. It runs on ONE extra thread here, the
    // waiter, which speaks IKMA's NVDA lines one at a time and in order.
    // SpeechPump's worker stays the only thread inside UniversalSpeech - the
    // waiter never touches that library, only NVDA's own DLL.
    //
    // WHAT THE PLAYER HEARS IS MEANT TO BE THE SAME. Same text, the
    // player's own punctuation level (SYMBOL_LEVEL_UNCHANGED), normal
    // priority. What changes is that IKMA knows when the line ended.
    //
    // IT CAN NEVER COST SPEECH. Everything that can go wrong ends on the old
    // path (UniversalSpeech and the word estimate):
    //   - NVDA older than 2024.1: the version check answers 1717 and this
    //     class is never used.
    //   - An older nvdaControllerClient.dll beside the game (another mod's
    //     copy): the new function is missing; never used, said once.
    //   - NVDA closed or restarted: the call fails, the line is handed back
    //     and spoken through UniversalSpeech, and the check is repeated
    //     every 30 seconds.
    //   - A call that never returns: see THE WATCHDOG below.
    //   - [Speech] NvdaLineEnd = false switches it off.
    //
    // HEARD 2026-10-08 (Session 52, 0.7.463), AND IT BROKE NVDA. Switched OFF
    // BY DEFAULT in 0.7.464. At the first encounter NVDA 2026.2 went
    // silent for good and had to be restarted. NVDA's own log (nvda-old.log,
    // %TEMP%) has the one error in it, at the same second IKMA's watchdog gave
    // up on a line: "RuntimeError: OrderedDict mutated during iteration", in
    // synthDrivers\oneCore _callback -> _processQueue -> extensionPoints
    // notify. Cause, read from NVDA's source (source/NVDAHelper/__init__.py,
    // release-2026.2, nvdaController_speakSsml): the waiting call registers
    // synthDoneSpeaking.onDoneSpeaking on the synth thread, and when the line
    // ends its `finally` UNREGISTERS it on the RPC thread - while the synth
    // thread may still be iterating the handler list. The error escapes the
    // synth callback and NVDA's speech queue stops. Every line that ends is a
    // chance for it, so it is a matter of minutes, not luck.
    // The asynchronous call has no such race but reports nothing: with
    // asynchronous=true NVDA passes no mark callback, so there is no end of
    // line. Nothing else in 2026.2 says when speech ended (isSpeaking is in
    // client 3.0, not in 2026.2). So: off, until NVDA fixes the unregister or
    // ships isSpeaking. Worth reporting to NV Access with the log above.
    //
    // (Before that: UNVERIFIED AS OF 0.7.461. Built from NVDA's source and run
    // against a stand-in for NVDA.)
    // ======================================================================

    /// <summary>
    /// The four NVDA calls this class makes, behind an interface so the
    /// logic can be run against a stand-in where there is no NVDA.
    /// Every one returns a Windows error code: 0 is success.
    /// </summary>
    internal interface INvdaCalls
    {
        /// <summary>Speak, and do not return until the line has finished or been cancelled.</summary>
        int SpeakSsmlAndWait(string ssml);
        /// <summary>The old call: hand the text over and return at once.</summary>
        int SpeakText(string text);
        int CancelSpeech();
        /// <summary>Only NVDA 2024.1 and later answer this. Used as the version check.</summary>
        int GetProcessId(out uint pid);
    }

    /// <summary>
    /// The real calls, into the nvdaControllerClient.dll beside the game.
    /// Signatures from NVDA's nvdaController.h and its own C# example
    /// (nvda_2026.2_controllerClient.zip, x86): error_status_t __stdcall,
    /// wide strings, SYMBOL_LEVEL and SPEECH_PRIORITY as 32-bit enums,
    /// "boolean" one byte.
    /// </summary>
    internal sealed class NvdaNativeCalls : INvdaCalls
    {
        private const int SYMBOL_LEVEL_UNCHANGED = -1;   // the player's own setting
        private const int SPEECH_PRIORITY_NORMAL = 0;

        [DllImport("nvdaControllerClient", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_speakSsml(
            [MarshalAs(UnmanagedType.LPWStr)] string ssml,
            int symbolLevel,
            int priority,
            [MarshalAs(UnmanagedType.U1)] bool asynchronous);

        [DllImport("nvdaControllerClient", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_speakText(
            [MarshalAs(UnmanagedType.LPWStr)] string text);

        [DllImport("nvdaControllerClient")]
        private static extern int nvdaController_cancelSpeech();

        [DllImport("nvdaControllerClient")]
        private static extern int nvdaController_getProcessId(out uint pid);

        public int SpeakSsmlAndWait(string ssml)
            => nvdaController_speakSsml(ssml, SYMBOL_LEVEL_UNCHANGED, SPEECH_PRIORITY_NORMAL, false);
        public int SpeakText(string text) => nvdaController_speakText(text);
        public int CancelSpeech() => nvdaController_cancelSpeech();
        public int GetProcessId(out uint pid) => nvdaController_getProcessId(out pid);
    }

    internal static class NvdaDirect
    {
        internal static INvdaCalls Calls = new NvdaNativeCalls();

        /// <summary>Where log lines go: SpeechPump's worker-safe queue. Set by UniversalSpeechBackend.</summary>
        internal static Action<string> LogSink;

        /// <summary>[Speech] NvdaLineEnd, as a plain flag so no thread here touches a ConfigEntry.</summary>
        internal static volatile bool SettingOn = true;

        // Windows error codes NVDA returns (source/winAPI/constants.py).
        private const int ACCESS_DENIED = 5;         // NVDA is in sleep mode for this window
        private const int INVALID_PARAMETER = 87;    // NVDA could not parse the line as SSML
        private const int CANCELLED = 1223;          // the line was cut before it finished
        private const int UNKNOWN_INTERFACE = 1717;  // this NVDA is older than 2024.1
        private const int THREW = int.MinValue;      // the call itself threw

        // ==================================================================
        // THE WATCHDOG. A synchronous call can fail to return:
        //   - NVDA's speech mode is off (a braille-only player). speech.speak
        //     returns before the line starts, and nothing ever ends it.
        //   - NVDA 2024.1 registers its "cancelled" hook only once the line
        //     starts. A line cancelled while still waiting behind NVDA's own
        //     speech is never reported. (2026.2 registers it up front.)
        //   - The player paused NVDA (Shift). It returns when they resume.
        // The waiter thread cannot be pulled out of the call, so it is left
        // where it is and a NEW waiter carries on with the next line. NVDA
        // keeps its own order, so nothing is lost or reordered by this.
        //
        // A call given up on is "stuck". Three stuck at once and this class
        // stands down; lines go through UniversalSpeech again. If the stuck
        // calls come back (speech resumed), it takes over again.
        // ==================================================================
        private const int CUT_WAIT_MS = 400;         // after IKMA's own cancel
        private const int WAITING_MARGIN_MS = 2000;  // lines are waiting behind this one
        private const int IDLE_MARGIN_MS = 5000;     // nothing is waiting
        private const int MAX_STUCK = 3;
        private const int REPROBE_MS = 30000;

        // A "cancelled" that comes back this soon, with no cut from IKMA, may
        // be a cancel that was already on its way to NVDA when the line
        // arrived: NVDA 2026.2 then reports the line cancelled and speaks it
        // anyway. For such a line IKMA answers "cannot tell" for the line's
        // estimated length, which is what it did for every NVDA line before.
        private const int QUICK_CANCEL_MS = 250;

        private sealed class Line
        {
            internal string Text;
            internal bool Blank;        // whitespace only: NVDA's speakText says "blank"; SSML would say nothing
            internal int EstimateMs;
            internal long IssuedMs = -1;
            internal bool CutByUs;
            internal long CutAtMs;
        }

        private enum Mode { Unknown, On, Off }
        private enum StoodDown { No, ForThisRun, WhileStuck }

        private static readonly object _lk = new object();
        private static readonly Queue<Line> _queue = new Queue<Line>();
        private static readonly List<string> _handBack = new List<string>();
        private static Line _inflight;
        private static Thread _waiter;
        private static int _gen;                 // which waiter owns _inflight and the queue
        private static int _waiterGen = -1;      // the generation _waiter was started for
        private static int _stuck;               // calls given up on that have not come back
        private static long _unsureUntilMs;
        private static Mode _mode = Mode.Unknown;
        private static StoodDown _stoodDown = StoodDown.No;
        private static long _reprobeAtMs;
        private static volatile bool _takes;
        private static volatile bool _stop;
        private static string _lastLogged;
        private static int _gaveUpLogged;

        private static long NowMs() => Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;

        private static void Log(string line)
        {
            try { LogSink?.Invoke(line); } catch { }
        }

        // The same sentence twice in a row says nothing new.
        private static void LogChange(string line)
        {
            lock (_lk)
            {
                if (line == _lastLogged) return;
                _lastLogged = line;
            }
            Log(line);
        }

        /// <summary>True when the next line should come here rather than go to UniversalSpeech. Set by Maintain.</summary>
        internal static bool Takes { get { return _takes; } }

        /// <summary>True while this class has, or may have, a line of IKMA's in the air.</summary>
        internal static bool Engaged
        {
            get
            {
                if (_takes) return true;
                lock (_lk) { return _inflight != null || _queue.Count > 0 || NowMs() < _unsureUntilMs; }
            }
        }

        /// <summary>
        /// Pump worker, before every line. Decides whether the next line
        /// comes here. Cheap unless a version check is due.
        /// </summary>
        internal static void Maintain(bool engineIsNvda)
        {
            Watchdog();
            bool probe = false, takes = false;
            lock (_lk)
            {
                if (_stoodDown == StoodDown.WhileStuck && _stuck == 0)
                {
                    _stoodDown = StoodDown.No;
                    _lastLogged = null;
                    Log("IKMA SPEECH: NVDA has answered for the lines it had not finished. IKMA is asking it for the end of each line again.");
                }
                if (!SettingOn || !engineIsNvda || _stoodDown != StoodDown.No) takes = false;
                else if (_mode == Mode.On) takes = true;
                else probe = NowMs() >= _reprobeAtMs;
            }
            if (probe) takes = Probe();
            _takes = takes;
        }

        // The version check. nvdaController_getProcessId is on the interface
        // NVDA added in 2024.1; an older NVDA answers 1717. It speaks nothing.
        private static bool Probe()
        {
            int rc;
            try { rc = Calls.GetProcessId(out uint _); }
            catch (EntryPointNotFoundException)
            {
                lock (_lk) { _stoodDown = StoodDown.ForThisRun; }
                LogChange("IKMA SPEECH: the nvdaControllerClient.dll beside the game is an older copy without the call that reports the end of a line. Line length is estimated.");
                return false;
            }
            catch (DllNotFoundException)
            {
                lock (_lk) { _stoodDown = StoodDown.ForThisRun; }
                LogChange("IKMA SPEECH: nvdaControllerClient.dll could not be loaded for the call that reports the end of a line. Line length is estimated.");
                return false;
            }
            catch (Exception e)
            {
                lock (_lk) { _mode = Mode.Off; _reprobeAtMs = NowMs() + REPROBE_MS; }
                LogChange($"IKMA SPEECH: could not ask NVDA whether it reports the end of a line ({e.GetType().Name}). Line length is estimated.");
                return false;
            }

            if (rc == 0)
            {
                lock (_lk) { _mode = Mode.On; }
                LogChange("IKMA SPEECH: NVDA reports the end of each line (NVDA 2024.1 or later). Line length is exact, not estimated.");
                return true;
            }
            lock (_lk) { _mode = Mode.Off; _reprobeAtMs = NowMs() + REPROBE_MS; }
            LogChange(rc == UNKNOWN_INTERFACE
                ? "IKMA SPEECH: this NVDA is older than 2024.1 and does not report the end of a line. Line length is estimated."
                : $"IKMA SPEECH: NVDA did not answer the check for the call that reports the end of a line (error {rc}). Line length is estimated.");
            return false;
        }

        /// <summary>
        /// Pump worker. Same contract as ISpeechBackend.Say: interrupt cuts
        /// what is playing; an empty line with interrupt only stops.
        /// </summary>
        internal static void Say(string text, bool interrupt)
        {
            Watchdog();
            bool cancel = false;
            if (interrupt)
            {
                lock (_lk)
                {
                    long now = NowMs();
                    // CUT ONLY WHAT IS IKMA'S. If nothing of IKMA's is in the
                    // air there is nothing to cut, and a cancel sent anyway
                    // can land AFTER NVDA has accepted the new line: 2026.2
                    // then reports that line cancelled and speaks it (see
                    // QUICK_CANCEL_MS). A bare stop always cancels.
                    bool oursMayBeSpeaking = _inflight != null || now < _unsureUntilMs || _stuck > 0;
                    _queue.Clear();
                    if (_inflight != null && !_inflight.CutByUs)
                    {
                        _inflight.CutByUs = true;
                        _inflight.CutAtMs = now;
                    }
                    _unsureUntilMs = 0;
                    cancel = oursMayBeSpeaking || string.IsNullOrEmpty(text);
                }
            }
            if (cancel)
            {
                // Outside the lock: never hold it across a call into NVDA.
                try { Calls.CancelSpeech(); }
                catch (Exception e) { LogChange($"IKMA SPEECH: the stop sent to NVDA failed ({e.GetType().Name})."); }
            }
            if (string.IsNullOrEmpty(text)) return;

            int words = 1;
            foreach (char c in text) if (c == ' ') words++;
            var line = new Line
            {
                Text = text,
                Blank = text.Trim().Length == 0,
                // Three words a second and a beat, as Speech.EstimateSpeechSeconds,
                // but not capped: this one decides how long to wait for NVDA.
                EstimateMs = (int)(words * 1000L / 3) + 400,
            };
            lock (_lk)
            {
                _queue.Enqueue(line);
                if (_waiter == null || !_waiter.IsAlive || _waiterGen != _gen) StartWaiterLocked();
                Monitor.PulseAll(_lk);
            }
        }

        /// <summary>
        /// Pump worker. An interrupting line is about to go through
        /// UniversalSpeech instead (which sends NVDA the stop itself): drop
        /// what is waiting here and stop counting the line in the air.
        /// </summary>
        internal static void CutForOtherPath()
        {
            lock (_lk)
            {
                _queue.Clear();
                _handBack.Clear();
                if (_inflight != null && !_inflight.CutByUs)
                {
                    _inflight.CutByUs = true;
                    _inflight.CutAtMs = NowMs();
                }
                _unsureUntilMs = 0;
            }
        }

        /// <summary>
        /// Pump worker. 1 = a line of IKMA's is being spoken or waiting,
        /// 0 = quiet, -1 = cannot tell (Speech.cs then uses its estimate).
        /// </summary>
        internal static int QueryBusy()
        {
            Watchdog();
            lock (_lk)
            {
                if (_queue.Count > 0) return 1;
                if (_inflight != null && !_inflight.CutByUs) return 1;
                if (NowMs() < _unsureUntilMs) return -1;
                return 0;
            }
        }

        /// <summary>
        /// Pump worker. Lines this class could not speak, oldest first, for
        /// UniversalSpeech to speak instead. Empties the list.
        /// </summary>
        internal static string[] TakeHandBack()
        {
            lock (_lk)
            {
                if (_handBack.Count == 0) return null;
                string[] lines = _handBack.ToArray();
                _handBack.Clear();
                return lines;
            }
        }

        internal static void Shutdown()
        {
            _stop = true;
            lock (_lk) Monitor.PulseAll(_lk);
        }

        private static void StartWaiterLocked()
        {
            int gen = _gen;
            _waiterGen = gen;
            _waiter = new Thread(() => WaiterLoop(gen))
            {
                // Background, so a call NVDA never answers cannot hold the game open on exit.
                IsBackground = true,
                Name = "IKMA NVDA line waiter",
            };
            _waiter.Start();
        }

        private static void WaiterLoop(int gen)
        {
            try
            {
                while (true)
                {
                    Line line;
                    lock (_lk)
                    {
                        while (_queue.Count == 0 || _inflight != null)
                        {
                            if (_gen != gen || _stop) return;
                            Monitor.Wait(_lk, 1000);
                        }
                        if (_gen != gen || _stop) return;
                        line = _queue.Dequeue();
                        line.IssuedMs = NowMs();
                        _inflight = line;
                    }

                    int rc;
                    string threw = null;
                    try
                    {
                        rc = line.Blank ? Calls.SpeakText(line.Text) : Calls.SpeakSsmlAndWait(ToSsml(line.Text));
                    }
                    catch (Exception e) { rc = THREW; threw = e.GetType().Name; }

                    bool retryAsText = false;
                    lock (_lk)
                    {
                        if (_gen != gen)
                        {
                            // The pump gave up on this call and moved on. It
                            // came back after all: one fewer stuck.
                            if (_stuck > 0) _stuck--;
                            return;
                        }
                        _inflight = null;
                        long now = NowMs();

                        if (line.Blank && rc == 0)
                        {
                            // speakText does not wait. Its length is a guess.
                            _unsureUntilMs = now + line.EstimateMs;
                        }
                        else if (rc == 0 || rc == ACCESS_DENIED || (line.Blank && rc == -1))
                        {
                            // Finished. (Or NVDA is asleep for this window and
                            // refused the line, as it refuses speakText.)
                        }
                        else if (rc == CANCELLED)
                        {
                            if (!line.CutByUs && now - line.IssuedMs <= QUICK_CANCEL_MS)
                                _unsureUntilMs = line.IssuedMs + line.EstimateMs;
                        }
                        else if (rc == INVALID_PARAMETER && !line.CutByUs)
                        {
                            retryAsText = true;
                            _inflight = line;      // still this waiter's line
                        }
                        else
                        {
                            // NVDA is gone, or stopped answering this call.
                            if (!line.CutByUs) _handBack.Add(line.Text);
                            while (_queue.Count > 0) _handBack.Add(_queue.Dequeue().Text);
                            _mode = Mode.Off;
                            _reprobeAtMs = now + REPROBE_MS;
                            _takes = false;
                            _lastLogged = null;
                            Log("IKMA SPEECH: NVDA stopped answering the call that reports the end of a line (" +
                                (threw ?? ("error " + rc)) +
                                "). Lines go through UniversalSpeech and their length is estimated; checking again in 30 seconds.");
                        }
                        Monitor.PulseAll(_lk);
                    }

                    if (!retryAsText) continue;

                    // NVDA could not read the line as SSML. Speak it the old
                    // way, on this thread, so the line is not lost.
                    int rc2;
                    try { rc2 = Calls.SpeakText(line.Text); }
                    catch { rc2 = THREW; }
                    lock (_lk)
                    {
                        if (_gen != gen) { if (_stuck > 0) _stuck--; return; }
                        _inflight = null;
                        if (rc2 == 0) _unsureUntilMs = NowMs() + line.EstimateMs;
                        else if (!line.CutByUs) _handBack.Add(line.Text);
                        Monitor.PulseAll(_lk);
                    }
                    LogChange("IKMA SPEECH: NVDA could not read a line as SSML. It was spoken as plain text and its length is estimated.");
                }
            }
            catch (Exception e)
            {
                // A waiter that dies must not take speech with it.
                lock (_lk)
                {
                    if (_gen == gen)
                    {
                        if (_inflight != null && !_inflight.CutByUs) _handBack.Add(_inflight.Text);
                        _inflight = null;
                        while (_queue.Count > 0) _handBack.Add(_queue.Dequeue().Text);
                        _stoodDown = StoodDown.ForThisRun;
                        _takes = false;
                    }
                }
                Log($"IKMA SPEECH: the NVDA line waiter stopped ({e.GetType().Name}). Lines go through UniversalSpeech and their length is estimated.");
            }
        }

        // See THE WATCHDOG above. Runs on the pump worker, from Maintain, Say
        // and QueryBusy - the last of those every 100 ms while the pump idles.
        private static void Watchdog()
        {
            lock (_lk)
            {
                Line l = _inflight;
                if (l == null || l.IssuedMs < 0) return;
                long now = NowMs();
                string why = null;
                if (l.CutByUs)
                {
                    if (now - l.CutAtMs > CUT_WAIT_MS) why = "after IKMA cut the line";
                }
                else if (_queue.Count > 0)
                {
                    if (now - l.IssuedMs > l.EstimateMs * 2L + WAITING_MARGIN_MS) why = "with more lines waiting";
                }
                else if (now - l.IssuedMs > l.EstimateMs * 2L + IDLE_MARGIN_MS)
                {
                    why = "long after the line should have ended";
                }
                if (why == null) return;

                // Leave that thread in its call and carry on without it.
                _gen++;
                _inflight = null;
                _stuck++;
                if (_gaveUpLogged < 5)
                {
                    _gaveUpLogged++;
                    Log($"IKMA SPEECH: NVDA has not reported the end of a line {(now - l.IssuedMs) / 1000.0:F1}s after it was sent ({why}). IKMA stopped waiting for that line." +
                        (_gaveUpLogged == 5 ? " Further lines like this one are not logged." : ""));
                }

                if (_stuck >= MAX_STUCK)
                {
                    while (_queue.Count > 0) _handBack.Add(_queue.Dequeue().Text);
                    _stoodDown = StoodDown.WhileStuck;
                    _takes = false;
                    _lastLogged = null;
                    Log($"IKMA SPEECH: NVDA has not reported the end of {MAX_STUCK} lines (speech may be paused or switched off in NVDA). Lines go through UniversalSpeech and their length is estimated until NVDA answers for them.");
                    return;
                }
                StartWaiterLocked();
                Monitor.PulseAll(_lk);
            }
        }

        // The line as SSML: the text inside one speak element, with the three
        // characters XML reserves written as entities and the control
        // characters XML forbids replaced by a space.
        internal static string ToSsml(string text)
        {
            var sb = new StringBuilder(text.Length + 24);
            sb.Append("<speak>");
            foreach (char c in text)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    default:
                        if ((c < 0x20 && c != '\t' && c != '\n' && c != '\r') || c == '￾' || c == '￿')
                            sb.Append(' ');
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append("</speak>");
            return sb.ToString();
        }

#if IKMA_TEST
        // For the stand-in test only: a fresh state and a fake NVDA.
        internal static void ResetForTest(INvdaCalls calls, Action<string> log)
        {
            lock (_lk)
            {
                _gen++;
                _queue.Clear(); _handBack.Clear(); _inflight = null; _waiter = null; _waiterGen = -1;
                _stuck = 0; _unsureUntilMs = 0; _mode = Mode.Unknown; _stoodDown = StoodDown.No;
                _reprobeAtMs = 0; _takes = false; _stop = false; _lastLogged = null; _gaveUpLogged = 0;
                Calls = calls; LogSink = log; SettingOn = true;
                Monitor.PulseAll(_lk);
            }
        }
        internal static int StuckForTest { get { lock (_lk) return _stuck; } }
#endif
    }
}
