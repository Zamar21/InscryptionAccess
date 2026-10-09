// PlayConfirmHold.cs
//
// A PLAY CONFIRMATION THAT WAITS FOR THE CARD'S OWN ARRIVAL TRIGGER.
// (0.7.439, Session 43.)
//
// Zamar's log:
//   Pack Rat played in Slot 1.
//   Pack Rat's Trinket Bearer ability triggers, a Harpie's Birdleg Fan is
//   added to item slot 1.
// "Collapse to one line similar to an ETB effect in Magic. 'Pack Rat played
// in Slot 1. Its Trinket Bearer ability triggers, a Harpie's Birdleg Fan is
// added to item slot 1.'"
//
// The confirmation is normally spoken the moment the card is placed. For a
// card with Trinket Bearer it is held here instead, and
// SigilNarrator.ClaimItemCreation takes it and speaks both halves as one
// confirmation when the item appears, about a second later.
//
// HELD ONLY WHEN THE ITEM WILL COME. The game's own test, from
// RandomConsumable.OnResolveOnBoard: fewer consumables than the maximum. With
// a full pack nothing is held and the confirmation is immediate, as before.
// Not held either when play confirmations are switched off - the item line
// then speaks on its own, as before.
//
// IT CANNOT GO UNSAID. If nothing takes it within MaxWait, Tick speaks the
// plain confirmation. Tick runs from CombatAnnouncer.Update every frame.

using UnityEngine;
using DiskCardGame;

namespace IKMA
{
    internal static class PlayConfirmHold
    {
        private static PlayableCard _card;
        private static string _line;
        private static float _heldAt;

        private const float MaxWait = 3f;

        internal static bool TryHold(PlayableCard card, string playedLine)
        {
            try
            {
                if (card == null || string.IsNullOrEmpty(playedLine)) return false;
                if (card.GetComponent<RandomConsumable>() == null) return false;
                if (!(RunState.Run.consumables.Count < RunState.Run.MaxConsumables)) return false;

                bool drop, speak, keep;
                EventSettings.Decide(new EventTag(EventKind.CardPlayed, EventSource.CurrentPlayer),
                                     true, out drop, out speak, out keep);
                if (drop || !speak) return false;

                Release("another card was played");
                _card   = card;
                _line   = playedLine;
                _heldAt = Time.unscaledTime;
                Plugin.Log?.LogInfo("IKMA PLAY: confirmation held - Trinket Bearer is about to add an item.");
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA PLAY: hold - {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>The held confirmation for this card, handed over once.</summary>
        internal static string Take(PlayableCard card)
        {
            if (_line == null || !ReferenceEquals(_card, card)) return null;
            string line = _line;
            _card = null;
            _line = null;
            return line;
        }

        internal static void Tick()
        {
            if (_line == null) return;
            if (Time.unscaledTime - _heldAt < MaxWait) return;
            Release("no item followed");
        }

        private static void Release(string why)
        {
            if (_line == null) return;
            string line = _line;
            _card = null;
            _line = null;
            Plugin.Log?.LogInfo($"IKMA PLAY: held confirmation spoken on its own - {why}.");
            using (Speech.Event(EventKind.CardPlayed, EventSource.CurrentPlayer)) Speech.Confirm(line);
        }
    }
}

// PlayConfirmHold.cs
