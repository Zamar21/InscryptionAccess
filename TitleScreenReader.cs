// TitleScreenReader.cs
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// INSCRYPTION'S OWN TITLE SCREEN — the one that is not Kaycee's Mod.
    /// (0.7.231.)
    ///
    /// Zamar: "I want the title screen of non-KM working for v.5 as well, but
    /// when pressing enter on Continue, I want it to read 'Inscryption Main
    /// game support still in development. I'll get it done don't worry!
    /// -Zamar :)'"
    ///
    /// WHY THIS IS NOT MenuReader'S JOB. MenuReader is driven by
    /// AscensionMenuScreenTransition.OnEnable — the game hands it a live screen
    /// and a List&lt;MainInputInteractable&gt; of that screen's contents. The base
    /// title screen has neither. It is a MenuController with a private
    /// List&lt;MenuCard&gt; and a MenuSlot, and a card is "pressed" by being carried
    /// into the slot, not clicked. Two different games' worth of machinery
    /// behind the same idea, so two readers.
    ///
    /// HOW A CARD IS ACTUALLY ACTIVATED — THE SAME TWO STEPS PauseMenuReader
    /// ALREADY PROVED ON THIS EXACT CLASS. Zamar, 0.7.85: "When you select
    /// Options it's the same as clicking it which picks up the options card,
    /// but then you need to mouse over and drop it to select it. We need to
    /// mimic all that movement." So Enter is: click the card, which lifts it;
    /// wait for the game's own DoingCardTransition to go false; then click the
    /// slot, which plays it. MenuController.PlayMenuCardImmediate looks like a
    /// one-call shortcut for the same thing, and is deliberately NOT used — the
    /// gesture above is the one with a playtest behind it, and a title screen
    /// that silently refuses Enter would cost a whole session to find out.
    ///
    /// AND THE SINGLETON IS NEVER POLLED. Singleton&lt;T&gt;.Instance falls back to
    /// Object.FindObjectOfType INSIDE A LOCK when the instance is absent, and a
    /// per-frame call to it from Update is what killed 0.7.216. The
    /// MenuController arrives here from a Harmony patch on its own Start and is
    /// dropped on every scene load. Nothing in this file searches the scene.
    /// </summary>
    public static class TitleScreenReader
    {
        private static ManualLogSource _log;

        private static MenuController _menu;
        private static string _sceneAtCapture;

        private static FieldInfo _cardsField;
        private static FieldInfo _optionsField;

        // The card the game is showing as hovered. IKMA drives the game's own
        // cursor primitive so the screen looks the way it sounds — see
        // HoverCard.
        private static MenuCard _hovered;

        // Same NOWHERE contract as MenuReader and NodeScreenReader: arrival
        // names the first card, the cursor is not standing on it, and the first
        // arrow press lands ON it rather than stepping past it.
        private const int NOWHERE = -1;
        private static int _index = NOWHERE;

        private static bool _announced;
        private static float _settleFor;
        private const float SETTLE_SECONDS = 0.45f;

        /// <summary>The scene the base title screen lives in. From the log's
        /// own SCENE LOADED lines, not guessed.</summary>
        internal const string TITLE_SCENE = "Start";

        public static void Init(ManualLogSource log) => _log = log;

        // ------------------------------------------------------------------
        // Arming and disarming
        // ------------------------------------------------------------------

        /// <summary>
        /// Called from the MenuController.Start postfix. The same class runs
        /// the in-game pause menus (Concede, End Run, Return To Start Menu), so
        /// the scene it was captured in is recorded and checked — this reader
        /// speaks on the title screen and nowhere else.
        /// </summary>
        internal static void Capture(MenuController menu)
        {
            if (menu == null) return;

            _menu = menu;
            _sceneAtCapture = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            _index = NOWHERE;
            _announced = false;
            _settleFor = 0f;
            _hovered = null;

            _log?.LogInfo($"IKMA TITLE: MenuController captured in scene '{_sceneAtCapture}'.");

            // THE OPTIONS PANEL ON THIS SCREEN IS THE SAME PANEL AS THE PAUSE
            // MENU'S, hanging off a different root. (0.7.232.) OptionsReader
            // anchored on PauseMenu.instance, which does not exist here, so the
            // panel opened and read nothing. Handing it the root now — while
            // the object is still inactive — costs nothing: PanelRoot only
            // prefers the override while it is actually on screen.
            if (_sceneAtCapture == TITLE_SCENE)
                OptionsReader.SetPanelRoot(OptionsUI());
        }

        /// <summary>Called from the scene-load path, like every other reader.</summary>
        public static void Reset()
        {
            ReleaseHover();
            OptionsReader.SetPanelRoot(null);
            _menu = null;
            _sceneAtCapture = null;
            _index = NOWHERE;
            _announced = false;
            _settleFor = 0f;
        }

        /// <summary>
        /// Is the base title screen up and taking keys? Cheap and allocation
        /// free — this is asked every frame.
        /// </summary>
        public static bool Active
        {
            get
            {
                if (_menu == null) return false;
                if (_sceneAtCapture != TITLE_SCENE) return false;

                try
                {
                    if (_menu.gameObject == null || !_menu.gameObject.activeInHierarchy) return false;
                    if (_menu.TransitioningToScene) return false;
                }
                catch { return false; }

                return true;
            }
        }

        /// <summary>
        /// MenuController's private `optionsUI` GameObject — the options panel
        /// this screen shows once the Options card is in the slot. Resolved
        /// once and held; it is the same object for the life of the scene.
        /// </summary>
        private static GameObject OptionsUI()
        {
            if (_menu == null) return null;

            try
            {
                if (_optionsField == null)
                    _optionsField = typeof(MenuController).GetField(
                        "optionsUI", BindingFlags.Instance | BindingFlags.NonPublic);

                if (_optionsField == null)
                {
                    _log?.LogWarning("IKMA TITLE: MenuController.optionsUI not found. Options unreadable here.");
                    return null;
                }

                return _optionsField.GetValue(_menu) as GameObject;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA TITLE: reading optionsUI threw {e.GetType().Name}.");
                return null;
            }
        }

        /// <summary>
        /// Is the options panel up over the title screen? Asked of the screen
        /// every frame rather than remembered — the panel is switched on and
        /// off by the game, and the mouse can do it too.
        /// </summary>
        public static bool OptionsOpen
        {
            get
            {
                if (!Active) return false;
                var ui = OptionsUI();
                if (ui == null) return false;
                try { return ui.activeInHierarchy; } catch { return false; }
            }
        }

        // ------------------------------------------------------------------
        // The cards
        // ------------------------------------------------------------------

        /// <summary>
        /// MenuController's private List&lt;MenuCard&gt; cards, in the order the
        /// game lays them out left to right. Locked cards stay in the list: a
        /// sighted player sees them sitting there glitched, so a blind player
        /// hears them and hears that they are locked. Cards the game has
        /// REMOVED (TweenInCards drops the Kaycee's Mod card before the chapter
        /// select is unlocked) are gone from the list and are correctly silent.
        /// </summary>
        private static List<MenuCard> Cards()
        {
            var result = new List<MenuCard>();
            if (_menu == null) return result;

            try
            {
                if (_cardsField == null)
                    _cardsField = typeof(MenuController).GetField(
                        "cards", BindingFlags.Instance | BindingFlags.NonPublic);

                if (_cardsField == null)
                {
                    _log?.LogWarning("IKMA TITLE: MenuController.cards not found. Title screen unreadable.");
                    return result;
                }

                var raw = _cardsField.GetValue(_menu) as List<MenuCard>;
                if (raw == null) return result;

                foreach (var card in raw)
                {
                    if (card == null) continue;
                    if (card.gameObject == null || !card.gameObject.activeInHierarchy) continue;
                    result.Add(card);
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA TITLE: reading the card list failed: {e.Message}");
            }

            return result;
        }

        /// <summary>
        /// What one card is called. The title a sighted player reads is a
        /// SPRITE in English (MenuController.DisplayMenuCardTitle picks the
        /// sprite over the text whenever the language is English and a sprite
        /// exists), so there is no guaranteed string on the object. Every
        /// source is tried in turn and the one that answered is logged, the
        /// same way MenuReader logs its label sources — one playtest names the
        /// real source and this stops being a guess.
        /// </summary>
        private static string LabelFor(MenuCard card)
        {
            if (card == null) return Vocabulary.Title.OptionFallback;

            string via = "MenuAction";
            string label = null;

            try
            {
                if (!string.IsNullOrEmpty(card.TitleText))
                {
                    label = card.TitleText;
                    via = "MenuCard.TitleText";
                }
                else if (!string.IsNullOrEmpty(card.TitleLocId))
                {
                    label = Localization.TranslateWithID(card.TitleLocId);
                    via = "MenuCard.TitleLocId";
                }
            }
            catch { label = null; }

            if (string.IsNullOrEmpty(label))
                label = ActionName(card.MenuAction);

            label = Prettify(label);
            _log?.LogInfo($"IKMA TITLE: card label='{label}' via {via} (action {card.MenuAction}).");
            return label;
        }

        /// <summary>
        /// The fallback name, from the one field that is always populated and
        /// always public. These are the game's own menu words, not new wording.
        /// </summary>
        private static string ActionName(MenuAction action)
        {
            switch (action)
            {
                case MenuAction.NewGame:           return Vocabulary.Title.NewGame;
                case MenuAction.Continue:          return Vocabulary.Title.Continue;
                case MenuAction.Options:           return Vocabulary.Title.Options;
                case MenuAction.Credits:           return Vocabulary.Title.Credits;
                case MenuAction.Quit:              return Vocabulary.Title.Quit;
                case MenuAction.Library:           return Vocabulary.Title.CardLibrary;
                case MenuAction.EditDeck:          return Vocabulary.Title.EditDeck;
                case MenuAction.EnterAscension:    return Vocabulary.Title.KayceesMod;
                case MenuAction.ReturnToStartMenu: return Vocabulary.Title.ReturnToStartMenu;
                case MenuAction.EndRun:            return Vocabulary.Title.EndRun;
                case MenuAction.Concede:           return Vocabulary.Title.Concede;
                default:                           return Vocabulary.Title.OptionFallback;
            }
        }

        /// <summary>The titles are drawn in caps. Speech is not.</summary>
        private static string Prettify(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            string s = raw.Trim().Trim('-', ' ');
            if (s.Length == 0) return raw;

            // Only re-case a string that is ALL caps; anything the game already
            // wrote in mixed case is left exactly as written.
            bool allCaps = true;
            foreach (char c in s)
                if (char.IsLetter(c) && !char.IsUpper(c)) { allCaps = false; break; }

            if (!allCaps) return s;

            var sb = new System.Text.StringBuilder(s.Length);
            bool startOfWord = true;
            foreach (char c in s)
            {
                sb.Append(startOfWord ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                startOfWord = !char.IsLetter(c) && c != '\'';
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // THE SCREEN FOLLOWS THE ARROWS. (0.7.232.)
        //
        // Zamar: "The left and right arrows work on the main menu now, but the
        // cursor hover isn't. We need that to work the same way it does
        // elsewhere for the sighted viewers."
        //
        // MenuCard.OnCursorEnter is what raises the card's border AND calls
        // MenuController.OnCardCursorEntered, which prints the card's title
        // across the screen — so without this the title text stayed blank and
        // the whole menu looked untouched while it was being read out. Same
        // primitive MenuReader.HoverMenuItem uses on the Kaycee's Mod screens,
        // so the two menus behave identically for anyone watching.
        // ------------------------------------------------------------------
        private static void HoverCard(MenuCard entering)
        {
            try
            {
                if (_hovered != null && !ReferenceEquals(_hovered, entering)) _hovered.CursorExit();
                if (entering != null) entering.CursorEnter();
                _hovered = entering;
            }
            catch (System.Exception e)
            {
                // Presentation must never break navigation.
                _log?.LogWarning($"IKMA TITLE: hover sync failed: {e.Message}");
            }
        }

        private static void ReleaseHover()
        {
            try { if (_hovered != null) _hovered.CursorExit(); }
            catch { }
            _hovered = null;
        }

        /// <summary>
        /// What one card says when the cursor lands on it.
        ///
        /// NO "LOCKED" HERE, AND THAT IS DELIBERATE. (0.7.232.) 0.7.231 read
        /// "New Game. Locked." and Zamar cut it: "New game should not say
        /// locked. It should do the same as clicking on New Game and causing
        /// that glitch to happen." A sighted player is not told the card is
        /// locked either — they press it and the screen glitches at them. The
        /// glitch is audible, so parity is served by letting the press happen
        /// rather than by describing a state the screen never states. See
        /// Activate.
        /// </summary>
        private static string Describe(MenuCard card) => LabelFor(card) + ".";

        // ------------------------------------------------------------------
        // Arrival
        // ------------------------------------------------------------------

        public static bool NeedsAnnouncement(float deltaTime)
        {
            if (!Active || _announced) return false;

            if (Cards().Count == 0)
            {
                _settleFor = 0f;
                return false;
            }

            _settleFor += deltaTime;
            return _settleFor >= SETTLE_SECONDS;
        }

        public static void Announce()
        {
            var cards = Cards();
            if (cards.Count == 0) return;

            _announced = true;
            _index = NOWHERE;

            // ARRIVAL NAMES NO CARD. (0.7.234.)
            //
            // Zamar, on "Inscryption title screen. New Game. Arrows to
            // navigate, Enter to confirm.": "New Game should not be listed
            // here."
            //
            // Every other screen in the mod leads with the option the cursor is
            // on, and this one is the exception because the cursor is not on
            // anything: the cards tween in with nothing hovered and no title
            // printed, and a sighted player sees exactly that. Naming a card
            // here also lit it up on screen through the hover, which said the
            // player was standing somewhere they were not. The hover is
            // released with it — nothing on, nothing named, and the first arrow
            // press puts them on card one.
            ReleaseHover();

            Speech.Browse(Vocabulary.Title.InscryptionTitleScreenArrows);
        }

        // ------------------------------------------------------------------
        // Keys
        // ------------------------------------------------------------------

        public static void Browse(int direction)
        {
            if (!_announced) return;

            var cards = Cards();
            if (cards.Count == 0)
            {
                Speech.Browse(Vocabulary.NoOptionsAvailable);
                return;
            }

            if ((_index == NOWHERE || _index >= cards.Count) && !ListStep.IsJump(direction))
                _index = direction >= 0 ? 0 : cards.Count - 1;
            else
            { if (!ListStep.Step(ref _index, direction, cards.Count)) return; }   // 0.4.8.007 - stops at the ends

            HoverCard(cards[_index]);
            Speech.Browse(Describe(cards[_index]));
        }

        public static void AnnounceCurrent()
        {
            var cards = Cards();
            if (cards.Count == 0)
            {
                Speech.Browse(Vocabulary.NoOptionsAvailable);
                return;
            }

            int here = (_index == NOWHERE || _index >= cards.Count) ? 0 : _index;
            Speech.Browse(Vocabulary.OptionOf(Describe(cards[here]), here + 1, cards.Count));
        }

        public static void Activate()
        {
            var cards = Cards();
            if (cards.Count == 0)
            {
                Speech.Browse(Vocabulary.NoOptionsAvailable);
                return;
            }

            if (_index == NOWHERE || _index >= cards.Count) _index = 0;

            var card = cards[_index];
            string name = LabelFor(card);

            // A LOCKED CARD IS PRESSED, NOT REFUSED. (0.7.232.)
            //
            // Zamar: "It should do the same as clicking on New Game and causing
            // that glitch to happen." MenuController.OnCardSelected answers a
            // locked card with a glitch sound and a screen glitch effect, and
            // that IS the game's reply — audible, so a blind player gets the
            // same answer a sighted one does. IKMA adds no words to it.
            //
            // The LIFT is performed and the DROP is skipped. OnCardSelected
            // assigns SelectedCard even for a locked card but leaves the slot
            // disabled, so pressing the slot afterwards would be IKMA pushing
            // past a door the game just shut.
            bool locked = false;
            try { locked = card.Locked; } catch { }
            if (locked)
            {
                _log?.LogInfo($"IKMA TITLE: '{name}' is locked — pressing it for the game's own glitch.");
                try { card.CursorSelectStart(); card.CursorSelectEnd(); }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA TITLE: pressing the locked card threw {e.GetType().Name}.");
                }

                // AND THEN SAY WHAT WAS SEEN. (0.7.234.) Zamar: "When pressing
                // Enter on New Game and the screen glitches, have it read 'The
                // screen glitches for a second.'" The glitch is a sound AND a
                // picture; 0.7.233 let the sound stand for both, and half the
                // answer was missing.
                //
                // THREE QUARTERS OF A SECOND BEHIND THE PRESS. (0.7.235, his
                // measurement after hearing it.) Said on the same frame it
                // arrived under the game's own glitch noise, so the line
                // competed with the thing it was describing. A description
                // waits for what it describes to happen.
                SpeakAfter(0.75f, Vocabulary.TitleCardGlitches());
                return;
            }

            // THE MAIN GAME IS NOT BUILT YET, AND THE KEY SAYS SO. (0.7.231.)
            //
            // Zamar's own line, word for word. Both doors into Act 1 get it:
            // CONTINUE is the one he named, and NEW GAME lands in exactly the
            // same unnarrated place by a different route. Opening either would
            // put a blind player inside a game this mod does not speak for yet,
            // with no announcement to tell them where they are.
            if (card.MenuAction == MenuAction.Continue || card.MenuAction == MenuAction.NewGame)
            {
                _log?.LogInfo($"IKMA TITLE: '{name}' ({card.MenuAction}) held — main game support is not built.");
                Speech.Browse(Vocabulary.MainGameNotSupported());
                return;
            }

            _log?.LogInfo($"IKMA TITLE: activating '{name}' ({card.MenuAction}).");

            // Step one: lift the card. The universal cursor primitive, the same
            // one the draw piles, map nodes, item slots and the pause menu use.
            try { card.CursorSelectStart(); card.CursorSelectEnd(); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA TITLE: picking the card up threw {e.GetType().Name}.");
                Speech.Browse(Vocabulary.OptionCouldNotBeSelected);
                return;
            }

            Speech.Confirm(name + ".");

            // Step two happens when the game says the card has arrived, not
            // after a duration IKMA invented.
            CombatAnnouncer.Instance?.StartCoroutine(DropWhenReady());
        }

        /// <summary>
        /// Say something a moment from now. Used where a line describes
        /// something the game is still in the middle of doing — see the glitch
        /// on a locked card. A coroutine rather than a queued line, because the
        /// wait is tied to the PICTURE, not to whatever else is speaking.
        /// </summary>
        private static void SpeakAfter(float seconds, string line)
        {
            var host = CombatAnnouncer.Instance;
            if (host == null) { Speech.Browse(line); return; }
            host.StartCoroutine(SpeakAfterRoutine(seconds, line));
        }

        private static System.Collections.IEnumerator SpeakAfterRoutine(float seconds, string line)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Speech.Browse(line);
        }

        private static FieldInfo _slotField;

        /// <summary>
        /// Drop the lifted card into the menu slot once MenuController says its
        /// own tween has finished. DoingCardTransition and CardInSlot are both
        /// PUBLIC — the game answers both questions, so nothing here waits a
        /// guessed number of seconds or tracks state of its own.
        /// </summary>
        private static System.Collections.IEnumerator DropWhenReady()
        {
            var menu = _menu;
            if (menu == null) yield break;

            for (int i = 0; i < 60; i++)
            {
                bool moving = true;
                try { moving = menu.DoingCardTransition; } catch { moving = false; }
                if (!moving) break;
                yield return new WaitForSecondsRealtime(0.05f);
            }

            if (!Active) yield break;

            // Already slotted? Then the lift did the whole job and pressing the
            // slot as well would take the card back out again.
            bool slotted = false;
            try { slotted = menu.CardInSlot; } catch { }
            if (slotted)
            {
                _log?.LogInfo("IKMA TITLE: card is already in the slot; no drop needed.");
                yield break;
            }

            if (_slotField == null)
            {
                _slotField = typeof(MenuController).GetField("menuSlot",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            MenuSlot slot = null;
            try { slot = _slotField?.GetValue(menu) as MenuSlot; } catch { }

            if (slot == null)
            {
                _log?.LogWarning("IKMA TITLE: no menu slot to drop into.");
                yield break;
            }

            _log?.LogInfo("IKMA TITLE: dropping the card into the slot.");
            try { slot.CursorSelectStart(); slot.CursorSelectEnd(); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA TITLE: dropping threw {e.GetType().Name}.");
            }
        }

        /// <summary>
        /// Back on the cards after the options panel closes. The screen name is
        /// spoken again because the player has just come back to it from
        /// somewhere else, which is the one case MenuReader also re-announces.
        /// </summary>
        public static void ReAnnounce()
        {
            _announced = false;
            _settleFor = 0f;
            _index = NOWHERE;
            Announce();
        }

        public static void Help()
        {
            Speech.Browse(
                Vocabulary.Title.InscryptionTitleScreenArrow());
        }
    }
}
