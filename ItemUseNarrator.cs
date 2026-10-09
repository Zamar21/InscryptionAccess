// ItemUseNarrator.cs
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// What an item DID, in Zamar's words, for the items whose effect nothing
    /// else announces. (Session 34, his Session 32 answers.)
    /// </summary>
    /// <remarks>
    /// Four items, three moments:
    ///   Magickal Bleach, Harpie's Birdleg Fan - when the game COMPLETES the
    ///     use (ConsumableItemSlot.CompleteItemActivation, the same hook as
    ///     the item rumble; a cancelled use says nothing).
    ///   Scissors - when the game cuts the card (ScissorsItem.
    ///     OnValidTargetSelected). The game Object.Destroys it and never calls
    ///     Die, so no death line fires on its own - the Skinning Knife's case.
    ///   Hourglass - "when the skip happens, not on use" (his call): at the
    ///     start of the opponent's turn, when the game finds SkipNextTurn set.
    ///     HourglassItem is the only thing in the game that sets it, so
    ///     naming the Hourglass is the game's own cause, not a guess.
    /// </remarks>
    internal static class ItemUseNarrator
    {
        /// <summary>From the CompleteItemActivation prefix; dataName is ItemData.name.</summary>
        internal static void NoteUseCompleted(string dataName)
        {
            string line = null;
            switch (dataName)
            {
                case "BleachPot":  line = Vocabulary.Hotkeys.OpponentCardsOnThe; break;   // his: match the description
                case "BirdLegFan": line = Vocabulary.Hotkeys.YourCardsGainTemporaryAirborne; break;
            }
            if (line == null) return;
            Plugin.Log?.LogInfo($"IKMA ITEM: {dataName} used - effect line spoken.");
            using (Speech.Event(EventKind.ItemUsed, EventSource.CurrentPlayer)) Speech.Result(line);
        }

        /// <summary>
        /// Hourglass. Called from TurnManager_OpponentTurn_Patch (Plugin.cs),
        /// the prefix on TurnManager.OpponentTurn() (NONPUBLIC private
        /// IEnumerator, TurnManager.cs:452). A coroutine prefix runs when it
        /// is CREATED - the start of the opponent's turn - which is when the
        /// game reads Opponent.SkipNextTurn (PUBLIC get) and skips.
        /// </summary>
        internal static void NoteOpponentTurnStarting(TurnManager tm)
        {
            try
            {
                var opp = tm != null ? tm.Opponent : null;
                if (opp == null || !opp.SkipNextTurn) return;
                Plugin.Log?.LogInfo("IKMA ITEM: opponent turn skipped (Hourglass).");
                using (Speech.Event(EventKind.ItemUsed, EventSource.CurrentPlayer)) Speech.Result(Vocabulary.Hotkeys.OpponentTurnSkippedHourglass);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA ITEM: hourglass line - {e.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// Scissors. OnValidTargetSelected(CardSlot target, GameObject) is
    /// NONPUBLIC protected override on ScissorsItem (ScissorsItem.cs:33); the
    /// card is still in the slot at the prefix. Through TryPatch.
    /// </summary>
    public static class ScissorsItem_OnValidTargetSelected_Patch
    {
        public static void Prefix(CardSlot target)
        {
            try
            {
                var card = target != null ? target.Card : null;
                if (card?.Info == null) return;
                string name = CardReader.CardName(card);
                BoardWatcher.NoteAnnounced(card);
                Plugin.Log?.LogInfo($"IKMA ITEM: '{name}' cut by the Scissors - destroyed without dying.");
                using (Speech.Event(EventKind.Death, EventTag.Side(card))) Speech.Result(Vocabulary.Combat.IsCutInHalfAnd(name, card.Info));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA ITEM: scissors line - {e.GetType().Name}");
            }
        }
    }
}
