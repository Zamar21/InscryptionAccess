// HotkeyManager.cs
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// Hotkey layout (mirrors Hearthstone Access in-game commands where there
    /// is an equivalent).
    ///
    /// THIS HEADER IS A SUMMARY, NOT THE SOURCE OF TRUTH. It is written by
    /// hand and it has been wrong twice — it advertised the number-key jumps
    /// for two sessions after they were removed, and it omitted R, Shift+R and
    /// the card reward screen for four. The source of truth is the
    /// KeyIn.Down call sites below (Unity key reads before Session 34). `IKMA_Controls_SOURCE.md` is those
    /// call sites enumerated by context, and is what the player-facing
    /// `IKMA_Controls.md` should be written against.
    ///
    /// Enter is always Return OR KeypadEnter. Shift is either shift key.
    ///
    ///   GLOBAL — checked before anything else, works everywhere:
    ///   Shift+R        - Read the rulebook entry for every ability named in
    ///                    the line just spoken. Never shadows plain R, and
    ///                    plain R never shadows it.
    ///   H              - Context-sensitive help
    ///
    ///   QUERY HOTKEYS (normal browsing, mid-play, and the opponent's turn):
    ///   C              - Read hand: card names and costs only (HSA: c)
    ///   G              - Read opponent board (HSA: g)
    ///   Shift+G        - Read your board (HSA: b reads your minions only)
    ///   B              - Full situational read: your board, enemy board,
    ///                    upcoming queue (Inscryption-specific extension)
    ///   A              - Read resources (scales first, then bones) (HSA: a)
    ///   I              - Step through held items (tap repeatedly);
    ///                    Enter uses the one you are on. Arrows, Backspace or
    ///                    Tab return you to your hand.
    ///   Shift+I        - Read all three item slots, empties included
    ///   Shift+1/2/3    - Use item 1, 2 or 3 directly without entering
    ///                    item mode
    ///   U              - Read upcoming opponent queue (what Leshy plays next)
