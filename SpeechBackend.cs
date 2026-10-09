// SpeechBackend.cs
using System;
using System.Runtime.InteropServices;
using BepInEx.Configuration;

namespace IKMA
{
    // ======================================================================
    // SPEECH BACKENDS. (Session 32, Steam Deck support. UNTESTED IN GAME.)
    //
    // WHAT A "BACKEND" IS. Everything IKMA says ends up in one place:
    // SpeechPump's worker thread, which takes lines off a first-in-first-out
    // queue and hands them to something that actually makes sound. Until
    // Session 32 that "something" was hard-wired: the UniversalSpeech DLL,
    // which forwards to NVDA, JAWS, or Windows' own SAPI voices.
    //
    // On a Steam Deck the game runs under Proton (Valve's build of Wine, a
    // layer that lets Windows programs run on Linux). There is no NVDA there.
    // So IKMA needs a second way out: send each line over a local network
    // connection (127.0.0.1, "this same machine") to a small native Linux
    // program - the speech helper in tools/steamdeck/ - which speaks it with
    // the Linux speech tools.
    //
    // The interface below is the seam between those two worlds. SpeechPump
    // keeps EVERYTHING it did before - the thread, the queue, the background
    // hold, the stall warning, the timing - and asks a backend only to do the
    // four things that differ per engine: get ready, speak, report whether it
    // is still talking, and name itself. That split is the whole safety
    // argument for the refactor: the part that decides WHEN and IN WHAT ORDER
    // lines are spoken was moved as a block and not rewritten, so Windows
    // behaves exactly as before.
    //
    // THE ONE-THREAD RULE STILL HOLDS. Every method marked "worker" below is
    // called only from SpeechPump's worker thread (or, if that thread never
    // started, from the main thread instead - never both). A backend therefore
    // needs no locks of its own for those methods.
    // ======================================================================

    /// <summary>
    /// Which way speech leaves the game. Stored in BepInEx's config file,
    /// BepInEx\config\com.zamar.ikma.cfg, section [Speech], key Backend.
    /// BepInEx writes an enum's names into the file as the acceptable values,
    /// so a player editing it by hand sees exactly these three words.
    /// </summary>
    internal enum SpeechBackendChoice
    {
        /// <summary>
        /// Windows: UniversalSpeech, exactly as before Session 32 - nothing is
        /// probed and no network socket is ever opened.
        /// Proton/Wine (Steam Deck, Linux): the Linux bridge when the helper
        /// answers, UniversalSpeech when it does not.
        /// </summary>
        Auto,

        /// <summary>UniversalSpeech only (NVDA, JAWS, SAPI). The pre-Session-32 path.</summary>
        NVDA,

        /// <summary>The Linux speech helper only, over 127.0.0.1.</summary>
        LinuxBridge,
    }

    /// <summary>
    /// One way of turning text into sound. See the block comment above.
    /// </summary>
    internal interface ISpeechBackend
    {
        /// <summary>Short name for the log, e.g. "UniversalSpeech".</summary>
        string Name { get; }

        /// <summary>
        /// Worker. Called once when the worker starts and again before every
        /// line. For UniversalSpeech this is the engine pinning (which
        /// rate-limits itself); for the bridge it is (re)connecting. Must be
        /// cheap when there is nothing to do, because it runs per line.
        /// </summary>
        void Maintain();

        /// <summary>
        /// Worker. Speak one line. interrupt = cut whatever is playing first.
        /// An EMPTY line with interrupt = stop talking and say nothing: that is
        /// how CardReader.Silence and the background hold cut speech.
        /// May throw; SpeechPump catches and logs it.
        /// </summary>
        void Say(string text, bool interrupt);

        /// <summary>Worker. Called straight after every Say, for bookkeeping.</summary>
        void AfterSay();

        /// <summary>
        /// Worker. Is the engine still talking? 1 = yes, 0 = no, -1 = this
        /// engine cannot say. -1 is not a failure: Speech.cs falls back to its
        /// word-count estimate, which is what it already does for any screen
        /// reader that does not report busy.
        /// </summary>
        int QueryBusy();

