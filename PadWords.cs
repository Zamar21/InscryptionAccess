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
            string name = ButtonNameCore(b);
            return Templated ? LocalButton(name) : name;
        }

        private static string ButtonNameCore(PadInput.PadButton b)
        {
            var f = CurrentFamily();
            if (Fr)
            {
                string fr = ButtonNameFr(b, f);
                if (fr != null) return fr;
            }
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
                // Session 46: a Control chord is its own key. Without this
                // a plain "M" or a plain Up arrow could be answered with the
                // button that sends Control plus M or Control plus Up.
                if (b.Key != key || b.Shift != shift || b.Ctrl) continue;
                return Gesture(b.Chord, b.Button);
            }
            return null;
        }

        /// <summary>
        /// Session 46. The spoken pad gesture for a key pressed with Control
        /// (review history, Mod Settings), or null if no button does it.
        /// </summary>
        internal static string ForKey(KeyCode key, bool shift, bool ctrl)
        {
            if (!ctrl) return ForKey(key, shift);
            foreach (var b in KeyIn.Map)
            {
                if (b.Key != key || b.Shift != shift || !b.Ctrl) continue;
                return Gesture(b.Chord, b.Button);
            }
            return null;
        }

        /// <summary>
        /// Session 46. "LB plus RB plus D-pad up" and "LB plus RB plus D-pad
        /// down" said as one gesture, "LB plus RB plus D-pad up or down", when
        /// they differ only in the last word; otherwise both, joined by "or".
        /// Null when either has no button, so the keyboard words are kept.
        /// </summary>
        private static string UpOrDown(string up, string down)
        {
            if (up == null || down == null) return null;
            if (Templated) return Loc.F($"{up} or {down}");   // Session 64
            string U = Fr ? " haut" : " up", D = Fr ? " bas" : " down", OR = Fr ? " ou " : " or ";
            if (up.EndsWith(U, System.StringComparison.Ordinal) && down.EndsWith(D, System.StringComparison.Ordinal)
                && up.Substring(0, up.Length - U.Length) == down.Substring(0, down.Length - D.Length))
                return up + OR + D.Trim();
            return up + OR + down;
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
            if (Templated)
            {
                // Session 64: the other ten languages join a chord their own way.
                string lb = ButtonName(PadInput.PadButton.LB), rb = ButtonName(PadInput.PadButton.RB);
                switch (chord)
                {
                    case KeyIn.Chord.LB:   return Loc.F($"{lb} plus {btn}");
                    case KeyIn.Chord.RB:   return Loc.F($"{rb} plus {btn}");
                    case KeyIn.Chord.Both: { string inner = Loc.F($"{rb} plus {btn}"); return Loc.F($"{lb} plus {inner}"); }
                    default:               return btn;
                }
            }
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
            // Session 64 (found making the pad versions for the other
            // languages): kept as keys. Control plus Space has no button
            // (it was said "Control plus X"), and the Mod Settings line about
            // pressing a button names the keyboard on purpose (it was said
            // "B on the keyboard also cancels").
            @"(?<keep>Control plus Space|\b(?:Backspace|Escape) on the keyboard)" +
            @"|(?<quick>Shift plus 1, 2, or 3)" +
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
            //
            // Session 46 (found with the test driver, 0.7.450, on the pad):
            // not a list of SLOTS either. "Kingfisher, Kingfisher, and River
            // Otter's Waterborne abilities trigger in slots 1, 2, and 3" was
            // spoken as "trigger in slots RB plus D-pad up, right, and down",
            // and the Curious Egg's "Powers met: 1, 2, and 3" would have gone
            // the same way. Shutting out one wrong neighbour at a time is the
            // wrong way round: the ONLY line that names the number keys as a
            // bare list is the cabin's "1, 2 and 3 examine what is in front
            // of you", so the list is rewritten there and nowhere else.
            // Session 64: and the safe's "Press 1, 2, and 3 to interact with
            // the knobs", which was said "Press RB plus D-pad up, 2, and 3".
            @"|(?<nums>\b1, 2,? (?<conj>and|or) 3(?= examine\b| to interact\b))" +
            // Session 46 (same driver run): the review history and Mod
            // Settings are Control chords on the keyboard. "Control plus Up
            // or Down arrow scrolls the log history" was spoken on the pad as
            // "Control plus Up or D-pad down". The pad's own gesture is read
            // from the map in use, so a moved action is named where it is.
            @"|(?<hist>Control plus Up or Down arrow)" +
            // Session 64 (found with the test driver, 0.4.8.023, on the pad):
            // the help list's buffer rows were spoken "Next buffer, Control
            // plus D-pad right". Left and Right are buffer chords too.
            @"|(?<carrow>Control plus (?<cdir>Up|Down|Left|Right) arrow)" +
            @"|(?<cletter>Control plus (?<cl>[A-Z])\b)" +
            @"|(?<turn>\b(?<tk>[AD]) turns you\b)" +
            @"|(?<sarrow>Shift(?: plus | and |\+| )(?<sdir>Up|Down|Left|Right)(?: arrow)?)" +
            @"|(?<sletter>Shift(?: plus | and |\+| )(?<sl>[A-Z])\b)" +
            @"|(?<lr>[Ll]eft and right arrows)(?<v1> " + Verbs + @")?" +
            @"|(?<ud>[Uu]p and down arrows)(?<v2> " + Verbs + @")?" +
            @"|(?<onearrow>\b(?<adir>[Uu]p|[Dd]own|[Ll]eft|[Rr]ight) arrow\b)" +
            @"|(?<arrows>\bArrow keys\b|\bArrows\b|\barrows\b)(?<v3> " + Verbs + @")?" +
            // Session 51: the help list row "Review history as a list, Y."
            // The list has a button now (KeyIn: HistoryList).
            @"|(?<histlist>(?<=as a list, )Y\b)" +
            // Session 64 (same run): the help list said "Help list, F1" and
            // "Silence speech, tap Control" on the pad. Both have buttons.
            @"|(?<silence>\btap Control\b)" +
            @"|(?<named>\b(?:Enter|Space|Backspace|Escape|Tab|F1)\b)" +
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
                string lang = Loc.CurrentLanguage();
                if (lang != "English" && lang != "French") return text;   // 0.4.8.018 - French too
                if (text.Contains("Type a name") || text.Contains("Tapez un nom")) return text;
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
            return OrderFace(OrderDpad(Fr ? ReplaceKeysFr(text) : ReplaceKeys(text)));
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
                + Regex.Escape(y) + "|" + Regex.Escape(b) + @")(?=,| to\b| for\b| pour\b| :)",
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
            @"^(?<chord>(?:\S+ plus )*)(?:D-pad|[Cc]roix) (?<dir>up|right|down|left|haut|droite|bas|gauche)\b(?! and| et)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static int DirRank(string d)
        {
            switch (d.ToLowerInvariant())
            {
                case "up": case "haut": return 0;
                case "right": case "droite": return 1;
                case "down": case "bas": return 2;
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
                if (m.Groups["keep"].Success) return m.Value;   // Session 64
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
                else if (m.Groups["hist"].Success)
                    r = UpOrDown(ForKey(KeyCode.UpArrow, shift: false, ctrl: true),
                                 ForKey(KeyCode.DownArrow, shift: false, ctrl: true));
                else if (m.Groups["carrow"].Success)
                    r = ForKey(Arrow(m.Groups["cdir"].Value), shift: false, ctrl: true);
                else if (m.Groups["cletter"].Success)
                    r = ForKey(Letter(m.Groups["cl"].Value), shift: false, ctrl: true);
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
                else if (m.Groups["histlist"].Success)
                    r = ForKey(KeyCode.Y, shift: false);
                else if (m.Groups["silence"].Success)
                    r = ForKey(SilenceKey.PadToken, shift: false);
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
        // ==================================================================
        // 0.4.8.018 - FRENCH. Zamar, Session 62, on whether the French beta
        // tester plays on a controller: "I do not know but let's get it
        // supported." The same rewrite as ReplaceKeys, over the key words the
        // French lines use (lang/French.tsv): Entrée, Retour arrière, Espace,
        // Échap, Tab, Maj, Ctrl, flèches. Button words are French where French
        // players use them (Croix, Rond, Carré, croix directionnelle, stick
        // droit); Xbox letters and LB/RB stay as printed on the pad. Claude's
        // words, unreviewed, like the rest of the French file.
        // ==================================================================
        internal static bool Fr => Loc.SpokenLanguage == "French";

        private static string ButtonNameFr(PadInput.PadButton b, Family f)
        {
            switch (b)
            {
                case PadInput.PadButton.A:      return f == Family.PlayStation ? "Croix"    : null;
                case PadInput.PadButton.B:      return f == Family.PlayStation ? "Rond"     : null;
                case PadInput.PadButton.X:      return f == Family.PlayStation ? "Carré"    : null;
                case PadInput.PadButton.Y:      return f == Family.PlayStation ? "Triangle" : null;
                case PadInput.PadButton.View:   return f == Family.Switch ? "Moins" : null;
                case PadInput.PadButton.Start:  return f == Family.Switch ? "Plus" : null;
                case PadInput.PadButton.L3:     return f == Family.Switch ? "clic du stick gauche" : null;
                case PadInput.PadButton.R3:     return f == Family.Switch ? "clic du stick droit"  : null;
                case PadInput.PadButton.Up:     return "croix haut";
                case PadInput.PadButton.Down:   return "croix bas";
                case PadInput.PadButton.Left:   return "croix gauche";
                case PadInput.PadButton.Right:  return "croix droite";
                case PadInput.PadButton.RUp:    return "stick droit haut";
                case PadInput.PadButton.RDown:  return "stick droit bas";
                case PadInput.PadButton.RLeft:  return "stick droit gauche";
                case PadInput.PadButton.RRight: return "stick droit droite";
            }
            return null;
        }

        private static readonly Regex KeysFr = new Regex(
            @"(?<keep>Ctrl plus (?:Espace|les flèches)|(?:Retour arrière|Échap) sur le clavier)" +
            @"|(?<quick>Maj plus 1, 2,? ou 3)" +
            @"|(?<pages>\b1, 2,? et 3 changent de page)" +
            @"|(?<convo>(?<=[Aa]ppuyez sur )Espace(?= pour continuer))" +
            @"|(?<drawD>\bD(?=,? (?:pour )?piocher dans votre deck))" +
            @"|(?<drawS>\bS(?=,? (?:pour )?piocher dans la pioche d'écureuils))" +
            @"|(?<nums>\b1, 2,? (?<conj>et|ou) 3(?= examinent\b| pour manipuler\b))" +
            @"|(?<hist>Ctrl plus flèche haut ou bas)" +
            @"|(?<carrow>Ctrl plus flèche (?<cdir>haut|bas|gauche|droite))" +
            @"|(?<cletter>Ctrl plus (?<cl>[A-Z])\b)" +
            @"|(?<turn>\b(?<tk>[AD])\b(?= vous fait pivoter| de 90 degrés))" +
            @"|(?<sarrow>Maj(?: plus | et | )(?:flèche )?(?<sdir>[Hh]aut|[Bb]as|[Gg]auche|[Dd]roite)\b)" +
            @"|(?<sletter>Maj(?: plus | et | )(?<sl>[A-Z])\b)" +
            @"|(?<art>[Ll]es )?(?<lr>[Ff]lèches gauche et droite)(?<v1> (?:parcourent|tournent|déplacent))?" +
            @"|(?<art>[Ll]es )?(?<ud>[Ff]lèches haut et bas)(?<v2> (?:parcourent|tournent|déplacent))?" +
            @"|(?<onearrow>\b[Ff]lèche (?<adir>haut|bas|gauche|droite)\b)" +
            @"|(?<art>\b[Ll]es )?(?<arrows>\b[Ff]lèches\b)(?<v3> (?:parcourent|tournent|déplacent))?" +
            @"|(?<histlist>(?<=sous forme de liste, )Y\b)" +
            @"|(?<silence>appui bref sur Ctrl\b)" +
            @"|(?<named>Retour arrière|Entrée|Espace|Échap|\bTab\b|\bF1\b)" +
            @"|(?<=\b[Aa]ppuyez sur )(?<pressed>[A-Z0-9])\b" +
            @"|\b(?<letter>[A-Z])\b(?=(?: pour\b| :|, | et\b| ou\b| ouvre\b| fait\b| lit\b| compte\b| ferme\b| consulte\b| affiche\b| enregistre\b))",
            RegexOptions.CultureInvariant);

        private static string SingularFr(Group verb)
        {
            if (!verb.Success) return "";
            string v = verb.Value.Trim();
            return " " + (v == "parcourent" ? "parcourt" : v == "tournent" ? "tourne" : "déplace");
        }

        private static string ReplaceKeysFr(string text)
        {
            return KeysFr.Replace(text, m =>
            {
                string r = null;
                if (m.Groups["keep"].Success) return m.Value;   // no button for these yet
                if (m.Groups["quick"].Success)
                    r = ButtonName(PadInput.PadButton.RB) + " plus croix haut, droite ou bas";
                else if (m.Groups["convo"].Success)
                    r = ButtonName(PadInput.PadButton.X);
                else if (m.Groups["drawD"].Success)
                    r = ButtonName(PadInput.PadButton.A);
                else if (m.Groups["drawS"].Success)
                    r = ButtonName(PadInput.PadButton.B);
                else if (m.Groups["pages"].Success)
                    r = ButtonName(PadInput.PadButton.RB) + " passe à la page de droite, "
                      + ButtonName(PadInput.PadButton.LB) + " à la page de gauche";
                else if (m.Groups["nums"].Success)
                    r = ButtonName(PadInput.PadButton.RB) + " plus croix haut, droite " + m.Groups["conj"].Value + " bas";
                else if (m.Groups["hist"].Success)
                    r = UpOrDown(ForKey(KeyCode.UpArrow, shift: false, ctrl: true),
                                 ForKey(KeyCode.DownArrow, shift: false, ctrl: true));
                else if (m.Groups["carrow"].Success)
                    r = ForKey(ArrowFr(m.Groups["cdir"].Value), shift: false, ctrl: true);
                else if (m.Groups["cletter"].Success)
                    r = ForKey(Letter(m.Groups["cl"].Value), shift: false, ctrl: true);
                else if (m.Groups["turn"].Success)
                    r = ForKey(m.Groups["tk"].Value == "A" ? KeyCode.LeftArrow : KeyCode.RightArrow, shift: true);
                else if (m.Groups["sarrow"].Success)
                    r = ForKey(ArrowFr(m.Groups["sdir"].Value), shift: true);
                else if (m.Groups["sletter"].Success)
                    r = ForKey(Letter(m.Groups["sl"].Value), shift: true);
                else if (m.Groups["lr"].Success)
                    r = "croix gauche et droite" + SingularFr(m.Groups["v1"]);
                else if (m.Groups["ud"].Success)
                    r = "croix haut et bas" + SingularFr(m.Groups["v2"]);
                else if (m.Groups["onearrow"].Success)
                    r = "croix " + m.Groups["adir"].Value;
                else if (m.Groups["arrows"].Success)
                    r = "croix directionnelle" + SingularFr(m.Groups["v3"]);
                else if (m.Groups["histlist"].Success)
                    r = ForKey(KeyCode.Y, shift: false);
                else if (m.Groups["silence"].Success)
                    r = ForKey(SilenceKey.PadToken, shift: false);
                else if (m.Groups["named"].Success)
                    r = ForKey(NamedFr(m.Groups["named"].Value), shift: false);
                else if (m.Groups["pressed"].Success)
                    r = ForKey(Letter(m.Groups["pressed"].Value), shift: false);
                else if (m.Groups["letter"].Success)
                    r = ForKey(Letter(m.Groups["letter"].Value), shift: false);
                if (r == null) return m.Value;   // no pad binding: keep the key
                // "Les flèches" is plural; the pad has one cross: "la croix".
                if (m.Groups["art"].Success)
                    r = (char.IsUpper(m.Groups["art"].Value[0]) ? "La " : "la ") + r;
                bool sentenceStart = m.Index == 0
                    || (m.Index >= 2 && text[m.Index - 1] == ' ' && ".!?".IndexOf(text[m.Index - 2]) >= 0);
                if (sentenceStart && r.Length > 0 && char.IsLower(r[0]))
                    r = char.ToUpperInvariant(r[0]) + r.Substring(1);
                return r;
            });
        }

        private static KeyCode ArrowFr(string dir)
        {
            switch (dir.ToLowerInvariant())
            {
                case "haut":   return KeyCode.UpArrow;
                case "bas":    return KeyCode.DownArrow;
                case "gauche": return KeyCode.LeftArrow;
                default:       return KeyCode.RightArrow;
            }
        }

        private static KeyCode NamedFr(string name)
        {
            switch (name)
            {
                case "Entrée":         return KeyCode.Return;
                case "Espace":         return KeyCode.Space;
                case "Retour arrière": return KeyCode.Backspace;
                case "Échap":          return KeyCode.Escape;
                case "F1":             return KeyCode.F1;
                default:               return KeyCode.Tab;
            }
        }

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
                case "F1":        return KeyCode.F1;
                default:          return KeyCode.Tab;
            }
        }

        // ==================================================================
        // Session 64 - THE OTHER TEN LANGUAGES. Zamar, on the pad words for
        // Italian, German, Spanish, Brazilian Portuguese, Turkish, Russian,
        // Japanese, Korean and Chinese: "let's fix that with the translations."
        //
        // English and French rewrite the finished sentence (above), because
        // their key words are known. The other ten word their keys too freely
        // for that ("Pfeiltasten links und rechts", "左右の矢印キー"). So each
        // line that names a key has a PAD VERSION, translated like any other
        // line: lang/pad/<Language>.tsv, "English line<TAB>pad version". The
        // pad version holds [[1]], [[2]] ... where the buttons go; Loc swaps
        // it in when the last press was a pad button (Loc.PadLine), and the
        // buttons are filled from the map in use, as in English.
        //
        // The slots come from the English line: the same Keys pattern, each
        // match in order, each with a fixed number of button slots. The
        // English pad versions handed to the translator are made by
        // TemplateFor below, so the numbering is the same at both ends.
        // ==================================================================

        /// <summary>True when IKMA speaks one of the ten languages with pad versions.</summary>
        internal static bool Templated
        {
            get
            {
                if (_makingTemplates) return false;
                string lang = Loc.SpokenLanguage;
                return lang != null && lang != "English" && lang != "French";
            }
        }

        private static bool _makingTemplates;

        // Printed labels (LB, View, Menu, Options) stay as printed, as in
        // French; the rest are words.
        private static string LocalButton(string name)
        {
            switch (name)
            {
                case "Cross":             return Loc.T("Cross");
                case "Circle":            return Loc.T("Circle");
                case "Square":            return Loc.T("Square");
                case "Triangle":          return Loc.T("Triangle");
                case "Minus":             return Loc.T("Minus");
                case "Plus":              return Loc.T("Plus");
                case "left stick press":  return Loc.T("left stick press");
                case "right stick press": return Loc.T("right stick press");
                case "D-pad up":          return Loc.T("D-pad up");
                case "D-pad down":        return Loc.T("D-pad down");
                case "D-pad left":        return Loc.T("D-pad left");
                case "D-pad right":       return Loc.T("D-pad right");
                case "right stick up":    return Loc.T("right stick up");
                case "right stick down":  return Loc.T("right stick down");
                case "right stick left":  return Loc.T("right stick left");
                case "right stick right": return Loc.T("right stick right");
            }
            return name;
        }

        private const char SLOT = '\u0001';

        /// <summary>One key phrase in an English line: where it is, its pad
        /// words with SLOT where a button goes, and those buttons.</summary>
        private sealed class KeyPiece
        {
            public int Index, Length;
            public string Original, Shape;
            public string[] Buttons;
        }

        /// <summary>
        /// The key phrases of an English line, in order, as ReplaceKeys sees
        /// them (the game's capitals sentences left out, as in ForSpeechCore).
        /// A button is null where the map has none.
        /// </summary>
        private static System.Collections.Generic.List<KeyPiece> Pieces(string english)
        {
            var pieces = new System.Collections.Generic.List<KeyPiece>();
            foreach (Match s in Regex.Matches(english, @"[^.!?]+[.!?]*"))
            {
                int up = 0, low = 0;
                foreach (char ch in s.Value) { if (char.IsUpper(ch)) up++; else if (char.IsLower(ch)) low++; }
                if (up > low) continue;
                foreach (Match m in Keys.Matches(s.Value))
                {
                    var p = new KeyPiece { Index = s.Index + m.Index, Length = m.Length, Original = m.Value };
                    string sl = SLOT.ToString();
                    if (m.Groups["keep"].Success) continue;
                    if (m.Groups["quick"].Success)
                    { p.Shape = sl + " plus D-pad up, right, or down"; p.Buttons = new[] { ButtonName(PadInput.PadButton.RB) }; }
                    else if (m.Groups["convo"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ButtonName(PadInput.PadButton.X) }; }
                    else if (m.Groups["drawD"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ButtonName(PadInput.PadButton.A) }; }
                    else if (m.Groups["drawS"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ButtonName(PadInput.PadButton.B) }; }
                    else if (m.Groups["pages"].Success)
                    {
                        p.Shape = sl + " goes to the next page right, " + sl + " goes to the next page left";
                        p.Buttons = new[] { ButtonName(PadInput.PadButton.RB), ButtonName(PadInput.PadButton.LB) };
                    }
                    else if (m.Groups["nums"].Success)
                    { p.Shape = sl + " plus D-pad up, right, " + m.Groups["conj"].Value + " down"; p.Buttons = new[] { ButtonName(PadInput.PadButton.RB) }; }
                    else if (m.Groups["hist"].Success)
                    {
                        p.Shape = sl;
                        p.Buttons = new[] { UpOrDown(ForKey(KeyCode.UpArrow, shift: false, ctrl: true),
                                                     ForKey(KeyCode.DownArrow, shift: false, ctrl: true)) };
                    }
                    else if (m.Groups["carrow"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ForKey(Arrow(m.Groups["cdir"].Value), shift: false, ctrl: true) }; }
                    else if (m.Groups["cletter"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ForKey(Letter(m.Groups["cl"].Value), shift: false, ctrl: true) }; }
                    else if (m.Groups["turn"].Success)
                    { p.Shape = sl + " turns you"; p.Buttons = new[] { ForKey(m.Groups["tk"].Value == "A" ? KeyCode.LeftArrow : KeyCode.RightArrow, shift: true) }; }
                    else if (m.Groups["sarrow"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ForKey(Arrow(m.Groups["sdir"].Value), shift: true) }; }
                    else if (m.Groups["sletter"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ForKey(Letter(m.Groups["sl"].Value), shift: true) }; }
                    else if (m.Groups["lr"].Success)
                    { p.Shape = "D-pad left and right" + Singular(m.Groups["v1"]); p.Buttons = new string[0]; }
                    else if (m.Groups["ud"].Success)
                    { p.Shape = "D-pad up and down" + Singular(m.Groups["v2"]); p.Buttons = new string[0]; }
                    else if (m.Groups["onearrow"].Success)
                    { p.Shape = "D-pad " + m.Groups["adir"].Value.ToLowerInvariant(); p.Buttons = new string[0]; }
                    else if (m.Groups["arrows"].Success)
                    { p.Shape = "D-pad" + Singular(m.Groups["v3"]); p.Buttons = new string[0]; }
                    else if (m.Groups["histlist"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ForKey(KeyCode.Y, shift: false) }; }
                    else if (m.Groups["silence"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ForKey(SilenceKey.PadToken, shift: false) }; }
                    else if (m.Groups["named"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ForKey(Named(m.Groups["named"].Value), shift: false) }; }
                    else if (m.Groups["pressed"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ForKey(Letter(m.Groups["pressed"].Value), shift: false) }; }
                    else if (m.Groups["letter"].Success)
                    { p.Shape = sl; p.Buttons = new[] { ForKey(Letter(m.Groups["letter"].Value), shift: false) }; }
                    else continue;
                    pieces.Add(p);
                }
            }
            return pieces;
        }

        private static bool SentenceStart(string text, int index)
            => index == 0 || (index >= 2 && text[index - 1] == ' ' && ".!?".IndexOf(text[index - 2]) >= 0);

        /// <summary>
        /// The English pad version of a line, for the translator: the key
        /// phrases in pad words, [[n]] where a button goes. A phrase with no
        /// button in the standard map keeps its key words (its slot number is
        /// still used up, so the numbers match Fill). Null when the line names
        /// no key. Run offline (tools\dev\make_pad_templates.ps1).
        /// </summary>
        internal static string TemplateFor(string english)
        {
            if (string.IsNullOrEmpty(english) || english.Contains("Type a name")) return null;
            _makingTemplates = true;
            try
            {
                var pieces = Pieces(english);
                if (pieces.Count == 0) return null;
                var sb = new System.Text.StringBuilder();
                int at = 0, slot = 0;
                bool changed = false;
                foreach (var p in pieces)
                {
                    sb.Append(english, at, p.Index - at);
                    at = p.Index + p.Length;
                    int first = slot;
                    slot += p.Buttons.Length;
                    if (System.Array.IndexOf(p.Buttons, null) >= 0) { sb.Append(p.Original); continue; }
                    string shape = p.Shape;
                    for (int i = 0; i < p.Buttons.Length; i++)
                    {
                        int k = shape.IndexOf(SLOT);
                        shape = shape.Substring(0, k) + "[[" + (first + i + 1) + "]]" + shape.Substring(k + 1);
                    }
                    if (SentenceStart(english, p.Index) && shape.Length > 0 && char.IsLower(shape[0]))
                        shape = char.ToUpperInvariant(shape[0]) + shape.Substring(1);
                    sb.Append(shape);
                    changed = true;
                }
                sb.Append(english, at, english.Length - at);
                return changed ? sb.ToString() : null;
            }
            finally { _makingTemplates = false; }
        }

        private static readonly Regex SlotToken = new Regex(@"\[\[(\d+)\]\]", RegexOptions.CultureInvariant);

        /// <summary>
        /// A translated pad version with its buttons filled in from the map in
        /// use. Null (so the keyboard words are said) when a slot has no
        /// button or a slot number is not one the English line has.
        /// </summary>
        internal static string Fill(string english, string template)
        {
            try
            {
                var buttons = new System.Collections.Generic.List<string>();
                foreach (var p in Pieces(english)) buttons.AddRange(p.Buttons);
                bool ok = true;
                string filled = SlotToken.Replace(template, m =>
                {
                    int n = int.Parse(m.Groups[1].Value) - 1;
                    if (n < 0 || n >= buttons.Count || buttons[n] == null) { ok = false; return m.Value; }
                    string b = buttons[n];
                    if (SentenceStart(template, m.Index) && b.Length > 0 && char.IsLower(b[0]))
                        b = char.ToUpperInvariant(b[0]) + b.Substring(1);
                    return b;
                });
                return ok ? filled : null;
            }
            catch { return null; }
        }

        /// <summary>True when a line should be said in its pad version.</summary>
        internal static bool PadLineWanted(string english)
        {
            try
            {
                return KeyIn.LastWasPad && KeyIn.PadOwned && !english.Contains("Type a name");
            }
            catch { return false; }
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