///                    (0.7.175: was O, which is not an HSA key and read as
///                    a second "opponent" alongside G.)
    ///   R              - Open the rulebook (map and battle)
    ///
    ///   NAVIGATION (HSA horizontal-list conventions, arrows only):
    ///   Left/Right     - Browse hand outside play flow; navigate slots during
    ///                    sacrifice or placement
    ///   Tab            - Jump to next playable (affordable) card in hand
    ///
    ///   NO NUMBER-KEY JUMPS. Removed in Session 11. JumpToSlot,
    ///   JumpToHandCard, JumpSlotEdge and JumpHandEdge are kept in source,
    ///   unbound, on purpose — ready if a long-list context appears in Act 2
    ///   or 3. Shift+1/2/3 for items is unaffected. There is no Home/End
    ///   binding today either.
    ///
    ///   KAYCEE'S MOD MENU:
    ///   Arrows         - Browse the options on screen (up/down work too)
    ///   Enter          - Choose the option you are on
    ///   Backspace      - Back, only on screens that have a back button
    ///   M              - Repeat the current option
    ///
    ///   CHOOSING AN ITEM TARGET (Fish Hook, Scissors, Hammer, Trapper Knife):
    ///   Arrows         - Move between valid targets (up/down work too)
    ///   Enter          - Select. Cannot be cancelled.
    ///                    Board reads work here EXCEPT I and Shift+I.
    ///
    ///   CARD REWARD AFTER A BATTLE (CardChoiceReader):
    ///   Arrows         - Browse the cards on offer (up/down work too)
    ///   Enter          - Turn a card over, or take it; the game decides which
    ///   M              - Repeat the card you are on
    ///                    A face-down card is never described, position only.
    ///
    ///   MAP MODE (active when no battle is live):
    ///   Left/Right     - Browse paths ahead of your current node
    ///   M              - Where am I: current node + all paths ahead
    ///   Enter          - Travel to the selected path (clicks the node via
    ///                    the universal primitive; outcome narrated)
    ///                    The map announces itself when an encounter ends and
    ///                    control returns. Idle prompt at 5.5s, then every 15s.
    ///
    ///   RULEBOOK (R):
    ///   Left/Up        - Previous page
    ///   Right/Down     - Next page
    ///   M              - Repeat the current page
    ///   Backspace / R  - Close the book
    ///
    ///   DRAW PHASE:
    ///   D              - Draw from your main deck
    ///   S              - Draw a squirrel (side deck)
    ///                    (Only active during the game's draw phase; the drawn
    ///                    card is announced by name once the game completes
    ///                    the draw. No HSA analog — Hearthstone draws
    ///                    automatically.)
    ///
    ///   PLAY FLOW:
    ///   Enter          - Play browsed card / confirm sacrifice / confirm slot
    ///   Backspace      - Attempt cancel (HSA universal cancel): injects the
    ///                    game's own cancel input via InputButtons for two
    ///                    frames; the game applies its own rules and the
    ///                    watcher narrates the real outcome. Confirmed NOT to
    ///                    take during slot placement or sacrifice — the key
    ///                    still tries and still reports honestly.
    ///   E              - Ring bell / end turn (HSA: e)
    ///   Any other key  - Re-read the slot you are on
    /// </summary>
    public class HotkeyManager : MonoBehaviour
    {
        private int _handIndex = -1;
        private bool _handArrowFromStart;   // Session 37, note D6
        private static int _slotIndex = 0;

        // True while a D/S draw coroutine is running — blocks double draws
        // from repeated key presses before the game exits the draw phase.
        private bool _drawInProgress = false;

        // Read by the add-to-hand narrator. A drawn card is already announced by
        // AnnounceDrawnCard, so the generic "created in your hand" line must not
        // also fire for it. (Session 14.)
        internal static bool DrawInFlight = false;
        private static bool  _bellHeldForDraw;   // Session 36
        private static float _bellDrawSeenAt, _bellHeldAt;

        // CardDrawPiles.Exhausted is a protected property (build error CS0122
        // when accessed directly) — read it via cached reflection instead.
        private static PropertyInfo _pilesExhaustedProperty;

        // Map-return latch (Session 9). A blind player gets no cue that an
        // encounter has ended and the map is theirs again. These track the
        // battle-active -> not-active transition so the map announces itself
        // once, when it is actually navigable. Observe-only.
        private bool _wasBattleActive = false;
        private bool _mapReturnPending = false;

        // Idle map prompt. (Session 11.)
        //
        // Loading into the map, the music starts and nothing indicates the game
        // is waiting on the player. A sighted player sees the nodes lit and the
        // cursor live; a blind player gets ambience and no reason to think it is
        // their move. Same class of gap as the Session 9 map-return latch —
        // control is yours and nothing says so.
        //
        // First prompt comes quickly, because silence right after a load is the
        // complaint. After that it repeats slowly, and any keypress resets it:
        // someone actively browsing does not need telling.
        private float _mapIdleTimer = 0f;
        private float _mapIdleInterval = MAP_IDLE_FIRST;
        // Session 11 test: 3s fired while the previous prompt was still being
        // read at Zamar's speech rate, and 10s between repeats stacked them.
        // The prompt is long on purpose — it names every key, for someone who
        // walked away or came back to a game left running — so the gaps have to
        // clear it.
        private const float MAP_IDLE_FIRST  = 5.5f;
        private const float MAP_IDLE_REPEAT = 15f;

        // Item mode (Session 9). Pressing I enters a browse layer over the
        // consumable slots: arrows move, Enter uses, Backspace leaves. Modeled
        // on the hand-browse pattern so there is one mental model for "browse a
        // list, act on the item you are on."
        private bool _itemMode = false;

        // Set by the PlayerTurn patch. Item mode does not survive a turn
        // boundary — see the reset in Update.
        internal static bool TurnStartedFlag = false;
        private const int NO_ITEM = -1;
        private int _itemIndex = NO_ITEM;

        // Item targeting (Session 9). Fish Hook, Scissors, Hammer and Trapper
        // Knife all derive from TargetSlotItem, whose ActivateSequence calls
        // BoardManager.ChooseTarget — the same class our placement navigation
        // already drives. Plugin.cs's ChooseTarget prefix hands us the target
        // lists; we browse the valid ones and click through the universal
        // primitive. There is deliberately no cancel: the game does not offer
        // one once an item is committed.
        // Long enough for "Using Scissors." to finish at Zamar's speech rate.
        // Measured against the 0.7.37 log, where that line took 431ms to speak.
        private const float TARGET_PROMPT_SETTLE = 0.75f;

        // The slots the game says this card may be played in, handed over by the
        // ChooseSlot patch. Null outside placement.
        private static List<CardSlot> _validPlacementSlots;

        // True once a slot has been read aloud since the game offered the
        // current placement. (0.7.341.)
        private static bool _slotReadThisPlacement;

        internal static void SetValidPlacementSlots(List<CardSlot> slots)
        {
            _validPlacementSlots = slots;
            _slotReadThisPlacement = false;
            Plugin.Log?.LogInfo($"IKMA PLACE: {slots?.Count ?? 0} valid slot(s) offered by the game.");
        }

        // The slot the game's cursor is currently on because IKMA put it there
        // during placement or sacrifice browsing. Released when the layer ends.
        private static CardSlot _hoveredSlot;

        private static List<CardSlot> _targetSlots;
        private static bool _targeting = false;
        private static int _targetIndex = 0;

        // Name of the item currently in flight, set the moment its slot is
        // clicked. Gives the resulting death the item's own verb rather than a
        // generic one. (Session 10.)
        private static string _activeItemName = null;

        // Cached reflection handles for item activation. ConsumableItem itself
        // is not clickable (Item -> ManagedBehaviour); the SLOT is the
        // interactable, and ConsumableItemSlot overrides OnCursorSelectStart.
        // Resolved by reflection because ItemSlot's base was not in the dump,
        // so CursorSelectStart/End may or may not be visible at compile time.
        private static System.Reflection.MethodInfo _cursorSelectStartMethod;
        private static System.Reflection.MethodInfo _cursorSelectEndMethod;
        private static bool _itemClickResolved = false;


        // Read by Plugin.cs's InputButtons_GetButtonDown_Patch: for frames up
        // to and including this value, the game's "was Cancel pressed?" query
        // answers yes. Set by the Backspace handler. (Bug 3 final approach.)
        // The placement cancel keeps its multi-frame window: it is a one-shot
        // and a missed frame there costs the cancel outright. Only the camera
        // cared about the difference between one press and three.
        internal static int CancelInjectUntilFrame
        {
            get { return InjectUntilFrame; }
            set
            {
                InjectUntilFrame = value;
                InjectButtonName = "Cancel";
                InjectDownOnly   = false;   // a one-shot the game may poll three ways
            }
        }

        // GENERALISED INPUT INJECTION. (Session 16.)
        //
        // Read by Plugin.cs's three InputButtons patches: for frames up to and
        // including InjectUntilFrame, the game's "was <InjectButtonName>
        // pressed?" query answers yes.
        //
        // The camera ladder rides this. A scroll of the wheel is Button.LookUp
        // or Button.LookDown, so pressing that button asks the game to do the
        // whole of what a wheel click does rather than having IKMA reproduce a
        // part of it. Matching is by EXACT name — the old cancel path matched
        // "cancel" as a substring, which is safe for one button and not for
        // four Look directions.
        internal static int    InjectUntilFrame  = -1;
        internal static string InjectButtonName  = null;

        // ONE PRESS IS ONE KEYSTROKE — and the way to get that is ONE FRAME,
        // not a narrower kind of press.
        //
        // 0.7.62 tried answering only GetButtonDown, on the theory that
        // answering the held queries too is what made a single arrow slide
        // across the cabin. The playtest killed that outright: EVERY press-only
        // injection was ignored and every held one worked —
        //
        //   IKMA VIEW: press-only LookUp was not taken at MapDefault; retrying...
        //   IKMA INPUT: injecting LookUp (frames 6300-6300, held).
        //   IKMA VIEW: MapDefault -> MapArial.
        //
        // ViewController polls the HELD queries, so press-only reaches nothing,
        // and the wait before each retry is the lag Zamar reported as "much less
        // snappy... like it had to load".
        //
        // The slide was never the kind of press. It was the LENGTH: a four-frame
        // window is four frames of a held button, and continuous movement moves
        // for as long as it is held. One frame is one keystroke's worth of
        // input, whichever way the caller polls.
        //
        // Kept as a switch because the climb still uses it as a second attempt
        // for any caller that does read GetButtonDown, and because a future
        // caller may need it. It is not the default.
        internal static bool   InjectDownOnly    = false;

        /// <summary>
        /// Press one of the game's own buttons for the next few frames. Named
        /// rather than typed, so this file never has to reference the Button
        /// enum and a renamed member fails in the log rather than the compiler.
        /// </summary>
        /// <param name="extraFrames">
        /// How many EXTRA frames beyond this one the button stays down.
        ///
        /// ZERO IS THE DEFAULT AND IT MATTERS. The placement cancel has always
        /// used a three-frame window, and for a one-shot cancel that is
        /// harmless. For the camera it was a bug: ViewController polls once per
        /// frame, so a three-frame window is THREE PRESSES. Zamar's 0.7.58 log
        /// shows it plainly —
        ///
        ///   IKMA VIEW: press 1 of LookUp, at MapDefault.
        ///   IKMA VIEW: MapDefault -&gt; MapArial.
        ///   IKMA VIEW: MapArial -&gt; MapDeckReview.     &lt;- same single press
        ///
        /// Shift+Up looked perfect because two rungs happened to be exactly
        /// where he wanted to land. Backspace from the deck view did not: the
        /// climb correctly stopped at MapDefault and a leftover frame of the
        /// same press carried him on down into standing.
        ///
        /// One frame is one press. Anything that genuinely needs a HELD button —
        /// walking, turning — asks for the frames it needs.
        /// </param>
        internal static void InjectButton(string buttonName, int extraFrames = 0, bool downOnly = false)
        {
            InjectButtonName = buttonName;
            InjectDownOnly   = downOnly;
            InjectUntilFrame = Time.frameCount + extraFrames;
            Plugin.Log?.LogInfo(
                $"IKMA INPUT: injecting {buttonName} (frames {Time.frameCount}-{InjectUntilFrame}" +
                (downOnly ? ", press only" : ", held") + ").");
        }

        // Written by Plugin.cs's BoardManager_ChooseSlot_Patch from the game's
        // own canCancel parameter — the game's declaration of whether the
        // current slot selection can be backed out of. Grounds the Backspace
        // narration in actual game state rather than assumption. (Session 8.)
        internal static bool LastChooseSlotCanCancel = false;

        // Cached reflection handle for TurnManager.OnCombatBellRang.
        // Lazy-resolved on first use (confirmed via reflection dump, Session 7).
        // OnCombatBellRang is the TurnManager hook the physical bell calls when
        // actually rung -- this is the correct target, NOT CombatBell3D's
        // cursor-dispatch methods (OnCursorSelectEnd etc.), which are generic
        // MainInputInteractable handlers shared by every interactable and do
        // not themselves trigger the turn-end/combat-start logic.
        private static MethodInfo _onCombatBellRangMethod;

        // Cached reflection handles for the physical bell (BoardManager3D.Bell
        // -> CombatBell3D). Confirmed via reflection dump, Session 7:
        // BoardManager3D : BoardManager, BoardManager is Singleton<BoardManager>,
        // and CombatBell3D exposes OnBellPressed(), PlaySound(), PressingAllowed().
        // Session 8: E now invokes OnBellPressed() — the game's own click
        // handler — to replicate a real bell press (ding + ring) in one call,
        // instead of recomposing it from PlaySound + OnCombatBellRang.
        private static PropertyInfo _boardManager3DBellProperty;
        private static MethodInfo _combatBellPlaySoundMethod;
        private static MethodInfo _combatBellOnBellPressedMethod;
        private static MethodInfo _combatBellPressingAllowedMethod;

        // FOCUS DIAGNOSTIC. (Session 17.) Log only.
        //
        // The crash happens on minimise-and-restore and leaves nothing behind.
        // These two lines put a marker either side of it, so the next log says
        // whether the cut lands at focus loss, at restore, or somewhere else
        // entirely — which decides whether the STA fix in SpeechPump was the
        // right suspect.
        //
        // Queue depth is included because his crash had a speech call in flight
        // with three more waiting.
        //
        // AND DEPTH ALONE WAS MISLEADING US. (0.7.116.) The worker dequeues an
        // item and only then calls into UniversalSpeech, so depth reads 0 for
        // the whole duration of a call — every crash log so far said "depth 0"
        // and that was taken as the pump being idle. SpeechPump.InCall is the
        // question that was actually meant: was the worker thread inside the
        // library when this fired. Zamar's Session 18 report — the crash
        // happening while it was reading — is why it matters.
        private static string SpeechState()
        {
            double ms = SpeechPump.InCallMs;
            return SpeechPump.InCall
                ? $"speech queue depth {SpeechPump.PendingCount}, IN A SPEECH CALL for {ms:F0}ms"
                : $"speech queue depth {SpeechPump.PendingCount}, no call in flight";
        }

        // FOCUS LOGGING IS OFF BY DEFAULT. (0.7.168.)
        //
        // Zamar, reading a Prospector log: "This log is too busy, hide the
        // views and focus calls from the log if we can."
        //
        // These fire on every alt-tab, four lines at a time — focus lost,
        // paused, resumed, focus gained — and during a playtest where he is
        // switching to the chat window to report something, they bury the
        // sequence he is trying to show. THE LOG IS AN ARTIFACT HE READS WITH A
        // SCREEN READER, so a line that is only useful to a specific
        // investigation is a cost on every other one.
        //
        // KEPT, NOT DELETED, and behind a flag rather than removed: this
        // logging is what diagnosed the minimise crash in Session 18, and
        // SpeechState() with it. Set VerboseFocusLog to true when working on
        // anything to do with backgrounding, speech in flight, or the pump.
        internal static bool VerboseFocusLog = false;

        /// <summary>
        /// The game is in the draw phase and the player has not drawn yet.
        /// (0.7.173.) Asked of the game every time rather than latched, so the
        /// answer cannot outlive the phase — CardsDrawnThisTurn is the game's
        /// own counter and is what the draw prompt itself withdraws on.
        /// </summary>
        private static bool DrawPhaseAwaitingDraw()
        {
            try
            {
                var tm = TurnManager.Instance;
                if (tm == null || !tm.IsPlayerTurn || tm.IsSetupPhase) return false;

                var hand = Singleton<PlayerHand>.Instance;
                if (hand == null) return false;

                return hand.CardsDrawnThisTurn == 0;
            }
            catch { return false; }
        }

#if IKMA_DEV
        // The timecode readout, and the only OnGUI in the mod. It draws two
        // labels and nothing else; the string it draws was built in Update,
        // because OnGUI runs several times per frame.
        //
        // DEV ONLY — the method itself is compiled out of a release build, so
        // the shipped DLL has no OnGUI at all and Unity never calls one.
        // See VideoTimecode.
        private void OnGUI()
        {
            VideoTimecode.Draw();
        }
#endif

        private void OnApplicationFocus(bool hasFocus)
        {
            // 0.7.339 — the game stops while the window is in the background,
            // so the first frame back is as long as the time away. Not a hitch.
            if (!hasFocus) Perf.NoteFocusLost();
            if (!VerboseFocusLog) return;
            Plugin.Log?.LogInfo(
                $"IKMA FOCUS: focus {(hasFocus ? "gained" : "lost")}, {SpeechState()}.");
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Perf.NoteFocusLost();
            if (!VerboseFocusLog) return;
            Plugin.Log?.LogInfo(
                $"IKMA FOCUS: application {(paused ? "paused" : "resumed")}, {SpeechState()}.");
        }

        // ======================================================================
        // H IS NEVER ALLOWED TO DO NOTHING. (0.7.266.)
        //
        // Zamar, after losing every key but Escape on the way to the Snow Line
        // map: "the H key should be on the same level as escape, there should
        // never be a situation where pressing the H key does not read out the
        // current screen's context and controls."
        //
        // Escape earns that by sitting above every layer and returning early.
        // H cannot: twenty-odd screens bind it to their own help, and a global
        // handler in front of them would answer for all of them with the wrong
        // line. So this is a WATCHDOG instead of a handler — it runs a few
        // frames AFTER the press and only speaks if nothing else did.
        //
        // "Nothing else did" is measured at the speech choke point rather than
        // by listing the handlers that might have answered. Any line at all
        // reaching CardReader.Speak counts as an answer. A queued combat result
        // draining at that moment could mask a genuinely dead H, which is why
        // the miss is logged either way — the log says whether the fallback
        // fired, so a masked one still shows up as an H with no help line.
        //
        // The fallback prefers a help line Zamar has already approved: if a map
        // or a battle is live, it speaks that screen's own help. The last-resort
        // sentence is PROVISIONAL and needs his words.
        // ======================================================================
        private const int HELP_WATCHDOG_FRAMES = 12;
        private static int  _helpPressedFrame = -1;
        private static bool _helpAnswered;

        /// <summary>
        /// Called from CardReader.Speak for every line. Any speech at all
        /// counts as H having been answered.
        /// </summary>
        internal static void NoteSomethingSpoken()
        {
            if (_helpPressedFrame >= 0) _helpAnswered = true;
        }

        private void HelpWatchdog()
        {
            // Session 32: while a death card is being named, H is a letter in
            // the name, not a help request. Disarm so no fallback help fires.
            if (DeathCardNameReader.Active)
            {
                _helpPressedFrame = -1;
                _helpAnswered = false;
                return;
            }

            try
            {
                if (_helpPressedFrame >= 0 &&
                    Time.frameCount - _helpPressedFrame >= HELP_WATCHDOG_FRAMES)
                {
                    bool answered = _helpAnswered;
                    _helpPressedFrame = -1;
                    _helpAnswered = false;

                    if (!answered)
                    {
                        // Session 46 (0.7.451): H while a character waits on
                        // Space, on a screen with no reader of its own, said
                        // "Nothing here can be read yet." There IS something
                        // to do, and the line for it exists: the one every
                        // other locked key gets.
                        bool characterWaiting = false;
                        try { characterWaiting = DialogueAdvancer.ConversationHolding(); } catch { }
                        if (characterWaiting)
                        {
                            Plugin.Log?.LogInfo(
                                "IKMA HELP: H reached no reader during a conversation - the conversation line answers it.");
                            Speech.Browse(Vocabulary.ConversationInProgress);
                        }
                        else
                        if (IsBattleActive())
                        {
                            Plugin.Log?.LogWarning(
                                "IKMA HELP: H reached no reader — falling back to the battle help.");
                            SpeakHelp(HelpContext.Battle);
                        }
                        else if (MapReader.MapAvailable())
                        {
                            Plugin.Log?.LogWarning(
                                "IKMA HELP: H reached no reader — falling back to the map help.");
                            SpeakHelp(HelpContext.Map);
                        }
                        else
                        {
                            Plugin.Log?.LogWarning(
                                "IKMA HELP: H reached no reader and no screen claimed it.");
                            Speech.Browse(Vocabulary.Hotkeys.NothingHereCanBe);
                        }
                    }
                }

                if (KeyIn.Down(KeyCode.H))
                {
                    _helpPressedFrame = Time.frameCount;
                    _helpAnswered = false;
                }
            }
            catch { _helpPressedFrame = -1; _helpAnswered = false; }
        }

        // Session 36: the dev autopilot reads which hand card IKMA is on.
        internal static HotkeyManager Current;
        internal int HandIndexNow => _handIndex;

        private void Update()
        {
            Current = this;
            // 0.7.338 — the hitch watch times ALL of this method, including
            // the node-screen branch that returns before UpdateInner's own
            // timer starts. See Perf.NoteFrame.
            long tAll = Perf.Now();
            try { UpdateOuter(); }
            finally { Perf.NoteFrame(Perf.MsSince(tAll)); }
        }

        /// <summary>
        /// Session 34, Zamar: on the options pages, an RB tap goes to the next
        /// page right and an LB tap to the next page left, wrapping. True if a
        /// page was switched. The page is asked of the screen, not remembered.
        /// </summary>
        private static bool PadPageFlip()
        {
            int step = KeyIn.ShoulderTap;
            if (step == 0) return false;
            int? cur = OptionsReader.CurrentPageNow();
            if (!cur.HasValue) return false;
            int count = OptionsReader.PAGE_COUNT;
            int next = ((cur.Value - 1 + step + count) % count) + 1;
            bool switched = PauseMenuReader.SwitchOptionsPage(next);
            // Session 34, Zamar: the page flip gets the browse tap, LB on the
            // left motor, RB on the right.
            if (switched) { try { Rumble.Tap(left: step < 0); } catch { } }
            return switched;
        }

        private void UpdateOuter()
        {
            // H is armed and checked before anything can return early. The
            // check itself speaks nothing unless a previous press went
            // unanswered for HELP_WATCHDOG_FRAMES.
            HelpWatchdog();

            // 0.7.341 — sentences held for a hand arrival line; says any that
            // no line claimed. Before every early return. See HandFollowUps.
            HandFollowUps.Tick();

            // Session 32 - the update question's two keys (F9 yes, F10 no)
            // and its title-screen idle repeat. Returns at once unless a
            // question is waiting; consumes the frame only on F9 or F10,
            // which nothing else in IKMA or the game uses. See AutoUpdate.cs.
            if (AutoUpdate.Tick()) return;

            // ==================================================================
            // SHIFT+L — WRITE THE LOG TO THE DESKTOP. (0.7.306.)
            //
            // For the v0.5 beta. Zamar: "let's create a one button for them to
            // be able to print that entire log that they can just drag into
            // discord as an attachment for me."
            //
            // CHECKED BEFORE EVERY EARLY RETURN IN THIS METHOD, deliberately —
            // the moment a player most needs it is the moment something has
            // gone wrong, which is exactly when a context-scoped key would be
            // unreachable. It changes no game state and reads one file, so it
            // is safe anywhere.
            //
            // A CHORD, NOT A BARE KEY: a stray L mid-combat would write a file
            // and talk over the board. L is free; the chord is only to stop an
            // accident.
            // ==================================================================
            // Session 32: not while a death card is being named - Shift+L is
            // a capital L there, and it belongs to the name.
            // Session 40: nor while Mod Settings is waiting for a new
            // controller button - the press that saves the log (LB + RB +
            // View) is then a button being offered, not the command.
            if (!DeathCardNameReader.Active &&
                !ModSettingsMenu.Listening &&
                KeyIn.Down(KeyCode.L) &&
                (KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift)))
            {
                string why;
                string written = LogExport.Export(out why);
                Speech.Confirm(written != null
                    ? Vocabulary.LogExported(written)
                    : Vocabulary.LogExportFailed(why));
                return;
            }

            // 0.7.209 — load the game's own gamepad layout onto any pad Rewired
            // has assigned and IKMA has not yet seen. Usually zero work.
            GamepadSupport.Tick();
            // Session 34 - read the pad and apply Zamar's controller map, once
            // per frame (KeyIn.Tick is a no-op after the first call). Every
            // key check in IKMA goes through KeyIn, so the pad works on every
            // screen from here.
            KeyIn.Tick();
            Rumble.TickVoice();   // Session 34 - rumble follows the voice sound
            SoftlockWatchdog.Tick();   // Session 34 - releases a reader the game has left behind
            VideoNarrator.Tick();   // 0.7.210 — footage subtitles, by time.

#if IKMA_DEV
            VideoTimecode.Tick();   // 0.7.269 — the authoring readout's string.
#endif

            // THE GAME ANSWERS WHETHER IT IS IN THE FOREGROUND, every frame.
            // (0.7.117.) Pushed down rather than remembered from a pause event:
            // Unity APIs are main-thread only, so the worker cannot ask, and a
            // hold released only by an event that might not fire is exactly the
            // stranded-state bug this project has already paid for twice.
            // SetBackgroundHold returns immediately when nothing changed.
#if IKMA_DEV
            // 0.7.456: the driver's silent run keeps the pump going in the
            // background (SpeechPump.DevSilentRun).
            SpeechPump.SetBackgroundHold(!Application.isFocused && !SpeechPump.DevSilentRun);
#else
            SpeechPump.SetBackgroundHold(!Application.isFocused);
#endif

            // THE REVIEW HISTORY (Session 35, MASTER_PLAN M9). Ctrl+Up/Down
            // anywhere, ahead of every reader, and the frame ends here so no
            // reader also takes the arrow. ReviewHistory.cs.
            // Session 40: not while Mod Settings is waiting for a new
            // controller button; R3 is then a button being offered.
            if (!ModSettingsMenu.Listening) SilenceKey.Tick();   // Session 36 - Ctrl tap / R3, M9

            // Session 36: a bell pressed during a draw rings once the card
            // has landed (a beat after "Drew X."), through the normal E path.
            if (_bellHeldForDraw)
            {
                if (DrawInFlight || PlayIsLocked()) _bellDrawSeenAt = Time.unscaledTime;
                if (Time.unscaledTime - _bellHeldAt > 6f) _bellHeldForDraw = false;   // never strand it
                else if (!DrawInFlight && Time.unscaledTime - _bellDrawSeenAt > 0.3f)
                {
                    _bellHeldForDraw = false;
                    KeyIn.Synthesize(KeyCode.E);
                    Plugin.Log?.LogInfo("IKMA BELL: the held bell is rung now that the card has landed.");
                }
            }
            // THE MOD SETTINGS MENU (Session 37, M9) - Ctrl+M / LB+Start from
            // anywhere; every key is its own while it is open. ModSettingsMenu.cs.
            if (!DeathCardNameReader.Active && ModSettingsMenu.HandleKeys()) return;
            // THE HELP LIST (Session 37, M9) - F1 / LB+View. HelpList.cs.
            if (!DeathCardNameReader.Active && HelpList.HandleKeys()) return;
            if (!DeathCardNameReader.Active && ReviewHistory.HandleKeys()) return;

            // Session 37, note D8 - Zamar: Space pressed after a character has
            // started talking but before IKMA has said the line (it is held for
            // the voice sting) waits for the dialogue: no screen read cutting in
            // ahead of it, and no advance past a line nobody has heard yet.
            if (KeyIn.Down(KeyCode.Space) && !DeathCardNameReader.Active && CombatAnnouncer.DialoguePending)
            {
                Plugin.Log?.LogInfo("IKMA DIALOGUE: Space waits - a character's line is still to be spoken.");
                return;
            }

            // ------------------------------------------------------------------
            // ESCAPE — THE PAUSE MENU, AND IT IS ABOVE EVERYTHING. (Session 17.)
            //
            // Zamar: "Escape should be globally available, I dont think theres
            // supposed to be a single screen you cant hit Escape and get that
            // menu." He is right, and the reason it did nothing is IKMA's:
            // Plugin disables every Rewired KEYBOARD map so the game's own
            // bindings cannot fight the mod's, and Escape went with them. The
            // key was not swallowed by a layer — it never reached the game at
            // all.
            //
            // Button.Menu is in the enum the mod already logs at startup, so
            // this presses the game's own pause input rather than writing
            // PauseMenu.Paused. Setting that flag would be the write-a-state-
            // flag mistake: the menu does work on the way in and on the way out
            // that a bool does not describe.
            //
            // ABOVE EVERY LAYER, and it must stay there. Pause has to work from
            // a battle, the map, standing in the cabin, the deck view, the
            // rulebook and the card choice screen — every one of those returns
            // early below this point. Closing the menu is the same button, so
            // the game handles the toggle and IKMA does not track it.
            if (KeyIn.Down(KeyCode.Escape))
            {
                // THE CREDITS LEAVE THE SAME WAY BACKSPACE DOES. (0.7.236.)
                // Escape already exited them — CreditsDisplayer watches the raw
                // key — but the queued roll kept talking over the title screen
                // afterwards. Routed through CreditsReader so the two keys
                // cannot drift apart.
                if (CreditsReader.Active) { CreditsReader.Leave(); return; }

                // ESCAPE ON INSCRYPTION'S OWN TITLE SCREEN ANSWERS, IT DOES
                // NOT OPEN. (0.7.234.)
                //
                // Zamar, 0.7.232: "Disable the Escape key on the main menu for
                // now. I don't want the footage screen accessible in v.5" —
                // then 0.7.234: "dont disable the escape key, have it instead
                // not open the menu and read 'Video logs not yet available.
                // Coming soon!'"
                //
                // 0.7.233 swallowed it, and his log shows what that feels like:
                // twenty-odd "Escape swallowed" lines in a row, a player
                // pressing a key that gives back nothing at all. A key with no
                // destination yet is not the same as a key with no meaning —
                // this one has a destination, it just is not built, and saying
                // so is the difference between a door that is locked and a wall
                // that might be a door.
                //
                // OVER THE PANEL, ESCAPE LEAVES THE PANEL. His other ask this
                // build: "Escape should also exit the Options menu screen here,
                // in addition to backspace." Same two lines Backspace runs, so
                // the two keys cannot drift apart.
                if (TitleScreenReader.Active)
                {
                    if (TitleScreenReader.OptionsOpen)
                    {
                        CombatAnnouncer.DropCommentary("left the options menu");
                        Speech.Silence();

                        OptionsReader.Close();
                        TitleScreenReader.ReAnnounce();
                        return;
                    }

                    Plugin.Log?.LogInfo("IKMA TITLE: Escape — the footage menu is not built for v0.5.");
                    Speech.Browse(Vocabulary.VideoLogsNotAvailable());
                    return;
                }

                // The boot screen takes Enter and Space and nothing else, by
                // his instruction: "no other buttons should exist here though."
                // The title card behind it takes ANY key, which is the game's
                // own WaitUntil and needs nothing from IKMA — but Escape would
                // reach the footage menu through the pause injection below, and
                // that screen is out of scope for v0.5 exactly as it is on the
                // main menu. (0.7.233.)
                if (BootScreenReader.Active || BootScreenReader.TitleCardShowing) return;

                // Escape out of the options panel is a LEAVE, same as Backspace,
                // so it cuts the screen's speech the same way. Anywhere else
                // Escape opens or closes the pause menu and the new screen
                // announces itself, which already interrupts.
                if (PauseMenuReader.Active && PauseMenuReader.OptionsOpen)
                {
                    CombatAnnouncer.DropCommentary("left the options menu");
                    Speech.Silence();
                }

                Plugin.Log?.LogInfo("IKMA PAUSE: Escape pressed — injecting Menu.");
                InjectButton("Menu");
                return;
            }

            // THE PAUSE MENU OWNS THE KEYBOARD WHILE IT IS UP, and it sits
            // directly under Escape so EVERY other screen layer is below it.
            // The menu covers a battle, the map, the cabin, an open rulebook
            // and a map event alike — any key falling through would act on a
            // screen the player cannot see. Zamar, 0.7.102: "I went to the
            // sacrifice stone, pressed R to open rulebook, then pressed Escape
            // to open the pause menu, and it remained in the rulebook context
            // for the arrows."
            //
            // MOVED ABOVE THE MAP EVENT LAYER IN 0.7.102. Zamar: "pressing
            // escape on the Sacrifice Screen did not have the arrow keys switch
            // context. Need to make sure that always switches over, from any
            // screen, when the pause menu event is fired." The node layer had
            // been written above this one and kept the arrows while the menu was
            // up — so the game was paused and the keyboard was not.
            //
            // This is the third time one screen has been stacked over another
            // that covers it — the rulebook twice, now the pause menu — so the
            // rule is worth stating plainly: A LAYER THAT COVERS THE SCREEN GOES
            // ABOVE EVERY LAYER IT COVERS, and a new screen reader is added
            // BELOW both of them unless it covers them too. Escape stays at the
            // very top, because pausing has to work from everywhere.
            if (PauseMenuReader.Active)
            {
                // ------------------------------------------------------------
                // THE OPTIONS PANEL IS ITS OWN SCREEN, above the pause menu's
                // own cards. (Session 17, his ask: "have the context switch
                // over to it. I want arrow keys, enter, backspace, space, and H
                // key working in this menu.")
                //
                // Its presence is asked of the screen every frame rather than
                // remembered — the tabs only exist while the panel is up. That
                // is the cached-state rule, and forgetting it is what left the
                // node reader swallowing every key one build ago.
                // ------------------------------------------------------------
                if (PauseMenuReader.OptionsOpen)
                {
                    if (!OptionsReader.Announced) OptionsReader.Announce();

                    // HOLDING A SETTING: the arrows belong to it, not to the
                    // list. His spec — Enter takes hold, Backspace lets go, and
                    // both directions of both axes change the value so neither
                    // hand has to remember which pair does what.
                    if (OptionsReader.Adjusting)
                    {
                        if (KeyIn.Down(KeyCode.LeftArrow) || KeyIn.Down(KeyCode.UpArrow))
                        { OptionsReader.Adjust(-1); return; }

                        if (KeyIn.Down(KeyCode.RightArrow) || KeyIn.Down(KeyCode.DownArrow))
                        { OptionsReader.Adjust(1);  return; }

                        if (KeyIn.Down(KeyCode.Backspace)) { OptionsReader.StopAdjusting(); return; }
                        if (KeyIn.Down(KeyCode.Space))     { OptionsReader.SpeakPosition();  return; }
                        if (KeyIn.Down(KeyCode.H))         { PauseMenuReader.SpeakHelp();    return; }

                        // Everything else is swallowed: a key falling through
                        // would act on the list while a setting is held.
                        return;
                    }

                    // BROWSING the page.
                    if (KeyIn.Down(KeyCode.UpArrow) || KeyIn.Down(KeyCode.LeftArrow))
                    { OptionsReader.Browse(-1); return; }

                    if (KeyIn.Down(KeyCode.DownArrow) || KeyIn.Down(KeyCode.RightArrow))
                    { OptionsReader.Browse(1);  return; }

                    if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
                    { OptionsReader.Activate(); return; }

                    if (KeyIn.Down(KeyCode.Space))     { OptionsReader.SpeakPosition(); return; }
                    if (KeyIn.Down(KeyCode.H))         { PauseMenuReader.SpeakHelp();   return; }

                    if (KeyIn.Down(KeyCode.Backspace))
                    {
                        // CUT WHATEVER THE OPTIONS SCREEN WAS STILL SAYING.
                        // (0.7.107.) Zamar: "pressing Escape or Backspace from
                        // in the options menu should stomp and end any speak
                        // from that menu."
                        //
                        // A setting's name read half a second ago is describing
                        // a screen the player has just left, and the pause
                        // menu's own line queues BEHIND it — so leaving felt
                        // slow and the two screens talked over each other. The
                        // queue is cleared before the new screen names itself.
                        CombatAnnouncer.DropCommentary("left the options menu");
                        Speech.Silence();

                        OptionsReader.Close();
                        PauseMenuReader.ReAnnounce();
                        return;
                    }

                    // THREE PAGES, not four. Zamar, 0.7.97: "there's only 3
                    // pages. So 1/2/3, no 4 needed."
                    if (KeyIn.Down(KeyCode.Alpha1) && PauseMenuReader.SwitchOptionsPage(1)) { OptionsReader.Reset(); return; }
                    if (KeyIn.Down(KeyCode.Alpha2) && PauseMenuReader.SwitchOptionsPage(2)) { OptionsReader.Reset(); return; }
                    if (KeyIn.Down(KeyCode.Alpha3) && PauseMenuReader.SwitchOptionsPage(3)) { OptionsReader.Reset(); return; }
                    if (PadPageFlip()) { OptionsReader.Reset(); return; }

                    return;
                }
                else if (OptionsReader.Announced)
                {
                    // The panel went away — by Backspace, by the mouse, or by
                    // the game. Drop the state so re-entering announces again.
                    OptionsReader.Reset();
                }

                if (KeyIn.Down(KeyCode.UpArrow)   || KeyIn.Down(KeyCode.LeftArrow))
                { PauseMenuReader.Browse(-1); return; }

                if (KeyIn.Down(KeyCode.DownArrow) || KeyIn.Down(KeyCode.RightArrow))
                { PauseMenuReader.Browse(1);  return; }

                if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
                { PauseMenuReader.Activate(); return; }

                // Options pages, hovered before clicked so the highlight and
                // the sound match the change. Falls through when the options
                // panel is not up.
                if (KeyIn.Down(KeyCode.Alpha1) && PauseMenuReader.SwitchOptionsPage(1)) return;
                if (KeyIn.Down(KeyCode.Alpha2) && PauseMenuReader.SwitchOptionsPage(2)) return;
                if (KeyIn.Down(KeyCode.Alpha3) && PauseMenuReader.SwitchOptionsPage(3)) return;
                if (PadPageFlip()) return;

                if (KeyIn.Down(KeyCode.Space))     { PauseMenuReader.SpeakPosition(); return; }
                if (KeyIn.Down(KeyCode.H))         { PauseMenuReader.SpeakHelp();     return; }
                if (KeyIn.Down(KeyCode.Backspace)) { PauseMenuReader.Close();         return; }

                return;
            }

            // ------------------------------------------------------------------
            // NAMING THE DEATH CARD. (Session 32.) Every letter, number and
            // Space the player types here goes into the card's name, so no
            // other IKMA key may answer: the reader owns the frame and this
            // returns. Below Escape and the pause menu on purpose - pausing
            // must still work, and the game's own typing stops while paused.
            // ------------------------------------------------------------------
            if (DeathCardNameReader.Tick()) return;

            // ------------------------------------------------------------------
            // RULEBOOK. Above EVERY reader, not just the in-scene ones.
            // (Moved up in Session 16.)
            //
            // It used to sit below the deck view and the card choice screen, and
            // Zamar found what that means: "Opening the rule book with R while
            // in the deck view does not change the arrow keys focus. The focus
            // is still on the deck so I cant flip the pages. This also makes it
            // so pressing R just re-opens the rulebook, not closes it."
            //
            // Both symptoms are one cause. The book opened, but the layer above
            // it returned first and kept the arrows, M, Backspace and R — so the
            // book was open and unreachable, and R hit the open-the-book binding
            // again instead of the close-the-book one.
            //
            // The rulebook is a MODAL OVERLAY. It can be opened from the map,
            // the deck view, the card choice screen, a map event screen and a
            // battle, so it belongs above all of them rather than in the middle
            // of the stack. StillOpen() also catches the book being closed by
            // the game rather than by us, so this layer cannot strand itself.
            //
            // MOVED AGAIN IN SESSION 17, above the map-event layer, for exactly
            // the same failure one floor up. Zamar, on the sacrifice stone: "R
            // opens rulebook but then does not close it. Shift + R also didnt
            // work... Backspace didnt close the book." The node screen layer had
            // been added ABOVE this one and swallowed R and Backspace, so the
            // book opened into a screen that would not let go of the keyboard.
            //
            // THE RULE THIS KEEPS RE-LEARNING: a modal overlay does not sit at a
            // fixed height in the stack, it sits at the TOP. Every new screen
            // layer added below it is safe; any layer added above it re-breaks
            // the book. Escape stays above even this, because pausing has to
            // work from everywhere.
            // ------------------------------------------------------------------
            if (RulebookReader.IsOpen)
            {
                if (RulebookReader.StillOpen())
                {
                    // Owns the deferred page render — narration runs at keyboard
                    // speed and the book catches up once the player stops.
                    RulebookReader.Tick();
                    HandleRulebookKeys();
                    return;
                }
            }

            // ------------------------------------------------------------------
            // Shift+R — explain the sigils in the line you just heard.
            // (Session 13, Zamar's call. Moved up in 0.7.103.)
            //
            // The whole point is that it works where the rulebook cannot be
            // opened: the unlock screens, the starter deck list, a card reward,
            // mid-sacrifice. A blind player learns every sigil by ear and had no
            // way to look one up without leaving what they were doing. Plain R
            // still opens the book where that is bound; this is the shifted key
            // and never shadows it.
            //
            // Its own comment further down already claimed it was above every
            // layer, and it was not: the map-event layer answered plain R by
            // opening the rulebook without ever asking whether Shift was held,
            // so Shift+R on the sacrifice stone opened the book instead of
            // explaining the card just read. Zamar: "Shift + R is not working
            // in sacrifice stone screen. Very important to."
            //
            // A CHECK THAT DEPENDS ON A MODIFIER CANNOT LIVE BELOW A LAYER THAT
            // CLAIMS THE BARE KEY. Either it sits above that layer, or every
            // such layer has to remember to test the modifier — and one of them
            // will not. Placed under the rulebook, which owns its own keys while
            // the book is up, and over everything else.
            // ------------------------------------------------------------------
            if (KeyIn.Down(KeyCode.R) &&
                (KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift)))
            {
                ExplainLastSigils();
                return;
            }

            // ------------------------------------------------------------------
            // BUILDING THE DEATH CARD. (Session 32.) Only while the game has
            // the choice cards turned on - before that Leshy is talking and the
            // layers below answer as usual. Under the rulebook and Shift+R, so
            // both still work here.
            // ------------------------------------------------------------------
            if (DeathCardChoiceReader.Tick()) return;


            // ------------------------------------------------------------------
            // THE GAME HOLDING ON A DIALOGUE LINE OUTRANKS EVERY READER.
            // (Moved here at 0.7.122, and the move is the fix.)
            //
            // 0.7.121 added Space as an advance key and it did not work at the
            // campfire, because this check sat BELOW the map-event-screen block
            // and NodeScreenReader claimed Space first for its own position
            // read. Zamar was stuck on Leshy's line with the one key that should
            // have moved it being eaten by the screen behind him.
            //
            // The ordering is not a preference. While the game is waiting on
            // this line, nothing else on the screen can proceed — so no reader
            // below has anything to answer for. Advancing IS the screen.
            //
            // TryAdvance asks the game and answers false everywhere it does not
            // apply, so Enter still plays cards and Space still means "you are
            // in an encounter", "describe what is ahead" and "read the position"
            // in every other state. It claims the keys only where they had no
            // meaning at all.
            //
            // Deliberately not announced anywhere: it is not a new binding
            // competing with the others, it is Enter's twin in a state where
            // waiting is the only thing to do.
            // ------------------------------------------------------------------
            DialogueAdvancer.Tick();
            // The Prospector's board wipe speaks on a debounce off the last
            // pickaxe strike rather than from a hook that waits on the player.
            // (0.7.173.) See ProspectorNarrator.Tick.
            ProspectorNarrator.Tick();
            BossNarrator.Tick();
            TickViewRestore();

            // SPACE ONLY. ENTER NO LONGER ADVANCES DIALOGUE. (0.7.125.)
            //
            // Zamar's reasoning, and it is a safety argument rather than a
            // preference: "I want Spacebar to advance all dialogue in this mod,
            // since Enter is a selection input. I want to avoid situations where
            // people mash Enter to get through speech and then accidentally
            // select something."
            //
            // He is right, and the risk is specific to how a blind player gets
            // through a long speech: you press the key repeatedly because you
            // cannot see how many lines are left. Every one of those presses
            // that lands after the last line is a SELECTION — a card played, a
            // node travelled to, a ritual begun. Enter is the one key in this
            // mod that can spend something.
            //
            // THE DIVISION IS NOW CLEAN AND WORTH KEEPING: Space is information
            // and never commits anything; Enter commits and never doubles as a
            // "keep going" key. Advancing dialogue is the one place Space acts
            // at all, and the worst a stray press can do there is skip a line
            // the player has already heard.
            // SPACE BELONGS TO THE TRADE SCREEN WHILE IT IS LIVE. (0.7.308.)
            //
            // Zamar: "space did nothing and I needed it to." His 0.7.301 log
            // says why — the Trader's "TRADE FOR WHAT YOU CAN" line stays on
            // screen for the whole phase:
            //
            //   DIALOGUE: Space -> continuePressed set, speech cut.
            //   DIALOGUE: same line one second later — continuePressed did NOT
            //             advance it. Treating the text as a standing prompt.
            //
            // Five times in one phase. Space was reaching the dialogue
            // advancer, cutting whatever was being read, and advancing
            // nothing, so it never reached TradeReader.SpeakPosition below.
            // A standing prompt is not a line waiting to be advanced, and the
            // screen underneath it is the thing the player is using.
            if (TradeReader.Active && KeyIn.Down(KeyCode.Space))
            {
                TradeReader.SpeakPosition();
                return;
            }

            if (KeyIn.Down(KeyCode.Space) && DialogueAdvancer.TryAdvance())
                return;

            // ENTER IS SWALLOWED WHILE A CHARACTER IS TALKING. (0.7.129.)
            //
            // Zamar asked for the "Conversation in progress" line on Enter as
            // well as the arrows — and answering it here does more than answer
            // it. Enter is the only key in this mod that can SPEND something,
            // and the reason he moved dialogue onto Space was the player who
            // mashes a key to get through a speech and lands a selection with
            // the last press.
            //
            // Telling him the key is wrong is the small half. CONSUMING it is
            // the half that closes the hole: a stray Enter during dialogue can
            // no longer reach card play, a map node or a confirm stone at all.
            // The two changes together make that whole class of accident
            // impossible rather than merely discouraged.
            //
            // Deliberately AFTER the Space check, so the advance itself is never
            // delayed by this, and gated on the game genuinely waiting — Enter
            // is untouched everywhere else.
            // AND SO ARE THE ARROWS. (0.7.130.)
            //
            // Zamar: "When a conversation is happening the only things that
            // should work are the H key, and Space key. (And the arrows + Enter
            // key telling you to use the space key instead). That should be
            // universally true I believe."
            //
            // He is right and it is a better rule than the one it replaces. The
            // "Conversation in progress" answer only appeared when the screen
            // behind happened to have nothing to browse — so on a screen that
            // DID have options, the arrows quietly moved a selection while a
            // character was mid-sentence. Whether the keys work depended on what
            // was behind the dialogue, which is not something a player can know.
            //
            // Now it depends on one thing: is the game holding on a line. H is
            // deliberately still live, because "what can I do here" is the one
            // question that is never premature.
            // ConversationHolding, NOT AwaitingInput. (0.7.134.) The text being
            // on screen is not the question — whether the game is still waiting
            // on a press is. See DialogueAdvancer.ConversationHolding.
            bool talking = false;
            try { talking = DialogueAdvancer.ConversationHolding(); } catch { }

            // AND IT LETS GO THE MOMENT THERE IS SOMETHING TO DO. (0.7.132.)
            //
            // Zamar: "After Leshy said 'or pull away?' the conversation ended
            // but the arrow keys and enter did not resume working."
            //
            // AwaitingInput was not latched and the check was not wrong — the
            // TEXT IS STILL ON SCREEN. "PUSH YOUR LUCK? OR PULL AWAY?" stays up
            // as a standing question while the screen becomes interactive, so
            // Displaying is true and the mode is still Input. The game is no
            // longer waiting on the LINE; it is waiting on the CHOICE.
            //
            // So "a conversation is in progress" cannot be answered by the text
            // alone. The honest question is whether there is anything else the
            // player could be doing: if the screen has parts to browse or cards
            // laid out, the keys belong to it, dialogue on screen or not.
            //
            // This keeps the whole point of the lockout — during Leshy's opening
            // there IS nothing to act on, so a stray Enter still cannot reach a
            // selection — while never holding the keyboard hostage to a line the
            // game has finished with.
            // THE "SOMETHING TO DO" ESCAPE HATCH IS GONE. (0.7.134.)
            //
            // 0.7.132 unlocked the keys whenever the screen had parts or cards,
            // and that is exactly what let him move a selection while Leshy was
            // still speaking. The screen having options says nothing about
            // whether the game is ready for him to use them — at the campfire
            // the cards are laid out and live while the conversation runs.
            //
            // It was a workaround for the wrong question, and now that the right
            // one exists it has to go rather than stack on top.
            // Session 34, Zamar: the rulebook does not open during a
            // conversation either ("I believe that is true in Vanilla"), so
            // plain R joins the swallowed keys. Shift+R, which only speaks,
            // is answered above and is unaffected.
            if (talking
                && (KeyIn.Down(KeyCode.Return)    || KeyIn.Down(KeyCode.KeypadEnter)
                 || KeyIn.Down(KeyCode.UpArrow)   || KeyIn.Down(KeyCode.DownArrow)
                 || KeyIn.Down(KeyCode.LeftArrow) || KeyIn.Down(KeyCode.RightArrow)
                 || KeyIn.Down(KeyCode.R)))
            {
                Plugin.Log?.LogInfo("IKMA DIALOGUE: key swallowed — a conversation is in progress.");
                Speech.Browse(Vocabulary.ConversationInProgress);
                return;
            }

            // SESSION 47 - THE READING KEYS ARE LOCKED TOO. (0.7.455.)
            //
            // Zamar, Session 18: "When a conversation is happening the only
            // things that should work are the H key, and Space key." The list
            // above only ever held Enter, the arrows and R; in a battle C, G,
            // I and A all answered while Leshy's line was holding, and I
            // opened the item menu. Escape, Backspace, the Control chords and
            // IKMA's own L, M and Y keys are left as they were.
            if (talking && ConversationLockedKeyDown())
            {
                Plugin.Log?.LogInfo("IKMA DIALOGUE: reading key swallowed — a conversation is in progress.");
                Speech.Browse(Vocabulary.ConversationInProgress);
                return;
            }

            // A MAP EVENT SCREEN OWNS THE KEYBOARD WHILE IT IS UP.
            //
            // Above the map layer, which is what Zamar hit: "The H key on
            // sacrifice stone still reads Map Controls." The node manager does
            // not care that a sequencer has taken over, so MapAvailable() stayed
            // true and the map answered for a screen that had replaced it —
            // the same shape as the 0.7.53 standing softlock.
            //
            // Below the pause menu, which covers this screen entirely — see the
            // layering note there.
            // ------------------------------------------------------------------
            // THE TRADER'S TRADE OWNS THE ARROWS WHILE IT IS UP. (0.7.216.)
            //
            // Above the node screens because it is not one — it happens inside
            // a battle, with the bell disabled and the game refusing to
            // continue until the player is out of pelts or the board and queue
            // are empty. Before this, a keyboard player could reach the
            // Trapper's phase two and had no way to finish it.
            //
            // AND IT DOES NOT SWALLOW THE REST. Every other reader here ends
            // with a bare `return` that eats the keypress; this one handles
            // only the arrows, Enter, Space and H and falls through, because
            // a player mid-trade still wants G, B and A to weigh up what they
            // are buying. The screen IS a decision — taking away the keys that
            // inform it would be its own kind of blocker.
            // ------------------------------------------------------------------
            // ------------------------------------------------------------------
            // CHOOSING A CARD FROM YOUR DECK MID-BATTLE. (0.7.339.) Hoarder,
            // Magpie's Glass and the Magpie boon all lay the deck out and wait.
            // Same shape as the trade below: arrows, Enter, Space and H are
            // taken, the board-reading keys fall through. See DeckPickReader.
            // ------------------------------------------------------------------
            DeckPickReader.Tick(Time.deltaTime);
            if (DeckPickReader.Active)
            {
                if (KeyIn.Down(KeyCode.UpArrow)   || KeyIn.Down(KeyCode.LeftArrow))
                { DeckPickReader.Browse(-1); return; }

                if (KeyIn.Down(KeyCode.DownArrow) || KeyIn.Down(KeyCode.RightArrow))
                { DeckPickReader.Browse(1);  return; }

                if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
                { DeckPickReader.Choose(); return; }

                if (KeyIn.Down(KeyCode.Space)) { DeckPickReader.SpeakPosition(); return; }
                if (KeyIn.Down(KeyCode.H))     { DeckPickReader.SpeakHelp();     return; }

                // Tab would hunt the hand for a playable card; the hand is
                // not what the game is waiting on.
                if (KeyIn.Down(KeyCode.Tab)) { DeckPickReader.SpeakHelp(); return; }

                // 0.7.357 — NOTHING THAT ACTS WHILE THE DECK IS OPEN. Zamar:
                // "Items shouldn't be available (the I key) while resolving a
                // Hoarder search. Make sure other keys that could break that
                // sequence aren't either." Items (I, Shift+1/2/3), the bell (E),
                // the draw piles (D, S), the number keys, Backspace and the
                // rulebook (plain R) all start something of their own. Each
                // answers with the screen's help instead of a dead key. The
                // board-reading keys (and Shift+R, Shift+I) still fall through.
                if (_itemMode) ExitItemMode(announce: false);
                bool shiftHeld = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);
                if ((KeyIn.Down(KeyCode.I) && !shiftHeld)
                 || KeyIn.Down(KeyCode.E) || KeyIn.Down(KeyCode.D) || KeyIn.Down(KeyCode.S)
                 || KeyIn.Down(KeyCode.Backspace) || (KeyIn.Down(KeyCode.R) && !shiftHeld)
                 || KeyIn.Down(KeyCode.Alpha1) || KeyIn.Down(KeyCode.Alpha2)
                 || KeyIn.Down(KeyCode.Alpha3) || KeyIn.Down(KeyCode.Alpha4)
                 || KeyIn.Down(KeyCode.Keypad1) || KeyIn.Down(KeyCode.Keypad2)
                 || KeyIn.Down(KeyCode.Keypad3) || KeyIn.Down(KeyCode.Keypad4))
                {
                    Plugin.Log?.LogInfo("IKMA DECKPICK: an acting key was held back — the deck is open.");
                    DeckPickReader.SpeakHelp();
                    return;
                }
            }

            TradeReader.Tick(Time.deltaTime);
            if (TradeReader.Active)
            {
                if (KeyIn.Down(KeyCode.UpArrow)   || KeyIn.Down(KeyCode.LeftArrow))
                { TradeReader.Browse(-1); return; }

                if (KeyIn.Down(KeyCode.DownArrow) || KeyIn.Down(KeyCode.RightArrow))
                { TradeReader.Browse(1);  return; }

                if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
                { TradeReader.Select(); return; }

                if (KeyIn.Down(KeyCode.Space)) { TradeReader.SpeakPosition(); return; }
                if (KeyIn.Down(KeyCode.H))     { TradeReader.SpeakHelp();     return; }

                // Deliberately no bare return — see above.
            }

            // THE DECK OWNS THE KEYBOARD WHILE IT IS UP. (0.7.257.)
            //
            // Shift+Up at the Woodcarver puts the deck on screen without ending
            // the node, so both readers are live at once. Standing the node
            // block down lets the existing deck handling further down answer —
            // its arrows, its Space, its H, and its Backspace, which climbs back
            // to the origin NodeScreenReader.OpenDeckView handed it. One deck
            // reader, one set of keys, wherever the deck was opened from.
            // BACKSPACE OUT OF THE DECK IS OURS, WHEREVER THE DECK CAME FROM.
            // (0.7.258.)
            //
            // The deck reader owns everything else while it is up — that is
            // 0.7.257's rule and it stands. But its Backspace CLIMBS, and the
            // Woodcarver locks the view controller, so the game refused every
            // LookDown and the camera drifted to the bench. See
            // NodeScreenReader.CloseDeckView.
            if (NodeScreenReader.Active && MapReader.InDeckView &&
                NodeScreenReader.BackpackAvailable && !CardChoiceReader.Active)
            {
                bool deckShift = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);

                // Shift+Up brought the deck up, so Shift+Up puts it away —
                // Zamar's rule, 0.7.260. Shift is tested before the bare arrow
                // the deck reader browses with, as everywhere else in this file.
                if ((deckShift && KeyIn.Down(KeyCode.UpArrow)) ||
                    KeyIn.Down(KeyCode.Backspace))
                {
                    NodeScreenReader.CloseDeckView();
                    return;
                }
            }

            // A CARD CHOICE INSIDE A NODE SCREEN OWNS THE KEYS. (Session 33.)
            //
            // The Mycologists with no duplicates in the deck hand the player an
            // ordinary card choice: DuplicateMergeSequencer runs
            // CardSingleChoicesSequencer.CardSelectionSequence while its own node
            // screen is still up. This branch came first and ends in a bare
            // return, so the node reader browsed the three face-down cards by
            // their GameObject names - "Card (Raccoon)" - naming cards no
            // sighted player could see yet. While the game is running a card
            // choice, the card choice reader below answers every key instead.
            if (NodeScreenReader.Active && !MapReader.InDeckView && !CardChoiceReader.Active)
            {
                // Notice the deck appearing before any key is judged.
                NodeScreenReader.Tick();

                // 0.7.359 — THE CAMERA WATCHER TICKS HERE TOO. This branch ends
                // in a bare return, so UpdateInner, and the
                // MapReader.TickViewChange it runs "above every layer", was never
                // reached while a node screen owned the keyboard. Harmless while
                // no node screen climbed the Look ladder. The Trader's Shift+Up
                // does: the climb back down from the deck lands on
                // TradingTopDown, which is this branch, and without a tick here
                // the climb could never see itself arrive — it would stay
                // "walking" and press again on the map once the Trader closed.
                // One cached property read a frame; its other ticks are guarded
                // no-ops unless a walk or a survey is pending.
                MapReader.TickViewChange();

                // SHIFT IS TESTED FIRST. The bare-arrow browse below claims
                // UpArrow whether or not Shift is held, so a shifted binding
                // written underneath it can never be reached — the mistake that
                // cost 0.7.60 and is spelled out at the card choice screen.
                //
                // AND ONLY WHERE THE GAME OFFERS IT. (0.7.250.)
                // NodeScreenReader.DeckViewAvailable is the same set that
                // decides whether the help line mentions the key, so the mod
                // cannot advertise a key it does not bind or bind one it does
                // not advertise. Today that is the Woodcarver alone: its
                // sequencer handles View.MapDeckReview and the others lock it.
                bool nodeShift = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);

                if (nodeShift && KeyIn.Down(KeyCode.UpArrow))
                {
                    // 0.7.257 — the Woodcarver has its own route in, because
                    // ShowDeck's climb cannot start from a view that is not a
                    // rung. DeckViewAvailable stays for any screen that is.
                    if (NodeScreenReader.BackpackAvailable)      NodeScreenReader.OpenDeckView();
                    else if (NodeScreenReader.DeckViewAvailable) DeckViewReader.Open();
                    // 0.7.359 — a screen with a deck view the game has not
                    // unlocked yet (the Trader before its offers are down)
                    // answers the key rather than swallowing it.
                    else if (NodeScreenReader.DeckViewOffered)   Speech.Browse(Vocabulary.NodeScreens.DeckCannotBeReached);
                    return;
                }

                if (KeyIn.Down(KeyCode.UpArrow)   || KeyIn.Down(KeyCode.LeftArrow))
                { NodeScreenReader.Browse(-1); return; }

                if (KeyIn.Down(KeyCode.DownArrow) || KeyIn.Down(KeyCode.RightArrow))
                { NodeScreenReader.Browse(1);  return; }

                if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
                { NodeScreenReader.Activate(); return; }

                if (KeyIn.Down(KeyCode.Space)) { NodeScreenReader.SpeakPosition(); return; }
                if (KeyIn.Down(KeyCode.H))     { NodeScreenReader.SpeakHelp();     return; }
                if (KeyIn.Down(KeyCode.R))     { RulebookReader.Open();            return; }

                // 0.7.337 — I cycles the items you already carry, on the item
                // pickup screen only (his help line names it there). Same read
                // as I in the deck view. Swallowed elsewhere, like A.
                if (KeyIn.Down(KeyCode.I))
                {
                    if (NodeScreenReader.ItemPickupActive) BoardReader.ReadNextItem();
                    return;
                }

                // A OPENS THE BACKPACK. (0.7.257, moving off B at his ask.)
                // Gated on the same property that decides whether the help line
                // mentions it, so the key and the sentence cannot outlive each
                // other. Swallowed either way: a key that means something on one
                // screen must not fall through and come to mean something else
                // on another.
                if (KeyIn.Down(KeyCode.A))
                {
                    if (NodeScreenReader.BackpackAvailable) NodeScreenReader.OpenBackpackView();
                    else if (NodeScreenReader.TrapperActive) NodeScreenReader.TrapperSpeakTeeth();   // 0.7.330
                    return;
                }

                // Backspace climbs back down out of the backpack. It has no
                // other meaning on a node screen and is swallowed rather than
                // left to fall through to the map.
                if (KeyIn.Down(KeyCode.Backspace))
                {
                    if (NodeScreenReader.InBackpackView) NodeScreenReader.CloseBackpackView();
                    else if (NodeScreenReader.ReturnToBackpackFromTotem()) { }                        // Session 34
                    else if (NodeScreenReader.TrapperActive) NodeScreenReader.TrapperLeave();        // 0.7.330
                    return;
                }

                return;
            }

            // ------------------------------------------------------------------
            // Kaycee's Mod menus live in their own scene, so this check comes
            // before the Part1_Cabin gate. MenuReader only reports active when
            // the game has handed it a live, enabled screen.
            // ------------------------------------------------------------------
            // Session 13: Update runs every frame, so anything expensive in it
            // costs frame rate rather than one keypress — a different symptom
            // with a different feel. Reported only when a single frame's IKMA
            // work crosses the threshold, which should be never.
            long tFrame = Perf.Now();
            try { UpdateInner(); }
            finally { Perf.Report("Update frame", tFrame); }
        }

        private void UpdateInner()
        {
            // The mouse is switched off exactly while a menu owns the keyboard.
            // Ticked every frame rather than toggled on transitions, so it can
            // never be left off after the menu has gone.
            MenuReader.TickMouseSuppression();

            // A page turn on the card unlock screen keeps the same screen, so
            // nothing re-announces it. This settles the new cards and reads the
            // first one. (Session 14.)
            MenuReader.TickPageTurn(Time.deltaTime);

            // ------------------------------------------------------------------
            // DIALOGUE AND SURRENDER. (Session 15.) Above the menu gate and the
            // scene gate, because a character can talk over anything and the
            // offered hand is the one action that ends a fight.
            //
            // The prompt ticks unconditionally; DialogueAdvancer.AwaitingInput
            // asks the game and answers false everywhere it does not apply.
            //
            // TICKED ABOVE THE MAP-EVENT LAYER SINCE 0.7.122, so this second
            // call is gone. Ticking twice in one frame double-advanced the
            // prompt's own clock.
            // ------------------------------------------------------------------


            // 0.7.360 — Shift+E, his choice (was T). Above the E bell handler,
            // and returns, so Shift+E never also rings the bell.
            //
            // Session 46 (0.7.451) - AND IT DID RING THE BELL. This asked for
            // an offer to be standing before it took the key, so with no
            // offer on the table Shift+E (RB plus Y on the pad) fell through
            // to the plain E handler below and ended the turn - found with
            // the test driver. In a battle the key is now always taken here:
            // TryAccept already has the line for it, "No surrender is being
            // offered." Outside a battle nothing changes.
            if (KeyIn.Down(KeyCode.E)
                && (KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift))
                && (SurrenderReader.OfferStanding() || IsBattleActive()))
            {
                SurrenderReader.TryAccept();
                return;
            }

            // ------------------------------------------------------------------
            // THE DECK VIEW. (Session 15.) Above the map and the battle gate,
            // because while your deck is laid out in front of you it is the only
            // thing on the table.
            // ------------------------------------------------------------------
            // ------------------------------------------------------------------
            // THE NODE SURVEY. (Session 16.) Log-only and speaks nothing, so it
            // ticks before every gate and never returns — a probe that could be
            // starved by whichever layer owns the keyboard would come back with
            // half a survey and no way to tell which half.
            // ------------------------------------------------------------------
            NodeProbe.Tick(Time.deltaTime);

            // The base game's title card, watched for the frame its
            // "PRESS ANY BUTTON" text goes up. Ticked beside NodeProbe and for
            // the same reason: it speaks once, claims no key, and must not be
            // starved by whichever layer owns the keyboard. Costs a null check
            // in every scene but Start. (0.7.233.)
            BootScreenReader.TickTitleCard();

            // ------------------------------------------------------------------
            // THE CAMERA WATCHER. (Session 16.) Above every layer, because the
            // camera is now the source of truth for which layer owns the
            // keyboard — see MapReader's view-ladder block.
            //
            // It lived inside the map branch in 0.7.55, which the deck view
            // never reaches, so not one transition into or out of the deck view
            // was logged and DeckViewReader was never told it had been left.
            // A watcher that only runs where the thing it watches cannot happen
            // is not a watcher.
            //
            // Cost is one cached property read per frame; the ViewManager lookup
            // is backed off, so scenes without one do not spam the log.
            MapReader.TickViewChange();

            // The book can be opened with the mouse, and until Session 16 that
            // softlocked: IKMA did not know, so the rulebook layer below never
            // took the keyboard. Asked of the game every frame, next to the
            // camera, for the same reason.
            RulebookReader.TickOpenState(Time.deltaTime);

            // The window can change without a keypress: the options panel applies
            // its pending video settings when it closes. Two int reads and a bool
            // per frame, and it catches the change whatever caused it.
            OptionsReader.TickDisplay();

            // The safe can give way at any moment while the player is looking at
            // it, and nothing else would say so.
            MapReader.TickSafe();

            // ------------------------------------------------------------------
            // STANDING UP IN THE CABIN. (Session 16.) Above every other layer,
            // beside the deck view, because standing is a CAMERA state and not a
            // map state — it must not sit inside a branch gated on
            // MapAvailable(). That was the 0.7.53 softlock: the node manager
            // does not care where the camera is, so MapAvailable() stayed true
            // while Zamar was out of his chair and IKMA went on offering map
            // controls into a state where the arrows and Enter did nothing.
            //
            // This is deliberately NOT a reader for the cabin. It says where you
            // are, gets you back to the map, and admits the rest still needs the
            // mouse. Every other key is swallowed — one that fell through would
            // act on a map the player cannot see.
            // ------------------------------------------------------------------
            // A camera walk is in flight — the player is between rungs and
            // whatever layer answers a key would be answering for a screen they
            // are leaving. Swallow everything until it lands. Walks are under a
            // second and the destination announces itself.
            if (MapReader.Walking) return;

            if (MapReader.Standing)
            {
                // Zamar, Session 16: "Backspace while standing up, regardless of
                // position, should cause the player to sit back down at the
                // map." Backspace is the universal leave key everywhere else in
                // the mod, so standing is no longer the one place it means
                // nothing.
                bool standShift = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);

                // SitDown rather than ReturnToMap since 0.7.74. The game will
                // not seat a player who has walked away from the table — that
                // refusal is the game's and it is correct — so SitDown walks
                // back first and then presses the same button, which is what
                // a sighted player does. At the table it is the old call,
                // unchanged.
                if (KeyIn.Down(KeyCode.Backspace)) { MapReader.SitDown(); return; }
                if (KeyIn.Down(KeyCode.Space))     { MapReader.DescribeAhead();             return; }
                // M IS GONE FROM THE WHOLE MOD AS OF 0.7.111.
                //
                // Session 16 moved every M binding in IKMA to Space. This one
                // survived the migration and was the last M left in the source
                // — one key, in one layer, doing what no other screen's M did.
                //
                // It also spoke a line that had outlived its truth: "You are
                // standing at the table." was said from the far corner of the
                // room. And the rest of what it said — the standing controls —
                // is what H already answers.
                //
                // Zamar, Session 18: "drop M, move it's unique info to Space."
                // The only thing it carried that nothing else does is WHERE the
                // player is, and that now rides on Space with the description.
                // Removing a key that is wrong is not a loss; leaving one that
                // lies is the defect this project fixes everywhere else.
                // H answers for the layer the player is actually in. Focused
                // on an object, the digits and Backspace both mean something
                // else, so the standing help would be wrong.
                if (KeyIn.Down(KeyCode.H))
                {
                    if (MapReader.Focused()) MapReader.SpeakFocusHelp();
                    else                     MapReader.SpeakStandingHelp();
                    return;
                }
                // R — the rulebook, but only where the book actually is. See
                // MapReader.OpenRulebookFromCabin. Deliberately NOT in the
                // standing help: it works in one slot, and his own description
                // of that slot is what tells the player about it.
                if (KeyIn.Down(KeyCode.R))         { MapReader.OpenRulebookFromCabin();     return; }

                // 1, 2 and 3 — a closer look at part of what is in front of you.
                // These are the keys his cabin descriptions have been promising
                // since Session 16 and which did nothing until now. Alpha row
                // only; the numpad is deliberately not bound, matching how every
                // other digit in this mod is read.
                if (KeyIn.Down(KeyCode.Alpha1))    { MapReader.ExamineAhead(1);              return; }
                if (KeyIn.Down(KeyCode.Alpha2))    { MapReader.ExamineAhead(2);              return; }
                if (KeyIn.Down(KeyCode.Alpha3))    { MapReader.ExamineAhead(3);              return; }

                // TURNING. A quarter turn per press, so GetKeyDown — a held key
                // would spin. Three ways in for counterclockwise and clockwise,
                // and only A and D are named in the help: Zamar asked for the
                // others "silently", because a player who reaches for Shift plus
                // an arrow or the space bar should find it works without every
                // alternative being read out to everyone else.
                if (KeyIn.Down(KeyCode.A)
                 || (standShift && KeyIn.Down(KeyCode.LeftArrow)))
                { MapReader.TurnCounterclockwise(); return; }

                // Space is NO LONGER a turn. It became the describe-what-is-in-
                // front-of-me key across the whole mod, and a key cannot mean
                // two things on the same screen.
                if (KeyIn.Down(KeyCode.D)
                 || (standShift && KeyIn.Down(KeyCode.RightArrow)))
                { MapReader.TurnClockwise(); return; }

                // WALKING — ONE STEP PER PRESS, not a hold. Zamar, 0.7.60:
                // "moving in a direction moves me multiple spaces in that
                // direction until I hit a wall, like a pokemon ice puzzle."
                //
                // GetKey renewed the button every frame the arrow was down, so a
                // normal keypress was a third of a second of held movement and
                // carried him across the room. GetKeyDown plus a short fixed
                // burst is one step. Left and right step SIDEWAYS — what the Dir
                // buttons turned out to do, found in the 0.7.59 playtest, and he
                // kept it.
                // MOVING CUTS WHAT THE ROOM WAS SAYING. (0.7.244.) Zamar:
                // "While standing up, moving with the arrow keys should
                // interrupt a space readout."
                //
                // Space describes what is in front of you, and a step changes
                // exactly that — so the description is stale the instant the
                // key goes down, and it was still being read out over the new
                // one. The same rule every browse in the mod already follows.
                if (KeyIn.Down(KeyCode.UpArrow)   || KeyIn.Down(KeyCode.DownArrow)
                 || KeyIn.Down(KeyCode.LeftArrow) || KeyIn.Down(KeyCode.RightArrow))
                {
                    CombatAnnouncer.DropCommentary("moved while standing");
                    Speech.Silence();
                }

                if (KeyIn.Down(KeyCode.UpArrow))    { MapReader.StepForward();  return; }
                if (KeyIn.Down(KeyCode.DownArrow))  { MapReader.StepBackward(); return; }
                if (KeyIn.Down(KeyCode.LeftArrow))  { MapReader.StrafeLeft();   return; }
                if (KeyIn.Down(KeyCode.RightArrow)) { MapReader.StrafeRight();  return; }

                // Everything else swallowed: a key falling through would act on
                // a map the player is not looking at.
                return;
            }

            // ------------------------------------------------------------------
            // THE RUN END SCREEN. (Session 16.) Above the scene gate for the
            // same reason as the card reward: Active is set and cleared by the
            // game's own patch, so this does not depend on guessing which scene
            // the screen lives in — and this one certainly is not Part1_Cabin.
            // ------------------------------------------------------------------
            if (RunEndReader.Active)
            {
                if (RunEndReader.NeedsAnnouncement(Time.deltaTime))
                {
                    RunEndReader.Announce();
                    return;
                }

                RunEndReader.Tick(Time.deltaTime);
                HandleRunEndKeys();
                return;
            }

            if (DeckViewReader.Active)
            {
                if (DeckViewReader.NeedsAnnouncement(Time.deltaTime))
                {
                    DeckViewReader.Announce();
                    return;
                }
                HandleDeckViewKeys();
                return;
            }

            // ------------------------------------------------------------------
            // THE CREDITS ROLL. (0.7.236.) One scene of its own, and the only
            // key it answers is the way out — the scroll speeds up on any key,
            // which is the game's own business and needs nothing from IKMA.
            // ------------------------------------------------------------------
            if (CreditsReader.Active)
            {
                if (KeyIn.Down(KeyCode.Backspace)) { CreditsReader.Leave(); return; }
                return;
            }

            // ------------------------------------------------------------------
            // THE BOOT SCREEN. (0.7.232.) Above the title screen because it
            // comes before it and hands over to it — and above the Kaycee's Mod
            // menu for the same reason the pause menu sits above everything it
            // covers: while the intro is running, nothing else on screen is
            // real.
            // ------------------------------------------------------------------
            if (BootScreenReader.Active)
            {
                BootScreenReader.Tick(Time.unscaledDeltaTime);

                if (BootScreenReader.NeedsAnnouncement())
                {
                    BootScreenReader.Announce();
                    return;
                }

                HandleBootScreenKeys();
                return;
            }

            if (MenuReader.MenuActive())
            {
                if (MenuReader.NeedsScreenAnnouncement(Time.deltaTime))
                {
                    MenuReader.AnnounceScreen();
                    return;
                }
                HandleMenuKeys();
                return;
            }

            // ------------------------------------------------------------------
            // INSCRYPTION'S OWN TITLE SCREEN. (0.7.231.) Below the Kaycee's Mod
            // menu gate and not beside it: the two never coexist — one is the
            // Start scene, the other Ascension_Configure — but MenuReader is the
            // screen the player reaches ninety-nine times out of a hundred, so
            // it answers first.
            //
            // Active is false unless a MenuController was captured IN THE START
            // SCENE, so the pause menus, which are the same class, never land
            // here.
            // ------------------------------------------------------------------
            if (TitleScreenReader.Active)
            {
                // The options panel is its own screen over the title cards,
                // exactly as it is over the pause menu's cards. Same reader,
                // same keys — only the root it hangs off differs, and
                // OptionsReader.PanelRoot answers that.
                if (TitleScreenReader.OptionsOpen)
                {
                    if (!OptionsReader.Announced) OptionsReader.Announce();
                    HandleTitleOptionsKeys();
                    return;
                }
                else if (OptionsReader.Announced)
                {
                    // The panel went away — by Backspace, by the mouse, or by
                    // the game. Drop the state so re-entering announces again.
                    OptionsReader.Reset();
                }

                if (TitleScreenReader.NeedsAnnouncement(Time.deltaTime))
                {
                    TitleScreenReader.Announce();
                    return;
                }
                HandleTitleScreenKeys();
                return;
            }

            // ------------------------------------------------------------------
            // CARD REWARD (Session 13). Above the battle scene gate on purpose.
            // The reward screen runs inside Part1_Cabin today, but Active is set
            // and cleared by the game's own sequencer patches, so this does not
            // depend on guessing which scene it lives in — and the other choice
            // sequencers on the roadmap may not live in the same one.
            //
            // It also fixes ITERATION POINT 4 further down: the map-return
            // announcement used to fire during this screen, telling the player
            // the map was back while the game was still waiting on a card.
            // ------------------------------------------------------------------
            if (CardChoiceReader.Active)
            {
                ReleasePostBossLatchToReward();   // Session 40: see PostBattleScreenPending

                if (CardChoiceReader.NeedsAnnouncement(Time.deltaTime))
                {
                    CardChoiceReader.Announce();
                    return;
                }

                CardChoiceReader.Tick(Time.deltaTime);
                HandleCardChoiceKeys();
                return;
            }

            // Gate: only poll during the battle scene.
            // "Part1_Cabin" is the confirmed runtime name for Kaycee's Mod battles
            // (logged via IKMA SCENE LOADED diagnostic). Using scene name avoids
            // calling Singleton<TurnManager>.Instance every frame, which generates
            // null warnings outside of combat.
            bool inBattle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Part1_Cabin";

            if (!inBattle)
            {
                // A screen that named itself is waiting on the player somewhere
                // the Part1_Cabin gate does not cover. Everything with a real
                // reader — menus, the rulebook, the deck view, the card reward,
                // the run end screen — has already returned above this, so
                // reaching here with a context set means the game is genuinely
                // holding and IKMA has nothing to say about where.
                if (!string.IsNullOrEmpty(_explicitUnreadableContext))
                    TickUnreadableScreen();

                return;
            }

            // Cursor is managed via Harmony patch on PlayableCard.OnCursorEnter
            // in Plugin.cs — no cursor logic needed here.

            // ------------------------------------------------------------------
            // MAP MODE (roadmap item 2, Session 8 draft): the map lives in the
            // same Part1_Cabin scene as battles, so battle-vs-map is decided by
            // live TurnManager state. ITERATION POINT 3: IsBattleActive is a
            // heuristic (Opponent set and game not ended) — if map keys act
            // during battle or battle keys act on the map, this check is the
            // suspect. IKMA MAPMODE log line shows the decision.
            // ------------------------------------------------------------------
            // ------------------------------------------------------------------
            // Item targeting takes priority over everything in battle: the game
            // is blocked waiting for a target and nothing else can proceed.
            // ------------------------------------------------------------------
            if (_targeting)
            {
                if (HandleTargetingKeys()) return;
            }

            bool battleActive = IsBattleActive();

            // Anywhere IKMA has a real reader, the unreadable-screen latch is
            // not armed. Cleared here rather than in each branch so a new
            // context can never inherit a stale one.
            if (battleActive || MapReader.MapAvailable())
            {
                ClearUnreadableScreen();

                // A battle or a usable map is proof the self-named screen is
                // finished, and it is one of the few things that is.
                ClearExplicitUnreadableContext("battle or map available");
            }

            // Session 9 note 1: latch the moment an encounter ends. The map
            // returning is a purely visual event — without this the player is
            // left pressing keys into silence, not knowing control is back.
            if (_wasBattleActive && !battleActive)
                _mapReturnPending = true;
            _wasBattleActive = battleActive;

            // ==================================================================
            // ITERATION POINT 4, CLOSED. (0.7.198.)
            //
            // The comment that used to sit here said: "if this announcement
            // fires DURING the post-battle card reward sequence (before the map
            // is really usable), the gate needs to be node interactability
            // rather than MapAvailable(). Report if the timing feels early."
            //
            // It fired. Zamar beat the Prospector and his log reads:
            //
            //   IKMA MAP: encounter ended, map navigable - announcing.
            //   IKMA SPEAK: Map. You are at Campfire. 1 path ahead...
            //   IKMA MAP: hovering Boss battle: Prospector.   (on every arrow)
            //
            // while the screen in front of him was the reward chest. The mod
            // narrated a different screen, confidently, and answered four
            // keypresses with information about somewhere he was not.
            //
            // THE GATE IS THE GAME'S OWN. TurnManager.PostBattleSpecialNode is
            // a SpecialNodeData the game sets when a fight has a screen after
            // it — <BossDefeatedSequence>d__24 is one of the four places that
            // set it (dumps/dump_rarechoice.txt) — and it clears once that
            // screen is done. Non-null means "there is still something between
            // the player and the map".
            //
            // HELD, NOT DROPPED. _mapReturnPending stays armed, so the map is
            // announced properly the moment the reward screen finishes. The old
            // behaviour lost nothing except its timing.
            // ==================================================================
            if (_mapReturnPending && PostBattleScreenPending())
                return;

            if (!battleActive)
            {
                if (MapReader.MapAvailable())
                {
                    if (_mapReturnPending)
                    {
                        _mapReturnPending = false;
                        _mapIdleTimer = 0f;
                        _mapIdleInterval = MAP_IDLE_REPEAT;
                        Plugin.Log?.LogInfo("IKMA MAP: encounter ended, map navigable — announcing.");
                        MapReader.AnnounceMapReady();
                        return;
                    }

                    // Idle prompt. Any keypress means the player is here and
                    // engaged, so the clock resets and the interval drops back
                    // to the slower repeat — the fast first prompt exists for
                    // the silence after a load, not as a running commentary.
                    if (KeyIn.AnyDown)
                    {
                        _mapIdleTimer = 0f;
                        _mapIdleInterval = MAP_IDLE_REPEAT;
                    }
                    else
                    {
                        _mapIdleTimer += Time.deltaTime;
                        if (_mapIdleTimer >= _mapIdleInterval)
                        {
                            _mapIdleTimer = 0f;
                            _mapIdleInterval = MAP_IDLE_REPEAT;
                            MapReader.AnnounceIdlePrompt();
                            return;
                        }
                    }

                    if (KeyIn.Down(KeyCode.H))          { SpeakHelp(HelpContext.Map); return; }

                    // SHIFT+UP AND SHIFT+DOWN ride the camera ladder; the bare
                    // arrows go back to meaning what they mean everywhere else.
                    //
                    // Zamar, Session 16: "Instead of just up arrow and down
                    // arrow going to deck and stand up, let's return those
                    // arrows to how they usually work in menus." A key that
                    // means the same thing on every screen is worth more than a
                    // key perfectly tuned to one — the same reasoning that put A
                    // on teeth in the deck view.
                    bool mapShift = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);

                    if (mapShift && KeyIn.Down(KeyCode.UpArrow))   { MapReader.ShowDeck(); return; }
                    if (mapShift && KeyIn.Down(KeyCode.DownArrow)) { MapReader.StandUp();  return; }

                    // Bare Up and Down browse the paths, matching the menu
                    // convention: up is previous, down is next.
                    // I reads your item slots, the same line and the same
                    // numbering as I in a battle — item slots are fixed
                    // positions, so slot 2 is slot 2 whether or not slot 1 is
                    // empty. One key meaning one thing everywhere.
                    if (KeyIn.Down(KeyCode.I))         { BoardReader.ReadItems(); return; }

                    // B, Zamar's call, Session 16. In a battle B is the whole
                    // board situation and O is the opponent's queue; on the map
                    // he wants B for what is coming. His keyboard, his mapping —
                    // and B is free here either way.
                    if (KeyIn.Down(KeyCode.B))         { MapReader.AnnouncePathsAhead(); return; }

                    if (KeyIn.Down(KeyCode.UpArrow))    { MapReader.BrowseChoices(-1); return; }
                    if (KeyIn.Down(KeyCode.DownArrow))  { MapReader.BrowseChoices(1);  return; }

                    // BACKSPACE IS DEAD ON THE MAP, on purpose. Zamar:
                    // "Backspace on the map selection should be disabled."
                    // There is nothing above the map to go back to, and the
                    // never-offer-a-dead-key rule cuts both ways: the key is
                    // swallowed here rather than falling through to something
                    // that would answer for a different screen.
                    if (KeyIn.Down(KeyCode.Backspace)) return;

                    // D IS DEAD ON THE MAP. (0.7.152, his call.)
                    //
                    // It opened DeckViewReader while Shift+Up opened
                    // MapReader.ShowDeck — two keys showing two different deck
                    // screens on the same map, and only one of them was ever
                    // announced. Shift+Up is the one that walks the camera to
                    // the deck on the table, so it is the one that stays.
                    //
                    // Swallowed rather than left to fall through, the same rule
                    // Backspace follows above: a key that no longer means
                    // anything here must not come to mean something elsewhere.
                    if (KeyIn.Down(KeyCode.D)) return;
                    // No shift guard needed here or at the other R bindings:
                    // the global Shift+R handler at the top of Update returns
                    // before any of them is reached.
                    if (KeyIn.Down(KeyCode.R))          { RulebookReader.Open(); return; }
                    if (KeyIn.Down(KeyCode.LeftArrow))  { MapReader.BrowseChoices(-1); return; }
                    if (KeyIn.Down(KeyCode.RightArrow)) { MapReader.BrowseChoices(1);  return; }
                    if (KeyIn.Down(KeyCode.Space))          { MapReader.AnnouncePosition(); return; }

                    if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
                    {
                        var node = MapReader.GetSelectedChoice();
                        if (node == null)
                        {
                            Speech.Browse(Vocabulary.Hotkeys.NoPathSelectedUse);
                            return;
                        }
                        // Universal primitive: click the node the way a mouse
                        // would — delegates and all. The watcher narrates
                        // whether the game actually starts traveling.
                        string destination = MapReader.GetNodeFriendlyName(node);

                        // THE MAP HAS TO BE READY TO BE PRESSED. (0.7.246.)
                        //
                        // His 0.7.245 log, arriving back from a card choice:
                        //
                        //   [Error] Coroutine couldn't be started because the
                        //           game object 'Nodes' is inactive!
                        //   IKMA MAP: node clicked (Card choice Cost).
                        //   IKMA SPEAK: Cannot travel there.
                        //
                        // The nodes were browsable — hovering read them fine —
                        // but the map was still transitioning in, and pressing a
                        // node then threw inside the GAME's own code and left
                        // him told he could not travel somewhere he could.
                        //
                        // MapAvailable already asks exactly this question and has
                        // since Session 11; the click path simply never asked it.
                        // Refused with a line rather than silently, because the
                        // player pressed a key and is owed an answer.
                        if (!MapReader.MapAvailable())
                        {
                            Plugin.Log?.LogInfo("IKMA MAP: Enter held — the map is still settling.");
                            Speech.Browse(Vocabulary.Hotkeys.MapIsStillSettling);
                            return;
                        }

                        // ======================================================
                        // THE NODE HAS TO BE SWITCHED ON TOO. (0.7.442.)
                        //
                        // Zamar, 0.7.441, after the Trapper: "The mod
                        // completely broke the gamestate by being able to
                        // advance to that first card choice before the
                        // conversation played out." His log:
                        //
                        //   DIALOGUE: line advanced.            (LET ME THINK...)
                        //   MAP: node clicked (Card choice Random).
                        //   QUEUE (dialogue): YOU BEHELD THE BEAUTY OF THE DAWN...
                        //   CHOICE: card selection started.
                        //   ...
                        //   WATCHDOG: SOFTLOCK CAUGHT
                        //
                        // PaperGameMap.CompleteRegionSequence (PaperGameMap.cs:51)
                        // unrolls the new map, which sets the active node, and
                        // on the next line calls SetAllNodesInteractable(false).
                        // It then waits a second, plays the region's lines, and
                        // only after them calls FindAndSetActiveNodeInteractable
                        // again. For that whole stretch ActiveNode is set,
                        // nothing is moving and the Nodes object is on, so
                        // MapAvailable says yes - and in the second before the
                        // region's first line there is no conversation lock
                        // either.
                        //
                        // A mouse cannot click a node there: MapNode.SetActive
                        // (MapNode.cs:36) turns the node's collider off, and
                        // the cursor only finds colliders. IKMA calls
                        // CursorSelectStart on the node directly, which no
                        // collider guards. So the game's own answer is asked
                        // first: InteractableBase.Enabled, PUBLIC, is that
                        // collider's enabled flag. PaperGameMap.ChangingRegion,
                        // PUBLIC, covers the few frames between the unroll and
                        // the switch-off.
                        //
                        // Asked on the key press, never per frame. If either
                        // question throws, the click goes through as it did
                        // before: a map that never answers is the worse fault.
                        // ======================================================
                        bool nodeOff = false;
                        try { nodeOff = !node.Enabled; } catch { nodeOff = false; }
                        bool changingRegion = false;
                        try
                        {
                            var paper = PaperGameMap.Instance;
                            changingRegion = paper != null && paper.ChangingRegion;
                        }
                        catch { changingRegion = false; }

                        if (nodeOff || changingRegion)
                        {
                            Plugin.Log?.LogInfo(
                                $"IKMA MAP: Enter held — the game has not opened '{destination}' yet " +
                                $"(node switched off={nodeOff}, changing region={changingRegion}).");
                            Speech.Browse(Vocabulary.Hotkeys.MapIsStillSettling);
                            return;
                        }

                        // Session 11: cut whatever is still reading. The idle
                        // prompt is long and kept listing map controls while the
                        // piece was already sliding — advice about a decision the
                        // player has just finished making. Nothing replaces it:
                        // the piece slide is the game's own confirmation and
                        // "Arrived at X." lands at the far end.
                        Speech.Silence();

                        node.CursorSelectStart();
                        node.CursorSelectEnd();
                        Plugin.Log?.LogInfo($"IKMA MAP: node clicked ({destination}).");
                        StartCoroutine(WatchTravelOutcome(destination));
                        return;
                    }
                }
                else
                {
                    // No battle, and the map is not navigable either. See
                    // TickUnreadableScreen — this used to be silence.
                    TickUnreadableScreen();
                    return;
                }

                return; // Not in battle: battle hotkeys stay inactive.
            }

            bool shift = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);

            var bm = Singleton<BoardManager>.Instance;
            var ph = Singleton<PlayerHand>.Instance;

            bool choosingSlot       = bm != null && bm.ChoosingSlot;
            bool choosingSacrifices = bm != null && bm.ChoosingSacrifices;

            // Session 11: item mode used to survive the turn boundary. Zamar
            // used an item, ended his turn, and at the start of the next one H
            // still read the ITEMS help — the layer was still on, so every key
            // meant what it means in item mode rather than what it means in
            // battle. A layer the player did not leave and cannot see is the
            // worst kind of stuck. Entering the play flow ends it too: choosing
            // a slot or a sacrifice is unambiguously not browsing items.
            if (TurnStartedFlag)
            {
                TurnStartedFlag = false;
                if (_itemMode) ExitItemMode(announce: false);
            }

            if (_itemMode && (choosingSlot || choosingSacrifices))
                ExitItemMode(announce: false);
            bool inPlayFlow         = choosingSlot || choosingSacrifices;

            // The settle window is measured from the moment the game enters the
            // state, so only the first press after a prompt can ever be held.
            TrackPlayFlowEntry(inPlayFlow);

            // ------------------------------------------------------------------
            // Session 13, Zamar's call: while the game is not accepting player
            // actions, action keys say nothing at all.
            //
            // From the 0.7.24 log: during the opening deal, before "Your turn.",
            // three arrow presses each answered "Hand is empty." The answer was
            // true and it was still noise — he could not have acted on it, and a
            // blind player exploring the keyboard during a phase they cannot
            // affect gets a stream of replies about a game that is not listening.
            //
            // PlayingLocked is the game's own statement that play is locked, so
            // this asks rather than infers. Query keys still answer: reading the
            // board is not a game action, and wanting to know where things stand
            // during the opponent's turn is exactly when it matters most.
            // ------------------------------------------------------------------
            if (!inPlayFlow && PlayIsLocked())
            {
                if (HandleQueryReadKeys(shift)) return;
                if (KeyIn.Down(KeyCode.H)) { SpeakHelp(HelpContext.Battle); return; }
                if (KeyIn.Down(KeyCode.R)) { RulebookReader.Open(); return; }

                // SESSION 14 REGRESSION FIX, and this gate caused it.
                //
                // The gate above was added at 0.7.30 to silence action keys while
                // the game is not accepting them. It was right about the opening
                // deal and wrong about the draw phase, because THE DRAW PHASE IS
                // A PLAY-LOCKED STATE — the game locks the hand precisely until
                // you draw. So the one moment D and S are the only legal actions
                // in the game was the one moment they were swallowed.
                //
                // Worse than a dead key: IKMA was actively saying "Draw phase.
                // Press D to draw from your deck, or S to draw from the Squirrel
                // deck" into a state where neither did anything. Offering a blind
                // player a key that does nothing is a bug by this project's own
                // standard; offering one while announcing it is that bug with a
                // megaphone.
                //
                // It survived seven builds because no playtest happened between
                // 0.7.30 and 0.7.37. Dated by `git log -S "PlayIsLocked()"`.
                //
                // The draw keys go INSIDE the gate rather than before it: they
                // belong to exactly this state and nowhere else. TryDraw still
                // asks the game whether a draw is possible and lets Leshy refuse
                // in his own voice, so nothing here decides anything.
                if (KeyIn.Down(KeyCode.D)) { TryDraw(sideDeck: false); return; }
                if (KeyIn.Down(KeyCode.S)) { TryDraw(sideDeck: true);  return; }

                // Session 36, Zamar: "Hold the E until it lands." The hand is
                // locked while a drawn card lands, so the bell arrives HERE, not
                // at the bell handler. Held, and rung from UpdateOuter.
                if (KeyIn.Down(KeyCode.E) && DrawInFlight)
                {
                    _bellHeldForDraw = true;
                    _bellDrawSeenAt = _bellHeldAt = Time.unscaledTime;
                    Plugin.Log?.LogInfo("IKMA BELL: held until the drawn card lands.");
                    return;
                }

                // Session 37, note D5 - a bell the locked game will not take is
                // no longer silent. Zamar: "Now is not the time to ring the bell."
                if (KeyIn.Down(KeyCode.E))
                {
                    Plugin.Log?.LogInfo("IKMA BELL: E while play is locked - refused.");
                    Speech.Browse(Vocabulary.CannotEndTurn);
                    return;
                }

                // Session 34, Zamar: on the controller, A and B draw during the
                // draw phase ("During your Draw Phase: A, draw from your deck,
                // or B, draw from the Squirrel deck"). Only while the game is
                // waiting for the draw, only the pad, no shoulder held.
                if (DrawPhaseAwaitingDraw())
                {
                    if (KeyIn.PadAlone(PadInput.PadButton.A)) { TryDraw(sideDeck: false); return; }
                    if (KeyIn.PadAlone(PadInput.PadButton.B)) { TryDraw(sideDeck: true);  return; }
                }

                // SPACE REPEATS THE PROMPT, AND IT HAS TO BE HERE. (0.7.174.)
                //
                // 0.7.173 put this on the general encounter Space handler far
                // below and it never fired once — this gate ends in an
                // unconditional return, so during the draw phase no key reaches
                // that far. Zamar: "I need Space to repeat the Draw Phase line
                // during the Draw Phase. It wasn't here."
                //
                // EXACTLY THE REASONING THE D AND S COMMENT ABOVE ALREADY GIVES:
                // a key that belongs to this state goes inside the gate for this
                // state. A handler placed after a blanket return is not a
                // handler, and it fails SILENTLY — his log showed neither the
                // draw line nor the encounter fallback, which is what a key that
                // never arrives looks like from the outside.
                //
                // The prompt text is duplicated from the line Plugin.cs speaks
                // when the phase opens, which check_source.ps1 will report as a
                // duplicate. That is the right trade here: the two must say the
                // same words or the repeat is not a repeat.
                if (KeyIn.Down(KeyCode.Space))
                {
                    // 0.7.266 — one composer. The duplicate this comment used to
                    // defend is gone; see Vocabulary.DrawPhasePrompt for why it
                    // had to be.
                    Speech.Browse(Vocabulary.DrawPhasePrompt());
                    return;
                }

                // ARROWS BROWSE THE HAND HERE TOO. (0.7.184.)
                //
                // Zamar: "during the Draw phase we need to enable one more
                // functionality. The arrow keys should also be enabled to be
                // able to hear your current hand before you have to decide
                // which deck to draw from. A sighted player can mouse cursor
                // through their hand during this draw phase."
                //
                // THAT IS THE PARITY ARGUMENT EXACTLY, and the gate was
                // over-broad rather than wrong: it exists to stop keys that
                // ACT reaching a state the game is not ready for, and browsing
                // acts on nothing. Which deck to draw from is a real decision
                // and the hand is the information it depends on.
                //
                // ENTER IS DELIBERATELY NOT INCLUDED — his call: "not enter,
                // since it's just informational." Enter would try to PLAY the
                // browsed card into a state that cannot accept it, which is the
                // exact class of key this gate was built to stop.
                //
                // BrowseHand, not the play-flow slot navigation: inPlayFlow is
                // false during the draw phase, so this is the same branch the
                // arrows would take anyway a moment later.
                if (KeyIn.Down(KeyCode.LeftArrow))  { BrowseHand(-1); return; }
                if (KeyIn.Down(KeyCode.RightArrow)) { BrowseHand(1);  return; }

                return;
            }

            // ------------------------------------------------------------------
            // Item mode (Session 9). Captures arrows/Enter/Backspace while
            // active. Force-exit if the game pulls the player into play flow so
            // they can never be stranded in a layer that no longer applies.
            // ------------------------------------------------------------------
            if (_itemMode && inPlayFlow)
                ExitItemMode(announce: false);

            if (_itemMode)
            {
                if (HandleItemModeKeys(shift))
                    return;
            }

            // Shift + 1/2/3: use an item without entering item mode. Kept as a
            // shortcut for players who know their loadout. (Session 9.)
            // Session 34: RB + D-pad numbers count as Shift + number here,
            // except in play flow, where a number picks a slot (below).
            if (shift || (KeyIn.PadNumber && !inPlayFlow))
            {
                if (KeyIn.Down(KeyCode.Alpha1)) { UseItem(0); return; }
                if (KeyIn.Down(KeyCode.Alpha2)) { UseItem(1); return; }
                if (KeyIn.Down(KeyCode.Alpha3)) { UseItem(2); return; }
            }

            // ------------------------------------------------------------------
            // Left/Right arrows: HSA horizontal-list navigation.
            // In play flow: navigate slots. Outside: browse hand.
            // (Comma/period removed Session 8 — not HSA keys.)
            // ------------------------------------------------------------------
            if (KeyIn.Down(KeyCode.LeftArrow))
            {
                if (inPlayFlow) NavigateSlot(bm, -1, choosingSacrifices);
                else            BrowseHand(-1);
                return;
            }

            if (KeyIn.Down(KeyCode.RightArrow))
            {
                if (inPlayFlow) NavigateSlot(bm, 1, choosingSacrifices);
                else            BrowseHand(1);
                return;
            }

            // Home/End removed Session 10; number-key jumps removed Session 11.
            // JumpSlotEdge, JumpHandEdge, JumpToSlot and JumpToHandCard are kept
            // below, unbound, rather than deleted — they are correct code and
            // cost nothing sitting there, and if a long-list context turns up in
            // Act 2 or 3 the jump behaviour is already written and tested.

            // Session 11: number-key jumps removed at Zamar's call. 1-9 and 0
            // jumped to the Nth card in hand or the Nth slot, an HSA convention
            // that does not carry: HSA lists are long, and Inscryption's are
            // four slots and a handful of cards. Two keystrokes of arrowing
            // beats remembering an index, and every extra convention is one more
            // thing a new player has to be taught before they can play.
            // Arrows, Enter and Backspace carry the game. Tab stays — jumping to
            // the next AFFORDABLE card is real information, not a shortcut.

            // ------------------------------------------------------------------
            // Play flow: Enter / Backspace during slot/sacrifice selection.
            // Query reads (C/G/B/A/I/O) stay available mid-play (Session 8):
            // re-hear the board while holding a card, then arrow back to your
            // slot. E/Tab/D/S remain outside play flow — ending the turn or
            // drawing mid-placement is not a legal game state.
            // ------------------------------------------------------------------
            if (inPlayFlow)
            {
                if (KeyIn.Down(KeyCode.H))
                {
                    SpeakHelp(choosingSacrifices ? HelpContext.Sacrifice : HelpContext.PlayFlow);
                    return;
                }

                // NUMBER KEYS PICK A SLOT OUTRIGHT. (0.7.353.)
                //
                // Zamar: "keep Shift + 1, 2, and 3 as quick use those items, but
                // have regular 1,2,3, and 4 during the play card and/or choose
                // sacrifice phase" — Enter on a Rabbit, 3 plays it in slot 3;
                // Enter on a Pack Rat, 1 and 3 sacrifice those slots, 2 places
                // it. One key is arrow-to-the-slot plus Enter, through the same
                // ConfirmSelection, so every rule Enter obeys still applies.
                //
                // This reverses Session 11's removal of number jumps, at his
                // call, and only here: in play flow the number IS the slot a
                // sighted player clicks, not an index into a list.
                if (!shift && (choosingSlot || choosingSacrifices))
                {
                    int picked = -1;
                    if      (KeyIn.Down(KeyCode.Alpha1) || KeyIn.Down(KeyCode.Keypad1)) picked = 0;
                    else if (KeyIn.Down(KeyCode.Alpha2) || KeyIn.Down(KeyCode.Keypad2)) picked = 1;
                    else if (KeyIn.Down(KeyCode.Alpha3) || KeyIn.Down(KeyCode.Keypad3)) picked = 2;
                    else if (KeyIn.Down(KeyCode.Alpha4) || KeyIn.Down(KeyCode.Keypad4)) picked = 3;

                    if (picked >= 0)
                    {
                        DirectSlotPick(bm, ph, picked, choosingSlot, choosingSacrifices);
                        return;
                    }
                }

                if (HandleQueryReadKeys(shift))
                    return;

                // TAB CYCLES SACRIFICE CANDIDATES. (0.7.139.)
                //
                // Zamar: "tab didnt cycle through all the available sacrifices
                // here, only the first one." It did not cycle at all — Tab was
                // bound to the HAND jump and to slot placement, never to
                // sacrifice selection, so what he heard was the arrows' landing
                // slot repeated back.
                //
                // Tab means the same thing everywhere it exists in this mod:
                // move to the next thing you can act on. In the hand that is the
                // next affordable card; here it is the next occupied slot. Same
                // key, same promise.
                if (choosingSacrifices && KeyIn.Down(KeyCode.Tab))
                {
                    ProbeSacrificeCandidates(bm);
                    NavigateSlot(bm, 1, true);
                    return;
                }

                if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
                {
                    // The whole keypress, before anything decides about it — so
                    // the log can count presses against injections and say which
                    // of the two is wrong.
                    Plugin.Log?.LogInfo(
                        $"IKMA PLAY: Enter in play flow — sacrifices={choosingSacrifices}, " +
                        $"slot={choosingSlot}, focus=slot {_slotIndex + 1}.");

                    ConfirmSelection(choosingSlot, choosingSacrifices, bm, ph);
                    return;
                }

                // Backspace: inject the game's own cancel input (Bug 3 final).
                // The InputButtons_GetButtonDown_Patch in Plugin.cs answers yes
                // to the game's cancel query for the next two frames; the game
                // runs its own cancel logic with its own guards. The watcher
                // narrates what actually happened.
                if (KeyIn.Down(KeyCode.Backspace))
                {
                    if (!choosingSacrifices && !LastChooseSlotCanCancel)
                    {
                        Speech.Browse(Vocabulary.Hotkeys.CardPlacementCannotBe);
                        return;
                    }

                    string cancelCardName = choosingSacrifices
                        ? CardReader.CardName(bm.CurrentSacrificeDemandingCard?.Info)
                        : CardReader.CardName(ph?.ChoosingSlotCard?.Info);

                    CancelInjectUntilFrame = Time.frameCount + 2;
                    Plugin.Log?.LogInfo($"IKMA CANCEL: injecting cancel input (frames {Time.frameCount}-{CancelInjectUntilFrame}).");
                    StartCoroutine(WatchCancelOutcome(bm, choosingSacrifices, cancelCardName));
                    return;
                }

                // Tab: jump to the next slot this card may actually be played
                // in. (Session 14, Zamar's ask.) The list comes from ChooseSlot,
                // so it is the game's own definition of legal, and Tab only
                // means anything when the choice is genuinely restricted.
                if (KeyIn.Down(KeyCode.Tab) && choosingSlot)
                {
                    JumpToNextValidSlot(bm);
                    return;
                }

                // Any other key during play flow: re-read current slot.
                if (KeyIn.AnyDown)
                    AnnounceCurrentSlot(bm, choosingSacrifices);
                return;
            }

            // ------------------------------------------------------------------
            // Query hotkeys — normal turn browsing.
            // ------------------------------------------------------------------
            if (HandleQueryReadKeys(shift))
                return;

            // SPACE IN AN ENCOUNTER — "WHERE AM I?" (0.7.114, his wording.)
            //
            // Space is the orientation key everywhere else in the mod: it names
            // the screen, or describes what is in front of you. In a battle it
            // was the one layer where it did nothing, so a player who put the
            // headphones back on or came back to a game left running had no way
            // to ask what screen they were on without pressing a key that acts.
            //
            // DELIBERATELY NOT IN THE H LIST. Zamar: "This new button does not
            // need to be added to the H key list, it can stay silent function."
            // That is his call and it is a narrow exception to the standing rule
            // that a key which works should be discoverable — the answer this
            // one gives is the location of the help itself, so a player who
            // finds it needs nothing else, and a player who never finds it has
            // lost nothing that H does not already say.
            //
            // It reports and never acts, so it is safe at any point in a turn —
            // including while the game is waiting on something else.
            if (KeyIn.Down(KeyCode.Space))
            {
                // DURING THE DRAW PHASE, SPACE REPEATS THE DRAW PROMPT.
                // (0.7.173.) Zamar: "If I press Space during the Draw Phase it
                // should repeat this line."
                //
                // The draw phase is the one moment in a turn where the game is
                // waiting on the player and the answer is two specific keys. A
                // player who missed the prompt — it arrived behind a character
                // line, or he stepped away — otherwise has to press H and read
                // past the whole battle help to find the two keys he needs now.
                //
                // Same shape as Space on the map, the rulebook and the card
                // choice screen: Space says WHERE YOU ARE and what this screen
                // wants. The encounter fallback below stays for the rest of the
                // turn, where there is no pending question to repeat.
                //
                // Composed live rather than remembered, so it cannot outlive
                // the phase it describes: if the draw already happened the
                // condition is false and the general line answers instead.
                // The draw-phase repeat lives inside the draw gate above, not
                // here — this handler is unreachable during that phase.
                // (0.7.174.)
                //
                // Session 37, note D7 - Zamar: Space in the hand repeats the
                // hovered card, like Space on every menu and node screen. The
                // encounter line stays for when no card is hovered.
                var spaceHand = Singleton<PlayerHand>.Instance;
                if (spaceHand != null && spaceHand.CardsInHand != null &&
                    _handIndex >= 0 && _handIndex < spaceHand.CardsInHand.Count &&
                    spaceHand.CardsInHand[_handIndex] != null)
                {
                    ReadHandCardAt(spaceHand.CardsInHand[_handIndex]);
                    return;
                }
                Speech.Browse(Vocabulary.Hotkeys.YouAreInAn);
                return;
            }

            if (KeyIn.Down(KeyCode.H))
            {
                SpeakHelp(HelpContext.Battle); return;
            }

            if (KeyIn.Down(KeyCode.R))
            {
                RulebookReader.Open(); return;
            }

            if (KeyIn.Down(KeyCode.D))
            {
                TryDraw(sideDeck: false); return;
            }

            if (KeyIn.Down(KeyCode.S))
            {
                TryDraw(sideDeck: true); return;
            }

            if (KeyIn.Down(KeyCode.Tab))
            {
                JumpToNextPlayableCard(); return;
            }

            if (KeyIn.Down(KeyCode.E))
            {
                // Session 36, Zamar: "Hold the E until it lands." The game
                // ignores the bell while a drawn card is still landing, and
                // this press used to vanish in silence. Released from
                // UpdateOuter once the draw is announced.
                if (DrawInFlight)
                {
                    _bellHeldForDraw = true;
                    _bellDrawSeenAt = _bellHeldAt = Time.unscaledTime;
                    Plugin.Log?.LogInfo("IKMA BELL: held until the drawn card lands.");
                    return;
                }

                var tm = Singleton<TurnManager>.Instance;
                if (tm != null && tm.PlayerCanInitiateCombat)
                {
                    // Session 10: Zamar pressed I then E, and the entire combat
                    // resolved underneath a still-running item description —
                    // every attack, death and scale line queued politely behind
                    // the 1.2-second holdoff that the item read had imposed.
                    // Ringing the bell is the player committing to advance the
                    // turn, so nothing they were browsing a moment ago has any
                    // claim on the next second of speech. Drop the holdoff here;
                    // the combat lines themselves are Action priority and will
                    // interrupt from that point on.
                    CombatAnnouncer.CancelHoldoff();

                    // 0.7.197 — the bell is the player handing the turn over.
                    // The game moves its own camera for combat from here, and
                    // the post-play restore must not argue with it.
                    _restoreViewPending = false;

                    // Play the physical bell's ring SFX instead of speaking
                    // "Ending turn." -- gives the same non-verbal cue a sighted
                    // player gets from hearing the bell. (Session 7, note 3.)
                    if (_boardManager3DBellProperty == null)
                    {
                        _boardManager3DBellProperty = typeof(BoardManager3D).GetProperty(
                            "Bell",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    }

                    var bm3d = Singleton<BoardManager>.Instance as BoardManager3D;
                    object bell = (bm3d != null && _boardManager3DBellProperty != null)
                        ? _boardManager3DBellProperty.GetValue(bm3d)
                        : null;

                    Plugin.Log?.LogInfo(
                        $"IKMA BELL: bm3d={(bm3d != null)}, bellProp={(_boardManager3DBellProperty != null)}, bell={(bell != null ? bell.GetType().Name : "null")}");

                    bool bellPressHandled = false;

                    if (bell != null)
                    {
                        try
                        {
                            // Respect the bell's own gate, same as a mouse click would.
                            if (_combatBellPressingAllowedMethod == null)
                            {
                                _combatBellPressingAllowedMethod = bell.GetType().GetMethod(
                                    "PressingAllowed",
                                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            }
                            if (_combatBellPressingAllowedMethod != null)
                            {
                                object allowed = _combatBellPressingAllowedMethod.Invoke(bell, null);
                                if (allowed is bool b && !b)
                                {
                                    Plugin.Log?.LogInfo("IKMA BELL: PressingAllowed returned false.");
                                    Speech.Browse(Vocabulary.CannotEndTurn);
                                    return;
                                }
                            }

                            // 0.7.352 — THE BELL CLEARS THE TABLE TALK. Zamar:
                            // "Everything here before the Bell should be stomped
                            // once the bell is hit. If the player isnt waiting
                            // around to hear all the main phase info ... and is
                            // hitting E, then let's clean out the log so it
                            // doesn't delay the combat callouts." Everything
                            // still queued, and the line in the air, goes.
                            CombatAnnouncer.ClearQueue();
                            SacrificeBoneMerger.Abandon();
                            Speech.Silence();

                            // Invoke the game's own press handler — the same code a
                            // physical click runs (ding + ring in one path).
                            if (_combatBellOnBellPressedMethod == null)
                            {
                                _combatBellOnBellPressedMethod = bell.GetType().GetMethod(
                                    "OnBellPressed",
                                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            }
                            if (_combatBellOnBellPressedMethod != null)
                            {
                                _combatBellOnBellPressedMethod.Invoke(bell, null);
                                bellPressHandled = true;
                                Plugin.Log?.LogInfo("IKMA BELL: OnBellPressed invoked.");
                            }
                            else
                            {
                                Plugin.Log?.LogWarning("IKMA BELL: OnBellPressed method not found.");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Plugin.Log?.LogWarning($"IKMA BELL: OnBellPressed failed: {ex.Message}");
                        }
                    }

                    // Session 8 fix (test outcome b: ding without turn end).
                    // After OnBellPressed, check live game state: if the game
                    // still says combat can be initiated, the press did not
                    // advance the turn — ring TurnManager ourselves. If
                    // OnBellPressed ever does ring it, this check comes back
                    // false and we do not double-ring.
                    if (bellPressHandled)
                    {
                        if (tm.PlayerCanInitiateCombat)
                        {
                            Plugin.Log?.LogInfo("IKMA BELL: turn not advanced by OnBellPressed — ringing OnCombatBellRang.");
                            if (_onCombatBellRangMethod == null)
                            {
                                _onCombatBellRangMethod = typeof(TurnManager).GetMethod(
                                    "OnCombatBellRang",
                                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            }
                            _onCombatBellRangMethod?.Invoke(tm, null);
                        }
                        else
                        {
                            Plugin.Log?.LogInfo("IKMA BELL: OnBellPressed advanced the turn itself.");
                        }
                        return;
                    }

                    // Fallback (bell unavailable): old known-working path so the
                    // player is never stuck unable to end the turn.
                    if (bell != null)
                    {
                        try
                        {
                            if (_combatBellPlaySoundMethod == null)
                            {
                                _combatBellPlaySoundMethod = bell.GetType().GetMethod(
                                    "PlaySound",
                                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            }
                            _combatBellPlaySoundMethod?.Invoke(bell, null);
                        }
                        catch (System.Exception ex)
                        {
                            Plugin.Log?.LogWarning($"IKMA BELL: PlaySound fallback failed: {ex.Message}");
                        }
                    }

                    // Fallback turn-end: OnCombatBellRang is the TurnManager hook
                    // the physical bell calls when rung (Session 7, known working).
                    if (_onCombatBellRangMethod == null)
                    {
                        _onCombatBellRangMethod = typeof(TurnManager).GetMethod(
                            "OnCombatBellRang",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    }

                    if (_onCombatBellRangMethod != null)
                    {
                        Plugin.Log?.LogInfo("IKMA BELL: fallback OnCombatBellRang invoked.");
                        _onCombatBellRangMethod.Invoke(tm, null);
                    }
                    else
                    {
                        Speech.Browse(Vocabulary.Hotkeys.UnableToEndTurn);
                    }
                }
                else
                {
                    Speech.Browse(Vocabulary.CannotEndTurn);
                }
                return;
            }

            if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
            {
                TryPlayBrowsedCard(ph); return;
            }
        }

        // ----------------------------------------------------------------------
        // D/S: draw from main deck or side deck. (Session 8; gate removed in
        // Session 10.)
        //
        // No longer gated on TurnManager.IsPlayerDrawPhase. The keys click the
        // pile through the game's own dispatch exactly as a mouse would, and if
        // the draw is not allowed the game refuses in its own voice. The drawn
        // card is announced by name afterward, read from the hand the game
        // actually dealt into — narration grounded in the game's result, not our
        // intent. If no card arrives, the watcher expires in silence.
        // ----------------------------------------------------------------------
        private void TryDraw(bool sideDeck)
        {
            // Session 10: this used to gate on TurnManager.IsPlayerDrawPhase and
            // answer "Cannot draw right now." — a flat, mod-authored refusal
            // that told the player nothing. Left-clicking the deck at the same
            // moment made Leshy say "YOU CANNOT DRAW A CARD ON YOUR FIRST TURN."
            // The game already had a better answer than ours and we were
            // talking over it. So the gate is gone: D and S click the pile
            // exactly as a mouse would, and whatever the game says in response
            // is what the player hears, through the dialogue patch. The mod
            // stops inventing refusals for a question the game answers itself.
            if (_drawInProgress)
                return;

            // THE PROMPT IS ANSWERED, SO IT STOPS BEING SAID. (0.7.206.)
            //
            // Zamar, 0.7.205: "This info line should be stomped by pressing D
            // or S to draw. If I play fast enough that the combat read out
            // buries this, I shouldn't hear this one in the log after already
            // completing my next turn before it finishes."
            //
            // Plugin.cs already composes the draw prompt at SPEAK time and
            // returns null if a draw has happened — but only if it is still
            // waiting in the queue when its turn comes. Behind a long combat
            // readout it was reaching the front a whole turn later, by which
            // point the check passes (a new turn, nothing drawn yet) and the
            // player is told to press a key they pressed thirty seconds ago.
            //
            // A prompt that INSTRUCTS is owed a withdrawal at the moment the
            // instruction is carried out, not a re-test later. The draw prompt
            // is plain commentary — the lowest tier — so dropping it here
            // cannot touch a combat result or a line of dialogue.
            CombatAnnouncer.DropCommentary("draw taken — the prompt was answered");

            var piles = Singleton<CardDrawPiles>.Instance as CardDrawPiles3D;
            if (piles == null)
            {
                Speech.Browse(Vocabulary.Hotkeys.DrawPilesUnavailable);
                return;
            }

            // 0.7.349 — ASK EACH PILE ITS OWN COUNT. This asked the game's
            // Exhausted property, which is true only when BOTH piles are
            // empty, so with the main deck gone D clicked an empty pile and
            // said nothing. Zamar: "Pressing D while there are no cards in my
            // deck should read 'No cards remaining in your deck. Press S to
            // draw from the Squirrel deck.' Same should be true for Squirrel
            // deck." His wording; the Squirrel half mirrors it.
            int mainLeft = -1, sideLeft = -1;
            try { mainLeft = piles.Deck != null ? piles.Deck.CardsInDeck : -1; } catch { }
            try { sideLeft = piles.SideDeck != null ? piles.SideDeck.CardsInDeck : -1; } catch { }

            // 0.7.360 — ONLY IN THE DRAW PHASE. Zamar: "This should not play
            // during my main phase outside the draw phase." Outside it the
            // press goes to the pile like any other, and the game answers.
            bool drawPhase = false;
            try { drawPhase = Singleton<TurnManager>.Instance != null && Singleton<TurnManager>.Instance.IsPlayerDrawPhase; } catch { }

            // Session 46 (0.7.452) - BOTH PILES EMPTY. His 0.7.450 log, while
            // starving: S answered "No cards remaining in the Squirrel deck.
            // Press D to draw from your deck." - and the deck was empty too.
            // Each line below sends him to the OTHER pile, so neither is true
            // here. The line that is true already exists: the one the turn
            // opened with.
            if (drawPhase && mainLeft == 0 && sideLeft == 0)
            {
                Speech.Browse(Vocabulary.Turns.DrawPhaseSkippedDue);
                return;
            }

            if (drawPhase && !sideDeck && mainLeft == 0)
            {
                Speech.Browse(Vocabulary.Hotkeys.NoCardsRemainingIn);
                return;
            }
            if (drawPhase && sideDeck && sideLeft == 0)
            {
                Speech.Browse(Vocabulary.Hotkeys.NoCardsRemainingInThe);
                return;
            }

            var pile = sideDeck ? piles.SidePile : piles.Pile;
            if (pile == null)
            {
                Speech.Browse(Vocabulary.Hotkeys.DrawPileUnavailable);
                return;
            }

            // Session 8 fix (softlock + NRE confirmed in test): starting the
            // draw coroutines directly bypassed ChooseDraw's wait (infinite
            // draws, camera never released) and DrawCardFromDeck(null, ...)
            // NREs because specificCard is spawned directly. Instead, click
            // the pile through the game's own dispatch: CardPile is a
            // MainInputInteractable (confirmed via inventory dump), and
            // CursorSelectStart/End fire the assigned delegates — the exact
            // path a mouse click takes. The game draws its own top card,
            // increments its counter, releases ChooseDraw, and moves on.
            var hand = Singleton<PlayerHand>.Instance;
            int handCountBefore = hand?.CardsInHand?.Count ?? 0;

            _drawInProgress = true;
            DrawInFlight    = true;
            pile.CursorSelectStart();
            pile.CursorSelectEnd();
            Plugin.Log?.LogInfo($"IKMA DRAW: {(sideDeck ? "side" : "main")} pile clicked via CursorSelectStart/End.");

            // ==================================================================
            // THE DRAW ITSELF STOPS THE PROMPT. (0.7.445.)
            //
            // Zamar, Session 42: "drawing any card should stomp the 'Draw
            // Phase...' line". It was the "Drew X." line that did the
            // stomping, about half a second after the key. His 0.7.444 log
            // shows where that fails: he drew the Curious Egg, the egg's
            // hatch held the card out of the hand for longer than the draw
            // watcher waits, no "Drew" line ever came, and "Draw phase. Press
            // D to draw from your deck..." was read to the end after he had
            // already pressed D. "This should have been stomped."
            //
            // So the key press cuts it, and only it: the cut happens when
            // the draw prompt is the last line that was handed to the speech
            // engine and nothing is waiting behind it there. A combat result
            // still being read is left to the draw line, exactly as before.
            // ==================================================================
            try
            {
                if (CardReader.LastSpokenWas(Vocabulary.DrawPhasePrompt()) && SpeechPump.PendingCount == 0)
                {
                    Plugin.Log?.LogInfo("IKMA DRAW: the draw prompt was the line in the air - cut by the draw.");
                    Speech.Silence();
                }
            }
            catch { }
            try { Rumble.Draw(squirrel: sideDeck); } catch { }   // Session 34

            StartCoroutine(AnnounceDrawnCard(handCountBefore));
        }

        // How long to let a freshly drawn card settle before announcing it, so
        // a totem/woodcarving sigil grant is folded into the same line.
        // (Session 9 note 3 — raise this if grants still slip past.)
        private const float DRAW_SETTLE_SECONDS = 0.4f;

        // "Drew Black Goat." / "Drew Black Goat. Gains Sigil Ability: Airborne."
        // Temporary mods on a just-drawn card are always grants — the card's own
        // sigils live in Info.Abilities — so everything in TemporaryMods is
        // reported as gained.
        //
        // 0.7.284 — EXCEPT THAT A MOD'S abilities LIST IS WHAT WAS OFFERED,
        // NOT WHAT THE CARD HAS. PlayableCard.AddTemporaryMod refuses to
        // register an ability when another mod on the same card negates it,
        // and HasAbility agrees: a negate beats every grant regardless of
        // order. The Kaycee's Mod Fecundity nerf puts exactly such a negate on
        // every copy DrawCopy creates. Listing the mod's contents would then
        // announce a sigil the card does not have — the same defect class as
        // announcing what was attempted rather than what is true. Ask the game
        // instead.
        private static string ComposeDrawLine(PlayableCard drawn)
        {
            string name = CardReader.CardName(drawn);

            var granted = new List<string>();
            var seen = new HashSet<Ability>();
            if (drawn.TemporaryMods != null)
            {
                foreach (var mod in drawn.TemporaryMods)
                {
                    if (mod?.abilities == null) continue;
                    foreach (var ability in mod.abilities)
                    {
                        if (!seen.Add(ability)) continue;
                        if (!drawn.HasAbility(ability)) continue;
                        string abilityName = CardReader.GetAbilityName(ability);
                        if (abilityName != null) granted.Add(abilityName);
                    }
                }
            }

            if (granted.Count == 0)
                return Vocabulary.Hotkeys.Drew(name);

            string label = Vocabulary.Hotkeys.SigilAbilityCount(granted.Count);
            // "It gains" rather than a bare "Gains" — without the pronoun the
            // second sentence sounded like a separate announcement about
            // something else. (Session 9 note.)
            return Vocabulary.Hotkeys.DrewItGainsAnd(name, label, granted);
        }

        // Exhausted is protected on CardDrawPiles — reflection read. If it ever
        // fails to resolve, return false and let the game's own exhausted-deck
        // handling take over rather than blocking the draw.
        private static bool IsMainDeckExhausted(CardDrawPiles piles)
        {
            try
            {
                if (_pilesExhaustedProperty == null)
                {
                    _pilesExhaustedProperty = typeof(CardDrawPiles).GetProperty(
                        "Exhausted",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }
                if (_pilesExhaustedProperty != null)
                {
                    object value = _pilesExhaustedProperty.GetValue(piles);
                    if (value is bool b) return b;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogWarning($"IKMA DRAW: Exhausted check failed: {ex.Message}");
            }
            return false;
        }

        // Watches for the hand to actually grow, then announces the card the
        // game dealt — narration grounded in the game's result. Times out
        // quietly if no card arrives (e.g. the click was rejected).
        private IEnumerator AnnounceDrawnCard(int handCountBefore)
        {
            float timeout = 3f;
            while (timeout > 0f)
            {
                var hand = Singleton<PlayerHand>.Instance;
                var cards = hand?.CardsInHand;
                if (cards != null && cards.Count > handCountBefore)
                {
                    var drawn = cards[cards.Count - 1];
                    // 0.7.445 - a card that changed as it was drawn (the
                    // Curious Egg, the Glitched card) has already had its
                    // draw told by that line. "Drew X." on top would cut it.
                    // See SigilNarrator.DrawLineAlreadySaid.
                    if (SigilNarrator.DrawLineAlreadySaid(drawn))
                    {
                        Plugin.Log?.LogInfo("IKMA DRAW: no \"Drew\" line - the card's own line told this draw.");
                        SurrenderReader.SpeakOfferWhenQuiet("after the draw");
                        StartCoroutine(WatchForGrantedAbilities(drawn));
                    }
                    else if (drawn?.Info != null)
                    {
                        // Session 9 note 3: a totem grant lands on the drawn card
                        // a beat after it reaches the hand, announced in-game only
                        // by an SFX. Settle briefly before speaking so the grant is
                        // part of the draw line rather than a separate afterthought
                        // (or, previously, silent — the old watcher snapshotted the
                        // card's sigils at coroutine start, so a grant that had
                        // already landed counted as "known" and was never spoken).
                        yield return new WaitForSeconds(DRAW_SETTLE_SECONDS);

                        // interrupt:true since 0.7.43. Zamar: once a card is
                        // drawn, "Draw phase. Press D to draw..." is instructing
                        // him to do the thing he has just done, and it should be
                        // cut rather than sat through. The prompt already
                        // withdraws itself if it has not been spoken yet; this
                        // covers the case where it is already in the air.
                        //
                        // AND FROM 0.7.263 IT IS NOT DOWNGRADED EITHER. Zamar:
                        // "the drew [card name] line should always stomp the
                        // Draw Phase info line." It was not. His log:
                        //
                        //   IKMA SPEAK: Your turn.
                        //   IKMA SPEAK: Draw phase. Press D to draw...
                        //   IKMA DRAW: main pile clicked
                        //   IKMA SPEAK: queued behind a combat result rather
                        //               than cutting it.
                        //   IKMA SPEAK: Drew Opossum.
                        //
                        // "Your turn." is an Action, so it armed the one-shot
                        // that stops the next interrupt cutting a combat
                        // result. By the time the draw landed the result was
                        // long finished and the protection was spent on the
                        // draw prompt instead — protecting the very line he
                        // wanted cut.
                        //
                        // CancelInterruptProtection exists for exactly this and
                        // says so in its own summary: a line reporting a game
                        // action the player just took outranks anything already
                        // being read, because the player is waiting to hear
                        // whether the press landed.
                        //
                        // 0.7.266 — BUT ONLY OVER THE PROMPT IT IS ANSWERING.
                        // 0.7.263 dropped the protection unconditionally and
                        // went too far the other way. Zamar: "Drawing a card
                        // should not have stomped the combat result info from
                        // the previous turn. Only the Draw Phase info line."
                        //
                        // An interrupt flushes the speech pump's whole FIFO, not
                        // just the sentence being read, so cutting in while a
                        // combat readout is still draining throws away lines he
                        // has not heard yet. The two rules only look contrary:
                        // what he wants cut is THIS prompt, and what he wants
                        // kept is everything else.
                        //
                        // So ask which one is in the air. CardReader remembers
                        // the last line handed to the pump; if it is the draw
                        // prompt — the same sentence, from the same composer,
                        // which is why that duplicate had to go — the draw line
                        // cuts. If it is anything else, the protection stands
                        // and the draw line queues behind it.
                        // 0.7.294 — THE DRAW LINE IS A CONFIRMATION, AND IT
                        // CUTS. FIFTH TIME OF ASKING; SHIPPED AS ASKED.
                        //
                        // Zamar has called this out at 0.7.263, 0.7.266,
                        // 0.7.285 and again at 0.7.293: "I want drawing a
                        // Squirrel to finally stomp the Draw Phase though."
                        //
                        // The history, so nobody re-derives it: 0.7.263 made
                        // it cut unconditionally and it took a combat readout
                        // with it. 0.7.266 narrowed it to "cut only when the
                        // draw prompt is the line in the air", asking
                        // CardReader._lastSpoken — which records the last line
                        // HANDED TO THE PUMP, not the one being read. 0.7.286
                        // gave up on cutting entirely and queued instead,
                        // which is the wrong half of the trade for him.
                        //
                        // WHY NO TEST WORKS. The pump hands lines to the
                        // screen reader as fast as the native call returns,
                        // so a whole turn is inside NVDA's queue while IKMA's
                        // own queues read empty. The instrumentation below has
                        // now printed "announcer pending=False, pump
                        // pending=0" four times, in both the good case and the
                        // bad one. There is no signal to branch on, and NVDA's
                        // controller client has no way to report what is
                        // outstanding. Fixing that means pacing the announcer
                        // by spoken duration so NVDA's queue stays near empty
                        // — his call, deferred: "ignore that build for now".
                        //
                        // SO THE PROVENANCE DECIDES IT, AND THE HONEST ONE IS
                        // Confirmation: "the answer to a keypress that CHANGED
                        // the game... the player is waiting to hear whether
                        // the press landed." Drawing a card is exactly that.
                        // It interrupts unconditionally and is protected
                        // afterwards, at the one policy point, with no
                        // special case here.
                        //
                        // THE KNOWN COST, which he has heard twice and ruled
                        // on: drawing while a combat readout is still being
                        // read will cut the rest of it. That is the trade he
                        // chose. Do not quietly narrow this again — take it
                        // back to him.
                        Plugin.Log?.LogInfo(
                            "IKMA DRAW: draw line cuts in (Confirmation). " +
                            $"announcer pending={CombatAnnouncer.HasPendingMessages}, " +
                            $"pump pending={SpeechPump.PendingCount}, " +
                            $"prompt was last handed over={CardReader.LastSpokenWas(Vocabulary.DrawPhasePrompt())}.");

                        // UNCONDITIONAL MEANS UNCONDITIONAL. The 0.7.293
                        // Morsel shield downgrades the next Confirmation to a
                        // queued line, and a stale one within its three-second
                        // window would silently put this line back where it
                        // was. Cleared first; the shield exists for a sigil
                        // caused by the play it precedes, not for a draw.
                        Speech.ConsumeConfirmationShield();

                        using (Speech.Event(EventKind.CardDrawn, EventSource.CurrentPlayer)) Speech.Confirm(ComposeDrawLine(drawn));

                        // 0.7.360 — the olive branch after each draw while it stands.
                        SurrenderReader.SpeakOfferWhenQuiet("after the draw");

                        // Still watch afterwards: a grant arriving later than the
                        // settle window gets its own line. The watcher re-snapshots
                        // current sigils, so nothing announced above repeats.
                        StartCoroutine(WatchForGrantedAbilities(drawn));
                    }
                    break;
                }
                timeout -= Time.deltaTime;
                yield return null;
            }
            _drawInProgress = false;
            DrawInFlight    = false;
        }

        // ======================================================================
        // ITEM MODE (Session 9)
        //
        // I enters. Arrows browse, Enter uses, Backspace leaves, I re-reads the
        // full list. Shift+1/2/3 uses an item directly without entering.
        //
        // Activation path: ConsumableItem derives from Item -> ManagedBehaviour
        // and is NOT itself clickable. ConsumableItemSlot is the interactable —
        // it overrides OnCursorSelectStart and exposes a Consumable property
        // (both confirmed via reflection dump). Clicking the slot through
        // CursorSelectStart/End runs the game's own activation, delegates
        // included, exactly as the draw piles and map nodes do.
        //
        // NOT YET SUPPORTED: items that ask for a target after activation
        // (Fish Hook, Scissors and similar). Those enter a targeting state we
        // cannot drive from the keyboard yet. The watcher below detects that
        // state and SAYS SO rather than leaving the player in silence.
        // ======================================================================

        private void EnterItemMode()
        {
            var bm = Singleton<BoardManager>.Instance;
            if (bm != null && (bm.ChoosingSlot || bm.ChoosingSacrifices))
            {
                Speech.Browse(Vocabulary.Hotkeys.FinishPlacingYourCard);
                return;
            }

            var slots = BoardReader.GetConsumableSlots();
            if (slots == null || slots.Count == 0)
            {
                Speech.Browse(Vocabulary.Hotkeys.NoItems);
                return;
            }

            if (CountHeldItems(slots) == 0)
            {
                Speech.Browse(Vocabulary.NoItems);
                return;
            }

            _itemMode = true;

            // THE CURSOR STARTS NOWHERE HERE TOO. (0.7.245.) Zamar: "do the -1
            // default thing for the I key to."
            //
            // Entering item mode named the first item AND stood on it, so the
            // next tap of I stepped past the only item the player had been told
            // about — the same off-by-one every other reader has now had fixed.
            // The entry key and the browse key being the same key does not
            // change the rule: the read on arrival is a preview, and the first
            // tap lands on it.
            //
            // 0.7.354 — SUPERSEDED BY HIS NEW LAYOUT. Zamar: "pressing I makes
            // you cycle through them with arrows and enter, and backspace
            // returns context to your hand." With I no longer the step key,
            // the I press that opens the layer IS the first landing — the same
            // as the first arrow press on the hand — so item 1 is selected on
            // entry and Right goes to item 2. Kept preview-only, the first
            // arrow would re-read item 1: "Feels like it resets back to one".
            _itemIndex = FirstHeldItemIndex(slots);

            Speech.Browse(Vocabulary.ItemModeEntered(DescribeItemSlot(slots, _itemIndex)));
        }

        private void ExitItemMode(bool announce)
        {
            if (!_itemMode) return;
            _itemMode  = false;
            _itemIndex = NO_ITEM;   // re-entering starts at NOWHERE again
            if (announce)
                Speech.Browse(Vocabulary.ItemModeLeft);
        }

        /// <summary>
        /// Item-mode key handling. Returns true if the key was consumed.
        /// Board-reading keys deliberately still work here, so the player can
        /// check the board before committing an item.
        /// </summary>
        private bool HandleItemModeKeys(bool shift)
        {
            if (KeyIn.Down(KeyCode.I))
            {
                // I still steps forward here, beside the arrows (0.7.354).
                // Shift+I reads all three slots including empties.
                if (shift) BoardReader.ReadItems();
                else       BrowseItems(1);
                return true;
            }

            // Session 9: arrows and Backspace no longer belong to item mode.
            // Pressing an arrow means "I am done with items, back to my hand" —
            // so leave the layer and let the SAME keypress do its normal job
            // this frame by falling through. That is also the exit, now that
            // Backspace has been handed back to cancel.
            // 0.7.354 — arrows move between items; Backspace goes back to
            // the hand and says so. Tab still leaves silently and jumps to the
            // next playable card, which speaks for itself.
            if (KeyIn.Down(KeyCode.LeftArrow))  { BrowseItems(-1); return true; }
            if (KeyIn.Down(KeyCode.RightArrow)) { BrowseItems(1);  return true; }

            if (KeyIn.Down(KeyCode.Backspace))
            {
                ExitItemMode(announce: true);
                return true;
            }

            if (KeyIn.Down(KeyCode.Tab))
            {
                ExitItemMode(announce: false);
                return false;
            }

            if (KeyIn.Down(KeyCode.H))
            {
                Speech.Browse(Vocabulary.ItemModeHelp);
                return true;
            }

            // 0.7.357 — Space says where you are. Zamar's line.
            if (KeyIn.Down(KeyCode.Space))
            {
                Speech.Browse(Vocabulary.ItemModeWhere);
                return true;
            }

            if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
            {
                // Enter straight off the entry line uses the item that line
                // named, the same clamp every other reader uses at NOWHERE.
                if (_itemIndex == NO_ITEM)
                    _itemIndex = FirstHeldItemIndex(BoardReader.GetConsumableSlots());

                UseItem(_itemIndex);
                return true;
            }

            // Board reads stay available inside item mode.
            if (HandleQueryReadKeysExceptItems(shift))
                return true;

            return false;
        }

        // Same as HandleQueryReadKeys minus the I key, which item mode owns.
        private bool HandleQueryReadKeysExceptItems(bool shift)
        {
            if (KeyIn.Down(KeyCode.C)) { BoardReader.ReadHand(); return true; }

            if (KeyIn.Down(KeyCode.G))
            {
                if (shift) BoardReader.ReadPlayerBoard();
                else       BoardReader.ReadOpponentBoard();
                return true;
            }

            if (KeyIn.Down(KeyCode.B)) { BoardReader.ReadFullSituation(); return true; }
            if (KeyIn.Down(KeyCode.A)) { BoardReader.ReadResources();     return true; }
            // U, NOT O, FOR THE UPCOMING QUEUE. (0.7.175.)
            //
            // Zamar asked whether O came from Hearthstone Access. It did
            // not: every key this mod borrowed from HSA is annotated
            // "(HSA: x)" in the header at the top of this file — C, G,
            // Shift+G, A — and the queue read never was. It is an
            // Inscryption-specific read, and O only ever stood for
            // "Opponent", which already collides with G meaning the
            // opponent's board.
            //
            // U for Upcoming. U was completely unbound.
            //
            // IKMA_Controls.md still says O and is HIS file to update —
            // Claude supplies structure only. Flagged in the handover.
            if (KeyIn.Down(KeyCode.U)) { BoardReader.ReadOpponentQueue(); return true; }

            return false;
        }

        // Steps to the next slot that actually holds an item, wrapping around.
        // Empty slots are skipped while browsing — cycling through "Item 2:
        // empty" to reach the thing you want is friction, not information. The
        // announced number is still the real slot number, and Shift+I reads the
        // full loadout including gaps.
        private void BrowseItems(int direction)
        {
            var slots = BoardReader.GetConsumableSlots();
            if (slots == null || slots.Count == 0) { ExitItemMode(announce: false); return; }

            if (CountHeldItems(slots) == 0)
            {
                Speech.Browse(Vocabulary.NoItems);
                ExitItemMode(announce: false);
                return;
            }

            // From NOWHERE the first tap lands ON the item the entry line
            // named, rather than stepping past it.
            if (_itemIndex == NO_ITEM)
            {
                _itemIndex = FirstHeldItemIndex(slots);
                Speech.Browse(DescribeItemSlot(slots, _itemIndex));
                return;
            }

            int index = _itemIndex;
            for (int i = 0; i < slots.Count; i++)
            {
                index = (index + direction + slots.Count) % slots.Count;
                if (BoardReader.GetConsumable(slots[index]) != null) break;
            }

            _itemIndex = index;
            Speech.Browse(DescribeItemSlot(slots, _itemIndex));
        }

        // "Item 2: Fish Hook. Pull a card from the opponent's side."
        // Empty slots are announced as such rather than skipped — the player
        // should know the shape of their loadout, including the gaps.
        private string DescribeItemSlot(List<ConsumableItemSlot> slots, int index)
        {
            if (index < 0 || index >= slots.Count) return Vocabulary.Hotkeys.ItemUnavailable;

            object consumable = BoardReader.GetConsumable(slots[index]);
            var data = BoardReader.GetConsumableData(consumable);
            if (data == null)
                return Vocabulary.Hotkeys.ItemEmpty(index + 1);

            return Vocabulary.Hotkeys.ItemLine(index + 1, ItemNameAndDescription(data));
        }

        /// <summary>
        /// "Fish Hook. Hook a card to take it as your own..." — the item's
        /// game name, then his tightened description if there is one, else the
        /// game's rulebook text. Shared by the inventory read and the item
        /// pickup node's slots (0.7.336) so one item reads the same everywhere.
        /// </summary>
        internal static string ItemNameAndDescription(ConsumableItemData data)
        {
            if (data == null) return null;
            CardReader.NoteItemInLine(data);   // 0.7.344, for Shift+R
            // The game's text goes through the rulebook's Clean: it carries
            // raw codes ("[define:Squirrel]" on the bottles) that the page
            // expands and a straight read would speak as brackets. (0.7.337.)
            string raw = _itemDescriptions.TryGetValue(data.rulebookName, out string spoken)
                ? spoken
                : RulebookReader.Clean(Loc.Game(data.rulebookDescription));

            string description = string.IsNullOrEmpty(raw) ? "" : " " + raw;
            return Vocabulary.Hotkeys.ItemNameAndDescription(Loc.Game(data.rulebookName), description);
        }

        // Session 10 notes 4-6: the game's own rulebook text is written in
        // Leshy's voice, addressed to the player from across the table. Read
        // aloud it wanders — "You may cut up one of your adversary's cards",
        // "You will place a weight on the scales" — and the player is listening
        // for what the item DOES, not for characterisation. These are tightened
        // to the mechanical fact. Anything not listed here falls through to the
        // game's own text unchanged.
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _itemDescriptions => Loc.PerLanguage(ref _itemDescriptionsCache, ref _itemDescriptionsLanguage, Build_itemDescriptions);
        private static Dictionary<string, string> Build_itemDescriptions() =>
            new Dictionary<string, string>
        {
            { "Scissors",  Vocabulary.Hotkeys.CutUpOneOf },
            { "Pliers",    Vocabulary.Hotkeys.PlaceAWeightOn },
            { "Fish Hook", Vocabulary.Hotkeys.HookACardTo },
            // His line, Session 26. The game's text is in Leshy's voice ("My
            // cards"), which read aloud to the player names the wrong side.
            { "Magickal Bleach", Vocabulary.Hotkeys.OpponentCardsOnThe },
            // His line, Session 27 (0.7.355).
            { "Skinning Knife", Vocabulary.Hotkeys.SkinOneOfYour },
            // His line, 0.7.357.
            { "Magpie's Glass", Vocabulary.Hotkeys.SearchYourDeckFor },
            // His lines, 0.7.359, from PROVISIONAL_LINES.md.
            { "Hoggy Bank", Vocabulary.Hotkeys.UseToImmediatelyGain },
            { "Special Dagger", Vocabulary.Hotkeys.PlaceASignificantWeight },
        };
        private static Dictionary<string, string> _itemDescriptionsCache;
        private static string _itemDescriptionsLanguage;

        // Purely observational: which side of the board, if either, is empty.
        // Returns "" when both sides hold cards — in that case we have nothing
        // useful and honest to add. Never asserts a cause. (Session 10 note 5.)
        private static string DescribeEmptyBoards()
        {
            var bm = Singleton<BoardManager>.Instance;
            if (bm == null) return "";

            bool playerEmpty   = !AnyCardIn(bm.PlayerSlotsCopy);
            bool opponentEmpty = !AnyCardIn(bm.OpponentSlotsCopy);

            if (playerEmpty && opponentEmpty) return " because there are no cards on the board";
            if (opponentEmpty)                return " because there are no cards on the opponent's board";
            if (playerEmpty)                  return " because there are no cards on your board";
            return "";
        }

        private static bool AnyCardIn(List<CardSlot> slots)
        {
            if (slots == null) return false;
            foreach (var s in slots)
                if (s?.Card != null) return true;
            return false;
        }

        private int CountHeldItems(List<ConsumableItemSlot> slots)
        {
            int n = 0;
            foreach (var s in slots)
                if (BoardReader.GetConsumable(s) != null) n++;
            return n;
        }

        private int FirstHeldItemIndex(List<ConsumableItemSlot> slots)
        {
            if (slots == null) return 0;
            for (int i = 0; i < slots.Count; i++)
                if (BoardReader.GetConsumable(slots[i]) != null) return i;
            return 0;
        }

        // ----------------------------------------------------------------------
        // Activate the item in slot `index`. Every refusal is spoken — silence
        // after a keypress is the thing we are trying to eliminate.
        // ----------------------------------------------------------------------
        private void UseItem(int index)
        {
            var im = Singleton<ItemsManager>.Instance;
            if (im == null)
            {
                Speech.Browse(Vocabulary.ItemsUnavailable);
                return;
            }

            if (im.ActivatingItem)
            {
                Speech.Browse(Vocabulary.Hotkeys.ItemIsAlreadyBeing);
                return;
            }

            var slots = BoardReader.GetConsumableSlots();
            if (slots == null || slots.Count == 0)
            {
                Speech.Browse(Vocabulary.ItemsUnavailable);
                return;
            }

            if (index < 0 || index >= slots.Count)
            {
                Speech.Browse(Vocabulary.Hotkeys.ThereIsNoItem(index + 1));
                return;
            }

            var slot = slots[index];
            object consumable = BoardReader.GetConsumable(slot);
            var data = BoardReader.GetConsumableData(consumable);
            if (consumable == null || data == null)
            {
                Speech.Browse(Vocabulary.Hotkeys.ItemIsEmpty(index + 1));
                return;
            }

            string name = Loc.Game(data.rulebookName);   // spoken only

            // The game's own gate — do not second-guess it.
            bool canActivate;
            try { canActivate = BoardReader.ConsumableCanActivate(consumable); }
            catch { canActivate = true; }

            if (!canActivate)
            {
                // Session 34 (Zamar: "always try the game's way"). This used to
                // stop here and say "No valid targets for [item]" WITHOUT
                // pressing the item. That named a cause the game did not state
                // (CanActivate is also false in the draw phase, mid-trigger and
                // while choosing a slot), and it kept the game's own answer from
                // happening: ConsumableItemSlot.OnCursorSelectStart plays Leshy's
                // hint line (first try, then every few) and shakes the item
                // (dumps\dump_items_cannot_use_s34.txt). So the item is pressed
                // the game's way and the game answers. IKMA adds nothing: what
                // it should say when Leshy is silent (repeat tries, the Birdleg
                // Fan) is Zamar's call, on PROVISIONAL_LINES.md.
                if (!ClickItemSlot(slot))
                {
                    Speech.Browse(Vocabulary.Hotkeys.CouldNotBeUsed(name));
                    return;
                }
                Plugin.Log?.LogInfo($"IKMA ITEM: slot {index + 1} ({name}) pressed; the game refused it (CanActivate false) and answers itself.");
                // Session 37 - Zamar: "Give the reason." When Leshy is silent.
                ItemRefusal.AfterRefusedPress(consumable, name);
                return;
            }

            if (!ClickItemSlot(slot))
            {
                Speech.Browse(Vocabulary.Hotkeys.CouldNotBeUsed(name));
                return;
            }

            // Remember which item is in flight. If it goes on to ask for a
            // target, ConfirmTarget uses this to give the resulting death the
            // item's own verb ("Coyote is cut and dies."). (Session 10.)
            _activeItemName = name;

            Plugin.Log?.LogInfo($"IKMA ITEM: slot {index + 1} ({name}) clicked via CursorSelectStart/End.");

            // Session 9: item descriptions run long, and pressing Enter used to
            // leave them reading on for seconds after the item had already
            // fired. This interrupt stomps whatever is mid-sentence. It
            // describes a real action — the click has just gone through — not an
            // intention, so it stays inside the "announce what is true" rule.
            // The success case says nothing further: the item's own effects
            // narrate themselves (scales, damage, draws). Only failures speak.
            Speech.Browse(Vocabulary.Hotkeys.Using(name));

            StartCoroutine(WatchItemUse(slot, name));
        }

        // Click the slot through the game's own dispatch. Reflection rather than
        // a direct call because ItemSlot's base class was not dumped; resolved
        // once and cached. Returns false if no click path could be found, so the
        // caller can say so out loud instead of failing silently.
        private bool ClickItemSlot(ConsumableItemSlot slot)
        {
            if (!_itemClickResolved)
            {
                _itemClickResolved = true;
                var t = slot.GetType();
                _cursorSelectStartMethod = t.GetMethod("CursorSelectStart",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    null, System.Type.EmptyTypes, null);
                _cursorSelectEndMethod = t.GetMethod("CursorSelectEnd",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    null, System.Type.EmptyTypes, null);

                // Fallback: the virtual handler the slot overrides. Session 7
                // showed this alone is not always enough (the bell's logic lived
                // in delegates) but ConsumableItemSlot DOES override it, so it
                // is a real path here if the primitive is missing.
                if (_cursorSelectStartMethod == null)
                {
                    _cursorSelectStartMethod = t.GetMethod("OnCursorSelectStart",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                        null, System.Type.EmptyTypes, null);
                }

                Plugin.Log?.LogInfo($"IKMA ITEM: click path resolved — start={_cursorSelectStartMethod?.Name ?? "NONE"}, end={_cursorSelectEndMethod?.Name ?? "NONE"}");
            }

            if (_cursorSelectStartMethod == null) return false;

            try
            {
                _cursorSelectStartMethod.Invoke(slot, null);
                _cursorSelectEndMethod?.Invoke(slot, null);
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA ITEM: click failed: {e.Message}");
                return false;
            }
        }

        // Narrate what actually happened, not what was asked for.
        //   Consumable gone      -> the item was spent.
        //   ActivatingItem stuck -> it wants a target we cannot supply yet.
        //   Nothing changed      -> the game refused the click.
        private IEnumerator WatchItemUse(ConsumableItemSlot slot, string name)
        {
            // Session 9: 3 seconds was far too short. Pliers plays a long
            // animation, and the watcher declared "waiting for a target" while
            // the item was busy working perfectly — a confidently wrong
            // announcement, which is worse than none. Give it room; the item is
            // spent the moment the slot empties, so a long ceiling costs
            // nothing when things go well.
            float timeout = 15f;
            while (timeout > 0f)
            {
                if (slot == null) yield break;

                if (BoardReader.GetConsumable(slot) == null)
                {
                    // Success is silent — "Using X." already confirmed the press
                    // and the effect narrates itself. Saying "Used X." a second
                    // later just talked over the result.
                    Plugin.Log?.LogInfo($"IKMA ITEM: {name} consumed.");

                    // An item that destroys or moves a card changes the board and
                    // the game says nothing IKMA can hear. Ask the watcher to
                    // look, rather than waiting for the next turn boundary.
                    BoardWatcher.AnnounceAfterItem();

                    // The slot we were sitting on is now empty. Move to whatever
                    // is left so the next Enter does not hit a gap.
                    if (_itemMode)
                    {
                        var remaining = BoardReader.GetConsumableSlots();
                        if (remaining == null || CountHeldItems(remaining) == 0)
                            ExitItemMode(announce: false);
                        else
                            _itemIndex = FirstHeldItemIndex(remaining);
                    }
                    yield break;
                }

                // 0.7.352 — the clock stops while he is choosing a target.
                // Zamar chose the Skinning Knife's target fifteen seconds in and
                // heard "Skinning Knife is still waiting for input." the moment
                // after he pressed Enter.
                if (!_targeting) timeout -= Time.deltaTime;
                yield return null;
            }

            var im = Singleton<ItemsManager>.Instance;
            if (im != null && im.ActivatingItem)
            {
                // Session 10: this used to announce that keyboard targeting was
                // unsupported and tell the player to reach for the mouse. That
                // was true when it was written and is now flatly false —
                // targeting works, and Zamar heard the mod deny a feature he was
                // in the middle of successfully using. If we are targeting, the
                // player is not stuck and there is nothing to say.
                if (_targeting)
                {
                    Plugin.Log?.LogInfo($"IKMA ITEM: {name} still activating — target selection in progress, staying quiet.");
                    yield break;
                }

                Plugin.Log?.LogInfo($"IKMA ITEM: {name} is mid-activation after timeout, not targeting.");
                Speech.Browse(Vocabulary.Hotkeys.IsStillWaitingFor(name));
                yield break;
            }

            Plugin.Log?.LogInfo($"IKMA ITEM: {name} — no state change after click.");
            Speech.Browse(Vocabulary.Hotkeys.WasNotUsed(name));
        }

        // ======================================================================
        // RULEBOOK KEYS (Session 10)
        // Arrows turn pages, M repeats, Backspace or R closes. Deliberately the
        // same shape as every other browse layer in the mod — arrows move,
        // Backspace leaves, M re-reads — so there is one convention to learn
        // rather than one per screen.
        // ======================================================================
        private void HandleRulebookKeys()
        {
            if (KeyIn.Down(KeyCode.LeftArrow) || KeyIn.Down(KeyCode.UpArrow))
            { RulebookReader.Turn(-1); return; }

            if (KeyIn.Down(KeyCode.RightArrow) || KeyIn.Down(KeyCode.DownArrow))
            { RulebookReader.Turn(1); return; }

            if (KeyIn.Down(KeyCode.Backspace) || KeyIn.Down(KeyCode.R))
            { RulebookReader.Close(); return; }

            if (KeyIn.Down(KeyCode.Space)) { RulebookReader.RepeatPage(); return; }

            if (KeyIn.Down(KeyCode.H)) { RulebookReader.SpeakHelp(); return; }
        }

        // ======================================================================
        // KAYCEE'S MOD MENU (Session 9)
        // Arrows browse, Enter chooses, Backspace goes back if the screen has a
        // back button. Up/Down and Left/Right both work — the screens are
        // vertical lists, but a blind player should not have to guess which
        // axis a given screen uses.
        // ======================================================================
        // ======================================================================
        // Card reward keys. Deliberately the same shape as the menu keys: a
        // blind player should not have to learn a second set of conventions for
        // a second list. Up/Down and Left/Right both browse, for the same reason
        // they do in menus.
        //
        // No Backspace. The game offers no way out of a card reward, and this
        // project does not advertise keys that do nothing.
        // ======================================================================
        private void HandleCardChoiceKeys()
        {
            // SHIFT IS TESTED FIRST, and this is why Shift+Up did nothing in
            // 0.7.60: the bare-arrow browse line below claims UpArrow whether or
            // not Shift is held, so the shifted binding underneath could never
            // be reached. The identical mistake made Shift+Down browse a card
            // instead of leaving the deck view.
            //
            // A modified binding must be tested before the unmodified one that
            // shares its key. Nowhere else in this file gets to assume otherwise.
            bool choiceShift = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);

            // Session 34, Zamar: the Mycologists' card choice offers neither
            // the deck view nor the rulebook - "You cant do those in the game
            // and it softlocked me trying to view deck." Swallowed there.
            bool mycologists = NodeScreenReader.MycologistsActive;
            if (mycologists && ((choiceShift && KeyIn.Down(KeyCode.UpArrow)) || KeyIn.Down(KeyCode.R)))
            {
                Plugin.Log?.LogInfo("IKMA CHOICE: deck view / rulebook not offered at the Mycologists - key ignored.");
                return;
            }

            if (choiceShift && KeyIn.Down(KeyCode.UpArrow))
            { CardChoiceReader.ResetIdle(); DeckViewReader.Open(); return; }

            if (KeyIn.Down(KeyCode.LeftArrow) || KeyIn.Down(KeyCode.UpArrow))
            { CardChoiceReader.Browse(-1); return; }

            if (KeyIn.Down(KeyCode.RightArrow) || KeyIn.Down(KeyCode.DownArrow))
            { CardChoiceReader.Browse(1); return; }

            if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
            { CardChoiceReader.Select(); return; }

            if (KeyIn.Down(KeyCode.Space)) { CardChoiceReader.AnnounceCurrent(); return; }

            // R opens the rulebook here too. (0.7.50.) Zamar: "I should be able
            // to press R to open the Rulebook in this screen — I can left click
            // it — but I cannot." The book is on the table on this screen and a
            // sighted player can reach it, so the key that reaches it everywhere
            // else has to work here as well.
            if (KeyIn.Down(KeyCode.R)) { CardChoiceReader.ResetIdle(); RulebookReader.Open(); return; }

            if (KeyIn.Down(KeyCode.H))
            {
                CardChoiceReader.ResetIdle();
                // Zamar, 0.7.43 playtest: on this screen H was still reading
                // MAP controls, because the map reader still owned the keyboard.
                // With the sequencer patch fixed this fires, and it names the
                // screen first — "Card Choice", the node's own name on the map,
                // so the player hears the same words they travelled to.
                // Zamar's wording, 0.7.45, and the correction behind it:
                // "we never want to break the fourth wall like this." The first
                // draft said "the game is offering you a card" — narrating
                // Inscryption from outside it, to a player who is inside it.
                // Say what is on the table, not what the software is doing.
                //
                // This applies to every help and prompt string in the mod, not
                // just this one.
                //
                // THE CLOVER LINE IS CONDITIONAL. (Session 16, Zamar: "Don't
                // say the Lucky Clover line unless one is available for use.")
                // "A Lucky Clover, if you have one" made the player carry a
                // conditional IKMA can answer for them — the sequencer knows
                // whether the clover is on the table, so the help says either
                // what it does or nothing at all.
                string cloverHelp = CardChoiceReader.CloverAvailable
                    ? Vocabulary.Hotkeys.UsingTheLuckyClover
                    : "";

                // THE HELP OPENS THE SAME WAY THE SCREEN DID. Zamar, Session
                // 16: "Make the opening of the H key the same as the intro one."
                // Two different descriptions of one screen make a player who
                // pressed H wonder whether they are somewhere else.
                // 0.7.206 — THE LEAD SENTENCE IS NO LONGER WRITTEN HERE.
                //
                // It was a second copy of the screen's opening line, and it had
                // never learned about the boss reward chest: pressing H there
                // answered "Card Choice. Before you lay 3 cards." on a screen
                // that had introduced itself as a Rare Card Choice out of a
                // wooden box. Zamar: "H key should have done the rare card line
                // not the normal one."
                //
                // CardChoiceReader.HelpLead composes it once, from the table as
                // it stands, for both callers. The keys stay here — this is the
                // controls half, and it is the only place that lists them.
                //
                // Shift plus Up is in this list and not in the arrival line,
                // which is what he asked for: "You can still Shift+Up to look at
                // your deck though so that line needs to be added to the rare
                // one." The arrival line stays short and points at H.
                // Session 34 - THE DECK TRIAL HAS ITS OWN H LINE, his words
                // (Session 33): what the trial is and how it pays, plus its
                // controls. The arrival line is unchanged.
                if (CardChoiceReader.InTrialSelection())
                {
                    Speech.Browse(Vocabulary.CardChoices.DeckTrialHelp);
                    return;
                }

                if (NodeScreenReader.MycologistsActive)
                {
                    Speech.Browse(
                        Vocabulary.Hotkeys.LeftAndRightArrowsNoBookNoDeck(CardChoiceReader.HelpLead, cloverHelp, CardReader.ShiftRHelp));
                    return;
                }

                Speech.Browse(
                    Vocabulary.Hotkeys.LeftAndRightArrows(CardChoiceReader.HelpLead, cloverHelp, CardReader.ShiftRHelp));
                return;
            }
        }

        // ----------------------------------------------------------------------
        // Deck view. Arrows browse, M repeats, H explains, Backspace closes.
        // R opens the rulebook, which is on the table here as everywhere else.
        // ----------------------------------------------------------------------
        // ----------------------------------------------------------------------
        // THE RUN END SCREEN. One option and one outcome line, so the key set is
        // deliberately tiny: nothing here is offered that does not work.
        //
        // No arrows. There is exactly one interactable on this screen, so an
        // arrow key would either repeat the same option forever or say "1 of 1",
        // and both are noise. M repeats, Enter takes it, H says so.
        // ----------------------------------------------------------------------
        // Session 47 (0.7.455). The letter, number and Tab keys a conversation
        // locks. Only asked while a conversation is holding.
        private static bool ConversationLockedKeyDown()
        {
            if (KeyIn.Held(KeyCode.LeftControl) || KeyIn.Held(KeyCode.RightControl)) return false;

            for (KeyCode k = KeyCode.A; k <= KeyCode.Z; k++)
            {
                if (k == KeyCode.H || k == KeyCode.L || k == KeyCode.M || k == KeyCode.Y) continue;
                if (KeyIn.Down(k)) return true;
            }

            if (KeyIn.Down(KeyCode.Tab)) return true;

            for (KeyCode k = KeyCode.Alpha0; k <= KeyCode.Alpha9; k++)
                if (KeyIn.Down(k)) return true;

            return false;
        }

        private void HandleRunEndKeys()
        {
            if (KeyIn.Down(KeyCode.Space)) { RunEndReader.AnnounceCurrent(); return; }
            if (KeyIn.Down(KeyCode.H)) { RunEndReader.SpeakHelp();       return; }

            if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
            {
                RunEndReader.Select();
                return;
            }

            // 0.7.229, his call: "Backspace should take you back to the main
            // menu, enter should begin a new run with the same starter deck."
            // Enter already did the second half — the retry button's own
            // handler is TransitionToGame(), which rebuilds the run from
            // currentStarterDeck. Backspace presses the screen's own back
            // button; see RunEndReader.Back.
            if (KeyIn.Down(KeyCode.Backspace))
            {
                RunEndReader.Back();
                return;
            }

            // Any other key resets the idle clock. Someone pressing keys is
            // present and does not need telling where they are.
            if (KeyIn.AnyDown) RunEndReader.ResetIdle();
        }

        private void HandleDeckViewKeys()
        {
            // Shift first — see HandleCardChoiceKeys. Shift+Down browsed a card
            // in 0.7.60 because the bare-arrow line below took DownArrow before
            // the shifted binding was ever reached.
            bool deckShift = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);

            // SHIFT+UP CLOSES IT TOO — the same combination that opened it,
            // not its visual inverse. Zamar, Session 16: "Although Shift+Down
            // makes more sense visually, that wont matter to our players. So the
            // redo of the same command over the inverse of the same command
            // makes more sense."
            //
            // He is right about who this is for: a player who never sees the
            // camera rise has no visual inverse to reach for, but does remember
            // the key that got them here. Deliberately absent from H — Backspace
            // is the one advertised.
            if (deckShift && KeyIn.Down(KeyCode.UpArrow))
            { DeckViewReader.Close(); return; }

            if (KeyIn.Down(KeyCode.LeftArrow) || KeyIn.Down(KeyCode.UpArrow))
            { DeckViewReader.Browse(-1); return; }

            if (KeyIn.Down(KeyCode.RightArrow) || KeyIn.Down(KeyCode.DownArrow))
            { DeckViewReader.Browse(1); return; }

            if (KeyIn.Down(KeyCode.Space)) { DeckViewReader.AnnounceCurrent(); return; }
            if (KeyIn.Down(KeyCode.A)) { DeckViewReader.AnnounceResources(); return; }
            // 0.7.275 — CYCLES here, where the arrows belong to the deck and
            // there is no other key to browse items with. Everywhere else I
            // stays a full read. See BoardReader.ReadNextItem.
            // 0.7.434 - ONE PRESS READS ALL THREE, IN EVERY DECK VIEW.
            // Zamar, Session 42, at the Deck Trial: "pressing I on that
            // screen should just read all 3 item slots including empty", and
            // then: "This should also be true for every Shift+Up deck view."
            // That replaces the one-per-press cycle 0.7.275 put here.
            if (KeyIn.Down(KeyCode.I)) { BoardReader.ReadItems(); return; }
            if (KeyIn.Down(KeyCode.H)) { DeckViewReader.SpeakHelp(); return; }
            if (KeyIn.Down(KeyCode.R)) { RulebookReader.Open(); return; }

            // BACKSPACE RETURNS THE VIEW TO THE MAP. (Session 16, Zamar:
            // "Back space on the deck view screen should return the view to the
            // map selection.") It rides the ladder back down by pressing the
            // game's own button, so the map arrives properly rather than the
            // camera being moved to it.
            //
            // D closes it too: the key that opened it is the one a player
            // reaches for first, and a toggle needs no explaining.
            if (KeyIn.Down(KeyCode.Backspace) || KeyIn.Down(KeyCode.D))
            { DeckViewReader.Close(); return; }
        }

        /// <summary>
        /// ENTER AND SPACE, AND NOTHING ELSE. Zamar, 0.7.232: "no other buttons
        /// should exist here though." Every other key is swallowed rather than
        /// falling through to a layer below — there is no layer below yet, and
        /// a key that reaches one later would act on a screen that has not
        /// loaded.
        /// </summary>
        private void HandleBootScreenKeys()
        {
            if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
            { BootScreenReader.Press(); return; }

            if (KeyIn.Down(KeyCode.Space)) { BootScreenReader.AnnounceCurrent(); return; }

            if (KeyIn.Down(KeyCode.H)) { BootScreenReader.Help(); return; }
        }

        /// <summary>
        /// The options panel over the base game's title screen. The same keys
        /// as the pause menu's copy of this panel, with one difference that
        /// matters: Backspace goes back to the TITLE CARDS, not to a pause menu
        /// that does not exist here.
        /// </summary>
        private void HandleTitleOptionsKeys()
        {
            if (OptionsReader.Adjusting)
            {
                if (KeyIn.Down(KeyCode.LeftArrow) || KeyIn.Down(KeyCode.UpArrow))
                { OptionsReader.Adjust(-1); return; }

                if (KeyIn.Down(KeyCode.RightArrow) || KeyIn.Down(KeyCode.DownArrow))
                { OptionsReader.Adjust(1);  return; }

                if (KeyIn.Down(KeyCode.Backspace)) { OptionsReader.StopAdjusting(); return; }
                if (KeyIn.Down(KeyCode.Space))     { OptionsReader.SpeakPosition();  return; }
                if (KeyIn.Down(KeyCode.H))         { PauseMenuReader.SpeakHelp();    return; }
                return;
            }

            if (KeyIn.Down(KeyCode.UpArrow) || KeyIn.Down(KeyCode.LeftArrow))
            { OptionsReader.Browse(-1); return; }

            if (KeyIn.Down(KeyCode.DownArrow) || KeyIn.Down(KeyCode.RightArrow))
            { OptionsReader.Browse(1);  return; }

            if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
            { OptionsReader.Activate(); return; }

            if (KeyIn.Down(KeyCode.Space)) { OptionsReader.SpeakPosition(); return; }
            if (KeyIn.Down(KeyCode.H))     { PauseMenuReader.SpeakHelp();   return; }

            if (KeyIn.Down(KeyCode.Backspace))
            {
                CombatAnnouncer.DropCommentary("left the options menu");
                Speech.Silence();

                OptionsReader.Close();
                TitleScreenReader.ReAnnounce();
                return;
            }

            if (KeyIn.Down(KeyCode.Alpha1) && PauseMenuReader.SwitchOptionsPage(1)) { OptionsReader.Reset(); return; }
            if (KeyIn.Down(KeyCode.Alpha2) && PauseMenuReader.SwitchOptionsPage(2)) { OptionsReader.Reset(); return; }
            if (KeyIn.Down(KeyCode.Alpha3) && PauseMenuReader.SwitchOptionsPage(3)) { OptionsReader.Reset(); return; }
            if (PadPageFlip()) { OptionsReader.Reset(); return; }
        }

        private void HandleTitleScreenKeys()
        {
            if (KeyIn.Down(KeyCode.UpArrow)   || KeyIn.Down(KeyCode.LeftArrow))
            { TitleScreenReader.Browse(-1); return; }

            if (KeyIn.Down(KeyCode.DownArrow) || KeyIn.Down(KeyCode.RightArrow))
            { TitleScreenReader.Browse(1); return; }

            if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
            { TitleScreenReader.Activate(); return; }

            // Re-read where you are without moving.
            if (KeyIn.Down(KeyCode.Space)) { TitleScreenReader.AnnounceCurrent(); return; }

            // NO BACKSPACE HERE, AND THE HELP DOES NOT OFFER ONE. This is the
            // front door of the game: there is nothing behind it, and a key
            // that does nothing is the defect this project fixes.
            if (KeyIn.Down(KeyCode.H)) { TitleScreenReader.Help(); return; }
        }

        private void HandleMenuKeys()
        {
            if (KeyIn.Down(KeyCode.UpArrow)   || KeyIn.Down(KeyCode.LeftArrow))
            { MenuReader.Browse(-1); return; }

            if (KeyIn.Down(KeyCode.DownArrow) || KeyIn.Down(KeyCode.RightArrow))
            { MenuReader.Browse(1); return; }

            if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
            { MenuReader.Activate(); return; }

            // ESCAPE GOES BACK TOO, AND IT CUTS WHAT IS BEING READ. (0.7.230.)
            //
            // Zamar, on the card unlocks screen: "pressing Escape or backspace
            // to go back should stomp the info being read."
            //
            // TWO SEPARATE THINGS WERE WRONG AND ONLY ONE WAS OBVIOUS.
            //
            // Escape was never handled here at all — only Backspace was — so
            // on a screen reading a long card description ("Card Unlocks.
            // Cuckoo. Cost: 1 blood. 1, 1. Abilities: Airborne and Brood
            // Parasite...") Escape did nothing and the description simply kept
            // going. Nothing to stomp it because nothing happened.
            //
            // And Backspace, which DID work, left the description draining
            // through the announcer behind the new screen's line. Going back is
            // a player action that makes everything about the previous screen
            // stale, so the queue is dropped rather than raced.
            if (KeyIn.Down(KeyCode.Backspace) || KeyIn.Down(KeyCode.Escape))
            {
                CombatAnnouncer.DropCommentary("left a menu screen");

                if (!MenuReader.ActivateBackButton())
                    Speech.Browse(Vocabulary.Hotkeys.NoBackOptionOn);
                return;
            }

            // Re-read where you are without moving.
            if (KeyIn.Down(KeyCode.Space)) { MenuReader.AnnounceCurrent(); return; }

            if (KeyIn.Down(KeyCode.H))
            {
                // 0.7.307 — the Kaycee's Mod main menu and every other
                // MenuReader screen answer here, so this is where the
                // bug-report key is taught for KM. One composer, shared with
                // the pause menu and the title screen.
                Speech.Browse(
                    Vocabulary.Hotkeys.MenuNavigationArrowKeys());
                return;
            }
        }

        // ======================================================================
        // ITEM TARGETING (Session 9)
        //
        // Entered from Plugin.cs's BoardManager.ChooseTarget prefix. Only VALID
        // targets are offered — the game refuses the others anyway, and cycling
        // past slots that cannot be chosen is friction rather than information.
        // No cancel: once an item is committed the game does not offer a way
        // back, so the mod does not pretend otherwise.
        // ======================================================================
        internal static void BeginTargeting(List<CardSlot> allTargets, List<CardSlot> validTargets)
        {
            var usable = new List<CardSlot>();
            if (validTargets != null)
                foreach (var s in validTargets)
                    if (s != null) usable.Add(s);

            if (usable.Count == 0)
            {
                Plugin.Log?.LogInfo("IKMA TARGET: ChooseTarget fired with no valid targets.");
                Speech.Browse(Vocabulary.Hotkeys.NoValidTargets);
                _targeting = false;
                _targetSlots = null;
                return;
            }

            _targetSlots = usable;

            // THE CURSOR STARTS NOWHERE HERE TOO. (0.7.431.) Zamar, Session 41:
            // "Fish hook targeting needs the -1 default thing."
            //
            // This reader was the last one still standing on its first option
            // at arrival, so the first arrow stepped PAST the only target the
            // prompt had named. Arrival now names no target and hovers none;
            // the first arrow, either direction, lands ON target one, and the
            // game's cursor moves with it as before.
            _targetIndex = -1;
            _targeting = true;

            Plugin.Log?.LogInfo($"IKMA TARGET: choosing among {usable.Count} valid target(s).");

            // Session 10 note: same stomp as the sacrifice prompt. This fires
            // an instant after "Using Scissors." and an interrupt cut that line
            // off every time. Queued, so the announcer's post-interrupt holdoff
            // lets it finish first.
            //
            // Session 14, and this one was found by auditing rather than by a
            // playtest report: it was the last instructing line in the mod
            // still queued as a fixed Info string. Every other prompt had been
            // made deferred and self-withdrawing across 0.7.23 to 0.7.25 and
            // this one was missed, which means it carried both faults the
            // others had fixed.
            //
            // FAULT ONE, staleness. It was composed here and spoken later, so a
            // player who selected a target before it surfaced was told to
            // choose one after the item had already gone off.
            //
            // FAULT TWO, and this is the worse of them: it named the target at
            // index 0 forever. Arrowing to a different target speaks that slot
            // immediately off the keypress, so a player who moved before this
            // line was heard got a confident description of a slot that is not
            // the one under their cursor. A wrong target read during item use
            // is exactly the kind of confidently false line the log discipline
            // exists to prevent.
            //
            // Both go away by composing at speak time and reading the CURRENT
            // browse position, and by withdrawing entirely once targeting is
            // over. Prompt tier for the ordering, same as the other two.
            int offered = usable.Count;

            // Session 14 REGRESSION FIX. 0.7.33 moved this to the Prompt tier
            // for its queue POSITION, and took the interrupt behaviour with it —
            // Prompt carries IsAction, which cuts through speech from outside
            // the queue. "Using Scissors." is exactly that, spoken an instant
            // earlier straight off the keypress, and the 0.7.37 log shows the
            // prompt cutting it off every time.
            //
            // The two lines are one moment: the player pressed a key, the item
            // came out, and now the game wants a target. Nothing here is racing
            // anything — this is the settle rule, and a settle costs nothing.
            // Delayed rather than demoted, because the ORDERING win of the
            // Prompt tier is still wanted: it must not queue behind commentary.
            Speech.Prompt(() =>
            {
                if (!_targeting || _targetSlots == null || _targetSlots.Count == 0)
                {
                    Plugin.Log?.LogInfo("IKMA PROMPT: target prompt withdrawn — targeting is over.");
                    return null;
                }

                // 0.7.431 - nothing browsed yet, so no slot is named.
                int idx = _targetIndex;
                if (idx < 0 || idx >= _targetSlots.Count)
                    return Vocabulary.Hotkeys.ChooseATarget(offered);

                return Vocabulary.Hotkeys.ChooseATargetAvailable(offered, DescribeTargetSlot(_targetSlots[idx]));
            }, TARGET_PROMPT_SETTLE);
        }

        private static void EndTargeting()
        {
            _targeting = false;
            _targetSlots = null;
            _targetIndex = -1;
        }

        /// <summary>Returns true if the key was consumed by targeting.</summary>
        private bool HandleTargetingKeys()
        {
            // Zamar, Session 14. Backspace here used to fall through to the
            // "swallow everything else" line at the bottom and answer with
            // silence, which reads exactly like a key that failed to register.
            // The item is already committed — the game offers no way out — so
            // say that, and say what the player must do instead.
            if (KeyIn.Down(KeyCode.Backspace))
            {
                Speech.Browse(Vocabulary.Hotkeys.CannotCancelItemUse);
                return true;
            }

            // The game clears ActivatingItem when the item finishes. If that has
            // happened, targeting is over whatever else we think.
            var im = Singleton<ItemsManager>.Instance;
            if (im != null && !im.ActivatingItem)
            {
                EndTargeting();
                return false;
            }

            if (_targetSlots == null || _targetSlots.Count == 0)
            {
                EndTargeting();
                return false;
            }

            if (KeyIn.Down(KeyCode.LeftArrow)  || KeyIn.Down(KeyCode.UpArrow))
            { BrowseTargets(-1); return true; }

            if (KeyIn.Down(KeyCode.RightArrow) || KeyIn.Down(KeyCode.DownArrow))
            { BrowseTargets(1); return true; }

            if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
            { ConfirmTarget(); return true; }

            // QUICK TARGET. (Session 52, 0.7.464.) Zamar: "can we have 1/2/3/4
            // work as a quick target? if pressing a button without a valid
            // target say that." The number is the slot's number on the side
            // the item aims at. Every target item aims at ONE side (Fish Hook,
            // Scissors, Trapper Knife the opponent's, Hammer your own), so a
            // slot number names exactly one slot. It aims AND fires, like
            // arrow-then-Enter, and like Enter it cannot be taken back. Shift
            // is left alone: Shift+1/2/3 are the item keys, not targets.
            bool shiftDown = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);
            if (!shiftDown)
            {
                int quick = -1;
                if      (KeyIn.Down(KeyCode.Alpha1) || KeyIn.Down(KeyCode.Keypad1)) quick = 0;
                else if (KeyIn.Down(KeyCode.Alpha2) || KeyIn.Down(KeyCode.Keypad2)) quick = 1;
                else if (KeyIn.Down(KeyCode.Alpha3) || KeyIn.Down(KeyCode.Keypad3)) quick = 2;
                else if (KeyIn.Down(KeyCode.Alpha4) || KeyIn.Down(KeyCode.Keypad4)) quick = 3;
                if (quick >= 0) { QuickTarget(quick); return true; }
            }

            if (KeyIn.Down(KeyCode.H))
            {
                // Session 11: help said "this item" without ever naming it. If
                // you have reached for help you have most likely lost track of
                // WHICH item you committed to, and that is the one fact the
                // sentence was missing.
                string what = Vocabulary.Hotkeys.ChoosingATargetOrChoosingATarget(string.IsNullOrEmpty(_activeItemName), _activeItemName);

                Speech.Browse(
                    Vocabulary.Hotkeys.ArrowKeysMoveBetween(what));
                return true;
            }

            // Board reads stay available so the choice can be made informed.
            bool shift = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);
            if (HandleQueryReadKeysExceptItems(shift)) return true;

            return true; // Swallow everything else; the game is blocked here.
        }

        // Session 52. Aim at the valid target in slot number slotIndex+1, then
        // fire. The aim (hover) and the fire (select) are one frame apart: the
        // item aims at whatever the game's cursor is hovering (Session 11's Fish
        // Hook lesson), and a hover and a select in the same frame can race.
        private void QuickTarget(int slotIndex)
        {
            int at = -1;
            for (int i = 0; i < _targetSlots.Count; i++)
                if (_targetSlots[i] != null && _targetSlots[i].Index == slotIndex) { at = i; break; }

            if (at < 0)
            {
                Plugin.Log?.LogInfo($"IKMA TARGET: quick target {slotIndex + 1} - no valid target in that slot.");
                Speech.Browse(Vocabulary.Hotkeys.NoValidTargetInSlot);
                return;
            }

            bool onATarget = _targetIndex >= 0 && _targetIndex < _targetSlots.Count;
            CardSlot previous = onATarget ? _targetSlots[_targetIndex] : null;
            _targetIndex = at;
            HoverTargetSlot(previous, _targetSlots[at]);
            Plugin.Log?.LogInfo($"IKMA TARGET: quick target {slotIndex + 1} - aimed, firing next frame.");
            StartCoroutine(QuickTargetFire(_targetSlots[at]));
        }

        private System.Collections.IEnumerator QuickTargetFire(CardSlot aimed)
        {
            yield return null;
            // Still choosing, and still aimed where the key sent it.
            if (!_targeting || _targetSlots == null) yield break;
            if (_targetIndex < 0 || _targetIndex >= _targetSlots.Count) yield break;
            if (_targetSlots[_targetIndex] != aimed) yield break;
            ConfirmTarget();
        }

        private void BrowseTargets(int direction)
        {
            // 0.7.431 - from NOWHERE (-1) the first press lands on target one
            // whichever arrow it was, and there is no slot to leave.
            bool onATarget = _targetIndex >= 0 && _targetIndex < _targetSlots.Count;
            CardSlot previous = onATarget ? _targetSlots[_targetIndex] : null;

            _targetIndex = onATarget
                ? (_targetIndex + direction + _targetSlots.Count) % _targetSlots.Count
                : 0;
            var slot = _targetSlots[_targetIndex];

            // Session 11: move the GAME'S hover, not just ours.
            //
            // Fish Hook worked mechanically and lied visually — it hooked the
            // right card and animated flying at the wrong slot. The item aims at
            // whatever the game's cursor is hovering, and IKMA had only ever
            // used the CursorSelectStart/End half of MainInputInteractable,
            // never CursorEnter/CursorExit. So selection went to the right slot
            // while the presentation stayed wherever the mouse had last been.
            //
            // Blind players stream to sighted audiences. A hook flying at the
            // wrong card is not a cosmetic defect, it is the narration and the
            // screen telling the viewer two different stories.
            HoverTargetSlot(previous, slot);

            // The "N of M" tail is gone at Zamar's call. On a two- or
            // three-target list the position is obvious from arrowing, and it
            // was the last thing said every time — so the slot and card, which
            // is what the choice turns on, got pushed further from the ear.
            Speech.Browse(DescribeTargetSlot(slot));
        }

        // Hand the game's own cursor from one slot to the next, so hover-driven
        // presentation follows the browse position.
        private static void HoverTargetSlot(CardSlot leaving, CardSlot entering)
        {
            try
            {
                if (leaving != null && leaving != entering) leaving.CursorExit();
                if (entering != null) entering.CursorEnter();
            }
            catch (System.Exception e)
            {
                // Never let a presentation detail break target selection.
                Plugin.Log?.LogWarning($"IKMA TARGET: hover sync failed: {e.Message}");
            }

            // ==================================================================
            // AND CursorEnter IS ONLY HALF OF IT. (0.7.323.)
            //
            // Zamar: "The fishing hook hover target was inaccurate. It looked
            // visually like it was hovering four but mechanically was targeting
            // 3."
            //
            // Session 11 added the CursorEnter above for exactly this symptom
            // and it is not enough, for the reason Session 18 found on the node
            // screens and wrote down: InteractionCursor.ManagedUpdate RAYCASTS
            // FROM THE CURSOR'S WORLD POSITION EVERY FRAME and re-derives who
            // is hovered. Telling a slot it has been entered is an EFFECT; the
            // cursor's position is the CAUSE, and the next frame re-derives the
            // hover from wherever the mouse was left — slot 4, in his case,
            // while IKMA read and used slot 3.
            //
            // Drive the cause: put the cursor at the slot and let the game work
            // out the hover itself, which is also what makes it hold.
            //
            // GUARDED BY THE GAME'S OWN RAYCAST. CastForInteractableAtWorldPosition
            // is asked what a cast at that point would actually hit, and the
            // cursor is only moved when the answer is this slot or the card
            // standing in it. Anything else is logged and the cursor is left
            // alone — a cursor moved onto the wrong thing would be the same
            // defect pointing somewhere new.
            // ==================================================================
            if (entering == null) return;

            try
            {
                var cursor = Singleton<InteractionCursor>.Instance;
                if (cursor == null || !cursor.isActiveAndEnabled || cursor.InteractionDisabled)
                {
                    Plugin.Log?.LogInfo(
                        "IKMA TARGET: the interaction cursor is not available — " +
                        "hover left where the game put it.");
                    return;
                }

                Vector3 world = entering.transform.position;

                MainInputInteractable wouldHit = null;
                try { wouldHit = cursor.CastForInteractableAtWorldPosition(world); } catch { }

                bool onTarget = ReferenceEquals(wouldHit, entering);
                if (!onTarget)
                {
                    PlayableCard standing = null;
                    try { standing = entering.Card; } catch { }
                    onTarget = standing != null && ReferenceEquals(wouldHit, standing);
                }

                if (!onTarget)
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA TARGET: cursor not moved to slot {entering.Index + 1} — the " +
                        "game's own raycast there returns " +
                        (wouldHit == null ? "nothing" : wouldHit.name) + ".");
                    return;
                }

                cursor.MatchWorldPosition(world);
                Plugin.Log?.LogInfo(
                    $"IKMA TARGET: cursor moved to slot {entering.Index + 1} — the screen " +
                    "now shows what is being read.");
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA TARGET: cursor move threw {e.GetType().Name}.");
            }
        }

        private void ConfirmTarget()
        {
            // 0.7.431 - Enter before any arrow. Nothing is under the cursor,
            // so nothing is clicked; the prompt is said again rather than
            // leaving a dead key.
            if (_targetIndex < 0 || _targetIndex >= _targetSlots.Count)
            {
                Plugin.Log?.LogInfo("IKMA TARGET: Enter at NOWHERE - no target browsed yet, prompt repeated.");
                Speech.Browse(Vocabulary.Hotkeys.ChooseATarget(_targetSlots.Count));
                return;
            }

            var slot = _targetSlots[_targetIndex];
            if (slot == null) { EndTargeting(); return; }

            string description  = DescribeTargetSlot(slot);
            var    card         = slot.Card;
            string cardName     = CardReader.CardName(card?.Info);
            string itemName     = _activeItemName;
            bool   wasOpponents = !slot.IsPlayerSlot;

            Plugin.Log?.LogInfo($"IKMA TARGET: selecting {description} with {itemName ?? "unknown item"}");

            // Session 10, last note: this used to say "Targeting Opponent slot
            // 4: Coyote, 2, 1." — the aim, not the shot. The player already
            // knows what they were pointing at; what they need is what happened
            // to it. So we say nothing here, register the card as this item's
            // target, and let the outcome speak for itself: Plugin's Die patch
            // turns the resulting death into "Coyote is cut and dies.", and the
            // watcher below catches the non-lethal outcomes like Fish Hook.
            // If the item fizzles, nothing is claimed at all.
            if (card != null)
                ItemTargetOutcome.Register(card, itemName);

            try
            {
                // CardSlot derives from HighlightedInteractable ->
                // MainInputInteractable, whose CursorSelectStart/End are public.
                // Same primitive as draw piles, map nodes, item slots and menus.
                slot.CursorSelectStart();
                slot.CursorSelectEnd();
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA TARGET: selection click failed: {e.Message}");
                ItemTargetOutcome.Clear();
                Speech.Browse(Vocabulary.Hotkeys.ThatTargetCouldNot);
                return;
            }

            EndTargeting();
            _activeItemName = null;

            if (card != null && cardName != null)
                StartCoroutine(WatchTargetOutcome(card, cardName, itemName, wasOpponents));
        }

        // Non-lethal item outcomes. A kill announces itself through the Die
        // patch; what that path cannot see is a card that SURVIVES and changes
        // hands — Fish Hook's whole purpose. Watch for the targeted card
        // appearing in one of our own slots and say so, naming the slot it
        // landed in so the board model stays accurate.
        //
        // Deliberately conservative: PlayableCard.Slot was never confirmed by a
        // reflection dump, so rather than guess at a member name this scans
        // PlayerSlotsCopy for the card by reference — every member used here is
        // already proven elsewhere in the mod. If nothing observable changes,
        // the watcher expires in silence rather than inventing a result.
        private IEnumerator WatchTargetOutcome(PlayableCard card, string name, string itemName, bool wasOpponents)
        {
            float timeout = 2f;
            while (timeout > 0f)
            {
                if (card == null) yield break; // Destroyed — the Die patch has it.

                if (wasOpponents)
                {
                    var bm = Singleton<BoardManager>.Instance;
                    var mine = bm?.PlayerSlotsCopy;
                    if (mine != null)
                    {
                        for (int i = 0; i < mine.Count; i++)
                        {
                            if (ReferenceEquals(mine[i]?.Card, card))
                            {
                                Plugin.Log?.LogInfo($"IKMA TARGET: {name} changed sides into player slot {i + 1}.");
                                // Zamar's wording, Session 14. "Joins your side"
                                // sounded like the card chose to; the hook drags
                                // it, and the line should carry that.
                                // Session 14, 0.7.39 log: this line went out, and
                                // then the item diff a second later reported the
                                // same card again as "Porcupine moves to your
                                // slot 1." Zamar heard it twice and asked why.
                                // The watcher is told, exactly as the play and
                                // death paths tell it.
                                BoardWatcher.NoteAnnounced(card);
                                // 0.7.431 - Zamar, Session 41: the slot read he
                                // had just browsed "should have been stomped
                                // by" this line. It was Commentary, which
                                // waits its turn; the card changing sides is
                                // the state moving, and a Result cuts a stale
                                // browse read while still never cutting
                                // another queued line.
                                Speech.Result(Vocabulary.Hotkeys.IsDraggedDownTo(name, i + 1));
                                yield break;
                            }
                        }
                    }
                }

                timeout -= Time.deltaTime;
                yield return null;
            }

            Plugin.Log?.LogInfo($"IKMA TARGET: no observable change to {name} after {itemName ?? "item"}.");
        }

        // "Opponent slot 2: Coyote, 2 attack, 1 health."
        private static string DescribeTargetSlot(CardSlot slot)
        {
            if (slot == null) return Vocabulary.Hotkeys.UnknownSlot;

            string side = Vocabulary.Hotkeys.YourSlotOrOpponentSlot(slot.IsPlayerSlot);
            int number = slot.Index + 1;

            // Session 13: LiveCard, not slot.Card — a destroyed card lingers in
            // its slot and was being read out as a live target.
            var card = BoardReader.LiveCard(slot);
            if (card?.Info == null)
                return Vocabulary.Hotkeys.TargetSlotEmpty(side, number);

            // Abilities included since 0.7.43, at Zamar's request. Choosing a
            // target is a decision, and Sharp Quills or Deathtouch on the card
            // under the cursor changes it — a sighted player reads the sigils
            // off the card before committing. Bare names rather than the
            // "Ability:" label, the same as a queue read: the player is
            // comparing several slots and the labels dominate the line.
            string targetAbilities = BoardReader.FormatBareAbilitiesFor(card);

            return Vocabulary.Hotkeys.TargetSlotCard(side, number, CardReader.CardName(card), card.Attack, card.Health, targetAbilities);
        }

        // ----------------------------------------------------------------------
        // H key: context-sensitive spoken help. (Session 8 tent-pole — blind
        // players cannot glance at a README mid-game; HSA does the same.)
        // ----------------------------------------------------------------------
        // ----------------------------------------------------------------------
        // THE UNREADABLE SCREEN. (Session 14.)
        //
        // Zamar is about to spend the saved encounter and start testing full
        // runs — every node type on the map, and the bosses. Most of what a run
        // stops at has no reader yet: the campfire, the trader, the trapper,
        // the totem builder, the sacrifice stone, the mycologists, item pickups.
        // Those are roadmap items 5 through 8 and they are waiting on a dump.
        //
        // Until then, arriving at one of them dropped the player into TOTAL
        // SILENCE. Not a missing feature — a dead end. A blind player standing
        // in that silence cannot tell an unimplemented screen from a softlock,
        // from a crash, from a key that stopped registering. There is no way to
        // find out and no way to know whether pressing something will help.
        //
        // The project rule is already written: any state where the game waits
        // on player input needs an audible prompt. This is that rule applied to
        // the states IKMA has not built yet, and it says the honest thing —
        // that IKMA cannot read this screen — rather than pretending nothing is
        // there.
        //
        // It also turns a full run into a survey. Every entry logs
        // "IKMA UNREAD" with the node it happened at, so the log comes back as
        // a list of exactly what to build, in the order a real run hits them.
        //
        // WHAT IT WILL NOT DO. It never fires in a menu, on the reward screen,
        // in the rulebook or during a battle — all four are handled above this
        // and return before reaching here. It sits inside the Part1_Cabin scene
        // gate, so no other scene can trigger it. And it holds while the game
        // is still talking or the announcer still has lines queued, because a
        // character speaking is the game DOING something, not waiting.
        //
        // Timing follows the map idle rule, which was tuned against Zamar's
        // speech rate: first prompt at 5.5s, then every 15s, and any keypress
        // resets the clock.
        // ----------------------------------------------------------------------
        private bool  _unreadableActive = false;
        private float _unreadableTimer = 0f;
        private float _unreadableInterval = MAP_IDLE_FIRST;
        private string _unreadableWhere = null;

        // ----------------------------------------------------------------------
        // SESSION 16 — THE NET COULD NOT REACH THE END OF A RUN.
        //
        // DescribeUnreadableScreen names the screen from MapNodeManager.ActiveNode
        // and returns null when there is no node, which is correct and was the
        // 0.7.37 fix for the net arming with node='unknown'. But it means the net
        // can only ever cover screens the MAP is still standing behind.
        //
        // A losing run tears the map down and then puts the player on the death
        // card screen, where the game waits on free text entry. That screen was
        // not "named but unreadable" — it was TOTAL SILENCE, which is the exact
        // trap this whole mechanism exists to remove: a blind player standing in
        // it cannot tell an unbuilt screen from a softlock or a dead keyboard.
        //
        // The fix is not to loosen the map-node rule. It is to let a screen NAME
        // ITSELF, from its own patch, the same way CardChoicesSequencer hands
        // over the card reward. The name is then the game telling IKMA where the
        // player is rather than IKMA inferring it, which is what the 0.7.37 guard
        // was protecting in the first place.
        //
        // NO END HOOK EXISTS for these sequences, so this is cleared from every
        // direction that certainly ends one: a scene change, a battle starting,
        // the map coming back, and the run end screen opening. A context left set
        // would nag about a screen the player has already left, which is worse
        // than the silence it replaces.
        // ----------------------------------------------------------------------
        private static string _explicitUnreadableContext;

        internal static void SetExplicitUnreadableContext(string name)
        {
            if (_explicitUnreadableContext == name) return;
            _explicitUnreadableContext = name;
            Plugin.Log?.LogInfo($"IKMA UNREAD: context set to '{name}'.");
        }

        internal static void ClearExplicitUnreadableContext(string reason)
        {
            if (_explicitUnreadableContext == null) return;
            Plugin.Log?.LogInfo(
                $"IKMA UNREAD: context '{_explicitUnreadableContext}' cleared ({reason}).");
            _explicitUnreadableContext = null;
        }

        // How long the game must have been quiet before this speaks. Arriving
        // at a node usually fires a character line, and talking over one to say
        // "I cannot read this" would be the worst of both.
        private const float UNREADABLE_QUIET_SECONDS = 3f;

        // Session 40: how long the screen must stay unclaimed, frame after
        // frame, before this arms. See TickUnreadableScreen.
        private const float UNREADABLE_DWELL_SECONDS = 1.5f;
        private float _unreadableSeenSince = 0f;
        private int   _unreadableLastFrame = -10;

        private void ClearUnreadableScreen()
        {
            if (!_unreadableActive) return;
            _unreadableActive   = false;
            _unreadableTimer    = 0f;
            _unreadableInterval = MAP_IDLE_FIRST;
            _unreadableWhere    = null;
        }

        private void TickUnreadableScreen()
        {
            // Still loading. The incoming context announces itself.
            if (Plugin.LoadingAnnounced) { ClearUnreadableScreen(); return; }

            // ==================================================================
            // SESSION 46 - A CHARACTER WAITING ON SPACE IS NOT AN UNREAD
            // SCREEN. (0.7.451.)
            //
            // Found with the test driver on 0.7.450. After the Prospector's
            // rare card, Leshy says one line and the game waits on Space
            // before the map changes. Left alone for a few seconds, IKMA said
            //   "Boss battle: Prospector. IKMA cannot read this screen yet.
            //    The game is waiting for you here."
            // The quiet test below only knows how long ago the line was SHOWN,
            // so a line the player has not answered yet looked like silence.
            // The game is waiting on the conversation, and the conversation
            // has its own prompt. ConversationHolding is the question the key
            // lock already asks.
            // ==================================================================
            bool characterWaiting = false;
            try { characterWaiting = DialogueAdvancer.ConversationHolding(); } catch { }
            if (characterWaiting)
            {
                ClearUnreadableScreen();
                _unreadableTimer     = 0f;
                _unreadableSeenSince = Time.unscaledTime;
                return;
            }

            // The game is talking, or IKMA still has lines to say. Either way
            // something is happening and this is not a dead end yet.
            if (CombatAnnouncer.HasPendingMessages)
            {
                _unreadableTimer = 0f;
                return;
            }

            float since = Time.realtimeSinceStartup - TextDisplayer_ShowMessage_Patch.LastLineTime;
            if (since < UNREADABLE_QUIET_SECONDS)
            {
                _unreadableTimer = 0f;
                return;
            }

            if (!_unreadableActive)
            {
                // ==============================================================
                // SESSION 40 - A GAP BETWEEN TWO SCREENS IS NOT A SCREEN.
                //
                // Zamar's 0.7.427 log armed this five times in one run, every
                // time in the moment between two readers and every time with
                // the node he had just LEFT:
                //
                //   IKMA SPEAK: Arrived at Campfire.
                //   IKMA UNREAD: no reader for this screen. node='Card choice Kin'
                //   IKMA NODE: Campfire - reader active.
                //
                // Nothing was spoken, because the next reader arrived before
                // the idle prompt - but the log line was false every time, and
                // Space or H in that moment would have been answered with
                // "IKMA cannot read this screen" about a screen it was about
                // to read. (STATUS_KM has carried this since Session 29.)
                //
                // So the silence has to LAST before it counts: this method must
                // reach here on consecutive frames for UNREADABLE_DWELL_SECONDS.
                // A reader taking the frame, a line being spoken or a load
                // starts the count again. And the log line moved to where the
                // prompt is actually said, so the log lists screens a player
                // really met in silence - the survey it was meant to be.
                // ==============================================================
                if (Time.frameCount - _unreadableLastFrame > 1) _unreadableSeenSince = Time.unscaledTime;
                _unreadableLastFrame = Time.frameCount;
                if (Time.unscaledTime - _unreadableSeenSince < UNREADABLE_DWELL_SECONDS) return;

                // Session 14, from the 0.7.37 log: this armed twice when it
                // should not have — once at map load with node='unknown', and
                // once mid-travel with the node the player was LEAVING.
                //
                // Both have the same shape: the map exists but is between
                // states, so there is no answer to "where am I" yet. Arming
                // without one would announce a screen IKMA cannot even name,
                // which is worse than the silence it exists to replace. Wait
                // until the game can say where the player is standing.
                string where = DescribeUnreadableScreen();

                // 0.7.317 — EMPTY IS NOT AN ANSWER EITHER. The deck trial
                // armed this with node='' in the frame between arriving and
                // the sequencer starting. Nothing was spoken, but the comment
                // above is the rule and an empty string was slipping past it.
                if (string.IsNullOrEmpty(where)) return;

                _unreadableActive   = true;
                _unreadableTimer    = 0f;
                _unreadableInterval = MAP_IDLE_FIRST;
                _unreadableWhere    = where;

                if (Plugin.VerboseDiagnostics)
                    Plugin.Log?.LogInfo(
                        $"IKMA UNREAD: watching a screen with no reader. node='{_unreadableWhere}' " +
                        $"scene='{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'.");
            }

            // Keys that genuinely work here. Nothing else is offered, because
            // offering a blind player a key that does nothing is the defect
            // this whole prompt exists to stop.
            if (KeyIn.Down(KeyCode.Space))
            {
                _unreadableTimer    = 0f;
                _unreadableInterval = MAP_IDLE_REPEAT;
                SpeakUnreadableScreenNow();
                return;
            }

            if (KeyIn.Down(KeyCode.H))
            {
                _unreadableTimer    = 0f;
                _unreadableInterval = MAP_IDLE_REPEAT;

                // H must not just repeat the prompt. It answers the question a
                // player actually has in this silence: what still works, and
                // what am I supposed to do. Both answers are honest, including
                // the part where the honest answer is the mouse — this is the
                // one place in the mod where that is true, and saying so beats
                // leaving someone stuck.
                Speech.Browse(
                    Vocabulary.Hotkeys.ThereIsNoIkma(CardReader.ShiftRHelp));
                return;
            }

            if (KeyIn.AnyDown)
            {
                _unreadableTimer    = 0f;
                _unreadableInterval = MAP_IDLE_REPEAT;
                return;
            }

            _unreadableTimer += Time.deltaTime;
            if (_unreadableTimer >= _unreadableInterval)
            {
                _unreadableTimer    = 0f;
                _unreadableInterval = MAP_IDLE_REPEAT;
                SpeakUnreadableScreen();
            }
        }

        // The node the player travelled to IS the screen they are standing in,
        // and MapReader already turns it into a name a player recognises. So
        // this is not "something is here" — it is "you are at the campfire and
        // IKMA cannot read the campfire", which is the difference between a
        // dead end and a known gap.
        private string DescribeUnreadableScreen()
        {
            // A screen that named itself wins over the map, because it is the
            // more specific answer AND because it is the only answer available
            // once the map is gone. See the block above SetExplicitUnreadableContext.
            if (!string.IsNullOrEmpty(_explicitUnreadableContext))
                return _explicitUnreadableContext;

            try
            {
                var mgr = MapNodeManager.Instance;
                if (mgr == null) return null;

                // Mid-move the ActiveNode is still the one being left, so a name
                // read here describes where the player WAS.
                if (mgr.MovingNodes) return null;

                var node = mgr.ActiveNode;
                if (node == null) return null;
                return MapReader.GetNodeFriendlyName(node);
            }
            catch { return null; }
        }

        // ----------------------------------------------------------------------
        // The unreadable-screen prompt is an IDLE LOOP and obeys the idle rule:
        // Info tier, deferred, never an interrupt. (Session 16.)
        //
        // Zamar: "This is true for every single idle loop instruction callout we
        // ever make." This one fires on a timer in a screen with no reader, so
        // whatever else is in the air — a character talking, a result the player
        // asked for — is more wanted than a reminder that IKMA is stuck.
        //
        // M and H still speak IMMEDIATELY. Those are answers to a keypress, not
        // idle chatter, and the player who pressed them is waiting.
        // ----------------------------------------------------------------------
        private void SpeakUnreadableScreen()
        {
            string where = _unreadableWhere;
            Speech.Commentary(() =>
            {
                // Withdraw if a reader has taken over since this was queued.
                if (!_unreadableActive) return null;
                string lead = string.IsNullOrEmpty(where) ? "" : where + ". ";
                LogUnreadableSaid(where);
                return Vocabulary.Hotkeys.IkmaCannotReadThis(lead);
            });
        }

        // Session 40: the one log line for an unreadable screen, written when
        // the player is actually told - so every one in a log is a screen a
        // player met with no reader.
        private static void LogUnreadableSaid(string where)
        {
            Plugin.Log?.LogInfo(
                $"IKMA UNREAD: no reader for this screen. node='{where}' " +
                $"scene='{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'.");
        }

        /// <summary>
        /// The same line, spoken at once because the player pressed M for it.
        /// </summary>
        private void SpeakUnreadableScreenNow()
        {
            LogUnreadableSaid(_unreadableWhere);
            string lead = string.IsNullOrEmpty(_unreadableWhere) ? "" : _unreadableWhere + ". ";
            Speech.Browse(
                Vocabulary.Hotkeys.IkmaCannotReadThis(lead));
        }

        private enum HelpContext { Map, Battle, PlayFlow, Sacrifice }

        private void SpeakHelp(HelpContext context)
        {
            switch (context)
            {
                case HelpContext.Map:
                    Speech.Browse(
                        // Zamar's ordering and wording, Session 16, verbatim.
                        // The shape of it is the point: what you do most often
                        // first, then what you look at, then the keys that leave
                        // this screen.
                        Vocabulary.Hotkeys.MapControlsArrowsBrowse);
                        // 0.7.339 — his reorder, Session 26: standing up moved
                        // after the rulebook.
                    break;
                case HelpContext.Battle:
                    Speech.Browse(
                        // HIS REWRITE, VERBATIM. (Session 18.) Reordered so the reads group
                        // together and E sits with them, "read" spelled out on each
                        // one, and Shift+R named here for the first time — it was
                        // previously silent everywhere by his Session 16 call.
                        Vocabulary.Hotkeys.EncounterControlsLeftAnd);
                    break;
                case HelpContext.PlayFlow:
                    Speech.Browse(
                        Vocabulary.Hotkeys.PlacingACardLeft);
                    break;
                // Session 10: pressing H mid-sacrifice offered Backspace to
                // cancel, which the game does not honour — the sacrifice wait
                // never polls InputButtons at all. Help that names a key which
                // does nothing is worse than no help; the player presses it,
                // hears "A sacrifice cannot be cancelled once started." and learns to distrust
                // every other instruction the mod gives. Its own context now,
                // stating plainly that there is no way out.
                case HelpContext.Sacrifice:
                    Speech.Browse(
                        Vocabulary.Hotkeys.ChoosingSacrificesLeftAnd);
                    break;
            }
        }

        // ----------------------------------------------------------------------
        // Battle-vs-map context. ITERATION POINT 3 (see Update): heuristic —
        // a battle is considered active while TurnManager has an Opponent and
        // the game hasn't ended. If this misfires, log the two values here.
        // ----------------------------------------------------------------------
        // STATIC since 0.7.266: the post-boss latch's stand-down test needs it
        // and that test is static. The body never touched instance state.
        private static bool IsBattleActive()
        {
            var tm = Singleton<TurnManager>.Instance;
            return tm != null && tm.Opponent != null && !tm.GameEnded;
        }

        // ----------------------------------------------------------------------
        // After clicking a map node, watch whether the map actually starts
        // moving and narrate the real outcome. (Same grounded pattern as the
        // cancel watcher.)
        // ----------------------------------------------------------------------
        private IEnumerator WatchTravelOutcome(string destination)
        {
            float timeout = 0.75f;
            while (timeout > 0f)
            {
                var mgr = MapNodeManager.Instance;
                if (mgr != null && mgr.MovingNodes)
                {
                    // Session 11: "Traveling to X." deleted. The move takes about
                    // a second and the game already plays the piece sliding across
                    // the map — a sighted player gets no more than that, and the
                    // line was narrating something the player had just chosen and
                    // could already hear happening. "Arrived at X." still lands at
                    // the other end, which is the part that carries information.
                    // Failure still speaks: that one is not audible otherwise.
                    Plugin.Log?.LogInfo($"IKMA MAP: traveling to {destination}.");
                    yield break;
                }
                timeout -= Time.deltaTime;
                yield return null;
            }
            Speech.Browse(Vocabulary.Hotkeys.CannotTravelThere);
        }

        // ----------------------------------------------------------------------
        // Query read keys shared by normal browsing AND play flow (Session 8):
        // C hand, G enemy board, Shift+G your board, B full read, A resources,
        // I items, U queue. Returns true if a key was consumed.
        // ----------------------------------------------------------------------
        private bool HandleQueryReadKeys(bool shift)
        {
            if (KeyIn.Down(KeyCode.C)) { BoardReader.ReadHand(); return true; }

            if (KeyIn.Down(KeyCode.G))
            {
                if (shift) BoardReader.ReadPlayerBoard();
                else       BoardReader.ReadOpponentBoard();
                return true;
            }

            if (KeyIn.Down(KeyCode.B)) { BoardReader.ReadFullSituation(); return true; }
            if (KeyIn.Down(KeyCode.A)) { BoardReader.ReadResources();     return true; }
            if (KeyIn.Down(KeyCode.I))
            {
                // Shift+I: full loadout read including empty slots.
                // Plain I: enter item mode, or step to the next held item.
                if (shift) BoardReader.ReadItems();
                else       EnterItemMode();
                return true;
            }
            if (KeyIn.Down(KeyCode.U)) { BoardReader.ReadOpponentQueue(); return true; }

            return false;
        }

        // ----------------------------------------------------------------------
        // Watches whether the game actually exits the play-flow state after a
        // cancel injection, and narrates the real outcome. Observe-only — no
        // game state is written here. (Bug 3 final approach, Session 8.)
        // ----------------------------------------------------------------------
        private IEnumerator WatchCancelOutcome(BoardManager bm, bool wasSacrifice, string cardName)
        {
            // Session 14 — instrumentation, not a fix, and deliberately not an
            // experiment either. Master Handoff B8: the game advertises that a
            // placement can be cancelled (canCancel comes back true), IKMA's
            // triple injection through InputButtons is confirmed ignored, and
            // nobody knows what the wait actually reads. Reflection cannot see
            // method bodies, so no dump can answer it — but the wait leaves a
            // trace, and BoardManager holds exactly one field about it.
            //
            // So: watch the flag while the cancel is in flight and report what
            // it does. If it never moves under any input the player tries, the
            // field is not the path either and that is worth knowing before a
            // session is spent on it.
            //
            // No input is injected here beyond what was already injected, and
            // nothing is written. Trying a second cancel route on a hunch could
            // softlock a placement, and the saved encounter Zamar tests on is
            // not something to risk on a guess.
            bool? flagAtStart = ReadCancelledPlacementFlag(bm);
            bool  flagMoved   = false;
            Plugin.Log?.LogInfo(
                $"IKMA CANCEL PROBE: watch opened. wasSacrifice={wasSacrifice}, " +
                $"cancelledPlacementWithInput={Describe(flagAtStart)}.");

            float timeout = 0.75f;
            while (timeout > 0f)
            {
                if (!flagMoved)
                {
                    bool? now = ReadCancelledPlacementFlag(bm);
                    if (now.HasValue && flagAtStart.HasValue && now.Value != flagAtStart.Value)
                    {
                        flagMoved = true;
                        Plugin.Log?.LogInfo(
                            $"IKMA CANCEL PROBE: cancelledPlacementWithInput {flagAtStart.Value} -> {now.Value}.");
                    }
                }

                bool stillInState = wasSacrifice ? bm.ChoosingSacrifices : bm.ChoosingSlot;
                if (!stillInState)
                {
                    Plugin.Log?.LogInfo("IKMA CANCEL: game exited the state — cancel succeeded.");
                    Plugin.Log?.LogInfo(
                        $"IKMA CANCEL PROBE: exited with flag {Describe(ReadCancelledPlacementFlag(bm))}, moved={flagMoved}.");
                    Speech.Browse(Vocabulary.Hotkeys.CancelledReturnedToOrCancelledCardReturned(cardName));
                    yield break;
                }
                timeout -= Time.deltaTime;
                yield return null;
            }
            Plugin.Log?.LogInfo("IKMA CANCEL: state unchanged — game did not accept the cancel.");
            Plugin.Log?.LogInfo(
                $"IKMA CANCEL PROBE: timed out with flag {Describe(ReadCancelledPlacementFlag(bm))}, moved={flagMoved}.");
            // Session 9 note 1: name what actually refused. "Cannot cancel right
            // now" is vague when the player is mid-sacrifice — the sacrifice
            // prompt is the one place the game genuinely will not back out.
            Speech.Browse(Vocabulary.Hotkeys.SacrificeCannotBeOrCannotCancelRight(wasSacrifice));
        }

        // ----------------------------------------------------------------------
        // BoardManager.cancelledPlacementWithInput — NONPUBLIC instance bool,
        // confirmed in dump_menus_targeting.txt.
        //
        // Read by reflection because it is non-public, and held as a nullable
        // so "could not read it" is a distinct answer from "it was false".
        // Those are different findings and collapsing them would waste the
        // playtest that reads the log.
        //
        // Resolution is attempted once. If the member is not there under this
        // name the probe goes quiet rather than logging a failure every frame.
        // ----------------------------------------------------------------------
        private static FieldInfo _cancelledPlacementField;
        private static bool _cancelledPlacementResolved;

        private static bool? ReadCancelledPlacementFlag(BoardManager bm)
        {
            if (bm == null) return null;

            if (!_cancelledPlacementResolved)
            {
                _cancelledPlacementResolved = true;
                try
                {
                    _cancelledPlacementField = typeof(BoardManager).GetField(
                        "cancelledPlacementWithInput",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                    if (_cancelledPlacementField == null)
                        Plugin.Log?.LogInfo("IKMA CANCEL PROBE: cancelledPlacementWithInput did not resolve — probe is inert.");
                    else if (_cancelledPlacementField.FieldType != typeof(bool))
                    {
                        Plugin.Log?.LogInfo($"IKMA CANCEL PROBE: cancelledPlacementWithInput is {_cancelledPlacementField.FieldType.Name}, not bool — probe is inert.");
                        _cancelledPlacementField = null;
                    }
                }
                catch (System.Exception e)
                {
                    Plugin.Log?.LogWarning($"IKMA CANCEL PROBE: resolve failed: {e.Message}");
                    _cancelledPlacementField = null;
                }
            }

            if (_cancelledPlacementField == null) return null;

            try { return (bool)_cancelledPlacementField.GetValue(bm); }
            catch { return null; }
        }

        private static string Describe(bool? value)
            => value.HasValue ? (value.Value ? "true" : "false") : "unreadable";

        // ----------------------------------------------------------------------
        // Called as sacrifice selection begins: put the browse cursor on a slot
        // that actually holds a card, so Enter is immediately meaningful.
        // (Session 9.)
        // ----------------------------------------------------------------------
        internal static void FocusFirstSacrificeSlot()
        {
            var bm = Singleton<BoardManager>.Instance;
            var slots = bm?.PlayerSlotsCopy;
            if (slots == null || slots.Count == 0) return;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i]?.Card != null)
                {
                    _slotIndex = i;
                    Plugin.Log?.LogInfo($"IKMA SACRIFICE: focus snapped to slot {i + 1}.");
                    return;
                }
            }
        }

        // ----------------------------------------------------------------------
        // Slot navigation during sacrifice or placement selection.
        // In sacrifice mode: skip empty slots automatically.
        // In placement mode: all slots are valid, announce normally.
        // ----------------------------------------------------------------------
        // ======================================================================
        // WHICH CARDS HAVE ALREADY BEEN CHOSEN? LOG ONLY. (0.7.139.)
        //
        // Zamar: "Once a card is chosen to be sacrificed for a 2+ blood cost
        // creature like wolf, It should no longer be included in Tab's next
        // valid target selection."
        //
        // He is right, and IKMA cannot answer it yet. ChooseSacrificesForCard is
        // a coroutine that collects the chosen cards internally; nothing in
        // dump_card_play.txt exposes a per-card "already marked" flag, and
        // BoardManager.SetSacrificeMarkersShown takes a COUNT, not a card.
        //
        // TWO WAYS TO GET THIS WRONG, both of which this project has paid for:
        // guess at a member name, or have IKMA remember which cards it pressed —
        // which strands the moment he uses the mouse, exactly like the deck-view
        // bool did.
        //
        // So this prints every true NONPUBLIC bool on each board card while a
        // sacrifice is being chosen. Whatever the game flips when a card is
        // marked is sitting in that list, named — and then it is one lookup
        // rather than a third guess.
        //
        // Once per card per distinct state, because the browse fires this on
        // every press and this project has already put 82,761 lines of its own
        // noise into a log.
        // ======================================================================
        private static readonly HashSet<string> _sacProbeSeen = new HashSet<string>();

        private void ProbeSacrificeCandidates(BoardManager bm)
        {
            try
            {
                var slots = bm?.PlayerSlotsCopy;
                if (slots == null) return;

                const System.Reflection.BindingFlags any =
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic;

                for (int i = 0; i < slots.Count; i++)
                {
                    var card = slots[i]?.Card;
                    if (card?.Info == null) continue;

                    string name = CardReader.CardName(card);
                    var flags = new List<string>();

                    foreach (var f in card.GetType().GetFields(any))
                    {
                        if (f.FieldType != typeof(bool)) continue;
                        try { if ((bool)f.GetValue(card)) flags.Add(f.Name); }
                        catch { }
                    }

                    string line = flags.Count == 0 ? "(none true)" : string.Join(", ", flags.ToArray());
                    if (_sacProbeSeen.Add($"{name}|{i}|{line}"))
                        Plugin.Log?.LogInfo(
                            $"IKMA SACPROBE: slot {i + 1} '{name}' true bools: {line}");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA SACPROBE: threw {e.GetType().Name}.");
            }
        }

        // ======================================================================
        // THE GAME'S OWN LIST OF UNCHOSEN SACRIFICES. (0.7.143.)
        //
        // Handed over by the prefix on ChooseSacrificesForCard — see
        // BoardManager_ChooseSacrificesForCard_Patch. This is a REFERENCE to the
        // list the coroutine walks, not a copy: as each sacrifice is chosen the
        // game removes its slot, so an already-marked card drops out on its own.
        //
        // NOTHING IS TRACKED AND NOTHING IS COUNTED. The alternative — IKMA
        // remembering which cards it pressed — strands the instant Zamar uses
        // the mouse, which is the stranded-bool bug this project has paid for
        // twice. Holding the game's own answer cannot go out of date.
        //
        // Dropped when the sequence ends, so a stale list can never be consulted
        // for the next card played.
        // ======================================================================
        private static List<CardSlot> _sacrificeCandidates;

        internal static void NoteSacrificeCandidates(List<CardSlot> validSlots, PlayableCard card)
        {
            _sacrificeCandidates = validSlots;

            string name = null;
            try { name = CardReader.CardName(card?.Info); } catch { }
            Plugin.Log?.LogInfo(
                $"IKMA SACRIFICE: {validSlots?.Count ?? 0} candidate slot(s) for {name ?? "a card"}.");
        }

        // ======================================================================
        // A SACRIFICED CARD IS DEAD, AND THAT IS THE WHOLE ANSWER. (0.7.146.)
        //
        // THE INSTRUMENTATION FINALLY SAID IT. His 0.7.145 log:
        //
        //   sacrifice INJECTED on slot 4 ('Squirrel') — candidates 2 -> 2.
        //   sacrifice INJECTED on slot 1 ('Black Goat') — candidates 2 -> 2.
        //   sacrifice INJECTED on slot 4 ('Squirrel') — candidates 2 -> 2.
        //   ... six times, all on cards already spent
        //
        // TWO THINGS AT ONCE, AND BOTH WERE MINE:
        //
        // 1. validSlots IS NOT MUTATED ON SELECTION. 0.7.143 assumed the game
        //    removes a slot as each sacrifice is chosen. It does not — the count
        //    is 2 before and after every single injection. That whole mechanism
        //    answered nothing, and I shipped it on an assumption about a list I
        //    had only seen the DECLARATION of.
        //
        // 2. SO EVERY EXTRA PRESS SPENT THE SAME CARD AGAIN. One Squirrel paid
        //    for a two-blood Wolf six times over. That is the cheat, and it is
        //    also the crash: six injections into a coroutine mid-enumeration is
        //    the "Collection was modified" exception in his log.
        //
        // THE REAL FLAG WAS IN THE PROBE OUTPUT ALL ALONG:
        //
        //   IKMA SACPROBE: slot 1 'Black Goat' true bools: <Dead>k__BackingField
        //
        // A sacrificed card is DEAD. PlayableCard.Dead is PUBLIC and this mod
        // has used it since 0.7.23 — BoardReader.LiveCard was written on exactly
        // this fact so the narrator would stop describing destroyed cards. The
        // answer was already in the codebase, under a different name, and I went
        // looking for a new one instead.
        //
        // THE FRAME GUARD is the second half. Dead is set when the game resolves
        // the sacrifice, not on the frame it is chosen, so a fast mash can still
        // land several presses in that gap. One injection per slot per few
        // frames closes it, and it expires on its own — IKMA remembers only its
        // own last action, for a moment, and never the game's state.
        // ======================================================================
        private static readonly Dictionary<int, int> _lastInjectedFrame = new Dictionary<int, int>();

        private const int SACRIFICE_REINJECT_FRAMES = 12;

        /// <summary>Is this slot still an unchosen sacrifice candidate?</summary>
        private bool IsSacrificeCandidate(CardSlot slot)
        {
            var card = slot?.Card;
            if (card == null) return false;

            // ALREADY SPENT. The game marks a sacrificed card Dead and leaves it
            // in its slot until the sequence resolves, so "occupied" was never
            // the right question.
            try { if (card.Dead) return false; }
            catch { }

            // ALREADY MARKED. (Session 51, 0.7.463.) A marked card is not Dead
            // until the whole cost is chosen, so after the twelve frames below
            // it read like any other ("Slot 1: Geck, 1/1.") and Enter on it
            // would have un-marked it. Zamar: "can we just skip a marked card
            // in the browse and tabs since it's already on its way?" The
            // game's own list of marked slots is the answer.
            if (IsMarkedForSacrifice(slot)) return false;

            // JUST INJECTED. Dead has not been set yet, but IKMA pressed this
            // slot a moment ago and the game has not had a frame to act on it.
            int frame;
            if (_lastInjectedFrame.TryGetValue(slot.Index, out frame)
                && Time.frameCount - frame < SACRIFICE_REINJECT_FRAMES)
                return false;

            // The candidate list, kept as a secondary filter only. It does not
            // shrink as sacrifices are chosen — proven by his log — so it can
            // narrow the starting set and nothing more.
            var list = _sacrificeCandidates;
            if (list == null) return true;

            try { return list.Contains(slot); }
            catch { return true; }
        }

        // BoardManager.currentSacrifices: NONPUBLIC (protected List<CardSlot>),
        // declared on BoardManager. OnSlotSelected adds a slot when its card
        // is marked and removes it when it is un-marked; ChooseSacrificesForCard
        // clears it when the sequence ends (dumps/dump_s51_from_decompile.txt).
        // Read only, never written.
        private static FieldInfo _currentSacrificesField;
        private static bool _currentSacrificesResolved;

        private static bool IsMarkedForSacrifice(CardSlot slot)
        {
            try
            {
                if (!_currentSacrificesResolved)
                {
                    _currentSacrificesResolved = true;
                    _currentSacrificesField = typeof(BoardManager).GetField(
                        "currentSacrifices", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (_currentSacrificesField == null)
                        Plugin.Log?.LogWarning(
                            "IKMA SACRIFICE: BoardManager.currentSacrifices did not resolve - a marked card can still be browsed.");
                }
                if (_currentSacrificesField == null || slot == null) return false;

                var bm = Singleton<BoardManager>.Instance;
                if (bm == null || !bm.ChoosingSacrifices) return false;

                var marked = _currentSacrificesField.GetValue(bm) as List<CardSlot>;
                return marked != null && marked.Contains(slot);
            }
            catch { return false; }
        }

        private void NavigateSlot(BoardManager bm, int direction, bool choosingSacrifices)
        {
            var slots = bm.PlayerSlotsCopy;
            if (slots == null || slots.Count == 0) return;

            if (choosingSacrifices)
            {
                int startIndex = _slotIndex;
                for (int i = 0; i < slots.Count; i++)
                {
                    _slotIndex = (_slotIndex + direction + slots.Count) % slots.Count;
                    if (IsSacrificeCandidate(slots[_slotIndex]))
                        break;
                }
                if (!IsSacrificeCandidate(slots[_slotIndex]))
                {
                    _slotIndex = startIndex;
                    Speech.Browse(Vocabulary.Hotkeys.NoCardsToSacrifice);
                    return;
                }
            }
            else
            {
                _slotIndex = (_slotIndex + direction + slots.Count) % slots.Count;
            }

            AnnounceCurrentSlot(bm, choosingSacrifices);
        }

        // ----------------------------------------------------------------------
        // Home/End in play flow: jump to first/last slot. In sacrifice mode,
        // land on the first/last OCCUPIED slot (scanning inward if the edge
        // slot is empty). (Session 8 — HSA Home/End convention.)
        // ----------------------------------------------------------------------
        private void JumpSlotEdge(BoardManager bm, bool toStart, bool choosingSacrifices)
        {
            var slots = bm?.PlayerSlotsCopy;
            if (slots == null || slots.Count == 0) return;

            int idx = toStart ? 0 : slots.Count - 1;
            int step = toStart ? 1 : -1;

            if (choosingSacrifices)
            {
                bool found = false;
                for (int i = 0; i < slots.Count; i++)
                {
                    int probe = idx + step * i;
                    if (probe < 0 || probe >= slots.Count) break;
                    if (IsSacrificeCandidate(slots[probe])) { idx = probe; found = true; break; }
                }
                if (!found)
                {
                    Speech.Browse(Vocabulary.Hotkeys.NoCardsToSacrifice);
                    return;
                }
            }

            _slotIndex = idx;
            AnnounceCurrentSlot(bm, choosingSacrifices);
        }

        // ----------------------------------------------------------------------
        // Number key in play flow: jump directly to slot N (0-based index).
        // (Session 8 — HSA number-jump convention.)
        // ----------------------------------------------------------------------
        private void JumpToSlot(BoardManager bm, int index, bool choosingSacrifices)
        {
            var slots = bm?.PlayerSlotsCopy;
            if (slots == null || slots.Count == 0) return;

            if (index >= slots.Count)
            {
                Speech.Browse(Vocabulary.Hotkeys.NoSlot(index + 1));
                return;
            }

            _slotIndex = index;
            AnnounceCurrentSlot(bm, choosingSacrifices);
        }

        // ----------------------------------------------------------------------
        // Home/End outside play flow: jump to first/last card in hand.
        // ----------------------------------------------------------------------
        private void JumpHandEdge(bool toStart)
        {
            var hand = Singleton<PlayerHand>.Instance;
            if (hand == null || hand.CardsInHand == null || hand.CardsInHand.Count == 0)
            {
                Speech.Browse(Vocabulary.HandEmpty);
                _handIndex = -1;
                ClearHandHover();
                return;
            }

            _handIndex = toStart ? 0 : hand.CardsInHand.Count - 1;
            _handArrowFromStart = false;
            ReadHandCardAt(hand.CardsInHand[_handIndex]);
        }

        // ----------------------------------------------------------------------
        // Number key outside play flow: jump directly to card N in hand
        // (0-based index; key 1 = first card, key 0 = tenth).
        // ----------------------------------------------------------------------
        private void JumpToHandCard(int index)
        {
            var hand = Singleton<PlayerHand>.Instance;
            if (hand == null || hand.CardsInHand == null || hand.CardsInHand.Count == 0)
            {
                Speech.Browse(Vocabulary.HandEmpty);
                _handIndex = -1;
                ClearHandHover();
                return;
            }

            int count = hand.CardsInHand.Count;
            if (index >= count)
            {
                string cardWord = Vocabulary.CardCount(count);
                Speech.Browse(Vocabulary.Hotkeys.OnlyInHand(cardWord));
                return;
            }

            _handIndex = index;
            _handArrowFromStart = false;
            ReadHandCardAt(hand.CardsInHand[_handIndex]);
        }

        // ----------------------------------------------------------------------
        // Tab: jump to the next card in hand that can currently be played
        // (affordable per GetAffordabilityError), scanning forward from the
        // current browse position and wrapping. (Session 8 — HSA-inspired
        // "next valid play"; exact HSA Tab semantics pending verification.)
        // ----------------------------------------------------------------------
        private void JumpToNextPlayableCard()
        {
            var hand = Singleton<PlayerHand>.Instance;
            if (hand == null || hand.CardsInHand == null || hand.CardsInHand.Count == 0)
            {
                Speech.Browse(Vocabulary.HandEmpty);
                _handIndex = -1;
                ClearHandHover();
                return;
            }

            int count = hand.CardsInHand.Count;

            // 0.7.349 — FROM NOTHING SELECTED, THE SEARCH STARTS AT THE LEFT
            // CARD ITSELF. This started at 0 and probed from 0 + 1, so the
            // leftmost card was the LAST one checked. Zamar: "when I tab first
            // with my opening hand with the smoke in it, it always tabs first
            // to the squirrel not the smoke, even though the smoke is further
            // left in my hand."
            int start = _handIndex < 0 ? -1 : _handIndex;

            for (int i = 1; i <= count; i++)
            {
                int probe = (start + i) % count;
                var candidate = hand.CardsInHand[probe];

                // 0.7.424 — THE GAME'S OWN QUESTION. Zamar: "I was able to tab
                // to a Squirrel even though my board was full... I shouldnt
                // have been able to tab to that Squirrel." This asked only
                // whether the cost could be paid. PlayableCard.CanPlay()
                // (PUBLIC) is what PlayerHand.OnCardSelected asks before it
                // lets a card be played: the cost AND whether there is room
                // on the board once the sacrifices are made.
                bool playable = false;
                try { playable = candidate != null && candidate.CanPlay(); } catch { }
                if (playable)
                {
                    _handIndex = probe;
                    _handArrowFromStart = false;
                    ReadHandCardAt(candidate);
                    return;
                }
            }

            // HIS WORDING, Session 18. "No playable cards." reads as though the
            // hand is empty; the hand may be full and simply unaffordable right
            // now, which is a different fact and the one Tab is answering.
            string none = Vocabulary.Hotkeys.NoCardsInHand;   // Session 32: not const - it is translated

            // 0.7.156 — THE ONE PLACE THE REPEAT GUARD BELONGS.
            //
            // From his 0.7.150 mashing log: Leshy answered the refused play in
            // his own voice — "TO PLAY THAT WOLF YOU'LL NEED TO SACRIFICE 2
            // DIFFERENT CREATURES" — and this line cut him off seven times over.
            // Both lines answer the same question and Leshy's answers it better,
            // so repeating this one on top of him costs the player the only
            // explanation available.
            //
            // Narrow on purpose: only THIS line, only when it would be an exact
            // repeat, only while the game is mid-sentence. A first press still
            // speaks, and a press once Leshy is done still speaks. 0.7.151 tried
            // this as a global rule inside Speak and silenced the campfire's
            // conversation warning; a rule about WHY a line is spoken cannot
            // live where the reason is not known.
            if (CardReader.WouldRepeatOverGameVoice(none))
            {
                Plugin.Log?.LogInfo("IKMA PLAY: no-playable-cards repeat held — the game is answering it.");
                return;
            }

            Speech.Browse(none);
        }

        // ----------------------------------------------------------------------
        // After a draw, watch the card's temporary mods for ~2.5s. If a new
        // ability appears (the totem/woodcarving trigger, heard as an SFX),
        // announce it — grounded in the mod actually landing on the card.
        // ----------------------------------------------------------------------
        private IEnumerator WatchForGrantedAbilities(PlayableCard card)
        {
            var known = new HashSet<Ability>();
            if (card.Info?.Abilities != null)
                foreach (var a in card.Info.Abilities) known.Add(a);
            if (card.TemporaryMods != null)
                foreach (var m in card.TemporaryMods)
                    if (m?.abilities != null)
                        foreach (var a in m.abilities) known.Add(a);

            float timeout = 2.5f;
            while (timeout > 0f && card != null)
            {
                var gained = new List<string>();
                if (card.TemporaryMods != null)
                    foreach (var m in card.TemporaryMods)
                        if (m?.abilities != null)
                            foreach (var a in m.abilities)
                                if (known.Add(a))
                                {
                                    string n = CardReader.GetAbilityName(a);
                                    if (n != null) gained.Add(n);
                                }

                if (gained.Count > 0)
                {
                    string name = Vocabulary.Hotkeys.CardOrCardWord(CardReader.CardName(card));
                    using (Speech.Event(EventKind.Challenges)) Speech.Quiet(Vocabulary.Hotkeys.GainsFromYourTotem(name, gained));
                    yield break;
                }

                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        // ----------------------------------------------------------------------
        // Shift+R. Reads the rulebook entry for each sigil named in the last
        // line spoken. Queued at Info priority — it is a reference lookup the
        // player asked for, not an event, so it waits its turn behind anything
        // describing the game.
        // ----------------------------------------------------------------------
        private void ExplainLastSigils()
        {
            // Zamar, Session 14: "Pressing Shift+R multiple times should only
            // read that first one, stomping the new ones. It shouldn't stomp the
            // oldest, only the newest of itself."
            //
            // The 0.7.39 log has three presses in a row, each cutting the last
            // off mid-explanation — so a long sigil description could never be
            // heard to the end by anyone leaning on the key. A repeat press is
            // not new information; it is the same answer to the same question.
            //
            // The guard is exact rather than timed: CardReader clears this the
            // moment anything else speaks, so Shift+R blocks only ITSELF and
            // stays stompable by everything else, which is the rest of his rule.
            if (CardReader.LastLineWasAbilityLookup)
            {
                Plugin.Log?.LogInfo("IKMA ABILITY: repeat request ignored — the explanation is still the last thing said.");
                return;
            }

            // 0.7.344 — AN ITEM, IF AN ITEM WAS HEARD LAST. The description is
            // the one the I key and the item pickup use (his audited list,
            // PROVISIONAL_LINES #50), so one item never has two explanations.
            var items = CardReader.LastSpokenItemsIfNewest;
            if (items != null)
            {
                var itemParts = new List<string>();
                foreach (var data in items)
                {
                    string d = ItemNameAndDescription(data);
                    if (!string.IsNullOrEmpty(d)) itemParts.Add(d);
                }
                if (itemParts.Count > 0)
                {
                    // PROVISIONAL — PROVISIONAL_LINES #53, built on his
                    // "Recent ability:" lead.
                    string itemLead = Vocabulary.Hotkeys.RecentItemCount(itemParts.Count);
                    Plugin.Log?.LogInfo($"IKMA ABILITY: explaining {itemParts.Count} item(s) on request.");
                    string itemLine = $"{itemLead} {string.Join(" ", itemParts.ToArray())}";
                    CardReader.MarkNextLineAsAbilityLookup(itemLine);
                    Speech.ResultFirst(itemLine);   // 0.7.436 - ahead of what is waiting
                    return;
                }
            }

            var abilities = CardReader.LastSpokenAbilities;
            if (abilities == null || abilities.Count == 0)
            {
                Speech.Browse(Vocabulary.Hotkeys.NoRecentAbilityTo);
                return;
            }

            var parts = new List<string>();
            foreach (var ability in abilities)
            {
                string described = RulebookReader.DescribeAbility(ability);
                if (string.IsNullOrEmpty(described)) continue;

                // THE MOVER'S DIRECTION, WHEN THE SIGIL CAME FROM A CARD.
                // (0.7.179.) Zamar: "Shift + R in the context of the card being
                // in your hand or on the board ... should read which direction
                // Sprinter and Rampager are moving. In the rulebook I think
                // they both point right by default, which doesn't matter, but
                // anywhere else it does matter."
                //
                // The rulebook page (R) is generic text about a sigil with no
                // card behind it. Shift+R explains the sigils in the line just
                // spoken, and those DO have a card — so the lookup can be
                // specific where the page cannot.
                //
                // CardForLastSpokenAbility returns null when the ability was
                // heard with no card behind it, and the description is then
                // exactly what it was before. Nothing is invented for a sigil
                // whose owner is unknown.
                // THE DIRECTION GOES RIGHT AFTER THE SIGIL NAME. (0.7.184.)
                //
                // 0.7.179 appended it to the end of the whole rulebook
                // paragraph. Zamar: "The moving left part should have been
                // right after the name Sprinter, before the full abilities
                // explanation."
                //
                // SAME CORRECTION HE MADE TO THE BOARD READ, and the second
                // time he has had to make it: a modifier goes next to the thing
                // it modifies. At the end of a five-sentence explanation the
                // listener has to carry the whole paragraph and then work out
                // which sigil the trailing clause belonged to — and in a
                // multi-sigil lookup there is more than one candidate.
                //
                // DescribeAbility builds "Name. Explanation", so the insertion
                // point is a parameter on it rather than string surgery here —
                // one place owns that sentence's shape.
                var source = CardReader.CardForLastSpokenAbility(ability);
                if (source != null)
                {
                    string dir = CardReader.DescribeMoveDirection(source);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        // " Moving right." -> "moving right"
                        string clause = dir.Trim().TrimEnd('.');
                        if (clause.Length > 0)
                        {
                            clause = char.ToLowerInvariant(clause[0]) + clause.Substring(1);
                            string withDir = RulebookReader.DescribeAbility(ability, clause);
                            if (!string.IsNullOrEmpty(withDir)) described = withDir;
                        }
                    }
                }

                parts.Add(described);
            }

            if (parts.Count == 0)
            {
                Speech.Browse(Vocabulary.Hotkeys.NoDescriptionAvailableFor);

                return;
            }

            // Named as a lookup rather than dropped in bare. Without the lead
            // the description arrives with no frame — the player asked a
            // question and should hear that this is the answer to it, not a new
            // event in the game.
            //
            // ABILITY, not sigil. Zamar's correction and he is right: a sigil is
            // the stamp that grants an ability, and Airborne is the ability
            // itself. This key looks up abilities. The distinction matters
            // because the rulebook uses both words for different things, and a
            // player learning the game by ear has only our wording to go on.
            string lead = Vocabulary.Hotkeys.RecentAbilityCount(parts.Count);

            Plugin.Log?.LogInfo($"IKMA ABILITY: explaining {parts.Count} ability description(s) on request.");

            // Action priority, not Info. The player pressed a key and is owed an
            // answer now — Zamar asked for it to cut through a card read still
            // in the air, which is exactly what Action does: it interrupts
            // speech from OUTSIDE the queue and never stomps a queued combat
            // result. A lookup displacing a full card description is right; a
            // lookup displacing a death is not.
            string line = $"{lead} {string.Join(" ", parts)}";
            CardReader.MarkNextLineAsAbilityLookup(line);

            // 0.7.436 - AND AHEAD OF THE QUEUE. Session 43: the answer about
            // the enemy totem's Fledgling waited behind "Your turn", the
            // upcoming queue and the hand read. Zamar: "Jump ahead."
            Speech.ResultFirst(line);
        }

        // Is the game refusing player actions right now? PlayerHand.PlayingLocked
        // is PUBLIC (confirmed, dump_card_play.txt) and is the game's own answer.
        // Defaults to NOT locked if the hand is unavailable, so a missing
        // reference can never leave the player unable to press anything.
        private static bool PlayIsLocked()
        {
            try
            {
                var ph = Singleton<PlayerHand>.Instance;
                return ph != null && ph.PlayingLocked;
            }
            catch { return false; }
        }

        // ----------------------------------------------------------------------
        // Hand browse
        // ----------------------------------------------------------------------
        private void BrowseHand(int direction)
        {
            // The exact keypress Zamar reports hitching on. Timed end to end so
            // the log can say whether the cost is here at all — the card read
            // inside reports its own split, so a slow browse with a fast read
            // would point at the hand lookup instead.
            long tBrowse = Perf.Now();
            try { BrowseHandInner(direction); }
            finally { Perf.Report("hand browse keypress", tBrowse); }
        }

        private void BrowseHandInner(int direction)
        {
            var hand = Singleton<PlayerHand>.Instance;
            if (hand == null || hand.CardsInHand == null || hand.CardsInHand.Count == 0)
            {
                Speech.Browse(Vocabulary.HandEmpty);
                _handIndex = -1;
                ClearHandHover();
                return;
            }

            int count = hand.CardsInHand.Count;
            // Session 37, note D6 - Zamar: after a card is played the first
            // arrow lands on card 1; Enter with no arrow still plays the card
            // in the old spot, so the index itself is kept until an arrow.
            if (_handArrowFromStart) { _handArrowFromStart = false; _handIndex = 0; }
            else if (_handIndex < 0) _handIndex = 0;
            else _handIndex = (_handIndex + direction + count) % count;

            ReadHandCardAt(hand.CardsInHand[_handIndex]);
        }

        // ----------------------------------------------------------------------
        // Enter outside play flow: play the currently browsed card.
        // ----------------------------------------------------------------------
        private void TryPlayBrowsedCard(PlayerHand ph)
        {
            if (ph == null || ph.PlayingLocked)
            {
                Speech.Browse(Vocabulary.Hotkeys.CannotPlayACard);
                return;
            }

            if (ph.CardsInHand == null || ph.CardsInHand.Count == 0)
            {
                // Session 9: telling the player to browse a hand that has no
                // cards in it sent them arrowing through silence. Name the real
                // condition instead.
                Speech.Browse(Vocabulary.HandEmpty);
                return;
            }

            if (_handIndex < 0)
            {
                Speech.Browse(Vocabulary.Hotkeys.NoCardSelectedUse);
                return;
            }

            if (_handIndex >= ph.CardsInHand.Count)
                _handIndex = ph.CardsInHand.Count - 1;

            var card = ph.CardsInHand[_handIndex];
            if (card == null)
            {
                Speech.Browse(Vocabulary.NoCardSelected);
                return;
            }

            // Session 13 — the Leshy bark (Master Handoff B7).
            //
            // IKMA no longer decides whether this card can be played. This used
            // to pre-check GetAffordabilityError and refuse in the mod's own
            // flat voice, while left-clicking the same card made Leshy say
            // "NO... BUT YOU CAN PLAY YOUR SQUIRREL." The game had the better
            // answer and we were talking over it. Same principle as the D/S
            // draw change in 0.7.10.
            //
            // Confirmed by dump_card_play.txt BEFORE this was written: there is
            // no drag gesture anywhere on the card path. PlayableCard overrides
            // only OnCursorEnter and OnCursorSelectStart, PlayerHand holds no
            // "card being carried" member, and a click dispatches
            //     CursorSelectStart -> PlayableCard.OnCursorSelectStart
            //                       -> PlayerHand.OnCardSelected(card)
            //                       -> SelectSlotForCard -> ChooseSlot
            // which is exactly where ph.OnCardSelected(card) used to land. So
            // this is additive, not a rewrite: the same success flow, with the
            // game's own gate in front of it. The ChooseSlot and sacrifice
            // narration built on OnCardSelected is untouched.
            //
            // Session 10 still applies: "Playing X." is not spoken here. Both
            // prompts that can follow name the card themselves, and this line
            // used to be cut off mid-word by them every time. Plugin's
            // ChooseSlot patch leads with "Playing X." as one continuous line.
            string cardName = Vocabulary.Hotkeys.ThatCard(CardReader.CardName(card));
            float dialogueMark = TextDisplayer_ShowMessage_Patch.LastLineTime;

            // Session 13, from the 0.7.24 log. Leshy refused Corpse Maggots
            // once, Zamar pressed Enter on it twice more, the game stayed silent
            // on the repeats, and three seconds later IKMA said "Corpse Maggots
            // was not played." — after he had already moved on and played
            // something else. Two faults in one: the answer was redundant
            // (Leshy had explained the reason moments earlier) and it was stale.
            //
            // The fallback exists so Enter never lands in SILENCE. A second
            // press on a card the player was just refused is not silence, so
            // one answer per card is enough. And every attempt supersedes the
            // one before it, so an older fallback withdraws rather than
            // narrating a moment that has passed.
            _playAttempt++;
            int thisAttempt = _playAttempt;

            bool answeredRecently =
                ReferenceEquals(card, _lastRefusedCard) &&
                Time.unscaledTime - _lastRefusedAt < REFUSAL_MEMORY_SECONDS;

            _lastRefusedCard = card;
            _lastRefusedAt   = Time.unscaledTime;

            try
            {
                // Hover first, then click — the exact order a mouse takes.
                // Session 11's Fish Hook lesson: selecting without hovering
                // leaves the logic right and the presentation wherever the
                // mouse happened to be, which reads as a lie on a stream.
                HoverHandCard(card);

                // 0.7.193 — remember where the camera was standing BEFORE this
                // play. See TickViewRestore.
                CaptureViewBeforePlay();

                card.CursorSelectStart();
                card.CursorSelectEnd();
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA PLAY: click failed on {cardName}: {e.Message}");
                Speech.Browse(Vocabulary.Hotkeys.ThatCardCouldNot);
                return;
            }

            Plugin.Log?.LogInfo($"IKMA PLAY: {cardName} clicked via CursorSelectStart/End.");

            _slotIndex = 0;
            StartCoroutine(WatchPlayOutcome(ph, card, cardName, dialogueMark, thisAttempt, answeredRecently));
        }

        // ----------------------------------------------------------------------
        // Move the game's own cursor onto a card in hand, exiting whatever it
        // was on before. Same shape as HoverTargetSlot, and the same reason:
        // the game's presentation follows its cursor, not IKMA's browse index.
        //
        // Session 14: hand browse drives this too, which was the half of B7
        // deliberately split out of the 0.7.20 build so that a double-read
        // would have exactly one suspect. The play click has since shipped and
        // read correctly, so the suspect is cleared and browse can have it.
        //
        // Why it matters beyond presentation: blind players stream to sighted
        // audiences. Arrowing across the hand while no card visibly rises
        // reads on screen as nothing happening, and the narration and the
        // picture stop agreeing.
        // ----------------------------------------------------------------------
        private static PlayableCard _hoveredCard;

        // False until IKMA has taken ownership of the hand hover in this battle.
        // Reset by ClearHandHover, so a new encounter claims it again.
        private static bool _handHoverClaimed;

        // ======================================================================
        // PUT THE CAMERA BACK AFTER A CARD IS PLAYED. (0.7.193.)
        //
        // Zamar, 2026-09-12, with two screenshots:
        //
        //   "When a card is played, the game's camera stays on the top-down view
        //    of the board. Can we make it so when you successfully play a card
        //    it returns the camera to the default view? This shouldn't make a
        //    difference for the blind players but would help their sighted
        //    audience (and me) a lot."
        //
        // WHOSE CAMERA MOVE IS IT. Not IKMA's — IKMA never calls SwitchToView in
        // combat. The SIGILS do: GuardDog.OnOtherCardResolve and
        // CreateEgg.OnResolveOnBoard both open with ViewManager.SwitchToView
        // (dumps/dump_guardian.txt IL_0039, dump_negation.txt IL_0039), because
        // the game wants a sighted player looking at the board while the sigil
        // resolves. With a mouse the player's own hand brings the camera back.
        // On the keyboard nothing ever does, so it sticks.
        //
        // NO VIEW CONSTANT IS NAMED HERE, and that is deliberate.
        // ikma-camera-views is unambiguous: driving ViewManager to a view picked
        // by reading the enum cost two softlocks and a black screen. So the view
        // is not chosen — it is REMEMBERED. CaptureViewBeforePlay reads
        // CurrentView at the moment the card is clicked out of hand, which is a
        // view the game itself was already standing in, and the restore puts it
        // back exactly there. Nothing is guessed and no rung is jumped.
        //
        // WHEN. Not at the click: the sigils that move the camera fire AFTER the
        // card resolves, so restoring immediately would be undone a moment
        // later. GlobalTriggerHandler.StackSize is the game's own count of
        // triggers currently resolving (dumps/dump_negation.txt), so the restore
        // waits for it to reach zero. That is the game answering "has the board
        // finished reacting" rather than a timer guessing at it.
        //
        // If anything is missing or throws, the pending flag clears and the
        // camera stays where it is. A stuck camera is a cosmetic annoyance; a
        // half-driven ViewController is a lost run.
        // ======================================================================
        // 0.7.195 — A WINDOW, NOT A SINGLE SHOT.
        //
        // Zamar: "After playing the Smoke it did not return the camera to where
        // I want it."
        //
        // 0.7.193 restored once, the first frame GlobalTriggerHandler.StackSize
        // read zero. For a card with a sigil that moves the camera, the stack is
        // busy at that moment and the timing works out. For The Smoke — no
        // trigger at all — the stack was ALREADY zero when the card was played,
        // so the restore fired before the game had moved the camera, found
        // nothing to do, and disarmed. The game moved it a moment later and
        // nothing was left watching.
        //
        // So the restore now holds for a short window and puts the camera back
        // whenever it finds it somewhere else, rather than looking exactly once.
        // The window is short enough that it cannot fight the player: IKMA's own
        // view keys do nothing in combat, and a mouse scroll inside the window
        // would be answered once and then left alone.
        private const float VIEW_RESTORE_WINDOW_SECONDS = 2.5f;

        private static bool  _restoreViewPending;
        private static float _restoreViewUntil = -99f;
        private static bool  _haveViewBeforePlay;
        private static View  _viewBeforePlay;

        private static void CaptureViewBeforePlay()
        {
            // A new card is being played. Whatever the last one armed is stale.
            _restoreViewPending = false;

            _haveViewBeforePlay = false;
            try
            {
                var vm = Singleton<ViewManager>.Instance;
                if (vm == null) return;

                _viewBeforePlay     = vm.CurrentView;
                _haveViewBeforePlay = true;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA VIEW: could not read the current view: {e.Message}");
            }
        }

        private static void TickViewRestore()
        {
            if (!_restoreViewPending) return;

            if (Time.unscaledTime > _restoreViewUntil)
            {
                _restoreViewPending = false;
                return;
            }

            // ==================================================================
            // ABANDON THE MOMENT THE PLAYER DOES ANYTHING ELSE. (0.7.197.)
            //
            // Zamar: "Something weird is happening with the sacrifices now with
            // this camera change... If I sacrifice quickly I think it cancels
            // the sacrifice for some reason, which is also cheating."
            //
            // It was the camera restore, and the word "quickly" is the whole
            // diagnosis: the 2.5-second window opened by the PREVIOUS card was
            // still running when the next card's sacrifice began. The restore
            // moved the camera out from under a live sacrifice selection, and
            // the game dropped the selection with it.
            //
            // ikma-camera-views already says why in general terms — a camera
            // move that skips the game's own work leaves the ViewController
            // inconsistent. This is the combat-side instance of it.
            //
            // THE RULE: this feature is a courtesy that runs in the quiet after
            // a card lands, and it yields to everything. BoardManager's own
            // ChoosingSacrifices / ChoosingSlot flags are the game saying it is
            // mid-interaction, and PlayerCanInitiateCombat going false means the
            // bell has rung and the game is moving its own camera for combat —
            // which is Zamar's third report, the same bug wearing a different
            // hat:
            //
            //   "When pressing E to End turn, I believe the camera is supposed
            //    to return to that top-down view before the attacks go through.
            //    It stayed zoomed out."
            //
            // Both are the restore competing with the game. It does not compete.
            // ==================================================================
            try
            {
                var bmNow = Singleton<BoardManager>.Instance;
                // WAIT, do not abandon. The flag is set the instant the
                // placement is injected, and ChoosingSlot can still read true
                // for a frame afterwards — abandoning here would cancel the
                // restore for the very card that armed it. The window expiry
                // ends it if the player is still choosing when time runs out,
                // and CaptureViewBeforePlay clears it outright the moment a new
                // card is picked up, which is the real "started something else"
                // signal.
                if (bmNow != null && (bmNow.ChoosingSacrifices || bmNow.ChoosingSlot))
                    return;

                var tmNow = Singleton<TurnManager>.Instance;
                if (tmNow != null && !tmNow.PlayerCanInitiateCombat)
                {
                    Plugin.Log?.LogInfo(
                        "IKMA VIEW: restore abandoned — the turn is no longer the player's.");
                    _restoreViewPending = false;
                    return;
                }
            }
            catch { _restoreViewPending = false; return; }

            if (!_haveViewBeforePlay)
            {
                _restoreViewPending = false;
                return;
            }

            try
            {
                // Still resolving. Triggers chain, so this can stay above zero
                // for several frames after the card lands.
                var gth = Singleton<GlobalTriggerHandler>.Instance;
                if (gth != null && gth.StackSize > 0) return;

                var vm = Singleton<ViewManager>.Instance;
                if (vm == null) return;

                // Already where it should be. Keep watching until the window
                // closes — the camera may still be moved by something that has
                // not run yet.
                if (vm.CurrentView.Equals(_viewBeforePlay)) return;

                // 0.7.315 — a camera move IKMA made, not something on the
                // table. Behind Plugin.VerboseDiagnostics.
                if (Plugin.VerboseDiagnostics)
                    Plugin.Log?.LogInfo(
                        $"IKMA VIEW: restoring {vm.CurrentView} -> {_viewBeforePlay} after a card was played.");

                vm.SwitchToView(_viewBeforePlay);
            }
            catch (System.Exception e)
            {
                _restoreViewPending = false;
                Plugin.Log?.LogWarning($"IKMA VIEW: restore after play failed: {e.Message}");
            }
        }

        // ----------------------------------------------------------------------
        // Is the game still holding a screen between the fight and the map?
        //
        // 0.7.199 — THE GETTER IS NOT PUBLIC. 0.7.198 read
        // TurnManager.PostBattleSpecialNode directly and the build failed with
        // CS0154, "lacks the get accessor". The dump listed
        // get_PostBattleSpecialNode as a method but the helper that printed it
        // does not print visibility, so its being NONPUBLIC was invisible — a
        // gap in dumps/dump_rarechoice.ps1 worth fixing before the next one.
        //
        // Reflected once and cached, the same shape BoardReader uses for
        // CardDrawPiles.Deck. On any failure this answers "no screen pending",
        // which restores the 0.7.197 behaviour rather than leaving the player
        // in silence — a map announced early is a nuisance, a map never
        // announced is a dead keyboard.
        // ----------------------------------------------------------------------
        private static System.Reflection.PropertyInfo _postBattleNodeProp;
        private static bool _postBattleNodeResolved;

        // 0.7.202 — THE NODE GATE ALONE WAS TOO LATE.
        //
        // Zamar, after 0.7.199 shipped: "Every line between Leshy Lights your
        // candle and Your Reward was inaccurate and should not have been there
        // or read out loud." The map still announced itself, and the log has it
        // arriving BEFORE "boss reward chest opening".
        //
        // The IL says why. Part1BossOpponent.BossDefeatedSequence, in order
        // (dumps/dump_candle.txt):
        //
        //   IL_0192  CandleHolder.ReplenishFlamesSequence
        //   IL_01BB  RunState.playerLives = maxPlayerLives
        //   IL_01CA  TurnManager.set_PostBattleSpecialNode      <- LAST
        //
        // The node is set at the very END of the post-boss script. Everything
        // before it — the mask, the scenery teardown, the relight, the dialogue
        // — happens while the node is still null, which is exactly the window
        // he was hearing the map in.
        //
        // So the latch opens at the START of that coroutine and hands over to
        // the node gate once the node exists. A baton pass: the latch covers
        // from the boss dying to the node being set, the node covers from there
        // to the reward screen closing, and neither has to guess at a duration.
        internal static bool PostBossScriptRunning;

        // Session 40: when the battle was first seen over with the latch still
        // up, and the longest it may then wait for the reward screen. The real
        // wait is about a fifth of a second (see the note in the method).
        private static float _postBossWaitSince = -1f;
        private const float POST_BOSS_WAIT_MAX = 10f;

        /// <summary>
        /// Session 40: the reward screen's reader is up, so the latch has
        /// nothing left to cover. Called from the card-choice branch of
        /// UpdateInner, because that branch returns before the map gate and
        /// PostBattleScreenPending is never reached while a choice is open.
        /// </summary>
        private static void ReleasePostBossLatchToReward()
        {
            if (!PostBossScriptRunning) return;
            PostBossScriptRunning = false;
            _postBossWaitSince = -1f;
            if (Plugin.VerboseDiagnostics)
                Plugin.Log?.LogInfo("IKMA MAP: post-boss latch handed over to the reward screen.");
        }

        private static bool PostBattleScreenPending()
        {
            bool nodePending = false;
            try { nodePending = PostBattleNode() != null; } catch { }

            // The latch stands down as soon as the node it is covering for
            // exists; from that point the property below is the real answer.
            if (PostBossScriptRunning && nodePending)
            {
                PostBossScriptRunning = false;
                Plugin.Log?.LogInfo(
                    "IKMA MAP: post-boss latch handed over to the node gate.");
            }

            // ==================================================================
            // AND IT STANDS DOWN WHEN THERE IS NOTHING LEFT TO COVER FOR.
            // (0.7.266 — this stranded Zamar's keyboard.)
            //
            // He beat the Angler, took the rare reward, walked to the Snow Line
            // and had no keys at all except Escape. This method is the cause:
            // the line that calls it returns out of the whole key handler, so a
            // latch stuck true is a dead keyboard, not a late map line.
            //
            // THE BATON WAS DROPPED BECAUSE NOBODY WAS LOOKING WHEN IT PASSED.
            // TurnManager.PostBattleSpecialNode is set on the LAST instruction
            // of BossDefeatedSequence and cleared by TransitionToNextGameState
            // a moment later, and this method is only reached once
            // _mapReturnPending is armed — which happens when the battle goes
            // inactive, AFTER that transition. In his run the property was
            // never non-null on any frame this code ran, so the hand-over
            // above could not happen and the latch stayed up for good.
            //
            // A latch that covers for a condition has to stand down when the
            // condition can no longer be observed. If the battle is over, no
            // node is pending and no reader has claimed the screen, then
            // whatever this latch was bridging to has already been and gone.
            // The 0.7.202 case it exists for is unaffected: during the post-boss
            // script the battle is still active, so this branch cannot fire.
            // ==================================================================
            //
            // ==================================================================
            // SESSION 40 - THE STAND-DOWN ABOVE WAS FIRING AFTER EVERY BOSS,
            // AND A MOMENT TOO SOON.
            //
            // Zamar's logs (0.7.424, 0.7.425, 0.7.426) all carry the warning
            // this branch wrote, and the 0.7.426 one shows what it cost:
            //
            //   IKMA MAP: post-boss latch stood down ... hand-over ... missed.
            //   IKMA UNREAD: no reader for this screen. node='Item pickup'
            //   IKMA CHOICE: boss reward chest opening.
            //
            // The source says why (TurnManager.CleanupPhase, lines 366-382):
            // the opponent is destroyed, and TransitionToNextGameState hands
            // the node to GameFlowManager and CLEARS it in the same call. So
            // the node is set and cleared entirely while the battle is still
            // active by IKMA's test, and this method only runs once it is not.
            // The node hand-over at the top can never happen; "missed" was the
            // normal path, not a fault.
            //
            // And between that frame and the chest there is a real gap:
            // GameFlowManager.TransitionFrom(CardBattle) waits 0.2 seconds
            // before SpecialNodeHandler starts the rare card choice. Standing
            // down at the first of those frames left the screen unowned for
            // the rest of them - the "no reader for this screen" watcher armed
            // itself with the map node from BEFORE the boss, and Space or H in
            // that gap would have been answered with it.
            //
            // So the latch now holds until what it was covering for ARRIVES:
            // the reward screen's reader. The 0.7.266 stranding is still
            // closed, by the two things that were true in that run and are
            // never true in the gap: the map is usable again, or far longer
            // has passed than the hand-over takes.
            // ==================================================================
            if (CardChoiceReader.Active) ReleasePostBossLatchToReward();

            if (PostBossScriptRunning &&
                !nodePending &&
                !IsBattleActive())
            {
                if (_postBossWaitSince < 0f) _postBossWaitSince = Time.unscaledTime;
                bool mapBack  = MapReader.MapAvailable();
                bool tooLong  = Time.unscaledTime - _postBossWaitSince > POST_BOSS_WAIT_MAX;
                if (mapBack || tooLong)
                {
                    PostBossScriptRunning = false;
                    _postBossWaitSince = -1f;
                    Plugin.Log?.LogWarning(
                        "IKMA MAP: post-boss latch stood down — the battle is over and no reward " +
                        "screen claimed it (" + (mapBack ? "the map is usable again" : "waited too long") + ").");
                }
            }
            else if (!PostBossScriptRunning) _postBossWaitSince = -1f;

            if (PostBossScriptRunning) return true;
            if (CardChoiceReader.Active) return true;

            return nodePending;
        }

        private static object PostBattleNode()
        {
            try
            {
                if (!_postBattleNodeResolved)
                {
                    _postBattleNodeResolved = true;
                    _postBattleNodeProp = typeof(TurnManager).GetProperty(
                        "PostBattleSpecialNode",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);

                    if (_postBattleNodeProp == null)
                        Plugin.Log?.LogWarning(
                            "IKMA MAP: TurnManager.PostBattleSpecialNode did not resolve — " +
                            "the map may announce before a post-battle screen is done.");
                }

                if (_postBattleNodeProp == null) return null;

                var tm = Singleton<TurnManager>.Instance;
                if (tm == null) return null;

                return _postBattleNodeProp.GetValue(tm, null);
            }
            catch { return null; }
        }

        private static void HoverHandCard(PlayableCard entering, bool force = false)
        {
            // PlayableCard.OnCursorEnter is patched in Plugin.cs to read the
            // card aloud, which is right for a mouse and wrong for us — the
            // browse read already happened when the player arrowed here. The
            // scene gate in that patch would suppress it anyway, but that is two
            // string literals in two files agreeing by coincidence. Say it out
            // loud instead, so the guarantee survives someone editing either
            // one. This also keeps IKMA's own calls out of the Bug 2 hover-leak
            // diagnostic, which is still waiting to catch a real leak.
            //
            // Session 14: browse now hovers, so by the time the play click
            // calls this the card is almost always already hovered. Re-entering
            // a cursor the game already has raises the card a second time for
            // no reason — visible on a stream as a twitch, and free to avoid.
            // The call stays in TryPlayBrowsedCard rather than being deleted:
            // play must not depend on browse having run first.
            // 0.7.197 — THE EARLY RETURN IS RIGHT FOR PLAY AND WRONG FOR BROWSE.
            //
            // Zamar: "if I tab when the smoke is the only playable card in my
            // hand, it wasn't visually increasing the Smoke. I think it like
            // doesn't do the equivalent of a mouse hover in that state."
            //
            // Exactly right. Tab lands on the one playable card, which is the
            // card _hoveredCard already names, and this returns without touching
            // the game. IKMA's bookkeeping says the card is hovered; the card on
            // the table is not raised, because something dropped the visual
            // without telling us.
            //
            // The guard stays for the PLAY path, where it earned its place —
            // re-entering a cursor the game already has raises the card twice
            // and reads as a twitch on a stream. But a browse keypress must
            // always leave the board looking the way it says it does, so it
            // asks for the hover again: exit first, then enter, because
            // CursorEnter on a card the game thinks is already entered does
            // nothing at all.
            if (entering != null && ReferenceEquals(entering, _hoveredCard))
            {
                if (!force) return;

                try { entering.CursorExit(); } catch { }
                _hoveredCard = null;
            }

            SuppressHoverRead = true;
            try
            {
                // Session 14, from the 0.7.37 playtest: combat opened with the
                // game's own cursor already resting on Mantis God, IKMA raised
                // the Squirrel, and TWO cards sat highlighted at once. IKMA only
                // tracked hovers it had caused, so a hover that was already there
                // when the battle started was invisible to it and never released.
                //
                // On the first hand hover of a battle, exit every OTHER card in
                // hand before entering this one. Cheap — a hand is a handful of
                // cards — and it runs once per battle rather than per keypress.
                if (!_handHoverClaimed)
                {
                    _handHoverClaimed = true;
                    try
                    {
                        var hand = Singleton<PlayerHand>.Instance;
                        var cards = hand?.CardsInHand;
                        if (cards != null)
                        {
                            for (int i = 0; i < cards.Count; i++)
                            {
                                var other = cards[i];
                                if (other != null && !ReferenceEquals(other, entering))
                                    other.CursorExit();
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        Plugin.Log?.LogWarning($"IKMA HOVER: initial clear failed: {e.Message}");
                    }
                }

                // Only ever un-hover a card that is still IN HAND. A card
                // that has been played is on the board now and belongs to
                // whatever presentation the board is running; reaching in to
                // exit a cursor it never asked for is how a stream ends up
                // showing a card flicker for no reason the viewer can see.
                if (_hoveredCard != null && _hoveredCard != entering && _hoveredCard.InHand)
                    _hoveredCard.CursorExit();

                if (entering != null)
                    entering.CursorEnter();

                _hoveredCard = entering;
            }
            catch (System.Exception e)
            {
                // Never let a presentation detail break card play.
                Plugin.Log?.LogWarning($"IKMA PLAY: hover sync failed: {e.Message}");
            }
            finally
            {
                SuppressHoverRead = false;
            }
        }

        // Read by Plugin.cs's PlayableCard_OnCursorEnter_Patch. True only for
        // the instant IKMA is moving the game's cursor itself.
        internal static bool SuppressHoverRead = false;

        // ----------------------------------------------------------------------
        // Drop the hand hover entirely. Called when the hand is empty, and from
        // Plugin's scene-loaded hook.
        //
        // A hover left standing across a scene change points at a PlayableCard
        // from a battle that is over. The reference may be destroyed, and even
        // if it survives, the next card the player arrows to would exit a
        // cursor belonging to nothing on screen.
        // ----------------------------------------------------------------------
        internal static void ClearSlotHover()
        {
            if (_hoveredSlot == null) return;
            try { HoverTargetSlot(_hoveredSlot, null); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA HOVER: slot clear failed: {e.Message}");
            }
            _hoveredSlot = null;
        }

        internal static void ClearHandHover()
        {
            SuppressHoverRead = true;
            try
            {
                if (_hoveredCard != null && _hoveredCard.InHand)
                    _hoveredCard.CursorExit();
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA HOVER: clear failed: {e.Message}");
            }
            finally
            {
                _hoveredCard = null;
                _handHoverClaimed = false;
                SuppressHoverRead = false;
            }
        }

        // ----------------------------------------------------------------------
        // The one way IKMA lands on a card in hand: move the game's cursor
        // there, then read it.
        //
        // Hover FIRST, read second, and that order is deliberate. If the
        // SuppressHoverRead guard ever failed, the game's own hover read would
        // fire — and being first, it gets stomped by the browse read that
        // follows, which interrupts. The player still hears exactly one line,
        // and it is the right one. Reading first would let a leaked hover read
        // cut off a correct card read mid-word, which is the worst of the two
        // failures and the one Zamar would hear.
        // ----------------------------------------------------------------------
        private void ReadHandCardAt(PlayableCard card)
        {
            if (card == null) return;
            // force: a browse keypress re-asserts the hover even on the card it
            // is already sitting on. See HoverHandCard.
            HoverHandCard(card, force: true);
            CardReader.ReadCard(card, includeCost: true);
        }

        // ----------------------------------------------------------------------
        // After the click, watch for the game's answer.
        //
        // Removing the pre-check means IKMA no longer knows why a play failed,
        // and it should not — the game states its own reasons. But it cannot
        // leave Enter landing in total silence either, because a blind player
        // has no way to tell a refused play from a key that never registered.
        // Leshy's barks are not guaranteed on every refusal.
        //
        // So: watch, and speak only if nothing at all happened. Every exit here
        // is read from live game state, never from the keypress.
        // ----------------------------------------------------------------------
        // Was 1.2s at 0.7.20 and that was too tight. Zamar's test log caught
        // the exact failure: the fallback line fired, and only THEN did
        // "YOU DON'T HAVE 5 BONES YET" arrive. Leshy's refusal takes longer than
        // a second to reach TextDisplayer, so the safety net was catching the
        // very thing it exists to let through — IKMA talking over the game's own
        // voice, which is the whole reason this build was written.
        //
        // Three separate things now have to fail before the player hears the
        // fallback, because guessing one number wrong is what caused this:
        //   1. this window, widened;
        //   2. the line is DEFERRED, so it re-checks at speak time and withdraws
        //      itself if the game spoke while it sat in the queue;
        //   3. character dialogue now inserts ahead of pending Info lines, so a
        //      late bark overtakes the fallback rather than queueing behind it.
        private const float PLAY_WATCH_SECONDS = 3.0f;

        // The card most recently refused, and when. Used to keep the fallback
        // to one answer per card rather than one per keypress.
        private static PlayableCard _lastRefusedCard;
        private static float _lastRefusedAt = -999f;
        private const float REFUSAL_MEMORY_SECONDS = 8f;

        // Increments on every play attempt. A watcher whose number is no longer
        // the current one is describing a moment the player has left.
        private static int _playAttempt;

        private IEnumerator WatchPlayOutcome(PlayerHand ph, PlayableCard card, string cardName,
                                             float dialogueMark, int attempt, bool answeredRecently)
        {
            var bm = Singleton<BoardManager>.Instance;
            float elapsed = 0f;

            while (elapsed < PLAY_WATCH_SECONDS)
            {
                if (card == null) yield break;

                // The game took the card. Three separate signals because the
                // accepted play has two shapes — straight to slot choice, or via
                // a sacrifice demand first — and the card leaves the hand later
                // than either. Any one of them means something else owns the
                // narration from here.
                if (ph != null && ph.ChoosingSlotCard == card) yield break;
                if (bm != null && bm.CurrentSacrificeDemandingCard == card) yield break;
                if (!card.InHand) yield break;

                // The game answered in its own voice. That is the whole point of
                // this build — say nothing on top of it.
                if (TextDisplayer_ShowMessage_Patch.LastLineTime > dialogueMark) yield break;

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Nothing took the card and nobody spoke. State only what was
            // observed. No cause is named, because the game declined to name
            // one and inventing a plausible reason is exactly the failure the
            // Airborne bug taught this project to avoid.
            //
            // Deferred, not fixed: the announcer composes this when it reaches
            // the front of the queue and silently skips a provider that returns
            // empty. So if the game finds its voice in the meantime, this line
            // is never spoken at all.
            if (answeredRecently)
            {
                Plugin.Log?.LogInfo($"IKMA PLAY: {cardName} — no answer, but it was already refused moments ago. Staying quiet.");
                yield break;
            }

            Plugin.Log?.LogInfo($"IKMA PLAY: {cardName} — no answer in {PLAY_WATCH_SECONDS}s, queueing fallback.");

            Speech.Commentary(() =>
            {
                if (attempt != _playAttempt)
                {
                    Plugin.Log?.LogInfo($"IKMA PLAY: fallback for {cardName} withdrawn — superseded by a later attempt.");
                    return null;
                }

                if (TextDisplayer_ShowMessage_Patch.LastLineTime > dialogueMark)
                {
                    Plugin.Log?.LogInfo($"IKMA PLAY: fallback for {cardName} withdrawn — the game spoke first.");
                    return null;
                }

                Plugin.Log?.LogInfo($"IKMA PLAY: {cardName} was not played and the game said nothing.");
                return Vocabulary.Hotkeys.WasNotPlayed(cardName);
            });
        }

        // ----------------------------------------------------------------------
        // Enter during play flow: confirm the currently focused slot.
        // ----------------------------------------------------------------------
        // ======================================================================
        // AN EARLY PRESS IS DROPPED. NOT REFUSED, NOT HELD. (0.7.142.)
        //
        // *** THIS IS THE THIRD ATTEMPT AT THE SAME 300 MILLISECONDS AND THE
        // *** SECOND ONE THAT MADE THINGS WORSE. The history is the argument
        // *** for how small this version is.
        //
        //   0.7.140  Refused the press if slot.Chooseable was false.
        //            Chooseable is the PLACEMENT flag and is never true during a
        //            sacrifice, so the key died for the whole selection.
        //
        //   0.7.141  Held the press and fired it 150ms later. AND IT LET HIM
        //            CHEAT. His log:
        //
        //              SACRIFICE: focus snapped to slot 2
        //              sacrifice pressed 138ms ... held sacrifice released.
        //              SPEAK: Black Goat played in Slot 2.      <- no sacrifice
        //              ...
        //              held sacrifice released.
        //              SPEAK: Wolf played in Slot 2.
        //
        //            By the time a held press fired, the game had moved from
        //            CHOOSING A SACRIFICE to CHOOSING A SLOT — and OnSlotSelected
        //            means different things in those two phases. A press meant
        //            as a sacrifice landed as a placement, so the cost was never
        //            paid. He got two Wolves, four blood, off one Squirrel and
        //            one Black Goat.
        //
        // THE LESSON, AND IT IS THE EXPENSIVE ONE: A DEFERRED INPUT IS NOT THE
        // SAME INPUT. It carries the player's intent into a state that may no
        // longer match it, and OnSlotSelected is exactly the kind of call whose
        // meaning depends entirely on when it arrives. My "is this still wanted"
        // check accepted EITHER phase, which is how a sacrifice became a play.
        //
        // SO THE PRESS IS DROPPED. The window is 300ms, it exists only after a
        // prompt the player is still hearing, and pressing again costs him one
        // keystroke. That is the whole cost, and it cannot crash the coroutine,
        // cannot dead-key, and cannot spend the wrong thing.
        //
        // Silent on purpose: the prompt is mid-sentence at this point and the
        // honest thing is to let it finish rather than talk over it to report a
        // keystroke that did nothing. The log carries it.
        //
        // STILL INSTRUMENTED. Every drop records how far into the sequence it
        // landed and what Chooseable said, because the real "ready" signal for a
        // sacrifice is STILL UNKNOWN and needs a dump, not a fourth guess.
        // ======================================================================
        private const float PLAY_FLOW_SETTLE = 0.30f;

        private float _playFlowEnteredAt = -1f;
        private bool  _wasInPlayFlow;

        /// <summary>Called every frame from Update so the window starts when the game does.</summary>
        private void TrackPlayFlowEntry(bool inPlayFlow)
        {
            if (inPlayFlow && !_wasInPlayFlow) _playFlowEnteredAt = Time.unscaledTime;

            // The candidate list belongs to one ChooseSacrificesForCard call.
            // Dropped the moment the sequence ends so it can never be consulted
            // for the next card — a stale list would hide slots that are valid
            // again, which is the failure mode worth guarding here.
            if (!inPlayFlow && _wasInPlayFlow)
            {
                _sacrificeCandidates = null;
                _lastInjectedFrame.Clear();
            }

            _wasInPlayFlow = inPlayFlow;
        }

        private bool TooSoonToAct(CardSlot slot, string verb)
        {
            if (_playFlowEnteredAt < 0f) return false;

            float since = Time.unscaledTime - _playFlowEnteredAt;
            if (since >= PLAY_FLOW_SETTLE) return false;

            bool chooseable = false;
            try { chooseable = slot != null && slot.Chooseable; } catch { }

            Plugin.Log?.LogInfo(
                $"IKMA PLAY: {verb} pressed {since * 1000f:F0}ms into the sequence — " +
                $"dropped. slot.Chooseable={chooseable}.");

            return true;
        }

        // True only inside DirectSlotPick: the player named this slot, so
        // ConfirmSelection must not substitute another one for it. (0.7.353.)
        private bool _directPick;

        private void DirectSlotPick(BoardManager bm, PlayerHand ph, int index,
                                    bool choosingSlot, bool choosingSacrifices)
        {
            var slots = bm?.PlayerSlotsCopy;
            if (slots == null || slots.Count == 0) return;
            if (index >= slots.Count)
            {
                Speech.Browse(Vocabulary.Hotkeys.NoSlot(index + 1));
                return;
            }

            // 0.7.357 — TOO SOON IS WAITED OUT, NOT DROPPED. A press inside the
            // first 0.3s of the sequence was silently discarded (the game's
            // slots are not chooseable yet), so Zamar's first "3" did nothing
            // and he pressed again. The arrows+Enter path keeps its drop; a
            // number key names its slot outright, so it is retried once the
            // settle window has passed, if the game is still asking.
            if (_playFlowEnteredAt >= 0f)
            {
                float wait = PLAY_FLOW_SETTLE - (Time.unscaledTime - _playFlowEnteredAt);
                if (wait > 0f)
                {
                    Plugin.Log?.LogInfo($"IKMA PLAY: number key {index + 1} held {wait * 1000f:F0}ms for the sequence to settle.");
                    StartCoroutine(RetryDirectPick(index, wait + 0.02f));
                    return;
                }
            }

            _slotIndex = index;

            // The screen follows the key, as it follows the arrows.
            HoverTargetSlot(_hoveredSlot, slots[index]);
            _hoveredSlot = slots[index];

            Plugin.Log?.LogInfo(
                $"IKMA PLAY: number key in play flow — sacrifices={choosingSacrifices}, " +
                $"slot={choosingSlot}, picked slot {index + 1}.");

            _directPick = true;
            try { ConfirmSelection(choosingSlot, choosingSacrifices, bm, ph); }
            finally { _directPick = false; }
        }

        private IEnumerator RetryDirectPick(int index, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            var bm = Singleton<BoardManager>.Instance;
            var ph = Singleton<PlayerHand>.Instance;
            if (bm == null) yield break;
            bool slotNow = bm.ChoosingSlot, sacNow = bm.ChoosingSacrifices;
            if (!slotNow && !sacNow) yield break;
            DirectSlotPick(bm, ph, index, slotNow, sacNow);
        }

        private void ConfirmSelection(bool choosingSlot, bool choosingSacrifices, BoardManager bm, PlayerHand ph)
        {
            var slots = bm.PlayerSlotsCopy;
            if (slots == null || _slotIndex >= slots.Count) return;

            var slot = slots[_slotIndex];

            if (choosingSacrifices)
            {
                if (slot.Card == null)
                {
                    // Session 9: if exactly one slot on the board can be
                    // sacrificed, Enter means that one — there is nothing else
                    // it could mean, and making the player hunt for it is
                    // busywork. With several candidates, say so and let them
                    // choose rather than picking for them.
                    // Counted from the game's own candidate list, not from
                    // "occupied" — a card already marked for this sacrifice is
                    // still sitting in its slot, and offering it again is how a
                    // cost gets paid twice with one card.
                    int occupied = 0, onlyIndex = -1;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        if (IsSacrificeCandidate(slots[i])) { occupied++; onlyIndex = i; }
                    }

                    if (occupied == 1 && !_directPick)
                    {
                        _slotIndex = onlyIndex;
                        slot = slots[onlyIndex];
                    }
                    else if (occupied == 0)
                    {
                        Speech.Browse(Vocabulary.Hotkeys.NoCardsToSacrifice);
                        return;
                    }
                    else
                    {
                        Speech.Browse(Vocabulary.Hotkeys.NoCardInThis);
                        return;
                    }
                }
                // Session 10 note 3: "Sacrificing X." was announcing the
                // keypress, not the outcome — the exact thing the hard rules
                // forbid — and it arrived a beat before "X is sacrificed." said
                // the same thing truthfully. Removed. Plugin's Die patch now
                // speaks one merged line: "Squirrel is sacrificed. Received 1
                // bone."
                //
                // DROPPED if the sequence only just started. See TooSoonToAct:
                // injecting into a coroutine that is still building its list
                // killed his run, refusing outright broke the key, and holding
                // the press let him cheat.
                if (TooSoonToAct(slot, "sacrifice")) return;

                // EVERY INJECTION IS LOGGED, NOT JUST THE REFUSED ONES.
                // (0.7.145.)
                //
                // His 0.7.144 log can show a sacrifice being DROPPED and cannot
                // show one being ACCEPTED — so a run where he still got two
                // Wolves out reads as a card being played with no sacrifice at
                // all, when the truth might be two accepted injections that were
                // never printed.
                //
                // That is a hole in the instrumentation, not a mystery about the
                // game, and it is the reason this bug has survived three fixes:
                // every diagnosis so far has been made from a log that omits the
                // event in question. Count what actually goes in.
                //
                // The candidate count comes with it, because it is the game's own
                // list shrinking — one injection should remove exactly one slot,
                // and any other number is the bug naming itself.
                // THE SAME GATE THE BROWSE USES. Enter can reach a slot the
                // arrows would refuse — the focus may already be sitting on a
                // spent card when the sequence starts — so the check belongs
                // here too, not only in navigation. His six injections all came
                // through this path.
                if (!IsSacrificeCandidate(slot))
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA PLAY: slot {_slotIndex + 1} is not a sacrifice candidate — ignored.");
                    // A number key names the slot outright, so silence would
                    // be a dead key: say what is there instead. (0.7.353.)
                    if (_directPick) AnnounceCurrentSlot(bm, choosingSacrifices);
                    return;
                }

                // ==================================================================
                // ONE PRESS TOO MANY UN-MARKS A SACRIFICE. (0.7.149.)
                //
                // Zamar: "I still ended with a squirrel and wolf. That should be
                // impossible." His 0.7.148 log finally shows the whole thing,
                // because the blood markers are now written down:
                //
                //   sacrifice INJECTED on slot 3 ('Squirrel')  markers 0 -> 1
                //   sacrifice INJECTED on slot 4 ('Wolf')      markers 1 -> 2
                //   sacrifice INJECTED on slot 3 ('Squirrel')  <- one press later
                //   ... SetSacrificeMarkersValue is never called again ...
                //   Wolf is sacrificed.        <- and the Squirrel is still alive
                //
                // At 2 of 2 the game had what it asked for and the marking loop
                // was on its way out. The third press landed in that window and
                // TOGGLED the Squirrel back off — a click on a marked card
                // un-marks it, which is a real and correct game behaviour — so
                // the blood had already been counted but the card never died.
                // Squirrel and Wolf both on the board, exactly as he described.
                //
                // This is the same shape as every earlier attempt and the reason
                // none of them worked: they all asked "is this CARD still a legal
                // sacrifice", and it was. The question that answers it is "does
                // the game still WANT one", and the markers are the game's own
                // answer — the same number the sighted player is reading.
                //
                // Once satisfied, IKMA sends nothing. It does not un-mark on his
                // behalf, and mashing can no longer undo a sacrifice already
                // paid for. Silent: at his mashing rate a spoken line per
                // blocked press would bury the sacrifice result itself.
                // ==================================================================
                if (SacrificeProgress.Satisfied())
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA PLAY: sacrifice press ignored — {SacrificeProgress.Value} of " +
                        $"{SacrificeProgress.Cost} blood already marked.");
                    return;
                }

                int before = _sacrificeCandidates == null ? -1 : _sacrificeCandidates.Count;
                string sacName = null;
                try { sacName = CardReader.CardName(slot.Card?.Info); } catch { }
                // Session 37, note D4: what this sacrifice is worth, read before
                // the click (the game's GetValueOfSacrifices: Worthy Sacrifice = 3).
                int sacWorth = 1;
                try { if (slot.Card != null && slot.Card.HasAbility(Ability.TripleBlood)) sacWorth = 3; } catch { }

                // ==================================================================
                // SESSION 46 - "SACRIFICED" FOR A CARD THE GAME REFUSED. (0.7.451.)
                //
                // Found with the test driver on 0.7.450, twice: with two blood
                // to pay, Enter on a Boulder and later on a Rabbit Pelt said
                //   "Rabbit Pelt in slot 2 sacrificed. Choose 1 additional
                //    sacrifice."
                // and then Leshy's own "A RABBIT PELT DOES NOT BLEED." The card
                // was never marked. The line below was said for the PRESS, not
                // for what the game did with it - announcing what was attempted
                // rather than what is true.
                //
                // The game decides this in BoardManager.OnSlotSelected, and it
                // asks one PUBLIC property to do it: PlayableCard.
                // CanBeSacrificed (terrain, pelts and face-down cards are not).
                // Same question, asked before the click because the click may
                // change the board. When the answer is no, IKMA says nothing:
                // the game has a voice for the refusal.
                // ==================================================================
                bool gameTakesSacrifice = true;
                try { if (slot.Card != null) gameTakesSacrifice = slot.Card.CanBeSacrificed; } catch { }
                int bloodBefore = SacrificeProgress.Value < 0 ? 0 : SacrificeProgress.Value;
                int sacSlot = _slotIndex + 1;

                _lastInjectedFrame[slot.Index] = Time.frameCount;

                // ==================================================================
                // CLICK THE SLOT. DO NOT CALL THE HANDLER. (0.7.147.)
                //
                // FIVE ATTEMPTS AT THIS BUG AND THE FIRST RULE WAS THE ANSWER
                // THE WHOLE TIME: "We should never ever be doing things
                // ourselves. Our mod should always follow what the game does...
                // When we try to tell it what to do, it breaks or cheats."
                //
                // It broke AND cheated. His 0.7.146 log: 35 injections, one
                // blocked, the same Squirrel spent over and over and never going
                // Dead — and the coroutine dying on "Collection was modified".
                //
                // bm.OnSlotSelected(slot) is the HANDLER. It is what runs at the
                // END of a click, after the game has decided the click was legal.
                // Calling it directly skips every one of those decisions, which
                // is why a keyboard could do what a mouse provably cannot: Zamar
                // mashing a mouse on that board does not crash the game.
                //
                // CardSlot OVERRIDES OnCursorSelectStart (dump_sacrifice.txt's
                // audit), so a slot is a clickable thing like every other one in
                // this mod. CursorSelectStart/CursorSelectEnd is the universal
                // primitive the hard rules say to reach for FIRST for any new
                // clickable — the same pair that fixed card play and the Leshy
                // bark, where the identical mistake was made and corrected in
                // Session 13.
                //
                // Whatever guard stops a second mouse click from double-spending
                // a sacrifice now runs for the keyboard too, because it is the
                // same code path. IKMA stops deciding when a press is legal and
                // goes back to asking.
                //
                // The Dead check and the frame guard above stay. They are cheap,
                // they are honest, and they are no longer load bearing.
                // ==================================================================
                try
                {
                    slot.CursorSelectStart();
                    slot.CursorSelectEnd();
                }
                catch (System.Exception e)
                {
                    Plugin.Log?.LogWarning($"IKMA PLAY: sacrifice click threw {e.GetType().Name}.");
                }

                int after = _sacrificeCandidates == null ? -1 : _sacrificeCandidates.Count;
                Plugin.Log?.LogInfo(
                    $"IKMA PLAY: sacrifice INJECTED on slot {_slotIndex + 1} " +
                    $"('{sacName ?? "?"}') — candidates {before} -> {after}.");
                Speech.NotePlayerActed("a sacrifice was chosen");   // 0.7.341

                // Session 37, note D4 - Zamar: "One line per button press." While
                // more blood is still wanted, say which card was marked and how
                // many more; the last press is answered by the sacrifice line.
                int more = SacrificeProgress.Cost - (bloodBefore + sacWorth);
                if (!gameTakesSacrifice)
                    Plugin.Log?.LogInfo(
                        $"IKMA PLAY: '{sacName ?? "?"}' cannot be sacrificed (the game's CanBeSacrificed) - " +
                        "no \"sacrificed\" line; the game answers the press itself.");
                else if (SacrificeProgress.Cost > 0 && more > 0 && !string.IsNullOrEmpty(sacName))
                    Speech.Confirm(Vocabulary.Hotkeys.SacrificedChooseMore(sacName, sacSlot, more));
            }
            else if (choosingSlot)
            {
                if (slot.Card != null)
                {
                    Speech.Browse(Vocabulary.Hotkeys.SlotIsOccupiedBy(_slotIndex + 1, CardReader.CardName(slot.Card)));
                    return;
                }
                // THE GATE COMES BEFORE THE ANNOUNCEMENT. Same gate as the
                // sacrifice branch — placement runs through ChooseSlot, another
                // coroutine with another list, and there is no reason to think
                // it is immune to what killed his run.
                //
                // Order matters here and I got it wrong on the first pass:
                // gating AFTER the "played in Slot N" line would announce a
                // placement that then never happened, which is the confidently
                // wrong announcement this project treats as worse than silence.
                if (TooSoonToAct(slot, "place a card")) return;

                var placing = ph?.ChoosingSlotCard;
                string cardName = Vocabulary.Hotkeys.CardOrLowercaseCard(CardReader.CardName(placing?.Info));

                // Logged for the same reason as the sacrifice injection above:
                // "X played in Slot N" is spoken here whether or not the game
                // accepted anything, so the log has to say that IKMA is the one
                // who pressed it.
                Plugin.Log?.LogInfo(
                    $"IKMA PLAY: placement INJECTED on slot {_slotIndex + 1} for '{cardName}'.");
                Speech.NotePlayerActed("a slot was chosen");   // 0.7.341

                // 0.7.189 — THIS LINE IS NOT INTERRUPTIBLE. A Guardian trigger
                // landed on top of it in the Prospector fight and cut it in
                // half. A card entering the board is a game action objectively
                // taken; whatever the board does in response can wait a beat.
                //
                // 0.7.207 — that protection, and the interrupt itself, are now
                // the Confirmation policy in Speech.cs. One kind, one rule.
                // 0.7.268 — tell any pending "choose a slot" prompt that the
                // slot has been chosen. The game clears ChoosingSlotCard a
                // frame or two later, inside the coroutine this injection
                // starts, which is late enough for a queued prompt to test it
                // and still come out true. See SlotPromptState.
                SlotPromptState.NotePlaced();
                _handArrowFromStart = true;   // Session 37, note D6

                // 0.7.439 - a card whose Trinket Bearer is about to hand over
                // an item: the confirmation waits a moment and is spoken as
                // one line with the item. See PlayConfirmHold.
                string playedLine = Vocabulary.Hotkeys.PlayedInSlot(cardName, _slotIndex + 1);
                if (!PlayConfirmHold.TryHold(placing, playedLine))
                using (Speech.Event(EventKind.CardPlayed, EventSource.CurrentPlayer)) Speech.Confirm(playedLine);

                // 0.7.193 — the card is down; put the camera back once the board
                // has finished reacting. See TickViewRestore.
                _restoreViewPending = true;
                _restoreViewUntil   = Time.unscaledTime + VIEW_RESTORE_WINDOW_SECONDS;

                // Session 14: this line IS the arrival announcement for a card
                // the player placed himself, so the diff watcher stays quiet
                // about it at the next turn boundary.
                BoardWatcher.NoteAnnounced(placing);

                // Session 51: this placement is IKMA's and has been spoken.
                // MousePlay.cs speaks the same line for one that is not.
                _injectedPlacement = placing;
                _injectedPlacementAt = Time.unscaledTime;

                bm.OnSlotSelected(slot);
            }
        }

        private static PlayableCard _injectedPlacement;
        private static float _injectedPlacementAt;

        /// <summary>
        /// Session 51. True when IKMA itself placed this card a moment ago
        /// (and so has already said "X played in Slot N."). Asked once per
        /// placement by MousePlay.cs.
        /// </summary>
        internal static bool TakeInjectedPlacement(PlayableCard card)
        {
            bool mine = card != null && ReferenceEquals(card, _injectedPlacement)
                        && Time.unscaledTime - _injectedPlacementAt < 3f;
            _injectedPlacement = null;
            return mine;
        }

        // ----------------------------------------------------------------------
        // Announce the currently focused slot during navigation.
        // ----------------------------------------------------------------------
        // ----------------------------------------------------------------------
        // Tab during placement: the next slot the game says this card may go in.
        //
        // Scans forward from where the browse cursor is and wraps, matching the
        // shape of Tab in the hand. Says so plainly when every slot is legal —
        // that is not a failure, it is the answer, and silence would read like a
        // dead key.
        // ----------------------------------------------------------------------
        private void JumpToNextValidSlot(BoardManager bm)
        {
            var slots = bm?.PlayerSlotsCopy;
            if (slots == null || slots.Count == 0) return;

            var valid = _validPlacementSlots;
            if (valid == null || valid.Count == 0)
            {
                Speech.Browse(Vocabulary.Hotkeys.NoSlotRestrictionsTo);
                return;
            }

            int count = slots.Count;
            int start = _slotIndex < 0 ? 0 : _slotIndex;

            // 0.7.341 — THE FIRST TAB CAN LAND WHERE THE FOCUS ALREADY IS.
            // Zamar: "I pressed tab once and even though slot 1 was empty and
            // valid tab went to slot 2 first." The focus sat on slot 1 but
            // nothing had read it, so stepping past it skipped a slot he had
            // never heard. Until a slot has been read this placement, Tab
            // starts its search at the focus itself.
            int firstStep = _slotReadThisPlacement ? 1 : 0;

            for (int i = firstStep; i <= count; i++)
            {
                int probe = (start + i) % count;
                if (!valid.Contains(slots[probe])) continue;

                _slotIndex = probe;
                AnnounceCurrentSlot(bm, choosingSacrifices: false);
                return;
            }

            Speech.Browse(Vocabulary.Hotkeys.NoOtherSlotAvailable);
        }

        // 0.7.344 — " The cannons are aimed here." when the Pirate Skull's
        // crosshair is on this slot; "" everywhere else. PROVISIONAL, #54.
        private static string CannonHere(CardSlot slot)
            => PirateSkullNarrator.IsCannonTarget(slot) ? " " + Vocabulary.CannonAimedHere() : "";

        // 0.7.425 — WHAT A CARD PLAYED HERE WOULD GAIN FROM LEADER.
        //
        // The game has no question to ask about an EMPTY slot: the buff is
        // worked out per card, in PlayableCard.GetPassiveAttackBuffs (private),
        // as +1 for every card in BoardManager.GetAdjacentSlots(Slot) that
        // HasAbility(Ability.BuffNeighbours). So this asks the same two public
        // things of the same slot. Null when nothing beside it has Leader.
        // The sigil's spoken name comes back in leaderName, from the game.
        private static string LeaderGainFor(CardSlot slot, out string leaderName)
        {
            leaderName = null;
            try
            {
                var board = Singleton<BoardManager>.Instance;
                if (board == null || slot == null) return null;

                var sources = new List<string>();
                foreach (var adjacent in board.GetAdjacentSlots(slot))
                {
                    var beside = adjacent != null ? adjacent.Card : null;
                    if (beside == null || beside.Info == null) continue;
                    if (!beside.HasAbility(Ability.BuffNeighbours)) continue;
                    sources.Add(Vocabulary.Hotkeys.LeaderSource(
                        CardReader.CardName(beside), adjacent.Index + 1));
                }
                if (sources.Count == 0) return null;

                leaderName = CardReader.GetAbilityName(Ability.BuffNeighbours);
                return Vocabulary.Hotkeys.LeaderGain(sources.Count, string.Join(Loc.T(" and "), sources));
            }
            catch { return null; }
        }

        private void AnnounceCurrentSlot(BoardManager bm, bool choosingSacrifices)
        {
            if (bm == null) return;
            var slots = bm.PlayerSlotsCopy;
            if (slots == null || _slotIndex >= slots.Count) return;

            var slot   = slots[_slotIndex];
            int slotNum = _slotIndex + 1;
            _slotReadThisPlacement = true;   // 0.7.341, see JumpToNextValidSlot

            // Zamar, Session 14: "when playing a card it does not highlight the
            // slot, like it does if you mouse over it." The hand hover and the
            // item-target hover both moved the game's cursor already; slot
            // placement was the one browse layer still leaving the screen
            // wherever the mouse last was.
            //
            // Same mechanism as HoverTargetSlot, and the same reason beyond
            // presentation: blind players stream to sighted audiences, so a
            // player choosing a slot in silence on screen is unreadable to
            // anyone watching.
            HoverTargetSlot(_hoveredSlot, slot);
            _hoveredSlot = slot;

            if (choosingSacrifices)
            {
                if (slot.Card != null)
                {
                    var card         = slot.Card;
                    var abilityNames = new List<string>();
                    // 0.7.346 — ONCE PER ABILITY. A Fecundity-copied Mealworm
                    // carries Morsel twice in Info.Abilities and read "Morsel
                    // and Morsel"; Zamar: the card shows "just one" icon. The
                    // board read already de-duplicates the same way.
                    var seenHere = new HashSet<Ability>();
                    if (card.Info?.Abilities != null)
                        foreach (var ability in card.Info.Abilities)
                        {
                            if (!seenHere.Add(ability)) continue;
                            // The arrow inside the sigil, spoken as part of
                            // that sigil's name. (0.7.171.) Third and last of
                            // the three ability formatters — they now place the
                            // direction identically, which is the whole point:
                            // hearing "Sprinter moving right" here and a
                            // trailing "Moving right." on the board read would
                            // be two vocabularies for one arrow.
                            string n = CardReader.AbilityNameWithDirection(ability, card);
                            if (n != null) abilityNames.Add(n);
                        }

                    string sigilPart = abilityNames.Count > 0
                        ? (abilityNames.Count == 1
                            ? Vocabulary.Hotkeys.Ability(abilityNames[0])
                            : Vocabulary.Hotkeys.AbilitiesAnd(abilityNames))
                        : "";

                    Speech.Browse(
                        Vocabulary.Hotkeys.SlotWithCard(slotNum, CardReader.CardName(card), card.Attack, card.Health, sigilPart, CannonHere(slot)));
                }
                else
                {
                    Speech.Browse(Vocabulary.Hotkeys.SlotEmpty(slotNum, CannonHere(slot)));
                }
            }
            else
            {
                // Session 10: arrowing across four slots produced "Slot 1:
                // empty. Slot 2: empty. Slot 3: empty." with nothing to say WHY
                // the player was looking at slots at all. Two seconds of that
                // and the card being placed has dropped out of working memory.
                // The reminder is appended rather than led with, so the slot
                // state — the part that changes — is still heard first.
                string placing = CardReader.CardName(Singleton<PlayerHand>.Instance?.ChoosingSlotCard?.Info);
                string reminder = placing != null ? Vocabulary.Hotkeys.ChooseASlotTo(placing) : "";
                if (placing != null) SlotPromptState.NoteBrowsed();   // Session 36

                var occupant = BoardReader.LiveCard(slot);
                if (occupant?.Info != null)
                    Speech.Browse(Vocabulary.Hotkeys.SlotOccupiedBy(slotNum, CardReader.CardName(occupant), CannonHere(slot), reminder));
                else
                {
                    // 0.7.425 — LEADER. Zamar: "When highlighting a slot being
                    // enhanced by a Leader ability, call that out first."
                    string leader;
                    string gain = LeaderGainFor(slot, out leader);
                    Speech.Browse(gain != null && !string.IsNullOrEmpty(leader)
                        ? Vocabulary.Hotkeys.SlotEmptyLeader(leader, slotNum, CannonHere(slot), gain, reminder)
                        : Vocabulary.Hotkeys.SlotEmptyName(slotNum, CannonHere(slot), reminder));
                }
            }
        }
    }
}
