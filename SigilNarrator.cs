// SigilNarrator.cs
//
// -----------------------------------------------------------------------------
// SIGILS THAT ACT ON THE BOARD BY THEMSELVES. (Session 19, v0.7.180.)
//
// First tenant: Guardian. Zamar asked for both halves of it —
//
//   "When a card is played on the opposite board Guardian always triggers. If
//    it can move to the adjacent slot it does, and that needs to be called out.
//    If it can't move to the adjacent slot, the card(s) with Guardian still
//    visually shake. And that should be called out too."
//
// THE SECOND HALF IS THE ONE WORTH ARGUING FOR. A sigil that fires and achieves
// nothing still SHOWS a sighted player that it fired — the card shakes. Silence
// there is not neutral: it teaches a blind player the sigil is dormant while it
// is being spent every single turn. That is the parity test in its purest form.
//
// -----------------------------------------------------------------------------
// WHAT GUARDIAN ACTUALLY DOES, read from IL (dumps/dump_guardian.txt).
//
// The class is GuardDog; the sigil reads "Guardian". Internal ids are never
// display names on this project, so the spoken name comes from
// AbilityInfo.rulebookName through CardReader.GetAbilityName like every other.
//
// RespondsToOtherCardResolve — the gate. It requires the resolving card to be
// on the OPPOSITE side, and its slot to be one this Guardian does not already
// face. So the trigger is "an enemy card landed opposite a space I am not in".
//
// OnOtherCardResolve — the branch, and both paths are right there:
//
//     targetSlot = otherCard.Slot.opposingSlot
//     if (targetSlot.Card != null)      -> Card.Anim.StrongNegationEffect()
//     else                              -> tween, BoardManager.AssignCardToSlot
//
// So "can it move" is exactly "is the slot opposite the new card empty", and
// the failing path plays StrongNegationEffect — the game's generic "this
// trigger did nothing" gesture.
//
// -----------------------------------------------------------------------------
// WHY THIS PATCHES GuardDog AND NOT StrongNegationEffect.
//
// Patching the animation itself was tempting: it is shared, so one patch would
// make EVERY fizzling sigil announceable at once. It is also the wrong first
// move. That method has no idea WHICH sigil fizzled or why, so the line could
// only ever be a vague "something did nothing" — and it fires in contexts this
// project has never looked at, which is how a well-meant general patch turns
// into noise nobody asked for.
//
// Guardian first, specifically, because he asked for Guardian specifically. If
// a second fizzling sigil comes up, the shared animation is the obvious place
// to generalise to, and this comment is the note to a future session that the
// option exists and why it was not taken yet.
//
// -----------------------------------------------------------------------------
// WHY THE OUTCOME IS OBSERVED, NOT PREDICTED.
//
// A prefix on a coroutine fires at enumerator creation, and the board state
// there IS the state the game branches on — so the outcome could be predicted
// from it correctly. It is still predicted rather than observed, and this
// project has been burned twice by lines that described what was about to
// happen (the pickaxe's "Wolf takes its place", the draw prompt that told him
// to press a key he had already pressed).
//
// So the prefix captures only the QUESTION — which card, which slot — and the
// answer is read at speak time from where the Guardian actually ended up.
// -----------------------------------------------------------------------------

using DiskCardGame;

