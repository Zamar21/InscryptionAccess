// Speech.cs
using System;

namespace IKMA
{
    /// <summary>
    /// WHY a line is being spoken. Every utterance names one of these, and the
    /// policy in <see cref="Speech"/> decides from it - and from nothing else -
    /// whether the line interrupts, queues, or waits.
    /// </summary>
    /// <remarks>
    /// 0.7.207 - THE ONE POLICY POINT (graph-a11y spec A7: "interrupt by
    /// provenance, not timing, decided at one policy point, not per call
    /// site").
    ///
    /// Until this build the decision lived in three places at once: an
    /// <c>interrupt:</c> boolean on 200 CardReader.Speak call sites, the
    /// Enqueue tier chosen at 67 CombatAnnouncer call sites, and two
    /// one-shot flags (ProtectSpokenAction, 0.7.189; the interrupt
    /// protection, 0.7.193) that patched the cases the boolean got wrong.
    /// Session 19 produced four reported bugs out of that arrangement, in
    /// both directions. docs/AUDIT_BRIEF.md section 2.1 names it as the
    /// strongest finding about the codebase.
    ///
    /// What changed: the boolean is gone from every call site. A caller says
    /// what KIND of line it has; this enum is the whole vocabulary of kinds.
    /// The mechanics underneath - CardReader.Speak as the choke point for
    /// kin/tribe, the Shift+R memory and the log; CombatAnnouncer as the
    /// queue with its holdoffs - are unchanged, so every rule they enforce
    /// still holds and every log line still reads the same.
    /// </remarks>
    public enum Provenance
    {
        /// <summary>
        /// The answer to a keypress that only READS: an arrow, a board read,
        /// help, a repeat. Interrupts whatever is playing - the player asked
        /// for this and the previous read is stale. Is itself cut by a
        /// Result that lands a moment later, and follows (does not cut) a
        /// Result that was spoken just before it (0.7.193).
        /// </summary>
        Browse,

        /// <summary>
        /// The answer to a keypress that CHANGED the game: a card placed, a
        /// sacrifice made, a reward card flipped. Zamar, 0.7.202: "Information
        /// vs. Game action taken." Interrupts anything, including a result
        /// still in the air - the player is waiting to hear whether the press
        /// landed - and is then protected: the queue may follow it but may
        /// not cut it (0.7.189).
        /// </summary>
        Confirmation,

        /// <summary>
        /// Something the player did not press for, that they must not miss:
        /// a version line, a totem grant. Spoken directly, never interrupts.
        /// </summary>
        Quiet,

        /// <summary>
        /// Passive: the game state moved - damage, a death, a sigil, a draw.
        /// Queues. A queued line never cuts another queued line; a Result
        /// cuts a stale Browse read that is still playing. Arms the
        /// one-shot that keeps the next Browse from cutting it.
        /// </summary>
        Result,

        /// <summary>
        /// Passive: information with no state change behind it. Queues and
        /// waits its turn; never interrupts.
        /// </summary>
        Commentary,

        /// <summary>
        /// The game is waiting on the player and this says what to press.
        /// Queues AHEAD of pending commentary - a prompt that arrives after
        /// the player has already acted is not late, it is false.
        /// </summary>
        Prompt,

        /// <summary>
        /// A character said it. Queues ahead of commentary and waits for the
        /// voice sting to finish before the words (Session 11).
        /// </summary>
        Dialogue,

        /// <summary>
        /// The game flashed an overlay the player must not miss - the red
        /// Kaycee's Mod command-line panel, which is on screen for 1.25
        /// seconds and then gone. Same policy row as Confirmation:
        /// interrupts unconditionally, and is protected afterwards so the
        /// queue may follow it but may not cut it.
        ///
        /// It is a KIND of its own rather than a reuse of Confirmation
        /// because Confirmation means "the answer to a keypress the player
        /// made". Nobody pressed anything here. Zamar, 0.7.284: the overlay
        /// "needs to read as high prio non-stompable". Two call sites share
        /// this kind and both are in ChallengeNarrator.
        ///
        /// ONE UTTERANCE PER OVERLAY, never one per line. An interrupt
        /// flushes the speech pump's whole FIFO, so three lines sent as
        /// three Alerts would each throw away the one before it and the
        /// player would hear only the last.
        /// </summary>
        Alert,
    }

