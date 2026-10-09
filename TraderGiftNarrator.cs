// TraderGiftNarrator.cs
//
// THE TRADER WITH NO PELTS. (0.7.455, Session 47.)
//
// Zamar: "The trader generously gives you five teeth..."
//
// A player who reaches the Trader with no pelts is given teeth instead.
// TradePeltsSequencer.NoPeltsSequence (NONPUBLIC, private IEnumerator, one
// bool) plays the Trader's lines, then yields CurrencyBowl.ShowGain(5, ...)
// (PUBLIC IEnumerator; int amount first) and adds 5 to the run's currency.
// IKMA said nothing: Leshy said "I MUST GIVE YOU THIS." and the teeth were
// only heard about later, from A at the Trapper.
//
// Two prefixes. The first marks that the no-pelts sequence has begun; the
// second runs when the bowl's gain is created - after the Trader's first
// lines, before his last - and speaks the line once, with the game's amount.
// ShowGain is also how teeth arrive after a battle, which has its own line,
// so the second prefix speaks only inside the first one's window.
//
// Both are registered through Plugin.TryPatch, with no HarmonyPatch attribute.
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    internal static class TraderGiftNarrator
    {
        private static float _noPeltsAt = -9999f;

        // The Trader's first lines wait on the player's Space, so the window
        // is long. It is spent by the first gain.
        private const float WINDOW_SECONDS = 600f;

        internal static void NoteNoPelts()
        {
            _noPeltsAt = Time.unscaledTime;
            Plugin.Log?.LogInfo("IKMA TRADER: no pelts - the Trader's gift of teeth will be spoken when the bowl shows it.");
        }

        internal static void NoteGain(int amount)
        {
            if (Time.unscaledTime - _noPeltsAt > WINDOW_SECONDS) return;
            _noPeltsAt = -9999f;

            if (amount <= 0) return;

            Plugin.Log?.LogInfo($"IKMA TRADER: the bowl gains {amount} teeth.");
            string line = Vocabulary.NodeScreens.TraderGivesTeeth(amount);
            using (Speech.Event(EventKind.Teeth)) Speech.Result(() => line);
        }
    }

    public class TradePeltsSequencer_NoPeltsSequence_Patch
    {
        static void Prefix() => TraderGiftNarrator.NoteNoPelts();
    }

    public class CurrencyBowl_ShowGain_Patch
    {
        static void Prefix(int amount) => TraderGiftNarrator.NoteGain(amount);
    }
}
