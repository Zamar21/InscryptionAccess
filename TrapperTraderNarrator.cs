// TrapperTraderNarrator.cs
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The Trapper, who becomes the Trader halfway through. His trade phase
    /// already speaks from LeshyNarrator — the behaviour object is shared —
    /// so what is left is the turn: the board clearing, the pelts coming back
    /// to your hand, and the mask FLIPPING rather than going on.
    /// </summary>
    /// <remarks>
    /// 0.7.215, Session 21. From `TrapperTraderBossOpponent`; the script is
    /// `docs/BOSS_SCRIPTS_KM.md`.
    ///
    /// THE MASK FLIP IS THE POINT OF THIS FILE, and it is registered from
    /// here rather than left in BossNarrator because this is the only fight
    /// in the game that uses it.
    ///
    /// `LeshyAnimationController.FlipMask(Mask)` is a DIFFERENT method from
    /// `PutOnMask` — it swaps the face he is already wearing for another one.
    /// docs/COVERAGE_KM.md flagged exactly this ("FlipMask is not PutOnMask —
    /// check attribution") and it was real: nothing in IKMA touched it, so
    /// when the Trapper became the Trader the actor stayed "The Trapper" for
    /// the whole of phase two, including the trade. Every line about the
    /// character running the trade named the character who had already left.
    ///
    /// It routes to the same handler as PutOnMask, keyed on the same Mask
    /// enum, so a description Zamar writes for the Trader is spoken whether
    /// the mask is put on at a map node, taken off the orbiter by Leshy, or
    /// flipped to here.
    ///
    /// WHY THE PELT LINE HANGS ON ClearBoardAndReturnPlayedPelts AND NOT ON
    /// StartNewPhaseSequence: same reason as the Angler's bait. The phase
    /// override checks `HasGrizzlyGlitchPhase(1)` first and yield-breaks into
    /// the Grizzly sequence with no board clear and no pelts returned. A line
    /// on the phase method would announce a thing that did not happen.
    /// </remarks>
    public static class TrapperTraderNarrator
    {
        // ------------------------------------------------------------------
        // PHASE 2 OPENS.
        // TrapperTraderBossOpponent.ClearBoardAndReturnPlayedPelts() (NONPUBLIC)
        //
        // Everything of his leaves the board, the queue is cleared, and then
        // every PELT you had played comes back to your HAND — one at a time,
        // through PlayerHand.AddCardToHand.
        //
        // The count is read at enumerator creation, while the board is still
        // intact, which is the only moment it can be read at all: by the time
        // the coroutine ends the pelts are in hand and the board is empty.
        //
        // MEASURE-BEFORE-OPTIMISING NOTE, and it is why the log line below is
        // as specific as it is. PlayerHand.AddCardToHand is already patched,
        // so each returning pelt may well announce itself — and if it
        // announces as a DRAW ("added to your hand") that is a false
        // statement: these are not drawn, they are given back. The doc raised
        // this and nobody has heard it yet. One line naming the event ships
        // now; whether the per-card lines that follow need the Session 14
        // DrawInFlight treatment is a question the first playtest answers,
        // not this file.
        // ------------------------------------------------------------------
        internal static void OnPeltsReturned()
        {
            try
            {
                int pelts = 0;
                try
                {
                    var bm = Singleton<BoardManager>.Instance;
                    var mine = bm?.PlayerSlotsCopy;
                    if (mine != null)
                        for (int i = 0; i < mine.Count; i++)
                        {
                            var card = BoardReader.LiveCard(mine[i]);
                            // The game's own test — Trait.Pelt, not a name.
                            if (card?.Info != null && card.Info.HasTrait(Trait.Pelt)) pelts++;
                        }
                }
                catch { }

                string who = BossNarrator.ActorName();


                // THE SWEEP LINE ALREADY COUNTS THE PELTS. (0.7.312.)
                //
                // Zamar's 0.7.311 log:
                //
                //   SPEAK: The Trapper sweeps their side of the table clean,
                //          and one played pelt returns to your hand.
                //   SPEAK: Wolf Pelt is created in your hand.
                //
                // His verdict: "Didnt need both these wolf pelt callouts for
                // returning and creating. That's confusing cause I only end
                // up with 1." One pelt, two sentences — the count in the
                // first line is the whole story, and the second reads as a
                // second pelt.
                //
                // A WINDOW, because the pelts arrive one at a time over the
                // sweep's own animation rather than in the same frame as this
                // line. Sized to the sweep, not guessed at: if a pelt ever
                // lands after it, the window is what to widen.
                HandArrivalGate.OpenFor(3f, "the Trapper's sweep line counts the pelts");

                // AND NOTHING FROM PHASE ONE IS ANNOUNCED AS LEAVING.
                // (0.7.314.) The sweep line says the table was cleared; the
                // differ naming every card that was on it repeats one event
                // once per card. His words: "Nothing from phase 1 needs to be
                // called out here."
                BoardWatcher.SuppressDeparturesFor(4f, "the Trapper's sweep cleared the board");

                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.TrapperPhaseTwo(who, pelts));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA TRAPPER: phase 2 — {e.GetType().Name}: {e.Message}");
            }
        }
    }

    // Registered by Plugin.TryPatch. No [HarmonyPatch] attribute — CHECK 1
    // fails a class that carries both.

    public static class TrapperTraderBossOpponent_ClearBoardAndReturnPlayedPelts_Patch
    {
        public static void Prefix() => TrapperTraderNarrator.OnPeltsReturned();
    }

    // THE MASK FLIPS. Routed to the same handler as PutOnMask so one mask has
    // one description wherever it appears. (0.7.215.)
    public static class LeshyAnimationController_FlipMask_Patch
    {
        public static void Prefix(LeshyAnimationController.Mask mask)
            => BossNarrator.AnnounceMaskFlipped(mask.ToString());
    }
}

// TrapperTraderNarrator.cs