        /// <summary>
        /// Any thread. The engine's name for the bug-report header
        /// (LogExport). Null if it cannot be told.
        /// </summary>
        string CurrentEngineName();

        /// <summary>
        /// Main thread, on game exit, only once the worker has stopped.
        /// Release anything held open (a socket). Must never throw.
        /// </summary>
        void Shutdown();
    }

    /// <summary>
    /// Reads the [Speech] config and builds the backend it asks for.
    /// </summary>
    internal static class SpeechBackends
    {
        // A port is a numbered "door" on a machine; the helper listens behind
        // this one and IKMA knocks on it. 17432 is arbitrary: high enough that
        // nothing needs root to use it (ports under 1024 do on Linux), and not
        // a number any common program claims. The helper's default MUST match -
        // it is the same constant in tools/steamdeck/ikma_speech_helper.py.
        internal const int DEFAULT_BRIDGE_PORT = 17432;

        internal static ConfigEntry<SpeechBackendChoice> Choice;
        internal static ConfigEntry<int> BridgePort;

        // 0.7.459 (Session 49) - Zamar, 2026-10-06: "add the braiile display
        // option to v.5". BrailleOn is the copy the speech worker reads: a
        // plain volatile bool, so the worker never touches a ConfigEntry.
        internal static ConfigEntry<bool> Braille;
        internal static volatile bool BrailleOn = true;

        // 0.7.461 (Session 50) - Zamar, 2026-10-06, on IKMA knowing when
        // NVDA has finished a line: "I want that for v.5". The switch for
        // it; NvdaDirect reads its own plain copy. See NvdaDirect.cs.
        internal static ConfigEntry<bool> NvdaLineEnd;

        /// <summary>
        /// Bind the two [Speech] settings. BepInEx creates them in the .cfg file
        /// with these defaults the first time the game runs with this build,
        /// and reads the player's value on every run after that.
        /// </summary>
        internal static void BindConfig(ConfigFile config)
        {
            Choice = config.Bind("Speech", "Backend", SpeechBackendChoice.Auto,
                "Where IKMA's speech goes. Auto = on Windows, the screen reader through UniversalSpeech, as always; " +
                "on Steam Deck or Linux (Proton), the IKMA speech helper if it is running, otherwise UniversalSpeech. " +
                "NVDA = always UniversalSpeech (NVDA, JAWS or SAPI). LinuxBridge = always the IKMA speech helper.");

            BridgePort = config.Bind("Speech", "BridgePort", DEFAULT_BRIDGE_PORT,
                new ConfigDescription(
                    "Port on 127.0.0.1 where the IKMA speech helper listens. Must match the helper's --port. " +
                    "Only used when speech goes to the helper.",
                    new AcceptableValueRange<int>(1024, 65535)));

            // UNVERIFIED AS OF 0.7.459: built from the library's source and
            // NVDA's documentation. Nobody with a braille display has tried it.
            Braille = config.Bind("Speech", "Braille", true,
                "Also send every spoken line to a braille display, through the screen reader (NVDA or JAWS). " +
                "Does nothing when no braille display is attached. Not yet confirmed by a braille reader.");
            BrailleOn = Braille.Value;
            Braille.SettingChanged += (sender, args) => { BrailleOn = Braille.Value; };

            // OFF BY DEFAULT FROM 0.7.464 (Session 52). Heard once, with NVDA
            // 2026.2: NVDA went completely silent at the first encounter. NVDA's
            // own log shows why - "OrderedDict mutated during iteration" in the
            // oneCore synth callback. NVDA's speakSsml, asked to wait, unregisters
            // its synthDoneSpeaking handler on the RPC thread while the synth
            // thread is still notifying; the race breaks NVDA's speech queue.
            // It is NVDA's bug, but IKMA's waiting call is what triggers it, so
            // the feature stays off until NVDA fixes it or another way to learn
            // the end of a line exists. See NvdaDirect.cs.
            NvdaLineEnd = config.Bind("Speech", "NvdaLineEnd", false,
                "OFF by default: with NVDA 2026.2, asking NVDA when each line has finished can crash NVDA's own speech (a fault in NVDA). " +
                "When true and NVDA is 2024.1 or later, ask NVDA when each line has finished instead of estimating how long it takes to read. " +
                "No effect with other screen readers or with older NVDA.");
            NvdaDirect.SettingOn = NvdaLineEnd.Value;
            NvdaLineEnd.SettingChanged += (sender, args) => { NvdaDirect.SettingOn = NvdaLineEnd.Value; };
        }