    /// <summary>
    /// The single entry point for everything IKMA says. Call sites name a
    /// <see cref="Provenance"/>; nothing else about interrupting is decided
    /// anywhere else.
    /// </summary>
    /// <remarks>
    /// THE POLICY, in one table. Read this before adding a kind.
    ///
    ///   kind          path      interrupts?           protected after?
    ///   Browse        direct    yes (see 0.7.193)     no
    ///   Confirmation  direct    yes, unconditionally  yes (0.7.189)
    ///   Quiet         direct    no                    no
    ///   Result        queue     stale Browse only     one-shot (0.7.193)
    ///   Commentary    queue     never                 no
    ///   Prompt        queue     as Result; jumps ahead of Commentary
    ///   Dialogue      queue     as Result; jumps ahead; waits for the sting
    ///   Alert         direct    yes, unless an Alert  yes (0.7.284)
    ///                           or a queued result is
    ///                           still being read, which
    ///                           it follows (0.7.359)
    ///
    /// "direct" = CardReader.Speak now, through SpeechPump's FIFO.
    /// "queue"  = CombatAnnouncer, which paces lines 0.5s apart and holds
    ///            off after any direct interrupt (HoldOffAfterInterrupt).
    ///
    /// THE WHOLE BEHAVIOUR, as of 0.7.365. Written down before the flags moved
    /// here (M7 refactor 2) so the move could be checked against it; every row
    /// names the member below that enforces it.
    ///
    /// Direct rows (Say):
    ///   Browse        CardReader.Speak(interrupt) - subject to the one-shot.
    ///   Confirmation  queues as a Result if the Morsel shield is armed and
    ///                 under 8s old (ConsumeConfirmationShield), or if a
    ///                 sacrifice line has not been said yet
    ///                 (SacrificeBoneMerger.LinePending). Otherwise drops the
    ///                 one-shot, interrupts, and is protected
    ///                 (ProtectSpokenAction: the queue may follow, not cut).
    ///   Quiet         CardReader.Speak without interrupt.
    ///   Alert         drops the one-shot, interrupts, is protected - unless a
    ///                 queued result or an earlier Alert is still being read,
    ///                 in which case it follows that line (AlertMustWait).
    ///
    /// The choke point (CardReader.Speak):
    ///   The one-shot. Any interrupting line within 3s of a queued Action or
    ///   Dialogue line is downgraded to a non-interrupting one, once
    ///   (ConsumeInterruptProtection). Every interrupt that does reach the
    ///   engine, and every Silence, marks the speech in the air as outside
    ///   speech and applies the queue holdoff (NoteInterrupted) - unless the
    ///   queue itself is the speaker.
    ///
    /// The queue (CombatAnnouncer.Update, then QueueHeadMayGo and
    /// HandOverFromQueue):
    ///   Pacing 0.5s. Holdoff 1.2s after outside speech, unless a result or
    ///   dialogue line is already waiting (then none); an arriving Action or
    ///   Dialogue line cancels a holdoff. HoldQueueFor holds everything.
    ///   Dialogue and Prompt insert after the last Action/Dialogue/Prompt,
    ///   ahead of commentary. NotBefore holds the head, and everything behind.
    ///   ResultWhenReady holds the head until Ready or its deadline.
    ///   While the one-shot is armed: a Prompt at the head waits while the
    ///   result before it is still being read; an interrupting Action or
    ///   Dialogue waits the same way unless the last line handed over was a
    ///   prompt, in which case it clears the one-shot and cuts the prompt.
    ///   "Still being read" = SP_BUSY where the engine reports it, else the
    ///   word estimate (EstimateSpeechSeconds), never more than 12s.
    ///   A queued Action or Dialogue line interrupts only outside speech; a
    ///   queued line never cuts another queued line. After handing over,
    ///   outside speech is cleared, and an Action or Dialogue line that is not
    ///   a prompt, off the card choice screen, arms the one-shot.
    ///
    /// Around it:
    ///   NotePlayerActed - the player answered the prompt last handed over:
    ///   it is silenced at once and becomes outside speech.
    ///   The draw line clears the Morsel shield before confirming
    ///   (HotkeyManager). ClearQueue forgets outside speech.
    ///
    /// The ONE behaviour change this refactor makes, deliberately: a
    /// Confirmation no longer consumes the 0.7.193 one-shot. Before, "X played
    /// in Slot N" spoken right after a combat result was downgraded to a
    /// queued line and followed the result; now it cuts in, as the flip line
    /// already did since 0.7.202 (which cancelled the one-shot by hand for
    /// exactly this reason). Both are the same kind of line, so they get the
    /// same policy. If that turns out wrong in play, the fix is one line in
    /// <see cref="Say(string, Provenance)"/>, not a hunt through call sites.
    ///
    /// check_source.ps1 CHECK 8 still holds: only CardReader and SpeechPump
    /// touch the native call. This class is a caller of CardReader.Speak.
    /// </remarks>
    public static class Speech
    {
        // ------------------------------------------------------------------
        // The policy point.
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // EVENT TAGS (Session 37, M9 per-announcement settings). A call site
        // wraps its Speech call in `using (Speech.Event(kind, source))`; the
        // queue captures the tag with the line, and the line is spoken, kept
        // in history, or dropped by EventSettings.Decide when it is handed to
        // the engine. Main thread only, like everything that speaks.
        // ------------------------------------------------------------------
        private static Func<EventTag> _tag;

        /// <summary>The tag in force for the Speech call being made, or null.</summary>
        internal static Func<EventTag> CurrentTag => _tag;

        internal static IDisposable Event(EventKind kind, EventSource source = EventSource.Any)
        {
            var fixedTag = new EventTag(kind, source);
            return Event(() => fixedTag);
        }

        /// <summary>
        /// A tag decided when the line is composed, not when it is queued: a
        /// damage record that turns out to be a death.
        /// </summary>
        internal static IDisposable Event(Func<EventTag> late)
        {
            var scope = new TagScope(_tag);
            _tag = late;
            return scope;
        }

        private sealed class TagScope : IDisposable
        {
            private readonly Func<EventTag> _previous;
            internal TagScope(Func<EventTag> previous) { _previous = previous; }
            public void Dispose() { _tag = _previous; }
        }

        internal static EventTag Resolve(Func<EventTag> tag)
        {
            if (tag == null) return null;
            try { return tag(); } catch { return null; }
        }

