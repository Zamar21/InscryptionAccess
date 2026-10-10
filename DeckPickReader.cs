// DeckPickReader.cs
using System.Collections.Generic;
using System.Reflection;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// Choosing a card from your deck in the middle of a battle. (0.7.339.)
    /// </summary>
    /// <remarks>
    /// Zamar, Session 26, on the 0.7.338 log: he played an Opossum carrying
    /// Hoarder, the game laid his whole deck out on the table, and every arrow
    /// read his HAND instead. The game was waiting on a pick nothing could make.
    ///
    /// THREE ROUTES, ONE SCREEN. All three end in Deck.ChooseCard (public
    /// IEnumerator), which lays the deck out through
    /// BoardManager.CardSelector.SelectCardFrom:
    ///   Hoarder         Tutor.OnResolveOnBoard   -> Deck.Tutor -> ChooseCard
    ///   Magpie's Glass  MagnifyingGlassItem      -> Deck.Tutor -> ChooseCard
    ///   the boon        CardDrawPiles3D, BoonData.Type.TutorDraw, on a main
    ///                   deck draw                -> Deck.Tutor -> ChooseCard
    /// So one prefix on ChooseCard covers all three.
    ///
    /// THE GAME'S OWN STATE ANSWERS EVERY QUESTION HERE:
    ///   the cards      SelectableCardArray.displayedCards  (private List)
    ///   ready to pick  SelectableCard.Enabled — SelectCardFrom switches them
    ///                  all on only once every card has landed
    ///   picked         SelectableCardArray.selectedCard    (protected field),
    ///                  set by OnCardSelected. It keeps the PREVIOUS pick until
    ///                  the next SelectCardFrom clears it, so the value seen at
    ///                  arm time is recorded and ignored.
    /// Choosing clicks the card exactly as the node screens do:
    /// CursorSelectStart then CursorSelectEnd, which the game wired to
    /// OnCardSelected.
    ///
    /// NO "CHOSEN" LINE OF ITS OWN. The game spawns the pick into the hand and
    /// the hand watcher already says so; a second line would be two lines for
    /// one event.
    ///
    /// Keys, like the Trader's trade: arrows, Enter, Space and H are taken;
    /// everything else falls through so G, B and A still read the board.
    /// </remarks>
    internal static class DeckPickReader
    {
        private const float IDLE_FIRST  = 5.5f;
        private const float IDLE_REPEAT = 15f;
        private const float NEVER_OPENED_S = 10f;

        private static readonly BindingFlags Any =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo _displayedField =
            typeof(SelectableCardArray).GetField("displayedCards", Any);
        private static readonly FieldInfo _selectedField =
            typeof(SelectableCardArray).GetField("selectedCard", Any);

        private static bool _armed;
        private static float _armedFor;
        private static SelectableCardArray _array;
        private static object _staleSelected;
        private static bool _opened;
        private static int _index = -1;
        private static SelectableCard _hovered;
        private static float _idleFor;
        private static float _idleInterval = IDLE_FIRST;

        internal static bool Active => _armed;

        /// <summary>Deck.ChooseCard prefix. The cards are not out yet.</summary>
        internal static void Arm()
        {
            if (_displayedField == null || _selectedField == null)
            {
                Plugin.Log?.LogWarning(
                    "IKMA DECKPICK: SelectableCardArray fields not found — the pick cannot be read.");
                return;
            }

            SelectableCardArray array = null;
            try { array = Singleton<BoardManager>.Instance?.CardSelector; } catch { }
            if (array == null)
            {
                Plugin.Log?.LogWarning("IKMA DECKPICK: no BoardManager.CardSelector — not armed.");
                return;
            }

            _array = array;
            try { _staleSelected = _selectedField.GetValue(array); } catch { _staleSelected = null; }
            _armed = true;
            _armedFor = 0f;
            _opened = false;
            _index = -1;
            _hovered = null;
            _idleFor = 0f;
            _idleInterval = IDLE_FIRST;
            Plugin.Log?.LogInfo("IKMA DECKPICK: armed by Deck.ChooseCard.");
        }

        private static void Disarm(string why)
        {
            ReleaseHover();
            _armed = false;
            _array = null;
            _staleSelected = null;
            Plugin.Log?.LogInfo($"IKMA DECKPICK: released — {why}.");
        }

        /// <summary>The cards on the table, in the game's own order, skipping
        /// any the game has already destroyed.</summary>
        private static List<SelectableCard> Cards()
        {
            var live = new List<SelectableCard>();
            if (_array == null) return live;
            List<SelectableCard> all = null;
            try { all = _displayedField.GetValue(_array) as List<SelectableCard>; } catch { }
            if (all == null) return live;
            foreach (var c in all)
            {
                // Unity's == null is true for a destroyed object.
                if (c == null) continue;
                bool shown = false;
                try { shown = c.gameObject.activeInHierarchy; } catch { }
                if (shown) live.Add(c);
            }
            return live;
        }

        private static bool Picked()
        {
            if (_array == null) return false;
            object sel = null;
            try { sel = _selectedField.GetValue(_array); } catch { }
            var card = sel as SelectableCard;
            return card != null && !ReferenceEquals(sel, _staleSelected);
        }

        /// <summary>Every frame from HotkeyManager while armed.</summary>
        internal static void Tick(float dt)
        {
            if (!_armed) return;
            _armedFor += dt;

            if (Picked())
            {
                // Hand the pick's name to the arrival gate before letting go,
                // so the card reaching the hand is announced once, as a game
                // action. (0.7.340.)
                string picked = null;
                try
                {
                    var sel = _selectedField.GetValue(_array) as SelectableCard;
                    picked = CardReader.CardName(sel?.Info);
                }
                catch { }
                HandArrivalGate.ClaimPick(picked);
                Disarm("the game has the pick");
                return;
            }

            var cards = Cards();

            if (!_opened)
            {
                if (cards.Count == 0)
                {
                    if (_armedFor > NEVER_OPENED_S) Disarm("no cards were ever laid out");
                    return;
                }

                // Ready only when the game has switched every card on.
                foreach (var c in cards)
                {
                    bool on = false;
                    try { on = c.Enabled; } catch { }
                    if (!on) return;
                }

                _opened = true;
                _idleFor = 0f;
                int n = cards.Count;
                Plugin.Log?.LogInfo($"IKMA DECKPICK: {n} card(s) laid out and live.");
                // Queued, not interrupting: the Hoarder trigger line or the
                // item's own line is usually still being said.
                Speech.Commentary(() => ReviewHistory.AsPrompt(OpeningLine()));   // a prompt: kept while History > Prompts is on (Session 38)
                return;
            }

            if (cards.Count == 0) { Disarm("the cards are gone"); return; }

            // THE IDLE PROMPT. The game is waiting on the player: 5.5s, then
            // every 15s, full controls every time. Any key resets it.
            _idleFor += dt;
            if (_idleFor >= _idleInterval)
            {
                _idleFor = 0f;
                _idleInterval = IDLE_REPEAT;
                Speech.Commentary(() => ReviewHistory.AsPrompt(HelpLine()));   // a prompt: kept while History > Prompts is on (Session 38)
            }
        }

        internal static void NoteKey()
        {
            _idleFor = 0f;
            _idleInterval = IDLE_REPEAT;
        }

        private static string CountWord(int n) => Vocabulary.CardCount(n);

        // PROVISIONAL — PROVISIONAL_LINES.md #51. Claude's wording.
        private static string OpeningLine()
        {
            if (!_armed) return null;
            int n = Cards().Count;
            if (n == 0) return null;
            return Vocabulary.DeckPick.OpeningLine(CountWord(n));
        }

        // PROVISIONAL — PROVISIONAL_LINES.md #52. Claude's wording, in the
        // shape of his item pickup line.
        private static string HelpLine()
        {
            if (!_armed) return null;
            int n = Cards().Count;
            return Vocabulary.DeckPick.HelpLine(CountWord(n));
        }

        internal static void SpeakHelp()
        {
            NoteKey();
            string line = HelpLine();
            if (!string.IsNullOrEmpty(line)) Speech.Browse(line);
        }

        internal static void Browse(int direction)
        {
            NoteKey();
            var cards = Cards();
            if (!_opened || cards.Count == 0) return;

            // First press reads where you are, same as the deck view.
            if (_index < 0 && !ListStep.IsJump(direction)) _index = 0;
            else if (!ListStep.Step(ref _index, direction, cards.Count)) return;   // 0.4.8.007 - stops at the ends

            Hover(cards, _index);
            Speech.Browse(Describe(cards, _index));
        }

        internal static void SpeakPosition()
        {
            NoteKey();
            var cards = Cards();
            if (!_opened || cards.Count == 0 || _index < 0 || _index >= cards.Count) return;
            Speech.Browse(Vocabulary.OptionOf(Describe(cards, _index), _index + 1, cards.Count));
        }

        internal static void Choose()
        {
            NoteKey();
            var cards = Cards();
            if (!_opened || cards.Count == 0) return;

            // Enter before any card has been read picks nothing — it says
            // what the screen is instead.
            if (_index < 0 || _index >= cards.Count) { SpeakHelp(); return; }

            var card = cards[_index];
            bool on = false;
            try { on = card.Enabled; } catch { }
            if (!on)
            {
                Plugin.Log?.LogInfo("IKMA DECKPICK: Enter on a card the game has switched off — ignored.");
                return;
            }

            string name = "?";
            try { name = CardReader.CardName(card); } catch { }
            Plugin.Log?.LogInfo($"IKMA DECKPICK: choosing '{name}' (card {_index + 1} of {cards.Count}).");

            ReleaseHover();
            try { card.CursorSelectStart(); card.CursorSelectEnd(); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA DECKPICK: choosing threw {e.GetType().Name}: {e.Message}");
            }
        }

        private static string Describe(List<SelectableCard> cards, int index)
        {
            CardInfo info = null;
            try { info = cards[index].Info; } catch { }
            return Vocabulary.CardOrUnreadable(CardReader.DeckDisambiguated(cards, index, CardReader.DescribeCardInfo(info)));   // 0.7.433
        }

        // Visual sync: the raised card is the one being read.
        private static void Hover(List<SelectableCard> cards, int index)
        {
            var entering = cards[index];
            try
            {
                if (_hovered != null && _hovered != entering) _hovered.CursorExit();
                if (entering != null) entering.CursorEnter();
                _hovered = entering;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA DECKPICK: hover sync failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void ReleaseHover()
        {
            try { if (_hovered != null) _hovered.CursorExit(); } catch { }
            _hovered = null;
        }
    }

    // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the prefix would run TWICE.
    public class Deck_ChooseCard_Patch
    {
        public static void Prefix()
        {
            DeckPickReader.Arm();
        }
    }
}
