// BoardReader.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;

namespace IKMA
{
    public static class BoardReader
    {
        private static ManualLogSource _log;

        // Cached reflection for deck counts (Session 8): CardDrawPiles.Deck /
        // CardDrawPiles3D.SideDeck / Deck.CardsInDeck all have unverified
        // visibility (Exhausted turned out protected), so read them via
        // reflection — a resolution failure drops the counts from the read
        // instead of breaking the build or the A key.
        private static PropertyInfo _deckProperty;
        private static PropertyInfo _sideDeckProperty;
        private static PropertyInfo _cardsInDeckProperty;

        public static void Init(ManualLogSource log)
        {
            _log = log;
        }

        // ----------------------------------------------------------------------
        // C key: name and cost only. No stats, no sigils.
        // ----------------------------------------------------------------------
        public static void ReadHand()
        {
            var hand = Singleton<PlayerHand>.Instance;
            if (hand == null || hand.CardsInHand == null || hand.CardsInHand.Count == 0)
            {
                Speech.Browse(Vocabulary.HandEmpty);
                return;
            }

            var parts = new List<string>();
            foreach (var card in hand.CardsInHand)
            {
                if (card?.Info == null) continue;
                string costPart = FormatCost(card.Info);
                parts.Add(costPart != null
                    ? Vocabulary.Board.HandCardWithCost(CardReader.CardName(card.Info), costPart)
                    : CardReader.CardName(card.Info));
            }

            int count = parts.Count;
            string countWord = Vocabulary.CardCount(count);
            Speech.Browse(Vocabulary.Board.Hand(countWord, parts));
        }

        public static void ReadPlayerBoard()
        {
            // Session 15, Zamar's rule: while the opponent is holding its hand
            // out, that leads every board read for the rest of the combat. It is
            // the only action on the table that ends the fight, and it can be
            // revoked, so it is asked live rather than latched.
            string surrender = SurrenderReader.Prefix();

            var bm = Singleton<BoardManager>.Instance;
            if (bm == null) { Speech.Browse(Vocabulary.BoardUnavailable); return; }
            Speech.Browse(surrender + FormatSlots(Vocabulary.Board.YourBoard, bm.PlayerSlotsCopy) + ".");
        }

        // ----------------------------------------------------------------------
        // The card in this slot, or null if the slot is effectively empty.
        //
        // Session 13, and this is a false-narration fix, not a tidy-up. Zamar
        // cut a Rabbit with Scissors; the Rabbit was destroyed and gone from the
        // screen. `slot.Card` still returned it, so the enemy-board read
        // answered "Slot 3: Rabbit, 0/1" for a creature that no longer existed,
        // and the multi-strike line then announced "Mantis God attacks ... and
        // Rabbit in Slot 3" for the same ghost. A confidently wrong announcement
        // is worse than silence, and this one put a phantom blocker in the
        // player's board model.
        //
        // `PlayableCard.Dead` is the game's own answer (PUBLIC, confirmed in
        // dump_input.txt). Every line that DESCRIBES the board goes through
        // here. Navigation and selection logic deliberately does NOT — what can
        // be sacrificed or targeted is the game's question to answer, not ours.
        // ----------------------------------------------------------------------
        internal static PlayableCard LiveCard(CardSlot slot)
        {
            var card = slot?.Card;
            if (card == null) return null;
            try { if (card.Dead) return null; }
            catch { /* a card mid-teardown answers however it answers; keep it */ }
            return card;
        }

        public static void ReadOpponentBoard()
        {
            // Session 15, Zamar's rule: while the opponent is holding its hand
            // out, that leads every board read for the rest of the combat. It is
            // the only action on the table that ends the fight, and it can be
            // revoked, so it is asked live rather than latched.
            string surrender = SurrenderReader.Prefix();

            // 0.7.222 — in Leshy's fight, which mask is on leads the enemy
            // read. Empty string in every other fight; see BossNarrator.
            string mask = BossNarrator.MaskPrefix();

            var bm = Singleton<BoardManager>.Instance;
            if (bm == null) { Speech.Browse(Vocabulary.BoardUnavailable); return; }
            Speech.Browse(surrender + mask + FormatSlots(Vocabulary.Board.EnemyBoard, bm.OpponentSlotsCopy) + ".");
        }

        // ----------------------------------------------------------------------
        // B key: full situational read — your board, then enemy board, then
        // upcoming opponent queue. (Session 8, note 2. Inscryption-specific
        // extension of HSA's `b`, which reads your minions only; your-board-only
        // remains available on Shift+G.)
        // ----------------------------------------------------------------------
        public static void ReadFullSituation()
        {
            // Session 15, Zamar's rule: while the opponent is holding its hand
            // out, that leads every board read for the rest of the combat. It is
            // the only action on the table that ends the fight, and it can be
            // revoked, so it is asked live rather than latched.
            string surrender = SurrenderReader.Prefix();

            // 0.7.222 — the mask leads here too. B is the full situational
            // read and the enemy's face is part of the situation.
            string mask = BossNarrator.MaskPrefix();

            var bm = Singleton<BoardManager>.Instance;
            if (bm == null) { Speech.Browse(Vocabulary.BoardUnavailable); return; }
            string player   = FormatSlots(Vocabulary.Board.YourBoard, bm.PlayerSlotsCopy);
            string opponent = FormatSlots(Vocabulary.Board.EnemyBoard, bm.OpponentSlotsCopy);
            string queue    = BuildOpponentQueueText();
            Speech.Browse(Vocabulary.Board.BoardSummary(surrender, mask, player, opponent, queue));
        }