        public static void Say(string text, Provenance kind)
        {
            // Session 37: F1 presses H for the player and keeps what it says
            // as the help list's rows instead of speaking them. HelpList.cs.
            if (HelpList.Capture(text)) return;

            // A tagged line said directly (Confirmation, Alert, Quiet): the
            // player's event settings decide before anything is spoken.
            bool keep = kind == Provenance.Confirmation || kind == Provenance.Alert;
            if (_tag != null &&
                (kind == Provenance.Confirmation || kind == Provenance.Alert || kind == Provenance.Quiet))
            {
                var tag = Resolve(_tag);
                bool drop, speak;
                EventSettings.Decide(tag, true, out drop, out speak, out keep);
                if (drop || !speak)
                {
                    Plugin.Log?.LogInfo($"IKMA EVENTS: not spoken ({EventSettings.Describe(tag)} {(drop ? "source off" : "announce off")}): {text}");
                    if (keep && !drop) ReviewHistory.Add(text);
                    // A shield armed for this confirmation must not outlive it
                    // and downgrade the next one (ShieldFromNextConfirmation).
                    if (kind == Provenance.Confirmation) ConsumeConfirmationShield();
                    return;
                }
            }

            switch (kind)
            {
                case Provenance.Browse:
                    CardReader.Speak(text, interrupt: true);
                    // Session 38: reads the player asked for are kept too
                    // (Mod Settings > History > Reads), except the history's
                    // own reads and mod messages (BrowseUnrecorded).
                    if (!_browseUnrecorded) ReviewHistory.Consider(text, ReviewHistory.ReadsOn);
                    // 0.4.8.007 - a read the player asked for is what they are
                    // on: it becomes the Card buffer (Buffers.cs).
                    // 0.4.8.012 - not for the lists' own lines (BrowseNoFocus).
                    if (!_browseUnrecorded && !_browseNoFocus) Buffers.BindFocus(text);
                    return;

                case Provenance.Confirmation:
                    // 0.7.293 — UNLESS THE LINE IN THE AIR IS THE OTHER HALF
                    // OF THIS SAME PRESS. Morsel fires on the sacrifice that
                    // pays for the placement this line is confirming, so
                    // cutting it is a press talking over itself. See
                    // CombatAnnouncer.ShieldFromNextConfirmation.
                    if (ConsumeConfirmationShield())
                    {
                        CombatAnnouncer.EnqueueAction(text);
                        return;
                    }

                    // 0.7.340 — A SACRIFICE THAT PAID FOR THIS HAS NOT BEEN
                    // SAID YET. Zamar's 0.7.339 log: "Worker Ant played in
                    // Slot 4." then "Squirrel in Slot 4 is sacrificed.
                    // Received 1 bone." — "That order needs to be reversed."
                    // The sacrifice line waits a moment for its bone; with
                    // only one slot on offer he placed the card inside that
                    // wait. The sacrifice happened first, so it is said first:
                    // the confirmation queues behind it.
                    if (SacrificeBoneMerger.LinePending)
                    {
                        Plugin.Log?.LogInfo("IKMA SPEECH: confirmation queued behind the unsaid sacrifice line.");
                        CombatAnnouncer.EnqueueAction(text);
                        return;
                    }

                    // Outranks a result still in the air: drop the one-shot
                    // rather than let Speak consume it and queue this line.
                    CancelInterruptProtection();
                    CardReader.Speak(text, interrupt: true);
                    // ...and is not cut by the queue that follows.
                    ProtectSpokenAction();
                    if (keep) ReviewHistory.Add(text);   // Session 35: a press that changed the game is an event
                    return;

                case Provenance.Quiet:
                    CardReader.Speak(text, interrupt: false);
                    if (_tag != null && keep) ReviewHistory.Add(text);   // Session 37: a tagged quiet line is an event
                    return;

                case Provenance.Result:
                    CombatAnnouncer.EnqueueAction(text);
                    return;

                case Provenance.Commentary:
                    CombatAnnouncer.Enqueue(text);
                    return;

                case Provenance.Prompt:
                    CombatAnnouncer.EnqueuePrompt(() => text);
                    return;

                case Provenance.Dialogue:
                    CombatAnnouncer.EnqueueDialogue(text, 0f);
                    return;

                // Identical mechanics to Confirmation on purpose - see the
                // enum. The overlay is on screen for 1.25s; a line that
                // waited its turn would describe something already gone.
                //
                // 0.7.359 — EXCEPT OVER A LINE THAT MAY NOT BE CUT. Zamar, two
                // challenges at one battle's start: "the second challenge
                // stomped the first and it shouldn't." The first overlay line
                // had also cut "Leshy's totem gives enemy Canine cards
                // Rampager." the moment it started. So an Alert still speaks at
                // once, ahead of anything waiting in the queue, but if another
                // Alert or a queued result is still being read it follows that
                // line instead of cutting it. "Still being read" is the
                // engine's own busy state where it can say, the word estimate
                // where it cannot, and never longer than 12 seconds.
                case Provenance.Alert:
                {
                    bool follow = AlertMustWait();
                    if (follow)
                    {
                        Plugin.Log?.LogInfo("IKMA SPEECH: overlay line follows the line still being read instead of cutting it.");
                        CardReader.Speak(text, interrupt: false);
                    }
                    else
                    {
                        CancelInterruptProtection();
                        CardReader.Speak(text, interrupt: true);
                    }
                    ProtectSpokenAction();
                    NoteAlertSpoken(text, follow);
                    if (keep) ReviewHistory.Add(text);   // Session 35
                    return;
                }
            }
        }

