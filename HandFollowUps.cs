// HandFollowUps.cs
using System.Collections.Generic;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// Sentences about a card that has not been announced arriving yet.
    /// (0.7.341.)
    /// </summary>
    /// <remarks>
    /// Zamar's 0.7.340 log, the Pack Mule's pack:
    ///   SPEAK: Mealworm gains ability: Fecundity.
    ///   SPEAK: Squirrel, Skunk, Bloodhound and Mealworm are added to your hand.
    /// "card names are added to your hand should have played before Mealworm
    ///  (from that pack) gains Fecundity."
    /// Same shape with Ant Spawner and Fecundity copies: "Worker Ant gains
    /// ability: Fecundity." and "Copy of Fecundity sigil negated by Kaycee
    /// override." both spoken before the line saying the card had arrived.
    ///
    /// WHY IT HAPPENS. The woodcarving applies its sigil when the card is
    /// SPAWNED, and speaks then. The line saying the card arrived is composed
    /// later: the pack waits for the whole pack, and a sigil-made card waits
    /// for the card to reach the hand. So the consequence was always told
    /// before the event.
    ///
    /// THE FIX. The woodcarving files its sentence here instead of speaking.
    /// Whichever line announces the card's arrival takes it and says it
    /// straight after, as one entry. A line that announces an arrival RESERVES
    /// the card's sentences the moment it is queued, so the fallback below
    /// never races it. Anything never claimed is said on its own after three
    /// seconds, so nothing true is ever lost.
    ///
    /// Keyed on the card name, one sentence set per card, first in first out:
    /// two Worker Ants arriving each take their own.
    /// </remarks>
    internal static class HandFollowUps
    {
        private const float UNCLAIMED_AFTER_S = 3f;
        private const float RESERVED_GIVE_UP_S = 15f;

        private class Entry
        {
            internal string Name;
            internal string Line;
            internal float At;
            internal bool Reserved;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        private static float Now()
        {
            try { return Time.unscaledTime; } catch { return 0f; }
        }

        // An arrival line can be queued BEFORE the woodcarving files its
        // sentence (the pack registers each card as it spawns, and the grant
        // lands a moment later), so a reservation can wait for its entry.
        private static readonly List<KeyValuePair<string, float>> _reservations =
            new List<KeyValuePair<string, float>>();

        internal static void Add(string cardName, string line)
        {
            if (string.IsNullOrEmpty(cardName) || string.IsNullOrEmpty(line)) return;

            float now = Now();
            bool reserved = false;
            for (int i = 0; i < _reservations.Count; i++)
            {
                if (now - _reservations[i].Value > RESERVED_GIVE_UP_S) { _reservations.RemoveAt(i); i--; continue; }
                if (_reservations[i].Key != cardName) continue;
                // Several sentences for one card arrive in the same frame and
                // share the reservation; it is spent by Take.
                reserved = true;
                break;
            }

            _entries.Add(new Entry { Name = cardName, Line = line, At = now, Reserved = reserved });
            Plugin.Log?.LogInfo($"IKMA HAND: held for the arrival line of '{cardName}' — \"{line}\"");
        }

        /// <summary>An arrival line for this card has been queued.</summary>
        internal static void Reserve(string cardName)
        {
            if (string.IsNullOrEmpty(cardName)) return;
            foreach (var e in _entries)
                if (!e.Reserved && e.Name == cardName) { e.Reserved = true; return; }
            _reservations.Add(new KeyValuePair<string, float>(cardName, Now()));
        }

        private static void SpendReservation(string cardName)
        {
            for (int i = 0; i < _reservations.Count; i++)
                if (_reservations[i].Key == cardName) { _reservations.RemoveAt(i); return; }
        }

        /// <summary>
        /// The held sentences for ONE card of this name, joined, or "" if
        /// there are none. Called by the arrival line as it is composed.
        /// </summary>
        internal static string Take(string cardName)
        {
            if (string.IsNullOrEmpty(cardName)) return "";
            SpendReservation(cardName);
            if (_entries.Count == 0) return "";

            // All the sentences for the earliest card of this name. A card can
            // gain one sigil and have another negated, and both belong to it.
            float firstAt = -1f;
            foreach (var e in _entries)
                if (e.Name == cardName) { firstAt = e.At; break; }
            if (firstAt < 0f) return "";

            var parts = new List<string>();
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Name != cardName || Mathf.Abs(e.At - firstAt) > 0.05f) continue;
                parts.Add(e.Line);
                _entries.RemoveAt(i);
                i--;
            }
            return parts.Count == 0 ? "" : " " + string.Join(" ", parts.ToArray());
        }

        /// <summary>Every frame. Says anything no arrival line claimed.</summary>
        internal static void Tick()
        {
            if (_entries.Count == 0) return;
            float now = Now();
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                float age = now - e.At;
                bool due = e.Reserved ? age > RESERVED_GIVE_UP_S : age > UNCLAIMED_AFTER_S;
                if (!due) continue;

                Plugin.Log?.LogInfo(
                    $"IKMA HAND: no arrival line took the sentence for '{e.Name}' — said on its own.");
                Speech.Commentary(e.Line);
                _entries.RemoveAt(i);
                i--;
            }
        }
    }
}
