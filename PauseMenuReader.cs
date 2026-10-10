// PauseMenuReader.cs
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// The Escape menu. (Session 17.)
    ///
    /// Zamar: "Escape should be globally available, I dont think theres supposed
    /// to be a single screen you cant hit Escape and get that menu." Opening it
    /// was fixed at 0.7.82 by injecting Button.Menu; this makes it READ and
    /// makes it navigable from the keyboard.
    ///
    /// EVERY WORD SPOKEN FOR AN OPTION IS THE GAME'S OWN. `MenuCard.TitleText`
    /// is PUBLIC, so "Return to Start", "Options" and "End Run" come from the
    /// card rather than from its GameObject name — which was the whole problem
    /// the 0.7.82 probe reported as `label=<none>`, and which the Kaycee's Mod
    /// front end already taught this project not to work around by speaking
    /// object names.
    ///
    /// THE GAME OWNS THE SELECTION. `MenuController` is a Singleton and holds
    /// `SelectedCard` plus its own `cards` list. Browsing calls the game's
    /// `OnCardCursorEntered` so the card physically highlights, and Enter calls
    /// `OnCardSelected` — the same entry point a mouse click ends in. IKMA never
    /// reproduces what choosing a card does.
    /// </summary>
    public static class PauseMenuReader
    {
        private static ManualLogSource _log;
        public static void Init(ManualLogSource log) => _log = log;

        // THE CURSOR STARTS NOWHERE. (0.7.238.) Zamar: "If we're doing the -1
        // default thing, then we can't read the first option on -1 as well."
        //
        // This menu already named no option on arrival — and then hovered
        // option one and sat on it anyway, so the first arrow press moved to
        // option TWO and option one could only be reached by going all the way
        // round. Silent version of the same off-by-one every other reader has
        // now had fixed.
        private const int NOWHERE = -1;
        private static int      _index = NOWHERE;
        private static bool     _open;
        private static MenuCard _hovered;

        public static bool Active => _open && IsPaused();

        private static FieldInfo _cardsField;
        private static FieldInfo _infoBarField;
        private static FieldInfo _savedField;
        private static bool      _fieldsResolved;

        private static void ResolveFields()
        {
            if (_fieldsResolved) return;
            _fieldsResolved = true;

            const BindingFlags any = BindingFlags.Instance
                                   | BindingFlags.Public
                                   | BindingFlags.NonPublic;

            _cardsField = typeof(MenuController).GetField("cards", any);
            if (_cardsField == null)
                _log?.LogWarning("IKMA PAUSE: MenuController.cards not found.");

            _infoBarField = typeof(PauseMenu3D).GetField("ascensionRunInfoBar", any);
            if (_infoBarField == null)
                _log?.LogWarning("IKMA PAUSE: PauseMenu3D.ascensionRunInfoBar not found.");
        }

        private static bool IsPaused()
        {
            try
            {
                var m = PauseMenu.instance;
                return m != null && m.Paused;
            }
            catch { return false; }
        }

        private static MenuController Controller()
        {
            try { return Singleton<MenuController>.Instance; }
            catch { return null; }
        }

        /// <summary>
        /// The options actually on screen, in the game's own order.
        ///
        /// FILTERED ON activeInHierarchy, because Kaycee's Mod hides some. His
        /// 0.7.82 log had four MenuCards and only three live —
        /// MenuCard_Library is inactive since there is no deck editing in a run.
        /// Reading a hidden option would offer a key that does nothing.
        /// </summary>
        public static List<MenuCard> Options()
        {
            var result = new List<MenuCard>();
            ResolveFields();

            var mc = Controller();
            if (mc == null || _cardsField == null) return result;

            try
            {
                var list = _cardsField.GetValue(mc) as IEnumerable<MenuCard>;
                if (list == null) return result;

                foreach (var c in list)
                {
                    if (c == null) continue;
                    bool live = false;
                    try { live = c.gameObject.activeInHierarchy; } catch { }
                    if (live) result.Add(c);
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA PAUSE: reading the card list threw {e.GetType().Name}.");
            }

            return result;
        }

        /// <summary>
        /// A card's spoken name — the game's own TitleText, never the GameObject
        /// name. A locked card says so, because the game shows that too.
        /// </summary>
        private static string Label(MenuCard card)
        {
            if (card == null) return null;

            string title = null;
            try { title = card.TitleText; } catch { }
            if (string.IsNullOrEmpty(title)) return null;

            bool locked = false;
            try { locked = card.Locked; } catch { }

            return Vocabulary.Pause.LockedMark(locked, title);
        }

        /// <summary>
        /// Whatever the run info bar prints. Zamar asked for "how many rare's or
        /// shinies you have? (not sure what that value is)" — so nothing is
        /// named from a member here. The bar's own strings are read in the order
        /// it holds them, which is what a sighted player sees.
        /// </summary>
        public static string InfoBarText()
        {
            ResolveFields();

            var menu = PauseMenu.instance as PauseMenu3D;
            if (menu == null || _infoBarField == null) return null;

            // "LAST SAVED ..." LIVES ON PauseMenu, NOT ON THE INFO BAR.
            // His 0.7.86 log came back info='MAP #3' and nothing else, because
            // the save line is a separate PixelText the bar does not own. He
            // asked for both: "Pause menu. 3 options. Map 3. Last saved 1 second
            // ago." Read from the game's own field, so the wording and the
            // elapsed time are the screen's rather than IKMA's.
            string saved = null;
            try
            {
                if (_savedField == null)
                {
                    _savedField = typeof(PauseMenu).GetField("lastSavedText",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                var t = _savedField?.GetValue(menu) as Component;
                if (t != null) saved = TextOf(t);
            }
            catch { }

            GameObject bar = null;
            try { bar = _infoBarField.GetValue(menu) as GameObject; } catch { }
            if (bar == null) return null;

            var parts = new List<string>();
            var seen  = new HashSet<string>();

            try
            {
                foreach (var c in bar.GetComponentsInChildren<Component>(true))
                {
                    string v = TextOf(c);
                    if (string.IsNullOrEmpty(v)) continue;
                    v = v.Trim();
                    if (v.Length == 0) continue;

                    // The screen carries a long rule of dashes as a divider.
                    if (v.Replace("-", "").Trim().Length == 0) continue;
                    if (!seen.Add(v)) continue;
                    parts.Add(v);
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(saved))
            {
                saved = saved.Trim();
                if (saved.Length > 0 && !seen.Contains(saved)) parts.Add(saved);
            }

            return parts.Count == 0 ? null : string.Join(". ", parts.ToArray());
        }

        private static string TextOf(Component c) => UiText.Of(c);

        // ==================================================================

        /// <summary>Called from the pause patch when the menu opens or closes.</summary>
        public static void OnPausedChanged(bool paused)
        {
            if (!paused)
            {
                if (_open)
                {
                    _log?.LogInfo("IKMA PAUSE: menu closed.");

                    // CLOSING THE MENU CUTS WHATEVER IT WAS SAYING. (0.7.136.)
                    //
                    // Zamar: "When the pause menu closes it should stomp
                    // anything being read from it or the options menu."
                    //
                    // The menu's lines are about a screen that no longer exists.
                    // Letting them finish means hearing an option list describe
                    // something the player has already left — the same class as
                    // a line that outlives its own truth, and worse here because
                    // the player is now back in a game that may be talking.
                    //
                    // Silence rather than a replacement line: the screen behind
                    // announces itself, and adding "menu closed" on top would be
                    // IKMA narrating its own bookkeeping.
                    CombatAnnouncer.DropCommentary("pause menu closed");
                    Speech.Silence();
                }

                _open = false;
                return;
            }

            _open      = true;
            _index     = NOWHERE;
            _hovered   = null;
            _hoveredTab = null;

            // SETTLE BEFORE SPEAKING. The menu tweens its cards in, and reading
            // the list on the frame Paused flips counts whatever has arrived —
            // the same defect that made the deck view announce "6 cards" then
            // "3 cards" for one nine-card deck.
            CombatAnnouncer.Instance?.StartCoroutine(AnnounceWhenSettled());
        }

        private static System.Collections.IEnumerator AnnounceWhenSettled()
        {
            int last = -1;
            int stable = 0;

            for (int i = 0; i < 40 && stable < 3; i++)
            {
                yield return new WaitForSecondsRealtime(0.05f);
                int n = Options().Count;
                if (n == last && n > 0) stable++; else { stable = 0; last = n; }
            }

            if (!IsPaused()) yield break;
            AnnounceOpen();
        }

        private static void AnnounceOpen()
        {
            var opts = Options();
            _index = NOWHERE;

            // 0.4.8.004 - Zamar, Session 56: "Pause menu. 3 options." while the
            // arrows stop four times. "make it four then" - the Active
            // Challenges row is counted wherever it is one of the stops.
            string countWord = Vocabulary.Pause.OptionCount(opts.Count + (HasChallengeRow ? 1 : 0));

            // HIS LINE, Session 17: "Pause menu. 3 options. Map 3. Last saved 1
            // second ago." — the screen named, its size, then what the run info
            // bar actually prints.
            string info = InfoBarText();
            string line = Vocabulary.Pause.PauseMenu(countWord, string.IsNullOrEmpty(info), info);

            _log?.LogInfo($"IKMA PAUSE: opened with {opts.Count} option(s). info='{info}'");

            CombatAnnouncer.DropCommentary("pause menu opened");
            Speech.Browse(line);

            // NOT HOVERED. The line above names no option, so lighting one
            // would tell a sighted viewer the player is somewhere the speech
            // never put them. The first arrow lands on option one.
            try { if (_hovered != null) _hovered.CursorExit(); } catch { }
            _hovered = null;
        }

        /// <summary>
        /// Move the game's own highlight onto the browsed card, so the screen
        /// matches the narration. Several Kaycee's Mod screens taught this: a
        /// blind player streaming to a sighted audience must not be describing
        /// something the screen is not showing.
        /// </summary>
        private static void HoverCurrent(List<MenuCard> opts)
        {
            if (opts == null || opts.Count == 0) return;
            if (_index < 0 || _index >= opts.Count) return;

            // THE CURSOR PRIMITIVE, NOT THE CONTROLLER CALLBACK. (0.7.85
            // playtest.) Zamar: "when you mouse hover over these options it
            // highlights the box and makes a sound. We need the left and right
            // arrows to mimic that mouse highlight."
            //
            // OnCardCursorEntered is what MenuCard calls the controller WITH,
            // partway through its own hover — so calling it directly skipped the
            // highlight and the sound, which live in MenuCard.OnCursorEnter.
            // CursorEnter/CursorExit are the same primitive hand browse and menu
            // navigation already use everywhere else in this mod, and they
            // virtual-dispatch into the card's own handler.
            //
            // The previous card is released first, or two options sit lit at
            // once — the exact defect the Kaycee's Mod menus had in Session 13.
            try
            {
                if (_hovered != null && !ReferenceEquals(_hovered, opts[_index]))
                    _hovered.CursorExit();

                _hovered = opts[_index];
                _hovered.CursorEnter();
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA PAUSE: hover threw {e.GetType().Name}.");
            }
        }

        // ==================================================================
        // THE OPTIONS PAGES. (Session 17.)
        //
        // Zamar: "I like the 1/2/3 for the different option pages, but it should
        // follow the clicking on those pages thing, since pressing those buttons
        // worked but didnt provide the visuals that clicking on them does."
        //
        // The digits already worked because the GAME binds them —
        // GenericUIButton carries its own inputKey, so the key press reaches the
        // button directly and skips everything a mouse would do on the way in.
        // The tab changed and nothing highlighted.
        //
        // So IKMA moves the cursor onto the tab first and then clicks it, which
        // is the same fix the pause menu's own cards needed: the highlight and
        // the sound live in the hover, not in the activation. A blind player
        // streaming to a sighted audience must not be changing pages invisibly.
        //
        // The tabs are found by name because that is how the screen is built —
        // Tab_1 through Tab_4, seen in his 0.7.82 survey. Their ORDER is what
        // matters here, not their names, and none of it is spoken.
        // ==================================================================
        public static List<GBC.GenericUIButton> OptionsTabs()
        {
            var result = new List<GBC.GenericUIButton>();

            // OptionsReader owns the answer to "where does the panel hang",
            // because the panel is the same one on the title screen and in the
            // pause menu and only its root differs. (0.7.232.)
            var root = OptionsReader.PanelRoot();
            if (root == null) return result;

            try
            {
                foreach (var b in root.GetComponentsInChildren<GBC.GenericUIButton>(true))
                {
                    if (b == null) continue;

                    string nm = null;
                    try { nm = b.gameObject.name; } catch { }
                    if (nm == null || !nm.StartsWith("Tab_")) continue;

                    bool live = false;
                    try { live = b.gameObject.activeInHierarchy; } catch { }
                    if (live) result.Add(b);
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA PAUSE: reading the option tabs threw {e.GetType().Name}.");
            }

            result.Sort((a, b) =>
            {
                string an = "", bn = "";
                try { an = a.gameObject.name; } catch { }
                try { bn = b.gameObject.name; } catch { }
                return string.CompareOrdinal(an, bn);
            });

            return result;
        }

        /// <summary>
        /// Switch to an options page the way a mouse does — hover, then click.
        /// Returns false when the options panel is not up, so the digit falls
        /// through to whatever else wants it.
        /// </summary>
        public static bool SwitchOptionsPage(int number)
        {
            var tabs = OptionsTabs();
            if (tabs.Count == 0) return false;

            if (number < 1 || number > tabs.Count)
            {
                _log?.LogInfo($"IKMA PAUSE: no options page {number} ({tabs.Count} available).");
                return true;
            }

            var tab = tabs[number - 1];

            string nm = "?";
            try { nm = tab.gameObject.name; } catch { }
            _log?.LogInfo($"IKMA PAUSE: switching to options page {number} ('{nm}').");

            // HIS NAMES. The tabs carry no readable label of their own — the
            // 0.7.97 survey found the pages only by the TabGroup objects behind
            // them — so these three words are Zamar's, matching the order the
            // log showed: gameplay, video, audio.
            //
            // HANDED OVER RATHER THAN SPOKEN. (0.7.101.) Speaking it here put
            // the page name one frame ahead of the options reader's own
            // re-announce, which stomped it — Zamar heard "Options Menu" on all
            // three pages. The reader owns the one line the screen gets, and
            // this names it.
            CombatAnnouncer.DropCommentary("options page switched");
            OptionsReader.SetPendingName(PageName(number));

            try
            {
                // Release whatever was lit, or two tabs sit highlighted at once —
                // the defect the Kaycee's Mod menus had in Session 13.
                if (_hoveredTab != null && !ReferenceEquals(_hoveredTab, tab))
                    _hoveredTab.CursorExit();

                _hoveredTab = tab;
                tab.CursorEnter();
                tab.CursorSelectStart();
                tab.CursorSelectEnd();
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA PAUSE: switching page threw {e.GetType().Name}.");
            }

            return true;
        }

        /// <summary>Zamar's names for the three option pages.</summary>
        public static string PageName(int number)
        {
            switch (number)
            {
                case 1:  return Vocabulary.Pause.GameSettings;
                case 2:  return Vocabulary.Pause.DisplaySettings;
                case 3:  return Vocabulary.Pause.AudioSettings;
                default: return Vocabulary.Pause.Page(number);
            }
        }

        private static GBC.GenericUIButton _hoveredTab;

        /// <summary>
        /// Say where you are again after coming back from a sub-screen. Zamar:
        /// "When hitting backspace out of the options menu it needs to re-read
        /// Pause Menu when going back to it." Arriving somewhere always names
        /// the place — the same rule as the map and the node screens.
        /// </summary>
        public static void ReAnnounce()
        {
            CombatAnnouncer.Instance?.StartCoroutine(AnnounceWhenSettled());
        }

        // ==================================================================
        // THE CHALLENGE SKULLS ARE THE LAST STOP ON THIS MENU. (0.7.240.)
        //
        // Zamar: "When in that pause menu of a run with challenges enabled,
        // scrolling right past the top End Run option should highlight hover
        // the skulls at the bottom, listing what the active challenges are."
        //
        // They sit below the cards in a ChallengeIconGrid, they are the only
        // place a player can be reminded what they signed up for mid-run, and
        // they were unreachable: the browse wrapped from the last card straight
        // back to the first. One extra stop at the end of the list, which is
        // where they are on screen.
        //
        // A READOUT, NOT A CHOICE. Enter does nothing on it — the icons on this
        // screen are not clickable — so Enter is left alone rather than given a
        // meaning the screen does not have.
        // ==================================================================
        // THE SKULLS ARE NOT UNDER THE PAUSE MENU. (0.7.241.)
        //
        // 0.7.240 looked for a ChallengeIconGrid among PauseMenu.instance's
        // children and found nothing, so the extra stop never existed and Zamar
        // still could not reach the row. His survey says why, and it was already
        // in the log: 88 interactables under the pause menu and exactly ONE
        // AscensionIconInteractable in the whole tree —
        //
        //   IKMA PAUSE:   item 88: AscensionIconInteractable obj='Icon' active=False
        //
        // — inactive, and not the row. The skulls he can see at the bottom of
        // that screen are drawn somewhere this reader cannot walk to.
        //
        // So the row stops being a list of objects and becomes a list of FACTS.
        // AscensionSaveData.Data.activeChallenges is the run's own record of
        // what is switched on, and AscensionChallengesUtil.GetInfo is PUBLIC and
        // maps each one to the info that carries its title. No icon required,
        // and it cannot be defeated by the icons living somewhere else.
        //
        // The hover is still attempted, because he asked for it — but only if a
        // grid turns up, and its absence is logged rather than passed over in
        // silence. Nothing about the row's existence depends on it any more.
        private static List<string> ActiveChallengeTitles()
        {
            var names = new List<string>();

            try
            {
                var data = AscensionSaveData.Data;
                if (data?.activeChallenges == null) return names;

                foreach (var challenge in data.activeChallenges)
                {
                    var info = AscensionChallengesUtil.GetInfo(challenge);
                    string title = info?.title;
                    if (string.IsNullOrEmpty(title)) continue;

                    // The same challenge can be stacked, and a title said twice
                    // is a list the player has to re-count.
                    if (!names.Contains(title)) names.Add(title);
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA PAUSE: reading the active challenges threw {e.GetType().Name}.");
            }

            return names;
        }

        /// <summary>Is there a challenge row to stop on at all? A run with no
        /// challenges has no extra option, and the list is one shorter.</summary>
        private static bool HasChallengeRow => ActiveChallengeTitles().Count > 0;

        /// <summary>
        /// What the row says. Only the ACTIVE challenges are named — his words,
        /// "listing what the active challenges are". PROVISIONAL wording.
        /// </summary>
        private static string ChallengeRowLine()
        {
            var names = ActiveChallengeTitles();


            // HIS WORDING, 0.7.244: "Active Challenges. Final Boss, [etc],
            // [etc]." One shape for one and for many — the old line changed
            // grammar with the count, which makes a list the player has to
            // parse differently every time they open it.
            if (names.Count == 0) return Vocabulary.Pause.NoActiveChallenges;
            return Vocabulary.Pause.ActiveChallenges(names.ToArray());
        }

        /// <summary>
        /// Whatever icons CAN be reached, lit the way a cursor over them would
        /// be. Best effort by design: see the note above — the row reads
        /// correctly whether or not anything here finds something to light.
        /// </summary>
        private static List<AscensionIconInteractable> ReachableIcons()
        {
            var result = new List<AscensionIconInteractable>();

            var menu = PauseMenu.instance;
            if (menu == null) return result;

            try
            {
                foreach (var icon in menu.GetComponentsInChildren<AscensionIconInteractable>(true))
                {
                    if (icon == null || icon.Info == null) continue;
                    bool live = false;
                    try { live = icon.gameObject.activeInHierarchy; } catch { }
                    if (live) result.Add(icon);
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA PAUSE: reading the challenge icons threw {e.GetType().Name}.");
            }

            return result;
        }

        private static void HoverChallengeRow()
        {
            try { if (_hovered != null) _hovered.CursorExit(); } catch { }
            _hovered = null;

            var icons = ReachableIcons();
            if (icons.Count == 0)
            {
                _log?.LogInfo("IKMA PAUSE: no challenge icons reachable from the pause menu — " +
                              "the row reads, nothing lights.");
                return;
            }

            foreach (var icon in icons)
            {
                try { icon.CursorEnter(); }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA PAUSE: hovering a challenge icon threw {e.GetType().Name}.");
                    return;
                }
            }
        }

        private static void ReleaseChallengeRow()
        {
            foreach (var icon in ReachableIcons())
            {
                try { icon.CursorExit(); } catch { }
            }
        }

        public static void Browse(int direction)
        {
            var opts = Options();
            if (opts.Count == 0) { Speech.Browse(Vocabulary.NoOptions); return; }

            // The challenge row is one past the last card.
            int stops = opts.Count + (HasChallengeRow ? 1 : 0);

            int next;
            if (_index == NOWHERE || _index >= stops)
                next = direction >= 0 ? 0 : stops - 1;
            else
            {
                next = _index + direction;
                if (next < 0) next = stops - 1;
                if (next >= stops) next = 0;
            }

            if (next >= opts.Count)
            {
                _index = next;
                CombatAnnouncer.DropCommentary("pause menu browse");
                HoverChallengeRow();
                Speech.Browse(ChallengeRowLine());
                return;
            }

            // Leaving the row puts its skulls out again.
            if (_index >= opts.Count) ReleaseChallengeRow();
            _index = next;

            HoverCurrent(opts);

            // ARROWING CUTS THE CHATTER. Zamar: "arrowing to a new selection
            // should stomp the low-prio info of reading the current selection.
            // Why wasnt that true for this menu when it is for all others".
            //
            // Because interrupt only cuts what is IN THE AIR — the next queued
            // Info line still speaks over the new one. The other readers drop
            // commentary as well, and this one was written without it. Any
            // browse that moves a selection should do both.
            CombatAnnouncer.DropCommentary("pause menu browse");

            string label = Label(opts[_index]);
            if (label == null)
            {
                // The game had no title for it. Say nothing invented — this is
                // the only honest answer, and it logs so it can be chased.
                _log?.LogWarning($"IKMA PAUSE: option {_index + 1} has no TitleText.");
                Speech.Browse(Vocabulary.Pause.UnnamedOption);
                return;
            }

            Speech.Browse(label);
        }

        /// <summary>Space — position and the run info, same as everywhere else.</summary>
        public static void SpeakPosition()
        {
            var opts = Options();
            if (opts.Count == 0) { Speech.Browse(Vocabulary.NoOptions); return; }

            // On the challenge row, Space repeats the row.
            if (_index >= opts.Count && HasChallengeRow)
            {
                Speech.Browse(ChallengeRowLine());
                return;
            }

            // Space answers for option one while the cursor is still NOWHERE,
            // and does NOT commit the cursor there — repeating where you are
            // must never be a move.
            int here = (_index == NOWHERE || _index >= opts.Count) ? 0 : _index;

            string label = Vocabulary.PartNameOrUnnamed(Label(opts[here]));
            string info  = InfoBarText();

            string line = Vocabulary.Pause.OptionOf(label, here + 1, opts.Count + (HasChallengeRow ? 1 : 0), string.IsNullOrEmpty(info), info);
            Speech.Browse(line);
        }

        /// <summary>
        /// Enter. MenuController.OnCardSelected is PUBLIC and is where a mouse
        /// click on a card ends up, so the game decides what choosing it means.
        /// </summary>
        public static void Activate()
        {
            var opts = Options();
            if (opts.Count == 0) return;

            // The challenge row is a readout — the icons on this screen are
            // not clickable, so Enter says nothing rather than pretending to.
            if (_index >= opts.Count && HasChallengeRow)
            {
                _log?.LogInfo("IKMA PAUSE: Enter on the challenge row — nothing to choose there.");
                return;
            }

            // Enter straight off the pause menu's own line chooses option one,
            // the same clamp every other reader uses for a cursor that is still
            // NOWHERE.
            if (_index == NOWHERE || _index >= opts.Count) _index = 0;
            if (_index < 0 || _index >= opts.Count) return;

            var card = opts[_index];
            var mc   = Controller();
            if (mc == null) return;

            bool locked = false;
            try { locked = card.Locked; } catch { }
            if (locked)
            {
                Speech.Browse(Vocabulary.Pause.ThatOptionIsLocked);
                return;
            }

            _log?.LogInfo($"IKMA PAUSE: activating '{Label(card) ?? "?"}'.");

            // TWO STEPS, BECAUSE THAT IS WHAT THE MOUSE DOES. Zamar, 0.7.85:
            // "When you select Options it's the same as clicking it which picks
            // up the options card, but then you need to mouse over and drop it
            // to select it. We need to mimic all that movement by just
            // highlighting it with the arrows, and pressing enter. There are
            // multiple menus within Inscryption that function this way."
            //
            // So Enter is: click the card, which lifts it; then click the slot,
            // which plays it. Both through the cursor primitive, so the game
            // runs its own transitions — MenuController.OnCardSelected on its
            // own only did the first half, which is why the menu appeared to
            // accept the choice and then sit there.
            //
            // THE SLOT PRESS IS DEFERRED, not fired on the same frame. The card
            // tweens to the slot and the game refuses the drop while
            // DoingCardTransition is true; a coroutine waits for the game to say
            // it has arrived rather than guessing a duration.
            try { card.CursorSelectStart(); card.CursorSelectEnd(); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA PAUSE: picking the card up threw {e.GetType().Name}.");
                return;
            }

            CombatAnnouncer.Instance?.StartCoroutine(DropWhenReady());
        }

        /// <summary>
        /// Backspace. Closes the menu by pressing the same button that opened
        /// it, because the game already treats Menu as a toggle — nothing here
        /// writes PauseMenu.Paused, which would skip whatever the close does.
        /// </summary>
        public static void Close()
        {
            _log?.LogInfo("IKMA PAUSE: closing via Menu.");
            HotkeyManager.InjectButton("Menu");
        }

        private static FieldInfo _slotField;

        /// <summary>
        /// Drop the lifted card into the menu slot once the game says the
        /// transition has finished. MenuController.DoingCardTransition is
        /// PUBLIC and is the game's own answer — never a fixed wait, because a
        /// tween's duration is not something IKMA gets to assume.
        /// </summary>
        private static System.Collections.IEnumerator DropWhenReady()
        {
            var mc = Controller();
            if (mc == null) yield break;

            for (int i = 0; i < 60; i++)
            {
                bool moving = true;
                try { moving = mc.DoingCardTransition; } catch { moving = false; }
                if (!moving) break;
                yield return new WaitForSecondsRealtime(0.05f);
            }

            if (!IsPaused()) yield break;

            // Already slotted? Then the click did the whole job on this screen
            // and pressing the slot as well would undo it.
            bool slotted = false;
            try { slotted = mc.CardInSlot; } catch { }
            if (slotted)
            {
                _log?.LogInfo("IKMA PAUSE: card is already in the slot; no drop needed.");
                yield break;
            }

            if (_slotField == null)
            {
                _slotField = typeof(MenuController).GetField("menuSlot",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            MenuSlot slot = null;
            try { slot = _slotField?.GetValue(mc) as MenuSlot; } catch { }

            if (slot == null)
            {
                _log?.LogWarning("IKMA PAUSE: no menu slot to drop into.");
                yield break;
            }

            _log?.LogInfo("IKMA PAUSE: dropping the card into the slot.");
            try { slot.CursorSelectStart(); slot.CursorSelectEnd(); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA PAUSE: dropping threw {e.GetType().Name}.");
            }
        }

        /// <summary>
        /// True while the options panel is up. Asked of the screen — the tabs
        /// only exist and only go active when the panel is showing, so their
        /// presence IS the answer and nothing has to be remembered.
        /// </summary>
        public static bool OptionsOpen => OptionsTabs().Count > 0;

        public static void SpeakHelp()
        {
            // The options panel is a different screen with different keys, so
            // the pause menu's help would be wrong here — the same defect as the
            // map controls answering on the sacrifice stone. His wording.
            if (OptionsOpen)
            {
                // THREE PAGES, AND THE HELP NAMES THEM. Zamar: "only 3 pages.
                // Read them out on H." A help line that says "1 to 4" when
                // there are three is the offer-a-dead-key defect in the one
                // place that exists to prevent it.
                // HIS WORDING, 0.7.103. "1, 2, and 3" rather than "1 to 3" —
                // a range read aloud is a range the listener has to expand, and
                // three keys named is three keys heard.
                Speech.Browse(
                    Vocabulary.Pause.OptionsAndSwitchPages());
                return;
            }

            // H CARRIES THE CHALLENGES TOO. (0.7.241.) Zamar: "Add info about
            // this to the H button on the pause menu too."
            //
            // The row is one arrow press past the last card, and H is the key a
            // player reaches for when they do not know what a screen holds — so
            // it says both that the row is there and what is on it, rather than
            // making them find it first. A run with no challenges says nothing
            // extra, because there is no extra stop to describe.
            string challenges = HasChallengeRow
                ? Vocabulary.Pause.TheyAreTheLast(ChallengeRowLine())
                : "";

            Speech.Browse(
                Vocabulary.Pause.PauseMenuArrowsBrowse(challenges));
        }
    }
}

// PauseMenuReader.cs
