// PadInput.cs
using System.Collections.Generic;
using BepInEx.Configuration;
using Rewired;

namespace IKMA
{
    /// <summary>
    /// IKMA's own read of the gamepad: which physical button went down this
    /// frame, by NAME (Y, LB, ...), on any pad Rewired recognises.
    /// (Session 32, M2 plumbing. UNTESTED WITH A PAD.)
    /// </summary>
    /// <remarks>
    /// WHAT THIS IS FOR. The physical half of the pad: which button went
    /// down, by name, the same way on an Xbox pad, a PlayStation pad and the
    /// Steam Deck. It binds nothing itself. KeyIn turns these presses into
    /// IKMA's keyboard keys using Zamar's map (IKMAccess\docs\GAMEPAD_MAP.md).
    ///
    /// WHY NOT REWIRED ACTIONS. The game's actions (Select, Cancel, EndTurn
    /// ...) are fixed in its data and IKMA cannot add new ones. So IKMA reads
    /// the buttons themselves, and leaves the game's actions to the game.
    ///
    /// WHY THE "GAMEPAD TEMPLATE". Every pad model numbers its buttons
    /// differently. Rewired's Gamepad Template is a translation table that
    /// gives each physical button a standard name - template.y is the top face
    /// button on every recognised pad (Triangle on PlayStation). The game's own
    /// pad layout is built on the same template (see GamepadSupport), so a pad
    /// the game understands, IKMA understands too. A pad Rewired does NOT
    /// recognise has no template; that is logged once, by name.
    ///
    /// Verified against the decompile before writing: IGamepadTemplate and its
    /// y / leftShoulder1 / center1 / leftStick.press members are in
    /// _gamesource\code\Rewired\IGamepadTemplate.cs (Assembly-CSharp);
    /// Controller.GetTemplate&lt;T&gt;(), IControllerTemplateButton.value /
    /// justPressed, IControllerTemplateAxis.AsButton and
    /// IControllerTemplateDPad.up..left are in _gamesource\rewired\Rewired
    /// (Rewired_Core). The build compiling against the game's own DLLs is the
    /// second confirmation.
    ///
    /// THE DIAGNOSTIC. With [Gamepad] LogButtonPresses on (off by default since 0.7.428),
    /// every pad press is one "IKMA PAD:" log line, game buttons included. That
    /// answers the open question from the controller audit without any new
    /// code: in one pad playtest, the log shows each press next to whatever
    /// IKMA did or did not say after it - does IKMA follow the game's cursor
    /// when the D-pad moves it, or stay silent? It speaks nothing. A keyboard
    /// player never presses a pad button, so never sees a line. Turn it off in
    /// the M8 release checklist.
    /// </remarks>
    public static class PadInput
    {
        /// <summary>
        /// Every button IKMA reads, by its Xbox name and position. Session 34:
        /// this was the six free buttons only; the whole pad is read now,
        /// because IKMA drives the pad the way it drives the keyboard (see
        /// KeyIn). Up / Down / Left / Right are the D-pad AND the left stick.
        /// </summary>
        public enum PadButton { A, B, X, Y, LB, RB, LT, RT, View, Start, L3, R3, Up, Down, Left, Right,
                                RUp, RDown, RLeft, RRight }
        private const int BUTTON_COUNT = 20;

        // How far the left stick must lean to count as a D-pad press. It must
        // come back inside the same line before the next lean counts, so one
        // push is one press - the way a D-pad click is.
        private const float STICK_PRESS = 0.6f;

        internal static ConfigEntry<bool> LogButtonPresses;

        // Pressed this frame / held now, across every pad. Rebuilt each Tick.
        private static readonly bool[] _pressed = new bool[BUTTON_COUNT];
        private static readonly bool[] _held = new bool[BUTTON_COUNT];

        /// <summary>True if any pad button at all went down this frame.</summary>
        public static bool AnyPressed { get; private set; }

