// EventsMenu.cs
using System;
using System.Collections.Generic;

namespace IKMASetup
{
    /// <summary>
    /// IKMA Manager's Events menu (Session 37, M9): the same switches as the
    /// in-game Mod Settings > Events (IKMAccess\ModSettingsMenu.cs), written
    /// to the same [Events.X] sections of IKMA's config.
    /// </summary>
    /// <remarks>
    /// THE LIST BELOW COPIES EventSettings.All (IKMAccess\EventSettings.cs):
    /// key, group, label, which switches exist, and their defaults. The
    /// Manager cannot load the mod, so a change there must be made here too.
    /// Labels are Say the Spire 2's (Zamar's rule, Session 37); the ones in
    /// PROVISIONAL_LINES.md are provisional here as well.
    /// </remarks>
    internal static class EventsMenu
    {
        private sealed class Switch
        {
            internal readonly string Key, Label; internal readonly bool Default;
            internal Switch(string key, string label, bool def) { Key = key; Label = label; Default = def; }
        }

        private sealed class Ev
        {
            internal readonly string Key, Group, Label; internal readonly Switch[] Switches;
            internal string Section;   // Session 38: null = "Events." + Key
            internal string Sec => Section ?? "Events." + Key;
            internal Ev(string key, string group, string label, params Switch[] switches)
            { Key = key; Group = group; Label = label; Switches = switches; }
        }

        private static Switch Announce()            => new Switch("Announce", L.T("Announce"), true);
        private static Switch History(bool d = true) => new Switch("AddToHistory", L.T("Add to history"), d);
        private static Switch Player(bool d = true)  => new Switch("CurrentPlayer", L.T("Current Player"), d);
        private static Switch Enemy()                => new Switch("Enemies", L.T("Enemies"), true);

        // Sorted as the in-game menu sorts them: group, then label.
        private static readonly Ev[] All =
        {
            new Ev("CardDrawn",      "Cards",     L.T("Card Drawn"),            Announce(), History()),
            new Ev("CardObtained",   "Cards",     L.T("Card Obtained"),         Announce(), History(), Player()),
            new Ev("CardPlayed",     "Cards",     L.T("Card Played"),           Announce(), History(), Player(false)),
            new Ev("Attacks",        "Combat",    L.T("Attacks"),               Announce(), History(), Player(), Enemy()),
            new Ev("Bosses",         "Combat",    L.T("Bosses"),                Announce(), History()),
            new Ev("Challenges",     "Combat",    L.T("Challenges and Totems"), Announce(), History()),
            new Ev("Death",          "Combat",    L.T("Death"),                 Announce(), History(), Player(), Enemy()),
            new Ev("EnemyMoves",     "Combat",    L.T("Enemy Moves"),           Announce(), History(), Enemy()),
            new Ev("HPChanges",      "Combat",    L.T("HP Changes"),            Announce(), History(), Player(), Enemy()),
            new Ev("Powers",         "Combat",    L.T("Powers"),                Announce(), History(), Player(), Enemy()),
            new Ev("Turns",          "Combat",    L.T("Turns"),                 Announce(), History(),
                   new Switch("CurrentPlayer", L.T("Player Turn Start"), true), new Switch("Enemies", L.T("Enemy Turn Start"), true)),
            new Ev("TurnStartReads", "Combat",    L.T("Your Turn"),             Announce(), History()),
            new Ev("Dialogue",       "Other",     L.T("Dialogue"),              Announce(), History()),   // Session 38: kept by default
            new Ev("NodeEntered",    "Other",     L.T("Node Entered"),          Announce(), History()),
            new Ev("NodeResults",    "Other",     L.T("Node Results"),          Announce(), History()),
            new Ev("Saving",         "Other",     L.T("Saving"),                Announce(), History()),
            new Ev("Bones",          "Resources", L.T("Bones"),                 Announce(), History(), Player()),
            new Ev("ItemObtained",   "Resources", L.T("Item Obtained"),         Announce(), History(), Player()),
            new Ev("ItemUsed",       "Resources", L.T("Item Used"),             Announce(), History(), Player(false)),
            new Ev("Teeth",          "Resources", L.T("Teeth"),                 Announce(), History(), Player()),
        };

        private static bool Value(string path, Ev e, Switch s)
        {
            string raw = Settings.Get(path, e.Sec, s.Key);
            bool v;
            return raw != null && bool.TryParse(raw, out v) ? v : s.Default;
        }

        /// <summary>The line for this menu in the Settings list.</summary>
        internal static string SummaryName(string path)
        {
            int changed = 0;
            foreach (Ev e in All)
                foreach (Switch s in e.Switches)
                    if (Value(path, e, s) != s.Default) changed++;
            return Text.EventsSummary(changed);
        }

        /// <summary>The Events menu. Returns on Enter alone.</summary>
        internal static void Run(string path)
        {
            while (true)
            {
                Program.Say(Text.EventsMenu);
                for (int i = 0; i < All.Length; i++)
                    Program.Say(Text.EventLine(i + 1, L.T(All[i].Group), All[i].Label));

                string typed = Program.ReadLine();
                if (typed.Length == 0) return;
                if (!int.TryParse(typed, out int n) || n < 1 || n > All.Length)
                {
                    Program.Say(Text.NotAChoice);
                    continue;
                }
                RunEvent(path, All[n - 1]);
            }
        }

        // Session 38: the game's Mod Settings > History switches, here too
        // (Zamar: "Exist in both"). [History] Reads / Prompts in the cfg.
        private static readonly Ev HistoryEv = new Ev("History", "", L.T("History"),
            new Switch("Reads", L.T("Reads"), true), new Switch("Prompts", L.T("Prompts"), true)) { Section = "History" };

        internal static string HistorySummary(string path)
        {
            var parts = new List<string>();
            foreach (Switch s in HistoryEv.Switches)
                parts.Add(s.Label + ": " + (Value(path, HistoryEv, s) ? Text.Checked : Text.Unchecked));
            return string.Join(", ", parts.ToArray());
        }

        internal static void RunHistory(string path) => RunEvent(path, HistoryEv);

        private static void RunEvent(string path, Ev e)
        {
            while (true)
            {
                Program.Say(Text.EventMenu(e.Label));
                for (int i = 0; i < e.Switches.Length; i++)
                    Program.Say(Text.SwitchLine(i + 1, e.Switches[i].Label, Value(path, e, e.Switches[i])));

                string typed = Program.ReadLine();
                if (typed.Length == 0) return;
                if (!int.TryParse(typed, out int n) || n < 1 || n > e.Switches.Length)
                {
                    Program.Say(Text.NotAChoice);
                    continue;
                }
                Switch s = e.Switches[n - 1];
                bool now = !Value(path, e, s);
                Settings.Set(path, e.Sec, s.Key, now ? "true" : "false");
                Program.Say(Text.SettingSaved(s.Label, now ? Text.Checked : Text.Unchecked));
            }
        }
    }
}
// EventsMenu.cs
