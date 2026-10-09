// ProspectorNarrator.cs
//
// -----------------------------------------------------------------------------
// THE PROSPECTOR BOSS. (Session 19, v0.7.161.)
//
// EVERY SPOKEN LINE IN THIS FILE IS A PLACEHOLDER. Zamar asked for them
// explicitly: "I won't know what needs to be read in those 11 things until
// seeing it in game. Can you add place holders for now."
//
// So these are deliberately FLAT — subject, verb, object, no styling, no
// personality, no attempt at a voice. They exist to make the fight audible
// once, so he can decide from experience which moments deserve a line and what
// each should say. NONE of this wording is proposed. The whole file is expected
// to be rewritten or partly deleted after one playtest.
//
// This is the single exception to the wording-authority rule, and it is his
// exception, not one Claude took. Anything that survives the playtest gets its
// real words from him before it ships.
//
// -----------------------------------------------------------------------------
// WHAT THE PROSPECTOR ACTUALLY DOES, read from IL, not from recollection.
// Sources: dumps/dump_prospector.txt, dump_basecalls.txt, dump_prospector_il.txt.
//
// PHASE 2 DESTROYS THE PLAYER'S ENTIRE BOARD. StartNewPhaseSequence runs
// StrikeGoldSequence, then Opponent.ClearQueue, then
// Opponent.ReplaceBlueprint("ProspectorBossP2"). StrikeGoldSequence walks every
// player slot holding a card and calls PickAxeSlam.StrikeCardSlot, which runs
// PlayableCard.Die on the card and then creates a "GoldNugget" in the empty
// slot. Board gone, queue gone, opponent's deck swapped — and the only audible
// content in the whole sequence is three barks that state none of it.
//
// THE GAME SPEAKS FOR ITSELF at: "THAR'S GOLD IN THEM CARDS!",
// "G-G-GOLD! I'VE STRUCK GOLD!", "N-... NO GOLD?", "Git 'em!", the
// ProspectorMuleKilled dialogue, and both intro dialogues. All route through
// TextDisplayer, which IKMA already narrates. NOTHING HERE REPEATS THEM.
//
// -----------------------------------------------------------------------------
// WHY EVERY LINE IS DEFERRED.
//
// A Harmony prefix on a coroutine fires when the ENUMERATOR IS CREATED, not
// when the sequence runs. Reading the board there reports the board as it was
// before anything happened. So every line below is composed at SPEAK time
// through Speech.Commentary / Speech.Result (deferred), which asks
// the game its state when the words are about to leave.
//
// The one place that inverts: StrikeCardSlot has to capture the DOOMED CARD'S
// NAME in the prefix, because by speak time that card is dead and the slot
// holds a Gold Nugget. So the name is captured early and the CONSEQUENCE is
// read late. That split is deliberate and is the only correct order available.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;

namespace IKMA
{
    internal static class ProspectorNarrator
    {
        /// <summary>
        /// Is the current opponent the Prospector? Every announcement in this
        /// file is gated on this. PickAxeSlam and PackMule are his alone today,
        /// but a mod or a later act could reuse either, and a Prospector line
        /// firing in someone else's fight would be a false statement about the
        /// game — the exact defect class the narration rules call worse than
        /// silence.
        /// </summary>
        private static bool IsProspectorFight()
        {
            try
            {
                if (TurnManager.Instance?.Opponent is ProspectorBossOpponent) return true;

                // 0.7.211 - AND WHEN LESHY IS WEARING HIS MASK.
                //
                // docs/COVERAGE_KM.md flagged this to be checked and it was
                // real: in the final fight Leshy takes the Prospector's mask
                // off the orbiter and instantiates the SAME PickAxeSlam
                // behaviour, which strikes every occupied player slot
                // (LeshyBossOpponent.ActivateProspector). The opponent object
                // is LeshyBossOpponent throughout, so this gate answered false
                // and the entire board wipe - the loudest thing that happens
                // in the mask's turn - was silent.
                //
                // The gate now asks the same question every other actor line
                // asks: WHO IS BEHIND THE FACE. That keeps the original
                // purpose intact (a Prospector line must never fire in a
                // fight the Prospector is not in) while letting it fire in
                // the fight where he is, wearing someone else's head.
                return BossNarrator.ActorName() == "The Prospector";
            }
            catch { return false; }
        }

