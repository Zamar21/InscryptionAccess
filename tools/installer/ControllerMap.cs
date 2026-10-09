// ControllerMap.cs
using System;
using System.Collections.Generic;

namespace IKMASetup
{
    /// <summary>
    /// IKMA Manager's controller buttons menu (Session 34, DRAFT, NEVER RUN).
    /// Moves IKMA's own actions to other controller buttons by writing
    /// [ControllerMap] in IKMA's config. The mod reads it in KeyIn.BindRemap.
    /// </summary>
    /// <remarks>
    /// THE LIST BELOW COPIES KeyIn.Defaults (IKMAccess\KeyIn.cs) - the
    /// actions that carry an Action name, their default gesture, in the same
    /// order. The Manager cannot load the mod, so a change there must be made
    /// here too. docs\GAMEPAD_MAP.md is where the map is decided.
    ///
    /// Gesture text is the mod's KeyIn.GestureText: "Y", "LB+X",
    /// "RB+DpadUp", "LB+RB+View". The rules are the mod's TryParseGesture:
    ///   - LB and RB are never an action's button (they are the chords, and
    ///     a tap flips options pages).
    ///   - Without a chord, only the buttons the game does not use: X, Y,
    ///     LT, View, L3, R3.
    ///   - The right stick is never offered (it is the game's look).
    ///
    /// A button another action already uses SWAPS the two actions (Zamar,
    /// Session 34), so the file never holds a clash; the mod would otherwise
    /// silently put one of the two back on its default.
    ///
    /// Button names are Xbox's, the names the config file uses. All wording
    /// PROVISIONAL: Claude's.
    /// </remarks>
    internal static class ControllerMap
    {
        private const string Section = "ControllerMap";

        private sealed class Action
        {
            internal readonly string Key, Name, Default;
            internal Action(string key, string name, string def) { Key = key; Name = name; Default = def; }
        }

        // KeyIn.Defaults, in order. Names PROVISIONAL.
        private static readonly Action[] Actions =
        {
            new Action("Rulebook",           L.T("Open the rulebook"),                        "Y"),
            new Action("Help",               L.T("Help"),                                     "View"),
            new Action("Repeat",             L.T("Repeat, describe, or advance a conversation"), "X"),
            new Action("NextPlayable",       L.T("Next playable card, slot, or sacrifice"),   "LT"),
            new Action("LastAbilitiesQuick", L.T("Rulebook for the last heard abilities"),    "L3"),
            new Action("ReadHand",           L.T("Read your hand"),                           "LB+DpadLeft"),
            new Action("ReadEnemyQueue",     L.T("Read the enemy queue"),                     "LB+DpadUp"),
            new Action("ReadEnemyBoard",     L.T("Read the enemy board"),                     "LB+DpadRight"),
            new Action("ReadYourBoard",      L.T("Read your board"),                          "LB+DpadDown"),
            new Action("GameInfo",           L.T("Game info: scales, bones, and teeth"),      "LB+A"),
            new Action("FullBoard",          L.T("Full board and queue"),                     "LB+B"),
            new Action("AllItemSlots",       L.T("All three item slots"),                     "LB+X"),
            new Action("LastAbilities",      L.T("Rulebook for the last heard abilities, second button"), "LB+Y"),
            new Action("ModSettings",        L.T("Mod Settings"),                             "LB+Start"),        // Session 37, Say the Spire 2
            new Action("HelpList",           L.T("Help list"),                                "LB+View"),         // Session 37, Say the Spire 2
            new Action("DrawFromDeck",       L.T("Draw from your deck"),                      "RB+A"),
            new Action("DrawSquirrel",       L.T("Draw from the Squirrel deck"),              "RB+B"),
            new Action("ItemMenu",           L.T("Item menu"),                                "RB+X"),
            new Action("AcceptSurrender",    L.T("Accept surrender"),                         "RB+Y"),
            new Action("Number1",            L.T("Number 1"),                                 "RB+DpadUp"),
            new Action("Number2",            L.T("Number 2"),                                 "RB+DpadRight"),
            new Action("Number3",            L.T("Number 3"),                                 "RB+DpadDown"),
            new Action("Number4",            L.T("Number 4"),                                 "RB+DpadLeft"),
            new Action("SaveLog",            L.T("Save a bug report log"),                    "LB+RB+View"),
            new Action("HistoryOlder",       L.T("Review history, older"),                    "LB+RB+DpadDown"),   // Session 35, provisional
            new Action("HistoryNewer",       L.T("Review history, newer"),                    "LB+RB+DpadUp"),     // Session 35, provisional
            new Action("HistoryList",        L.T("Review history as a list"),                 "LB+R3"),            // Session 51 (0.7.463), provisional name
            new Action("Silence",            L.T("Silence speech"),                           "R3"),               // Session 36, provisional
        };

        // The chords, as offered. Index 0 is no chord.
        private static readonly string[] ChordPrefix = { "", "LB+", "RB+", "LB+RB+" };