        /// <summary>
        /// The name Rewired gives the pad that was pressed last ("XInput
        /// Gamepad 1", "Sony DualSense", ...). PadWords reads it to pick the
        /// button words. Session 34.
        /// </summary>
        public static string LastPadName { get; private set; }

        // One template per joystick id, looked up once. A null value means
        // "this pad has no gamepad template" - remembered so the lookup and the
        // log line happen once per pad, not once per frame.
        private static readonly Dictionary<int, IGamepadTemplate> _templates
            = new Dictionary<int, IGamepadTemplate>();

        private static bool _loggedError;

        internal static void BindConfig(ConfigFile config)
        {
            // Session 40 (M8 release checklist): off by default. A config
            // file that already holds the setting keeps what it has, so
            // Zamar's own playtest logs still carry the lines.
            LogButtonPresses = config.Bind("Gamepad", "LogButtonPresses", false,
                "Write one IKMA PAD line to the log for every gamepad button press. For playtesting; speaks nothing.");
        }

        /// <summary>True on the one frame the button went down, on any pad.</summary>
        public static bool Pressed(PadButton b) { return _pressed[(int)b]; }

        /// <summary>True every frame the button is down - for chords.</summary>
        public static bool Held(PadButton b) { return _held[(int)b]; }

        /// <summary>
        /// Called once per frame, by KeyIn.Tick (Session 34; it used to be
        /// HotkeyManager directly). Cheap: with no pad connected it is one
        /// property read and a return.
        /// </summary>
        internal static void Tick()
        {
            for (int i = 0; i < _pressed.Length; i++) { _pressed[i] = false; _held[i] = false; }
            AnyPressed = false;

            try
            {
                if (!ReInput.isReady) return;
                Player player = ReInput.players.GetPlayer(0);
                if (player == null) return;
                IList<Joystick> joysticks = player.controllers.Joysticks;
                if (joysticks == null || joysticks.Count == 0) return;

                bool log = LogButtonPresses != null && LogButtonPresses.Value;

                for (int j = 0; j < joysticks.Count; j++)
                {
                    Joystick joystick = joysticks[j];
                    if (joystick == null) continue;
                    IGamepadTemplate t = TemplateFor(joystick);
                    if (t == null) continue;

                    bool anyBefore = AnyPressed;
                    AnyPressed = false;

                    Read(t.a,               PadButton.A);
                    Read(t.b,               PadButton.B);
                    Read(t.x,               PadButton.X);
                    Read(t.y,               PadButton.Y);
                    Read(t.leftShoulder1,   PadButton.LB);
                    Read(t.rightShoulder1,  PadButton.RB);
                    Read(t.leftTrigger?.AsButton,  PadButton.LT);
                    Read(t.rightTrigger?.AsButton, PadButton.RT);
                    Read(t.center1,         PadButton.View);
                    Read(t.center2,         PadButton.Start);
                    Read(t.leftStick?.press,  PadButton.L3);
                    Read(t.rightStick?.press, PadButton.R3);
                    if (t.dPad != null)
                    {
                        Read(t.dPad.up,    PadButton.Up);
                        Read(t.dPad.down,  PadButton.Down);
                        Read(t.dPad.left,  PadButton.Left);
                        Read(t.dPad.right, PadButton.Right);
                    }
                    ReadStick(t.leftStick, 0);
                    ReadStick(t.rightStick, RIGHT_STICK_OFFSET);   // Session 34: RUp..RRight

                    if (AnyPressed) LastPadName = joystick.name;
                    AnyPressed = AnyPressed || anyBefore;

                    if (log) LogPresses(t);
                }
            }
            catch (System.Exception e)
            {
                // Never let pad reading break the frame. Say so once.
                if (!_loggedError)
                {
                    _loggedError = true;
                    Plugin.Log?.LogWarning($"IKMA PAD: {e.GetType().Name}: {e.Message}");
                }
            }
            finally
            {
#if IKMA_DEV
                // Session 35: the test driver's pad presses, merged in as if a
                // pad had pressed them, so the whole pad path (the map, chords,
                // taps, spoken button names) runs. In a finally because the
                // body RETURNS early when no pad is plugged in, and the driver
                // must work without one.
                if (DevDriver.PadFrame(_pressed, _held)) { AnyPressed = true; LastPadName = "IKMA test driver"; }
#endif
            }
        }

