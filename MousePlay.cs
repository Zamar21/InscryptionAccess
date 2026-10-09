// MousePlay.cs
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// A CARD PLACED WITHOUT IKMA'S KEYS. (Session 51, 0.7.463.)
    ///
    /// "X played in Slot N." was spoken by HotkeyManager.ConfirmSelection,
    /// which is IKMA's own Enter (keyboard or pad). A card put down with the
    /// mouse never went through it, so it got no line at all (found with the
    /// test driver, Session 48). Zamar, asked whether that matters: "Probably
    /// should have it yes."
    ///
    /// PlayerHand.PlayCardOnSlot is the game's own "this card goes on this
    /// slot". PUBLIC, declared on PlayerHand, called from three places:
    /// PlayerHand.SelectSlotForCard (the player chose a slot), CorpseEater
    /// and SpawnLice (an ability plays the card). Only the first is a play
    /// the player made, and it is the only one made while
    /// PlayerHand.ChoosingSlotCard is that card - so that is the test. The
    /// ability plays keep the lines they already have.
    ///
    /// A prefix on a coroutine runs when the enumerator is CREATED, which is
    /// the moment the game accepted the slot: the same moment IKMA's own
    /// line is spoken for a keyboard placement.
    /// </summary>
    // Registered through Plugin.TryPatch. No HarmonyPatch attribute.
    public static class PlayerHand_PlayCardOnSlot_Patch
    {
        public static void Prefix(PlayerHand __instance, PlayableCard card, CardSlot slot)
        {
            try
            {
                if (__instance == null || card == null || slot == null) return;
                if (!ReferenceEquals(__instance.ChoosingSlotCard, card)) return;

                // IKMA pressed this one and has already said so.
                if (HotkeyManager.TakeInjectedPlacement(card)) return;

                string cardName = Vocabulary.Hotkeys.CardOrLowercaseCard(CardReader.CardName(card.Info));
                int slotNumber = slot.Index + 1;

                Plugin.Log?.LogInfo(
                    $"IKMA PLAY: '{cardName}' placed on slot {slotNumber} without IKMA's keys (the mouse) - confirmation spoken.");
                Speech.NotePlayerActed("a slot was chosen with the mouse");
                SlotPromptState.NotePlaced();

                string playedLine = Vocabulary.Hotkeys.PlayedInSlot(cardName, slotNumber);
                if (!PlayConfirmHold.TryHold(card, playedLine))
                    using (Speech.Event(EventKind.CardPlayed, EventSource.CurrentPlayer)) Speech.Confirm(playedLine);

                BoardWatcher.NoteAnnounced(card);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA PLAY: mouse placement line failed: {e.GetType().Name}.");
            }
        }
    }
}
// MousePlay.cs
