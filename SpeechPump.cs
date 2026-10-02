// SpeechPump.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace IKMA
{
    /// <summary>
    /// The one place in IKMA that talks to UniversalSpeech, and the only thread
    /// that ever does. (Session 15.)
    ///
    /// SESSION 32: "UniversalSpeech" now means "the speech backend". The
    /// native calls moved to UniversalSpeechBackend.cs, and a second backend,
    /// LinuxBridgeBackend.cs, carries lines to a Linux helper for the Steam
    /// Deck. Everything below about ONE thread, FIFO order and the stall rule
    /// applies to every backend unchanged. See SpeechBackend.cs.
    ///
    /// WHY THIS EXISTS. Measured over 337 speech calls at v0.7.38: composing a
    /// line costs 0.0ms median and 1.7ms at worst, while the speechSay P/Invoke
    /// costs 75ms median, 490ms at p90 and 765ms at worst, with 76 of the 337
    /// calls blocking for over 300ms. Every one of those was blocking Unity's
    /// main thread, which is the sluggishness Zamar has been reporting. String
    /// building was never the cost and must not be optimised.
    ///
    /// TWO SEPARATE FIXES LIVE HERE, and they are separately observable in the
    /// log on purpose:
    ///
    ///   1. The call is made on a dedicated background thread, so the game no
    ///      longer waits on it. Watch the "hand browse keypress" and "Update"
    ///      PERF lines for this one.
    ///   2. The speech engine is pinned, which stops UniversalSpeech
    ///      re-detecting it every ten seconds. Watch the "speechSay" PERF line
    ///      for this one — if the outliers vanish, re-detection was the cause of
    ///      them. See PinEngine below.
    ///
    /// ORDERING IS UNCHANGED, DELIBERATELY. This is strict FIFO with nothing
    /// dropped and nothing merged. Today a Speak call blocks until the text has
    /// been handed to NVDA, so when a later interrupt=true line arrives, every
    /// earlier line has already reached the screen reader. A FIFO worker
    /// reproduces exactly that, in exactly that order. Anything cleverer — for
    /// instance dropping pending lines that a following interrupt would stomp
    /// anyway — would change what the player actually hears, and that is a
    /// design decision for Zamar rather than an implementation detail. The queue
    /// depth is logged when it exceeds one so we can find out whether it is even
    /// a question worth asking.
    ///
    /// THREAD SAFETY IS THE REASON THIS IS A SINGLE THREAD AND NOT A TASK POOL.
    /// Read from the UniversalSpeech source shipped in the repo
    /// (UniversalSpeech/src/UniversalSpeech.c): speechSay reads and writes a
    /// file-scope `current` engine index, and on failure calls unload() and
    /// re-runs detect(). None of it is locked. Two threads calling in at once is
    /// a data race on that index and a use-after-unload on the engine table. One
    /// thread, always, is the whole safety argument — which is why Silence()
    /// goes through this queue too rather than calling the library directly.
    /// </summary>
    internal static class SpeechPump
    {
        // ==================================================================
        // THE BACKEND. (Session 32, Steam Deck support. UNTESTED IN GAME.)
        //
        // Everything that talks to a speech ENGINE - the UniversalSpeech
        // P/Invokes, the engine pinning, the busy query, the engine names -
        // moved to UniversalSpeechBackend.cs, word for word. This class kept
        // everything that decides WHEN a line goes out: the thread, the FIFO,
        // the background hold, the stall warning, the PERF timing. It now
        // hands each line to _backend instead of calling the DLL itself.
        //
        // Which backend is chosen once, in Init, from the [Speech] Backend
        // config setting (SpeechBackend.cs). On Windows with the default
        // setting it is UniversalSpeechBackend, and the calls it receives are
        // the same calls, in the same order, on the same thread, as before.
        // ==================================================================
        private static ISpeechBackend _backend;

        // ~half a frame at 60fps, matching Plugin.Perf. Below this nobody feels
        // anything and the line is not worth the scrollback.
        // Session 16, Zamar: "I want all this clutter like timing out of the
        // log eventually. I want someone to be able to NVDA through this log
        // again if they have to, as a backup."
        //
        // In the 0.7.53 log 237 of 953 lines were PERF, a quarter of the file,
        // and nearly all of them reported a speech call that took 20ms and told
        // nobody anything. The 8ms floor was set when the question was WHERE the
        // hitch lived; that is settled — compose is free, the P/Invoke was the
        // whole cost, and pinning the engine removed the outliers.
        //
        // So the floor moves to where the number still says something. A speech
        // call over 250ms is a stutter a player can hear and is worth a line;
        // everything under it is noise in a document a blind developer has to
        // read. The wording of the line itself is unchanged so the numbers stay
        // comparable with every previous build's log.
        //
        // Lower it to 8.0 if a perf question ever reopens.
        private const double WARN_MS = 250.0;

        // Reached only if speech has stalled completely, which means the screen
        // reader is gone. Bounded so a stall cannot grow the queue without limit.
        private const int MAX_PENDING = 64;

        private const int STALL_WARN_MS = 3000;

        private struct Item
        {
            internal string Text;
            internal bool Interrupt;
        }

        private static ManualLogSource _log;

        private static readonly object _gate = new object();
        private static readonly Queue<Item> _pending = new Queue<Item>();

        // Written by the worker, drained and written out by the main thread.
        // BepInEx listeners are not documented thread-safe and this project
        // treats the log as a player-facing artifact, so a torn line would be a
        // real cost rather than cosmetic. Nothing here logs from the worker.
        private static readonly ConcurrentQueue<string> _logLines
            = new ConcurrentQueue<string>();

        private static Thread _worker;
        private static volatile bool _stop;

        // Set only when the worker is not running — either it never started or
        // it has already returned. Speak then happens inline on the caller,
        // which is still exactly one thread in the library.
        private static volatile bool _bypass;

        // ==================================================================
        // IS A CALL ACTUALLY IN FLIGHT? (0.7.116.)
        //
        // THE EXISTING CRASH DIAGNOSTIC HAS BEEN ANSWERING THE WRONG QUESTION.
        // OnApplicationFocus and OnApplicationPause both log PendingCount, and
        // every crash log so far reads "speech queue depth 0" — which looks like
        // the pump was idle and has quietly cleared the speech layer as a
        // suspect for two sessions.
        //
        // IT IS NOT EVIDENCE OF THAT. The worker DEQUEUES an item and only then
        // calls into UniversalSpeech, so depth is already 0 for the entire
        // duration of the call. Depth 0 means "nothing waiting", never "nothing
        // running", and the whole window of interest is inside that call.
        //
        // Zamar's Session 18 report is what makes this worth instrumenting:
        // "Minimizing and going back into the mod while it was READING something
        // had the game crash again." That is the first report that names the
        // speech call directly, and the last two log lines before the cut are a
        // pause and a resume with, supposedly, nothing in flight.
        //
        // So this flag says what depth cannot: whether the worker thread was
        // inside the library when the application paused or resumed, and for how
        // long. LOG ONLY — nothing here changes behaviour. Instrument, do not
        // experiment, when a wrong guess costs him a run.
        // ==================================================================
        private static volatile bool _inCall;
        private static long _inCallSince;

        // ==================================================================
        // HOLD SPEECH WHILE THE GAME IS IN THE BACKGROUND. (0.7.117.)
        //
        // The minimise-and-restore crash, and Zamar's Session 18 report is what
        // pointed at it: "Minimizing and going back into the mod while it was
        // READING something had the game crash again." The suspect is a call
        // sitting inside UniversalSpeech — and so inside the screen reader's COM
        // proxy — while Windows tears the window down and puts it back.
        //
        // His call, asked before building: "hold new speech while the game is
        // minimized and resume on restore... sounds great." It changes what he
        // hears — a line queued while he is away now arrives when he comes back
        // rather than being spoken to an empty room — which is why it was a
        // question and not an implementation detail.
        //
        // THIS HOLDS NEW CALLS. IT NEVER INTERRUPTS ONE IN FLIGHT. Reaching into
        // a call that is already inside the library is the one thing this class
        // exists to prevent, and it is also the thing most likely to cause the
        // very crash being chased. A call already running finishes; the next one
        // waits at the gate.
        //
        // IT CANNOT STRAND THE QUEUE, and that matters more than the feature.
        // The flag is not IKMA remembering a state the game owns — HotkeyManager
        // pushes Application.isFocused down every frame, so the moment the game
        // is back the hold clears whether or not any pause event ever arrived.
        // The wait is also bounded rather than indefinite, so even a worker that
        // somehow missed the release re-checks on its own. Every cached-state
        // bug on this project has been something that could only be cleared by
        // an event that did not fire.
        // ==================================================================
        private static volatile bool _backgroundHold;

        private static volatile bool _silencedForHold;

        internal static void SetBackgroundHold(bool hold)
        {
            if (_backgroundHold == hold) return;

            _backgroundHold = hold;
            if (!hold)
            {
                _silencedForHold = false;
                // Wake the worker the instant the game is back, rather than
                // letting it sit out the remainder of its timed wait.
                lock (_gate) Monitor.PulseAll(_gate);
            }
        }

        /// <summary>True while speech is held because the game is in the background.</summary>
        internal static bool BackgroundHold { get { return _backgroundHold; } }

        /// <summary>True while the worker is inside UniversalSpeech.</summary>
        internal static bool InCall { get { return _inCall; } }

        /// <summary>Milliseconds the current call has been running, or -1.</summary>
        internal static double InCallMs
        {
            get
            {
                if (!_inCall) return -1;
                long since = System.Threading.Interlocked.Read(ref _inCallSince);
                if (since == 0) return -1;
                return (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;
            }
        }

        // When the queue last went from empty to non-empty. The stall check
        // measures how long the HEAD has been waiting, not how long since the
        // last line was spoken — the first version measured the latter and
        // reported a stall every time a quiet stretch was followed by one line.
        // Nothing was ever stalled. (Fixed Session 15, from the 0.7.42 log.)
        private static int _queueFilledTick;
        private static bool _stallWarned;
        private static int _deepestQueueLogged;

        internal static void Init(ManualLogSource log, ConfigFile config)
        {
            _log = log;

            // Session 32: pick the backend BEFORE the thread starts, so the
            // worker's very first act (Maintain) already has one. Building it
            // does no I/O - no DLL load, no socket - see SpeechBackends.Create.
            // Log lines from the backend go through _logLines, the same
            // worker-safe queue this class already uses.
            try
            {
                if (config != null) SpeechBackends.BindConfig(config);
                _backend = SpeechBackends.Create(line => _logLines.Enqueue(line), out string choiceLine);
                _log?.LogInfo(choiceLine);
            }
            catch (Exception e)
            {
                // A broken config must never cost the player speech. Fall back
                // to the path that has always worked.
                _backend = new UniversalSpeechBackend(line => _logLines.Enqueue(line));
                _log?.LogWarning($"IKMA SPEECH: speech backend setup failed ({e.Message}) - using UniversalSpeech.");
            }

            try
            {
                _worker = new Thread(WorkerLoop)
                {
                    // Background, so a wedged screen reader can never hold the
                    // game open on exit.
                    IsBackground = true,
                    Name = "IKMA speech pump",
                };

                // STA, AND THIS IS A CRASH FIX. (Session 17.)
                //
                // Zamar has reported a crash on minimise-and-restore twice, and
                // BepInEx logged nothing at the cut either time. Pulling
                // IKMAccess.dll out of plugins and repeating the exact steps did
                // NOT crash, so it is ours — that test is what turned a
                // year-old "unattributed" into a real bug.
                //
                // The pump is IKMA's only background thread and the only place
                // that P/Invokes into UniversalSpeech. **SAPI5 is COM**, and it
                // is what he actually runs on — his logs pin the engine to SAPI5
                // every session because NVDA is not running on that machine.
                //
                // A .NET thread with no apartment state set defaults to MTA, so
                // every call into an STA COM object goes through a marshalling
                // proxy that needs a message pump. Minimising and restoring is
                // exactly when Windows floods the process with window messages.
                // A native access violation there kills the process outright,
                // which is why the managed try/catch around the worker loop
                // caught nothing and why the log simply stops.
                //
                // The last three PERF lines before his crash were 708ms, 654ms
                // and 757ms with the queue at depth 3 — a call was in flight.
                //
                // STA gives the thread its own apartment, which is what a COM
                // speech engine expects from its caller.
                try { _worker.SetApartmentState(ApartmentState.STA); }
                catch (Exception apartment)
                {
                    // Never fatal: a pump in the wrong apartment still speaks.
                    _log?.LogWarning(
                        "IKMA SPEECH: could not set the pump thread to STA " +
                        $"({apartment.GetType().Name}). Continuing.");
                }

                _worker.Start();
                _log?.LogInfo("IKMA SPEECH: pump thread started.");
            }
            catch (Exception e)
            {
                // Silence is the worst outcome there is for a blind player, so
                // failing to start the thread must cost performance and nothing
                // else. Straight back to the 0.7.40 behaviour.
                _bypass = true;
                _log?.LogWarning(
                    $"IKMA SPEECH: pump thread would not start ({e.Message}) — " +
                    "speaking on the main thread instead.");
            }
        }

        /// <summary>
        /// Hand a line to the screen reader. Returns immediately.
        /// Called from the main thread only, by CardReader.Speak and
        /// CardReader.Silence.
        /// </summary>
        internal static void Say(string text, bool interrupt)
        {
#if IKMA_DEV
            // Session 35: every line also goes to Claude's test driver mailbox.
            DevDriver.NoteSpeech(text, interrupt);
#endif
            if (_bypass)
            {
                FlushInline();
                SayInline(text, interrupt);
                return;
            }

            lock (_gate)
            {
                if (_pending.Count == 0) _queueFilledTick = Environment.TickCount;
                _pending.Enqueue(new Item { Text = text ?? string.Empty, Interrupt = interrupt });

                if (_pending.Count > MAX_PENDING)
                {
                    _pending.Dequeue();
                    _logLines.Enqueue(
                        $"IKMA SPEECH: pump queue hit its {MAX_PENDING}-line cap — " +
                        "oldest line dropped. Speech is stalled.");
                }
                else if (_pending.Count > 1 && _pending.Count > _deepestQueueLogged)
                {
                    // Expected to be rare: the announcer already spaces its own
                    // lines, and a drain costs about 75ms. Recorded so the
                    // question of whether depth ever matters is answered by the
                    // log rather than by argument.
                    _deepestQueueLogged = _pending.Count;
                    _logLines.Enqueue($"IKMA SPEECH: pump queue reached depth {_pending.Count}.");
                }

                Monitor.Pulse(_gate);
            }
        }

        /// <summary>
        /// Main-thread housekeeping: write out whatever the worker wanted logged,
        /// and notice if speech has stopped draining. Called every frame from
        /// CombatAnnouncer.Update.
        /// </summary>
        internal static void Tick()
        {
            while (_logLines.TryDequeue(out string line))
                _log?.LogInfo(line);

            int depth;
            lock (_gate) depth = _pending.Count;

            if (depth == 0)
            {
                _stallWarned = false;
                return;
            }

            // Session 35: while the window is in the background the pump HOLDS
            // its lines on purpose (SetBackgroundHold), so a full queue is not
            // a stalled screen reader. The warning was firing on every
            // background stretch - a false line in the log.
            if (_backgroundHold)
            {
                Volatile.Write(ref _queueFilledTick, Environment.TickCount);
                return;
            }

            // Unchecked subtraction, because Environment.TickCount wraps.
            if (!_stallWarned &&
                unchecked(Environment.TickCount - Volatile.Read(ref _queueFilledTick)) > STALL_WARN_MS)
            {
                _stallWarned = true;
                // Not a fallback to the main thread: if the screen reader is
                // wedged, calling it from the main thread would wedge the game
                // instead, and racing the stuck worker into the library is the
                // one thing this class exists to prevent. Report and let the log
                // say what happened.
                _log?.LogWarning(
                    $"IKMA SPEECH: pump has not drained in over {STALL_WARN_MS}ms " +
                    $"with {depth} line(s) waiting. The screen reader is not answering.");
            }
        }

        /// <summary>How many lines are waiting. Diagnostic only.</summary>
        internal static int PendingCount
        {
            get { lock (_gate) { return _pending.Count; } }
        }

        internal static void Shutdown()
        {
            _stop = true;
            lock (_gate) Monitor.PulseAll(_gate);
            try { _worker?.Join(250); } catch { /* exiting anyway */ }

            // Session 32: let the backend close anything it holds (the bridge's
            // socket) - but only once the worker is really gone, so the two
            // threads never touch the backend at the same time. A worker still
            // stuck in a call is left alone; the process is exiting anyway.
            if (_worker == null || !_worker.IsAlive)
            {
                try { _backend?.Shutdown(); } catch { }
            }
        }

        // ---------------------------------------------------------------------

        // ==================================================================
        // IS THE ENGINE STILL TALKING? (0.7.355.)
        //
        // The library answers it: speechGetValue(SP_BUSY), with
        // SP_BUSY_SUPPORTED saying whether the current engine can (SAPI can;
        // a screen reader may not). Enum order in UniversalSpeech.h: VOLUME
        // x4, RATE x4, PITCH x4, INFLEXION x4, PAUSED, PAUSE_SUPPORTED,
        // BUSY = 18, BUSY_SUPPORTED = 19.
        //
        // Polled HERE, on the worker, every 100ms while idle and straight
        // after every line — the one-thread rule holds. The main thread only
        // reads the published value: 1 busy, 0 quiet, -1 cannot tell.
        //
        // WHY: Zamar, three builds running, "mantis god playing stomped
        // Morsel line again." The confirmation interrupts to cut the slot
        // prompt, and the only way to cut the prompt and not the result ahead
        // of it is to know when the result has finished. A word-count guess
        // came up short.
        // ==================================================================
        // The SP_BUSY constants and the "does this engine report busy?" cache
        // moved to UniversalSpeechBackend with the rest of the library calls
        // (Session 32). What stays here is the published value and the grace.
        private static volatile int _busyState = -1;

        /// <summary>1 = the engine is speaking, 0 = quiet, -1 = cannot tell.</summary>
        internal static int EngineBusy
        {
            get
            {
                if (_bypass) return -1;
                // Session 36: while the window is in the background the pump
                // HOLDS its lines, so "lines pending" never clears and every
                // caller waiting for quiet sat out its full cap (12 s). Found
                // by the driver: "Choose a slot" after a sacrifice arrived only
                // after the first arrow press. Nothing is heard while held, so
                // report "cannot tell" and let the word estimate pace the queue.
                if (_backgroundHold) return -1;
                if (_inCall) return 1;
                lock (_gate) { if (_pending.Count > 0) return 1; }
                return _busyState;
            }
        }

        // SAPI speaks asynchronously; straight after the call it may not
        // report busy yet. A just-handed line counts as speaking for 250ms.
        private static int _busyGraceUntil;

        private static void PollBusy()
        {
            if (unchecked(Environment.TickCount - _busyGraceUntil) < 0) return;
            try { _busyState = _backend.QueryBusy(); }
            catch { _busyState = -1; }
        }

        private static void WorkerLoop()
        {
            try
            {
                _backend.Maintain();

                while (true)
                {
                    Item item;
                    lock (_gate)
                    {
                        while (_pending.Count == 0 && !_stop)
                        {
                            if (!Monitor.Wait(_gate, 100)) PollBusy();
                        }

                        // THE BACKGROUND HOLD. Waited on with a timeout so a
                        // release that never pulsed still lets the worker out;
                        // Shutdown always wins, so the thread can be joined
                        // while the game is minimised.
                        //
                        // CUT THE LINE ALREADY IN THE AIR. (0.7.118.) 0.7.117
                        // held the next CALL, and Zamar: "the mod will keep
                        // reading the last info even after minimizing the
                        // window." Holding calls was never going to stop that —
                        // speechSay hands the whole string to the screen reader
                        // and returns, so NVDA goes on speaking a sentence IKMA
                        // has already finished delivering.
                        //
                        // An empty interrupting say is the library's own stop,
                        // and it runs HERE, on the worker, inside the same lock
                        // discipline as every other call. Silencing from the
                        // main thread would put a second thread inside
                        // UniversalSpeech, which is the one thing this class
                        // exists to prevent — and the likeliest cause of the
                        // very crash the hold was added for.
                        //
                        // Once per hold, not once per loop: the flag is cleared
                        // on release, so a long minimise does not re-issue it
                        // every 200ms.
                        if (_backgroundHold && !_silencedForHold && !_stop)
                        {
                            _silencedForHold = true;
                            SayInline(string.Empty, true);
                        }

                        while (_backgroundHold && !_stop)
                            Monitor.Wait(_gate, 200);

                        if (_stop) return;
                        if (_pending.Count == 0) continue; // released with nothing left
                        item = _pending.Dequeue();
                        // The next line's wait starts now, not when it was
                        // queued behind this one.
                        _queueFilledTick = Environment.TickCount;
                    }

                    _backend.Maintain();
                    SayInline(item.Text, item.Interrupt);
                    _backend.AfterSay();
                    if (!string.IsNullOrEmpty(item.Text))
                    {
                        _busyState = 1;
                        _busyGraceUntil = Environment.TickCount + 250;
                    }
                    else PollBusy();
                }
            }
            catch (Exception e)
            {
                // The worker is done. _bypass is set as its last act, so the
                // main thread takes the library over cleanly and there is never
                // a moment when two threads could both be inside it.
                _bypass = true;
                _logLines.Enqueue(
                    $"IKMA SPEECH: pump thread died ({e.Message}) — " +
                    "speech falls back to the main thread.");
            }
        }

        /// <summary>
        /// The whole native call, wherever it is being made from.
        /// </summary>
        private static void SayInline(string text, bool interrupt)
        {
            long t = Stopwatch.GetTimestamp();

            // The flag is raised BEFORE the call and lowered in a finally, so it
            // is true for exactly the window a crash could land in — including
            // when the call throws.
            System.Threading.Interlocked.Exchange(ref _inCallSince, t);
            _inCall = true;
            try
            {
                // Session 32: through the backend. For UniversalSpeech this is
                // the same speechSay call that used to sit here.
                _backend.Say(text ?? string.Empty, interrupt);
            }
            catch (Exception e)
            {
                // Wording kept from before the backend split, so logs stay
                // searchable across builds, whichever backend threw.
                _logLines.Enqueue($"IKMA SPEECH: speechSay threw: {e.Message}");
            }
            finally
            {
                _inCall = false;
                System.Threading.Interlocked.Exchange(ref _inCallSince, 0);
            }

            double ms = (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency;
            // Same wording Plugin.Perf uses, so the existing PERF lines in the
            // log stay comparable across builds.
            if (ms >= WARN_MS)
                _logLines.Enqueue($"IKMA PERF: speechSay took {ms:F1}ms.");
        }

        private static void FlushInline()
        {
            while (true)
            {
                Item item;
                lock (_gate)
                {
                    if (_pending.Count == 0) return;
                    item = _pending.Dequeue();
                }
                SayInline(item.Text, item.Interrupt);
            }
        }

        /// <summary>
        /// The engine in use right now, for the bug-report header (LogExport).
        /// Null if it cannot be read. (0.7.306; Session 32 asks the backend.)
        /// </summary>
        internal static string CurrentEngineName()
        {
            try { return _backend?.CurrentEngineName(); }
            catch { return null; }
        }
    }
}
