// DeckViewReader.cs
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// Your deck, laid out. (Session 15, Zamar's request: "if I scroll wheel up
    /// I can also see my deck laid out in front of me and mouse over each card.
    /// This screen should open by pressing the D key.")
    ///
    /// It is reachable with a scroll wheel today, which makes it a screen a
    /// sighted player uses freely and a blind player cannot open at all — and
    /// knowing what is left in your deck is most of how Act 1 is played.
    ///
    /// EVERY MEMBER CONFIRMED IN dump_deck_view.txt BEFORE ANY OF THIS:
    ///   DeckReviewSequencer                      base Singleton`1
    ///   DeckReviewSequencer.SetDeckReviewShown(Boolean)          PUBLIC
    ///   DeckReviewSequencer.cardArray : SelectableCardArray      NONPUBLIC
    ///   DeckReviewSequencer.ShowCurrencyBowl() -> Boolean        NONPUBLIC
    ///   SelectableCardArray.displayedCards : List`1              NONPUBLIC
    ///   Card.Info : CardInfo                                     PUBLIC
    ///   RunState.DeckList : List`1                        PUBLIC STATIC
    ///   RunState.Run.currency : Int32                            PUBLIC
    ///
    /// `Card.Info` is the point worth naming. CardChoiceReader reads
    /// ChoiceInfo.CardInfo, which is the card-CHOICE wrapper; a deck review card
    /// is not a choice and carries its info on the base Card. Guessing between
    /// those two is how Flipped shipped backwards for six sessions, so it was
    /// dumped instead.
    /// </summary>
    public static class DeckViewReader
    {
        private static ManualLogSource _log;

        private static FieldInfo _cardArrayField;
        private static FieldInfo _displayedCardsField;
        private static MethodInfo _showCurrencyBowlMethod;

        private static int _index = -1;
        private static SelectableCard _hovered;
        private static float _settleFor;
        private static bool _announced;

        /// How long nothing may be speaking before an unasked-for deck view
        /// reads itself. Long enough to cover the gap between the map settling
        /// and the run intro's first line.
        private const float DIALOGUE_GRACE_SECONDS = 2.0f;
        private static float _quietFor;

        // THE COUNT HAS TO HOLD STILL BEFORE ANYTHING IS SPOKEN. (Session 16.)
        //
        // Zamar's 0.7.53 log, two opens seconds apart in the same run:
        //   "Your deck. 6 cards. Card 1 of 6. Cuckoo..."
        //   "Your deck. 3 cards. Card 1 of 3. Mantis God..."
        // Neither was his deck. The old test started a fixed clock at the FIRST
        // card to appear and spoke when it ran out, so what it counted was
        // however many had spawned by then — SpawnAndPlaceCards places them one
        // at a time, and a real run's deck is still arriving at 0.5s.
        //
        // A three-card screen spawns fast enough to hide this, which is why the
        // card choice screen carried the same bug for two sessions without
        // anyone hearing it. A DECK is the first thing big enough to expose it.
        //
        // Announcing a wrong count is the confidently-wrong failure this project
        // keeps coming back to: "6 cards" is not hedged, and a player has no way
        // to tell it from the truth.
        private static int _stableCount = -1;
        private static float _lastCountAt;

        // Set by MapReader.ShowDeck, which says "Your deck." the instant the key
        // is pressed so the climb is not a second of silence. Without this the
        // read that lands behind it says "Your deck." again — Zamar heard it
        // twice. Arriving by SCROLL WHEEL never sets it, and that path still
        // needs the screen named.
        private static bool _openingSpoken;

        internal static void NoteOpeningSpoken() => _openingSpoken = true;

        // The cards are spawned and tweened into place inside a coroutine, so
        // nothing exists to count on the frame the key is pressed. Settle
        // windows are free.
        private const float SETTLE_SECONDS = 0.5f;

        public static void Init(ManualLogSource log) => _log = log;

        /// <summary>
        /// True while the deck view owns the keyboard — and it is the GAME'S
        /// answer, not IKMA's. (Rewritten Session 16.)
        ///
        /// This used to return a private _open bool set by Open() and cleared by
        /// Close(). The scroll wheel leaves this screen without telling IKMA, so
        /// _open stayed true, this layer went on swallowing every key above the
        /// map, and Zamar's report was "it seems like all our navigation
        /// controls break." A reader that keeps its own copy of a state the
        /// player can change behind its back will always eventually strand
        /// itself; asking the camera cannot.
        ///
        /// _open is gone. MapReader.InDeckView reads ViewManager.CurrentView.
        /// </summary>
        public static bool Active => MapReader.InDeckView;

        private static DeckReviewSequencer Sequencer()
        {
            try { return Singleton<DeckReviewSequencer>.Instance; }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        // D, or the Up arrow at the map. Bring the deck up.
        //
        // THIS NO LONGER CALLS SetDeckReviewShown. That laid the deck out
        // without moving the camera, which is not what the scroll wheel does and
        // is not a state the game ever enters by itself — Zamar: "up arrow broke
        // the gamestate again". The wheel moves the camera to MapDeckReview and
        // the sequencer reacts to the view change, so IKMA does the same thing
        // and lets the game run its own path.
        // ------------------------------------------------------------------
        // WHO OPENED IT. (0.7.243.) A deck view the PLAYER asked for answers
        // immediately; one the game put up on its own can afford to wait for
        // Leshy to finish. See NeedsAnnouncement.
        private static bool _playerAsked;

        public static void Open()
        {
            _playerAsked = true;
            MapReader.ShowDeck();
        }

        /// <summary>
        /// The player asked for the deck by some other route. (0.7.257.) The
        /// quiet-time gate exists to keep the deck line off Leshy's opening
        /// speech; a keypress is the player asking, and that gate does not apply
        /// to an answer.
        /// </summary>
        internal static void NotePlayerAsked() => _playerAsked = true;

        /// <summary>Backspace, or Down at the deck view. Back to the map.</summary>
        public static void Close()
        {
            if (!Active) return;
            ReleaseHover();

            // Back to the rung it was opened from. Falls back to the map only
            // when the camera never told us — which should not happen, and is
            // logged if it does.
            if (_cameFrom == null)
            {
                _log?.LogWarning("IKMA DECK: closing with no recorded origin; falling back to the map.");
                MapReader.ReturnToMap(announce: false);
            }
            else
            {
                MapReader.ReturnTo(_cameFrom.Value, PlaceBelow, announce: false);
            }

            // "Deck returned." — Zamar's word, Session 16. Rather than naming
            // the destination: the player asked to leave the deck, and what is
            // underneath announces itself.
            Speech.Browse(Vocabulary.DeckView.DeckReturned);
        }

        // ------------------------------------------------------------------
        // Told by MapReader when the camera arrives at or leaves the deck view,
        // BY ANY ROUTE — the Up arrow, D, the scroll wheel, or the game moving
        // the camera itself. This is what makes the screen impossible to strand:
        // there is no path in or out that IKMA does not see.
        // ------------------------------------------------------------------
        // WHERE THIS SCREEN WAS OPENED FROM. (Session 16.)
        //
        // The deck view sits one rung above whatever the player was looking at,
        // and that is NOT always the map: from the card choice screen it sits
        // above Choices. 0.7.60 always aimed Backspace at MapDefault, so from
        // the card choice it pressed past Choices, found nothing, and told him
        // he could not enter the map.
        //
        // Everything this screen SAYS about leaving follows the same fact, so
        // the help and the controls line name the right place too — Zamar: "The
        // Your Deck H key and Your Deck screen should update based on what is
        // below it, or what screen it is being entered from."
        private static View? _cameFrom;

        // 0.7.359 — and a node screen's own name when the deck was opened from
        // one: at the Trader, Backspace goes back to the Trader, not the map.
        private static string PlaceBelow =>
            Vocabulary.DeckView.PlaceBelow(CardChoiceReader.Active,
                NodeScreenReader.Active ? NodeScreenReader.ScreenName : null);

        internal static void OnViewEntered(View? cameFrom)
        {
            // 0.7.359 — A NODE SCREEN WITH A DECK VIEW KNOWS WHERE IT SITS. At
            // the Trader the deck is one click above TradingTopDown whichever
            // way the player came up. Shift+Up records that; the scroll wheel
            // never passes through ShowDeck and leaves a stale origin behind —
            // his 0.7.358 log said "opened from MapDefault" at the Trader.
            var nodeOrigin = NodeScreenReader.DeckViewOrigin;
            if (nodeOrigin != null && cameFrom != nodeOrigin)
            {
                cameFrom = nodeOrigin;
                _log?.LogInfo($"IKMA DECK: origin taken from the node screen — {cameFrom}.");
            }

            // MapArial is NEVER a destination. It is the overhead rung the climb
            // passes through on the way up, and Zamar asked never to be left on
            // it — 0.7.61 recorded it as the origin and Backspace put him right
            // back there. If the origin is missing or is that rung, fall back to
            // what is actually underneath this screen by context.
            if (cameFrom == null || cameFrom == View.MapArial || cameFrom == View.MapDeckReview)
            {
                cameFrom = CardChoiceReader.Active ? View.Choices : View.MapDefault;
                _log?.LogInfo($"IKMA DECK: origin resolved by context to {cameFrom}.");
            }

            _cameFrom = cameFrom;
            _log?.LogInfo($"IKMA DECK: opened from {cameFrom?.ToString() ?? "unknown"} ({PlaceBelow})" +
                          (_playerAsked ? ", by the player." : ", by the game."));
            _index       = -1;
            _hovered     = null;
            _settleFor   = 0f;
            _quietFor    = 0f;
            _announced   = false;
            _stableCount = -1;

            // 0.7.275 — I cycles items on this screen, so the cursor starts at
            // slot 1 every time the deck opens rather than wherever the last
            // visit left it. A remembered position in a screen the player has
            // left is stale state, and this project has paid for that twice.
            BoardReader.ResetItemCycle();

            _log?.LogInfo("IKMA DECK: deck view entered.");
        }

        internal static void OnViewLeft()
        {
            _openingSpoken = false;
            _playerAsked   = false;
            _cameFrom      = null;
            ReleaseHover();
            _index       = -1;
            _hovered     = null;
            _announced   = false;
            _stableCount = -1;
            _log?.LogInfo("IKMA DECK: deck view left.");
        }

        internal static void Reset()
        {
            _index       = -1;
            _hovered     = null;
            _announced   = false;
            _stableCount = -1;
        }

        // ------------------------------------------------------------------
        // The cards actually laid out on the table. Not RunState.DeckList —
        // that is the deck as data, and what the player is looking at is what
        // the array has spawned. If the two ever disagree the array is what a
        // sighted player can see, and that is the one to read.
        // ------------------------------------------------------------------
        private static List<SelectableCard> Cards()
        {
            var live = new List<SelectableCard>();
            var seq = Sequencer();
            if (seq == null) return live;

            try
            {
                if (_cardArrayField == null)
                    _cardArrayField = typeof(DeckReviewSequencer).GetField(
                        "cardArray", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                var array = _cardArrayField?.GetValue(seq);
                if (array == null) return live;

                if (_displayedCardsField == null)
                    _displayedCardsField = typeof(SelectableCardArray).GetField(
                        "displayedCards", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                var raw = _displayedCardsField?.GetValue(array) as List<SelectableCard>;
                if (raw == null) return live;

                foreach (var c in raw) if (c != null) live.Add(c);
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA DECK: displayedCards read failed: {e.Message}");
            }

            return live;
        }

        public static bool NeedsAnnouncement(float deltaTime)
        {
            if (!Active || _announced) return false;

            // NOT OVER A CHARACTER, AND NOT BETWEEN TWO OF THEIR LINES.
            // (0.7.239, corrected 0.7.240.)
            //
            // Zamar: "delay this Your Deck line until after Leshy is done
            // speaking and you gain control" — and then, one build later,
            // "Your Deck should not read until after Leshy's ENTIRE opening
            // conversation."
            //
            // 0.7.239 gated on AwaitingInput, which is false in the moment
            // between one line being dismissed and the next appearing. The deck
            // line went straight through that gap; his log shows it landing and
            // the next Leshy line opening immediately after. ConversationRunning
            // asks TextDisplayer.PlayingEvent instead, which stays true across
            // the whole scripted event, gaps and all.
            //
            // The clock is NOT reset while it waits. The count has already
            // settled; holding the line is not a reason to re-measure the deck.
            if (DialogueWaiting()) { _quietFor = 0f; return false; }

            int count = Cards().Count;
            if (count == 0)
            {
                _settleFor   = 0f;
                _stableCount = -1;
                return false;
            }

            // Every new card resets the clock. Same shape as
            // MenuReader.NeedsScreenAnnouncement, which has always done this
            // correctly and is why menus never had this bug.
            if (count != _stableCount)
            {
                _stableCount = count;
                _settleFor   = 0f;
                _lastCountAt = UnityEngine.Time.realtimeSinceStartup;
                return false;
            }

            _settleFor += deltaTime;
            if (_settleFor < SETTLE_SECONDS) return false;

            // Logged so the next run can confirm the count settled rather than
            // was caught mid-spawn again. If this number still disagrees with
            // the deck on screen, the spawn is slower than the window and the
            // window is what moves — not the test.
            // AND THEN WAIT TO SEE IF ANYONE IS ABOUT TO TALK. (0.7.243.)
            //
            // Zamar, twice now: "The deck read out still happened before Leshy's
            // conversation."
            //
            // 0.7.240 asked whether a conversation was RUNNING, and at the moment
            // the deck settles — half a second after the map loads — the run
            // intro has not started yet, so the honest answer is no. There is
            // nothing to wait for, and then a beat later there is.
            //
            // So the test is quiet TIME, not state: the count has settled AND
            // nothing has spoken for a while. A conversation starting inside the
            // window puts the clock back to zero, so the whole of Leshy's intro
            // plus the window has to pass before the deck reads.
            //
            // ONLY WHEN THE GAME OPENED IT. A deck view the player pressed D for
            // is the answer to a keypress and must not sit on a timer, so this
            // branch is skipped there. The run-start deck view is the one that
            // arrives unasked, and it is the only one that was ever stepping on
            // the performance.
            if (!_playerAsked)
            {
                _quietFor += deltaTime;
                if (_quietFor < DIALOGUE_GRACE_SECONDS) return false;
            }

            _log?.LogInfo($"IKMA DECK: card count settled at {count} " +
                          $"after {UnityEngine.Time.realtimeSinceStartup - _lastCountAt:F2}s" +
                          (_playerAsked ? " (opened by the player)." : $", plus {_quietFor:F2}s quiet."));
            return true;
        }

        /// <summary>
        /// Is a character mid-sentence and waiting on the player? Asked of
        /// DialogueAdvancer, which is the one place in the mod that knows, and
        /// wrapped because a throw here must never cost the announcement
        /// permanently — a false answer delays one line, an exception would
        /// swallow every one.
        /// </summary>
        private static bool DialogueWaiting()
        {
            try { return DialogueAdvancer.ConversationRunning(); }
            catch { return false; }
        }

        public static void Announce()
        {
            var cards = Cards();
            if (cards.Count == 0) return;

            _announced = true;

            // THE CURSOR STARTS NOWHERE HERE TOO. (0.7.277.)
            //
            // Zamar: "do the nowhere -1 thing here. I dont want to hear Ant
            // Queen in this line. Not till I hit right arrow and hear it as my
            // first card."
            //
            // The mod-wide rule from 0.7.245 — arrival names no option, the
            // first arrow lands ON option one — and this screen was the last
            // place still reading its first card as part of the arrival. It
            // also made that card sound like a position the player was on when
            // it was only the front of the list.
            //
            // Browse already does the right thing from -1 (it reads where the
            // cursor is rather than moving off it), so one press of Right is
            // card 1. Nothing else changes.
            _index = -1;

            // Teeth after the card count, matching the H text and the A key.
            // (Session 16, Zamar: "It also should read your teeth amount after
            // the card amount, like it does in the other context.") One screen
            // should not describe itself two different ways depending on which
            // key got you there.
            string lead = _openingSpoken ? "" : Vocabulary.DeckView.YourDeck;
            _openingSpoken = false;

            // THE KIN BREAKDOWN GOES HERE, NOT ON THE ACK. (0.7.276.)
            //
            // 0.7.275 put it on the instant "Your deck." acknowledgement and
            // Zamar still did not hear it: "I still didnt get the Kin amount
            // here like I need to." His log says why in two lines —
            //
            //   SPEAK (interrupt): Your deck. Insect, 7 cards. Reptile, 1 ...
            //   SPEAK (interrupt): 10 cards. 28 teeth collected. Ant Queen...
            //
            // — half a second apart, and the second one INTERRUPTS. That is by
            // design: the ack exists to answer the keypress immediately, and
            // this line replaces it once the count has settled. Anything the
            // ack says beyond "Your deck." is spoken into a sentence that is
            // about to be cut.
            //
            // The lesson is one this project keeps relearning in new places: a
            // line is not delivered because it was spoken. Both acks are back
            // to a bare "Your deck." and the breakdown lives in the one line
            // that is never cut, so it reads the same from the map and from
            // the Woodcarver.
            //
            // Placed directly after the card count because it IS that count,
            // broken down — then teeth, then the first card, which is the
            // order the rest of this line already had.
            Speech.Browse(
                Vocabulary.DeckView.LeftAndRightArrows(lead, CountWord(cards.Count), NodeScreenReader.KinBreakdown(), CurrencyPart(), PlaceBelow));

            // No hover on arrival: the cursor is nowhere, so there is nothing
            // to point at. The first Browse hovers card 1.
        }

        /// <summary>
        /// Make the next settle announce again, even though the view never
        /// actually changed.
        /// </summary>
        /// <remarks>
        /// 0.7.277. Zamar reopened the deck from the Woodcarver and heard only
        /// "Your deck." — his log has two "deck view opened from the
        /// Woodcarver" lines with no "DECK: opened from ..." between them, so
        /// OnViewEntered never ran and the reader still believed it had
        /// announced. Closing and reopening fast enough leaves the camera on
        /// the same rung, and MapReader's view watcher only speaks on a
        /// CHANGE.
        ///
        /// So the opener says so directly rather than relying on a transition
        /// being observed. Same lesson as the post-boss latch and the boot
        /// clock: a hand-over that depends on somebody noticing a moment will
        /// eventually miss one.
        /// </remarks>
        internal static void ForceReannounce()
        {
            _announced   = false;
            _index       = -1;
            _hovered     = null;
            _settleFor   = 0f;
            _quietFor    = 0f;
            _stableCount = -1;
            _log?.LogInfo("IKMA DECK: re-announcing — reopened without a view change.");
        }

        private static string CountWord(int n) => Vocabulary.CardCount(n);

        private static string Describe(List<SelectableCard> cards, int index)
        {
            if (index < 0 || index >= cards.Count) return "";

            CardInfo info = null;
            try { info = cards[index].Info; } catch { }

            string described = CardReader.DescribeCardInfo(info);

            // Session 16: no position here. Zamar's general rule — the number of
            // options and which one you are on belong to M and nowhere else.
            // A card in your deck has a name, so unlike a face-down reward it
            // still has something to say without its index.
            return Vocabulary.CardOrUnreadable(described);
        }

        /// <summary>
        /// The card AND where it sits. M only. Position goes at the END, which
        /// is the settled rule for counts — the part being scanned for is the
        /// card, and the number is orientation behind it.
        /// </summary>
        private static string DescribeWithPosition(List<SelectableCard> cards, int index)
        {
            if (index < 0 || index >= cards.Count) return "";
            return Vocabulary.OptionOf(Describe(cards, index), index + 1, cards.Count);
        }

        public static void Browse(int direction)
        {
            var cards = Cards();
            if (cards.Count == 0) return;

            // Same first-press rule as the map paths: the first arrow READS
            // where the cursor already is rather than moving off it, so card 1
            // is reachable without going all the way round.
            if (_index < 0) _index = 0;
            else _index = (_index + direction + cards.Count) % cards.Count;

            Hover(cards, _index);
            Speech.Browse(Describe(cards, _index));
        }

        public static void AnnounceCurrent()
        {
            var cards = Cards();
            if (cards.Count == 0 || _index < 0) return;
            Speech.Browse(DescribeWithPosition(cards, _index));
        }

        // ------------------------------------------------------------------
        // H. What the screen is, and the two numbers that are on the table with
        // it. Zamar asked for both here specifically.
        // ------------------------------------------------------------------
        public static void SpeakHelp()
        {
            var cards = Cards();

            // "YOUR DECK. 0 CARDS." IS A LIE AND IT SHIPPED. Zamar's 0.7.55 log
            // has it five times in a row: he pressed H while the deck was still
            // being dealt onto the table, and this read the array as it found
            // it. Announce set out to fix exactly that and this line was missed,
            // because it reaches Cards() by its own route.
            //
            // A count of zero on a screen showing your deck is never true — the
            // deck is either still arriving or the read failed, and both of
            // those are "not yet", not "none". Say so.
            string sizePart = cards.Count == 0
                ? Vocabulary.DeckView.YourDeckIsStill
                : Vocabulary.DeckView.YourDeckName(CountWord(cards.Count));

            Speech.Browse(
                Vocabulary.DeckView.LeftAndRightArrowsBrowse(sizePart, CurrencyPart(), CardReader.ShiftRHelp, PlaceBelow));
        }

        /// <summary>
        /// A. The same key that reads resources in combat, reading the one
        /// resource that is on the table here. (0.7.52, Zamar's call: "the A key
        /// should also display teeth in that deck viewer page, to keep
        /// consistent with A reading resources during combat.")
        ///
        /// A key that means the same thing everywhere is worth more than a key
        /// that is perfectly tuned to each screen.
        /// </summary>
        public static void AnnounceResources()
        {
            string teeth = CurrencyPart();

            // The bowl is not on the table, so the number is not on offer to
            // anyone. Say that, rather than a figure or silence — silence here
            // reads exactly like a key that failed.
            Speech.Browse(
                string.IsNullOrEmpty(teeth)
                    ? Vocabulary.DeckView.NoTeethAreShown
                    : teeth.TrimStart());
        }

        // ------------------------------------------------------------------
        // ONE CURRENCY, NOT TWO. (Session 15.)
        //
        // Zamar asked for "[x] gold, [x] teeth", believing them to be two
        // visually different things. The dump says otherwise, and this is what
        // it says:
        //
        //   RunState.currency   PUBLIC Int32 — the number the game itself shows
        //                       through CurrencyTabPart1.GetCurrency(), and the
        //                       number BuyPeltsSequencer.ShowSpendTeeth(price)
        //                       spends. In Act 1 that is gold teeth, one thing.
        //
        //   RunState.skullTeeth PUBLIC Int32 — belongs to FreeTeethSkull, the
        //                       prop on the table, alongside MAX_TEETH,
        //                       GetRandomNumTeeth() and RefreshTeeth(). Its
        //                       spawn method is SpawnTeethIfNotAscension, so it
        //                       does not appear in Kaycee's Mod at all.
        //
        // Reflection cannot read method bodies, so this is inference from names
        // and signatures rather than proof — but splitting one number into two
        // on a hunch would put a figure in front of the player that is on no
        // screen anywhere. One number, until he says otherwise.
        //
        // THE BOWL IS THE PARITY GATE. ShowCurrencyBowl() decides whether the
        // bowl is on the table; if it is not, the number is not on offer to
        // anyone and IKMA does not read it either.
        // ------------------------------------------------------------------
        private static string CurrencyPart()
        {
            try
            {
                if (!BowlShown()) return "";

                var run = RunState.Run;
                if (run == null) return "";

                // "collected" is Zamar's word, Session 16. It says these are
                // teeth you have EARNED rather than teeth that exist somewhere,
                // which is the question a player actually has looking at a bowl.
                int teeth = run.currency;
                return Vocabulary.DeckView.ToothCollectedCount(teeth);
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA DECK: currency read failed: {e.Message}");
                return "";
            }
        }

        private static bool BowlShown()
        {
            var seq = Sequencer();
            if (seq == null) return false;

            try
            {
                if (_showCurrencyBowlMethod == null)
                    _showCurrencyBowlMethod = typeof(DeckReviewSequencer).GetMethod(
                        "ShowCurrencyBowl",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (_showCurrencyBowlMethod == null) return false;
                return (bool)_showCurrencyBowlMethod.Invoke(seq, null);
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        // Visual sync — the raised card must be the one being read out.
        // ------------------------------------------------------------------
        private static void Hover(List<SelectableCard> cards, int index)
        {
            if (index < 0 || index >= cards.Count) return;
            var entering = cards[index];

            try
            {
                if (_hovered != null && _hovered != entering) _hovered.CursorExit();
                if (entering != null) entering.CursorEnter();
                _hovered = entering;
            }
            catch (System.Exception e)
            {
                // TYPE AND MESSAGE, not message alone. The 0.7.53 log carried
                // "IKMA DECK: hover sync failed:" with nothing after it on every
                // open — an exception whose Message is empty logs as no
                // information at all, which is the log-discipline equivalent of
                // announcing silence. Every catch that only printed e.Message
                // could do this.
                _log?.LogWarning(
                    $"IKMA DECK: hover sync failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void ReleaseHover()
        {
            try { _hovered?.CursorExit(); } catch { }
            _hovered = null;
        }
    }
}
