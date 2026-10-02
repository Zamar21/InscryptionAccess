// HandArrivalGate.cs
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// Times when a card reaching the hand must NOT be announced on its own,
    /// because a line that already went out speaks for it. (0.7.312.)
    /// </summary>
    /// <remarks>
    /// Two arrival paths exist — EventNarrator's, which names a cause, and
    /// BossNarrator's, which names the card — and both had to be told
    /// separately not to speak. BossNarrator.SuppressNextHandArrival only
    /// works when the richer line is composed FIRST, and Zamar's 0.7.311 log
    /// shows the order can go the other way:
    ///
    ///   HAND: card spawned to hand — 'Wolf Pelt'.      &lt;- already queued
    ///   HAND: arrival suppressed — the trade screen...  &lt;- too late
    ///   SPEAK: Wolf Pelt is added to your hand.
    ///
    /// So the guard belongs where the line is COMPOSED, and it belongs in one
    /// place that both paths ask. A screen or a phase that speaks for its own
    /// cards opens this gate; the arrival paths check it and stay quiet.
    ///
    /// A window rather than a flag with an end, because these phases deal
    /// cards over their own animations and there is no single event that says
    /// "the last one has landed". The reason is logged once per opening so a
    /// gate that swallows something it should not is visible.
    /// </remarks>
    internal static class HandArrivalGate
    {
        private static float _until = -99f;
        private static string _reason;

        private static float Now()
        {
            try { return Time.unscaledTime; } catch { return 0f; }
        }

        internal static void OpenFor(float seconds, string reason)
        {
            _until  = Now() + seconds;
            _reason = reason;
            Plugin.Log?.LogInfo($"IKMA HAND: arrivals silent for {seconds:0.#}s — {reason}.");
        }

        /// <summary>True while something else is speaking for the cards that arrive.</summary>
        // ==================================================================
        // THE DECK PICK'S CARD. (0.7.340.)
        //
        // Zamar's 0.7.339 log, after choosing Ouroboros with Hoarder:
        //   SPEAK: Ouroboros is created in your hand.
        //   SPEAK: Ouroboros is added to your hand.
        // Two lines for one card, the first one false (it came from the deck,
        // nothing was created), and neither cut the card reads still in the
        // air: "Added to hand line should have stomped all the info lines
        // before it. Game action vs info rule."
        //
        // So DeckPickReader claims the card the game took, and whichever of
        // the two arrival paths asks first speaks his approved sentence as a
        // Confirmation; the other is closed. Keyed on the name, and it
        // expires, so it can never swallow an unrelated card.
        // ==================================================================
        private static string _claimName;
        private static float  _claimSetAt = -99f;
        private static float  _claimSpokenAt = -99f;

        internal static void ClaimPick(string cardName)
        {
            if (string.IsNullOrEmpty(cardName)) return;
            HandFollowUps.Reserve(cardName);
            _claimName = cardName;
            _claimSetAt = Now();
            _claimSpokenAt = -99f;
            Plugin.Log?.LogInfo($"IKMA HAND: '{cardName}' is the deck pick — its arrival line is the pick's.");
        }

        private static bool ClaimedByPick(string cardName)
        {
            if (_claimName == null) return false;
            float now = Now();

            bool spoken = _claimSpokenAt > 0f;
            if ((spoken && now - _claimSpokenAt > 3f) || (!spoken && now - _claimSetAt > 10f))
            {
                _claimName = null;
                return false;
            }

            if (cardName != _claimName) return false;

            if (!spoken)
            {
                _claimSpokenAt = now;
                Speech.Confirm(Vocabulary.Events.IsAddedToYour(cardName, HandFollowUps.Take(cardName)));
            }
            else
            {
                Plugin.Log?.LogInfo($"IKMA HAND: '{cardName}' second arrival path — already announced as the deck pick.");
            }
            return true;
        }

        internal static bool Closed(string cardName)
        {
            if (ClaimedByPick(cardName)) return true;
            // The trade screen names every card it hands over, for as long as
            // it is up — no window needed, the game says when it ends.
            try
            {
                if (TradeReader.Armed)
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA HAND: '{cardName}' not announced — the trade screen speaks for its own cards.");
                    return true;
                }
            }
            catch { }

            if (Now() >= _until) return false;

            Plugin.Log?.LogInfo($"IKMA HAND: '{cardName}' not announced — {_reason}.");
            return true;
        }
    }
}

// HandArrivalGate.cs
