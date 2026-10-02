// BoardWatcher.cs
using System;
using System.Collections.Generic;
using System.Text;
using DiskCardGame;

namespace IKMA
{
    // -------------------------------------------------------------------------
    // The board diff watcher. Master Handoff B8, and the top narration gap on
    // the project.
    //
    // THE PROBLEM. Seven Act 1 sigils move a card from one slot to another —
    // Strafe, StrafePush, SquirrelStrafe, SkeletonStrafe, WhackAMole,
    // MoveBeside, GuardDog — and IKMA announces none of them. The player's
    // model of the board goes silently stale, and every announcement afterwards
    // sounds confident while being wrong. It has already cost the project its
    // first false bug report: a card appeared on the board that Zamar had no
    // way to know about, and he asked whether the GAME was broken. It was not.
    // The mod's silence was.
    //
    // THE MECHANISM. Snapshot both sides, diff at the next turn boundary,
    // announce what changed. One mechanism covers all seven movers rather than
    // seven ability-specific patches, and it reports what the board DID rather
    // than what a sigil is supposed to do — so it stays true for a mover this
    // code has never heard of.
    //
    // WHAT THIS BUILD SPEAKS, AND WHAT IT ONLY LOGS.
    //
    // Movement is spoken. A card that is on the board in both snapshots and
    // sits in a different slot has moved, and nothing else in IKMA says a word
    // about it today, so there is no line to duplicate.
    //
    // Cards that APPEARED or VANISHED are logged and not spoken, on purpose.
    // Those two cases overlap with narration that already exists — the
    // ChooseSlot patch, SlotArrival.Describe, the death lines — and in this
    // project a duplicate line is a bug, not noise. The overlap cannot be
    // settled by reading the code, because it depends on ordering inside a
    // live turn. So this build writes what it WOULD have said to the log, the
    // next playtest log answers whether each one would have doubled a line
    // already spoken, and the build after that turns them on with evidence
    // instead of with an argument. Same method that settled the menu hitch.
    //
    // WHAT IT DELIBERATELY DOES NOT SAY. Not which DIRECTION a card moved.
    // Slot numbers are what IKMA's whole vocabulary is built on and both slot
    // numbers are true whichever way the board is laid out; "moved left" would
    // be IKMA asserting that slot 1 is the leftmost, which nothing has
    // confirmed. Naming the direction a SIGIL moves is a separate open item
    // with a dump behind it, and it is not this one.
    // -------------------------------------------------------------------------
    internal static class BoardWatcher
    {
        // One card's position at snapshot time. The card reference is the
        // identity — names collide constantly in this game, and two Squirrels
        // swapping slots must not read as one Squirrel moving twice.
        private struct Placement
        {
            internal PlayableCard Card;
            internal int SlotIndex;   // zero-based; spoken as SlotIndex + 1
            internal bool PlayerSide;
        }

        private static readonly List<Placement> _previous = new List<Placement>();
        private static bool _haveSnapshot;

        // 0.7.361 — THE GRIZZLY WIPE SPEAKS FOR THE ENEMY SIDE. Zamar: the
        // eyes-turn-red line "handles all the cards leaving so they dont need
        // to be individually called out", and "Every opposing slot on the
        // board is filled with Grizzlies..." "should cover all the rest,
        // instead of individual callouts." One-shot: the next diff drops
        // enemy departures, enemy arrivals and queue arrivals, and says his
        // line in their place. Your own side still reports.
        private static bool _grizzlyWipe;
        internal static void NoteGrizzlyWipe() => _grizzlyWipe = true;

        // TurnManager.SetupPhase(EncounterData) — private IEnumerator; a
        // prefix fires at creation, which is the battle's start.
        internal static void OnBattleSetup() => ResetForNewBattle();

        // THE OPPONENT'S QUEUE AT THE LAST SNAPSHOT. (0.7.43.)
        //
        // Zamar, 0.7.42 playtest: "when the queued cards move it should more
        // clearly state 'Enemy Rabbit moves down to slot 3'." A card arriving on
        // the enemy board is one of two completely different events — the
        // opponent playing something new, or a card that has been sitting in the
        // queue all along finally coming down — and "is in slot 3" read the same
        // for both. The player has already heard the queue read; this is the
        // line that connects the two.
        //
        // Card identity, never the name: two Coyotes in the queue must not make
        // one arrival read as either of them. Same argument as the movement
        // diff. If the card was not in the previous queue snapshot, the wording
        // does not change — an unconfirmed origin gets the neutral line.
        private static readonly List<PlayableCard> _previousQueue = new List<PlayableCard>();

        // Cards IKMA has already narrated the arrival or departure of since the
        // last diff. Parallel to DamageDeathMerger's reasoning: these are Unity
        // objects that may be destroyed between insert and lookup, and Unity's
        // == and Equals overloads make hashed lookup on a destroyed object
        // unreliable, so this is a plain list scanned by reference over a
        // handful of entries.
        private static readonly List<PlayableCard> _announced = new List<PlayableCard>();

        // THE SLOT THE CARD WAS IN WHEN IT WAS ANNOUNCED. (0.7.177.)
        //
        // Parallel to _announced, same reason the merger uses parallel lists:
        // these are Unity objects that may be destroyed between insert and
        // lookup, and hashed lookup on a destroyed object is unreliable.
        private static readonly List<int> _announcedSlots = new List<int>();

        private const int MAX_ANNOUNCED = 24;

