// ResourceKeys.cs
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// ONE FACT PER KEY. (0.4.8.007, Session 58.)
    ///
    /// The tester: "why A for checking candles and bones?" Zamar: conform to
    /// the other mods - Say the Spire 2 gives each resource its own Ctrl+letter
    /// (Ctrl+H HP, Ctrl+G gold, Ctrl+Y energy). His letters ("Initials"):
    ///
    ///   Ctrl+S scales    Ctrl+B bones    Ctrl+C candles
    ///   Ctrl+W woodcarving    Ctrl+D deck counts    Ctrl+T teeth
    ///
    /// Each says exactly its piece of A's line, in A's words; A stays as the
    /// all-in-one read. Outside an encounter: "Not in an encounter." (his).
    /// Teeth follow the deck view's rule - only where the bowl is on the table.
    /// Runs ahead of every reader, so Ctrl+D never also draws a card.
    /// </summary>
    internal static class ResourceKeys
    {
        internal static bool HandleKeys()
        {
            bool ctrl = KeyIn.Held(KeyCode.LeftControl) || KeyIn.Held(KeyCode.RightControl);
            if (!ctrl) return false;
            if (KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift)) return false;

            KeyCode key;
            if      (KeyIn.Down(KeyCode.S)) key = KeyCode.S;
            else if (KeyIn.Down(KeyCode.B)) key = KeyCode.B;
            else if (KeyIn.Down(KeyCode.C)) key = KeyCode.C;
            else if (KeyIn.Down(KeyCode.W)) key = KeyCode.W;
            else if (KeyIn.Down(KeyCode.D)) key = KeyCode.D;
            else if (KeyIn.Down(KeyCode.T)) key = KeyCode.T;
            else return false;

            try { Speak(key); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA RESOURCE KEY: {key} failed: {e.Message}");
                Speech.BrowseNoFocus(Vocabulary.Board.ResourcesUnavailable);
            }
            return true;
        }

        private static void Speak(KeyCode key)
        {
            if (key == KeyCode.T)
            {
                string teeth = DeckViewReader.CurrencyPart();
                Speech.BrowseNoFocus(string.IsNullOrEmpty(teeth)
                    ? Vocabulary.DeckView.NoTeethAreShown : teeth.Trim());
                return;
            }

            if (!HotkeyManager.IsBattleActive())
            {
                Speech.BrowseNoFocus(Vocabulary.BufferWords.NotInEncounter);
                return;
            }

            string line = "";
            switch (key)
            {
                case KeyCode.S:
                    line = BoardReader.GetScaleText();
                    break;
                case KeyCode.B:
                    var rm = ResourcesManager.Instance;
                    if (rm != null) line = Vocabulary.BoneCount(rm.PlayerBones) + ".";
                    break;
                case KeyCode.C:
                    int mine = BossNarrator.PlayerCandles();
                    if (mine >= 0) line += Vocabulary.Board.CandleLitCount(mine);
                    int theirs = BossNarrator.BossCandles();
                    if (theirs >= 0) line += Vocabulary.Board.EnemyHasCandleCount(theirs);
                    break;
                case KeyCode.W:
                    line = BoardReader.GetWoodcarvingText();
                    if (string.IsNullOrEmpty(line)) line = Vocabulary.BufferWords.NoWoodcarving;
                    break;
                case KeyCode.D:
                    line = BoardReader.GetDeckCountsText();
                    break;
            }
            line = (line ?? "").Trim();
            Speech.BrowseNoFocus(line.Length > 0 ? line : Vocabulary.Board.ResourcesUnavailable);
        }
    }
}
// ResourceKeys.cs
