// KeyIn.cs
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// The one place IKMA asks "was this key pressed?" - and the answer is
    /// yes for the keyboard key OR for the pad button Zamar mapped to it.
    /// (Session 34, milestone M2: the controller map.)
    /// </summary>
    /// <remarks>
    /// WHY A LAYER AND NOT PAD CODE ON EVERY SCREEN. IKMA checked the keyboard
    /// directly, about 220 times, one screen at a time. Every one of those
    /// checks now says KeyIn.Down(KeyCode.H) instead of
    /// Input.GetKeyDown(KeyCode.H). So when the View button counts as H, it
    /// counts as H on every screen at once - the pause menu, the map, battle,
    /// the Trader - with nothing screen-specific to forget. A pad player gets
    /// exactly the lines a keyboard player gets, from the same code.
    ///
    /// WHY IKMA TAKES THE WHOLE PAD (Zamar's OK, Session 34). IKMA already
    /// switches off the game's own keyboard controls (Plugin.cs, "Rewired
    /// keyboard maps disabled") and drives the game itself. If the game ALSO
    /// read the pad, A would be IKMA's Enter and the game's Select at once -
    /// two picks, or the wrong card, wherever the game's pad cursor happened
    /// to be. So while [Gamepad] IKMADrivesPad is on:
    ///   - the game's own pad layout is not loaded (GamepadSupport), so the
    ///     game sees no pad buttons at all, exactly as with the keyboard;
    ///   - the game is told no pad button is down (the AnyGamepadButton patch
    ///     below), so it never flips into its console "gamepad mode", whose
    ///     cursor would wander off what IKMA is showing - the stream must
    ///     match the narration;
    ///   - Start is Escape and LT / RT are E, IKMA's own pause and end-turn
    ///     keys, which drive the game the same way they do from the keyboard.
    ///
    /// SHIFT. Some of IKMA's keys are Shift combinations (Shift+G, Shift+R,
    /// ...). A pad button mapped to one sets "Shift is held" for that one
    /// frame, so the existing checks (KeyIn.Held(KeyCode.LeftShift) next to
    /// KeyIn.Down(KeyCode.G)) read it as the keyboard combination. It is never
    /// held longer than the frame of the press.
    ///
    /// CHORDS. Holding LB turns the D-pad and face buttons into the
    /// information keys; holding RB turns them into the action keys; holding
    /// both, View saves the log. While a shoulder is held the D-pad and face
    /// buttons are NOT also arrows / Enter - one press, one key.
    ///
    /// ONE READ PER FRAME. Tick runs at most once per frame (Time.frameCount),
    /// the first time anything asks, so a reader that polls before
    /// HotkeyManager in the same frame sees the same presses.
    /// </remarks>
    public static class KeyIn
    {
        internal static ConfigEntry<bool> IKMADrivesPad;

        private static readonly HashSet<KeyCode> _padDown = new HashSet<KeyCode>();
        private static bool _padShift;
        private static bool _padCtrl;
        private static bool _padNumber;

        // SHOULDER TAPS. (Session 34, Zamar: in options, RB is the next page
        // right and LB the next page left.) A tap is LB or RB pressed and
        // released with no other pad button pressed while it was held, so a
        // chord (LB + RB + View, RB + D-pad) is never also a tap.
        private static bool _lbHeldPrev, _rbHeldPrev, _lbClean, _rbClean;
        private static bool _lbTap, _rbTap;

        /// <summary>
        /// This pad button went down this frame with no shoulder held. For the
        /// few places a face button means something of its own (Session 34:
        /// A and B draw in the draw phase).
        /// </summary>
        public static bool PadAlone(PadInput.PadButton b)
        {
            Tick();
            return PadOwned && PadInput.Pressed(b)
                && !PadInput.Held(PadInput.PadButton.LB) && !PadInput.Held(PadInput.PadButton.RB);
        }

        /// <summary>+1 on an RB tap this frame, -1 on an LB tap, else 0.</summary>
        public static int ShoulderTap { get { Tick(); return _rbTap ? 1 : _lbTap ? -1 : 0; } }
        private static int _tickedFrame = -1;

        internal static void BindConfig(ConfigFile config)
        {
            IKMADrivesPad = config.Bind("Gamepad", "IKMADrivesPad", true,
                "IKMA reads the controller and drives the game with it, the same way it does with the keyboard (Zamar's map). Off = IKMA ignores the controller and the game's own controller layout is loaded instead.");
            BindRemap(config);
        }

        /// <summary>True while IKMA, not the game, reads the controller.</summary>
        public static bool PadOwned => IKMADrivesPad == null || IKMADrivesPad.Value;

        /// <summary>The key went down this frame, on the keyboard or the pad.</summary>
        public static bool Down(KeyCode key)
        {
            if (Input.GetKeyDown(key)) return true;
            Tick();
            return _padDown.Contains(key);
        }

        /// <summary>
        /// The key is held. From the pad, only Shift and Ctrl can be "held",
        /// and only on the frame a Shift- or Ctrl-mapped button went down.
        /// </summary>
        public static bool Held(KeyCode key)
        {
            if (Input.GetKey(key)) return true;
            if (key == KeyCode.LeftControl || key == KeyCode.RightControl) { Tick(); return _padCtrl; }
            if (key != KeyCode.LeftShift && key != KeyCode.RightShift) return false;
            Tick();
            return _padShift;
        }

        /// <summary>Any key, or any pad button, went down this frame.</summary>
        public static bool AnyDown
        {
            get
            {
                if (Input.anyKeyDown) return true;
                Tick();
#if IKMA_DEV
                if (DevDriver.AnyInjected) return true;
#endif
                return PadOwned && PadInput.AnyPressed;
            }
        }

        /// <summary>
        /// True when the last thing the player pressed was a pad button, false
        /// when it was a keyboard key. Spoken key names follow it (PadWords).
        /// Starts false: IKMA speaks keyboard keys until a pad is used.
        /// </summary>
        public static bool LastWasPad { get; private set; }

        // ------------------------------------------------------------------
        // ZAMAR'S MAP - IKMAccess\docs\GAMEPAD_MAP.md (decided Session 33,
        // changed Session 34). Change the map there first, then here.
        //
        // ONE TABLE, TWO READERS. Tick walks it to turn presses into keys;
        // PadWords walks it backwards to say which button a key is on. So
        // what a line tells the player to press is always what IKMA reads.
        // Order matters for the second reader: the FIRST binding for a key
        // is the one spoken (RT before LT, so "E" is spoken as RT).
        // ------------------------------------------------------------------
        public enum Chord { None, LB, RB, Both }

        public sealed class Binding
        {
            public readonly Chord Chord;
            public readonly PadInput.PadButton Button;
            public readonly KeyCode Key;
            public readonly bool Shift;
            public readonly bool Ctrl;
            public readonly bool Number;
            /// <summary>
            /// The config name of an action the player may move (IKMA
            /// Manager's controller settings, [GamepadMap] in the config).
            /// Null for the game's own buttons, which stay where the console
            /// game has them.
            /// </summary>
            public readonly string Action;
            public Binding(Chord chord, PadInput.PadButton button, KeyCode key, bool shift = false, bool number = false, string action = null, bool ctrl = false)
            { Chord = chord; Button = button; Key = key; Shift = shift; Number = number; Action = action; Ctrl = ctrl; }
            public Binding MovedTo(Chord chord, PadInput.PadButton button)
                => new Binding(chord, button, Key, Shift, Number, Action, Ctrl);
        }

        /// <summary>
        /// The map in use: Zamar's defaults below, with any moves the player
        /// made in IKMA Manager applied (BindConfig). Tick and PadWords both
        /// read this, so a moved action is also SPOKEN on its new button.
        /// </summary>
        // Not "= Defaults": static fields initialise in the order they are
        // written, and Defaults is declared below, so it would still be null.
        public static Binding[] Map => _map ?? Defaults;
        private static Binding[] _map;

        private static readonly Binding[] Defaults =
        {
            // No chord. The game's own (fixed) first.
            new Binding(Chord.None, PadInput.PadButton.Up,     KeyCode.UpArrow),
            new Binding(Chord.None, PadInput.PadButton.Down,   KeyCode.DownArrow),
            new Binding(Chord.None, PadInput.PadButton.Left,   KeyCode.LeftArrow),
            new Binding(Chord.None, PadInput.PadButton.Right,  KeyCode.RightArrow),
            new Binding(Chord.None, PadInput.PadButton.A,      KeyCode.Return),
            new Binding(Chord.None, PadInput.PadButton.B,      KeyCode.Backspace),
            new Binding(Chord.None, PadInput.PadButton.Start,  KeyCode.Escape),
            // RT rings the bell. Only RT (Zamar, Session 34): LT is Tab now.
            new Binding(Chord.None, PadInput.PadButton.RT,     KeyCode.E),           // end turn
            // Right stick: the console's look up / down, as IKMA's Shift +
            // arrows (Shift+Up deck, Shift+Down stand up, Shift+Left / Right
            // turn in the cabin). Session 34.
            new Binding(Chord.None, PadInput.PadButton.RUp,    KeyCode.UpArrow,    shift: true),
            new Binding(Chord.None, PadInput.PadButton.RDown,  KeyCode.DownArrow,  shift: true),
            new Binding(Chord.None, PadInput.PadButton.RLeft,  KeyCode.LeftArrow,  shift: true),
            new Binding(Chord.None, PadInput.PadButton.RRight, KeyCode.RightArrow, shift: true),
            // X: the game's Alt select on a console; IKMA's keyboard has no
            // key for it, so it does nothing yet.

            // IKMA's own, no chord. Movable.
            new Binding(Chord.None, PadInput.PadButton.Y,      KeyCode.R,     action: "Rulebook"),
            new Binding(Chord.None, PadInput.PadButton.View,   KeyCode.H,     action: "Help"),
            // Zamar, Session 34: X is Space now, everywhere ("R3 should be
            // replaced everywhere by X"). R3 is free.
            new Binding(Chord.None, PadInput.PadButton.X,      KeyCode.Space, action: "Repeat"),
            // Zamar, Session 34: LT is Tab (was L3), and L3 is Shift+R, "a
            // frequent use control". LB+Y stays Shift+R too; L3 is listed
            // first, so it is the one the mod names when it speaks.
            new Binding(Chord.None, PadInput.PadButton.LT,     KeyCode.Tab,   action: "NextPlayable"),
            new Binding(Chord.None, PadInput.PadButton.L3,     KeyCode.R, shift: true, action: "LastAbilitiesQuick"),

            // Hold LB: information.
            new Binding(Chord.LB, PadInput.PadButton.Left,  KeyCode.C,              action: "ReadHand"),
            new Binding(Chord.LB, PadInput.PadButton.Up,    KeyCode.U,              action: "ReadEnemyQueue"),
            new Binding(Chord.LB, PadInput.PadButton.Right, KeyCode.G,              action: "ReadEnemyBoard"),
            new Binding(Chord.LB, PadInput.PadButton.Down,  KeyCode.G, shift: true, action: "ReadYourBoard"),
            new Binding(Chord.LB, PadInput.PadButton.A,     KeyCode.A,              action: "GameInfo"),
            new Binding(Chord.LB, PadInput.PadButton.B,     KeyCode.B,              action: "FullBoard"),
            new Binding(Chord.LB, PadInput.PadButton.X,     KeyCode.I, shift: true, action: "AllItemSlots"),
            new Binding(Chord.LB, PadInput.PadButton.Y,     KeyCode.R, shift: true, action: "LastAbilities"),
            // Session 37, M9: Say the Spire 2's LT+Start, on IKMA's hold button.
            new Binding(Chord.LB, PadInput.PadButton.Start, KeyCode.M, ctrl: true, action: "ModSettings"),
            // Session 37, M9: Say the Spire 2's LT+Back, on IKMA's hold button.
            new Binding(Chord.LB, PadInput.PadButton.View,  KeyCode.F1,             action: "HelpList"),

            // Hold RB: actions.
            new Binding(Chord.RB, PadInput.PadButton.A,     KeyCode.D,              action: "DrawFromDeck"),
            new Binding(Chord.RB, PadInput.PadButton.B,     KeyCode.S,              action: "DrawSquirrel"),
            new Binding(Chord.RB, PadInput.PadButton.X,     KeyCode.I,              action: "ItemMenu"),
            new Binding(Chord.RB, PadInput.PadButton.Y,     KeyCode.E, shift: true, action: "AcceptSurrender"),
            // RB + D-pad = the number keys, clockwise from Up (Zamar, Session
            // 34). Numbers everywhere: item 1-3 (the keyboard's Shift +
            // number), slot 1-4 in play flow, options page, map path.
            new Binding(Chord.RB, PadInput.PadButton.Up,    KeyCode.Alpha1, number: true, action: "Number1"),
            new Binding(Chord.RB, PadInput.PadButton.Right, KeyCode.Alpha2, number: true, action: "Number2"),
            new Binding(Chord.RB, PadInput.PadButton.Down,  KeyCode.Alpha3, number: true, action: "Number3"),
            new Binding(Chord.RB, PadInput.PadButton.Left,  KeyCode.Alpha4, number: true, action: "Number4"),

            // Rare: hold both shoulders, press View - save a bug-report log.
            new Binding(Chord.Both, PadInput.PadButton.View, KeyCode.L, shift: true, action: "SaveLog"),

            // Session 35: the review history (ReviewHistory.cs), Ctrl+Down /
            // Ctrl+Up on the keyboard. Both shoulders + D-pad on the pad: the
            // shape Persona 4 Golden Access uses (two shoulders + D-pad), and
            // free in this map.
            new Binding(Chord.Both, PadInput.PadButton.Down, KeyCode.DownArrow, ctrl: true, action: "HistoryOlder"),
            new Binding(Chord.Both, PadInput.PadButton.Up,   KeyCode.UpArrow,   ctrl: true, action: "HistoryNewer"),

            // Session 36, Zamar: R3 is the silence key (the keyboard's is a
            // Ctrl tap, SilenceKey.cs). R3 arrives as a token key.
            new Binding(Chord.None, PadInput.PadButton.R3, SilenceKey.PadToken, action: "Silence"),
        };

        // ------------------------------------------------------------------
        // REMAPPING. (Session 34; Zamar's ask, Session 33: "remapping of
        // IKMA's pad actions in the IKMA Manager's settings".) Each movable
        // action is one line in [GamepadMap] of IKMA's config, e.g.
        //     DrawFromDeck = RB+A
        // IKMA Manager writes these; a hand edit works the same way.
        //
        // THE RULES, so a map can never break the game or the chords:
        //   - LB and RB are the chord buttons, never an action's button.
        //   - With no chord held, only IKMA's own buttons can be used (Y, X,
        //     View, L3, R3, LT). A, B, the D-pad, Start and RT are the game's
        //     own jobs.
        //   - Two actions on one button: the first one placed keeps it and
        //     the other stays on its default (or is dropped if that is taken
        //     too). Said in the log.
        // ------------------------------------------------------------------
        private static readonly PadInput.PadButton[] FreeAlone =
        {
            PadInput.PadButton.Y, PadInput.PadButton.X, PadInput.PadButton.View,
            PadInput.PadButton.L3, PadInput.PadButton.R3,
            PadInput.PadButton.LT,   // Session 34: LT no longer rings the bell
        };

        /// <summary>"RB+A", "LB+DpadLeft", "LB+RB+View", "Y".</summary>
        public static string GestureText(Chord chord, PadInput.PadButton button)
        {
            string prefix = chord == Chord.LB ? "LB+" : chord == Chord.RB ? "RB+" : chord == Chord.Both ? "LB+RB+" : "";
            string name;
            switch (button)
            {
                case PadInput.PadButton.Up:    name = "DpadUp"; break;
                case PadInput.PadButton.Down:  name = "DpadDown"; break;
                case PadInput.PadButton.Left:  name = "DpadLeft"; break;
                case PadInput.PadButton.Right: name = "DpadRight"; break;
                default:                       name = button.ToString(); break;
            }
            return prefix + name;
        }

        /// <summary>Parse GestureText; false if it is not a gesture IKMA allows.</summary>
        public static bool TryParseGesture(string text, out Chord chord, out PadInput.PadButton button)
        {
            chord = Chord.None; button = PadInput.PadButton.Y;
            if (string.IsNullOrEmpty(text)) return false;
            bool lb = false, rb = false;
            string last = null;
            foreach (var raw in text.Split('+'))
            {
                string t = raw.Trim();
                if (t.Length == 0) return false;
                if (last != null) return false;          // the button must be the last part
                if (string.Equals(t, "LB", StringComparison.OrdinalIgnoreCase)) { lb = true; continue; }
                if (string.Equals(t, "RB", StringComparison.OrdinalIgnoreCase)) { rb = true; continue; }
                last = t;
            }
            if (last == null) return false;
            string b = last.StartsWith("Dpad", StringComparison.OrdinalIgnoreCase) ? last.Substring(4) : last;
            PadInput.PadButton parsed;
            try { parsed = (PadInput.PadButton)Enum.Parse(typeof(PadInput.PadButton), b, true); }
            catch { return false; }
            if (parsed == PadInput.PadButton.LB || parsed == PadInput.PadButton.RB) return false;
            if (parsed >= PadInput.PadButton.RUp) return false;     // the right stick is the game's look
            chord = lb && rb ? Chord.Both : lb ? Chord.LB : rb ? Chord.RB : Chord.None;
            if (chord == Chord.None && Array.IndexOf(FreeAlone, parsed) < 0) return false;
            button = parsed;
            return true;
        }

        private static readonly Dictionary<string, ConfigEntry<string>> _remap
            = new Dictionary<string, ConfigEntry<string>>();

        private static void BindRemap(ConfigFile config)
        {
            foreach (var b in Defaults)
            {
                if (b.Action == null) continue;
                // "ControllerMap", not "GamepadMap" (0.7.389): BepInEx keeps a
                // saved value over a new default, so the section 0.7.385 wrote
                // would have kept Repeat on R3. Nobody has moved an action yet
                // (the Manager menu is not built), so a fresh section loses
                // nothing. The old [GamepadMap] lines are ignored.
                _remap[b.Action] = config.Bind("ControllerMap", b.Action, GestureText(b.Chord, b.Button),
                    "Controller button for this IKMA action (IKMA Manager sets it). Default " + GestureText(b.Chord, b.Button) + ".");
            }
            RebuildMap(log: true);
        }

        // Session 40: split out of BindRemap, so Mod Settings > Controller
        // buttons can move an action while the game runs. The log lines are
        // written at start-up only; a move made in the menu writes its own.
        private static void RebuildMap(bool log)
        {
            var map = new List<Binding>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // The game's own buttons first, so nothing can be moved onto them.
            foreach (var b in Defaults)
                if (b.Action == null) { map.Add(b); taken.Add(GestureText(b.Chord, b.Button)); }

            foreach (var b in Defaults)
            {
                if (b.Action == null) continue;
                string want = _remap[b.Action].Value;
                Chord c; PadInput.PadButton btn;
                if (!TryParseGesture(want, out c, out btn))
                {
                    if (log) Plugin.Log?.LogWarning($"IKMA PAD MAP: '{want}' is not a button IKMA can use for {b.Action}; its default {GestureText(b.Chord, b.Button)} is used.");
                    c = b.Chord; btn = b.Button;
                }
                string g = GestureText(c, btn);
                if (taken.Contains(g))
                {
                    string def = GestureText(b.Chord, b.Button);
                    if (log) Plugin.Log?.LogWarning($"IKMA PAD MAP: {g} is already used; {b.Action} stays on {def}.");
                    if (taken.Contains(def))
                    {
                        if (log) Plugin.Log?.LogWarning($"IKMA PAD MAP: {def} is taken too; {b.Action} has no controller button.");
                        continue;
                    }
                    c = b.Chord; btn = b.Button; g = def;
                }
                taken.Add(g);
                map.Add(c == b.Chord && btn == b.Button ? b : b.MovedTo(c, btn));
                if (log && (c != b.Chord || btn != b.Button))
                    Plugin.Log?.LogInfo($"IKMA PAD MAP: {b.Action} moved to {g}.");
            }
            _map = map.ToArray();
        }

        // ------------------------------------------------------------------
        // MOVING AN ACTION IN THE GAME. (Session 40: Mod Settings >
        // Controller buttons, ModSettingsMenu.cs. Zamar, Session 38: every
        // IKMA Manager setting that can exist in the game does - "Exist in
        // both".) Same file section, same rules and the same swap as the
        // Manager's menu (tools\installer\ControllerMap.cs), so the two can
        // never disagree about what a map means.
        // ------------------------------------------------------------------

        /// <summary>The movable actions on their default buttons, in the order IKMA Manager lists them.</summary>
        internal static IEnumerable<Binding> MovableDefaults
        {
            get { foreach (var b in Defaults) if (b.Action != null) yield return b; }
        }

        /// <summary>The binding an action has now, or null if it has no button.</summary>
        internal static Binding CurrentFor(string action)
        {
            foreach (var b in Map) if (b.Action == action) return b;
            return null;
        }

        /// <summary>True if IKMA's rules (above) let an action sit on this gesture.</summary>
        internal static bool GestureAllowed(Chord chord, PadInput.PadButton button)
        {
            Chord c; PadInput.PadButton b;
            return TryParseGesture(GestureText(chord, button), out c, out b);
        }

        /// <summary>
        /// Put an action on a gesture, saved to [ControllerMap] and used from
        /// the next press. If another action already has that gesture the two
        /// SWAP (Zamar, Session 34) and that action's name is returned;
        /// otherwise null. The caller checks GestureAllowed first.
        /// </summary>
        internal static string MoveAction(string action, Chord chord, PadInput.PadButton button)
        {
            ConfigEntry<string> entry;
            if (!_remap.TryGetValue(action, out entry)) return null;

            // Where this action is now. An action a hand-edited file left
            // with no button hands over its default instead.
            Binding mine = CurrentFor(action);
            if (mine == null)
                foreach (var d in Defaults) if (d.Action == action) { mine = d; break; }

            string swapped = null;
            foreach (var b in Map)
            {
                if (b.Action == null || b.Action == action) continue;
                if (b.Chord != chord || b.Button != button) continue;
                _remap[b.Action].Value = GestureText(mine.Chord, mine.Button);
                swapped = b.Action;
                break;
            }
            entry.Value = GestureText(chord, button);
            RebuildMap(log: false);
            return swapped;
        }

        /// <summary>Every action back on its default button.</summary>
        internal static void ResetControllerMap()
        {
            foreach (var b in Defaults)
                if (b.Action != null) _remap[b.Action].Value = GestureText(b.Chord, b.Button);
            RebuildMap(log: false);
        }

        /// <summary>
        /// Read the pad and apply the map. Safe to call any number of times a
        /// frame; only the first call does the work. HotkeyManager calls it at
        /// the top of every frame so the diagnostic log line is never skipped.
        /// </summary>
        private static KeyCode _synthetic = KeyCode.None;
        internal static void Synthesize(KeyCode key) => _synthetic = key;

        internal static void Tick()
        {
            int frame = Time.frameCount;
            if (frame == _tickedFrame) return;
            _tickedFrame = frame;

            _padDown.Clear();
            _padShift = false;
            _padCtrl = false;
            _padNumber = false;

            // Session 36: a key IKMA presses for the player (the bell held
            // through a draw), seen by every KeyIn.Down for one frame.
            if (_synthetic != KeyCode.None) { _padDown.Add(_synthetic); _synthetic = KeyCode.None; }

#if IKMA_DEV
            // Session 35: Claude's test driver injects one key per frame into
            // the same set the pad fills, so it reaches every KeyIn.Down.
            DevDriver.Frame(_padDown, ref _padShift, ref _padCtrl);
#endif

            PadInput.Tick();

            // Which device spoke last. A pad press wins the frame; only a real
            // KEYBOARD key switches back. (0.7.385: Unity counts pad buttons
            // as "any key" too, and can see a pad press a frame before
            // Rewired does - that frame flipped IKMA to keyboard mid-play and
            // a queued line came out with key names. So the keyboard is asked
            // key by key, and only on a frame where something went down.)
            bool wasPad = LastWasPad;
            if (PadInput.AnyPressed) LastWasPad = true;
            else if (Input.anyKeyDown && KeyboardKeyDown()) LastWasPad = false;

            // THE SWITCH IS SPOKEN. (Session 34, Zamar: "When changing to
            // gamepad controls say 'Gamepad.' When changing to keyboard from
            // gamepad (not if starting the game on keyboard already) say
            // 'Keyboard.'") Starting on the keyboard is the default, so only a
            // real change is said. The word rides on the front of whatever the
            // same press makes IKMA say, so the press's own line does not cut
            // it off; if the press says nothing, it is said on its own.
            if (LastWasPad != wasPad && PadOwned)
            {
                _switchWord = LastWasPad ? Vocabulary.GamepadSwitch : Vocabulary.KeyboardSwitch;
                _switchAt = Time.unscaledTime;
            }
            if (_switchWord != null && Time.unscaledTime - _switchAt > SWITCH_WAIT)
            {
                string w = _switchWord;
                _switchWord = null;
                try { Speech.Browse(w); } catch { }
            }

            if (!PadOwned) return;

            bool lb = PadInput.Held(PadInput.PadButton.LB);
            bool rb = PadInput.Held(PadInput.PadButton.RB);
            TickTaps(lb, rb);
            Chord held = lb && rb ? Chord.Both : lb ? Chord.LB : rb ? Chord.RB : Chord.None;

            // While a shoulder is held, its buttons are ONLY the chord - one
            // press, one key. A chord press that maps to nothing does nothing.
            foreach (var b in Map)
            {
                if (b.Chord != held || !PadInput.Pressed(b.Button)) continue;
                _padDown.Add(b.Key);
                if (b.Shift) _padShift = true;
                if (b.Ctrl) _padCtrl = true;
                if (b.Number) _padNumber = true;
            }

            // (0.7.388 made X advance conversations only; 0.7.389 makes X
            // Space everywhere, in the table above.)
        }

        private static void TickTaps(bool lb, bool rb)
        {
            _lbTap = _rbTap = false;
            bool other = false;
            foreach (PadInput.PadButton b in Enum.GetValues(typeof(PadInput.PadButton)))
                if (b != PadInput.PadButton.LB && b != PadInput.PadButton.RB && PadInput.Pressed(b)) { other = true; break; }

            if (lb && !_lbHeldPrev) _lbClean = true;          // LB went down
            if (rb && !_rbHeldPrev) _rbClean = true;
            if (other || (lb && rb)) { _lbClean = false; _rbClean = false; }
            if (!lb && _lbHeldPrev && _lbClean) _lbTap = true; // LB came up clean
            if (!rb && _rbHeldPrev && _rbClean) _rbTap = true;
            if (!lb) _lbClean = _lbClean && lb;
            if (!rb) _rbClean = _rbClean && rb;
            _lbHeldPrev = lb;
            _rbHeldPrev = rb;
        }

        private static string _switchWord;
        private static float _switchAt;
        private const float SWITCH_WAIT = 0.25f;

        /// <summary>
        /// From CardReader.Speak: the device word, if one is waiting, goes on
        /// the front of this line. Null when there is nothing waiting.
        /// </summary>
        internal static string TakeSwitchWord()
        {
            string w = _switchWord;
            _switchWord = null;
            return w;
        }

        // Every keyboard key Unity knows: the KeyCode values below the mouse
        // buttons (Mouse0 = 323; joystick buttons start at 330). Built once.
        private static KeyCode[] _keyboardKeys;

        private static bool KeyboardKeyDown()
        {
            if (_keyboardKeys == null)
            {
                var list = new List<KeyCode>();
                foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                    if (k > KeyCode.None && (int)k < (int)KeyCode.Mouse0) list.Add(k);
                _keyboardKeys = list.ToArray();
            }
            foreach (var k in _keyboardKeys)
                if (Input.GetKeyDown(k))
                {
                    // Session 34: name the key that switched IKMA to the
                    // keyboard. "Keyboard." was heard mid-pad-play and nobody
                    // remembered touching a key; this says which key it was
                    // (a real one, or Steam sending keystrokes).
                    if (LastWasPad) Plugin.Log?.LogInfo($"IKMA INPUT: keyboard key '{k}' - switching to keyboard.");
                    return true;
                }
            return false;
        }

        /// <summary>
        /// A number key came from the pad (RB + D-pad) this frame. The
        /// keyboard uses Shift + number for quick item use and plain numbers
        /// for slots; the pad has one gesture for both, so the item check
        /// asks this instead of Shift.
        /// </summary>
        public static bool PadNumber { get { Tick(); return _padNumber; } }
    }

    /// <summary>
    /// While IKMA drives the pad, the game is told no pad button is down, so
    /// it stays in the mouse-and-keyboard mode a keyboard player's game is in.
    /// InputButtons.AnyGamepadButton() is PUBLIC STATIC, returns bool, no
    /// parameters (_gamesource\code\InputButtons.cs). Registered through
    /// Plugin.TryPatch, not PatchAll: new patch, so a mistake costs only this.
    /// </summary>
    public static class InputButtons_AnyGamepadButton_Patch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!KeyIn.PadOwned) return true;
            __result = false;
            return false;
        }
    }
}