        /// <summary>
        /// Build the backend. Called on the main thread from SpeechPump.Init.
        /// Nothing here touches the network or loads UniversalSpeech: the
        /// constructors only store settings, and the first real work happens
        /// in Maintain, on the worker. <paramref name="choiceLine"/> is the
        /// one log line saying what was chosen and why.
        /// </summary>
        internal static ISpeechBackend Create(Action<string> workerLog, out string choiceLine)
        {
            SpeechBackendChoice choice = Choice != null ? Choice.Value : SpeechBackendChoice.Auto;
            int port = BridgePort != null ? BridgePort.Value : DEFAULT_BRIDGE_PORT;

            switch (choice)
            {
                case SpeechBackendChoice.NVDA:
                    choiceLine = "IKMA SPEECH: backend UniversalSpeech (config: NVDA).";
                    return new UniversalSpeechBackend(workerLog);

                case SpeechBackendChoice.LinuxBridge:
                    choiceLine = $"IKMA SPEECH: backend Linux bridge on 127.0.0.1:{port} (config: LinuxBridge).";
                    return new LinuxBridgeBackend(port, workerLog);

                default:
                    // AUTO, NATIVE LINUX OR MAC. Inscryption also ships a
                    // native Linux build (no Proton). There the UniversalSpeech
                    // DLL cannot load at all, so the bridge is the only way
                    // out. Mono reports Unix/MacOSX here; Windows AND Wine
                    // both report Win32NT, so neither ever takes this branch.
                    // Whether IKMA supports the native build at all is an
                    // open question (WEEKEND_NOTES.md); this only keeps Auto
                    // from picking a backend that cannot work.
                    PlatformID os = Environment.OSVersion.Platform;
                    if (os == PlatformID.Unix || os == PlatformID.MacOSX)
                    {
                        choiceLine = $"IKMA SPEECH: native {os} build. Backend Auto: Linux bridge on 127.0.0.1:{port}.";
                        return new LinuxBridgeBackend(port, workerLog);
                    }

                    // AUTO. The Windows branch is the important one: it builds
                    // the same UniversalSpeech backend the NVDA setting does,
                    // so a Windows player on the default config gets the
                    // pre-Session-32 behaviour with no probe and no socket.
                    if (!RunningUnderWine(out string evidence))
                    {
                        choiceLine = "IKMA SPEECH: backend UniversalSpeech (config: Auto, Windows).";
                        return new UniversalSpeechBackend(workerLog);
                    }
                    choiceLine = $"IKMA SPEECH: running under Proton/Wine ({evidence}). " +
                                 $"Backend Auto: Linux bridge on 127.0.0.1:{port} when the helper answers, UniversalSpeech otherwise.";
                    return new AutoBridgeBackend(
                        new LinuxBridgeBackend(port, workerLog),
                        new UniversalSpeechBackend(workerLog),
                        workerLog);
            }
        }