        // ----------------------------------------------------------------------
        // Called by whatever already spoke about a card arriving or leaving, so
        // the diff does not say it a second time.
        // ----------------------------------------------------------------------
        /// <summary>
        /// Note a card as already announced AT A SLOT IT HAS NOT REACHED YET.
        /// </summary>
        /// <remarks>
        /// 0.7.263, for the Angler's hook. The plain overload below reads
        /// <c>card.Slot.Index</c>, which is right for every mover that notes
        /// itself AFTER it has moved. The hook cannot: its narration hangs on a
        /// prefix of <c>FishHookGrab.PullHook</c>, which fires at enumerator
        /// creation, three quarters of a second before
        /// <c>AssignCardToSlot</c> carries the card across. Reading the slot
        /// there would record where the card still is.
        ///
        /// It would in fact have suppressed correctly by accident — a slot and
        /// its opposing slot share an index, so the old value matches the new
        /// one — and that is exactly the kind of coincidence that holds until
        /// somebody changes a board layout and nobody knows why the line came
        /// back. The caller knows the destination, so it says it.
        /// </remarks>
        internal static void NoteAnnouncedAtSlot(PlayableCard card, int slotIndex)
        {
            if (card == null) return;
            // 0.7.360 — a second note moves the slot. See NoteAnnounced.
            for (int i = 0; i < _announced.Count; i++)
                if (ReferenceEquals(_announced[i], card))
                {
                    if (i < _announcedSlots.Count) _announcedSlots[i] = slotIndex;
                    return;
                }

            if (_announced.Count >= MAX_ANNOUNCED)
            {
                _announced.RemoveAt(0);
                if (_announcedSlots.Count > 0) _announcedSlots.RemoveAt(0);
            }
            _announced.Add(card);
            _announcedSlots.Add(slotIndex);

            try
            {
                Plugin.Log?.LogInfo(
                    $"IKMA BOARDDIFF: noted '{CardReader.CardName(card.Info)}' as already " +
                    $"announced at slot {slotIndex + 1} — a destination it has not reached yet " +
                    $"({_announced.Count} held).");
            }
            catch { }
        }

        // ==================================================================
        // A MOVER WHOSE OWN LINE IS STILL COMING. (0.7.344.)
        //
        // His 0.7.342 log:
        //   "Enemy Pack Mule moves right, from slot 1 to slot 2. ..."
        //   "Pack Mule's Sprinter ability triggers: it moves right to slot 2."
        // "Only the ability trigger line should have happened." The Sprinter
        // line notes the card as announced when it is SPOKEN, and the turn's
        // board diff composed first. So the Strafe patch registers the mover
        // at TRIGGER time; the diff leaves its move to that line.
        // ==================================================================
        private static readonly List<PlayableCard> _pendingMovers = new List<PlayableCard>();
        private static readonly List<float> _pendingMoversAt = new List<float>();

        internal static void NoteMoverPending(PlayableCard card)
        {
            if (card == null) return;
            for (int i = 0; i < _pendingMovers.Count; i++)
                if (ReferenceEquals(_pendingMovers[i], card)) { _pendingMoversAt[i] = UnityEngine.Time.unscaledTime; return; }
            _pendingMovers.Add(card);
            _pendingMoversAt.Add(UnityEngine.Time.unscaledTime);
        }

        internal static void ClearMoverPending(PlayableCard card)
        {
            for (int i = 0; i < _pendingMovers.Count; i++)
                if (ReferenceEquals(_pendingMovers[i], card)) { _pendingMovers.RemoveAt(i); _pendingMoversAt.RemoveAt(i); return; }
        }

        private static bool IsPendingMover(PlayableCard card)
        {
            float now = UnityEngine.Time.unscaledTime;
            for (int i = 0; i < _pendingMovers.Count; i++)
            {
                // A registration the line never cleared expires, so it can
                // never hide a later, unrelated move.
                if (now - _pendingMoversAt[i] > 10f) { _pendingMovers.RemoveAt(i); _pendingMoversAt.RemoveAt(i); i--; continue; }
                if (ReferenceEquals(_pendingMovers[i], card)) return true;
            }
            return false;
        }

        internal static void NoteAnnounced(PlayableCard card)
        {
            if (card == null) return;

            // 0.7.360 — A SECOND NOTE MOVES THE SLOT. Zamar heard "Elk's
            // Sprinter ability triggers: it moves right to slot 4." and then
            // "Enemy Elk moves right, from slot 3 to slot 4." Fledgling had
            // noted the Elk at slot 3; Sprinter's note returned early because
            // the card was already held, so the held slot stayed 3 and the
            // slot-aware check below let the move through.
            for (int i = 0; i < _announced.Count; i++)
                if (ReferenceEquals(_announced[i], card))
                {
                    int again = -1;
                    try { again = card.Slot != null ? card.Slot.Index : -1; } catch { }
                    if (i < _announcedSlots.Count) _announcedSlots[i] = again;
                    try
                    {
                        Plugin.Log?.LogInfo(
                            $"IKMA BOARDDIFF: '{CardReader.CardName(card.Info)}' noted again — held slot now {again + 1}.");
                    }
                    catch { }
                    return;
                }

            if (_announced.Count >= MAX_ANNOUNCED)
            {
                _announced.RemoveAt(0);
                if (_announcedSlots.Count > 0) _announcedSlots.RemoveAt(0);
            }
            _announced.Add(card);

            int notedSlot = -1;
            try { notedSlot = card.Slot != null ? card.Slot.Index : -1; }
            catch { notedSlot = -1; }
            _announcedSlots.Add(notedSlot);

            // 0.7.39 log: "Your Mantis God is in slot 3" was spoken by the diff
            // for a card Zamar had just played and heard announced. Something is
            // not reaching this list, or is being cleared before the diff runs.
            // Logged so the next run says which, rather than being guessed at.
            try
            {
                Plugin.Log?.LogInfo(
                    $"IKMA BOARDDIFF: noted '{CardReader.CardName(card.Info)}' as already announced " +
                    $"({_announced.Count} held).");
            }
            catch { }
        }

