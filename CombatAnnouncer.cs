// CombatAnnouncer.cs
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// Queued play-by-play combat announcer. Mirrors Hearthstone Access commentary style:
    /// each combat event is announced in sequence, ~0.5s apart, without interrupting.
    ///
    /// Interrupt holdoff: whenever HotkeyManager speaks with interrupt=true (slot nav,
    /// confirmations, affordability errors), it calls NotifyInterrupted(). This resets
    /// the drip timer to a longer holdoff so queued announcements (bones, damage, deaths)
    /// wait for the play-flow speech to finish before resuming. Without this, bone/death
    /// announcements get stomped by rapid interrupt=true slot navigation calls.
    ///
    /// Race condition fix: patches can fire before MonoBehaviour Awake() runs.
    /// Enqueue() stores messages in a static pre-init buffer if Instance isn't ready yet.
    /// The buffer drains into the real queue once Awake() sets Instance.
    ///
    /// DEFERRED MESSAGES (Session 9): the queue holds message *providers*, not
    /// strings. Enqueue(string) wraps a fixed line; EnqueueDeferred(Func<string>)
    /// composes the line at speak time instead. That matters for reads of live
    /// game state queued from a coroutine Prefix — the opening encounter queue
    /// read, for instance, is queued the moment PlayerTurn's enumerator is
    /// created, when Leshy's queue may still be filling. Composing late means we
    /// speak what is actually on the board rather than a stale snapshot.
    /// A provider returning null or empty is skipped silently.
    /// </summary>
    public class CombatAnnouncer : MonoBehaviour
    {
        public static CombatAnnouncer Instance { get; private set; }

        private static ManualLogSource _log;
        private static readonly Queue<Entry> _preInitBuffer = new Queue<Entry>();

        // A LinkedList rather than a Queue since Session 13: everything still
        // arrives at the tail and leaves from the head, but character dialogue
        // is inserted mid-queue (see EnqueueDialogue) and a Queue cannot do
        // that.
        private readonly LinkedList<Entry> _queue = new LinkedList<Entry>();
        private float _timer = 0f;

        // True while _timer holds an interrupt holdoff rather than the ordinary
        // between-messages interval. Only a holdoff may be cut short; cutting
        // the ordinary interval short would let each announcement stomp the one
        // before it and combat would become unintelligible.
        private bool _holdoffActive = false;

        /// <summary>
        /// Info  — commentary and prompts. Waits its turn, never interrupts.
        /// Action — the game state actually moved. Cuts through a pending
        ///          interrupt holdoff and interrupts whatever is being spoken.
        /// </summary>
        private class Entry
        {
            internal Func<string> Provider;
            internal bool IsAction;

            // A character said this. Interrupts outside speech exactly as an
            // Action line does, and is inserted ahead of pending Info lines
            // rather than appended. (Session 13.)
            internal bool IsDialogue;

            // This line INSTRUCTS — it tells the player the game is waiting on
            // them and what to press. (Session 14.)
            //
            // Prompts carry IsAction as well, so every existing rule about
            // interrupting and holdoffs applies to them unchanged. IsPrompt
            // changes one thing only: where the entry lands in the queue. It
            // goes ahead of pending commentary by the same rule dialogue uses,
            // because a prompt that arrives after the player has already acted
            // is not merely late — it is false, and it tells them to do a thing
            // they have done.
            internal bool IsPrompt;

            // Earliest unscaled time this line may be spoken. Zero means now.
            // (Session 11.) Used for character dialogue: Leshy and the others
            // have voice stings that play as the text appears, and narration
            // landing on top of them buries a piece of audio design that is one
            // of the best things about this game. A blind player should get the
            // performance and then the words, not the words over the
            // performance.
            internal float NotBefore;

            // Held at the head of the queue until this answers true, or until
            // ReadyDeadline passes. (0.7.347.) For a line that reserves its
            // place BEFORE the event it describes has finished — the
            // multi-strike summary, which must speak ahead of anything the
            // attack itself triggers, and cannot be composed until the last
            // strike lands. The deadline means a coroutine that never finishes
            // cannot silence the announcer.
            internal Func<bool> Ready;
            internal float ReadyDeadline;

            // 0.7.447 - A HELD LINE AND A CONVERSATION. Three things, all for
            // one case: a character starts talking in the MIDDLE of the event
            // this line is waiting on (the Pack Mule dying to a Hydra's second
            // strike, "DAAAG NAB IT!", three strikes still to come).
            //
            //   Partial   - says what has happened so far. Zamar, Session 45:
            //               the Mule's death and the pack go BEFORE the boss
            //               line, the rest of the attack after Space.
            //   MaxWait   - the wait this line was given, so its clock can be
            //               started again (see Update).
            //   HeldSince - when it was queued; the clock is never pushed
            //               back for longer than MaxHeldSeconds in all.
            internal Func<string> Partial;
            internal float MaxWait;
            internal float HeldSince;

            // The silence key was pressed while this line waited (Session 36).
            // It is still composed at its normal moment, and written to the
            // review history instead of spoken. SilenceKey.cs.
            internal bool Silenced;

            // The event tag in force when the line was queued (Session 37,
            // EventSettings). Null = not an event the player can switch.
            internal Func<EventTag> Tag = Speech.CurrentTag;
        }

        /// <summary>
        /// The silence key (Session 36): every queued line goes quiet. Prompts
        /// and dialogue are dropped (the history never holds them); game events
        /// stay in order, marked, and reach the history unspoken as each one
        /// becomes ready. Returns how many lines were silenced.
        /// </summary>
        internal static int SilenceQueue()
        {
            int n = _preInitBuffer.Count;
            _preInitBuffer.Clear();
            if (Instance == null) return n;

            var node = Instance._queue.First;
            while (node != null)
            {
                var next = node.Next;
                var e = node.Value;
                if (!e.Silenced)
                {
                    n++;
                    if (e.IsPrompt || e.IsDialogue) Instance._queue.Remove(node);
                    else e.Silenced = true;
                }
                node = next;
            }
            Instance._timer = 0f;
            return n;
        }

        // ==================================================================
        // SESSION 46 - THE KILLING BLOW KEEPS WHAT IT CUTS. (0.7.452.)
        //
        // When the scale reaches its stop the damage line cuts everything
        // still waiting (0.7.120: the swing-by-swing of a turn that is now
        // over). That used ClearQueue, which throws the lines away. Found
        // with the test driver on a LOST battle: the last two attacks, a
        // death and its bone were never said and were nowhere to be found
        // afterwards - "QUEUE: cleared 3 message(s) on transition".
        //
        // Zamar: "It should appear in the log so they can scroll back and
        // read exactly what killed them, but Im fine with it getting
        // stomped." So they are still not spoken, and they are kept: each
        // waiting game event is composed now, in order, and written to the
        // review history and the log ahead of the damage line that follows
        // this call. Prompts and dialogue are dropped, as the silence key
        // drops them. A line still waiting on an event that has not
        // finished gives what has happened so far if it can (its Partial),
        // and is otherwise dropped: nothing here may wait, because the
        // candle and the character's line are next.
        // ==================================================================
        public static void QuietQueueIntoHistory()
        {
            _preInitBuffer.Clear();
            if (Instance == null) return;

            int kept = 0, dropped = 0;
            foreach (var e in Instance._queue)
            {
                if (e == null || e.IsPrompt || e.IsDialogue) { dropped++; continue; }

                bool finished = true;
                if (e.Ready != null && Time.unscaledTime < e.ReadyDeadline)
                    try { finished = e.Ready(); } catch { finished = true; }

                string text = null;
                try
                {
                    var source = finished ? e.Provider : e.Partial;
                    text = source != null ? source() : null;
                }
                catch { text = null; }
                if (string.IsNullOrEmpty(text)) { dropped++; continue; }

                bool drop, speak, keep;
                EventSettings.Decide(Speech.Resolve(e.Tag), true, out drop, out speak, out keep);
                if (!keep || drop) { dropped++; continue; }

                ReviewHistory.Add(text);
                _log?.LogInfo($"IKMA HISTORY (not spoken): {text}");
                kept++;
            }

            Instance._queue.Clear();
            Instance._timer = 0f;
            Instance._holdoffActive = false;
            Speech.ForgetOutsideSpeech();
            if (kept + dropped > 0)
                _log?.LogInfo($"IKMA QUEUE: the final blow cut {kept + dropped} waiting line(s); {kept} kept in the review history.");
        }

        // Silenced lines at the front of the queue go straight to the history,
        // in order, each at the moment it would have been composed.
        private void FlushSilenced()
        {
            var node = _queue.First;
            while (node != null)
            {
                var next = node.Next;
                var e = node.Value;
                if (e.Silenced)
                {
                    if (e.Ready != null && Time.unscaledTime < e.ReadyDeadline)
                    {
                        bool ready = true;
                        try { ready = e.Ready(); } catch { ready = true; }
                        if (!ready) return;   // keep the order: wait for it
                    }
                    _queue.Remove(node);
                    string text = null;
                    try { text = e.Provider != null ? e.Provider() : null; } catch { }
                    if (!string.IsNullOrEmpty(text))
                    {
                        bool drop, speak, keep;
                        EventSettings.Decide(Speech.Resolve(e.Tag), true, out drop, out speak, out keep);
                        if (keep && !drop)
                        {
                            ReviewHistory.Add(text);
                            // Session 46: and in the log file, so a bug report
                            // holds what the history holds.
                            _log?.LogInfo($"IKMA HISTORY (not spoken): {text}");
                        }
                    }
                }
                node = next;
            }
        }

        // Normal interval between queued announcements.
        private const float ANNOUNCE_INTERVAL = 0.5f;

        // How long to hold off after an interrupt=true call from HotkeyManager.
        // Long enough for NVDA to finish speaking the play-flow line before we
        // resume the announcement queue.
        private const float INTERRUPT_HOLDOFF = 1.2f;

        public static void Init(ManualLogSource log)
        {
            _log = log;
        }

        private void Awake()
        {
            Instance = this;

            // Drain anything that was enqueued before Awake() ran.
            while (_preInitBuffer.Count > 0)
                _queue.AddLast(_preInitBuffer.Dequeue());
        }

        /// <summary>
        /// Queue a fixed line at Info priority. Text is final at enqueue time.
        /// </summary>
        public static void Enqueue(string message) => Add(message, isAction: false);

        /// <summary>
        /// Queue an Info line that must not be spoken for at least
        /// <paramref name="delaySeconds"/>. Everything behind it in the queue
        /// waits too, so ordering is preserved. (Session 11.)
        /// </summary>
        public static void EnqueueDelayed(string message, float delaySeconds)
            => Add(message, isAction: false, delaySeconds: delaySeconds);

        /// <summary>
        /// Queue a fixed line at Action priority: the game state has moved, and
        /// whatever is currently being read aloud is now describing the past.
        /// (Session 10.) Zamar raised this three separate ways in one test —
        /// the sacrifice line waiting politely behind "Slot 3: Squirrel, 0/1.",
        /// a bell press whose entire combat resolved under an item description,
        /// a card played while the slot read still trailed. They were one bug.
        /// An announcement about a board that no longer exists is not merely
        /// slow, it is false, and it puts a stale model in the player's head.
        /// </summary>
        public static void EnqueueAction(string message) => Add(message, isAction: true);

        /// <summary>
        /// A line the player asked for, put at the FRONT of the queue.
        /// (0.7.436, Session 43.) Zamar pressed Shift+R after the enemy totem
        /// line and the answer waited behind "Your turn", the upcoming queue
        /// and the hand read; asked whether a Shift+R answer should jump ahead
        /// of queued lines: "Jump ahead."
        ///
        /// It goes ahead of what is WAITING. It does not cut the line being
        /// spoken, and nothing behind it is dropped - those lines follow it.
        /// </summary>
        public static void EnqueueActionFirst(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            var entry = new Entry { Provider = () => message, IsAction = true };
            if (Instance == null) { _preInitBuffer.Enqueue(entry); return; }

            Instance.CancelHoldoffInternal();
            _log?.LogInfo($"IKMA QUEUE (action, ahead of {Instance._queue.Count} waiting): {message}");
            Instance._queue.AddFirst(entry);
        }

        /// <summary>
        /// Queue an Info line to be composed when it reaches the front of the
        /// queue. Use for reads of live game state that may not be settled yet
        /// at the moment of enqueue. Provider returning null/empty is skipped.
        /// (Session 9.)
        /// </summary>
        public static void EnqueueDeferred(Func<string> provider) => AddDeferred(provider, isAction: false);

        /// <summary>Action-priority counterpart of EnqueueDeferred.</summary>
        public static void EnqueueActionDeferred(Func<string> provider) => AddDeferred(provider, isAction: true);

        /// <summary>
        /// Action-priority deferred line that may not compose for at least
        /// <paramref name="delaySeconds"/>. (Session 13.)
        ///
        /// A deferred line normally composes the moment it reaches the front of
        /// the queue, which is usually what you want — but not when the thing it
        /// needs to fold in has not happened yet. The sacrifice line is the
        /// case: it waits for the bone the sacrifice grants, and if the queue is
        /// empty it composes before the game has granted it.
        /// </summary>
        public static void EnqueueActionDeferredDelayed(Func<string> provider, float delaySeconds)
            => AddDeferred(provider, isAction: true, label: null, delaySeconds: delaySeconds);

        /// <summary>
        /// Queue a line that INSTRUCTS: the game is waiting on the player and
        /// this says what to press. (Session 14.)
        ///
        /// Two properties, and they are separate things. It interrupts like an
        /// Action line, because the player has just committed to something and
        /// whatever browse read is still in the air belongs to the moment
        /// before. And it is INSERTED ahead of pending commentary rather than
        /// appended, because commentary is orientation the player can ask for
        /// again at any time, while a prompt has a window.
        ///
        /// From the 0.7.22 log: Squirrel was clicked and "Playing Squirrel.
        /// Choose a slot..." was heard only after "Your turn.", "Upcoming: 3
        /// cards..." and "Your hand: 4 cards..." had all drained. Zamar pressed
        /// Backspace three times in the gap. The prompt was correct, deferred
        /// and self-withdrawing by then — none of which helps when it is simply
        /// behind three reads he did not need at that moment.
        ///
        /// It never overtakes a combat result. That rule is untouched: the
        /// insert lands after the last Action or Dialogue entry in the queue,
        /// so a death or a damage line already waiting still goes first.
        ///
        /// Always deferred. An instructing line must be true when it is SPOKEN,
        /// which is a demand a reporting line never makes.
        /// </summary>
        public static void EnqueuePrompt(Func<string> provider)
            => AddDeferred(provider, isAction: true, isPrompt: true);

        /// <summary>
        /// A prompt that keeps its queue position but holds back for
        /// <paramref name="delaySeconds"/> before it may speak. (Session 14.)
        ///
        /// For a prompt that follows immediately on the player's own action,
        /// where the mod has just said what they did. The item-target prompt is
        /// the case: "Using Scissors." goes out off the keypress and this line
        /// arrives an instant later, and a Prompt interrupts outside speech by
        /// design — so without a hold it cuts off the sentence explaining why it
        /// is being asked.
        /// </summary>
        public static void EnqueuePromptDelayed(float delaySeconds, Func<string> provider)
            => AddDeferred(provider, isAction: true, label: null,
                           delaySeconds: delaySeconds, isDialogue: false, isPrompt: true);

        /// <summary>
        /// Queue a line spoken by a character. Interrupts speech from outside
        /// the queue, and goes ahead of pending commentary without overtaking a
        /// combat result. <paramref name="delaySeconds"/> holds it back for the
        /// character's voice sting. (Session 13.)
        /// </summary>
        public static void EnqueueActionWhenReady(Func<bool> ready, Func<string> provider,
                                                  float maxWaitSeconds, string label,
                                                  Func<string> partial = null)
        {
            if (ready == null || provider == null) return;
            var entry = new Entry
            {
                Provider      = provider,
                IsAction      = true,
                Ready         = ready,
                ReadyDeadline = Time.unscaledTime + maxWaitSeconds,
                Partial       = partial,
                MaxWait       = maxWaitSeconds,
                HeldSince     = Time.unscaledTime,
            };
            if (Instance == null) { _preInitBuffer.Enqueue(entry); return; }
            Instance.CancelHoldoffInternal();
            if (label != null) _log?.LogInfo($"IKMA QUEUE (action): {label}");
            Instance._queue.AddLast(entry);
        }

        public static void EnqueueDialogue(string message, float delaySeconds)
            => Add(message, isAction: false, delaySeconds: delaySeconds, isDialogue: true);

        private static void Add(string message, bool isAction, float delaySeconds = 0f,
                                bool isDialogue = false)
        {
            if (string.IsNullOrEmpty(message)) return;
            AddDeferred(() => message, isAction, message, delaySeconds, isDialogue);
        }

        private static void AddDeferred(Func<string> provider, bool isAction,
                                        string label = null, float delaySeconds = 0f,
                                        bool isDialogue = false, bool isPrompt = false)
        {
            if (provider == null) return;

            var entry = new Entry
            {
                Provider   = provider,
                IsAction   = isAction,
                IsDialogue = isDialogue,
                IsPrompt   = isPrompt,
                NotBefore  = delaySeconds > 0f ? Time.unscaledTime + delaySeconds : 0f,
            };
            string what = label ?? "[deferred read]";
            string tag  = isPrompt ? "prompt" : (isDialogue ? "dialogue" : (isAction ? "action" : "info"));

            if (Instance == null)
            {
                _log?.LogInfo($"IKMA QUEUE: buffered pre-init ({tag}): {what}");
                _preInitBuffer.Enqueue(entry);
                return;
            }

            // An Action line cancels any holdoff imposed by play-flow speech, so
            // it speaks on the next frame instead of waiting the interrupt out.
            if (isAction || isDialogue) Instance.CancelHoldoffInternal();

            // ==================================================================
            // A LINE THAT SAYS NOTHING IS NOT A LOG LINE. (0.7.315.)
            //
            // A deferred entry has no text yet — it composes when it reaches
            // the front of the queue — so this printed "[deferred read]".
            // Zamar's 0.7.313 log had SEVENTY-SEVEN of them in 1,176 lines,
            // one in every fifteen, every one of them content-free. The text
            // they stand for is printed a moment later by the SPEAK line
            // anyway, so nothing is lost by not printing the placeholder.
            //
            // A LABELLED enqueue still logs: that is a caller saying what it
            // queued, which is worth reading. The log is part of the mod, and
            // this pass is Zamar's: "We've gotta keep that log as cleaned up
            // as possible."
            // ==================================================================
            if (label != null) _log?.LogInfo($"IKMA QUEUE ({tag}): {what}");

            if (isDialogue || isPrompt) Instance.InsertAheadOfCommentary(entry);
            else                        Instance._queue.AddLast(entry);
        }

        /// <summary>
        /// Place a line ahead of pending commentary, but never ahead of a
        /// combat result. (Zamar's call, Session 13, from a test log where
        /// Leshy's refusal queued behind a card cost read.)
        ///
        /// The rule: insert immediately after the last Action, Dialogue or
        /// Prompt entry in the queue, or at the very front if there is none.
        /// That gets the line ahead of every scales read and piece of
        /// commentary waiting behind it, while keeping the hard rule intact —
        /// a death or a damage line already in the queue is never overtaken.
        ///
        /// Session 14: prompts use this too, which is why it is no longer
        /// called InsertDialogue. Two kinds of line have a window that
        /// commentary does not — a character speaking over their own voice
        /// sting, and an instruction the player is waiting on — and they want
        /// the same position for the same reason.
        ///
        /// Entries inserted this way keep their arrival order among
        /// themselves, since each new one lands after the last of them.
        ///
        /// Note the limit, so it is not mistaken for a bug later: this reorders
        /// what is still WAITING. A line already being spoken is out of the
        /// queue and is not cut short, because a queued line never interrupts
        /// another queued line.
        /// </summary>
        private void InsertAheadOfCommentary(Entry entry)
        {
            var node = _queue.Last;
            while (node != null &&
                   !(node.Value.IsAction || node.Value.IsDialogue || node.Value.IsPrompt))
                node = node.Previous;

            if (node == null) _queue.AddFirst(entry);
            else              _queue.AddAfter(node, entry);
        }

        /// <summary>
        /// Drop a pending interrupt holdoff without queueing anything. Pressing
        /// the bell calls this: the player has committed to advancing the turn,
        /// so nothing they were browsing a moment ago should delay the combat
        /// that follows. (Session 10.)
        /// </summary>
        public static void CancelHoldoff()
        {
            Instance?.CancelHoldoffInternal();
        }

        private void CancelHoldoffInternal()
        {
            if (!_holdoffActive) return;
            _holdoffActive = false;
            _timer = 0f;
        }

        /// <summary>
        /// True if there are announcements still waiting to be spoken (or buffered
        /// pre-init). Used by patches that need to decide whether a player-facing
        /// prompt should cut in immediately (queue empty -- nothing to preserve
        /// ordering against) or take its place in line after pending combat
        /// announcements (queue non-empty -- e.g. a sacrifice's "X is sacrificed."
        /// / "N bone(s) received." should be heard before "Choose a slot...").
        /// (Session 7, note 2.)
        /// </summary>
        /// <summary>
        /// A character's line is queued and not yet spoken - usually held for
        /// its voice sting (Session 37, note D8: Space waits for it).
        /// </summary>
        internal static bool DialoguePending
        {
            get
            {
                if (Instance == null) return false;
                foreach (var e in Instance._queue)
                    if (e.IsDialogue && !e.Silenced) return true;
                return false;
            }
        }

        public static bool HasPendingMessages
        {
            get
            {
                if (Instance != null && Instance._queue.Count > 0) return true;
                if (Instance == null && _preInitBuffer.Count > 0) return true;
                return false;
            }
        }

        /// <summary>
        /// Drop everything still waiting to be spoken. Used on transitions —
        /// leaving a menu screen, changing scene — where queued lines describe
        /// a context the player has already left. (Session 9.)
        /// </summary>
        /// <summary>
        /// Drop only the COMMENTARY still waiting — the plain Info lines. Action,
        /// Dialogue and Prompt entries are left exactly where they are.
        /// (Session 17.)
        ///
        /// WHY THIS EXISTS RATHER THAN A ClearQueue CALL. Arriving somewhere new
        /// has to be able to cut through chatter about where the player just
        /// was: Zamar pressed H while standing, walked back, and the map opened
        /// underneath a help line that was still draining. Speak(interrupt) cuts
        /// what is IN THE AIR, and the next queued Info line then talks over the
        /// arrival anyway.
        ///
        /// ClearQueue would do it and would also throw away combat results, which
        /// this project treats as never stompable. So this drops the one tier that
        /// is safe to lose — commentary about a screen the player has left — and
        /// touches nothing else.
        /// </summary>
        public static void DropCommentary(string reason)
        {
            if (Instance == null) return;

            int dropped = 0;
            var node = Instance._queue.First;
            while (node != null)
            {
                var next = node.Next;
                var e = node.Value;
                if (!e.IsAction && !e.IsDialogue && !e.IsPrompt)
                {
                    Instance._queue.Remove(node);
                    dropped++;
                }
                node = next;
            }

            if (dropped > 0)
                _log?.LogInfo($"IKMA QUEUE: dropped {dropped} commentary line(s) — {reason}.");
        }

        // ==================================================================
        // HOLD EVERYTHING QUEUED FOR A MOMENT. (0.7.364.)
        //
        // Zamar, on the Grizzly phase: the game plays a sudden loud distorted
        // sound, and "Anything after Volume Warning scales hit five should be
        // delayed by 3 seconds." Nothing queued is spoken until the hold runs
        // out; order is kept, nothing is dropped.
        // ==================================================================
        private static float _queueHeldUntil = -1f;

        public static void HoldQueueFor(float seconds, string reason)
        {
            float until = Time.unscaledTime + seconds;
            if (until > _queueHeldUntil) _queueHeldUntil = until;
            _log?.LogInfo($"IKMA QUEUE: held for {seconds:0.0}s — {reason}.");
        }

        public static void ClearQueue()
        {
            _preInitBuffer.Clear();
            if (Instance != null)
            {
                int dropped = Instance._queue.Count;
                Instance._queue.Clear();
                Instance._timer = 0f;
                Instance._holdoffActive = false;
                Speech.ForgetOutsideSpeech();
                if (dropped > 0)
                    _log?.LogInfo($"IKMA QUEUE: cleared {dropped} message(s) on transition.");
            }
        }

        /// <summary>
        /// The queue-timing half of an interrupt from outside the queue. Resets
        /// the drip timer so queued announcements hold off until play-flow
        /// speech has finished. Called only by Speech.NoteInterrupted, which
        /// owns the other half (marking the speech in the air as outside
        /// speech) and has already checked Instance and ignored the queue's own
        /// lines. (M7 refactor 2, 0.7.365.)
        /// </summary>
        internal static void HoldOffAfterInterrupt()
        {
            if (Instance == null) return;

            // 0.7.148 — BROWSE SPEECH NO LONGER OUTRUNS A COMBAT RESULT.
            //
            // Zamar, mashing Tab through a sacrifice: "X is sacrificed." arrived
            // seconds late, sometimes after the whole sequence was over. Every
            // Tab press landed here and re-armed a 1.2-second holdoff, so a
            // result line already sitting in the queue was pushed back by 1.2
            // seconds per keypress and a fast player could hold it off forever.
            //
            // Enqueue already cancels the holdoff when an Action arrives — that
            // rule was only ever half-applied, because it ran once at arrival
            // and nothing re-applied it to the presses that came after. The
            // holdoff protects play-flow speech from COMMENTARY; a combat
            // result is not commentary, and is entitled to cut in (Update sets
            // interrupt for exactly this case). So while one is waiting, browse
            // speech gets no holdoff at all.
            //
            // Speech.NoteInterrupted has still marked outside speech: the
            // waiting line should still interrupt the browse read it is about
            // to speak over.
            if (Instance.HasPendingResult())
            {
                Instance._holdoffActive = false;
                Instance._timer = 0f;
                return;
            }

            if (Instance._timer < INTERRUPT_HOLDOFF)
            {
                Instance._timer = INTERRUPT_HOLDOFF;
                Instance._holdoffActive = true;
            }
        }

        /// <summary>
        /// True if an Action or Dialogue line is waiting to be spoken. Checked
        /// across the whole queue rather than just the head: an Action queued
        /// behind commentary is still a result the player is owed, and holding
        /// the commentary holds the result with it.
        /// </summary>
        private bool HasPendingResult()
        {
            for (var node = _queue.First; node != null; node = node.Next)
                if (node.Value.IsAction || node.Value.IsDialogue) return true;
            return false;
        }

        private void Update()
        {
            // Before the early return, deliberately: the pump has housekeeping
            // to do whether or not the announcer has anything queued. It writes
            // out the log lines the speech thread produced (BepInEx listeners
            // are not documented thread-safe and this project treats the log as
            // a player-facing artifact) and reports a stall. (Session 15.)
            SpeechPump.Tick();
            PlayConfirmHold.Tick();   // 0.7.439 - every frame, ahead of the empty-queue return

            if (_queue.Count == 0) return;

            // 0.7.447 - A HELD LINE'S CLOCK STOPS WHILE THE GAME WAITS ON THE
            // PLAYER. Zamar's 0.7.446 log: the Hydra's five-strike summary was
            // given fifteen seconds; the Pack Mule died on strike two, the
            // Prospector's line held the attack until Space, the fifteen
            // seconds ran out during that wait, and the summary went out with
            // only the Mule in it. The Wolf died on strike three and nothing
            // was ever said about it. The deadline is there for a coroutine
            // that never finishes, not for a player who has not pressed Space
            // yet. HoldingUntilInput is a plain bool - no Singleton is asked.
            if (DialogueAdvancer.HoldingUntilInput)
            {
                float now = Time.unscaledTime;
                foreach (var held in _queue)
                {
                    if (held.Ready == null || held.MaxWait <= 0f) continue;
                    if (now - held.HeldSince > MaxHeldSeconds) continue;
                    if (held.ReadyDeadline < now + held.MaxWait)
                        held.ReadyDeadline = now + held.MaxWait;
                }
            }

            FlushSilenced();   // Session 36
            if (_queue.Count == 0) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            // 0.7.364 — a whole-queue hold. See HoldQueueFor.
            if (Time.unscaledTime < _queueHeldUntil) return;

            // A delayed line holds the whole queue rather than being skipped —
            // letting later lines overtake it would reorder the commentary, and
            // the reason this one is waiting is that something audible is
            // happening right now.
            var head = _queue.First?.Value;
            if (head != null && head.NotBefore > 0f && Time.unscaledTime < head.NotBefore) return;

            // WAIT FOR THE RESULT, THEN CUT THE PROMPT (0.7.349, 0.7.355) -
            // the rule and its history live in Speech.QueueHeadMayGo.
            if (head != null && !Speech.QueueHeadMayGo(head.IsAction, head.IsDialogue, head.IsPrompt)) return;

            if (head != null && head.Ready != null && Time.unscaledTime < head.ReadyDeadline)
            {
                bool ready = true;
                try { ready = head.Ready(); } catch { ready = true; }
                if (!ready)
                {
                    // 0.7.437 - A CHARACTER'S LINE DOES NOT WAIT BEHIND A HELD
                    // LINE. THIS WAS A SOFTLOCK. Session 43, at the Angler:
                    // Zamar's Mantis broke a Bait Bucket and "GO FISH." opened
                    // mid-attack. The multi-strike summary held the head of the
                    // queue until the attack finished; the attack could not
                    // finish until the conversation was advanced; Space is
                    // refused while a character's line is still unspoken; and
                    // that line sat behind the summary. He had to left click.
                    //
                    // A held line waits on the game. A conversation makes the
                    // game wait on the player. So the conversation goes first,
                    // and the held line keeps its place for everything else.
                    //
                    // 0.7.447 - unless the held line can say what has happened
                    // so far. Then THAT goes first, and the character after it.
                    if (FlushPartialBeforeDialogue()) return;
                    SpeakDialogueBehindHeldHead();
                    return;
                }
            }

            _holdoffActive = false;

            var entry = head;
            _queue.RemoveFirst();

            string message;
            try
            {
                message = entry?.Provider != null ? entry.Provider() : null;
            }
            catch (System.Exception e)
            {
                // A deferred read touching game state that has since torn down
                // must never take the announcer down with it.
                _log?.LogWarning($"IKMA QUEUE: deferred message failed: {e.Message}");
                return;
            }

            // Nothing worth saying — drop it and let the next item come up on
            // the following frame rather than burning the interval on silence.
            if (string.IsNullOrEmpty(message)) return;

            // Whether this line cuts what is playing, and what it arms
            // afterwards, is Speech's decision. See Speech.HandOverFromQueue.
            // Session 37: a line the player's event settings keep quiet costs
            // no interval - the next line comes up on the following frame.
            if (!Speech.HandOverFromQueue(message, entry.IsAction, entry.IsDialogue, entry.IsPrompt, entry.Tag))
                return;

            _timer = ANNOUNCE_INTERVAL;
        }

        /// <summary>
        /// The speech thread is a background thread, so the game can always
        /// exit regardless. This is tidiness rather than a requirement: it gives
        /// the pump a chance to finish the line in its hand. (Session 15.)
        /// </summary>
        /// <summary>
        /// The first character line waiting behind a held head, spoken now.
        /// (0.7.437.) See the note in Update.
        /// </summary>
        private void SpeakDialogueBehindHeldHead()
        {
            LinkedListNode<Entry> node = _queue.First != null ? _queue.First.Next : null;
            while (node != null && !(node.Value.IsDialogue && !node.Value.Silenced))
                node = node.Next;
            if (node == null) return;

            var entry = node.Value;
            if (entry.NotBefore > 0f && Time.unscaledTime < entry.NotBefore) return;
            if (!Speech.QueueHeadMayGo(false, true, false)) return;

            _queue.Remove(node);
            _log?.LogInfo("IKMA QUEUE: a character's line goes ahead of a held line - the game is waiting on the conversation.");

            string message;
            try { message = entry.Provider != null ? entry.Provider() : null; }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA QUEUE: deferred message failed: {e.Message}");
                return;
            }
            if (string.IsNullOrEmpty(message)) return;

            if (!Speech.HandOverFromQueue(message, entry.IsAction, entry.IsDialogue, entry.IsPrompt, entry.Tag))
                return;
            _timer = ANNOUNCE_INTERVAL;
        }

        // A held line's clock is not pushed back for longer than this in
        // all, so a conversation flag that never clears cannot hold the queue.
        private const float MaxHeldSeconds = 120f;

        /// <summary>
        /// 0.7.447 - a character has started talking while the line at the head
        /// of the queue is still waiting on its event. If that line can say
        /// what has happened so far, it does so now, and what is left of it
        /// moves to just behind the character's line. Zamar, Session 45, asked
        /// where the Mule's death and the pack belong when "DAAAG NAB IT!"
        /// opens mid-attack: "Before the boss line", the rest of the attack
        /// after Space.
        ///
        /// Lines queued between the two (the pack's cards) keep their place,
        /// so they are also said before the character. Space stays refused
        /// until the character's line has been handed over
        /// (DialoguePending), and nothing ahead of it waits on the attack any
        /// more, so this cannot bring back the 0.7.437 softlock.
        /// </summary>
        private bool FlushPartialBeforeDialogue()
        {
            var headNode = _queue.First;
            var head = headNode != null ? headNode.Value : null;
            if (head == null || head.Partial == null || head.Silenced) return false;

            LinkedListNode<Entry> node = headNode.Next;
            while (node != null && !(node.Value.IsDialogue && !node.Value.Silenced))
                node = node.Next;
            if (node == null) return false;

            _queue.RemoveFirst();
            _queue.AddAfter(node, headNode);
            if (head.ReadyDeadline < Time.unscaledTime + head.MaxWait)
                head.ReadyDeadline = Time.unscaledTime + head.MaxWait;

            string message = null;
            try { message = head.Partial(); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA QUEUE: partial line failed: {e.Message}");
            }

            _log?.LogInfo(string.IsNullOrEmpty(message)
                ? "IKMA QUEUE: a character speaks mid-event - the held line had nothing to say yet and now waits behind the conversation."
                : "IKMA QUEUE: a character speaks mid-event - what has happened so far is said first; the rest waits behind the conversation.");

            if (string.IsNullOrEmpty(message)) return true;
            if (!Speech.HandOverFromQueue(message, true, false, false, head.Tag)) return true;
            _timer = ANNOUNCE_INTERVAL;
            return true;
        }

        private void OnApplicationQuit()
        {
            SpeechPump.Shutdown();
        }
    }
}