        // ----------------------------------------------------------------------
        // ----------------------------------------------------------------------
        // THE PHASE-2 TRANSITION. SILENT, BY HIS DECISION.
        //
        // 0.7.161 spoke "The Prospector is starting a new phase." He heard it in
        // play and cut it: "This whole line can be removed."
        //
        // It is not needed because the game announces the phase ITSELF, loudly,
        // in character — "THAR'S GOLD IN THEM CARDS!" lands one line later. The
        // placeholder was IKMA explaining what the Prospector was about to say.
        //
        // What this still does is OPEN THE WIPE WINDOW, which is the whole
        // mechanism below. Do not delete the method with the line.
        // ----------------------------------------------------------------------
        internal static void AnnouncePhaseChange()
        {
            if (!IsProspectorFight()) return;
            OpenWipeWindow("the Prospector's own phase 2");
        }

        /// <summary>
        /// Open the wipe window and count the blows that are about to land.
        /// (0.7.218 — extracted so Leshy's Prospector mask can open it too.)
        ///
        /// Both callers create their enumerator BEFORE the first strike, while
        /// the board is still intact, which is the only moment the count can be
        /// taken. <paramref name="cause"/> only goes to the log, so the two
        /// paths can be told apart in a playtest.
        /// </summary>
        internal static void OpenWipeWindow(string cause)
        {
            if (!IsProspectorFight()) return;

            ReleaseHeldForWipe();   // 0.7.342 — nothing held from an earlier window is lost

            _cardsStruck     = 0;
            _wipeInProgress  = true;
            _wipeLineSpoken  = false;
            _bonesDuringWipe = 0;
            _lastStrikeAt    = -1f;

            // HOW MANY THE PICKAXE WILL HIT. StrikeGoldSequence walks every
            // player slot and strikes the ones holding a card, so the count is
            // knowable before the first blow - and this prefix runs before any
            // of it, while the board is still intact.
            _expectedStrikes = 0;
            try
            {
                var slots = Singleton<BoardManager>.Instance?.PlayerSlotsCopy;
                if (slots != null)
                    foreach (var slot in slots)
                        if (slot != null && slot.Card != null) _expectedStrikes++;
            }
            catch { _expectedStrikes = 0; }

            Plugin.Log?.LogInfo(
                $"IKMA PROSPECTOR: wipe window open ({cause}), " +
                $"{_expectedStrikes} strike(s) expected.");
        }

        // ----------------------------------------------------------------------
        // THE WIPE WINDOW. (0.7.168.)
        //
        // WHAT WENT WRONG IN 0.7.167, from his log and his report — "this
        // sequence was close but not quite right. Things were getting stomped.
        // I never heard Gold Nugget is played in Slot 3."
        //
        //   IKMA SPEAK: Cuckoo dies. Gold Nugget is played in Slot 3.
        //   IKMA SPEAK: 1 bone received.
        //   IKMA SPEAK: Boulder dies. Gold Nugget is played in Slot 4.
        //   IKMA SPEAK: 1 bone received.
        //   IKMA SPEAK: 1 bone received.
        //
        // The single wipe line was correct and the GENERAL machinery narrated
        // the same event underneath it anyway. Every card destroyed by the
        // pickaxe is an ordinary death as far as the death patch is concerned,
        // and every Gold Nugget is an ordinary arrival, so IKMA produced the
        // per-slot commentary he had explicitly replaced — plus a bone line per
        // death, three of them, one at a time.
        //
        // THIS IS REGRESSION PATTERN 4 (announcing one event from two paths),
        // and the lesson is sharper than the existing note: replacing a
        // specific announcement with a summary DOES NOT remove the general
        // announcements underneath it. The old lines have to be suppressed
        // deliberately, and the summary is not a summary until they are.
        //
        // SUPPRESSED, NOT DELETED. Deaths and arrivals during the window are
        // logged exactly as before and only the SPEECH is withheld, so the log
        // still shows every card. Bones are ACCUMULATED rather than dropped —
        // his words, "All the bone receives should be grouped together for this
        // sequence" — because bones are a resource the player spends and losing
        // the count would be losing information, not noise.
        // ----------------------------------------------------------------------
        private static bool _wipeInProgress;
        private static int _bonesDuringWipe;