        /// <summary>
        /// Which slot this card was in when something announced it, or -1 if
        /// nothing did. (0.7.177.)
        /// </summary>
        private static int AnnouncedInSlot(PlayableCard card)
        {
            for (int i = 0; i < _announced.Count; i++)
                if (ReferenceEquals(_announced[i], card))
                    return i < _announcedSlots.Count ? _announcedSlots[i] : -1;
            return -1;
        }

        private static bool WasAnnounced(PlayableCard card)
        {
            for (int i = 0; i < _announced.Count; i++)
                if (ReferenceEquals(_announced[i], card)) return true;
            return false;
        }

        // ----------------------------------------------------------------------
        // Forget everything. A snapshot that survives into a different battle
        // describes a board that no longer exists.
        //
        // Note that a stale snapshot could never produce a false MOVEMENT line
        // even without this: movement requires the same card reference in both
        // snapshots, and a new encounter builds new cards. That is a large part
        // of why movement is the half this build speaks.
        // ----------------------------------------------------------------------
        private static void SnapshotQueue()
        {
            _previousQueue.Clear();
            try
            {
                var queue = TurnManager.Instance?.Opponent?.Queue;
                if (queue == null) return;
                for (int i = 0; i < queue.Count; i++)
                    if (queue[i] != null) _previousQueue.Add(queue[i]);
            }
            catch
            {
                // No snapshot means the neutral wording, never a wrong one.
                _previousQueue.Clear();
            }
        }

        private static bool WasInPreviousQueue(PlayableCard card)
        {
            if (card == null) return false;
            for (int i = 0; i < _previousQueue.Count; i++)
                if (ReferenceEquals(_previousQueue[i], card)) return true;
            return false;
        }

        // 0.7.360 — ONE BATTLE'S SNAPSHOT NEVER REACHES THE NEXT. Zamar, on
        // the first turn of a Totem battle: "Your Dire Wolf has left slot 2.
        // Your Frozen Opossum is played in slot 2. Enemy Snowy Fir is played
        // in slot 4..." — "The dire wolf one was wrong and the others were not
        // played, they just started there." Reset ran only on a scene load,
        // and Act 1 battles share one scene, so the first diff compared
        // against the previous fight's last board. Now reset at every battle's
        // SetupPhase; the first diff after it stores the board silently.
        internal static void ResetForNewBattle()
        {
            Plugin.Log?.LogInfo("IKMA BOARDDIFF: new battle — snapshot cleared; the first board is stored, not announced.");
            Reset();
        }

        internal static void Reset()
        {
            _previousQueue.Clear();
            _previous.Clear();
            _announced.Clear();
            _grizzlyWipe = false;
            _haveSnapshot = false;
            _orderProbed  = false;
            _indexRunsLeftToRight = false;
        }

        // ----------------------------------------------------------------------
        // Does slot index order run left to right? Logged once per battle.
        // (Session 14.)
        //
        // IKMA now says two things about position in two different vocabularies.
        // The movement line says slot numbers — "moves from slot 2 to slot 3" —
        // and the card read says a direction — "Moving left" — off
        // Strafe.movingLeft. Both are true on their own. A player hearing them
        // together will naturally combine them, and that inference is only safe
        // if slot 1 really is the leftmost.
        //
        // Nothing has ever confirmed that. It is the assumption this file
        // deliberately refused to make when it chose slot numbers over "moved
        // left", and refusing it was right — but the answer is available for
        // the asking. BoardManager.SlotIsLeftOfSlot is PUBLIC and is the game's
        // own question, so ask it rather than reasoning about it.
        //
        // Nothing is spoken either way. This writes one line to the log, and
        // the next playtest settles whether the movement line can name a
        // direction as well as a slot.
        // ----------------------------------------------------------------------
        private static bool _orderProbed;

        // Set by the probe from the game's own SlotIsLeftOfSlot. Direction is
        // named ONLY when this battle's probe confirmed the mapping — never
        // assumed, and never carried over from a previous encounter.
        private static bool _indexRunsLeftToRight;

        private static void ProbeSlotOrder(BoardManager bm)
        {
            if (_orderProbed) return;

            var row = bm?.PlayerSlotsCopy;
            if (row == null || row.Count < 2) return;

            _orderProbed = true;
            try
            {
                bool firstIsLeft = bm.SlotIsLeftOfSlot(row[0], row[1], row);
                _indexRunsLeftToRight = firstIsLeft;
                Plugin.Log?.LogInfo(firstIsLeft
                    ? "IKMA SLOT ORDER: slot 1 is LEFT of slot 2 — index order runs left to right."
                    : "IKMA SLOT ORDER: slot 1 is NOT left of slot 2 — index order does NOT run left to right.");
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA SLOT ORDER: probe failed: {e.Message}");
            }
        }

