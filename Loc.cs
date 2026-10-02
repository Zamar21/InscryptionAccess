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
        }

        /// <summary>A fixed spoken line, in the player's language.</summary>
        internal static string T(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            Dictionary<string, string> table = Table();
            if (table == null) return english;
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
            if (table.TryGetValue(line.Format, out string pattern))
            {
                try { return string.Format(pattern, line.GetArguments()); }
                catch (FormatException)
                {
                    if (_reportedMissing.Add("bad:" + line.Format))
                        _log?.LogWarning($"IKMA LANGUAGE: the {_tableLanguage} pattern for \"{line.Format}\" does not fit its blanks; said in English.");
                }
            }
            else ReportMissing(line.Format);
            return line.ToString();
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
        private static volatile string _spokenLanguage;

        /// <summary>
        /// Session 34, Zamar: v0.5 speaks ENGLISH ONLY, "until all are
        /// supported later". Localization is a later pre-1.0 release. The
        /// machinery stays; flip this to true when every language is done
        /// and heard. While false, the [Language] setting and the game's Text
        /// Language are both ignored.
        /// </summary>
        internal static readonly bool LocalizationShipped = false;   // readonly, not const: a const false makes the rest unreachable (warning CS0162)

        private static string ReadLanguage()
        {
            if (!LocalizationShipped) return "English";
            IkmaLanguage chosen = Setting != null ? Setting.Value : IkmaLanguage.Game;
            if (chosen != IkmaLanguage.Game) return chosen.ToString();
            return ReadGameLanguage() ?? "English";
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
}
