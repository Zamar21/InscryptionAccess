// PadWords.cs
using System;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// When the player is on a controller, every key IKMA names in a spoken
    /// line becomes the button that does it. (Session 34.)
    /// </summary>
    /// <remarks>
    /// ZAMAR'S RULES (Session 34, all four OK'd):
    ///   1. Names follow whatever the player pressed last - a pad button
    ///      switches the lines to pad names, a key switches them back.
    ///   2. A chord is said with "plus": "Press RB plus A to draw from your
    ///      deck" - the same way "Shift plus I" is said.
    ///   3. "Left and right arrows" becomes "D-pad left and right"; a plain
    ///      "Arrows" becomes "D-pad".
    ///   4. Button words come from his table (docs\GAMEPAD_MAP.md, "Button
    ///      names by family"): Xbox, PlayStation, Steam Deck, Switch, by
    ///      position - a PlayStation player hears "Cross" where Xbox hears "A".
    ///
    /// WHY A REWRITE AT THE MOMENT OF SPEECH, NOT NEW LINES IN Vocabulary.
    /// Every line in Vocabulary is also its own translation key (Loc.T /
    /// Loc.F look the English text up). Changing 86 lines to carry button
    /// tokens would orphan every translation of them. So the lines stay as
    /// they are and this rewrites the finished English sentence on its way
    /// out, in CardReader.Speak - the one place every utterance passes.
    /// It runs only in English; other languages keep the keyboard names
    /// until localization resumes.
    ///
    /// WHAT IT KNOWS ABOUT KEYS comes from KeyIn.Map - the same table the
    /// pad presses go through - so a spoken button is always the button
    /// that works. A key with no pad binding (F9, F10, typing a death card
    /// name) is left as the key.
    ///
    /// THE LETTER KEYS ARE THE HARD PART. "A" is a key and an article. A
    /// single capital letter is only treated as a key where IKMA's own lines
    /// use one: after "Press", or followed by " to", " for", ", ", " and",
    /// " or", " opens", " cycles", " reads", " counts", " closes", " looks".
    /// "A sacrifice cannot be cancelled" stays as it is. The patterns are
    /// matched in ONE pass, so a replacement is never re-read: Backspace
    /// becomes "B" and that B is not then taken for the B key.
    /// </remarks>
    public static class PadWords
    {
        public enum Family { Auto, Xbox, PlayStation, SteamDeck, Switch }

        internal static ConfigEntry<Family> ButtonNames;

        internal static void BindConfig(ConfigFile config)
        {
            ButtonNames = config.Bind("Gamepad", "ButtonNames", Family.Auto,
                "Which controller's button names IKMA says: Auto (from the controller's name), Xbox, PlayStation, SteamDeck or Switch. Steam Input can make a PlayStation pad look like an Xbox pad; set it here if the names are wrong.");
        }

        // ------------------------------------------------------------------
        // Rule 4: the family, and its words. By POSITION, from his table.
        // ------------------------------------------------------------------
        internal static Family CurrentFamily()
        {
            var set = ButtonNames != null ? ButtonNames.Value : Family.Auto;
            if (set != Family.Auto) return set;

            // THE PAD'S OWN NAME FIRST. (Session 34.) A PlayStation or Switch
            // pad paired to a Steam Deck by Bluetooth, with Steam Input off,
            // reports its own name and gets its own words. With Steam Input
            // on, Steam presents every pad as a generic Xbox-style pad, and
            // IKMA follows what Steam reports (Zamar: that is the player's
            // own Steam setting).
            string n = (PadInput.LastPadName ?? "").ToLowerInvariant();
            if (n.Contains("sony") || n.Contains("dualshock") || n.Contains("dualsense")
                || n.Contains("playstation") || n.Contains("ps4") || n.Contains("ps5")
                || n.Contains("wireless controller"))
                return Family.PlayStation;
            if (n.Contains("nintendo") || n.Contains("pro controller") || n.Contains("joy-con")
                || n.Contains("switch"))
                return Family.Switch;

            // A generic Xbox-style name on a Steam Deck is the Deck's own
            // controls as Steam presents them: L1 / R1 / L2 / R2 words.
            // Steam sets SteamDeck=1 for games on a Deck; Proton passes it on.
            try { if (Environment.GetEnvironmentVariable("SteamDeck") == "1") return Family.SteamDeck; } catch { }
            return Family.Xbox;
        }

        private static bool IsPs5()
        {
            string n = (PadInput.LastPadName ?? "").ToLowerInvariant();
            return n.Contains("dualsense") || n.Contains("ps5");
        }

        internal static string ButtonName(PadInput.PadButton b)
        {
            var f = CurrentFamily();
            switch (b)
            {
                case PadInput.PadButton.A:      return f == Family.PlayStation ? "Cross"    : f == Family.Switch ? "B" : "A";
                case PadInput.PadButton.B:      return f == Family.PlayStation ? "Circle"   : f == Family.Switch ? "A" : "B";
                case PadInput.PadButton.X:      return f == Family.PlayStation ? "Square"   : f == Family.Switch ? "Y" : "X";
                case PadInput.PadButton.Y:      return f == Family.PlayStation ? "Triangle" : f == Family.Switch ? "X" : "Y";
                case PadInput.PadButton.LB:     return f == Family.Xbox ? "LB" : f == Family.Switch ? "L"  : "L1";
                case PadInput.PadButton.RB:     return f == Family.Xbox ? "RB" : f == Family.Switch ? "R"  : "R1";
                case PadInput.PadButton.LT:     return f == Family.Xbox ? "LT" : f == Family.Switch ? "ZL" : "L2";
                case PadInput.PadButton.RT:     return f == Family.Xbox ? "RT" : f == Family.Switch ? "ZR" : "R2";
                case PadInput.PadButton.View:   return f == Family.PlayStation ? (IsPs5() ? "Create" : "Share") : f == Family.Switch ? "Minus" : "View";
                case PadInput.PadButton.Start:  return f == Family.PlayStation ? "Options" : f == Family.Switch ? "Plus" : "Menu";
                case PadInput.PadButton.L3:     return f == Family.Switch ? "left stick press"  : "L3";
                case PadInput.PadButton.R3:     return f == Family.Switch ? "right stick press" : "R3";
                case PadInput.PadButton.Up:     return "D-pad up";
                case PadInput.PadButton.Down:   return "D-pad down";
                case PadInput.PadButton.Left:   return "D-pad left";
                case PadInput.PadButton.Right:  return "D-pad right";
                case PadInput.PadButton.RUp:    return "right stick up";
                case PadInput.PadButton.RDown:  return "right stick down";
                case PadInput.PadButton.RLeft:  return "right stick left";
                case PadInput.PadButton.RRight: return "right stick right";
            }
            return b.ToString();
        }

        /// <summary>The spoken pad gesture for a key, or null if no button does it.</summary>
        internal static string ForKey(KeyCode key, bool shift)
        {
            foreach (var b in KeyIn.Map)
            {
                if (b.Key != key || b.Shift != shift) continue;
                return Gesture(b.Chord, b.Button);
            }
            return null;
        }

        /// <summary>
        /// A chord and a button as they are spoken, in the pad's own words:
        /// "RB plus A", "LB plus RB plus View", "Y". (Session 40: taken out
        /// of ForKey so Mod Settings > Controller buttons says a button the
        /// same way every other line does.)
        /// </summary>
        internal static string Gesture(KeyIn.Chord chord, PadInput.PadButton button)
        {
            string btn = ButtonName(button);
            switch (chord)
            {
                case KeyIn.Chord.LB:   return ButtonName(PadInput.PadButton.LB) + " plus " + btn;
                case KeyIn.Chord.RB:   return ButtonName(PadInput.PadButton.RB) + " plus " + btn;
                case KeyIn.Chord.Both: return ButtonName(PadInput.PadButton.LB) + " plus " + ButtonName(PadInput.PadButton.RB) + " plus " + btn;
                default:               return btn;
            }
        }

        // ------------------------------------------------------------------
        // WORDS THAT ARE ALREADY BUTTONS. (Session 40.)
        //
        // The rewrite below turns KEY names into button names. A line from
        // Mod Settings > Controller buttons already names BUTTONS, and to the
        // rewrite a button called "A" or "B" looks exactly like the A key or
        // the B key: "Draw from your deck: RB plus A, button" would be said
        // as "RB plus LB plus A", because the A key is on LB plus A.
        //
        // So a line can mark a piece of itself as said-as-written. The marks
        // are two characters no screen reader is ever sent (Unicode's private
        // use area, the same trick the capitals rule below uses). They are
        // taken out here, at the one place speech leaves the mod, so the
        // review history can keep the marked line and read it back correctly
        // on any device.
        // ------------------------------------------------------------------
        private const string LIT_OPEN = "\uE001", LIT_CLOSE = "\uE002";
        private static readonly Regex Literals = new Regex(LIT_OPEN + "([^" + LIT_CLOSE + "]*)" + LIT_CLOSE, RegexOptions.CultureInvariant);

        /// <summary>Mark text as already in button words: spoken as written.</summary>
        internal static string Literal(string text) => LIT_OPEN + text + LIT_CLOSE;

        /// <summary>The text with the marks taken out - for the log.</summary>
        internal static string StripLiteral(string text)
            => string.IsNullOrEmpty(text) ? text : text.Replace(LIT_OPEN, "").Replace(LIT_CLOSE, "");

        // ------------------------------------------------------------------
        // The rewrite. One regex, one pass; order inside it is longest first.
        // ------------------------------------------------------------------
        // Rule 3 turns plural "arrows" into one "D-pad", so a verb right after
        // it goes singular: "Arrows browse" -> "D-pad browses" (Zamar,
        // 0.7.379). Only the verbs IKMA's lines use after arrows.
        private const string Verbs = @"(?:browse|move|turn|step|navigate|adjust)\b";

        private static readonly Regex Keys = new Regex(
            @"(?<quick>Shift plus 1, 2, or 3)" +
            // Session 34: a bare number list ("1, 2 and 3 examine ...") and the
            // cabin's turn keys ("A turns you ...", "D turns you ...") - the
            // pad turns with the right stick, the keyboard's other turn keys.
            @"|(?<pages>\b1, 2,? and 3 switch pages)" +
            // Session 34: X advances a conversation on the pad.
            @"|(?<convo>(?<=[Pp]ress )Space(?= to proceed))" +
            // Session 34: in the draw phase the pad draws with plain A and B.
            @"|(?<drawD>\bD(?=,? (?:to )?draw from your deck))" +
            @"|(?<drawS>\bS(?=,? (?:to )?draw from the Squirrel deck))" +
            // Session 35 (found with the test driver): not card POSITIONS.
            // "Starter Deck: Eggs. Cards 1, 2, and 3 of 3" was spoken as
            // "Cards RB plus D-pad up, right, and down of 3".
            @"|(?<nums>(?<!Cards )\b1, 2,? (?<conj>and|or) 3\b(?! of\b))" +
            @"|(?<turn>\b(?<tk>[AD]) turns you\b)" +
            @"|(?<sarrow>Shift(?: plus | and |\+| )(?<sdir>Up|Down|Left|Right)(?: arrow)?)" +
            @"|(?<sletter>Shift(?: plus | and |\+| )(?<sl>[A-Z])\b)" +
            @"|(?<lr>[Ll]eft and right arrows)(?<v1> " + Verbs + @")?" +
            @"|(?<ud>[Uu]p and down arrows)(?<v2> " + Verbs + @")?" +
            @"|(?<onearrow>\b(?<adir>[Uu]p|[Dd]own|[Ll]eft|[Rr]ight) arrow\b)" +
            @"|(?<arrows>\bArrow keys\b|\bArrows\b|\barrows\b)(?<v3> " + Verbs + @")?" +
            @"|(?<named>\b(?:Enter|Space|Backspace|Escape|Tab)\b)" +
            @"|(?<=\b[Pp]ress )(?<pressed>[A-Z0-9])\b" +
            @"|\b(?<letter>[A-Z])\b(?=(?: to\b| for\b|, | and\b| or\b| opens\b| cycles\b| reads\b| counts\b| closes\b| looks\b))",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// The line as a pad player should hear it. Unchanged unless the last
        /// press was a pad button, IKMA drives the pad, and IKMA is speaking
        /// English. Lines about typing a death card name are left alone: the
        /// name needs the keyboard.
        /// </summary>
        internal static string ForSpeech(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(LIT_OPEN, System.StringComparison.Ordinal) < 0)
                return ForSpeechCore(text);

            // Session 40: the marked pieces are set aside, the rest is
            // rewritten as usual, and they are put back without their marks.
            var lits = new System.Collections.Generic.List<string>();
            string masked = Literals.Replace(text, m =>
            {
                lits.Add(m.Groups[1].Value);
                return "\uE003" + (char)(0xE200 + lits.Count - 1) + "\uE003";
            });
            string spoken = ForSpeechCore(masked);
            for (int i = 0; i < lits.Count; i++)
                spoken = spoken.Replace("\uE003" + (char)(0xE200 + i) + "\uE003", lits[i]);
            return StripLiteral(spoken);
        }

        private static string ForSpeechCore(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            try
            {
                if (!KeyIn.LastWasPad || !KeyIn.PadOwned) return text;
                if (Loc.CurrentLanguage() != "English") return text;
                if (text.Contains("Type a name")) return text;
                // The game's own dialogue is set in capitals ("I, TOO, ...").
                // A SENTENCE with more capitals than small letters is the game
                // talking, not IKMA naming a key: it is left alone.
                //
                // Session 35 (found with the test driver): this used to be
                // decided for the WHOLE LINE, so the New Run popup - the
                // game's capitals quoted, then IKMA's "Arrows to navigate,
                // Enter to confirm" - kept its keyboard words on a pad. Now
                // the game's sentences are set aside, the rest is rewritten,
                // and they are put back.
                var kept = new System.Collections.Generic.List<string>();
                string masked = Regex.Replace(text, @"[^.!?]+[.!?]*", m =>
                {
                    int up = 0, low = 0;
                    foreach (char ch in m.Value) { if (char.IsUpper(ch)) up++; else if (char.IsLower(ch)) low++; }
                    if (up <= low) return m.Value;
                    kept.Add(m.Value);
                    return "\uE000" + (char)(0xE100 + kept.Count - 1) + "\uE000";
                });
                if (kept.Count > 0 && masked.Replace("\uE000", "").Length <= kept.Count) return text;   // all game text
                string rewritten = Rewrite(masked);
                for (int i = 0; i < kept.Count; i++)
                    rewritten = rewritten.Replace("\uE000" + (char)(0xE100 + i) + "\uE000", kept[i]);
                return rewritten;
            }
            catch { return text; }
        }

        internal static string Rewrite(string text)
        {
            return OrderFace(OrderDpad(ReplaceKeys(text)));
        }

        // ------------------------------------------------------------------
        // FACE-BUTTON ORDER. (Session 34, Zamar: "when reading a list of the
        // face buttons, it should always read A > X > Y > B order. Similar to
        // the Dpad thing.") Same shape as OrderDpad: a run of sentences on the
        // same chord that each start with a face button is put in that order.
        // By POSITION, so a PlayStation list reads Cross, Square, Triangle,
        // Circle.
        // ------------------------------------------------------------------
        private static string OrderFace(string text)
        {
            string a = ButtonName(PadInput.PadButton.A), x = ButtonName(PadInput.PadButton.X),
                   y = ButtonName(PadInput.PadButton.Y), b = ButtonName(PadInput.PadButton.B);
            var face = new Regex(
                @"^(?<chord>(?:\S+ plus )*)(?<btn>" + Regex.Escape(a) + "|" + Regex.Escape(x) + "|"
                + Regex.Escape(y) + "|" + Regex.Escape(b) + @")(?=,| to\b| for\b)",
                RegexOptions.CultureInvariant);
            System.Func<string, int> rank = s =>
            {
                string btn = face.Match(s).Groups["btn"].Value;
                return btn == a ? 0 : btn == x ? 1 : btn == y ? 2 : 3;
            };

            string[] parts = Regex.Split(text, @"(?<=[.])\s+");
            if (parts.Length < 2) return text;
            bool changed = false;
            int i = 0;
            while (i < parts.Length)
            {
                var m = face.Match(parts[i]);
                if (!m.Success) { i++; continue; }
                string chord = m.Groups["chord"].Value;
                int j = i + 1;
                while (j < parts.Length)
                {
                    var n = face.Match(parts[j]);
                    if (!n.Success || n.Groups["chord"].Value != chord) break;
                    j++;
                }
                for (int p = i + 1; p < j; p++)
                {
                    string item = parts[p];
                    int r = rank(item);
                    int q = p - 1;
                    while (q >= i && rank(parts[q]) > r) { parts[q + 1] = parts[q]; q--; changed = true; }
                    parts[q + 1] = item;
                }
                i = j;
            }
            return changed ? string.Join(" ", parts) : text;
        }

        // ------------------------------------------------------------------
        // D-PAD ORDER. (Session 34, Zamar: "Dpad controls should always be
        // read in order Up > Right > Down > Left. To help teach 1, 2, 3, 4.")
        // A run of sentences that each start with a D-pad direction on the
        // same chord ("LB plus D-pad left, read your hand. LB plus D-pad up,
        // ...") is put in clock order. A pair like "D-pad left and right" is
        // one control, not a list, and is left alone.
        // ------------------------------------------------------------------
        private static readonly Regex DpadSentence = new Regex(
            @"^(?<chord>(?:\S+ plus )*)D-pad (?<dir>up|right|down|left)\b(?! and)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static int DirRank(string d)
        {
            switch (d.ToLowerInvariant())
            {
                case "up": return 0;
                case "right": return 1;
                case "down": return 2;
                default: return 3;
            }
        }

        private static string OrderDpad(string text)
        {
            string[] parts = Regex.Split(text, @"(?<=[.])\s+");
            if (parts.Length < 2) return text;
            bool changed = false;
            int i = 0;
            while (i < parts.Length)
            {
                var m = DpadSentence.Match(parts[i]);
                if (!m.Success) { i++; continue; }
                string chord = m.Groups["chord"].Value;
                int j = i + 1;
                while (j < parts.Length)
                {
                    var n = DpadSentence.Match(parts[j]);
                    if (!n.Success || n.Groups["chord"].Value != chord) break;
                    j++;
                }
                if (j - i > 1)
                {
                    // Insertion sort on the run: stable, and runs are 2-4 long.
                    for (int a = i + 1; a < j; a++)
                    {
                        string item = parts[a];
                        int rank = DirRank(DpadSentence.Match(item).Groups["dir"].Value);
                        int b = a - 1;
                        while (b >= i && DirRank(DpadSentence.Match(parts[b]).Groups["dir"].Value) > rank)
                        {
                            parts[b + 1] = parts[b];
                            b--;
                            changed = true;
                        }
                        parts[b + 1] = item;
                    }
                }
                i = j;
            }
            return changed ? string.Join(" ", parts) : text;
        }

        private static string ReplaceKeys(string text)
        {
            return Keys.Replace(text, m =>
            {
                string r = null;
                if (m.Groups["quick"].Success)
                    r = ButtonName(PadInput.PadButton.RB) + " plus D-pad up, right, or down";
                else if (m.Groups["convo"].Success)
                    r = ButtonName(PadInput.PadButton.X);
                else if (m.Groups["drawD"].Success)
                    r = ButtonName(PadInput.PadButton.A);
                else if (m.Groups["drawS"].Success)
                    r = ButtonName(PadInput.PadButton.B);
                else if (m.Groups["pages"].Success)
                    // Zamar, Session 34: the shoulders turn the options pages.
                    r = ButtonName(PadInput.PadButton.RB) + " goes to the next page right, "
                      + ButtonName(PadInput.PadButton.LB) + " goes to the next page left";
                else if (m.Groups["nums"].Success)
                    r = ButtonName(PadInput.PadButton.RB) + " plus D-pad up, right, " + m.Groups["conj"].Value + " down";
                else if (m.Groups["turn"].Success)
                {
                    string stick = ForKey(m.Groups["tk"].Value == "A" ? KeyCode.LeftArrow : KeyCode.RightArrow, shift: true);
                    r = stick != null ? stick + " turns you" : null;
                }
                else if (m.Groups["sarrow"].Success)
                    r = ForKey(Arrow(m.Groups["sdir"].Value), shift: true);
                else if (m.Groups["sletter"].Success)
                    r = ForKey(Letter(m.Groups["sl"].Value), shift: true);
                else if (m.Groups["lr"].Success)
                    r = "D-pad left and right" + Singular(m.Groups["v1"]);
                else if (m.Groups["ud"].Success)
                    r = "D-pad up and down" + Singular(m.Groups["v2"]);
                else if (m.Groups["onearrow"].Success)
                    r = "D-pad " + m.Groups["adir"].Value.ToLowerInvariant();
                else if (m.Groups["arrows"].Success)
                    r = "D-pad" + Singular(m.Groups["v3"]);
                else if (m.Groups["named"].Success)
                    r = ForKey(Named(m.Groups["named"].Value), shift: false);
                else if (m.Groups["pressed"].Success)
                    r = ForKey(Letter(m.Groups["pressed"].Value), shift: false);
                else if (m.Groups["letter"].Success)
                    r = ForKey(Letter(m.Groups["letter"].Value), shift: false);
                if (r == null) return m.Value;   // no pad binding: keep the key

                // "right stick up" opening a sentence reads "Right stick up".
                bool sentenceStart = m.Index == 0
                    || (m.Index >= 2 && text[m.Index - 1] == ' ' && ".!?".IndexOf(text[m.Index - 2]) >= 0);
                if (sentenceStart && r.Length > 0 && char.IsLower(r[0]))
                    r = char.ToUpperInvariant(r[0]) + r.Substring(1);
                return r;
            });
        }

        /// <summary>" browse" -> " browses"; nothing captured -> "".</summary>
        private static string Singular(Group verb)
            => verb.Success ? verb.Value + "s" : "";

        private static KeyCode Arrow(string dir)
        {
            switch (dir.ToLowerInvariant())
            {
                case "up":   return KeyCode.UpArrow;
                case "down": return KeyCode.DownArrow;
                case "left": return KeyCode.LeftArrow;
                default:     return KeyCode.RightArrow;
            }
        }

        private static KeyCode Named(string name)
        {
            switch (name)
            {
                case "Enter":     return KeyCode.Return;
                case "Space":     return KeyCode.Space;
                case "Backspace": return KeyCode.Backspace;
                case "Escape":    return KeyCode.Escape;
                default:          return KeyCode.Tab;
            }
        }

        private static KeyCode Letter(string c)
        {
            char ch = c[0];
            if (ch >= '1' && ch <= '4') return KeyCode.Alpha1 + (ch - '1');
            if (ch >= 'A' && ch <= 'Z') return KeyCode.A + (ch - 'A');
            return KeyCode.None;
        }
    }
}
