// AnglerNarrator.cs
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The Angler's own fight. His hook already speaks from LeshyNarrator —
    /// the behaviour object is shared — so what is left is his phase two: the
    /// bait buckets, and the sharks that climb out of them.
    /// </summary>
    /// <remarks>
    /// 0.7.215, Session 21. From `AnglerBossOpponent` and
    /// `AnglerBattleSequencer`; the script is `docs/BOSS_SCRIPTS_KM.md`.
    ///
    /// THE HOOK IS NOT HERE. `FishHookGrab` is instantiated by BOTH this boss
    /// and Leshy's Angler mask, so its aim / pull / cancel lines live in
    /// LeshyNarrator where they were first needed and serve both. The actor in
    /// each is asked of the mask, so the same code says "The Angler" in either
    /// fight without knowing which one it is in.
    ///
    /// WHY THE BAIT LINE HANGS ON PlaceBaitSequence AND NOT ON
    /// StartNewPhaseSequence. His phase-two override checks
    /// `HasGrizzlyGlitchPhase(0)` FIRST and, with the Grizzly Bosses challenge
    /// on, yield-breaks into the Grizzly sequence having placed no bait at
    /// all. A line on the phase method would then announce bait that never
    /// arrived — a confidently wrong announcement, which is worse than
    /// silence. PlaceBaitSequence runs only on the path that actually places
    /// bait, so it cannot lie. The Grizzly path has its own line, in
    /// BossNarrator, on the sequence they share.
    ///
    /// WHAT IS DELIBERATELY LOGGED AND NOT SPOKEN: the shark. See OnSharkFromBait.
    /// </remarks>
    public static class AnglerNarrator
    {
        // ------------------------------------------------------------------
        // PHASE 2 — THE BAIT.  AnglerBossOpponent.PlaceBaitSequence() (NONPUBLIC)
        //
        // The board and queue are cleared, then a Bait Bucket is created
        // opposite EVERY slot of yours that holds a card — so the count is
        // knowable before any of it runs, and the prefix reads it while the
        // board is still intact. Same trick as the Prospector's expected
        // strike count.
        //
        // The buckets themselves arrive through CreateCardInSlot and the board
        // differ reports them by name at the next turn boundary. This line is
        // the EVENT, not the inventory: one line for one thing happening.
        // ------------------------------------------------------------------
        internal static void OnBaitPlaced()
        {
            try
            {
                int willPlace = 0;

                // 0.7.437 - the cards on his side right now are the ones the
                // clear takes. The "clears his side" line below speaks for
                // them, so the differ is told not to say each one has left.
                var cleared = new System.Collections.Generic.List<PlayableCard>();
                try
                {
                    var bm = Singleton<BoardManager>.Instance;
                    var opp = bm?.OpponentSlotsCopy;
                    if (opp != null)
                        for (int i = 0; i < opp.Count; i++)
                        {
                            if (opp[i]?.opposingSlot?.Card != null) willPlace++;
                            if (opp[i]?.Card != null) cleared.Add(opp[i].Card);
                        }
                }
                catch { }
                BoardWatcher.SuppressDeparturesOf(cleared, "the Angler cleared his side for the bait");

                string who = BossNarrator.ActorName();

                // THE COUNT IS LOGGED, NOT SPOKEN, FROM 0.7.262. Zamar asked
                // for the bait to be announced after the "Go fish." line, and
                // the board differ already names every bucket by slot at the
                // next turn boundary. So this line reports only the clear, and
                // the count stays here as the check that the differ missed
                // none of them.
                Plugin.Log?.LogInfo(
                    $"IKMA ANGLER: bait phase — {willPlace} bucket(s) will be placed; " +
                    "the board differ names them after the conversation.");

                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.AnglerBaitPhase(who));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA ANGLER: bait — {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // A BAIT BUCKET DIES AND A SHARK TAKES ITS SLOT.
        // AnglerBattleSequencer.OnOtherCardDie(...)  (PUBLIC)
        //
        // MEASURED AND SETTLED, 0.7.261 playtest. The question this method was
        // shipped to answer — does the board differ speak the shark's arrival
        // on its own? — came back YES, in his log, in one line with the death:
        //
        //   "Bait Bucket takes 2 damage and is destroyed.
        //    Great White is played in Slot 2 by Ability: Waterborne."
        //
        // So nothing is spoken here and Vocabulary.AnglerShark stays unused.
        // Adding a line would be the same event twice.
        //
        // WHAT THIS METHOD DOES DO NOW IS STOP THE GUESS IN THAT SENTENCE.
        // "by Ability: Waterborne" is false — Waterborne is the Great White's
        // own combat ability and had nothing to do with it arriving; the
        // Angler's sequencer put it there, in the very method this prefix is
        // attached to. Zamar, 0.7.261: "This was inaccurate. Just remove 'by
        // ability waterborne'. No need to explain why. The puzzle is learning
        // that destroying the bait bucket will play a Great White."
        //
        // SlotArrival infers the cause from the arriving card's single printed
        // ability, which is his Session 14 call and is right for Corpse Eater —
        // a card whose own ability genuinely plays it. The inference is only
        // ever wrong when something OTHER than the card caused the arrival, and
        // that is knowable exactly here, so the fix is to say so rather than to
        // retire the clause everywhere. This prefix fires at enumerator
        // creation, before CreateCardInSlot runs and before the deferred death
        // line composes — confirmed by the order in his log.
        // ------------------------------------------------------------------
        internal static void OnSharkFromBait(PlayableCard card, CardSlot deathSlot)
        {
            try
            {
                int slot = -1;
                try { slot = deathSlot != null ? deathSlot.Index + 1 : -1; } catch { }

                string dead = null;
                try { dead = CardReader.CardName(card?.Info); } catch { }

                // Name the real cause to SlotArrival for this one slot, so the
                // arrival is announced plainly instead of being credited to
                // whatever ability the incoming card happens to print.
                SlotArrival.SuppressInferredCause(deathSlot);

                Plugin.Log?.LogInfo(
                    $"IKMA ANGLER (not spoken): '{dead ?? "bait"}' died in slot {slot}; the " +
                    "sequencer creates a Shark there. The board differ speaks the arrival; " +
                    "its inferred ability clause is suppressed for this slot.");
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA ANGLER: shark — {e.GetType().Name}: {e.Message}");
            }
        }
        // ------------------------------------------------------------------
        // THE GREAT WHITE IS NAMED WHEN IT LANDS. (0.4.8.004, Session 56.)
        //
        // The arrival used to ride on the bait bucket's death line as its
        // arrival clause (SlotArrival.Describe). That only works when the
        // death line composes AFTER the shark is in the slot, and the game
        // waits 0.2 s before CreateCardInSlot. In the Session 56 driver run
        // every death line composed first, so no arrival was spoken at all:
        // Mantis God's strike named "Great White in slot 2" as a target before
        // anything had said a Great White was there, and the board differ
        // only reported "Enemy Great White is played in slots 1, 2, and 3" on
        // the enemy's turn.
        //
        // Now the coroutine is wrapped, and the step after CreateCardInSlot
        // returns speaks the board differ's own line for it (his wording,
        // 0.7.263: "Enemy Bait Bucket is played in slot 3.") and notes the
        // card so the differ does not say it again. The death line's clause is
        // dropped for that slot through SlotArrival.SuppressArrival, so it is
        // one event, one line. If the death line got there first and already
        // said it, the card is noted and nothing more is spoken.
        // ------------------------------------------------------------------
        internal static System.Collections.IEnumerator WrapSharkArrival(
            System.Collections.IEnumerator inner, CardSlot deathSlot)
        {
            bool done = false;
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext()) break;
                    current = inner.Current;
                }
                catch (System.Exception e)
                {
                    Plugin.Log?.LogWarning($"IKMA ANGLER: shark coroutine threw {e.GetType().Name}: {e.Message}");
                    yield break;
                }
                if (!done) done = TrySpeakSharkArrival(deathSlot);
                yield return current;
            }
            if (!done) TrySpeakSharkArrival(deathSlot);
        }

        private static bool TrySpeakSharkArrival(CardSlot slot)
        {
            try
            {
                if (slot == null) return true;
                var card = BoardReader.LiveCard(slot);
                if (card?.Info == null) return false;
                if (BoardWatcher.IsAnnounced(card))
                {
                    Plugin.Log?.LogInfo("IKMA ANGLER: shark arrival already spoken by the death line.");
                    return true;
                }
                SlotArrival.SuppressArrival(slot);
                BoardWatcher.NoteAnnounced(card);
                string line = Vocabulary.BoardChanges.IsPlayedInSlot(
                    Vocabulary.BoardChanges.Possessive(false),
                    CardReader.CardName(card), slot.Index + 1, "");
                Plugin.Log?.LogInfo($"IKMA ANGLER: shark landed in slot {slot.Index + 1} - \"{line}\"");
                using (Speech.Event(EventKind.Bosses)) Speech.Result(line);
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA ANGLER: shark arrival - {e.GetType().Name}: {e.Message}");
                return true;
            }
        }
    }


    // Registered by Plugin.TryPatch. No [HarmonyPatch] attribute — CHECK 1
    // fails a class that carries both.

    public static class AnglerBossOpponent_PlaceBaitSequence_Patch
    {
        public static void Prefix() => AnglerNarrator.OnBaitPlaced();
    }

    public static class AnglerBattleSequencer_OnOtherCardDie_Patch
    {
        public static void Prefix(PlayableCard card, CardSlot deathSlot)
            => AnglerNarrator.OnSharkFromBait(card, deathSlot);
    }

    public static class AnglerBattleSequencer_OnOtherCardDie_Arrival_Patch
    {
        public static void Postfix(CardSlot deathSlot, ref System.Collections.IEnumerator __result)
            => __result = AnglerNarrator.WrapSharkArrival(__result, deathSlot);
    }
}

// AnglerNarrator.cs