        // ----------------------------------------------------------------------
        // The provider handed to Speech.Commentary at each turn
        // boundary. Composed at SPEAK time, never at enqueue time.
        //
        // Deferring is not a formality here. A turn prefix fires when the
        // coroutine's enumerator is created, and the moves this is watching for
        // resolve in the turn-end sequence just before it. Composing at speak
        // time puts the read after everything queued ahead of it has drained,
        // which is the settle window this project keeps paying for and never
        // regretting. It also means the snapshot and the sentence describing it
        // are taken at the same instant, so they cannot disagree.
        //
        // Returns null when nothing moved. The announcer drops an empty
        // provider silently, so a quiet turn costs the player nothing.
        // ----------------------------------------------------------------------
        // ----------------------------------------------------------------------
        // Run a diff NOW rather than waiting for the turn boundary. (Session 14.)
        //
        // An item kill was reading a full turn late, which is how the ordering
        // problem above became a suspected gameplay bug: by the time IKMA
        // mentioned the Porcupine, three other things had happened.
        //
        // `PlayableCard.Die` does not appear to fire for a Scissors destroy —
        // that is an old mystery and is NOT guessed at here. What is certain is
        // that the card leaves the board, and this watcher can see that. So the
        // item path asks for a diff the moment the item resolves.
        //
        // Deferred and delayed: the item animates before the card goes, so a
        // diff composed on the keypress would see a board that has not changed
        // yet. Settle first — the standing rule, and the settle costs nothing.
        // ----------------------------------------------------------------------
        internal const float ITEM_SETTLE_SECONDS = 0.9f;

        internal static void AnnounceAfterItem()
        {
            using (Speech.Event(EventKind.Death)) Speech.Result(ComposeDiff, ITEM_SETTLE_SECONDS);
        }

        /// <summary>One compact line naming every card and slot in a snapshot.</summary>
        private static string Describe(List<Placement> set)
        {
            if (set == null || set.Count == 0) return "(empty)";

            var parts = new List<string>();
            foreach (var p in set)
            {
                string n = NameOf(p.Card) ?? "?";
                parts.Add($"{(p.PlayerSide ? "your" : "enemy")} {n}@{p.SlotIndex + 1}");
            }
            return string.Join(", ", parts.ToArray());
        }

        internal static string ComposeDiff()
        {
            var current = new List<Placement>();
            if (!Capture(current))
            {
                // No board to read. Leave the old snapshot alone rather than
                // replacing it with an empty one, which would make every card
                // read as vanished on the next pass.
                return null;
            }

            ProbeSlotOrder(Singleton<BoardManager>.Instance);

            if (!_haveSnapshot)
            {
                Store(current);
                return null;
            }

            var moves    = new List<string>();
            var appeared = new List<string>();
            var vanished = new List<string>();

            // 0.7.340 — the Prospector's wipe. See ProspectorNarrator.
            var struck = ProspectorNarrator.TakeStruckSlots();
            string lead = ProspectorNarrator.TakeDiffLead();

            // BOTH SNAPSHOTS, ONCE PER DIFF. (0.7.119. Log only.)
            //
            // Zamar saw his Cuckoo move and heard nothing, on a build where the
            // enemy's moves in the very same diff WERE announced. That rules out
            // the direction code and the announcer, and leaves the diff itself —
            // but which half is wrong is not deducible from the outcome:
            //
            //   the card was missing from the BEFORE set,
            //   or missing from the AFTER set,
            //   or present in both at the same slot because the snapshot was
            //   taken after the move had already happened.
            //
            // The third is the one to watch for and it is invisible from
            // outside: a snapshot stored late reads a completed move as no
            // change at all. Sprinter fires at the END of the owner's turn, so
            // it lands very close to the boundary that takes the snapshot.
            //
            // Printing both sets is what separates the three, and it costs one
            // line per turn rather than one per frame — the discipline this
            // project has broken three times.
            Plugin.Log?.LogInfo($"IKMA BOARDDIFF: before = {Describe(_previous)}");
            Plugin.Log?.LogInfo($"IKMA BOARDDIFF: after  = {Describe(current)}");

            // MOVED — in both snapshots, different slot.
            for (int i = 0; i < current.Count; i++)
            {
                int was = IndexInPrevious(current[i].Card);
                if (was < 0) continue;

                var before = _previous[was];
                var now    = current[i];

                if (before.SlotIndex == now.SlotIndex && before.PlayerSide == now.PlayerSide)
                    continue;

                string name = NameOf(now.Card);
                if (name == null) continue;

                // 0.7.344 — its sigil's line says this move. See NoteMoverPending.
                if (IsPendingMover(now.Card))
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA BOARDDIFF: move of '{name}' to slot {now.SlotIndex + 1} left to its own sigil line.");
                    continue;
                }

                // ALREADY SAID, BY WHOEVER MOVED IT. (0.7.228.)
                //
                // THIS BRANCH NEVER CONSULTED THE ANNOUNCED LIST. Only the
                // APPEARED branch below did, and it has since 0.7.177 — so
                // every NoteAnnounced call made by a MOVER has been a no-op
                // here for as long as movers have had bespoke lines.
                //
                // Zamar heard it on Burrower, 0.7.227:
                //
                //   IKMA BOARDDIFF: noted 'Mole Man' as already announced
                //   IKMA SPEAK: Mole Man's Burrower ability triggers: it moves
                //               right to slot 4.
                //   ...
                //   IKMA SPEAK: ... Your Mole Man moves right, from slot 2 to
                //               slot 4. ...
                //
                // The note was made, the log said so, and this loop never
                // looked. Sprinter and Guardian call NoteAnnounced for the same
                // stated reason and were never suppressed either — they only
                // escaped notice because a Strafe fires at end of turn, where
                // the next snapshot usually swallows the move before a diff
                // reports it. Timing, not suppression.
                //
                // THE KEY IS THE SLOT, exactly as it is below: a mover notes
                // the card AFTER it has moved, so the noted slot IS the
                // destination. Noted slot == current slot means someone has
                // already said where this card ended up. A card announced in
                // some other slot and since moved still reports, which is the
                // 0.7.177 rule and the reason the note carries a slot at all.
                if (WasAnnounced(now.Card))
                {
                    int noted = AnnouncedInSlot(now.Card);
                    if (noted >= 0 && noted == now.SlotIndex)
                    {
                        Plugin.Log?.LogInfo(
                            $"IKMA BOARDDIFF: move of '{name}' to slot {now.SlotIndex + 1} " +
                            "suppressed — whoever moved it already said where it landed.");
                        continue;
                    }
                }

                // A card changing SIDES is not something any Act 1 mover does,
                // and calling it a move between slot numbers would be actively
                // misleading. Say what happened instead.
                if (before.PlayerSide != now.PlayerSide)
                {
                    moves.Add(Vocabulary.BoardChanges.MovesToSlot(name, SideWord(now.PlayerSide), now.SlotIndex + 1));
                    continue;
                }

                // Session 14. The probe confirmed against the game's own
                // SlotIsLeftOfSlot that index order runs left to right, so a
                // direction can be named without asserting anything. Zamar's
                // call to add it; the slot numbers stay because they are the
                // vocabulary every other line uses.
                //
                // Silent on direction if the probe did not run or came back the
                // other way — the slot numbers are true either way.
                string dir = "";
                if (_indexRunsLeftToRight)
                    dir = Vocabulary.BoardChanges.RightOrLeft(now.SlotIndex, before.SlotIndex);

                moves.Add(Vocabulary.BoardChanges.MovesFromSlotTo(Possessive(now.PlayerSide), name, dir, before.SlotIndex + 1, now.SlotIndex + 1));
            }

