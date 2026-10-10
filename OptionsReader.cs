// OptionsReader.cs
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// The Options screen, reached from the Escape menu. (Session 17.)
    ///
    /// Zamar: "After pressing enter on options it should stomp the old info and
    /// read Options Menu and have the context switch over to it. I want arrow
    /// keys, enter, backspace, space, and H key working in this menu."
    ///
    /// A CONTROL THAT HOLDS A VALUE, which is a shape IKMA has never read.
    /// Every other screen in this mod is a list of discrete things — cards, menu
    /// options, map paths. Here a row has a SETTING as well as a name, and left
    /// and right have to change it. The safe dials were the same shape one floor
    /// down, and the same rule applies: read the value the game holds, never a
    /// count IKMA keeps.
    ///
    /// ROWS ARE GROUPED BY PARENT, NOT GUESSED AT BY POSITION. The 0.7.82 survey
    /// showed the panel is built from GBC.GenericUIButtons named Button_Minus,
    /// Button_Plus, Button_Left, Button_Right, SliderIncrement_0..5 and
    /// CardButton, with the row's label on a separate text object. Buttons that
    /// share a parent are one row, which is how the screen is actually built —
    /// grouping by y-coordinate would break the moment a row moved.
    ///
    /// VALUES COME FROM GameOptions.Options, which is PUBLIC STATIC and returns
    /// an OptionsSaveData carrying masterVolume, musicVolume,
    /// screenshakeDisabled, flickeringDisabled and gamma. Anything this cannot
    /// resolve reads its name alone rather than a made-up value.
    ///
    /// TOGGLES ARE SPOKEN THE WAY THE SCREEN READS THEM, not the way the game
    /// stores them. His call: the screen says SCREEN SHAKE, so an enabled shake
    /// is "On" even though the field is `screenshakeDisabled`. What a blind
    /// player hears has to match what a sighted player sees.
    /// </summary>
    public static class OptionsReader
    {
        private static ManualLogSource _log;
        public static void Init(ManualLogSource log) => _log = log;

        // THE CURSOR STARTS NOWHERE. (0.7.234.) Zamar, on the options pages:
        // "need the -1 default thing on audio settings screen." Same fault
        // MenuReader and NodeScreenReader already fixed, and the comment on
        // Announce below had already described it without naming it: arrival
        // reads the first row, the cursor is ALSO on the first row, so the
        // first arrow press moves off the only row the player was told about.
        // Arrival is a preview. The player is not standing anywhere until they
        // press an arrow, and then the first press lands ON row one.
        private const int NOWHERE = -1;
        private static int  _index = NOWHERE;
        private static bool _announced;

        // ==================================================================
        // TWO LEVELS INSIDE A PAGE. (Session 17, his spec.)
        //
        //   Browsing  — arrows move between rows.
        //   Adjusting — arrows change the highlighted setting.
        //
        // Zamar: "Each setting should require being highlighted, pressing enter
        // to select it, having the arrow keys either go up in value or down in
        // value (or toggle on/off) then requiring them to backspace to deselect
        // the option and return context to the options menu."
        //
        // WHY THE EXTRA STEP IS RIGHT, and it is not just a preference: with one
        // level the same arrow key means "next setting" on one row and "louder"
        // on the next, decided by whether that row happens to have a minus
        // button. 0.7.97 did exactly that and answered "That setting cannot be
        // changed with the arrows" — a key whose meaning depends on invisible
        // state. Selecting first makes the mode explicit and audible.
        // ==================================================================
        private static bool _adjusting;

        // ==================================================================
        // THE DISPLAY CHANGING UNDER YOU. (0.7.105.)
        //
        // Zamar: "Changing to Windowed while in Fullscreen, and then hitting
        // back or escape, switches to windowed without confirming. (It
        // auto-applies). We need to read that as a medium prio info callout."
        //
        // The game applies the pending video options when the panel closes, so
        // the change lands AFTER the reader has let go of the screen — there is
        // no keypress to hang the line off. It is watched for instead: the
        // engine is asked every frame what the window is doing, and a change
        // announces itself whoever caused it. Apply, closing the panel, alt-enter
        // and a change made with the mouse all read the same, because the source
        // is the window and not IKMA's idea of what the player pressed.
        //
        // QUEUED AS AN ACTION, not spoken over what is playing. It is the result
        // of something the player did rather than a running commentary, so it
        // survives a commentary drop — but it does not stomp a line mid-word for
        // a change they can already see happening.
        // ==================================================================
        private static bool _displayKnown;
        private static bool _wasFullscreen;
        private static int  _wasWidth, _wasHeight;

        public static void TickDisplay()
        {
            bool full; int w, h;
            try { full = Screen.fullScreen; w = Screen.width; h = Screen.height; }
            catch { return; }

            if (!_displayKnown)
            {
                _displayKnown  = true;
                _wasFullscreen = full;
                _wasWidth = w; _wasHeight = h;
                return;
            }

            if (full == _wasFullscreen && w == _wasWidth && h == _wasHeight) return;

            bool modeChanged = full != _wasFullscreen;
            bool sizeChanged = w != _wasWidth || h != _wasHeight;

            _wasFullscreen = full;
            _wasWidth = w; _wasHeight = h;

            string line = Vocabulary.Options.DisplayIsNowOrDisplayResolutionChanged(modeChanged, full);

            if (sizeChanged) line += Vocabulary.Options.ResolutionChanged(w, h);

            _log?.LogInfo($"IKMA OPTIONS: display changed — fullScreen={full}, {w}x{h}.");
            Speech.Result(line + ".");
        }

        public static void Reset()
        {
            // 0.4.8.012 - a setting let go by anything but Backspace is worth
            // one log line; it is how a page switch under a held setting
            // showed up.
            if (_adjusting) _log?.LogInfo("IKMA OPTIONS: reset while a setting was held.");
            _index = NOWHERE;
            _announced = false;
            _adjusting = false;
            ReleaseHover();
        }

        /// <summary>
        /// The name the NEXT announcement should use.
        ///
        /// Turning to a page used to speak "Display settings" and then be
        /// stomped a frame later by "Options Menu", because the page switch and
        /// the re-announce are two different layers and the second one did not
        /// know a page had been named. Zamar: "Turning to the page should say
        /// Game Settings, Display, or Audio. All 3 just read Options Menu."
        ///
        /// One announcement, named by whoever caused it. Consumed when spoken,
        /// so an announce with no cause falls back to the screen's own name.
        /// </summary>
        private static string _pendingName;

        public static void SetPendingName(string name) { _pendingName = name; }

        public static bool Adjusting => _adjusting;

        // ==================================================================

        public sealed class Row
        {
            public string Label;
            public Transform Parent;
            public readonly List<GBC.GenericUIButton> Buttons = new List<GBC.GenericUIButton>();

            public GBC.GenericUIButton Decrease;   // Minus / Left
            public GBC.GenericUIButton Increase;   // Plus / Right
            public GBC.GenericUIButton Action;     // anything else — a plain press
        }

        /// <summary>
        /// Every row on the page the panel is currently showing.
        ///
        /// Ordered top to bottom the way the screen reads, then left to right —
        /// the same ordering rule the map paths needed twice before it stuck.
        /// </summary>
        // ------------------------------------------------------------------
        // WHERE THE PANEL HANGS. (0.7.232.)
        //
        // Zamar: "The options screen should work in the base game main menu
        // though." It did not, and the reason was one line: this reader asked
        // PauseMenu.instance for the panel's root. There is no PauseMenu on the
        // title screen — the same options prefab hangs off
        // MenuController.optionsUI instead — so Rows() came back empty and the
        // screen opened silent.
        //
        // The override is set once, by TitleScreenReader, when it captures the
        // title screen's MenuController, and cleared on scene load. It only
        // WINS while the object is actually on screen, so in-game the pause
        // menu keeps answering exactly as before and nothing had to learn which
        // screen it is on.
        // ------------------------------------------------------------------
        private static GameObject _panelRootOverride;

        public static void SetPanelRoot(GameObject root)
        {
            _panelRootOverride = root;
            if (root != null)
                _log?.LogInfo($"IKMA OPTIONS: panel root set to '{root.name}'.");
        }

        /// <summary>The object the options panel lives under, whichever screen
        /// is showing it.</summary>
        public static GameObject PanelRoot()
        {
            try
            {
                if (_panelRootOverride != null && _panelRootOverride.activeInHierarchy)
                    return _panelRootOverride;
            }
            catch { }

            var pm = PauseMenu.instance;
            return pm == null ? null : pm.gameObject;
        }

        public static List<Row> Rows()
        {
            var rows = new List<Row>();

            var root = PanelRoot();
            if (root == null) return rows;

            var byParent = new Dictionary<Transform, Row>();

            try
            {
                foreach (var b in root.GetComponentsInChildren<GBC.GenericUIButton>(false))
                {
                    if (b == null) continue;

                    string nm = null;
                    try { nm = b.gameObject.name; } catch { }
                    if (nm == null) continue;

                    // The page tabs are their own control (1 to 4) and are not
                    // rows on the page.
                    if (nm.StartsWith("Tab_")) continue;

                    Transform parent = null;
                    try { parent = b.transform.parent; } catch { }
                    if (parent == null) continue;

                    Row row;
                    if (!byParent.TryGetValue(parent, out row))
                    {
                        row = new Row { Parent = parent };
                        byParent[parent] = row;
                        rows.Add(row);
                    }
                    row.Buttons.Add(b);

                    string lower = nm.ToLowerInvariant();
                    if (lower.Contains("minus") || lower.Contains("_left"))       row.Decrease = b;
                    else if (lower.Contains("plus") || lower.Contains("_right"))  row.Increase = b;
                    else if (row.Action == null)                                  row.Action   = b;
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA OPTIONS: reading rows threw {e.GetType().Name}.");
            }

            // NO CONTAINER FILTER ANY MORE. TabGroup_Gameplay was excluded as
            // scenery at 0.7.98 and it was really the RESET SAVE DATA button's
            // row — Zamar: "The Reset save Data option is currently not
            // selectable." Labelling from the button's own text tells the two
            // apart without throwing a real control away.

            foreach (var r in rows) r.Label = LabelFor(r);

            // SORTED ON THE BUTTONS, NOT ON THE PARENT. (0.7.101.)
            //
            // Two rows on his pages hang off a PAGE CONTAINER rather than a box
            // of their own — RESET SAVE DATA under TabGroup_Gameplay, APPLY
            // under TabGroup_Video — and a container's transform sits at the top
            // of the page whatever its button does. So both sorted to the top
            // while sitting at the bottom of the screen. Zamar: "Reset Saved
            // Data is before Language in the options list for some reason,
            // should be after", and "Apply seems like it's after noise effect
            // but should be after brightness."
            //
            // A row's buttons are where the row actually IS. Reading order is
            // what a sighted player's eye does, so it has to follow the pixels
            // and not the hierarchy.
            rows.Sort((a, b) =>
            {
                float ay = TopY(a), by = TopY(b);
                int topDown = by.CompareTo(ay);          // higher y first
                return topDown != 0 ? topDown : LeftX(a).CompareTo(LeftX(b));
            });

            return rows;
        }

        /// <summary>
        /// The row's printed name. Searched on the row's own object first, then
        /// its parent — the label is a sibling of the buttons rather than a
        /// child of them, which is why hunting the button alone found nothing in
        /// the 0.7.82 survey.
        /// </summary>
        private static string LabelFor(Row row)
        {
            // AN OVERRIDE FIRST, where the screen's own text is the VALUE rather
            // than a name. The language row prints "English", which is what is
            // selected, not what the row is — Zamar: "The English box should
            // read Text Language not English."
            string objName = null;
            try { objName = row.Parent.gameObject.name; } catch { }

            string forced;
            if (objName != null &&
                _labelOverrides.TryGetValue(objName.Trim().ToLowerInvariant(), out forced))
                return forced;

            // THE GAME'S LANGUAGE BUTTON. (Session 34, his words from Session
            // 33.) After Text Language is changed, OptionsUI shows a button
            // printed in the NEW language (LocalizedLanguageNames.
            // SET_LANGUAGE_BUTTON_TEXT, e.g. "日本語でリセット"). Its text is
            // matched against the game's own table, and spoken as "Reset with
            // [language]" in IKMA's language. Not cached: the text changes with
            // every language picked.
            string live = ScanLabel(row);
            if (!string.IsNullOrEmpty(live))
            {
                var texts = LocalizedLanguageNames.SET_LANGUAGE_BUTTON_TEXT;
                for (int i = 0; i < texts.Length; i++)
                    if (string.Equals(texts[i], live.Trim(), System.StringComparison.Ordinal))
                        return Vocabulary.Options.ResetWith(LanguageName(LocalizedLanguageNames.NAMES[i]));
            }

            // THE BUTTON'S OWN TEXT BEFORE THE PARENT'S. (0.7.100.)
            //
            // Searching the parent first made two rows on a page share a label,
            // because sibling rows sit under one container carrying the text
            // above them — and it hid RESET SAVE DATA entirely, since that
            // button's row looked like a container and got filtered out.
            //
            // A button that carries its own words is named by them; only a
            // control with no text of its own (a plus, a minus) falls back to
            // the row it sits in.
            return Remember(objName, ScanLabel(row));
        }

        /// <summary>Where the row sits on screen — its topmost button.</summary>
        private static float TopY(Row row)
        {
            float best = float.NegativeInfinity;
            if (row != null)
                foreach (var b in row.Buttons)
                {
                    try { if (b != null && b.transform.position.y > best) best = b.transform.position.y; }
                    catch { }
                }
            if (best > float.NegativeInfinity) return best;

            try { return row.Parent.position.y; } catch { return 0f; }
        }

        private static float LeftX(Row row)
        {
            float best = float.PositiveInfinity;
            if (row != null)
                foreach (var b in row.Buttons)
                {
                    try { if (b != null && b.transform.position.x < best) best = b.transform.position.x; }
                    catch { }
                }
            if (best < float.PositiveInfinity) return best;

            try { return row.Parent.position.x; } catch { return 0f; }
        }

        private static string ScanLabel(Row row)
        {
            foreach (var b in row.Buttons)
            {
                string own = TextUnder(b == null ? null : b.transform);
                if (own != null) return own;
            }

            string found = TextUnder(row.Parent);
            if (found != null) return found;

            try
            {
                if (row.Parent != null && row.Parent.parent != null)
                    found = TextUnder(row.Parent.parent);
            }
            catch { }

            return found;
        }

        /// <summary>
        /// The name a row keeps, once it has been read cleanly.
        ///
        /// THE SCREEN TYPES ITS LABELS OUT ONE LETTER AT A TIME and re-runs the
        /// effect whenever a value changes. His 0.7.99 log is the proof: one
        /// slider named itself 'DI', then 'DIALOGUE TE', then
        /// 'DIALOGUE TEXT SPEED' — and, on the frames where the label was
        /// momentarily EMPTY and the scan fell through to the next text under
        /// the same parent, 'RESET SAVE DATA'. A label read live is a label
        /// read mid-animation.
        ///
        /// So a row's name is captured once and then KEPT. The only change ever
        /// accepted is GROWTH OF THE SAME STRING — 'DIA' becoming
        /// 'DIALOGUE TEXT SPEED' is the typewriter finishing, and is an
        /// upgrade. Anything else is another object's text bleeding in and is
        /// refused.
        ///
        /// This is the settle rule in a different shape. Everywhere else in
        /// this mod IKMA waits for a value to stop changing before speaking it;
        /// here the change is a redraw rather than a load, so the wait becomes
        /// a cache. Object names are unique per control, so the cache is never
        /// cleared and a page switch cannot pick up the previous page's words.
        /// </summary>
        private static readonly Dictionary<string, string> _labelCache
            = new Dictionary<string, string>();

        private static string Remember(string objName, string candidate)
        {
            if (string.IsNullOrEmpty(objName)) return candidate;

            string kept;
            bool have = _labelCache.TryGetValue(objName, out kept);

            if (string.IsNullOrEmpty(candidate)) return have ? kept : null;
            if (!have) { _labelCache[objName] = candidate; return candidate; }

            if (candidate.Length > kept.Length &&
                candidate.StartsWith(kept, System.StringComparison.Ordinal))
            {
                _labelCache[objName] = candidate;
                return candidate;
            }

            return kept;
        }

        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _labelOverrides => Loc.PerLanguage(ref _labelOverridesCache, ref _labelOverridesLanguage, Build_labelOverrides);
        private static Dictionary<string, string> Build_labelOverrides() =>
            new Dictionary<string, string>
        {
            { "incrementalfield_language", Vocabulary.Options.TextLanguage },
        };
        private static Dictionary<string, string> _labelOverridesCache;
        private static string _labelOverridesLanguage;

        /// <summary>
        /// The SECOND piece of text on a row — what the screen prints as the
        /// current setting. "1920 x 1080" under RESOLUTION, "English" under the
        /// language field.
        ///
        /// This is the honest source for anything the save data does not hold,
        /// and for anything that has been CHANGED BUT NOT APPLIED: resolution
        /// and graphics quality only reach Unity after APPLY, so reading the
        /// engine reports the old value while the screen already shows the new
        /// one. The screen is what the player sees, so the screen wins.
        /// </summary>
        private static string PrintedValue(Row row)
        {
            if (row?.Parent == null) return null;

            string label = row.Label;
            try
            {
                foreach (var c in row.Parent.GetComponentsInChildren<Component>(false))
                {
                    string v = TextOf(c);
                    if (string.IsNullOrEmpty(v)) continue;
                    v = v.Trim();
                    if (v.Length == 0) continue;
                    if (v.Replace("-", "").Trim().Length == 0) continue;
                    if (!string.IsNullOrEmpty(label) &&
                        string.Equals(v, label, System.StringComparison.OrdinalIgnoreCase)) continue;
                    return v;
                }
            }
            catch { }
            return null;
        }

        private static string TextUnder(Transform t)
        {
            if (t == null) return null;
            try
            {
                foreach (var c in t.GetComponentsInChildren<Component>(false))
                {
                    string v = TextOf(c);
                    if (string.IsNullOrEmpty(v)) continue;
                    v = v.Trim();
                    if (v.Length == 0) continue;
                    if (v.Replace("-", "").Trim().Length == 0) continue;   // divider rule
                    return v;
                }
            }
            catch { }
            return null;
        }

        private static string TextOf(Component c) => UiText.Of(c);

        // ==================================================================
        // VALUES
        //
        // Label -> the field on OptionsSaveData, and whether the stored bool is
        // the OPPOSITE of what the screen prints. `screenshakeDisabled` true
        // means the screen's SCREEN SHAKE row reads Off.
        // ==================================================================
        private sealed class Binding
        {
            public string Field;
            public bool   Inverted;

            // THE STORED NUMBER IS NOT ALWAYS THE PRINTED ONE. Zamar, 0.7.98:
            // "The lowest is not zero, it's one. The highest is six not 5."
            // dialogueSpeed is a zero-based index into GameOptions.DIALOGUE_TIERS
            // and the screen prints it one-based. Same class of mistake as the
            // safe dial, where the stored position was not the number on the
            // face — and the same rule: speak what the player can see.
            //
            // Applied per binding rather than globally: the volumes were NOT
            // reported wrong, so nothing is shifted that he has not checked.
            public int Offset;

            // THE VALUE THE GAME SHIPS WITH, as the screen PRINTS it — after
            // Offset, not before. (0.7.236.) Zamar: "6 should read 6 default.
            // Same for all 3 audio settings."
            //
            // Taken from OptionsSaveData's own field initializers, which is the
            // only place the game states a default: masterVolume, musicVolume
            // and soundVolume all start at 6; gamma starts at 0; dialogueSpeed
            // starts at 3, which the screen prints as 4 because the field is a
            // zero-based index into GameOptions.DIALOGUE_TIERS. So the printed
            // default for DIALOGUE TEXT SPEED is FOUR, not the six he was
            // sitting on when he asked — see the note where it is set.
            //
            // Null where the game states no default, which is most rows: a
            // setting whose default nobody has written down does not get one
            // invented for it.
            public int? Default;

            public Binding(string f, bool inv, int offset = 0, int? dflt = null)
            { Field = f; Inverted = inv; Offset = offset; Default = dflt; }
        }

        // KEYED ON THE ROW'S OBJECT NAME, NOT ITS LABEL. (0.7.98.)
        //
        // His 0.7.97 log named every row, and the objects turn out to be far
        // better keys than the printed text: IncrementalSlider_SFXVolume,
        // LargeToggle_Fullscreen, IncrementalField_Quality. Two rows on a page
        // shared a LABEL — the label hunt walks up to the parent and siblings
        // can share the text above them — so keying on labels was ambiguous by
        // construction. Object names are unique and say what the control is.
        //
        // The fields come from the same log, which printed everything
        // OptionsSaveData actually holds rather than what a name search guessed.
        private static readonly Dictionary<string, Binding> _bindings
            = new Dictionary<string, Binding>
        {
            // DEFAULTS ARE READ OFF OptionsSaveData, NOT GUESSED. The three
            // volumes ship at 6 and print 6. dialogueSpeed ships at 3 and
            // prints 4 — Zamar asked for "6, default" on this row while his own
            // save sat at 6, but the game's default here is four, and telling a
            // blind player that six is the default would be the mod stating
            // something the screen does not. Four is marked instead; say the
            // word and the number changes.
            { "incrementalslider_textspeed",   new Binding("dialogueSpeed",       false, 1, 4) },
            { "incrementalslider_mastervolume",new Binding("masterVolume",        false, 0, 6) },
            { "incrementalslider_musicvolume", new Binding("musicVolume",         false, 0, 6) },
            { "incrementalslider_sfxvolume",   new Binding("soundVolume",         false, 0, 6) },
            { "incrementalslider_gamma",       new Binding("gamma",               false, 0, 0) },
            { "incrementalfield_language",     new Binding("language",            false) },

            // INVERTED: the field records what is switched OFF, the row prints
            // the thing itself. SCREEN SHAKE reads On when shaking happens.
            { "largetoggle_flickering",        new Binding("flickeringDisabled",  true)  },
            { "largetoggle_screenshake",       new Binding("screenshakeDisabled", true)  },

            // Also inverted, and this one is easy to get backwards: the row says
            // PAUSE WHEN WINDOW NOT FOCUSED, and the field is runInBackground.
            // Running in the background is exactly NOT pausing.
            { "largetoggle_pausewhentabbed",   new Binding("runInBackground",     true)  },

            // Not inverted — noiseEnabled already means what the row prints.
            { "largetoggle_noiseeffect",       new Binding("noiseEnabled",        false) },
        };

        // ROWS THAT ARE NOT ROWS. TabGroup_Gameplay and TabGroup_Video showed up
        // in his log carrying a stray button and borrowing the label of the row
        // above them — they are the page containers, not settings.
        private static bool IsRealRow(string objName)
        {
            if (string.IsNullOrEmpty(objName)) return false;
            return !objName.StartsWith("TabGroup_", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a LargeToggle row is switched on, asked of the toggle itself.
        ///
        /// NOTHING IS GUESSED AT: the row's components are searched for a bool
        /// under any of the names Unity UI code tends to use, and the one that
        /// answers is LOGGED ONCE with its type and member name. If the game
        /// calls it something else the log says so and the caller falls back,
        /// rather than a silent wrong answer — the same shape as every other
        /// reflective lookup in this project.
        /// </summary>
        /// <summary>
        /// Whether a LargeToggle row is switched on, asked of the toggle itself.
        ///
        /// NAMES ARE NOT GUESSED AT ANY MORE. 0.7.102 searched a list of likely
        /// member names and found none — the log said "LargeToggle carries no
        /// known toggle bool" and fullscreen fell back to Screen.fullScreen,
        /// which does not move until APPLY. Zamar: "Now it only reads windowed,
        /// windowed, it doesnt accuratatly change when the value is changed."
        ///
        /// So the type is WALKED instead. Every bool DECLARED ON the toggle's
        /// own class (or its own base classes, stopping at MonoBehaviour) is a
        /// candidate; MonoBehaviour's inherited bools — enabled, useGUILayout,
        /// isActiveAndEnabled — are excluded by construction rather than by a
        /// name blocklist that would have to be maintained.
        ///
        /// Everything found is LOGGED ONCE with its value, so the next log names
        /// the real member whatever the game calls it, and a wrong pick is
        /// visible rather than silent. A name carrying "toggle" or "on" wins if
        /// there is one; otherwise the first declared bool is used.
        /// </summary>
        private static bool _toggleMemberLogged;

        private static bool? ToggleState(Row row)
        {
            if (row?.Parent == null) return null;

            const BindingFlags any = BindingFlags.Instance
                                   | BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.DeclaredOnly;
            try
            {
                foreach (var c in row.Parent.GetComponentsInChildren<Component>(false))
                {
                    if (c == null) continue;
                    var top = c.GetType();
                    if (top.Name.IndexOf("Toggle", System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var found = new List<KeyValuePair<string, bool>>();
                    var readers = new Dictionary<string, System.Func<bool>>();

                    for (var t = top; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    {
                        foreach (var f in t.GetFields(any))
                        {
                            if (f.FieldType != typeof(bool)) continue;
                            var ff = f; var cc = c;
                            if (readers.ContainsKey(f.Name)) continue;
                            readers[f.Name] = () => (bool)ff.GetValue(cc);
                            found.Add(new KeyValuePair<string, bool>(
                                $"{t.Name}.{f.Name}", (bool)f.GetValue(c)));
                        }
                        foreach (var pr in t.GetProperties(any))
                        {
                            if (pr.PropertyType != typeof(bool)) continue;
                            if (!pr.CanRead) continue;
                            if (pr.GetIndexParameters().Length != 0) continue;
                            var pp = pr; var cc = c;
                            if (readers.ContainsKey(pr.Name)) continue;
                            readers[pr.Name] = () => (bool)pp.GetValue(cc, null);
                            try
                            {
                                found.Add(new KeyValuePair<string, bool>(
                                    $"{t.Name}.{pr.Name}", (bool)pr.GetValue(c, null)));
                            }
                            catch { }
                        }
                    }

                    if (!_toggleMemberLogged)
                    {
                        _toggleMemberLogged = true;
                        if (found.Count == 0)
                            _log?.LogWarning($"IKMA OPTIONS: {top.Name} declares no bool of its own.");
                        else
                            foreach (var kv in found)
                                _log?.LogInfo($"IKMA OPTIONS: [toggle] {kv.Key} = {kv.Value}");
                    }

                    if (readers.Count == 0) continue;

                    // ANSWERED BY HIS 0.7.104 LOG: LargeToggle.value is the
                    // one, and it read correctly through a dozen flips. Named
                    // explicitly now rather than arrived at by "first declared
                    // bool", which happened to be right and would not stay right
                    // if the class ever gained another field.
                    string pick = null;
                    foreach (var nm in readers.Keys)
                    {
                        string l = nm.ToLowerInvariant();
                        if (l == "value" || l.Contains("toggle") || l == "on" || l.StartsWith("ison"))
                        { pick = nm; break; }
                    }
                    if (pick == null)
                        foreach (var nm in readers.Keys) { pick = nm; break; }

                    try { return readers[pick](); } catch { return null; }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// A row that DOES something rather than holding a value: one button,
        /// no minus, no plus, and not one of the screen's toggles.
        /// </summary>
        /// <summary>One of the screen's two-state switches.</summary>
        /// <summary>
        /// Which of the three pages is showing, read off the tab group the rows
        /// actually live under. Null when it cannot be told, and the caller says
        /// nothing rather than guessing at a number.
        /// </summary>
        public const int PAGE_COUNT = 3;

        private static readonly Dictionary<string, int> _pageNumbers
            = new Dictionary<string, int>
        {
            { "tabgroup_gameplay", 1 },
            { "tabgroup_video",    2 },
            { "tabgroup_audio",    3 },
        };

        /// <summary>The options page showing now (1-3), or null. Session 34.</summary>
        public static int? CurrentPageNow() => CurrentPage(Rows());

        private static int? CurrentPage(List<Row> rows)
        {
            if (rows == null) return null;

            foreach (var r in rows)
            {
                Transform t = r?.Parent;
                for (int hops = 0; t != null && hops < 6; hops++, t = t.parent)
                {
                    string nm = null;
                    try { nm = t.gameObject.name; } catch { }
                    if (nm == null) continue;

                    int n;
                    if (_pageNumbers.TryGetValue(nm.Trim().ToLowerInvariant(), out n)) return n;
                }
            }

            _log?.LogInfo("IKMA OPTIONS: could not tell which page is showing.");
            return null;
        }

        private static bool IsToggle(Row row)
        {
            if (row == null) return false;
            string objName = null;
            try { objName = row.Parent.gameObject.name; } catch { }
            return objName != null &&
                   objName.StartsWith("LargeToggle", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlainButton(Row row)
        {
            if (row == null) return false;
            if (row.Action == null) return false;
            if (row.Decrease != null || row.Increase != null) return false;

            string objName = null;
            try { objName = row.Parent.gameObject.name; } catch { }
            if (objName != null &&
                objName.StartsWith("LargeToggle", System.StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static object OptionsData()
        {
            try
            {
                var pr = typeof(GameOptions).GetProperty("Options",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return pr?.GetValue(null, null);
            }
            catch { return null; }
        }

        /// <summary>
        /// The row's current value as the SCREEN would show it, or null when
        /// nothing is bound. A row with no binding reads its name alone —
        /// never an invented number.
        /// </summary>
        public static string ValueFor(Row row)
        {
            if (row == null) return null;

            string objName = null;
            try { objName = row.Parent.gameObject.name; } catch { }
            if (objName == null) return null;

            string lower = objName.ToLowerInvariant();

            // THE SCREEN WINS WHERE THE ENGINE IS BEHIND IT.
            //
            // Resolution and graphics quality only reach Unity on APPLY, and
            // the language field only takes effect on reboot. Reading the
            // engine on those three reports the OLD value while the row in
            // front of the player already shows the NEW one — which is exactly
            // what Zamar hit: "resolution and graphics quality also dont read
            // the new values", and "Selecting it should then read which
            // language is currently selected not just English for all of them."
            //
            // Parity in both directions: never reveal what is hidden, never
            // withhold what is not. A sighted player watching this row sees it
            // change on every press, so a blind player must hear it change on
            // every press. The printed text is the only source that does that.
            if (lower == "incrementalfield_resolution" ||
                lower == "incrementalfield_quality" ||
                lower == "incrementalfield_language")
            {
                string printed = PrintedValue(row);
                if (!string.IsNullOrEmpty(printed))
                    return lower == "incrementalfield_language"
                         ? LanguageName(printed)
                         : Spoken(printed);
            }

            // FULLSCREEN IS NOT ON AND OFF. His words: "Fullscreen isnt on or
            // off its Full Screen or Windowed." The row names a MODE, and "On"
            // for a mode says nothing — on what?
            if (lower == "largetoggle_fullscreen")
            {
                // THE TOGGLE, NOT THE ENGINE. Screen.fullScreen reports what the
                // window is doing right now, and this row does not touch the
                // window until APPLY — so it read "Full screen" forever, however
                // many times he flipped it. Zamar: "Full screen only reads 'Full
                // Screen' It should alternatively read Windowed."
                //
                // Same rule as resolution and quality one paragraph up: the
                // screen shows the pending choice, so the screen is the source.
                // A toggle has no printed value to read, so its own switched
                // state is asked for instead.
                bool? state = ToggleState(row);
                if (state.HasValue) return Vocabulary.Options.FullScreenOrWindowed(state.Value);

                try { return Vocabulary.Options.FullScreenOrWindowed(Screen.fullScreen); } catch { return null; }
            }

            // Fallbacks for the two above, for the case where the row prints
            // nothing readable. The engine is behind the screen but it is never
            // wrong about what is actually in force.
            if (lower == "incrementalfield_resolution")
            {
                try { return Vocabulary.Options.Resolution(Screen.width, Screen.height); } catch { return null; }
            }
            if (lower == "incrementalfield_quality")
            {
                try { return Spoken(GameOptions.GraphicsQuality.ToString()); } catch { return null; }
            }

            Binding b;
            if (!_bindings.TryGetValue(lower, out b)) return null;

            var data = OptionsData();
            if (data == null) return null;

            try
            {
                var f = data.GetType().GetField(b.Field,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null)
                {
                    _log?.LogWarning($"IKMA OPTIONS: OptionsSaveData.{b.Field} not found.");
                    return null;
                }

                object v = f.GetValue(data);
                if (v is bool)
                {
                    bool on = (bool)v;
                    if (b.Inverted) on = !on;
                    return Vocabulary.Options.OnOrOff(on);
                }
                if (v is int)
                {
                    int n = (int)v + b.Offset;

                    // BRIGHTNESS ZERO IS NOT NOTHING — it is the setting the
                    // game ships with. Zamar: "Brightness zero should read as
                    // Zero: Default." A bare "0" sounds like the screen has
                    // gone black, which is a bad thing to tell a player who
                    // cannot check.
                    string shown = (n == 0 && lower == "incrementalslider_gamma")
                                 ? Vocabulary.Options.GammaZero
                                 : n.ToString();

                    // AND THE SAME MARK EVERYWHERE ELSE IT IS TRUE. (0.7.236.)
                    // Gamma had this by hand since 0.7.98; Zamar asked for it on
                    // the text speed and the three volumes, which is enough
                    // rows that the marker belongs in the binding rather than in
                    // another special case. "Zero, default" comes out of this
                    // line unchanged, so nothing he has already signed off moves.
                    if (b.Default.HasValue && n == b.Default.Value) shown += Vocabulary.Options.DefaultMark;

                    return shown;
                }
                return v?.ToString();
            }
            catch { return null; }
        }

        /// <summary>
        /// One row, spoken. His format, settled for the dials and reused here:
        /// the name, then the value, and nothing else — the value is last so it
        /// can be listened for and the rest tuned out.
        /// </summary>
        private static string Describe(Row row)
        {
            string label = row?.Label;
            if (string.IsNullOrEmpty(label)) return Vocabulary.Options.UnnamedSetting;

            string value = ValueFor(row);
            if (string.IsNullOrEmpty(value)) return Sentence(label);

            // NO FULL STOP BETWEEN THE NAME AND THE VALUE. Zamar: "remove the
            // period after speed before 6." A stop makes the voice pause and
            // drop, so "Dialogue text speed. 6." lands as two statements when
            // it is one — the setting and where it is set.
            return $"{Sentence(label).TrimEnd('.')} {value}.";
        }

        /// <summary>
        /// The screen prints in capitals; a screen reader shouts those or spells
        /// them out. Same treatment the menus already give their labels.
        /// </summary>
        /// <summary>
        /// THE LANGUAGE ROW PRINTS EACH LANGUAGE IN ITSELF — Français, Русский,
        /// 日本語. A sighted player recognises those on sight. An English
        /// screen reader does not: Zamar heard silence on Russian and on every
        /// Asian script, because the voice has no glyphs for them. "I'd expect
        /// it to read 'Russian. Japanese. Korean.' etc, in english here."
        ///
        /// So the printed name is translated into the language the READER
        /// speaks. This is the one place in the mod where the screen's own words
        /// are deliberately not used, and the reason is the same parity rule
        /// that governs everywhere else: a sighted player can tell these twelve
        /// options apart, so a blind player must be able to as well. Reading
        /// nothing at all is the failure being fixed.
        ///
        /// Anything not in the table falls through to the printed string and
        /// logs, so a language added by a patch is spoken as best the voice can
        /// rather than swallowed.
        /// </summary>
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _languageNames => Loc.PerLanguage(ref _languageNamesCache, ref _languageNamesLanguage, Build_languageNames);
        private static Dictionary<string, string> Build_languageNames() =>
            new Dictionary<string, string>
        {
            { "english",       Vocabulary.Options.English              },
            { "français",      Vocabulary.Options.French               },
            { "italiano",      Vocabulary.Options.Italian              },
            { "deutsch",       Vocabulary.Options.German               },
            { "español",       Vocabulary.Options.Spanish              },
            { "português br",  Vocabulary.Options.BrazilianPortuguese },
            { "türkçe",        Vocabulary.Options.Turkish              },
            { "русский",       Vocabulary.Options.Russian              },
            { "日本語",         Vocabulary.Options.Japanese             },
            { "한국어",         Vocabulary.Options.Korean               },
            { "简体中文",        Vocabulary.Options.SimplifiedChinese   },
            { "繁體中文",        Vocabulary.Options.TraditionalChinese  },
        };
        private static Dictionary<string, string> _languageNamesCache;
        private static string _languageNamesLanguage;

        private static string LanguageName(string printed)
        {
            if (string.IsNullOrEmpty(printed)) return printed;

            string spoken;
            if (_languageNames.TryGetValue(printed.Trim().ToLowerInvariant(), out spoken))
                return spoken;

            _log?.LogWarning(
                $"IKMA OPTIONS: no English name for the language '{printed}'. " +
                "Ask Zamar for a word before this ships.");
            return printed;
        }

        /// <summary>
        /// A value taken off the screen, made speakable. The panel prints in
        /// capitals and a screen reader either shouts those or spells them out,
        /// so HIGH becomes High. Anything already carrying lower case is left
        /// exactly as the game wrote it — this must not rewrite a language name.
        /// </summary>
        private static string Spoken(string printed)
        {
            if (string.IsNullOrEmpty(printed)) return printed;

            string t = printed.Trim();

            bool hasLower = false, hasUpper = false;
            foreach (char c in t)
            {
                if (char.IsLower(c)) { hasLower = true; break; }
                if (char.IsUpper(c)) hasUpper = true;
            }
            if (hasLower || !hasUpper) return t;

            string lowered = t.ToLowerInvariant();
            for (int i = 0; i < lowered.Length; i++)
            {
                if (char.IsLetter(lowered[i]))
                    return lowered.Substring(0, i)
                         + char.ToUpperInvariant(lowered[i])
                         + lowered.Substring(i + 1);
            }
            return lowered;
        }

        private static string Sentence(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            string trimmed = label.Trim();
            // The game prints its labels in capitals, which is what this
            // lowers. A label IKMA wrote itself ("Reset with French") already
            // has its case and keeps it (Session 34).
            bool anyLower = false;
            foreach (char ch in trimmed) if (char.IsLower(ch)) { anyLower = true; break; }
            // A label that already ends a sentence ("REALLY RESET IT?") gets no
            // extra full stop.
            bool ended = ".?!".IndexOf(trimmed[trimmed.Length - 1]) >= 0;
            if (anyLower) return ended ? trimmed : trimmed + ".";
            string lower = trimmed.ToLowerInvariant();
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1) + (ended ? "" : ".");
        }

        // ==================================================================

        /// <summary>
        /// A row while BROWSING. His line: "Dialogue Text speed. Press Enter to
        /// adjust." The value is deliberately not here — he did not ask for it
        /// and a browse should stay short. Space reads it.
        /// </summary>
        private static string RowPrompt(Row row)
        {
            string label = row?.Label;
            if (string.IsNullOrEmpty(label)) return Vocabulary.Options.UnnamedSettingPressEnter;
            return Vocabulary.Options.PressEnterToAdjust(Sentence(label));
        }

        public static void Announce()
        {
            var rows = Rows();
            _index = NOWHERE;
            _announced = true;

            _log?.LogInfo($"IKMA OPTIONS: opened with {rows.Count} row(s).");
            foreach (var r in rows)
            {
                // The PARENT NAME is logged because two rows on his 0.7.96 page
                // came back with the same label — the label hunt walks up to the
                // parent and two sibling rows can share the text above them.
                // The object name is what tells them apart, and what a binding
                // will have to key on if the label cannot.
                string parent = "?";
                try { parent = r.Parent.gameObject.name; } catch { }

                _log?.LogInfo($"IKMA OPTIONS:   row '{r.Label ?? "?"}' obj='{parent}' " +
                              $"dec={(r.Decrease != null)} inc={(r.Increase != null)} " +
                              $"act={(r.Action != null)} value={ValueFor(r) ?? "<unbound>"}");
            }

            LogOptionsFields();

            string name = (Vocabulary.Options.OptionsMenu(_pendingName)).TrimEnd('.', ' ');
            _pendingName = null;

            // WHERE YOU ARE IN THE SET, not just which page. Zamar: "After
            // Options Menu, add in 'Page [x] of 3.'" Landing on a page named
            // "Display settings" says what it is; it does not say that there are
            // two more or which way they lie.
            //
            // Asked of the SCREEN — which tab group is live — rather than
            // remembered from the last key pressed, so opening the panel with
            // the mouse reports the same number.
            int? page = CurrentPage(rows);
            string where = page.HasValue ? Vocabulary.Options.PageOf(page.Value, PAGE_COUNT) : "";

            // ARRIVAL NAMES NO SETTING. (0.7.238.) Zamar: "If we're doing the
            // -1 default thing, then we can't read the first option on -1 as
            // well."
            //
            // This page used to read the first row here, and the comment that
            // argued for it said why: "that arrow costs them the first row,
            // since browsing moves before it speaks." NOWHERE is the answer to
            // that objection — the first press now lands ON row one rather than
            // past it — so the preview is no longer paying for anything, and it
            // was claiming a position the cursor did not hold. The hover goes
            // with it: nothing named, nothing lit.
            ReleaseHover();

            CombatAnnouncer.DropCommentary("options opened");
            Speech.Browse(Vocabulary.Options.PressHForHelp(name, where));
        }

        /// <summary>
        /// Every field on OptionsSaveData, with its live value, logged once.
        ///
        /// The binding table below covers master volume, music volume, screen
        /// shake, flicker and gamma — the five the dump's name search happened
        /// to find. His 0.7.96 pages also show sound effects volume, dialogue
        /// text speed, resolution, fullscreen, noise effect, graphics quality
        /// and pause-when-unfocused, and those read their name with no value
        /// rather than an invented one.
        ///
        /// Rather than guess at more field names, this prints what the object
        /// actually holds. One run names every remaining setting and the table
        /// gets filled from evidence.
        /// </summary>
        private static bool _fieldsLogged;

        private static void LogOptionsFields()
        {
            if (_fieldsLogged) return;
            _fieldsLogged = true;

            var data = OptionsData();
            if (data == null) { _log?.LogWarning("IKMA OPTIONS: GameOptions.Options is null."); return; }

            try
            {
                foreach (var f in data.GetType().GetFields(
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object v = null;
                    try { v = f.GetValue(data); } catch { }
                    _log?.LogInfo(
                        $"IKMA OPTIONS: [field] {f.FieldType.Name} {f.Name} = {v?.ToString() ?? "null"}");
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA OPTIONS: field walk threw {e.GetType().Name}.");
            }
        }

        public static bool Announced => _announced;

        public static void Browse(int direction)
        {
            var rows = Rows();
            if (rows.Count == 0) { Speech.Browse(Vocabulary.NoSettingsOnPage); return; }

            // From NOWHERE the first press lands ON the row arrival named
            // (forwards) or on the last one (backwards); it does not step past
            // them. See the NOWHERE note at the top.
            //
            // 0.4.8.012 - STOPS AT THE ENDS, silent there. Session 58's rule
            // ("every arrow list stops at its ends") reached every list but
            // this one; the options pages still wrapped.
            if (!ListStep.Step(ref _index, direction, rows.Count)) return;

            Hover(rows[_index]);

            CombatAnnouncer.DropCommentary("options browse");
            Speech.Browse(RowPrompt(rows[_index]));
        }

        /// <summary>
        /// 0.4.8.012 - Home and End. A beta tester: "one thing I would love when
        /// going through options would be home and end key navigation." Home
        /// lands on the first row, End on the last, and the row is read the
        /// way an arrow reads it - the same keys the Ctrl+M menu, the help
        /// list and the history already take.
        /// </summary>
        public static void Jump(bool toEnd)
        {
            var rows = Rows();
            if (rows.Count == 0) { Speech.Browse(Vocabulary.NoSettingsOnPage); return; }

            _index = toEnd ? rows.Count - 1 : 0;
            Hover(rows[_index]);

            CombatAnnouncer.DropCommentary("options browse");
            Speech.Browse(RowPrompt(rows[_index]));
        }

        // ------------------------------------------------------------------
        // WHAT LIGHTS UP WHEN THE CURSOR IS ON A ROW. (0.7.235.)
        //
        // Zamar: "in the options menu, the visual hover is not working for the
        // arrows on; Dialogue speed, brightness, master, music, or sfx
        // volumes."
        //
        // Every one of those five is an IncrementalSlider, and every
        // IncrementalSlider has THREE buttons: minus, plus, and a third that
        // Rows() files as the row's Action. Hover preferred the Action, which
        // on these rows is not a control a mouse ever highlights — so the row
        // was read aloud while the screen showed nothing moving. The arrows are
        // where a sighted player's cursor goes and where the game draws its
        // highlight, so that is where the selection is shown.
        //
        // BOTH ARROWS, not one. A mouse can only be over one of them, but a
        // keyboard is on the ROW, and either arrow will act on the next press.
        // Lighting both says that; lighting one would claim a direction the
        // player has not chosen. This is why the hover is a LIST now — the old
        // single field could not hold two.
        // ------------------------------------------------------------------
        private static readonly List<GBC.GenericUIButton> _hovered =
            new List<GBC.GenericUIButton>();

        private static void Hover(Row row)
        {
            if (row == null) { ReleaseHover(); return; }

            if (row.Decrease != null || row.Increase != null)
            {
                Enter(row.Decrease, row.Increase);
                return;
            }

            Enter(row.Action);
        }

        /// <summary>
        /// THE ONLY PLACE IKMA LIGHTS A CONTROL ON THIS SCREEN, and the only
        /// place it puts one out. (0.7.101.)
        ///
        /// Zamar's screenshot: FLICKER FX, SCREEN SHAKE and a slider box all
        /// highlighted at once. Browsing released what it had lit, but ADJUSTING
        /// called CursorEnter on the plus or minus button of the row being
        /// changed and never released it — so every setting he touched left a
        /// lit control behind, and the screen slowly filled up with them.
        ///
        /// A stream viewer sees that. The parity rule cuts both ways: the screen
        /// must not show a blind player hovering three things at once when a
        /// sighted player would be hovering one.
        ///
        /// The fix is structural rather than another CursorExit at the call
        /// site — every entry goes through here, so a new caller cannot
        /// reintroduce the leak.
        /// </summary>
        /// <summary>
        /// Light exactly these buttons and nothing else. Nulls are ignored, a
        /// button already lit is left alone rather than re-entered, and
        /// anything lit that is not in the new set is exited first — so moving
        /// between rows never leaves a stale highlight behind.
        /// </summary>
        private static void Enter(params GBC.GenericUIButton[] targets)
        {
            try
            {
                for (int i = _hovered.Count - 1; i >= 0; i--)
                {
                    var held = _hovered[i];

                    bool keep = false;
                    if (targets != null)
                    {
                        foreach (var t in targets)
                            if (t != null && ReferenceEquals(t, held)) { keep = true; break; }
                    }
                    if (keep) continue;

                    try { if (held != null) held.CursorExit(); } catch { }
                    _hovered.RemoveAt(i);
                }

                if (targets == null) return;

                foreach (var t in targets)
                {
                    if (t == null) continue;

                    bool already = false;
                    foreach (var held in _hovered)
                        if (ReferenceEquals(t, held)) { already = true; break; }
                    if (already) continue;

                    t.CursorEnter();
                    _hovered.Add(t);
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA OPTIONS: hover threw {e.GetType().Name}.");
            }
        }

        private static void ReleaseHover()
        {
            for (int i = _hovered.Count - 1; i >= 0; i--)
            {
                try { if (_hovered[i] != null) _hovered[i].CursorExit(); } catch { }
            }
            _hovered.Clear();
        }

        // THE SPRITE SWAP IS GONE. (0.7.109.)
        //
        // 0.7.104 called the button's own TrySetHoveringSprite on hover and
        // ResetSprite on the way out, to get the mouse-over art for a keyboard
        // browse. It worked, and it also wrecked the sliders: Zamar, "the slider
        // options get distorted visually after scrolling past them with the
        // arrows", with a screenshot of the text-speed row showing a diamond
        // where its minus belongs.
        //
        // ResetSprite restores the DEFAULT sprite, and these buttons do not sit
        // at their default. A slider's increments are filled or empty depending
        // on where the slider is, and its arrows change with them — so leaving a
        // row put its art back to a state that was never true, and the damage
        // stayed behind after the browse moved on.
        //
        // CursorEnter and CursorExit are what the game itself runs on a mouse
        // hover, and they already give the pixel border. Reaching past them for
        // a second effect meant owning a piece of the game's rendering, and this
        // project does not have the information to own that correctly. The
        // border is the highlight, and that is enough.

        /// <summary>
        /// Left and right change the setting; the new value is read back from
        /// the game rather than predicted, so what is spoken is what was stored.
        /// </summary>
        public static void Adjust(int direction)
        {
            var rows = Rows();
            if (rows.Count == 0) return;
            if (_index < 0 || _index >= rows.Count) _index = 0;

            var row = rows[_index];
            // A row with a minus and a plus takes a DIRECTION. A row with
            // neither is a toggle, and any arrow flips it — the game gives a
            // toggle one button, so there is nothing for a direction to mean.
            var btn = direction < 0 ? row.Decrease : row.Increase;
            if (btn == null) btn = row.Action;

            if (btn == null)
            {
                Speech.Browse(Vocabulary.Options.ThatSettingCannotBe);
                return;
            }

            Enter(btn);
            try { btn.CursorSelectStart(); btn.CursorSelectEnd(); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA OPTIONS: adjust threw {e.GetType().Name}.");
                return;
            }

            // Back to the whole row once the press has landed. The press lights
            // the arrow it used, which is right for the instant it happens and
            // wrong a moment later — the player is still on the row, not on one
            // of its arrows.
            Hover(row);

            CombatAnnouncer.Instance?.StartCoroutine(SpeakAfterChange(row));
        }

        private static System.Collections.IEnumerator SpeakAfterChange(Row row)
        {
            // One frame for the game to store it, then read the game's own
            // answer. Never a predicted value.
            yield return null;
            yield return new WaitForSecondsRealtime(0.05f);

            string value = ValueFor(row);
            _log?.LogInfo($"IKMA OPTIONS: '{row.Label}' is now {value ?? "?"}.");

            Speech.Browse(value ?? Describe(row));
        }

        /// <summary>
        /// After a plain button press: wait for the button's printed text to
        /// stop typing out (the screen types labels a letter at a time), then
        /// speak it if it changed, or the button's name if it did not.
        /// </summary>
        private static System.Collections.IEnumerator SpeakAfterPress(Row row, string before)
        {
            string last = null;
            float stableSince = Time.realtimeSinceStartup;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 2f)
            {
                yield return null;
                string now = null;
                try { now = ScanLabel(row); } catch { }
                if (now != last) { last = now; stableSince = Time.realtimeSinceStartup; continue; }
                if (!string.IsNullOrEmpty(now) && Time.realtimeSinceStartup - stableSince >= 0.3f) break;
            }

            bool changed = !string.IsNullOrEmpty(last) && !string.Equals(last, before, System.StringComparison.Ordinal);
            _log?.LogInfo($"IKMA OPTIONS: button text after press: '{last ?? "?"}'{(changed ? " (changed)" : "")}.");
            Speech.Browse(Sentence(changed ? last : Vocabulary.Options.SettingFallback(row.Label)));
        }

        /// <summary>Enter — take hold of the highlighted setting.</summary>
        public static void Activate()
        {
            var rows = Rows();
            if (rows.Count == 0) return;
            if (_index < 0 || _index >= rows.Count) _index = 0;

            // A PLAIN BUTTON IS PRESSED, NOT TAKEN HOLD OF. APPLY and RESET
            // SAVE DATA do one thing; there is no value to walk with the arrows,
            // so making the player press Enter and then an arrow was asking for
            // a second keystroke that means nothing. Zamar: "For apply, have
            // that select Apply just with enter, it should not require the use
            // of arrows at all. same with Reset Saved Data."
            //
            // Told apart STRUCTURALLY rather than by name: no minus, no plus,
            // and not a toggle. A toggle also has only one button, but pressing
            // it changes a value the player needs read back, which is what the
            // adjust layer is for.
            // A TOGGLE IS FLIPPED, NOT TAKEN HOLD OF EITHER. (0.7.104.)
            //
            // Zamar: "Pause When Window Not Focus should also just toggle with
            // Enter, not need the arrows." It is two states — Enter is enough,
            // and the value is read back straight away so the flip confirms
            // itself. Applied to every LargeToggle on the screen, not just the
            // one he was standing on, because the shape is the same for all of
            // them and one behaving differently is the surprise.
            //
            // The select-then-adjust layer stays for the sliders and fields,
            // where an arrow has somewhere to travel.
            if (IsToggle(rows[_index]))
            {
                var flip = rows[_index].Action;
                if (flip != null)
                {
                    _log?.LogInfo($"IKMA OPTIONS: toggling '{rows[_index].Label ?? "?"}'.");
                    Enter(flip);
                    try { flip.CursorSelectStart(); flip.CursorSelectEnd(); }
                    catch (System.Exception e)
                    {
                        _log?.LogWarning($"IKMA OPTIONS: toggling threw {e.GetType().Name}.");
                        return;
                    }
                    CombatAnnouncer.Instance?.StartCoroutine(SpeakAfterChange(rows[_index]));
                    return;
                }
            }

            if (IsPlainButton(rows[_index]))
            {
                var press = rows[_index].Action;
                _log?.LogInfo($"IKMA OPTIONS: pressing '{rows[_index].Label ?? "?"}'.");

                CombatAnnouncer.DropCommentary("options button pressed");

                // Session 34 - A BUTTON THAT RE-PRINTS ITSELF SAYS ITS NEW WORDS.
                // RESET SAVE DATA changes its own text on each press ("Really
                // reset it?", ...), the game's words, and Zamar wants each one
                // heard (Session 33). So the press is answered after the text
                // settles: the new words if they changed, the button's name if
                // they did not (Apply).
                var pressedRow = rows[_index];
                string before = ScanLabel(pressedRow);

                Enter(press);
                try { press.CursorSelectStart(); press.CursorSelectEnd(); }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA OPTIONS: pressing threw {e.GetType().Name}.");
                }
                CombatAnnouncer.Instance?.StartCoroutine(SpeakAfterPress(pressedRow, before));
                return;
            }

            _adjusting = true;
            _log?.LogInfo($"IKMA OPTIONS: adjusting '{rows[_index].Label ?? "?"}'.");

            // HIS LINE, plus the current value in front of it where there is
            // one. Taking hold of a slider without knowing where it starts
            // leaves the player pressing an arrow to find out — and the first
            // press has already changed it.
            string current = ValueFor(rows[_index]);

            CombatAnnouncer.DropCommentary("options adjust entered");
            Speech.Browse(
                Vocabulary.Options.ArrowsToAdjustBackspace(string.IsNullOrEmpty(current), current));
        }

        /// <summary>
        /// Backspace while adjusting — let the setting go and say where you
        /// landed. His spec: "Pressing backspace from there should then once
        /// more read 'Dialogue Text speed. Press Enter to adjust.'"
        /// </summary>
        public static void StopAdjusting()
        {
            _adjusting = false;

            var rows = Rows();
            if (rows.Count == 0) return;
            if (_index < 0 || _index >= rows.Count) _index = 0;

            _log?.LogInfo($"IKMA OPTIONS: released '{rows[_index].Label ?? "?"}'.");

            // "Back" first, as confirmation that the setting was let go — his
            // ask. Without it, hearing the row name again is ambiguous between
            // "you are out" and "nothing happened".
            Speech.Browse(Vocabulary.Options.BackThenRowPrompt(RowPrompt(rows[_index])));
        }

        public static void SpeakPosition()
        {
            var rows = Rows();
            if (rows.Count == 0) { Speech.Browse(Vocabulary.NoSettingsOnPage); return; }

            // Space answers for the row arrival named while the cursor is still
            // NOWHERE, and does NOT commit the cursor there — repeating where
            // you are must never be a move. (0.7.234.)
            int here = (_index == NOWHERE || _index >= rows.Count) ? 0 : _index;

            Speech.Browse(
                Vocabulary.OptionOf(Describe(rows[here]), here + 1, rows.Count));
        }

        /// <summary>
        /// Backspace. MenuController.ResetToDefaultState is PUBLIC and is the
        /// game's own way back out of a slotted card, so the menu runs its own
        /// transition rather than IKMA reproducing it.
        /// </summary>
        public static void Close()
        {
            _announced = false;
            _index = NOWHERE;

            ReleaseHover();

            var mc = Singleton<MenuController>.Instance;
            if (mc == null) return;

            _log?.LogInfo("IKMA OPTIONS: closing back to the pause menu.");
            try { mc.ResetToDefaultState(); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA OPTIONS: closing threw {e.GetType().Name}.");
            }
        }
    }

    /// <summary>
    /// 0.4.8.012 - THE GAME'S OWN PAGE KEYS. A beta tester: "when you hit enter
    /// on the audio page, for example, and change the master volume, then
    /// press 1 or 2, it jumps to the general or video category. I do not think
    /// that should be allowed while you are still changing the selected
    /// setting."
    ///
    /// IKMA already swallowed every other key while a setting is held. The
    /// digits got through anyway because the GAME listens for them itself:
    /// each option tab is a GBC.GenericUIButton with its own inputKey
    /// (OptionsUI.prefab: Tab_1..Tab_4 carry KeyCode 49..52), polled in
    /// GenericUIButton.UpdateInputKey (NONPUBLIC, void, no parameters -
    /// _gamesource\code\GBC\GenericUIButton.cs). The page turned under the
    /// held setting while IKMA still thought the player held a row of the
    /// old page.
    ///
    /// While a setting is held the tabs' hotkeys are skipped, for the
    /// keyboard and the screen alike. Nothing else is touched: other buttons,
    /// and the tabs whenever no setting is held, run as the game wrote them.
    ///
    /// Registered through Plugin.TryPatch, not PatchAll.
    /// </summary>
    public static class GenericUIButton_UpdateInputKey_Patch
    {
        public static bool Prefix(GBC.GenericUIButton __instance)
        {
            if (!OptionsReader.Adjusting) return true;
            try
            {
                string nm = __instance != null ? __instance.gameObject.name : null;
                if (nm != null && nm.StartsWith("Tab_")) return false;
            }
            catch { }
            return true;
        }
    }
}

// OptionsReader.cs
