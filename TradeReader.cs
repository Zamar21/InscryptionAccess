// TradeReader.cs
using System.Collections.Generic;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The Trader's trade, on the keyboard. The one screen in Kaycee's Mod
    /// that a blind player could reach and then not finish.
    /// </summary>
    /// <remarks>
    /// 0.7.216, Session 21.
    ///
    /// WHAT WAS WRONG. During <c>TradeCardsForPelts.TradePhase</c> the game
    /// switches to <c>ViewController.ControlMode.TraderCardsForPeltsPhase</c>
    /// and hangs a <c>CursorSelectStarted</c> delegate on every opponent board
    /// slot and every opponent queue slot holding a card: click one while you
    /// hold a pelt and it becomes yours. IKMA has no concept of ControlMode
    /// anywhere, and those slots are not cards in a hand or parts of a node
    /// screen, so nothing offered them. The bell is disabled for the whole
    /// phase and the game will not continue until you are out of pelts or the
    /// board and queue are empty — so a keyboard player reached the Trapper's
    /// phase two and could not leave it. Quiet is a gap; this was a wall.
    ///
    /// It is reachable two ways: the Trapper/Trader boss's phase two
    /// (<c>TradePhase(4, 4, tier+1, tier)</c>) and Leshy's Trader mask
    /// (<c>TradePhase(1, 1, 2, 1)</c>) — so both final-region fights need it.
    ///
    /// EVERY FACT BELOW CAME OUT OF THE DECOMPILED SOURCE, NOT A PLAYTEST.
    /// This file was nearly deferred for "a log from a Trapper fight", and
    /// rule 7 is right: the game's own source answers "what does the game do
    /// here" and it answered all of it. `dumps/dump_trade_from_decompile.txt`.
    ///
    /// ASK THE GAME WHICH SLOTS ARE LIVE. A tradable slot is one the game has
    /// given the Pickup cursor AND a CursorSelectStarted delegate — see
    /// <see cref="IsTradable"/> for why both, and for the set-only-property
    /// trap that failed the first build of this file. Those are the game's own
    /// answer to "can the player act on this", so they are asked live and
    /// never cached. No flag of IKMA's own decides when this reader is up.
    ///
    /// PARITY. The trade cards have <c>RenderInfo.hiddenCost = false</c> for
    /// the whole phase — the game deliberately shows what each one costs, and
    /// hides them again afterwards. So the cost is read here, through the same
    /// CardReader.DescribeCardInfo every other card read uses. Outside this
    /// phase nothing changes.
    ///
    /// WHAT IT DOES NOT SWALLOW. Unlike the node screens, this reader handles
    /// only the arrows, Enter, Space and H, and lets every other key fall
    /// through — a player mid-trade still wants G, B and A to weigh up what
    /// they are buying, and the whole point of the screen is a decision.
    /// </remarks>
    public static class TradeReader
    {
        private static int _index = -1;
        private static bool _announced;
        private static float _settle;

        // ==================================================================
        // ARMED BY THE GAME, NEVER BY POLLING. (0.7.217 — and this is a crash
        // fix, not a tidy-up.)
        //
        // 0.7.216 called Active from Update every frame, and Active asks
        // BoardManager for its slots. OUTSIDE A BATTLE THERE IS NO
        // BoardManager, and Singleton<T>.Instance does not merely return null
        // when the instance is missing:
        //
        //     protected static void FindInstance()
        //     {
        //         if (m_Instance == null)
        //             m_Instance = (T)Object.FindObjectOfType(typeof(T));
        //     }
        //
        // — a full FindObjectOfType, inside a lock, on EVERY call. So the map,
        // the cabin and every menu ran two scene-wide object searches per
        // frame and wrote a warning line each time. Zamar's log came back 1358
        // lines of "Got null in Singleton<DiskCardGame.BoardManager>" out of
        // 1565, starting at line 153 and never stopping, and the game died.
        //
        // THE RULE THIS BROKE IS ALREADY WRITTEN DOWN: never FindObjectOfType.
        // It was not called directly — it was reached through
        // Singleton<T>.Instance, which is the APPROVED accessor and is cheap
        // only while the instance exists. A cached singleton degrades into the
        // banned call the moment it is absent. Asking a Singleton for
        // something that is not there is not free, and nothing may ask one
        // every frame without first knowing it is there.
        //
        // So the phase now announces itself. TradeCardsForPelts.TradePhase is
        // already patched; that prefix calls Arm(). Until it does, Tick and
        // Active return on a bool and touch nothing. The liveness test still
        // decides when the phase is OVER — that part was right, and it is only
        // ever consulted inside a battle, where BoardManager exists and
        // Instance is the cached field it is meant to be.
        // ==================================================================
        private static bool _armed;
        private static bool _sawOptions;

        /// <summary>Settle before the opening line: the cards are placed one
        /// at a time, 0.1s apart, and the dialogue runs before the delegates
        /// are hung. Nothing here is reaction-timed.</summary>
        private const float ANNOUNCE_SETTLE = 0.6f;

        /// <summary>
        /// A LIVENESS TEST, NOT A FLAG. The same rule NodeScreenReader learned
        /// the hard way: never remember where IKMA put the player, ask the
        /// game. When the phase ends the game clears every delegate and resets
        /// every HighlightCursorType, so this goes false on its own however
        /// the phase ended — traded out, bought out, or the run abandoned.
        /// </summary>
        public static bool Active
        {
            get
            {
                // The bool FIRST, always. Everything below this line can reach
                // Singleton<BoardManager>.Instance; see the note on _armed.
                if (!_armed) return false;
                try { return Options().Count > 0; }
                catch { return false; }
            }
        }

        /// <summary>
        /// A trade phase has begun. Called from the TradeCardsForPelts.TradePhase
        /// prefix — the game's own announcement that this screen exists.
        ///
        /// The delegates and cursors are not wired until several seconds into
        /// that coroutine, so Options() is legitimately empty at first: that
        /// is what _sawOptions is for, so an empty list before the phase has
        /// set itself up is not mistaken for the phase being over.
        /// </summary>
        /// <summary>
        /// True from the moment the game announces the trade phase until the
        /// reader is disarmed. Cheap — a bool, no Singleton lookup — so a
        /// per-event suppression check can ask it. (0.7.302.)
        /// </summary>
        internal static bool Armed => _armed;

        internal static void Arm()
        {
            _armed      = true;
            _sawOptions = false;
            _announced  = false;
            _settle     = 0f;
            _index      = -1;
            Plugin.Log?.LogInfo("IKMA TRADE: armed by TradePhase — polling starts now, not before.");
        }

        /// <summary>
        /// Disarm. Called when the phase ends on its own, and from the same
        /// place BoardWatcher is reset, so a trade cannot outlive its battle
        /// and leave Update asking a dead Singleton for a board every frame.
        /// </summary>
        internal static void Reset()
        {
            if (!_armed && _index == -1 && !_announced) return;
            Plugin.Log?.LogInfo("IKMA TRADE: disarmed.");
            _armed      = false;
            _sawOptions = false;
            _announced  = false;
            _settle     = 0f;
            _index      = -1;
        }

        /// <summary>
        /// Every slot the game will currently accept a trade click on, board
        /// first then queue, in slot order. Both lists are PUBLIC on
        /// BoardManager; CardSlot and the queue slots are both
        /// HighlightedInteractable, which is what carries HighlightCursorType.
        /// </summary>
        internal static List<HighlightedInteractable> Options()
        {
            var live = new List<HighlightedInteractable>();
            var bm = Singleton<BoardManager>.Instance;
            if (bm == null) return live;

            try
            {
                var slots = bm.OpponentSlotsCopy;
                if (slots != null)
                    for (int i = 0; i < slots.Count; i++)
                        if (IsTradable(slots[i])) live.Add(slots[i]);
            }
            catch { }

            try
            {
                var queue = bm.OpponentQueueSlots;
                if (queue != null)
                    for (int i = 0; i < queue.Count; i++)
                        if (IsTradable(queue[i])) live.Add(queue[i]);
            }
            catch { }

            return live;
        }

        // ------------------------------------------------------------------
        // IS THE GAME ACCEPTING A TRADE CLICK ON THIS SLOT RIGHT NOW?
        //
        // TWO SIGNALS, BOTH PUBLIC GETTERS, BOTH SET AND CLEARED BY THE SAME
        // METHOD. Either alone would probably do; together they cannot be
        // wrong in the direction that matters, which is offering the player a
        // key that does nothing.
        //
        //   1. The cursor. `HighlightCursorType` is SET-ONLY on
        //      HighlightedInteractable (0.7.216 build failure, CS0154 — it
        //      declares a setter and no getter). It writes the `cursorType`
        //      field, and InteractableBase exposes that same field through
        //      `public override CursorType CursorType => cursorType`, so the
        //      value IS readable, just not under the name it is written by.
        //      TradeCardsForPelts is the ONLY thing in the game that ever
        //      sets Pickup on a board or queue slot (lines 96 and 107), and
        //      it resets both to Default when the phase ends (117, 122).
        //
        //   2. The delegate. TradePhase hangs CursorSelectStarted on each
        //      tradable slot and calls ClearDelegates() on every opponent slot
        //      and every queue slot when the phase is over, which nulls it.
        //
        // Signal 2 is what guards against the thing signal 1 cannot rule out
        // from source alone: a prefab whose serialized default cursorType is
        // already Pickup. A slot with the cursor but no delegate is not
        // tradable, and vice versa.
        // ------------------------------------------------------------------
        private static bool IsTradable(HighlightedInteractable it)
        {
            if (it == null) return false;
            try
            {
                if (it.CursorType != DiskCardGame.CursorType.Pickup) return false;
                return it.CursorSelectStarted != null;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        // WHAT IS ON A GIVEN SLOT.
        //
        // A board slot carries its card directly. A QUEUE slot does not — it
        // is a separate interactable, and the card that belongs to it is found
        // the way TradeCardsForPelts itself finds it: by matching the queued
        // card's QueuedSlot against the board slot at the same index. Asking
        // the game its own question rather than guessing an ordering.
        // ------------------------------------------------------------------
        private static PlayableCard CardOn(HighlightedInteractable it, out bool queued, out int slotNumber)
        {
            queued = false;
            slotNumber = -1;
            if (it == null) return null;

            var bm = Singleton<BoardManager>.Instance;
            if (bm == null) return null;

            // The board case.
            var asSlot = it as CardSlot;
            if (asSlot != null)
            {
                try { slotNumber = asSlot.Index + 1; } catch { }
                return BoardReader.LiveCard(asSlot);
            }

            // The queue case.
            queued = true;
            try
            {
                var queue = bm.OpponentQueueSlots;
                int qi = queue != null ? queue.IndexOf(it) : -1;
                if (qi < 0) return null;

                slotNumber = qi + 1;

                var boardSlots = bm.OpponentSlotsCopy;
                if (boardSlots == null || qi >= boardSlots.Count) return null;
                var forSlot = boardSlots[qi];

                var opponent = TurnManager.Instance?.Opponent;
                if (opponent?.Queue == null) return null;

                for (int i = 0; i < opponent.Queue.Count; i++)
                {
                    var c = opponent.Queue[i];
                    if (c != null && ReferenceEquals(c.QueuedSlot, forSlot)) return c;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Pelts you are holding. The game gates the whole phase on this
        /// (its own PeltInHand is private), and it is the difference between
        /// "choose one" and "you have nothing to trade with" — so it is asked
        /// by the game's own test, Trait.Pelt, not by card name.
        /// </summary>
        /// <summary>The name of the pelt the game will spend: the first in hand.</summary>
        private static string FirstPeltInHandName()
        {
            try
            {
                var hand = Singleton<PlayerHand>.Instance?.CardsInHand;
                if (hand == null) return null;
                for (int i = 0; i < hand.Count; i++)
                    if (hand[i]?.Info != null && hand[i].Info.HasTrait(Trait.Pelt))
                        return CardReader.CardName(hand[i].Info);
            }
            catch { }
            return null;
        }

        internal static int PeltsInHand()
        {
            try
            {
                var hand = Singleton<PlayerHand>.Instance?.CardsInHand;
                if (hand == null) return 0;
                int n = 0;
                for (int i = 0; i < hand.Count; i++)
                    if (hand[i]?.Info != null && hand[i].Info.HasTrait(Trait.Pelt)) n++;
                return n;
            }
            catch { return 0; }
        }

        // ------------------------------------------------------------------
        // Lifecycle. Driven from HotkeyManager.Update, like the node reader.
        // ------------------------------------------------------------------
        internal static void Tick(float dt)
        {
            // THE BOOL BEFORE ANYTHING ELSE. This method runs every frame from
            // HotkeyManager.Update; below this line it can reach
            // Singleton<BoardManager>.Instance, which is a FindObjectOfType
            // when there is no board. See the note on _armed.
            if (!_armed) return;

            int count = 0;
            try { count = Options().Count; } catch { }

            if (count > 0) _sawOptions = true;
            else if (_sawOptions)
            {
                // The game cleared every delegate and reset every cursor, which
                // it does in exactly one place: the end of TradePhase.
                //
                // 0.7.426 — THE TRADE SCREEN ALREADY READ THE BOARD. Zamar, on
                // "Enemy Leaping Trap has left slot 4. Enemy Worker Ant is
                // played in slot 4. Enemy Kingfisher is queued...": "Only the
                // Que should read here. Not the leaving or played." The cards
                // left on the board were browsed as trade options, and what
                // phase one left behind went with the sweep. So the board is
                // re-read here without a word; the queue is left for the
                // differ to announce.
                BoardWatcher.RebaselineBoard("the trade phase ended");
                Reset();
                return;
            }

            if (count == 0) return;   // wired up not yet; wait, do not announce
            if (_announced) return;

            _settle += dt;
            if (_settle < ANNOUNCE_SETTLE) return;

            _announced = true;
            _index = 0;

            var options = Options();
            Plugin.Log?.LogInfo(
                $"IKMA TRADE: phase live — {options.Count} tradable slot(s), " +
                $"{PeltsInHand()} pelt(s) in hand.");

            Speech.Prompt(new System.Func<string>(delegate
            {
                return Vocabulary.TradeScreenOpen(Options().Count, PeltsInHand());
            }));
        }

        internal static void Browse(int direction)
        {
            var options = Options();
            if (options.Count == 0) { Speech.Browse(Vocabulary.NoOptions); return; }

            if (_index < 0) _index = 0;
            else _index = (_index + direction + options.Count) % options.Count;

            SpeakAt(_index, options);
        }

        internal static void SpeakPosition()
        {
            var options = Options();
            if (options.Count == 0) { Speech.Browse(Vocabulary.NoOptions); return; }
            if (_index < 0) _index = 0;
            SpeakAt(_index, options);
        }

        private static void SpeakAt(int index, List<HighlightedInteractable> options)
        {
            if (index < 0 || index >= options.Count) return;

            bool queued;
            int slotNumber;
            var card = CardOn(options[index], out queued, out slotNumber);

            if (card?.Info == null)
            {
                Plugin.Log?.LogInfo(
                    $"IKMA TRADE: option {index + 1} of {options.Count} has no readable card.");
                Speech.Browse(Vocabulary.TradeOptionUnreadable(index + 1, options.Count));
                return;
            }

            // Cost included on purpose: the game reveals it for this phase.
            // NO COST ON THIS SCREEN. (0.7.313.) Zamar: "in this trading
            // phase, skip the cost of the read cards on their board and
            // queue."
            //
            // The cost is the wrong number here and it is the longest part of
            // the read. Every card on this screen costs the same thing — one
            // pelt — and its printed blood or bone cost is what it will cost
            // to PLAY once it is yours, which is a question for later. Six
            // cards browsed with a cost each is six clauses that answer
            // nothing about the trade.
            string described = CardReader.DescribeCardInfo(card.Info, includeCost: false);

            Speech.Browse(Vocabulary.TradeOption(
                index + 1, options.Count, described, queued, slotNumber));
        }

        // ------------------------------------------------------------------
        // TAKE IT. The same two calls the node reader uses to stand in for a
        // mouse click, on the interactable the game itself wired.
        //
        // IKMA does NOT check whether a pelt is in hand before firing. The
        // game's own OnTradableSelected does that check and does nothing when
        // the answer is no — asking the game its own question rather than
        // reimplementing the rule, which is the first rule of this project.
        // What IKMA does is say what happened afterwards.
        // ------------------------------------------------------------------
        internal static void Select()
        {
            var options = Options();
            if (options.Count == 0) { Speech.Browse(Vocabulary.NoOptions); return; }
            if (_index < 0 || _index >= options.Count) { _index = 0; }

            bool queued;
            int slotNumber;
            var target = options[_index];
            var card = CardOn(target, out queued, out slotNumber);

            string name = null;
            try { name = CardReader.CardName(card?.Info); } catch { }

            int peltsBefore = PeltsInHand();

            // Session 32: which pelt pays. The game spends the FIRST card in
            // hand with the Pelt trait (TradeCardsForPelts.OnTradableSelected,
            // CardsInHand.Find), so read that one, before the click.
            string peltName = FirstPeltInHandName();

            Plugin.Log?.LogInfo(
                $"IKMA TRADE: selecting option {_index + 1} ('{name ?? "?"}'), " +
                $"pelts in hand before = {peltsBefore}.");

            if (peltsBefore == 0)
            {
                // Not a refusal of IKMA's own — a statement of the game's
                // rule, which would silently do nothing here.
                Speech.Confirm(Vocabulary.TradeNoPelt());
                return;
            }

            try { target.CursorSelectStart(); target.CursorSelectEnd(); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA TRADE: select threw {e.GetType().Name}: {e.Message}");
                return;
            }

            if (!string.IsNullOrEmpty(name))
                using (Speech.Event(EventKind.CardObtained, EventSource.CurrentPlayer)) Speech.Confirm(Vocabulary.TradeTaken(peltName, name));

            // The list shrinks under us; step back so the next arrow lands on
            // a neighbour rather than past the end.
            if (_index > 0) _index--;
        }

        internal static void SpeakHelp()
        {
            Speech.Browse(Vocabulary.TradeHelp(Options().Count, PeltsInHand()));
        }
    }
}

// TradeReader.cs
