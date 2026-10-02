// DiagnosticGate.cs
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace IKMA
{
    /// <summary>
    /// The log trim (plan M1). IKMA's diagnostic lines stop shipping by
    /// default; the lines a player or Zamar reads a session back from stay.
    /// (Session 32. UNTESTED - read the next log and say what is missing.)
    /// </summary>
    /// <remarks>
    /// THE RULE (Zamar, 2026-09-13, in ikma_log_is_presentation): the log is
    /// part of the mod's presentation - "precise, brief, and scrollable to a
    /// screen reader with no purely debug info in it" - and "clean while you
    /// cook". Measured on his 0.7.364 log: 576 lines, 122 of them spoken
    /// lines; nearly everything else was IKMA explaining its own machinery.
    ///
    /// GATE, DO NOT DELETE. Not one LogInfo call was removed or edited. The
    /// lines are still written by the code; this class decides at the one
    /// place every line passes through (LogFilter's listener wrapper) whether
    /// it reaches the console and LogOutput.log. Turn a category back on in
    /// BepInEx\config\com.zamar.ikma.cfg:
    ///
    ///   [Diagnostics]
    ///   Show =                  (default: the clean log)
    ///   Show = ALL              (everything, as before Session 32)
    ///   Show = PLAY, BOARDDIFF  (just those tags)
    ///
    /// A "tag" is the word after IKMA: "IKMA PLAY: ..." is tag PLAY,
    /// "IKMA QUEUE (action): ..." is tag QUEUE.
    ///
    /// WHAT ALWAYS SHOWS, whatever Show says:
    ///   - Every warning and error. A feature standing down must be loud.
    ///   - Anything that is not an IKMA line (BepInEx, the game, the banner).
    ///   - IKMA SPEAK - the spine of the log: every line the player heard.
    ///   - Zamar's review queue and running lists: PROVISIONAL, WORDING,
    ///     SIGIL FIRST SEEN.
    ///   - Lines that explain a SILENCE ("not spoken", "not announced",
    ///     "silent", "dropped", "skipped", "nothing spoken"). A silence with
    ///     no marker is indistinguishable from a bug - the single most useful
    ///     diagnostic class, per the same memory.
    ///   - The queue's own "held for / cleared / dropped" (plain IKMA QUEUE:),
    ///     but not the per-line enqueue echoes (IKMA QUEUE (action): ...),
    ///     which only repeat a SPEAK line that follows.
    ///   - UNREAD (a screen with no reader), LOG FILTER (what the filter hides,
    ///     said once), LOGEXPORT (the player pressed Shift+L), PAD (it has its
    ///     own switch, [Gamepad] LogButtonPresses).
    ///   - UPDATE: what auto-update checked, downloaded or installed
    ///     (Session 32). Rare, and the log is the only record of it.
    ///   - The speech engine lines that name the engine or a problem with it.
    ///     The startup roll call and per-call timings are diagnostics.
    ///
    /// THE BUG REPORT CARRIES THE CLEAN LOG TOO. Shift+L copies LogOutput.log,
    /// which this gate filters. For a hard report, ask the tester to set
    /// Show = ALL and reproduce it.
    /// </remarks>
    internal static class DiagnosticGate
    {
        internal static ConfigEntry<string> Show;

        private static bool _all;
        private static readonly HashSet<string> _shown =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> _alwaysTags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SPEAK", "PROVISIONAL", "WORDING", "SIGIL FIRST SEEN",
                "UNREAD", "LOG FILTER", "LOG", "LOGEXPORT", "PAD",
                // Session 32: auto-update. Rare, and the log is the only
                // record of what the mod downloaded or installed.
                "UPDATE",
                // Session 34: the key that switched IKMA to the keyboard. Rare.
                "INPUT",
            };

        private static readonly HashSet<string> _bookkeepingTags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DIFF" };

        private static readonly string[] _silenceMarkers =
        {
            "not spoken", "not announced", "silent", "nothing spoken",
            "no reading spoken", "dropped", "skipped",
        };

        // IKMA SPEECH lines that are telemetry, not news. Everything else
        // under SPEECH (engine pinned / changed / lost, backend, bridge
        // connected or not answering, stalls, failures) stays.
        private static readonly string[] _speechTelemetry =
        {
            "pump thread started", "engines running", "pump queue reached depth",
            "when it is speaking", "prompt answered", "follows the line",
            "will queue",
        };

        internal static void BindConfig(ConfigFile config)
        {
            Show = config.Bind("Diagnostics", "Show", "",
                "IKMA's diagnostic log lines. Empty = clean log (spoken lines, warnings, and why something was not spoken). " +
                "ALL = everything. Or a comma list of tags, e.g. PLAY, BOARDDIFF.");
            Apply(Show.Value);
            // Session 38: Mod Settings can change it while the game runs.
            Show.SettingChanged += (s, e) => { Apply(Show.Value); Plugin.VerboseDiagnostics = _all; };
        }

        private static void Apply(string value)
        {
            _all = false;
            _shown.Clear();
            if (string.IsNullOrEmpty(value)) return;
            foreach (string part in value.Split(','))
            {
                string tag = part.Trim();
                if (tag.Length == 0) continue;
                if (string.Equals(tag, "ALL", StringComparison.OrdinalIgnoreCase)) { _all = true; return; }
                _shown.Add(tag);
            }
        }

        /// <summary>True when ALL is set - the old everything-on log.</summary>
        internal static bool ShowAll => _all;

        /// <summary>
        /// True if this log event is an IKMA diagnostic the config does not ask
        /// for. Called for every line by LogFilter's listener wrapper. Any
        /// failure answers false: the gate may only ever cost a hidden line,
        /// never a shown one's absence being a crash.
        /// </summary>
        internal static bool Hide(LogEventArgs e)
        {
            try
            {
                if (e == null || _all) return false;
                if (e.Level != LogLevel.Info && e.Level != LogLevel.Message && e.Level != LogLevel.Debug)
                    return false;   // warnings, errors, fatal: always

                string text = e.Data as string;
                if (text == null || !text.StartsWith("IKMA", StringComparison.Ordinal)) return false;

                // The startup roll of "IKMA: <feature> patch applied." - 137
                // lines in his 0.7.364 log. A patch that FAILS is an error and
                // always shows; one that worked is not news.
                if (text.EndsWith("patch applied.", StringComparison.Ordinal)) return true;

                string tag = TagOf(text, out bool parenthetical);
                if (_shown.Contains(tag)) return false;
                if (_alwaysTags.Contains(tag)) return false;

                // Tags whose "skipped" is IKMA's own bookkeeping, not a silence
                // the player would notice: DIFF's "slot 3 skipped (one card
                // holding more than one slot)" is a giant card, 51 lines in
                // his 0.7.356 log.
                if (_bookkeepingTags.Contains(tag)) return true;

                foreach (string m in _silenceMarkers)
                    if (text.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return false;

                if (tag == "QUEUE" && !parenthetical) return false;   // held / cleared / dropped

                if (tag == "SPEECH")
                {
                    foreach (string t in _speechTelemetry)
                        if (text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    return false;
                }

                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// "IKMA QUEUE (action): x" -> "QUEUE" (parenthetical = true);
        /// "IKMA SIGIL FIRST SEEN: x" -> "SIGIL FIRST SEEN"; "IKMA: x" -> "".
        /// The tag ends at the first ':' or '(' or " —".
        /// </summary>
        private static string TagOf(string text, out bool parenthetical)
        {
            parenthetical = false;
            int i = 4;                                  // after "IKMA"
            int end = text.Length;
            for (int j = i; j < text.Length; j++)
            {
                char c = text[j];
                if (c == ':') { end = j; break; }
                if (c == '(') { end = j; parenthetical = true; break; }
                if (c == '—') { end = j; break; }  // an em dash
            }
            return text.Substring(i, end - i).Trim();
        }

        /// <summary>One line at startup saying the log is trimmed and how to undo it.</summary>
        internal static string StartupLine()
            => _all
                ? "IKMA LOG: all diagnostics shown ([Diagnostics] Show = ALL)."
                : "IKMA LOG: diagnostic lines hidden; set [Diagnostics] Show = ALL in com.zamar.ikma.cfg to see them.";
    }
}
