// Settings.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IKMASetup
{
    /// <summary>
    /// The Settings menu of IKMA Setup, and the small editor for IKMA's
    /// config file it needs. (Session 32. DRAFT, NEVER RUN.)
    /// </summary>
    /// <remarks>
    /// THE FILE: BepInEx\config\com.zamar.ikma.cfg in the game folder. It is
    /// IKMA's own BepInEx config, the same file a player could edit in
    /// Notepad, so Setup and a hand edit can never disagree. BepInEx writes
    /// it the first time the game runs with IKMA; before that it may not
    /// exist, and Setup creates just the lines it sets. BepInEx keeps them
    /// and adds its descriptions when the game next starts.
    ///
    /// Its shape (BepInEx 5):
    ///     [Updates]
    ///
    ///     ## description
    ///     # Setting type: UpdateMode
    ///     Mode = Automatic
    ///
    /// The editor changes the value after "Key =" inside the right
    /// [Section], and touches no other line - comments, other settings and
    /// other sections stay byte for byte as they were.
    ///
    /// THE GAME MUST BE CLOSED while this writes: BepInEx saves the file
    /// itself while the game runs and would put its own copy back.
    /// Program.cs makes sure of that before opening this menu.
    ///
    /// Four settings, Zamar's pick (Session 32). Each one names the config
    /// key it writes, so this file and the mod stay in step:
    ///   Updates       [Updates] Mode         AutoUpdate.cs
    ///   Speech engine [Speech] Backend       SpeechBackend.cs
    ///   NVDA line timing [Speech] NvdaLineEnd   SpeechBackend.cs / NvdaDirect.cs (Session 50)
    ///   Braille display  [Speech] Braille       SpeechBackend.cs (Session 50)
    ///   Language      [Language] Language    (localization, Session 32)
    ///   Full log      [Diagnostics] Show     DiagnosticGate.cs
    ///   Vibration     [Gamepad] VibrationLevel  GamepadSupport.cs (Session 34)
    ///   Controller buttons  [ControllerMap] *   ControllerMap.cs / KeyIn.cs (Session 34)
    /// </remarks>
    internal static class Settings
    {
        private const string ConfigFile = @"BepInEx\config\com.zamar.ikma.cfg";

        /// <summary>One choice: the value written to the file, and what the player hears.</summary>
        private sealed class Option
        {
            internal readonly string Value, Name;
            internal Option(string value, string name) { Value = value; Name = name; }
        }

        // Mirrors the mod's Loc.LocalizationShipped. Session 38.
        private const bool LanguageShipped = false;

        private sealed class Setting
        {
            internal string Section, Key, Title, Default;
            internal Option[] Options;
        }

        // The values must match the mod's enums exactly (BepInEx reads them
        // by name). PROVISIONAL wording: Claude's.
        private static readonly Setting[] All =
        {
            new Setting
            {
                Section = "Updates", Key = "Mode", Title = Text.SettingUpdates, Default = "Automatic",
                Options = new[]
                {
                    new Option("Automatic",       L.T("Automatic: new versions download and install with no question.")),
                    new Option("DownloadThenAsk", L.T("Download, then ask before installing.")),
                    new Option("AskFirst",        L.T("Ask before downloading.")),
                    new Option("Off",             L.T("Off: IKMA never connects to the internet.")),
                },
            },
            new Setting
            {
                Section = "Speech", Key = "Backend", Title = Text.SettingSpeech, Default = "Auto",
                Options = new[]
                {
                    new Option("Auto",        L.T("Automatic: your screen reader on Windows, the IKMA speech helper on a Steam Deck.")),
                    new Option("NVDA",        L.T("Always your screen reader, through UniversalSpeech: NVDA, JAWS, or Windows speech.")),
                    new Option("LinuxBridge", L.T("Always the IKMA speech helper, for Steam Deck and Linux.")),
                },
            },
            // Session 50 (0.7.462): the two [Speech] switches. BepInEx writes a
            // true / false setting as the words "true" and "false". Wording:
            // Claude's (Zamar: "name it what you would recommend"), the same
            // as the game's Mod Settings (Vocabulary.ModSettings).
            new Setting
            {
                Section = "Speech", Key = "NvdaLineEnd", Title = Text.SettingNvdaTiming, Default = "true",
                Options = new[]
                {
                    new Option("true",  L.T("Exact: IKMA asks NVDA when each line has finished. Needs NVDA 2024.1 or later.")),
                    new Option("false", L.T("Estimated: IKMA guesses how long each line takes to read.")),
                },
            },
            new Setting
            {
                Section = "Speech", Key = "Braille", Title = Text.SettingBraille, Default = "true",
                Options = new[]
                {
                    new Option("true",  L.T("On: every spoken line is also sent to your braille display, through NVDA or JAWS.")),
                    new Option("false", L.T("Off: nothing is sent to a braille display.")),
                },
            },
            // Session 34: v0.5 is English only (Loc.LocalizationShipped), so
            // the Language setting is hidden until localization ships -
            // offering it would change nothing. Remove the #if to bring it back.
#if IKMA_LOCALIZATION
            new Setting
            {
                Section = "Language", Key = "Language", Title = Text.SettingLanguage, Default = "Game",
                Options = new[]
                {
                    // Each language named in English and in itself, so a
                    // player who reads only their own language finds it.
                    // These names are NOT translated (Session 32): they are
                    // already readable by every player they are for.
                    new Option("Game",                L.T("Follow the game's language setting.")),
                    new Option("English",             "English"),
                    new Option("French",              "French, Français"),
                    new Option("Italian",             "Italian, Italiano"),
                    new Option("German",              "German, Deutsch"),
                    new Option("Spanish",             "Spanish, Español"),
                    new Option("BrazilianPortuguese", "Brazilian Portuguese, Português do Brasil"),
                    new Option("Turkish",             "Turkish, Türkçe"),
                    new Option("Russian",             "Russian, Русский"),
                    new Option("Japanese",            "Japanese, 日本語"),
                    new Option("Korean",              "Korean, 한국어"),
                    new Option("ChineseSimplified",   "Simplified Chinese, 简体中文"),
                    new Option("ChineseTraditional",  "Traditional Chinese, 繁體中文"),
                },
            },
#endif
            // Session 34 - Zamar's four levels. Values match
            // GamepadSupport.VibrationLevel in the mod.
            new Setting
            {
                Section = "Gamepad", Key = "VibrationLevel", Title = Text.SettingVibration, Default = "High",
                Options = new[]
                {
                    new Option("Off",    L.T("Off")),
                    new Option("Low",    L.T("Low")),
                    new Option("Medium", L.T("Medium")),
                    new Option("High",   L.T("High")),
                },
            },
            new Setting
            {
                Section = "Diagnostics", Key = "Show", Title = Text.SettingFullLog, Default = "",
                Options = new[]
                {
                    new Option("",    L.T("Off: the short log, what IKMA said and anything that went wrong.")),
                    new Option("ALL", L.T("On: every diagnostic line, for a bug report. The log gets much longer.")),
                },
            },
        };

        /// <summary>The Settings menu. Returns when the player presses Enter alone.</summary>
        internal static void Run(string game)
        {
            string path = Path.Combine(game, ConfigFile);
            while (true)
            {
                Program.Say(Text.SettingsMenu);
                for (int i = 0; i < All.Length; i++)
                    Program.Say(Text.SettingLine(i + 1, All[i].Title, CurrentName(path, All[i])));
                // Session 34: the controller buttons menu, last (ControllerMap.cs).
                int controller = All.Length + 1;
                Program.Say(Text.SettingLine(controller, Text.SettingController, ControllerMap.SummaryName(path)));
                // Session 37: the Events menu, after it (EventsMenu.cs).
                int events = controller + 1;
                Program.Say(Text.SettingLine(events, Text.SettingEvents, EventsMenu.SummaryName(path)));
                // Session 38: History, after Events (the game's Mod Settings > History).
                int history = events + 1;
                Program.Say(Text.SettingLine(history, L.T("History"), EventsMenu.HistorySummary(path)));

                string typed = Program.ReadLine();
                if (typed.Length == 0) return;
                if (!int.TryParse(typed, out int n) || n < 1 || n > history)
                {
                    Program.Say(Text.NotAChoice);
                    continue;
                }
                if (n == controller) { ControllerMap.Run(path); continue; }
                if (n == events) { EventsMenu.Run(path); continue; }
                if (n == history) { EventsMenu.RunHistory(path); continue; }
                // Zamar, Session 38: Language says "Feature coming soon." until
                // IKMA's translations are switched on (Loc.LocalizationShipped
                // in the mod; keep the two in step).
                if (All[n - 1].Section == "Language" && !LanguageShipped) { Program.Say(L.T("Feature coming soon.")); continue; }
                Choose(path, All[n - 1]);
            }
        }

        private static void Choose(string path, Setting s)
        {
            string current = Get(path, s.Section, s.Key) ?? s.Default;
            Program.Say(Text.ChooseFor(s.Title));
            for (int i = 0; i < s.Options.Length; i++)
            {
                bool isCurrent = string.Equals(s.Options[i].Value, current, StringComparison.OrdinalIgnoreCase);
                Program.Say(Text.OptionLine(i + 1, s.Options[i].Name, isCurrent));
            }

            string typed = Program.ReadLine();
            if (typed.Length == 0) return;
            if (!int.TryParse(typed, out int n) || n < 1 || n > s.Options.Length)
            {
                Program.Say(Text.NotAChoice);
                return;
            }
            Option picked = s.Options[n - 1];
            Set(path, s.Section, s.Key, picked.Value);
            Program.Say(Text.SettingSaved(s.Title, picked.Name));
        }

        private static string CurrentName(string path, Setting s)
        {
            string value = Get(path, s.Section, s.Key) ?? s.Default;
            foreach (Option o in s.Options)
                if (string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase)) return o.Name;
            // A hand edit Setup has no name for (e.g. Show = PLAY, BOARDDIFF).
            return Text.CustomValue(value);
        }

        // ------------------------------------------------------------------
        // The config file editor.
        // ------------------------------------------------------------------

        /// <summary>The value after "Key =" in [Section], or null if absent.</summary>
        internal static string Get(string path, string section, string key)
        {
            if (!File.Exists(path)) return null;
            string[] lines = File.ReadAllLines(path);
            int at = FindKey(lines, section, key, out _);
            if (at < 0) return null;
            int eq = lines[at].IndexOf('=');
            return lines[at].Substring(eq + 1).Trim();
        }

        /// <summary>
        /// Write "Key = value" into [Section]: replace the existing line, or
        /// add the line under the section header, or add the section at the
        /// end. Nothing else in the file changes.
        /// </summary>
        internal static void Set(string path, string section, string key, string value)
        {
            bool exists = File.Exists(path);
            bool bom = false;
            var lines = new List<string>();
            if (exists)
            {
                byte[] raw = File.ReadAllBytes(path);
                bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
                lines.AddRange(File.ReadAllLines(path));
            }

            string line = key + " = " + value;
            int at = FindKey(lines.ToArray(), section, key, out int header);
            if (at >= 0) lines[at] = line;
            else if (header >= 0) lines.Insert(header + 1, line);
            else
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add("");
                lines.Add(line);
            }

            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine,
                new UTF8Encoding(bom));
        }

        /// <summary>
        /// The line index of "Key =" inside [Section], or -1. header is the
        /// index of the "[Section]" line, or -1 if the section is absent.
        /// </summary>
        private static int FindKey(string[] lines, string section, string key, out int header)
        {
            header = -1;
            bool inside = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    inside = string.Equals(t.Substring(1, t.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase);
                    if (inside && header < 0) header = i;
                    continue;
                }
                if (!inside || t.StartsWith("#")) continue;
                int eq = t.IndexOf('=');
                if (eq > 0 && string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
    }
}
