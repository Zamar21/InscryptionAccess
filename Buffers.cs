// Buffers.cs
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// 0.4.8.008 - the two control templates. Zamar, Session 59: "In the
    /// settings, I want this new one to be labeled Default, and the old one
    /// to be labeled Original. Have them be two control templates you can
    /// switch between." Original brings back, by his pick: Ctrl+Up/Down step
    /// the review history only (newest first, no buffers, Ctrl+Left/Right
    /// free); H speaks the help paragraph (F1 still opens the list); map
    /// Ctrl+arrows are the history (no map preview). Lists stop at their ends
    /// and the Ctrl+letter reads stay in both.
    /// </summary>
    public enum ControlTemplate { Default, Original }

    /// <summary>
    /// THE BUFFERS. (0.4.8.007, Session 58.)
    ///
    /// Zamar, from a beta tester who also writes these mods: "I think we need
    /// to conform to what these other mods are doing and not do our own thing
    /// trying to reinvent this wheel." Say the Spire 2, Monster Train 2,
    /// Guildrun and Darkest Dungeon 2 all use the same four keys, and this is
    /// Say the Spire 2's BufferManager / BufferControls, ported:
    ///
    ///   Ctrl+Up      next line of the current buffer     (pad: LB+RB+D-pad Up)
    ///   Ctrl+Down    previous line                       (LB+RB+D-pad Down)
    ///   Ctrl+Right   next buffer, says "Name: line"      (LB+RB+D-pad Right)
    ///   Ctrl+Left    previous buffer                     (LB+RB+D-pad Left)
    ///
    /// At either end of a buffer the end line is read again (theirs). The
    /// buffer list goes round (theirs - only the arrow LISTS stop at the ends).
    ///
    /// TWO BUFFERS, in their order (focused element first, events last):
    ///   Card     what the player is on. Every read they ask for (Speech.Browse)
    ///            binds it: the line split into sentences, one per row, then
    ///            what each sigil or item named in it does - the lookup Shift+R
    ///            gives, as rows. Theirs binds on every focus change and makes
    ///            it the current buffer (UIManager: SetCurrentBuffer), so the
    ///            first Ctrl+Up after landing on a card reads its next line.
    ///            Zamar's call over his Session 39 rule: "StS2: focused item
    ///            first". Named "Card" by him.
    ///   History  the review history (ReviewHistory.cs), newest first on
    ///            switching to it (their FollowLatest).
    ///
    /// ON THE MAP the same keys are the map preview (theirs: MapScreen claims
    /// the buffer actions) - MapReview.cs. Ctrl+Space is its route summary.
    /// </summary>
    internal static class Buffers
    {
        private const int CARD = 0, HISTORY = 1, COUNT = 2;
        private static int _current = -1;
        private static readonly List<string> _card = new List<string>();
        private static int _cardPos;

        private static readonly Regex Sentence = new Regex(@"(?<=[.!?])\s+(?=\S)");

        // 0.4.8.008 - [Controls] Template. Ctrl+M > Control template, and IKMA
        // Manager's settings (Zamar, Session 38: "Exist in both").
        internal static ConfigEntry<ControlTemplate> Template;

        internal static void BindConfig(ConfigFile config)
        {
            Template = config.Bind("Controls", "Template", ControlTemplate.Default,
                "Default: the 0.4.8.007 controls (buffers on Control plus arrows, map preview, H opens the help list). " +
                "Original: the earlier controls (Control plus Up and Down step the review history, H speaks the help, no map preview).");
        }

        /// <summary>True while the Original control template is chosen.</summary>
        internal static bool Original => Template != null && Template.Value == ControlTemplate.Original;

        private static bool Enabled(int b) =>
            b == CARD ? _card.Count > 0 : b == HISTORY && ReviewHistory.Count > 0;

        private static string Label(int b) =>
            b == CARD ? Vocabulary.BufferWords.Card : Vocabulary.BufferWords.History;

        /// <summary>
        /// A read the player asked for was just spoken: it is what they are on.
        /// Speech.Say (Browse) calls this right after CardReader.Speak, so the
        /// sigils and items latched for that line are this line's.
        /// </summary>
        internal static void BindFocus(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                string said = CardReader.KinWording(PadWords.ForSpeech(text));
                _card.Clear();
                var seen = new HashSet<string>();
                foreach (string part in Sentence.Split(said))
                {
                    string row = part.Trim();
                    if (row.Length > 0 && seen.Add(row)) _card.Add(row);
                }
                foreach (string row in Explanations())
                    if (!string.IsNullOrEmpty(row) && seen.Add(row)) _card.Add(row);
                _cardPos = 0;
                _current = CARD;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA BUFFERS: bind failed: {e.GetType().Name}: {e.Message}");
            }
        }

        // What each sigil / item named in the line does - the same lookup as
        // Shift+R (HotkeyManager.ExplainLastSigils), one row each.
        private static IEnumerable<string> Explanations()
        {
            var rows = new List<string>();
            if (CardReader.LatchedAbilitiesThisLine)
            {
                foreach (var ability in CardReader.LastSpokenAbilities)
                {
                    string described = RulebookReader.DescribeAbility(ability);
                    if (string.IsNullOrEmpty(described)) continue;
                    var source = CardReader.CardForLastSpokenAbility(ability);
                    if (source != null)
                    {
                        string dir = CardReader.DescribeMoveDirection(source);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            string clause = dir.Trim().TrimEnd('.');
                            if (clause.Length > 0)
                            {
                                clause = char.ToLowerInvariant(clause[0]) + clause.Substring(1);
                                string withDir = RulebookReader.DescribeAbility(ability, clause);
                                if (!string.IsNullOrEmpty(withDir)) described = withDir;
                            }
                        }
                    }
                    rows.Add(CardReader.KinWording(described));
                }
            }
            if (CardReader.LatchedItemsThisLine)
            {
                var items = CardReader.LastSpokenItemsIfNewest;
                if (items != null)
                    foreach (var data in items)
                    {
                        string d = HotkeyManager.ItemNameAndDescription(data);
                        if (!string.IsNullOrEmpty(d)) rows.Add(CardReader.KinWording(d));
                    }
            }
            return rows;
        }

        /// <summary>Every frame, after the help list and the history list.</summary>
        internal static bool HandleKeys()
        {
            bool ctrl = KeyIn.Held(KeyCode.LeftControl) || KeyIn.Held(KeyCode.RightControl);
            if (!ctrl) return false;
            if (Original) return OriginalHistoryKeys();
            if (KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift)) return false;

            bool up    = KeyIn.Down(KeyCode.UpArrow);
            bool down  = KeyIn.Down(KeyCode.DownArrow);
            bool left  = KeyIn.Down(KeyCode.LeftArrow);
            bool right = KeyIn.Down(KeyCode.RightArrow);
            bool space = KeyIn.Down(KeyCode.Space);
            if (!(up || down || left || right || space)) return false;

            if (HotkeyManager.MapFocused)
                return MapReview.HandleKeys(up, down, left, right, space);
            if (space) return false;

            if (left || right) SwitchBuffer(right ? 1 : -1);
            else StepLine(up ? 1 : -1);
            return true;
        }

        // 0.4.8.008 - the Original template: the pre-0.4.8.007 history keys,
        // restored as they were (ReviewHistory.HandleKeys, 0.4.8.006). Ctrl+Up
        // newer, Ctrl+Down older; the first press reads the newest line.
        // Ctrl+Left / Right / Space are left to the screen, on the map too.
        private static bool OriginalHistoryKeys()
        {
            bool older = KeyIn.Down(KeyCode.DownArrow);
            bool newer = KeyIn.Down(KeyCode.UpArrow);
            if (!older && !newer) return false;
            string line = ReviewHistory.Step(newer ? 1 : -1);
            Speech.BrowseUnrecorded(string.IsNullOrEmpty(line) ? Vocabulary.HistoryEmpty : line);
            return true;
        }

        // Their MoveToNext / MoveToPrevious: round the list to the next enabled one.
        private static void SwitchBuffer(int direction)
        {
            int start = _current < 0 ? (direction > 0 ? COUNT - 1 : 0) : _current;
            int i = start;
            int found = -1;
            do
            {
                i = (i + (direction > 0 ? 1 : -1) + COUNT) % COUNT;
                if (Enabled(i)) { found = i; break; }
            } while (i != start);

            if (found < 0)
            {
                Speech.BrowseUnrecorded(Vocabulary.BufferWords.NoBuffers);
                return;
            }
            _current = found;
            if (found == HISTORY) ReviewHistory.FollowLatest();
            string item = found == CARD ? _card[_cardPos] : ReviewHistory.Step(0);
            Plugin.Log?.LogInfo($"IKMA BUFFERS: switched to {Label(found)}.");
            Speech.BrowseUnrecorded(string.IsNullOrEmpty(item)
                ? Vocabulary.BufferWords.Empty(Label(found))
                : Vocabulary.BufferWords.Current(Label(found), item));
        }

        // Their NextItem / PreviousItem. At an end, the end line again.
        private static void StepLine(int direction)
        {
            // Theirs: the events buffer becomes current when its first line
            // arrives with nothing else selected.
            if (_current < 0 && Enabled(HISTORY)) _current = HISTORY;
            if (_current < 0 || !Enabled(_current))
            {
                Speech.BrowseUnrecorded(Vocabulary.BufferWords.NoBufferSelected);
                return;
            }

            if (_current == HISTORY)
            {
                string line = ReviewHistory.Step(direction);
                Speech.BrowseUnrecorded(string.IsNullOrEmpty(line)
                    ? Vocabulary.BufferWords.Empty(Label(HISTORY)) : line);
                return;
            }

            int next = _cardPos + direction;
            if (next >= 0 && next < _card.Count) _cardPos = next;
            Speech.BrowseUnrecorded(_card[_cardPos]);
        }
    }
}
// Buffers.cs