        /// <summary>
        /// True while the pickaxe sequence is running. The general death,
        /// arrival and bone paths check this and stay quiet.
        /// </summary>
        internal static bool WipeInProgress => _wipeInProgress;

        /// <summary>
        /// Called by the AddBones patch instead of announcing. Returns true if
        /// the bones were taken for the grouped line.
        /// </summary>
        // ------------------------------------------------------------------
        // THE WIPE LINE FIRES ON A DEBOUNCE, NOT AT ClearQueue. (0.7.173.)
        //
        // Zamar: "This final action line should play automatically after the
        // I've Struck Gold line, not wait until you press space."
        //
        // WHY IT WAITED. The line was emitted from the ClearQueue prefix, and
        // ClearQueue is the step AFTER StrikeGoldSequence in the phase
        // coroutine. StrikeGoldSequence ends on a ShowUntilInput bark, which
        // holds until the player presses Space — so the summary of what just
        // happened to his board sat behind his own keypress.
        //
        // GENERAL: a line that describes event A must not be emitted from a
        // hook that runs at event B, however convenient B is to patch. The
        // ordering that looks right in the source is the ordering of the
        // COROUTINE, and a coroutine can stop and wait for a human in between.
        //
        // The debounce: every strike stamps the clock, and Tick speaks once the
        // strikes have stopped coming. That lands the line right after the last
        // pickaxe blow, which is where the game plays its own "I'VE STRUCK
        // GOLD!" — so his ear gets the bark and then the consequence, with no
        // press in between.
        // ------------------------------------------------------------------
        // COUNT THE SLOTS, DO NOT TIME THE STRIKES. (0.7.174.)
        //
        // 0.7.173 spoke the wipe line after 0.75s of quiet. His log:
        //
        //   pickaxe strikes slot 1
        //   board wipe - 1 card(s) struck     <- fired after ONE strike
        //   pickaxe strikes slot 3            <- and now nothing is suppressed
        //   pickaxe strikes slot 4
        //   board wipe - 2 card(s) struck     <- spoken a SECOND time
        //
        // Each strike switches the camera and plays an animation, so the gaps
        // between them are far longer than the debounce. The line fired early,
        // and - the real damage - speaking it also CLOSED the suppression
        // window, so the remaining deaths and the bone lines came back. Three
        // separate complaints from him, one cause.
        //
        // TWO LESSONS, AND THE SECOND IS THE GENERAL ONE:
        //
        //   1. A timing constant guessed against an animation is a guess. The
        //      game already knows how many cards it will strike - every player
        //      slot holding a card when the phase begins - so ASK, and speak
        //      when that many have been struck.
        //   2. DO NOT TIE A SUPPRESSION WINDOW TO AN ANNOUNCEMENT'S TIMING.
        //      They answer different questions: "has enough happened to
        //      describe it" and "is the sequence still running". The window now
        //      closes at ClearQueue, which is the game's own end of the phase.
        private static int  _expectedStrikes;
        private static bool _wipeLineSpoken;

        // Fallback only: if the expected count is ever wrong - a card dies
        // between the count and the strikes, a mod adds a slot - this speaks
        // rather than staying silent. Long enough to clear the animations that
        // broke the 0.75s version.
        private const float STRIKE_QUIET_SECONDS = 4.0f;
        private static float _lastStrikeAt = -1f;

        /// <summary>
        /// Driven from HotkeyManager.Update, beside DialogueAdvancer.Tick.
        /// Only the fallback path needs a tick; the normal path speaks as soon
        /// as the last expected strike lands.
        /// </summary>
        internal static void Tick()
        {
            if (!_wipeInProgress || _wipeLineSpoken || _lastStrikeAt < 0f) return;
            if (UnityEngine.Time.unscaledTime - _lastStrikeAt < STRIKE_QUIET_SECONDS) return;

            Plugin.Log?.LogInfo(
                "IKMA PROSPECTOR: wipe line on the QUIET FALLBACK - expected " +
                $"{_expectedStrikes} strikes, saw {_cardsStruck}. Worth checking why.");
            SpeakWipeLine();
        }