        // ------------------------------------------------------------------
        // AN OVERLAY LINE IN THE AIR. (0.7.359.) Kept here, beside the one row
        // that reads it, rather than as another flag on CombatAnnouncer.
        // ------------------------------------------------------------------
        private static float _alertAt = -99f;
        private static float _alertEndsAt = -99f;
        private static int   _alertInterruptMark = -1;

        private static bool AlertMustWait()
            => ResultInTheAir() || AlertInTheAir();

        /// <summary>
        /// The last overlay line is still being read: nothing has cut it since,
        /// and the engine says it is still speaking (or, where the engine
        /// cannot say, the word estimate has not run out). Capped at 12s.
        /// </summary>
        private static bool AlertInTheAir()
        {
            if (_alertAt < 0f) return false;
            if (CardReader.InterruptCount != _alertInterruptMark) return false;

            float now = UnityEngine.Time.unscaledTime;
            if (now - _alertAt > 12f) return false;

            int busy = SpeechPump.EngineBusy;
            if (busy >= 0) return busy == 1;
            return now < _alertEndsAt;
        }

        private static void NoteAlertSpoken(string text, bool followed)
        {
            float now = UnityEngine.Time.unscaledTime;
            // A line that followed starts when the one ahead of it ends.
            float start = followed && _alertEndsAt > now ? _alertEndsAt : now;
            _alertAt = now;
            _alertEndsAt = start + EstimateSpeechSeconds(text);
            _alertInterruptMark = CardReader.InterruptCount;
        }

        /// <summary>
        /// Deferred form for the queued kinds: the text is composed when the
        /// line reaches the front of the queue, not when it is enqueued. Use
        /// it for anything read off game state after an async step (the
        /// coroutine-prefix rule: prefixes fire at creation, not completion).
        /// Direct kinds have nothing to defer and fall through to Say.
        /// </summary>
        public static void Say(Func<string> provider, Provenance kind)
        {
            if (HelpList.Capturing)
            {
                string now = null;
                try { now = provider?.Invoke(); } catch { }
                HelpList.Capture(now);
                return;
            }

            switch (kind)
            {
                case Provenance.Result:
                    CombatAnnouncer.EnqueueActionDeferred(provider);
                    return;
                case Provenance.Commentary:
                    CombatAnnouncer.EnqueueDeferred(provider);
                    return;
                case Provenance.Prompt:
                    CombatAnnouncer.EnqueuePrompt(provider);
                    return;
                default:
                    Say(provider(), kind);
                    return;
            }
        }

        // ==================================================================
        // THE POLICY STATE. (M7 refactor 2, 0.7.365.) Everything below this
        // line until the shorthands used to live on CombatAnnouncer as
        // instance fields; it moved here so every "may this cut that?" rule
        // sits beside the table above. The comments are the originals. The
        // Instance == null guards are kept exactly: before the announcer's
        // Awake, none of these flags can be set, as before. CombatAnnouncer
        // keeps the queue and its timing (pacing, holdoff, NotBefore, Ready).
        // ==================================================================

        // True when speech from OUTSIDE the queue is in the air — a browse read,
        // a prompt, a refusal. (Session 11.)
        //
        // This is the whole priority model, and it replaces the one 0.7.10
        // shipped. That version decided interrupt PER ENTRY: Action lines
        // interrupted, Info lines did not. Since nearly every combat line is
        // Action, every combat line stomped the one ahead of it, and Zamar's
        // test log showed exactly that — Coyote's attack killing Porcupine's,
        // "Your turn." killing the scales read.
        //
        // The rule now: a queued line NEVER interrupts another queued line.
        // There is nothing to gain by it; the line ahead is describing the same
        // combat and the player wants both. An Action line has something worth
        // displacing only when the speech playing came from outside the queue,
        // because that line is describing a board that has since moved.
        //
        // Zamar's framing, which is the better one: combat results are the thing
        // that must never be stomped, and everything else queues behind them.
        // That falls out of this without needing a priority table to enforce it
        // — "Your turn." simply arrives after the damage it used to talk over.
        private static bool _externalSpeech = false;

        // Set while the announcer itself is speaking, so that an Action line's
        // interrupt=true does not loop back through NotifyInterrupted and impose
        // a 1.2-second holdoff on the message immediately behind it.
        private static bool _speakingFromQueue = false;

        /// <summary>
        /// Every interrupt that reaches the engine, from CardReader.Speak and
        /// CardReader.Silence. A line the queue itself is handing over is
        /// ignored; anything else marks the speech in the air as outside
        /// speech (a queued result may cut it) and applies the queue's holdoff.
        /// </summary>
        internal static void NoteInterrupted()
        {
            if (CombatAnnouncer.Instance == null) return;

            // Ignore our own Action lines. Without this guard an Action message
            // would impose a holdoff on the message directly behind it, so a
            // death that interrupts a slot read would then delay the next death
            // by over a second. (Session 10.)
            if (_speakingFromQueue) return;

            _externalSpeech = true;

            CombatAnnouncer.HoldOffAfterInterrupt();
        }

        /// <summary>CombatAnnouncer.ClearQueue: nothing in the air is ours to cut any more.</summary>
        internal static void ForgetOutsideSpeech() => _externalSpeech = false;

