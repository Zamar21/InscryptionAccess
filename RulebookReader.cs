// RulebookReader.cs
//
// The rulebook / ability encyclopedia. (Roadmap item 4, Session 10;
// visual sync Session 11.)
//
// SESSION 11 REWRITE — the reader no longer keeps a page number of its own.
//
// Session 10's version maintained a private _index and narrated from it while
// the physical book sat wherever it had opened. Two consequences, both
// confirmed in test: the book rendered EMPTY, because SetShown only enables the
// rig and page content is loaded by the flipper's render path; and the arrows
// moved a number in our head that nothing on screen shared.
//
// dump_rulebook_visual.txt resolved it. The game's own paging is public:
//
//   RuleBookController : Singleton<RuleBookController>
//       PUBLIC    PageData : List<RuleBookPageInfo>   [get]
//       PUBLIC    Shown    : bool                     [get]
//       PUBLIC    SetShown(bool shown, bool offsetView)
//       PUBLIC    OpenToAbilityPage(string abilityName, PlayableCard card, bool immediate)
//       NONPUBLIC flipper : PageFlipper
//
//   PageFlipper  (the live instance is RulebookPageFlipper, which overrides
//                 ShowFlip with AnimateFlip / TweenPagesForFlip / RenderPages)
//       PUBLIC    Flip(bool forwards, float duration)        -> void
//       PUBLIC    FlipToPage(int pageIndex, float duration)  -> IEnumerator
//       PUBLIC    FlippingToPage : bool
//       NONPUBLIC currentPageIndex : int
//       NONPUBLIC WrapIndex(int index)
//
// So: call the game's Flip, then read currentPageIndex back and narrate THAT.
// The book becomes the source of truth and desync is structurally impossible
// rather than something we keep in step by hand. Wrap-around comes free — the
// game's WrapIndex already does it, which is why running off the end now
// returns to the other cover instead of announcing a wall.
//
// Same principle as CanAttackDirectly and AttackIsBlocked: ask the game its own
// question instead of reimplementing the answer.

using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using DiskCardGame;

namespace IKMA
{
    public static class RulebookReader
    {
        private static ManualLogSource _log;

        private static bool _open = false;

        // Mirror of the flipper's index, refreshed from the book after every
        // move. Never authoritative — only used when the flipper cannot be
        // reached at all, and the log says so when that happens.
        private static int _index = 0;

        public static bool IsOpen => _open;

        // Cached reflection handles, resolved once. Both members are NONPUBLIC
        // and neither is on a type we can reach at compile time in a useful
        // way, so they are read by name with a type-based fallback: a renamed
        // field costs the visual sync and says so, rather than throwing.
        // Milliseconds the last Flip call blocked for. Session 11: page turns
        // hitch for roughly a second each, which makes reaching page 30 a chore.
        // Timed rather than guessed at — the log now reports the game's render
        // cost and our narration cost separately, so the next change targets
        // whichever one is actually expensive.
        private static long _lastFlipMs = 0;

        // Render coalescing. (Session 11.)
        //
        // The timing instrumentation cleared our code: flip 0-14ms, compose
        // 0-1ms. The quarter-second hitch is the game instantiating the page
        // prefab and rebuilding TextMeshPro layout in LoadPageContent, on the
        // frames AFTER Flip returns, and there is no making that cheaper from
        // out here.
        //
        // But narration does not have to wait for paper. The arrow key advances
        // the read immediately and the visual flip is held until the player
        // stops pressing. One press: the book turns 150ms later, imperceptible.
        // Thirty presses: thirty pages of speech at keyboard speed, one render
        // at the end. The stream still lands on the right page, which is the
        // entire point of visual sync — it was never that every intermediate
        // page had to be drawn.
        private const float RENDER_COALESCE_SECONDS = 0.15f;

        // Duration handed to the deferred FlipToPage. Session 11: this was 0f,
        // on the reasoning that an instant render is what we want — and every
        // page came out visually half-drawn, one sheet superimposed on the next.
        // TweenPagesForFlip needs a real interval to settle the page transforms;
        // with zero it starts and never lands. 0.2f is the value that rendered
        // correctly when Flip was being driven directly.
        private const float RENDER_FLIP_DURATION = 0.2f;
        private static bool  _renderPending = false;
        private static float _renderDueAt = 0f;
        private static bool  _desyncReported = false;

        private static FieldInfo _flipperField;
        private static FieldInfo _currentPageIndexField;
        private static bool _reflectionResolved = false;

        public static void Init(ManualLogSource log)
        {
            _log = log;
        }

        // ------------------------------------------------------------------
        // Puzzle codes.
        //
        // Inscryption hides clues for the cabin puzzles on rulebook pages —
        // painted into the page art, where reflection cannot reach them. A
        // sighted player opens the book and sees them; that is the whole
        // argument for putting them here. This is narration of what is
        // visibly on the page, the same as describing card art, and it is
        // parity rather than advantage.
        //
        // The table is keyed on RuleBookPageInfo.pageId and is EMPTY on
        // purpose. Every page read logs its pageId, so the way to fill this in
        // is for Zamar to walk the book, find the pages carrying markings, and
        // pair what is drawn there with the id from the log. Inventing entries
        // from memory of the game would be exactly the confidently-wrong
        // failure the hard rules exist to stop — a wrong safe combination is
        // worse than no combination, because the player will trust it and
        // conclude the puzzle is broken.
        //
        // Spoken FIRST, ahead of the section name and the ability's own name.
        // Zamar's call, Session 14. A player on this page came for the code.
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _puzzleNotes => Loc.PerLanguage(ref _puzzleNotesCache, ref _puzzleNotesLanguage, Build_puzzleNotes);
        private static Dictionary<string, string> Build_puzzleNotes() =>
            new Dictionary<string, string>
        {
            // Session 11, from Zamar reading the page: the Mighty Leap page
            // (pageId 'Reach') carries a combination in red ink. This is his
            // transcription of what is drawn there, not my recollection of the
            // game — which is the only basis on which an entry may be added.
            { "Reach", Vocabulary.Rulebook.CombinationCode },
        };
        private static Dictionary<string, string> _puzzleNotesCache;
        private static string _puzzleNotesLanguage;

