// SetupLoc.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace IKMASetup
{
    /// <summary>
    /// IKMA Setup in the player's language. (Session 32. DRAFT, NEVER RUN.)
    /// </summary>
    /// <remarks>
    /// The same scheme as the mod's own Loc.cs, cut down: the ENGLISH TEXT IS
    /// THE KEY. Every sentence in Text (Program.cs) is wrapped:
    ///     L.T("Looking for Inscryption in your Steam library.")
    ///     L.F($"Found Inscryption at {path}.")
    /// In English both return exactly the old string, so English Setup is
    /// unchanged. Otherwise the sentence is looked up in
    /// tools\installer\lang\&lt;Language&gt;.tsv (English [TAB] translation),
    /// built into IKMA_Manager.exe. Anything missing or broken is printed in
    /// English, never left out.
    ///
    /// WHICH LANGUAGE. Setup runs before the game, so it cannot ask the game.
    /// It follows the language Windows shows its own menus in (the "display
    /// language", CultureInfo.CurrentUICulture). For testing, or for a player
    /// who wants another one:  IKMA_Manager.exe --language French
    /// (the same names as IKMA's [Language] setting).
    ///
    /// PORTUGUESE. The game has only Brazilian Portuguese, so every Portuguese
    /// Windows gets it, as the game would give it.
    /// </remarks>
    internal static class L
    {
        private static string _language;
        private static Dictionary<string, string> _table;

        // Windows' two-letter language code -> the game's language name.
        private static readonly Dictionary<string, string> ByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "fr", "French" }, { "it", "Italian" }, { "de", "German" }, { "es", "Spanish" },
            { "pt", "BrazilianPortuguese" }, { "tr", "Turkish" }, { "ru", "Russian" },
            { "ja", "Japanese" }, { "ko", "Korean" },
        };

        private static readonly string[] Known =
        {
            "English", "French", "Italian", "German", "Spanish", "BrazilianPortuguese", "Turkish",
            "Russian", "Japanese", "Korean", "ChineseSimplified", "ChineseTraditional",
        };

        /// <summary>
        /// Called first thing in Main. A --language NAME argument wins over
        /// Windows' language. Also switches the console to UTF-8 when the
        /// language is not English, so accented letters and Chinese, Japanese,
        /// Korean and Cyrillic text reach the screen reader intact. In English
        /// the console is left exactly as before.
        /// </summary>
        internal static void Start(string[] args)
        {
            string asked = null;
            for (int i = 0; args != null && i + 1 < args.Length; i++)
                if (string.Equals(args[i], "--language", StringComparison.OrdinalIgnoreCase))
                    asked = args[i + 1];

            _language = Match(asked) ?? FromWindows(CultureInfo.CurrentUICulture);
            // Session 34, Zamar: v0.5 is English only, the Manager too, until
            // every language is supported. --language still works for testing.
            if (asked == null) _language = "English";
            _table = _language == "English" ? null : Load(_language);

            if (_table != null)
            {
                try { Console.OutputEncoding = new UTF8Encoding(false); }
                catch { /* an old console that refuses: English letters still print */ }
            }
        }

        /// <summary>A fixed sentence, in the player's language.</summary>
        internal static string T(string english)
        {
            if (_table != null && _table.TryGetValue(english, out string translated)) return translated;
            return english;
        }

        /// <summary>A sentence with blanks, in the player's language.</summary>
        internal static string F(FormattableString line)
        {
            if (_table != null && _table.TryGetValue(line.Format, out string pattern))
            {
                try { return string.Format(CultureInfo.CurrentCulture, pattern, line.GetArguments()); }
                catch (FormatException) { /* a bad translation: English below */ }
            }
            return line.ToString();
        }

        private static string Match(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (string k in Known)
                if (string.Equals(k, name.Trim(), StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }

        private static string FromWindows(CultureInfo culture)
        {
            try
            {
                string code = culture.TwoLetterISOLanguageName;
                if (string.Equals(code, "zh", StringComparison.OrdinalIgnoreCase))
                {
                    // Traditional: zh-Hant, and Taiwan, Hong Kong, Macau.
                    // Everything else Chinese: Simplified.
                    for (CultureInfo c = culture; c != null && !string.IsNullOrEmpty(c.Name); c = c.Parent)
                    {
                        string n = c.Name;
                        if (n.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) ||
                            n.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ||
                            n.EndsWith("-MO", StringComparison.OrdinalIgnoreCase))
                            return "ChineseTraditional";
                        if (c.Parent == c) break;
                    }
                    return "ChineseSimplified";
                }
                return ByCode.TryGetValue(code, out string lang) ? lang : "English";
            }
            catch { return "English"; }
        }

        private static Dictionary<string, string> Load(string lang)
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup_lang/" + lang + ".tsv");
                if (s == null) return null;
                using (var reader = new StreamReader(s, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                        int tab = line.IndexOf('\t');
                        if (tab <= 0) continue;
                        string english = line.Substring(0, tab);
                        string translated = line.Substring(tab + 1);
                        if (translated.Length > 0) table[english] = translated;
                    }
                }
            }
            catch { return null; }
            return table.Count > 0 ? table : null;
        }
    }
}
