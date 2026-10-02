// GiantCardNarrator.cs
using System.Collections.Generic;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// A card that lies across more than one slot. Leshy's moon and the Pirate
    /// Skull's ship are the two in Kaycee's Mod, and they are the same
    /// mechanism.
    /// </summary>
    /// <remarks>
    /// 0.7.212, Session 21. Lifted out of LeshyNarrator the moment the ship
    /// proved the moon was not a Leshy feature: both are a plain
    /// <c>GiantCard</c> special behaviour whose <c>OnResolveOnBoard</c> writes
    /// ONE PlayableCard into EVERY opponent slot, and both are killed through
    /// their own sequencer's <c>OnOtherCardDie</c>. A giant is a property of
    /// the board, not of whoever put it there, so it reads the same in both
    /// fights and would read the same in a third.
    ///
    /// THE DEFECT THIS EXISTS TO PREVENT. Captured per slot, one moon entered
    /// the board read four times - "Slot 1: the moon, 6/40. Slot 2: the moon,
    /// 6/40..." - four creatures where a sighted player sees one card lying
    /// across the row. Four blockers to plan around, four things to kill, and
    /// a player would build a whole turn on it. The reader
    /// (BoardReader.FormatSlots) and the differ (BoardWatcher.CaptureSide)
    /// now collapse by OBJECT IDENTITY, not by name, so two Squirrels are
    /// still two Squirrels. This file is the line that says so out loud when
    /// the thing arrives.
    ///
    /// NOT gated on the moon, on the ship, on Trait.Giant, or on either
    /// encounter. The rule is about what the board IS.
    /// </remarks>
    public static class GiantCardNarrator
    {
        // ------------------------------------------------------------------
        // IT LANDS.  GiantCard.OnResolveOnBoard()  (PUBLIC override)
        //
        // Composed DEFERRED, and this is the one hook in either boss file
        // that genuinely needs it: the slot fan-out is the BODY of this very
        // coroutine, so at prefix time the card is still sitting in one slot.
        // At speak time it holds four, and the width is COUNTED rather than
        // assumed - a giant covering three would say three.
        // ------------------------------------------------------------------
        internal static void OnResolved(GiantCard giant)
        {
            try
            {
                Plugin.Log?.LogInfo("IKMA GIANT: giant card resolving — line composes at speak time.");

                // 0.7.348 — this line IS the arrival. Zamar's 0.7.347 log had
                // it and then "Enemy The Limoncello is played in slot 1." from
                // the board diff at the next turn boundary.
                try
                {
                    var pc = giant != null ? giant.GetComponent<PlayableCard>() : null;
                    if (pc != null) BoardWatcher.NoteAnnounced(pc);
                }
                catch { }

                var line = new System.Func<string>(delegate
                {
                    try
                    {
                        // SpecialCardBehaviour.PlayableCard is PROTECTED
                        // (SpecialCardBehaviour.cs:7), so it cannot be reached
                        // from here. The property is GetComponent<PlayableCard>()
                        // and nothing more, so this asks the component directly
                        // rather than reflecting a protected getter.
                        PlayableCard card = null;
                        try { card = giant != null ? giant.GetComponent<PlayableCard>() : null; } catch { }
                        if (card == null || card.Info == null) return null;

                        string name = CardReader.CardName(card.Info);
                        if (string.IsNullOrEmpty(name)) return null;

                        var held = SlotsHeldBy(card);


                        // 0.7.348 — board AND queue. Zamar: "across all eight
                        // slots", matching the board read's "Slots 1 to 8".
                        int width = held.Count;
                        try
                        {
                            int board = Singleton<BoardManager>.Instance.OpponentSlotsCopy.Count;
                            if (width == board && board > 1) width = board * 2;
                        }
                        catch { }

                        return Vocabulary.GiantArrives(name, width, card.Attack, card.Health);
                    }
                    catch { return null; }
                });

                // 0.7.348 — the Pirate Skull speaks about his ship first.
                if (PirateSkullNarrator.HoldShipLine(line)) return;

                // 0.7.351 — when the skull's conversation has already been
                // played in an earlier run, the ship scene still comes first.
                try
                {
                    if (TurnManager.Instance?.Opponent is PirateSkullBossOpponent)
                        PirateSkullNarrator.SayShipScene();
                }
                catch { }

                using (Speech.Event(EventKind.Bosses)) Speech.Result(line);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA GIANT: resolve — {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // IT DIES.  LeshyBattleSequencer.OnOtherCardDie(...)
        //           PirateSkullBattleSequencer.OnOtherCardDie(...)
        //
        // Both sequencers answer RespondsToOtherCardDie only for their own
        // giant killed in combat - Leshy tests the card's name for "moon",
        // the Pirate Skull tests Trait.Giant - and both then play their own
        // dialogue about a second later. So this handler needs no test of its
        // own, and must not talk over what follows: it queues as a Result and
        // the game's line lands behind it.
        // ------------------------------------------------------------------
        internal static void OnDied(PlayableCard card)
        {
            try
            {
                string name = null;
                try { name = CardReader.CardName(card?.Info); } catch { }
                if (string.IsNullOrEmpty(name)) return;

                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.GiantDestroyed(name));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA GIANT: death — {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Which opponent slots this one card is occupying, by object
        /// identity - the same test BoardReader and BoardWatcher use to read
        /// it once. Slot numbers are 1-based, as everything spoken is.
        /// </summary>
        internal static List<int> SlotsHeldBy(PlayableCard card)
        {
            var held = new List<int>();
            if (card == null) return held;
            try
            {
                var bm = Singleton<BoardManager>.Instance;
                if (bm == null) return held;

                var slots = bm.OpponentSlotsCopy;
                if (slots == null) return held;

                for (int i = 0; i < slots.Count; i++)
                    if (ReferenceEquals(slots[i]?.Card, card)) held.Add(i + 1);
                return held;
            }
            catch { return held; }
        }
    }

    // Registered by Plugin.TryPatch. No [HarmonyPatch] attribute — CHECK 1
    // fails a class that carries both.

    public static class GiantCard_OnResolveOnBoard_Patch
    {
        public static void Prefix(GiantCard __instance)
            => GiantCardNarrator.OnResolved(__instance);
    }

    public static class LeshyBattleSequencer_OnOtherCardDie_Patch
    {
        public static void Prefix(PlayableCard card)
            => GiantCardNarrator.OnDied(card);
    }

    public static class PirateSkullBattleSequencer_OnOtherCardDie_Patch
    {
        public static void Prefix(PlayableCard card)
            => GiantCardNarrator.OnDied(card);
    }
}

// GiantCardNarrator.cs