            // APPEARED — on the board now, not before, and nobody said so.
            for (int i = 0; i < current.Count; i++)
            {
                if (IndexInPrevious(current[i].Card) >= 0) continue;

                // SUPPRESSION IS KEYED TO THE FACT, NOT THE CARD. (0.7.177.)
                //
                // Zamar: "it didn't announce my Cuckoo moving right to slot 3".
                // His log has the Cuckoo played into slot 2 and announced
                // there, then Sprinter moved it to slot 3 at end of turn — and
                // the diff said nothing, because the card was on the
                // already-announced list and the list only remembered the CARD.
                //
                // He was left believing it was still in slot 2, which is the
                // worst possible outcome for a board read: not missing
                // information, but stale information he has no reason to
                // doubt.
                //
                // THE PREVIOUS SNAPSHOT CANNOT CATCH THIS. The Cuckoo was
                // played after that snapshot was taken, so at diff time it
                // looks like a card APPEARING at slot 3 rather than one moving
                // from 2 — the move branch above never sees it. The only record
                // that it was ever in slot 2 is the announcement itself.
                //
                // So: announced in the same slot it is in now → still true,
                // stay quiet. Announced in a DIFFERENT slot → it moved since,
                // and that is new information whoever announced it could not
                // have known.
                //
                // GENERAL: a "we already said this" list has to remember WHAT
                // was said, not just what it was said about. Anything that can
                // change after the announcement needs to be part of the key.
                string name = NameOf(current[i].Card);
                if (name == null) continue;

                // 0.7.349 — SKELETONS FROM THE SHIP ARE THE MUTINY LINE'S.
                // Zamar: "'your skeleton crew is played in-' This line
                // shouldnt exist." The diff can run between the skeleton
                // landing and the mutiny line noting it, because the line
                // waits for the end of the upkeep. SkeletonPirate reaches the
                // player's side only by GiantShip.MutinySequence.
                try
                {
                    if (current[i].PlayerSide && current[i].Card?.Info?.name == "SkeletonPirate")
                        continue;
                }
                catch { }

                if (WasAnnounced(current[i].Card))
                {
                    int noted = AnnouncedInSlot(current[i].Card);
                    if (noted < 0 || noted == current[i].SlotIndex) continue;

                    string movedDir = "";
                    if (_indexRunsLeftToRight)
                        movedDir = Vocabulary.BoardChanges.RightOrLeft(current[i].SlotIndex, noted);

                    moves.Add(Vocabulary.BoardChanges.MovesFromSlotToSlot(Possessive(current[i].PlayerSide), name, movedDir, noted + 1, current[i].SlotIndex + 1));
                    continue;
                }
                // A nugget the pickaxe left in your slot: the wipe line
                // already said so. (0.7.340.)
                if (current[i].PlayerSide && struck.Contains(current[i].SlotIndex))
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA BOARDDIFF: '{name}' in slot {current[i].SlotIndex + 1} left out — the wipe line covers it.");
                    continue;
                }

                if (_grizzlyWipe && !current[i].PlayerSide) continue;

                bool fromQueue = !current[i].PlayerSide && WasInPreviousQueue(current[i].Card);