        internal static bool TryFoldBones(int amount)
        {
            if (!_wipeInProgress || amount <= 0) return false;
            _bonesDuringWipe += amount;
            Plugin.Log?.LogInfo(
                $"IKMA PROSPECTOR: {amount} bone(s) folded into the wipe total " +
                $"(now {_bonesDuringWipe}).");
            return true;
        }

        // ----------------------------------------------------------------------
        // ITEMS 3, 4, 5 and 11 — THE BOARD WIPE, AS ONE LINE. (0.7.162.)
        //
        // ZAMAR'S WORDING, AND HIS DECISION. Session 19, after hearing the
        // per-slot version in play:
        //
        //   "For the pickaxe boardwipe that should be all one line.
        //    'The Prospector uses his pickaxe to strike all of your creatures,
        //     turning them into Gold Nuggets.'"
        //
        // So the per-slot strike lines and the post-wipe board summary are both
        // GONE. Three cards on the board produced four lines where one was
        // wanted, and 0.7.161 shipped both competing versions on purpose so he
        // could hear them against each other. He picked. This is that line.
        //
        // AND IT FIXES A REAL BUG, not just a length complaint. The per-slot
        // line read the slot's replacement at SPEAK time and still got there
        // too early — his log has
        //
        //   "The pickaxe destroys your Wolf in Slot 1. Wolf takes its place."
        //
        // three times over, naming each dying card as its own replacement,
        // because StrikeCardSlot had not yet created the Gold Nugget when the
        // announcer composed. A deferred read is not automatically a late
        // enough read: "compose at speak time" only helps when speak time is
        // actually after the thing being described. The single line describes
        // the whole sequence and so cannot be caught mid-sequence.
        //
        // WHY A COUNT IS STILL KEPT. The line claims Gold Nuggets replaced the
        // player's creatures, and that is only true if there were creatures to
        // strike. On an empty board StrikeGoldSequence strikes nothing and the
        // game itself says "N-... NO GOLD?" — so IKMA says nothing rather than
        // announcing a transformation that did not happen.
        // ----------------------------------------------------------------------
        private static int _cardsStruck;

        // ----------------------------------------------------------------------
        // WHAT THE NEXT BOARD DIFF MUST NOT SAY. (0.7.340.)
        //
        // Zamar's 0.7.339 log, after the phase-two wipe:
        //   "Your Gold Nugget is played in slot 1. Your Gold Nugget is played
        //    in slot 2. Your Gold Nugget is played in slot 4. Enemy Bloodhound
        //    is queued targeting slot 3."
        // "I did not play gold nuggets here. They were already called out ...
        //  the line 'The Prospector clears his queue, Enemy Bloodhound is
        //  queued targeting slot 3.' should have played instead."
        //
        // So the slots the pickaxe struck are held for the next diff, which
        // drops a card of yours arriving in one of them, and the queue being
        // cleared in the same phase leads that diff with his words.
        // ----------------------------------------------------------------------
        private static readonly HashSet<int> _struckSlots = new HashSet<int>();
        private static string _diffLead;

        /// <summary>Taken by BoardWatcher.ComposeDiff: the player slots the
        /// wipe turned into nuggets. Empty after one take.</summary>
        internal static HashSet<int> TakeStruckSlots()
        {
            var taken = new HashSet<int>(_struckSlots);
            _struckSlots.Clear();
            return taken;
        }

        /// <summary>Taken by BoardWatcher.ComposeDiff: his lead-in for the
        /// diff that follows the phase-two queue clear, or null.</summary>
        internal static string TakeDiffLead()
        {
            string lead = _diffLead;
            _diffLead = null;
            return lead;
        }