        /// <summary>
        /// Mark the line JUST spoken as a confirmation that a game action has
        /// objectively been taken — a card placed, a creature sacrificed. The
        /// queue may follow it, but may not cut it.
        /// </summary>
        /// <remarks>
        /// 0.7.189. Zamar, Prospector fight, on a Guardian line landing on top
        /// of "Mole played in Slot 1":
        ///
        ///   "That's a top priority 'game action has objectively been taken'
        ///    callout that should never get stomped."
        ///
        /// WHY IT HAPPENED. _externalSpeech exists so an Action line can cut
        /// over a stale BROWSE read — arrow through four slots, and the first
        /// three readouts are worth losing. It could not tell a browse read
        /// from a confirmation, because both arrive through
        /// CardReader.Speak(interrupt: true), so it cut both.
        ///
        /// THE FIX IS ONE BIT, NOT A TIMER. Clearing the flag means the next
        /// queued line finds nothing it is allowed to interrupt, so it waits
        /// its turn and speaks after. The holdoff is untouched, so the queue
        /// does not start on top of the confirmation either. This can only ever
        /// REMOVE an interrupt, never add one — the worst case is a line that
        /// arrives a moment later than it used to.
        ///
        /// The graph-a11y spec states the general rule this is a step toward
        /// (A7): interrupt by PROVENANCE, not by timing, decided at one policy
        /// point rather than per call site. IKMA still decides per call site.
        /// See ikma_graph_a11y_spec.
        /// </remarks>
        // ==================================================================
        // AN ANSWERED PROMPT IS STALE. (0.7.341.)
        //
        // Zamar's 0.7.340 log: "Fused Flying Ant costs 1 blood. Choose 1
        // sacrifice. Left and right arrows to navigate, Enter to confirm. A
        // sacrifice cannot be cancelled once started." — he had already chosen
        // the sacrifice, and the Morsel and sacrifice results waited politely
        // behind the rest of that sentence. "this line wasnt stomped like it
        // should have been."
        //
        // The prompt is spoken FROM the queue, and a queued line never cuts
        // another queued line, so nothing could. But a prompt is an
        // instruction, not a result: once the player has done what it asked,
        // it is describing the past. The moment IKMA injects the press, the
        // prompt in the air is marked as outside speech, so the next result
        // line may cut it exactly as it would cut a browse read.
        // ==================================================================
        private static bool _lastWasPrompt;

        internal static void NotePlayerActed(string what)
        {
            if (CombatAnnouncer.Instance == null || !_lastWasPrompt) return;
            _lastWasPrompt = false;
            _externalSpeech = true;

            // 0.7.357 — THE ANSWER CUTS THE QUESTION NOW, not when the next
            // result happens to interrupt. Zamar: "Mantis God costs 1 blood.
            // Choose 1 sacrifice..." "didnt get stomped by playing the card
            // with the numbers." The result it was waiting for (the sacrifice
            // line) holds 0.3s for its bone and the next queued line was plain
            // commentary, which never interrupts — so the prompt read on to the
            // end. The prompt is the last line handed over, so silencing now
            // cuts it and nothing else; a prompt is only handed over once the
            // result before it has finished (0.7.355).
            CardReader.Silence();

            // 0.7.348 tried cancelling the result's protection here and it
            // cut the result itself: the screen reader was still reading the
            // sacrifice + Morsel line when the confirmation interrupted, so
            // Zamar never heard Morsel. Replaced by the timed hold in Update —
            // see "WAIT FOR THE RESULT, THEN CUT THE PROMPT".

            Plugin.Log?.LogInfo($"IKMA SPEECH: prompt answered ({what}) — the next result may cut it.");
        }

        public static void ProtectSpokenAction()
        {
            if (CombatAnnouncer.Instance == null) return;
            _externalSpeech = false;
        }

        // 0.7.193 — THE OTHER DIRECTION. A COMBAT RESULT IS NOT CUT BY A BROWSE.
        //
        // Zamar, Prospector fight, after "Bloodhound with Guardian is blocked
        // from moving to slot 3." was cut in half by the card read from his very
        // next keypress:
        //
        //   "Yeah here the Bloodhound's Guardian callout got stomped, that's
        //    important info."
        //
        // 0.7.189 stopped the queue cutting a CONFIRMATION. This is the reverse
        // case: a keypress cutting a RESULT. Both are "something objectively
        // happened"; only the direction differs.
        //
        // ONE SHOT, NOT A TIMER. A duration cannot be estimated — a single
        // speech call on this project measured anywhere from 12ms to 765ms, and
        // ikma-perf-and-regressions is explicit that timers are the wrong tool
        // for "is IKMA still speaking". So instead the NEXT interrupting line
        // after a result is downgraded to a queued one, exactly once. It follows
        // the result instead of cutting it. A second keypress interrupts
        // normally, because by then it is cutting the browse read and not the
        // result — which is the behaviour he wants when he is arrowing.
        //
        // The three-second cap only stops a flag set before a long silence from
        // swallowing an interrupt minutes later. It is not a duration estimate;
        // nothing depends on it being accurate.
        private const float PROTECT_EXPIRY_SECONDS = 3f;
        private static bool  _protectNextInterrupt;
        private static float _protectSetAt = -99f;
        private static float _protectLineSeconds = PROTECT_EXPIRY_SECONDS;

        // The last line this queue handed to the engine was a prompt. Unlike
        // _lastWasPrompt, NotePlayerActed does not clear it. (0.7.355.)
        private static bool _lastHandedWasPrompt;

