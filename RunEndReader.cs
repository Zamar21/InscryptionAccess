// RunEndReader.cs
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The Kaycee's Mod run end screen. (Session 16.)
    ///
    /// EVERY RUN FINISHES HERE, win or lose, and until now it was total
    /// silence — not "unreadable", silence. The unreadable-screen net cannot
    /// reach it: that net needs a live MapNodeManager.ActiveNode to name where
    /// the player is standing, and a finished run has torn the map down. So the
    /// one screen no player can skip was the one screen with no prompt at all.
    ///
    /// WHAT IS ON IT, and this decides everything else in this file.
    /// AscensionRunEndScreen declares exactly three things a player can
    /// perceive: isVictory, titleText, and retryButton. That is all. It does
    /// NOT hold the run's stats.
    ///
    /// THE STATS ARE DELIBERATELY NOT READ. AscensionSaveData.Data is a PUBLIC
    /// STATIC route to AscensionStatsData.currentRunStats, and every stat
    /// carries the game's own label from AscensionStat.GetStringForType() — so
    /// reading them here would have been easy and would have needed no invented
    /// wording. Zamar's call was to leave them out: "It should only read what is
    /// visually on that screen. If the stats aren't there and only in the stat
    /// menu then don't add them."
    ///
    /// That is the parity rule in the direction it usually does not run. A
    /// sighted player reads those numbers on the Stats screen off the Kaycee's
    /// Mod menu, not here, and IKMA does not hand a blind player a screen's
    /// worth of information the screen is not showing. Do not add them back
    /// without him asking.
    ///
    /// VICTORY_TITLE and DEFEAT_TITLE are compile-time literals in the
    /// assembly — "VICTORY" and "DEFEAT..." — confirmed by reading their raw
    /// constant values in dump_act1_nodes_interaction.txt. They are the game's
    /// own words for the outcome, so nothing here is invented. The live
    /// titleText is preferred over them anyway, because that is the string
    /// actually on the screen; the constants are the fallback.
    ///
    /// NOT SCENE-SEARCHED. Initialize(Boolean victory) is PUBLIC and hands the
    /// screen over from its own patch, the same way CardChoicesSequencer hands
    /// over the card reward.
    /// </summary>
    public static class RunEndReader
    {
        private static ManualLogSource _log;

        private static AscensionRunEndScreen _screen;
        private static bool _victory;

        private static FieldInfo _retryField;
        private static FieldInfo _titleField;

        private static bool  _announced;
        private static float _settleFor;

        // The screen's own Start() wires the button and ShowRunOutcomeText()
        // fills the title, and Initialize fires before both have necessarily
        // run. Settle rather than guess. Settle windows are free.
        private const float SETTLE_SECONDS = 0.5f;

        // Standing rule: any state where the game waits on player input needs
        // an audible prompt. This screen waits indefinitely. Same timing as the
        // card choice screen, whose opening line is a comparable length.
        private const float IDLE_FIRST  = 20f;
        private const float IDLE_REPEAT = 20f;
        private static float _idleTimer;
        private static float _idleInterval = IDLE_FIRST;
        private static bool  _idlePending;

        public static void Init(ManualLogSource log) => _log = log;

        /// <summary>True while the run end screen owns the keyboard.</summary>
        public static bool Active => _screen != null;

        internal static void Begin(AscensionRunEndScreen screen, bool victory)
        {
            _screen    = screen;
            _victory   = victory;
            _announced = false;
            _settleFor = 0f;
            ResetIdle();

            // The death card screen runs immediately before this one on a lost
            // run. Its explicit unreadable context has no end hook of its own —
            // arriving here IS the end of it.
            HotkeyManager.ClearExplicitUnreadableContext("run end screen reached");

            _log?.LogInfo($"IKMA RUNEND: run end screen opened (victory={victory}).");
        }

        internal static void End(string reason)
        {
            if (_screen == null) return;
            _log?.LogInfo($"IKMA RUNEND: closed ({reason}).");
            _screen    = null;
            _announced = false;
            _settleFor = 0f;
        }

        public static void Reset() => End("reset");

        // ------------------------------------------------------------------
        // The button. Gated on activeInHierarchy for the same reason the Lucky
        // Clover is: an option that is not on the screen is not an option, and
        // offering a key that does nothing is the defect this project keeps
        // fixing.
        // ------------------------------------------------------------------
        private static AscensionMenuInteractable Retry()
        {
            if (_screen == null) return null;
            try
            {
                if (_retryField == null)
                    _retryField = typeof(AscensionRunEndScreen).GetField(
                        "retryButton",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                var button = _retryField?.GetValue(_screen) as AscensionMenuInteractable;
                if (button == null) return null;

                return button.gameObject.activeInHierarchy ? button : null;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RUNEND: retry button read failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// The outcome, in the game's own words. Live titleText first, because
        /// that is the string actually rendered; the compile-time constants are
        /// the fallback for the case where the title has not been filled yet.
        /// </summary>
        private static string OutcomeLine()
        {
            string live = null;

            try
            {
                if (_titleField == null)
                    _titleField = typeof(AscensionRunEndScreen).GetField(
                        "titleText",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                // Read as a plain object and hunt its "text" member rather than
                // naming PixelText. The field's declared type is confirmed but
                // its namespace is not, and naming a type this file does not
                // need is a compile error waiting for a game update.
                var titleObj = _titleField?.GetValue(_screen);
                live = TextOn(titleObj);
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RUNEND: title read failed: {e.Message}");
            }

            if (!string.IsNullOrEmpty(live))
            {
                _log?.LogInfo($"IKMA RUNEND: outcome from live titleText: '{live}'.");
                return Tidy(live);
            }

            string constant = ConstantTitle(_victory ? "VICTORY_TITLE" : "DEFEAT_TITLE");
            if (!string.IsNullOrEmpty(constant))
            {
                _log?.LogInfo($"IKMA RUNEND: outcome from constant: '{constant}'.");
                return Tidy(constant);
            }

            // Last resort. isVictory is the game's own answer and the patch
            // hands it over directly, so this is still not an invention — it is
            // the same fact with IKMA's own word on it.
            _log?.LogWarning("IKMA RUNEND: no title text found; falling back to the victory flag.");
            return Vocabulary.RunEnd.OutcomeLine(_victory);
        }

        private static string ConstantTitle(string fieldName)
        {
            try
            {
                var f = typeof(AscensionRunEndScreen).GetField(
                    fieldName,
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (f == null) return null;

                // Both are compile-time literals in the assembly, so the value
                // is on the field itself and no instance is involved.
                return f.IsLiteral ? f.GetRawConstantValue() as string
                                   : f.GetValue(null) as string;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        // Hunt a string "text" member on an object or on any component under
        // it. This is deliberately a local copy of the hunt MenuReader uses on
        // its interactables rather than a call into it: MenuReader's version is
        // wired into a per-screen cache and a label pipeline this screen has no
        // part in, and reaching into working narration code to save twenty
        // lines is how a regression gets introduced between playtests.
        // ------------------------------------------------------------------
        private static string TextOn(object target)
        {
            if (target == null) return null;

            string direct = TextMember(target);
            if (!string.IsNullOrEmpty(direct)) return direct;

            var component = target as UnityEngine.Component;
            if (component == null) return null;

            try
            {
                var children = component.gameObject.GetComponentsInChildren<UnityEngine.Component>(true);
                foreach (var c in children)
                {
                    if (c == null) continue;
                    string found = TextMember(c);
                    if (!string.IsNullOrEmpty(found)) return found;
                }
            }
            catch { }

            return null;
        }

        private static string TextMember(object o)
        {
            if (o == null) return null;
            try
            {
                var type = o.GetType();

                var prop = type.GetProperty("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.IgnoreCase);
                if (prop != null && prop.PropertyType == typeof(string))
                {
                    string v = prop.GetValue(o) as string;
                    if (!string.IsNullOrEmpty(v)) return v;
                }

                var field = type.GetField("text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.IgnoreCase);
                if (field != null && field.FieldType == typeof(string))
                {
                    string v = field.GetValue(o) as string;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Menu text arrives shouted and decorated, and DEFEAT_TITLE literally
        /// ends in an ellipsis. Strip the decoration, keep the word, and let the
        /// caller supply the full stop — a screen reader reading "DEFEAT dot dot
        /// dot" is the kind of detail that only shows up in a listen.
        /// </summary>
        private static string Tidy(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string s = raw.Replace("\n", " ").Trim();
            s = s.Trim('-', '=', '<', '>', '.', ' ');
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s;
        }

        // ------------------------------------------------------------------
        public static bool NeedsAnnouncement(float deltaTime)
        {
            if (_screen == null || _announced) return false;
            _settleFor += deltaTime;
            return _settleFor >= SETTLE_SECONDS;
        }

        public static void Announce()
        {
            _announced = true;
            ResetIdle();
            // 0.7.360 — after a death the screen line follows Leshy's last
            // lines instead of cutting them. See RunEndProbe.KilledAt.
            if (RunEndProbe.DeathJustHappened)
            {
                RunEndProbe.KilledAt = -999f;
                _log?.LogInfo("IKMA RUNEND: screen line follows the last lines of the run.");
                Speech.Quiet(ScreenLine());
                return;
            }

            // 0.7.449 - Zamar, Session 45: "Before Victory, it should read
            // 'Leshy blows out his final candle, the cabin goes dark.'" Said
            // once, on arrival; Space re-reads the page without it. A Kaycee's
            // Mod victory is only ever reached through Part1BossOpponent.
            // TransitionFromFinalBoss (called by LeshyBossOpponent alone),
            // which blows out the last candle and blacks the screen three
            // seconds before it loads this scene.
            string line = ScreenLine();
            if (_victory) line = Vocabulary.RunEnd.LeshyFinalCandle + " " + line;
            Speech.Browse(line);
        }

        /// <summary>
        /// Everything spoken here is something the screen shows: the outcome
        /// title, and the one button on it read from its own label.
        ///
        /// When the button cannot be found, the line says so rather than
        /// offering a key that does nothing — the same honesty the
        /// unreadable-screen prompt uses, and for the same reason. A blind
        /// player cannot tell a missing button from a key that did not register.
        /// </summary>
        // ----------------------------------------------------------------------
        // THE RUN'S NUMBERS, READ OFF THE SCREEN ITSELF. (0.7.229.)
        //
        // Zamar: "remove Your Run is over, and replace it with all of the
        // statistics on the defeat screen."
        //
        // "Your run is over" said nothing the word DEFEAT had not already said.
        // The six numbers beside it are the only thing on this screen a sighted
        // player actually reads, and they were silent.
        //
        // READ FROM THE PixelText OBJECTS, NOT REBUILT FROM THE SAVE DATA.
        // AscensionStatsScreen.FillStatsText composes each row as
        //
        //     Localization.ToUpper(Translate(stat.GetStringForType())) + ":  " + value
        //
        // and writes it into a PixelText, whose Text property is PUBLIC
        // (PixelText.cs:23). Reading those strings gets the game's own names,
        // the game's own order, the game's own values, already localised — and,
        // critically, only the rows the screen is actually SHOWING. Which stats
        // appear is a [SerializeField] list set in the prefab; rebuilding the
        // line from AscensionSaveData would have meant guessing that list and
        // being wrong the moment it differs.
        //
        // The instance is captured when the game fills it (see
        // NoteStatsScreen), so nothing here searches the scene for one.
        // ----------------------------------------------------------------------
        private static AscensionStatsScreen _statsScreen;

        /// <summary>
        /// Captured from AscensionStatsScreen.FillStatsText's postfix — the
        /// moment the rows hold their final text. Ordering between that and
        /// this screen's Initialize is not guaranteed either way, which is why
        /// the line composes at speak time rather than at enqueue.
        /// </summary>
        internal static void NoteStatsScreen(AscensionStatsScreen screen)
        {
            _statsScreen = screen;
            _log?.LogInfo("IKMA RUNEND: stats screen captured.");
        }

        internal static void ForgetStatsScreen() => _statsScreen = null;

        // ----------------------------------------------------------------------
        // BACKSPACE — OUT OF KAYCEE'S MOD AND BACK TO THE MENU. (0.7.229.)
        //
        // Zamar: "Backspace should take you back to the main menu, enter should
        // begin a new run with the same starter deck."
        //
        // ENTER ALREADY DOES HIS HALF, and it is worth recording why rather
        // than changing anything: the retry button's own handler is
        // AscensionMenuScreens.TransitionToGame(), whose newRun branch reads
        // StarterDecksUtil.GetInfo(AscensionSaveData.Data.currentStarterDeck)
        // and calls NewRun with those cards. "A new run with the same starter
        // deck" is the button's literal behaviour, so Select() is already
        // right and is left alone.
        //
        // BACKSPACE PRESSES THE SCREEN'S OWN BACK BUTTON. It does NOT call
        // AscensionMenuScreens.ExitAscension() or SwitchToScreen() directly —
        // the first rule of this project is that the game leads, and a menu
        // transition driven by IKMA rather than by the button would skip
        // whatever the button's own handler does now or later. The button is
        // AscensionMenuBackButton, whose OnCursorSelectStart is exactly
        // SwitchToScreen(screenToReturnTo), so pressing it goes wherever the
        // game says this screen goes back to.
        //
        // The reference lives on AscensionMenuScreens, not on the run end
        // screen, so it is read off the SINGLETON — which exists here, in the
        // menu scene, and is only asked on a keypress, never per frame.
        // ----------------------------------------------------------------------
        private static AscensionMenuBackButton BackButton()
        {
            try
            {
                var screens = Singleton<AscensionMenuScreens>.Instance;
                if (screens == null) return null;

                var field = typeof(AscensionMenuScreens).GetField(
                    "runEndBackButton",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                var button = field?.GetValue(screens) as AscensionMenuBackButton;
                if (button == null)
                {
                    _log?.LogWarning("IKMA RUNEND: runEndBackButton did not resolve.");
                    return null;
                }

                if (!button.gameObject.activeInHierarchy)
                {
                    _log?.LogInfo("IKMA RUNEND: back button exists but is not active on this screen.");
                    return null;
                }

                return button;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RUNEND: back button read failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Backspace. Presses the screen's own back button; says so plainly if
        /// there is none rather than leaving a pressed key unanswered.
        /// </summary>
        internal static void Back()
        {
            ResetIdle();

            var button = BackButton();
            if (button == null)
            {
                // Session 32, Zamar: "Cut". The old line claimed there was no
                // way back, which IKMA does not know; now it only logs.
                _log?.LogInfo("IKMA RUNEND: Backspace, but no back button was found. Nothing pressed.");
                return;
            }

            try
            {
                _log?.LogInfo("IKMA RUNEND: back button pressed via CursorSelectStart/End.");
                button.CursorSelectStart();
                button.CursorSelectEnd();
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RUNEND: back press threw {e.GetType().Name}: {e.Message}");
            }

            // AND LET GO OF THE SCREEN. (0.7.241 — this was a softlock.)
            //
            // Zamar: "the defeat screen is broken. I hit backspace off it, went
            // to main menu, and was softlocked."
            //
            // Back leaves WITHOUT a scene load — AscensionMenuScreens switches
            // from RunEnd to Start inside Ascension_Configure — and Reset() is
            // wired to the scene-load path. So _screen stayed set, Active stayed
            // true, and RunEndReader sits ABOVE MenuReader in HotkeyManager's
            // layers: every key on the Kaycee's Mod main menu went to
            // HandleRunEndKeys, and MenuReader.NeedsScreenAnnouncement was never
            // reached, so the menu never announced itself and never took an
            // arrow. His log ends on exactly that — "screen active — StartScreen"
            // and then silence.
            //
            // Enter never showed it because the retry button loads a scene,
            // which fires Reset. One exit path was cleaned up by accident and
            // the other was not cleaned up at all.
            End("back button pressed");
        }

        /// <summary>
        /// The stat rows as the screen renders them, or "" if there are none to
        /// read. Never invents a row and never reorders them.
        /// </summary>
        private static string StatsLine()
        {
            if (_statsScreen == null) return "";

            try
            {
                var field = typeof(AscensionStatsScreen).GetField(
                    "statsText",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);

                var rows = field?.GetValue(_statsScreen)
                           as System.Collections.Generic.List<GBC.PixelText>;
                if (rows == null)
                {
                    _log?.LogWarning("IKMA RUNEND: AscensionStatsScreen.statsText did not resolve.");
                    return "";
                }

                var parts = new System.Collections.Generic.List<string>();
                foreach (var row in rows)
                {
                    if (row == null) continue;

                    string text = null;
                    try { text = row.Text; } catch { }
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    // "BOSSES DEFEATED:  3" -> "BOSSES DEFEATED 3". Tidy does
                    // the shout and the spacing the same way every other menu
                    // string on this screen is handled.
                    //
                    // AND THE COLON GOES. (0.7.242, his call: "remove all :".)
                    // A colon between a label and its number is punctuation for
                    // the eye — it lines a column up. Read aloud it is either a
                    // spoken "colon" or a pause in the middle of a phrase that
                    // is one thought, and six of them in a row is the screen
                    // being punctuated at the player rather than read to them.
                    string tidy = Tidy(text);
                    if (!string.IsNullOrWhiteSpace(tidy))
                        parts.Add(tidy.Replace(":", ""));
                }

                if (parts.Count == 0)
                {
                    _log?.LogInfo("IKMA RUNEND: stats screen present but every row was empty.");
                    return "";
                }

                _log?.LogInfo($"IKMA RUNEND: {parts.Count} stat row(s) read from the screen.");
                return string.Join(". ", parts.ToArray()) + ". ";
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RUNEND: stats read threw {e.GetType().Name}: {e.Message}");
                return "";
            }
        }

        private static string ScreenLine()
        {
            string outcome = OutcomeLine();

            // The numbers stand where "Your run is over" used to. If they
            // cannot be read the sentence simply goes without them rather than
            // falling back to a phrase he has removed.
            string stats = StatsLine();

            var button = Retry();
            if (button == null)
            {
                // 0.7.449 - NOT A FAILURE. AscensionRunEndScreen.Start switches
                // retryButton off when isVictory is true, so on a victory there
                // is nothing to find and nothing that needs the mouse. Zamar,
                // Session 45: "This victory screen is a mess. Needs the Enter
                // option removed, needs the no options callouts removed, needs
                // the IKMA Cannot Find An Option callout removed."
                _log?.LogInfo("IKMA RUNEND: no retry button on this screen (the game hides it on a victory).");
                // Session 47 (0.7.455): after a win that clears the level
                // the back button leads on to the unlock screens.
                if (LeadsOnward()) return Vocabulary.RunEnd.OutcomeStatsContinue(outcome, stats);
                return Vocabulary.RunEnd.OutcomeStatsNoRetry(outcome, stats);
            }

            string label = Tidy(TextOn(button));
            if (string.IsNullOrEmpty(label))
            {
                // The button is there and clickable; only its wording is
                // missing. Say what is true of it without naming words the
                // screen may not be showing.
                _log?.LogWarning("IKMA RUNEND: button found but no label text on it.");
                return Vocabulary.RunEnd.OutcomeStatsControls(outcome, stats);
            }

            _log?.LogInfo($"IKMA RUNEND: button label='{label}'.");
            return Vocabulary.RunEnd.OutcomeStatsLabelControls(outcome, stats, label);
        }

        public static void AnnounceCurrent()
        {
            if (_screen == null) return;
            ResetIdle();
            Speech.Browse(ScreenLine());
        }

        // ------------------------------------------------------------------
        // Enter. The universal primitive again — click the button the way a
        // mouse would and let the game decide what that means. IKMA announces
        // nothing on success: the screen it takes you to announces itself, and
        // a line here would be describing an intention rather than a result.
        // ------------------------------------------------------------------
        public static void Select()
        {
            var button = Retry();
            if (button == null)
            {
                // 0.7.449 - Enter has nothing to press here and says nothing:
                // his call, "needs the no options callouts removed".
                // Session 47 (0.7.455), Zamar: "Can we just change it to
                // Enter to continue?" Enter presses the screen's own back
                // button when that button leads on to the unlock screens.
                if (LeadsOnward())
                {
                    _log?.LogInfo("IKMA RUNEND: Enter continues - the back button leads on to the unlock screens.");
                    Back();
                    return;
                }
                _log?.LogInfo("IKMA RUNEND: Enter, but this screen has no retry button. Nothing pressed.");
                return;
            }

            ResetIdle();
            try
            {
                button.CursorEnter();
                button.CursorSelectStart();
                button.CursorSelectEnd();
                _log?.LogInfo("IKMA RUNEND: button clicked via CursorSelectStart/End.");
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA RUNEND: click failed: {e.Message}");
                Speech.Browse(Vocabulary.OptionCouldNotBeSelected);
            }
        }

        // ------------------------------------------------------------------
        // SESSION 47 - WHERE THE BACK BUTTON GOES. (0.7.455.)
        //
        // AscensionMenuScreens.ConfigurePostGameScreens points the run end
        // screen's back button at the devlog entry, the new cards, the new
        // starter deck or the new challenge when the win cleared the
        // challenge level, and leaves it on Start otherwise.
        // AscensionMenuBackButton.screenToReturnTo is PUBLIC. "Backspace to
        // return to the main menu." was false in the first case.
        // ------------------------------------------------------------------
        internal static bool LeadsOnward()
        {
            try
            {
                // Session 48 (0.7.457): asked of the button whether or not it
                // is switched on yet. The game points it in
                // AscensionMenuScreens.Start and only shows it once the
                // stats have counted up, so the screen's FIRST read found no
                // active button and said "Backspace to return to the main
                // menu." while Space, a moment later, said "Enter to
                // continue." (driver log, Session 48).
                var button = BackButtonAnyState();
                return button != null && button.screenToReturnTo != AscensionMenuScreens.Screen.Start;
            }
            catch { return false; }
        }

        // The run end screen's back button, shown or not. Only for reading
        // where it leads; pressing it still goes through BackButton().
        private static AscensionMenuBackButton BackButtonAnyState()
        {
            try
            {
                var screens = Singleton<AscensionMenuScreens>.Instance;
                if (screens == null) return null;

                var field = typeof(AscensionMenuScreens).GetField(
                    "runEndBackButton",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                return field?.GetValue(screens) as AscensionMenuBackButton;
            }
            catch { return null; }
        }

        public static void SpeakHelp()
        {
            ResetIdle();
            // 0.7.449 - the victory screen has its own name and no Enter.
            Speech.Browse(_victory
                ? (LeadsOnward() ? Vocabulary.RunEnd.VictoryScreenControlsEnter
                                 : Vocabulary.RunEnd.VictoryScreenControlsSpace)
                : Vocabulary.RunEnd.RunEndControlsSpace);
        }

        // ------------------------------------------------------------------
        // The idle prompt. Info tier and deferred, never an interrupt: Zamar's
        // rule from the card choice screen is that an idle loop is the lowest
        // priority in the mod and must never stomp anything.
        // ------------------------------------------------------------------
        public static void Tick(float deltaTime)
        {
            if (_screen == null) return;

            // THE SCREEN ITSELF IS THE AUTHORITY ON WHETHER IT IS STILL UP.
            // (0.7.241.) Back releases the screen explicitly above, and this is
            // the backstop for every other way off it — the mouse, a transition
            // IKMA did not start, a path not yet written. A reader that can only
            // be released by one specific keypress is one missed path away from
            // owning the keyboard forever, which is what it just did.
            bool showing = false;
            try { showing = _screen.gameObject.activeInHierarchy; } catch { }
            if (!showing) { End("the screen is no longer displayed"); return; }

            if (!_announced) return;

            if (_idlePending) return;

            _idleTimer += deltaTime;
            if (_idleTimer < _idleInterval) return;

            _idleTimer    = 0f;
            _idleInterval = IDLE_REPEAT;
            _idlePending  = true;

            Speech.Commentary(() =>
            {
                _idlePending = false;
                // Composed at speak time and withdrawn if the screen has gone,
                // the same shape as every other instructing line in the mod.
                // Session 37, note D9 - Zamar: the repeat is the controls only;
                // Space still reads the whole screen (AnnounceCurrent).
                if (_screen == null) return null;
                // 0.7.449 - no Enter in the idle prompt when there is no retry button.
                if (Retry() != null) return Vocabulary.RunEndControls();
                return LeadsOnward() ? Vocabulary.RunEnd.ContinueControls : Vocabulary.RunEnd.NoRetryControls;
            });
        }

        public static void ResetIdle()
        {
            _idleTimer    = 0f;
            _idleInterval = IDLE_FIRST;
            _idlePending  = false;
        }
    }

    // Captures the stats screen the moment the game has filled its rows, so
    // RunEndReader never has to search the scene for one. (0.7.229.)
    public static class AscensionStatsScreen_FillStatsText_Patch
    {
        public static void Postfix(AscensionStatsScreen __instance)
            => RunEndReader.NoteStatsScreen(__instance);
    }

}

// RunEndReader.cs