        private static void Read(IControllerTemplateButton button, PadButton which)
        {
            if (button == null) return;
            if (button.justPressed) { _pressed[(int)which] = true; AnyPressed = true; }
            if (button.value) _held[(int)which] = true;
        }

        /// <summary>
        /// The left stick as four D-pad buttons. Whichever axis leans further
        /// wins, so a diagonal is one direction, never two. A press is the
        /// frame the lean first crosses STICK_PRESS; Rewired keeps last
        /// frame's position (valuePrev), so no state of IKMA's own is needed.
        /// </summary>
        // RUp..RRight sit the same distance after Up..Right in the enum.
        private const int RIGHT_STICK_OFFSET = (int)PadButton.RUp - (int)PadButton.Up;

        private static void ReadStick(IControllerTemplateThumbStick stick, int offset)
        {
            if (stick == null) return;
            int now  = StickDir(stick.value);
            int prev = StickDir(stick.valuePrev);
            if (now >= 0) now += offset;
            if (prev >= 0) prev += offset;
            if (now < 0) return;
            _held[now] = true;
            if (now != prev) { _pressed[now] = true; AnyPressed = true; }
        }

        private static int StickDir(UnityEngine.Vector2 v)
        {
            float ax = System.Math.Abs(v.x), ay = System.Math.Abs(v.y);
            if (ax < STICK_PRESS && ay < STICK_PRESS) return -1;
            if (ay >= ax) return (int)(v.y > 0 ? PadButton.Up : PadButton.Down);
            return (int)(v.x > 0 ? PadButton.Right : PadButton.Left);
        }

        private static IGamepadTemplate TemplateFor(Joystick joystick)
        {
            if (_templates.TryGetValue(joystick.id, out IGamepadTemplate cached)) return cached;

            IGamepadTemplate t = null;
            try { t = joystick.GetTemplate<IGamepadTemplate>(); } catch { t = null; }
            _templates[joystick.id] = t;

            Plugin.Log?.LogInfo(t != null
                ? $"IKMA PAD: '{joystick.name}' (id {joystick.id}) recognised as a gamepad."
                : $"IKMA PAD: '{joystick.name}' (id {joystick.id}) has no gamepad template. IKMA's pad buttons will not work on it.");
            return t;
        }

        // ------------------------------------------------------------------
        // THE DIAGNOSTIC LINES. Standard (Xbox) names; PlayStation in the
        // controls doc. Game buttons are tagged with the game action the
        // game's own pad layout gives them, free ones with "free", so the log
        // reads on its own: "IKMA PAD: A (game: Select)".
        // ------------------------------------------------------------------
        private static void LogPresses(IGamepadTemplate t)
        {
            Note(t.a,              "A");
            Note(t.b,              "B");
            Note(t.x,              "X");
            Note(t.y,              "Y");
            Note(t.leftShoulder1,  "LB");
            Note(t.rightShoulder1, "RB");
            Note(t.leftTrigger?.AsButton,  "LT");
            Note(t.rightTrigger?.AsButton, "RT");
            Note(t.center1,        "View");
            Note(t.center2,        "Start");
            Note(t.leftStick?.press,  "L3");
            Note(t.rightStick?.press, "R3");
            if (t.dPad != null)
            {
                Note(t.dPad.up,    "D-pad up");
                Note(t.dPad.down,  "D-pad down");
                Note(t.dPad.left,  "D-pad left");
                Note(t.dPad.right, "D-pad right");
            }
        }

        private static void Note(IControllerTemplateButton button, string label)
        {
            if (button != null && button.justPressed)
                Plugin.Log?.LogInfo("IKMA PAD: " + label);
        }
    }
}