        // ----------------------------------------------------------------------
        // A key: scales first (most important), then bones.
        // ----------------------------------------------------------------------
        public static void ReadResources()
        {
            // Session 15, Zamar's rule: while the opponent is holding its hand
            // out, that leads every board read for the rest of the combat. It is
            // the only action on the table that ends the fight, and it can be
            // revoked, so it is asked live rather than latched.
            string surrender = SurrenderReader.Prefix();

            var rm = ResourcesManager.Instance;
            if (rm == null) { Speech.Browse(Vocabulary.Board.ResourcesUnavailable); return; }

            string scaleText = GetScaleText();
            int bones = rm.PlayerBones;
            string boneText = Vocabulary.BoneCount(bones);

            // THE CANDLES, DIRECTLY AFTER THE BONES. (0.7.219.)
            //
            // Zamar: "We need to add the candle count to A. Our candle count
            // should come directly after bone count. Then Enemy candle count
            // (if applicable), then Woodcarving and the rest."
            //
            // Candles are lives, and until now the only way to learn how many
            // were left was to hear one go out — which tells a player who was
            // listening at that moment and nobody else. A sighted player reads
            // the candelabra whenever they look at it.
            //
            // Both numbers are asked of BossNarrator, which already owned them.
            // The enemy half is "if applicable" in the strict sense: it is
            // omitted entirely outside a boss fight, because an ordinary
            // opponent has no candles on the table at all.
            string candleText = "";
            try
            {
                int mine = BossNarrator.PlayerCandles();
                if (mine >= 0)
                    candleText += Vocabulary.Board.CandleLitCount(mine);

                int theirs = BossNarrator.BossCandles();
                if (theirs >= 0)
                    candleText += Vocabulary.Board.EnemyHasCandleCount(theirs);
            }
            catch { candleText = ""; }

            // Deck counts trail (Session 8) — public information a sighted
            // player reads off the pile heights.
            string deckText = GetDeckCountsText();

            // The woodcarving sits BEFORE the deck counts, where Zamar asked
            // for it. (0.7.192.)
            string totemText = GetWoodcarvingText();

            // Scales, bones, your candles, their candles, woodcarving, decks.
            Speech.Browse(Vocabulary.Board.Name(surrender, scaleText, boneText, candleText, totemText, deckText));
        }

        // ----------------------------------------------------------------------
        // THE WOODCARVING. (0.7.192.)
        //
        // Zamar, 2026-09-12:
        //
        //   "Currently there's no way to tell what your current woodcarving is.
        //    Add that info to the A key before Your Deck info is read. Should
        //    only read if you have an active woodcarving, and if you do it
        //    should read 'Woodcarving, Kin type: [x], Sigil Ability [x].'"
        //
        // WHERE IT LIVES, read from IL (dumps/dump_woodcarving.txt). The end of
        // BuildTotemSequencer.BuildPhase, which is the woodcarver node handing
        // the finished carving to the run:
        //
        //     def.tribe   = completedTotem.top.prerequisites.tribe
        //     def.ability = completedTotem.bottom.effectParams.ability
        //     RunState.Run.totems.Clear()
        //     RunState.Run.totems.Add(def)
        //
        // THE CLEAR IS THE ANSWER TO "ONLY IF YOU HAVE ONE". The list is emptied
        // before every add, so it holds exactly one carving or none. Count == 0
        // is the game's own way of saying the player has no woodcarving — a
        // question with an answer, not an exception to catch.
        //
        // Note what is NOT used: RunState.totemTops and RunState.totemBottoms
        // are the loose PIECES collected on the map. Reading those would tell
        // him what he is carrying, not what is carved and active.
        //
        // THE TWO WORDS. The Kin comes out as the tribe enum's own name, which
        // is what CardReader already speaks on every card read — the same word
        // for the same thing, and "Kin type:" is already his framing there. The
        // sigil goes through CardReader.GetAbilityName like every other sigil in
        // the mod, so it is the rulebook name and Shift+R is armed for it.
        // Either one missing drops the whole line rather than speaking half of
        // it.
        // ----------------------------------------------------------------------
        private static string GetWoodcarvingText()
        {
            try
            {
                var run = RunState.Run;
                if (run == null || run.totems == null || run.totems.Count == 0)
                    return "";

                var def = run.totems[0];
                if (def == null) return "";

                if (def.tribe == Tribe.None) return "";
                string kin = def.tribe.ToString();

                string sigil = null;
                try { sigil = CardReader.GetAbilityName(def.ability); } catch { }
                if (string.IsNullOrEmpty(sigil)) return "";

                // ZAMAR'S WORDING, 2026-09-12, to the letter.
                return Vocabulary.Board.WoodcarvingKinTypeSigil(kin, sigil);
            }
            catch (Exception e)
            {
                _log?.LogWarning($"IKMA BOARD: woodcarving read failed: {e.Message}");
                return "";
            }
        }