        internal static void NotePickaxeStrike(CardSlot slot)
        {
            if (!IsProspectorFight() || slot == null) return;

            string doomed = null;
            int slotNumber = -1;
            try
            {
                doomed = CardReader.CardName(slot.Card?.Info);
                slotNumber = slot.Index + 1;
            }
            catch { }

            // Logged per strike even though it is spoken as one line, so the
            // log still shows exactly which slots were hit and what was in
            // them. The log is the diagnostic record; the line is the product.
            Plugin.Log?.LogInfo(
                $"IKMA PROSPECTOR: pickaxe strikes slot {slotNumber}, " +
                $"card at strike time = '{doomed ?? "none"}'.");

            if (doomed != null) _cardsStruck++;
            // 0.7.340 — the nugget that replaces this card is covered by the
            // wipe line, so the next board diff must not call it "played".
            if (doomed != null && slotNumber > 0) _struckSlots.Add(slotNumber - 1);
            _lastStrikeAt = UnityEngine.Time.unscaledTime;

            // The moment the last expected blow lands, describe the whole
            // thing. No waiting, no timer, and the window stays open.
            if (!_wipeLineSpoken && _expectedStrikes > 0 && _cardsStruck >= _expectedStrikes)
                SpeakWipeLine();
        }

        /// <summary>
        /// Spoken once, after every strike in the sequence has run. Silent if
        /// the pickaxe found nothing to destroy.
        /// </summary>
        /// <summary>
        /// The one line describing the whole wipe. Speaks at most once per
        /// phase, and does NOT close the suppression window — see the note
        /// above for why those were wrongly the same thing.
        /// </summary>
        // ----------------------------------------------------------------------
        // WHAT THE WIPE CAUSED WAITS FOR THE WIPE LINE. (0.7.342.)
        //
        // His 0.7.341 log: the pickaxe killed Ouroboros, Unkillable put it back
        // in his hand, and "Ouroboros's Unkillable ability triggers: An
        // Ouroboros is added to your hand." was spoken BEFORE "The Prospector
        // uses his pickaxe to strike all of your creatures". The wipe line
        // waits for the last blow; the card came back after the first. A hand
        // arrival during an unsaid wipe is held here and queued straight after
        // the wipe line.
        // ----------------------------------------------------------------------
        private static readonly List<Func<string>> _heldForWipe = new List<Func<string>>();

        internal static void EnqueueAfterWipeLine(Func<string> provider)
        {
            if (provider == null) return;
            if (_wipeInProgress && !_wipeLineSpoken)
            {
                _heldForWipe.Add(provider);
                Plugin.Log?.LogInfo("IKMA PROSPECTOR: a hand arrival is held until the wipe line is said.");
                return;
            }
            using (Speech.Event(EventKind.Bosses)) Speech.Commentary(provider);
        }

        private static void ReleaseHeldForWipe()
        {
            if (_heldForWipe.Count == 0) return;
            foreach (var p in _heldForWipe) using (Speech.Event(EventKind.Bosses)) Speech.Commentary(p);
            _heldForWipe.Clear();
        }

        internal static void SpeakWipeLine()
        {
            if (_wipeLineSpoken) return;
            _wipeLineSpoken = true;
            _lastStrikeAt   = -1f;

            if (_cardsStruck == 0)
            {
                Plugin.Log?.LogInfo(
                    "IKMA PROSPECTOR: board wipe struck nothing — no line, the game says " +
                    "\"N-... NO GOLD?\" itself.");
                ReleaseHeldForWipe();
                return;
            }

            Plugin.Log?.LogInfo($"IKMA PROSPECTOR: board wipe — {_cardsStruck} card(s) struck.");

            using (Speech.Event(EventKind.Bosses)) Speech.Result(
                Vocabulary.ProspectorBoss.ProspectorUsesHisPickaxe);
            ReleaseHeldForWipe();
        }

