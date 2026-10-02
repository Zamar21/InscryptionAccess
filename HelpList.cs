// HelpList.cs
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// HELP AS A LIST. (Session 37, MASTER_PLAN M9.)
    ///
    /// Say the Spire 2's, by Zamar's rule (Session 37): "Match their
    /// implementation and choices instead of asking me for my opinion."
    /// Theirs is UI/Screens/HelpScreen.cs + Help/HelpScreenBuilder.cs:
    ///   - F1 opens it anywhere (their pad chord is LT+Back; IKMA's hold
    ///     button is LB, so LB+View). H / View keep IKMA's spoken paragraph.
    ///   - Rows: the screen's own explanation first, then this screen's
    ///     controls, then the controls that work everywhere; no row twice.
    ///   - Opening names the list ("Help") and reads the first row; every
    ///     row ends with its position, "N of M".
    ///   - Up/Down move, Home/End jump, nothing happens at either end.
    ///     Session 38, Zamar: Left = Up and Right = Down, as Mod Settings.
    ///   - Enter does nothing (their list does not run keys).
    ///   - Backspace, Escape or F1 closes it and says "Closed".
    ///
    /// WHERE THE ROWS COME FROM. The screen's rows are the sentences of the
    /// same help IKMA speaks on H - Zamar's own words, verbatim, one per row
    /// - taken by pressing H for the player with every spoken line captured
    /// instead of said (Speech.Say asks Capture first). So every screen that
    /// has H help has a help list, and the two can never drift apart. The
    /// global rows (M9's keys) are appended after. Their wording is
    /// PROVISIONAL: Say the Spire 2's row shape "action, keys", with IKMA
    /// Manager's action names.
    /// </summary>
    internal static class HelpList
    {
        private static readonly List<string> _rows = new List<string>();
        private static bool _open;
        private static int _focus;

        // The capture: F1 presses H for the player and keeps what H says.
        private static bool _capturing;
        private static int _captureStartFrame;
        private static readonly List<string> _captured = new List<string>();

        internal static bool IsOpen => _open;
        internal static bool Capturing => _capturing;

        /// <summary>
        /// True while a line should be kept for the help list instead of
        /// spoken. Speech.Say calls this for every line.
        /// </summary>
        internal static bool Capture(string text)
        {
            if (!_capturing) return false;
            if (!string.IsNullOrEmpty(text)) _captured.Add(text);
            return true;
        }

        /// <summary>Every frame, ahead of every reader but the settings menu.</summary>
        internal static bool HandleKeys()
        {
            if (_capturing)
            {
                // The frame H was pressed on belongs to the readers; the list
                // opens once they have spoken (or after a few quiet frames).
                int frames = Time.frameCount - _captureStartFrame;
                if (frames < 2) return false;
                if (_captured.Count == 0 && frames < 6) return false;
                _capturing = false;
                Build();
                _open = true;
                _focus = 0;
                Plugin.Log?.LogInfo($"IKMA HELP LIST: opened, {_rows.Count} row(s).");
                Speak(true);
                return true;
            }

            bool f1 = KeyIn.Down(KeyCode.F1);

            if (!_open)
            {
                if (!f1) return false;
                _captured.Clear();
                _capturing = true;
                _captureStartFrame = Time.frameCount;
                KeyIn.Synthesize(KeyCode.H);
                return true;
            }

            if (f1 || KeyIn.Down(KeyCode.Backspace) || KeyIn.Down(KeyCode.Escape))
            {
                _open = false;
                Plugin.Log?.LogInfo("IKMA HELP LIST: closed.");
                Speech.Browse(Vocabulary.ModSettings.Closed);
                return true;
            }

            int n = _rows.Count;
            // Session 38, Zamar: "left and up go up and right and down go down".
            bool next = KeyIn.Down(KeyCode.DownArrow) || KeyIn.Down(KeyCode.RightArrow);
            bool prev = KeyIn.Down(KeyCode.UpArrow)   || KeyIn.Down(KeyCode.LeftArrow);
            if      (next) { if (_focus < n - 1) { _focus++; Speak(false); } }
            else if (prev) { if (_focus > 0)     { _focus--; Speak(false); } }
            else if (KeyIn.Down(KeyCode.Home))      { _focus = 0;     Speak(false); }
            else if (KeyIn.Down(KeyCode.End))       { _focus = n - 1; Speak(false); }
            // Enter and every other key: the list's, and nothing happens.
            return true;
        }

        private static void Speak(bool withListName)
        {
            if (_rows.Count == 0) return;
            // Theirs joins with ", " and drops trailing punctuation.
            string row = _rows[_focus].TrimEnd('.');
            string pos = Vocabulary.ModSettings.Position(_focus + 1, _rows.Count);
            Speech.Browse(withListName
                ? Vocabulary.ModSettings.Join(Vocabulary.HelpListWords.Help, row, pos)
                : Vocabulary.ModSettings.Join(row, pos));
        }

        private static readonly Regex Sentence = new Regex(@"(?<=[.!?])\s+(?=\S)");

        private static void Build()
        {
            _rows.Clear();
            var seen = new HashSet<string>();
            foreach (string line in _captured)
                foreach (string part in Sentence.Split(line))
                {
                    string row = part.Trim();
                    if (row.Length > 0 && seen.Add(row)) _rows.Add(row);
                }
            foreach (string row in Vocabulary.HelpListWords.Everywhere())
                if (seen.Add(row)) _rows.Add(row);
        }
    }
}
// HelpList.cs