                // THE PACK RIDES THE ARRIVAL LINE. (0.7.174.) Zamar:
                // "Add after 'Enemy Pack Mule moves down to slot 1, it is
                // carrying a pack of cards.'"
                //
                // It replaces a separate "That mule is carrying a pack." line,
                // which said the same thing one sentence later and made the
                // player hold two facts about one card. Same principle as the
                // Sprinter's direction moving inside its sigil name: a fact
                // about a card belongs in that card's clause.
                //
                // ASKED OF THE CARD, not of its name. A PackMule component is
                // what makes the pack appear on the model, so the component IS
                // the question — a name check would break the moment a mod or a
                // later act reuses the behaviour.
                string pack = PackNote(current[i].Card);

                appeared.Add(fromQueue
                    ? Vocabulary.BoardChanges.MovesDownToSlot(Possessive(current[i].PlayerSide), name, current[i].SlotIndex + 1, pack)
                    // "is PLAYED in slot N", 0.7.263. Zamar, on the bait
                    // buckets: "should be 'Enemy Bait Bucket is played in slot
                    // 3.'" A card that was not on the board and is on it now was
                    // put there by somebody, and the bare "is in slot 3" read as
                    // a position report rather than as an event — which is the
                    // opposite of what this branch exists to say.
                    : Vocabulary.BoardChanges.IsPlayedInSlot(Possessive(current[i].PlayerSide), name, current[i].SlotIndex + 1, pack));
            }

            // ENTERED THE QUEUE — not queued before, queued now. (0.7.177.)
            //
            // Zamar: "After it said his que was cleared, it didn't announce
            // that the bloodhound was placed in que targeting slot 4."
            //
            // The queue was only ever read on request (U) or once at the start
            // of an encounter. Everything the opponent lined up after that
            // arrived in silence — and the Prospector's phase 2 CLEARS the
            // queue and refills it, so the one moment the queue matters most
            // was the one moment nothing was said.
            //
            // WHY THIS IS NOT NOISE. A sighted player sees a card slide into
            // the queue; it is the single best piece of information about the
            // turn ahead, and it is what the player plans the next turn
            // around. The snapshot machinery to detect it already existed for
            // the "moves down" wording — it just was not being asked.
            //
            // WORDING reuses the queue read's own "Entering Slot N:" /
            // "Blocked for Slot N:" shape rather than inventing a second
            // vocabulary for the same fact. PROVISIONAL — logged as such,
            // because the sentence around it is Claude's.
            // 0.7.195 — SORTED BY SLOT, NOT BY QUEUE POSITION.
            //
            // Zamar: "Why did slot 4 adder get read before slot 3 adder?"
            //
            // Because the loop below walks Opponent.Queue in the order the
            // OPPONENT built it, which has nothing to do with where the cards
            // are pointing. Two Adders came out as slot 4 then slot 3.
            //
            // Every other board readout in this mod goes left to right, because
            // that is how the table is laid out and how a sighted player scans
            // it. A forecast that jumps around the board makes the player hold
            // an unordered list in their head and sort it themselves.
            //
            // Slot index carries the sort; the string is built after.
            var queueArrivalItems = new List<KeyValuePair<long, string>>();
            var queueArrivals = new List<string>();
            if (_haveSnapshot)
            {
                try
                {
                    var opponent = TurnManager.Instance?.Opponent;
                    var queue    = opponent?.Queue;
                    var qSlots   = opponent?.QueuedSlots;

                    if (queue != null)
                    {
                        for (int i = 0; i < queue.Count; i++)
                        {
                            var qCard = queue[i];
                            if (qCard == null) continue;
                            if (WasInPreviousQueue(qCard)) continue;

                            string qName = NameOf(qCard);
                            if (qName == null) continue;

                            int slotIdx = -1;
                            if (qSlots != null && i < qSlots.Count && qSlots[i] != null)
                                slotIdx = qSlots[i].Index;

                            string where = slotIdx >= 0
                                ? Vocabulary.BoardChanges.TargetingSlot(slotIdx + 1)
                                : "";

                            // Unslotted entries sort last: int.MaxValue, not -1,
                            // so "somewhere" never leads the left-to-right walk.
                            // Slot in the high bits, queue position in the low
                            // bits, so the sort is genuinely stable — List.Sort
                            // with a comparison is not, and two cards aimed at
                            // one slot would otherwise swap at random.
                            long sortKey = ((long)(slotIdx >= 0 ? slotIdx : 0xFFFF) << 20) | (uint)i;
                            queueArrivalItems.Add(new KeyValuePair<long, string>(
                                sortKey, Vocabulary.BoardChanges.EnemyIsQueued(qName, where)));
                        }
                    }
                }
                catch { queueArrivalItems.Clear(); }
            }