        private static bool ResultStillSpeaking()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now - _protectSetAt > 12f) return false;
            int busy = SpeechPump.EngineBusy;
            if (busy >= 0) return busy == 1;
            return now < _protectSetAt + _protectLineSeconds;
        }

        // Roughly how long the default voice takes to read a line: about three
        // words a second, plus a beat, capped. Only used to decide WHEN an
        // interrupt may land, never whether a line is spoken. (0.7.349.)
        internal static float EstimateSpeechSeconds(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            int words = 1;
            foreach (char c in text) if (c == ' ') words++;
            float secs = words / 3f + 0.4f;
            return secs > 10f ? 10f : secs;
        }

        /// <summary>
        /// A queued Action or Dialogue line is still being read: it armed the
        /// one-shot above, nothing interrupting has been spoken since (anything
        /// that interrupts consumes or cancels it), and the engine — or the word
        /// estimate — says it has not finished. Consumes nothing. For Speech's
        /// Alert row, 0.7.359: an overlay line follows such a line rather than
        /// cutting it.
        /// </summary>
        internal static bool ResultInTheAir()
            => CombatAnnouncer.Instance != null && _protectNextInterrupt && ResultStillSpeaking();

        /// <summary>
        /// True once, if a combat result was spoken a moment ago and has not yet
        /// been followed by anything. Consumes the flag.
        /// </summary>
        /// <summary>
        /// Drop the one-shot without using it. For a line that reports a GAME
        /// ACTION THE PLAYER JUST TOOK — those outrank anything already being
        /// read, because the player is waiting to hear whether the press landed.
        /// </summary>
        public static void CancelInterruptProtection()
        {
            if (CombatAnnouncer.Instance == null) return;
            _protectNextInterrupt = false;
        }

        // ==================================================================
        // A LINE A CONFIRMATION MAY NOT CUT. (0.7.293.)
        //
        // ZAMAR, 0.7.292: "Morsel is an exception since it triggers on play.
        // So Morsel should read."
        //
        // The general policy is that a Confirmation — the answer to a keypress
        // that CHANGED the game — interrupts anything, including a result in
        // the air, because the player is waiting to hear whether the press
        // landed. His 0.7.291 log is the case where that is wrong:
        //
        //   SPEAK (interrupt): Mealworm's Morsel ability triggers.
        //   PLAY: placement INJECTED on slot 1 for 'Flying Ant'.
        //   SPEAK (interrupt): Flying Ant played in Slot 1.
        //
        // Morsel fires on the sacrifice that PAYS for that placement, so the
        // trigger line and the confirmation are two halves of one press. The
        // confirmation is not newer news arriving over stale news; it is the
        // second half cutting off the first.
        //
        // NARROW ON PURPOSE. This is not "results outrank confirmations" —
        // that would undo 0.7.202 and put every board read ahead of hearing
        // whether a card was placed. It is armed only by a trigger whose
        // cause IS the press that is about to be confirmed, and it is a one
        // shot with the same three-second cap as the protection above, for
        // the same reason: so a flag set before a long silence cannot swallow
        // an interrupt minutes later.
        // ==================================================================
        private const float SHIELD_EXPIRY_SECONDS = 8f;
        private static bool  _shieldNextConfirmation;
        private static float _shieldSetAt = -99f;

        /// <summary>
        /// The next Confirmation queues instead of cutting. Armed by a line
        /// caused by the same player action the confirmation reports.
        /// </summary>
        public static void ShieldFromNextConfirmation(string reason)
        {
            if (CombatAnnouncer.Instance == null) return;
            _shieldNextConfirmation = true;
            try { _shieldSetAt = UnityEngine.Time.unscaledTime; } catch { }
            Plugin.Log?.LogInfo($"IKMA SPEECH: next confirmation will queue — {reason}.");
        }

        /// <summary>True once, if a shielded line is still owed the ear.</summary>
        public static bool ConsumeConfirmationShield()
        {
            if (CombatAnnouncer.Instance == null) return false;
            if (!_shieldNextConfirmation) return false;

            _shieldNextConfirmation = false;

            float now = 0f;
            try { now = UnityEngine.Time.unscaledTime; } catch { }
            // 0.7.339 — its own, longer cap. A shielded line is a whole
            // sentence the player has not heard the end of; three seconds cut
            // the Bone King line. The cap still only stops a stale flag from
            // swallowing a confirmation long after — a confirmation that
            // queues instead of cutting loses nothing.
            if (now - _shieldSetAt > SHIELD_EXPIRY_SECONDS) return false;

            return true;
        }

        public static bool ConsumeInterruptProtection()
        {
            if (CombatAnnouncer.Instance == null) return false;
            if (!_protectNextInterrupt) return false;

            _protectNextInterrupt = false;

            float now = 0f;
            try { now = UnityEngine.Time.unscaledTime; } catch { }
            if (now - _protectSetAt > PROTECT_EXPIRY_SECONDS) return false;

            return true;
        }

        // ==================================================================
        // WAIT FOR THE RESULT, THEN CUT THE PROMPT. (0.7.349.)
        //
        // Two reports that looked opposite. 0.7.347: "Mantis God played in
        // Slot 3." queued behind the whole "Choose a slot" prompt — "should
        // have been stomped". 0.7.348 cancelled the protection to fix that
        // and the confirmation cut the sacrifice line instead: "I never
        // heard the Morsel triggers line". The screen reader queues on its
        // own, so when the prompt is handed over the result before it may
        // still be playing, and one interrupt kills both.
        //
        // So an interrupting Action that would land while a protected
        // result is still being read WAITS until that result's estimated
        // end, then interrupts — which cuts the prompt behind it and
        // nothing else. The estimate is words at the default voice rate,
        // capped, so nothing waits long.
        // ==================================================================
        //
        // 0.7.355 — ASK THE ENGINE, AND HOLD THE PROMPT TOO. The word
        // estimate came up short and Morsel was cut again. Now:
        //   - a PROMPT waits at the head while the result before it is
        //     still being read, so it is never handed to the engine behind
        //     a result; if he acts first it withdraws itself unsaid.
        //   - an interrupting Action waits the same way — unless the last
        //     line handed over was a prompt, in which case the result
        //     has already finished and the interrupt cuts only the prompt.
        // "Still being read" is the engine's own SP_BUSY where it can say
        // (SpeechPump.EngineBusy), the word estimate where it cannot, and
        // never longer than 12 seconds.
        internal static bool QueueHeadMayGo(bool isAction, bool isDialogue, bool isPrompt)
        {
            if (!_protectNextInterrupt) return true;

            bool promptWaiting = isPrompt;
            bool interruptWaiting = (isAction || isDialogue) && !isPrompt &&
                                    _externalSpeech && !_lastHandedWasPrompt;
            if (promptWaiting || interruptWaiting)
            {
                if (ResultStillSpeaking()) return false;
                if (interruptWaiting) _protectNextInterrupt = false;
            }
            else if ((isAction || isDialogue) && _externalSpeech && _lastHandedWasPrompt)
            {
                _protectNextInterrupt = false;   // cut the prompt, nothing else
            }
            return true;
        }

        /// <summary>
        /// The queue hands a line to the engine. Decides whether it cuts what
        /// is playing (only speech from outside the queue), speaks it, and arms
        /// what a queued result arms. Called only from CombatAnnouncer.Update.
        /// </summary>
        internal static bool HandOverFromQueue(string message, bool isAction, bool isDialogue, bool isPrompt,
                                               Func<EventTag> tagSource = null)
        {
            // Session 37: the player's event settings. Session 35's rule is the
            // default: every queued line is a game event except prompts and
            // character dialogue.
            // Session 38: prompts and dialogue are kept too, each by its own
            // switch (History > Prompts; Events > Dialogue > Add to history).
            bool keep = isPrompt ? ReviewHistory.PromptsOn
                      : isDialogue ? ReviewHistory.DialogueOn
                      : true;
            if (tagSource != null)
            {
                var tag = Resolve(tagSource);
                bool drop, speak;
                EventSettings.Decide(tag, keep, out drop, out speak, out keep);
                if (drop || !speak)
                {
                    Plugin.Log?.LogInfo($"IKMA EVENTS: not spoken ({EventSettings.Describe(tag)} {(drop ? "source off" : "announce off")}): {message}");
                    if (keep && !drop) ReviewHistory.Add(message);
                    return false;
                }
            }

            // An Action line cuts in only over speech from outside the queue.
            // Cleared whichever way this goes: after any queued line speaks, the
            // next one is competing with US, not with a browse read, and the
            // browse read has either been stomped or is already finished.
            bool interrupt = (isAction || isDialogue) && _externalSpeech;
            _externalSpeech = false;

            _speakingFromQueue = true;
            try   { CardReader.Speak(message, interrupt); }
            finally { _speakingFromQueue = false; }

            // Session 38: see ReviewHistory for the whole rule.
            ReviewHistory.Consider(message, keep);
            _lastWasPrompt = isPrompt;
            _lastHandedWasPrompt = isPrompt;

            // Arm the one-shot above, AFTER the line is away. Arming it first
            // would have this very call consume its own protection: Speak checks
            // the flag at the choke point and cannot tell which caller set it.
            //
            // 0.7.200 — PROMPTS ARE NOT RESULTS, AND MUST NOT BE PROTECTED.
            //
            // Zamar, three reports in one log, all the same defect:
            //
            //   "Let the tab key stomp the Playing Squirrel call out. We can
            //    give the tab key extra stomping permission, for snappier play."
            //   "Tab can also interrupt this info." (the sacrifice prompt)
            //   "Sparrow played in slot 4 should have stomped all of the lines
            //    above it."
            //
            // 0.7.193 armed this on IsAction, and the Prompt tier CARRIES
            // IsAction — that bundling is regression pattern 2 in
            // ikma-perf-and-regressions, biting for the second time. So every
            // "Choose a slot to play X" prompt started protecting itself from
            // the player's very next keypress, and from the confirmation that
            // the play had happened.
            //
            // THE DISTINCTION IS PROVENANCE, WHICH IS THE WHOLE POINT. A RESULT
            // is something the board did and the player cannot get back — a
            // sigil firing, a card moving, damage landing. A PROMPT is an
            // instruction the player is already acting on; by the time they have
            // pressed the key, it has done its job and is in the way. Protecting
            // it makes the mod feel slow for no gain, which is exactly what he
            // described.
            // 0.7.204 — AND NOT ON THE CARD CHOICE SCREEN AT ALL.
            //
            // Zamar: "it kept reading the Pack rat after I flipped other cards
            // again." The log shows each flip reading its own card correctly —
            // what changed is that the first flip INTERRUPTED and the ones after
            // it did not, so a long card read was still playing when the next
            // one arrived and the new line queued behind it.
            //
            // The card read itself was arming the protection. On this screen
            // nothing is a board result: every line is either information the
            // player asked for or the answer to a key they pressed, and both of
            // those are exactly the things that SHOULD cut each other off. The
            // protection is for combat, so it stands down where combat is not.
            if ((isAction || isDialogue) && !isPrompt &&
                !CardChoiceReader.Active)
            {
                _protectNextInterrupt = true;
                try { _protectSetAt = UnityEngine.Time.unscaledTime; } catch { }
                _protectLineSeconds = EstimateSpeechSeconds(message);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Named shorthands. These are the calls that appear in the readers;
        // the enum is for the table above and for code that carries a kind
        // as data.
        // ------------------------------------------------------------------

        /// <summary>A keypress asked for a read. Interrupts.</summary>
        public static void Browse(string text)  => Say(text, Provenance.Browse);

        // Session 38: a Browse read that never goes into the review history -
        // the history reading itself back, and mod messages ("Loading.").
        private static bool _browseUnrecorded;
        internal static void BrowseUnrecorded(string text)
        {
            _browseUnrecorded = true;
            try { Say(text, Provenance.Browse); }
            finally { _browseUnrecorded = false; }
        }

        // 0.4.8.012 - A READ THAT IS NOT WHAT THE PLAYER IS ON. Zamar's
        // playtest: "Im not sure why I kept hearing so many Closed". The help
        // list's "Closed" was a Browse, so it became the Card buffer, and every
        // Ctrl+Up after that said "Closed" again. The help list's rows, Mod
        // Settings' rows and their "Closed", "Rulebook closed" and the
        // Ctrl+letter reads (Say the Spire 2's announce keys, which never move
        // focus) are spoken and kept in the history as before, but leave the
        // Card buffer on the card.
        private static bool _browseNoFocus;
        internal static void BrowseNoFocus(string text)
        {
            _browseNoFocus = true;
            try { Say(text, Provenance.Browse); }
            finally { _browseNoFocus = false; }
        }

        /// <summary>A keypress changed the game. Interrupts, then is protected.</summary>
        public static void Confirm(string text) => Say(text, Provenance.Confirmation);

        /// <summary>Unrequested, must not be missed, must not cut anything.</summary>
        public static void Quiet(string text)   => Say(text, Provenance.Quiet);

        /// <summary>The state moved. Queues; never stomped by another queued line.</summary>
        public static void Result(string text)  => Say(text, Provenance.Result);
        public static void Result(Func<string> provider) => Say(provider, Provenance.Result);

        /// <summary>
        /// A Result the player pressed a key for: it goes to the front of the
        /// queue instead of the back. (0.7.436.) See
        /// CombatAnnouncer.EnqueueActionFirst.
        /// </summary>
        public static void ResultFirst(string text)
        {
            if (HelpList.Capture(text)) return;
            CombatAnnouncer.EnqueueActionFirst(text);
        }

        /// <summary>Information only. Queues; never interrupts.</summary>
        public static void Commentary(string text) => Say(text, Provenance.Commentary);
        public static void Commentary(Func<string> provider) => Say(provider, Provenance.Commentary);

        /// <summary>The game is waiting; say what to press. Jumps the commentary.</summary>
        public static void Prompt(Func<string> provider) => Say(provider, Provenance.Prompt);

        /// <summary>An on-screen overlay the player must not miss. Interrupts, then is protected.</summary>
        public static void Alert(string text) => Say(text, Provenance.Alert);

        // ------------------------------------------------------------------
        // The queued kinds with timing attached. (M7 refactor 2, 0.7.365.)
        // Before this build about a hundred call sites reached into
        // CombatAnnouncer.Enqueue* directly, so the kind of a line was implied
        // by which Enqueue method the caller happened to pick. Every one now
        // comes through here and names its kind. The queue mechanics behind
        // each overload are unchanged - these are the same calls, one hop
        // later. check_source.ps1 CHECK 12 keeps it that way.
        // ------------------------------------------------------------------

        /// <summary>
        /// A deferred result that may not compose for
        /// <paramref name="delaySeconds"/> - it folds in something that has not
        /// happened yet (the sacrifice line waiting for its bone).
        /// </summary>
        public static void Result(Func<string> provider, float delaySeconds)
            => CombatAnnouncer.EnqueueActionDeferredDelayed(provider, delaySeconds);

        /// <summary>
        /// A result that reserves its place in the queue NOW, at the start of
        /// the event it describes, and speaks when <paramref name="ready"/>
        /// answers true or <paramref name="maxWaitSeconds"/> runs out (the
        /// multi-strike summary, the Pirate Skull cannon).
        /// </summary>
        /// <para>0.7.447 - <paramref name="partial"/>, when given, says what has
        /// happened SO FAR if a character starts talking before the event is
        /// over. See CombatAnnouncer.FlushPartialBeforeDialogue.</para>
        public static void ResultWhenReady(Func<bool> ready, Func<string> provider,
                                           float maxWaitSeconds, string label,
                                           Func<string> partial = null)
            => CombatAnnouncer.EnqueueActionWhenReady(ready, provider, maxWaitSeconds, label, partial);

        /// <summary>
        /// A prompt that keeps its queue position but may not speak for
        /// <paramref name="delaySeconds"/>, so it does not cut the line
        /// explaining why it is being asked.
        /// </summary>
        public static void Prompt(Func<string> provider, float delaySeconds)
            => CombatAnnouncer.EnqueuePromptDelayed(delaySeconds, provider);

        /// <summary>
        /// A character's line, held <paramref name="delaySeconds"/> for the
        /// voice sting.
        /// </summary>
        public static void Dialogue(string text, float delaySeconds)
            => CombatAnnouncer.EnqueueDialogue(text, delaySeconds);

        /// <summary>
        /// Cut whatever is playing and say nothing: the line describes
        /// something the player has already left.
        /// </summary>
        public static void Silence() => CardReader.Silence();
    }
}