        // ------------------------------------------------------------------
        // ARE WE UNDER WINE?
        //
        // Proton is Wine, and Wine is a re-implementation of Windows' own
        // system DLLs. The game cannot normally tell - that is the point - but
        // Wine's ntdll.dll exports one extra function that real Windows does
        // not have: wine_get_version. Asking for its address is a harmless
        // lookup: GetProcAddress returns zero on Windows and non-zero on Wine.
        //
        // A second, independent signal: Proton sets STEAM_COMPAT_DATA_PATH in
        // the environment before starting the game, and Linux environment
        // variables are visible to the Windows program. Either one is enough.
        //
        // Every failure answers "not Wine", so the worst case of a wrong answer
        // here is the pre-Session-32 behaviour.
        // ------------------------------------------------------------------

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = false)]
        private static extern IntPtr GetModuleHandleA(string moduleName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = false)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);

        internal static bool RunningUnderWine(out string evidence)
        {
            evidence = null;
            try
            {
                IntPtr ntdll = GetModuleHandleA("ntdll.dll");
                if (ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero)
                {
                    evidence = "ntdll exports wine_get_version";
                    return true;
                }
            }
            catch { /* no kernel32 at all: certainly not Windows-under-Wine */ }

            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH")))
                {
                    evidence = "STEAM_COMPAT_DATA_PATH is set";
                    return true;
                }
            }
            catch { }

            return false;
        }
    }

    /// <summary>
    /// Auto mode under Proton: the Linux bridge when it answers, UniversalSpeech
    /// when it does not, switching either way while the game runs.
    ///
    /// WHY SWITCH BOTH WAYS. On a Deck the helper is a separate program. If the
    /// player starts it after the game, or it restarts, IKMA should find it
    /// without a game restart; and if it goes away, a line spoken to nobody is
    /// the worst outcome there is, so the line goes to UniversalSpeech instead
    /// (which under Wine may or may not have a voice - see WEEKEND_NOTES.md).
    /// </summary>
    internal sealed class AutoBridgeBackend : ISpeechBackend
    {
        private readonly LinuxBridgeBackend _bridge;
        private readonly UniversalSpeechBackend _fallback;
        private readonly Action<string> _log;

        // Which one the next line goes to. Only the worker writes it; volatile
        // so CurrentEngineName, read from the main thread, sees a fresh value.
        private volatile bool _usingBridge;

        // True once any line has gone to UniversalSpeech. Worker only.
        private bool _fallbackUsed;

        internal AutoBridgeBackend(LinuxBridgeBackend bridge, UniversalSpeechBackend fallback, Action<string> log)
        {
            _bridge = bridge;
            _fallback = fallback;
            _log = log;
        }

        public string Name { get { return _usingBridge ? _bridge.Name : _fallback.Name; } }

        public void Maintain()
        {
            // TryConnect rate-limits itself, so calling it before every line
            // costs nothing while the helper is absent.
            if (!_usingBridge && _bridge.TryConnect())
            {
                _usingBridge = true;
                _log("IKMA SPEECH: Auto - speech now goes to the Linux bridge.");
                // Anything UniversalSpeech is still saying would talk over the
                // first bridge line. Cut it, once, on the switch - but only if
                // it ever spoke, so a helper found at startup never loads the
                // UniversalSpeech DLL at all.
                if (_fallbackUsed)
                {
                    try { _fallback.Say(string.Empty, true); } catch { }
                }
            }
            if (!_usingBridge) _fallback.Maintain();
        }

        public void Say(string text, bool interrupt)
        {
            if (_usingBridge)
            {
                if (_bridge.TrySay(text, interrupt)) return;
                _usingBridge = false;
                _log("IKMA SPEECH: Auto - the Linux bridge stopped answering; falling back to UniversalSpeech.");
                _fallback.Maintain();
            }
            _fallbackUsed = true;
            _fallback.Say(text, interrupt);
        }

        public void AfterSay()
        {
            if (!_usingBridge) _fallback.AfterSay();
        }

        public int QueryBusy()
        {
            return _usingBridge ? _bridge.QueryBusy() : _fallback.QueryBusy();
        }

        public string CurrentEngineName()
        {
            return _usingBridge ? _bridge.CurrentEngineName() : _fallback.CurrentEngineName();
        }

        public void Shutdown()
        {
            _bridge.Shutdown();
            _fallback.Shutdown();
        }
    }
}
