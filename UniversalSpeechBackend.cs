// UniversalSpeechBackend.cs
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace IKMA
{
    /// <summary>
    /// Speech through the UniversalSpeech DLL: NVDA, JAWS, or Windows' SAPI
    /// voices. This is the path IKMA has always used, MOVED here from
    /// SpeechPump.cs in Session 32 and NOT REWRITTEN. (UNTESTED IN GAME.)
    ///
    /// WHAT MOVED AND WHAT DID NOT. Every native call - speechSay, the engine
    /// pinning, the busy query, the engine names - and every comment that
    /// explains them came across line for line; the only edits are that log
    /// lines go through Log() instead of SpeechPump's queue directly, and the
    /// four ISpeechBackend methods at the top, which are thin doors into the
    /// same static code. The thread, the queue, the background hold, the
    /// stall warning and the PERF timing stayed in SpeechPump, because they
    /// are about WHEN lines go out, not WHERE.
    ///
    /// WHY THE INSIDES ARE STILL STATIC. The UniversalSpeech DLL itself keeps
    /// one global engine index for the whole process (see SpeechPump's class
    /// comment). There can only ever be one real "instance" of it, so the
    /// state that mirrors it stays static and the class is a thin instance
    /// wrapper so it can stand behind the interface. Only one of these is ever
    /// constructed per run.
    ///
    /// THREADING: exactly as before. Maintain, Say, AfterSay and QueryBusy are
    /// called only on SpeechPump's worker (or only on the main thread if the
    /// worker never started). CurrentEngineName is the one exception, and it
    /// always was: LogExport calls it from the main thread for the bug-report
    /// header.
    /// </summary>
    internal sealed class UniversalSpeechBackend : ISpeechBackend
    {
        // Where log lines go: SpeechPump's worker-safe queue, which the main
        // thread writes out. Never BepInEx directly from here - see
        // SpeechPump's "The worker never logs" note.
        private static Action<string> _logSink;

        private static void Log(string line)
        {
            try { _logSink?.Invoke(line); } catch { }
        }

        internal UniversalSpeechBackend(Action<string> log)
        {
            _logSink = log;
        }

        public string Name { get { return "UniversalSpeech"; } }

        // Was SpeechPump.TryPinEngine(), called at worker start and before each line.
        public void Maintain() { TryPinEngine(); }

        // Was the body of SpeechPump.SayInline's try block. Exceptions are
        // deliberately NOT caught here: SpeechPump catches them and logs
        // "IKMA SPEECH: speechSay threw", exactly as before.
        public void Say(string text, bool interrupt)
        {
            speechSay(text ?? string.Empty, interrupt);
        }

        // Was SpeechPump.NoteEngineChange(), called after each line.
        public void AfterSay() { NoteEngineChange(); }

        // Was the try block of SpeechPump.PollBusy(). The 250ms "just handed a
        // line over" grace stays in SpeechPump: it is about timing, and it
        // applies to every backend alike.
        public int QueryBusy()
        {
            try
            {
                if (_busySupportedFor != _lastEngineSeen)
                {
                    _busySupportedFor = _lastEngineSeen;
                    _busySupported = speechGetValue(SP_BUSY_SUPPORTED) != 0;
                    Log($"IKMA SPEECH: engine {(_busySupported ? "reports" : "does not report")} when it is speaking.");
                }
                return _busySupported ? (speechGetValue(SP_BUSY) != 0 ? 1 : 0) : -1;
            }
            catch { return -1; }
        }

        public string CurrentEngineName() { return CurrentEngineNameStatic(); }

        // Nothing to release. The DLL stays loaded for the life of the
        // process, as it always did.
        public void Shutdown() { }

        // =================================================================
        // Below: moved verbatim from SpeechPump.cs (0.7.365).
        // =================================================================

        [DllImport("UniversalSpeech", CharSet = CharSet.Unicode)]
        private static extern int speechSay(
            [MarshalAs(UnmanagedType.LPWStr)] string text,
            bool interrupt);

        [DllImport("UniversalSpeech")]
        private static extern int speechGetValue(int what);

        [DllImport("UniversalSpeech")]
        private static extern int speechSetValue(int what, int value);

        // Returns a pointer to a static wide string inside the engine table.
        // Marshalled as IntPtr and read with PtrToStringUni ON PURPOSE: a
        // declared `string` return would make the interop layer try to free that
        // pointer with CoTaskMemFree, which it does not own.
        [DllImport("UniversalSpeech", CharSet = CharSet.Unicode)]
        private static extern IntPtr speechGetString(int what);

        // From UniversalSpeech/include/UniversalSpeech.h. speechGetValue with
        // this returns the current engine's 0-based index, running detection if
        // none is selected; speechSetValue with a valid index pins it and clears
        // the library's `useDefault` flag.
        private const int SP_ENGINE = 0x40000;

        // speechGetValue(SP_ENGINE_AVAILABLE + i) asks engine i whether it is
        // running, without selecting it.
        private const int SP_ENGINE_AVAILABLE = 0x50000;

        // Zamar's order, Session 15: NVDA is what most blind players use and is
        // the default; Jaws second; Window-Eyes kept for now and a candidate for
        // cutting. Asked in THIS order rather than the library's, which is
        // Jaws, Window-Eyes, NVDA — so with NVDA running we now pin it on the
        // first probe and never call the two COM-based availability checks at
        // all.
        private static readonly string[] ENGINE_PREFERENCE =
            { "NVDA", "Jaws", "Windows eye" };

        // How often to re-check whether something further up ENGINE_PREFERENCE
        // has started since we pinned. Zero cost once the top preference is
        // pinned — there is nothing above it left to ask about.
        private const int UPGRADE_CHECK_MS = 30000;

        // Pinning state. Moved from SpeechPump's field list.
        private static string _lastDetectLine;
        private static bool _pinned;
        private static int _lastEngineSeen = -2;

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
        private const int SP_BUSY = 18;
        private const int SP_BUSY_SUPPORTED = 19;

        private static int _busySupportedFor = int.MinValue;
        private static bool _busySupported;

        // ---------------------------------------------------------------------
        // ENGINE PINNING
        //
        // From UniversalSpeech.c, read directly rather than inferred:
        //
        //   static void periodicRetry (void) {
        //     if (!useDefault) return;
        //     ... if (GetTickCount()-sapiLastSpeak >= 10000) { current = -1; }
        //   }
        //
        // speechSay calls periodicRetry after every successful call, so with the
        // library's default settings the engine index is thrown away every ten
        // seconds and the next spoken line pays for a full detect(). detect()
        // walks the engine table in order — Jaws, then Window-Eyes, then NVDA —
        // and the first two probe through COM. That is a plausible cause of the
        // 490ms p90 and the 765ms worst case, and pinning is how we find out:
        // speechSetValue(SP_ENGINE, n) sets useDefault to 0, which makes
        // periodicRetry return immediately and leaves the index alone.
        //
        // We also no longer USE that order. Availability is asked in
        // ENGINE_PREFERENCE order instead, so with NVDA running the two COM
        // probes are never called even once.
        //
        // RECOVERY IS NOT LOST. speechSay's own failure path is independent of
        // useDefault: if the pinned engine's say() fails and it reports itself
        // unavailable, the library unloads it, clears the index and re-detects.
        // So a screen reader that restarts is still picked up.
        //
        // WHAT IS DELIBERATELY NOT PINNED. If detection lands on Narrator or
        // SAPI5, no screen reader was running when the game started, and pinning
        // one of those would mean IKMA talks to a fallback voice forever even
        // after NVDA comes up. Those two stay on auto-detect and the pin is
        // retried every thirty seconds instead. Matched on the engine's own name
        // from the library rather than on a hardcoded index, so inserting an
        // engine in a future version cannot silently shift the meaning.
        // ---------------------------------------------------------------------

        private static int _lastPinAttemptTick;

        private static void TryPinEngine()
        {
            if (unchecked(Environment.TickCount - _lastPinAttemptTick) < UPGRADE_CHECK_MS
                && _lastPinAttemptTick != 0) return;
            _lastPinAttemptTick = Environment.TickCount;

            // Nothing ranks above what we are already using. Stop asking.
            if (_pinned && _pinnedRank == 0) return;

            try
            {
                // Only engines that would be an UPGRADE on the current pin get
                // probed, so a session already on NVDA never runs a probe again
                // and a session on SAPI5 pays three cheap checks a minute.
                // Clamped: an unranked pin carries int.MaxValue.
                int limit = _pinned
                    ? Math.Min(_pinnedRank, ENGINE_PREFERENCE.Length)
                    : ENGINE_PREFERENCE.Length;
                for (int rank = 0; rank < limit; rank++)
                {
                    int index = IndexOfEngine(ENGINE_PREFERENCE[rank]);
                    if (index < 0) continue;
                    if (speechGetValue(SP_ENGINE_AVAILABLE + index) == 0) continue;
                    Pin(index, ENGINE_PREFERENCE[rank], rank);
                    return;
                }

                if (_pinned) return;   // already on something; nothing better is up

                // Nothing preferred is running. Zamar, Session 15: SAPI5 is a
                // real target, not a fallback — "I want this mod to work even if
                // they're not using NVDA, and to work better when using NVDA."
                // So whatever the library found gets pinned too, which buys the
                // same escape from the ten-second re-detection. The periodic
                // check above is what makes that safe: NVDA starting later is
                // still picked up, within thirty seconds.
                int auto = speechGetValue(SP_ENGINE);
                if (auto < 0)
                {
                    LogOnce("IKMA SPEECH: no speech engine detected yet.");
                    return;
                }

                Pin(auto, EngineName(auto) ?? $"engine {auto}", UNRANKED);
            }
            catch (Exception e)
            {
                Log($"IKMA SPEECH: engine pin failed: {e.Message}");
            }
        }

        private const int UNRANKED = int.MaxValue;
        private static int _pinnedRank = UNRANKED;

        private static void Pin(int index, string name, int rank)
        {
            if (speechSetValue(SP_ENGINE, index) == 0)
            {
                LogOnce($"IKMA SPEECH: could not pin \"{name}\" (index {index}) — " +
                        "leaving auto-detect on.");
                return;
            }

            bool upgrade = _pinned;
            _pinned = true;
            _pinnedRank = rank;
            _lastEngineSeen = index;

            if (!upgrade) LogEngineRollCall();

            string tail = rank == 0
                ? "Preferred engine; nothing further to check."
                : rank == UNRANKED
                    ? $"No preferred engine is running; re-checking every {UPGRADE_CHECK_MS / 1000}s."
                    : $"Preference rank {rank + 1}; still watching for a better one.";

            Log(
                (upgrade ? "IKMA SPEECH: engine UPGRADED to " : "IKMA SPEECH: engine pinned to ")
                + $"\"{name}\" (index {index}). Ten-second re-detection is off. " + tail);
        }

        private static void LogOnce(string line)
        {
            if (line == _lastDetectLine) return;
            _lastDetectLine = line;
            Log(line);
        }

        /// <summary>
        /// Every engine the library knows about and whether it says it is
        /// running, in one line. Added Session 15 because the 0.7.42 log could
        /// say which engine got picked but not why the others were passed over,
        /// and "NVDA is not answering" and "NVDA is not installed" are different
        /// problems with different fixes.
        /// </summary>
        private static void LogEngineRollCall()
        {
            try
            {
                var parts = new List<string>();
                for (int i = 0; i < 32; i++)
                {
                    string n = EngineName(i);
                    if (n == null) break;
                    parts.Add($"{n}:{(speechGetValue(SP_ENGINE_AVAILABLE + i) != 0 ? "yes" : "no")}");
                }
                Log("IKMA SPEECH: engines running — " + string.Join(", ", parts.ToArray()));
            }
            catch (Exception e)
            {
                Log($"IKMA SPEECH: engine roll call failed: {e.Message}");
            }
        }

        /// <summary>
        /// Where an engine sits in the library's table, by its own name, or -1
        /// if this build of UniversalSpeech does not have it. Never a hardcoded
        /// index: inserting an engine in a future version would silently shift
        /// every one of them.
        /// </summary>
        private static int IndexOfEngine(string name)
        {
            for (int i = 0; i < 32; i++)
            {
                string n = EngineName(i);
                if (n == null) return -1;      // end of the table
                if (n == name) return i;
            }
            return -1;
        }

        /// <summary>
        /// Cheap once an engine is selected — speechGetValue(SP_ENGINE) returns
        /// the stored index without doing any work. Worth asking after every
        /// line so that an engine changing under us (the failure path above)
        /// says so in the log instead of being invisible.
        /// </summary>
        /// <summary>
        /// The engine the library is using right now, for the bug-report
        /// header. Null if it cannot be read. (0.7.306.)
        /// </summary>
        internal static string CurrentEngineNameStatic()
        {
            try
            {
                int index = speechGetValue(SP_ENGINE);
                return index < 0 ? null : EngineName(index);
            }
            catch { return null; }
        }

        private static void NoteEngineChange()
        {
            try
            {
                int index = speechGetValue(SP_ENGINE);
                if (index == _lastEngineSeen) return;
                _lastEngineSeen = index;
                // The library re-detected under us, which only happens on its
                // own failure path. Drop the pin so the next check re-establishes
                // one rather than trusting a rank that no longer applies.
                _pinned = false;
                _pinnedRank = UNRANKED;
                Log(index < 0
                    ? "IKMA SPEECH: engine lost — the library is re-detecting."
                    : $"IKMA SPEECH: engine changed to \"{EngineName(index) ?? "?"}\" (index {index}).");
            }
            catch { /* a diagnostic must never be the thing that breaks speech */ }
        }

        private static string EngineName(int index)
        {
            try
            {
                IntPtr p = speechGetString(SP_ENGINE + index);
                return p == IntPtr.Zero ? null : Marshal.PtrToStringUni(p);
            }
            catch { return null; }
        }
    }
}