            // Two cards aimed at the same slot keep the order the opponent
            // queued them in, which is the only tie-break the game itself offers.
            queueArrivalItems.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var item in queueArrivalItems)
                queueArrivals.Add(item.Value);

            // 0.7.361 — A STRAY `foreach (var arrival in queueArrivals)` SAT
            // HERE WITH NO BODY OF ITS OWN, so the departures loop below became
            // its body and every departure was spoken once per queued card.
            // Zamar's Grizzly log: sixteen "has left slot" lines for four cards,
            // four queued. Removed.

            // VANISHED — was on the board, is not now, and nobody said so.
            for (int i = 0; i < _previous.Count; i++)
            {
                if (IndexInCurrent(current, _previous[i].Card) >= 0) continue;
                if (WasAnnounced(_previous[i].Card)) continue;
                if (_grizzlyWipe && !_previous[i].PlayerSide) continue;

                string name = NameOf(_previous[i].Card);
                if (name == null) continue;

                // Zamar asked twice for a kill confirmation on Scissors, and
                // "has left slot 3" is not one. ItemTargetOutcome knows which
                // card an item was aimed at and which item it was, so when the
                // card that vanished IS that target, the item's own verb is
                // used: "Rabbit is cut and dies."
                //
                // This joins two confirmed facts — the item was aimed here, and
                // this card is now gone — rather than inferring a cause from
                // nothing. It is still a JOIN, so it is deliberately narrow: the
                // pending target only, inside that item's validity window, and
                // it falls back to the plain departure line for anything else.
                string killed = ItemTargetOutcome.DeathLineFor(_previous[i].Card, name);
                if (killed != null) { vanished.Add(killed); continue; }

                // A PHASE THAT CLEARS THE BOARD SPEAKS FOR EVERY CARD ON IT.
                // (0.7.314.) See SuppressDeparturesFor.
                if (DeparturesSuppressed)
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA BOARDDIFF: '{name}' leaving slot {_previous[i].SlotIndex + 1} " +
                        $"not announced — {_departureReason}.");
                    continue;
                }

                vanished.Add(Vocabulary.BoardChanges.HasLeftSlot(Possessive(_previous[i].PlayerSide), name, _previous[i].SlotIndex + 1));
            }

            Store(current);
            _announced.Clear();
            _announcedSlots.Clear();

            // SPOKEN as of Session 14. The 0.7.37 log settled the question this
            // was waiting on: every appeared and vanished line was checked
            // against what was actually said in the same run, and NONE of them
            // duplicated anything. They were not redundant — they were the only
            // record of things nobody was told about.
            //
            // What that log showed, concretely: the opponent played Porcupine,
            // Rabbit and Coyote onto the board and IKMA said nothing at all. The
            // turn-start queue read forecasts what is COMING; nothing reported
            // what landed. And a Porcupine cut with Scissors left the board with
            // no death line — the only trace of it anywhere was this watcher's
            // unspoken "has left slot 1".
            //
            // Note the timing limit honestly: these fire at the NEXT turn
            // boundary, not when they happen. An item kill is reported a turn
            // late. That is late information rather than no information, and the
            // immediate line for an item kill is its own open item.
            // ORDER MATTERS, and getting it wrong cost real trust. Session 14,
            // 0.7.38 log: a Porcupine cut with Scissors was reported as
            // "Enemy Coyote is in slot 2. Enemy Coyote is in slot 3. Enemy
            // Sparrow is in slot 4. Enemy Porcupine has left slot 1." — the
            // departure last, after three arrivals from the opponent's queue.
            //
            // Zamar heard that as the Porcupine surviving until the queue moved,
            // and reported a major gameplay bug. The game was fine; the log
            // shows the Porcupine never acted again. **The sentence invented the
            // bug.** A line that puts a consequence after the events that
            // followed it describes a sequence that did not happen.
            //
            // Departures first, then arrivals, then moves: what LEFT is the part
            // of the board model that is now wrong, and it happened first.
            var lines = new List<string>();
            if (_grizzlyWipe)
            {
                _grizzlyWipe = false;
                queueArrivals.Clear();
                Plugin.Log?.LogInfo("IKMA BOARDDIFF: Grizzly wipe — enemy side covered by one line.");
                lines.Add(Vocabulary.GrizzliesFillEverySlot);
            }
            lines.AddRange(vanished);
            lines.AddRange(appeared);
            lines.AddRange(moves);

            // Queue arrivals LAST, and that placement follows the same
            // reasoning as the ordering note above. Everything before this
            // happened ON the board and is the player's model of NOW; what the
            // opponent has lined up is about the turn ahead. Leading with the
            // forecast would push the corrections to the board behind it.
            lines.AddRange(queueArrivals);

            if (lines.Count == 0)
            {
                if (lead == null) return null;
                string bare = Vocabulary.BoardChanges.ProspectorClearsHisQueue;
                Plugin.Log?.LogInfo($"IKMA BOARDDIFF: {bare}");
                return bare;
            }

            // One queue entry holding the whole diff, rather than one entry
            // each. A queued line never interrupts another queued line, but
            // separate entries can still be split apart by an Action line
            // arriving between them, and half a board report is worse than none.
            //
            // Departures lead — see the ordering note above.
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(lines[i]);
            }

            string line = (lead ?? "") + sb.ToString();
            Plugin.Log?.LogInfo($"IKMA BOARDDIFF: {line}");
            return line;
        }

        // ----------------------------------------------------------------------
        // Read both sides. Returns false if there is no board to read.
        // ----------------------------------------------------------------------
        private static bool Capture(List<Placement> into)
        {
            var bm = Singleton<BoardManager>.Instance;
            if (bm == null) return false;

            bool any = false;
            any |= CaptureSide(into, bm.PlayerSlotsCopy,   true);
            any |= CaptureSide(into, bm.OpponentSlotsCopy, false);
            return any;
        }

        private static bool CaptureSide(List<Placement> into, List<CardSlot> slots, bool playerSide)
        {
            if (slots == null) return false;

            // ONE CARD IS ONE PLACEMENT, HOWEVER MANY SLOTS IT HOLDS.
            // (0.7.211.)
            //
            // The same fix as BoardReader.FormatSlots, for the same reason and
            // in the same words: Leshy's moon is a single PlayableCard written
            // into every opponent slot by GiantCard.OnResolveOnBoard. Captured
            // per slot, it entered the snapshot four times, so the diff would
            // have announced it arriving four times and dying four times - and
            // worse, IndexInPrevious matches by reference and returns the FIRST
            // hit, so the other three would have compared against the wrong
            // entry and reported moves that never happened.
            //
            // Identity, not name: two Squirrels are two placements.
            int firstOfThisSide = into.Count;

            for (int i = 0; i < slots.Count; i++)
            {
                // LiveCard, not slot.Card. A destroyed card still sitting in its
                // slot is invisible to every line that DESCRIBES the board, and
                // this is such a line — otherwise a corpse would read as having
                // vanished a turn later than it did.
                var card = BoardReader.LiveCard(slots[i]);
                if (card == null) continue;

                bool already = false;
                for (int j = firstOfThisSide; j < into.Count; j++)
                    if (ReferenceEquals(into[j].Card, card)) { already = true; break; }

                if (already)
                {
                    // Logged, never spoken: a card in two slots is rare enough
                    // that when the diff looks wrong, this line is the first
                    // thing worth knowing.
                    Plugin.Log?.LogInfo(
                        $"IKMA DIFF: card already captured on this side, slot {i + 1} skipped " +
                        "(one card holding more than one slot).");
                    continue;
                }

                into.Add(new Placement { Card = card, SlotIndex = i, PlayerSide = playerSide });
            }
            return true;
        }

        /// <summary>
        /// Take the board as it stands as the new "before", saying nothing,
        /// and leave the queue snapshot alone so cards queued since the last
        /// diff are still announced. (0.7.426, the Trader's trade phase.)
        /// </summary>
        internal static void RebaselineBoard(string reason)
        {
            // No snapshot yet means the next diff only stores one anyway.
            if (!_haveSnapshot) return;

            var current = new List<Placement>();
            if (!Capture(current)) return;

            _previous.Clear();
            for (int i = 0; i < current.Count; i++) _previous.Add(current[i]);

            Plugin.Log?.LogInfo(
                $"IKMA BOARDDIFF: board re-read without announcing, {current.Count} card(s) — {reason}. " +
                "The queue is still to be announced.");
        }

        private static void Store(List<Placement> current)
        {
            _previous.Clear();
            for (int i = 0; i < current.Count; i++) _previous.Add(current[i]);
            SnapshotQueue();
            _haveSnapshot = true;
        }

        private static int IndexInPrevious(PlayableCard card)
        {
            for (int i = 0; i < _previous.Count; i++)
                if (ReferenceEquals(_previous[i].Card, card)) return i;
            return -1;
        }

        private static int IndexInCurrent(List<Placement> current, PlayableCard card)
        {
            for (int i = 0; i < current.Count; i++)
                if (ReferenceEquals(current[i].Card, card)) return i;
            return -1;
        }

        // Internal ids are never display names. Find the data object.
        /// <summary>
        /// The spoken name of a card ON THE BOARD, which is the only kind this
        /// file deals in.
        /// </summary>
        /// <remarks>
        /// 0.7.264 — and that is why the Submerged decoration belongs here.
        /// Every line this file composes describes a position on the table, so
        /// a card that is under the water is under it in all of them.
        /// </remarks>
        private static string NameOf(PlayableCard card)
        {
            try
            {
                string name = CardReader.CardName(card?.Info);
                if (string.IsNullOrEmpty(name)) return null;
                return Vocabulary.Submerged(card, name);
            }
            catch { return null; }
        }

        /// <summary>
        /// ", it is carrying a pack of cards" for a Pack Mule, empty otherwise.
        /// His wording. The pack is visible on the card's back from the moment
        /// it lands, so it belongs in the line that announces the landing.
        /// </summary>
        private static string PackNote(PlayableCard card)
        {
            if (card == null) return "";
            try
            {
                return card.GetComponent<PackMule>() != null
                    ? Vocabulary.BoardChanges.ItIsCarryingA
                    : "";
            }
            catch { return ""; }
        }

        private static string Possessive(bool playerSide) => Vocabulary.BoardChanges.Possessive(playerSide);

        private static string SideWord(bool playerSide) => Vocabulary.BoardChanges.SideWord(playerSide);
        // ==================================================================
        // A BOARD WIPE IS ONE EVENT, NOT ONE PER CARD. (0.7.314.)
        //
        // Zamar, on the Trapper's phase two: "That strange frog and leaping
        // trap callout did not have to be included since it was part of the
        // 'wipes board clean' thing. Nothing from phase 1 needs to be called
        // out here."
        //
        // The Prospector already had this, as ProspectorNarrator
        // .WipeInProgress checked in the death path — but a card SWEPT off the
        // board does not die, it simply stops being there, so it arrives here
        // in the differ instead and that guard never saw it.
        //
        // A WINDOW, because the cards leave over the sweep's own animation and
        // the differ runs on its own schedule. Every skipped card is still
        // logged by name, so a wipe that swallows something it should not have
        // is visible without being audible.
        // ==================================================================
        private static float _departuresSilentUntil = -99f;
        private static string _departureReason;

        internal static void SuppressDeparturesFor(float seconds, string reason)
        {
            float now = 0f;
            try { now = UnityEngine.Time.unscaledTime; } catch { }

            _departuresSilentUntil = now + seconds;
            _departureReason       = reason;

            Plugin.Log?.LogInfo($"IKMA BOARDDIFF: departures silent for {seconds:0.#}s — {reason}.");
        }

        private static bool DeparturesSuppressed
        {
            get
            {
                float now = 0f;
                try { now = UnityEngine.Time.unscaledTime; } catch { }
                return now < _departuresSilentUntil;
            }
        }

    }
}