        /// <summary>
        /// End of the phase. Closes the suppression window and pays out the
        /// bones every one of those deaths produced, as the single line he
        /// asked for: "we do need 1 line for how many bones total are
        /// received."
        /// </summary>
        internal static void CloseWipeWindow()
        {
            if (!_wipeInProgress) return;

            // Belt and braces: if the expected count came up short, the line
            // has still not been spoken and this is the last chance.
            SpeakWipeLine();

            _wipeInProgress = false;

            int bones = _bonesDuringWipe;
            _bonesDuringWipe = 0;
            if (bones > 0)
            {
                // Session 29, his pick: "Received 3 bones.", like every bone gain.
                using (Speech.Event(EventKind.Bones, EventSource.CurrentPlayer)) Speech.Commentary(Vocabulary.ReceivedBonesLine(bones));
            }
        }

        // ITEMS 6 and 7 — the queue was emptied, and his deck was replaced.
        //
        // Both are invisible to the board read and both change what the player
        // should plan for. The queue one matters most: a player who has been
        // tracking what is coming next has been tracking something that no
        // longer exists.
        //
        // GATED ON THE PROSPECTOR because Opponent.ClearQueue and
        // ReplaceBlueprint are general members that other fights use.
        // ----------------------------------------------------------------------
        internal static void AnnounceQueueCleared(bool afterWipe, int queuedBefore)
        {
            if (!IsProspectorFight()) return;

            Plugin.Log?.LogInfo(
                $"IKMA PROSPECTOR: ClearQueue (after wipe: {afterWipe}, {queuedBefore} queued before).");

            // 0.7.340 — HIS LINE, Session 26: "The Prospector clears his
            // queue, Enemy Bloodhound is queued targeting slot 3." Only at the
            // phase-two clear (the one that follows the wipe), and only when
            // there was something in the queue to clear. The end-of-fight
            // clear below stays silent, as he ruled at 0.7.206.
            if (afterWipe && queuedBefore > 0)
                _diffLead = Vocabulary.ProspectorBoss.ProspectorClearsHisQueue;

            // THE LINE IS GONE. (0.7.206.) Zamar, at the end of the fight:
            // "That queue cleared line needs to be removed."
            //
            // It was approved at 0.7.173 and heard in context at 0.7.205, which
            // is the only test that counts. By the time it speaks, the boss is
            // dead, the mask is off and the reward chest is opening — the
            // player is not tracking a queue any more, so a line about the
            // queue lands in the middle of the payoff saying nothing useful.
            //
            // The log line stays. Knowing ClearQueue fired is worth having if
            // this ever needs revisiting; saying it out loud is not.
        }

        // ----------------------------------------------------------------------
        // THE DECK SWAP IS NOT ANNOUNCED. (0.7.173.)
        //
        // Zamar: "that deck callout should not happen. That's hidden to players
        // usually."
        //
        // THE PARITY TEST, and this is the cleanest example of it the project
        // has: the test is not "can we reach the data", it is "can a sighted
        // player see it". ReplaceBlueprint is reachable, unambiguous and easy
        // to narrate — and a sighted player has no idea it happened. Speaking
        // it would have handed a blind player information the game withholds
        // from everyone, which is not accessibility.
        //
        // Still LOGGED, because knowing the phase actually changed is useful
        // when reading a playtest back.
        // ----------------------------------------------------------------------
        internal static void AnnounceBlueprintReplaced(string blueprintId)
        {
            if (!IsProspectorFight()) return;
            Plugin.Log?.LogInfo(
                $"IKMA PROSPECTOR: ReplaceBlueprint id='{blueprintId}' — not spoken, " +
                "hidden from sighted players too.");
        }

        // ----------------------------------------------------------------------
        // ITEM 8 — a queued Bloodhound gained a sigil.
        //
        // ModifyQueuedCard adds ONE random ability the card does not already
        // have, then sets forceEmissivePortrait and an emission colour. THE GLOW
        // IS THE SIGHTED PLAYER'S CUE, and there is no audio equivalent today.
        //
        // WHY A PREFIX/POSTFIX PAIR RATHER THAN READING THE MOD. The added
        // ability could be read off the CardModificationInfo, but diffing the
        // card's OWN ability list before and after is the game's answer to the
        // question actually being asked — "what does this card have that it did
        // not" — and it stays correct if the game ever adds a second source of
        // abilities to the same call.
        //
        // Not a coroutine, so the postfix genuinely runs after the work.
        // ----------------------------------------------------------------------
        private static readonly List<Ability> _abilitiesBefore = new List<Ability>();

