// MenuReader.cs
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// Kaycee's Mod menu navigation. (Roadmap item 6 — Session 9 FIRST DRAFT.)
    ///
    /// How this works, from the reflection dump:
    ///   AscensionMenuScreenTransition holds a private
    ///   List&lt;MainInputInteractable&gt; screenInteractables — the clickable
    ///   contents of one menu screen. One of these components sits on each
    ///   screen GameObject, and OnEnable fires as that screen becomes active.
    ///   Plugin.cs patches OnEnable to hand us the live instance, so there is
    ///   no scene searching: the game tells us which screen is up.
    ///
    ///   Every item derives from MainInputInteractable, whose CursorSelectStart
    ///   and CursorSelectEnd are PUBLIC (confirmed) — the same universal click
    ///   primitive already proven on draw piles, map nodes and item slots.
    ///
    /// KNOWN ITERATION POINT: labels. The dump gave no text field on
    /// AscensionMenuInteractable, so the visible words ("NEW RUN") live on some
    /// child component. LabelFor() hunts for a string 'text' member on any
    /// component under the object and falls back to a prettified GameObject
    /// name. Every resolution is logged as
    ///   IKMA MENU: item N label='X' via Y
    /// so the real source can be identified from one test and hard-coded.
    /// </summary>
    public static class MenuReader
    {
        private static ManualLogSource _log;

        private static AscensionMenuScreenTransition _activeScreen;
        private static FieldInfo _screenInteractablesField;

        // THE CURSOR STARTS NOWHERE. (0.7.231.)
        //
        // Zamar, on the Stats screen: "when I hit right it goes to the second
        // listed option Defeats instead of Victories. Can we do that default to
        // -1 thing again here?"
        //
        // Arrival read the screen AND the first option, which told him he was
        // on Victories. The cursor was on it too, so the first arrow press
        // moved OFF it — the one option he had actually been told about was the
        // one he could never reach by pressing right. Same defect
        // NodeScreenReader fixed with NOWHERE: the player is not standing
        // anywhere until they press an arrow, and then the first press lands ON
        // option one.
        //
        // 0.7.238 FINISHED THE JOB. Arrival no longer names an option either —
        // his rule, and the half this comment was missing: "If we're doing the
        // -1 default thing, then we can't read the first option on -1 as well."
        // See AnnounceScreen.
        //
        // Enter and Space still answer for option one while the cursor is
        // NOWHERE, because that is the option the first arrow would reach.
        private const int NOWHERE = -1;
        private static int _index = NOWHERE;

        // Screens reveal their options one at a time (SequentiallyRevealContents),
        // so the count climbs 1, 2, 3... as they appear. Announcing on every
        // change read the whole menu out backwards, one line per option. Wait
        // for the count to hold steady instead. (Session 9.)
        private const float SCREEN_SETTLE_SECONDS = 0.45f;
        private static int _stableCount = -1;
        private static float _countStableFor = 0f;
        private static bool _screenAnnounced = false;

        // Set when an option is activated: the screen is on its way out and
        // anything it would say next is noise. Cleared when a new screen enables.
        private static bool _suppressed = false;

        // GameObject name -> what to call the screen out loud. Names confirmed
        // from the IKMA MENU log lines in testing.
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _screenNames => Loc.PerLanguage(ref _screenNamesCache, ref _screenNamesLanguage, Build_screenNames);
        private static Dictionary<string, string> Build_screenNames() =>
            new Dictionary<string, string>
        {
            // BOTH SPELLINGS. His 0.7.105 log read this screen out as "Start",
            // which is what the fallback produces from a raw name of
            // "StartScreen" — so the dictionary was being missed and the entry
            // below was never the one answering. Zamar: "This shouldn't read
            // Start, it should say 'Inscryption. Kaycee's Mod main menu.'"
            //
            // The trimming fallback is doing its job — it turned an object name
            // into something sayable — but "Start" is a word about the scene
            // graph, not about where the player is. This is the front door of
            // the mod and it says so.
            { "AscensionStartScreen",              Vocabulary.Menus.InscryptionKayceesModMain },
            { "StartScreen",                       Vocabulary.Menus.InscryptionKayceesModMain },
            { "AscensionUnlocksSummaryScreen",     Vocabulary.Menus.Unlocks },
            { "AscensionCardsSummaryScreen",       Vocabulary.Menus.CardsUnlocked },
            { "AscensionCardsScreen",              Vocabulary.Menus.Cards },
            { "AscensionChallengeScreen",          Vocabulary.Menus.Challenges },
            { "AscensionChallengeConfirmScreen",   Vocabulary.Menus.ConfirmChallenges },
            // "First words should be 'Select Starter Deck.'" — Zamar, 0.7.236.
            // Two keys because the CLASS is AscensionChooseStarterDeckScreen
            // and the OBJECT in his log is AscensionStarterDeckScreen; the
            // object name is what ScreenName matches on, and the class name has
            // been sitting here unmatched since Session 13.
            { "AscensionChooseStarterDeckScreen",  Vocabulary.Menus.SelectStarterDeck },
            { "AscensionStarterDeckScreen",        Vocabulary.Menus.SelectStarterDeck },
            { "AscensionStatsScreen",              Vocabulary.Menus.Stats },
            { "AscensionRunEndScreen",             Vocabulary.Menus.RunComplete },
            { "AscensionJournalEntryScreen",       Vocabulary.Menus.Devlog },
            { "AscensionJournalSummaryScreen",     Vocabulary.Menus.Devlog },
            { "AscensionChallengeUnlockScreen",    Vocabulary.Menus.NewChallenges },
            { "AscensionStarterDeckUnlockScreen",  Vocabulary.Menus.NewStarterDeck },

            // "Starter Deck Summary" was the trimming fallback's work on the
            // object name, and it named a filing cabinet rather than a screen.
            // Zamar's words, 0.7.237: "Starter Deck Unlocked."
            { "AscensionStarterDeckSummaryScreen", Vocabulary.Menus.StarterDecksUnlocked },

            // The new-run warning. Its own header is spoken as the body text
            // (see ScreenBodyText), so the name here is short and says which
            // decision the player is standing in front of.
            //
            // AscensionChallengeConfirmScreen is NOT added here — it has been
            // in this table since Session 13 as "Confirm Challenges", and
            // adding it again is a repeated Dictionary key, which throws at
            // static construction and takes the whole class with it. The
            // pre-build check caught it; the body text reads on that screen
            // regardless, because ScreenBodyText matches on the object name
            // ending in ConfirmScreen rather than on this table.
            { "AscensionNewRunConfirmScreen",      Vocabulary.Menus.NewRun },
        };
        private static Dictionary<string, string> _screenNamesCache;
        private static string _screenNamesLanguage;

        public static void Init(ManualLogSource log)
        {
            _log = log;
        }

        /// <summary>Called from the OnEnable patch as a screen becomes active.</summary>
        // ------------------------------------------------------------------
        // The mouse is switched off while a Kaycee's Mod menu is up.
        // (Session 13, Zamar's call.)
        //
        // His standing requirement is that nothing in this mod may eventually
        // require a mouse — blind players often do not own one. The immediate
        // reason is smaller and just as real: a physical cursor resting on the
        // screen fights IKMA's keyboard-driven hover, so whichever option the
        // mouse happens to be over lights up alongside the one being read.
        //
        // InteractionCursor.InteractionDisabled is PUBLIC (confirmed,
        // dump_visual_sync.txt) and is the game's own switch for this. IKMA's
        // own CursorEnter / CursorSelectStart calls go straight to the
        // interactable and do not pass through the cursor's raycast, so
        // keyboard navigation is unaffected.
        //
        // Scoped to menus and restored on the way out: this is not the moment
        // to decide the mouse's fate in the rest of the game.
        // ------------------------------------------------------------------
        private static bool _mouseDisabledByUs;

        private static void SetMouseInteraction(bool enabled)
        {
            // Nothing to do, and asking costs a warning. Singleton<T>.Instance
            // runs FindInstance on a miss and logs every time it comes up empty;
            // this ran every frame and put 106 lines into Zamar's 0.7.47 log for
            // scenes that have no cursor. (0.7.48.)
            if (enabled && !_mouseDisabledByUs) return;

            try
            {
                var cursor = Singleton<InteractionCursor>.Instance;
                if (cursor == null) return;

                if (!enabled)
                {
                    if (_mouseDisabledByUs) return;
                    cursor.InteractionDisabled = true;
                    _mouseDisabledByUs = true;
                    _log?.LogInfo("IKMA MENU: mouse interaction disabled for menu navigation.");
                }
                else
                {
                    if (!_mouseDisabledByUs) return;
                    cursor.InteractionDisabled = false;
                    _mouseDisabledByUs = false;
                    _log?.LogInfo("IKMA MENU: mouse interaction restored.");
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MENU: mouse toggle failed: {e.Message}");
            }
        }

        /// <summary>Called every frame from HotkeyManager so the mouse is off
        /// exactly while a menu owns the keyboard, and on again after.</summary>
        /// <remarks>
        /// 0.7.232 — the base game's title screen counts as a menu here. Zamar
        /// asked for its cursor hover to "work the same way it does elsewhere",
        /// and the other half of "the same way" is this: a physical cursor
        /// resting on a card lights it up alongside the one being read, which
        /// is exactly the fight this switch was added to stop on the Kaycee's
        /// Mod screens. One switch, both menus.
        /// </remarks>
        public static void TickMouseSuppression()
        {
            SetMouseInteraction(!MenuActive() && !TitleScreenReader.Active);
        }

        public static void SetActiveScreen(AscensionMenuScreenTransition screen)
        {
            ClearHover();
            _screenComponentsResolved = false;
            _activeScreen = screen;
            _startRunButton   = null;
            _startRunResolved = false;
            _index = NOWHERE;
            _stableCount = -1;
            _countStableFor = 0f;
            _screenAnnounced = false;
            _suppressed = false;
            _log?.LogInfo($"IKMA MENU: screen active — {screen?.gameObject?.name ?? "null"}");
        }

        // Session 37 - the Kaycee's Mod start screen, by the same two names
        // the screen-name table knows it by.
        private static bool IsStartScreen()
        {
            string raw = _activeScreen?.gameObject?.name ?? "";
            return raw == "AscensionStartScreen" || raw == "StartScreen";
        }

        // The game shows it as a Roman numeral; asked of the save each time.
        private static string ChallengeLevelLine()
        {
            int level = 0;
            try { level = AscensionSaveData.Data.challengeLevel; } catch { }

            // 0.7.425 - THE GAME'S OWN TEXT PAST LEVEL 12. On the challenge
            // select screen ChallengeLevelText.UpdateText prints "Challenge
            // Level: N" up to 12 and, above that, "ALL CHALLENGE LEVELS
            // CLEARED!" with no number. Same split ChallengePointsLine makes.
            if (level > 12) return Vocabulary.Menus.AllChallengeLevelsCleared;
            return Vocabulary.Menus.ChallengeLevel(level);
        }

        private static string ScreenName()
        {
            string raw = _activeScreen?.gameObject?.name ?? "";
            // 0.7.424 - Zamar: "Why is challenge level being called at the
            // main menu screen. This is wrong, remove it." It was added here
            // in Session 37; gone from arrival and from Space.
            if (_screenNames.TryGetValue(raw, out string friendly)) return friendly;


            string s = raw;
            if (s.StartsWith("Ascension")) s = s.Substring("Ascension".Length);
            if (s.EndsWith("Screen")) s = s.Substring(0, s.Length - "Screen".Length);
            string pretty = Prettify(s);
            return Vocabulary.Menus.MenuOrPrettyName(string.IsNullOrEmpty(pretty), pretty);
        }

        public static bool MenuActive()
        {
            if (_activeScreen == null) return false;
            // A disabled screen object is a screen we have been switched away
            // from; the incoming one will announce itself through OnEnable.
            if (!_activeScreen.gameObject.activeInHierarchy) return false;
            return Singleton<AscensionMenuScreens>.Instance != null;
        }

        // ------------------------------------------------------------------
        // The clickable contents of the active screen, minus anything hidden
        // or destroyed. Order is the order the game lists them, which matches
        // the on-screen order.
        // ------------------------------------------------------------------
        // The CARDS UNLOCKED screen's own card list, or null if this is not
        // that screen. (Session 13.)
        //
        // Its browsable content is not in screenInteractables at all — the only
        // things there are the page arrows and some count labels, which is why
        // Zamar heard "1", "2", "1", "0" while looking at four cards. The cards
        // live in AscensionCardsScreen's NONPUBLIC `cards` list.
        //
        // GBC.PixelSelectableCard turns out to derive from Card, and so from
        // MainInputInteractable (confirmed, dump_cards_screen.txt). That is the
        // whole reason this works: the cards ARE menu interactables, so they
        // browse, hover and label through the machinery that already exists
        // rather than needing a reader of their own.
        //
        // GetComponentInChildren on a screen the game handed us — not a scene
        // search.
        private static FieldInfo _cardsField;
        private static FieldInfo _pageLeftField;
        private static FieldInfo _pageRightField;

        // ------------------------------------------------------------------
        // Screen-scoped caches. (Session 13 — the menu hitch.)
        //
        // Zamar: navigation "hitches". It does, and the cause is this file.
        // GetItems ran GetComponentInChildren on the live screen every call,
        // LabelFor ran ANOTHER one per item to identify the paging arrows, and
        // the text hunt walks every component under an object. GetItems is
        // called several times per keypress. On a 16-option screen that is
        // dozens of component walks for one arrow press.
        //
        // None of it changes while a screen is up, so it is resolved once per
        // screen. Cleared in SetActiveScreen, which is the only moment any of it
        // can become stale.
        //
        // Timing is logged behind a threshold rather than assumed — this
        // project has optimised the wrong thing before by guessing, and the
        // rulebook hitch turned out to be the game rebuilding text layout, not
        // IKMA at all.
        private static AscensionCardsScreen _cardsScreenCache;
        private static AscensionCardsSummaryScreen _cardsSummaryCache;
        private static bool _screenComponentsResolved;

        private static MainInputInteractable _pageLeft;
        private static MainInputInteractable _pageRight;

        private static void ResolveScreenComponents()
        {
            if (_screenComponentsResolved) return;
            _screenComponentsResolved = true;

            _cardsScreenCache  = null;
            _cardsSummaryCache = null;
            _pageLeft = null;
            _pageRight = null;

            if (_activeScreen == null) return;

            try
            {
                _cardsScreenCache  = _activeScreen.GetComponentInChildren<AscensionCardsScreen>(true);
                _cardsSummaryCache = _activeScreen.GetComponentInChildren<AscensionCardsSummaryScreen>(true);

                if (_cardsSummaryCache != null)
                {
                    if (_pageLeftField == null)
                    {
                        _pageLeftField = typeof(AscensionCardsSummaryScreen).GetField(
                            "pageLeftButton",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        _pageRightField = typeof(AscensionCardsSummaryScreen).GetField(
                            "pageRightButton",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    }
                    _pageLeft  = _pageLeftField?.GetValue(_cardsSummaryCache)  as MainInputInteractable;
                    _pageRight = _pageRightField?.GetValue(_cardsSummaryCache) as MainInputInteractable;
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MENU: screen component resolve failed: {e.Message}");
            }
        }

        private static List<MainInputInteractable> CardScreenItems()
        {
            if (_activeScreen == null) return null;

            try
            {
                ResolveScreenComponents();
                var cardsScreen = _cardsScreenCache;
                if (cardsScreen == null) return null;

                if (_cardsField == null)
                {
                    _cardsField = typeof(AscensionCardsScreen).GetField(
                        "cards",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }

                var cards = _cardsField?.GetValue(cardsScreen) as List<GBC.PixelSelectableCard>;
                if (cards == null || cards.Count == 0) return null;

                var result = new List<MainInputInteractable>();
                foreach (var c in cards)
                {
                    if (c == null) continue;
                    if (!c.gameObject.activeInHierarchy) continue;
                    result.Add(c);
                }

                // Session 14, Zamar: the page arrows on the card unlock screen
                // were unreachable from the keyboard, so only the first page of
                // unlocks existed as far as a blind player was concerned.
                //
                // Confirmed in dump_cards_screen.txt: AscensionCardsScreen holds
                // pageLeftButton and pageRightButton, both AscensionMenuInteractable
                // and both NONPUBLIC, plus pageIndex, NUM_PAGES and
                // ChangePage(Boolean left). They are not in screenInteractables,
                // which is why the screen only ever offered its cards and Back.
                //
                // His call: in the browse list, labelled "Previous page" and
                // "Next page" — not separate keys. So they are ordinary options
                // and Enter activates them through the same universal primitive
                // as everything else.
                // ResolveScreenComponents already found these and LabelFor
                // already names them. What was missing was the one line that
                // puts them in the list a player can arrow through — so they
                // were resolved, labelled, and unreachable.
                if (_pageLeft  != null && _pageLeft.gameObject.activeInHierarchy)  result.Add(_pageLeft);
                if (_pageRight != null && _pageRight.gameObject.activeInHierarchy) result.Add(_pageRight);

                return result.Count > 0 ? result : null;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MENU: card screen read failed: {e.Message}");
                return null;
            }
        }

        public static List<MainInputInteractable> GetItems()
        {
            var result = new List<MainInputInteractable>();
            if (_activeScreen == null) return result;

            // Cards first, page arrows and Back after, so the player lands on
            // content rather than on navigation furniture.
            var cardItems = CardScreenItems();
            if (cardItems != null) result.AddRange(cardItems);

            try
            {
                if (_screenInteractablesField == null)
                {
                    _screenInteractablesField = typeof(AscensionMenuScreenTransition).GetField(
                        "screenInteractables",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }

                var raw = _screenInteractablesField?.GetValue(_activeScreen)
                          as List<MainInputInteractable>;
                if (raw == null) return result;

                foreach (var item in raw)
                {
                    if (item == null) continue;
                    if (!item.gameObject.activeInHierarchy) continue;
                    if (result.Contains(item)) continue;
                    result.Add(item);
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MENU: screenInteractables read failed: {e.Message}");
            }

            // Session 14, from the 0.7.37 log: the Challenges screen read
            // "...Grizzly Bosses" at 14, "Back" at 15, "Final Boss" at 16.
            // screenInteractables comes back in the order the screen happens to
            // hold it, and Back was sitting in the middle of the challenges.
            //
            // This is also B8's "the cursor to reach the Final Boss challenge
            // was buggy" — same cause, and it was never a cursor problem. A
            // player arrowing through options hit what sounded like the end of
            // the list two items early and stopped.
            //
            // Back goes last, always. It is the way OUT of a screen, so it
            // belongs after everything the screen is for.
            MoveBackButtonLast(result);

            // Zamar's layout for the card unlock screen, Session 14:
            //   1 Back, 2 Previous page, 3..n the cards, last Next page.
            //
            // It reads as a row he can sweep: the way out first, then the way
            // back, then the content, then the way forward. Wrapping from Next
            // page lands on Back, which closes the loop rather than dumping him
            // in the middle of the cards.
            //
            // Only this screen — the page arrows exist nowhere else, so
            // everywhere else keeps the plain "content first, Back last" order.
            ReorderCardUnlockScreen(result);

            // Zamar's layout for the devlog, 0.7.42 playtest: "option 1 should
            // be Back, option 2 should be the default option and be entry log
            // number 1."
            //
            // He found it reading Entry 3, Entry 2, Back, Entry 1 — the same
            // fault as the Challenges screen, where screenInteractables came
            // back in whatever order the screen happened to hold it. Back last
            // was the general fix; here he wants Back FIRST and the entries in
            // their printed order, because a devlog is a list you read from the
            // top and the numbers are the whole index.
            ReorderDevlogScreen(result);

            // Zamar, Session 33 (built Session 34): on the Unlocked Challenges
            // screen the arrows walk every UNLOCKED challenge first, then every
            // locked one (it used to walk the top row, then the bottom row).
            ReorderUnlockedChallengesScreen(result);

            // THE START RUN BUTTON GOES AFTER THE CHALLENGES. (0.7.240.)
            //
            // Zamar: "Final boss challenge selection is after the continue
            // button in the list when choosing challenges."
            //
            // Exactly the fault MoveBackButtonLast was written for at Session
            // 14, one button over — his log has Grizzly Bosses at 14, Continue
            // at 15 and Final Boss at 16, so a player sweeping the list hits
            // what sounds like the end one challenge early and never reaches
            // the last one. screenInteractables comes back in whatever order
            // the screen happens to hold it, and the fix is the same: the way
            // ON belongs after everything the screen is for, just as the way
            // OUT does.
            MoveStartRunLast(result);

            return result;
        }

        // ----------------------------------------------------------------------
        // A page turn on the card unlock screen. (Session 14.)
        //
        // The screen stays, the cards change. Nothing in the game announces
        // that, so without this a player turns a page into silence and has to
        // arrow around to discover what is now in front of them.
        //
        // Settled before reading, for the usual reason: the new cards are not in
        // place on the frame the button is clicked. The cached item list and
        // label cache are dropped first — they describe the page that just left.
        // ----------------------------------------------------------------------
        private static float _pageTurnTimer = -1f;
        private const float PAGE_TURN_SETTLE = 0.35f;

        private static void StartPageTurn()
        {
            _pageTurnTimer = PAGE_TURN_SETTLE;
        }

        /// <summary>Ticked from the menu Update path; announces the new page once it has settled.</summary>
        public static void TickPageTurn(float deltaTime)
        {
            if (_pageTurnTimer < 0f) return;

            _pageTurnTimer -= deltaTime;
            if (_pageTurnTimer > 0f) return;
            _pageTurnTimer = -1f;

            // The page that just left owns both of these. _labelCache is keyed
            // on instance id and the cards are reused across pages, so a stale
            // entry would read the previous page's card under the new one's
            // object — the worst possible failure on a screen whose whole job is
            // telling you what card you are on.
            _screenComponentsResolved = false;
            _labelCache.Clear();

            var items = GetItems();
            if (items.Count == 0) return;

            _index = DefaultIndex(items);
            HoverMenuItem(null, items[_index]);

            Speech.Browse(Sentence(LabelFor(items, _index)));
        }

        private static void ReorderCardUnlockScreen(List<MainInputInteractable> items)
        {
            if (items == null || _pageLeft == null || _pageRight == null) return;
            if (!items.Contains(_pageLeft) || !items.Contains(_pageRight)) return;

            MainInputInteractable back = null;
            foreach (var item in items)
                if (item is AscensionMenuBackButton) { back = item; break; }

            var cards = new List<MainInputInteractable>();
            foreach (var item in items)
            {
                if (ReferenceEquals(item, _pageLeft))  continue;
                if (ReferenceEquals(item, _pageRight)) continue;
                if (ReferenceEquals(item, back))       continue;
                cards.Add(item);
            }

            items.Clear();
            if (back != null) items.Add(back);
            items.Add(_pageLeft);
            items.AddRange(cards);
            items.Add(_pageRight);
        }

        /// <summary>
        /// Back first, then the entries in printed number order. (0.7.43.)
        ///
        /// Sorted on the number in the label — "ENTRY #02" — rather than on any
        /// position the screen reports, because the position is exactly what was
        /// wrong. An entry whose label carries no number keeps its relative
        /// place at the end rather than being sorted to an invented one.
        /// </summary>
        private static void ReorderDevlogScreen(List<MainInputInteractable> items)
        {
            if (items == null || items.Count < 2) return;
            if (!IsDevlogSummaryScreen()) return;

            MainInputInteractable back = null;
            foreach (var item in items)
                if (item is AscensionMenuBackButton) { back = item; break; }

            var numbered   = new List<KeyValuePair<int, MainInputInteractable>>();
            var unnumbered = new List<MainInputInteractable>();

            foreach (var item in items)
            {
                if (ReferenceEquals(item, back)) continue;
                int n = TrailingNumber(LabelFor(items, items.IndexOf(item)));
                if (n >= 0) numbered.Add(new KeyValuePair<int, MainInputInteractable>(n, item));
                else unnumbered.Add(item);
            }

            numbered.Sort((a, b) => a.Key.CompareTo(b.Key));

            items.Clear();
            if (back != null) items.Add(back);
            foreach (var pair in numbered) items.Add(pair.Value);
            items.AddRange(unnumbered);
        }

        /// <summary>
        /// Unlocked challenges first, locked after, each group keeping the
        /// order it already had; anything that is not a challenge icon (Back)
        /// keeps its place at the end. Locked is the game's own test, the one
        /// the icon's lock sprite uses (ChallengeIsUnlocked).
        /// </summary>
        private static void ReorderUnlockedChallengesScreen(List<MainInputInteractable> items)
        {
            if (items == null || items.Count < 2) return;
            if (!IsUnlockedChallengesScreen()) return;

            var unlocked = new List<MainInputInteractable>();
            var locked   = new List<MainInputInteractable>();
            var others   = new List<MainInputInteractable>();
            foreach (var item in items)
            {
                var icon = item as AscensionIconInteractable;
                if (icon == null || icon.Info == null) { others.Add(item); continue; }
                if (ChallengeIsUnlocked(icon.Info)) unlocked.Add(item); else locked.Add(item);
            }
            items.Clear();
            items.AddRange(unlocked);
            items.AddRange(locked);
            items.AddRange(others);
        }

        private static bool IsDevlogSummaryScreen()
        {
            string raw = _activeScreen?.gameObject?.name ?? "";
            return raw == "AscensionJournalSummaryScreen";
        }

        /// <summary>The last run of digits in a string, or -1 if there is none.</summary>
        private static int TrailingNumber(string label)
        {
            if (string.IsNullOrEmpty(label)) return -1;

            int end = -1;
            for (int i = label.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(label[i])) { end = i; break; }
            }
            if (end < 0) return -1;

            int start = end;
            while (start > 0 && char.IsDigit(label[start - 1])) start--;

            int value;
            return int.TryParse(label.Substring(start, end - start + 1), out value) ? value : -1;
        }

        /// <summary>
        /// Where the cursor should sit when this screen is first read, or when
        /// its page changes. (Session 14.)
        ///
        /// On the card unlock screen that is the FIRST CARD, not the first
        /// option — turning a page and landing on "Back" tells the player
        /// nothing about the page they just turned to. Everywhere else, option
        /// one is the right place.
        /// </summary>
        private static int DefaultIndex(List<MainInputInteractable> items)
        {
            if (items == null || items.Count == 0) return 0;

            // Devlog: option 2, the first entry. Landing on Back tells the
            // player nothing about the screen they just opened — same argument
            // as the card unlock screen below. Zamar's call.
            if (IsDevlogSummaryScreen() && items.Count > 1) return 1;

            if (_pageLeft == null || _pageRight == null) return 0;
            if (!items.Contains(_pageLeft)) return 0;

            int first = items.IndexOf(_pageLeft) + 1;
            if (first <= 0 || first >= items.Count) return 0;
            if (ReferenceEquals(items[first], _pageRight)) return 0;
            return first;
        }

        /// <summary>
        /// The challenge screen's own continue button, moved to sit after every
        /// challenge and before Back. Matched on the object name, which his log
        /// gives as 'Continue' — the same source every label on this screen
        /// already comes from. (0.7.240.)
        /// </summary>
        // ==================================================================
        // THE BUTTON IS FOUND BY REFERENCE, NOT BY NAME. (0.7.242.)
        //
        // 0.7.240 matched gameObject.name == "Continue" and neither the reorder
        // nor the rename fired. The name in the LOG is not the name on the
        // object: LabelFor's last resort runs it through Prettify, which strips
        // a trailing "Button" — so an object called "ContinueButton" is logged
        // as 'Continue', and a raw-name comparison against "Continue" can never
        // match. The evidence was already in the file: the page arrows are
        // resolved this way, with the comment "Identified by reference against
        // the screen's own fields — never by guessing at a name." I guessed at
        // a name.
        //
        // AscensionChallengeScreen.continueButton is the screen's own field for
        // it. Reached through _activeScreen's children rather than through
        // Singleton<T>.Instance, which falls back to a scene search inside a
        // lock when it misses — and GetItems runs every frame while a screen is
        // settling. Cached per screen; SetActiveScreen clears it.
        // ==================================================================
        private static MainInputInteractable _startRunButton;
        private static bool _startRunResolved;

        private static MainInputInteractable StartRunButton()
        {
            if (_startRunResolved) return _startRunButton;
            _startRunResolved = true;
            _startRunButton   = null;

            if (!IsChallengeSelectScreen()) return null;

            try
            {
                var screen = _activeScreen.gameObject
                    .GetComponentInChildren<AscensionChallengeScreen>(true);
                if (screen == null)
                {
                    _log?.LogWarning("IKMA MENU: AscensionChallengeScreen not found under the challenge screen.");
                    return null;
                }

                var field = typeof(AscensionChallengeScreen).GetField(
                    "continueButton", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null)
                {
                    _log?.LogWarning("IKMA MENU: AscensionChallengeScreen.continueButton not found.");
                    return null;
                }

                _startRunButton = field.GetValue(screen) as MainInputInteractable;
                _log?.LogInfo($"IKMA MENU: start run button resolved — " +
                              $"obj='{_startRunButton?.gameObject?.name ?? "<null>"}'.");
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MENU: resolving the start run button threw {e.GetType().Name}.");
            }

            return _startRunButton;
        }

        private static void MoveStartRunLast(List<MainInputInteractable> items)
        {
            if (items == null || items.Count < 2) return;

            var start = StartRunButton();
            if (start == null || !items.Contains(start)) return;

            items.Remove(start);

            // Before Back, which keeps its own place at the very end.
            int back = -1;
            for (int i = 0; i < items.Count; i++)
                if (items[i] is AscensionMenuBackButton) { back = i; break; }

            if (back >= 0) items.Insert(back, start);
            else           items.Add(start);
        }

        /// <summary>
        /// The screen where a run's challenges are chosen — not the unlocks
        /// record. His log names the object 'ChallengesScreen'.
        /// </summary>
        private static bool IsChallengeSelectScreen()
        {
            string raw = _activeScreen?.gameObject?.name ?? "";
            return raw == "ChallengesScreen" || raw == "AscensionChallengeScreen";
        }

        private static void MoveBackButtonLast(List<MainInputInteractable> items)
        {
            if (items == null || items.Count < 2) return;

            for (int i = 0; i < items.Count; i++)
            {
                if (!(items[i] is AscensionMenuBackButton)) continue;
                if (i == items.Count - 1) return;

                var back = items[i];
                items.RemoveAt(i);
                items.Add(back);
                return;
            }
        }

        // ------------------------------------------------------------------
        // Announce the screen and where we are on it. Called when a screen
        // first becomes navigable.
        // ------------------------------------------------------------------
        // ------------------------------------------------------------------
        // A devlog entry, handed over by AscensionJournalEntryScreen's own
        // InitializeWithEntry patch. (Session 13.)
        //
        // The journal screen is the one screen that exists ONLY to be read, and
        // it was the one screen reading nothing: "Journal. 1 option. Back."
        // The entry text is in AscensionJournalData.BodyText (PUBLIC, confirmed)
        // and IKMA never touched it.
        //
        // Held rather than spoken on arrival, so it composes into the screen's
        // own announcement as one utterance instead of racing it.
        // ------------------------------------------------------------------
        private static string _pendingEntryText;

        internal static void SetPendingEntry(int entryId, string bodyText)
        {
            if (string.IsNullOrEmpty(bodyText)) { _pendingEntryText = null; return; }

            // The body carries the game's own inline markup and Unity rich text,
            // the same two families the dialogue patch strips. NVDA reads colour
            // hex codes aloud otherwise.
            string clean = System.Text.RegularExpressions.Regex.Replace(bodyText, @"\[[^\]]*\]", "");

            // THE TAG STRIP ATE A WHOLE ENTRY. (0.7.231.)
            //
            // Zamar: "Journal 11 is supposed to be empty of lore, but it still
            // says its corrupted and all, so I want this 'error message' to
            // read." His log said why:
            //
            //   IKMA MENU: journal entry 11 captured, 0 characters.
            //
            // Entry11.asset's whole body is
            //   <DATA CORRUPTED. FOR ANY QUALITY CONCERNS PLEASE CONTACT
            //    KAMINSKI DATA STORAGE MFG.>
            // — angle brackets and all, as prose. The old strip was
            // `<[^>]*>`, which cannot tell a rich-text tag from a sentence
            // inside angle brackets, so it deleted the entry and left nothing
            // to say. The screen shows that text; a blind player heard silence.
            //
            // A rich-text tag is SHAPED: an optional slash, a name with no
            // spaces in it, and then either the closing bracket or a `=`/`-`
            // and its value. `<b>`, `</color>`, `<size=20>` and
            // `<line-height=1.2>` all match; `<DATA CORRUPTED. ...>` does not,
            // because a space follows the first word. Entry 12's "<3" has no
            // closing bracket and was never at risk either way.
            clean = System.Text.RegularExpressions.Regex.Replace(
                clean, @"</?[a-zA-Z][a-zA-Z0-9]*(?:[-=][^<>]*)?>", "");

            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();

            // READ IT THE WAY IT IS WRITTEN, NOT THE WAY IT IS TYPED. (0.7.231.)
            //
            // Zamar, on entry 12: "Replace the word coords with 'coordinates',
            // just so it reads properly." A sighted player reads "coords" as an
            // abbreviation in a hurried diary entry; a synth says "coords",
            // which is a noise, not a word. Expanding it is the parity-correct
            // move and costs the entry nothing.
            clean = System.Text.RegularExpressions.Regex.Replace(
                clean, @"\bcoords\b", Vocabulary.Menus.CoordsExpanded,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            _pendingEntryText = clean.Length == 0 ? null : Vocabulary.Menus.EntryLine(entryId, clean);
            _log?.LogInfo($"IKMA MENU: journal entry {entryId} captured, {clean.Length} characters.");
        }

        public static void AnnounceScreen()
        {
            var items = GetItems();
            if (items.Count == 0)
            {
                Speech.Browse(Vocabulary.Menus.NoOptionsAvailable(ScreenName()));
                return;
            }

            _screenAnnounced = true;
            _index = NOWHERE;

            // THE COUNT IS DROPPED WHERE THE LABEL IS ENORMOUS. (0.7.45.)
            //
            // Zamar on the starter deck screen, 0.7.43: "9 options didn't need
            // to be there." That entry read is three full card descriptions
            // long, so the count arrives twenty seconds after the screen name
            // with nothing left to attach to — the same fault as the "1 of 9"
            // he had already caught on M, and the same answer.
            //
            // Everywhere else keeps it. On a screen of short labels the count is
            // real orientation and he has never asked for it to go.
            // Session 16: the option count is no longer spoken on arrival. The
            // local and the LabelIsLong guard that used to blank it went with
            // it — a variable assigned and never read is a warning, and this
            // project builds at zero.

            // Session 13, from Zamar's 0.7.20 test log: the main menu announced
            // "Backspace to go back", and Backspace then answered "No back
            // option on this screen." Offering a blind player a key that does
            // nothing is the same bug as the number-key jumps in
            // IKMA_Controls.md, and the controls line is where they learn what
            // the screen can do. Only promise the key when the screen has one.
            // Session 13: the controls line now describes THIS screen rather
            // than menus in general. Zamar on the devlog entry screen: it
            // offered arrows and Enter when the screen holds exactly one button
            // and only Backspace did anything. Same defect as the number-key
            // jumps and the slot-placement Backspace — a key offered to a blind
            // player that does nothing.
            bool hasBack   = HasBackButton(items);
            bool backOnly  = hasBack && items.Count == 1;

            // SCREENS THAT ARE A LIST OF READOUTS, not a list of choices.
            // Enter does nothing on any of them, so the controls line does not
            // offer it — the key-that-does-nothing defect this whole line
            // exists to avoid.
            //
            //   Stats                     Session 14. Zamar: "there is no enter
            //                             to confirm, only arrows to navigate
            //                             and backspace to go back."
            //   Unlocked challenges       0.7.231. "Remove the Enter
            //                             instructions here. No enter on the
            //                             challenges screen." His 0.7.230 log
            //                             had Enter pressed on 'No Hook' six
            //                             times, each logged as an activation
            //                             and each doing nothing.
            //   Unlocked starter decks    0.7.237. His target line for that
            //                             screen has no Enter in it either.
            //
            // All three are the UNLOCKS records. The screens where a run is
            // actually configured — AscensionChallengeScreen,
            // AscensionChooseStarterDeckScreen — keep their Enter.
            bool readoutScreen = IsStatsScreen()
                              || IsUnlockedChallengesScreen()
                              || IsStarterDeckSummaryScreen();

            string controls;
            if (backOnly)
            {
                controls = Vocabulary.Menus.BackspaceToGoBack;
            }
            else if (readoutScreen)
            {
                controls = Vocabulary.Menus.ArrowsToNavigate;
                controls += Vocabulary.Menus.AnnounceScreenBackspaceToGo(hasBack);
            }
            else
            {
                controls = Vocabulary.Menus.ArrowsToNavigateOrEnterToConfirm(items.Count);
                controls += Vocabulary.Menus.AnnounceScreenBackspaceToGo(hasBack);
            }

            // A devlog entry replaces the option list in the announcement. The
            // screen has one button on it; what the player came for is the text.
            if (_pendingEntryText != null)
            {
                string entry = _pendingEntryText;
                _pendingEntryText = null;
                Speech.Browse(Vocabulary.Menus.ScreenEntryControls(ScreenName(), entry, controls));
                return;
            }

            // Zamar, Session 13: the option count moves to the END of the line.
            // It used to sit between the screen name and the first option, so
            // every screen entry made the player wait through a number before
            // hearing the thing they had arrived at. The count is orientation,
            // not content — useful, but never the part you are listening for.
            // AnnounceCurrent already put its position at the end; this makes
            // the two agree.
            // ==================================================================
            // ARRIVAL NAMES NO OPTION. ANYWHERE. (0.7.238.)
            //
            // Zamar, and it is the rule the last four builds were converging on
            // one screen at a time: "If we're doing the -1 default thing, then
            // we can't read the first option on -1 as well."
            //
            // He is right, and the two halves were contradicting each other.
            // NOWHERE says the cursor is not on anything until an arrow is
            // pressed. Reading option one on arrival said it was. A sighted
            // player watching saw it lit as well, because the preview hovered
            // it — so the screen, the speech and the cursor disagreed three
            // ways.
            //
            // The old argument FOR the preview was that an arrow would
            // otherwise cost the player the first option, since browsing moved
            // before it spoke. NOWHERE is what answers that: the first press
            // lands ON option one instead of past it, so nothing is skipped and
            // nothing has to be said twice.
            //
            // This retires the per-screen exceptions with it — the Kaycee's Mod
            // front door (0.7.236) and Inscryption's title screen (0.7.234)
            // were this rule discovered twice, one screen at a time.
            //
            // THE DEVLOG ENTRY IS NOT AN EXCEPTION TO THIS. Its text goes out
            // above, before this point, and it is not an option: the screen
            // holds one button and the entry is the thing the player came for.
            //
            // The count is not spoken here either, and has not been since
            // Session 16 — Zamar: "The number of options available (and which
            // one you're currently on) should only be read by pressing M."
            // ==================================================================
            ClearHover();

            // A WARNING SCREEN IS ALL TEXT AND TWO BUTTONS, and the text is the
            // whole point of it. (0.7.240.)
            //
            // Zamar: "We need all this text to read on the new run overwrite
            // screen." His 0.7.239 log read "New Run Confirm. Arrows to
            // navigate, Enter to confirm, Backspace to go back." — the controls
            // for a decision, with the decision left out. A sighted player is
            // looking at "ARE YOU SURE? STARTING A NEW RUN WILL END YOUR
            // CURRENT RUN!" and IKMA was offering Enter without saying what
            // Enter would destroy.
            //
            // This is the same shape as the devlog entry above: not an option,
            // but the screen's own content, folded into one utterance rather
            // than racing the announcement.
            // THE TOTAL, BEFORE THE CHALLENGES. (0.7.242.) Zamar: "before
            // reading the names say what the total amount of current Challenge
            // Points for these mods equals again."
            //
            // The screen prints it in its header and it is the number every
            // choice on this screen moves. Arriving without it means the player
            // has to toggle something to find out where they stand.
            if (IsChallengeSelectScreen())
            {
                // 0.7.425 - Zamar: the challenge level is said HERE, where
                // the game prints it (it was on the main menu, Session 37).
                string level = ChallengeLevelLine();
                string total = ChallengePointsLine();
                total = total == null ? level : level + " " + total;
                Speech.Browse(Tidy(Vocabulary.Menus.Name(ScreenName(), total, controls)));
                return;
            }

            string body = ScreenBodyText();
            if (!string.IsNullOrEmpty(body))
            {
                Speech.Browse(Tidy(Vocabulary.Menus.ScreenBodyControls(ScreenName(), body, controls)));
                return;
            }

            Speech.Browse(Tidy(Vocabulary.Menus.ScreenThenControlsNoBody(ScreenName(), controls)));
        }

        /// <summary>
        /// Every string the screen itself is printing, in hierarchy order, for
        /// the screens that are made of text rather than of options.
        ///
        /// SCOPED BY NAME, NOT GENERAL. Read on every screen this would double
        /// each option's label back at the player, since an option's own text
        /// components live under the same root. The two confirm screens are the
        /// only ones whose content is not reachable any other way.
        ///
        /// The buttons on them carry no text of their own — his log has both
        /// reading "via gameObject name", which is LabelFor reporting that it
        /// found no string on them — so nothing collected here is an option
        /// label. Logged anyway, so the first playtest can say whether that
        /// holds.
        /// </summary>
        private static string ScreenBodyText()
        {
            string raw = _activeScreen?.gameObject?.name ?? "";
            if (!raw.EndsWith("ConfirmScreen")) return null;

            var parts = new List<string>();
            var seen  = new HashSet<string>();

            try
            {
                foreach (var c in _activeScreen.gameObject.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform) continue;

                    bool live = false;
                    try { live = c.gameObject.activeInHierarchy; } catch { }
                    if (!live) continue;

                    string v = UiText.Of(c);
                    if (string.IsNullOrEmpty(v)) continue;

                    // THE SCREEN'S MARKUP IS NOT THE SCREEN'S WORDS. (0.7.244.)
                    //
                    // Zamar: "The New Run screen was borked." His log shows both
                    // reasons — a hundred and seventy dashes read out as a
                    // divider, and then "STARTING A <COLOR=#EEF4C6>NEW RUN
                    // </COLOR> WILL..." with the colour tags said aloud.
                    //
                    // Same tag shape the devlog cleaner uses, and for the same
                    // reason: a rich-text tag is a name with no spaces in it,
                    // optionally followed by a value. See SetPendingEntry.
                    v = System.Text.RegularExpressions.Regex.Replace(
                            v, @"</?[a-zA-Z][a-zA-Z0-9]*(?:[-=][^<>]*)?>", "");
                    v = System.Text.RegularExpressions.Regex.Replace(v, @"\s+", " ").Trim();
                    if (v.Length == 0) continue;

                    // A RULE IS NOT A SENTENCE. The dashes draw a line across
                    // the screen; read aloud they are a hundred and seventy
                    // hyphens or, worse, a long silence. Anything with no letter
                    // and no digit in it is decoration.
                    bool hasWord = false;
                    foreach (char ch in v)
                        if (char.IsLetterOrDigit(ch)) { hasWord = true; break; }
                    if (!hasWord) continue;

                    // A header and its drop shadow are two components holding
                    // the same string. Saying it twice is the defect.
                    if (!seen.Add(v)) continue;

                    parts.Add(Sentence(v));
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MENU: reading the screen text threw {e.GetType().Name}.");
                return null;
            }

            if (parts.Count == 0) return null;

            string joined = string.Join(" ", parts.ToArray());
            _log?.LogInfo($"IKMA MENU: {raw} body text — '{joined}'");
            return joined;
        }

        /// <summary>
        /// Is the option under the cursor a paragraph rather than a phrase?
        /// Measured rather than screen-matched, so a future screen with a long
        /// label gets the same treatment without anyone remembering to add it.
        ///
        /// CURRENTLY UNCALLED. (Session 16.) Its only caller blanked the option
        /// count on a long label, and the option count is no longer spoken on
        /// arrival at all. Kept rather than deleted because the underlying
        /// question — "is this label a paragraph" — is the right test for any
        /// future decision about what to trim on a long screen, and rewriting it
        /// from scratch later would be waste. Delete it if nothing claims it.
        /// </summary>
        private static bool LabelIsLong(List<MainInputInteractable> items, int index)
        {
            string label = LabelFor(items, index);
            return !string.IsNullOrEmpty(label) && label.Length > 120;
        }

        /// <summary>Collapse the double space and stray " ." an empty part leaves.</summary>
        private static string Tidy(string line)
            => string.IsNullOrEmpty(line) ? line : line.Replace("  ", " ").Replace(" .", ".");

        /// <summary>
        /// True once a newly-enabled screen has finished revealing its options
        /// and has not yet been announced. Called every frame from
        /// HotkeyManager.Update, which supplies the elapsed time.
        /// </summary>
        public static bool NeedsScreenAnnouncement(float deltaTime)
        {
            if (_activeScreen == null || _screenAnnounced || _suppressed) return false;

            int count = GetItems().Count;
            if (count == 0)
            {
                _stableCount = -1;
                _countStableFor = 0f;
                return false;
            }

            if (count != _stableCount)
            {
                _stableCount = count;
                _countStableFor = 0f;
                return false;
            }

            _countStableFor += deltaTime;
            return _countStableFor >= SCREEN_SETTLE_SECONDS;
        }

        public static void Browse(int direction)
        {
            // Session 13: a screen that has not announced itself yet does not
            // take browse input. Zamar's menu log has the symptom — coming back
            // to Unlocks read "STARTER DECKS.", then the Unlocks screen
            // announcement, then more stale labels. An arrow pressed during the
            // transition was being answered against the NEW screen's item list
            // while the player still believed they were on the old one, so the
            // reply belonged to neither.
            //
            // Swallowed rather than answered: the screen announcement is
            // already on its way and will say where they are.
            if (!_screenAnnounced) return;

            var items = GetItems();
            if (items.Count == 0)
            {
                Speech.Browse(Vocabulary.NoOptionsAvailable);
                return;
            }

            // Session 9: position ("4 of 13") is NOT spoken while browsing.
            // On a long screen like Stats it doubled the length of every line
            // for information the player rarely needs mid-scroll. M reports it
            // on demand instead.
            var previous = (_index >= 0 && _index < items.Count) ? items[_index] : null;

            // From NOWHERE, the first press lands ON the option arrival named
            // (forwards) or on the last one (backwards) — it does not step past
            // them. Everywhere else, move and wrap as before.
            if (_index == NOWHERE || _index >= items.Count)
                _index = direction >= 0 ? DefaultIndex(items) : items.Count - 1;
            else
                _index = (_index + direction + items.Count) % items.Count;

            // Menu browse is timed separately from the battle path: the two
            // share almost no code, and the 0.7.30 caching fix was aimed here.
            // If menus are snappy and the hand still hitches, that rules a whole
            // half of the codebase out.
            long tLabel = Perf.Now();
            string spoken = Sentence(LabelFor(items, _index));
            double composeMs = Perf.MsSince(tLabel);

            HoverMenuItem(previous, items[_index]);

            long tSpeak = Perf.Now();
            Speech.Browse(spoken);
            Perf.ReportSplit("menu browse", composeMs, Perf.MsSince(tSpeak));
        }

        /// <summary>
        /// The word in front of "N of M" for this kind of option, or null for a
        /// bare count. "Deck 1 of 9" is Zamar's wording for the starter deck
        /// screen, where "1 of 9" alone had nothing to attach to.
        /// </summary>
        private static string PositionNounFor(object item)
            => Vocabulary.Menus.PositionNounFor(item is AscensionStarterDeckIcon);

        /// <summary>
        /// Put "N of M" after the first sentence of a label. A label with no
        /// sentence break gets it appended, which is where it has always been.
        /// </summary>
        private static string InsertPosition(string label, string noun, int index, int count)
        {
            // Session 16, Zamar, on hearing "CARDS DRAWN: 75. 4 of 13." from the
            // Stats screen: "When pressing M make sure it says Option 4 of 13."
            // Two bare numbers had nothing to attach to. Every screen now names
            // what it is counting, and "Option" is the default where the screen
            // has no better word of its own.
            string position = Vocabulary.Menus.OfCount(noun, index, count);

            if (string.IsNullOrEmpty(label)) return position;

            int cut = label.IndexOf(". ", System.StringComparison.Ordinal);
            if (cut < 0) return $"{label} {position}";

            return label.Substring(0, cut + 1) + " " + position + label.Substring(cut + 1);
        }

        public static void JumpEdge(bool toStart)
        {
            var items = GetItems();
            if (items.Count == 0) return;
            _index = toStart ? 0 : items.Count - 1;
            Speech.Browse(LabelFor(items, _index) + ".");
        }

        public static void AnnounceCurrent()
        {
            var items = GetItems();
            if (items.Count == 0)
            {
                Speech.Browse(Vocabulary.NoOptionsAvailable);
                return;
            }
            // Space answers for the option arrival named while the cursor is
            // still NOWHERE, and does NOT commit the cursor there — repeating
            // where you are must never be a move.
            int here = (_index == NOWHERE || _index >= items.Count) ? DefaultIndex(items) : _index;
            // Session 13: the screen name is spoken ON ENTRY and nowhere else.
            // M used to lead with it, so every repeat began by telling the
            // player which screen they were on — which they knew, having just
            // arrived there and not moved. The player pressed M to hear the
            // OPTION again; the name was in the way of the answer.
            // POSITION GOES NEAR THE FRONT ON A LONG LABEL. (0.7.43.)
            //
            // Zamar, 0.7.42 playtest: "at the end of reading starter deck
            // Vanilla it says 1 of 9 with no context." A starter deck label runs
            // three full card descriptions long, so by the time "1 of 9" arrives
            // the player has sat through twenty seconds of speech and the two
            // numbers have nothing left to attach to.
            //
            // The settled rule that option counts go at the END of a line still
            // holds for short labels, and this does not break it: the position
            // is inserted after the label's FIRST SENTENCE, so "Back." still
            // reads "Back. 1 of 15." and only a label with a body ahead of it
            // moves. Same principle as the rulebook puzzle code — put the part
            // being scanned for in front of the part being sat through.
            // Session 37 (note C15 / D10): a starter deck's position counts
            // decks only. The game has 8 (data/ascension/starterdecks); the
            // count of every item on the screen (the decks plus Back and the
            // like) made New Run say "of 10" and Unlocks "of 9".
            int posIndex = here + 1, posCount = items.Count;
            if (items[here] is AscensionStarterDeckIcon)
            {
                posIndex = 0; posCount = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (!(items[i] is AscensionStarterDeckIcon)) continue;
                    posCount++;
                    if (i <= here) posIndex++;
                }
            }
            // 0.7.424 - no challenge level on the main menu (his call).
            // 0.7.425 - on Space at the challenge select screen instead, the
            // same arrival-and-Space pair he set in Session 37.
            string levelNote = IsChallengeSelectScreen() ? " " + ChallengeLevelLine() : "";
            Speech.Browse(
                InsertPosition(Sentence(LabelFor(items, here)),
                               PositionNounFor(items[here]),
                               posIndex, posCount) + levelNote);
        }

        // ------------------------------------------------------------------
        // Activate through the game's own dispatch — delegates and virtual
        // handlers both. Identical to the draw pile / map node / item slot path.
        // ------------------------------------------------------------------
        // ----------------------------------------------------------------------
        // IS THIS ITEM THE GREYED-OUT TWIN OF A REAL ONE? (0.7.230.)
        //
        // Returns the line to say instead of pressing, or null to press
        // normally. Asked of the screen's own serialized reference, so nothing
        // here duplicates the game's rule about when a run can be continued.
        //
        // Only the start screen has such a twin today. If another screen grows
        // one, add it here beside this — the shape generalises, the list does
        // not need to be guessed at in advance.
        // ----------------------------------------------------------------------
        private static MainInputInteractable _disabledContinue;
        private static object _disabledContinueOwner;

        private static string InertReason(MainInputInteractable item)
        {
            if (item == null) return null;

            // Session 34 - A LOCKED DEVLOG ENTRY. The same two-object shape as
            // Continue Run: AscensionJournalSummaryScreen shows an entry from
            // its private disabledEntryInteractables list when the entry is not
            // unlocked yet, and nothing is wired to those. Zamar's words.
            try
            {
                if (IsDevlogSummaryScreen())
                {
                    var summary = _activeScreen != null
                        ? _activeScreen.GetComponentInChildren<AscensionJournalSummaryScreen>(true)
                        : null;
                    var field = typeof(AscensionJournalSummaryScreen).GetField(
                        "disabledEntryInteractables",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var disabled = summary != null
                        ? field?.GetValue(summary) as System.Collections.Generic.List<AscensionMenuInteractable>
                        : null;
                    if (disabled != null && item is AscensionMenuInteractable ami && disabled.Contains(ami))
                        return Vocabulary.DevLogLocked;
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MENU: devlog lock check threw {e.GetType().Name}: {e.Message}");
            }

            try
            {
                var screens = Singleton<AscensionMenuScreens>.Instance;
                if (screens == null) return null;

                var startField = typeof(AscensionMenuScreens).GetField(
                    "startScreen",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                var startObj = startField?.GetValue(screens) as UnityEngine.GameObject;
                var start = startObj != null ? startObj.GetComponent<AscensionStartScreen>() : null;
                if (start == null) return null;

                // Cached per screen instance; the reflection is only paid once
                // and this runs on a keypress, never per frame.
                if (!ReferenceEquals(_disabledContinueOwner, start))
                {
                    _disabledContinueOwner = start;
                    _disabledContinue = null;

                    var field = typeof(AscensionStartScreen).GetField(
                        "continueRunDisabledText",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                    _disabledContinue = field?.GetValue(start) as MainInputInteractable;

                    if (_disabledContinue == null)
                        _log?.LogInfo("IKMA MENU: continueRunDisabledText did not resolve.");
                }

                if (_disabledContinue != null && ReferenceEquals(item, _disabledContinue))
                    return Vocabulary.NoRunToContinue();
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MENU: inert check threw {e.GetType().Name}: {e.Message}");
            }

            return null;
        }

        public static void Activate()
        {
            var items = GetItems();
            if (items.Count == 0)
            {
                Speech.Browse(Vocabulary.NoOptionsAvailable);
                return;
            }
            // Enter straight off the screen announcement chooses the option
            // that announcement named, not option zero by accident.
            if (_index == NOWHERE || _index >= items.Count) _index = DefaultIndex(items);

            var item = items[_index];
            string label = LabelFor(items, _index);

            // A KEY THAT DOES NOTHING IS THE DEFECT THIS PROJECT FIXES.
            // (0.7.230.)
            //
            // Zamar, Kaycee's Mod start screen with no run saved: "have
            // pressing Enter on it give a 'No current run available' warning."
            // His log has 'CONTINUE RUN' activated SEVEN times in a row, each
            // one logged as a successful activation, each one doing nothing:
            //
            //   IKMA MENU: activating 'CONTINUE RUN'.
            //   IKMA MENU: transition kept — active screen is live...
            //   ... x7
            //
            // A sighted player sees the words in a different colour and never
            // presses them. The mod read the greyed-out text as an option and
            // offered it as one.
            //
            // AND THE GAME'S OWN ANSWER IS TWO OBJECTS, NOT A FLAG.
            // AscensionStartScreen.UpdateContinueTextEnabled does
            //
            //     continueRunText.gameObject.SetActive(RunExists);
            //     continueRunDisabledText.gameObject.SetActive(!RunExists);
            //
            // so the live one IS the state, and the disabled object is a
            // separate interactable with nothing wired to it. IKMA does not
            // recompute RunExists or read the save — it asks which object this
            // is. See InertReason.
            string inert = InertReason(item);
            if (inert != null)
            {
                _log?.LogInfo($"IKMA MENU: '{label}' is the disabled variant — not pressed.");
                Speech.Confirm(inert);
                return;
            }

            try
            {
                _log?.LogInfo($"IKMA MENU: activating '{label}'.");

                // A page arrow does NOT leave the screen — it swaps the cards on
                // it. So it must not run the transition path below, which goes
                // silent waiting for a screen that will never enable. (Session
                // 14: this is why turning a page has to be handled here rather
                // than falling through.)
                bool isPageTurn = ReferenceEquals(item, _pageLeft) || ReferenceEquals(item, _pageRight);
                if (isPageTurn)
                {
                    item.CursorSelectStart();
                    item.CursorSelectEnd();
                    StartPageTurn();
                    return;
                }

                // A CHALLENGE TOGGLE DOES NOT LEAVE THE SCREEN EITHER, so it
                // takes the page-turn path rather than the transition one:
                // BeginTransition goes silent waiting for a screen to enable,
                // and this screen is not going anywhere. (0.7.240.)
                var challengeIcon = item as AscensionIconInteractable;
                if (challengeIcon != null && IsChallengeSelectScreen())
                {
                    bool wasActive = IconIsLit(challengeIcon);
                    item.CursorSelectStart();
                    item.CursorSelectEnd();
                    SpeakChallengeToggle(challengeIcon, wasActive);
                    return;
                }

                // Session 9: choosing an option means this screen is leaving.
                // Drop anything still queued from it and go silent until a new
                // screen enables — otherwise the outgoing screen kept talking
                // over the transition as its options deactivated one by one.
                BeginTransition();

                // Session 13: this used to re-speak the label. Zamar heard
                // "DEVLOG." browsing to it, "DEVLOG." again on Enter, then
                // "Devlog. 13 options..." from the screen itself — the same word
                // three times in a row. The player chose this option one
                // keypress ago and does not need it read back; the screen that
                // opens will say where they are.
                item.CursorSelectStart();
                item.CursorSelectEnd();
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MENU: activation failed: {e.Message}");
                Speech.Browse(Vocabulary.OptionCouldNotBeSelected);
            }
        }

        /// <summary>
        /// A CHALLENGE TOGGLE IS NOT A SCREEN CHANGE — it is a value changing
        /// in front of the player, and the screen answers it in its own footer.
        /// (0.7.240.)
        ///
        /// Zamar: "When enabling or disabling a challenge it should first read
        /// what your new current challenge points total is at."
        ///
        /// The total comes FIRST because that is the number being managed: a
        /// player on this screen is spending toward a level requirement, and
        /// which challenge moved is something they just chose and already know.
        /// Read AFTER the press — AscensionIconInteractable.OnCursorSelectStart
        /// calls SetChallengeActivated synchronously, so the number is already
        /// the new one by the time this runs.
        ///
        /// A LOCKED CHALLENGE IS SILENT HERE, and correctly so: that same
        /// handler is gated on `clickable &amp;&amp; Unlocked`, so nothing
        /// happened and there is no new total to report.
        ///
        /// PROVISIONAL wording.
        /// </summary>
        private static void SpeakChallengeToggle(AscensionIconInteractable icon, bool wasActive)
        {
            var info = icon?.Info;
            if (info == null) return;

            bool nowActive = IconIsLit(icon);
            if (nowActive == wasActive)
            {
                // Locked, or the press did nothing. Session 35 (test driver):
                // Enter on "All Totem Battles" changed nothing and said
                // nothing, while the browse line had read it as unlocked. The
                // game only toggles an icon that is unlocked for the current
                // challenge level (AscensionIconInteractable.OnCursorSelectStart).
                // Say so when it is locked, and log what IKMA and the save
                // said either way, so the next log shows which one disagreed.
                bool unlocked = ChallengeIsUnlocked(info);
                int level = -1;
                try { level = AscensionSaveData.Data.challengeLevel; } catch { }
                Plugin.Log?.LogWarning($"IKMA MENU: challenge press changed nothing - '{info.title}', IKMA says unlocked={unlocked}, challenge level {level}.");
                if (!unlocked) Speech.Browse(Vocabulary.Menus.ChallengeLocked);
                return;
            }

            string points = ChallengePointsLine();
            string state  = Vocabulary.Menus.EnabledOrDisabled(nowActive);
            int    nth    = DuplicateTitleIndex(icon, info.title);
            string title  = string.IsNullOrEmpty(info.title)
                          ? Vocabulary.Menus.ChallengeFallback
                          : (nth > 0 ? $"{info.title} {nth}" : info.title);


            Speech.Confirm(points == null
                ? $"{title} {state}."
                : $"{points} {title} {state}.");
        }

        /// <summary>
        /// Back button, if this screen has one. AscensionMenuBackButton derives
        /// from AscensionMenuInteractable, so it appears in the same list.
        /// </summary>
        /// <summary>
        /// Does this screen actually have a back button? Same lookup
        /// ActivateBackButton uses, so the announcement and the key can never
        /// disagree about whether Backspace does anything here.
        /// </summary>
        /// <summary>
        /// The Unlocks -> Challenges list. Matched on the screen object's name
        /// rather than a component type, because the screen is an ordinary
        /// AscensionMenuScreenTransition with challenge icons on it and has no
        /// class of its own to ask. The name is from Zamar's 0.7.230 log
        /// ("IKMA MENU: screen active - AscensionUnlockedChallengesScreen"),
        /// not guessed.
        /// </summary>
        private static bool IsUnlockedChallengesScreen()
            => (_activeScreen?.gameObject?.name ?? "") == "AscensionUnlockedChallengesScreen";

        /// <summary>
        /// The unlocked starter decks, under Unlocks. A readout like Stats and
        /// the unlocked challenges: Enter does nothing here. Matched on the
        /// object name from Zamar's 0.7.237 log, not guessed.
        /// </summary>
        /// <summary>
        /// Is this challenge switched on for the run being configured? Read
        /// from the game's own list every call — AssignInfo can reassign an
        /// icon and a toggle can happen between two keypresses, so nothing here
        /// is cached.
        /// </summary>
        internal static bool ChallengeIsActive(AscensionChallengeInfo info)
        {
            if (info == null) return false;
            try
            {
                var data = AscensionSaveData.Data;
                if (data?.activeChallenges == null) return false;
                return data.activeChallenges.Contains(info.challengeType);
            }
            catch { return false; }
        }

        // ==================================================================
        // ONE ICON'S OWN LIGHT, NOT THE RUN'S LIST. (0.7.242.)
        //
        // Zamar: "When enabling or disabling More Difficult while the other one
        // was on, I heard no call out."
        //
        // MORE DIFFICULT IS ONE CHALLENGE ON TWO ICONS. There is a single asset
        // — Challenge_BaseDifficulty, challengeType 3, worth 15 — and the screen
        // offers it twice so it can be STACKED; AscensionSaveData counts
        // duplicates in activeChallenges on purpose. So
        // activeChallenges.Contains is true the moment either icon is lit, and
        // the toggle guard "did this change?" answered no for the second one and
        // swallowed the line.
        //
        // AscensionIconInteractable.activatedRenderer is what a sighted player
        // is actually looking at, and OnCursorSelectStart flips it per icon. It
        // is private, so it is read by reflection — but it is the only source
        // that can tell two identical icons apart, which is the whole problem.
        //
        // Falls back to the run's list when the field cannot be found: worse,
        // but never wrong for the single-icon challenges, which is all of them
        // but one.
        // ==================================================================
        private static FieldInfo _activatedRendererField;
        private static bool _activatedRendererResolved;

        internal static bool IconIsLit(AscensionIconInteractable icon)
        {
            if (icon == null) return false;

            try
            {
                if (!_activatedRendererResolved)
                {
                    _activatedRendererResolved = true;
                    _activatedRendererField = typeof(AscensionIconInteractable).GetField(
                        "activatedRenderer", BindingFlags.Instance | BindingFlags.NonPublic);

                    if (_activatedRendererField == null)
                        _log?.LogWarning("IKMA MENU: AscensionIconInteractable.activatedRenderer not found — " +
                                         "stacked challenges will read from the run's list instead.");
                }

                if (_activatedRendererField != null)
                {
                    var renderer = _activatedRendererField.GetValue(icon) as Renderer;
                    if (renderer != null) return renderer.enabled;
                }
            }
            catch { }

            return ChallengeIsActive(icon.Info);
        }

        /// <summary>
        /// A number on a title the screen shows more than once. (0.7.242.)
        ///
        /// Zamar: "Let's change these to More Difficult 1 and More Difficult 2.
        /// Have 1 and 2 added to their names."
        ///
        /// Two icons reading the same words are two options a blind player
        /// cannot tell apart — and they are genuinely different options, because
        /// each is a separate stack of the same challenge. The number is IKMA's
        /// addition and says so: it counts icons of that title in screen order,
        /// so it is stable for as long as the list is.
        ///
        /// Returns 0 when the title appears once, which is every other row.
        /// </summary>
        private static int DuplicateTitleIndex(MainInputInteractable item, string title)
        {
            if (string.IsNullOrEmpty(title)) return 0;

            try
            {
                // The screen's own icons, read directly. NOT GetItems() —
                // LabelFor is called from inside GetItems' reordering, and
                // calling back into it would be re-entrant. Hierarchy order is
                // what the screen lays out, which is the order the numbers are
                // meant to follow.
                var icons = _activeScreen?.gameObject
                    ?.GetComponentsInChildren<AscensionIconInteractable>(true);
                if (icons == null) return 0;

                int total = 0, mine = 0;
                foreach (var other in icons)
                {
                    var icon = other as AscensionIconInteractable;
                    var info = icon?.Info;
                    if (info == null || info.title != title) continue;

                    total++;
                    if (ReferenceEquals(other, item)) mine = total;
                }

                return total > 1 ? mine : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// The running total the screen prints in its header, and the level it
        /// is being measured against. Both PUBLIC on AscensionSaveData:
        /// GetActiveChallengePoints() is what ChallengeLevelText.UpdateText
        /// reads, so this reports the number on screen rather than one IKMA
        /// adds up itself.
        /// </summary>
        internal static string ChallengePointsLine()
        {
            try
            {
                var data = AscensionSaveData.Data;
                if (data == null) return null;

                int points = data.GetActiveChallengePoints();
                int level   = data.challengeLevel;

                // Above level 12 the screen stops printing a requirement —
                // "ALL CHALLENGE LEVELS CLEARED!" — so neither does this.
                if (level > 12)
                    return Vocabulary.Menus.ChallengePointCount(points);

                int needed = AscensionSaveData.GetChallengePointsForLevel(level);
                return Vocabulary.Menus.OfChallengePoints(points, needed);
            }
            catch { return null; }
        }

        /// <summary>
        /// The screen where a run's starter deck is CHOSEN — not the unlocks
        /// record. His log names the object 'AscensionStarterDeckScreen'.
        /// </summary>
        // Session 32 (M6 audit). Any failure answers "unlocked": the old
        // behaviour, never a lock the screen does not show.
        private static bool ChallengeIsUnlocked(AscensionChallengeInfo info)
        {
            try
            {
                var data = AscensionSaveData.Data;
                if (data == null) return true;
                return AscensionUnlockSchedule.ChallengeIsUnlockedForLevel(info.challengeType, data.challengeLevel);
            }
            catch { return true; }
        }

        // Session 32 (M6 audit). The game's own condition in
        // AscensionCardsScreen.DisplayCardDescription: card.Info.name ==
        // "Coyote" on a card unlock / card summary screen.
        private static bool IsHiddenOnCardsScreen(GBC.PixelSelectableCard card)
        {
            try
            {
                return card?.Info != null && card.Info.name == "Coyote"
                    && _activeScreen != null
                    && _activeScreen.GetComponentInChildren<AscensionCardsScreen>(true) != null;
            }
            catch { return false; }
        }

        private static bool IsStarterDeckSelectScreen()
            => (_activeScreen?.gameObject?.name ?? "") == "AscensionStarterDeckScreen";

        private static bool IsStarterDeckSummaryScreen()
            => (_activeScreen?.gameObject?.name ?? "") == "AscensionStarterDeckSummaryScreen";

        private static bool IsStatsScreen()
        {
            try
            {
                return _activeScreen != null
                    && _activeScreen.GetComponentInChildren<AscensionStatsScreen>(true) != null;
            }
            catch { return false; }
        }

        // ==================================================================
        // 0.7.360 — NOT EVERY AscensionMenuBackButton GOES BACK. Zamar:
        // "I hit backspace on the Are You Sure screen and it advanced instead
        // of going back." The New Run confirm screen's Continue is itself an
        // AscensionMenuBackButton (screenToReturnTo = the starter deck
        // screen), listed first, and this took the first one. Prefer the one
        // the game binds to its own back key (listenForBackButton, PUBLIC),
        // then one named Back, then the first as before.
        // ==================================================================
        private static MainInputInteractable PickBackButton(List<MainInputInteractable> items)
        {
            if (items == null) return null;
            MainInputInteractable first = null, listening = null, named = null;
            foreach (var item in items)
            {
                var b = item as AscensionMenuBackButton;
                if (b == null) continue;
                if (first == null) first = item;
                string n = "";
                try { n = b.gameObject.name ?? ""; } catch { }
                bool isNamedBack = n.IndexOf("back", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (b.listenForBackButton && isNamedBack) return item;
                if (b.listenForBackButton && listening == null) listening = item;
                if (isNamedBack && named == null) named = item;
            }
            return listening ?? named ?? first;
        }

        private static bool HasBackButton(List<MainInputInteractable> items)
        {
            if (items == null) return false;
            foreach (var item in items)
                if (item is AscensionMenuBackButton) return true;
            return false;
        }

        public static bool ActivateBackButton()
        {
            var items = GetItems();
            var back = PickBackButton(items);
            foreach (var item in items)
            {
                if (ReferenceEquals(item, back))
                {
                    try
                    {
                        _log?.LogInfo("IKMA MENU: activating back button.");
                        BeginTransition();
                        Speech.Browse(Vocabulary.Menus.BackPressed);
                        item.CursorSelectStart();
                        item.CursorSelectEnd();
                        return true;
                    }
                    catch (System.Exception e)
                    {
                        _log?.LogWarning($"IKMA MENU: back button failed: {e.Message}");
                        return false;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// The screen is going away. Clear queued speech and suppress further
        /// menu announcements until a new screen enables. Public so scene
        /// changes can call it too — any transition should stop the previous
        /// screen's narration dead rather than let it trail into the next one.
        /// </summary>
        public static void BeginTransition()
        {
            // Session 11: this ran unconditionally from OnSceneLoaded and is why
            // the Kaycee's Mod main menu came up silent.
            //
            // The incoming screen's OnEnable fires DURING the scene load, before
            // sceneLoaded. So SetActiveScreen cleared the flags, and a moment
            // later BeginTransition set them straight back — the announcement
            // was suppressed before it ever had a chance to fire, and stayed
            // suppressed until the player clicked something. Loading into the
            // main menu meant hearing "Loading." and then nothing at all, with
            // no way to tell a waiting game from a broken mod.
            //
            // A screen that is alive and on-screen right now is not a stale one.
            // Only suppress when there is genuinely nothing there.
            if (_activeScreen != null
                && _activeScreen.gameObject != null
                && _activeScreen.gameObject.activeInHierarchy)
            {
                _log?.LogInfo("IKMA MENU: transition kept — active screen is live, announcement stands.");
                CombatAnnouncer.ClearQueue();
                return;
            }

            _suppressed = true;
            _screenAnnounced = true;
            _stableCount = -1;
            _countStableFor = 0f;
            CombatAnnouncer.ClearQueue();
        }

        // ==================================================================
        // Label resolution. ITERATION POINT — see class summary.
        // ==================================================================

        private static readonly Dictionary<int, string> _labelCache = new Dictionary<int, string>();

        // ------------------------------------------------------------------
        // Move the game's own cursor as the player arrows, so the screen shows
        // what is being read. (Session 13.)
        //
        // Several KM screens put the real content in a panel that only appears
        // on hover — the challenge screen's name, description and point value
        // live at the bottom and only when the cursor is on an icon. Without
        // this a blind player streaming to a sighted audience narrates a
        // challenge while the screen shows nothing at all, and the viewers
        // cannot follow. Same reason target slots got this in Session 11.
        //
        // Every menu interactable is a MainInputInteractable, so CursorEnter and
        // CursorExit are public and inherited.
        // ------------------------------------------------------------------
        private static MainInputInteractable _hovered;

        internal static void ClearHover()
        {
            // Session 13: leaving a screen without releasing the hover left the
            // option we were on lit up. Zamar came back from Stats to the main
            // menu and found BOTH the first option and Stats blinking — the old
            // screen's hover had never been released, so the game believed two
            // things were under the cursor at once. IKMA moved that cursor, so
            // IKMA has to put it back.
            try { if (_hovered != null) _hovered.CursorExit(); }
            catch { /* the object may already be gone with its screen */ }
            _hovered = null;
        }

        private static void HoverMenuItem(MainInputInteractable leaving, MainInputInteractable entering)
        {
            try
            {
                if (leaving != null && leaving != entering) leaving.CursorExit();
                if (_hovered != null && _hovered != entering && _hovered != leaving) _hovered.CursorExit();
                if (entering != null) entering.CursorEnter();
                _hovered = entering;
            }
            catch (System.Exception e)
            {
                // Presentation must never break navigation.
                _log?.LogWarning($"IKMA MENU: hover sync failed: {e.Message}");
            }
        }

        // A label that already ends in punctuation does not get another full
        // stop. Session 13: challenge descriptions end in a period, so the
        // screen read "You do not start with the Fish Hook item.." — a stutter
        // on every challenge.
        private static string Sentence(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            char last = label[label.Length - 1];
            return (last == '.' || last == '!' || last == '?') ? label : label + ".";
        }

        private static string LabelFor(List<MainInputInteractable> items, int index)
        {
            if (index < 0 || index >= items.Count) return Vocabulary.Menus.UnknownOption;
            return LabelFor(items[index], index);
        }

        /// <summary>
        /// Characters a screen reader says the wrong thing about. (0.7.45.)
        ///
        /// Zamar, 0.7.43 playtest: the devlog reads "ENTRY #01" and SAPI5 says
        /// "hashtag 1". The screen prints a symbol that means "number", so the
        /// word is what the player should hear — this is not rewriting what the
        /// screen says, it is reading the symbol correctly.
        ///
        /// Applied to menu labels only. Card text and rulebook text come from
        /// the game's own strings and have not been reported wrong.
        /// </summary>
        private static string SpeakableSymbols(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            if (label.IndexOf('#') < 0) return label;
            return label.Replace("#", Vocabulary.Menus.NumberSign).Replace(Vocabulary.Menus.NumberSign + " ", Vocabulary.Menus.NumberSign);
        }

        private static string LabelFor(MainInputInteractable item, int index)
        {
            if (item == null) return Vocabulary.Menus.UnknownOption;

            // THE CHALLENGE SCREEN'S CONTINUE BUTTON IS CALLED START RUN, and
            // the game says so itself. (0.7.240.)
            //
            // Zamar: "Continue should be Start Run." AscensionChallengeScreen
            // .OnContinueCursorEnter pushes the literal string "START RUN" into
            // the challenge displayer the moment a mouse touches that button —
            // so a sighted player reads START RUN and IKMA was reading the
            // GameObject's name, which is 'Continue'. This is not renaming
            // anything; it is reading what the screen prints instead of what
            // the scene graph calls it.
            //
            // NOT CACHED, and ahead of the cache on purpose: the same button
            // object is 'Continue' on the New Run Confirm screen, where that IS
            // the word on screen.
            if (ReferenceEquals(item, StartRunButton()))
            {
                _log?.LogInfo($"IKMA MENU: item {index + 1} label='Start Run' " +
                              "via AscensionChallengeScreen.continueButton (the screen prints START RUN)");
                return Vocabulary.Menus.StartRun;
            }

            int id = item.GetInstanceID();
            if (_labelCache.TryGetValue(id, out string cached)) return cached;

            string label = null;
            string via = "gameObject name";

            // ------------------------------------------------------------------
            // Session 13. Ask the typed interactables for their own data BEFORE
            // hunting for text, because on these screens there is no text to
            // find and the hunt falls through to the GameObject's name.
            //
            // Zamar's 0.7.25 menu playtest heard the result: "Starter Deck
            // Icon_4", "Icon_1", "Icon_2". Those are Unity object names being
            // read to a blind player as though they were what the screen says.
            // The screen actually shows a sprite, and the name lives on a
            // ScriptableObject hanging off the interactable — all PUBLIC,
            // confirmed in dump_km_frontend.txt. Internal ids are never display
            // names; find the data object.
            // ------------------------------------------------------------------
            try
            {
                var deckIcon = item as AscensionStarterDeckIcon;
                if (label == null && deckIcon != null)
                {
                    // Parity: a locked deck shows the player a locked icon, not
                    // its name. Reading the name would be telling them something
                    // the screen is deliberately withholding.
                    if (!deckIcon.Unlocked)
                    {
                        label = Vocabulary.Menus.LockedStarterDeck;
                    }
                    else
                    {
                        string title = deckIcon.Info?.title;
                        var deckCards = deckIcon.Info?.cards;
                        int count = deckCards?.Count ?? 0;

                        if (!string.IsNullOrEmpty(title))
                        {
                            // Zamar's format. Numbering the cards gives the ear
                            // something to hold onto — three card descriptions
                            // run together are one long stream with no seams,
                            // and "Card 2 of 3" tells the player where they are
                            // in it without having to count sentences.
                            // "Starter Deck: Vanilla." is Zamar's wording,
                            // 0.7.42 playtest.
                            label = Vocabulary.Menus.StarterDeck(title);

                            // Zamar, Session 13: read the deck's cards in full.
                            // This is parity, not verbosity — hovering a deck
                            // shows its three cards along the bottom of the
                            // screen, with cost, power, health and sigils all
                            // legible. A sighted player picks a starter deck by
                            // reading them, so a blind player has to be able to
                            // as well. Announcing only the name would leave the
                            // choice to memory or luck.
                            if (deckCards != null)
                            {
                                // Zamar, Session 13: the Egg deck is three
                                // copies of one card, and hearing the same
                                // description three times over is a waste of the
                                // player's attention, not thoroughness. Runs of
                                // identical cards collapse into one reading that
                                // names every position it covers, so nothing is
                                // hidden and nothing is said twice.
                                int i = 0;
                                while (i < deckCards.Count)
                                {
                                    string described = CardReader.DescribeCardInfo(deckCards[i]);

                                    int run = 1;
                                    while (i + run < deckCards.Count &&
                                           CardReader.DescribeCardInfo(deckCards[i + run]) == described)
                                        run++;

                                    if (!string.IsNullOrEmpty(described))
                                    {
                                        var positions = new List<string>();
                                        for (int p = 0; p < run; p++) positions.Add((i + p + 1).ToString());

                                        string where;
                                        if (positions.Count == 1)
                                            where = Vocabulary.Menus.CardOf(positions[0], count);
                                        else
                                            where = Vocabulary.Menus.CardsAndOf(positions.GetRange(0, positions.Count - 1), positions[positions.Count - 1], count);

                                        label += Vocabulary.Menus.WhereDescribed(where, described);
                                    }

                                    i += run;
                                }
                            }
                        }
                    }
                    if (label != null) via = "StarterDeckInfo.title";
                }

                // The card screen's paging arrows are plain interactables with
                // no text on them, so they fell through to their object names.
                // Identified by reference against the screen's own fields —
                // never by guessing at a name.
                if (label == null && _pageLeft != null && ReferenceEquals(item, _pageLeft))
                { label = Vocabulary.Menus.PreviousPage; via = "pageLeftButton"; }

                if (label == null && _pageRight != null && ReferenceEquals(item, _pageRight))
                { label = Vocabulary.Menus.NextPage; via = "pageRightButton"; }

                var pixelCard = item as GBC.PixelSelectableCard;
                if (pixelCard != null)
                {
                    // Faded is the screen's own way of showing a card the
                    // player has not unlocked. It is greyed out to a sighted
                    // player, so it is named as locked here and not described.
                    bool faded = false;
                    try { faded = pixelCard.Faded; } catch { }

                    // Session 32 (M6 audit). The card unlock screens always
                    // show the Coyote as "???" and "CARD LOCKED" - the game
                    // hard-codes it by name in AscensionCardsScreen.
                    // DisplayCardDescription. If the page hides it, IKMA hides
                    // it: same condition, same screen family, same answer.
                    if (!faded && IsHiddenOnCardsScreen(pixelCard)) faded = true;

                    if (faded)
                    {
                        label = Vocabulary.Menus.LockedCard;
                    }
                    else
                    {
                        // Everything a sighted player reads off this screen:
                        // the face carries cost, power and health, and the
                        // panel underneath carries name and rules text.
                        label = CardReader.DescribeCardInfo(pixelCard.Info);
                    }
                    if (label != null) via = "PixelSelectableCard.Info";
                }

                var challengeIcon = item as AscensionIconInteractable;
                if (label == null && challengeIcon != null && true)
                {
                    var info = challengeIcon.Info;

                    // Session 32 (M6 audit). A challenge not unlocked at the
                    // player's challenge level shows a lock sprite, and its
                    // panel reads "???" and "CHALLENGE LOCKED"
                    // (AscensionChallengeDisplayer). Its name, points and rules
                    // are hidden from a sighted player, so they are not read.
                    // Same test the icon uses: AscensionUnlockSchedule.
                    // ChallengeIsUnlockedForLevel (PUBLIC STATIC, confirmed).
                    if (info != null && !ChallengeIsUnlocked(info))
                    {
                        label = Vocabulary.Menus.ChallengeLocked;
                        via = "AscensionUnlockSchedule (locked)";
                    }
                    else if (info != null && !string.IsNullOrEmpty(info.title))
                    {
                        // Point value is on the icon for a sighted player and is
                        // the whole basis of choosing challenges, so it is part
                        // of the label rather than an extra press away. The
                        // description trails, because the player is scanning
                        // names first.
                        // The screen itself prints "+5 CHALLENGE POINTS", so
                        // that is what it says. "5 points" left the player to
                        // work out points of what, and challenge points are a
                        // currency of their own.
                        string points = Vocabulary.Menus.PlusChallengePointCount(info.pointValue);

                        // The same title twice on one screen is two options a
                        // blind player cannot tell apart. See DuplicateTitleIndex.
                        int nth = DuplicateTitleIndex(item, info.title);
                        string name = nth > 0 ? $"{info.title} {nth}" : info.title;

                        label = Vocabulary.Menus.ChallengeWithPoints(name, points);

                        if (!string.IsNullOrEmpty(info.description))
                            label += $". {info.description}";

                        // WHETHER IT IS SWITCHED ON, FIRST. (0.7.240.)
                        //
                        // Zamar: "When a challenge is active it should say that
                        // before it's challenge name. example 'Active, No Boss
                        // Rares, plus 15 challenge points...'"
                        //
                        // A sighted player sees the icon lit; this is the one
                        // fact about a challenge that a blind player could not
                        // get at all, and it goes FIRST because it is what they
                        // are sweeping the list for once they start toggling.
                        //
                        // Asked of AscensionSaveData.Data.activeChallenges —
                        // the same list SetChallengeActivated writes — so the
                        // answer is the screen's, never a state IKMA keeps.
                        // THE ICON'S OWN LIGHT, not the run's list — the two
                        // More Difficult icons share one challengeType and the
                        // list cannot tell them apart. See IconIsLit.
                        // Session 37 - Zamar: a beaten challenge says so, after
                        // "Active" (the game marks it on the icon).
                        bool conquered = false;
                        try { conquered = AscensionSaveData.Data.conqueredChallenges.Contains(info.challengeType); } catch { }
                        if (conquered) label = Vocabulary.Menus.ChallengeComplete + " " + label;
                        if (IconIsLit(challengeIcon)) label = Vocabulary.Menus.ActiveLabel(label);

                        via = "AscensionChallengeInfo.title";
                    }
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MENU: typed label read failed: {e.Message}");
                label = null;
            }

            if (label != null)
            {
                // Deliberately NOT cached. AssignInfo can run after an icon
                // exists, and Unlocked can change within a session, so a cached
                // "Locked starter deck" would outlive the lock. A property read
                // per keypress costs nothing.
                _log?.LogInfo($"IKMA MENU: item {index + 1} label='{label}' via {via}");
                return label;
            }

            try
            {
                // Hunt for a string member called "text" on any component under
                // this object. Covers GBC.PixelText, TextMeshPro, UI.Text and
                // anything else the game uses, without naming a type we have
                // not confirmed exists.
                var components = item.gameObject.GetComponentsInChildren<Component>(true);
                foreach (var c in components)
                {
                    if (c == null) continue;
                    var type = c.GetType();

                    var prop = type.GetProperty("text",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.IgnoreCase);
                    if (prop != null && prop.PropertyType == typeof(string))
                    {
                        string value = prop.GetValue(c) as string;
                        if (!string.IsNullOrEmpty(value))
                        {
                            label = value;
                            via = type.Name + ".text (property)";
                            break;
                        }
                    }

                    var field = type.GetField("text",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.IgnoreCase);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        string value = field.GetValue(c) as string;
                        if (!string.IsNullOrEmpty(value))
                        {
                            label = value;
                            via = type.Name + ".text (field)";
                            break;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MENU: label lookup failed: {e.Message}");
            }

            if (string.IsNullOrEmpty(label))
            {
                label = Prettify(item.gameObject.name);

                // THE RANDOM DECK IS A DECK, AND ITS NAME DOES NOT SAY SO.
                // (0.7.242.) Zamar: "Change this to Random Starter Deck."
                //
                // It sits among nine options that each read out three cards, so
                // a bare "Random" is the one entry that never says what kind of
                // thing it is. The object carries no text of its own — this is
                // the Prettify fallback talking — so the word is added here,
                // where the fallback is, rather than invented upstream.
                if (label == "Random" && IsStarterDeckSelectScreen())
                {
                    label = Vocabulary.Menus.RandomStarterDeck;
                    via   = "gameObject name (named as a deck)";
                }
            }

            label = SpeakableSymbols(Clean(label));
            _labelCache[id] = label;
            _log?.LogInfo($"IKMA MENU: item {index + 1} label='{label}' via {via}");
            return label;
        }

        // Menu text arrives shouted and decorated ("- NEW RUN -"). Strip the
        // dashes and collapse whitespace; leave the words themselves alone.
        private static string Clean(string raw)
        {
            string s = raw.Replace("\n", " ").Trim();
            s = s.Trim('-', '=', '<', '>', ' ');
            s = System.Text.RegularExpressions.Regex.Replace(s, @"<[^>]*>", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
            return Vocabulary.Menus.UnlabelledOrText(s.Length, s);
        }

        // "NewRunText" -> "New Run". Fallback only.
        private static string Prettify(string raw)
        {
            string s = raw;
            if (s.EndsWith("Text")) s = s.Substring(0, s.Length - 4);
            if (s.EndsWith("Button")) s = s.Substring(0, s.Length - 6);

            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
