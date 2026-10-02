// ReviewHistory.cs
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// The review history: a scroll-back list of the game events IKMA has
    /// spoken, stepped through with Ctrl+Down (older) and Ctrl+Up (newer), or
    /// on the pad by holding LB and RB together with D-pad Down / Up.
    /// </summary>
    /// <remarks>
    /// Session 35, 0.7.398. MANDATORY FOR v0.5 (MASTER_PLAN M9). Zamar's rule
    /// for how it works: do what most of the other accessibility mods do,
    /// with Say the Spire 2 as the north star (docs/LESSONS_FROM_OTHER_MODS.md).
    ///
    /// WHAT GOES IN - game events only (Zamar: "only game events"; 5 of the 6
    /// mods with a history agree). A line is recorded at the moment it is
    /// actually handed to the screen reader, from Speech.cs, by its
    /// Provenance - the same label that already decides whether it
    /// interrupts, so the history and the speech policy cannot disagree:
    ///   recorded      Result, Commentary (both through the queue),
    ///                 Confirmation (the answer to a press that changed the
    ///                 game), Alert (the challenge overlay).
    ///   not recorded  Browse (menu and board reads - the player can read
    ///                 those again), Prompt (what to press), Quiet (the
    ///                 version line), Dialogue (Say the Spire 2 leaves
    ///                 dialogue out by default; it becomes an option with
    ///                 the per-announcement settings, M9).
    ///
    /// HOW IT MOVES - Say the Spire 2's events buffer: it starts at the newest
    /// line; Down goes back in time, Up comes forward (Accessible Arena agrees);
    /// at either end the edge line is read again. Reading the history is a
    /// Browse, so it interrupts and is never itself recorded.
    ///
    /// Y OPENS IT AS A LIST - Hearthstone Access's key ("Open the play
    /// history log: y"; Zamar: where HSA has a feature, match it too). IKMA's
    /// keyboard layout already follows HSA. While open it is a list with
    /// HSA's vertical-list keys: Up / Down, Home / End, Shift+Up re-reads;
    /// Backspace, Escape or Y closes. Newest first, Down is older - the same
    /// direction as Ctrl+Down, and Accessible Arena's order. Nothing else
    /// hears a key while it is open.
    ///
    /// NEW LINES WHILE BROWSING. Say the Spire 2 keeps the reading position
    /// when a line is added. So here: a player reading at the newest line
    /// stays "at the newest" (the next press reads the new one); a player
    /// who has scrolled back keeps their place.
    /// </remarks>
    /// <remarks>
    /// SESSION 38 - EVERYTHING YOU HEAR. Zamar: "by default everything I hear
    /// spoken I want in the log." His answers: reads you asked for (cards,
    /// board, menus, map, rulebook) and prompts (what to press, idle
    /// reminders, help) go in, each with its own switch in Mod Settings >
    /// History; dialogue goes in (its Events switch now defaults on); mod
    /// messages (version loaded, Saving, Loading, update lines) stay out;
    /// reading the history itself never goes in. "Only count once for any
    /// identical lines": a line identical to the newest one is not added again.
    /// This replaces Session 35's "only game events" rule above.
    /// </remarks>
    internal static class ReviewHistory
    {
        // Session 38: the two new switches ([History] in the cfg).
        internal static ConfigEntry<bool> KeepReads;
        internal static ConfigEntry<bool> KeepPrompts;

        internal static void BindConfig(ConfigFile config)
        {
            KeepReads   = config.Bind("History", "Reads", true,
                "Keep the reads you asked for (cards, board, menus, map, rulebook) in the review history.");
            KeepPrompts = config.Bind("History", "Prompts", true,
                "Keep prompts (what to press, idle reminders, help) in the review history.");
        }

        internal static bool ReadsOn   => KeepReads   == null || KeepReads.Value;
        internal static bool PromptsOn => KeepPrompts == null || KeepPrompts.Value;

        /// <summary>
        /// Dialogue's "Add to history" switch (Events > Other > Dialogue),
        /// for a dialogue line that carries no event tag.
        /// </summary>
        internal static bool DialogueOn
        {
            get
            {
                var d = EventSettings.Get(EventKind.Dialogue);
                return d == null || d.History == null || d.History.Value;
            }
        }

        /// <summary>
        /// A line was spoken; keep it when <paramref name="keep"/>. A line
        /// marked NotEvent is consumed either way, so a mark never outlives
        /// the line it was made for.
        /// </summary>
        internal static void Consider(string text, bool keep)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_notEvents.Remove(text.Trim())) return;
            if (keep) Add(text);
        }

        // Enough for several fights. The oldest line drops off the front.
        private const int CAPACITY = 500;

        private static readonly List<string> _lines = new List<string>();

        // Index into _lines of the line last read, or -1 = not browsing yet
        // (the next Ctrl+Down reads the newest line).
        private static int _position = -1;

        // Lines that ride the Commentary queue for timing reasons but are not
        // game events: idle prompts, help, "Saving." Marked by the code that
        // composes them (NotEvent) and skipped once when they are spoken.
        private static readonly HashSet<string> _notEvents = new HashSet<string>();

        /// <summary>
        /// Wraps a line that is spoken through the queue but is not a game
        /// event, so the history skips it. Returns the line unchanged.
        /// Found with the test driver, Session 35: the map's idle prompt and
        /// "Saving." were landing in the history.
        /// </summary>
        internal static string NotEvent(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                if (_notEvents.Count > 50) _notEvents.Clear();
                _notEvents.Add(text.Trim());
            }
            return text;
        }

        // Session 38: lines that ride the Commentary queue for timing but are
        // prompts (idle reminders, help, a screen's opening line). Kept only
        // while History > Prompts is on. Were NotEvent until Session 38.
        private static readonly HashSet<string> _promptMarks = new HashSet<string>();

        /// <summary>Marks a queued line as a prompt. Returns it unchanged.</summary>
        internal static string AsPrompt(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                if (_promptMarks.Count > 50) _promptMarks.Clear();
                _promptMarks.Add(text.Trim());
            }
            return text;
        }

        /// <summary>Record a spoken game event. Main thread.</summary>
        internal static void Add(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.Trim();
            if (text.Length == 0) return;
            if (_notEvents.Remove(text)) return;
            if (_promptMarks.Remove(text) && !PromptsOn) return;
            // Session 38, Zamar: "only count once for any identical lines".
            if (_lines.Count > 0 && _lines[_lines.Count - 1] == text) return;

            bool following = _position < 0 || _position == _lines.Count - 1;
            _lines.Add(text);
            if (_lines.Count > CAPACITY)
            {
                _lines.RemoveAt(0);
                if (_position > 0) _position--;
            }
            // 0.7.423, Zamar: "When pressing Control Up, it should always
            // start you on the most recent history log." He had scrolled
            // back once, played on, and then had to step through everything
            // spoken since. So a new line ends a Ctrl+Up / Ctrl+Down browse:
            // the next press reads the newest. The Y list keeps its place
            // while it is open (it always opens on the newest).
            if (following || !_open) _position = -1;
        }

        /// <summary>
        /// Ctrl+Up / Ctrl+Down (the pad's LB+RB + D-pad arrive here as the
        /// same keys through KeyIn). True when the press was the history's,
        /// so HotkeyManager ends the frame and no other reader sees an arrow.
        /// </summary>
        internal static bool HandleKeys()
        {
            if (_open) return HandleOpenList();

            bool shiftHeld = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);
            bool ctrlHeld  = KeyIn.Held(KeyCode.LeftControl) || KeyIn.Held(KeyCode.RightControl);
            if (!shiftHeld && !ctrlHeld && KeyIn.Down(KeyCode.Y))
            {
                if (_lines.Count == 0) { Speech.BrowseUnrecorded(Vocabulary.HistoryEmpty); return true; }
                _open = true;
                _position = _lines.Count - 1;
                Speech.BrowseUnrecorded(Vocabulary.HistoryOpened(_lines[_position]));
                return true;
            }

            if (!ctrlHeld) return false;

            bool older = KeyIn.Down(KeyCode.DownArrow);
            bool newer = KeyIn.Down(KeyCode.UpArrow);
            if (!older && !newer) return false;

            if (_lines.Count == 0)
            {
                Speech.BrowseUnrecorded(Vocabulary.HistoryEmpty);
                return true;
            }

            int last = _lines.Count - 1;
            if (_position < 0 || _position > last)
                _position = last;                                   // at the newest: read it
            else if (older)
                _position = System.Math.Max(0, _position - 1);      // at the oldest: read it again
            else
                _position = System.Math.Min(last, _position + 1);   // at the newest: read it again

            Speech.BrowseUnrecorded(_lines[_position]);
            return true;
        }

        private static bool _open;

        internal static bool IsOpen => _open;

        /// <summary>
        /// The list opened with Y. Every key is the list's while it is open.
        /// </summary>
        private static bool HandleOpenList()
        {
            if (KeyIn.Down(KeyCode.Backspace) || KeyIn.Down(KeyCode.Escape) || KeyIn.Down(KeyCode.Y))
            {
                _open = false;
                _position = -1;
                Speech.BrowseUnrecorded(Vocabulary.HistoryClosed);
                return true;
            }

            int last = _lines.Count - 1;
            if (last < 0) { _open = false; return true; }
            if (_position < 0 || _position > last) _position = last;

            bool shift = KeyIn.Held(KeyCode.LeftShift) || KeyIn.Held(KeyCode.RightShift);
            if (KeyIn.Down(KeyCode.UpArrow) && shift) { }                                  // re-read
            // Session 38, Zamar: Left works as Up and Right as Down, as in
            // Mod Settings and the help list.
            else if (KeyIn.Down(KeyCode.DownArrow) || KeyIn.Down(KeyCode.RightArrow)) _position = System.Math.Max(0, _position - 1);
            else if (KeyIn.Down(KeyCode.UpArrow)   || KeyIn.Down(KeyCode.LeftArrow))  _position = System.Math.Min(last, _position + 1);
            else if (KeyIn.Down(KeyCode.Home))      _position = last;      // first = newest
            else if (KeyIn.Down(KeyCode.End))       _position = 0;         // last = oldest
            else return true;   // any other key: swallowed while the list is open

            Speech.BrowseUnrecorded(_lines[_position]);
            return true;
        }
    }
}
// ReviewHistory.cs
