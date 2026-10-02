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
                $"IKMA SIGIL: Guardian on '{CardReader.CardName(guardian.Info)}' triggered by " +
                $"'{CardReader.CardName(otherCard.Info)}' resolving. Target slot " +
                $"{targetIndex + 1} occupied at trigger = {blockedAtTrigger}.");

            var capturedGuardian = guardian;
            int capturedTarget   = targetIndex;
            int capturedFrom     = fromSlot;
            bool capturedBlocked = blockedAtTrigger;

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                // The card's CURRENT slot is deliberately not read here any
                // more. It was the source of the 0.7.188 false callout; see the
                // note below.
                string name = null;
                try { name = CardReader.CardName(capturedGuardian.Info); } catch { }

                if (string.IsNullOrEmpty(name)) return null;

                string sigil = GuardianName();

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
                    return Vocabulary.Sigils.SAbilityTriggersIt(name, sigil, capturedTarget + 1);
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
                return Vocabulary.Sigils.SAbilityTriggersItMoves(name, sigil, capturedTarget + 1);
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

            Plugin.Log?.LogInfo(
                $"IKMA SIGIL: Burrower on '{CardReader.CardName(mover.Info)}' — " +
                $"slot {fromSlot + 1} -> slot {toSlot + 1} (target slot is the argument).");

            var capturedMover = mover;
            int capturedFrom  = fromSlot;

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                string name = null;
                int nowSlot = -1;
                try
                {
                    name    = CardReader.CardName(capturedMover.Info);
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
                    name    = CardReader.CardName(capturedMover.Info);
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
                    return Vocabulary.SigilFizzles(name, sigilName);
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
                return Vocabulary.Sigils.SAbilityTriggersItMovesTo(name, sigilName, dir, nowSlot + 1);
            });
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
                    name = CardReader.CardName(captured.Info);
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
                $"IKMA SIGIL: Brood Parasite on '{CardReader.CardName(cuckoo.Info)}' " +
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
                try { name = CardReader.CardName(capturedCuckoo.Info); } catch { }
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
                cageName  = CardReader.CardName(cage.Info);
            }
            catch { }

            if (slotIndex < 0 || string.IsNullOrEmpty(cageName)) return;

            Plugin.Log?.LogInfo(
                $"IKMA SIGIL: cage '{cageName}' broken in slot {slotIndex + 1} " +
                $"(enemy={enemySide}).");

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
                try { freedName = CardReader.CardName(freed.Info); } catch { }
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
                string line = Vocabulary.Sigils.SAbilityTriggersA(capturedCage, sigil, freedName, side, capturedIndex + 1);

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
        internal static void NoteTransformed(Evolve behaviour)
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
                    newName = CardReader.CardName(capturedCard.Info);

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
                return Vocabulary.Sigils.SAbilityTriggersItBecomes(who, capturedOld, sigilName, atk, hp, newName, withClause);
            });
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
            _adjacentBatch = batch;

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
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
                try { name = CardReader.CardName(capturedCard.Info); } catch { }
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
            try { name = CardReader.CardName(card.Info); } catch { }
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

            using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
            {
                // The window closes here whatever the outcome, so a later item
                // creation from somewhere else can never be claimed by it.
                if (ReferenceEquals(_trinket, claim)) _trinket = null;
                if (claim.Claimed) return null;

                string name = null;
                try { name = CardReader.CardName(claim.Card?.Info); } catch { }

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

    // Fledgling and Transformer, from Evolve's one coroutine. Prefix, so the
    // card's name before the change is captured — the whole point of the line.
    public class Evolve_OnUpkeep_Patch
    {
        static void Prefix(Evolve __instance)
            => SigilNarrator.NoteTransformed(__instance);
    }
}

// SigilNarrator.cs