        // Buttons offered, in the order they are read: face buttons A, X, Y,
        // B; D-pad up, right, down, left (both Zamar's list orders, Session
        // 34); then the rest. Config names.
        private static readonly string[] WithChord =
            { "A", "X", "Y", "B", "DpadUp", "DpadRight", "DpadDown", "DpadLeft", "LT", "RT", "View", "Start", "L3", "R3" };
        private static readonly string[] Alone =
            { "X", "Y", "LT", "View", "L3", "R3" };

        /// <summary>The line for this menu in the Settings list.</summary>
        internal static string SummaryName(string path)
        {
            int moved = 0;
            foreach (Action a in Actions)
                if (!Same(Current(path, a), a.Default)) moved++;
            return Text.ControllerSummary(moved);
        }

        /// <summary>The controller buttons menu. Returns on Enter alone.</summary>
        internal static void Run(string path)
        {
            while (true)
            {
                Program.Say(Text.ControllerMenu);
                for (int i = 0; i < Actions.Length; i++)
                {
                    string cur = Current(path, Actions[i]);
                    Program.Say(Text.ActionLine(i + 1, Actions[i].Name, Spoken(cur),
                        Same(cur, Actions[i].Default) ? null : Spoken(Actions[i].Default)));
                }

                string typed = Program.ReadLine();
                if (typed.Length == 0) return;
                if (string.Equals(typed, "R", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (Action a in Actions) Settings.Set(path, Section, a.Key, a.Default);
                    Program.Say(Text.ControllerReset);
                    continue;
                }
                if (!int.TryParse(typed, out int n) || n < 1 || n > Actions.Length)
                {
                    Program.Say(Text.NotAChoice);
                    continue;
                }
                Move(path, Actions[n - 1]);
            }
        }

        private static void Move(string path, Action a)
        {
            // Step 1: which shoulder buttons are held with it.
            Program.Say(Text.ChooseChord(a.Name, Spoken(Current(path, a))));
            string typed = Program.ReadLine();
            if (typed.Length == 0) return;
            if (!int.TryParse(typed, out int c) || c < 1 || c > ChordPrefix.Length)
            {
                Program.Say(Text.NotAChoice);
                return;
            }
            string prefix = ChordPrefix[c - 1];
            string[] buttons = prefix.Length == 0 ? Alone : WithChord;

            // Step 2: the button. Each one says which action already has it.
            Program.Say(Text.ChooseButton(a.Name));
            for (int i = 0; i < buttons.Length; i++)
            {
                string g = prefix + buttons[i];
                Action holder = Holder(path, g);
                Program.Say(Text.ButtonLine(i + 1, Spoken(g),
                    holder == null ? null : holder == a ? Text.ThisAction : holder.Name));
            }
            typed = Program.ReadLine();
            if (typed.Length == 0) return;
            if (!int.TryParse(typed, out int b) || b < 1 || b > buttons.Length)
            {
                Program.Say(Text.NotAChoice);
                return;
            }

            string want = prefix + buttons[b - 1];
            Action owner = Holder(path, want);
            if (owner != null && owner != a)
            {
                // Zamar, Session 34: a taken button SWAPS the two actions.
                // Every action takes the same gestures, so the other action
                // can always use this one's old button.
                string old = Current(path, a);
                Settings.Set(path, Section, owner.Key, old);
                Settings.Set(path, Section, a.Key, want);
                Program.Say(Text.Swapped(a.Name, Spoken(want), owner.Name, Spoken(old)));
                return;
            }
            Settings.Set(path, Section, a.Key, want);
            Program.Say(Text.SettingSaved(a.Name, Spoken(want)));
        }

        /// <summary>The action on this gesture now, or null.</summary>
        private static Action Holder(string path, string gesture)
        {
            foreach (Action x in Actions)
                if (Same(Current(path, x), gesture)) return x;
            return null;
        }

        /// <summary>
        /// The gesture the mod will use: the file's value, or the default when
        /// it is absent or not one the mod accepts (the mod falls back the
        /// same way).
        /// </summary>
        private static string Current(string path, Action a)
        {
            string v = Settings.Get(path, Section, a.Key);
            return Valid(v) ? v : a.Default;
        }

        private static bool Valid(string g)
        {
            if (string.IsNullOrEmpty(g)) return false;
            for (int c = 0; c < ChordPrefix.Length; c++)
                foreach (string b in c == 0 ? Alone : WithChord)
                    if (Same(g, ChordPrefix[c] + b)) return true;
            return false;
        }

        private static bool Same(string x, string y)
            => string.Equals(Norm(x), Norm(y), StringComparison.OrdinalIgnoreCase);

        private static string Norm(string g) => (g ?? "").Replace(" ", "");

        /// <summary>"LB+DpadLeft" as the player hears it: "LB plus D-pad Left".</summary>
        private static string Spoken(string gesture)
        {
            var parts = new List<string>();
            foreach (string raw in Norm(gesture).Split('+'))
            {
                string p = raw;
                if (p.StartsWith("Dpad", StringComparison.OrdinalIgnoreCase)) p = "D-pad " + p.Substring(4);
                else if (string.Equals(p, "Start", StringComparison.OrdinalIgnoreCase)) p = "Menu";
                parts.Add(p);
            }
            return string.Join(Text.Plus, parts.ToArray());
        }
    }
}
