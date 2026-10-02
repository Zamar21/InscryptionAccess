// MapReader.cs
using System.Collections.Generic;
using BepInEx.Logging;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// World map navigation. (Roadmap item 2 — Session 8 FIRST DRAFT.)
    ///
    /// Model (HSA browse+activate): arrows cycle the paths ahead of the
    /// current node, M announces position and all paths, Enter clicks the
    /// chosen node through the universal primitive (CursorSelectStart/End —
    /// same mechanism that fixed the draw piles). HotkeyManager owns the
    /// input polling and travel-watcher coroutine; this class owns node
    /// naming, choice enumeration, and announcements.
    ///
    /// KNOWN ITERATION POINTS (expect these to be session 9's first bugs):
    /// 1. connectedNodes may include backward links — if arrow-browsing
    ///    offers nodes behind you, the choice list needs filtering (likely by
    ///    comparing node grid row/position).
    /// 2. Friendly names below are best-effort mappings from NodeData class
    ///    names — Zamar should correct any that mislabel the actual event.
    /// 3. Battle-vs-map context detection lives in HotkeyManager
    ///    (IsBattleActive) and is heuristic — see notes there.
    /// </summary>
    public static class MapReader
    {
        private static ManualLogSource _log;
        private static int _choiceIndex = 0;

        // Has the player heard a path yet since arriving here? (0.7.49.)
        //
        // Zamar: "when picking a path I can never arrow and get it to start on
        // option 1. If I go left or right to start, it always starts on option
        // 2." He is right, and it is not a wrap bug — the index starts at 0 and
        // the FIRST press moved off it, so option 1 could only ever be reached
        // by going all the way round. With two paths that is every arrival.
        //
        // The first press on a fresh map now READS where the cursor already is
        // instead of moving. Every press after it moves as before. Same shape as
        // a menu, which announces the option under the cursor on entry.
        private static bool _choiceHeard = false;

        // NodeData class name (minus "NodeData") -> spoken name.
        // Best-effort Act 1 / Kaycee's Mod labels; fallback prettifies the
        // raw class name so unknown nodes are still identifiable.
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _friendlyNames => Loc.PerLanguage(ref _friendlyNamesCache, ref _friendlyNamesLanguage, Build_friendlyNames);
        private static Dictionary<string, string> Build_friendlyNames() =>
            new Dictionary<string, string>
        {
            // Session 9: Zamar's terminology — a standard battle node is an
            // "Encounter" (matches how Kaycee's Mod players talk about them).
            // Totem and boss battles keep distinct names: they are meaningfully
            // different fights and a blind player needs the warning.
            { "CardBattle",        Vocabulary.Map.Encounter },
            { "TotemBattle",       Vocabulary.Map.TotemBattle },
            { "BossBattle",        Vocabulary.Map.BossBattle },
            { "CardChoices",       Vocabulary.Map.CardChoice },
            { "CardBundleChoices", Vocabulary.Map.CardBundleChoice },
            { "ChooseRareCard",    Vocabulary.Map.RareCardChoice },
            { "BuildTotem",        Vocabulary.Woodcarver },
            { "CardMerge",         Vocabulary.SacrificeStone },
            { "CardStatBoost",     Vocabulary.Campfire },
            { "TradePelts",        Vocabulary.Trader },
            { "BuyPelts",          Vocabulary.Trapper },
            { "GainConsumables",   Vocabulary.ItemPickupName },
            { "DuplicateMerge",    Vocabulary.Mycologists },
            { "CardRemove",        Vocabulary.Map.CardRemoval },
            { "RecycleCard",       Vocabulary.Map.CardRecycling },
            { "CopyCard",          Vocabulary.Map.CardCopying },
            { "BoulderChoice",     Vocabulary.Map.BoulderChoice },
            { "ModifySideDeck",    Vocabulary.Map.SideDeckChange },
            { "DeckTrial",         Vocabulary.Map.DeckTrial },
            { "VictoryFeast",      Vocabulary.Map.VictoryFeast },
        };
        private static Dictionary<string, string> _friendlyNamesCache;
        private static string _friendlyNamesLanguage;

        /// <summary>
        /// Random, Kin, Cost or Death card — or null for a node that is not a
        /// card choice. KIN, not "Tribe": the game's word is tribe, Zamar's word
        /// is kin, and kin is what every card in this mod already says.
        /// </summary>
        private static string CardChoiceKindOf(object data)
        {
            var choices = data as CardChoicesNodeData;
            if (choices == null) return null;

            try
            {
                switch (choices.choicesType)
                {
                    case CardChoicesType.Random:    return Vocabulary.Map.RandomChoice;
                    case CardChoicesType.Tribe:     return Vocabulary.Kin;
                    case CardChoicesType.Cost:      return Vocabulary.Map.CostChoice;
                    case CardChoicesType.Deathcard: return Vocabulary.DeathCard;
                    default:                        return null;
                }
            }
            catch { return null; }
        }

        public static void Init(ManualLogSource log)
        {
            _log = log;
        }

        // ------------------------------------------------------------------
        // True when map browsing should be possible: the map manager exists,
        // has a current node, and the map isn't mid-move.
        // ------------------------------------------------------------------
        // ==================================================================
        // THE CAMERA LADDER — BY PRESSING THE GAME'S OWN BUTTON.
        // (Session 16, fourth attempt, and the first one that obeys the first
        // rule rather than working around it.)
        //
        // THREE ATTEMPTS FAILED THE SAME WAY: each tried to REPRODUCE what the
        // scroll wheel does rather than ask the game to do it.
        //   0.7.55  laid the deck out without moving the camera.
        //   0.7.56  moved the camera in one jump. Softlock, then a black screen,
        //           and a control mode that stayed poisoned for the whole run.
        //   0.7.57  walked ViewController.altTransitionInputs one rung at a time
        //           — and that table turned out to be the WRONG table.
        //
        // THE 0.7.57 LOG IS WHAT SETTLED IT, and the finding is a negative one:
        // all 17 rungs of altTransitionInputs are LookLeft / LookRight moves
        // between the side views (Consumables and Scales). Not one of them
        // mentions MapDeckReview or FirstPerson. The up-and-down ladder the
        // player actually rides is not in that table at all.
        //
        // Meanwhile, in the same log, the wheel did it effortlessly:
        //   IKMA VIEW: MapDefault -> MapArial.
        //   IKMA VIEW: MapArial -> MapDeckReview.
        //
        // SO STOP LOOKING FOR THE LADDER AND PRESS THE BUTTON. The Button enum
        // is in the log — None, ScrollWheel, Select, LookUp, LookDown, LookLeft,
        // LookRight, ... — and IKMA already injects Button.Cancel through three
        // InputButtons patches for the placement cancel. Injecting LookUp is the
        // same mechanism aimed at the same question.
        //
        // This is Zamar's rule stated exactly: "our mod should always follow
        // what the game does, and should move the game in the way it expects
        // to." A wheel click IS Button.LookUp. Whatever the game does in
        // response — move the camera, lay the deck out, change the control mode,
        // anything nobody has found yet — it does all of it, because it is the
        // one running the code.
        //
        // STEP AND VERIFY, never a blind sequence. One press, wait, ask
        // CurrentView. Reached the goal: stop. Moved but not there yet: press
        // again. DID NOT MOVE AT ALL: stop, say so, and log — an injection the
        // game ignores is a finding, and the sacrifice wait and the placement
        // cancel have both already proved injection can be silently dropped.
        // The step budget means a wrong guess about direction costs four presses
        // and a sentence, never a run.
        // ==================================================================

        private static ViewManager _viewManager;
        private static float _nextViewLookup;
        private const float VIEW_LOOKUP_BACKOFF = 2f;

        private static ViewManager Views()
        {
            if (_viewManager != null) return _viewManager;
            if (UnityEngine.Time.unscaledTime < _nextViewLookup) return null;
            _nextViewLookup = UnityEngine.Time.unscaledTime + VIEW_LOOKUP_BACKOFF;

            try { _viewManager = Singleton<ViewManager>.Instance; }
            catch { _viewManager = null; }
            return _viewManager;
        }

        public static View? CurrentView()
        {
            var vm = Views();
            if (vm == null) return null;
            try { return vm.CurrentView; }
            catch { return null; }
        }

        // The rungs, all four OBSERVED in Zamar's own wheel transitions rather
        // than picked out of the forty-member View enum.
        private const View MAP_VIEW      = View.MapDefault;
        private const View DECK_VIEW     = View.MapDeckReview;
        private const View STANDING_VIEW = View.FirstPerson;

        public static bool Standing   => CurrentView() == STANDING_VIEW;
        public static bool InDeckView => CurrentView() == DECK_VIEW;

        // ------------------------------------------------------------------
        // The climb. PRESS ONCE, THEN WAIT FOR THE CAMERA TO ACTUALLY MOVE.
        //
        // 0.7.58 pressed on a fixed 0.45s clock, and both of its failures were
        // that clock rather than the method:
        //   - A transition slower than the window read as "the button did
        //     nothing", so Zamar heard "Could not stand up right now" for a
        //     stand-up that then worked.
        //   - A press whose effect had not landed yet got another press on top.
        //
        // A view change is an EVENT, so wait for the event. The only thing a
        // clock is still needed for is the case where the game genuinely ignores
        // the input — and that is a real finding, so it gets a generous window
        // and an honest line rather than a retry.
        // ------------------------------------------------------------------
        private const int MAX_STEPS = 4;

        // WAIT FOR THE TRANSITION, NOT JUST FOR A FEW FRAMES.
        //
        // 0.7.63 left six frames between presses and the second one was still
        // swallowed every time:
        //
        //   press 1 of LookDown, at MapDeckReview.
        //   MapDeckReview -> MapArial.
        //   press 2 of LookDown, at MapArial.
        //   climb ended — LookDown did not move the camera from MapArial.
        //
        // The very next climb pressed LookDown from MapArial and it worked, so
        // the button is fine — the game will not take a view input WHILE a view
        // transition is playing. CurrentView flips the moment a transition
        // starts, so "it moved" is true long before the camera has arrived, and
        // six frames is a tenth of a second into a move that takes longer.
        //
        // A wall-clock settle, not a frame count: frames are the wrong unit for
        // a tween and this project has made that mistake before, in the
        // sacrifice/bone fold.
        //
        // AND A FIXED WAIT IS STILL A GUESS. Zamar, 0.7.64: "working as expected
        // ... but feels a tad slow." Half a second was picked to be safely
        // longer than a transition, which means it is always longer than it
        // needs to be.
        //
        // So ask the camera instead: press again the moment its position stops
        // changing. That is the transition's own definition of finished, it is
        // as fast as the game allows, and it cannot be wrong for a slower
        // machine or a longer tween the way a constant can. The wall-clock
        // values below are only floor and ceiling.
        private const float PRESS_GAP_MIN     = 0.08f;
        private const float PRESS_GAP_MAX     = 0.60f;
        private const float CAMERA_STILL_EPSILON = 0.001f;

        /// <summary>The last choice layout logged, so it prints once. (0.7.315.)</summary>
        private static string _lastChoiceLayout;

        private static UnityEngine.Vector3? _lastCameraPos;
        private static UnityEngine.Vector3? _lastCameraRot;
        private static int _stillFrames;
        private const int STILL_FRAMES_REQUIRED = 3;

        // A RELEASED FRAME BETWEEN PRESSES, and this is the whole of the 0.7.59
        // Backspace bug. From the log:
        //
        //   IKMA INPUT: injecting LookDown (frames 27560-27560).
        //   IKMA VIEW: press 1 of LookDown, at MapDeckReview.
        //   IKMA INPUT: injecting LookDown (frames 27561-27561).
        //   IKMA VIEW: press 2 of LookDown, at MapArial.
        //   IKMA VIEW: climb ended — LookDown did not move the camera from MapArial.
        //
        // CurrentView updates the instant the transition starts, so "it moved"
        // was true one frame later and the next press went out immediately. Down
        // on frame 27560 and down on 27561 is not two presses to the game — it
        // is one button held for two frames, so the second GetButtonDown never
        // happened. The very next climb pressed LookDown from MapArial and it
        // worked first time, which is the proof.
        //
        // A button has to come UP before it can go down again.
        private const int PRESS_GAP_FRAMES = 6;

        // How long to wait for a press to show up in the camera before deciding
        // the game did not take it.
        //
        // THIS WAS THE SECOND ZAMAR KEPT HEARING. It was 1.2s, so every refused
        // press cost 1.2s of standing on the overhead view before IKMA even
        // decided it had been refused — and the 0.12s backoff steps were doing
        // nothing to help, because the backoff is not what he was waiting on.
        //
        // It can be short. CurrentView flips when a transition STARTS, not when
        // it finishes — the log has "MapDeckReview -> MapArial" on the frame
        // after the press — so a press that landed shows up almost immediately.
        // A quarter of a second is generous for that and cheap to be wrong about,
        // because a retry follows either way.
        private const float CHANGE_TIMEOUT = 0.20f;

        // THE RETRY IS A DIFFERENT KIND OF PRESS, NOT MORE OF THEM.
        //
        // Never a wider window — a wider window is more presses, and that is the
        // overshoot. Same one frame, different question: the first attempt
        // answers every input query the game might ask, and the retry answers
        // only GetButtonDown, for a caller that reads presses rather than held
        // state.
        //
        // The 0.7.62 playtest settled which order these belong in:
        // ViewController polls HELD, so press-first meant every camera move
        // waited out a timeout before working. That timeout was the lag.

        private static bool   _climbing;
        private static View   _goal;
        private static string _climbButton;
        private static string _climbLabel;
        private static string _climbArrival;
        private static string _climbRefusalExtra;
        private static int    _stepsTaken;
        private static View?  _viewBeforePress;
        private static float  _pressedAt;
        private static int    _pressedOnFrame;
        private static bool   _retriedWide;

        // A SWALLOWED PRESS BACKS OFF AND TRIES AGAIN, it does not end the climb.
        //
        // Every 0.7.65 log says the same thing: LookDown from MapDeckReview
        // works, and the LookDown that follows it at MapArial is refused —
        // every single time, both ways of pressing. Yet LookUp from MapArial
        // works, and LookDown from MapArial works when it is the first press of
        // a climb seconds later. The button is fine; the game will not take the
        // same view input twice this close together.
        //
        // Giving up there is what stranded Zamar on the overhead view again. A
        // longer wait and another go costs a fraction of a second; stopping
        // costs him the screen he asked for.
        // SMALL STEPS, MANY OF THEM. 0.7.66 added 0.45s per refusal, so the
        // second press landed somewhere between 0.45s and 1.35s late and Zamar
        // heard it as "hung on the aerial view for a second or two".
        //
        // The right wait is unknown and is probably short; a coarse ladder
        // overshoots it by design. Retrying every 0.12s finds it instead of
        // stepping past it, and the log records which attempt won so the
        // constant can stop being a search.
        private static float  _backoff;
        private const float   BACKOFF_STEP = 0.06f;
        private const float   BACKOFF_MAX  = 1.8f;

        // A CAMERA CLIMB OR A WALK BACK TO THE TABLE. Both mean the player is
        // between places, and HotkeyManager swallows every key while this is
        // true — a key answered mid-walk would be answered by a layer for
        // somewhere they are leaving.
        public static bool Walking => _climbing || _walkingToTable;

        private static void Climb(View goal, string button, string what,
                                  string arrival, string refusalExtra = null)
        {
            if (_climbing) return;

            var now = CurrentView();
            if (now == null)
            {
                Speech.Browse(Vocabulary.Map.CannotRightNow(what));
                _log?.LogInfo($"IKMA VIEW: cannot {what} — the camera cannot be asked where it is.");
                return;
            }

            if (now.Value == goal)
            {
                if (!string.IsNullOrEmpty(arrival)) Speech.Browse(arrival);
                return;
            }

            _climbing          = true;
            _goal              = goal;
            _climbButton       = button;
            _climbLabel        = what;
            _climbArrival      = arrival;
            _climbRefusalExtra = refusalExtra;
            _stepsTaken        = 0;
            _viewBeforePress   = null;
            _retriedWide       = false;
            _lastCameraPos     = null;
            _lastCameraRot     = null;
            _stillFrames       = 0;
            _backoff           = 0f;

            _log?.LogInfo($"IKMA VIEW: climbing from {now} to {goal} by pressing {button} ({what}).");
            Press(0);
        }

        private static void Press(int extraFrames, bool downOnly = false)
        {
            // ==================================================================
            // NEVER PRESS WHILE ALREADY AT THE GOAL. (0.7.251.)
            //
            // This is what cost Zamar his controls. Coming back from the deck at
            // the Woodcarver, the descent read, verbatim:
            //
            //   press 2 of LookDown, at MapArial
            //   press 3 of LookDown, at MapDefault      <- MapDefault IS the goal
            //   press 4 of LookDown, at FirstPerson     <- stood him up
            //
            // TickClimb's first act is to check exactly this and stop, so the
            // climb ended up pressing from a rung it had already been told to
            // stand on — the view changed between that check and this call, and
            // every retry and backoff path below leads here without asking
            // again. One more press past MapDefault is not a harmless overshoot:
            // LookDown there walks the player away from the table, which is a
            // screen with no map, no reader and nothing bound.
            //
            // THE GENERAL SHAPE, and 0.7.60 and 0.7.63 are the other two
            // instances: a climb must re-ask where it is at the moment it acts,
            // not at the moment it decided to act. A check whose answer is used
            // a frame later is a guess.
            // ==================================================================
            var here = CurrentView();
            if (here != null && here.Value == _goal)
            {
                _log?.LogInfo(
                    $"IKMA VIEW: already at {_goal} when press {_stepsTaken + 1} of " +
                    $"{_climbButton} was about to go out — not pressing.");
                _climbing = false;
                if (!string.IsNullOrEmpty(_climbArrival))
                {
                    CombatAnnouncer.DropCommentary($"arrived at {_goal}");
                    Speech.Browse(_climbArrival);
                }
                return;
            }

            _viewBeforePress = CurrentView();
            _pressedAt       = UnityEngine.Time.unscaledTime;
            _stillFrames     = 0;
            _pressedOnFrame  = UnityEngine.Time.frameCount;
            _stepsTaken++;

            // One frame by default. See HotkeyManager.InjectButton — a
            // multi-frame window is multiple presses, and that is what walked
            // Zamar past the map and into standing.
            HotkeyManager.InjectButton(_climbButton, extraFrames, downOnly);
            _log?.LogInfo(
                $"IKMA VIEW: press {_stepsTaken} of {_climbButton}, at " +
                $"{_viewBeforePress?.ToString() ?? "unknown"}" +
                (downOnly ? " (press only)" : "") + ".");
        }

        /// <summary>
        /// Has the camera stopped moving since the last frame? Two samples of
        /// CameraParent.position; anything under the epsilon is a tween that has
        /// arrived. Returns true when the camera cannot be read, so an
        /// unanswerable question falls back to the wall-clock ceiling rather
        /// than stalling the climb.
        /// </summary>
        private static bool CameraIsStill()
        {
            var t = CameraTransform();
            if (t == null) { _lastCameraPos = null; return true; }

            UnityEngine.Vector3 now;
            try { now = t.position; }
            catch { _lastCameraPos = null; return true; }

            UnityEngine.Vector3 rot;
            try { rot = t.eulerAngles; }
            catch { rot = UnityEngine.Vector3.zero; }

            var wasPos = _lastCameraPos;
            var wasRot = _lastCameraRot;
            _lastCameraPos = now;
            _lastCameraRot = rot;

            if (wasPos == null || wasRot == null) { _stillFrames = 0; return false; }

            // ROTATION AS WELL AS POSITION. MapDeckReview to MapArial barely
            // moves the camera — it mostly tilts — so a position-only test read
            // "still" while the transition was very much running, and the next
            // press went out into a game that was not listening.
            bool posStill = (now - wasPos.Value).sqrMagnitude
                            < CAMERA_STILL_EPSILON * CAMERA_STILL_EPSILON;
            bool rotStill = (rot - wasRot.Value).sqrMagnitude
                            < CAMERA_STILL_EPSILON * CAMERA_STILL_EPSILON;

            // STILL FOR SEVERAL FRAMES, not one. A tween eases in and out, so
            // its first and last frames barely move — a single-frame test calls
            // "still" at the START of a transition and the next press goes out
            // into a game that is not listening. That refusal is the whole of
            // the delay leaving the deck view: going UP is two accepted presses
            // and feels instant, going DOWN is one accepted press and one
            // refused, and the refusal costs a timeout plus a retry.
            if (!(posStill && rotStill)) { _stillFrames = 0; return false; }

            _stillFrames++;
            return _stillFrames >= STILL_FRAMES_REQUIRED;
        }

        private static void StopClimb(string reason, bool refuse)
        {
            _climbing = false;
            _log?.LogInfo($"IKMA VIEW: climb ended — {reason}.");

            if (!refuse) return;

            string line = Vocabulary.Map.CannotRightNow(_climbLabel);
            if (!string.IsNullOrEmpty(_climbRefusalExtra))
                line += " " + _climbRefusalExtra;
            Speech.Browse(line);
        }

        private static void TickClimb()
        {
            if (!_climbing) return;

            var now = CurrentView();

            if (now != null && now.Value == _goal)
            {
                _climbing = false;
                _log?.LogInfo(
                    $"IKMA VIEW: reached {_goal} in {_stepsTaken} press(es)" +
                    (_backoff > 0f ? $", last wait {_backoff:0.00}s" : "") + ".");
                // ARRIVING SOMEWHERE CUTS THE CHATTER ABOUT WHERE YOU WERE.
                // (Session 17, Zamar: the map opening "should stomp any low
                // priority info, like from an H key press.")
                //
                // Speak(interrupt) cuts what is in the air; it does nothing about
                // the next queued Info line, which then talks straight over the
                // arrival. Commentary only — combat results, dialogue and prompts
                // are never dropped.
                if (!string.IsNullOrEmpty(_climbArrival))
                {
                    CombatAnnouncer.DropCommentary($"arrived at {_goal}");
                    Speech.Browse(_climbArrival);
                }
                return;
            }

            // Still waiting for this press to land.
            if (now != null && now == _viewBeforePress)
            {
                if (UnityEngine.Time.unscaledTime - _pressedAt < CHANGE_TIMEOUT) return;

                // ONLY RETRY ON A CLIMB THAT HAS NOT MOVED AT ALL. The wide
                // retry exists to answer "does this input work here" — once a
                // rung has moved, that is answered, and a second press would
                // just be an overshoot waiting for a slow transition. That is
                // how 0.7.60's Backspace walked past the map and stood him up.
                // ANY step may need the other kind of press, not just the
                // first. 0.7.63 restricted this to step one, so when press two
                // was swallowed the climb simply gave up — and gave up ON THE
                // OVERHEAD VIEW, the one screen Zamar asked never to be left on.
                // The retry is reset after every successful rung, so this is one
                // extra press per rung at worst.
                if (!_retriedWide)
                {
                    _retriedWide = true;
                    _stepsTaken--;   // the retry is the same step, not a new one
                    _log?.LogInfo(
                        $"IKMA VIEW: {_climbButton} was not taken at {now}; " +
                        "retrying as a press-only input.");
                    Press(0, downOnly: true);
                    return;
                }

                // Both kinds of press refused. Wait longer and try again rather
                // than abandoning the player on a rung — see the note on
                // _backoff. Only a spent budget ends the climb.
                if (_backoff < BACKOFF_MAX)
                {
                    _backoff    += BACKOFF_STEP;
                    _retriedWide = false;
                    _stepsTaken--;
                    _log?.LogInfo(
                        $"IKMA VIEW: {_climbButton} refused at {now}; " +
                        $"waiting {_backoff:0.00}s and trying again.");
                    Press(0);
                    return;
                }

                // The game does not take this input here. That is the answer,
                // not a reason to press harder — the sacrifice wait and the
                // placement cancel have both proved injection can be dropped.
                StopClimb(
                    $"{_climbButton} did not move the camera from {now}, " +
                    $"after backing off to {_backoff:0.00}s",
                    refuse: true);
                return;
            }

            // It moved, but not far enough.
            if (_stepsTaken >= MAX_STEPS)
            {
                StopClimb(
                    $"gave up after {MAX_STEPS} presses of {_climbButton}; wanted {_goal}, " +
                    $"stopped at {now?.ToString() ?? "unknown"}",
                    refuse: true);
                return;
            }

            // Let the button up first — pressing on the next frame reads as one
            // held button rather than two presses.
            if (UnityEngine.Time.frameCount - _pressedOnFrame < PRESS_GAP_FRAMES) return;

            float waited = UnityEngine.Time.unscaledTime - _pressedAt;
            if (waited < PRESS_GAP_MIN + _backoff) return;

            // Then let the camera finish. The game refuses a view input while a
            // transition is playing, and the transition is over when the camera
            // stops moving.
            if (waited < PRESS_GAP_MAX && !CameraIsStill()) return;

            _retriedWide = false;
            _backoff     = 0f;
            Press(0);
        }

        public static void CancelClimb()
        {
            if (!_climbing) return;
            _climbing = false;
            _log?.LogInfo("IKMA VIEW: climb cancelled.");
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// Log every camera view transition. Off by default — see the note at
        /// the transition log below. Turn on for camera-ladder work.
        /// </summary>
        internal static bool VerboseViewLog = false;

        private static View? _lastView;

        public static void TickViewChange()
        {
            TickClimb();
            TickWalk();
            TickSurvey();

            var now = CurrentView();
            if (now == null) return;
            if (_lastView == now) return;

            var was = _lastView;
            _lastView = now;

            // VIEW TRANSITION LOGGING IS OFF BY DEFAULT. (0.7.168.) Zamar:
            // "This log is too busy, hide the views and focus calls from the
            // log if we can."
            //
            // The game changes camera view constantly — the Prospector's board
            // wipe alone produced eight of these interleaved with the lines he
            // was actually trying to read. Every OTHER view log in this file
            // (the climb ladder, refusals, cancellations) is kept, because
            // those fire only when IKMA is DRIVING the camera and each one
            // marks a decision. This one just narrates the game moving.
            //
            // Behind a flag rather than deleted: the camera ladder is the most
            // fragile thing in the project and this line is how its rungs were
            // mapped. Turn it on before touching the map, the deck view or
            // standing up.
            if (VerboseViewLog)
                _log?.LogInfo($"IKMA VIEW: {(was?.ToString() ?? "unknown")} -> {now}.");

            // Told on arrival and departure BY ANY ROUTE — arrow, wheel, or the
            // game moving the camera itself.
            // The rung it came FROM is handed over, because that is where
            // Backspace has to put the player back — the map from the map, the
            // card choice from the card choice.
            if (now == DECK_VIEW)      DeckViewReader.OnViewEntered(_deckOrigin);
            else if (was == DECK_VIEW) DeckViewReader.OnViewLeft();
        }

        // ==================================================================
        // WHICH AUTHORED DIGIT OPENED THE ZOOM THAT IS UP. (0.7.111.)
        //
        // Ten objects in the cabin carry the GameObject name "ZoomInteractable",
        // so the name cannot say which one the player is looking at. The digit
        // Zamar authored can, and this is the one fact IKMA has to carry from
        // the press to the back-out to say the same word both times.
        //
        // THE OBJECT IS KEPT ALONGSIDE THE WORD ON PURPOSE. Anything the player
        // can change behind IKMA's back will eventually strand a cached value —
        // this project has the softlock to prove it — so the pair is only
        // trusted when the object still matches the zoom actually in the air.
        // Otherwise it is dropped, and the short form is spoken instead.
        // ==================================================================
        private static string _zoomOpenedName;
        private static object _zoomOpenedObj;
        // 0.7.423: the authored "space|facing|digit" that opened it, so Space
        // can read his closer line for that object (CabinMap.ZoomedLookFor).
        private static string _zoomOpenedKey;

        public static void ResetViewLog()
        {
            _lastView       = null;
            _viewManager    = null;
            _nextViewLookup = 0f;
            _climbing       = false;
            _walkingToTable = false;

            // A scene change ends any zoom, so the remembered word cannot
            // survive one. Dropped here rather than left to the back-out path,
            // which does not run when the scene is torn out from under it.
            _zoomOpenedName = null;
            _zoomOpenedObj  = null;
            _zoomOpenedKey  = null;
        }

        // ------------------------------------------------------------------
        // Shift+Up at the map. Two wheel clicks up, pressed rather than faked.
        // ------------------------------------------------------------------
        /// <summary>
        /// Where the player was standing when they asked for the deck. This is
        /// the rung Backspace has to put them back on.
        ///
        /// THE 0.7.61 BUG: DeckViewReader was handed the view the camera came
        /// from on the LAST rung of the climb, which is MapArial — the top-down
        /// overview the climb passes THROUGH. So Backspace dutifully returned
        /// him to the one screen he had asked never to be left on. The log says
        /// it outright: "IKMA DECK: opened from MapArial (the map)."
        ///
        /// The origin of the whole climb is the answer, not the previous rung.
        /// </summary>
        private static View? _deckOrigin;

        public static View? DeckOrigin => _deckOrigin;

        /// <summary>
        /// Tell the deck view where Backspace should put the player back, for a
        /// route into it that is not ShowDeck's climb. (0.7.257 — the Woodcarver
        /// switches views directly rather than climbing, because its own screen
        /// is not a rung on the Look ladder.)
        /// </summary>
        internal static void NoteDeckOrigin(View? from) => _deckOrigin = from;

        public static void ShowDeck()
        {
            if (InDeckView) { DeckViewReader.AnnounceCurrent(); return; }

            _deckOrigin = CurrentView();

            // "Your deck." AT ONCE, before the camera has gone anywhere.
            // (Session 16, Zamar's ask.) The climb plus the card-count settle is
            // over a second of silence, and a blind player cannot tell that from
            // a key that never registered. The full read lands behind it.
            //
            // And it tells the reader it has already said this, so the read that
            // follows drops its own "Your deck." — Zamar heard it twice. The
            // flag rather than deleting it from the read, because arriving by
            // SCROLL WHEEL never passes through here and still needs naming.
            // KIN AMOUNTS RIDE THIS LINE FROM 0.7.275. Zamar: "I also want to
            // hear Kin amounts called out on Shift Up." 0.7.260 put them on
            // the Woodcarver's ack only, because that screen is where the
            // decision turns on them — but the question "what is my deck made
            // of" is the same question wherever the deck is opened from.
            //
            // NodeScreenReader.KinBreakdown is the one composer; this is the
            // second caller, not a second copy. Each path speaks the ack
            // exactly once, so nothing is said twice.
            // Bare again at 0.7.276 — the kin breakdown moved to the deck
            // reader's own line, which is the one that is not interrupted.
            Speech.Browse(Vocabulary.YourDeck);
            DeckViewReader.NoteOpeningSpoken();
            Climb(DECK_VIEW, "LookUp", Vocabulary.Map.BringYourDeckUp, null);
        }

        /// <summary>Shift+Down at the map. One wheel click down.</summary>
        public static void StandUp()
        {
            if (Standing)
            {
                Speech.Browse(Vocabulary.Map.YouAreAlreadyStanding);
                return;
            }

            Climb(STANDING_VIEW, "LookDown", Vocabulary.Map.StandUp,
                Vocabulary.Map.StandingUpFromThe);
        }

        /// <summary>
        /// Backspace from anywhere on the ladder. Climbs back to the map from
        /// whichever rung the player is on, in whichever direction that is —
        /// Zamar: "Backspace while standing up, regardless of position, should
        /// cause the player to sit back down at the map."
        /// </summary>
        /// <summary>
        /// Climb back to a specific rung. The deck view uses this to return to
        /// whatever it was opened FROM — which is not always the map. Zamar
        /// opened it from the card choice screen and Backspace tried to reach
        /// MapDefault, pressed straight past Choices, and reported that it could
        /// not enter the map.
        /// </summary>
        public static void ReturnTo(View goal, string placeName, bool announce)
        {
            var now = CurrentView();
            if (now == null || now.Value == goal)
            {
                if (announce) Speech.Browse(Vocabulary.Map.YouAreAt(placeName));
                return;
            }

            string button = now.Value == STANDING_VIEW ? "LookUp" : "LookDown";

            string extra = CardChoiceReader.Active
                ? Vocabulary.MustSelectCardFirst
                : null;

            Climb(goal, button, Vocabulary.Map.ReturnTo(placeName),
                  announce ? Vocabulary.Map.BackAt(placeName) : null, extra);
        }

        public static void ReturnToMap(bool announce)
        {
            var now = CurrentView();
            if (now == null || now.Value == MAP_VIEW)
            {
                if (announce) Speech.Browse(Vocabulary.Map.YouAreAtThe);
                return;
            }

            // Standing is BELOW the map, the deck and the overhead view are
            // ABOVE it, so the way back is not the same button from both. The
            // direction is decided by where the player actually is, which the
            // camera has just told us.
            string button = now.Value == STANDING_VIEW ? "LookUp" : "LookDown";

            // WHY THE REFUSAL NEEDS A REASON HERE. The deck view can be opened
            // from the card choice screen, and there the game will not give the
            // map back until a card has been taken. "Cannot return to the map
            // right now." on its own is true and useless — it names no way out.
            // Zamar's wording.
            string extra = CardChoiceReader.Active
                ? Vocabulary.MustSelectCardFirst
                : null;

            // HIS WORDING, Session 17: the map names itself when it opens.
            // "Back at the map." only made sense coming FROM somewhere, and the
            // map is also reached by routes that are not a return. The fuller
            // arrival line ("Map. You are at X...") is deliberately unchanged —
            // his call, so the orientation read keeps its own opener.
            Climb(MAP_VIEW, button, Vocabulary.Map.ReturnToTheMap,
                  announce ? Vocabulary.Map.GameMap : null, extra);
        }


        // ==================================================================
        // BACKSPACE WHEN YOU HAVE WALKED AWAY FROM THE TABLE. (Session 17.)
        //
        // Backspace has always sat the player down, and it has always worked
        // only at the table. Every attempt from anywhere else logged the same
        // line: "LookUp did not move the camera from FirstPerson, either way
        // of pressing it."
        //
        // THE REFUSAL IS THE GAME BEING CORRECT. Zamar, asked how a sighted
        // player sits down after walking off: you cannot — you walk back to
        // the table first. So IKMA was not failing to press a button; it was
        // asking for something the game does not allow, and reporting that
        // honestly.
        //
        // WHAT WAS DELIBERATELY NOT DONE. GameFlowManager declares a PUBLIC
        // TransitionFromFirstPerson(Boolean), and Part1GameFlowManager does
        // not override it. Calling it would almost certainly seat the player
        // from anywhere in the room — and it would be IKMA overruling a
        // refusal the game meant, which is the exact shape that produced two
        // softlocks and a black screen on the camera ladder. A call that
        // succeeds proves nothing about what it left behind.
        //
        // So the mod does what the player does: walks back, then presses the
        // button that already works from there. Nothing new is asked of the
        // game at any point.
        //
        // HIS CHOICE OF BEHAVIOUR, Session 17: one key does the whole thing,
        // and it is quiet — "Returning to the table." at the start, the
        // existing seated line at the end, nothing in between. That is why
        // every press below goes through InjectButton directly rather than
        // through Step or Turn, which speak.
        // ==================================================================

        private static bool   _walkingToTable;
        private static int    _walkPresses;
        private static int    _walkWaits;
        private static bool   _walkRetried;
        private static bool   _walkAnnounced;
        private static float  _walkPressedAt;
        private static float  _walkNextCheckAt;
        private static string _walkLastButton;
        private static int    _walkLastSpaceId;
        private static float  _walkLastHeading;

        // Worst case is the gramophone: four steps and four turns. The budget
        // is a stop, not a schedule — it exists so a walk that is going wrong
        // ends in a sentence rather than pressing buttons at the room forever.
        private const int WALK_PRESS_BUDGET = 30;
        private const int WALK_WAIT_BUDGET  = 40;

        // WAIT FOR THE STEP TO LAND, NOT FOR A CLOCK TO RUN OUT. (0.7.75.)
        //
        // 0.7.74 held a flat 0.45s after every press, which is the survey's
        // settle time and was picked because it was already there. Zamar asked
        // for the walk about 50% faster, and a smaller fixed number is the wrong
        // way to give him that: too small reads a position mid-tween, too large
        // is the delay he is asking about, and the right value is different for
        // a turn than for a step.
        //
        // This is the project's own settled rule — never speak a count that has
        // not settled, wait for it to stop CHANGING — applied to movement. The
        // walker now looks every 80ms and moves on the instant the game says the
        // player is standing in a new space. CabinMap does the detecting for
        // free: SpaceAt returns null unless y is at the resting height and the
        // position is within four units of a space centre, and FacingAt returns
        // null for a heading between two compass points. **A mid-tween sample
        // cannot be mistaken for an arrival**, so looking early costs one more
        // look and never a wrong answer.
        private const float WALK_POLL_SECONDS = 0.08f;

        // How long NOTHING may change before the press counts as refused. A
        // blocked step is silent, so only elapsed time can tell "still moving"
        // apart from "that press did nothing". Generous on purpose: this is the
        // failure path, and paying half a second on the rare refusal is better
        // than calling a slow tween a refusal and pressing again into it.
        private const float WALK_REFUSED_SECONDS = 0.55f;

        // Matches the window CabinMap.FacingAt uses to name a heading, so the
        // walker and the description reader never disagree about which way the
        // player is looking.
        private const float FACING_TOLERANCE = 20f;

        private static CabinMap.Space CurrentSpace()
        {
            var t = CameraTransform();
            if (t == null) return null;
            try { return CabinMap.SpaceAt(t.position); }
            catch { return null; }
        }

        private static bool FacingMatches(float heading, float bearing)
        {
            float delta = ((heading - bearing) % 360f + 360f) % 360f;
            return delta < FACING_TOLERANCE || delta > 360f - FACING_TOLERANCE;
        }

        /// <summary>
        /// Backspace while standing. Sits down where that is legal, and walks
        /// back to the table first where it is not.
        /// </summary>
        public static void SitDown()
        {
            if (_walkingToTable || _climbing) return;

            // ZOOMED IN? BACK OUT OF THAT FIRST. The camera is not at a standing
            // space while a zoom is up, so every position lookup fails and the
            // walk can never start — that is exactly how his 0.7.80 safe zoom
            // became a softlock. Backspace means "leave what I am in", and the
            // zoom is the innermost thing to leave.
            if (ExitZoom()) return;


            // Not standing at all — the ladder owns this, unchanged.
            if (!Standing) { ReturnToMap(announce: true); return; }

            var space = CurrentSpace();

            // ALREADY AT THE TABLE: press the button the way it has always been
            // pressed. No walk, and no walk line.
            if (space != null && space.Id == CabinMap.TABLE_SPACE_ID)
            {
                ReturnToMap(announce: true);
                return;
            }

            // A KNOWN SPACE WITH NO ROUTE is the one case that fails outright.
            if (space != null)
            {
                var check = CabinMap.RouteBetween(space.Id, CabinMap.TABLE_SPACE_ID);
                if (check == null || check.Count < 2)
                {
                    _log?.LogWarning(
                        $"IKMA CABIN: no route from space {space.Id} to the table.");
                    Speech.Browse(Vocabulary.CouldNotReachTable);
                    return;
                }
                _log?.LogInfo(
                    "IKMA CABIN: walking back to the table, route " +
                    string.Join(" -> ", check.ConvertAll(id => "space " + id).ToArray()) + ".");
            }
            else
            {
                // POSITION UNREADABLE AT THE MOMENT OF THE PRESS — the player
                // pressed Backspace during a step, before the camera settled.
                // SpaceAt returns null on purpose there.
                //
                // 0.7.74 fell straight through to the seated press, which the
                // game refuses anywhere but the table, so a fast player got a
                // refusal for asking at the wrong instant. The walk already
                // knows how to wait for a position, so it starts and waits —
                // and the announcement moves to the first tick that can see
                // where the player actually is, so a player who was at the
                // table all along never hears the walk line.
                _log?.LogInfo(
                    "IKMA CABIN: Backspace pressed mid-step; waiting for the position to settle.");
            }

            _walkingToTable  = true;
            _walkPresses     = 0;
            _walkWaits       = 0;
            _walkRetried     = false;
            _walkLastButton  = null;
            // -1 when the position was unreadable at the keypress. Only ever
            // consulted once a press has gone out, and the first tick has no
            // previous press, so the sentinel is never compared against a
            // real space.
            _walkLastSpaceId = (space == null) ? -1 : space.Id;
            _walkLastHeading = Heading() ?? 0f;
            _walkNextCheckAt = 0f;
            _walkAnnounced   = false;
            _walkPressedAt   = UnityEngine.Time.unscaledTime;
        }

        private static void TickWalk()
        {
            if (!_walkingToTable) return;

            // The game left first person on its own — an event, a load, the
            // wheel. Whatever the walk was for is gone; drop it silently
            // rather than pressing movement buttons into another screen.
            if (!Standing)
            {
                _walkingToTable = false;
                _log?.LogInfo("IKMA CABIN: walk abandoned — no longer standing.");
                return;
            }

            if (UnityEngine.Time.unscaledTime < _walkNextCheckAt) return;

            var space = CurrentSpace();
            float? heading = Heading();

            // MID-STEP IS NOT A FAILURE. SpaceAt returns null while the camera
            // is in flight, on purpose — a sample away from the resting height
            // is the walk still happening. Wait it out.
            if (space == null || heading == null)
            {
                if (++_walkWaits > WALK_WAIT_BUDGET)
                {
                    FailWalk("the position never settled");
                    return;
                }
                _walkNextCheckAt = UnityEngine.Time.unscaledTime + WALK_POLL_SECONDS;
                return;
            }
            _walkWaits = 0;

            // ARRIVED. Turn to face the table before sitting.
            //
            // The two presses of LookUp that have ever worked were both made
            // facing north, immediately after standing up. Whether facing
            // actually matters is not something the source can answer, and a
            // quarter turn is free and certain — so the unknown is removed
            // rather than gambled on. If a later log shows LookUp taken at
            // space 1 on any heading, this turn can come out.
            if (space.Id == CabinMap.TABLE_SPACE_ID)
            {
                if (!FacingMatches(heading.Value, CabinMap.TABLE_BEARING))
                {
                    TurnTowards(heading.Value, CabinMap.TABLE_BEARING, space.Id);
                    return;
                }

                _walkingToTable = false;
                _log?.LogInfo(
                    $"IKMA CABIN: back at the table in {_walkPresses} press(es); sitting down.");
                ReturnToMap(announce: true);
                return;
            }

            if (_walkPresses >= WALK_PRESS_BUDGET)
            {
                FailWalk("the walk ran out of presses");
                return;
            }

            // DID THE LAST PRESS DO ANYTHING? A blocked step is completely
            // silent — his log has a "step right" into the east wall that left
            // the position byte-for-byte unchanged and said nothing — so
            // "nothing changed" is the only evidence a press was refused.
            bool moved = space.Id != _walkLastSpaceId
                      || !FacingMatches(heading.Value, _walkLastHeading);

            if (_walkLastButton != null && !moved)
            {
                // STILL WITHIN THE WINDOW: the tween is probably just running.
                // Look again rather than concluding anything. This is what makes
                // the fast poll safe — an early look that sees no change costs
                // 80ms, not a wrong retry.
                if (UnityEngine.Time.unscaledTime - _walkPressedAt < WALK_REFUSED_SECONDS)
                {
                    _walkNextCheckAt = UnityEngine.Time.unscaledTime + WALK_POLL_SECONDS;
                    return;
                }

                // ONE RETRY, THEN STOP. Never more. A press that lands late
                // would stack with a repeat and carry the player a space past
                // where the walk thinks they are — and this walker verifies
                // position precisely so it never has to guess about that. The
                // camera ladder can afford an open-ended retry because a rung
                // is reversible; a step across the room is not.
                if (_walkRetried)
                {
                    FailWalk($"{_walkLastButton} was not taken at space {space.Id}");
                    return;
                }
                _walkRetried = true;
                _log?.LogInfo(
                    $"IKMA CABIN: {_walkLastButton} did nothing at space {space.Id}; one retry.");
                PressWalk(_walkLastButton, space.Id, heading.Value);
                return;
            }
            if (_walkLastButton != null)
            {
                // HOW LONG THE LEG ACTUALLY TOOK. Logged so WALK_POLL_SECONDS and
                // WALK_REFUSED_SECONDS can be set from measurement rather than
                // left as the numbers that happened to work first.
                _log?.LogInfo(
                    $"IKMA CABIN: {_walkLastButton} landed in " +
                    $"{(UnityEngine.Time.unscaledTime - _walkPressedAt) * 1000f:F0}ms.");
            }
            _walkRetried = false;

            // WHERE NEXT — RECOMPUTED FROM WHERE THE PLAYER ACTUALLY IS, every
            // leg, rather than followed off a route stored at the start. A step
            // that landed somewhere unexpected corrects itself instead of
            // walking out the rest of a plan that is already wrong.
            var route = CabinMap.RouteBetween(space.Id, CabinMap.TABLE_SPACE_ID);
            if (route == null || route.Count < 2)
            {
                FailWalk($"no route from space {space.Id}");
                return;
            }

            float? bearing = CabinMap.BearingBetween(route[0], route[1]);
            if (bearing == null)
            {
                FailWalk($"space {route[0]} and space {route[1]} are not neighbours");
                return;
            }

            // HIS LINE, SPOKEN ONCE, HERE RATHER THAN AT THE KEYPRESS. By this
            // point the position has settled and the route is known, so a player
            // who pressed Backspace mid-step while already at the table has
            // already sat down above without ever hearing it.
            if (!_walkAnnounced)
            {
                _walkAnnounced = true;
                Speech.Browse(Vocabulary.Map.ReturningToTheTable);
            }

            if (!FacingMatches(heading.Value, bearing.Value))
            {
                TurnTowards(heading.Value, bearing.Value, space.Id);
                return;
            }

            // FORWARD. DirUp is the one movement button whose frame of
            // reference his log pins down at all four headings, which is why
            // this turns to face a direction rather than strafing into it.
            PressWalk("DirUp", space.Id, heading.Value);
        }

        private static void TurnTowards(float heading, float bearing, int spaceId)
        {
            float delta = ((bearing - heading) % 360f + 360f) % 360f;

            // Clockwise raises the heading and counterclockwise lowers it —
            // read off his log, where LookLeft took him from 90 to 0. A half
            // turn is two clockwise presses, one at a time, because every
            // press here is verified before the next one goes out.
            string button = delta > 180f ? "LookLeft" : "LookRight";
            PressWalk(button, spaceId, heading);
        }

        /// <summary>
        /// One press, then wait and look. Straight to InjectButton rather than
        /// through Step or Turn, because those speak and this walk is silent
        /// between its two lines — Zamar's choice, Session 17.
        /// </summary>
        private static void PressWalk(string button, int spaceId, float heading)
        {
            _walkPresses++;
            _walkLastButton  = button;
            _walkLastSpaceId = spaceId;
            _walkLastHeading = heading;
            _walkPressedAt   = UnityEngine.Time.unscaledTime;
            _walkNextCheckAt = UnityEngine.Time.unscaledTime + WALK_POLL_SECONDS;

            HotkeyManager.InjectButton(button);
            _log?.LogInfo(
                $"IKMA CABIN: walk press {_walkPresses}, {button} at space {spaceId} " +
                $"facing {Compass(heading)}.");
        }

        private static void FailWalk(string why)
        {
            _walkingToTable = false;
            _log?.LogWarning($"IKMA CABIN: walk back to the table abandoned — {why}.");

            // LAST RESORT: A ZOOM NOBODY KNEW WAS UP. (0.7.106.)
            //
            // A walk that never moves is the exact signature of the camera being
            // held by something — and the only thing that holds it here is a
            // zoom that ActiveZoom did not report. Rather than leave the player
            // stuck (his words for the picture frame: "it soft locks unless
            // mouse wheel down"), back out of whatever the game's static is
            // pointing at and say so.
            //
            // Safe to do blind: SetZoomed(false) on an object that is not zoomed
            // does nothing. This is the recovery that lets the guard above be
            // simple — the cheap fix is tried first, and this catches the case
            // where it was wrong.
            var stale = ZoomedInteractable();
            if (stale != null)
            {
                string nm = "?";
                try { nm = stale.gameObject.name; } catch { }
                _log?.LogWarning($"IKMA CABIN: the walk never moved — backing out of '{nm}' and stopping.");

                try { stale.SetZoomed(false); }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA CABIN: backing out threw {e.GetType().Name}.");
                }

                Speech.Browse(Vocabulary.Map.YouStepBackPress);
                return;
            }

            Speech.Browse(Vocabulary.CouldNotReachTable);
        }

        // ==================================================================
        // WALKING AROUND WHILE STANDING. (Session 16, Zamar's ask: "Since it's
        // first person, Up should be step forward, left should be turn
        // counterclockwise, right turn clockwise, and down step backwards.")
        //
        // Same method as the ladder and for the same reason: press the game's
        // own buttons. DirUp / DirDown / DirLeft / DirRight are in the Button
        // enum the mod already logs at startup, and first-person movement is
        // what they exist for.
        //
        // HELD, NOT TAPPED. A camera rung is one discrete press; walking and
        // turning are continuous, so these hold the button for a short burst.
        // The burst length is the one number here that is a guess rather than
        // an observation, and it is the safest kind: too short is a small step,
        // too long is a big one, and neither breaks anything. Tune it against
        // what he reports.
        //
        // NOTHING IS SPOKEN. IKMA cannot see the cabin, so it has nothing true
        // to say about where a step landed. Announcing "you stepped forward"
        // would be reporting an intention, and the room is not readable yet.
        // The presses are logged so a run says whether the game took them.
        // ==================================================================
        // MOVING and TURNING are different actions on different buttons, and
        // the 0.7.59 playtest is what separated them. Zamar: "Left arrow moved
        // the player left, and right moved them right" — so the Dir buttons
        // STRAFE. He kept that and asked for turning on its own keys.
        //
        // Turning goes on LookLeft / LookRight, the same pair that switches to
        // the side views out on the map. One button meaning different things in
        // different control modes is exactly why this mod presses buttons
        // instead of reproducing effects: the game decides what a press means
        // where the player is standing, and it is never wrong about that.
        // ONE STEP, NOT A GLIDE. Zamar, 0.7.60: "moving in a direction moves me
        // multiple spaces in that direction until I hit a wall, like a pokemon
        // ice puzzle."
        //
        // The button was being renewed every frame the arrow was held, so an
        // ordinary keypress was a third of a second of continuous movement. A
        // fixed short burst per press is a step.
        //
        // NO FRAME BURST. 0.7.61 held the button for four frames and Zamar slid
        // to the far wall; continuous movement moves for as long as the button
        // is down, so four frames is four frames of walking. One frame is one
        // keystroke's worth of input and there is nothing left to tune — how far
        // the game carries you for one is the game's business, not IKMA's.
        public static void StepForward()  => Step("DirUp",    "step forward");
        public static void StepBackward() => Step("DirDown",  "step back");
        public static void StrafeLeft()   => Step("DirLeft",  "step left");
        public static void StrafeRight()  => Step("DirRight", "step right");

        private static void Step(string button, string what)
        {
            if (!Standing) return;
            HotkeyManager.InjectButton(button);
            _log?.LogInfo($"IKMA MOVE: {what} ({button}).");
            LogCabinPosition("after " + what);
        }

        /// <summary>
        /// A quarter turn. Zamar: "It's always 90 degrees though" — his
        /// knowledge of the game, and the reason this may state the angle.
        /// A single press, not a hold: a held turn would spin.
        /// </summary>
        public static void TurnCounterclockwise() => Turn("LookLeft",  Vocabulary.Map.Counterclockwise);
        public static void TurnClockwise()        => Turn("LookRight", Vocabulary.Map.Clockwise);

        private static void Turn(string button, string direction)
        {
            if (!Standing) return;

            HotkeyManager.InjectButton(button);
            _log?.LogInfo($"IKMA MOVE: turn {direction} ({button}).");
            LogCabinPosition("after turning " + direction);

            // DIRECTION BEFORE THE ANGLE. Zamar: "change the 90 degrees to be
            // after the direction for snappier screen reader info." The angle
            // never varies, so it is the half he can stop listening to — the
            // same principle that put the puzzle code ahead of the page number.
            Speech.Browse(Vocabulary.Map.TurnedDegrees(direction));
        }

        // ==================================================================
        // WHAT IS IN FRONT OF YOU. (Session 16.)
        //
        // Zamar wants to author descriptions himself, per standing position and
        // facing: "while standing in the space next to the Clock, if I'm both in
        // that space and facing the clock, I want Space to read my custom
        // description."
        //
        // NOTHING IS AUTHORED YET AND NOTHING IS INVENTED. This build reads the
        // camera's world position and heading, logs them, and speaks them, so
        // one walk around the cabin produces the actual coordinate space to
        // author against. His call: "Log it AND speak raw coordinates."
        //
        // ViewManager.CameraParent is PUBLIC and is the supported route — no
        // scene search. Whether the cabin moves on a grid or freely is exactly
        // what the log will say; assuming either would be the guess this project
        // keeps paying for.
        //
        // The spoken fallback is his line, verbatim.
        // ==================================================================
        private static UnityEngine.Transform CameraTransform()
        {
            var vm = Views();
            if (vm == null) return null;
            try { return vm.CameraParent; }
            catch { return null; }
        }

        /// <summary>Heading in degrees, 0 to 359, or null if it cannot be read.</summary>
        private static float? Heading()
        {
            var t = CameraTransform();
            if (t == null) return null;
            try
            {
                float y = t.eulerAngles.y % 360f;
                return y < 0f ? y + 360f : y;
            }
            catch { return null; }
        }

        /// <summary>
        /// Write one survey line. Called after every step and every turn, not
        /// only when the player asks.
        ///
        /// 0.7.64 logged this from the Space key alone, and Zamar did exactly
        /// what anyone would: he stood in every space and spun in each one, and
        /// the log came back with nothing in it. A survey that depends on the
        /// surveyor pressing an extra key each time is not a survey.
        ///
        /// SAMPLED ON A DELAY, because a step is a tween like everything else
        /// here and the position on the frame the key is pressed is where the
        /// player is LEAVING.
        /// </summary>
        private static string _pendingSurveyLabel;
        private static float  _pendingSurveyAt;

        private const float SURVEY_SETTLE_SECONDS = 0.45f;

        private static void LogCabinPosition(string label)
        {
            _pendingSurveyLabel = label;
            _pendingSurveyAt    = UnityEngine.Time.unscaledTime + SURVEY_SETTLE_SECONDS;
        }

        private static void TickSurvey()
        {
            if (_pendingSurveyLabel == null) return;
            if (UnityEngine.Time.unscaledTime < _pendingSurveyAt) return;

            string label = _pendingSurveyLabel;
            _pendingSurveyLabel = null;

            var t = CameraTransform();
            float? heading = Heading();
            if (t == null || heading == null) return;

            try
            {
                var p = t.position;
                _log?.LogInfo(
                    $"IKMA CABIN [{label}]: x={p.x:F2} y={p.y:F2} z={p.z:F2} " +
                    $"heading={heading.Value:F1} ({Compass(heading.Value)}).");
            }
            catch { }
        }

        /// <summary>
        /// The slot the player is standing in, as "space|facing", or null while
        /// the camera is between spaces or between facings.
        /// </summary>
        // ASK THE GAME WHERE THE PLAYER IS. (Session 17.)
        //
        // FirstPersonController is a Singleton and both of these are PUBLIC:
        //     CurrentZone   -> NavigationZone3D, the space being stood in
        //     LookDirection -> North / East / South / West
        //
        // This replaced a lookup that measured the camera's world position and
        // heading, and the reason it had to is in his 0.7.80 log: zooming into
        // the safe moved the camera to y=8.76, so the coordinate lookup
        // returned null, every number key reported slot 'unknown', and
        // Backspace could not start a walk at all. The game knew the whole time.
        //
        // The measured grid still owns routing and the description keys; it is
        // no longer the answer to "where is the player".
        private static FirstPersonController Fpc()
        {
            try { return Singleton<FirstPersonController>.Instance; }
            catch { return null; }
        }

        private static string CurrentSlotKey()
        {
            var fpc = Fpc();
            if (fpc == null) return null;

            NavigationZone3D zone;
            string facing;
            try
            {
                zone   = fpc.CurrentZone;
                facing = fpc.LookDirection.ToString();
            }
            catch { return null; }

            if (zone == null) return null;

            string zoneName = zone.ToString();
            int spaceId = CabinMap.SpaceIdForZone(zoneName);
            if (spaceId == 0)
            {
                _log?.LogWarning(
                    $"IKMA CABIN: unrecognised zone '{zoneName}' — no slot key. " +
                    "Add it to CabinMap._zoneToSpace.");
                return null;
            }

            return CabinMap.SlotKey(spaceId, facing);
        }

        /// <summary>
        /// The object the player is currently zoomed into, or null.
        /// ZoomInteractable.currentZoomInteractable is a NONPUBLIC STATIC and is
        /// the only global handle to it — read once by cached reflection.
        /// </summary>
        private static System.Reflection.FieldInfo _currentZoomField;
        private static bool _zoomFieldResolved;

        internal static ZoomInteractable ZoomedInteractable()
        {
            if (!_zoomFieldResolved)
            {
                _zoomFieldResolved = true;
                _currentZoomField = typeof(ZoomInteractable).GetField(
                    "currentZoomInteractable",
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                if (_currentZoomField == null)
                    _log?.LogWarning("IKMA CABIN: ZoomInteractable.currentZoomInteractable not found.");
            }

            if (_currentZoomField == null) return null;
            try { return _currentZoomField.GetValue(null) as ZoomInteractable; }
            catch { return null; }
        }

        /// <summary>
        /// The zoom the player is ACTUALLY in, or null. (0.7.92.)
        ///
        /// THE 0.7.91 REGRESSION, and it broke the most important key in the
        /// mod. `ZoomInteractable.currentZoomInteractable` is a STATIC and it
        /// keeps pointing at the last object zoomed, long after the player has
        /// left it. His log shows Backspace reporting "backing out of the zoom
        /// on 'LargePictureFrame'" twice while he was standing across the room —
        /// so instead of walking him to the table it called SetZoomed on a
        /// picture frame, moved the camera, and the walk then failed with "the
        /// position never settled".
        ///
        /// Two gates, and the second is the strong one. `Zooming` is no longer
        /// accepted on its own — a finished transition leaves Zoomed false while
        /// Zooming can lag. And if the game says the player is standing in a
        /// known space facing a known direction, then a zoom is NOT up, whatever
        /// a leftover static claims. Those two states cannot both be true.
        ///
        /// One helper for all three callers, because three copies of a guard is
        /// how one of them ends up not having it.
        /// </summary>
        /// <summary>
        /// Is the CAMERA parked at a standing space? False while a zoom is up,
        /// because a zoom moves the camera off the grid — his 0.7.80 log caught
        /// it at y=8.76 against a standing height of 9.50.
        /// </summary>
        // ==================================================================
        // THE CUCKOO CLOCK'S HANDS. (0.7.106.)
        //
        // Zamar: "There is no focusing in on the cucoo clock, it should just say
        // which hand you adjusted with 1/2/3. Have it read briefly. '[Hand] to
        // [x o'clock]'."
        //
        // READ, NOT COUNTED. CuckooClock.handPositions is the array the game
        // turns the hands from, and the knob's index in clockHandKnobs says
        // which entry is which — so the number spoken is the one the hand is
        // actually pointing at, even if the player moved it with the mouse.
        // Same rule as the safe's dials.
        //
        // *** solutionPositionsLarge AND solutionPositionsSmall LIVE ON THIS
        // *** SAME OBJECT AND ARE NEVER READ. They are the answer to the
        // *** puzzle. A sighted player sees where the hands ARE, not where they
        // *** SHOULD BE, and reading the solution here would be a single
        // *** field name away at all times.
        //
        // Position 0 is twelve o'clock, which is how a clock face is read and
        // not how an array is indexed.
        // ==================================================================
        private static System.Reflection.FieldInfo _handPositionsField;
        private static System.Reflection.FieldInfo _handKnobsField;
        private static bool _clockFieldsResolved;

        private static void ResolveClockFields()
        {
            if (_clockFieldsResolved) return;
            _clockFieldsResolved = true;

            const System.Reflection.BindingFlags any =
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;

            _handPositionsField = typeof(CuckooClock).GetField("handPositions", any);
            _handKnobsField     = typeof(CuckooClock).GetField("clockHandKnobs", any);

            if (_handPositionsField == null)
                _log?.LogWarning("IKMA CABIN: CuckooClock.handPositions not found.");
            if (_handKnobsField == null)
                _log?.LogWarning("IKMA CABIN: CuckooClock.clockHandKnobs not found.");
        }

        // ==================================================================
        // THE WOODCARVING'S THREE PIECES. (0.7.112.)
        //
        // Zamar: "Pressing Space when focused on the woodcarving should describe
        // which 3 pieces are currently selected."
        //
        // WHAT THE GAME OWNS, AND WHERE. CustomizableFigurine holds the three
        // controls he presses (headInteractable / armsInteractable /
        // bodyInteractable) and a CompositeFigurine, which stores the live
        // selection as definedHead / definedArms / definedBody. Each is a
        // FigurineType — a nested enum, so the value carries the game's own name
        // for the piece and IKMA never has to infer one from a mesh.
        //
        // READ, NEVER COUNTED AND NEVER REMEMBERED. This asks the composite what
        // it currently is, every time Space is pressed. A tally of how many times
        // each control was pressed would drift the moment he used the mouse, and
        // this project already has the stranded-bool softlock to show for that
        // kind of bookkeeping.
        //
        // THERE IS NO SOLUTION TO AVOID HERE, AND THAT WAS CHECKED. The safe and
        // the cuckoo clock both keep their answers on the same object as their
        // state, which is why those two are read so carefully. The figurine is
        // the player's Kaycee's Mod avatar, not a puzzle: nothing on
        // CompositeFigurine or CustomizableFigurine is a target or a solution.
        // ==================================================================
        // NO REFLECTION IS NEEDED HERE ANY MORE. 0.7.113 cached FieldInfos for
        // CompositeFigurine.definedHead / definedArms / definedBody; all three
        // turned out to be prefab defaults rather than the player's choice, and
        // the real source — AscensionSaveData — is public all the way down.
        // Deleted rather than left resolved-but-unused: a cached handle to the
        // wrong field is an invitation to read it again.

        /// <summary>
        /// Is the open zoom the woodcarving?
        ///
        /// ASKED BY SHAPE, NOT BY NAME. The zoom's GameObject is called
        /// PastFigurines, but this cabin has ten objects sharing one name
        /// already (see CabinMap._zoomNamesByDigit), so "does a
        /// CustomizableFigurine hang under this zoom" is the sounder question
        /// and it is the same one that finds the data a moment later.
        /// </summary>
        public static bool WoodcarvingIsOpen()
        {
            return ActiveFigurine() != null;
        }

        private static CustomizableFigurine ActiveFigurine()
        {
            var z = ActiveZoom();
            if (z == null) return null;

            CustomizableFigurine fig = null;
            try
            {
                fig = z.GetComponentInChildren<CustomizableFigurine>(true);
                if (fig == null) fig = z.GetComponentInParent<CustomizableFigurine>();
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CABIN: figurine lookup threw {e.GetType().Name}.");
            }
            return fig;
        }

        /// <summary>
        /// Space while focused on the woodcarving. His format, Session 18:
        /// "[type] [bodypart] for quick identification" — "Chief head."
        /// </summary>
        public static void SpeakFigurine()
        {
            var fig = ActiveFigurine();
            if (fig == null) return;

            // ==============================================================
            // THE SAVE FILE IS THE SELECTION. CompositeFigurine IS NOT.
            // (0.7.114 — 0.7.113 read the wrong object and said so confidently.)
            //
            // 0.7.113 read CompositeFigurine.definedHead / definedArms /
            // definedBody. His 0.7.113 log: "woodcarving — Settler Man head.
            // Settler Man arms. Settler Man body." every single time, whatever
            // he had actually chosen. SettlerMan is FigurineType 0 — those three
            // fields are the prefab's SERIALIZED DEFAULTS, not live state, and
            // reading a default is how you get an answer that never changes and
            // never looks broken.
            //
            // THE TELL WAS IN THE DUMP AND WAS READ PAST. CompositeFigurine also
            // declares `generateAsPlayer`, which only makes sense if the player's
            // own choice lives somewhere else. It does:
            //
            //   AscensionSaveData.Data                 PUBLIC STATIC
            //   AscensionSaveData.playerAvatarHead     PUBLIC, FigurineType
            //   AscensionSaveData.playerAvatarArms     PUBLIC
            //   AscensionSaveData.playerAvatarBody     PUBLIC
            //
            // All four public, no reflection needed. This is the game's own
            // record of what he picked, and it is what the controls write to.
            //
            // THE GENERAL LESSON, and it is worth more than this feature:
            // A FIELD THAT HOLDS THE RIGHT TYPE IS NOT THE SAME AS THE FIELD
            // THAT HOLDS THE ANSWER. definedHead is a FigurineType on the object
            // that renders the figurine, which is as plausible as a wrong field
            // ever gets. What separates them is not the name or the type, it is
            // which one the game WRITES when the player acts.
            //
            // AND THIS IS WHY A CONSTANT ANSWER IS A RED FLAG. Three identical
            // pieces on a customisable figurine is a possible state and an
            // improbable one. Reading the same value every time is the shape of
            // a default being read as live — the same class as the line that
            // said "You are standing at the table." from the far corner.
            // ==============================================================
            AscensionSaveData save = null;
            try { save = AscensionSaveData.Data; }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CABIN: AscensionSaveData.Data threw {e.GetType().Name}.");
            }

            if (save == null)
            {
                _log?.LogWarning("IKMA CABIN: no AscensionSaveData to read the woodcarving from.");
                Speech.Browse(Vocabulary.WoodcarvingCouldNotBeRead);
                return;
            }

            // HEAD, BODY, ARMS — the same order as the 1/2/3 keys. (0.7.126.)
            //
            // Zamar: "Space should list them in the 1/2/3 order. Always head,
            // body, arms."
            //
            // The read and the keys have to agree or the player builds a mental
            // index off one and presses the other. This is the same fix as the
            // digits themselves a build ago, applied to the sentence rather than
            // the controls — the game's arms/body/head order does not appear in
            // either place now.
            string head = NameFigurinePiece(save.playerAvatarHead, "head");
            string body = NameFigurinePiece(save.playerAvatarBody, "body");
            string arms = NameFigurinePiece(save.playerAvatarArms, "arms");

            var parts = new System.Collections.Generic.List<string>();
            if (head != null) parts.Add(head);
            if (body != null) parts.Add(body);
            if (arms != null) parts.Add(arms);

            if (parts.Count == 0)
            {
                // NEVER GUESS AT A PIECE. An unrecognised enum member — a modded
                // figurine, or NUM_FIGURINES leaking through — is dropped and
                // logged, the same way an unconfirmed rulebook token is. The log
                // is how the name table grows.
                Speech.Browse(Vocabulary.WoodcarvingCouldNotBeRead);
                return;
            }

            string line = string.Join(" ", parts.ToArray());
            _log?.LogInfo($"IKMA CABIN: woodcarving — {line}");
            CombatAnnouncer.DropCommentary("woodcarving read");
            Speech.Browse(line);
        }

        private static string NameFigurinePiece(
            CompositeFigurine.FigurineType type, string partKey)
        {
            string enumName = type.ToString();
            string spoken   = CabinMap.FigurinePiece(enumName, partKey);

            // NEVER GUESS AT A PIECE. NUM_FIGURINES is a count sentinel and any
            // other unknown member would be a modded figurine; either way the
            // piece is dropped and logged rather than announced by its id. The
            // log is how the name table grows — the same rule the rulebook's
            // unknown bracket tokens follow.
            if (spoken == null)
                _log?.LogWarning(
                    $"IKMA CABIN: no approved name for figurine {partKey} '{enumName}' — dropped.");

            return spoken;
        }

        private static void SpeakClockHand(UnityEngine.Component knob, string objName)
        {
            string hand = CabinMap.ClockHandName(objName);
            if (hand == null) return;

            CuckooClock clock = null;
            try { clock = knob.GetComponentInParent<CuckooClock>(); } catch { }
            if (clock == null)
            {
                _log?.LogWarning($"IKMA CABIN: '{objName}' has no CuckooClock above it.");
                Speech.Browse(Vocabulary.Map.ClockHandTurned(hand));
                return;
            }

            ResolveClockFields();

            // The knob's place in the clock's own list is the index into
            // handPositions. Asked rather than assumed from the hand's name,
            // because nothing guarantees the two lists are written in the same
            // order as the three GameObjects happen to be named.
            int index = -1;
            try
            {
                var knobs = _handKnobsField?.GetValue(clock) as System.Collections.IEnumerable;
                if (knobs != null)
                {
                    int i = 0;
                    foreach (var k in knobs)
                    {
                        if (ReferenceEquals(k, knob)) { index = i; break; }
                        i++;
                    }
                }
            }
            catch { }

            int? position = null;
            try
            {
                var arr = _handPositionsField?.GetValue(clock) as int[];
                if (arr != null && index >= 0 && index < arr.Length) position = arr[index];
            }
            catch { }

            if (!position.HasValue)
            {
                _log?.LogWarning($"IKMA CABIN: could not read the position of '{objName}' (index {index}).");
                Speech.Browse(Vocabulary.Map.ClockHandTurned(hand));
                return;
            }

            int face = position.Value % 12;
            if (face == 0) face = 12;

            _log?.LogInfo($"IKMA CABIN: {hand} at position {position.Value} — {face} o'clock.");
            CombatAnnouncer.DropCommentary("clock hand turned");
            Speech.Browse(Vocabulary.Map.ToOclock(hand, face));
        }

        // ==================================================================
        // THE SAFE OPENING. (0.7.107.)
        //
        // Zamar wrote the line himself: "The mechanism cracks and the safe door
        // swings open. Inside is a large mass of bloodied mangled flesh." A
        // sighted player watches the door swing; this is the same moment, said.
        //
        // WATCHED, NOT PATCHED. SafeInteractable.OpenDoor is NONPUBLIC and would
        // need a TryPatch; `locked` is a plain bool on the same object that goes
        // true -> false exactly once when the safe gives way. Reading a state
        // the game already keeps is cheaper and cannot break the mod if the
        // method signature moves.
        //
        // *** correctLockPositions AND ascensionSecretLockPositions SIT ON THIS
        // *** SAME OBJECT AND ARE NEVER READ. They are the combination. This
        // *** code holds a SafeInteractable and asks it one question: are you
        // *** still locked. That is what a sighted player can see.
        //
        // Once open, 1/2/3 stop working there — his call. The dials are done and
        // pressing them would be turning knobs on a solved lock.
        // ==================================================================
        private static System.Reflection.FieldInfo _safeLockedField;
        private static bool _safeFieldResolved;
        private static SafeInteractable _watchedSafe;
        private static bool _watchedSafeWasLocked;

        private static bool? SafeLocked(SafeInteractable safe)
        {
            if (safe == null) return null;

            if (!_safeFieldResolved)
            {
                _safeFieldResolved = true;
                _safeLockedField = typeof(SafeInteractable).GetField("locked",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);

                if (_safeLockedField == null)
                    _log?.LogWarning("IKMA CABIN: SafeInteractable.locked not found.");
            }

            if (_safeLockedField == null) return null;
            try { return (bool)_safeLockedField.GetValue(safe); }
            catch { return null; }
        }

        /// <summary>True once the safe the player is looking at has given way.</summary>
        public static bool SafeIsOpen()
        {
            var safe = ActiveZoom() as SafeInteractable;
            if (safe == null) return false;

            bool? locked = SafeLocked(safe);
            return locked.HasValue && !locked.Value;
        }

        /// <summary>Driven every frame from HotkeyManager. Speaks once, on the change.</summary>
        public static void TickSafe()
        {
            var safe = ActiveZoom() as SafeInteractable;

            if (safe == null)
            {
                // Not looking at a safe — forget what the last one was doing so
                // walking away and back cannot re-fire the line.
                _watchedSafe = null;
                return;
            }

            bool? locked = SafeLocked(safe);
            if (!locked.HasValue) return;

            if (!ReferenceEquals(safe, _watchedSafe))
            {
                _watchedSafe = safe;
                _watchedSafeWasLocked = locked.Value;
                return;
            }

            if (!_watchedSafeWasLocked || locked.Value) { _watchedSafeWasLocked = locked.Value; return; }

            _watchedSafeWasLocked = false;
            _log?.LogInfo("IKMA CABIN: the safe opened.");

            CombatAnnouncer.DropCommentary("the safe opened");
            Speech.Browse(
                Vocabulary.Map.MechanismCracksAndThe);
        }

        private static bool CameraAtStandingSpace()
        {
            var t = CameraTransform();
            if (t == null) return false;
            try { return CabinMap.SpaceAt(t.position) != null; }
            catch { return false; }
        }

        private static ZoomInteractable ActiveZoom()
        {
            var z = ZoomedInteractable();
            if (z == null) return null;

            bool zoomed = false;
            try { zoomed = z.Zoomed; } catch { }
            if (!zoomed) return null;

            // THE CAMERA GUARD IS GONE. (0.7.106.)
            //
            // It was belt and braces on top of `Zoomed`, and it turned out to
            // have a false negative that cost a softlock. Zamar: "Pressing
            // backspace after focusing on the picture frame says returning to
            // the table, but it does not, it soft locks unless mouse wheel
            // down." The picture frame's zoom leaves the camera close enough to
            // a standing space that CabinMap.SpaceAt still resolves — so the
            // guard declared no zoom was up, Backspace walked instead of backing
            // out, and the walk pressed into a camera that could not move. The
            // safe's zoom moves further and never hit it, which is why this
            // survived three builds.
            //
            // TWO GUARDS THAT CAN DISAGREE IS ONE GUARD TOO MANY. `Zoomed` is
            // the game's own answer to the only question being asked, and it was
            // already doing the work: the 0.7.91 regression this all came from
            // was accepting `Zooming`, which lags, and requiring `Zoomed` fixed
            // it. Guessing at the same fact a second way from camera position
            // only added a way for the two to disagree.
            //
            // The stale static is still handled — by FailWalk, which backs out
            // of any zoom before it gives up. That recovery works whatever the
            // camera is doing, which is more than this guard could say.
            return z;
        }

        /// <summary>
        /// THE SOFTLOCK, and it was a real one. (0.7.80 playtest.)
        ///
        /// Zamar left-clicked the safe, the camera zoomed in, and Backspace could
        /// not get him out: the standing layer still owned the keyboard and tried
        /// to walk him to the table from a camera position that is not a
        /// standing space at all. Two "Could not reach the table." and no way
        /// back without the mouse.
        ///
        /// Backing out uses the object's OWN SetZoomed, which is PUBLIC — the
        /// game's method for its own state, not a reproduction of it.
        /// </summary>
        public static bool ExitZoom()
        {
            var z = ActiveZoom();
            if (z == null) return false;

            string objName = "?";
            try { objName = z.gameObject.name; } catch { }

            _log?.LogInfo($"IKMA CABIN: backing out of the zoom on '{objName}'.");

            try { z.SetZoomed(false, false, 0.3f); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CABIN: SetZoomed failed ({e.GetType().Name}).");
                return false;
            }

            // HIS LINE: "You step back from the [game object]".
            //
            // THE NAME IS THE PROBLEM AND IT IS NOT SOLVED HERE. A zoom object
            // has no display name anywhere in the assembly — 'Safe' is its
            // GameObject name, and speaking GameObject names is the bug this
            // project fixed across the whole Kaycee's Mod front end. So only
            // names Zamar has actually written are used, and anything else logs
            // itself and gets the short form until he supplies a word for it.
            // THE WORD THE DIGIT USED ON THE WAY IN, IF IT IS STILL THE SAME
            // ZOOM. (0.7.111.) Ten cabin objects share the GameObject name
            // "ZoomInteractable", so objName alone cannot name the skull.
            //
            // The identity check is what makes remembering safe: if the player
            // opened something else with the mouse in between, the object in the
            // air is not the one the digit opened and the memory is dropped
            // rather than used. A remembered word that has gone stale would be a
            // confidently wrong announcement, which is worse than the short form.
            string spoken = null;
            if (_zoomOpenedName != null && ReferenceEquals(_zoomOpenedObj, z))
            {
                spoken = _zoomOpenedName;
            }
            else if (_zoomOpenedName != null)
            {
                _log?.LogInfo(
                    "IKMA CABIN: the open zoom is not the one a number key opened — " +
                    "using the name table instead.");
            }

            spoken = spoken ?? CabinMap.ZoomDisplayName(objName);

            _zoomOpenedName = null;
            _zoomOpenedObj  = null;
            _zoomOpenedKey  = null;

            if (spoken == null)
            {
                _log?.LogWarning(
                    $"IKMA CABIN: no approved name for zoom object '{objName}' — " +
                    "speaking the short form. Ask Zamar for a word.");
                Speech.Browse(Vocabulary.Map.YouStepBack);
            }
            else
            {
                Speech.Browse(Vocabulary.Map.YouStepBackFrom(spoken));
            }
            return true;
        }



        /// <summary>
        /// R while standing. (Session 17, Zamar's ask.)
        ///
        /// ONLY AT THE RULEBOOK SHELF, and that is the game's rule rather than
        /// a restriction IKMA invented: the book is a physical object on the
        /// shelf west of the starting spot — CabinRulebookInteractable, a
        /// CabinInteractable like the clock and the safe — so a sighted player
        /// has to be standing there and facing it too. His own description for
        /// that slot is what promises the key.
        ///
        /// Elsewhere it says so rather than going quiet. R is not advertised in
        /// the standing help (his call), so nobody is told about a key that
        /// does nothing — but a player who learned it at the shelf and tries it
        /// across the room gets an answer instead of silence, which is the
        /// difference between a refusal and a key that failed to register.
        /// </summary>
        private const string RULEBOOK_SLOT = "1|west";

        /// <summary>
        /// 1, 2 and 3 while standing — a closer look at part of what is in front
        /// of you. (Session 17.)
        ///
        /// ZAMAR'S FRAMING, and it decides why this reads text rather than only
        /// pressing things: "I know it's more of scenery than full puzzles in
        /// Kaycees mod... The standing up and walking around and interacting
        /// with the cabin stuff is a teaser for how the main game will work...
        /// Getting them used to these controls now is my goal."
        ///
        /// So the key has to DO something wherever his descriptions offer it —
        /// that is the never-offer-a-dead-key rule — and a closer look at the
        /// object is real whether or not Kaycee's Mod has anything to click.
        /// The same key drives a real puzzle in the main game later.
        ///
        /// THE DRIVE HALF IS LOGGED, NOT WIRED, IN THIS BUILD. Nothing owns the
        /// cabin's objects, so IKMA builds its own registry from the objects the
        /// game hands it — and that registry is knowingly incomplete: the clock,
        /// the safe, the door and the gramophone never appeared at all in the
        /// 0.7.77 survey. Mapping digit 2 to "the globe" before a log says which
        /// objects actually register beside which zone would be guessing, and
        /// this project logs the half it cannot prove.
        /// </summary>
        /// <summary>
        /// A number key while ZOOMED IN — operate that object's own controls.
        /// (Session 17, his spec.)
        ///
        /// The zoom is a layer above standing, so the digits change meaning
        /// inside it: standing, they choose WHICH object to focus; focused, they
        /// work the thing in front of you. His words: "when you press 1 to focus
        /// on the safe, 1/2/3 should then interact with the safe's knobs. 2
        /// currently changes focus to the woodcarving which is incorrect."
        ///
        /// NOTHING IS SPOKEN ON A SUCCESSFUL PRESS. A dial turning makes its own
        /// sound, and no word for what it did has been agreed — inventing one is
        /// the wording rule's whole subject. The press is logged; ask him what
        /// he wants to hear once he has heard the game's own answer.
        /// </summary>
        private static bool ExamineZoomed(int digit)
        {
            var z = ActiveZoom();
            if (z == null) return false;

            string objName = "?";
            try { objName = z.gameObject.name; } catch { }

            // A SOLVED LOCK HAS NO DIALS LEFT TO TURN. Zamar: "after the safe is
            // open the 1/2/3 keys should be disabled." The knobs still respond
            // to the game, so without this the keys keep working on a puzzle
            // that is over — a key that does something meaningless is the defect
            // this project keeps closing.
            if (SafeIsOpen())
            {
                _log?.LogInfo($"IKMA CABIN: key {digit} on '{objName}' — the safe is already open.");
                Speech.Browse(Vocabulary.NothingToExamine);
                return true;
            }

            var controls = CabinProbe.SubControls(z);
            _log?.LogInfo(
                $"IKMA CABIN: key {digit} while zoomed on '{objName}' — " +
                $"{controls.Count} control(s).");

            for (int i = 0; i < controls.Count; i++)
            {
                string nm = "?";
                try { nm = controls[i].gameObject.name; } catch { }
                _log?.LogInfo($"IKMA CABIN:   control {i + 1}: {controls[i].GetType().Name} obj='{nm}'");
            }

            if (digit < 1 || digit > controls.Count)
            {
                Speech.Browse(Vocabulary.NothingToExamine);
                return true;
            }

            var control = controls[digit - 1];

            // THE WOODCARVING'S DIGITS ARE HIS, NOT THE LIST'S. (0.7.124.)
            //
            // Zamar: "change 1 to change the head, 2 to change the body, and 3
            // to change the arms."
            //
            // The control list comes back in the game's order — arms, body, head
            // — so taking the Nth gave him arms on 1 and head on 3. That order is
            // the scene's, and it is exactly the kind of thing that is not a
            // decision anyone made; his is head-down, which is how a person
            // describes a figure.
            //
            // Matched by the game's own GameObject names rather than by
            // reordering the list, so this survives the controls arriving in a
            // different order on some other screen or after a game update.
            var byName = FigurineControlFor(controls, digit);
            if (byName != null) control = byName;

            // WHAT THE DIAL WAS BEFORE, so the change can be reported rather
            // than the mere fact that a key was pressed.
            int? before = LockPosition(control);

            // THE FIGURINE PART, SAMPLED BEFORE THE PRESS. It has to be read on
            // this side of PressSub or "before" and "after" are the same value
            // and the change can never be detected.
            string figPart   = FigurinePartOf(control);
            string figBefore = figPart == null ? null : ReadFigurineType(figPart);

            CabinProbe.PressSub(control);

            // READ THE NEW VALUE. Zamar: "for the safe, pressing 1 and changing
            // the value should just read the new number value that knob is now."
            //
            // SpinningLockInteractable.Position is PUBLIC, so this is the dial's
            // own answer and not a count IKMA keeps. Nothing else is spoken —
            // just the number, which is what he asked for and what a sighted
            // player reads off the dial face.
            //
            // DEFERRED, because the dial TWEENS. Position updates when the spin
            // starts, but reading it on the same frame is the read-state-back-
            // after-an-async-call mistake this project already has a rule
            // about; a short settle costs nothing and nothing here is
            // reaction-timed.
            // SPOKEN ON THE PRESS, NOT AFTER A SETTLE. Zamar: "the knobs are not
            // interrupting their own call out as it should, to make the reading
            // snappier."
            //
            // The interrupt flag was already right; the LATENCY was the settle
            // coroutine in front of it, which waited for three stable samples
            // before saying anything. That wait was wrong here for a reason
            // worth keeping: SpinToNextPosition sets Position to the new index
            // IMMEDIATELY and the tween is only the dial's visual catch-up. The
            // number is true the instant the key is pressed, so there is nothing
            // to wait for.
            //
            // This is the settle rule read correctly rather than applied by
            // reflex: wait when the value can still change, and do not when it
            // cannot. The camera walk waits because a position genuinely is not
            // final mid-step. A dial index is.
            if (before != null) SpeakLockValue(control);

            // THE WOODCARVING'S THREE CONTROLS. (0.7.116, his ask.)
            //
            // Zamar: "When focused on the woodcarving figure, pressing 1/2/3
            // changes a part of it. It needs to then read what changed... just
            // 'Robot head.' or 'Chief head.'"
            //
            // Same sentence Space already speaks for the whole figure, one part
            // of it — so the format needs no new decision and cannot drift from
            // the full read. CabinMap.FigurinePiece composes both.
            SpeakFigurinePieceChange(figPart, figBefore);
            return true;
        }

        /// <summary>
        /// After a woodcarving control is pressed, say what that part is now.
        ///
        /// WHICH PART, ASKED OF THE OBJECT. The three controls are named
        /// HeadInteractable, ArmsInteractable and BodyInteractable by the game —
        /// CustomizableFigurine's own fields are headInteractable /
        /// armsInteractable / bodyInteractable, so the mapping is the game's and
        /// not a guess about press order. Their order in the control list is
        /// NOT the mapping: his log shows arms, body, head in that order.
        /// </summary>
        /// <summary>
        /// The woodcarving control a digit should press, or null when this is
        /// not the woodcarving.
        ///
        /// HIS ORDER: 1 head, 2 body, 3 arms. The game hands the controls over
        /// as arms, body, head, and the Nth-in-list rule was picking them up in
        /// that order.
        /// </summary>
        private static MainInputInteractable FigurineControlFor(
            List<MainInputInteractable> controls, int digit)
        {
            if (controls == null || !WoodcarvingIsOpen()) return null;

            string want;
            switch (digit)
            {
                case 1:  want = "head"; break;
                case 2:  want = "body"; break;
                case 3:  want = "arms"; break;
                default: return null;
            }

            foreach (var c in controls)
            {
                string nm = null;
                try { nm = c.gameObject.name; } catch { }
                if (string.IsNullOrEmpty(nm)) continue;

                if (nm.ToLowerInvariant().Contains(want)) return c;
            }

            _log?.LogWarning(
                $"IKMA CABIN: no '{want}' control found for digit {digit} on the woodcarving.");
            return null;
        }

        /// <summary>
        /// Which of the figurine's three parts a control drives, or null.
        ///
        /// ASKED OF THE OBJECT, NOT OF THE PRESS ORDER. The game names these
        /// HeadInteractable, ArmsInteractable and BodyInteractable, matching
        /// CustomizableFigurine's own headInteractable / armsInteractable /
        /// bodyInteractable fields. Their position in the control list is NOT
        /// the mapping — his log lists them arms, body, head.
        /// </summary>
        private static string FigurinePartOf(MainInputInteractable control)
        {
            if (control == null) return null;
            if (!WoodcarvingIsOpen()) return null;

            string nm = null;
            try { nm = control.gameObject.name; } catch { }
            if (string.IsNullOrEmpty(nm)) return null;

            string lower = nm.ToLowerInvariant();
            if (lower.Contains("head")) return "head";
            if (lower.Contains("arms")) return "arms";
            if (lower.Contains("body")) return "body";

            _log?.LogInfo($"IKMA CABIN: '{nm}' is not one of the figurine's three parts — nothing read.");
            return null;
        }

        private static void SpeakFigurinePieceChange(string partKey, string before)
        {
            if (partKey == null) return;

            // READ IT NOW, THE WAY THE SAFE'S DIALS DO. The game's own handler
            // writes the new piece on the press; SwitchPieceAnimation is the
            // figure's visual catch-up, not the state changing. Speaking on the
            // press is what makes it snappy — his note about the knobs, applied
            // to the control that behaves the same way.
            string after = ReadFigurineType(partKey);

            if (after != null && after != before)
            {
                SpeakFigurinePiece(after, partKey);
                return;
            }

            // NOT YET, SO DO NOT GUESS. If the value has not moved on this frame
            // the honest options are to say nothing or to look again — never to
            // speak `before`, which is the piece that is being replaced and
            // would be a confidently wrong announcement.
            //
            // One deferred look, composed at speak time. If it still has not
            // changed, the provider returns null and the entry is dropped
            // silently; the press made its own sound either way.
            string captured = before;
            Speech.Commentary(() =>
            {
                string later = ReadFigurineType(partKey);
                if (later == null || later == captured) 
                {
                    _log?.LogInfo(
                        $"IKMA CABIN: figurine {partKey} unchanged at speak time — nothing read.");
                    return null;
                }
                return CabinMap.FigurinePiece(later, partKey);
            });
        }

        /// <summary>The game's enum name for one part, or null.</summary>
        private static string ReadFigurineType(string partKey)
        {
            try
            {
                var save = AscensionSaveData.Data;
                if (save == null) return null;

                switch (partKey)
                {
                    case "head": return save.playerAvatarHead.ToString();
                    case "arms": return save.playerAvatarArms.ToString();
                    case "body": return save.playerAvatarBody.ToString();
                }
            }
            catch { }
            return null;
        }

        private static void SpeakFigurinePiece(string enumName, string partKey)
        {
            string line = CabinMap.FigurinePiece(enumName, partKey);
            if (line == null)
            {
                _log?.LogWarning(
                    $"IKMA CABIN: no approved name for figurine {partKey} '{enumName}' — dropped.");
                return;
            }

            _log?.LogInfo($"IKMA CABIN: figurine {partKey} is now {enumName}.");
            Speech.Browse(line);
        }

        /// <summary>
        /// A spinning lock's current position, or null if this control is not
        /// one. Position is PUBLIC on SpinningLockInteractable.
        /// </summary>
        private static UnityEngine.Component _lastSpokenControl;

        // numPositions is NONPUBLIC, so it is read once by cached reflection
        // rather than hardcoded as 10. A dial with a different number of stops
        // then still reads correctly, and a missing field says so instead of
        // silently producing wrong digits.
        private static System.Reflection.FieldInfo _numPositionsField;
        private static bool _numPositionsResolved;

        private static int? LockFace(UnityEngine.Component control)
        {
            int? pos = LockPosition(control);
            if (pos == null) return null;

            if (!_numPositionsResolved)
            {
                _numPositionsResolved = true;
                _numPositionsField = typeof(SpinningLockInteractable).GetField(
                    "numPositions",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                if (_numPositionsField == null)
                    _log?.LogWarning("IKMA DIAL: SpinningLockInteractable.numPositions not found.");
            }

            if (_numPositionsField == null) return null;

            int n;
            try { n = (int)_numPositionsField.GetValue(control); }
            catch { return null; }

            if (n <= 0) return null;
            return ((n - pos.Value) % n + n) % n;
        }

        private static int? LockPosition(UnityEngine.Component control)
        {
            var lockCtl = control as SpinningLockInteractable;
            if (lockCtl == null) return null;
            try { return lockCtl.Position; }
            catch { return null; }
        }

        private static void SpeakLockValue(UnityEngine.Component control)
        {
            int? pos  = LockPosition(control);
            int? face = LockFace(control);
            if (pos == null) return;

            string nm = "?";
            try { nm = control.gameObject.name; } catch { }

            string eulers = "?";
            try { eulers = control.transform.localEulerAngles.ToString("F1"); }
            catch { }

            _log?.LogInfo(
                $"IKMA DIAL: '{nm}' Position={pos.Value} face={(face?.ToString() ?? "?")} " +
                $"localEulers={eulers}");

            if (face == null) return;

            // PER-KNOB STOMP. Spinning the SAME knob again cuts its own previous
            // number, so a fast player hears only the latest; a different knob
            // waits its turn rather than talking over the one before it. The
            // latch is exact rather than timed — a speech call runs 12ms to
            // 765ms, so no duration guess is sound.
            // ALWAYS CUT. Zamar, 0.7.91: "when mashing the safe dials, i expect
            // the reading to keep getting cut off. Right now if it fires a line
            // it fully finishes it and that leads to audio confusion."
            //
            // This replaces the per-knob latch from 0.7.89 — his earlier ask,
            // and wrong in practice: mashing across three dials queued three
            // numbers, and by the time the second was spoken the dial had moved
            // again. Only the newest number is ever true.
            _lastSpokenControl = control;
            Speech.Browse(face.Value.ToString());
        }

        public static void ExamineAhead(int digit)
        {
            if (!Standing) return;

            // ZOOMED IN? THE DIGITS BELONG TO THAT OBJECT, NOT TO THE ROOM.
            if (ExamineZoomed(digit)) return;

            // Ask the cabin for its own contents the first time a number key is
            // used. Cheap, once per cabin, and it is what puts the door knob and
            // the clock's knobs in reach at all.
            CabinProbe.SweepCabin();

            string slotNow = CurrentSlotKey();
            if (CabinMap.NumberKeysDisabled(slotNow))
            {
                // Zamar ruled these out: the mahogany box is a puzzle Kaycee's
                // Mod does not include, and the drawers behind it DO respond —
                // so without this the key would open something meaningless.
                _log?.LogInfo($"IKMA CABIN: number keys are disabled at '{slotNow}'.");
                Speech.Browse(Vocabulary.NothingToExamine);
                return;
            }

            if (CabinMap.DigitDisabled(slotNow, digit))
            {
                _log?.LogInfo($"IKMA CABIN: key {digit} is disabled at '{slotNow}'.");
                Speech.Browse(Vocabulary.NothingToExamine);
                return;
            }

            string slot = CurrentSlotKey();

            // ONLY WHERE THE MAPPING IS STILL MISSING. (0.7.105.)
            //
            // This survey prints every interactable that matches the zone and
            // facing — up to fourteen lines — and it used to run on EVERY number
            // key. 592 lines of his 1,797-line log, all of it re-describing
            // slots that have already been mapped from an earlier one.
            //
            // Its job is to write the mapping, so it runs where the mapping does
            // not exist yet. Once a digit points at a named object the survey
            // has nothing left to tell us about it, and a log that repeats what
            // is already known is a log nobody can find anything in.
            if (CabinMap.ObjectForDigit(slot, digit) == null)
                CabinProbe.LogWhatIsHere(digit);

            // PRESS THE OBJECT FIRST, THEN SAY THE WORDS. Zamar, Session 17:
            // "There's no current way to examine the safe without left clicking.
            // Let's get 1 hooked up to do that examine."
            //
            // The press goes through the game's own primitive and the game
            // decides what a click means there — a zoom, a spin, a track change,
            // or nothing. IKMA never reproduces the effect.
            bool pressed = false;
            var matches = CabinProbe.MatchesHere();

            if (matches.Count > 0)
            {
                CabinInteractable target = null;
                string wanted = CabinMap.ObjectForDigit(slot, digit);

                if (wanted != null)
                {
                    foreach (var m in matches)
                    {
                        string nm = null;
                        try { nm = m.gameObject.name; } catch { }
                        if (string.Equals(nm, wanted, System.StringComparison.OrdinalIgnoreCase))
                        { target = m; break; }
                    }
                    if (target == null)
                    {
                        // NOT EVERY MAPPED OBJECT HAS ZONE DATA. (0.7.107.)
                        //
                        // MatchesHere only offers CabinInteractables that carry
                        // zones and facings. The caged wolf is a
                        // DiscoverableCardInteractable owned by a plain
                        // behaviour and has neither, so the name was mapped
                        // correctly and then never found — Zamar: "On the globe
                        // table, the 3 key still doesnt interact with the
                        // cage/wolf."
                        //
                        // The loose registry is exactly the list of interactables
                        // with no zone data, and it is already built from the
                        // cabin sweep. A mapped name is an authored decision, so
                        // it is worth looking in both places for it; only the
                        // UNMAPPED fallback has to stay inside the zone matches,
                        // where "the Nth thing in front of you" means something.
                        var loose = CabinProbe.Loose(wanted);
                        if (loose != null)
                        {
                            _log?.LogInfo(
                                $"IKMA CABIN: key {digit} at '{slot}' — '{wanted}' " +
                                "found in the loose registry.");

                            // TOUCH, NOT PRESS, where the object is something
                            // that can be COLLECTED. See CabinMap._touchOnly —
                            // pressing the caged wolf handed Zamar the card.
                            if (CabinMap.DigitIsTouchOnly(slot, digit))
                            {
                                _log?.LogInfo($"IKMA CABIN: touching '{wanted}' at '{slot}' (hover only).");
                                pressed = false;
                                try
                                {
                                    loose.CursorEnter();
                                    loose.CursorExit();
                                    pressed = true;
                                }
                                catch (System.Exception e)
                                {
                                    _log?.LogWarning($"IKMA CABIN: touching threw {e.GetType().Name}.");
                                }
                            }
                            else
                            {
                                pressed = CabinProbe.PressSub(loose);
                            }

                            if (pressed)
                            {
                                if (CabinMap.DigitIsSilent(slot, digit))
                                {
                                    _log?.LogInfo($"IKMA CABIN: '{wanted}' pressed silently at '{slot}'.");
                                    return;
                                }

                                string looseName = CabinMap.ZoomDisplayName(wanted);
                                CombatAnnouncer.DropCommentary($"stepped towards {wanted}");
                                Speech.Browse(
                                    Vocabulary.Map.YouFocusInOrYouFocusOn(looseName));
                                return;
                            }
                        }
                        else
                        {
                            _log?.LogWarning(
                                $"IKMA CABIN: key {digit} at '{slot}' wants '{wanted}', " +
                                "which is in neither the matches nor the loose registry.");
                        }
                    }
                }
                else if (digit >= 1 && digit <= matches.Count)
                {
                    // AN UNMAPPED DIGIT DOES NOTHING NOW. (0.7.219.)
                    //
                    // Zamar, standing at the game table: "I pressed 1 while
                    // standing infront of the game table and got a focus in
                    // call that didnt happen and shouldnt happen."
                    //
                    //   IKMA CABIN: key 1 at '1|north' has no mapping —
                    //               pressed match 1, 'Tutorial_CandleholderShelf'.
                    //   IKMA SPEAK: You focus in.
                    //
                    // Two defects in one path, and the second is the worse one.
                    //
                    // It ACTED ON A GUESS. With no mapping it took the Nth
                    // entry of an unordered match list and pressed it — at the
                    // game table that was a candleholder shelf, an object he
                    // was not looking at and did not ask for. A key that does
                    // something arbitrary is worse than a key that does
                    // nothing.
                    //
                    // And it ANNOUNCED A FOCUS THAT DID NOT HAPPEN. This file
                    // already states the rule twenty lines below — "Announcing
                    // a focus that did not happen is describing something the
                    // player cannot verify. His call." — and the guarded path
                    // honours it while this one walked straight past.
                    //
                    // The discovery workflow is unchanged: the warning still
                    // names the candidate so the pairing can be written into
                    // CabinMap._digitToObject and approved. It just no longer
                    // presses it to find out.
                    string nm = "?";
                    try { nm = matches[digit - 1].gameObject.name; } catch { }

                    _log?.LogWarning(
                        $"IKMA CABIN: key {digit} at '{slot}' has no mapping — NOT pressed. " +
                        $"The {digit}{(digit == 1 ? "st" : digit == 2 ? "nd" : digit == 3 ? "rd" : "th")} " +
                        $"match here is '{nm}'. If that is what {digit} should do, add " +
                        $"\"{slot}|{digit}\" -> \"{nm}\" to CabinMap._digitToObject.");

                    Speech.Browse(Vocabulary.NothingOnThatKey(digit));
                    return;
                }

                if (target != null)
                {
                    pressed = CabinProbe.Press(target);

                    // HIS LINE, Session 17: "When I press 1 to look at the safe
                    // it should read 'You step towards the [game object]' and
                    // stomp any previous low-prio information lines" — the slot
                    // description that named the key is exactly what is still
                    // draining when the key is pressed.
                    //
                    // Same treatment as arriving at the map: drop the commentary
                    // tier only, so nothing that reports a result is lost.
                    if (pressed)
                    {
                        string nm = "?";
                        try { nm = target.gameObject.name; } catch { }

                        // Some of these move the camera in on something and
                        // some just act — spinning a globe, trying a handle.
                        // Announcing a focus that did not happen is describing
                        // something the player cannot verify. His call.
                        if (CabinMap.DigitIsSilent(slot, digit))
                        {
                            _log?.LogInfo($"IKMA CABIN: '{nm}' pressed silently at '{slot}'.");

                            // Silent means no FOCUS line, not necessarily
                            // nothing. A clock knob turns a hand, and where that
                            // hand landed is the whole result of the press.
                            SpeakClockHand(target, nm);
                            return;
                        }

                        // THE AUTHORED KEY IS ASKED FIRST. (0.7.111.)
                        //
                        // Ten objects in this cabin are named "ZoomInteractable"
                        // and the skull is one of them, so the GameObject name
                        // cannot identify it. The slot and digit Zamar wrote can.
                        // See CabinMap._zoomNamesByDigit for the whole reason.
                        //
                        // The name table on the GameObject name stays as the
                        // fallback, for the objects whose names really are unique
                        // (Safe, PastFigurines) and for a mouse-opened zoom.
                        string byDigit = CabinMap.ZoomDisplayNameForDigit(slot, digit);
                        string spoken  = byDigit ?? CabinMap.ZoomDisplayName(nm);
                        CombatAnnouncer.DropCommentary($"stepped towards {nm}");

                        // REMEMBERED SO BACKING OUT CAN SAY THE SAME WORD.
                        // Only the key and the object are kept, and the object is
                        // re-checked on the way out — if the zoom in the air is
                        // not the one this digit opened, the memory is stale and
                        // is not used. IKMA is remembering its OWN authored key
                        // here, never the game's idea of what is open; that stays
                        // ZoomInteractable.currentZoomInteractable's job.
                        _zoomOpenedName = byDigit;
                        _zoomOpenedObj  = target;
                        _zoomOpenedKey  = $"{slot}|{digit}";

                        if (spoken == null)
                        {
                            _log?.LogWarning(
                                $"IKMA CABIN: no approved name for '{nm}' at '{slot}|{digit}' — " +
                                "speaking the short form. Ask Zamar for a word.");
                            Speech.Browse(Vocabulary.Map.YouFocusIn);
                        }
                        else
                        {
                            // ONE SENTENCE FOR EVERY ZOOM. (0.7.112.)
                            //
                            // 0.7.111 briefly spoke "You focus in on the X" for
                            // digit-named objects and "You focus on the X" for
                            // the rest, because his first message read "Change
                            // to you focus in on the skull" and changing the
                            // already-approved Safe line without asking would
                            // have broken the wording rule.
                            //
                            // Asking was right and the answer was the other way:
                            // "Sorry, that one is correct. They should all read
                            // 'You focus on the [x]'." Nomenclature holding
                            // across every screen is the whole point of that
                            // rule, and two phrasings for one action is exactly
                            // what it exists to prevent.
                            Speech.Browse(Vocabulary.Map.YouFocusOnThe(spoken));
                        }
                        return;
                    }
                }
            }

            string text = CabinMap.ExamineFor(slot, digit);

            if (string.IsNullOrEmpty(text))
            {
                _log?.LogInfo(
                    $"IKMA CABIN: key {digit} at slot '{slot ?? "unknown"}' — " +
                    $"nothing authored (pressed={pressed}).");

                // A PRESS IS ITSELF AN ANSWER. If the game did something, saying
                // "Nothing to examine here." would be false — the object just
                // moved. Stay quiet and let the game speak for itself; only a
                // key that did nothing at all reports nothing.
                if (!pressed) Speech.Browse(Vocabulary.NothingToExamine);
                return;
            }

            _log?.LogInfo($"IKMA CABIN: key {digit} at slot '{slot}' (pressed={pressed}).");
            Speech.Browse(text);
        }

        public static void OpenRulebookFromCabin()
        {
            if (!Standing) return;

            if (CurrentSlotKey() == RULEBOOK_SLOT)
            {
                _log?.LogInfo("IKMA CABIN: opening the rulebook from the shelf.");
                RulebookReader.Open();
                return;
            }

            Speech.Browse(Vocabulary.Map.RulebookIsNotWithin);
        }

        public static void DescribeAhead()
        {
            if (!Standing) return;

            // SPACE INSIDE THE OPEN SAFE. (0.7.108, his line.)
            //
            // Space always means "what is in front of me", and once the door is
            // open what is in front of the player is the safe's contents — not
            // the slot's description, which is written for someone standing back
            // from a shut safe. The opening line said this once as it happened;
            // this is the same fact, on demand, for a player who missed it or
            // walked away and came back.
            if (SafeIsOpen())
            {
                _log?.LogInfo("IKMA CABIN: Space inside the open safe.");
                Speech.Browse(
                    Vocabulary.Map.InsideTheSafeIs);
                return;
            }

            // THE WOODCARVING, ZOOMED. (0.7.112, his ask.)
            //
            // Same shape as the open safe above: once the camera is in on
            // something, "what is in front of me" is that thing, not the slot
            // description written for someone standing back from it.
            if (WoodcarvingIsOpen())
            {
                _log?.LogInfo("IKMA CABIN: Space on the woodcarving.");
                SpeakFigurine();
                return;
            }

            // ZOOMED ON SOMETHING A NUMBER KEY OPENED. (0.7.423.)
            //
            // Zamar, focused on the skull, pressed Space and heard "Nothing
            // is directly in front of you" - the camera is off the grid in a
            // zoom, so no slot description matched. Same shape as the two
            // above: what is in front of him is the thing he focused on. His
            // line, keyed on the digit that opened it; the object is
            // re-checked so a stale key is never spoken.
            var openZoom = ActiveZoom();
            if (openZoom != null && _zoomOpenedKey != null && ReferenceEquals(_zoomOpenedObj, openZoom))
            {
                string closer = CabinMap.ZoomedLookFor(_zoomOpenedKey);
                if (closer != null)
                {
                    _log?.LogInfo($"IKMA CABIN: Space while zoomed on '{_zoomOpenedKey}'.");
                    Speech.Browse(closer);
                    return;
                }
            }

            var t = CameraTransform();
            float? heading = Heading();

            if (t == null || heading == null)
            {
                _log?.LogWarning("IKMA CABIN: the camera transform could not be read.");
                Speech.Browse(Vocabulary.NothingInFront);
                return;
            }

            UnityEngine.Vector3 p;
            try { p = t.position; }
            catch
            {
                Speech.Browse(Vocabulary.NothingInFront);
                return;
            }

            string key;
            string described = CabinMap.DescribeAhead(p, heading.Value, out key);

            _log?.LogInfo(
                $"IKMA CABIN: x={p.x:F2} y={p.y:F2} z={p.z:F2} heading={heading.Value:F1} " +
                $"key={key ?? "off-grid"} described={(described != null)}.");

            // HIS TEXT OR HIS FALLBACK — nothing in between, and nothing
            // invented. Until the description sheet is filled in, the honest
            // answer is that there is nothing to say, which is the line he
            // wrote: "Nothing is directly in front of you."
            //
            // THE SLOT NUMBER IS NO LONGER SPOKEN. (0.7.111.) It was authoring
            // scaffolding, so he could hear which of the twenty-eight slots he
            // was in without reading the log. Zamar, Session 18: "Only describe
            // facing direction (slot numbers stay hidden, that's just for us on
            // the dev end)." It stays in the log line above, which is where the
            // dev end actually reads it.
            //
            // FACING COMES AFTER HIS DESCRIPTION, HIS CALL — asked as "before
            // your description or after it?", answered "after it." His prose is
            // the point of the line and it goes first; the compass point is
            // orientation the player can take or leave once they have heard the
            // room.
            //
            // THIS REPLACES THE M KEY, which said "You are standing at the
            // table." from anywhere in the room. See the deleted
            // AnnounceStanding below.
            //
            // IKMA WORDING: "Facing north." is PROVISIONAL — Zamar named the
            // information, not the sentence. Flagged to him in the same message
            // that shipped it, and it is one string to change when he answers.
            string facingLine = null;
            if (key != null)
            {
                int bar = key.IndexOf('|');
                if (bar >= 0 && bar + 1 < key.Length)
                {
                    facingLine = Vocabulary.Map.Facing(key.Substring(bar + 1));
                }
            }

            if (described != null)
            {
                Speech.Browse(
                    facingLine == null ? described : $"{described} {facingLine}");
                return;
            }

            Speech.Browse(
                Vocabulary.Map.NothingIsDirectlyIn(facingLine));
        }

        /// <summary>
        /// Eight-point heading. A label rather than a raw number so a repeated
        /// facing is recognisable by ear while walking the room.
        /// </summary>
        private static string Compass(float degrees)
        {
            string[] points = { "north", "northeast", "east", "southeast",
                                "south", "southwest", "west", "northwest" };
            int i = (int)System.Math.Round(degrees / 45f) % 8;
            return points[i];
        }

        // AnnounceStanding() WAS HERE AND IS DELETED. (0.7.111.)
        //
        // It was bound to M — the last M binding in the mod after Session 16
        // moved every other one to Space — and it opened with "You are standing
        // at the table." unconditionally, which stopped being true the moment
        // the player walked a single space. The remainder of the line repeated
        // the standing controls, which H already speaks.
        //
        // Deleted rather than left unbound. An unbound reader that speaks a
        // false sentence is a loaded gun for whoever re-binds it later; the
        // position now comes from CurrentZone, on Space, beside the description.

        /// <summary>
        /// H while ZOOMED IN. Zamar, Session 17: "When focusing on something,
        /// pressing H should not read Standing Controls, it should read Focus
        /// Controls."
        ///
        /// The keys genuinely mean different things inside a zoom — the digits
        /// work the object rather than choosing one, and Backspace leaves the
        /// zoom rather than walking the room — so the help that describes the
        /// standing layer was actively wrong here, which is the same defect
        /// class as the map controls answering on the sacrifice stone.
        ///
        /// EVERY WORD IS HIS.
        /// </summary>
        public static void SpeakFocusHelp()
        {
            Speech.Browse(
                Vocabulary.Map.FocusControlsAndExamine());
        }

        /// <summary>True while the player is zoomed into a cabin object.</summary>
        public static bool Focused()
        {
            return ActiveZoom() != null;
        }

        public static void SpeakStandingHelp()
        {
            Speech.Browse(
                Vocabulary.Map.StandingControlsUpArrow());
        }

        public static bool MapAvailable()
        {
            var mgr = MapNodeManager.Instance;
            if (mgr == null || mgr.ActiveNode == null || mgr.MovingNodes) return false;

            // AND THE BOARD HAS TO BE SWITCHED ON. (0.7.247.)
            //
            // Zamar's 0.7.246 log, three times in a row:
            //
            //   [Error] Coroutine couldn't be started because the game object
            //           'Nodes' is inactive!
            //   IKMA MAP: node clicked (Card choice Random).
            //   IKMA SPEAK: Cannot travel there.
            //
            // MapNodeManager lives on that object and OnNodeSelected starts
            // DoMoveToNewNode on it, so while it is inactive a click throws
            // inside Unity and the move never begins. ActiveNode and MovingNodes
            // both read fine across that window — they are plain fields and
            // survive the object being switched off — which is why the guard
            // added in 0.7.246 passed and let the click through anyway.
            //
            // The underlying cause was a node screen that had not finished (see
            // CardChoiceReader, 0.7.247) and that is fixed at the source. This
            // stays because the map has more than one way to be mid-transition
            // and the player should never be the one who finds the next one.
            try { if (!mgr.gameObject.activeInHierarchy) return false; }
            catch { }

            return true;
        }

        /// <summary>
        /// A node's horizontal position. Returns 0 when it cannot be read, which
        /// leaves the game's own order untouched rather than shuffling nodes
        /// against an unknown.
        /// </summary>
        private static float NodeX(MapNode node)
        {
            try { return node.transform.position.x; }
            catch { return 0f; }
        }

        /// <summary>
        /// Where the player is, for a sentence that begins "You are at ".
        /// (0.7.218.)
        ///
        /// Zamar, 0.7.217: "You are at blank. Should say 'beginning of new
        /// map.'" — the log line was literally `You are at . 4 paths ahead.`
        ///
        /// GetNodeFriendlyName answers for the node you are STANDING ON, and
        /// at the top of a fresh map there is no such node: the active node is
        /// the unnamed start marker and the name comes back empty. That is not
        /// an error to be fixed inside GetNodeFriendlyName — every other caller
        /// wants the empty answer for a nameless node — it is a missing case in
        /// the one sentence that needs a PLACE rather than a node.
        ///
        /// His words, so the sentence reads "You are at the beginning of a new
        /// map."
        /// </summary>
        // ==================================================================
        // THE MAP'S OWN NAME. (0.7.345.)
        //
        // Zamar, Session 26: "Does each game map have a name? Could this
        // instead have read 'You have arrived at The Beach / 4 paths
        // ahead...'"
        //
        // RegionData carries no display name — its asset name (Forest,
        // Wetlands, Alpine) is an internal id. But the GAME names three of
        // them, in Leshy's narration when a region begins
        // (PaperGameMap.CompleteRegionSequence plays "Region" + region.name):
        //   RegionForest   "You were embarking upon... The Woodlands."
        //   RegionWetlands "You tread cautiously into... The Wetlands."
        //   RegionAlpine   "You had ascended to... The Snow Line."
        // Those are the words used. The final regions (Midnight, and
        // Pirateville with the Final Boss challenge) have no such line, so
        // they have no name here either and keep "a new map".
        // ==================================================================
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _regionNames => Loc.PerLanguage(ref _regionNamesCache, ref _regionNamesLanguage, Build_regionNames);
        private static Dictionary<string, string> Build_regionNames() =>
            new Dictionary<string, string>
        {
            { "Forest",   Vocabulary.Map.Woodlands },
            { "Wetlands", Vocabulary.Map.Wetlands },
            { "Alpine",   Vocabulary.Map.SnowLine },
            // HIS NAME, Session 26 — the game gives the Final Boss map none.
            { "Pirateville", Vocabulary.Map.Beach },
            // HIS NAME, Session 26 — Leshy's final map, which the game never
            // names: "You have arrived at the path to the Cabin."
            { "Midnight",           Vocabulary.Map.PathToTheCabin },
            { "Midnight_Ascension", Vocabulary.Map.PathToTheCabin },
        };
        private static Dictionary<string, string> _regionNamesCache;
        private static string _regionNamesLanguage;

        private static readonly HashSet<string> _unnamedRegionsLogged = new HashSet<string>();

        private static string RegionName()
        {
            string id = null;
            try { id = RunState.CurrentMapRegion?.name; } catch { }
            if (string.IsNullOrEmpty(id)) return null;
            if (_regionNames.TryGetValue(id, out string spoken)) return spoken;
            if (_unnamedRegionsLogged.Add(id))
                Plugin.Log?.LogInfo($"IKMA MAP: region '{id}' has no name in the game's own lines — not named.");
            return null;
        }

        /// <summary>
        /// "You are at [node]" — or, at the top of a map whose name the game
        /// says, his sentence: "You have arrived at The Woodlands".
        /// No full stop; callers add it.
        /// </summary>
        private static string WhereSentence(MapNode node)
        {
            string name = null;
            try { name = GetNodeFriendlyName(node); } catch { }
            if (string.IsNullOrWhiteSpace(name))
            {
                string region = RegionName();
                if (region != null) return Vocabulary.Map.YouHaveArrivedAt(region);
            }
            return Vocabulary.Map.WhereSentenceYouAreAt(PlaceName(node));
        }

        private static string PlaceName(MapNode node)
        {
            string name = null;
            try { name = GetNodeFriendlyName(node); } catch { }
            if (!string.IsNullOrWhiteSpace(name)) return name;

            Plugin.Log?.LogInfo("IKMA MAP: active node has no name — reading it as the start of a map.");
            return Vocabulary.Map.PlaceName;
        }

        public static string GetNodeFriendlyName(MapNode node)
        {
            var data = node?.Data;
            if (data == null) return Vocabulary.Map.UnknownLocation;

            string raw = data.GetType().Name;
            if (raw.EndsWith("NodeData"))
                raw = raw.Substring(0, raw.Length - "NodeData".Length);

            if (_friendlyNames.TryGetValue(raw, out string friendly))
            {
                // NAME THE BOSS. (0.7.159, his ask: "Can we specify Boss
                // battle: Prospector?")
                //
                // PARITY CHECKED AND CLEARED BY HIM, not assumed: "The map node
                // is unique for each boss so a sighted player could tell yes."
                // Reading it is closing a gap, not opening one. Recorded here so
                // nobody pulls this line later on a parity worry he has already
                // ruled on.
                //
                // BossBattleNodeData.bossType is a PUBLIC Type field holding
                // the boss's own opponent class -- ProspectorBossOpponent,
                // AnglerBossOpponent, TrapperTraderBossOpponent,
                // LeshyBossOpponent (dump_bosses_and_run_end.txt). The name
                // comes off the game's own type rather than a table of guesses,
                // so a boss this project has not met still reads correctly.
                string boss = BossNameOf(data);
                if (boss != null) return Vocabulary.Map.NodeWithBoss(friendly, boss);

                // WHICH KIND OF CARD CHOICE. (0.7.243.) Zamar: "my first node is
                // card choice kin or random but they both just read card choice.
                // Card Choice, Kin. Card Choice, Random."
                //
                // Parity is the same argument the boss name already settled: the
                // node's PREFAB differs per type — CardChoicesNodeData.PrefabPath
                // appends the type name — so a sighted player can see which one
                // it is from the map. choicesType is PUBLIC and is what the game
                // itself reads to pick that prefab.
                string kind = CardChoiceKindOf(data);
                return kind == null ? friendly : $"{friendly} {kind}";
            }

            // Fallback: split CamelCase into words ("StartingIsland" -> "Starting Island").
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// "Prospector" for a boss node, null for everything else.
        ///
        /// bossType is Opponent.Type, the game's OWN ENUM -- not System.Type,
        /// which is what the dump's bare "Type" column read like and what cost
        /// a build. Its members are ProspectorBoss, AnglerBoss, WoodcarverBoss,
        /// TrapperTraderBoss, LeshyBoss and the later acts'
        /// (dump_bosses_and_run_end.txt).
        ///
        /// Strip the "Boss" suffix and split CamelCase, so TrapperTrader reads
        /// as two words. No lookup table -- a table would be a list of names
        /// IKMA believes in, and this way the game is still the one saying it,
        /// including for a boss this project has never met.
        /// </summary>
        private static string BossNameOf(NodeData data)
        {
            var bossNode = data as BossBattleNodeData;
            if (bossNode == null) return null;

            string n;
            try { n = bossNode.bossType.ToString(); }
            catch { return null; }

            if (string.IsNullOrEmpty(n)) return null;
            if (n == "Default" || n == "NoPlayQueue") return null;

            if (n.EndsWith("Boss"))
                n = n.Substring(0, n.Length - "Boss".Length);
            if (n.Length == 0) return null;

            // THE ONE EXCEPTION TO "NO LOOKUP TABLE". (0.7.301.)
            //
            // Zamar, on "Boss battle: Trapper Trader": "Should just be
            // Trapper." The enum value names both halves of the fight because
            // one opponent class runs both, but the node a player walks up to
            // is the Trapper's — the Trader is who he BECOMES, in phase two,
            // and announcing it on the map tells the player something the map
            // does not show them.
            //
            // Named here rather than in a table of every boss, so the
            // game-says-it rule above still holds for the other four and for
            // any boss this project has never met.
            if (n == "TrapperTrader") n = Vocabulary.Trapper;

            var sb = new System.Text.StringBuilder();
            foreach (char c in n)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Paths ahead of the current node. ITERATION POINT 1: if this offers
        // backward nodes, filter here.
        // ------------------------------------------------------------------
        public static List<MapNode> GetChoices()
        {
            var active = MapNodeManager.Instance?.ActiveNode;
            var connected = active?.connectedNodes;
            if (connected == null) return new List<MapNode>();

            var result = new List<MapNode>();
            foreach (var n in connected)
                if (n != null) result.Add(n);

            // ORDERED LEFT TO RIGHT, because that is the order they are on the
            // table. (Session 16.) Zamar, against his own screenshot: B read
            // "first path: Campfire, second path: Card removal" when removal is
            // the LEFTMOST option, so it should have been first.
            //
            // connectedNodes is whatever order the game stored them in, which is
            // not the order they are drawn — the same defect as the menu that
            // read Back in the middle of its list because screenInteractables
            // said so. What a blind player is told is "first" has to be what a
            // sighted player sees first.
            //
            // Sorted on world x, which is the map's horizontal axis. The choice
            // of axis is the one assumption here, so every sort logs its inputs:
            // if the numbers do not line up left to right on screen, the log
            // says which axis to use instead.
            result.Sort((a, b) => NodeX(a).CompareTo(NodeX(b)));

            if (result.Count > 1)
            {
                var parts = new List<string>();
                foreach (var n in result)
                    parts.Add($"{GetNodeFriendlyName(n)} x={NodeX(n):F2}");
                // ONCE PER LAYOUT, NOT ONCE PER CALL. (0.7.315.) Zamar's
                // 0.7.300 log had this fifteen times for five hovers —
                // GetChoices is asked by several callers and the answer only
                // changes when the map does. Log the CHANGE, not the call;
                // the same rule the per-frame lines learned in Session 18.
                string layout = string.Join(", ", parts.ToArray());
                if (layout != _lastChoiceLayout)
                {
                    _lastChoiceLayout = layout;
                    _log?.LogInfo($"IKMA MAP: choices left to right — {layout}.");
                }
            }
            else if (result.Count == 1)
            {
                // ONE CHOICE STILL LOGS. Zamar's map had a single node ahead, so
                // the multi-choice line never fired and the log said nothing
                // about ordering at all — while B, reading four rows deeper, was
                // getting it wrong. A row with one option is exactly when the
                // ordering question moves further down the map.
                _log?.LogInfo(
                    $"IKMA MAP: one choice ahead — {GetNodeFriendlyName(result[0])} x={NodeX(result[0]):F2}.");
            }

            return result;
        }

        public static void BrowseChoices(int direction)
        {
            var choices = GetChoices();
            if (choices.Count == 0)
            {
                Speech.Browse(Vocabulary.NoPathsAhead);
                return;
            }

            if (!_choiceHeard)
            {
                // Read the cursor's own position; do not move it.
                _choiceHeard = true;
                if (_choiceIndex >= choices.Count) _choiceIndex = 0;
            }
            else
            {
                _choiceIndex = ((_choiceIndex + direction) % choices.Count + choices.Count) % choices.Count;
            }

            // Session 16: the position is NOT spoken while browsing. Zamar's
            // rule, and it is general — "the number of options available (and
            // which one you're currently on) should only be read by pressing M."
            // Arrowing the paths ahead now answers with the path and nothing
            // else; M says where in the list you are.
            var node = choices[_choiceIndex];
            HoverNode(node);
            Speech.Browse($"{GetNodeFriendlyName(node)}.");
        }

        // ==================================================================
        // THE MAP HOVER, FOR THE ARROW KEYS. (0.7.152.)
        //
        // Zamar: "On map selection when you mouse hover over a path, it puts a
        // small decal on it, tilts your game piece towards it, and makes a
        // small sfx. I need that functionality for our arrow browsing."
        //
        // Three separate effects, and IKMA reproduces none of them — it calls
        // the game's own hover and lets the game do all three. MapNode declares
        // OnCursorEnter/OnCursorExit as overrides of MainInputInteractable
        // (dump_map.txt), so CursorEnter() is the same universal primitive that
        // drives card play, the cabin touch and the Leshy bark. Whatever a
        // mouse hover does — the decal, the tilt, the sound, and anything else
        // in there nobody has noticed — a browse now does too, and stays
        // correct if the game ever changes it.
        //
        // The EXIT matters as much as the enter: without it every node arrowed
        // through keeps its decal and the map ends up lit up all over, which is
        // a picture no sighted player ever sees. One hover at a time.
        //
        // This is also the streaming case the project has honoured since the
        // card choice screen — a blind player's screen should show a sighted
        // viewer what they are actually on.
        // ==================================================================
        private static MapNode _hoveredNode;

        private static void HoverNode(MapNode node)
        {
            if (ReferenceEquals(_hoveredNode, node)) return;

            if (_hoveredNode != null)
            {
                try { _hoveredNode.CursorExit(); }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA MAP: un-hover threw {e.GetType().Name}.");
                }
            }
            _hoveredNode = null;

            if (node == null) return;

            try
            {
                node.CursorEnter();
                _hoveredNode = node;
                _log?.LogInfo($"IKMA MAP: hovering {GetNodeFriendlyName(node)}.");
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MAP: hover threw {e.GetType().Name}.");
            }
        }

        /// <summary>
        /// Drop the hover. Called when the map stops being the thing the player
        /// is looking at — travel, arrival, a screen change — so no decal is
        /// left sitting on a path he is no longer choosing.
        /// </summary>
        internal static void ClearHover()
        {
            HoverNode(null);
        }

        /// <summary>
        /// Reset browse position to the first path. Called on node arrival and
        /// on map return so browsing always starts at option 1 rather than
        /// wherever the previous node's list left off. (Session 9.)
        /// </summary>
        public static void ResetChoiceIndex()
        {
            _choiceHeard = false;
            _choiceIndex = 0;
            ClearHover();
        }

        // ------------------------------------------------------------------
        // Spoken when the map becomes navigable again after an encounter or
        // event. This is where the "press M" hint belongs — it is only true
        // here, not on arrival at a node. (Session 9 note 1.)
        // ------------------------------------------------------------------
        /// <summary>
        /// THIS NO LONGER STOMPS THE END OF A COMBAT. (0.7.120.)
        ///
        /// Zamar: "when I won that combat the Map stomped all the damage info in
        /// an unsatisfying way." His log shows exactly that — the last damage
        /// line and the scales were still being spoken when the map arrival cut
        /// in with interrupt: true.
        ///
        /// The map arriving is a PROMPT, not a result. It tells the player where
        /// they are and what keys work, and it is true for as long as they stand
        /// there — so it can wait, and the rule this project already settled says
        /// combat results are never overtaken. An interrupt was the wrong tool
        /// for a line with no deadline.
        ///
        /// EnqueuePrompt puts it ahead of pending commentary but behind every
        /// Action line, which is the tier that carries damage, deaths and the
        /// victory. Deferred so the node and path count are read at speak time —
        /// by then the map has finished settling, which the old immediate read
        /// could not guarantee either.
        /// </summary>
        public static void AnnounceMapReady()
        {
            ResetChoiceIndex();

            Speech.Prompt(() =>
            {
                var mgr = MapNodeManager.Instance;
                if (mgr?.ActiveNode == null) return Vocabulary.Map.MapReady;

                // WHILE THE PIECE IS STILL SLIDING, ActiveNode IS THE NODE IT
                // LEFT. (0.7.245.)
                //
                // Zamar: "It said I was at campfire which was inaccurate. I hit
                // space afterwards and it was right." Space asks the same
                // ActiveNode a moment later and gets the right answer, so the
                // node is not wrong — the reading is early.
                //
                // NOT FIXED YET — MEASURED. A provider returning null drops
                // the line, and nothing calls AnnounceMapReady again when the
                // map settles, so withholding it would trade a wrong place name
                // for no place name at all. Silence on arrival is the worse of
                // the two.
                //
                // So this build only records whether the map still called
                // itself moving at the moment the line composed. If it did,
                // that is the cause and the fix is a delayed re-read; if it did
                // not, ActiveNode is genuinely lagging behind the player and the
                // fix is somewhere else entirely. One log answers which.
                bool moving = false;
                try { moving = mgr.MovingNodes; } catch { }

                string here = GetNodeFriendlyName(mgr.ActiveNode);
                _log?.LogInfo($"IKMA MAP: arrival line says '{here}' (map moving={moving}).");
                var choices = GetChoices();
                if (choices.Count == 0)
                {
                    // NOTHING TO SAY WHEN THE RUN IS OVER. (0.7.314.)
                    //
                    // Zamar, after beating the Trapper: "Map. You are at Boss
                    // battle: Trapper. No paths ahead." — "Dont need this line
                    // after beating a boss."
                    //
                    // The line exists to orient a player who is standing on
                    // the map with a choice to make. After a boss there is no
                    // choice and the game is already moving them on, so it
                    // describes a place they are leaving. The map's own
                    // reads are still there for anyone who presses for them.
                    _log?.LogInfo(
                        $"IKMA MAP: at '{here}' with no paths ahead — not announced.");
                    return null;
                }

                string pathWord = Vocabulary.Map.PathCount(choices.Count);
                // 0.7.152, his call: D is gone from the map and the line names
                // the key that actually shows the deck ON THE TABLE. D opened
                // DeckViewReader, a different screen from Shift+Up's MapReader.
                // ShowDeck — two keys, two deck views, one of them unannounced.
                // 0.7.345 — WhereSentence: the region's name at the top of a
                // map, the node's name everywhere else.
                return Vocabulary.Map.MapAheadArrowsTo(WhereSentence(mgr.ActiveNode), pathWord);
            });
        }

        // ------------------------------------------------------------------
        // Idle prompt. (Session 11.)
        //
        // Spoken on a repeat while the map sits waiting and the player has
        // touched nothing. Deliberately states the keys every time rather than
        // shortening on repeats: Zamar's call, and the right one — someone who
        // took their headphones off, walked away, or came back to the game an
        // hour later needs the controls, not a reminder that they once heard
        // them. This costs a few seconds of speech in a state where nothing is
        // happening anyway.
        //
        // Names the current node too, so the prompt doubles as orientation for
        // a player returning to a game they left running.
        // ------------------------------------------------------------------
        /// <summary>
        /// THE IDLE PROMPT NEVER INTERRUPTS. (Session 16, and it is general.)
        ///
        /// Zamar, on hearing "Awaiting path selection. 2 paths ahead..." cut
        /// something off: "this stomped something. It never should, lowest
        /// priority info. This is true for every single idle loop instruction
        /// callout we ever make."
        ///
        /// An idle prompt exists because the player has done NOTHING for several
        /// seconds. By definition it is never urgent, and by definition anything
        /// else in the air is more wanted than it. The card choice idle was
        /// fixed for this in Session 15 and the map's was missed.
        ///
        /// So: Info tier, deferred, and composed at SPEAK time — which also
        /// makes it self-withdrawing. If the player moved or travelled while it
        /// waited its turn, the provider returns null and the announcer drops it
        /// silently rather than describing a map they have left.
        /// </summary>
        public static void AnnounceIdlePrompt()
        {
            Speech.Commentary(() => ReviewHistory.AsPrompt(ComposeIdlePrompt()));   // a prompt: kept while History > Prompts is on (Session 38)
        }

        private static string ComposeIdlePrompt()
        {
            // Withdraw: a climb is in flight, or the player is no longer sitting
            // at a navigable map. Either way this line is about somewhere else.
            if (Walking) return null;
            if (CurrentView() != null && CurrentView() != MAP_VIEW) return null;
            if (!MapAvailable()) return null;

            // AND WITHDRAW WHILE THE GAME IS STILL TALKING. (0.7.217.)
            //
            // Zamar, 0.7.216 log:
            //
            //   IKMA SPEAK: Leshy: LET ME THINK...
            //   IKMA SPEAK: Map. Awaiting input. Press Space for your position...
            //   IKMA DIALOGUE: key swallowed - a conversation is in progress.
            //
            // "Should not read this Map call during this conversation. wait
            //  till that text is gone then fire it."
            //
            // He is right twice over. The line talked over a character, which
            // the narration rules forbid on its own - and it told him to press
            // keys that the dialogue gate was at that moment swallowing, which
            // is worse than noise: it is an instruction that does not work. His
            // next two presses were eaten and answered with "conversation in
            // progress", exactly as the prompt had invited.
            //
            // DialogueAdvancer.ConversationHolding() is the same question
            // HotkeyManager asks before swallowing those keys, so the prompt
            // and the keyboard now agree by construction rather than by luck.
            //
            // Withdrawing costs nothing: this provider composes at speak time
            // and the announcer drops a null silently, so the idle timer simply
            // offers it again once the text is gone. That is the mechanism the
            // rest of this method already relies on.
            try { if (DialogueAdvancer.ConversationHolding()) return null; }
            catch { /* if the question cannot be asked, speak as before */ }

            return ComposeIdlePromptInner();
        }

        private static string ComposeIdlePromptInner()
        {
            var mgr = MapNodeManager.Instance;
            var choices = GetChoices();

            if (mgr?.ActiveNode == null || choices.Count == 0)
            {
                // (No arrow hints here: with no active node there are no paths
                // to browse, and the ladder depends on a table the player is not
                // sitting at yet.)
                return Vocabulary.Map.MapAwaitingInputPress;
            }

            // "You are at X" dropped after test: on a repeating prompt it is the
            // one part that does not change and does not help — the player has
            // not moved, which is precisely why the prompt is firing. Space
            // still gives position on demand.
            //
            // AND THE FULL CONTROLS ARE GONE TOO, AS OF 0.7.122. His wording:
            // "Awaiting path selection. H, for help."
            //
            // THIS REVERSES A RULE THIS PROJECT WROTE DOWN, so it is worth being
            // explicit. The old rule was "state the full controls every time" on
            // an idle prompt, for someone who took their headphones off or came
            // back to a game left running. That was written when H did not
            // answer for every layer; it does now, so the prompt can point at
            // help instead of being help. A line that repeats on a timer is the
            // worst place to put twenty words, and this one repeats forever.
            //
            // The path count goes with it: counts belong to one key, and here
            // that key is Space.
            return Vocabulary.Map.AwaitingPathSelectionH;
        }

        public static MapNode GetSelectedChoice()
        {
            var choices = GetChoices();
            if (choices.Count == 0) return null;
            if (_choiceIndex >= choices.Count) _choiceIndex = 0;
            return choices[_choiceIndex];
        }

        // ------------------------------------------------------------------
        // M key: where am I, and what's ahead.
        // ------------------------------------------------------------------
        // ==================================================================
        // THE PATHS AHEAD. O on the map, the same key that reads the opponent's
        // upcoming queue in a battle. (Session 16, Zamar's ask: reuse the
        // combat key for the same shape of question — what is coming.)
        //
        // FOUR ROWS, because that is what he says a sighted player can see:
        // "I believe you can only ever see up to 4 space ahead." The parity rule
        // decides the depth, not what the data structure would allow — the tree
        // continues past four and IKMA stops where the eye does.
        //
        // FOLLOWED BRANCH BY BRANCH, his choice over a flat row-by-row list,
        // because what a player wants from this is where each option LEADS, not
        // an inventory of what exists nearby.
        //
        // Routes are enumerated depth-first and deduplicated, so a fork that
        // rejoins does not read as one long path that does not exist. The cap
        // exists because a wide map could otherwise produce sixteen routes and
        // a paragraph nobody can hold in their head; when it bites, the line
        // says so rather than quietly truncating.
        // ==================================================================
        private const int PATH_DEPTH     = 4;
        private const int MAX_PATHS_READ = 6;

        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static string[] ORDINALS => Loc.PerLanguage(ref ORDINALSCache, ref ORDINALSLanguage, BuildORDINALS);
        private static string[] BuildORDINALS() =>
            new string[] {
            Vocabulary.Map.First, Vocabulary.Map.Second, Vocabulary.Map.Third, Vocabulary.Map.Fourth, Vocabulary.Map.Fifth, Vocabulary.Map.Sixth, Vocabulary.Map.Seventh, Vocabulary.Map.Eighth,
        };
        private static string[] ORDINALSCache;
        private static string ORDINALSLanguage;

        public static void AnnouncePathsAhead()
        {
            var choices = GetChoices();
            if (choices.Count == 0)
            {
                Speech.Browse(Vocabulary.NoPathsAhead);
                return;
            }

            // CONVERGENCE POINTS FIRST. Zamar, from his own map: "When a
            // branching path reconnects, like at encounter 3 spaces ahead, have
            // those spots be where B stops, instead of including the fourth
            // option behind it."
            //
            // He is right about what the reading is FOR. Past a join the paths
            // are the same journey, so listing what lies beyond it twice makes
            // two routes sound different when the choice has already stopped
            // mattering. The join is where the decision ends, so it is where the
            // sentence ends.
            //
            // A join is a node with more than one way into it inside the four
            // rows being read. Counted first, so the walk knows to stop there.
            var joins = FindConvergencePoints(choices);

            var routes = new List<List<string>>();
            foreach (var start in choices)
            {
                var walked = new List<MapNode>();
                Walk(start, walked, routes, joins);
                if (routes.Count >= MAX_PATHS_READ) break;
            }

            // Two branches that both stop at the same join now read identically.
            // Saying it twice would be describing one path as two.
            routes = Distinct(routes);

            if (routes.Count == 0)
            {
                Speech.Browse(Vocabulary.NoPathsAhead);
                return;
            }

            var parts = new List<string>();
            for (int i = 0; i < routes.Count && i < MAX_PATHS_READ; i++)
            {
                string ordinal = i < ORDINALS.Length ? ORDINALS[i] : Vocabulary.Map.PathNumber(i + 1);
                parts.Add(Vocabulary.Map.PathThen(ordinal, routes[i].ToArray()));
            }

            string line = string.Join(". ", parts.ToArray()) + ".";

            if (routes.Count > MAX_PATHS_READ)
                line += Vocabulary.Map.MorePathsNotRead(routes.Count - MAX_PATHS_READ);

            _log?.LogInfo($"IKMA MAP: {routes.Count} route(s) ahead, {PATH_DEPTH} deep.");
            Speech.Browse(line);
        }

        /// <summary>
        /// Depth-first to PATH_DEPTH. A node with no continuation ends a route;
        /// a fork starts a new one from the shared prefix, which is why the
        /// walked list is copied rather than shared.
        /// </summary>
        /// <summary>
        /// Every node inside the four-row window that has more than one way into
        /// it. Counted over the whole window rather than per branch, because a
        /// join between two sub-branches of the same option is still a join.
        /// </summary>
        private static HashSet<MapNode> FindConvergencePoints(List<MapNode> starts)
        {
            var incoming = new Dictionary<MapNode, int>();
            var seen     = new HashSet<MapNode>();

            var frontier = new List<MapNode>(starts);
            for (int depth = 1; depth < PATH_DEPTH && frontier.Count > 0; depth++)
            {
                var nextFrontier = new List<MapNode>();
                foreach (var node in frontier)
                {
                    if (node == null) continue;
                    var next = node.connectedNodes;
                    if (next == null) continue;

                    foreach (var child in next)
                    {
                        if (child == null) continue;

                        int count;
                        incoming[child] = incoming.TryGetValue(child, out count) ? count + 1 : 1;

                        if (seen.Add(child)) nextFrontier.Add(child);
                    }
                }
                frontier = nextFrontier;
            }

            var joins = new HashSet<MapNode>();
            foreach (var pair in incoming)
                if (pair.Value > 1) joins.Add(pair.Key);

            return joins;
        }

        private static List<List<string>> Distinct(List<List<string>> routes)
        {
            var result = new List<List<string>>();
            var seen   = new HashSet<string>();

            foreach (var r in routes)
            {
                string key = string.Join("\u0001", r.ToArray());
                if (seen.Add(key)) result.Add(r);
            }
            return result;
        }

        /// <summary>
        /// A node's continuations, ordered the way they sit on the table.
        /// Sorted on world x, the same axis and the same reason as GetChoices —
        /// connectedNodes is storage order, which is not draw order.
        /// </summary>
        private static List<MapNode> SortedChildren(List<MapNode> next)
        {
            var kids = new List<MapNode>();
            if (next == null) return kids;

            foreach (var n in next) if (n != null) kids.Add(n);
            kids.Sort((a, b) => NodeX(a).CompareTo(NodeX(b)));
            return kids;
        }

        private static void Walk(MapNode node, List<MapNode> walked, List<List<string>> routes,
                                 HashSet<MapNode> joins)
        {
            if (node == null || routes.Count >= MAX_PATHS_READ) return;

            walked.Add(node);

            var next = node.connectedNodes;
            bool atDepth = walked.Count >= PATH_DEPTH;
            bool hasNext = next != null && next.Count > 0;

            // A join ends the route AND is included in it — it is the place the
            // paths become one, so it is the last thing worth naming.
            //
            // BUT ONLY WHEN THE JOIN ITSELF OFFERS A CHOICE. (0.7.121.)
            //
            // Zamar's map: B stopped at an Encounter because it was a rejoin,
            // and the Card choice sitting behind it — reachable one way and one
            // way only — was never read. His call: "that should have been read
            // as the fourth option in all 3 available paths."
            //
            // He is right, and the original rule was solving a different
            // problem. Stopping at a join exists so three routes do not each
            // recite an identical branching tail. A join with ONE way forward
            // branches nothing: the next node is the same fact for every route
            // that reaches it, and naming it is the information B exists to
            // give. Withholding it is the parity question in its usual form —
            // a sighted player sees that node sitting there.
            //
            // A join that itself forks still ends the route, which is where the
            // noise the rule was written for actually starts. PATH_DEPTH still
            // bounds the whole walk either way.
            bool atJoin = joins != null && joins.Contains(node)
                       && hasNext && next.Count > 1;

            if (atDepth || !hasNext || atJoin)
            {
                var names = new List<string>();
                foreach (var n in walked) names.Add(GetNodeFriendlyName(n));
                routes.Add(names);
                return;
            }

            // LEFT TO RIGHT AT EVERY DEPTH, NOT JUST THE FIRST ROW. (0.7.76.)
            //
            // GetChoices sorts the immediate choices, and 0.7.73 stopped there —
            // so B was ordered correctly at row one and by raw connectedNodes
            // order everywhere below it. Zamar's map had ONE choice ahead, so
            // the sort never applied at all and every branch B read came out in
            // storage order: it said "first path ... Campfire, second path ...
            // Card removal" with Card removal the leftmost on his screen.
            //
            // "First path" is a claim about what a sighted player sees first,
            // and a claim like that has to hold for the whole sentence, not the
            // first word of it. Same defect as the menu reading Back from the
            // middle of its list because screenInteractables said so.
            foreach (var child in SortedChildren(next))
            {
                if (routes.Count >= MAX_PATHS_READ) return;

                // A copy per branch: the prefix is shared, the tail is not.
                var branch = new List<MapNode>(walked);
                Walk(child, branch, routes, joins);
            }
        }

        public static void AnnouncePosition()
        {
            var mgr = MapNodeManager.Instance;
            if (mgr?.ActiveNode == null)
            {
                Speech.Browse(Vocabulary.Map.MapUnavailable);
                return;
            }

            string here = PlaceName(mgr.ActiveNode);
            string where = WhereSentence(mgr.ActiveNode);   // 0.7.345
            var choices = GetChoices();
            if (choices.Count == 0)
            {
                Speech.Browse(Vocabulary.Map.NoPathsAhead(where));
                return;
            }

            var parts = new List<string>();
            for (int i = 0; i < choices.Count; i++)
                parts.Add(Vocabulary.Map.OptionNode(i + 1, GetNodeFriendlyName(choices[i])));

            string pathWord = Vocabulary.Map.PathCount(choices.Count);

            // Session 16: M is now the only place the position is spoken, so it
            // has to say which path the cursor is actually on — until now this
            // listed every path and left the player to work out where they were
            // in it, which was fine while browsing announced it and is not now.
            //
            // _choiceHeard guards it: before the first arrow press the cursor
            // has a position but the player has never been told they are on it,
            // and claiming a selection they have not made would be a confident
            // wrong answer.
            string standingOn = _choiceHeard && _choiceIndex < choices.Count
                ? Vocabulary.Map.YouAreOnOption(_choiceIndex + 1, choices.Count)
                : "";

            // ONE PATH IS NOT A CHOICE. (0.7.244.) Zamar: "If there's only 1
            // path ahead skip the arrows to browse call here." Browsing a list
            // of one moves nowhere, so offering the keys for it is the
            // key-that-does-nothing defect in the line whose whole job is
            // telling the player which keys work.
            string browse = choices.Count > 1 ? Vocabulary.Map.LeftAndRightArrows : "";

            Speech.Browse(
                Vocabulary.Map.AheadEnterToTravel(where, pathWord, parts, standingOn, browse));
        }
    }
}
