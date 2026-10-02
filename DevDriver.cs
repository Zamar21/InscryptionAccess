// DevDriver.cs

// ============================================================================
// DEVELOPMENT TOOL. NEVER SHIPS. (Session 35, 0.7.397.)
//
// Zamar asked for it after reading Harkest Dungeon's "dev driver": a way for
// Claude to press IKMA's keys in the running game and read back what IKMA
// said, so screens can be checked between his playtests. It does not replace
// his playtests - he still drives those and he is the only one who hears the
// voice. It never presses anything the player did not ask Claude to test.
//
// Like VideoTimecode.cs, the whole file sits behind IKMA_DEV (csproj property
// IKMADevTools). A release build does not contain it at all.
//
// WHY FILES AND NOT A LOOPBACK SERVER (Harkest's shape). Claude's shell on
// Zamar's PC runs inside a Linux VM that cannot reach Windows' 127.0.0.1,
// but it CAN read and write the BepInEx folder. So the mailbox is two text
// files in BepInEx\ikma_dev\:
//
//   in.txt   Claude appends one command per line:  <id> <verb> [args]
//   out.txt  IKMA appends one event per line:      <seq> TAB <kind> TAB <text>
//
// Kinds in out.txt: READY (game started), SAY / SAYI (a line IKMA spoke,
// queued / interrupting), DONE <id> [note], ERR <id> <why>, STATE.
// tools/dev/ikma_drive.py is the client that writes and waits.
//
// Verbs:
//   key <KeyName>        one press of a Unity KeyCode for one frame, through
//                        KeyIn.Down - the same door every IKMA key goes
//                        through. "key shift+R" / "key ctrl+DownArrow" hold
//                        Shift / Ctrl for that frame.
//   wait <ms>            pause the command queue.
//   quiet <ms> <maxms>   wait until IKMA has said nothing for <ms> and the
//                        speech pump is empty, or until <maxms> passes.
//   mark <text>          echo a label into out.txt.
//   pad <Buttons>        one pad press through PadInput, as if a pad sent
//                        it: "pad A", "pad LB+Y" (LB held, Y pressed),
//                        "pad LB+RB+Down", "pad LB" (a tap: held one frame,
//                        released the next). Button names: PadInput.PadButton.
//   state                scene name, frame, focus, time scale.
//   autoplay battle [s]  Session 36: play this battle to its end through
//                        IKMA's keys (DevAutopilot.cs); DONE carries the result.
//   autoplay run [s]     the same, plus the map and node screens, until the
//                        run ends. Any new command stops the autopilot.
//   runbg on|off         Application.runInBackground, so the game keeps
//                        running while its window is not focused.
//
// WHAT IT CANNOT PRESS. Keys the GAME reads for itself (its own Rewired
// input) and the few IKMA reads straight from Unity (AutoUpdate's answer
// keys, the death-card name typing) do not see an injected press. Everything
// that goes through KeyIn.Down does.
// ============================================================================
#if IKMA_DEV

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace IKMA
{
    internal static class DevDriver
    {
        private const float POLL_SECONDS = 0.25f;

        private static bool _init, _failed;
        private static string _dir, _inPath, _outPath;
        private static long _inOffset;
        private static float _nextPoll;
        private static int _seq;

        private static readonly Queue<string> _commands = new Queue<string>();
        private static readonly StringBuilder _outBuffer = new StringBuilder();
        private static readonly object _outGate = new object();

        // The command being worked on, when it spans frames (wait, quiet).
        private static string _currentId, _currentVerb;
        private static float _currentStart, _waitUntil, _quietFor, _quietMax;

        private static float _lastSpeechAt;

        // This frame's injected press. Read by KeyIn.Tick and AnyDown.
        private static KeyCode _injectKey = KeyCode.None;
        private static bool _injectShift, _injectCtrl;

        internal static bool AnyInjected => _injectKey != KeyCode.None;

        // This frame's injected pad press: held buttons and the pressed one.
        private static readonly List<PadInput.PadButton> _padHeld = new List<PadInput.PadButton>();
        private static PadInput.PadButton? _padPressed;

        /// <summary>
        /// Called at the end of PadInput.Tick. Marks this frame's injected
        /// pad buttons pressed / held. True when anything was injected.
        /// </summary>
        internal static bool PadFrame(bool[] pressed, bool[] held)
        {
            if (_padPressed == null) return false;
            foreach (var b in _padHeld) held[(int)b] = true;
            pressed[(int)_padPressed.Value] = true;
            held[(int)_padPressed.Value] = true;
            return true;
        }

        /// <summary>
        /// Called once per frame from KeyIn.Tick, after the pad set is cleared
        /// and before the map fills it. Adds this frame's injected key, if any.
        /// </summary>
        internal static void Frame(HashSet<KeyCode> down, ref bool shift, ref bool ctrl)
        {
            if (_failed) return;
            try
            {
                if (!_init) Init();

                float now = Time.unscaledTime;
                if (now >= _nextPoll)
                {
                    _nextPoll = now + POLL_SECONDS;
                    ReadNewCommands();
                    FlushOut();
                }

                _injectKey = KeyCode.None;
                _injectShift = _injectCtrl = false;
                _padPressed = null;
                _padHeld.Clear();
                Advance(now);

                if (_injectKey != KeyCode.None)
                {
                    down.Add(_injectKey);
                    if (_injectShift) shift = true;
                    if (_injectCtrl) ctrl = true;
                }
            }
            catch (Exception e)
            {
                // One failure turns the driver off for the session rather than
                // throwing inside the key handler every frame.
                _failed = true;
                Plugin.Log?.LogWarning($"IKMA DEV: driver stopped - {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Every line IKMA hands to the speech pump. Main thread.</summary>
        internal static void NoteSpeech(string text, bool interrupt)
        {
            if (_failed) return;
            _lastSpeechAt = Time.unscaledTime;
            if (string.IsNullOrEmpty(text)) { Emit("SILENCE", ""); return; }
            Emit(interrupt ? "SAYI" : "SAY", text);
        }

        private static void Init()
        {
            _init = true;
            _dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "ikma_dev");
            Directory.CreateDirectory(_dir);
            _inPath = Path.Combine(_dir, "in.txt");
            _outPath = Path.Combine(_dir, "out.txt");

            // A fresh mailbox every launch: commands left over from the last
            // session must not run in this one.
            File.WriteAllText(_inPath, "");
            File.WriteAllText(_outPath, "");
            _inOffset = 0;
            Emit("READY", "IKMA " + Plugin.PluginVersion);

            // Dev builds keep running while the window is in the background,
            // so the driver works without the game focused. IKMA already holds
            // its speech in the background, so nothing is heard meanwhile.
            Application.runInBackground = true;
            FlushOut();
            Plugin.Log?.LogInfo($"IKMA DEV: driver mailbox at {_dir}");
        }

        private static void ReadNewCommands()
        {
            var info = new FileInfo(_inPath);
            if (!info.Exists) { File.WriteAllText(_inPath, ""); _inOffset = 0; return; }
            if (info.Length < _inOffset) _inOffset = 0;      // truncated by the client
            if (info.Length == _inOffset) return;

            string chunk;
            using (var fs = new FileStream(_inPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(_inOffset, SeekOrigin.Begin);
                var bytes = new byte[info.Length - _inOffset];
                int read = fs.Read(bytes, 0, bytes.Length);
                chunk = Encoding.UTF8.GetString(bytes, 0, read);
            }

            // Only whole lines. A half-written last line waits for the next poll.
            int lastNl = chunk.LastIndexOf('\n');
            if (lastNl < 0) return;
            _inOffset += Encoding.UTF8.GetByteCount(chunk.Substring(0, lastNl + 1));

            foreach (string raw in chunk.Substring(0, lastNl).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length > 0) _commands.Enqueue(line);
            }
        }

        private static void Advance(float now)
        {
            // A multi-frame command in progress.
            if (_currentVerb != null)
            {
                if (_currentVerb == "wait")
                {
                    if (now < _waitUntil) return;
                    Done(_currentId, "");
                }
                else if (_currentVerb == "quiet")
                {
                    // While the window is in the background the pump HOLDS
                    // its lines (SpeechPump.BackgroundHold), so a non-empty
                    // queue is not "still talking" then.
                    bool silent = now - _lastSpeechAt >= _quietFor
                                  && (SpeechPump.PendingCount == 0 || SpeechPump.BackgroundHold);
                    bool timedOut = now - _currentStart >= _quietMax;
                    if (!silent && !timedOut) return;
                    Done(_currentId, silent ? "" : "timeout");
                }
                else if (_currentVerb == "autoplay")
                {
                    // Session 36: the autopilot presses one key per settle
                    // until the battle ends (DevAutopilot.cs).
                    // Any new command stops the autopilot: the escape hatch.
                    if (_commands.Count > 0) DevAutopilot.Stop("interrupted by a new command");
                    KeyCode k = DevAutopilot.Tick(now, _lastSpeechAt);
                    if (k != KeyCode.None) _injectKey = k;
                    if (DevAutopilot.Active) return;
                    Done(_currentId, DevAutopilot.Summary ?? "");
                }
                _currentVerb = null;
            }

            // Start commands until one needs a frame of its own. A key press
            // always ends the frame's work: one press, one frame.
            while (_commands.Count > 0)
            {
                string line = _commands.Dequeue();
                string[] parts = line.Split(new[] { ' ' }, 3);
                string id = parts[0];
                string verb = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";
                string arg = parts.Length > 2 ? parts[2].Trim() : "";

                switch (verb)
                {
                    case "key":
                        if (!TryParseKey(arg, out _injectKey, out _injectShift, out _injectCtrl))
                        {
                            _injectKey = KeyCode.None;
                            Emit("ERR", id + " unknown key '" + arg + "'");
                            continue;
                        }
                        Done(id, arg);
                        return;

                    case "pad":
                        if (!TryParsePad(arg))
                        {
                            _padPressed = null; _padHeld.Clear();
                            Emit("ERR", id + " unknown pad buttons '" + arg + "'");
                            continue;
                        }
                        Done(id, "pad " + arg);
                        return;

                    case "wait":
                        _currentId = id; _currentVerb = "wait";
                        _waitUntil = now + Ms(arg, 0, 500);
                        return;

                    case "quiet":
                        _currentId = id; _currentVerb = "quiet"; _currentStart = now;
                        string[] q = arg.Split(' ');
                        _quietFor = Ms(q.Length > 0 ? q[0] : "", 0, 800);
                        _quietMax = Ms(q.Length > 1 ? q[1] : "", 0, 10000);
                        return;

                    case "mark":
                        Done(id, arg);
                        continue;

                    case "state":
                        Emit("STATE", $"scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} " +
                                      $"frame={Time.frameCount} focused={Application.isFocused} " +
                                      $"timeScale={Time.timeScale} runInBackground={Application.runInBackground} " +
                                      $"challengeLevel={ChallengeLevel()}");
                        Done(id, "");
                        continue;

                    case "autoplay":
                        if (arg.StartsWith("battle", StringComparison.OrdinalIgnoreCase)
                            || arg.StartsWith("run", StringComparison.OrdinalIgnoreCase))
                        {
                            DevAutopilot.Start(arg, Emit);
                            _currentId = id; _currentVerb = "autoplay";
                            return;
                        }
                        Emit("ERR", id + " autoplay: 'battle [seconds]' or 'run [seconds]'");
                        continue;

                    case "runbg":
                        Application.runInBackground = arg.Equals("on", StringComparison.OrdinalIgnoreCase);
                        Done(id, "runInBackground=" + Application.runInBackground);
                        continue;

                    default:
                        Emit("ERR", id + " unknown verb '" + verb + "'");
                        continue;
                }
            }
        }

        private static string ChallengeLevel()
        {
            try { return DiskCardGame.AscensionSaveData.Data.challengeLevel.ToString(); }
            catch { return "?"; }
        }

        private static float Ms(string s, int index, int fallback)
        {
            int v;
            return (int.TryParse(s, out v) ? v : fallback) / 1000f;
        }

        private static bool TryParseKey(string arg, out KeyCode key, out bool shift, out bool ctrl)
        {
            key = KeyCode.None;
            shift = ctrl = false;
            string name = arg;
            while (true)
            {
                if (name.StartsWith("shift+", StringComparison.OrdinalIgnoreCase)) { shift = true; name = name.Substring(6); continue; }
                if (name.StartsWith("ctrl+", StringComparison.OrdinalIgnoreCase))  { ctrl = true;  name = name.Substring(5); continue; }
                break;
            }
            try
            {
                key = (KeyCode)Enum.Parse(typeof(KeyCode), name, true);
                return key != KeyCode.None;
            }
            catch { return false; }
        }

        private static bool TryParsePad(string arg)
        {
            _padHeld.Clear();
            _padPressed = null;
            string[] parts = arg.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                PadInput.PadButton b;
                try { b = (PadInput.PadButton)Enum.Parse(typeof(PadInput.PadButton), parts[i].Trim(), true); }
                catch { return false; }
                if (i < parts.Length - 1) _padHeld.Add(b); else _padPressed = b;
            }
            return _padPressed != null;
        }

        private static void Done(string id, string note)
            => Emit("DONE", note.Length == 0 ? id : id + " " + note);

        private static void Emit(string kind, string text)
        {
            lock (_outGate)
            {
                _seq++;
                _outBuffer.Append(_seq).Append('\t').Append(kind).Append('\t')
                          .Append((text ?? "").Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
            }
        }

        private static void FlushOut()
        {
            string text;
            lock (_outGate)
            {
                if (_outBuffer.Length == 0) return;
                text = _outBuffer.ToString();
                _outBuffer.Length = 0;
            }
            using (var fs = new FileStream(_outPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                fs.Write(bytes, 0, bytes.Length);
            }
        }
    }
}

#endif
// DevDriver.cs
