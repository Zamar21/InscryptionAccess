// BootScreenReader.cs
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// THE FIRST THING THE GAME SHOWS, before the title screen. (0.7.232.)
    ///
    /// Zamar: "when you open the game from outside KM's menu, it opens on a
    /// small play button. We need this to read and have input. ... Have Space
    /// bar repeat that. no other buttons should exist here though."
    ///
    /// WHAT THE GAME DOES, from FirstPlaySceneController:
    ///
    ///   Initialize()            the panel goes up, the button appears 1s later
    ///   OnStartButtonPressed()  public — what the button's onClick calls
    ///   IntroSequence()         a fixed timeline of WaitForSeconds:
    ///       +0.55s  startAnim.Play("start")   — the developer parade
    ///       +3.55s  PlaySound2D("intro_voice") — the spoken intro line
    ///       +9.30s  subtitlesPanel on
    ///       +14.8s  subtitlesPanel off
    ///       +22.3s  Completed — the title screen takes over
    ///
    /// EVERY WAIT ABOVE IS A CONSTANT IN THE GAME'S OWN CODE, so the parade's
    /// window is known exactly: it is the three seconds between the animation
    /// starting and the voice line. WHERE INSIDE that window each logo lands is
    /// not in the code — it is in an animation clip — so the two logo lines are
    /// placed at IKMA's best reading of it and every beat is logged with its
    /// real elapsed time. One pass over this screen and Zamar can move them by
    /// the numbers rather than by feel.
    ///
    /// THE SUBTITLE PANEL IS READ, NOT TIMED. Its text is watched for rather
    /// than spoken on a schedule — on-screen words are parity, and a schedule
    /// would be a second guess stacked on the first.
    ///
    /// NOT POLLED INTO EXISTENCE. The controller arrives from a Harmony patch
    /// on its own Initialize; nothing here searches the scene or touches
    /// Singleton&lt;T&gt;.Instance from Update. (The 0.7.216 crash.)
    /// </summary>
    public static class BootScreenReader
    {
        private static ManualLogSource _log;

        private static FirstPlaySceneController _controller;
        private static FieldInfo _buttonField, _subtitlesField;

        // The scene's own controller, which owns the title card and the
        // "PRESS ANY BUTTON" text that follows the intro. Captured from its
        // Start prefix; see OnStartScreen.
        private static StartScreenController _start;
        private static FieldInfo _pressStartField;
        private static bool _titleCardUp;
        private static bool _titleCardSpoken;

        private static bool _announced;
        private static bool _pressed;

        // The intro timeline, measured from the press. Unscaled, because the
        // game's own waits here are WaitForSeconds on a menu that never pauses.
        private static float _sincePress = -1f;
        private static int   _beat;

        // IN TIME ORDER, and the order is load-bearing — the beats below run
        // as a chain. Zamar, 0.7.234, watching the picture: "Devolver Digital,
        // this callout needs to be delayed by about 3 seconds." That puts the
        // publisher AFTER the voice line starts rather than before it, which is
        // why the chain was resequenced rather than one number edited.
        // ==================================================================
        // MEASURED OFF THE SCREEN, 0.7.270. (Was: read out of the source.)
        //
        // The timecode overlay did exactly the job it was built for. Zamar,
        // watching the intro with the clock on screen: "the Dan Mullens
        // callout should be around 01:05 by my count, and the Devolver
        // Digital should be about 3:90."
        //
        // Both were guesses before this and both were wrong in the direction
        // the guess had to be wrong in — the WINDOW is a game constant, but
        // where each logo lands inside the animation clip is not, so 1.10 and
        // 4.65 were placed by reading FirstPlaySceneController's waits and
        // reasoning about the parade. The Devolver line was three quarters of
        // a second late.
        //
        // These two are no longer provisional as to WHEN. The words were
        // never provisional — they are his.
        // ==================================================================
        private const float STUDIO_AT    = 1.05f;   // MEASURED (was 1.10 by reading)
        private const float VOICE_AT     = 3.55f;   // game constant
        // REVERTED to 4.65, 0.7.271. Zamar heard 3.90 in place: "revert the
        // timing change to devolver it was way too early."
        //
        // WORTH RECORDING WHY THE MEASUREMENT DID NOT TRANSFER, because the
        // clock itself is sound: his own log has the subtitle panel observed
        // at 9.34s against the game's constant of 9.30, so _sincePress tracks
        // the game's timeline to within 40ms. The number he read off the
        // screen was the moment the logo APPEARS; the moment a line about it
        // should be spoken is not the same instant, and only his ear settles
        // which. 4.65 is the value he has now heard twice and kept.
        private const float PUBLISHER_AT = 4.65f;

        // ==================================================================
        // ==================================================================
        // NO AUDIO DESCRIPTION HERE. CLOSED BY ZAMAR, 0.7.274.
        //
        // "let's remove the AD from this intro scene since it's just a black
        // screen anyways."
        //
        // That is a parity ruling and it is the right one: there is nothing on
        // screen to describe between the logos and the title card, so a
        // description would be inventing picture for a blind player that a
        // sighted player is not being shown. 0.7.270 to 0.7.273 placed, moved
        // and re-placed a line into a window that never needed one — three
        // builds spent measuring an empty frame.
        //
        // WHAT THE EXERCISE WAS ACTUALLY WORTH, and it is why the machinery
        // stays: the timecode overlay and the beat markers came out of it, and
        // they are what any real audio description will be written against.
        // His words: "Good work building out this tech though that will be
        // helpful for 1.0 content."
        //
        // The seam is still logged at VOICE_AT, as it has been since 0.7.232,
        // so nothing has to be rediscovered if a later act wants one here.
        // ==================================================================

        private const float DONE_AT      = 22.4f;   // game constant, +a margin

        private static bool _subtitleWasUp;
        private static string _lastSubtitle;

        public static void Init(ManualLogSource log) => _log = log;

        // ------------------------------------------------------------------
        // Arming
        // ------------------------------------------------------------------

        internal static void Capture(FirstPlaySceneController controller)
        {
            _controller     = controller;
            _announced      = false;
            _pressed        = false;
            _sincePress     = -1f;
            _beat           = 0;
            _subtitleWasUp  = false;
            _lastSubtitle   = null;

            _log?.LogInfo("IKMA BOOT: first-play screen armed.");
        }

        public static void Reset()
        {
            _start           = null;
            _titleCardUp     = false;
            _titleCardSpoken = false;

            _controller    = null;
            _announced     = false;
            _pressed       = false;
            _sincePress    = -1f;
            _beat          = 0;
            _subtitleWasUp = false;
            _lastSubtitle  = null;
        }

        /// <summary>
        /// True from the moment the screen is armed until the intro is over.
        /// It stays true THROUGH the intro on purpose: the intro takes the
        /// keyboard with it, so nothing else can answer a key over the top of
        /// a sequence the player cannot interrupt anyway.
        /// </summary>
        public static bool Active
        {
            get
            {
                if (_controller == null) return false;
                try
                {
                    if (_controller.Completed) return false;
                    if (_controller.gameObject == null) return false;
                    if (!_controller.gameObject.activeInHierarchy) return false;
                }
                catch { return false; }
                return true;
            }
        }

        // ------------------------------------------------------------------
        // The button
        // ------------------------------------------------------------------

        private static GameObject StartButtonObject()
        {
            if (_controller == null) return null;
            try
            {
                if (_buttonField == null)
                    _buttonField = typeof(FirstPlaySceneController).GetField(
                        "startButton", BindingFlags.Instance | BindingFlags.NonPublic);

                if (_buttonField == null)
                {
                    _log?.LogWarning("IKMA BOOT: FirstPlaySceneController.startButton not found.");
                    return null;
                }

                var button = _buttonField.GetValue(_controller) as Component;
                return button == null ? null : button.gameObject;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA BOOT: reading the start button threw {e.GetType().Name}.");
                return null;
            }
        }

        private static bool ButtonShowing()
        {
            var obj = StartButtonObject();
            if (obj == null) return false;
            try { return obj.activeInHierarchy; } catch { return false; }
        }

        /// <summary>The button appears a second after the panel does, so the
        /// line waits for it rather than for a clock.</summary>
        public static bool NeedsAnnouncement()
            => Active && !_announced && !_pressed && ButtonShowing();

        public static void Announce()
        {
            _announced = true;

            // NOTHING TALKS OVER THE FIRST THING THE PLAYER EVER HEARS.
            // (0.7.233.) Zamar: "When starting up from the main game's side it
            // should not say anything before the play button line."
            //
            // The launch autosave is refused at its own source now (see
            // EventNarrator.NoteSave), so this is the backstop rather than the
            // fix: anything else the mod happens to have queued while the game
            // was loading describes a moment before the player pressed
            // anything, and this screen is where the session actually starts.
            CombatAnnouncer.DropCommentary("the boot screen is the first thing spoken");

            Speech.Browse(Vocabulary.BootPlayButton());
        }

        public static void AnnounceCurrent()
        {
            if (_pressed) return;
            Speech.Browse(Vocabulary.BootPlayButton());
        }

        /// <summary>
        /// Enter. Calls the game's own onClick handler — OnStartButtonPressed
        /// is PUBLIC and is exactly what the button is wired to, so the mouse
        /// path and the keyboard path are the same path.
        /// </summary>
        public static void Press()
        {
            if (_controller == null || _pressed) return;
            if (!ButtonShowing()) return;

            _log?.LogInfo("IKMA BOOT: pressing the play button.");
            try { _controller.OnStartButtonPressed(); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA BOOT: pressing threw {e.GetType().Name}.");
            }
        }

        /// <summary>
        /// Called from the OnStartButtonPressed postfix, so the timeline starts
        /// whether the press came from IKMA or from a mouse click.
        /// </summary>
        internal static void OnPressed()
        {
            if (_pressed) return;
            _pressed    = true;
            _sincePress = 0f;
            _beat       = 0;
            _log?.LogInfo("IKMA BOOT: play pressed — intro timeline starts.");
        }

        // ------------------------------------------------------------------
        // The intro
        // ------------------------------------------------------------------

        private static GameObject SubtitlesPanel()
        {
            if (_controller == null) return null;
            try
            {
                if (_subtitlesField == null)
                    _subtitlesField = typeof(FirstPlaySceneController).GetField(
                        "subtitlesPanel", BindingFlags.Instance | BindingFlags.NonPublic);

                return _subtitlesField?.GetValue(_controller) as GameObject;
            }
            catch { return null; }
        }

        /// <summary>
        /// Whatever the subtitle panel is showing, as one line. Every component
        /// under it that carries a string, in hierarchy order — the same "read
        /// what the screen holds, name no member" rule the run-end stats and
        /// the pause menu's info bar already use.
        ///
        /// BY REFLECTION, not by type. UnityEngine.UI.Text lives in
        /// UnityEngine.UI, which the csproj does not reference, and the panel
        /// may just as well be TextMeshPro. Asking each component for a `text`
        /// string works for both and keeps the build exactly as it is — the
        /// same call VideoNarrator makes for UnityEngine.VideoModule.
        /// </summary>
        private static string SubtitleText(GameObject panel)
        {
            if (panel == null) return null;

            var sb = new System.Text.StringBuilder();
            try
            {
                foreach (var c in panel.GetComponentsInChildren<Component>(true))
                {
                    string txt = UiText.Of(c);
                    if (string.IsNullOrEmpty(txt)) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(txt.Trim());
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA BOOT: reading the subtitle panel threw {e.GetType().Name}.");
            }

            string s = sb.ToString().Trim();
            return s.Length == 0 ? null : s;
        }

        /// <summary>
        /// Seconds since the play button was pressed, or 0 when the intro is
        /// not running. Published for the timecode overlay (0.7.269) — this is
        /// the clock the two logo lines are placed on, and the one the Devolver
        /// line was tweaked against.
        /// </summary>
        internal static float IntroElapsed
        {
            get
            {
                // A CLOCK THAT IS NOT RUNNING IS NOT A TIME. (0.7.270.)
                //
                // 0.7.269 shipped this as (_pressed && _controller != null &&
                // _beat < 4), and Zamar's screenshot of the New Game glitch
                // screen — minutes later — still read "0:22.31 boot intro".
                //
                // 22.31 and DONE_AT is 22.4, which names the cause exactly:
                // Tick is called from a branch of HotkeyManager.Update that
                // stops being reached the moment the boot screen hands over,
                // so _sincePress froze 0.09s short of the beat that would have
                // retired it. Every condition in the old test stayed true
                // forever and the readout became a remembered number.
                //
                // So it asks whether the clock is LIVE rather than whether it
                // was ever started: the frame Tick last ran, checked against
                // this frame. That holds however Tick comes to stop — a scene
                // change, an early return, a branch that moves — which is the
                // project's rule about never caching a claim about the game's
                // state, applied to a claim about IKMA's own.
                // _beat 4 is FINISHED, and it is the only beat that ends the
                // intro. (Beat 5 existed for the audio description and went
                // with it at 0.7.274; the == test is kept rather than restored
                // to >= because it is the correct shape either way — the
                // question is "is the intro over", not "how far has it got".)
                if (!_pressed || _controller == null || _beat == 4) return 0f;

                try
                {
                    if (UnityEngine.Time.frameCount - _lastTickFrame > 1) return 0f;
                }
                catch { return 0f; }

                return _sincePress;
            }
        }

        private static int _lastTickFrame = -999;

        // ==================================================================
        // WHAT FIRED, AND WHEN. (0.7.271, for the timecode overlay.)
        //
        // The clock alone was not enough, and the Devolver revert is why. A
        // time read off the screen says when the PICTURE changed; it does not
        // say when IKMA spoke, and the gap between those two is the thing
        // being tuned. With the beat printed beside the clock at the moment it
        // fires, Zamar can see both halves at once and give a number that is
        // about the line rather than about the logo.
        //
        // Plain fields rather than a call into VideoTimecode, so nothing here
        // has to be wrapped in IKMA_DEV — these cost three assignments and the
        // overlay is the only reader.
        // ==================================================================
        internal static string LastBeatName;
        internal static float  LastBeatAt;
        internal static float  LastBeatWallTime = -999f;

        private static void NoteBeat(string name)
        {
            LastBeatName = name;
            LastBeatAt   = _sincePress;
            try { LastBeatWallTime = UnityEngine.Time.unscaledTime; } catch { }
        }

        /// <summary>Ticked once per frame from HotkeyManager.Update.</summary>
        public static void Tick(float deltaTime)
        {
            if (!_pressed || _controller == null) return;

            try { _lastTickFrame = UnityEngine.Time.frameCount; } catch { }

            _sincePress += deltaTime;

            if (_beat == 0 && _sincePress >= STUDIO_AT)
            {
                _beat = 1;
                _log?.LogInfo($"IKMA PROVISIONAL: boot studio logo line at {_sincePress:0.00}s after the press.");
                NoteBeat("studio logo");
                Speech.Browse(Vocabulary.BootStudioLogo());
            }
            else if (_beat == 1 && _sincePress >= VOICE_AT)
            {
                _beat = 2;
                // NOTHING IS SPOKEN HERE. The intro line is real recorded
                // speech and it plays out loud; talking over it would be the
                // one thing the speech gate exists to prevent. This beat is
                // logged so the AUDIO DESCRIPTION — what the picture is doing
                // while that line plays — has a measured seam to sit in as soon
                // as Zamar writes it. See docs/VIDEO_AD.md for the convention.
                NoteBeat("intro voice starts (silent)");
                _log?.LogInfo($"IKMA BOOT: intro voice line begins at {_sincePress:0.00}s — " +
                              "audio-description seam, no words yet.");
            }
            else if (_beat == 2 && _sincePress >= PUBLISHER_AT)
            {
                _beat = 3;
                _log?.LogInfo($"IKMA PROVISIONAL: boot publisher logo line at {_sincePress:0.00}s after the press.");
                NoteBeat("publisher logo");
                Speech.Browse(Vocabulary.BootPublisherLogo());
            }
            else if (_beat == 3 && _sincePress >= DONE_AT)
            {
                _beat = 4;
                _log?.LogInfo($"IKMA BOOT: intro finished at {_sincePress:0.00}s.");
            }

            // The panel, watched rather than timed.
            var panel = SubtitlesPanel();
            bool up = false;
            try { up = panel != null && panel.activeInHierarchy; } catch { }

            if (up && !_subtitleWasUp)
            {
                // LOGGED, NOT SPOKEN. (0.7.233.) Zamar: "Do not read the
                // subtitles of the intro video, they are spoken by the
                // character." 0.7.232 read "Okay. Time to figure out what's on
                // this thing." on top of the recording that was saying it — the
                // subtitle is a caption for speech that is already audible, so
                // reading it is not parity, it is an echo. The text and its
                // timing stay in the log because they are the measurements the
                // audio description will be written against.
                string text = SubtitleText(panel);
                _lastSubtitle = text;
                _log?.LogInfo($"IKMA BOOT: subtitle panel up at {_sincePress:0.00}s, text='{text ?? "<none>"}' " +
                              "— not spoken, the character says it.");
            }
            _subtitleWasUp = up;
        }

        // ------------------------------------------------------------------
        // THE TITLE CARD. (0.7.233.)
        //
        // Zamar: "When the titlecard hits and the Press Any Button text
        // appears, then say Inscryption."
        //
        // StartScreenController.StartSequence shows the title animation, waits
        // 1.75 seconds and then switches on `pressStartText`, and the whole
        // screen then sits on WaitUntil(Input.anyKey). The text going active is
        // therefore the exact moment the screen becomes something the player
        // can answer, so that object is watched rather than a clock counted —
        // and it is watched on BOTH paths into the title card, the first-play
        // boot and a plain launch with the tutorial already done.
        //
        // Ticked unconditionally from Update, like NodeProbe: _start is null in
        // every scene but this one, so it costs a null check everywhere else.
        // ------------------------------------------------------------------
        internal static void OnStartScreen(StartScreenController controller)
        {
            _start           = controller;
            _titleCardUp     = false;
            _titleCardSpoken = false;
            _log?.LogInfo("IKMA BOOT: start screen armed for the title card.");
        }

        private static GameObject PressStartObject()
        {
            if (_start == null) return null;
            try
            {
                if (_pressStartField == null)
                    _pressStartField = typeof(StartScreenController).GetField(
                        "pressStartText", BindingFlags.Instance | BindingFlags.NonPublic);

                if (_pressStartField == null)
                {
                    _log?.LogWarning("IKMA BOOT: StartScreenController.pressStartText not found. The title card will be silent.");
                    _start = null;   // asked once; do not warn every frame
                    return null;
                }

                var anim = _pressStartField.GetValue(_start) as Component;
                return anim == null ? null : anim.gameObject;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA BOOT: reading the press-start text threw {e.GetType().Name}.");
                _start = null;
                return null;
            }
        }

        /// <summary>True while the title card is waiting on a keypress. The
        /// screen takes ANY key, so IKMA claims none of them — this exists so
        /// Escape can be held back from opening the footage menu behind it.</summary>
        public static bool TitleCardShowing => _titleCardUp;

        /// <summary>Ticked once per frame from HotkeyManager.Update.</summary>
        public static void TickTitleCard()
        {
            if (_start == null) return;

            var obj = PressStartObject();
            bool up = false;
            try { up = obj != null && obj.activeInHierarchy; } catch { }

            if (up && !_titleCardUp && !_titleCardSpoken)
            {
                _titleCardSpoken = true;
                _log?.LogInfo("IKMA BOOT: press-any-button text is up — title card spoken.");
                Speech.Browse(Vocabulary.BaseTitleCard());
            }

            _titleCardUp = up;
        }

        public static void Help()
        {
            Speech.Browse(
                Vocabulary.Boot.OpeningScreenEnterPresses);
        }
    }
}
