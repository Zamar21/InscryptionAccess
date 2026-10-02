// GamepadSupport.cs
using System.Collections.Generic;
using BepInEx.Configuration;
using DiskCardGame;
using HarmonyLib;
using Rewired;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// The half of gamepad support that does not depend on Zamar's binding
    /// map: make the pad work at all, and make it rumble.
    /// </summary>
    /// <remarks>
    /// 0.7.208 — plan M2, first cut. Evidence in docs/ and the Cowork memory
    /// ikma_gamepad.
    ///
    /// WHY THE PAD IS DEAD ON PC. The game's Rewired data (InputManager.prefab)
    /// contains a complete gamepad layout — D-pad and left stick navigate,
    /// A Select, B Cancel, X AltSelect, Start Menu, LT/RT End turn, right
    /// stick Look — but it is filed under a joystick LAYOUT literally named
    /// "DISABLED" (category "Default"), the player's default joystick maps
    /// list is empty, and nothing in the game's own code loads a map. Zamar
    /// confirmed it with an Xbox One pad on 2026-09-14: Steam saw it, the
    /// game did nothing. The console builds evidently load this layout;
    /// IKMA does it on PC.
    ///
    /// HOW. Every frame (cheap: one list walk, usually zero work), for each
    /// joystick Rewired has assigned to player 0 that IKMA has not seen yet,
    /// call Player.controllers.maps.LoadMap(Joystick, id, "Default",
    /// "DISABLED", startEnabled: true). The game's own GamepadInputHandler
    /// then flips into gamepad mode on the first button press, exactly as it
    /// would on a console. Nothing of the game's is reimplemented; its own
    /// map, its own mode, its own cursor grid. Verified signatures:
    /// _gamesource\rewired\Rewired\Player.cs (LoadMap, Joysticks),
    /// Joystick.cs (supportsVibration, SetVibration), ReInput.cs (isReady,
    /// players).
    ///
    /// VIBRATION. The game never vibrates a controller (zero call sites);
    /// every rumble is IKMA's. Rumble.Pulse drives both motors on every
    /// joystick that supports it, through Rewired. Patterns are a design
    /// decision Zamar has not made yet, so this build ships ONE provisional
    /// use — a short pulse when a character speaks, which he asked for by
    /// name — behind config switches, and the pattern numbers live in one
    /// place (Rumble.Dialogue) to be replaced by his table.
    ///
    /// CONFIG (BepInEx\config\com.zamar.ikma.cfg): Gamepad.LoadGameLayout
    /// (on), Gamepad.VibrationLevel (High; Session 34 replaced the old
    /// Vibration / VibrationStrength pair).
    ///
    /// NOT IN THIS BUILD: IKMA's own keys on the free buttons (Y, LB, RB,
    /// View, L3, R3, chords) — waiting on IKM Access\IKMA_Gamepad_Map.md.
    /// </remarks>
    public static class GamepadSupport
    {
        private const string MAP_CATEGORY = "Default";
        private const string MAP_LAYOUT   = "DISABLED";

        internal static ConfigEntry<bool>  LoadGameLayout;
        /// <summary>
        /// Session 34 - Zamar's four levels, set in IKMA Manager's settings
        /// ([Gamepad] VibrationLevel). Replaces the old on/off switch and the
        /// 0-to-1 strength number, which a player could not set by ear.
        /// </summary>
        public enum VibrationLevel { Off, Low, Medium, High }
        internal static ConfigEntry<VibrationLevel> Vibration;

        private static readonly HashSet<int> _mappedJoysticks = new HashSet<int>();
        private static bool _loggedNotReady;

        internal static void Init(ConfigFile config)
        {
            LoadGameLayout = config.Bind("Gamepad", "LoadGameLayout", true,
                "Load the game's own gamepad layout, which the PC build ships but never enables. Off = the pad does nothing, as in the unmodded PC game.");
            Vibration = config.Bind("Gamepad", "VibrationLevel", VibrationLevel.High,
                "Controller vibration from IKMA: Off, Low, Medium or High. The game itself never vibrates.");
        }

        /// <summary>Called once per frame from HotkeyManager.Update.</summary>
        internal static void Tick()
        {
            if (LoadGameLayout == null || !LoadGameLayout.Value) return;
            // Session 34 - while IKMA drives the pad (KeyIn), the game must
            // not ALSO read it: A would be IKMA's Enter and the game's
            // Select at once. So its layout is not loaded, exactly as the
            // game's keyboard map is switched off for keyboard players.
            if (KeyIn.PadOwned) return;
            try
            {
                if (!ReInput.isReady) return;
                var player = ReInput.players.GetPlayer(0);
                if (player == null) return;

                var joysticks = player.controllers.Joysticks;
                if (joysticks == null || joysticks.Count == 0) return;

                for (int i = 0; i < joysticks.Count; i++)
                {
                    var joystick = joysticks[i];
                    if (joystick == null || _mappedJoysticks.Contains(joystick.id)) continue;

                    player.controllers.maps.LoadMap(ControllerType.Joystick, joystick.id,
                                                    MAP_CATEGORY, MAP_LAYOUT, true);
                    _mappedJoysticks.Add(joystick.id);
                    Plugin.Log?.LogInfo(
                        $"IKMA GAMEPAD: loaded the game's layout for '{joystick.name}' (id {joystick.id}, vibration {(joystick.supportsVibration ? "supported" : "not supported")}).");
                }
            }
            catch (System.Exception e)
            {
                if (!_loggedNotReady)
                {
                    _loggedNotReady = true;
                    Plugin.Log?.LogWarning($"IKMA GAMEPAD: {e.GetType().Name}: {e.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Every rumble IKMA makes. One entry point, one place for the patterns.
    /// </summary>
    public static class Rumble
    {
        /// <summary>
        /// Both motors, on every pad that can, for <paramref name="seconds"/>.
        /// Levels are 0..1 before the config multiplier. Left is the low
        /// motor, right the high one, on an Xbox pad.
        /// </summary>
        public static void Pulse(float left, float right, float seconds)
        {
            try
            {
                if (!ReInput.isReady) return;
                // Session 34, Zamar: no rumble while the player is on the
                // keyboard - a pad lying on the desk must stay still. Follows
                // the last press, the same switch the spoken key names use.
                if (!KeyIn.LastWasPad) return;
                // Session 35, Zamar: "If the game is minimized the rumbles
                // should be disabled." Any time the game window is not the
                // one in front (minimized or behind another window).
                if (!UnityEngine.Application.isFocused) return;
                float k = LevelScale();
                if (k <= 0f) return;

                var player = ReInput.players.GetPlayer(0);
                if (player == null) return;
                var joysticks = player.controllers.Joysticks;
                if (joysticks == null) return;
                for (int i = 0; i < joysticks.Count; i++)
                {
                    var j = joysticks[i];
                    if (j == null || !j.supportsVibration) continue;
                    j.SetVibration(left * k, right * k, seconds, seconds);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA RUMBLE: {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // PROVISIONAL PATTERNS — Zamar's to replace. Numbers, not words, but
        // the same rule applies: what a blind player FEELS is his design.
        // ------------------------------------------------------------------

        /// <summary>
        /// A character spoke. One short, soft pulse at the start of the
        /// line, from the same hook that attributes the line (Plugin's
        /// TextDisplayer.ShowMessage postfix). Unattributed system text does
        /// not rumble.
        /// </summary>
        public static void Dialogue(string speaker)
        {
            // Session 34: a line with a voice sound is already rumbling -
            // the voice follower below moves with the sound itself. This
            // pulse is only for a line whose speaker has no voice sound.
            if (VoiceActive) return;

            // Session 34, Zamar: Leshy's lines rumble much harder than
            // anyone else's. "Single" is the game's speaker id when Leshy
            // talks at the table (Plugin.cs treats it as Leshy too). Mood
            // shading - the game tags each line with an Emotion - waits on
            // his map.
            if (speaker == "Leshy" || speaker == "Single")
                Pulse(0.9f, 1.0f, 0.35f);
            else
                Pulse(0.35f, 0.6f, 0.18f);
        }

        // ------------------------------------------------------------------
        // THE VOICE FOLLOWER. (Session 34.)
        //
        // Zamar: "is there any way we could have the rumble match the
        // waveform of those moods?" - and then: every character with a voice
        // sound, with Quiet and Surprise scaled.
        //
        // HOW. Every line of dialogue, TextDisplayer.PlayVoiceSound(Emotion)
        // (private) plays the speaker's voice sound for the line's mood and
        // keeps the AudioSource in its private field currentAudio
        // (_gamesource\code\DiskCardGame\TextDisplayer.cs; note in
        // dumps\dump_voice_rumble_from_decompile.txt). A postfix hands that
        // source here, and every frame IKMA reads the samples the source is
        // playing right now (AudioSource.GetOutputData) and sets the motors
        // from how loud they are. So the rumble IS the sound's shape: a laugh
        // rumbles in bursts because the laugh comes in bursts.
        //
        // FOUR SOUNDS, SIX MOODS. The game picks voice_frustrated for Anger,
        // voice_laughing for Laughter, voice_curious for Curious and
        // voice_calm for everything else - so Quiet and Surprise sound like
        // Neutral. Zamar: scale those two. Quiet is softer and low motor
        // only (a hush); Surprise leans on the high motor (a jolt).
        //
        // THE MOTORS. The low motor (left) follows the loudness; the high
        // motor (right) follows loudness squared, so it only joins on the
        // loud peaks - that is what makes the shape readable by hand.
        // Loudness is measured against the loudest moment of this sound so
        // far (never below a floor, so a soft start stays soft), because the
        // game plays voices at different volumes per speaker.
        //
        // PROVISIONAL NUMBERS, his to tune by feel. All of them are here.
        // ------------------------------------------------------------------
        private const float VOICE_PEAK_FLOOR = 0.06f;   // loudness that counts as "full" until something louder
        private const float VOICE_GATE       = 0.002f;  // below this is silence
        private const float QUIET_SCALE      = 0.4f;    // Quiet: low motor only, at this share
        private const float SURPRISE_HIGH    = 1.4f;    // Surprise: high motor boost
        private const float SURPRISE_LOW     = 0.8f;    // Surprise: low motor share

        private static AudioSource _voice;
        private static Emotion _voiceEmotion;
        private static float _voicePeak;
        private static bool _voiceRumbling;
        private static readonly float[] _voiceSamples = new float[256];

        /// <summary>A voice sound is playing and the rumble is following it.</summary>
        public static bool VoiceActive
        {
            get { try { return _voice != null && _voice.isPlaying; } catch { return false; } }
        }

        // ------------------------------------------------------------------
        // EVENT RUMBLES. (Session 34, Zamar.) The bell, and the scale hitting
        // 5: left motor leads when the scale tips against YOU, right motor
        // leads when you tip THEIRS. While one is playing, the voice follower
        // leaves the motors alone - otherwise it would overwrite the event on
        // the very next frame. PROVISIONAL numbers, his to tune by feel.
        // ------------------------------------------------------------------
        private static float _eventUntil;

        private static void EventPulse(float left, float right, float seconds)
        {
            Pulse(left, right, seconds);
            _eventUntil = Time.unscaledTime + seconds;
        }

        // ------------------------------------------------------------------
        // ITEM RUMBLES. (Session 34.) Zamar: a unique pattern for each item,
        // "your design for now", he adjusts by playtest. Each pattern is a
        // list of steps: low motor (left, heavy), high motor (right, sharp),
        // seconds. Keyed by the item's data name (ConsumableItemData.name,
        // the asset file names in data\consumables). Played when the game
        // completes the item's use - a cancelled use does not rumble.
        //
        // The idea behind each, so a change keeps the idea:
        //   Pliers          strain, then the yank
        //   Scissors        one big snip (Zamar)
        //   TrapperKnife    one long drawn cut
        //   SquirrelBottle  cork pop, light landing
        //   GoatBottle      cork pop, heavy landing
        //   FrozenOpossum   cork pop, cold shiver
        //   TerrainBottle   cork pop, boulder drop
        //   Hourglass       slow ticking sand
        //   MagnifyingGlass rising shimmer
        //   PiggyBank       the smash, then coins scattering
        //   FishHook        the cast, a pause, the tug
        //   SpecialDagger   the stab and the ache after
        //   BleachPot       one wide wash, in and out
        //   BirdLegFan      fluttering wings
        //   GooBottle       wet squelch
        //   PocketWatch     tick, tock, tick
        //   Act 3 (not in KM yet): Battery zap, BombRemote beep beep boom,
        //   ShieldGenerator rising hum.
        // ------------------------------------------------------------------
        private struct Step
        {
            public float L, R, S;
            public Step(float l, float r, float s) { L = l; R = r; S = s; }
        }

        private static Step[] P(params float[] v)
        {
            var steps = new Step[v.Length / 3];
            for (int i = 0; i < steps.Length; i++) steps[i] = new Step(v[i * 3], v[i * 3 + 1], v[i * 3 + 2]);
            return steps;
        }

        private static readonly Dictionary<string, Step[]> ItemPatterns = new Dictionary<string, Step[]>
        {
            { "Pliers",              P(0.2f,0.2f,0.30f,  0f,1f,0.12f) },
            { "Scissors",            P(0.4f,1f,0.15f) },   // Zamar, Session 34: one big snip
            { "TrapperKnife",        P(0.5f,0.7f,0.50f) },
            { "SquirrelBottle",      P(0f,0.7f,0.08f,  0f,0f,0.06f,  0.25f,0f,0.12f) },
            { "GoatBottle",          P(0f,0.7f,0.08f,  0f,0f,0.06f,  0.9f,0.1f,0.28f) },
            { "FrozenOpossumBottle", P(0f,0.7f,0.08f,  0f,0f,0.06f,  0.2f,0.5f,0.06f,  0f,0f,0.04f,  0.2f,0.5f,0.06f,  0f,0f,0.04f,  0.2f,0.5f,0.06f) },
            { "TerrainBottle",       P(0f,0.7f,0.08f,  0f,0f,0.06f,  1f,0.3f,0.40f) },
            { "Hourglass",           P(0f,0.4f,0.05f,  0f,0f,0.20f,  0f,0.4f,0.05f,  0f,0f,0.20f,  0f,0.4f,0.05f,  0f,0f,0.20f,  0f,0.4f,0.05f) },
            { "MagnifyingGlass",     P(0f,0.3f,0.15f,  0f,0.6f,0.15f,  0f,0.9f,0.15f) },
            { "PiggyBank",           P(1f,1f,0.15f,  0f,0.5f,0.05f,  0f,0f,0.05f,  0f,0.4f,0.05f,  0f,0f,0.05f,  0f,0.3f,0.05f) },
            { "FishHook",            P(0f,0.5f,0.10f,  0f,0f,0.25f,  0.8f,0.4f,0.30f) },
            { "SpecialDagger",       P(1f,1f,0.10f,  0.5f,0f,0.30f) },
            { "BleachPot",           P(0.3f,0.1f,0.20f,  0.5f,0.3f,0.20f,  0.3f,0.1f,0.20f) },
            { "BirdLegFan",          P(0f,0.6f,0.06f,  0f,0f,0.06f,  0f,0.6f,0.06f,  0f,0f,0.06f,  0f,0.6f,0.06f,  0f,0f,0.06f,  0f,0.6f,0.06f) },
            { "GooBottle",           P(0.7f,0f,0.20f,  0.3f,0.3f,0.15f,  0.6f,0f,0.20f) },
            { "PocketWatch",         P(0f,0.6f,0.05f,  0f,0f,0.25f,  0.6f,0f,0.05f,  0f,0f,0.25f,  0f,0.6f,0.05f) },
            { "Battery",             P(0f,1f,0.10f,  0f,0.3f,0.20f) },
            { "BombRemote",          P(0f,0.5f,0.05f,  0f,0f,0.15f,  0f,0.5f,0.05f,  0f,0f,0.15f,  1f,1f,0.40f) },
            { "ShieldGenerator",     P(0.2f,0.2f,0.15f,  0.4f,0.4f,0.15f,  0.6f,0.6f,0.25f) },
        };
        private static readonly Step[] DefaultItemPattern = P(0.5f, 0.5f, 0.2f);

        // THE SHORTEST RUMBLE WORTH SENDING. (Session 34.) Controller rumble
        // motors are spinning weights: they take up to ~50 ms to reach speed
        // (Boreas, ERM vs piezo), and a vibration has to last ~30 ms before
        // it is felt as one (Scientific Reports 2025, s41598-025-85778-6).
        // So any step that moves a motor lasts at least this long; silent
        // gaps keep their own length. Zamar's fix, applied to every pattern.
        private const float MIN_FELT_SECONDS = 0.1f;

        private static Step[] _steps;
        private static int _stepIndex;
        private static float _stepEndsAt;

        /// <summary>The game just completed the use of this item.</summary>
        public static void Item(string dataName)
        {
            Step[] steps;
            if (dataName == null || !ItemPatterns.TryGetValue(dataName, out steps)) steps = DefaultItemPattern;
            PlayPattern(steps);
        }

        // Session 34, Zamar: an "error buzz" each time a challenge triggers.
        // Three hard, even buzzes on both motors - the feel of a wrong-answer
        // buzzer, and unlike any item.
        private static readonly Step[] ChallengeBuzzPattern =
            P(0.7f,0.7f,0.10f,  0f,0f,0.05f,  0.7f,0.7f,0.10f,  0f,0f,0.05f,  0.7f,0.7f,0.10f);

        /// <summary>A Kaycee's Mod challenge just triggered (its icon flashed).</summary>
        public static void ChallengeBuzz() { PlayPattern(ChallengeBuzzPattern); }

        // ------------------------------------------------------------------
        // BROWSE TICKS. (Session 34, Zamar.) A very subtle tap on each arrow
        // press: left motor for Left / Up, right motor for Right / Down.
        // Keyboard arrows, D-pad and left stick alike (KeyIn). Not during a
        // conversation - arrows browse nothing there - and never on top of
        // an item, bell, scale or challenge rumble.
        // ------------------------------------------------------------------
        // Session 34, Zamar: "strength down, duration up". 0.05s was the
        // real problem: a rumble motor needs up to ~50 ms just to spin up,
        // so the tap ended before it was felt. See MIN_FELT_SECONDS.
        private const float BROWSE_LEVEL = 0.4f;
        private const float BROWSE_SECONDS = 0.12f;

        // ONLY WHEN THE SELECTION MOVED. (0.7.378, Zamar: "if the browse
        // doesn't change the highlighted option ... there should also be no
        // vibration" - e.g. the main menu's tween, before it is selectable.)
        //
        // IKMA has a dozen readers, each with its own list; none of them
        // reports "the selection changed". What every one of them does when
        // it changes is SPEAK the new option. So an arrow press arms the tap,
        // and the tap fires only if a line is spoken within BROWSE_WINDOW
        // that differs from the line spoken before the press. A press the
        // game ignored (tween) says nothing; a press at the end of a list
        // that re-reads the same option says the same thing. Neither taps.
        //
        // KNOWN MISS: two neighbours that read identically (three "Face
        // down." cards in a card choice) do not tap when moving between
        // them - the words are the only evidence IKMA has.
        private const float BROWSE_WINDOW = 0.3f;
        private static float _browseArmedUntil;
        private static bool _browseLeft;
        private static string _browseBaseline;
        private static string _lastSpokenText;

        private static void TickBrowse()
        {
            bool left  = KeyIn.Down(KeyCode.LeftArrow)  || KeyIn.Down(KeyCode.UpArrow);
            bool right = KeyIn.Down(KeyCode.RightArrow) || KeyIn.Down(KeyCode.DownArrow);
            // Session 34, Zamar: Tab (LT) taps too when it lands on a new card
            // - and, like the arrows, not when it stays on the same one. It
            // jumps FORWARD to the next playable card, so the right side.
            if (!left && !right && KeyIn.Down(KeyCode.Tab)) right = true;
            if (!left && !right) return;
            if (KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift)) return;   // Shift+arrow (deck, stand, turn) is not browsing
            if (KeyIn.Held(KeyCode.LeftControl) || KeyIn.Held(KeyCode.RightControl)) return;   // Ctrl+arrow is the review history (Session 35)
            try { if (DialogueAdvancer.ConversationRunning()) return; } catch { }
            _browseArmedUntil = Time.unscaledTime + BROWSE_WINDOW;
            _browseLeft = left;
            _browseBaseline = _lastSpokenText;
        }

        /// <summary>
        /// A browse tap right now, on the left or right motor. Session 34: the
        /// LB / RB page flips on the options screen (Zamar). Never over an
        /// item, bell, scale or challenge rumble.
        /// </summary>
        public static void Tap(bool left)
        {
            if (_steps != null || Time.unscaledTime < _eventUntil) return;
            Pulse(left ? BROWSE_LEVEL : 0f, left ? 0f : BROWSE_LEVEL, BROWSE_SECONDS);
            _eventUntil = Time.unscaledTime + BROWSE_SECONDS;
        }

        /// <summary>
        /// Every spoken line, from CardReader.Speak (the choke point). Fires
        /// an armed browse tap when the line shows the selection moved.
        /// </summary>
        internal static void NoteSpoken(string text)
        {
            _lastSpokenText = text;
            if (Time.unscaledTime > _browseArmedUntil) return;
            _browseArmedUntil = 0f;
            if (string.Equals(text, _browseBaseline, System.StringComparison.Ordinal)) return;
            if (_steps != null || Time.unscaledTime < _eventUntil) return;
            Pulse(_browseLeft ? BROWSE_LEVEL : 0f, _browseLeft ? 0f : BROWSE_LEVEL, BROWSE_SECONDS);
            // Held for its length, so a voice rumble does not erase it.
            _eventUntil = Time.unscaledTime + BROWSE_SECONDS;
        }

        private static void PlayPattern(Step[] steps)
        {
            float total = 0f;
            foreach (var st in steps) total += (st.L > 0f || st.R > 0f) ? Mathf.Max(st.S, MIN_FELT_SECONDS) : st.S;
            _steps = steps;
            _stepIndex = -1;
            _stepEndsAt = 0f;
            _eventUntil = Time.unscaledTime + total;
        }

        private static void TickSteps()
        {
            if (_steps == null) return;
            if (Time.unscaledTime < _stepEndsAt) return;
            _stepIndex++;
            if (_stepIndex >= _steps.Length) { _steps = null; return; }
            var st = _steps[_stepIndex];
            // The step's own length, so a gap (0, 0) is real silence; a step
            // that moves a motor is never shorter than MIN_FELT_SECONDS.
            float secs = (st.L > 0f || st.R > 0f) ? Mathf.Max(st.S, MIN_FELT_SECONDS) : st.S;
            Pulse(st.L, st.R, secs);
            _stepEndsAt = Time.unscaledTime + secs;
        }

        /// <summary>
        /// A draw from the pile (Session 34, Zamar): the Squirrel deck is a
        /// smallish rumble on the right, your deck a medium one on the left.
        /// Both clearly bigger than the browse tap. PROVISIONAL numbers.
        /// </summary>
        public static void Draw(bool squirrel)
        {
            if (squirrel) EventPulse(0f, 0.6f, 0.2f);
            else          EventPulse(0.75f, 0f, 0.25f);
        }

        /// <summary>The combat bell was rung (mouse, E or Y/LT/RT).</summary>
        public static void Bell() { EventPulse(0.25f, 0.9f, 0.2f); }

        // The scale edge. LifeManager.ShowDamageSequence is a coroutine, so its
        // postfix runs BEFORE the weights drop. It arms this; the tick fires
        // the rumble on the frame the game's own Balance reaches the edge -
        // the moment the scale slams - and gives up after ARM_SECONDS.
        private const float ARM_SECONDS = 8f;
        private static bool _scaleArmed, _scalePlayerLoses;
        private static float _scaleArmedAt;

        internal static void ArmScaleEdge(bool playerLoses)
        {
            _scaleArmed = true;
            _scalePlayerLoses = playerLoses;
            _scaleArmedAt = Time.unscaledTime;
        }

        private static void TickScale()
        {
            if (!_scaleArmed) return;
            if (Time.unscaledTime - _scaleArmedAt > ARM_SECONDS) { _scaleArmed = false; return; }
            int balance;
            try
            {
                var lm = Singleton<LifeManager>.Instance;   // only while armed - see ikma_singleton_cost
                if (lm == null) { _scaleArmed = false; return; }
                balance = lm.Balance;
            }
            catch { _scaleArmed = false; return; }

            if (_scalePlayerLoses ? balance <= -5 : balance >= 5)
            {
                _scaleArmed = false;
                if (_scalePlayerLoses) EventPulse(1.0f, 0.35f, 0.6f);   // yours tipped: left leads
                else                   EventPulse(0.35f, 1.0f, 0.6f);   // theirs tipped: right leads
            }
        }

        /// <summary>From the PlayVoiceSound postfix: follow this sound.</summary>
        internal static void FollowVoice(AudioSource source, Emotion emotion)
        {
            _voice = source;
            _voiceEmotion = emotion;
            _voicePeak = VOICE_PEAK_FLOOR;
        }

        /// <summary>Once a frame, from HotkeyManager.UpdateOuter.</summary>
        internal static void TickVoice()
        {
            TickScale();
            TickSteps();
            TickBrowse();
            if (_voice == null) return;
            if (Time.unscaledTime < _eventUntil) return;   // an event rumble owns the motors

            if (!VoiceActive)
            {
                // The sound ended (or was replaced, or the game paused):
                // stop the motors now rather than letting the last frame run.
                _voice = null;
                if (_voiceRumbling) { Pulse(0f, 0f, 0.05f); _voiceRumbling = false; }
                return;
            }

            float rms;
            try
            {
                _voice.GetOutputData(_voiceSamples, 0);
                float sum = 0f;
                for (int i = 0; i < _voiceSamples.Length; i++) sum += _voiceSamples[i] * _voiceSamples[i];
                rms = Mathf.Sqrt(sum / _voiceSamples.Length);
            }
            catch { _voice = null; return; }

            if (rms > _voicePeak) _voicePeak = rms;
            float level = rms < VOICE_GATE ? 0f : rms / _voicePeak;

            float low = level;
            float high = level * level;
            switch (_voiceEmotion)
            {
                case Emotion.Quiet:
                    low *= QUIET_SCALE;
                    high = 0f;
                    break;
                case Emotion.Surprise:
                    low *= SURPRISE_LOW;
                    high = Mathf.Min(1f, level * SURPRISE_HIGH);
                    break;
            }

            // A short duration, refreshed every frame: if the ticks ever stop,
            // the motors stop on their own a tenth of a second later.
            Pulse(low, high, 0.1f);
            _voiceRumbling = true;
        }

        /// <summary>
        /// The player's level as a multiplier. PROVISIONAL numbers, his to
        /// tune by feel: High is full strength.
        /// </summary>
        private static float LevelScale()
        {
            var level = GamepadSupport.Vibration != null
                ? GamepadSupport.Vibration.Value
                : GamepadSupport.VibrationLevel.High;
            switch (level)
            {
                case GamepadSupport.VibrationLevel.Off:    return 0f;
                case GamepadSupport.VibrationLevel.Low:    return 0.35f;
                case GamepadSupport.VibrationLevel.Medium: return 0.65f;
                default:                                   return 1f;
            }
        }
    }
    /// <summary>
    /// Session 34 - the voice follower's hook. PlayVoiceSound(Emotion) is
    /// PRIVATE, declared once, on TextDisplayer only (the GBC DialogueSpeaker
    /// one is a different class, Act 2). currentAudio is its PRIVATE
    /// AudioSource field, set by that method before it returns. Registered
    /// through Plugin.TryPatch: a new patch on a private member, so a wrong
    /// guess costs only the voice rumble.
    /// </summary>
    public static class TextDisplayer_PlayVoiceSound_Patch
    {
        public static void Postfix(TextDisplayer __instance, Emotion emotion)
        {
            try
            {
                var source = Traverse.Create(__instance).Field("currentAudio").GetValue<AudioSource>();
                if (source != null) Rumble.FollowVoice(source, emotion);
            }
            catch { }
        }
    }
    /// <summary>
    /// Session 34 - rumble on the bell. CombatBell3D.OnBellPressed() is
    /// NONPUBLIC, protected override, CombatBell3D.cs:62
    /// (dumps\dump_bell_from_decompile.txt). IKMA's E key calls this same
    /// method, so mouse, keyboard and pad all reach it. Through TryPatch.
    /// </summary>
    public static class CombatBell3D_OnBellPressed_Patch
    {
        public static void Postfix() { try { Rumble.Bell(); } catch { } }
    }
    /// <summary>
    /// Session 34 - rumble on each item use. ConsumableItemSlot's
    /// CompleteItemActivation() is NONPUBLIC protected virtual
    /// (ConsumableItemSlot.cs:137), called from ConsumeItem only when the use
    /// was NOT cancelled. A PREFIX, because the method destroys the item;
    /// ItemSlot.Item (PUBLIC get) and Item.Data (PUBLIC) are read before that.
    /// HammerItemSlot overrides it (Act 3) and is not covered. Through TryPatch.
    /// </summary>
    public static class ConsumableItemSlot_CompleteItemActivation_Patch
    {
        public static void Prefix(ConsumableItemSlot __instance)
        {
            string dataName = null;
            try { dataName = __instance?.Item?.Data?.name; } catch { }
            try { Rumble.Item(dataName); } catch { }
            try { ItemUseNarrator.NoteUseCompleted(dataName); } catch { }   // Bleach, Birdleg Fan lines
        }
    }
}