        internal static void CaptureAbilitiesBefore(PlayableCard card)
        {
            _abilitiesBefore.Clear();
            if (card == null) return;
            try
            {
                var abilities = card.Info?.Abilities;
                if (abilities != null) _abilitiesBefore.AddRange(abilities);
                var mods = card.TemporaryMods;
                if (mods != null)
                    foreach (var m in mods)
                        if (m?.abilities != null) _abilitiesBefore.AddRange(m.abilities);
            }
            catch { }
        }

        internal static void AnnounceQueuedCardModified(PlayableCard card)
        {
            if (!IsProspectorFight() || card == null) return;

            // NOT SPOKEN. (0.7.173.) Zamar, after hearing it: "remove this
            // line, don't need this callout."
            //
            // The sigil is on the card, and the card is in the queue — so the
            // queue read (O) and the card read already carry it whenever he
            // asks. An unrequested line for something already answerable on
            // demand is noise during the busiest moment of the fight.
            //
            // The diff is still COMPUTED and logged: it is the only record that
            // the hound buff fired at all, and T2_/T3_HOUND_ABILITIES cannot be
            // read any other way with the game closed.
            var gained = new List<Ability>();
            string cardName = null;

            try
            {
                cardName = CardReader.CardName(card);

                var after = new List<Ability>();
                var abilities = card.Info?.Abilities;
                if (abilities != null) after.AddRange(abilities);
                var mods = card.TemporaryMods;
                if (mods != null)
                    foreach (var m in mods)
                        if (m?.abilities != null) after.AddRange(m.abilities);

                var remaining = new List<Ability>(_abilitiesBefore);
                foreach (var a in after)
                {
                    if (remaining.Contains(a)) remaining.Remove(a);
                    else gained.Add(a);
                }
            }
            catch { }

            if (gained.Count == 0) return;

            var names = new List<string>();
            foreach (var a in gained)
            {
                string n = null;
                try { n = CardReader.GetAbilityName(a); } catch { }
                if (!string.IsNullOrEmpty(n)) names.Add(n);
            }

            Plugin.Log?.LogInfo(
                $"IKMA PROSPECTOR (not spoken): queued '{cardName}' gained " +
                $"{string.Join(", ", names.ToArray())}.");
        }

        // ----------------------------------------------------------------------
        // ITEM 9 — a Pack Mule resolved on the board with a pack on its back.
        // ----------------------------------------------------------------------
        internal static void AnnounceMuleResolved()
        {
            if (!IsProspectorFight()) return;

            // NOT SPOKEN HERE ANY MORE. (0.7.174.) Zamar: "Remove the
            // placeholder line. Add after 'Enemy Pack Mule moves down to slot
            // 1, it is carrying a pack of cards.'"
            //
            // The pack is now part of the board-diff line that announces the
            // mule arriving — see BoardWatcher.PackNote. One card, one clause,
            // instead of an arrival line and then a second sentence about the
            // same card.
            //
            // The patch stays because the log entry is the only marker of when
            // the mule actually resolved, which is worth having when reading a
            // fight back.
            Plugin.Log?.LogInfo(
                "IKMA PROSPECTOR: PackMule.OnResolveOnBoard (pack noted on the arrival line).");
        }

        // ----------------------------------------------------------------------
        // ITEM 10 — a Pack Mule died and its pack went into the player's hand.
        //
        // ONE LINE, BY HIS DECISION. Session 19: "Make the pack mule into one
        // row." GenerateCardPack builds a Squirrel plus three or four random
        // cards and SpawnAndOpenPack feeds every one of them to
        // CardSpawner.SpawnCardToHand, so the untouched behaviour would be four
        // or five separate announcements for what the player experiences as one
        // event.
        //
        // THE NAMING IS NOT DONE HERE. BossNarrator patches SpawnCardToHand
        // itself — that route was entirely unpatched and also carries the Smoke
        // after a candle goes out — so all this does is open the batching
        // window and let the general path collect the cards. Nothing about the
        // pack is special-cased, and the mule gets no wording of its own.
        //
        // FIVE SECONDS is chosen to outlast the pack sequence, which tweens the
        // pack, plays an "open" animation and switches the camera once per
        // card. The earlier version of this read the whole hand after four
        // seconds; naming just the arrivals is both shorter and more useful,
        // since the rest of the hand has not changed.
        // ----------------------------------------------------------------------
        internal static void AnnounceMulePackOpened()
        {
            if (!IsProspectorFight()) return;

            Plugin.Log?.LogInfo("IKMA PROSPECTOR: PackMule.OnDie — batching the pack into one line.");
            BossNarrator.BeginHandBatch(5.0f);
        }
    }

