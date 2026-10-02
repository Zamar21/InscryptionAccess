// ModSettingsMenu.cs
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using System;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// THE MOD SETTINGS MENU. (Session 37, MASTER_PLAN M9: per-announcement
    /// settings.)
    ///
    /// Say the Spire 2's, by Zamar's rule (Session 37): "Match their
    /// implementation and choices instead of asking me for my opinion."
    /// Theirs is UI/Screens/ModSettingsScreen.cs over Settings/*:
    ///   - Ctrl+M opens it from anywhere (their pad chord is LT+Start; IKMA's
    ///     LT is Tab and its hold-for-information button is LB, so LB+Start).
    ///   - A tree of categories. A category is a button; Enter opens it. A
    ///     true/false setting is a checkbox; Enter flips it and speaks only
    ///     "checked" / "unchecked". Changes save at once.
    ///   - Each level lists its children sorted by label.
    ///   - Up/Down move (Session 38: Left = Up, Right = Down, as the game's
    ///     own menus); at either end nothing happens (NavigableContainer
    ///     consumes the press and says nothing). Home/End jump to the ends.
    ///   - Backspace (pad B) closes ONE level; the parent is announced again
    ///     with its remembered focus. Session 38, Zamar: Escape (pad Start)
    ///     and Ctrl+M close the whole menu (theirs: one level each).
    ///   - A focus line is "label, type, status, N of M"; when focus crosses
    ///     into a list, the list's name leads (FocusContext's path diff).
    ///
    /// One difference: closing the top level says "Closed" (their Help
    /// screen's word). Theirs goes silent and the game's own focus speaks;
    /// IKMA has no general re-read of the screen underneath.
    ///
    /// Categories: Events (EventSettings.cs); History (Session 38, the Reads
    /// and Prompts switches, ReviewHistory.cs); and IKMA Manager's settings
    /// (Session 38, Zamar: "any settings from the external one that can exist
    /// internally also should. Exist in both"). A Manager setting is a
    /// category whose children are its choices, each a checkbox with only the
    /// current one checked; Enter on a choice selects it.
    ///
    /// CONTROLLER BUTTONS (Session 40), the Manager's last setting. Theirs is
    /// Settings/BindingSetting + UI/Screens/BindingListScreen, ListenScreen:
    /// each action is a button labelled "action: its buttons"; Enter leads to
    /// "Press a button... X to cancel.", the next press is the new binding
    /// ("Bound to Y"), and pressing the button the action already has
    /// cancels ("Cancelled"). Words checked against their Localization/eng/
    /// ui.json, 2026-10-01.
    /// IKMA's differs where Zamar has already decided otherwise:
    ///   - one controller button per action and no keyboard remapping, so
    ///     their list of bindings with Add / Replace / Delete is not needed
    ///     and Enter on an action goes straight to listening;
    ///   - a button another action has SWAPS the two (Zamar, Session 34);
    ///     theirs refuses with "already bound";
    ///   - the Manager's rules for which buttons may be used (KeyIn.cs), its
    ///     action names, its order, and its reset-everything choice.
    /// </summary>
    internal static class ModSettingsMenu
    {
        private sealed class Node
        {
            internal string Label;
            internal List<Node> Children;          // a category (button)
            internal Func<bool> IsOn;              // a checkbox: its state
            internal Action Flip;                  // a checkbox: what Enter does
            internal bool NextStart;               // read only when the game starts
            internal string Notice;                // Session 38: Enter says this instead of opening
            internal Action Run;                   // Session 40: a button that does something
            internal Func<string> LabelNow;        // Session 40: a label that changes while the menu is open
            internal string Text => LabelNow != null ? LabelNow() : Label;
            internal int Focus;                    // remembered focus, for a category
        }

        private static readonly List<Node> _stack = new List<Node>();

        internal static bool IsOpen => _stack.Count > 0;

        /// <summary>
        /// Every frame, ahead of every reader. True when the press was the
        /// menu's, so HotkeyManager ends the frame there.
        /// </summary>
        internal static bool HandleKeys()
        {
            bool ctrl = KeyIn.Held(KeyCode.LeftControl) || KeyIn.Held(KeyCode.RightControl);
            bool toggleKey = ctrl && KeyIn.Down(KeyCode.M);

            if (!IsOpen)
            {
                if (!toggleKey) return false;
                Open();
                return true;
            }

            // Session 40: waiting for the new controller button. Every press
            // is the listener's, the menu's own keys included.
            if (Listening) { HandleListen(toggleKey); return true; }

            // Zamar, Session 38: Escape (pad Start) and Ctrl+M close the whole
            // menu; Backspace (pad B) goes back one level.
            if (toggleKey || KeyIn.Down(KeyCode.Escape))
            {
                CloseAll();
                return true;
            }
            if (KeyIn.Down(KeyCode.Backspace))
            {
                Back();
                return true;
            }

            var level = _stack[_stack.Count - 1];
            int n = level.Children.Count;

            // Zamar, Session 38: "the same as the game" - Left and Up move up,
            // Right and Down move down.
            bool next = KeyIn.Down(KeyCode.DownArrow) || KeyIn.Down(KeyCode.RightArrow);
            bool prev = KeyIn.Down(KeyCode.UpArrow)   || KeyIn.Down(KeyCode.LeftArrow);
            if      (next) { if (level.Focus < n - 1) { level.Focus++; SpeakFocus(false); } }
            else if (prev) { if (level.Focus > 0)     { level.Focus--; SpeakFocus(false); } }
            else if (KeyIn.Down(KeyCode.Home))      { level.Focus = 0;     SpeakFocus(false); }
            else if (KeyIn.Down(KeyCode.End))       { level.Focus = n - 1; SpeakFocus(false); }
            else if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter)) Activate(level.Children[level.Focus]);
            // Session 40: H. Zamar's standing rule (0.7.266): "there should
            // never be a situation where pressing the H key does not read out
            // the current screen's context and controls." It used to fall
            // through to the battle or map help, about the screen underneath.
            else if (KeyIn.Down(KeyCode.H)) Speech.Browse(Vocabulary.ModSettings.Help);
            // Any other key is the menu's while it is open.
            return true;
        }

        private static void Open()
        {
            _listenAction = null;
            _stack.Clear();
            _stack.Add(BuildRoot());
            Plugin.Log?.LogInfo("IKMA SETTINGS: mod settings opened.");
            SpeakFocus(true);
        }

        private static void CloseAll()
        {
            _listenAction = null;
            _stack.Clear();
            Plugin.Log?.LogInfo("IKMA SETTINGS: mod settings closed.");
            Speech.Browse(Vocabulary.ModSettings.Closed);
        }

        private static void Back()
        {
            _stack.RemoveAt(_stack.Count - 1);
            if (_stack.Count == 0)
            {
                Plugin.Log?.LogInfo("IKMA SETTINGS: mod settings closed.");
                Speech.Browse(Vocabulary.ModSettings.Closed);
                return;
            }
            SpeakFocus(true);
        }

        private static void Activate(Node node)
        {
            if (node.Notice != null)
            {
                Speech.Browse(node.Notice);
                return;
            }
            if (node.Run != null)
            {
                node.Run();
                return;
            }
            if (node.Children != null)
            {
                node.Focus = 0;
                _stack.Add(node);
                SpeakFocus(true);
                return;
            }
            if (node.Flip == null) return;
            node.Flip();
            bool on = node.IsOn();
            Plugin.Log?.LogInfo($"IKMA SETTINGS: {PathText()} {node.Label} = {on}.");
            string word = on ? Vocabulary.ModSettings.Checked : Vocabulary.ModSettings.Unchecked;
            Speech.Browse(node.NextStart
                ? Vocabulary.ModSettings.Join(word, Vocabulary.ModSettings.NextStart)
                : word);
        }

        private static string PathText()
            => string.Join(" / ", _stack.Select(s => s.Label).ToArray());

        private static void SpeakFocus(bool withListName)
        {
            var level = _stack[_stack.Count - 1];
            if (level.Children.Count == 0) { Speech.Browse(level.Label); return; }
            var node = level.Children[level.Focus];

            var parts = new List<string>();
            if (withListName) parts.Add(level.Label);
            parts.Add(node.Text);
            if (node.Children != null || node.Run != null)
                parts.Add(Vocabulary.ModSettings.Button);
            else
            {
                parts.Add(Vocabulary.ModSettings.Checkbox);
                parts.Add(node.IsOn != null && node.IsOn()
                    ? Vocabulary.ModSettings.Checked : Vocabulary.ModSettings.Unchecked);
            }
            parts.Add(Vocabulary.ModSettings.Position(level.Focus + 1, level.Children.Count));
            Speech.Browse(Vocabulary.ModSettings.Join(parts.ToArray()));
        }

        // ------------------------------------------------------------------
        // The tree. Built on every open, so a changed language or a setting
        // edited in the cfg file is always current.
        // ------------------------------------------------------------------
        private static Node BuildRoot()
        {
            var groups = new Dictionary<string, Node>();
            foreach (var d in EventSettings.All)
            {
                if (d.Announce == null) continue;   // not bound
                Node group;
                if (!groups.TryGetValue(d.Group, out group))
                {
                    group = Category(Loc.T(d.Group));
                    groups[d.Group] = group;
                }

                var ev = Category(Loc.T(d.Label));
                ev.Children.Add(Checkbox(Vocabulary.ModSettings.Announce, d.Announce));
                ev.Children.Add(Checkbox(Vocabulary.ModSettings.AddToHistory, d.History));
                if (d.HasSources)
                {
                    if (d.SourcesAreDirect)
                    {
                        if (d.CurrentPlayer != null) ev.Children.Add(Checkbox(Loc.T(d.CurrentPlayerLabel), d.CurrentPlayer));
                        if (d.Enemies != null)       ev.Children.Add(Checkbox(Loc.T(d.EnemiesLabel), d.Enemies));
                    }
                    else
                    {
                        var sources = Category(Vocabulary.ModSettings.Sources);
                        if (d.CurrentPlayer != null) sources.Children.Add(Checkbox(Vocabulary.ModSettings.CurrentPlayer, d.CurrentPlayer));
                        if (d.Enemies != null)       sources.Children.Add(Checkbox(Vocabulary.ModSettings.Enemies, d.Enemies));
                        ev.Children.Add(sources);
                    }
                }
                Sort(ev);
                group.Children.Add(ev);
            }

            var events = Category(Vocabulary.ModSettings.Events);
            foreach (var g in groups.Values) { Sort(g); events.Children.Add(g); }
            Sort(events);

            var root = Category(Vocabulary.ModSettings.Title);
            root.Children.Add(events);

            // Session 38: History.
            var history = Category(Vocabulary.ModSettings.History);
            if (ReviewHistory.KeepReads != null)   history.Children.Add(Checkbox(Vocabulary.ModSettings.HistoryReads, ReviewHistory.KeepReads));
            if (ReviewHistory.KeepPrompts != null) history.Children.Add(Checkbox(Vocabulary.ModSettings.HistoryPrompts, ReviewHistory.KeepPrompts));
            Sort(history);
            if (history.Children.Count > 0) root.Children.Add(history);

            // Session 38: IKMA Manager's settings, the same choices in the
            // same order as the Manager lists them.
            if (AutoUpdate.Mode != null)
                root.Children.Add(Choice(Vocabulary.ModSettings.Updates, AutoUpdate.Mode, true,
                    new[] { UpdateMode.Automatic, UpdateMode.DownloadThenAsk, UpdateMode.AskFirst, UpdateMode.Off },
                    new[] { Vocabulary.ModSettings.UpdatesAutomatic, Vocabulary.ModSettings.UpdatesDownloadThenAsk,
                            Vocabulary.ModSettings.UpdatesAskFirst, Vocabulary.ModSettings.UpdatesOff }));
            if (SpeechBackends.Choice != null)
                root.Children.Add(Choice(Vocabulary.ModSettings.SpeechEngine, SpeechBackends.Choice, true,
                    new[] { SpeechBackendChoice.Auto, SpeechBackendChoice.NVDA, SpeechBackendChoice.LinuxBridge },
                    new[] { Vocabulary.ModSettings.SpeechAuto, Vocabulary.ModSettings.SpeechNvda, Vocabulary.ModSettings.SpeechLinux }));
            if (Loc.Setting != null)
            {
                var langs = new List<IkmaLanguage> { IkmaLanguage.Game };
                var names = new List<string> { Vocabulary.ModSettings.LanguageGame };
                var all = (IkmaLanguage[])Enum.GetValues(typeof(IkmaLanguage));
                for (int i = 1; i < all.Length && i - 1 < Vocabulary.ModSettings.LanguageNames.Length; i++)
                {
                    langs.Add(all[i]);
                    names.Add(Vocabulary.ModSettings.LanguageNames[i - 1]);
                }
                var language = Choice(Vocabulary.ModSettings.Language, Loc.Setting, false, langs.ToArray(), names.ToArray());
                // Zamar, Session 38: "Feature coming soon" until translations ship.
                if (!Loc.LocalizationShipped) language.Notice = Vocabulary.ModSettings.ComingSoon;
                root.Children.Add(language);
            }
            if (GamepadSupport.Vibration != null)
                root.Children.Add(Choice(Vocabulary.ModSettings.Vibration, GamepadSupport.Vibration, false,
                    new[] { GamepadSupport.VibrationLevel.Off, GamepadSupport.VibrationLevel.Low,
                            GamepadSupport.VibrationLevel.Medium, GamepadSupport.VibrationLevel.High },
                    new[] { Vocabulary.ModSettings.Off, Vocabulary.ModSettings.Low,
                            Vocabulary.ModSettings.Medium, Vocabulary.ModSettings.High }));
            // Session 40: Controller buttons. Not offered when IKMA is not
            // the one reading the controller ([Gamepad] IKMADrivesPad off):
            // the game's own layout is in use then and nothing here applies.
            if (KeyIn.PadOwned)
            {
                var pad = Category(Vocabulary.ModSettings.ControllerButtons);
                foreach (var d in KeyIn.MovableDefaults)
                {
                    string action = d.Action;
                    pad.Children.Add(new Node { LabelNow = () => ControllerRow(action), Run = () => StartListening(action) });
                }
                pad.Children.Add(new Node { Label = Vocabulary.ModSettings.ControllerResetRow, Run = ResetController });
                root.Children.Add(pad);
            }
            if (DiagnosticGate.Show != null)
            {
                var show = DiagnosticGate.Show;
                var log = Category(Vocabulary.ModSettings.FullLog);
                log.Children.Add(new Node { Label = Vocabulary.ModSettings.FullLogOff,
                    IsOn = () => string.IsNullOrEmpty((show.Value ?? "").Trim()),
                    Flip = () => show.Value = "" });
                log.Children.Add(new Node { Label = Vocabulary.ModSettings.FullLogOn,
                    IsOn = () => string.Equals((show.Value ?? "").Trim(), "ALL", StringComparison.OrdinalIgnoreCase),
                    Flip = () => show.Value = "ALL" });
                root.Children.Add(log);
            }

            Sort(root);
            return root;
        }

        // ------------------------------------------------------------------
        // CONTROLLER BUTTONS. (Session 40.) See the class note for what is
        // Say the Spire 2's and what is Zamar's. KeyIn owns the map and its
        // rules; this is only the menu over it.
        //
        // Every line here already names BUTTONS, so each is marked
        // PadWords.Literal: the key-name rewrite must leave it alone (to it,
        // the A button looks like the A key).
        // ------------------------------------------------------------------
        private static string _listenAction;        // the action waiting for its new button
        private static float _listenIdle, _listenIdleNext;

        /// <summary>True while the next controller press is a new button, not a command.</summary>
        internal static bool Listening => _listenAction != null;

        private static KeyIn.Binding DefaultFor(string action)
        {
            foreach (var d in KeyIn.MovableDefaults) if (d.Action == action) return d;
            return null;
        }

        private static string Spoken(KeyIn.Binding b)
            => b == null ? Vocabulary.ModSettings.ControllerNoButton : PadWords.Gesture(b.Chord, b.Button);

        /// <summary>"Help: View", or "Help: Y. Default View." once it has been moved.</summary>
        private static string ControllerRow(string action)
        {
            string name = Vocabulary.ModSettings.ControllerActionName(action);
            var cur = KeyIn.CurrentFor(action);
            var def = DefaultFor(action);
            bool moved = cur == null || def == null || cur.Chord != def.Chord || cur.Button != def.Button;
            return PadWords.Literal(moved && def != null
                ? Vocabulary.ModSettings.ControllerActionMoved(name, Spoken(cur), Spoken(def))
                : Vocabulary.ModSettings.ControllerAction(name, Spoken(cur)));
        }

        private static string ListenPrompt()
        {
            string lb = PadWords.ButtonName(PadInput.PadButton.LB), rb = PadWords.ButtonName(PadInput.PadButton.RB);
            var cur = KeyIn.CurrentFor(_listenAction);
            return PadWords.Literal(cur != null
                ? Vocabulary.ModSettings.ControllerListen(Spoken(cur)) + " " + Vocabulary.ModSettings.ControllerListenChords(lb, rb)
                : Vocabulary.ModSettings.ControllerListenNoButton(lb, rb));
        }

        private static void StartListening(string action)
        {
            _listenAction = action;
            _listenIdle = 0f;
            _listenIdleNext = LISTEN_IDLE_FIRST;
            Plugin.Log?.LogInfo($"IKMA SETTINGS: Controller buttons, waiting for a button for {action}.");
            Speech.Browse(ListenPrompt());
        }

        // The project's idle rule: 5.5 seconds, then every 15, the whole
        // prompt each time. This state waits on the player and says nothing
        // else, so without it a player who looked away has no way to know
        // the next press will be taken as a button.
        private const float LISTEN_IDLE_FIRST = 5.5f, LISTEN_IDLE_REPEAT = 15f;

        private static void HandleListen(bool toggleKey)
        {
            KeyIn.Tick();   // this frame's controller presses, if nothing has asked yet

            // THE CONTROLLER FIRST. A press there is the answer, whatever key
            // that button normally stands for (B is Backspace, Menu is
            // Escape - but here they are B and Menu).
            if (PadInput.AnyPressed)
            {
                foreach (PadInput.PadButton b in Enum.GetValues(typeof(PadInput.PadButton)))
                {
                    if (b == PadInput.PadButton.LB || b == PadInput.PadButton.RB) continue;   // the chords, never a button
                    if (!PadInput.Pressed(b)) continue;
                    bool lb = PadInput.Held(PadInput.PadButton.LB), rb = PadInput.Held(PadInput.PadButton.RB);
                    Capture(lb && rb ? KeyIn.Chord.Both : lb ? KeyIn.Chord.LB : rb ? KeyIn.Chord.RB : KeyIn.Chord.None, b);
                    return;
                }
                return;   // a shoulder button on its own: a chord being held
            }

            // The keyboard: Escape and Ctrl+M close the menu, Backspace
            // goes back - the same three keys as everywhere else in it.
            if (toggleKey || KeyIn.Down(KeyCode.Escape)) { CloseAll(); return; }
            // H on the keyboard: this state's context and controls are its prompt.
            if (KeyIn.Down(KeyCode.H))
            {
                _listenIdle = 0f;
                _listenIdleNext = LISTEN_IDLE_REPEAT;
                Speech.BrowseUnrecorded(ListenPrompt());
                return;
            }
            if (KeyIn.Down(KeyCode.Backspace))
            {
                Plugin.Log?.LogInfo($"IKMA SETTINGS: Controller buttons, {_listenAction} left as it was.");
                _listenAction = null;
                Speech.Browse(Vocabulary.ModSettings.Cancelled);
                return;
            }

            _listenIdle += Time.unscaledDeltaTime;
            if (_listenIdle < _listenIdleNext) return;
            _listenIdle = 0f;
            _listenIdleNext = LISTEN_IDLE_REPEAT;
            Speech.BrowseUnrecorded(ListenPrompt());
        }

        private static void Capture(KeyIn.Chord chord, PadInput.PadButton button)
        {
            string action = _listenAction;
            string name = Vocabulary.ModSettings.ControllerActionName(action);
            string pressed = PadWords.Gesture(chord, button);
            _listenIdle = 0f;
            _listenIdleNext = LISTEN_IDLE_REPEAT;

            // Theirs: pressing the binding the action already has cancels.
            var cur = KeyIn.CurrentFor(action);
            if (cur != null && cur.Chord == chord && cur.Button == button)
            {
                Plugin.Log?.LogInfo($"IKMA SETTINGS: Controller buttons, {action} left as it was.");
                _listenAction = null;
                Speech.Browse(Vocabulary.ModSettings.Cancelled);
                return;
            }

            // A button IKMA's rules do not allow. Said, and it keeps listening.
            if (!KeyIn.GestureAllowed(chord, button))
            {
                Plugin.Log?.LogInfo($"IKMA SETTINGS: Controller buttons, {KeyIn.GestureText(chord, button)} cannot be used for {action}.");
                Speech.Browse(PadWords.Literal(button >= PadInput.PadButton.RUp
                    ? Vocabulary.ModSettings.ControllerRightStick
                    : Vocabulary.ModSettings.ControllerGameButton(pressed,
                        PadWords.ButtonName(PadInput.PadButton.LB), PadWords.ButtonName(PadInput.PadButton.RB))));
                return;
            }

            string other = KeyIn.MoveAction(action, chord, button);
            _listenAction = null;

            // What is said is read back from the map, not assumed from the press.
            string now = Spoken(KeyIn.CurrentFor(action));
            if (other != null)
            {
                Plugin.Log?.LogInfo($"IKMA SETTINGS: Controller buttons, {action} and {other} swapped; {action} is on {KeyIn.GestureText(chord, button)}.");
                Speech.Browse(PadWords.Literal(Vocabulary.ModSettings.ControllerSwapped(
                    name, now, Vocabulary.ModSettings.ControllerActionName(other), Spoken(KeyIn.CurrentFor(other)))));
            }
            else
            {
                Plugin.Log?.LogInfo($"IKMA SETTINGS: Controller buttons, {action} moved to {KeyIn.GestureText(chord, button)}.");
                Speech.Browse(PadWords.Literal(Vocabulary.ModSettings.ControllerBound(now)));
            }
        }

        private static void ResetController()
        {
            KeyIn.ResetControllerMap();
            Plugin.Log?.LogInfo("IKMA SETTINGS: Controller buttons, every action back on its default.");
            Speech.Browse(Vocabulary.ModSettings.ControllerResetDone);
        }

        /// <summary>
        /// A Manager setting with one value from a list: a category whose
        /// children are the choices. Enter on a choice selects it.
        /// </summary>
        private static Node Choice<T>(string title, ConfigEntry<T> entry, bool nextStart, T[] values, string[] labels)
        {
            var node = Category(title);
            for (int i = 0; i < values.Length && i < labels.Length; i++)
            {
                T v = values[i];
                node.Children.Add(new Node
                {
                    Label = labels[i],
                    IsOn = () => EqualityComparer<T>.Default.Equals(entry.Value, v),
                    Flip = () => entry.Value = v,
                    NextStart = nextStart,
                });
            }
            return node;
        }

        private static Node Category(string label)
            => new Node { Label = label, Children = new List<Node>() };

        private static Node Checkbox(string label, ConfigEntry<bool> setting)
            => new Node { Label = label, IsOn = () => setting.Value, Flip = () => setting.Value = !setting.Value };

        // Theirs: OrderBy(SortPriority).ThenBy(Label); every priority here is 0.
        private static void Sort(Node category)
            => category.Children.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.CurrentCulture));
    }
}
// ModSettingsMenu.cs