        // ------------------------------------------------------------------
        // Ink-obscured pages.
        //
        // Three variable-stat pages have ink splattered across the description
        // so a sighted player CANNOT read them. Reading the clean text aloud
        // would hand blind players information the game deliberately withholds
        // from everyone — advantage, not parity, and the wrong side of the line
        // this whole project is built on. The puzzle codes are the mirror case:
        // those are visible and so we speak them.
        //
        // Detected from the page's own art rather than a hardcoded page list,
        // so a page that gains or loses ink stays correct. Wording per page goes
        // in the table below as Zamar and I write it; until then the fallback
        // states the situation honestly.
        // ------------------------------------------------------------------
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _obscuredPages => Loc.PerLanguage(ref _obscuredPagesCache, ref _obscuredPagesLanguage, Build_obscuredPages);
        private static Dictionary<string, string> Build_obscuredPages() =>
            new Dictionary<string, string>
        {
            // Written by Zamar from the pages themselves, Session 11. The
            // fragments are what survives under the splatter -- not a summary
            // of what the page would say if it were clean. A sighted player
            // gets broken words and has to work it out; so does this.
            { "Mirror",
              Vocabulary.Rulebook.ThisPageIsDestroyed },

            { "Bell",
              Vocabulary.Rulebook.ThisPageIsDestroyedBy },

            { "CardsInHand",
              Vocabulary.Rulebook.ThisPageIsDestroyedByInk },
        };
        private static Dictionary<string, string> _obscuredPagesCache;
        private static string _obscuredPagesLanguage;

