// DialogueAdvancer.cs
using System.Reflection;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// Space advances a line of dialogue, and the game says so while it waits.
    /// (Session 15, from Zamar's 0.7.42 playtest: Leshy said "YOU HAVE ME HERE.
    /// I SURRENDER." and the only way past it was a LEFT CLICK.)
    ///
    /// WHAT THE DUMP ESTABLISHED, and it is the whole basis for this file:
    ///   TextDisplayer.CurrentAdvanceMode  PUBLIC get/set, MessageAdvanceMode
    ///   MessageAdvanceMode.Auto = 0, Input = 1
    ///   TextDisplayer.Displaying          PUBLIC get
    ///   TextDisplayer.continuePressed     NONPUBLIC instance bool
    ///
    /// So "the game is waiting for the player to advance a line" is a question
    /// the game can be ASKED — Displaying is true and CurrentAdvanceMode is
    /// Input — rather than inferred from a keypress or a timer. Everything the
    /// prompt does hangs off that, and it is honest whatever the advance itself
    /// turns out to do.
    ///
    /// ADVANCING IS THE EXPERIMENTAL HALF AND IT IS INSTRUMENTED, NOT ASSUMED.
    /// Reflection cannot read method bodies, so what ManagedUpdate polls to set
    /// continuePressed is not knowable from a dump. Two candidates: an
    /// InputButtons poll, which the sacrifice wait and the placement cancel have
    /// both already proved can be silently ignored, or a raw mouse read, which
    /// nothing can inject into.
    ///
    /// continuePressed is set directly instead, and that needs justifying
    /// against the project's own rule. The banned writes — CancelledSacrifice,
    /// cancelledPlacementWithInput — are flags a coroutine checks AFTER a
    /// selection completes, so setting them early leaves the coroutine still
    /// waiting and the player stuck. This one is the opposite shape: it is the
    /// latch the wait itself spins on, cleared by the same code that reads it,
    /// so writing it is equivalent to the input having been seen. If that is
    /// wrong, the failure is a line that does not advance — not a softlock.
    ///
    /// Every press logs, and a watcher reports whether the line actually went
    /// away. If the log says the press was accepted and nothing advanced, the
    /// answer is that this route does not work and InputButtons injection is
    /// next. That is a real outcome and the log is how it gets settled.
    /// </summary>
    internal static class DialogueAdvancer
    {
        private static FieldInfo _continuePressed;
        private static bool _resolved;
        private static bool _loggedMissing;

        // A prompt is only useful once the player has had a moment to hear the
        // line itself. 4s then every 4s is Zamar's number for this one; the map
        // idle prompt's 5.5s/15s is a different situation — there the player is
        // being asked to choose, here they are being asked to continue.
        private const float FIRST_PROMPT_SECONDS = 4f;
        private const float REPEAT_PROMPT_SECONDS = 4f;

        private static float _waitingSince = -1f;
        private static float _nextPrompt = -1f;

        /// <summary>
        /// Is the game holding on a line of dialogue until the player says go?
        /// Asked of the game, never inferred.
        /// </summary>
        // THE LOOKUP IS CACHED AND BACKED OFF. (Fixed 0.7.48.)
        //
        // Singleton<T>.Instance runs FindInstance when it holds nothing, and
        // FindInstance logs a warning every time it comes up empty. Asking every
        // frame in scenes with no TextDisplayer put 2,909 lines of
        // "Got null in Singleton<DiskCardGame.TextDisplayer>.FindInstance" into
        // Zamar's 0.7.47 log — in a file he reads back with a screen reader.
        //
        // The log is a player-facing artifact and that was IKMA's own doing, not
        // the engine's. A found instance is held; a miss is not retried for two
        // seconds, which takes the worst case from sixty warnings a second to
        // one every two.
        private static TextDisplayer _textDisplayer;
        private static float _nextLookup;
        private const float LOOKUP_BACKOFF_SECONDS = 2f;

        private static TextDisplayer Displayer()
        {
            if (_textDisplayer != null) return _textDisplayer;

            // A Kaycee's Mod menu screen has no TextDisplayer and no dialogue,
            // so the search is guaranteed to miss and guaranteed to log. Skip it
            // rather than back off from it.
            if (MenuReader.MenuActive()) return null;

            if (Time.unscaledTime < _nextLookup) return null;
            _nextLookup = Time.unscaledTime + LOOKUP_BACKOFF_SECONDS;

            _textDisplayer = Singleton<TextDisplayer>.Instance;
            return _textDisplayer;
        }

        internal static bool AwaitingInput()
        {
            try
            {
                var td = Displayer();
                if (td == null) return false;
                if (!td.Displaying) return false;
                return td.CurrentAdvanceMode == TextDisplayer.MessageAdvanceMode.Input;
            }
            catch { return false; }
        }

        /// <summary>
        /// Space. Returns true if the key was consumed, so the caller does not
        /// also try to play a card with it.
        /// </summary>
        internal static bool TryAdvance()
        {
            if (!AwaitingInput()) return false;

            if (!_resolved)
            {
                _resolved = true;
                _continuePressed = typeof(TextDisplayer).GetField(
                    "continuePressed", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            if (_continuePressed == null)
            {
                if (!_loggedMissing)
                {
                    _loggedMissing = true;
                    Plugin.Log?.LogWarning(
                        "IKMA DIALOGUE: TextDisplayer.continuePressed did not resolve — " +
                        "Space cannot advance dialogue and the prompt is withdrawn.");
                }
                return false;
            }

            try
            {
                var td = Displayer();
                if (td == null) return false;

                _continuePressed.SetValue(td, true);

                // SPACE CUTS WHATEVER IKMA IS SAYING. (0.7.169.) Zamar, on
                // hearing "Conversation in progress, press Space to proceed."
                // still playing after he had pressed Space: "Pressing space
                // should stomp these lines."
                //
                // He has answered the prompt, so the prompt is stale — and the
                // game's next line is arriving right now. Letting the nag play
                // out means it lands on top of the line it was telling him to
                // go and get.
                //
                // GENERAL RULE, worth applying anywhere else it fits: a line
                // that TELLS THE PLAYER TO PRESS SOMETHING is dead the instant
                // they press it. This project already refuses to compose such
                // prompts early (see the deferred draw prompt); cutting them on
                // the press is the same principle at the other end.
                Speech.Silence();

                // The next line gets its own prompt; this one has been answered.
                _promptedForThisLine = false;
                // 0.7.315 — the dialogue layer's own state, not a line anyone heard. Behind Plugin.VerboseDiagnostics.
                if (Plugin.VerboseDiagnostics)
                    Plugin.Log?.LogInfo("IKMA DIALOGUE: Space -> continuePressed set, speech cut.");

                // Say nothing. The game has a voice for this and the next line
                // is about to arrive; a confirmation from IKMA would land on top
                // of it. Silence here is the correct answer, and the log is
                // where the verification lives.
                ClearUntilInputHold("the player advanced");

                CombatAnnouncer.Instance?.StartCoroutine(WatchAdvance());
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA DIALOGUE: advance failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Did it work? A press that is accepted and changes nothing is exactly
        /// the failure this needs to be able to report, because from the
        /// player's side it is indistinguishable from a key that never
        /// registered.
        /// </summary>
        // ======================================================================
        // IS THE GAME REALLY HOLDING, OR IS THE TEXT JUST SITTING THERE?
        // (0.7.134.)
        //
        // TWO STATES LOOK IDENTICAL FROM OUTSIDE, and every attempt to tell them
        // apart from the TextDisplayer alone has now failed twice in opposite
        // directions:
        //
        //   Leshy mid-speech      Displaying=true, mode=Input, waiting on Space
        //   "PUSH YOUR LUCK?"     Displaying=true, mode=Input, waiting on a CHOICE
        //
        // 0.7.130 locked the keys on that signal and stranded him at the second.
        // 0.7.132 unlocked them whenever the screen had something to browse, and
        // that let him move a selection while Leshy was still talking — "mod
        // breaking the game", and he is right.
        //
        // THE ANSWER WAS ALREADY IN THE LOG, AS A DIAGNOSTIC. This watcher has
        // been printing "continuePressed did NOT advance the line" for builds.
        // That IS the distinction: if setting continuePressed does not move
        // anything, the game is not waiting on the line — whatever the text on
        // screen says. So the diagnostic is promoted to a signal.
        //
        // A LATCH THAT CANNOT STRAND, because the game clears it: every new line
        // arriving through ShowMessage resets it, so the next real hold locks the
        // keys again. IKMA never decides the conversation is over; it only
        // notices that the last press did nothing.
        //
        // The cost of being wrong is asymmetric and this errs the safe way. A
        // false "not holding" gives back keys the player pressed on purpose; a
        // false "holding" is the softlock he just hit.
        // ======================================================================
        private static volatile bool _advanceDidNothing;

        // Counts lines the game has shown. The watcher below compares it across
        // its wait, which is what makes "did that press do anything" a question
        // about ONE line rather than about the passage of time.
        private static int _lineSeq;

        /// <summary>Cleared by Plugin's ShowMessage patch when a new line arrives.</summary>
        internal static void NoteNewLine()
        {
            _advanceDidNothing = false;
            _lineSeq++;
        }

        /// <summary>
        /// True only while the game is genuinely holding on a line — which is
        /// the question the key lockout has to ask, and is NOT the same as
        /// AwaitingInput.
        /// </summary>
        /// <summary>
        /// IS A CONVERSATION RUNNING AT ALL — not "is it holding on a line".
        /// (0.7.240.)
        ///
        /// The difference is the GAP BETWEEN LINES, and it cost a build. The
        /// deck view was told to wait for Leshy at 0.7.239 and asked
        /// AwaitingInput, which is false for the moment between one line being
        /// dismissed and the next being displayed — so the deck line slipped
        /// into the gap and Zamar heard it mid-conversation again. His log has
        /// the order exactly: "Your deck. 5 cards..." and then, immediately
        /// after it, "ShowUntilInput hold opened" and the next Leshy line.
        ///
        /// TextDisplayer.PlayingEvent is PUBLIC and answers the whole-sequence
        /// question — it is true from the first line of a scripted event to the
        /// last, gaps included. The hold flag covers a lone ShowUntilInput
        /// outside an event, which PlayingEvent does not see.
        ///
        /// Displayer() is the same backed-off lookup the rest of this class
        /// uses, so asking costs nothing in a scene with no dialogue.
        /// </summary>
        internal static bool ConversationRunning()
        {
            if (_holdingUntilInput) return true;
            if (AwaitingInput()) return true;

            try
            {
                var td = Displayer();
                return td != null && td.PlayingEvent;
            }
            catch { return false; }
        }

        internal static bool ConversationHolding()
        {
            // PlayingEvent IS THE GAME'S OWN ANSWER, AND IT WAS THERE ALL ALONG.
            // (0.7.136.)
            //
            // TextDisplayer.PlayingEvent is PUBLIC and means "a scripted
            // conversation is running". That is precisely the question the
            // lockout has been failing to ask for four builds — Displaying says
            // text is on screen, CurrentAdvanceMode says how lines advance, and
            // neither says whether a CONVERSATION is in progress.
            //
            // WHY THE PREVIOUS ANSWER STRANDED HIM. The failed-advance latch only
            // arms when a press does nothing — so a player who reaches the last
            // line, sees the screen go interactive and never presses Space again
            // is locked out forever. His 0.7.135 log is seven arrow presses
            // swallowed in a row with no Space between them, which is exactly
            // that: he had stopped pressing the key that would have freed him,
            // because as far as he could tell the conversation was over. It was.
            //
            // A latch that can only be cleared by an action the player has no
            // reason to take is not a latch, it is a trap. This asks the game
            // instead, every frame, and needs no press at all.
            //
            // The failed-advance latch stays as a second gate for the case
            // PlayingEvent misses — a single ShowUntilInput line outside an
            // event — but it is no longer load bearing.
            // A BARE ShowUntilInput COUNTS AS A CONVERSATION. (0.7.172.)
            //
            // 0.7.169 taught CardReader.GameIsTalking() about the hold and
            // stopped there. THAT WAS THE WRONG FUNCTION. The key-swallow gate
            // in HotkeyManager asks ConversationHolding() — this method — so
            // the fix went into a sibling that the Prospector path never calls,
            // and Zamar reported the identical bug again one build later.
            //
            // Two methods answer "is the game talking" and they are consulted
            // by different callers. When teaching the mod a new way the game
            // can be talking, grep for every one of them.
            if (_holdingUntilInput) return AwaitingInput() && !_advanceDidNothing;

            try
            {
                var td = Displayer();
                if (td == null) return false;
                if (!td.PlayingEvent) return false;
            }
            catch { return false; }

            return AwaitingInput() && !_advanceDidNothing;
        }

        private static System.Collections.IEnumerator WatchAdvance()
        {
            // WHICH LINE THIS PRESS WAS FOR. (0.7.135.)
            //
            // THE RACE THIS FIXES, straight out of his 0.7.134 log:
            //
            //   speaker=Single                     <- a line arrives
            //   Space -> continuePressed set       <- watcher starts its 1s wait
            //   speaker=Single                     <- the NEXT line arrives
            //   still waiting one second later ... <- watcher fires and latches
            //
            // The press worked. A new line was already up. But the watcher only
            // asked "is something waiting now", and something always is during a
            // conversation — so it declared the advance dead and unlocked the
            // keys mid-scene. Three "activating ... on Campfire" lines follow in
            // that same log: Enter reaching the confirm stone while Leshy talked.
            //
            // A COUNTER, NOT A TIMER. If the line count moved, the press
            // advanced, whatever the display says. Only a wait that ends on the
            // SAME line it started on is evidence of anything.
            //
            // The general shape: "is X true after a delay" is almost never the
            // question. "Is this the same X I was asking about" usually is.
            int seqAtPress = _lineSeq;

            yield return new WaitForSeconds(1f);

            if (_lineSeq != seqAtPress)
            {
                // 0.7.315 — the dialogue layer's own state, not a line anyone heard. Behind Plugin.VerboseDiagnostics.
                if (Plugin.VerboseDiagnostics)
                    Plugin.Log?.LogInfo("IKMA DIALOGUE: line advanced.");
                yield break;
            }

            if (AwaitingInput())
            {
                _advanceDidNothing = true;
                Plugin.Log?.LogInfo(
                    "IKMA DIALOGUE: same line one second later — continuePressed did NOT " +
                    "advance it. Treating the text as a standing prompt, not a hold.");
            }
            else
            {
                // 0.7.315 — the dialogue layer's own state, not a line anyone heard. Behind Plugin.VerboseDiagnostics.
                if (Plugin.VerboseDiagnostics)
                    Plugin.Log?.LogInfo("IKMA DIALOGUE: line advanced.");
            }
        }

        /// <summary>
        /// The looping prompt. One queued at a time, by Zamar's instruction:
        /// "only one of these lines should queue."
        ///
        /// Deferred and self-withdrawing like every other instructing line in
        /// the mod — by the time it reaches the front the player may already
        /// have pressed Space, and a prompt that is false when spoken is the
        /// bug this project has fixed four times.
        /// </summary>
        // ----------------------------------------------------------------------
        // A BARE ShowUntilInput HOLD. (0.7.169.)
        //
        // THE GAP THIS CLOSES, and a comment four lines from here predicted it
        // months ago: "the case PlayingEvent misses — a single ShowUntilInput
        // line outside an event".
        //
        // The conversation lock asks TextDisplayer.PlayingEvent, which means "a
        // scripted dialogue EVENT is running". The Prospector's three barks —
        // "THAR'S GOLD IN THEM CARDS!", "G-G-GOLD! I'VE STRUCK GOLD!",
        // "N-... NO GOLD?" — are direct ShowUntilInput calls, not events. So
        // PlayingEvent stayed false, the lock never engaged, and Zamar's arrows
        // and Enter did nothing and said nothing while the game sat waiting for
        // a Space he had no way of knowing it wanted.
        //
        // His 0.7.167 log ends mid-board-wipe for exactly this reason. That is
        // the real cost: not a missing convenience, a STRANDED PLAYER.
        //
        // HOW IT CLEARS, which is the part that has to be right. An
        // over-held latch is worse than none — it locks the keyboard against a
        // line the game has finished with, which is the bug four builds went
        // into fixing in Session 18. Two independent releases, either of which
        // is sufficient:
        //
        //   1. The player advances (TryAdvance succeeds). The next bark calls
        //      ShowUntilInput again and re-latches on its own.
        //   2. TextDisplayer stops Displaying anything at all. Covers the game
        //      dismissing the text by its own means — a sequence moving on, a
        //      timed clear — with no press from the player.
        //
        // Deliberately NOT cleared on "text still on screen but no longer
        // waiting". That distinction is what PlayingEvent already handles well
        // and what this latch is bad at; two mechanisms guessing at the same
        // ambiguous state is how the last version of this got stuck.
        // ----------------------------------------------------------------------
        private static bool  _holdingUntilInput;

        // THE HOLD USED TO CANCEL ITSELF ON THE FRAME IT OPENED. (0.7.172.)
        //
        // From Zamar's 0.7.171 log, two lines apart:
        //
        //   IKMA DIALOGUE: ShowUntilInput hold opened.
        //   IKMA DIALOGUE: ShowUntilInput hold cleared — nothing is displaying.
        //
        // The prefix fires at ENUMERATOR CREATION. The text is not on screen
        // yet, so Displaying is still false, so the "nothing is displaying"
        // release fired immediately and the lock was never live for a single
        // frame that mattered.
        //
        // THIS PROJECT'S OWN HARD RULE — "coroutine prefixes fire at enumerator
        // creation, not completion" — and it was broken while writing a fix for
        // a timing problem. The rule is easy to apply to a value being READ and
        // easy to forget for a condition being WATCHED.
        //
        // The release now has to see the text arrive before it can act on the
        // text leaving. Plus a hard timeout, because a latch whose release
        // depends on something that might never happen is the trap this whole
        // subsystem already paid for once.
        private static bool  _holdSawDisplaying;
        private static float _holdOpenedAt = -1f;

        /// <summary>
        /// Longest a ShowUntilInput hold may live without the text ever
        /// appearing. Generous — a real hold is cleared by the player advancing
        /// or by the text going away, and this only catches the case where
        /// neither ever happens.
        /// </summary>
        private const float HOLD_TIMEOUT_SECONDS = 30f;

        /// <summary>
        /// True while a bare ShowUntilInput is waiting for a press. Ored with
        /// PlayingEvent by everything that asks "is the game talking".
        /// </summary>
        internal static bool HoldingUntilInput => _holdingUntilInput;

        /// <summary>Called from the TextDisplayer.ShowUntilInput patch.</summary>
        internal static void NoteShowUntilInput()
        {
            if (_holdingUntilInput) return;
            _holdingUntilInput = true;
            _holdSawDisplaying  = false;
            _holdOpenedAt       = Time.unscaledTime;
            // 0.7.315 — the dialogue layer's own state, not a line anyone heard. Behind Plugin.VerboseDiagnostics.
            if (Plugin.VerboseDiagnostics)
                Plugin.Log?.LogInfo("IKMA DIALOGUE: ShowUntilInput hold opened.");
        }

        internal static void ClearUntilInputHold(string why)
        {
            if (!_holdingUntilInput) return;
            _holdingUntilInput = false;
            // 0.7.315 — the dialogue layer's own state, not a line anyone heard. Behind Plugin.VerboseDiagnostics.
            if (Plugin.VerboseDiagnostics)
                Plugin.Log?.LogInfo($"IKMA DIALOGUE: ShowUntilInput hold cleared — {why}.");
        }

        internal static void Tick()
        {
            // Release 2: nothing on screen at all means nothing is being waited
            // on. Cheap, and it cannot strand the keyboard.
            if (_holdingUntilInput)
            {
                try
                {
                    var td0 = Displayer();

                    if (td0 != null && td0.Displaying)
                    {
                        // The text has arrived. Only now is its absence
                        // meaningful.
                        _holdSawDisplaying = true;
                    }
                    else if (_holdSawDisplaying)
                    {
                        ClearUntilInputHold("the text is gone");
                    }
                    else if (_holdOpenedAt > 0f &&
                             Time.unscaledTime - _holdOpenedAt > HOLD_TIMEOUT_SECONDS)
                    {
                        // The text never appeared. Something else consumed the
                        // sequence; do not hold the keyboard on a promise.
                        ClearUntilInputHold("timed out waiting for the text to appear");
                    }
                }
                catch { ClearUntilInputHold("the displayer could not be asked"); }
            }

            if (!AwaitingInput())
            {
                _waitingSince = -1f;
                _nextPrompt = -1f;

                // THE LINE IS NO LONGER WAITING, SO THE NEXT ONE GETS A PROMPT.
                // Cleared here as well as on a successful advance, because the
                // game advances lines by its own means too — a timed line, a
                // mouse click, a sequence moving on. Clearing only in TryAdvance
                // would leave this latched true and silence every prompt after
                // the first, which is the stranded-flag bug this project has
                // paid for twice.
                _promptedForThisLine = false;
                return;
            }

            float now = Time.unscaledTime;

            if (_waitingSince < 0f)
            {
                _waitingSince = now;
                _nextPrompt = now + FIRST_PROMPT_SECONDS;
                return;
            }

            // ONCE PER LINE. NEVER ON A REPEAT. (0.7.125.)
            //
            // Zamar, twice now: "It kept spamming Press enter to Proceed. It
            // should stop doing that." His 0.7.124 log has it seven times in a
            // row, after the campfire reader had already released — which is why
            // the node-screen gate added at 0.7.124 did not catch it.
            //
            // THE REPEAT WAS THE WRONG TOOL HERE. An idle prompt repeats because
            // the player may have walked away and come back to a screen that is
            // waiting on them. Dialogue is not that screen: the game is actively
            // speaking, so a player who is present is hearing something every few
            // seconds, and one who is not will get the prompt again the moment
            // the next line arrives.
            //
            // So the prompt fires once for each line the game holds on, and the
            // stamp is cleared when a line actually advances. A player who
            // genuinely misses it hears it again on the next line rather than
            // seven times on this one.
            // The settle still applies: FIRST_PROMPT_SECONDS after the line
            // starts waiting, so a player reading at speed is never told to
            // press a key they are already pressing.
            if (now < _nextPrompt) return;

            if (_promptedForThisLine) return;
            _promptedForThisLine = true;

            // THE PROMPT IS GONE ENTIRELY. (0.7.126.)
            //
            // Zamar: "Remove this constant reminder to press space to proceed.
            // It ruins the moment."
            //
            // He is right and it is worth being precise about why, because the
            // rule it was built on is a good one. An idle prompt exists for a
            // screen that is WAITING IN SILENCE — the player has no way to know
            // anything is expected of them. Dialogue is the opposite: a
            // character has just spoken, the game has a voice, and IKMA talking
            // over the beat afterwards steps on the performance. This project
            // already has a rule about not burying Leshy's audio design; a
            // prompt every four seconds is that mistake on a timer.
            //
            // THE KEY IS STILL DISCOVERABLE, just pulled rather than pushed.
            // Pressing the arrows during a conversation now answers
            // "Conversation in progress, press Space to proceed." — the player
            // learns it the moment they reach for the keys they would naturally
            // try, and never hears it again once they know.
            //
            // The state machine above is kept because AwaitingInput and the
            // once-per-line stamp still drive that answer and the arrival line.
            return;
        }

        // Whether this waiting line has already been noted. Cleared on a
        // successful advance, when the line stops waiting, and on Reset, so it
        // can never strand.
        private static bool _promptedForThisLine;

        internal static void Reset()
        {
            // The old scene's TextDisplayer is gone. Dropped rather than
            // carried, so a destroyed Unity object is never asked anything.
            _textDisplayer = null;
            _nextLookup = 0f;
            _waitingSince = -1f;
            _nextPrompt = -1f;
            _promptedForThisLine = false;
        }
    }
}