namespace IKMA
{
    internal static class SigilNarrator
    {
        /// <summary>
        /// Another live card on this card's side with the same spoken name.
        /// (0.4.8.004.) Read when the line is composed, so it describes the
        /// board the player is hearing about.
        /// </summary>
        private static bool HasTwinOnSide(PlayableCard card, string name)
        {
            try
            {
                var bm = Singleton<BoardManager>.Instance;
                if (bm == null || card == null) return false;
                var side = card.OpponentCard ? bm.OpponentSlotsCopy : bm.PlayerSlotsCopy;
                foreach (var s in side)
                {
                    var c = BoardReader.LiveCard(s);
                    if (c?.Info == null || ReferenceEquals(c, card)) continue;
                    if (CardReader.CardName(c) == name) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// The sigil's own name, from the game. Falls back to the word on the
        /// rulebook page only if the lookup fails outright.
        /// </summary>
        private static string GuardianName()
        {
            string n = null;
            try { n = CardReader.GetAbilityName(Ability.GuardDog); } catch { }
            return Vocabulary.Sigils.GuardianName(string.IsNullOrEmpty(n), n);
        }

        internal static void NoteGuardianTriggered(GuardDog behaviour, PlayableCard otherCard)
        {
            if (behaviour == null || otherCard == null) return;

            PlayableCard guardian = null;
            CardSlot targetSlot   = null;
            int fromSlot          = -1;

            try
            {
                // AbilityBehaviour.Card is PROTECTED, so it cannot be read from
                // here. (0.7.181 — the 0.7.180 build failed on exactly that.)
                //
                // An ability behaviour is a component on the card's own
                // GameObject, so the card can be asked for itself. This is the
                // route CardReader.DescribeMoveDirection already uses to reach
                // the Strafe component from the other direction, and it is NOT
                // the banned lookup — the hard rule bans FindObjectOfType and
                // GameObject.Find, which scan the scene. GetComponent asks one
                // object about itself.
                guardian   = behaviour.GetComponent<PlayableCard>();
                targetSlot = otherCard.Slot?.opposingSlot;
                fromSlot   = guardian?.Slot != null ? guardian.Slot.Index : -1;
            }
            catch { }

            if (guardian == null || targetSlot == null) return;

            // Whether the slot was occupied when the game branched. Logged only
            // — the SPOKEN line is decided by where the card actually ends up.
            bool blockedAtTrigger = false;
            try { blockedAtTrigger = targetSlot.Card != null; } catch { }

            int targetIndex = -1;
            try { targetIndex = targetSlot.Index; } catch { }

            Plugin.Log?.LogInfo(
                $"IKMA SIGIL: Guardian on '{CardReader.CardName(guardian)}' triggered by " +
                $"'{CardReader.CardName(otherCard)}' resolving. Target slot " +
                $"{targetIndex + 1} occupied at trigger = {blockedAtTrigger}.");

            var capturedGuardian = guardian;
            int capturedTarget   = targetIndex;
            int capturedFrom     = fromSlot;
            bool capturedBlocked = blockedAtTrigger;

            // 0.7.432 - Zamar's sentence names the side: "Enemy Bloodhound's
            // Guardian ability triggers: it moves to slot 4." Same rule as the
            // other sigil lines: an opponent's card is "Enemy X", the player's
            // own card is just its name.
            bool capturedEnemy = false;
            try { capturedEnemy = guardian.OpponentCard; } catch { }

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                // The card's CURRENT slot is deliberately not read here any
                // more. It was the source of the 0.7.188 false callout; see the
                // note below.
                string name = null;
                try { name = CardReader.CardName(capturedGuardian); } catch { }

                if (string.IsNullOrEmpty(name)) return null;

                string sigil = GuardianName();
                string who   = (capturedEnemy ? Vocabulary.Sigils.Enemy : "") + name;

                // ZAMAR'S WORDING, 0.7.182:
                //
                //   "Bloodhound with Guardian moves to slot 3."
                //   "Bloodhound with Guardian is blocked from moving to slot 3."
                //
                // Note what he changed from the provisional pair. "X with
                // Guardian" attributes the act to the CARD and names the sigil
                // as the reason, which is the same shape he approved for Touch
                // of Death ("Bloodhound with Touch of Death attacks"). One
                // construction for "this card is doing something because of
                // this sigil", used everywhere.
                //
                // And "is blocked from moving" over "cannot reach": blocked
                // says something is IN THE WAY, which is exactly what the game
                // branched on — the target slot being occupied.

                // ==============================================================
                // 0.7.189 — THE FALSE CALLOUT, AND WHY OBSERVING WAS WRONG HERE.
                //
                // Zamar, Prospector fight, 2026-09-12:
                //
                //   "Bloodhound said it was blocked, but it wasn't and
                //    successfully moved even. Our callout was directly false."
                //
                // The log of that exact moment:
                //
                //   IKMA SIGIL: ... Target slot 1 occupied at trigger = False.
                //   IKMA SPEAK: Bloodhound with Guardian is blocked from ...
                //   IKMA BOARDDIFF: Enemy Bloodhound moves left, slot 4 to 1.
                //
                // The prefix had the truth. The SPEAK-TIME re-derivation did
                // not: the compose ran while the move was still a tween in
                // flight, so the card was still in its old slot and the old
                // code read that as "blocked".
                //
                // THE LESSON, AND IT IS A CORRECTION TO 0.7.182's OWN COMMENT.
                // "Observe, do not predict" is right when the game has not yet
                // decided. Here it HAD decided — before the prefix returned.
                // GuardDog.OnOtherCardResolve branches on one thing:
                //
                //     if (targetSlot.Card != null)  -> StrongNegationEffect
                //     else                          -> tween, AssignCardToSlot
                //
                // blockedAtTrigger is that exact boolean, read from the same
                // slot the game reads, at the moment the game reads it. Using
                // it is not predicting an outcome — it is asking the game its
                // own question instead of timing a race against an animation.
                //
                // GENERALISE: when the game branches on a condition, read the
                // CONDITION. Re-observing the RESULT only works when the result
                // has landed, and a coroutine gives no promise that it has.
                // ==============================================================

                if (capturedBlocked)
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA SIGIL: Guardian on '{name}' blocked — slot " +
                        $"{capturedTarget + 1} was occupied at trigger.");
                    // 0.7.227 — the one construction. See NoteStrafeMove.
                    return Vocabulary.Sigils.SAbilityTriggersIt(who, sigil, capturedTarget + 1);
                }

                if (capturedTarget < 0) return null;

                // MOVED. The game has already taken the branch that tweens the
                // card and calls BoardManager.AssignCardToSlot; nothing in that
                // branch can decline. Suppress the board diff's own version of
                // the same move so it is not reported twice, once late.
                try { BoardWatcher.NoteAnnounced(capturedGuardian); } catch { }

                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL: Guardian on '{name}' moving from slot " +
                    $"{capturedFrom + 1} to {capturedTarget + 1}.");
                // 0.7.227 — the one construction. See NoteStrafeMove.
                return Vocabulary.Sigils.SAbilityTriggersItMoves(who, sigil, capturedTarget + 1);
            });
        }

        // ==================================================================
        // A MOVER ACTUALLY MOVING, AT THE MOMENT IT MOVES. (0.7.184.)
        //
        // Zamar: "nowhere does it say my cuckoo moved right with Sprinter. It
        // should say that before Enemy Turn and even before the end-of-my-turn
        // scales readout."
        //
        // WHY THE BOARD DIFF WAS NEVER GOING TO COVER THIS PROPERLY. The diff
        // runs at a turn boundary and reports what changed since the last one,
        // so a Sprinter move is heard a whole turn late at best. 0.7.177 made
        // the diff slot-aware so the move at least stopped being SWALLOWED —
        // but "late" was still the ceiling, because the diff is the wrong
        // instrument for something with a known moment.
        //
        // Strafe.OnTurnEnd IS that moment. Patching it puts the line in the
        // turn-end sequence, ahead of the opponent's turn announcement, which
        // is where he asked for it.
        //
        // ON HIS "before the scales readout" — NOT DONE, deliberately, and
        // worth saying plainly. The scales line is enqueued during COMBAT,
        // which genuinely happens before the end-of-turn strafe. Putting the
        // move ahead of it would describe a sequence that did not happen, and
        // this project has already had a false bug report caused by exactly
        // that (see the ordering note in BoardWatcher). The move now lands at
        // the end of his turn instead of a turn later; if he still wants it
        // ahead of the scales after hearing it, that is a deliberate
        // re-ordering to decide with the reason on the table.
        //
        // OBSERVED, NOT PREDICTED. The prefix fires at enumerator creation,
        // before the card has moved, so it captures only the card and where it
        // started. Where it ENDED is read at speak time.
        //
        // COVERS THE WHOLE STRAFE FAMILY. Strafe is the base for Rampager,
        // StrafeSwap, SkeletonStrafe and SquirrelStrafe, so one patch serves
        // every sigil with an arrow on it — and each names its own sigil,
        // because the behaviour carries its own Ability.
        // ==================================================================
        // ==================================================================
        // BURROWER — the card that ducks out of the way. (0.7.225.)
        //
        // Zamar, 0.7.224 log:
        //
        //   IKMA SPEAK: Mole Man's Burrower ability triggers.
        //   ... a turn later, mixed into an unrelated diff ...
        //   IKMA SPEAK: Enemy Amalgam moves down to slot 4. Your Mole Man
        //               moves right, from slot 1 to slot 4. Enemy Mantis is
        //               queued targeting slot 2.
        //
        //   "That 'your mole man moves from slot 1 to slot 4' line should have
        //    been apart of the Burrower ability triggers line."
        //
        // The generic line was the cause with no consequence, and the
        // consequence turned up a turn later in a sentence about three
        // unrelated things. A player hearing "Burrower triggers" has to hold
        // the question open and then find the answer inside someone else's
        // line. One event is one line.
        //
        // WHAT THE GAME DOES, from WhackAMole.cs:
        //   RespondsToSlotTargetedForAttack — only when the target slot is
        //     EMPTY and on the card's own side (and, against a Flying
        //     attacker, only if the card has Reach).
        //   OnSlotTargetedForAttack(CardSlot slot, PlayableCard attacker)
        //     — PreSuccessfulTriggerSequence (the generic hook fires HERE),
        //       then tweens, then BoardManager.AssignCardToSlot(Card, slot).
        //
        // THE DESTINATION IS AN ARGUMENT, so unlike Strafe this does not need
        // to be observed at speak time — the game has already decided where the
        // card is going before the coroutine starts. The line still composes
        // deferred so it lands after the attack narration it belongs behind,
        // and it still reads the slot the card ENDED in rather than trusting
        // the argument, because a move that is blocked or overridden must not
        // be announced as if it happened.
        //
        // Ability.WhackAMole is added to SigilTriggers.AlreadyNamedElsewhere in
        // the same build, or this would be the second line for one event —
        // regression pattern 4, which has cost this project four fixes.
        // ==================================================================
        internal static void NoteBurrow(WhackAMole behaviour, CardSlot target)
        {
            if (behaviour == null) return;

            PlayableCard mover = null;
            int fromSlot = -1;
            int toSlot   = -1;

            try
            {
                mover    = behaviour.GetComponent<PlayableCard>();
                fromSlot = mover?.Slot != null ? mover.Slot.Index : -1;
                toSlot   = target != null ? target.Index : -1;
            }
            catch { }

            if (mover == null || fromSlot < 0)
            {
                Plugin.Log?.LogInfo("IKMA SIGIL: Burrower fired but the mover could not be read.");
                return;
            }

            // 0.7.347 — inside a multi-strike attack the burrow is folded into
            // that attack's summary: where it started and where it ended.
            if (MultiStrikeNarrator.TryRecordBurrow(mover, fromSlot, toSlot)) return;

            // 0.7.438 - a single attack into the empty slot: the move is part
            // of BurrowBlock's one sentence, not a line of its own.
            if (BurrowBlock.ClaimBurrow(mover, fromSlot, toSlot)) return;

            Plugin.Log?.LogInfo(
                $"IKMA SIGIL: Burrower on '{CardReader.CardName(mover)}' — " +
                $"slot {fromSlot + 1} -> slot {toSlot + 1} (target slot is the argument).");

            var capturedMover = mover;
            int capturedFrom  = fromSlot;

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                string name = null;
                int nowSlot = -1;
                try
                {
                    name    = CardReader.CardName(capturedMover);
                    nowSlot = capturedMover.Slot != null ? capturedMover.Slot.Index : -1;
                }
                catch { }

                if (string.IsNullOrEmpty(name) || nowSlot < 0) return null;

                string sigilName = null;
                try { sigilName = CardReader.GetAbilityName(Ability.WhackAMole); } catch { }
                if (string.IsNullOrEmpty(sigilName)) sigilName = Vocabulary.Sigils.Burrower;

                // Suppress the board differ's own version of this move. Noted
                // AFTER the move so the slot recorded is the one it is in now,
                // which is what 0.7.177's slot-aware check matches. Same call
                // and same reason as the Strafe family above.
                try { BoardWatcher.NoteAnnounced(capturedMover); } catch { }

                // OBSERVED, NOT PREDICTED. If it did not actually move, say
                // nothing rather than reporting the destination the game
                // intended.
                //
                // SILENT, NOT "does nothing". (0.7.347.) Zamar, 0.7.346: "if
                // burrow doesn't trigger a move it should not read that it
                // does nothing." His log had it twice inside one multi-strike
                // turn, and both were false: WhackAMole.OnSlotTargetedForAttack
                // always ends in AssignCardToSlot, so the card HAD moved — and
                // then moved back for the next strike before this line was
                // composed. A speak-time read cannot see a card that moved
                // twice. Logged, never spoken.
                if (nowSlot == capturedFrom)
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA SIGIL: {sigilName} on '{name}' reads as unmoved at speak time " +
                        $"(slot {capturedFrom + 1}) — not spoken.");
                    return null;
                }

                string dir = Vocabulary.Sigils.RightOrLeft(nowSlot, capturedFrom);

                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL: {sigilName} moved '{name}' from slot {capturedFrom + 1} " +
                    $"to {nowSlot + 1}.");

                // HIS WORDING, 0.7.226, replacing the Sprinter-shaped
                // "Mole Man with Burrower moves right to slot 4.":
                //
                //   "Mole Man's Burrower ability triggers: it moves right to
                //    slot 4."
                //
                // It keeps the generic line's opening — the one that told him
                // the sigil had fired — and then answers the question it used
                // to leave hanging, in the same sentence. Cause and
                // consequence, one line, one event.
                //
                // 0.7.227: EVERY bespoke sigil line in this file was moved
                // onto this construction at his request — Guardian, Bloodlust,
                // Sprinter and the whole Strafe family, Brood Parasite, Frozen
                // Away and Evolve/Fledgling. See NoteStrafeMove for the rule.
                return Vocabulary.Sigils.SAbilityTriggersItMovesTo(name, sigilName, dir, nowSlot + 1);
            });
        }

        internal static void NoteStrafeMove(Strafe behaviour)
        {
            if (behaviour == null) return;

            PlayableCard mover = null;
            int fromSlot = -1;
            Ability sigil = Ability.None;

            try
            {
                mover    = behaviour.GetComponent<PlayableCard>();
                fromSlot = mover?.Slot != null ? mover.Slot.Index : -1;
                sigil    = behaviour.Ability;
            }
            catch { }

            if (mover == null || fromSlot < 0) return;

            var capturedMover = mover;
            int capturedFrom  = fromSlot;
            var capturedSigil = sigil;

            // 0.7.344 — the diff must not say this move before this line does.
            BoardWatcher.NoteMoverPending(mover);

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                string name = null;
                int nowSlot = -1;
                try
                {
                    name    = CardReader.CardName(capturedMover);
                    nowSlot = capturedMover.Slot != null ? capturedMover.Slot.Index : -1;
                }
                catch { }

                // Gone from the board. Nothing to say about a card that is no
                // longer there.
                if (string.IsNullOrEmpty(name) || nowSlot < 0)
                    return null;

                string sigilName = null;
                try { sigilName = CardReader.GetAbilityName(capturedSigil); } catch { }
                if (string.IsNullOrEmpty(sigilName)) sigilName = Vocabulary.Sigils.Sprinter;

                // BLOCKED. (0.7.187.)
                //
                // The 0.7.184 version of this comment said "a blocked Strafe is
                // silent in the game too; it has no negation animation the way
                // Guardian does". THAT WAS WRONG, and it was an assumption, not
                // a reading. dumps/dump_negation.txt shows Strafe.MoveToSlot
                // calling CardAnimationController.StrongNegationEffect — the
                // card shakes exactly like a blocked Guardian.
                //
                // Zamar hit it in the Prospector fight: "The Mule with sprinter
                // moving left failed to activate, but it was not called out."
                //
                // Silence there is the parity failure in its purest form: the
                // shake tells a sighted player the sigil was spent, and a blind
                // player was being taught the sigil is dormant.
                //
                // OBSERVED, NOT PREDICTED, like the line below it — the card is
                // in the slot it started in, so it did not move. No timer, no
                // branch guess, no second patch.
                BoardWatcher.ClearMoverPending(capturedMover);

                if (nowSlot == capturedFrom)
                {
                    try { BoardWatcher.NoteAnnounced(capturedMover); } catch { }

                    Plugin.Log?.LogInfo(
                        $"IKMA SIGIL: {sigilName} on '{name}' was blocked — still in slot " +
                        $"{capturedFrom + 1}.");

                    // ZAMAR'S WORDING, approved 2026-09-12.
                    return HasTwinOnSide(capturedMover, name)
                        ? Vocabulary.SigilFizzlesInSlot(name, sigilName, capturedFrom + 1)
                        : Vocabulary.SigilFizzles(name, sigilName);
                }

                // Direction from the slot numbers actually travelled, not from
                // the sigil's flag — this reports where it WENT.
                string dir = Vocabulary.Sigils.RightOrLeft(nowSlot, capturedFrom);

                // The line suppresses the board diff's own version of the same
                // move: noted AFTER the move, so the slot recorded is the one
                // the card is in now and 0.7.177's slot-aware check matches.
                try { BoardWatcher.NoteAnnounced(capturedMover); } catch { }

                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL: {sigilName} moved '{name}' from slot " +
                    $"{capturedFrom + 1} to {nowSlot + 1}.");

                // ================================================================
                // THE ONE CONSTRUCTION FOR EVERY BESPOKE SIGIL LINE. (0.7.227.)
                //
                //     "<card>'s <Sigil> ability triggers: it <what happened>."
                //
                // His wording, asked for on Burrower and then widened: "change
                // sprinter to this new one too" / "and the others". So every
                // bespoke line in this file now opens the way the GENERIC line
                // opens — the half that tells the player a sigil fired — and
                // then answers, after a colon, the question the generic line
                // used to leave hanging.
                //
                //   "Cuckoo's Sprinter ability triggers: it moves right to slot 4."
                //
                // This REPLACES several shapes approved separately over four
                // builds ("X with SIGIL moves...", "...triggers, placing a...",
                // "...triggers, transforming it into..."). They are consistent
                // now, which is the point; do not reintroduce a one-off shape
                // for a new sigil without asking him.
                //
                // The FIZZLE lines are untouched — "X's SIGIL ability does
                // nothing." is his, approved 2026-09-12, and already reads as
                // one family with these.
                //
                // COVERS THE WHOLE STRAFE FAMILY from this one method:
                // Sprinter, Rampager, StrafeSwap, SkeletonStrafe and
                // SquirrelStrafe each name their own sigil through
                // behaviour.Ability, so all five changed together.
                // ================================================================
                //
                // COVERS THE WHOLE STRAFE FAMILY, because this one method
                // serves all of it: Sprinter, Rampager, StrafeSwap,
                // SkeletonStrafe and SquirrelStrafe each name their own sigil
                // through behaviour.Ability, so all five change together and
                // none of them needed a second edit.
                string moved = HasTwinOnSide(capturedMover, name)
                    ? Vocabulary.Sigils.SAbilityTriggersInSlotItMovesTo(name, sigilName, capturedFrom + 1, dir, nowSlot + 1)
                    : Vocabulary.Sigils.SAbilityTriggersItMovesTo(name, sigilName, dir, nowSlot + 1);
                return moved + NextStrafeDirection(capturedMover, name);
            });
        }

        // ==================================================================
        // THE EGG HATCHES. (0.7.443.)
        //
        // Zamar's 0.7.442 log: a Curious Egg was dealt into the opening hand,
        // the deck met its conditions, and it became a Hydra with no line at
        // all - the hand read simply said "Hydra". "Need a big callout for
        // when the eggs in the deck hatch and Hydra is added to hand." His
        // sentence:
        //
        //   "Curious Egg's Finical Hatchling ability triggers, it transforms
        //    into a hellish beast..."
        //
        // and, on hearing it in 0.7.443, "Changed my mind": the same sentence
        // followed by "A Hydra is added to your hand." (0.7.444.) The new
        // name is the card's own, read after the swap.
        //
        // WHY THE GENERIC HOOK NEVER SAW IT. HydraEgg.OnDrawn (PUBLIC
        // override, HydraEgg.cs:51) lifts the card, shakes it, flips it and
        // swaps its CardInfo inside the flip. It never calls
        // PreSuccessfulTriggerSequence, which is the one place SigilTriggers
        // listens. So it needs a patch of its own.
        //
        // OBSERVED, NOT PREDICTED. A patch on a coroutine runs when the
        // enumerator is CREATED, about a second before the flip. So the
        // postfix wraps the enumerator, and this watches the card's name
        // after every step the game takes. The line is spoken at the first
        // step where the name is no longer the one it started with: the
        // moment the swap has really happened. If the coroutine ends with
        // the name unchanged, nothing is said and the log says so.
        //
        // The old name is captured before the first step, for the same
        // reason NoteTransformed captures it: by speak time the card is the
        // Hydra, and the sentence is about the egg.
        // ==================================================================
        internal static System.Collections.IEnumerator WatchHatch(
            HydraEgg behaviour, System.Collections.IEnumerator inner)
        {
            PlayableCard card = null;
            string oldName = null;
            try
            {
                card    = behaviour.GetComponent<PlayableCard>();
                oldName = CardReader.CardName(card?.Info);
            }
            catch { }

            bool said = false;
            while (true)
            {
                bool more = inner.MoveNext();

                if (!said && card != null && !string.IsNullOrEmpty(oldName))
                {
                    string now = null;
                    try { now = CardReader.CardName(card); } catch { }
                    if (!string.IsNullOrEmpty(now) && now != oldName)
                    {
                        said = true;
                        NoteDrawLineSaid(card);   // 0.7.445 - no "Drew Hydra." on top of it
                        SayHatched(oldName, now);
                    }
                }

                if (!more) break;
                yield return inner.Current;
            }

            if (!said)
                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL: the hatch ended and the card still reads '{oldName ?? "?"}' - not spoken.");
        }

        private static void SayHatched(string oldName, string newName)
        {
            string sigil = null;
            try { sigil = CardReader.GetAbilityName(Ability.HydraEgg); } catch { }
            if (string.IsNullOrEmpty(sigil)) return;   // an internal id is never a display name

            Plugin.Log?.LogInfo($"IKMA SIGIL: {sigil} hatched '{oldName}' into '{newName}'.");
            using (Speech.Event(EventKind.Powers))
                Speech.Result(Vocabulary.Sigils.HatchesIntoHellishBeast(oldName, sigil, newName));
        }

        // ==================================================================
        // THE GLITCHED CARD. (0.7.445.)
        //
        // Zamar's 0.7.444 log: a Glitched card was dealt into the opening
        // hand and became a Dire Wolf. IKMA said nothing; the hand read just
        // listed "Dire Wolf". "When I drew the Glitched card it should have
        // read what happened." His sentence:
        //
        //   "Drew Glitched card, the screen glitches and a [card name] is
        //    added to your hand."
        //
        // WHAT THE GAME DOES. RandomCard.OnDrawn (PUBLIC override,
        // RandomCard.cs:13) lifts the card, waits half a second, turns the
        // ScreenGlitchEffect up, plays the "glitch" sound, picks a random
        // card and calls SetInfo with it. Every Glitched card does this every
        // time it is drawn, so "the screen glitches" is always true here.
        //
        // RandomCard is a SpecialCardBehaviour, not an AbilityBehaviour: it
        // is not a sigil and no sigil hook can see it. Same method as the egg
        // above: wrap the enumerator, speak at the first step where the
        // card's CardInfo is a different object. The Glitched card has no
        // name to compare (it reads as ''), which is why this one compares
        // the CardInfo itself.
        //
        // CUTS IN ON A DRAW, QUEUES ON THE DEAL. The sentence begins "Drew",
        // so on a real draw it IS the draw line, and the draw line cuts in
        // unconditionally by his standing ruling (see AnnounceDrawnCard).
        // In the opening hand nobody pressed a key and other lines are
        // waiting their turn, so there it is queued like any result.
        // ==================================================================
        internal static System.Collections.IEnumerator WatchGlitch(
            RandomCard behaviour, System.Collections.IEnumerator inner)
        {
            PlayableCard card = null;
            CardInfo oldInfo = null;
            try
            {
                card    = behaviour.GetComponent<PlayableCard>();
                oldInfo = card?.Info;
            }
            catch { }

            bool said = false;
            while (true)
            {
                bool more = inner.MoveNext();

                if (!said && card != null && oldInfo != null)
                {
                    CardInfo now = null;
                    try { now = card.Info; } catch { }
                    if (now != null && !ReferenceEquals(now, oldInfo))
                    {
                        said = true;
                        SayGlitched(card, now);
                    }
                }

                if (!more) break;
                yield return inner.Current;
            }

            if (!said)
                Plugin.Log?.LogInfo("IKMA GLITCH: the Glitched card's draw ended and the card did not change - not spoken.");
        }

        private static void SayGlitched(PlayableCard card, CardInfo now)
        {
            string newName = null;
            try { newName = CardReader.CardName(now); } catch { }
            if (string.IsNullOrEmpty(newName))
            {
                Plugin.Log?.LogInfo("IKMA GLITCH: the new card has no name to say - not spoken.");
                return;
            }

            string article = Vocabulary.Sigils.AnOrALower("AEIOUaeiou".IndexOf(newName[0]));
            string line = Vocabulary.Sigils.DrewGlitchedCard(article, newName);

            // The ordinary "Drew X." must not follow this and cut it off.
            NoteDrawLineSaid(card);

            bool onADraw = false;
            try { onADraw = HotkeyManager.DrawInFlight; } catch { }
            Plugin.Log?.LogInfo(
                $"IKMA GLITCH: the Glitched card became '{newName}' " +
                (onADraw ? "on a draw - the line cuts in." : "on the deal - the line is queued."));

            using (Speech.Event(EventKind.CardDrawn, EventSource.CurrentPlayer))
            {
                if (onADraw)
                {
                    Speech.ConsumeConfirmationShield();
                    Speech.Confirm(line);
                }
                else Speech.Result(line);
            }
        }

        // ==================================================================
        // ONE DRAW, ONE DRAW LINE. (0.7.445.)
        //
        // PlayerHand.AddCardToHand (PlayerHand.cs) runs the card's Drawn
        // trigger BEFORE it adds the card to CardsInHand. HotkeyManager's
        // AnnounceDrawnCard waits for that list to grow and then says "Drew
        // X." as a Confirmation, which cuts whatever is being spoken. So for
        // a card that changes when drawn, "Drew Hydra." would arrive a moment
        // after the hatch line and cut it in half - or, when the trigger
        // takes longer than that watcher's three seconds, never arrive.
        // (His 0.7.444 log is the second case: no draw line for the egg.)
        //
        // The line that already told the draw marks the card here, and the
        // watcher asks before it speaks.
        // ==================================================================
        private static PlayableCard _drawLineSaidFor;

        internal static void NoteDrawLineSaid(PlayableCard card) { _drawLineSaidFor = card; }

        /// <summary>True once, for the card whose draw has already been told.</summary>
        internal static bool DrawLineAlreadySaid(PlayableCard card)
        {
            if (card == null || !ReferenceEquals(card, _drawLineSaidFor)) return false;
            _drawLineSaidFor = null;
            return true;
        }

        // ==================================================================
        // WHICH WAY NEXT. (0.7.443.)
        //
        // Zamar, Session 44: "When Elk's sprinter ability changes arrow
        // direction where it will move, that always needs to be called out
        // as info." His sentence:
        //
        //   "Elk's Sprinter ability triggers: it moves [left] to slot 3. It
        //    will move [right] next."
        //
        // and his rule for when: "After every successful move, it should
        // announce which direction it will move next."
        //
        // WHERE THE ANSWER COMES FROM. The arrow is Strafe.movingLeft
        // (NONPUBLIC, read by CardReader.StrafeMovingLeft). The game sets it
        // at the START of an attempt and never after the card arrives
        // (Strafe.DoStrafe, Strafe.cs:30), so straight after a move it still
        // points the way the card just went. That is the answer in every
        // case but one: a card that has just arrived at the END of the row.
        // There is no slot beyond it - BoardManager.GetAdjacent, PUBLIC,
        // returns null - and all three DoStrafe versions (Strafe, StrafePush,
        // StrafeSwap) turn the arrow round when that is so. The end of the
        // row cannot change before the next attempt, so "the other way" is
        // certain there, although the arrow on screen has not turned yet.
        //
        // WHAT IT DOES NOT DO: look at whether the next slot is occupied. A
        // Sprinter turns round when it is, but that is decided at the next
        // turn's end from the board as it is THEN, and each sigil in the
        // family has its own rule for "occupied" (Hefty pushes, Rampager
        // swaps). Guessing it here would be reimplementing the sigil. So
        // with a card in the way this says the arrow's direction, and the
        // next move line says what happened.
        //
        // Returns "" when the arrow cannot be read: the move line stands on
        // its own, as before.
        // ==================================================================
        private static string NextStrafeDirection(PlayableCard mover, string name)
        {
            try
            {
                bool? arrow = CardReader.StrafeMovingLeft(mover);
                if (arrow == null || mover == null || mover.Slot == null) return "";

                bool nextLeft = arrow.Value;

                var bm = Singleton<BoardManager>.Instance;
                if (bm == null) return "";
                CardSlot ahead = bm.GetAdjacent(mover.Slot, nextLeft);
                bool atEnd = ahead == null;
                if (atEnd) nextLeft = !nextLeft;

                // Session 51 (0.7.463). With a card standing in the slot it
                // points at: Zamar, "It will try to move left next." The board
                // is read as it is NOW; nothing is predicted. Plain Sprinter
                // movers only - Hefty pushes and Rampager swaps, so a card in
                // their way does not stop them (StrafePush, StrafeSwap).
                bool inTheWay = false;
                try
                {
                    CardSlot next = bm.GetAdjacent(mover.Slot, nextLeft);
                    inTheWay = next != null && next.Card != null
                               && !mover.HasAbility(Ability.StrafePush)
                               && !mover.HasAbility(Ability.StrafeSwap);
                }
                catch { }

                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL: '{name}' arrow points {(arrow.Value ? "left" : "right")}" +
                    (atEnd ? ", at the end of the row - it turns round next." : ".") +
                    (inTheWay ? " A card is in the way." : ""));

                return inTheWay ? Vocabulary.Sigils.ItWillTryToMoveNext(nextLeft)
                                : Vocabulary.Sigils.ItWillMoveNext(nextLeft);
            }
            catch { return ""; }
        }

        // ==================================================================
        // BLOODLUST ACTUALLY FIRING. (0.7.185.)
        //
        // Zamar: "I sucessfully triggered Bloodlust with my Wolverine but
        // nothing was called out. We need two things." The first — naming the
        // sigil on the attack — lives with Touch of Death in DamageRecord, in
        // the same list, because he asked for it "similar (and in addition
        // to)" that one. This is the second: the moment it PAYS OFF.
        //
        //   "Wolverine's Bloodlust triggers, it now has [x] power."
        //
        // WHY THE POWER NUMBER IS THE POINT. A buff that is never read back is
        // a buff the player has to track by arithmetic across turns. The card
        // read would tell him if he asked, but he has to know to ask — and the
        // moment it changes is the moment it is cheapest to hear.
        //
        // The sigil is Ability.GainAttackOnKill; the rulebook calls it
        // Bloodlust, and that is the name spoken, from rulebookName as always.
        //
        // READ AT SPEAK TIME. The prefix fires at enumerator creation, BEFORE
        // the attack is added — reading Attack there would report the old
        // value, which is the whole failure mode this project keeps meeting.
        // ==================================================================
        internal static void NoteBloodlustTriggered(GainAttackOnKill behaviour)
        {
            if (behaviour == null) return;

            PlayableCard card = null;
            try { card = behaviour.GetComponent<PlayableCard>(); } catch { }
            if (card == null) return;

            int before = -1;
            try { before = card.Attack; } catch { }

            var captured = card;
            int capturedBefore = before;

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                string name = null;
                int now = -1;
                try
                {
                    name = CardReader.CardName(captured);
                    now  = captured.Attack;
                }
                catch { }

                if (string.IsNullOrEmpty(name) || now < 0) return null;

                string sigil = null;
                try { sigil = CardReader.GetAbilityName(Ability.GainAttackOnKill); } catch { }
                if (string.IsNullOrEmpty(sigil)) sigil = Vocabulary.Sigils.Bloodlust;

                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL: {sigil} on '{name}' — power {capturedBefore} -> {now}.");

                // HIS WORDING. Note it says what the card HAS now rather than
                // what it gained: the absolute number is what he needs to plan
                // the next trade, and it is true regardless of how many times
                // the sigil has fired this turn.
                // 0.7.227 — the one construction. "triggers, it now has" ->
                // "ability triggers: it now has". See NoteStrafeMove.
                return Vocabulary.Sigils.SAbilityTriggersItNow(name, sigil, now);
            });
        }
        // ==================================================================
        // BROOD PARASITE — the Cuckoo laying its egg. (0.7.190.)
        //
        // Zamar, Prospector fight, on the generic 0.7.186 line:
        //
        //   "Cuckoo's Brood Parasite ability triggers."
        //   At the end add "placing a Broken Egg in enemy Slot [x]."
        //
        // The generic line is the cause with no consequence, and here the
        // consequence is a card appearing on the other side of the table — the
        // single most board-relevant thing the sigil does.
        //
        // WHAT THE GAME ACTUALLY DOES, from IL (dumps/dump_negation.txt,
        // CreateEgg/<OnResolveOnBoard>d__4.MoveNext):
        //
        //     opposingSlot = Card.Slot.opposingSlot
        //     if (opposingSlot.Card == null)
        //         cardId = ravenEgg ? "RavenEgg" : "BrokenEgg"
        //         BoardManager.CreateCardInSlot(CardLoader.GetCardByName(cardId),
        //                                       opposingSlot)
        //     else
        //         Card.Anim.StrongNegationEffect()          <- it fizzles
        //
        // TWO THINGS THAT WOULD HAVE BEEN WRONG AS LITERALS.
        //
        // 1. IT IS NOT ALWAYS A BROKEN EGG. There is a seeded roll, and on a
        //    hit the card is a Raven Egg instead. So the name is READ OFF THE
        //    SLOT at speak time, after the card exists — the game's own
        //    DisplayedNameLocalized, like every other card name IKMA speaks.
        //
        // 2. IT IS NOT ALWAYS AN ENEMY SLOT. His line says "enemy Slot", which
        //    is true of HIS Cuckoo. An opponent's Cuckoo lays into one of HIS
        //    slots, and calling that "enemy Slot" would be a false callout of
        //    exactly the kind 0.7.189 just removed. The side word follows the
        //    slot: "enemy Slot 2" or "your Slot 2".
        //
        // The empty-slot check is the game's branch, so nothing is predicted:
        // if the slot was occupied the egg was never laid, and the line says so
        // with the fizzle wording he approved.
        // ==================================================================
        /// <summary>
        /// Long enough for CreateCardInSlot to have put the egg in the slot.
        /// The egg is created inside the same coroutine this prefix fires
        /// from; a plain deferred compose read the slot before it existed.
        /// Nothing here is reaction-timed. (0.7.218.)
        /// </summary>
        private const float EGG_SETTLE_SECONDS = 0.6f;

        // ==================================================================
        // CORPSE EATER. (0.7.304.)
        //
        // The only sigil narrated here that fires from the HAND, on another
        // card's death. CorpseEater.OnOtherCardDie is handed the dead card,
        // the slot it died in, whether it was combat and the killer — so
        // every value the sentence needs is an argument, captured in the
        // prefix rather than read back off a board that is mid-change.
        //
        // The game has already decided by the time this runs: its
        // RespondsToOtherCardDie requires fromCombat, a PLAYER slot, that
        // slot now empty, and this card still in hand. So the line does not
        // repeat those tests — it reports what the game committed to.
        //
        // Ability.CorpseEater is silent in SigilTriggers so the generic line
        // does not fire alongside this one.
        // ==================================================================
        internal static void NoteCorpseEater(CorpseEater behaviour, PlayableCard deadCard, CardSlot deathSlot)
        {
            if (behaviour == null) return;

            PlayableCard eater = null;
            try { eater = behaviour.GetComponent<PlayableCard>(); } catch { }

            string eaterName = null, deadName = null;
            try { eaterName = CardReader.CardName(eater?.Info); } catch { }
            try { deadName  = CardReader.CardName(deadCard?.Info); } catch { }

            int slotIndex = -1;
            try { slotIndex = deathSlot != null ? deathSlot.Index : -1; } catch { }

            if (string.IsNullOrEmpty(eaterName) || string.IsNullOrEmpty(deadName) || slotIndex < 0)
            {
                Plugin.Log?.LogInfo(
                    "IKMA SIGIL: Corpse Eater fired but the dead card, the eater or the slot " +
                    "could not be read — not spoken.");
                return;
            }

            int slotNumber = slotIndex + 1;
            Plugin.Log?.LogInfo(
                $"IKMA SIGIL: Corpse Eater on '{eaterName}' triggered by '{deadName}' dying in slot {slotNumber}.");

            // 0.7.438 - this line is the arrival. The death line must not
            // add its own "is played ... by Ability" clause for the same card.
            SlotArrival.SuppressArrival(deathSlot);

            using (Speech.Event(EventKind.Powers)) Speech.Result(
                Vocabulary.CorpseEaterTriggers(deadName, eaterName, slotNumber));
        }

        internal static void NoteBroodParasite(CreateEgg behaviour)
        {
            if (behaviour == null) return;

            PlayableCard cuckoo = null;
            CardSlot target     = null;

            try
            {
                cuckoo = behaviour.GetComponent<PlayableCard>();
                target = cuckoo?.Slot?.opposingSlot;
            }
            catch { }

            if (cuckoo == null || target == null) return;

            bool occupiedAtTrigger = false;
            try { occupiedAtTrigger = target.Card != null; } catch { }

            int slotIndex = -1;
            bool enemySide = false;
            try
            {
                slotIndex = target.Index;
                enemySide = !target.IsPlayerSlot;
            }
            catch { }

            Plugin.Log?.LogInfo(
                $"IKMA SIGIL: Brood Parasite on '{CardReader.CardName(cuckoo)}' " +
                $"targeting slot {slotIndex + 1} (enemy={enemySide}), occupied at " +
                $"trigger = {occupiedAtTrigger}.");

            if (slotIndex < 0) return;

            var capturedCuckoo = cuckoo;
            var capturedTarget = target;
            int capturedIndex  = slotIndex;
            bool capturedEnemy = enemySide;
            bool capturedBusy  = occupiedAtTrigger;

            // SETTLE BEFORE READING THE EGG. (0.7.218.)
            //
            // Zamar, 0.7.217: "Cuckoo's Brood Parasite ability triggers.
            // Doing.....what? It should have specified it played the egg in
            // slot 3 here after saying it triggered."
            //
            // The right line was already written here and never fired. The
            // log shows why:
            //
            //   IKMA SIGIL: Brood Parasite ... targeting slot 3, occupied = False
            //   IKMA SPEAK: Cuckoo's Brood Parasite ability triggers.
            //
            // "occupied = False" means the egg WAS laid, so the fizzle branch
            // was not taken — the composer fell through to the generic line
            // because capturedTarget.Card was still null when it ran. Deferred
            // is not the same as settled: BoardManager.CreateCardInSlot is the
            // BODY of the coroutine this prefix fired from, so at compose time
            // the slot is legitimately empty and the egg arrives a moment
            // later. The board diff saw it (enemy Broken Egg@3) two lines on.
            //
            // A delayed compose is the mechanism the rest of the mod already
            // uses for exactly this, and nothing here is reaction-timed.
            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                string name = null;
                try { name = CardReader.CardName(capturedCuckoo); } catch { }
                if (string.IsNullOrEmpty(name)) return null;

                string sigil = null;
                try { sigil = CardReader.GetAbilityName(Ability.CreateEgg); } catch { }
                if (string.IsNullOrEmpty(sigil)) return null;

                string side = Vocabulary.Sigils.EnemyOrYour(capturedEnemy);

                // The slot was already taken, so the game shook the card instead
                // of laying anything. ZAMAR'S FIZZLE WORDING, approved 2026-09-12.
                if (capturedBusy)
                    return Vocabulary.SigilFizzles(name, sigil);

                // Read the egg off the board rather than naming it. Broken Egg
                // usually, Raven Egg on the roll.
                string egg = null;
                try { egg = CardReader.CardName(capturedTarget.Card?.Info); } catch { }

                // 0.7.341 — THIS LINE ANNOUNCES THE EGG, so the board diff must
                // not announce it again. Zamar's 0.7.340 log: "Enemy Broken Egg
                // is played in slot 2." at the next turn change — "This egg
                // callout was inaccurate. Already covered from the ability
                // trigger earlier." Regression pattern 4: every path that
                // announces a card arriving calls NoteAnnounced.
                try
                {
                    var eggCard = capturedTarget.Card;
                    if (eggCard != null) BoardWatcher.NoteAnnouncedAtSlot(eggCard, capturedIndex);
                }
                catch { }

                // ZAMAR'S WORDING, approved 2026-09-12, with the side word made
                // to follow the slot.
                if (string.IsNullOrEmpty(egg))
                    return Vocabulary.SigilTriggers(name, sigil);

                // 0.7.227 — comma to colon, "placing" to "it places", so the
                // consequence is a clause of its own. See NoteStrafeMove.
                return Vocabulary.Sigils.SAbilityTriggersItPlaces(name, sigil, egg, side, capturedIndex + 1);
            }, EGG_SETTLE_SECONDS);
        }

        // ==================================================================
        // THE CAGE BREAKS. (0.7.196.)
        //
        // Zamar, Prospector fight:
        //
        //   "When Caged Wolf dies we need a call out that a Wolf is played in
        //    that space upon death."
        //
        // THE CAGED WOLF IS AN ICE CUBE. The Caged Wolf has no sigil of its
        // own; it carries Ability.IceCube, whose rulebook name is the one
        // spoken below. Three separate signs pointed here before the class was
        // read (dumps/dump_cagedwolf.txt): <BreakCage>d__3 appears among the
        // callers of BoardManager.CreateCardInSlot, CardInfo carries an
        // iceCubeParams field beside evolveParams, and IceCube is one of the 46
        // classes that reach PreSuccessfulTriggerSequence.
        //
        // WHAT IT DOES, from IL — IceCube/<OnDie>d__4.MoveNext:
        //
        //     creature = Card.Info.iceCubeParams.creatureWithin
        //                ?? DEFAULT_CREATURE ("Opossum")
        //     BoardManager.CreateCardInSlot(CardLoader.GetCardByName(creature),
        //                                   Card.Slot)
        //
        // So the newcomer lands in the DYING CARD'S OWN SLOT, and the cage
        // decides what comes out. RespondsToDie gates on Card.OnBoard, so a
        // cage that dies in hand releases nothing.
        //
        // ONE PATCH, EVERY CAGE. Nothing here is Caged-Wolf-specific. Any card
        // carrying this sigil — the Ice Cube itself, anything a future update
        // wraps the same way — reads correctly without being found first,
        // because the creature is asked for rather than named.
        //
        // AND IT IS READ OFF THE BOARD, not off iceCubeParams. The params say
        // what SHOULD come out; the slot says what DID. That difference has
        // already mattered twice this session — the Cuckoo's egg is a Raven Egg
        // on a roll, and Guardian's "blocked" line was false because it trusted
        // a prediction over the board.
        // ==================================================================
        internal static void NoteCageBroken(IceCube behaviour)
        {
            if (behaviour == null) return;

            PlayableCard cage = null;
            CardSlot slot     = null;

            try
            {
                cage = behaviour.GetComponent<PlayableCard>();
                slot = cage?.Slot;
            }
            catch { }

            if (cage == null || slot == null) return;

            int slotIndex  = -1;
            bool enemySide = false;
            string cageName = null;

            try
            {
                slotIndex = slot.Index;
                enemySide = !slot.IsPlayerSlot;
                cageName  = CardReader.CardName(cage);
            }
            catch { }

            if (slotIndex < 0 || string.IsNullOrEmpty(cageName)) return;

            Plugin.Log?.LogInfo(
                $"IKMA SIGIL: cage '{cageName}' broken in slot {slotIndex + 1} " +
                $"(enemy={enemySide}).");

            // Session 48 (0.7.458), Zamar, on "...and the ice is destroyed.
            // Opossum is played in Slot 4." followed by this narrator's own
            // line: "Just that second line not the first." This line is the
            // arrival; the death line keeps the attack and the ice and drops
            // its "is played" sentence. Same door as Corpse Eater (0.7.438).
            try { SlotArrival.SuppressArrival(slot); } catch { }

            var capturedSlot  = slot;
            string capturedCage = cageName;
            int capturedIndex = slotIndex;
            bool capturedEnemy = enemySide;
            var capturedCard  = cage;

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                PlayableCard freed = null;
                try { freed = capturedSlot.Card; } catch { }

                // Still the cage, or nothing at all. The game creates the
                // creature inside the same coroutine, so by the time this
                // composes it is either there or it never came.
                if (freed == null || ReferenceEquals(freed, capturedCard)) return null;

                string freedName = null;
                try { freedName = CardReader.CardName(freed); } catch { }
                if (string.IsNullOrEmpty(freedName)) return null;

                string sigil = null;
                try { sigil = CardReader.GetAbilityName(Ability.IceCube); } catch { }
                if (string.IsNullOrEmpty(sigil)) return null;

                // The board diff would otherwise report the newcomer a second
                // time at the next turn boundary.
                try { BoardWatcher.NoteAnnounced(freed); } catch { }

                string side = Vocabulary.Sigils.EnemyOrYour(capturedEnemy);

                // IKMA WORDING: PROVISIONAL. He asked for the event, not the
                // sentence. The shape is the Brood Parasite line he approved on
                // 2026-09-12 — sigil named, newcomer named, slot and side — so
                // the two read as one family until he replaces it.
                // 0.7.227 — the one construction. See NoteStrafeMove.
                // Zamar's wording, Session 25.
                string article = Vocabulary.Sigils.AnOrALower("AEIOUaeiou".IndexOf(freedName[0]));
                string line = Vocabulary.Sigils.SAbilityTriggersA(capturedCage, sigil, article, freedName, side, capturedIndex + 1);

                return line;
            });
        }

        // ==================================================================
        // THE CARD BECOMES SOMETHING ELSE. (0.7.203.)
        //
        // Zamar, Prospector fight, on the generic 0.7.186 line:
        //
        //   "Wolf's Fledgling ability triggers."
        //   "This seemed out of order. I expect to hear 'Enemy Wolf Pup's
        //    Fledgling ability triggers, transforming it into a [power] /
        //    [toughness] [cardname], with [abilities]' before it attacks as its
        //    transformed self."
        //
        // IT WAS NOT OUT OF ORDER, IT WAS MISNAMED, and that is worse. The
        // generic line reads the card's name at SPEAK time, by which point the
        // Wolf Pup had already become a Wolf — so the sentence said the Wolf
        // triggered the sigil that created it. Effect naming cause.
        //
        // So the OLD name is captured in the prefix and the NEW one read after.
        // Both halves of the sentence are observed; neither is predicted.
        //
        // ONE PATCH, BOTH SIGILS. Transformer derives from Evolve and overrides
        // only GetTransformCardInfo and RespondsToUpkeep — NOT OnUpkeep
        // (dumps/dump_cagedwolf.txt). So Fledgling and the Transformer sigil
        // share this one coroutine.
        //
        // AND IT FIRES EVERY UPKEEP, NOT ONLY THE LAST. Evolve counts turns and
        // transforms when numTurnsInPlay reaches evolveParams.turnsToEvolve, so
        // most calls change nothing. The name comparison at speak time IS the
        // test for "did it actually transform" — no counter is tracked here and
        // no turn is predicted.
        // ==================================================================
        // ==================================================================
        // SESSION 46 - THE IJIRAQ SHOWS ITSELF. (0.7.453.)
        //
        // Zamar: 'When played, "Strange [card name] transforms into The
        // Ijiraq."'
        //
        // In the hand the Ijiraq looks exactly like a card from the draw pile
        // (Shapeshifter.DisguiseInBattle; no red eyes there). When it lands
        // on the board, Shapeshifter.OnResolveOnBoard - PUBLIC, declared on
        // Shapeshifter - plays a sound and calls PlayableCard.
        // TransformIntoCard with the Ijiraq's own card, about half a second
        // later. Until now IKMA said nothing: the board read simply had a
        // different card in the slot.
        //
        // The prefix runs when that coroutine is CREATED, while the card
        // still wears its disguise, so the name is taken here. The line
        // reserves its place now and is spoken once the card's info really is
        // the Ijiraq's; if that never happens, nothing is said.
        // ==================================================================
        private const string IJIRAQ = "Ijiraq";
        private static PlayableCard _ijiraqLastCard;
        private static float _ijiraqLastAt = -999f;

        internal static void NoteIjiraqReveal(Shapeshifter behaviour)
        {
            if (behaviour == null) return;

            PlayableCard card = null;
            string disguise = null;
            try
            {
                card = behaviour.GetComponent<PlayableCard>();
                if (card == null || card.Info == null || card.Info.name == IJIRAQ) return;
                disguise = CardReader.RevealedDisguiseName(card);
            }
            catch { }
            if (card == null || string.IsNullOrEmpty(disguise)) return;

            // One card, one line: two Shapeshifter components on one card
            // would both answer the same resolve.
            if (ReferenceEquals(_ijiraqLastCard, card) && UnityEngine.Time.unscaledTime - _ijiraqLastAt < 10f) return;
            _ijiraqLastCard = card;
            _ijiraqLastAt = UnityEngine.Time.unscaledTime;

            var captured = card;
            string capturedDisguise = disguise;
            Plugin.Log?.LogInfo($"IKMA IJIRAQ: '{capturedDisguise}' is on the board - the line waits for it to change.");

            using (Speech.Event(EventKind.Powers)) Speech.ResultWhenReady(
                () =>
                {
                    try { return captured == null || captured.Info == null || captured.Info.name == IJIRAQ; }
                    catch { return true; }
                },
                () =>
                {
                    try
                    {
                        if (captured == null || captured.Info == null || captured.Info.name != IJIRAQ)
                        {
                            Plugin.Log?.LogInfo("IKMA IJIRAQ: the card did not change in time - nothing said.");
                            return null;
                        }
                        return Vocabulary.Cards.UnusualTransformsInto(capturedDisguise, CardReader.CardName(captured));
                    }
                    catch { return null; }
                },
                3f, "Ijiraq reveal");
        }

        // ==================================================================
        // SESSION 47 - THE LONG ELK'S VERTEBRAE. (0.7.455.)
        //
        // Zamar: "Long Elk extends its vertebrae into slot [1]."
        //
        // Strafe.PostSuccessfulMoveSequence(CardSlot oldSlot) - NONPUBLIC,
        // protected virtual, declared on Strafe - runs straight after a
        // successful sprint and, for the card named "Snelk" with the old slot
        // empty, creates a "Snelk_Neck" there. The prefix runs when that
        // coroutine is created, with the same two facts in hand, so the line
        // is queued behind the Sprinter line and ahead of "Enemy turn." The
        // board diff used to find the card later and say "Your Vertebrae is
        // played in slot 1." inside the enemy's sentence; it now leaves
        // Snelk_Neck to this line (BoardWatcher).
        // ==================================================================
        internal static void NoteVertebrae(Strafe behaviour, CardSlot oldSlot)
        {
            if (behaviour == null || oldSlot == null) return;

            try
            {
                var mover = behaviour.GetComponent<PlayableCard>();
                if (mover == null || mover.Info == null || mover.Info.name != "Snelk") return;
                if (oldSlot.Card != null) return;

                string name = CardReader.CardName(mover);
                if (string.IsNullOrEmpty(name)) return;

                int slotNumber = oldSlot.Index + 1;
                Plugin.Log?.LogInfo($"IKMA SIGIL: '{name}' leaves a Vertebrae in slot {slotNumber}.");

                string line = Vocabulary.Sigils.ExtendsItsVertebraeInto(name, slotNumber);
                using (Speech.Event(EventKind.Powers)) Speech.Result(() => line);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA SIGIL: vertebrae line failed: {e.Message}");
            }
        }

        // ==================================================================
        // SESSION 48 - CHILD 13 SACRIFICED, AND THE GREAT KRAKEN COMING UP.
        // (0.7.458.) Both were silent in the Session 48 driver run.
        //
        // Zamar: "Child 13 survives the sacrifice and transforms."
        // JerseyDevil.OnSacrifice (PUBLIC, declared on JerseyDevil) counts the
        // sacrifices in the NONPUBLIC int sacrificeCount. Below the NONPUBLIC
        // int MAX_SACRIFICES (13) each one swaps the card between its two
        // forms; the thirteenth only adds a decal and the fourteenth kills
        // it. The prefix runs when the coroutine is created, before the count
        // goes up, so the sentence is said for the first twelve and for
        // nothing else: the thirteenth does not transform and has no ruling.
        //
        // Zamar: "Great Kraken transforms." SubmergeSquid.OnResurface
        // (NONPUBLIC, protected override void, declared on SubmergeSquid) sets
        // the card to one of three tentacles. Prefix, so the name read is
        // still the Kraken's.
        // dumps/dump_s48_from_decompile.txt.
        // ==================================================================
        private static System.Reflection.FieldInfo _jerseyCountField;
        private static System.Reflection.FieldInfo _jerseyMaxField;
        private static bool _jerseyFieldsResolved;

        internal static void NoteChild13Sacrificed(JerseyDevil behaviour)
        {
            if (behaviour == null) return;

            try
            {
                var card = behaviour.GetComponent<PlayableCard>();
                if (card == null) return;

                if (!_jerseyFieldsResolved)
                {
                    _jerseyFieldsResolved = true;
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    _jerseyCountField = typeof(JerseyDevil).GetField("sacrificeCount", flags);
                    _jerseyMaxField   = typeof(JerseyDevil).GetField("MAX_SACRIFICES", flags);
                    if (_jerseyCountField == null || _jerseyMaxField == null)
                        Plugin.Log?.LogWarning("IKMA SIGIL: JerseyDevil's sacrifice count did not resolve - its sacrifice stays unspoken.");
                }
                if (_jerseyCountField == null || _jerseyMaxField == null) return;

                int thisOne = (int)_jerseyCountField.GetValue(behaviour) + 1;
                int max     = (int)_jerseyMaxField.GetValue(behaviour);

                string name = CardReader.CardName(card);
                if (string.IsNullOrEmpty(name)) return;

                if (thisOne >= max)
                {
                    Plugin.Log?.LogInfo($"IKMA SIGIL: '{name}' sacrifice {thisOne} of {max} - no change of form, not spoken.");
                    return;
                }

                Plugin.Log?.LogInfo($"IKMA SIGIL: '{name}' sacrifice {thisOne} of {max} - it survives and changes form.");

                string line = Vocabulary.Sigils.SurvivesTheSacrificeAndTransforms(name);
                using (Speech.Event(EventKind.Powers)) Speech.Result(() => line);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA SIGIL: Child 13 sacrifice line failed: {e.Message}");
            }
        }

        internal static void NoteKrakenResurface(SubmergeSquid behaviour)
        {
            if (behaviour == null) return;

            try
            {
                var card = behaviour.GetComponent<PlayableCard>();
                if (card == null) return;

                string name = CardReader.CardName(card);
                if (string.IsNullOrEmpty(name)) return;

                Plugin.Log?.LogInfo($"IKMA SIGIL: '{name}' comes back up as a tentacle.");

                string line = Vocabulary.Sigils.Transforms(name);
                using (Speech.Event(EventKind.Powers)) Speech.Result(() => line);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA SIGIL: Kraken resurface line failed: {e.Message}");
            }
        }

        internal static void NoteTransformed(Evolve behaviour, bool playerUpkeep)
        {
            if (behaviour == null) return;

            PlayableCard card = null;
            Ability sigil     = Ability.None;
            string oldName    = null;
            bool isEnemy      = false;

            try
            {
                card    = behaviour.GetComponent<PlayableCard>();
                sigil   = behaviour.Ability;
                oldName = CardReader.CardName(card?.Info);
                isEnemy = card != null && card.OpponentCard;
            }
            catch { }

            if (card == null || string.IsNullOrEmpty(oldName)) return;

            // 0.7.459 - two or more of one card evolving in the same upkeep
            // are one line now. See EvolveBatch below. If the patch that
            // reports each card's change did not apply, every card keeps its
            // own line, composed below exactly as before.
            if (EvolveEndWatched)
            {
                EvolveBatch.Note(card, sigil, oldName, isEnemy, playerUpkeep);
                return;
            }

            var capturedCard = card;
            var capturedSigil = sigil;
            string capturedOld = oldName;
            bool capturedEnemy = isEnemy;

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                string newName = null;
                int atk = 0, hp = 0;

                try
                {
                    newName = CardReader.CardName(capturedCard);

                    // LIVE stats, never CardInfo — the standing rule.
                    atk = capturedCard.Attack;
                    hp  = capturedCard.Health;
                }
                catch { }

                if (string.IsNullOrEmpty(newName)) return null;

                // Nothing happened this upkeep. The counter has not run out yet.
                if (newName == capturedOld) return null;

                string sigilName = null;
                try { sigilName = CardReader.GetAbilityName(capturedSigil); } catch { }
                if (string.IsNullOrEmpty(sigilName)) return null;

                // FormatBareAbilitiesFor returns ", A, B" or "". His sentence
                // wants ", with A, B", so the leading separator is rewritten
                // rather than the collection being rebuilt here — that helper
                // already folds in granted sigils, which a fresh list would miss.
                string abilities = "";
                try { abilities = BoardReader.FormatBareAbilitiesFor(capturedCard); } catch { }

                string withClause = "";
                if (!string.IsNullOrEmpty(abilities) && abilities.StartsWith(", "))
                    withClause = Vocabulary.Sigils.WithAbilities(abilities.Substring(2));

                // The board diff would otherwise report the new card arriving.
                try { BoardWatcher.NoteAnnounced(capturedCard); } catch { }

                string who = capturedEnemy ? Vocabulary.Sigils.Enemy : "";

                // ZAMAR'S WORDING, 2026-09-13:
                //   "Enemy Wolf Pup's Fledgling ability triggers, transforming
                //    it into a 3/2 Wolf."
                // and, on the no-sigil case: "Drop the clause entirely yeah. If
                // the transformed card has no sigils, then nothing to read."
                // 0.7.227 — comma to colon, "transforming" to "it becomes".
                // His 2026-09-13 sentence carried the same facts in the older
                // shape; only the join changes. See NoteStrafeMove.
                // 0.4.8.006 - Zamar, Session 57: the slot when a same-name card
                // is on that side ("Add the slot").
                int evSlot = -1;
                try { evSlot = capturedCard.Slot != null ? capturedCard.Slot.Index + 1 : -1; } catch { }
                if (evSlot > 0 && HasTwinOnSide(capturedCard, capturedOld))
                    return Vocabulary.Sigils.SAbilityTriggersInSlotItBecomes(who, capturedOld, sigilName, evSlot, atk, hp, newName, withClause);
                return Vocabulary.Sigils.SAbilityTriggersItBecomes(who, capturedOld, sigilName, atk, hp, newName, withClause);
            });
        }

        /// <summary>
        /// True once Evolve_OnUpkeep_Patch's Postfix is in place (Plugin sets it
        /// from TryPatch's answer). Without that patch nothing reports a
        /// card's change, so EvolveBatch is not used.
        /// </summary>
        internal static bool EvolveEndWatched;

        // ==================================================================
        // SESSION 49 (0.7.459) - SEVERAL OF ONE CARD EVOLVING TOGETHER.
        //
        // Zamar, Session 48, on "Enemy Elk Fawn's Fledgling ability triggers:
        // it becomes a 2/4 Elk, with Sprinter." said twice running: "Add both
        // or All (for 3+) for multiples and condense the lines to 1 line. 'All
        // enemy Elk Fawn's Fledgling abilities trigger.'" Asked whether the
        // second half is dropped: "They each become 2/4 Elks, with Sprinter."
        //
        // The game runs Evolve.OnUpkeep for one card at a time, each a second
        // or more behind the last, so one prefix per card made one line per
        // card. Now the FIRST card of an upkeep reserves a single place in
        // the queue, the rest only add themselves to it, and the line is held
        // until every one of them has settled. SigilTriggers.DiveBatch is the
        // model.
        //
        // WHO ELSE EVOLVES is the game's own answer, asked once when the
        // place is reserved: Evolve.RespondsToUpkeep (PUBLIC; Transformer
        // overrides it) for every card on the board, with the playerUpkeep
        // value the game passed to OnUpkeep.
        //
        // WHEN A CARD HAS SETTLED is observed, never timed: WatchEvolve wraps
        // the enumerator and reports the step on which the card's name has
        // changed, or the step that ends it with no change. Not the END of
        // the coroutine: after the change the game calls LearnAbility, which
        // for an ability the save has not met holds on Leshy's explanation
        // until the player presses a key.
        //
        // Evolve.OnUpkeep runs EVERY upkeep and changes nothing on most of
        // them. A batch in which no card changed says nothing, and its place
        // in the queue is not logged.
        //
        // ONLY CARDS THAT ENDED UP THE SAME ARE CONDENSED: same side, old
        // name and sigil, and the same new name, power, health and abilities.
        // "They each become 2/4 Elks" would be false if one of them were 3/4.
        // Every other card keeps his one-card sentence.
        // ==================================================================
        private static class EvolveBatch
        {
            private class Member
            {
                internal PlayableCard Card;
                internal Ability Sigil;
                internal string OldName;
                internal bool Enemy;
                internal bool Settled;
            }

            private class Group
            {
                internal bool Enemy;
                internal string OldName, SigilName, NewName, WithClause;
                internal int Attack, Health, Count;
                internal int Slot;     // 0.4.8.006
                internal bool Twin;    // 0.4.8.006 - a same-name card stays on that side
            }

            private static int SlotNumber(PlayableCard c)
            {
                try { return c?.Slot != null ? c.Slot.Index + 1 : -1; } catch { return -1; }
            }

            private static readonly System.Collections.Generic.List<Member> _members =
                new System.Collections.Generic.List<Member>();
            private static System.Collections.Generic.List<PlayableCard> _expected;
            private static bool _pending;
            private static float _reservedAt = -99f;

            // The queue gives up waiting after MaxWait and says what it has.
            // StaleAfter covers a reserved line that was cut from the queue
            // (the silence key, a scene change) and so never composed.
            private const float MaxWait = 8f;
            private const float StaleAfter = 12f;

            internal static void Note(PlayableCard card, Ability sigil, string oldName,
                                      bool enemy, bool playerUpkeep)
            {
                if (_pending && UnityEngine.Time.unscaledTime - _reservedAt > StaleAfter) Reset();

                _members.Add(new Member { Card = card, Sigil = sigil, OldName = oldName, Enemy = enemy });
                if (_pending) return;

                _pending = true;
                _reservedAt = UnityEngine.Time.unscaledTime;
                _expected = ExpectedEvolvers(card, playerUpkeep);

                // No label: this runs every upkeep for every card with the
                // sigil, and a place in the queue that will say nothing is
                // not a line for the log.
                using (Speech.Event(EventKind.Powers)) Speech.ResultWhenReady(
                    AllSettled, Compose, MaxWait, null);
            }

            /// <summary>The card has changed, or its upkeep ended unchanged.</summary>
            internal static void NoteSettled(PlayableCard card)
            {
                if (ReferenceEquals(card, null)) return;
                for (int i = _members.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(_members[i].Card, card)) continue;
                    _members[i].Settled = true;
                    return;
                }
            }

            /// <summary>Every card the game says evolves in this upkeep.</summary>
            private static System.Collections.Generic.List<PlayableCard> ExpectedEvolvers(
                PlayableCard first, bool playerUpkeep)
            {
                var expected = new System.Collections.Generic.List<PlayableCard> { first };
                try
                {
                    // Asked once per batch, on an evolving card's own trigger,
                    // so the board is known to be there.
                    var board = Singleton<BoardManager>.Instance;
                    if (board == null) return expected;

                    for (int side = 0; side < 2; side++)
                    {
                        foreach (var slot in board.GetSlots(side == 0))
                        {
                            var other = slot != null ? slot.Card : null;
                            if (other == null || other.Dead || expected.Contains(other)) continue;

                            var evolve = other.GetComponent<Evolve>();
                            if (evolve != null && evolve.RespondsToUpkeep(playerUpkeep)) expected.Add(other);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Plugin.Log?.LogWarning($"IKMA SIGIL: evolve batch - {e.GetType().Name}: {e.Message}");
                }
                return expected;
            }

            private static bool AllSettled()
            {
                try
                {
                    if (_expected == null) return true;
                    foreach (var c in _expected)
                    {
                        if (c == null || c.Dead) continue;

                        bool settled = false;
                        for (int i = 0; i < _members.Count; i++)
                        {
                            if (ReferenceEquals(_members[i].Card, c) && _members[i].Settled)
                            {
                                settled = true;
                                break;
                            }
                        }
                        if (!settled) return false;
                    }
                }
                catch { }
                return true;
            }

            private static string Compose()
            {
                var members = new System.Collections.Generic.List<Member>(_members);
                Reset();

                var groups = new System.Collections.Generic.List<Group>();
                foreach (var m in members)
                {
                    try
                    {
                        var c = m.Card;

                        // Gone from the board since its upkeep: left out
                        // rather than reported as a card that is not there.
                        if (c == null || c.Dead) continue;

                        string newName = CardReader.CardName(c);
                        if (string.IsNullOrEmpty(newName)) continue;

                        // Nothing happened this upkeep. The counter has not
                        // run out yet.
                        if (newName == m.OldName) continue;

                        string sigilName = CardReader.GetAbilityName(m.Sigil);
                        if (string.IsNullOrEmpty(sigilName)) continue;

                        // LIVE stats, never CardInfo - the standing rule.
                        int atk = c.Attack;
                        int hp  = c.Health;

                        // FormatBareAbilitiesFor returns ", A, B" or "". His
                        // sentence wants ", with A, B".
                        string abilities = "";
                        try { abilities = BoardReader.FormatBareAbilitiesFor(c); } catch { }

                        string withClause = "";
                        if (!string.IsNullOrEmpty(abilities) && abilities.StartsWith(", "))
                            withClause = Vocabulary.Sigils.WithAbilities(abilities.Substring(2));

                        // The board diff would otherwise report the new card
                        // arriving.
                        try { BoardWatcher.NoteAnnounced(c); } catch { }

                        Group match = null;
                        foreach (var g in groups)
                        {
                            if (g.Enemy == m.Enemy && g.OldName == m.OldName && g.SigilName == sigilName &&
                                g.NewName == newName && g.Attack == atk && g.Health == hp &&
                                g.WithClause == withClause)
                            {
                                match = g;
                                break;
                            }
                        }

                        if (match != null) { match.Count++; continue; }

                        groups.Add(new Group
                        {
                            Enemy = m.Enemy, OldName = m.OldName, SigilName = sigilName,
                            NewName = newName, WithClause = withClause,
                            Attack = atk, Health = hp, Count = 1,
                            Slot = SlotNumber(c), Twin = HasTwinOnSide(c, m.OldName),
                        });
                    }
                    catch { }
                }

                if (groups.Count == 0) return null;

                var lines = new System.Collections.Generic.List<string>();
                foreach (var g in groups)
                {
                    if (g.Count == 1)
                    {
                        // ZAMAR'S ONE-CARD SENTENCE, unchanged (0.7.227).
                        string who = g.Enemy ? Vocabulary.Sigils.Enemy : "";
                        // 0.4.8.006 - Zamar, Session 57: the slot when a twin stays.
                        lines.Add(g.Twin && g.Slot > 0
                            ? Vocabulary.Sigils.SAbilityTriggersInSlotItBecomes(
                                who, g.OldName, g.SigilName, g.Slot, g.Attack, g.Health, g.NewName, g.WithClause)
                            : Vocabulary.Sigils.SAbilityTriggersItBecomes(
                            who, g.OldName, g.SigilName, g.Attack, g.Health, g.NewName, g.WithClause));
                        continue;
                    }

                    Plugin.Log?.LogInfo(
                        $"IKMA SIGIL: {g.Count} '{g.OldName}' became '{g.NewName}' in one upkeep - one line.");

                    lines.Add(Vocabulary.Sigils.SeveralAbilitiesTriggerTheyEachBecome(
                        g.Count >= 3, g.Enemy, g.OldName, g.SigilName, g.Attack, g.Health,
                        Vocabulary.PluralName(g.NewName), g.WithClause));
                }

                return string.Join(" ", lines.ToArray());
            }

            private static void Reset()
            {
                _members.Clear();
                _expected = null;
                _pending = false;
            }
        }

        /// <summary>
        /// 0.7.459 - wraps Evolve.OnUpkeep's enumerator and tells EvolveBatch
        /// the step on which the card's name has changed, or the step that
        /// ends the coroutine with no change. The card is taken when the
        /// wrapper is made: the Evolve component itself is replaced when the
        /// card changes.
        /// </summary>
        internal static System.Collections.IEnumerator WatchEvolve(
            Evolve behaviour, System.Collections.IEnumerator inner)
        {
            PlayableCard card = null;
            string oldName = null;
            try
            {
                card    = behaviour.GetComponent<PlayableCard>();
                oldName = CardReader.CardName(card?.Info);
            }
            catch { }

            bool settled = false;
            while (true)
            {
                bool more;
                try { more = inner.MoveNext(); }
                catch
                {
                    if (!settled) EvolveBatch.NoteSettled(card);
                    throw;
                }

                if (!settled && !ReferenceEquals(card, null) && !string.IsNullOrEmpty(oldName))
                {
                    string now = null;
                    try { now = CardReader.CardName(card); } catch { }
                    if (!string.IsNullOrEmpty(now) && now != oldName)
                    {
                        settled = true;
                        EvolveBatch.NoteSettled(card);
                    }
                }

                if (!more) break;
                yield return inner.Current;
            }

            if (!settled) EvolveBatch.NoteSettled(card);
        }

        // ==================================================================
        // THE REST OF M5 — THE FIVE COMPOSERS NOTHING CALLED. (0.7.316.)
        //
        // AdjacentSpawnTriggers, LooseTailTriggers, AmorphousTriggers,
        // TidalLockTriggers and TrinketBearerTriggers have been Zamar's
        // approved wording in Vocabulary.cs since 0.7.304/305 with no patch
        // reaching them. Nothing here invents a word; this is the plumbing.
        //
        // Every hook below was read out of the decompiled source before it was
        // written, and the override question was asked of each one.
        // ==================================================================

        // ------------------------------------------------------------------
        // DAM BUILDER and CHIME — CreateCardsAdjacent. (0.7.316.)
        //
        // OnResolveOnBoard asks BoardManager.GetAdjacent for each neighbour,
        // keeps only the ones with no card in them, and calls the private
        // SpawnCardOnSlot ONCE PER SIDE. So the slots are never predicted
        // here: the batch opens when the coroutine is created and each spawn
        // reports the slot the game itself chose.
        //
        // CreateDams and CreateBells are the only subclasses and neither
        // overrides either method, so the base declarations are the ones that
        // run for both. (The override trap, asked.)
        //
        // BLOCKED ON BOTH SIDES IS THE FIZZLE, not a shortened version of this
        // line — the game spawns nothing and shows its own dialogue, once per
        // run. Vocabulary.AdjacentSpawnTriggers refuses an empty slot list by
        // design and the approved fizzle wording carries that case.
        // ------------------------------------------------------------------

        /// <summary>
        /// Long enough for PreSuccessfulTriggerSequence and BOTH spawns —
        /// 0.1s plus CreateCardInSlot's 0.15s, twice — to have landed. The
        /// game's own animation owns the table for that whole window, so
        /// nothing is held back that could have been heard sooner.
        /// </summary>
        private const float ADJACENT_SETTLE_SECONDS = 1.5f;

        private class AdjacentSpawnBatch
        {
            internal PlayableCard Card;
            internal Ability Ability;
            internal CardSlot FirstSlot;
            internal readonly System.Collections.Generic.List<int> Slots =
                new System.Collections.Generic.List<int>();
            // Session 48 (0.7.457): the slots themselves, so the cards that
            // landed in them can be handed to the board diff.
            internal readonly System.Collections.Generic.List<CardSlot> SlotRefs =
                new System.Collections.Generic.List<CardSlot>();
            internal float OpenedAt;
            internal bool Composed;
        }

        // Session 48 (0.7.457) - THE CHIMES ARE THE BELLIST LINE'S. Driver
        // log: "The Daus's Bellist ability triggers, placing a Chime in slot
        // 1 and slot 3." and then, a turn boundary later, "Your Chime is
        // played in slots 1 and 3." Nothing had told the board diff. The
        // line below notes each card it names; this answers the diff in the
        // second and a half before that line composes.
        internal static bool AdjacentSpawnOwns(PlayableCard card)
        {
            var batch = _adjacentBatch;
            if (batch == null || batch.Composed || ReferenceEquals(card, null)) return false;
            try
            {
                if (UnityEngine.Time.unscaledTime - batch.OpenedAt > 10f) return false;
                for (int i = 0; i < batch.SlotRefs.Count; i++)
                    if (ReferenceEquals(batch.SlotRefs[i]?.Card, card)) return true;
            }
            catch { }
            return false;
        }

        private static AdjacentSpawnBatch _adjacentBatch;

        internal static void NoteAdjacentSpawnStart(CreateCardsAdjacent behaviour)
        {
            if (behaviour == null) return;

            PlayableCard card = null;
            Ability ability   = Ability.None;
            try { card = behaviour.GetComponent<PlayableCard>(); } catch { }
            try { ability = behaviour.Ability; } catch { }

            if (card == null || ability == Ability.None) return;

            var batch = new AdjacentSpawnBatch { Card = card, Ability = ability };
            try { batch.OpenedAt = UnityEngine.Time.unscaledTime; } catch { }
            _adjacentBatch = batch;

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                batch.Composed = true;

                string name = null;
                try { name = CardReader.CardName(batch.Card?.Info); } catch { }

                string sigil = null;
                try { sigil = CardReader.GetAbilityName(batch.Ability); } catch { }

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(sigil)) return null;

                if (batch.Slots.Count == 0)
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA SIGIL: {sigil} on '{name}' spawned nothing — blocked on both sides.");
                    return Vocabulary.SigilFizzles(name, sigil);
                }

                // READ OFF THE SLOT, never named from SpawnedCardId — that is
                // an internal id, and an internal id is never a display name.
                string spawned = null;
                try { spawned = CardReader.CardName(batch.FirstSlot?.Card?.Info); } catch { }

                var numbers = new System.Collections.Generic.List<int>();
                for (int i = 0; i < batch.Slots.Count; i++) numbers.Add(batch.Slots[i] + 1);

                if (string.IsNullOrEmpty(spawned))
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA SIGIL: {sigil} on '{name}' spawned into {numbers.Count} slot(s), " +
                        "but the spawned card could not be read — generic line used.");
                    return Vocabulary.SigilTriggers(name, sigil);
                }

                string where = "";
                for (int i = 0; i < numbers.Count; i++)
                    where += (i == 0 ? "" : ", ") + numbers[i];

                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL: {sigil} on '{name}' placed '{spawned}' in slot(s) {where}.");

                // 0.7.457 - this line names them, so the board diff must not.
                for (int i = 0; i < batch.SlotRefs.Count; i++)
                {
                    try
                    {
                        var made = batch.SlotRefs[i]?.Card;
                        if (!ReferenceEquals(made, null) && !ReferenceEquals(made, batch.Card))
                            BoardWatcher.NoteAnnounced(made);
                    }
                    catch { }
                }

                return Vocabulary.AdjacentSpawnTriggers(name, sigil, spawned, numbers);
            }, ADJACENT_SETTLE_SECONDS);
        }

        internal static void NoteAdjacentSpawnSlot(CardSlot slot)
        {
            var batch = _adjacentBatch;
            if (batch == null || slot == null) return;

            try
            {
                if (batch.Slots.Contains(slot.Index)) return;
                batch.Slots.Add(slot.Index);
                batch.SlotRefs.Add(slot);
                if (batch.FirstSlot == null) batch.FirstSlot = slot;
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // LOOSE TAIL — TailOnHit.OnCardGettingAttacked. (0.7.316.)
        //
        // The coroutine moves the card into the free neighbour (right first,
        // then left) and leaves a tail behind in the slot it came from. The
        // destination is not derived here: the card is asked where it ended
        // up once the move has settled, which is the game's own answer.
        //
        // AND THE SAME READ IS THE GUARD. OnCardGettingAttacked is entered
        // even when both neighbours are occupied — the entire body sits inside
        // that test — so a card that has not moved dropped no tail, and the
        // line is withheld rather than spoken about a move that never was.
        //
        // THIS ALSO CLOSES A FALSE FIZZLE. TailOnHit calls StrongNegationEffect
        // on SUCCESS, twice, to shake the card and the tail. While the generic
        // line still owned this sigil, 0.7.305's fizzle watch read those shakes
        // as "the sigil achieved nothing", so a successful Loose Tail would
        // have announced itself as doing nothing. Ability.TailOnHit is silent
        // in SigilTriggers now, and no record is opened for a silent ability.
        //
        // THE MOVE IS NOT SUPPRESSED IN THE BOARD DIFF and that is deliberate
        // for one build: whether the differ also reports it cannot be answered
        // from source, so both are logged and the next log settles it.
        // ------------------------------------------------------------------
        private const float TAIL_SETTLE_SECONDS = 1.2f;

        internal static void NoteLooseTail(TailOnHit behaviour)
        {
            if (behaviour == null) return;

            PlayableCard card = null;
            try { card = behaviour.GetComponent<PlayableCard>(); } catch { }
            if (card == null) return;

            int originIndex = -1;
            try { originIndex = card.Slot != null ? card.Slot.Index : -1; } catch { }
            if (originIndex < 0) return;

            var capturedCard   = card;
            int capturedOrigin = originIndex;

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                string name = null;
                try { name = CardReader.CardName(capturedCard); } catch { }
                if (string.IsNullOrEmpty(name)) return null;

                int now = -1;
                try { now = capturedCard.Slot != null ? capturedCard.Slot.Index : -1; } catch { }

                if (now < 0 || now == capturedOrigin)
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA SIGIL: Loose Tail on '{name}' — still in slot " +
                        $"{capturedOrigin + 1}, so no tail was dropped. Not spoken.");
                    return null;
                }

                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL: Loose Tail on '{name}' moved slot {capturedOrigin + 1} " +
                    $"to slot {now + 1}.");

                return Vocabulary.LooseTailTriggers(name, now + 1);
            }, TAIL_SETTLE_SECONDS);
        }

        // ------------------------------------------------------------------
        // AMORPHOUS — RandomAbility.AddMod. (0.7.316.)
        //
        // A POSTFIX, and that is the whole point: the sigil the card gains does
        // not EXIST at trigger time. OnDrawn flips the card and hands AddMod in
        // as the callback, and AddMod is where the roll happens and the
        // temporary mod is attached. Before it returns there is nothing to
        // name.
        //
        // The granted sigil is read off the mod the game just added — never
        // re-rolled here. RandomAbility does not call
        // PreSuccessfulTriggerSequence, so there is no generic line to silence.
        // ------------------------------------------------------------------
        internal static void NoteAmorphous(RandomAbility behaviour)
        {
            if (behaviour == null) return;

            PlayableCard card = null;
            try { card = behaviour.GetComponent<PlayableCard>(); } catch { }
            if (card == null) return;

            string name = null;
            try { name = CardReader.CardName(card); } catch { }
            if (string.IsNullOrEmpty(name)) return;

            Ability gained = Ability.None;
            try
            {
                var mods = card.TemporaryMods;
                if (mods != null)
                {
                    for (int i = mods.Count - 1; i >= 0 && gained == Ability.None; i--)
                    {
                        var abilities = mods[i]?.abilities;
                        if (abilities == null) continue;
                        for (int j = abilities.Count - 1; j >= 0; j--)
                        {
                            if (abilities[j] == Ability.RandomAbility) continue;
                            gained = abilities[j];
                            break;
                        }
                    }
                }
            }
            catch { }

            string sigil = null;
            try { sigil = gained != Ability.None ? CardReader.GetAbilityName(gained) : null; } catch { }

            if (string.IsNullOrEmpty(sigil))
            {
                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL: Amorphous on '{name}' fired but the granted sigil could not " +
                    "be read — not spoken.");
                return;
            }

            Plugin.Log?.LogInfo($"IKMA SIGIL: Amorphous on '{name}' granted {sigil} [{gained}].");
            using (Speech.Event(EventKind.Powers)) Speech.Result(Vocabulary.AmorphousTriggers(name, sigil));
        }

        // ------------------------------------------------------------------
        // TIDAL LOCK — SquirrelOrbit and the Moon's portrait. (0.7.316.)
        //
        // Two hooks, because neither end holds the whole sentence.
        // SquirrelOrbit.OnUpkeep walks the player's slots and kills each
        // Squirrel, Aqua Squirrel or Rabbit it finds; MoonAnimatedPortrait
        // .InstantiateOrbitingObject is then handed that card's CardInfo, once
        // per card pulled up. The portrait component has no route back to the
        // card that owns the sigil, so the owner is latched when the upkeep
        // coroutine is created and the pulled card comes from the game's own
        // argument.
        //
        // THE ACTOR IS NOT HARDCODED TO THE MOON. SquirrelOrbit is the Moon's
        // today; the line reads whichever card carries it.
        //
        // THE DEATH IS NOT SUPPRESSED, for one build and deliberately: the
        // pulled card dies through PlayableCard.Die before this fires, and
        // whether the board differ also reports that departure cannot be
        // answered from source. Both are logged; the next log settles it
        // rather than a guess removing a line he wanted.
        // ------------------------------------------------------------------
        private static PlayableCard _tidalLockOwner;

        internal static void NoteTidalLockUpkeep(SquirrelOrbit behaviour)
        {
            if (behaviour == null) return;
            try { _tidalLockOwner = behaviour.GetComponent<PlayableCard>(); } catch { }
        }

        internal static void NoteTidalLockPull(CardInfo pulled)
        {
            string owner = null;
            try { owner = CardReader.CardName(_tidalLockOwner?.Info); } catch { }

            string victim = null;
            try { victim = CardReader.CardName(pulled); } catch { }

            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(victim))
            {
                Plugin.Log?.LogInfo(
                    "IKMA SIGIL: Tidal Lock pulled a card but the owner or the card could not " +
                    $"be read (owner='{owner}', pulled='{victim}') — not spoken.");
                return;
            }

            Plugin.Log?.LogInfo($"IKMA SIGIL: Tidal Lock on '{owner}' pulled '{victim}' into orbit.");
            using (Speech.Event(EventKind.Powers)) Speech.Result(Vocabulary.TidalLockTriggers(owner, victim));
        }

        // ------------------------------------------------------------------
        // TRINKET BEARER — RandomConsumable. (0.7.316.)
        //
        // The coroutine adds a rolled consumable to the run and calls
        // ItemsManager.UpdateItems, which builds the item in a slot — and
        // EventNarrator ALREADY announces exactly that ("X is created in item
        // slot 3", Session 15). So the arrival is not read twice: the trigger
        // latches, the existing item-created path asks whether a latch is live,
        // and if it is, his Trinket Bearer sentence is composed THERE, where
        // the item and the slot are already in hand. One event, one line, and
        // the suppression sits where the line is composed.
        //
        // AND THE FULL-SLOTS CASE IS THE FIZZLE. The game's else branch shakes
        // the card and adds nothing, so a window that closes unclaimed speaks
        // the approved fizzle wording.
        // ------------------------------------------------------------------
        private const float TRINKET_SETTLE_SECONDS = 2f;

        private class TrinketClaim
        {
            internal PlayableCard Card;
            internal bool Claimed;
        }

        private static TrinketClaim _trinket;

        internal static void NoteTrinketBearer(RandomConsumable behaviour)
        {
            if (behaviour == null) return;

            PlayableCard card = null;
            try { card = behaviour.GetComponent<PlayableCard>(); } catch { }
            if (card == null) return;

            var claim = new TrinketClaim { Card = card };
            _trinket = claim;

            // Session 47 (0.7.455): the name is taken NOW. A disguised Ijiraq
            // fires its disguise's Trinket Bearer and is the Ijiraq by the
            // time the line below is composed; his log had "Ijiraq's Trinket
            // Bearer ability fizzles" ahead of the transform line.
            string nameAtTrigger = null;
            try { nameAtTrigger = CardReader.RevealedDisguiseName(card); } catch { }

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                // The window closes here whatever the outcome, so a later item
                // creation from somewhere else can never be claimed by it.
                if (ReferenceEquals(_trinket, claim)) _trinket = null;
                if (claim.Claimed) return null;

                string name = nameAtTrigger;
                if (string.IsNullOrEmpty(name))
                {
                    try { name = CardReader.CardName(claim.Card?.Info); } catch { }
                }

                string sigil = null;
                try { sigil = CardReader.GetAbilityName(Ability.RandomConsumable); } catch { }

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(sigil)) return null;

                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL: {sigil} on '{name}' added no item — the slots were full.");
                // 0.7.339 — his wording, Session 26.
                return Vocabulary.Sigils.SAbilityFizzlesDue(name, sigil);
            }, TRINKET_SETTLE_SECONDS);
        }

        /// <summary>
        /// Asked by EventNarrator when the game creates an item. Returns the
        /// Trinket Bearer sentence when this creation belongs to a live
        /// trigger, and null when it does not — in which case the ordinary
        /// item-created line is spoken exactly as before.
        /// </summary>
        // 0.7.439 - set when ClaimItemCreation has already spoken the item as
        // part of the play confirmation; the caller then says nothing more.
        private static bool _trinketSpokenWithPlay;
        internal static bool ConsumeSpokenWithPlay()
        {
            bool was = _trinketSpokenWithPlay;
            _trinketSpokenWithPlay = false;
            return was;
        }

        internal static string ClaimItemCreation(string itemName, int slotNumber)
        {
            var claim = _trinket;
            if (claim == null || claim.Claimed) return null;
            if (string.IsNullOrEmpty(itemName) || slotNumber <= 0) return null;

            string name = null;
            try { name = CardReader.CardName(claim.Card?.Info); } catch { }
            if (string.IsNullOrEmpty(name)) return null;

            claim.Claimed = true;
            _trinket = null;

            Plugin.Log?.LogInfo(
                $"IKMA SIGIL: Trinket Bearer on '{name}' added '{itemName}' to item slot {slotNumber}.");

            // 0.7.439 - the player just played this card and its confirmation
            // is being held: one line, spoken as the confirmation it is.
            string playedLine = PlayConfirmHold.Take(claim.Card);
            if (playedLine != null)
            {
                using (Speech.Event(EventKind.CardPlayed, EventSource.CurrentPlayer))
                    Speech.Confirm(Vocabulary.PlayedThenTrinketBearer(playedLine, itemName, slotNumber));
                _trinketSpokenWithPlay = true;
                return null;
            }

            return Vocabulary.TrinketBearerTriggers(name, itemName, slotNumber);
        }
    }

    // Registered through Plugin.TryPatch, never PatchAll.
    //
    // PREFIX, so the question is captured before the board changes. The ANSWER
    // is read later — see the note at the top of this file.
    public class GuardDog_OnOtherCardResolve_Patch
    {
        static void Prefix(GuardDog __instance, PlayableCard otherCard)
            => SigilNarrator.NoteGuardianTriggered(__instance, otherCard);
    }

    // Every Strafe-family mover, from the one base declaration. Prefix, so the
    // starting slot is captured before the card moves.
    public class Strafe_OnTurnEnd_Patch
    {
        static void Prefix(Strafe __instance) => SigilNarrator.NoteStrafeMove(__instance);
    }

    // Bloodlust paying off. The power is read later — see the note above.
    public class GainAttackOnKill_OnOtherCardDie_Patch
    {
        static void Prefix(GainAttackOnKill __instance)
            => SigilNarrator.NoteBloodlustTriggered(__instance);
    }

    // The cage breaking. Prefix, so the slot is read before the creature lands
    // in it — what comes out is read later, off that same slot.
    // Burrower ducking into an empty slot. The destination slot is an argument
    // of the coroutine, so nothing has to be guessed. (0.7.225.)
    public class WhackAMole_OnSlotTargetedForAttack_Patch
    {
        public static void Prefix(WhackAMole __instance, CardSlot slot)
            => SigilNarrator.NoteBurrow(__instance, slot);
    }

    public class IceCube_OnDie_Patch
    {
        static void Prefix(IceCube __instance)
            => SigilNarrator.NoteCageBroken(__instance);
    }

    // The Long Elk leaving a Vertebrae behind (Session 47). Prefix on the
    // coroutine Strafe runs after a successful move. Registered by TryPatch,
    // with no HarmonyPatch attribute on it.
    public class Strafe_PostSuccessfulMoveSequence_Patch
    {
        static void Prefix(Strafe __instance, CardSlot oldSlot)
            => SigilNarrator.NoteVertebrae(__instance, oldSlot);
    }

    // Child 13 given as a sacrifice (Session 48). Registered by TryPatch,
    // with no attribute on it.
    public class JerseyDevil_OnSacrifice_Patch
    {
        static void Prefix(JerseyDevil __instance)
            => SigilNarrator.NoteChild13Sacrificed(__instance);
    }

    // The Great Kraken coming back up (Session 48). Registered by TryPatch,
    // with no attribute on it.
    public class SubmergeSquid_OnResurface_Patch
    {
        static void Prefix(SubmergeSquid __instance)
            => SigilNarrator.NoteKrakenResurface(__instance);
    }

    // The Ijiraq dropping its disguise (Session 46). Prefix, so the name it
    // was wearing is captured before it changes. Registered by TryPatch.
    public class Shapeshifter_OnResolveOnBoard_Patch
    {
        static void Prefix(Shapeshifter __instance)
            => SigilNarrator.NoteIjiraqReveal(__instance);
    }

    // Fledgling and Transformer, from Evolve's one coroutine. Prefix, so the
    // card's name before the change is captured — the whole point of the line.
    public class Evolve_OnUpkeep_Patch
    {
        // 0.7.459: playerUpkeep is the game's own argument (Evolve.OnUpkeep(bool
        // playerUpkeep)), passed on so EvolveBatch can ask which other cards
        // answer the same upkeep.
        static void Prefix(Evolve __instance, bool playerUpkeep)
            => SigilNarrator.NoteTransformed(__instance, playerUpkeep);

        /// <summary>
        /// 0.7.459 - when a card's change has happened. PUBLIC override
        /// IEnumerator Evolve.OnUpkeep(bool playerUpkeep), Evolve.cs:21
        /// (dumps\dump_s49_from_decompile.txt). A POSTFIX that wraps the
        /// enumerator for SigilNarrator.WatchEvolve. Registered by its own
        /// TryPatch call, and no attribute on this class. 0.7.460: moved here
        /// from a second class, so one class patches Evolve.OnUpkeep.
        /// </summary>
        public static void Postfix(Evolve __instance, ref System.Collections.IEnumerator __result)
        {
            try
            {
                if (__instance != null && __result != null)
                    __result = SigilNarrator.WatchEvolve(__instance, __result);
            }
            catch { }
        }
    }

    /// <summary>
    /// 0.7.443 - the Curious Egg hatching. PUBLIC override IEnumerator
    /// HydraEgg.OnDrawn(), HydraEgg.cs:51
    /// (dumps\dump_hydraegg_from_decompile.txt). A POSTFIX that wraps the
    /// enumerator, so SigilNarrator.WatchHatch sees each step the game takes.
    /// Through TryPatch, and no attribute on this class.
    /// </summary>
    public static class HydraEgg_OnDrawn_Patch
    {
        public static void Postfix(HydraEgg __instance, ref System.Collections.IEnumerator __result)
        {
            try
            {
                if (__instance != null && __result != null)
                    __result = SigilNarrator.WatchHatch(__instance, __result);
            }
            catch { }
        }
    }

    /// <summary>
    /// 0.7.445 - the Glitched card. PUBLIC override IEnumerator
    /// RandomCard.OnDrawn(), RandomCard.cs:13
    /// (dumps\dump_hydraegg_from_decompile.txt). A POSTFIX that wraps the
    /// enumerator for SigilNarrator.WatchGlitch. Through TryPatch, and no
    /// attribute on this class.
    /// </summary>
    public static class RandomCard_OnDrawn_Patch
    {
        public static void Postfix(RandomCard __instance, ref System.Collections.IEnumerator __result)
        {
            try
            {
                if (__instance != null && __result != null)
                    __result = SigilNarrator.WatchGlitch(__instance, __result);
            }
            catch { }
        }
    }
}

// SigilNarrator.cs