        // ----------------------------------------------------------------------
        // Remaining cards in main and side decks, e.g.
        // " Your deck: 12 cards. Squirrel deck: 8 cards." (leading space for
        // direct concatenation). Empty string if anything fails to resolve.
        // ----------------------------------------------------------------------
        private static string GetDeckCountsText()
        {
            try
            {
                var piles = Singleton<CardDrawPiles>.Instance as CardDrawPiles3D;
                if (piles == null) return "";

                if (_deckProperty == null)
                    _deckProperty = typeof(CardDrawPiles).GetProperty(
                        "Deck", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (_sideDeckProperty == null)
                    _sideDeckProperty = typeof(CardDrawPiles3D).GetProperty(
                        "SideDeck", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                object mainDeck = _deckProperty?.GetValue(piles);
                object sideDeck = _sideDeckProperty?.GetValue(piles);
                if (mainDeck == null && sideDeck == null) return "";

                string result = "";
                int? main = GetCardsInDeck(mainDeck);
                int? side = GetCardsInDeck(sideDeck);
                if (main.HasValue)
                    result += Vocabulary.Board.YourDeckRemainingCard(main.Value);
                if (side.HasValue)
                    result += Vocabulary.Board.SquirrelDeckRemainingCard(side.Value);
                return result;
            }
            catch (Exception e)
            {
                _log?.LogWarning($"IKMA BOARD: deck counts failed: {e.Message}");
                return "";
            }
        }

        private static int? GetCardsInDeck(object deck)
        {
            if (deck == null) return null;
            if (_cardsInDeckProperty == null)
                _cardsInDeckProperty = deck.GetType().GetProperty(
                    "CardsInDeck", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object value = _cardsInDeckProperty?.GetValue(deck);
            return value is int i ? i : (int?)null;
        }

        // ----------------------------------------------------------------------
        // I key: items numbered 1-3 for slot clarity.
        // ----------------------------------------------------------------------
        // ======================================================================
        // I CYCLES, ONE ITEM PER PRESS. (0.7.275.)
        //
        // Zamar: "Item cycling didnt work with I on the deck view." It was not
        // broken — it read the whole list every time, which is the right
        // answer on the map and in a battle, where I is a status check and the
        // arrows belong to something else entirely.
        //
        // In the DECK VIEW the arrows are the deck. There is no second key to
        // browse items with, so repeated presses of the one key have to do it,
        // and hearing all three slots read out again for each press is the
        // same defect as a browse read that will not move.
        //
        // The cursor resets whenever the deck view opens, so I always starts
        // at slot 1 rather than wherever the last visit left it — a remembered
        // position in a screen the player has left is the stale-state bug this
        // project has paid for more than once.
        //
        // EMPTY SLOTS ARE READ, not skipped. Item slots are fixed positions
        // (Session 9): slot 2 is slot 2 whether or not slot 1 holds anything,
        // and a cycle that silently jumped an empty would make the numbering
        // lie.
        // ======================================================================
        private static int _itemCursor = -1;

        /// <summary>Called when a screen that cycles items opens.</summary>
        public static void ResetItemCycle() => _itemCursor = -1;

        /// <summary>Reads the next item slot, wrapping. One per press.</summary>
        public static void ReadNextItem()
        {
            var slots = GetConsumableSlots();
            if (slots == null || slots.Count == 0)
            {
                Speech.Browse(Vocabulary.ItemsUnavailable);
                return;
            }

            _itemCursor++;
            if (_itemCursor >= slots.Count) _itemCursor = 0;

            var data = GetConsumableData(GetConsumable(slots[_itemCursor]));
            string what = data != null ? Loc.Game(data.rulebookName) : Vocabulary.Board.ItemEmpty;
            if (data != null) CardReader.NoteItemInLine(data);   // 0.7.344, Shift+R

            Speech.Browse(Vocabulary.Board.ItemAtCursor(_itemCursor + 1, what));
        }

        public static void ReadItems()
        {
            // Session 9 fix: this used ItemsManager.Consumables, which is a
            // COMPACTED list of items actually held — so after spending the
            // item in slot 1, the Fish Hook in slot 2 was announced as "Item 1"
            // while it had not moved. Item slots are fixed positions; the
            // numbering the player hears has to match the slot, not the list
            // index. Enumerate the slots instead and name empties as empty.
            var slots = GetConsumableSlots();
            if (slots == null || slots.Count == 0)
            {
                Speech.Browse(Vocabulary.ItemsUnavailable);
                return;
            }

            var parts = new List<string>();
            for (int i = 0; i < slots.Count; i++)
            {
                var data = GetConsumableData(GetConsumable(slots[i]));
                if (data != null) CardReader.NoteItemInLine(data);   // 0.7.344, Shift+R
                parts.Add(Vocabulary.Board.ItemEmptyName(i + 1, (data != null ? Loc.Game(data.rulebookName) : Vocabulary.Board.ItemEmpty)));
            }

            Speech.Browse(string.Join(". ", parts) + ".");
        }

        // ======================================================================
        // Consumable item access.
        //
        // ItemsManager.consumableSlots is a private List<ConsumableItemSlot>,
        // and both ConsumableItemSlot.Consumable and ConsumableItem.CanActivate
        // are non-public (CS0122 at build). Everything below is cached
        // reflection, with the consumable held as `object` so no non-public
        // TYPE is named either. ConsumableItemData is public, so that one is
        // typed directly.
        //
        // Lives here rather than in HotkeyManager because this is the class
        // that reads game state; HotkeyManager calls in for item mode.
        // ======================================================================

        private static FieldInfo _consumableSlotsField;
        private static PropertyInfo _consumableProperty;
        private static MethodInfo _canActivateMethod;

        public static List<ConsumableItemSlot> GetConsumableSlots()
        {
            var im = Singleton<ItemsManager>.Instance;
            if (im == null) return null;

            try
            {
                if (_consumableSlotsField == null)
                {
                    _consumableSlotsField = typeof(ItemsManager).GetField(
                        "consumableSlots",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }
                if (_consumableSlotsField != null)
                    return _consumableSlotsField.GetValue(im) as List<ConsumableItemSlot>;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA ITEM: consumableSlots read failed: {e.Message}");
            }
            return null;
        }

        public static object GetConsumable(ConsumableItemSlot slot)
        {
            if (slot == null) return null;
            try
            {
                if (_consumableProperty == null)
                {
                    _consumableProperty = typeof(ConsumableItemSlot).GetProperty(
                        "Consumable",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                return _consumableProperty?.GetValue(slot);
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA ITEM: Consumable read failed: {e.Message}");
                return null;
            }
        }

        public static ConsumableItemData GetConsumableData(object consumable)
        {
            if (consumable == null) return null;
            try
            {
                var dataProp = consumable.GetType().GetProperty(
                    "Data",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return dataProp?.GetValue(consumable) as ConsumableItemData;
            }
            catch { return null; }
        }

        // Defaults to true if the gate cannot be found: better to let the game
        // refuse the click than to block a legal item because reflection missed.
        public static bool ConsumableCanActivate(object consumable)
        {
            if (consumable == null) return false;
            try
            {
                if (_canActivateMethod == null)
                {
                    _canActivateMethod = consumable.GetType().GetMethod(
                        "CanActivate",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, System.Type.EmptyTypes, null);
                }
                if (_canActivateMethod == null) return true;
                object result = _canActivateMethod.Invoke(consumable, null);
                return result is bool b ? b : true;
            }
            catch { return true; }
        }

        // ----------------------------------------------------------------------
        // O key: upcoming opponent queue — what Leshy will play next turn.
        // Uses Opponent.Queue (CardInfo list) and Opponent.QueuedSlots to report
        // which slot each card is targeting.
        // ----------------------------------------------------------------------
        public static void ReadOpponentQueue()
        {
            // 0.7.223 — the mask leads here too, his call. The queue is what
            // the enemy does NEXT turn, and in Leshy's fight which face is on
            // is half of that answer: an equipped mask is about to act, a bare
            // one is about to be put on.
            //
            // ONLY ON THIS PATH, not inside BuildOpponentQueueText. That
            // builder is shared with the B key — which already prefixes the
            // mask itself — and with the deferred opening-encounter read at
            // battle start, where the intro has just said it. Putting it in
            // the shared formatter would say it twice on B and once too early
            // at the bell.
            Speech.Browse(BossNarrator.MaskPrefix() + BuildOpponentQueueText());
        }

        /// <summary>
        /// Queue text without speaking it — used by CombatAnnouncer's deferred
        /// enqueue so the opening-encounter queue read is composed at speak
        /// time rather than at enqueue time. (Session 9.)
        /// </summary>
        public static string GetOpponentQueueText()
        {
            return BuildOpponentQueueText();
        }

        // Shared queue formatter — used by both the O key and the B key's full
        // situational read. Always returns a speakable sentence.
        private static string BuildOpponentQueueText()
        {
            var tm = TurnManager.Instance;
            if (tm == null) return Vocabulary.Board.OpponentUnavailable;

            var opponent = tm.Opponent;
            if (opponent == null) return Vocabulary.Board.OpponentUnavailable;

            var queue = opponent.Queue;
            var queuedSlots = opponent.QueuedSlots;

            if (queue == null || queue.Count == 0)
                return Vocabulary.Board.NoCardsQueued;

            // Build paired list of (card, targetSlotIndex) sorted by slot index
            // so we always read Slot 1 → Slot 2 → Slot 3 → Slot 4.
            var bm2       = Singleton<BoardManager>.Instance;
            var oppSlots2 = bm2?.OpponentSlotsCopy;

            var entries = new List<(PlayableCard card, int slotIdx)>();
            for (int i = 0; i < queue.Count; i++)
            {
                var qCard = queue[i];
                if (qCard?.Info == null) continue;

                int slotIdx = -1;
                if (queuedSlots != null && i < queuedSlots.Count && queuedSlots[i] != null && oppSlots2 != null)
                    slotIdx = oppSlots2.IndexOf(queuedSlots[i]);

                entries.Add((qCard, slotIdx));
            }

            // Sort by slot index (unslotted entries go last).
            entries.Sort((a, b) =>
            {
                int ai = a.slotIdx < 0 ? 999 : a.slotIdx;
                int bi = b.slotIdx < 0 ? 999 : b.slotIdx;
                return ai.CompareTo(bi);
            });

            var parts = new List<string>();
            foreach (var (qCard, slotIdx) in entries)
            {
                var cardInfo = qCard.Info;

                // Occupancy leads (Session 8): "Targeting occupied Slot 1: ..."
                // tells the player up front that the queued card cannot advance.
                // The blocker's name is dropped — the enemy board read already
                // names what sits in that slot.
                bool occupied = false;
                if (slotIdx >= 0 && oppSlots2 != null && slotIdx < oppSlots2.Count)
                    occupied = LiveCard(oppSlots2[slotIdx])?.Info != null;

                // Session 10: "Targeting Slot 1: ... Targeting Slot 3: ...
                // Targeting Slot 4: ..." repeated a five-syllable word three
                // times in one breath and buried the slot numbers, which are
                // the part the player is actually counting on. The header
                // already says these are upcoming; the word carries no
                // information after the first time.
                // Zamar's wording, 0.7.42 playtest: the board read runs three
                // sections together and the queue section read like more board.
                // "Entering" and "Blocked for" say which of the two a slot
                // number belongs to without the player having to hold the
                // section header in their head to the end of the sentence.
                string slotLabel = slotIdx >= 0
                    ? (occupied ? Vocabulary.Board.BlockedForSlot(slotIdx + 1) : Vocabulary.Board.EnteringSlot(slotIdx + 1))
                    : "";

                // Live values, not printed base values — same reason as the
                // hand read: variable-stat and buffed cards lie in CardInfo.
                string statPart    = $"{qCard.Attack}/{qCard.Health}";

                // Sigils listed bare here rather than with the "Ability:" label
                // the board reads use. In a queue read the player is scanning
                // several cards at once and the labels dominate the line; the
                // sigil names alone are unambiguous in this position.
                // GRANTED SIGILS COUNT. (0.7.177.)
                //
                // Zamar, on a queued Bloodhound the Prospector had just buffed:
                // "It also only read Guardian on the U press, didnt mention
                // Touch of Death."
                //
                // FormatBareAbilities was reading CardInfo.Abilities — the
                // card's PRINTED sigils. The Prospector grants Touch of Death
                // as a TEMPORARY MOD on the PlayableCard, so the one sigil that
                // makes that card lethal to anything it touches was the one
                // sigil the queue read omitted.
                //
                // SAME SPLIT AS THE STATS, which this method's own neighbours
                // already respect: the line two above deliberately uses live
                // qCard.Attack/Health because "variable-stat and buffed cards
                // lie in CardInfo". Abilities lie in exactly the same way and
                // were left on the printed source.
                //
                // Rule for this file: if a read describes a LIVE card, every
                // part of it comes off the PlayableCard. CardInfo is only for
                // cards that have no PlayableCard behind them.
                string abilityPart = FormatBareAbilitiesFor(qCard);
                parts.Add(Vocabulary.Board.Word(slotLabel, CardReader.CardName(cardInfo), statPart, abilityPart));
            }

            if (parts.Count == 0)
                return Vocabulary.Board.NoCardsQueued;

            string countWord = Vocabulary.CardCount(parts.Count);
            return Vocabulary.Board.UpcomingQueue(countWord, parts);
        }

        /// <summary>
        /// Bare sigil names for a card, for reads that compare several cards at
        /// once — the queue read and, since 0.7.43, the item-target read.
        ///
        /// Reads PlayableCard.Info.Abilities, the same source the queue read
        /// uses. Whether that list picks up a sigil granted mid-combat is NOT
        /// established; CardReader.ReadCard handles temporary mods separately
        /// and that is the read to copy from if this one is ever reported short.
        /// </summary>
        internal static string FormatBareAbilitiesFor(PlayableCard card)
        {
            // PRINTED SIGILS PLUS GRANTED ONES. (0.7.177.)
            //
            // This read CardInfo.Abilities alone, so any sigil added at runtime
            // was invisible everywhere it is used — the queue read and the
            // item-target read. The Prospector's buffed Bloodhound arrived in
            // the queue reading "Guardian" with no mention of the Touch of
            // Death that had just been granted to it.
            //
            // Same collection FormatSlots already does for the board reads:
            // native abilities first, then every TemporaryMod, de-duplicated in
            // first-seen order so the printed sigils keep their card order and
            // granted ones land after them.
            try
            {
                if (card == null) return "";

                var all  = new List<Ability>();
                var seen = new HashSet<Ability>();

                // Session 32: a sigil the game has negated (Magickal Bleach)
                // has no icon on the card, so it is not read either.
                var native = card.Info?.Abilities;
                if (native != null)
                    foreach (var a in native)
                        if (!CardReader.NegatedOnCard(card, a) && seen.Add(a)) all.Add(a);

                var mods = card.TemporaryMods;
                if (mods != null)
                    foreach (var mod in mods)
                        if (mod?.abilities != null)
                            foreach (var a in mod.abilities)
                                if (!CardReader.NegatedOnCard(card, a) && seen.Add(a)) all.Add(a);

                return FormatBareAbilities(all);
            }
            catch { return ""; }
        }

        // Comma-joined sigil names with no "Ability:" label — queue reads only.
        private static string FormatBareAbilities(List<Ability> abilities)
        {
            if (abilities == null || abilities.Count == 0) return "";

            var names = new List<string>();
            var seen  = new HashSet<Ability>();
            foreach (var ability in abilities)
            {
                if (!seen.Add(ability)) continue;
                string n = CardReader.GetAbilityName(ability);
                if (n != null) names.Add(n);
            }
            return names.Count == 0 ? "" : ", " + string.Join(", ", names);
        }

        /// <summary>
        /// Card names only — no costs, no stats, no sigils. Spoken automatically
        /// at the start of an encounter, after the opponent queue read, so the
        /// player begins the turn knowing both halves of the picture without
        /// pressing anything. (Session 10.) Deliberately shorter than the C-key
        /// read: this arrives unrequested, and an unrequested line has to earn
        /// every word. Returns null when the hand is empty, which the announcer
        /// skips silently rather than saying so.
        /// </summary>
        public static string GetHandNamesText()
        {
            var hand = Singleton<PlayerHand>.Instance;
            if (hand?.CardsInHand == null || hand.CardsInHand.Count == 0) return null;

            // NUMBERED HERE TOO. (0.7.138.) Zamar: "It should also add the
            // repeated numbers here as well. Wolf 1, wolf 2."
            //
            // The same disambiguator the arrow browse uses, so the list and the
            // browse agree — hearing "Wolf 2" on the arrows and then a list with
            // two plain Wolves in it would be worse than never numbering at all.
            var names = new List<string>();
            foreach (var card in hand.CardsInHand)
                if (card?.Info != null)
                    names.Add(CardReader.HandDisambiguated(card, CardReader.CardName(card.Info)));

            if (names.Count == 0) return null;
            string countWord = Vocabulary.CardCount(names.Count);
            return Vocabulary.Board.GetHandNamesText(countWord, names);
        }

        // ----------------------------------------------------------------------
        // The player's side of the board, as the G key reads it, without
        // speaking it. (0.7.161.)
        //
        // Added for ProspectorNarrator's post-wipe summary. It routes through
        // FormatSlots rather than formatting a second time, so the recap after
        // the Prospector destroys the board is worded identically to what G
        // says a second later. Two formatters for one board would drift, and
        // the player would hear the same slots described two different ways in
        // the same fight.
        // ----------------------------------------------------------------------
        internal static string GetPlayerBoardText()
        {
            var bm = Singleton<BoardManager>.Instance;
            if (bm == null) return null;
            return FormatSlots(Vocabulary.Board.YourBoard, bm.PlayerSlotsCopy);
        }

        // ----------------------------------------------------------------------
        // The player's board, but ONLY if something is on it. (0.7.162.)
        //
        // For the battle-start read. An empty board is the normal case and
        // needs no line — "Your board: empty" at the opening bell is noise on
        // every ordinary encounter, and noise on the common path is how a
        // player learns to stop listening. Returns null instead, which the
        // announcer drops without a sound.
        // ----------------------------------------------------------------------
        /// <summary>
        /// Anything already sitting on the OPPONENT'S board at the opening
        /// bell, or null when that row is empty.
        /// </summary>
        /// <remarks>
        /// 0.7.268. Zamar: "If there is something that starts on the enemy board
        /// it needs to also read here before my board after que. Frozen Opposum
        /// would have been a surprise to players."
        ///
        /// 0.7.162 added the player's own side for exactly this reason and only
        /// looked at one row. A Kaycee's Mod encounter can seed either side —
        /// his Angler run opened with a Frozen Opossum already in enemy slot 2 —
        /// and the opening read named the queue, his board and his hand while
        /// staying silent about a card that was already facing him.
        ///
        /// A sighted player takes the whole table in at once, so this is
        /// restoring parity, not adding a convenience. His order: queue, then
        /// the enemy's board, then yours, then your hand — what is coming, what
        /// is already across the table, what is already down, what you can play.
        ///
        /// Empty is the normal case and stays silent, the same rule the player
        /// board read follows: noise on the common path teaches a player to stop
        /// listening.
        /// </remarks>
        internal static string GetOpponentBoardTextIfOccupied()
        {
            var bm = Singleton<BoardManager>.Instance;
            if (bm == null) return null;

            var slots = bm.OpponentSlotsCopy;
            if (slots == null) return null;

            bool anyCard = false;
            foreach (var slot in slots)
            {
                if (LiveCard(slot)?.Info != null) { anyCard = true; break; }
            }
            if (!anyCard) return null;

            // 0.7.411: FormatSlots ends without a full stop; this caller adds it.
            return FormatSlots(Vocabulary.Board.EnemyBoard, slots) + ".";
        }

        internal static string GetPlayerBoardTextIfOccupied()
        {
            var bm = Singleton<BoardManager>.Instance;
            if (bm == null) return null;

            var slots = bm.PlayerSlotsCopy;
            if (slots == null) return null;

            bool anyCard = false;
            foreach (var slot in slots)
            {
                if (LiveCard(slot)?.Info != null) { anyCard = true; break; }
            }
            if (!anyCard) return null;

            // 0.7.411: FormatSlots ends without a full stop; this caller adds it.
            return FormatSlots(Vocabulary.Board.YourBoard, slots) + ".";
        }

        // ----------------------------------------------------------------------
        // Slot formatter — used for board reads (G, Shift+G, B).
        // Includes "Ability: X" / "Abilities: X and Y" label before sigil list.
        // ----------------------------------------------------------------------
        private static string FormatSlots(string label, List<CardSlot> slots)
        {
            if (slots == null || slots.Count == 0)
                return Vocabulary.Board.SideEmpty(label);

            var parts = new List<string>();

            // ONE CARD IS READ ONCE, HOWEVER MANY SLOTS IT HOLDS. (0.7.211.)
            //
            // Leshy's moon is a single PlayableCard whose GiantCard behaviour
            // writes itself into EVERY opponent slot on resolve
            // (GiantCard.OnResolveOnBoard). Before this, the enemy board read
            // answered "Slot 1: the moon, 6/40. Slot 2: the moon, 6/40. Slot
            // 3... Slot 4..." - four creatures, four separate blockers, four
            // things to kill, where a sighted player sees ONE card lying
            // across the whole row. That is not a missing line, it is a false
            // board model, and the player would plan a fight around it.
            //
            // The test is object identity, not name: two Squirrels are two
            // cards and read as two. It is also not gated on the moon, on
            // GiantCard, or on the Leshy encounter - the rule is about what
            // the board IS, so anything that ever holds two slots reads
            // correctly by the same sentence.
            var seenCards = new List<PlayableCard>();

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var card = LiveCard(slot);
                if (card?.Info == null) continue;

                bool already = false;
                for (int s2 = 0; s2 < seenCards.Count; s2++)
                    if (ReferenceEquals(seenCards[s2], card)) { already = true; break; }
                if (already) continue;
                seenCards.Add(card);

                // WHICH slots it lies across, not just how many. The moon
                // takes a contiguous row, but nothing guarantees that in
                // general, and "Slots 1 to 4" for a card actually sitting in
                // 1 and 3 would be a false statement about the board - the
                // same defect class this whole block exists to remove. So the
                // numbers are collected and rendered from what is there.
                var heldSlots = new List<int>();
                for (int s2 = 0; s2 < slots.Count; s2++)
                    if (ReferenceEquals(LiveCard(slots[s2]), card)) heldSlots.Add(s2 + 1);

                var info = card.Info;

                // Collect all abilities from native + mods + temp mods.
                var seen       = new HashSet<Ability>();
                var allAbilities = new List<Ability>();

                // Session 32: negated sigils (Magickal Bleach) are not drawn,
                // so they are not read. See CardReader.NegatedOnCard.
                foreach (var ability in info.Abilities)
                    if (!CardReader.NegatedOnCard(card, ability) && seen.Add(ability)) allAbilities.Add(ability);

                if (card.TemporaryMods != null)
                    foreach (var mod in card.TemporaryMods)
                        if (mod?.abilities != null)
                            foreach (var ability in mod.abilities)
                                if (!CardReader.NegatedOnCard(card, ability) && seen.Add(ability)) allAbilities.Add(ability);

                string abilityPart = FormatAbilityLabel(allAbilities, card);

                // THE DIRECTION MOVED INTO THE SIGIL NAME. (0.7.171.)
                //
                // It used to be appended here as a trailing clause after the
                // whole ability list. Zamar: "The moving right thing should be
                // directly after Sprinter, before other sigils." It now rides
                // the mover's own name inside abilityPart — see
                // CardReader.AbilityNameWithDirection, which also records the
                // punctuation bug the trailing version caused first.

                // Audit fix: use live PlayableCard.Attack/Health (includes damage
                // taken and buffs) rather than base CardInfo stats, so board reads
                // reflect the card's actual current state — same members HotkeyManager
                // already uses for sacrifice navigation (in-game confirmed safe).
                // A card holding more than one slot says so where the slot
                // number goes, because that IS its position.
                string where = heldSlots.Count > 1
                    ? Vocabulary.Board.SlotsList(JoinSlotNumbers(heldSlots))
                    : Vocabulary.Board.SlotNumber(i + 1);

                // 0.7.348 — A GIANT FILLS THE QUEUE TOO. Zamar, on "Enemy
                // board: Slots 1 to 4: The Limoncello": "It fills the board
                // and queue, so 8 slots. ... Same for the Moon." A giant
                // holding every board slot is drawn across the queue row as
                // well, so it is read as all eight.
                bool giant = false;
                try { giant = info.HasTrait(Trait.Giant); } catch { }
                if (giant && heldSlots.Count == slots.Count && slots.Count > 1)
                    where = Vocabulary.Board.SlotsOneTo(slots.Count * 2);

                // A VARYING NUMBER SAYS SO. (0.7.245.)
                //
                // Zamar, on "Slot 1: Flying Ant, 1/1": "This value is true but
                // we still need to say it's current power and that its power is
                // affected by the number of ants."
                //
                // A sighted player sees a STAR where the attack number would be
                // — the game's own way of saying this figure moves. The board
                // read printed the resolved number with nothing to mark it as
                // anything but fixed: true, and quietly misleading, because the
                // whole point of the card is that it changes.
                //
                // Only these cards pay for the extra words. SpecialStatIcon is
                // None on everything else and the terse "2/3" is untouched.
                bool varies = false;
                try { varies = info.SpecialStatIcon != SpecialStatIcon.None; } catch { }

                string stats = varies
                    ? Vocabulary.Board.CurrentlyStats(card.Attack, card.Health)
                    : $"{card.Attack}/{card.Health}";

                string statNote = varies
                    ? CardReader.DescribeSpecialStat(info.SpecialStatIcon)
                    : "";

                // 0.7.264 — the board read is a live read by this file's own
                // rule ("if a read describes a LIVE card, every part of it comes
                // off the PlayableCard"), so it says when a card is under the
                // water. The hand, queue and deck reads deliberately do not:
                // nothing in them is on the table to be submerged.
                parts.Add(Vocabulary.Board.FormatSlots(where, card, CardReader.CardName(info), stats, statNote, abilityPart));
            }

            // 0.7.344 — the Pirate Skull's crosshairs on this side, which a
            // sighted player sees the whole turn. PROVISIONAL wording, #54.
            // This read ends WITHOUT a full stop (the callers add one), so the
            // note is joined the same way the parts are.
            var cannonNotes = new List<string>();
            for (int i = 0; i < slots.Count; i++)
                if (PirateSkullNarrator.IsCannonTarget(slots[i]))
                    cannonNotes.Add(Vocabulary.CannonAimedAtSlot(i + 1).TrimEnd('.'));
            string cannon = cannonNotes.Count > 0 ? ". " + string.Join(". ", cannonNotes.ToArray()) : "";

            if (parts.Count == 0)
                return Vocabulary.Board.SideEmptyCannon(label, cannon);

            return Vocabulary.Board.SideCards(label, parts, cannon);
        }

        // ----------------------------------------------------------------------
        // "1 to 4" when the run is unbroken, "1, 2 and 4" when it is not.
        // A range is easier to hear than four numbers, and the contiguity is
        // checked rather than assumed. (0.7.211.)
        // ----------------------------------------------------------------------
        private static string JoinSlotNumbers(List<int> numbers)
        {
            if (numbers == null || numbers.Count == 0) return "";
            if (numbers.Count == 1) return numbers[0].ToString();

            bool contiguous = true;
            for (int i = 1; i < numbers.Count; i++)
                if (numbers[i] != numbers[i - 1] + 1) { contiguous = false; break; }

            if (contiguous)
                return Vocabulary.Board.NumberRange(numbers[0], numbers[numbers.Count - 1]);

            var head = new List<string>();
            for (int i = 0; i < numbers.Count - 1; i++) head.Add(numbers[i].ToString());
            return Vocabulary.Board.NumberList(head, numbers[numbers.Count - 1]);
        }

        // ----------------------------------------------------------------------
        // Formats a list of abilities as "Ability: X" or "Abilities: X and Y".
        // Returns empty string if no abilities.
        // ----------------------------------------------------------------------
        private static string FormatAbilityLabel(IList<Ability> abilities, PlayableCard card = null)
        {
            if (abilities == null || abilities.Count == 0) return "";

            var names = new List<string>();
            var seen  = new HashSet<Ability>();
            foreach (var ability in abilities)
            {
                if (!seen.Add(ability)) continue;
                // The mover's direction is attached to the mover's own name —
                // see CardReader.AbilityNameWithDirection. With no card passed
                // this yields the plain name, so other callers are unaffected.
                string n = CardReader.AbilityNameWithDirection(ability, card);
                if (n != null) names.Add(n);
            }

            if (names.Count == 0) return "";
            if (names.Count == 1) return Vocabulary.Board.Ability(names[0]);

            // "Abilities: X and Y and Z" — mirrors HSA multi-sigil format.
            return Vocabulary.Board.AbilitiesAnd(names);
        }

        // ----------------------------------------------------------------------
        // Returns a cost string like "costs 1 blood", "costs 2 bones", or null
        // if cost is zero (free cards have no cost worth announcing).
        // (Session 8: verb included in the C-key hand read too — players name
        // cards together with their cost.)
        // ----------------------------------------------------------------------
        private static string FormatCost(CardInfo info)
        {
            if (info.BloodCost > 0)
                return Vocabulary.Board.CostsBloodCount(info.BloodCost);
            if (info.BonesCost > 0)
                return Vocabulary.Board.CostsBoneCount(info.BonesCost);
            // Free cards (squirrels etc.) — omit cost entirely.
            return null;
        }

        // ----------------------------------------------------------------------
        // Scale text — reads LifeManager.Balance rather than recomputing the
        // tilt from the cumulative damage counters.
        //
        // Session 7 fix, and the test that settled it was in play, not in
        // theory: a mid-combat scale reset moves what the player can see on the
        // scales while the cumulative counters keep their old totals, so a
        // figure derived from those counters disagreed with the screen. Balance
        // tracked the visible scales through the reset and the derived figure
        // did not, so Balance is what IKMA reports. Announce what is true.
        //
        // Sign convention unchanged: positive is player favour.
        // ----------------------------------------------------------------------
        private static string GetScaleText()
        {
            var lm = LifeManager.Instance;
            if (lm == null) return Vocabulary.Board.GetScaleText;

            return Vocabulary.Scales(lm.Balance);
        }

    }
}