        private static bool HasInkOverlay(RuleBookPageInfo page)
        {
            if (page?.additivePrefabs == null) return false;
            foreach (var go in page.additivePrefabs)
            {
                if (go == null) continue;
                if (go.name.IndexOf("Ink", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        // THE LOOKUP IS CACHED AND BACKED OFF. (Fixed 0.7.75.)
        //
        // Singleton<T>.Instance runs FindInstance when it holds nothing, and
        // FindInstance logs a warning every time it comes up empty. TickOpenState
        // asks for the controller EVERY FRAME, above every layer, to notice a
        // book opened with the mouse — and most scenes have no RuleBookController
        // at all.
        //
        // Zamar's 0.7.74 log was 83,513 lines. 82,761 of them were
        // "Got null in Singleton<DiskCardGame.RuleBookController>.FindInstance".
        // 99.1% of a 7.5 MB file he reads back with a screen reader, and it was
        // IKMA's own doing.
        //
        // Same fix as DialogueAdvancer at 0.7.48 and for the same reason: hold a
        // found instance, do not retry a miss for two seconds, and skip the
        // search entirely where it is guaranteed to fail. Worst case goes from
        // sixty warnings a second to one every two.
        //
        // CLEARED ON SCENE CHANGE, because a held reference to a destroyed
        // controller is the cached-state mistake this project already has a rule
        // about — ask the game, never remember what IKMA saw.
        private static RuleBookController _ruleBookController;
        private static float _nextControllerLookup;
        private const float CONTROLLER_BACKOFF_SECONDS = 2f;

        internal static void ResetForScene()
        {
            _ruleBookController   = null;
            _nextControllerLookup = 0f;
        }

        private static RuleBookController Controller
        {
            get
            {
                if (_ruleBookController != null) return _ruleBookController;

                // A Kaycee's Mod menu screen has no rulebook and cannot open
                // one, so the search is guaranteed to miss and guaranteed to
                // log. Skip it rather than back off from it.
                if (MenuReader.MenuActive()) return null;

                if (UnityEngine.Time.unscaledTime < _nextControllerLookup) return null;
                _nextControllerLookup = UnityEngine.Time.unscaledTime + CONTROLLER_BACKOFF_SECONDS;

                _ruleBookController = Singleton<RuleBookController>.Instance;
                return _ruleBookController;
            }
        }

        /// <summary>True when the book exists and has pages to read.</summary>
        public static bool Available()
        {
            var c = Controller;
            return c != null && c.PageData != null && c.PageData.Count > 0;
        }

        private static List<RuleBookPageInfo> Pages()
        {
            return Controller?.PageData ?? new List<RuleBookPageInfo>();
        }

        // ------------------------------------------------------------------
        // Reflection into the flipper
        // ------------------------------------------------------------------
        private static void ResolveReflection()
        {
            if (_reflectionResolved) return;
            _reflectionResolved = true;

            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            _flipperField = typeof(RuleBookController).GetField("flipper", any);
            if (_flipperField == null)
            {
                // Name changed — find it by type instead, which is the thing we
                // actually care about.
                foreach (var f in typeof(RuleBookController).GetFields(any))
                {
                    if (typeof(PageFlipper).IsAssignableFrom(f.FieldType)) { _flipperField = f; break; }
                }
            }

            _currentPageIndexField = typeof(PageFlipper).GetField("currentPageIndex", any);

            _log?.LogInfo(
                $"IKMA RULEBOOK: flipper field={_flipperField?.Name ?? "NOT FOUND"}, " +
                $"index field={_currentPageIndexField?.Name ?? "NOT FOUND"}.");
        }

        private static PageFlipper GetFlipper()
        {
            ResolveReflection();
            var c = Controller;
            if (c == null || _flipperField == null) return null;
            try { return _flipperField.GetValue(c) as PageFlipper; }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RULEBOOK: flipper read failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// The page the physical book is actually showing, or -1 if it cannot
        /// be read. Never guessed at.
        /// </summary>
        private static int ReadBookIndex(PageFlipper flipper)
        {
            if (flipper == null || _currentPageIndexField == null) return -1;
            try
            {
                object v = _currentPageIndexField.GetValue(flipper);
                return v is int i ? i : -1;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RULEBOOK: page index read failed: {e.Message}");
                return -1;
            }
        }

        // ------------------------------------------------------------------
        // Open / close
        // ------------------------------------------------------------------
        public static void Open()
        {
            var c = Controller;
            if (c == null)
            {
                Speech.Browse(Vocabulary.Rulebook.RulebookIsNotAvailable);
                return;
            }

            var pages = Pages();
            if (pages.Count == 0)
            {
                Speech.Browse(Vocabulary.RulebookNoPages);
                return;
            }

            try { c.SetShown(true, false); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RULEBOOK: SetShown(true) failed: {e.Message}");
            }

            Adopt(c, pages, render: true);
        }

        // ==================================================================
        // THE BOOK CAN BE OPENED WITHOUT IKMA. (Session 16.)
        //
        // Zamar: "Left clicking the rulebook out in the cabin softlocks the
        // game. It should instead act the same as pressing R."
        //
        // Same class of bug as the deck view's private _open bool and the camera
        // before it: IKMA kept its own record of a state the PLAYER can change
        // behind its back. Click the book with the mouse and the game shows it
        // while IKMA still believes it is shut, so the rulebook layer never
        // takes the keyboard, every key falls through to whatever is underneath,
        // and the book sits there open and unreachable.
        //
        // RuleBookController.Shown is the game's own answer, and StillOpen has
        // been asking it for closes since Session 10. This asks it for opens
        // too, which is the half that was missing.
        // ==================================================================
        internal static void TickOpenState(float deltaTime)
        {
            TickAdopt(deltaTime);

            var c = Controller;
            if (c == null) return;

            bool shown;
            try { shown = c.Shown; }
            catch { return; }

            if (!shown || _open) return;

            var pages = Pages();
            if (pages.Count == 0) return;

            // SETTLE BEFORE READING THE PAGE. Zamar, 0.7.63: clicking the book
            // "opened to the page Mighty Leap, but read Repulsive."
            //
            // The game opens on a coroutine, so the page index at the instant
            // Shown flips is the one the book was left on LAST time. Reading it
            // then is the read-state-back-after-an-async-call mistake, and this
            // project already has that rule written down. Wait for the index to
            // hold still, then say what is actually on the page.
            _adoptPending  = true;
            _adoptSettle   = 0f;
            _adoptLastSeen = -1;
            _log?.LogInfo("IKMA RULEBOOK: the book was opened by the game — settling before reading it.");
        }

        // How long the page index has to stop moving before a click-opened book
        // is read. Settle windows are free.
        private const float ADOPT_SETTLE_SECONDS = 0.35f;

        private static bool  _adoptPending;
        private static float _adoptSettle;
        private static int   _adoptLastSeen = -1;

        private static void TickAdopt(float deltaTime)
        {
            if (!_adoptPending) return;

            var c = Controller;
            if (c == null) { _adoptPending = false; return; }

            bool shown;
            try { shown = c.Shown; } catch { _adoptPending = false; return; }
            if (!shown) { _adoptPending = false; return; }

            var pages = Pages();
            if (pages.Count == 0) return;

            int index = ReadBookIndex(GetFlipper());
            if (index != _adoptLastSeen)
            {
                _adoptLastSeen = index;
                _adoptSettle   = 0f;
                return;
            }

            _adoptSettle += deltaTime;
            if (_adoptSettle < ADOPT_SETTLE_SECONDS) return;

            _adoptPending = false;
            _log?.LogInfo($"IKMA RULEBOOK: adopting a click-opened book at page {index + 1}.");

            // No render: the game opened it to a page of its own choosing and
            // has already drawn it. Forcing a render would flip the book out
            // from under the player.
            Adopt(c, pages, render: false, index: index, fromShelf: MapReader.Standing);
        }

        /// <summary>
        /// Take ownership of an open book — whether IKMA opened it or the player
        /// clicked it — and read the page it is actually on.
        /// </summary>
        private static void Adopt(RuleBookController c, List<RuleBookPageInfo> pages, bool render,
                                  int index = -1, bool fromShelf = false)
        {
            _open = true;
            _lastSpokenSection = null;   // opening always names the section
            _renderPending = false;
            _desyncReported = false;

            // Queued speech describes the battle, not the book. Hold it.
            CombatAnnouncer.ClearQueue();

            var flipper = GetFlipper();
            _index = index >= 0 ? index : ReadBookIndex(flipper);

            // SetShown enables the rig but renders nothing — that is why the
            // book came up open and blank. Force the game's own render for
            // wherever the book currently sits. OpenToAbilityPage is the entry
            // point the game itself uses and takes the pageId, which for
            // ability pages is the Ability enum name.
            //
            // Skipped when the GAME opened the book: it has already drawn the
            // page it chose, and re-rendering would flip it out from under the
            // player.
            if (_index < 0 || _index >= pages.Count) _index = 0;
            if (render) ForceRender(c, pages, _index);

            // Session 11: this used to re-read currentPageIndex here to
            // "confirm" what had rendered. Reading back is right after a
            // synchronous Flip and WRONG after OpenToAbilityPage, which flips
            // on a coroutine — the read returned the index left over from the
            // last time the book was open, and the narration announced that
            // stale page over freshly rendered content. We rendered a known
            // page; that page is what we say.
            _renderPending = false;

            // "Rulebook opened.", not "Rulebook." — Zamar, Session 16. The book
            // closing already says "Rulebook closed.", so the bare noun on the
            // way in did not match its own pair.
            //
            // "Opened Rulebook from shelf" is his wording for the other route:
            // walking into the book while standing in the cabin. It names WHERE
            // the book came from, which is the part a player who did not press a
            // key needs to hear.
            string lead = Vocabulary.Rulebook.OpenedRulebookFromOrRulebookOpened(fromShelf);

            Speech.Browse(
                Vocabulary.Rulebook.LeftAndRightArrows(lead, ComposePage(_index, withPosition: true)));
        }

        // Get page content onto the physical page. Tries the game's own
        // open-to-page entry first, falls back to driving the flipper.
        private static void ForceRender(RuleBookController c, List<RuleBookPageInfo> pages, int index)
        {
            var page = pages[index];

            if (page != null && page.abilityPage && !string.IsNullOrEmpty(page.pageId))
            {
                try
                {
                    // Session 11: this passed immediate:true, which skipped the
                    // additive page art — Mighty Leap's puzzle marking was absent
                    // until you flipped away and back, which ran the full render
                    // path. immediate:false takes that path the first time.
                    c.OpenToAbilityPage(page.pageId, null, false);
                    _log?.LogInfo($"IKMA RULEBOOK: rendered via OpenToAbilityPage('{page.pageId}').");
                    return;
                }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA RULEBOOK: OpenToAbilityPage failed: {e.Message}");
                }
            }

            var flipper = GetFlipper();
            if (flipper == null)
            {
                _log?.LogWarning("IKMA RULEBOOK: no flipper — the book will render blank.");
                return;
            }

            try
            {
                c.StartCoroutine(flipper.FlipToPage(index, RENDER_FLIP_DURATION));
                _log?.LogInfo($"IKMA RULEBOOK: rendered via FlipToPage({index}).");
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RULEBOOK: FlipToPage failed: {e.Message}");
            }
        }

        public static void Close(bool announce = true)
        {
            if (!_open) return;
            _open = false;
            _renderPending = false;

            var c = Controller;
            if (c != null)
            {
                try { c.SetShown(false, false); }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA RULEBOOK: SetShown(false) failed: {e.Message}");
                }
            }

            if (announce) Speech.BrowseNoFocus(Vocabulary.Rulebook.RulebookClosed);
        }

        /// <summary>
        /// The book can be closed by the game underneath us (scene change, the
        /// player clicking away). Checked each frame so the reader never keeps
        /// swallowing keys for a book that is no longer on screen.
        /// </summary>
        public static bool StillOpen()
        {
            if (!_open) return false;

            var c = Controller;
            if (c == null) { _open = false; return false; }

            if (!c.Shown)
            {
                _log?.LogInfo("IKMA RULEBOOK: book was closed by the game.");
                _open = false;
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Paging.
        //
        // Wraps, because the game wraps — PageFlipper.WrapIndex is what decides,
        // not us. Session 10's version stopped at both covers and announced
        // "End of the rulebook", which was a rule the mod invented and imposed
        // on a book that does not work that way.
        // ------------------------------------------------------------------
        public static void Turn(int direction)
        {
            var pages = Pages();
            if (pages.Count == 0)
            {
                Speech.Browse(Vocabulary.RulebookNoPages);
                return;
            }

            // The index advances here, not in the flipper. Wrapping is the same
            // arithmetic PageFlipper.WrapIndex performs, so this is a
            // reproduction of the game's rule rather than a guess at it — and
            // Tick() below checks the book agrees once the dust settles.
            // 0.4.8.007 - stops at the ends (Zamar, Session 58: every list).
            if (!ListStep.Step(ref _index, direction, pages.Count)) return;

            // Speak now. The paper catches up.
            Speech.Browse(ComposePage(_index));

            _renderPending = true;
            _renderDueAt   = Time.unscaledTime + RENDER_COALESCE_SECONDS;
        }

        /// <summary>
        /// Called every frame while the book is open. Owns the deferred render
        /// and the desync check. Nothing here speaks — a page turn is already
        /// narrated by the time this runs.
        /// </summary>
        public static void Tick()
        {
            if (!_open) return;

            var flipper = GetFlipper();
            if (flipper == null) return;

            if (_renderPending)
            {
                if (Time.unscaledTime < _renderDueAt) return;

                // A flip already in flight: let it land rather than stacking
                // coroutines on top of it.
                if (flipper.FlippingToPage)
                {
                    _renderDueAt = Time.unscaledTime + RENDER_COALESCE_SECONDS;
                    return;
                }

                _renderPending = false;

                int current = ReadBookIndex(flipper);
                if (current == _index) return;   // already showing it

                var c = Controller;
                if (c == null) return;

                var timer = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    c.StartCoroutine(flipper.FlipToPage(_index, RENDER_FLIP_DURATION));
                }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA RULEBOOK: deferred FlipToPage({_index}) failed: {e.Message}");
                    return;
                }
                timer.Stop();
                _lastFlipMs = timer.ElapsedMilliseconds;
                return;
            }

            // Nothing pending and nothing animating: the book should be showing
            // the page we last read aloud. If it is not, the narration and the
            // screen have parted company and that is exactly the failure this
            // whole rewrite exists to prevent — so it gets said out loud in the
            // log rather than quietly corrected, once per open.
            if (_desyncReported || flipper.FlippingToPage) return;

            int shown = ReadBookIndex(flipper);
            if (shown >= 0 && shown != _index)
            {
                _desyncReported = true;
                _log?.LogWarning(
                    $"IKMA RULEBOOK: DESYNC — narrating page {_index + 1}, book is showing {shown + 1}.");
            }
        }

        /// <summary>Re-read the current page without turning.</summary>
        public static void RepeatPage()
        {
            var pages = Pages();
            if (pages.Count == 0)
            {
                Speech.Browse(Vocabulary.RulebookNoPages);
                return;
            }

            // Deliberately does NOT re-read the flipper. With coalesced
            // rendering our index leads the book by up to 150ms, so reading it
            // back mid-turn would announce the page the book is still leaving.
            if (_index < 0 || _index >= pages.Count) _index = 0;
            Speech.Browse(ComposePage(_index, withPosition: true));
        }

        /// <summary>
        /// "Mighty Leap. <rulebook description>" for one sigil, or null if the
        /// game has no entry for it. (Session 13 — Shift+R.)
        ///
        /// Reads the same AbilityInfo the rulebook page itself renders from, so
        /// the player hears exactly what the book would show them. No page is
        /// opened and no game state moves.
        ///
        /// Ink-obscured pages are not a concern here: the three withheld pages
        /// are VARIABLE STAT pages, not ability pages, so nothing this returns
        /// is hidden from a sighted reader.
        /// </summary>
        public static string DescribeAbility(Ability ability) => DescribeAbility(ability, null);

        /// <summary>
        /// The rulebook entry for an ability, optionally with a clause attached
        /// to the sigil's NAME rather than to the end of the paragraph.
        /// (0.7.184.)
        ///
        /// <para><paramref name="afterName"/> is for a fact that belongs to the
        /// specific card the sigil was heard from — currently a mover's
        /// direction, giving "Sprinter, moving left. At the end of the owner's
        /// turn, ..." Pass it without punctuation; the comma and stop are added
        /// here so every caller gets the same shape.</para>
        ///
        /// <para>Zamar asked for this placement twice, once on the board read
        /// and once here: "The moving left part should have been right after
        /// the name Sprinter, before the full abilities explanation." A
        /// modifier goes next to the thing it modifies — at the end of a long
        /// explanation the listener has to work out which sigil it belonged
        /// to, and in a multi-sigil lookup there is more than one candidate.
        /// </para>
        ///
        /// <para>The rulebook page itself (R) never passes this. That page is
        /// generic text about a sigil with no card behind it, and a direction
        /// there would say something the page does not.</para>
        /// </summary>
        public static string DescribeAbility(Ability ability, string afterName)
        {
            try
            {
                var info = FindAbilityInfo(ability);
                if (info == null) return null;

                string name = string.IsNullOrEmpty(info.rulebookName)
                    ? Prettify(ability.ToString())
                    : Loc.Game(info.rulebookName);

                if (!string.IsNullOrEmpty(afterName))
                    name = Vocabulary.Rulebook.NameAfterName(name, afterName);

                string desc = null;
                try { desc = info.LocalizedRulebookDescription; } catch { }

                return string.IsNullOrEmpty(desc) ? $"{name}." : Vocabulary.Rulebook.NameAndDescription(name, Clean(desc));
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RULEBOOK: DescribeAbility failed: {e.Message}");
                return null;
            }
        }

        public static void SpeakHelp()
        {
            Speech.Browse(
                // Zamar, Session 16: the wrap-around clause went (it is a
                // property of the book, not a control), and the two closing
                // sentences became one.
                Vocabulary.Rulebook.RulebookLeftAndRight);
        }

        // ------------------------------------------------------------------
        // Page composition.
        //
        // Session 11, Zamar's call: the ability NAME leads and the page number
        // trails. Flipping through a 76-page book to find something, "Page 1 of
        // 76" first means every page opens with three seconds of the same
        // information, and the one word you are listening for arrives last.
        // The position still matters — it is how you know where you are and
        // that the book wrapped — so it stays, at the end, where it can be
        // talked over by the next arrow press without losing anything.
        // ------------------------------------------------------------------
        // ==================================================================
        // SECTIONS  (Session 11, Zamar's call)
        //
        // "Page 57 of 76" is a filing coordinate. The book is not one list —
        // it is Abilities, then Variable Stats, then Boons, then Items — and
        // the game prints exactly that on every page:
        //
        //     APPENDIX XII, SUBSECTION VI  - ABILITIES 1
        //     APPENDIX XII, SUBSECTION VII - VARIABLE STATS 3
        //     APPENDIX XII, SUBSECTION IX  - ITEMS 12
        //
        // So the section and the in-section number are read off the game's own
        // header rather than counted by us. That matters beyond tidiness: when
        // Zamar says "in the game this is Ability 28", the number he means is
        // the one printed on the page, and any figure we derived independently
        // would eventually disagree with it.
        //
        // RuleBookInfo.pageRanges / PageRangeType is the structural version of
        // this and is the upgrade if header parsing ever proves brittle. It was
        // not dumped, and parsing text the game itself displays is grounded
        // enough to ship.
        // ==================================================================

        private static string[] _sectionKeyByPage;
        private static string[] _sectionDisplayByPage;
        private static int[]    _ordinalByPage;
        private static Dictionary<string, int> _sectionTotals;
        private static int _sectionCacheBuiltFor = -1;

        // The section last spoken aloud. Named only when it changes, so a run
        // of ability pages does not repeat the word "Abilities" forty times.
        private static string _lastSpokenSection = null;

        // Plural section names as printed -> the singular used for counting a
        // single page. Anything unlisted falls through to the printed name.
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _sectionSingulars => Loc.PerLanguage(ref _sectionSingularsCache, ref _sectionSingularsLanguage, Build_sectionSingulars);
        private static Dictionary<string, string> Build_sectionSingulars() =>
            new Dictionary<string, string>
        {
            { "ABILITIES",      Vocabulary.Rulebook.Ability },
            { "VARIABLE STATS", Vocabulary.Rulebook.VariableStat },
            { "BOONS",          Vocabulary.Rulebook.Boon },
            { "ITEMS",          Vocabulary.Rulebook.Item },
            { "RULES",          Vocabulary.Rulebook.Rule },
        };
        private static Dictionary<string, string> _sectionSingularsCache;
        private static string _sectionSingularsLanguage;

        private static void BuildSectionIndex(List<RuleBookPageInfo> pages)
        {
            if (_sectionCacheBuiltFor == pages.Count && _sectionKeyByPage != null) return;

            _sectionKeyByPage     = new string[pages.Count];
            _sectionDisplayByPage = new string[pages.Count];
            _ordinalByPage        = new int[pages.Count];
            _sectionTotals        = new Dictionary<string, int>();

            var running = new Dictionary<string, int>();

            for (int i = 0; i < pages.Count; i++)
            {
                string key = null, display = null;
                int ordinal = 0;

                string header = pages[i]?.headerText;
                if (!string.IsNullOrEmpty(header))
                {
                    // Everything after the final separator is "<SECTION> <n>".
                    int cut = header.LastIndexOf(" - ", System.StringComparison.Ordinal);
                    string tail = cut >= 0 ? header.Substring(cut + 3) : header;
                    tail = tail.Trim();

                    var m = System.Text.RegularExpressions.Regex.Match(tail, @"^(.*?)\s*(\d+)$");
                    if (m.Success)
                    {
                        key = m.Groups[1].Value.Trim().ToUpperInvariant();
                        int.TryParse(m.Groups[2].Value, out ordinal);
                    }
                    else if (tail.Length > 0)
                    {
                        key = tail.ToUpperInvariant();
                    }
                }

                if (key != null)
                {
                    int seen;
                    running.TryGetValue(key, out seen);
                    running[key] = seen + 1;
                    if (ordinal <= 0) ordinal = seen + 1;   // header had no number

                    int total;
                    _sectionTotals.TryGetValue(key, out total);
                    _sectionTotals[key] = total + 1;

                    display = TitleCase(key);
                }

                _sectionKeyByPage[i]     = key;
                _sectionDisplayByPage[i] = display;
                _ordinalByPage[i]        = ordinal;
            }

            _sectionCacheBuiltFor = pages.Count;

            var summary = new List<string>();
            foreach (var kv in _sectionTotals) summary.Add($"{kv.Key}={kv.Value}");
            _log?.LogInfo($"IKMA RULEBOOK: sections — {string.Join(", ", summary)}");
        }

        // "VARIABLE STATS" -> "Variable stats". The book prints in full caps;
        // read aloud, that is just a word, but it looks like shouting in the
        // log and some synthesisers spell out all-caps runs letter by letter.
        private static string TitleCase(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string lower = raw.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }

        /// <summary>
        /// One page, read aloud.
        ///
        /// <paramref name="withPosition"/> is M only. Session 16, Zamar's
        /// general rule: "The number of options available (and which one you're
        /// currently on) should only be read by pressing M." Turning pages now
        /// answers with the page and nothing else — on a 76-page book that is
        /// three or four words off every single flip.
        /// </summary>
        private static string ComposePage(int index, bool withPosition = false)
        {
            var pages = Pages();
            if (index < 0 || index >= pages.Count) return Vocabulary.Rulebook.PageUnavailable;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            BuildSectionIndex(pages);

            var page = pages[index];
            if (page == null) return Vocabulary.Rulebook.BlankPageOf(index + 1, pages.Count);

            _composeName = null;

            string body;
            string source;

            // Session 14, second pass. The clear that used to be here is GONE.
            //
            // The first fix over-corrected. Wiping the memory on any page that
            // named no sigil meant flipping one page past Made Of Stone answered
            // Shift+R with "no recent sigil" — the lookup went dead exactly when
            // he wanted it. Zamar's rule, and it is the simpler one: **Shift+R
            // reads the last sigil ANNOUNCED, wherever it came from, and holds it
            // until a new sigil replaces it.** A memory that expires is worse
            // than one that lingers, because the player cannot tell the
            // difference between "nothing to say" and "you waited too long".
            //
            // The original stale-answer bug is fixed by the other half of this
            // work rather than by forgetting: every page now records the sigils
            // it NAMES, so any page carrying one refreshes the memory as it is
            // read. ForgetSpokenAbilities stays on CardReader, unused here, for
            // a context that genuinely needs a reset.

            if (page.abilityPage)
            {
                var info = FindAbilityInfo(page.ability);
                if (info != null)
                {
                    string name = string.IsNullOrEmpty(info.rulebookName)
                        ? page.ability.ToString()
                        : Loc.Game(info.rulebookName);

                    string desc = null;
                    try { desc = info.LocalizedRulebookDescription; }
                    catch (System.Exception e)
                    {
                        _log?.LogWarning($"IKMA RULEBOOK: description read failed: {e.Message}");
                    }

                    body         = string.IsNullOrEmpty(desc) ? $"{name}." : Vocabulary.Rulebook.Name(name, Clean(desc));
                    source       = "AbilityInfo";
                    _composeName = name;

                    // Session 14: this page IS an ability, so Shift+R should
                    // explain THIS one. GetAbilityName is the single choke point
                    // that records what a line named, and the rulebook never went
                    // through it — it reads AbilityInfo directly — so the memory
                    // kept whatever card was browsed last. Cheap: the name is
                    // cached, and the return value is deliberately unused.
                    CardReader.GetAbilityName(page.ability);
                }
                else
                {
                    body   = Vocabulary.Rulebook.DescriptionUnavailable(Prettify(page.ability.ToString()));
                    source = "ability enum fallback";
                }
            }
            else
            {
                // Boon, item and variable-stat pages.
                //
                // Session 11: these read as bare prettified pageIds, which was
                // never going to converge — 'GooBottle' prints as "Failure" and
                // 'BirdLegFan' as "Harpie's Birdleg Fan". pageId is an internal
                // key, not a display name. dump_rulebook_content.txt found a
                // public static lookup for each of the three, so all of them now
                // read the same data the page itself renders from.
                body = null;
                _composeSource = null;

                string sk = _sectionKeyByPage[index];

                if (sk == "BOONS")               body = ComposeBoon(page);
                else if (sk == "VARIABLE STATS") body = ComposeStatIcon(page);
                else if (sk == "ITEMS")          body = ComposeItem(page);

                // Section unknown, or the expected lookup came back empty. Try
                // the others before giving up — the section headings are the
                // game's, but a page that lands somewhere unexpected should
                // still get read properly.
                if (body == null) body = ComposeBoon(page);
                if (body == null) body = ComposeStatIcon(page);
                if (body == null) body = ComposeItem(page);

                if (body != null)
                {
                    source = _composeSource;
                }
                else if (!string.IsNullOrEmpty(page.pageId))
                {
                    // Named but not described. Honest, and better than nothing.
                    body   = Prettify(page.pageId) + ".";
                    source = "pageId fallback";
                }
                else
                {
                    string header = Clean(page.headerText);
                    if (!string.IsNullOrEmpty(header))
                    {
                        body   = header.EndsWith(".") ? header : header + ".";
                        source = "headerText fallback";
                    }
                    else
                    {
                        body   = Vocabulary.Rulebook.BlankPage;
                        source = "none";
                    }
                }
            }

            // ----- position: section-relative where the book gives us one -----
            string sectionKey = _sectionKeyByPage[index];
            string position;

            if (sectionKey != null && _sectionTotals.ContainsKey(sectionKey))
            {
                string singular = _sectionSingulars.TryGetValue(sectionKey, out string sg)
                    ? sg
                    : TitleCase(sectionKey);
                position = Vocabulary.Rulebook.Of(singular, _ordinalByPage[index], _sectionTotals[sectionKey]);
            }
            else
            {
                position = Vocabulary.Rulebook.PageOf(index + 1, pages.Count);
            }

            // ----- section name, but only when it changes -----
            string sectionLead = "";
            if (sectionKey != null && sectionKey != _lastSpokenSection)
            {
                string display = _sectionDisplayByPage[index] ?? TitleCase(sectionKey);
                sectionLead = display + ". ";
                _lastSpokenSection = sectionKey;
            }

            // ----- ink: withhold what the page withholds from everyone -----
            if (HasInkOverlay(page))
            {
                string replacement;
                if (!string.IsNullOrEmpty(page.pageId)
                    && _obscuredPages.TryGetValue(page.pageId, out replacement))
                {
                    body = replacement;
                }
                else
                {
                    string shownName = !string.IsNullOrEmpty(_composeName)
                        ? _composeName
                        : Prettify(page.pageId);
                    body = Vocabulary.Rulebook.DescriptionOnThisPage(shownName);
                }
                source += " + ink";
            }

            // ----- puzzle markings, FIRST on the page -----
            //
            // Session 11: these used to trail the position, on the theory that
            // anything after the description reads as a find. In practice the
            // page number sat between the description and the code, and the one
            // piece of information the player actually came for was the last
            // thing said and the easiest to talk over with the next arrow press.
            // It moved to before the number.
            //
            // Session 14, Zamar: not far enough. "The combination should be the
            // first thing read on that page." It now leads the whole read —
            // ahead of the section name and ahead of the ability's own name.
            //
            // Which is the project's narration philosophy applied exactly: put
            // the part being scanned for FIRST, and let the ear drop the rest
            // once it has what it came for. Nobody arrows to the Mighty Leap
            // page to be reminded what Mighty Leap does; they go there for the
            // code, and every word in front of it is a word they have to sit
            // through or risk stomping. The description still follows, so
            // nothing is lost by moving it.
            string note = "";
            if (!string.IsNullOrEmpty(page.pageId)
                && _puzzleNotes.TryGetValue(page.pageId, out string recorded))
            {
                note = recorded + " ";
            }

            // ----- one log line, carrying the timing and the art inventory -----
            //
            // additivePrefabs is the extra art laid over a page — and the puzzle
            // markings ARE that art. The field is public, so while their content
            // is unreachable, their NAMES are not. If the prefab behind Mighty
            // Leap's "273" is called anything descriptive, the codes stop being a
            // manual transcription job. Logged on the pages that have them.
            string art = "";
            if (page.additivePrefabs != null && page.additivePrefabs.Count > 0)
            {
                var names = new List<string>();
                foreach (var go in page.additivePrefabs)
                    names.Add(go != null ? go.name : "null");
                art = $" ART[{string.Join(", ", names)}]";
            }

            sw.Stop();
            _log?.LogInfo(
                $"IKMA RULEBOOK: page {index + 1}/{pages.Count} id='{page.pageId}' via {source} " +
                $"(flip {_lastFlipMs}ms, compose {sw.ElapsedMilliseconds}ms){art}");

            // Session 14: every sigil this page NAMES is now available to
            // Shift+R, not just the one the page is about. Runs on the final
            // body, so it covers an ability page's own description, an item
            // that defines a creature by its sigils, and the surviving
            // fragments of an ink-destroyed page alike.
            CardReader.NoteAbilitiesMentionedIn(body);

            // Position last, and only when asked for. The section name still
            // leads on a section CHANGE, because that is not a count — it is
            // what part of the book you have crossed into.
            return withPosition
                ? $"{note}{sectionLead}{body} {position}"
                : $"{note}{sectionLead}{body}";
        }

        // ==================================================================
        // BOON / VARIABLE STAT / ITEM CONTENT  (Session 11)
        //
        // Confirmed PUBLIC STATIC in dump_rulebook_content.txt:
        //
        //   StatIconInfo.GetIconInfo(SpecialStatIcon) -> StatIconInfo
        //       PUBLIC rulebookName, rulebookDescription
        //   BoonsUtil.GetData(BoonData.Type)          -> BoonData
        //       PUBLIC displayedName, description, rulebookEntry
        //   ItemsUtil.GetConsumableByName(String)     -> ConsumableItemData
        //       PUBLIC rulebookName, rulebookDescription
        //
        // Each returns null rather than throwing when the page is not its kind,
        // so the caller can try all three without knowing the answer in advance.
        // ==================================================================

        private static string _composeSource;
        private static string _composeName;

        // BoonsUtil.GetData is called by reflection rather than directly. The
        // first dump listed RuleBookPageInfo.boon as "Type" and the second
        // showed BoonData carries a NESTED enum also called Type — they agree,
        // but a direct call commits the build to that reading, and being wrong
        // costs a compile error rather than a graceful null. Six boon pages do
        // not need the microsecond.
        private static MethodInfo _boonsUtilGetData;
        private static bool _boonsUtilResolved = false;

        private static string ComposeBoon(RuleBookPageInfo page)
        {
            if (page == null) return null;

            object boonValue = page.boon;
            if (boonValue == null) return null;
            if (boonValue.ToString() == "None") return null;

            if (!_boonsUtilResolved)
            {
                _boonsUtilResolved = true;
                _boonsUtilGetData = typeof(BoonsUtil).GetMethod(
                    "GetData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _log?.LogInfo($"IKMA RULEBOOK: BoonsUtil.GetData {( _boonsUtilGetData != null ? "resolved" : "NOT FOUND")}.");
            }
            if (_boonsUtilGetData == null) return null;

            BoonData data = null;
            try { data = _boonsUtilGetData.Invoke(null, new object[] { boonValue }) as BoonData; }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RULEBOOK: BoonsUtil.GetData failed: {e.Message}");
                return null;
            }
            if (data == null) return null;

            string name = !string.IsNullOrEmpty(data.displayedName)
                ? data.displayedName
                : Prettify(page.pageId);

            // rulebookEntry is the book's own wording; description is the
            // shorter in-run text. Prefer the book's, since this IS the book.
            string desc = Clean(!string.IsNullOrEmpty(data.rulebookEntry)
                ? data.rulebookEntry
                : data.description);

            _composeSource = "BoonData";
            _composeName   = name;
            return string.IsNullOrEmpty(desc) ? $"{name}." : Vocabulary.Rulebook.Word(name, desc);
        }

        private static string ComposeStatIcon(RuleBookPageInfo page)
        {
            if (page == null || string.IsNullOrEmpty(page.pageId)) return null;

            StatIconInfo info = null;

            // pageId on these pages is the SpecialStatIcon member name — 'Ants',
            // 'Mirror', 'Bell', 'CardsInHand'. Go through the enum rather than
            // the string overload, whose matching rule was not dumped.
            try
            {
                foreach (var candidate in StatIconInfo.AllIconInfo)
                {
                    if (candidate == null) continue;
                    if (candidate.iconType.ToString() == page.pageId) { info = candidate; break; }
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RULEBOOK: AllIconInfo scan failed: {e.Message}");
            }

            if (info == null)
            {
                try { info = StatIconInfo.GetIconInfo(page.pageId); }
                catch { }
            }

            if (info == null) return null;

            string name = !string.IsNullOrEmpty(info.rulebookName)
                ? Loc.Game(info.rulebookName)
                : Prettify(page.pageId);
            string desc = Clean(Loc.Game(info.rulebookDescription));

            _composeSource = "StatIconInfo";
            _composeName   = name;
            return string.IsNullOrEmpty(desc) ? $"{name}." : Vocabulary.Rulebook.ComposeStatIcon(name, desc);
        }

        private static string ComposeItem(RuleBookPageInfo page)
        {
            if (page == null || string.IsNullOrEmpty(page.pageId)) return null;

            ConsumableItemData data = null;
            try { data = ItemsUtil.GetConsumableByName(page.pageId); }
            catch { }

            // GetConsumableByName's matching rule was not dumped, and pageId is
            // demonstrably not the display name — 'GooBottle' is titled
            // "Failure" in the book. If the lookup misses, scan the loaded data
            // by asset name and prefab id, both of which pageId plausibly is.
            if (data == null)
            {
                try
                {
                    var all = ScriptableObjectLoader<ConsumableItemData>.AllData;
                    if (all != null)
                    {
                        foreach (var c in all)
                        {
                            if (c == null) continue;
                            if (string.Equals(c.name, page.pageId, System.StringComparison.OrdinalIgnoreCase)
                             || string.Equals(c.PrefabId, page.pageId, System.StringComparison.OrdinalIgnoreCase))
                            { data = c; break; }
                        }
                    }
                }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA RULEBOOK: consumable scan failed: {e.Message}");
                }
            }

            if (data == null) return null;

            string name = !string.IsNullOrEmpty(data.rulebookName)
                ? Loc.Game(data.rulebookName)
                : Prettify(page.pageId);

            // The BOOK gets the game's own wording. HotkeyManager's tightened
            // rewrites stay where they belong — on the I key, mid-battle, where
            // the player wants the mechanical fact and not Leshy's voice. Both
            // exist on purpose; this is the reference text.
            string desc = Clean(Loc.Game(data.rulebookDescription));

            // Every item page prints "TO THE USER:" ahead of its description —
            // the page adds it, so it is not in rulebookDescription. It is not
            // decoration: it marks whose creatures the effect applies to, and
            // the Harpie's Birdleg Fan reads as the wrong card without it.
            if (!string.IsNullOrEmpty(desc)
                && desc.IndexOf("To the user", System.StringComparison.OrdinalIgnoreCase) != 0)
            {
                desc = Vocabulary.Rulebook.ToTheUser(desc);
            }

            _composeSource = "ConsumableItemData";
            _composeName   = name;
            return string.IsNullOrEmpty(desc) ? $"{name}." : Vocabulary.Rulebook.ComposeItem(name, desc);
        }

        // Session 11: this scanned all 106 AbilityInfo entries on every page
        // turn. Cached now — the mapping never changes within a run, and page
        // turning is the one place in the mod where the player is hammering a
        // key and waiting on each result.
        private static readonly Dictionary<Ability, AbilityInfo> _abilityInfoCache
            = new Dictionary<Ability, AbilityInfo>();

        private static AbilityInfo FindAbilityInfo(Ability ability)
        {
            if (_abilityInfoCache.TryGetValue(ability, out AbilityInfo cached))
                return cached;

            var allData = ScriptableObjectLoader<AbilityInfo>.AllData;
            if (allData == null) return null;

            var found = allData.Find(x => x != null && x.ability == ability);
            if (found != null) _abilityInfoCache[ability] = found;
            return found;
        }

        // Tokens already reported, so an unknown one is logged once rather than
        // on every page turn.
        private static readonly HashSet<string> _seenTokens = new HashSet<string>();

        // Tokens that stand in for the card carrying the sigil. ONLY these are
        // substituted.
        //
        // Wording: the book itself prints "A CARD BEARING THIS SIGIL" (confirmed
        // from the Mighty Leap page), so the substitution matches the printed
        // text rather than paraphrasing it.
        //
        // Session 11: the first version substituted every unknown token, on the
        // reasoning that a missing subject was worse than a wrong one. Then the
        // Frozen Opossum Bottle read "A Frozen Opossum is created in your hand.
        // a card with this sigil" — a token in an ITEM description, which has no
        // sigil and no creature, turned into a dangling phrase. Guessing at an
        // unknown token's meaning is the same failure as guessing at a refusal's
        // cause. Unknown tokens are dropped and logged, and the log is how the
        // list below grows.
        private static readonly HashSet<string> _creatureTokens
            = new HashSet<string> { "creature" };

        // Card-definition codes.
        //
        // Session 11: item pages stopped mid-sentence. The Frozen Opossum
        // Bottle read "A Frozen Opossum is created in your hand." and dropped
        // "A Frozen Opossum is defined as: 0 Power, 5 Health, Frozen Away."
        // Same for the Black Goat and Squirrel bottles and the boulder.
        //
        // The description carries a CODE where that sentence belongs, and
        // RuleBookPage.ParseCardDefinition expands it. Ability pages never
        // showed the problem because AbilityInfo.LocalizedRulebookDescription
        // runs the expansion itself; ConsumableItemData.rulebookDescription is
        // a raw field and does not. Clean() was then eating the unexpanded code
        // as an unknown bracket token, which is why the line trailed off into a
        // stray fragment before token substitution was tightened. One cause,
        // two symptoms.
        //
        // Expansion runs BEFORE token stripping, or Clean deletes the code it
        // was meant to expand. Applied to every page kind: on already-expanded
        // text it finds no code and changes nothing.
        private static string ExpandCardDefinitions(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            // 0.4.8.006 - the game's expansion speaks the game's language. When
            // that is not IKMA's, expand in IKMA's own words instead. See
            // Loc.GameTextMatchesSpeech.
            if (!Loc.GameTextMatchesSpeech()) return ExpandDefinitionsInSpeech(raw);
            try { return RuleBookPage.ParseCardDefinition(raw) ?? raw; }
            catch (System.Exception e)
            {
                if (_seenTokens.Add("__parseCardDefinition"))
                    _log?.LogWarning($"IKMA RULEBOOK: ParseCardDefinition failed: {e.Message}");
                return raw;
            }
        }

        // internal, not private, since 0.7.324 — the Bone Lord's boon card
        // reads the same BoonData text and a second stripper would be a second
        // thing to keep in step.
        private static string ExpandDefinitionsInSpeech(string text)
        {
            try
            {
                for (int guard = 0; guard < 8; guard++)
                {
                    int i = text.IndexOf("[define:");
                    if (i < 0) break;
                    int j = text.IndexOf(']', i);
                    if (j < 0) break;
                    string token = text.Substring(i, j - i + 1);
                    string name = token.Substring(8, token.Length - 9);
                    CardInfo ci = CardLoader.GetCardByName(name);
                    string sigils = "";
                    foreach (var a in ci.DefaultAbilities)
                        sigils += ", " + Loc.Game(AbilitiesUtil.GetInfo(a).rulebookName);
                    text = text.Replace(token,
                        Vocabulary.Rulebook.CardIsDefinedAs(Loc.Game(ci.DisplayedNameEnglish), ci.Attack, ci.Health, sigils));
                }
            }
            catch (System.Exception e)
            {
                if (_seenTokens.Add("__expandInSpeech"))
                    _log?.LogWarning($"IKMA RULEBOOK: definition expand failed: {e.Message}");
            }
            return text;
        }

        internal static string Clean(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            raw = ExpandCardDefinitions(raw);

            string s = System.Text.RegularExpressions.Regex.Replace(
                raw, @"\[([^\]]*)\]", m =>
                {
                    string token = m.Groups[1].Value;

                    // Formatting codes: [c:bR] colour, [w:1] wait.
                    if (token.StartsWith("c:") || token.StartsWith("w:")) return "";

                    if (_creatureTokens.Contains(token.ToLowerInvariant()))
                        return "a card bearing this sigil";

                    if (_seenTokens.Add(token))
                        _log?.LogInfo($"IKMA RULEBOOK: unknown text token '[{token}]' dropped.");
                    return "";
                });

            s = System.Text.RegularExpressions.Regex.Replace(s, @"<[^>]*>", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

            // The substitution can land at the start of a sentence, where the
            // token used to supply the capital.
            if (s.Length > 0 && char.IsLower(s[0]))
                s = char.ToUpper(s[0]) + s.Substring(1);

            return s;
        }

        // "Ability_SharpQuills" / "sharpQuills" -> "Sharp Quills".
        private static string Prettify(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw.Replace('_', ' ');

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsUpper(c) && sb.Length > 0 && s[i - 1] != ' ') sb.Append(' ');
                sb.Append(c);
            }
            return System.Text.RegularExpressions.Regex
                .Replace(sb.ToString(), @"\s+", " ").Trim();
        }
    }
}
