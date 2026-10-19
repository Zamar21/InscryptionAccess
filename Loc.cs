// Loc.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace IKMA
{
    /// <summary>The language IKMA speaks in. Set in [Language] Language or in IKMA Setup.</summary>
    /// <remarks>
    /// The names after Game match the game's own Language enum exactly, so
    /// "follow the game" and "pick one" land on the same language files.
    /// </remarks>
    internal enum IkmaLanguage
    {
        Game,
        English, French, Italian, German, Spanish, BrazilianPortuguese, Turkish,
        Russian, Japanese, Korean, ChineseSimplified, ChineseTraditional,
    }

    /// <summary>
    /// Localization: IKMA's own lines in the player's language.
    /// (Session 32. BUILT, NEVER RUN IN THE GAME.)
    /// </summary>
    /// <remarks>
    /// Zamar (Session 32): "Speech in each language supported by the vanilla
    /// game would be the goal." Translations drafted by Claude and marked
    /// unreviewed, his call.
    ///
    /// HOW A LINE IS TRANSLATED. Every spoken string in Vocabulary.cs is
    /// wrapped in one of two calls:
    ///     Loc.T("Loading.")                        a fixed line
    ///     Loc.F($"IKMA version {version} loaded.")  a line with blanks
    /// The ENGLISH TEXT IS THE KEY, the way most translation systems work
    /// ("gettext"). Loc.F receives the interpolated string as a
    /// FormattableString - its English pattern with numbered blanks,
    /// "IKMA version {0} loaded.", plus the values - so a translator sees
    /// and fills the same pattern: "IKMA version {0} chargé." A translator
    /// may reorder the blanks ({1} before {0}) or leave one out.
    ///
    /// ENGLISH IS UNTOUCHED. In English, Loc.T returns its argument and Loc.F
    /// returns exactly what the interpolated string would have produced
    /// (FormattableString.ToString() is the same formatting, same culture).
    /// There is no table and no lookup; it is the pre-Session-32 string.
    ///
    /// WHICH LANGUAGE. [Language] Language, default Game = follow the game's
    /// own language setting. IKMA reads that setting only once the game has
    /// loaded it (GameOptions' own field, never the property that would load
    /// it early), so startup order is exactly as before. Until then: English.
    ///
    /// THE FILES. lang\<Language>.tsv in the repo, built INTO IKMAccess.dll
    /// (so auto-update carries them). One line per string:
    ///     English pattern [TAB] translation
    /// with \t, \n and \\ written as escapes. Lines starting # are comments;
    /// "#status: unreviewed" marks a machine draft. lang\English.tsv is the
    /// full list with empty translations: the file a translator starts from.
    ///
    /// ANYTHING MISSING OR BROKEN FALLS BACK TO ENGLISH, never to silence:
    /// a string with no translation, a translation whose blanks do not fit,
    /// a missing file. A confidently wrong line is worse than silence, but an
    /// English line is neither.
    ///
    /// THE GAME'S OWN WORDS (card names, sigil names, dialogue) are not in
    /// these files: the game translates them itself. See Loc.Game.
    /// </remarks>
    internal static class Loc
    {
        internal static ConfigEntry<IkmaLanguage> Setting;
        private static ManualLogSource _log;

        // The table for the language last asked for; null = English.
        private static string _tableLanguage = "English";
        private static Dictionary<string, string> _table;
        private static readonly HashSet<string> _reportedMissing = new HashSet<string>();

        // GameOptions.optionsData (private static): the game's loaded options,
        // null until the game loads them. Read, never written.
        private static FieldInfo _optionsField;
        private static bool _optionsFieldLooked;

        internal static void BindConfig(ConfigFile config, ManualLogSource log)
        {
            _log = log;
            Setting = config.Bind("Language", "Language", IkmaLanguage.Game,
                "The language IKMA speaks. Game = follow the game's own language setting. " +
                "Every language but English is a draft by Claude that no native speaker has checked yet.");
            // 0.5.0.007 - Session 66: a new pick is heard at once. The half-second
            // cache below let Mod Settings rebuild its labels in the old language.
            Setting.SettingChanged += (s, e) => _cachedLanguage = null;
        }

        /// <summary>A fixed spoken line, in the player's language.</summary>
        internal static string T(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            Dictionary<string, string> table = Table();
            if (table == null) return english;
            string pad = PadLine(english);
            if (pad != null) return pad;
            if (table.TryGetValue(english, out string translated)) return translated;
            ReportMissing(english);
            return english;
        }

        /// <summary>A spoken line with blanks, in the player's language.</summary>
        internal static string F(FormattableString line)
        {
            if (line == null) return null;
            Dictionary<string, string> table = Table();
            if (table == null) return line.ToString();
            string padPattern = PadLine(line.Format);
            if (padPattern != null)
            {
                try { return Elide(string.Format(Plurals.Instance, padPattern, line.GetArguments())); }
                catch (FormatException) { }   // the keyboard version below
            }
            if (table.TryGetValue(line.Format, out string pattern))
            {
                try { return Elide(string.Format(Plurals.Instance, pattern, line.GetArguments())); }
                catch (FormatException)
                {
                    if (_reportedMissing.Add("bad:" + line.Format))
                        _log?.LogWarning($"IKMA LANGUAGE: the {_tableLanguage} pattern for \"{line.Format}\" does not fit its blanks; said in English.");
                }
            }
            else ReportMissing(line.Format);
            return line.ToString();
        }

        // Session 67b: French drops the e of "de" before a vowel or a silent h
        // ("d'Hermine", "d'Écureuil"). A pattern cannot know which card name
        // will fill its blank, so it is done once the line is filled. Card
        // names with a silent h are listed (Hodag, Hrokkall and the game's
        // "Harpie" keep their h).
        private static readonly System.Text.RegularExpressions.Regex _frenchDe =
            new System.Text.RegularExpressions.Regex(
                @"\b([Dd])e (?=[AEIOUÀÂÄÉÈÊËÎÏÔÖÙÛÜŒaeiouàâäéèêëîïôöùûüœ]|Herm|Homm|Hydr|Hyperh|Hameç|herm|homm|hydr|hameç)",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        // Session 67b: a card name that starts with the article ("Le Daus")
        // contracts with "de" and "à" the way French requires: "du Daus",
        // "au Daus", "des"/"aux" for Les. Capital Le only - lowercase "de le"
        // is the pronoun ("de le faire") and stays.
        private static readonly System.Text.RegularExpressions.Regex _frenchDeLe =
            new System.Text.RegularExpressions.Regex(
                @"\b([Dd])e Le(s?) ",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        private static readonly System.Text.RegularExpressions.Regex _frenchALe =
            new System.Text.RegularExpressions.Regex(
                @"(^|\s)([àÀ]) Le(s?) ",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static string Elide(string filled)
        {
            if (_tableLanguage != "French" || string.IsNullOrEmpty(filled))
                return filled;
            filled = _frenchDeLe.Replace(filled, m => m.Groups[1].Value + (m.Groups[2].Value.Length > 0 ? "es " : "u "));
            filled = _frenchALe.Replace(filled, m => m.Groups[1].Value + (m.Groups[2].Value == "À" ? "A" : "a") + (m.Groups[3].Value.Length > 0 ? "ux " : "u "));
            if (filled.IndexOf("e ", StringComparison.Ordinal) < 0) return filled;
            return _frenchDe.Replace(filled, "$1'");
        }

        /// <summary>
        /// The game's own translation of one of its own English words - a
        /// sigil's rulebook name, an item name - exactly as the game shows it
        /// (Localization.Translate). In English, the word unchanged.
        /// </summary>
        internal static string Game(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            if (CurrentLanguage() == "English") return english;

            // SESSION 32 BUG-HUNT FIX. Localization.Translate is not a plain
            // lookup (checked in the IL of Assembly-CSharp.dll):
            //   - it asks GameOptions.Options, which LOADS the options file if
            //     the game has not yet, changing startup order;
            //   - if the game's translations are not loaded yet, it runs
            //     Localization.Initialize, which on a first play WRITES the
            //     options (firstPlaySetSystemLanguage) and saves them.
            // IKMA never writes game state. So the game is asked only once it
            // has done both itself: its options are loaded (the private field
            // is set) and its translation list is filled. Before that, the
            // English word - and nothing is cached, so the next call asks again.
            // The translation is in the GAME's language, which is what its
            // screen shows (visual sync), so the cache follows that language.
            string gameLang = ReadGameLanguage();
            if (gameLang == null || !GameTranslationsLoaded()) return english;
            if (gameLang == "English") return english;
            // Localization.Translate searches every one of the game's ~9,000
            // strings on each call, so each answer is kept per language.
            if (_gameCacheLanguage != gameLang) { _gameCache.Clear(); _gameCacheLanguage = gameLang; }
            if (_gameCache.TryGetValue(english, out string known)) return known;
            string translated;
            try { translated = Localization.Translate(english); }
            catch { translated = english; }
            if (string.IsNullOrEmpty(translated)) translated = english;
            _gameCache[english] = translated;
            return translated;
        }

        /// <summary>
        /// A label the game prints on screen (a menu item, a screen title, a
        /// sigil or item name), in the game's own translation - so a player
        /// hears the word the screen shows (Session 65, Zamar: "switch the
        /// on-screen labels to the game's own translations"). The game prints
        /// many labels in capitals (CARD LOCKED); IKMA speaks them in
        /// ordinary case, so a translation in capitals is lowered to match
        /// the English. In English, and before the game's table is loaded,
        /// the English unchanged.
        /// </summary>
        internal static string GameLabel(string english)
        {
            string s = Game(english);
            // The game has no word of its own here (Japanese "Continue" finds the
            // command-line "continue" first, left in English): IKMA's line instead.
            if (string.IsNullOrEmpty(s) || s == english) return T(english);
            bool allCaps = s.ToUpperInvariant() == s && s.ToLowerInvariant() != s;
            if (allCaps && english.ToUpperInvariant() != english)
            {
                var culture = ReadGameLanguage() == "Turkish"
                    ? System.Globalization.CultureInfo.GetCultureInfo("tr-TR")
                    : System.Globalization.CultureInfo.InvariantCulture;
                string lower = s.ToLower(culture);
                s = char.ToUpper(lower[0], culture) + lower.Substring(1);
            }
            // Session 67: Russian "Continue" finds a lowercase "продолжить"
            // (the command-line one). A capitalised label stays capitalised.
            else if (char.IsUpper(english[0]) && char.IsLower(s[0]))
            {
                var culture = ReadGameLanguage() == "Turkish"
                    ? System.Globalization.CultureInfo.GetCultureInfo("tr-TR")
                    : System.Globalization.CultureInfo.InvariantCulture;
                s = char.ToUpper(s[0], culture) + s.Substring(1);
            }
            return s;
        }

        private static readonly Dictionary<string, string> _gameCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _gameCacheLanguage;

        /// <summary>
        /// A table built from Vocabulary text, rebuilt only when the language
        /// changes. Tables that were "static readonly" would otherwise keep
        /// whatever language was current when first touched - English, if that
        /// was before the game loaded its options - and a screen name frozen in
        /// English would never equal the live, translated one it is compared
        /// with. In English this builds once, exactly like the old field.
        /// </summary>
        internal static T PerLanguage<T>(ref T cache, ref string builtFor, Func<T> build) where T : class
        {
            string lang = CurrentLanguage();
            if (cache == null || builtFor != lang)
            {
                cache = build();
                builtFor = lang;
            }
            return cache;
        }

        // The answer is kept for half a second: every spoken line asks many
        // times, and the language changes only when the player changes it.
        // Environment.TickCount is safe from any thread (Unity's clocks are not).
        private static string _cachedLanguage;
        private static int _cachedAt;
        private const int CacheMilliseconds = 500;

        /// <summary>"English", "French", ... - the game's enum names.</summary>
        internal static string CurrentLanguage()
        {
            string cached = _cachedLanguage;
            int now = Environment.TickCount;
            if (cached != null && unchecked(now - _cachedAt) < CacheMilliseconds) return cached;
            cached = ReadLanguage();
            _cachedLanguage = cached;
            _cachedAt = now;
            _spokenLanguage = cached;
            return cached;
        }

        /// <summary>
        /// The language IKMA last built a spoken line in ("English" before the
        /// first). For the Steam Deck speech helper, which picks a voice for
        /// it (LinuxBridgeBackend). A plain read of a field, so it is safe
        /// from the speech thread, which must never ask the game anything.
        /// </summary>
        internal static string SpokenLanguage => _spokenLanguage ?? "English";

        // SESSION 64 - THE COLON IN LINES IKMA JOINS ITSELF ("Votre plateau:
        // Emplacement 4", "Combat de boss: Prospector", "Capacité: ...").
        // Zamar delegated it ("you decide"). Each language's own colon, as its
        // translated lines already write it: French puts a space before it,
        // Japanese and Chinese use the full-width colon with no space after,
        // every other language ": ". Lines that go through Loc.F carry their
        // colon in the translation and do not use this.
        internal static string Colon
        {
            get
            {
                switch (SpokenLanguage)
                {
                    case "French": return " : ";
                    case "Japanese":
                    case "ChineseSimplified":
                    case "ChineseTraditional": return "\uFF1A";
                    default: return ": ";
                }
            }
        }
        private static volatile string _spokenLanguage;

        // SESSION 66 - THE FULL STOP IKMA ADDS ITSELF. The driver run heard
        // "たき火から離れる。." - a stop added after a Japanese one. A line that
        // already ends in any stop, full-width included, gets no other; else
        // Japanese and Chinese take their own full stop, as Colon above.
        internal static string Stop
        {
            get
            {
                switch (SpokenLanguage)
                {
                    case "Japanese":
                    case "ChineseSimplified":
                    case "ChineseTraditional": return "。";
                    default: return ".";
                }
            }
        }

        internal static bool EndsWithStop(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            switch (s[s.Length - 1])
            {
                case '.': case '!': case '?': case ':': case '…':
                case '。': case '！': case '？': case '：': case '．':
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// For TrimEnd before a line is joined into another: the English stop
        /// and the full-width ones ("たき火から離れる。。" in the same run).
        /// </summary>
        internal static readonly char[] Stops = { '.', '。', '．' };

        /// <summary>The line with a stop after it, unless it already ends in one.</summary>
        internal static string WithStop(string s)
            => string.IsNullOrEmpty(s) || EndsWithStop(s) ? s : s + Stop;

        /// <summary>
        /// Session 34, Zamar: v0.5 speaks ENGLISH ONLY, "until all are
        /// supported later". Localization is a later pre-1.0 release. The
        /// machinery stays; flip this to true when every language is done
        /// and heard. While false, the [Language] setting and the game's Text
        /// Language are both ignored.
        /// </summary>
        internal static readonly bool LocalizationShipped = true;

        /// <summary>
        /// 0.4.8.017 - Zamar, Session 62: "French support would be a great to
        /// have for v.5 as one of my most active testers is playing in french."
        /// The languages IKMA speaks besides English. Any other language - the
        /// game's or the [Language] setting's - speaks English, exactly as
        /// before. Add a language here only with its file built into the DLL
        /// (IKMAccess.csproj) and brought current with the code.
        /// </summary>
        internal static readonly HashSet<string> ShippedLanguages =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "French",
                // Zamar, Session 64: "All 10" for v0.5. Lines added since
                // Session 32 translated by ChatGPT (localization_work\chatgpt_returned).
                "Italian", "German", "Spanish", "BrazilianPortuguese", "Turkish",
                "Russian", "Japanese", "Korean", "ChineseSimplified", "ChineseTraditional",
            };

        internal static bool IsShipped(IkmaLanguage l)
            => l == IkmaLanguage.Game || l == IkmaLanguage.English || ShippedLanguages.Contains(l.ToString());

        /// <summary>
        /// 0.4.8.006 - true when the game's own text comes out in the language
        /// IKMA is speaking. A French tester heard Squirrel In A Bottle's
        /// description in French inside English lines: the game's
        /// ParseCardDefinition translates into the game's language, and the
        /// beta speaks English only. Unknown (options not loaded) counts as a
        /// match, as before.
        /// </summary>
        internal static bool GameTextMatchesSpeech()
        {
            string g = ReadGameLanguage();
            return g == null || g == CurrentLanguage();
        }

        /// <summary>
        /// Session 67 - true once IKMA can tell which language to speak: a
        /// language chosen in IKMA's own setting, or the game's options
        /// loaded. A Russian boot heard "IKMA version 0.5.0.016 loaded." in
        /// English because Awake runs before the game reads its options.
        /// Forgets the cached language when it answers yes, so the next line
        /// is built in the language just learned.
        /// </summary>
        internal static bool LanguageKnown()
        {
            bool known = !LocalizationShipped
                || (Setting != null && Setting.Value != IkmaLanguage.Game)
                || ReadGameLanguage() != null;
            if (known) _cachedLanguage = null;
            return known;
        }

        private static string ReadLanguage()
        {
            if (!LocalizationShipped) return "English";
            IkmaLanguage chosen = Setting != null ? Setting.Value : IkmaLanguage.Game;
            string lang = chosen != IkmaLanguage.Game ? chosen.ToString() : (ReadGameLanguage() ?? "English");
            return ShippedLanguages.Contains(lang) ? lang : "English";
        }

        /// <summary>
        /// The game's own language setting, or null while the game has not
        /// loaded its options yet. Reads GameOptions' private field, never the
        /// Options property (which would load them early).
        /// </summary>
        private static string ReadGameLanguage()
        {
            try
            {
                if (!_optionsFieldLooked)
                {
                    _optionsFieldLooked = true;
                    _optionsField = typeof(GameOptions).GetField("optionsData", BindingFlags.NonPublic | BindingFlags.Static);
                }
                var options = _optionsField?.GetValue(null) as OptionsSaveData;
                return options?.language.ToString();
            }
            catch { return null; }
        }

        // Localization.translations (private static List): the game's loaded
        // translations. Its own IsInitialized() is exactly "Count > 0"; that
        // method is private too, so the list is read the same way. Read only.
        private static FieldInfo _translationsField;
        private static bool _translationsFieldLooked;

        private static bool GameTranslationsLoaded()
        {
            try
            {
                if (!_translationsFieldLooked)
                {
                    _translationsFieldLooked = true;
                    _translationsField = typeof(Localization).GetField("translations", BindingFlags.NonPublic | BindingFlags.Static);
                }
                return _translationsField?.GetValue(null) is System.Collections.ICollection list && list.Count > 0;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        // Session 64 - PAD VERSIONS for the ten languages after French (see
        // PadWords, "THE OTHER TEN LANGUAGES"): lang/pad/<Language>.tsv,
        // "English line<TAB>pad version with [[n]] button slots".
        // ------------------------------------------------------------------
        private static string _padLanguage;
        private static Dictionary<string, string> _padTable;

        /// <summary>The line's pad version, buttons filled in, or null.</summary>
        private static string PadLine(string english)
        {
            string lang = _tableLanguage;
            if (lang == "English" || lang == "French") return null;
            if (lang != _padLanguage)
            {
                _padLanguage = lang;
                _padTable = LoadPad(lang);
            }
            if (_padTable == null || _padTable.Count == 0) return null;
            if (!_padTable.TryGetValue(english, out string template)) return null;
            if (!PadWords.PadLineWanted(english)) return null;
            return PadWords.Fill(english, template);
        }

        private static Dictionary<string, string> LoadPad(string lang)
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                Stream s = typeof(Loc).Assembly.GetManifestResourceStream("lang/pad/" + lang + ".tsv");
                if (s == null) return table;
                int bad = 0;
                using (var reader = new StreamReader(s, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                        int tab = line.IndexOf('\t');
                        if (tab <= 0) continue;
                        string english = Unescape(line.Substring(0, tab));
                        string pad = Unescape(line.Substring(tab + 1));
                        if (pad.Length == 0) continue;
                        if (!BlanksFit(english, pad)) { bad++; continue; }
                        table[english] = pad;
                    }
                }
                _log?.LogInfo($"IKMA LANGUAGE: {table.Count} {lang} lines have a pad version.");
                if (bad > 0)
                    _log?.LogWarning($"IKMA LANGUAGE: {bad} {lang} pad versions use blanks their English does not have; those keep their keyboard words.");
            }
            catch (Exception e)
            {
                _log?.LogWarning($"IKMA LANGUAGE: could not read the {lang} pad versions ({e.Message}); keyboard words on the pad.");
            }
            return table;
        }

        private static Dictionary<string, string> Table()
        {
            string lang = CurrentLanguage();
            if (lang == "English") return null;
            if (lang != _tableLanguage)
            {
                _tableLanguage = lang;
                _table = Load(lang);
            }
            return _table;
        }

        private static Dictionary<string, string> Load(string lang)
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                Stream s = typeof(Loc).Assembly.GetManifestResourceStream("lang/" + lang + ".tsv");
                if (s == null)
                {
                    _log?.LogWarning($"IKMA LANGUAGE: no {lang} file is built into IKMA; speaking English.");
                    return table;
                }
                bool unreviewed = false;
                int bad = 0;
                using (var reader = new StreamReader(s, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("#", StringComparison.Ordinal))
                        {
                            if (line.Trim() == "#status: unreviewed") unreviewed = true;
                            continue;
                        }
                        int tab = line.IndexOf('\t');
                        if (tab <= 0) continue;
                        string english = Unescape(line.Substring(0, tab));
                        string translated = Unescape(line.Substring(tab + 1));
                        if (translated.Length == 0) continue;           // not translated yet
                        if (!BlanksFit(english, translated)) { bad++; continue; }
                        table[english] = translated;
                    }
                }
                _log?.LogInfo($"IKMA LANGUAGE: speaking {lang}, {table.Count} lines translated.");
                if (unreviewed)
                    _log?.LogInfo($"IKMA LANGUAGE: the {lang} lines are a draft by Claude that no {lang} speaker has checked yet.");
                if (bad > 0)
                    _log?.LogWarning($"IKMA LANGUAGE: {bad} {lang} lines use blanks their English does not have; those are said in English.");
            }
            catch (Exception e)
            {
                _log?.LogWarning($"IKMA LANGUAGE: could not read the {lang} file ({e.Message}); speaking English.");
            }
            return table;
        }

        private static void ReportMissing(string english)
        {
            if (_reportedMissing.Add(english))
                _log?.LogInfo($"IKMA LANGUAGE: no {_tableLanguage} for \"{english}\"; said in English.");
        }

        /// <summary>
        /// A translation may use only the blanks its English has: {3} in a
        /// translation of a line with blanks {0} and {1} would throw.
        /// </summary>
        internal static bool BlanksFit(string english, string translated)
            => HighestBlank(translated) <= HighestBlank(english);

        private static int HighestBlank(string pattern)
        {
            int highest = -1;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] != '{') continue;
                if (i + 1 < pattern.Length && pattern[i + 1] == '{') { i++; continue; }   // "{{" is a literal brace
                int n = 0, j = i + 1;
                bool digits = false;
                while (j < pattern.Length && char.IsDigit(pattern[j])) { n = n * 10 + (pattern[j] - '0'); j++; digits = true; }
                if (digits && n > highest) highest = n;
            }
            return highest;
        }

        internal static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
    /// <summary>
    /// Session 67: a count's noun in a language file, "{0} {0:pl:карта|карты|карт}".
    /// The forms are one|few|many (Russian: 1, 21 / 2-4, 22-24 / 5-20, 11-14)
    /// or one|other everywhere else. Any other format string is left to the
    /// value itself, so every existing pattern formats exactly as before.
    /// </summary>
    internal sealed class Plurals : IFormatProvider, ICustomFormatter
    {
        internal static readonly Plurals Instance = new Plurals();

        public object GetFormat(Type t) => t == typeof(ICustomFormatter) ? this : null;

        public string Format(string format, object arg, IFormatProvider provider)
        {
            if (format != null && format.StartsWith("pl:", StringComparison.Ordinal))
            {
                string[] forms = format.Substring(3).Split('|');
                long n;
                try { n = Math.Abs(Convert.ToInt64(arg)); } catch { return arg?.ToString() ?? ""; }
                if (forms.Length >= 3)
                {
                    long ten = n % 10, hundred = n % 100;
                    if (ten == 1 && hundred != 11) return forms[0];
                    if (ten >= 2 && ten <= 4 && (hundred < 12 || hundred > 14)) return forms[1];
                    return forms[2];
                }
                if (forms.Length == 2) return n == 1 ? forms[0] : forms[1];
                return forms[0];
            }
            if (arg is IFormattable f) return f.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
            return arg?.ToString() ?? "";
        }
    }
}