    // -------------------------------------------------------------------------
    // Patch classes. All registered through Plugin.TryPatch, never PatchAll —
    // this whole file is new and unproven, and one wrong member name inside
    // PatchAll would silently kill every patch registered after it.
    // -------------------------------------------------------------------------

    // ITEM 2, 6, 7. StartNewPhaseSequence is [OVERRIDE of Part1BossOpponent] and
    // does NOT call base (dump_basecalls.txt), so this must be patched on
    // ProspectorBossOpponent's own declaration. A patch on Part1BossOpponent
    // would register cleanly and never fire.
    public class ProspectorBossOpponent_StartNewPhaseSequence_Patch
    {
        static void Prefix() => ProspectorNarrator.AnnouncePhaseChange();
    }

    // Counts strikes for the single wipe line. Speaks nothing itself.
    public class PickAxeSlam_StrikeCardSlot_Patch
    {
        static void Prefix(CardSlot slot) => ProspectorNarrator.NotePickaxeStrike(slot);
    }

    // THE WIPE LINE AND THE QUEUE LINE.
    //
    // ClearQueue's enumerator is created AFTER StrikeGoldSequence has finished
    // running, so this is the first hook in the sequence that is reliably past
    // the board wipe — which makes it the right place to hang the post-wipe
    // summary as well as the queue line. Both compose at speak time, so they
    // land in the announcer behind the per-slot strike lines rather than ahead
    // of them.
    public class Opponent_ClearQueue_Patch
    {
        static void Prefix(Opponent __instance)
        {
            // 0.7.340 — read before the window closes and before the game
            // empties the queue.
            bool afterWipe = ProspectorNarrator.WipeInProgress;
            int queued = 0;
            try { queued = __instance?.Queue?.Count ?? 0; } catch { }

            // The wipe line no longer fires from here — it is on a debounce off
            // the last pickaxe strike, because this hook sits behind a
            // ShowUntilInput bark that waits for the player. (0.7.173.)
            // ClearQueue is the game's own end of the phase sequence, so this
            // is where the window closes and the bone total is paid out. The
            // wipe LINE has normally already been spoken, on the last strike.
            ProspectorNarrator.CloseWipeWindow();
            ProspectorNarrator.AnnounceQueueCleared(afterWipe, queued);
        }
    }

    // ITEM 7. blueprintId is logged, never spoken — it is an internal id.
    public class Opponent_ReplaceBlueprint_Patch
    {
        static void Prefix(string blueprintId)
            => ProspectorNarrator.AnnounceBlueprintReplaced(blueprintId);
    }

    // ITEM 8. Prefix captures, postfix diffs. Not a coroutine, so the postfix
    // really does run after the ability has been added.
    public class ProspectorBossOpponent_ModifyQueuedCard_Patch
    {
        static void Prefix(PlayableCard card) => ProspectorNarrator.CaptureAbilitiesBefore(card);
        static void Postfix(PlayableCard card) => ProspectorNarrator.AnnounceQueuedCardModified(card);
    }

    // ITEM 9.
    public class PackMule_OnResolveOnBoard_Patch
    {
        static void Prefix() => ProspectorNarrator.AnnounceMuleResolved();
    }

    // ITEM 10.
    public class PackMule_OnDie_Patch
    {
        static void Prefix() => ProspectorNarrator.AnnounceMulePackOpened();
    }
}

// ProspectorNarrator.cs
