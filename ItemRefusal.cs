// ItemRefusal.cs
using DiskCardGame;
using HarmonyLib;

namespace IKMA
{
    /// <summary>
    /// WHY AN ITEM WAS REFUSED, WHEN LESHY DOES NOT SAY. (Session 37.)
    ///
    /// Since 0.7.395 IKMA presses a refused item the game's way, so the game
    /// answers: ConsumableItemSlot.OnCursorSelectStart plays one of Leshy's
    /// hints (HintsHandler, dumps\dump_items_cannot_use_s34.txt) - but only on
    /// the first try and every Nth after (Hint.TryPlayDialogue), and the
    /// Birdleg Fan has no hint at all. Zamar, Session 37: "Give the reason.
    /// 'Birdleg Fan already used this turn.'"
    ///
    /// So: a prefix on Hint.TryPlayDialogue notes which hint the press asked
    /// for and whether Leshy will actually say it. When he will, IKMA says
    /// nothing (his voice is the answer). When he will not, IKMA says the
    /// reason that hint stands for. With no hint at all, the reason is read
    /// from the game's own state: the draw phase, the fan already used this
    /// turn (its temporary "bird_leg_fan" mods are on your cards), or "not now".
    /// Every reason line except the fan's is PROVISIONAL.
    /// </summary>
    internal static class ItemRefusal
    {
        private static string _hintId;
        private static bool _hintPlayed;
        private static int _hintFrame = -1;

        private static readonly AccessTools.FieldRef<HintsHandler.Hint, int> _attempts =
            AccessTools.FieldRefAccess<HintsHandler.Hint, int>("playDialogueAttempts");

        /// <summary>Harmony prefix on HintsHandler.Hint.TryPlayDialogue.</summary>
        internal static void NoteHint(HintsHandler.Hint hint)
        {
            try
            {
                int attempts = _attempts(hint);
                _hintId = hint.dialogueId;
                _hintPlayed = hint.playDialogueFrequency > 0
                              && attempts % hint.playDialogueFrequency == 0
                              && Singleton<TextDisplayer>.Instance != null;
                _hintFrame = UnityEngine.Time.frameCount;
            }
            catch { _hintId = null; _hintFrame = -1; }
        }

        /// <summary>
        /// Called right after IKMA pressed an item the game refused. The game
        /// answered inside that press, so the hint (if any) was noted this frame.
        /// </summary>
        internal static void AfterRefusedPress(object consumable, string name)
        {
            bool hintNow = _hintFrame == UnityEngine.Time.frameCount;
            if (hintNow && _hintPlayed)
            {
                Plugin.Log?.LogInfo($"IKMA ITEM: {name} refused - Leshy says why ({_hintId}).");
                return;
            }

            string line = hintNow ? ReasonForHint(_hintId, name) : ReasonFromState(consumable, name);
            Plugin.Log?.LogInfo($"IKMA ITEM: {name} refused, Leshy silent ({(hintNow ? _hintId : "no hint")}) - IKMA says the reason.");
            Speech.Browse(line);
        }

        private static string ReasonForHint(string id, string name)
        {
            switch (id)
            {
                case "Hint_ConsumableOutsideOfBattle": return Vocabulary.ItemRefusals.OutsideBattle(name);
                case "Hint_ConsumableDuringDrawPhase": return Vocabulary.ItemRefusals.DrawPhase(name);
                case "Hint_MagnifyingGlassNoCards":    return Vocabulary.ItemRefusals.DeckEmpty(name);
                case "Hint_PocketWatchGiantCard":      return Vocabulary.ItemRefusals.GiantCard(name);
                case "Hint_ScissorsNoTarget":
                case "Hint_FishhookNoTarget":
                case "Hint_BleachPotNoTargets":
                case "Hint_PocketWatchNoCards":        return Vocabulary.ItemRefusals.NoTargets(name);
                default:                               return Vocabulary.ItemRefusals.NotNow(name);
            }
        }

        private static string ReasonFromState(object consumable, string name)
        {
            try
            {
                var tm = Singleton<TurnManager>.Instance;
                if (tm != null && tm.IsPlayerDrawPhase) return Vocabulary.ItemRefusals.DrawPhase(name);

                if (consumable is BirdLegFanItem)
                {
                    if (FanUsedThisTurn()) return Vocabulary.ItemRefusals.AlreadyUsedThisTurn(name);
                    return Vocabulary.ItemRefusals.NoTargets(name);
                }
            }
            catch { }
            return Vocabulary.ItemRefusals.NotNow(name);
        }

        // The fan gives each of your cards a temporary Airborne mod with the
        // singletonId "bird_leg_fan" (BirdLegFanItem.AddFlyingMods), removed at
        // your next upkeep. While any of yours carries it, the fan was used
        // this turn.
        private static bool FanUsedThisTurn()
        {
            var bm = Singleton<BoardManager>.Instance;
            if (bm == null) return false;
            foreach (var card in bm.CardsOnBoard)
            {
                if (card == null || card.OpponentCard) continue;
                foreach (var mod in card.TemporaryMods)
                    if (mod != null && mod.singletonId == "bird_leg_fan") return true;
            }
            return false;
        }
    }

    internal static class HintsHandler_Hint_TryPlayDialogue_Patch
    {
        static void Prefix(HintsHandler.Hint __instance) => ItemRefusal.NoteHint(__instance);
    }
}
// ItemRefusal.cs
