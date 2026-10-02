// EventNarrator.cs
using System;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    // -------------------------------------------------------------------------
    // Two events the game shows and IKMA never mentioned. (Session 14, both
    // from Zamar's 0.7.38 playtest.)
    //
    // 1. A CARD CREATED IN YOUR HAND. He played a Bat, its Rabbit Hole sigil put
    //    a Rabbit in his hand, and nothing was said. A sighted player watches the
    //    card fly in. This is the same silent-stale-model failure as the movers,
    //    moved from the board to the hand: the next hand read would have had one
    //    more card in it than he had any reason to expect.
    //
    //    Every Act 1 route into the hand goes through PlayerHand.AddCardToHand —
    //    sigils (Rabbit Hole, Bees Within, Fecundity, Unkillable, Ant Spawner),
    //    items (Squirrel in a Bottle, the Goat and Opossum bottles), and the
    //    draw. So one patch covers all of them, which is the same argument the
    //    board diff won on.
    //
    // 2. THE AUTOSAVE. Zamar asked for it directly: the game shows a saving
    //    icon and a blind player has no idea it happened. Low priority by his
    //    own framing, so it is an Info line that waits its turn and never cuts
    //    anything off.
    //
    // Both members confirmed before writing:
    //   PlayerHand.AddCardToHand(PlayableCard, Vector3, Single) -> IEnumerator, PUBLIC
    //   SaveManager.SaveToFile(Boolean saveActiveScene) -> Void, PUBLIC STATIC
    // -------------------------------------------------------------------------
    internal static class EventNarrator
    {
        // ----------------------------------------------------------------------
        // A card arriving in hand.
        //
        // The DRAW is deliberately excluded. AnnounceDrawnCard already reports it
        // with the settle window that folds in a totem grant, and two lines for
        // one card is the duplicate this project treats as a bug.
        //
        // Deferred so the card's name is read after it has actually landed, and
        // so a sigil granted on arrival is present by the time it is spoken.
        // ----------------------------------------------------------------------
        internal static void NoteCardAddedToHand(PlayableCard card)
        {
            if (card == null) return;
            if (HotkeyManager.DrawInFlight) return;

            // THE OPENING HAND IS NOT CREATED, IT IS DEALT. (0.7.113.)
            //
            // Zamar, Session 18: "at the start of this next encounter it said
            // these cards were created in my hand for some reason, even though I
            // just drew them from my deck." His 0.7.112 log shows all four
            // opening cards announced this way before "Your turn." was even
            // queued.
            //
            // THE OLD GUARD ONLY COVERED DRAWS IKMA STARTED. DrawInFlight is set
            // by the D and S keys, so it catches a draw the player asked for and
            // says nothing about the hand the GAME deals at the top of an
            // encounter — no keypress, no flag, four false lines.
            //
            // ASK THE GAME ITS OWN QUESTION. TurnManager.IsSetupPhase is PUBLIC
            // and is the game's own answer to "is this the opening sequence".
            // Inferring it from a timer or a card count would have been a model
            // of the game's internals; this is the game saying so itself.
            //
            // CHECKED AT ENQUEUE, NOT AT SPEAK. The line is EnqueueDeferred, so
            // its provider runs at speak time — by which point setup has ended
            // and the check would pass for exactly the cards it exists to
            // suppress. The 0.7.112 log is what shows that: all four were queued
            // before "Your turn." and spoken after it.
            //
            // "Created" is a claim about a CAUSE — a sigil or an item put this
            // card in your hand. On the opening deal the game made no such
            // claim, and naming a cause the game did not state is the failure
            // this project treats as worse than silence.
            try
            {
                var tm = TurnManager.Instance;
                if (tm != null && tm.IsSetupPhase) return;
            }
            catch { }

            // NOT Action tier. Zamar's 0.7.42 log: "Bat played in Slot 3." was
            // spoken straight off the keypress, and this line — queued as an
            // Action, which interrupts speech from outside the queue — cut the
            // confirmation of the card he had just played. A creation in hand is
            // commentary on something that already happened; the placement
            // confirmation is the result he was waiting for. Commentary does not
            // get to stomp a result.
            // A PACK OPENING OWNS ITS OWN LINE. (0.7.173.)
            //
            // Zamar: "Remove these individual lines from the mule prize."
            //
            // A Pack Mule's death spawns four or five cards, and BossNarrator
            // already collects them into "Squirrel, Mole, Adder and Wolverine
            // are added to your hand." His log had BOTH — the batch, and then
            // all four again one at a time from here.
            //
            // TWO SEPARATE ROUTES INTO THE HAND, and this is the third time
            // this shape has bitten: CardSpawner.SpawnCardToHand is what the
            // batch listens on, PlayerHand.AddCardToHand is what this listens
            // on, and one card arriving trips both. Suppressing at ENQUEUE
            // rather than at speak time is deliberate — the batch window is
            // open now and will have closed by the time a deferred line
            // composes.
            if (BossNarrator.HandBatchOpen)
            {
                try
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA HAND: '{CardReader.CardName(card.Info)}' arrived inside a " +
                        "pack batch — not announced separately.");
                }
                catch { }
                return;
            }

            // ==================================================================
            // THE CAUSE IS READ NOW, NOT WHEN THE LINE IS SPOKEN. (0.7.251.)
            //
            // Zamar's Beehive log: "Bee is created in your hand." with no cause
            // at all, and then a second sentence, "Bee is added to your hand",
            // for the same single Bee.
            //
            // CurrentCause is a THREE-FRAME window — the AbilityBehaviour that
            // made the card notes itself, and the note goes stale immediately
            // because a later card must not inherit an earlier card's reason.
            // This lambda composes when the line reaches the FRONT of the queue,
            // which is hundreds of frames later behind the attack it followed,
            // so the window had always closed and the cause was always null.
            // Beehive's Bees Within was being attributed to nobody.
            //
            // The same comment two blocks up says exactly this about the batch
            // window and acts on it; the cause needed the same treatment and did
            // not get it. Anything read out of a frame-scoped window belongs
            // OUTSIDE the deferred body.
            // ==================================================================
            string causeAtArrival = CurrentCause();

            // ==================================================================
            // THE TRADE SCREEN SPEAKS FOR ITS OWN CARDS. (0.7.302.)
            //
            // Zamar's 0.7.301 log, at the Trader:
            //
            //   SPEAK: Wolf Pelt is created in your hand.
            //   SPEAK: Wolf Pelt is added to your hand.
            //
            // and, for every card he traded for, "Traded a pelt for Porcupine."
            // followed later by "Porcupine is created in your hand." — the
            // second arriving after the phase had ended, over the Trader's
            // closing dialogue. His call on the pelt: "both should be removed
            // and that should be added to opening line."
            //
            // Every card that reaches the hand during this phase is either the
            // pelt the opening line now names or a card the trade line already
            // announced, so there is nothing here this path can add. The whole
            // phase is one screen with its own narrator, which is the same
            // arrangement the card choice screen has.
            //
            // TradeReader.Armed is set by the game's own TradePhase call and
            // cleared when the reader disarms — not a window and not a guess.
            // ==================================================================
            // 0.7.312 — the same question, asked of the one place that
            // answers it for both arrival paths. This used to set
            // BossNarrator.SuppressNextHandArrival, which only works when
            // this path composes FIRST; Zamar's 0.7.311 log has it composing
            // second and the pelt announced twice.
            string arrivingName = null;
            try { arrivingName = CardReader.CardName(card?.Info); } catch { }
            if (HandArrivalGate.Closed(arrivingName)) return;

            // 0.7.348 — the group line already has this card. See
            // BossNarrator.GatherHolds.
            if (causeAtArrival == null && BossNarrator.GatherHolds(arrivingName))
            {
                Plugin.Log?.LogInfo(
                    $"IKMA HAND: '{arrivingName}' is in the gathered hand line — not announced separately.");
                return;
            }

            // 0.7.341 — this line will carry what the woodcarving did to the
            // card, straight after it. See HandFollowUps.
            HandFollowUps.Reserve(arrivingName);

            // ONE CARD, ONE SENTENCE. The plain arrival line from BossNarrator
            // says less than this one does, so it stands down — the same handoff
            // 0.7.193 set up for Brood Parasite, which never covered the case
            // where the cause arrives through this path instead.
            if (causeAtArrival != null) BossNarrator.SuppressNextHandArrival();

            // 0.7.424 — CAUGHT IN A TRAP: THE PELT RIDES THE VICTIM'S LINE.
            // Zamar: "Bat gets caught in the Leaping Trap and dies. A Wolf
            // Pelt is added to your hand." That line is already waiting in
            // the queue (TrapCatchNarrator); this hands it the second half
            // and says nothing itself.
            if (causeAtArrival != null && TrapCatchNarrator.TryFold(() =>
                {
                    string n;
                    try { n = CardReader.CardName(card.Info); }
                    catch { return null; }
                    if (string.IsNullOrEmpty(n)) return null;
                    return Vocabulary.Events.AIsAddedToYourHand(Article(n), n) + HandFollowUps.Take(n);
                }))
            {
                Plugin.Log?.LogInfo(
                    $"IKMA HAND: '{arrivingName}' is said on the trap's caught line.");
                return;
            }

            // 0.7.342 — held behind the Prospector's wipe line when the wipe
            // caused it. See ProspectorNarrator.EnqueueAfterWipeLine.
            ProspectorNarrator.EnqueueAfterWipeLine(() =>
            {
                string name;
                try { name = CardReader.CardName(card.Info); }
                catch { return null; }

                if (string.IsNullOrEmpty(name)) return null;

                // ZAMAR'S SHAPE, 0.7.251: "Before Bee added to my hand it should
                // have said 'Beehive's Bees Within ability triggers: A bee is
                // added to your hand.'" The sigil that fired leads, because that
                // is the event; the card arriving is its result.
                if (causeAtArrival == null) return Vocabulary.Events.IsCreatedInYour(name, HandFollowUps.Take(name));

                string line = Vocabulary.Events.AbilityTriggersIsAdded(causeAtArrival, Article(name), name);

                // ==========================================================
                // OUROBOROS ONLY. (0.7.298.)
                //
                // ZAMAR'S WORDING, verbatim, and his scope: "Only for
                // Ouroboros though of course not for other unkillable
                // cards."
                //
                // Ouroboros is a SpecialCardBehaviour, not a sigil: OnDie
                // increments SaveFile.OuroborosDeaths on every death,
                // sacrifices included, and CardInfo adds that counter to
                // both attack and health. There is no icon for it, so the
                // only cue anyone gets is the printed numbers climbing —
                // which IKMA already reads. This sentence is an addition he
                // asked for on top of parity, not a gap being closed.
                //
                // Gated on the card's own SpecialTriggeredAbility.Ouroboros
                // rather than on its name or on Unkillable, so no other
                // Unkillable card can pick the line up and no renamed card
                // can lose it.
                // ==========================================================
                // CardInfo.specialAbilities is PRIVATE; the public accessor
                // is SpecialAbilities, which also folds in the mods'. Checked
                // against CardInfo.cs after 0.7.298 failed to compile on the
                // private name — the rule about confirming a member before
                // using it applies to reading the decompile too, not only to
                // patching.
                try
                {
                    var specials = card.Info?.SpecialAbilities;
                    if (specials != null && specials.Contains(SpecialTriggeredAbility.Ouroboros))
                        line += Vocabulary.Events.OuroborosComesBackStronger;
                }
                catch { /* the plain line is still true */ }

                return line + HandFollowUps.Take(name);
            });
        }

        /// <summary>
        /// "A" or "An", by the sound of the card's first letter. (0.7.299.)
        /// </summary>
        /// <remarks>
        /// Zamar, on hearing "A Ouroboros is added to your hand": "fix it."
        ///
        /// Vowel letters only, which is the rule that is true of every card
        /// name the game ships — Ouroboros, Adder, Ant Queen, Elk, Opossum,
        /// Urayuli. The English exceptions this misses ("a unicorn", "an
        /// hour") need a pronunciation dictionary, and inventing one for
        /// cards that do not exist would be guessing at names the game has
        /// not chosen. If a card ever lands on the wrong side of it, this is
        /// one method with one line to change.
        /// </remarks>
        private static string Article(string name)
        {
            if (string.IsNullOrEmpty(name)) return Vocabulary.Events.ArticleA;
            return Vocabulary.Events.AnOrA("AEIOUaeiou".IndexOf(name[0]));
        }

        // ======================================================================
        // WHAT PUT THAT CARD IN YOUR HAND. (0.7.115.)
        //
        // Zamar: "This line should be 'Rabbit is created in your hand by
        // [card name]'s [ability].'"
        //
        // THE HANDOFF SAID THIS WAS UNBUILDABLE AND THE HANDOFF WAS LOOKING IN
        // THE WRONG PLACE. Session 15 asked GlobalTriggerHandler what was
        // resolving and found only StackSize and NumTriggersThisBattle. True,
        // and the wrong question — the cause is not tracked centrally because
        // it does not need to be. THE ABILITY ITSELF IS THE THING RUNNING, and
        // an AbilityBehaviour knows both its own Ability and its own Card.
        //
        // ONE CHOKE POINT COVERS ALL SEVEN. dump_creators.txt:
        //
        //   DrawCreatedCard : AbilityBehaviour
        //     CreateDrawnCard()            NONPUBLIC, declared exactly ONCE
        //     DrawRabbits (Rabbit Hole), BeesOnHit (Bees Within), DrawAnt,
        //     DrawCopy, DrawCopyOnDeath, DrawRandomCardOnDeath, SteelTrap
        //
        // None of the seven redeclares CreateDrawnCard, so one patch on the
        // base fires for every one of them — checked in the dump's override
        // audit rather than assumed, because a patch on a base a subclass
        // overrides registers cleanly and can never fire.
        //
        // A FRAME STAMP, NOT A LATCH. The cause is only trusted for the frames
        // immediately around the creation. A stale cause would attach the wrong
        // ability to the next card that arrived by any other route — a draw, a
        // totem, an item — and naming a cause the game did not state is worse
        // than naming none. So it expires by itself and the line falls back to
        // the plain sentence, which was true before and stays true.
        // ======================================================================
        private static string _causeCardName;
        private static string _causeAbilityName;
        private static float  _causeAt    = -999f;

        internal static void NoteCreationCause(string cardName, string abilityName)
        {
            _causeCardName    = cardName;
            _causeAbilityName = abilityName;
            _causeAt          = Now();
        }

        // ======================================================================
        // THREE FRAMES WAS TOO SHORT, AND BeesOnHit IS THE PROOF. (0.7.252.)
        //
        // Zamar heard "Bee is created in your hand" with no cause and then "Bee
        // is added to your hand" for the same Bee, twice in a row across two
        // builds. 0.7.251 moved the read out of the deferred body, which was a
        // real bug and not this one.
        //
        // THE GAME'S OWN TIMING IS THE ANSWER. BeesOnHit.OnTakeDamage:
        //
        //   yield return PreSuccessfulTriggerSequence();
        //   base.Card.Anim.StrongNegationEffect();
        //   yield return new WaitForSeconds(0.4f);      <- here
        //   yield return CreateDrawnCard();             <- prefix fires
        //
        // and CreateDrawnCard itself may wait 0.2s, switch view, wait 0.2s again
        // before SpawnCardToHand. So the card lands the better part of a second
        // after the prefix noted the cause. Three frames is fifty milliseconds.
        // Ant Spawner's path is tight enough to fit, which is why exactly one
        // sigil worked and the reason looked like sigil-specific magic.
        //
        // WIDE WINDOW PLUS CONSUME-ONCE, not just a wider window. A note is
        // spent by the first card that arrives after it, so a stale one cannot
        // be handed to a second card even inside the three seconds. That is the
        // property that actually prevents mis-attribution; the duration only
        // decides how long a MISSED card stays unattributed.
        // ======================================================================
        private const float CAUSE_VALID_SECONDS = 3f;

        private static float Now()
        {
            try { return Time.unscaledTime; } catch { return 0f; }
        }

        private static string CurrentCause()
        {
            if (string.IsNullOrEmpty(_causeCardName))    return null;
            if (string.IsNullOrEmpty(_causeAbilityName)) return null;
            if (Now() - _causeAt > CAUSE_VALID_SECONDS)
            {
                Plugin.Log?.LogInfo(
                    $"IKMA HAND: the note from {_causeCardName}'s {_causeAbilityName} " +
                    "went stale before a card arrived.");
                _causeCardName = null;
                _causeAbilityName = null;
                return null;
            }

            string cause = Vocabulary.Events.CardsAbility(_causeCardName, _causeAbilityName);

            // SPENT. One note, one card.
            _causeCardName    = null;
            _causeAbilityName = null;

            return cause;
        }

        // ======================================================================
        // BROOD PARASITE'S EGG. (0.7.115.)
        //
        // Zamar: "When the Cuckoo was played, its ability Brood Parasite
        // activated since there was no card on the enemy board above it, and it
        // played an egg up there. None of that was announced, but should be, and
        // ONLY when that ability is successfully used to make an egg."
        //
        // "ONLY WHEN SUCCESSFULLY USED" IS THE ENTIRE DESIGN. CreateEgg does
        // nothing when the opposing space is occupied, and its dump shows only
        // OnResolveOnBoard and RespondsToResolveOnBoard — no success flag, no
        // return value, nothing reflection can read. The laying happens inside
        // the coroutine body.
        //
        // SO THE OUTCOME IS OBSERVED, NOT THE TRIGGER. The opposing slot is
        // sampled when the ability starts and read again at speak time; a line
        // is composed only if a card is there now that was not there then. That
        // is the project's standing answer to "did it work" — ask the board,
        // not the call — and it is the same shape as the item-target outcome
        // watcher and the card-play watchdog.
        //
        // EnqueueDeferred does the waiting for free: its provider runs at speak
        // time, which is after the coroutine has had its chance, and returning
        // null drops the entry silently.
        //
        // WHY THE BOARD DIFF DOES NOT ALREADY COVER THIS. The diff runs at turn
        // boundaries. His 0.7.113 log reported this very egg a whole turn later,
        // folded into the enemy-turn diff as "Enemy Broken Egg is in slot 1" —
        // by which point it had changed state and the cause was long gone.
        // ======================================================================
        internal static void NoteEggAbility(PlayableCard source, string abilityName)
        {
            if (source == null) return;

            CardSlot opposing = null;
            try { opposing = source.Slot?.opposingSlot; } catch { }
            if (opposing == null) return;

            PlayableCard before = null;
            try { before = opposing.Card; } catch { }

            string sourceName = null;
            try { sourceName = CardReader.CardName(source.Info); } catch { }

            int slotNumber = -1;
            try { slotNumber = opposing.Index + 1; } catch { }

            using (Speech.Event(EventKind.Powers, EventTag.Side(source))) Speech.Commentary(() =>
            {
                PlayableCard after = null;
                try { after = opposing.Card; } catch { return null; }

                // Nothing arrived, or the space was never empty. The ability
                // fired and did nothing, which is a thing the game says nothing
                // about either.
                if (after == null || ReferenceEquals(after, before)) return null;

                string laid = null;
                try { laid = CardReader.CardName(after.Info); } catch { }
                if (string.IsNullOrEmpty(laid)) return null;

                string by = (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(abilityName))
                    ? ""
                    : Vocabulary.Events.BySourcesAbility(sourceName, abilityName);

                // IKMA WORDING: PROVISIONAL. Zamar asked for the event, not the
                // sentence. The shape mirrors the hand line he DID author —
                // "[card] is created in your hand by [card]'s [ability]" — so
                // the two read as one family until he replaces it.
                string line = Vocabulary.Events.IsCreatedInOrIsCreatedOn(slotNumber, laid, by);

                return line;
            });
        }

        // ======================================================================
        // OVERKILL — DAMAGE THAT CARRIES PAST A DEAD DEFENDER. (0.7.120.)
        //
        // ZAMAR CALLED THIS AND THE ARITHMETIC PROVED HIM RIGHT. His 0.7.118
        // log: a 3/2 Stoat struck a 2/1 Coyote, "Coyote takes 3 damage and
        // dies", and then "Porcupine takes 2 damage" — the card still QUEUED
        // behind it. Three attack less one health is two spare, and two is what
        // the Porcupine took. His words: "it was hit while still in que, which I
        // believe happens in the base game if your creature's power exceeds the
        // opposing creature's toughness."
        //
        // Claude doubted it and should not have. The player's own knowledge of
        // this game is a source, and checking his number against the log would
        // have settled it in one step instead of a round trip.
        //
        // IKMA SAID NOTHING. The player heard a card he was never told was a
        // target take damage out of nowhere — the same silence that produced
        // this project's one false bug report, running the other way.
        //
        // THE GAME NAMES IT ALL, and the members are its own:
        //   CombatPhaseManager.DealOverkillDamage(Int32 damage,
        //                                         CardSlot attackingSlot,
        //                                         CardSlot opposingSlot)
        //   CombatPhaseManager.PreOverkillDamage(PlayableCard queuedCard)
        //
        // Both NONPUBLIC. DealOverkillDamage carries the AMOUNT; PreOverkillDamage
        // carries the VICTIM, which is the queued card and is not reachable from
        // the slot, because a queued card is not in a slot yet. So the two are
        // paired: the amount is stamped, the victim composes the line.
        //
        // NEITHER IS OVERRIDDEN IN ACT 1. CombatPhaseManager3D redeclares
        // PostOverkillDamage and VisualizeExcessLethalDamage but not these two,
        // and the only other declarations belong to Act 2's Magnificus and Pixel
        // managers. Checked in the dump's override audit, not assumed.
        //
        // NO CAUSE IS NAMED BEYOND THE ATTACKER. The game does not call this
        // Trample and neither does IKMA — announcing what is true rather than
        // what a player might call it.
        // ======================================================================
        // ORDER-INDEPENDENT PAIRING. (0.7.138.)
        //
        // 0.7.137 stamped the amount and read it when the victim arrived, and
        // his log came back "Coyote in the queue takes damage and dies." — no
        // number, no slot. The stamp was not stale, it was NOT YET SET:
        // PreOverkillDamage runs BEFORE DealOverkillDamage, so the victim half
        // arrived first and found nothing.
        //
        // I assumed an order instead of reading one, on two methods whose names
        // say the order out loud. "Pre" comes before the deal.
        //
        // SO NEITHER HALF WAITS ON THE OTHER. Each records what it has and asks
        // whether the pair is complete; whichever lands second composes the
        // line. That is correct whichever way round the game calls them, which
        // is the only version of this worth shipping — the next mover, or the
        // next game, may differ again.
        //
        // Both halves expire together on a frame stamp, so a victim from one
        // attack can never be paired with an amount from the next.
        private static int _overkillAmount = -1;
        private static int _overkillSlot   = -1;
        private static PlayableCard _overkillVictim;
        private static int _overkillFrame = -999;

        private const int OVERKILL_VALID_FRAMES = 3;

        private static void ForgetOverkillIfStale()
        {
            if (Time.frameCount - _overkillFrame > OVERKILL_VALID_FRAMES)
            {
                _overkillAmount = -1;
                _overkillSlot   = -1;
                _overkillVictim = null;
            }
        }

        internal static void NoteOverkillAmount(int damage, CardSlot opposingSlot)
        {
            ForgetOverkillIfStale();

            _overkillAmount = damage;
            _overkillFrame  = Time.frameCount;

            _overkillSlot = -1;
            try { if (opposingSlot != null) _overkillSlot = opposingSlot.Index + 1; }
            catch { }

            TryAnnounceOverkill();
        }

        internal static void NoteOverkillVictim(PlayableCard queuedCard)
        {
            if (queuedCard == null) return;

            ForgetOverkillIfStale();

            _overkillVictim = queuedCard;
            _overkillFrame  = Time.frameCount;

            TryAnnounceOverkill();
        }

        private static void TryAnnounceOverkill()
        {
            // Both halves, or nothing. The amount is the half that can be
            // missing legitimately (a mover with no damage figure), so it is
            // allowed through at -1 only once the victim is known.
            var victim = _overkillVictim;
            if (victim == null) return;

            int amount     = _overkillAmount;
            int slotNumber = _overkillSlot;

            // Consumed, so a second Pre in the same attack cannot re-announce.
            _overkillVictim = null;

            string name = null;
            try { name = CardReader.CardName(victim.Info); } catch { }
            if (string.IsNullOrEmpty(name)) return;

            using (Speech.Event(EventKind.HpChanges)) Speech.Commentary(() =>
            {
                // ==========================================================
                // THE DAMAGE IS ALREADY ANNOUNCED. ONLY THE CAUSE IS MISSING.
                // (0.7.139.)
                //
                // His 0.7.138 log, two lines for one event:
                //
                //   Sparrow in the queue takes damage, 1 health left.
                //   Sparrow takes 1 damage, 1 health left.
                //
                // The second is the mod's existing damage narration, and it is
                // the better line — it has the number. Mine restated it worse.
                // "A duplicate or false line is a bug" is this project's own
                // standard and I shipped one.
                //
                // AND IT WAS NEVER THE GAP. Zamar's original report was that a
                // card he had never been told was a target took damage out of
                // nowhere. The damage WAS announced; what was missing was WHY.
                // So this line now says only the thing nothing else says — that
                // the excess carried into the queue, and where — and gets out of
                // the way of the line that carries the figures.
                //
                // THE SLOT COMES FROM Opponent.QueuedSlots, the same pairing the
                // queue read has always used. DealOverkillDamage never fired in
                // his log, so the slot the patch was stamping was never set; the
                // queue itself knows, and asking it needs no second patch.
                // ==========================================================
                int slot = slotNumber;
                if (slot <= 0) slot = QueuedSlotOf(victim);

                string line = Vocabulary.Events.ExcessDamageCarriesOrExcessDamageCarries(slot);

                // IKMA WORDING: PROVISIONAL. Zamar reported the event and named
                // no sentence for it.
                return line;
            });
        }

        /// <summary>
        /// Which opponent slot a QUEUED card is aimed at, or -1.
        ///
        /// A queued card has no Slot of its own — that is what makes it queued —
        /// so the pairing comes from Opponent.Queue and Opponent.QueuedSlots at
        /// the same index, which is how BoardReader has read the queue since
        /// Session 8.
        /// </summary>
        private static int QueuedSlotOf(PlayableCard card)
        {
            if (card == null) return -1;

            try
            {
                var opponent = TurnManager.Instance?.Opponent;
                var queue    = opponent?.Queue;
                var slots    = opponent?.QueuedSlots;
                var board    = Singleton<BoardManager>.Instance?.OpponentSlotsCopy;

                if (queue == null || slots == null || board == null) return -1;

                for (int i = 0; i < queue.Count && i < slots.Count; i++)
                {
                    if (!ReferenceEquals(queue[i], card)) continue;
                    if (slots[i] == null) return -1;

                    int idx = board.IndexOf(slots[i]);
                    return idx >= 0 ? idx + 1 : -1;
                }
            }
            catch { }

            return -1;
        }

        // ======================================================================
        // WINNING A COMBAT, AND THE DAMAGE THAT WAS SPARE. (0.7.120.)
        //
        // Zamar: "when I won that combat the Map stomped all the damage info in
        // an unsatisfying way. Also the amount of over-damage I dealt was not
        // read. What should have stomped the damage was it reading 'Victory. [x]
        // extra teeth received. [if applicable].'"
        //
        // Two halves, and they are separate fixes. The stomping is dealt with in
        // MapReader.AnnounceMapReady, which no longer interrupts. This is the
        // half that needs the game to hand over a number.
        //
        // IT DOES: CombatPhaseManager.VisualizeExcessLethalDamage(Int32
        // excessDamage, SpecialBattleSequencer) — the game's own count of damage
        // dealt beyond what was needed to win, which is the figure the teeth are
        // paid from.
        //
        // *** TWO DECLARATIONS, AND ACT 1 USES THE OVERRIDE. *** The dump's
        // audit is unambiguous: CombatPhaseManager3D REDECLARES this method, and
        // CombatPhaseManager3D is what Act 1 runs. A patch on the base alone
        // would register cleanly, log success, and never fire — the trap that
        // made the card choice screen dead for six sessions. Both are patched.
        //
        // THE WORD "TEETH" IS HIS AND THE NUMBER IS THE GAME'S. IKMA does not
        // claim excess damage and teeth are the same thing; it reports the count
        // the game passes to its own teeth animation, in his sentence.
        // ======================================================================
        internal static void NoteVictory(int excessDamage)
        {
            Plugin.Log?.LogInfo($"IKMA VICTORY: excess lethal damage {excessDamage}.");

            using (Speech.Event(EventKind.Teeth, EventSource.CurrentPlayer)) Speech.Result(
                Vocabulary.Events.VictoryExtraReceivedOrVictory(excessDamage));
        }

        // ----------------------------------------------------------------------
        // AN ITEM ARRIVING IN A SLOT. (Session 15.)
        //
        // Zamar, 0.7.42: he played Pack Rat, an item appeared, and IKMA said
        // nothing. Same class as the card-in-hand gap and the movers — the
        // player now owns something and has no way to know it happened.
        //
        // From dump_input_and_causes.txt, all PUBLIC:
        //   ItemSlot.CreateItem(ItemData, Boolean)
        //   ConsumableItemSlot.CreateItem(ItemData, Boolean)
        //   ConsumableItemSlot.CreateItem(String, Boolean)
        //   ItemsManager.Slots  -> the list the slot number comes from
        //
        // ConsumableItemSlot DECLARES its own CreateItem(ItemData, Boolean), so
        // it overrides the base and a patch on ItemSlot alone would not fire for
        // it. Both are patched. Same trap as the boss lives in Session 14.
        //
        // ANNOUNCED ONLY DURING A BATTLE, AND THE REST IS LOGGED. CreateItem
        // also runs when the run's slots are built and when an item is picked up
        // at a map node, and whether either of those would duplicate an
        // announcement that already exists cannot be answered from source. So
        // the case Zamar reported is spoken and every other call writes
        // "IKMA ITEM CREATE (not spoken)" — the same method that settled the
        // board diff's appeared/vanished half in one playtest.
        //
        // NO CAUSE IS NAMED. He asked for "due to Pack Rat's [ability]", and the
        // dump looked for somewhere the resolving ability is tracked:
        // GlobalTriggerHandler holds StackSize and NumTriggersThisBattle and
        // nothing else. There is no way to ask the game what caused this, and a
        // cause inferred from timing is not a cause the game stated.
        // ----------------------------------------------------------------------
        internal static void NoteItemCreated(ItemSlot slot, ItemData data)
        {
            // rulebookName lives on ConsumableItemData, NOT on ItemData —
            // confirmed in dump_input_and_causes.txt. A totem piece or any other
            // plain ItemData has no display name anywhere, and prefabId is an
            // internal id, which is never a display name. So anything that is
            // not a consumable is logged and stays silent rather than being
            // announced by its key.
            string name = null;
            try { name = (data as ConsumableItemData)?.rulebookName; } catch { }

            if (string.IsNullOrEmpty(name))
            {
                Plugin.Log?.LogInfo(
                    "IKMA ITEM CREATE (not spoken): an item with no rulebook name " +
                    $"(type {data?.GetType().Name ?? "null"}) — no display name to read.");
                return;
            }

            name = Loc.Game(name);   // Session 32: spoken in the game's own language
            int number = SlotNumber(slot);
            string where = Vocabulary.Events.ItemSlotCount(number);

            if (!InBattle())
            {
                // 0.7.338 — THE ITEM PICKUP'S CONFIRMATION, his words: "[item
                // name] chosen." Spoken off the game putting the item into
                // one of YOUR slots (a numbered slot), which is the choice
                // having happened, not the Enter press that asked for it. The
                // three items on the cloth go into unnumbered pickup slots and
                // stay logged only. Action priority, so it cuts the browse
                // read still describing the item.
                if (number > 0 && NodeScreenReader.ItemPickupActive)
                {
                    Plugin.Log?.LogInfo($"IKMA ITEM CREATE: {name} into {where} (item pickup choice).");
                    using (Speech.Event(EventKind.ItemObtained, EventSource.CurrentPlayer)) Speech.Result(Vocabulary.Events.Chosen(name));
                    return;
                }

                // Session 34 - THE CAMPFIRE'S HOGGY BANK. When the survivors
                // eat the card, the game adds a Hoggy Bank to your items
                // (CardStatBoostSequencer: consumables.Add, UpdateItems) and
                // Leshy's line follows. Zamar: say which item and which slot.
                // Only at the campfire: slots are also rebuilt on loads,
                // where a line would repeat what the player already owns.
                // Result priority, so Leshy's line queues behind it - the
                // order the game shows it: the item appears, then he speaks.
                if (number > 0 && NodeScreenReader.CampfireActive)
                {
                    Plugin.Log?.LogInfo($"IKMA ITEM CREATE: {name} into {where} (campfire).");
                    using (Speech.Event(EventKind.ItemObtained, EventSource.CurrentPlayer)) Speech.Result(Vocabulary.Events.AddedInItemSlot(name, where));
                    return;
                }

                Plugin.Log?.LogInfo($"IKMA ITEM CREATE (not spoken): {name} into {where}.");
                return;
            }

            Plugin.Log?.LogInfo($"IKMA ITEM CREATE: {name} into {where}.");

            // TRINKET BEARER OWNS THIS ONE. (0.7.316.) When the creation is the
            // work of a live RandomConsumable trigger, Zamar's sentence names
            // the cause, the item and the slot in a single line and this
            // generic one must not also fire. Asked HERE, where the line is
            // composed, rather than by telling this path upstream to stay
            // quiet — the two orders are not guaranteed.
            string trinket = null;
            try { trinket = SigilNarrator.ClaimItemCreation(name, number); } catch { }
            if (!string.IsNullOrEmpty(trinket))
            {
                using (Speech.Event(EventKind.ItemObtained, EventSource.CurrentPlayer)) Speech.Result(trinket);
                return;
            }

            // Info tier, not Action. A created item is commentary on something
            // that already resolved — the same call as the card-in-hand line,
            // which was stomping the placement confirmation until 0.7.43.
            using (Speech.Event(EventKind.ItemObtained, EventSource.CurrentPlayer)) Speech.Commentary(Vocabulary.Events.IsCreatedIn(name, where));
        }

        private static bool InBattle()
        {
            try
            {
                return Singleton<BoardManager>.Instance != null
                    && TurnManager.Instance != null
                    && TurnManager.Instance.Opponent != null;
            }
            catch { return false; }
        }

        /// <summary>1-based position in ItemsManager.Slots, or 0 if unknown.</summary>
        private static int SlotNumber(ItemSlot slot)
        {
            try
            {
                var slots = Singleton<ItemsManager>.Instance?.Slots;
                if (slots == null || slot == null) return 0;
                for (int i = 0; i < slots.Count; i++)
                    if (ReferenceEquals(slots[i], slot)) return i + 1;
            }
            catch { }
            return 0;
        }

        // ----------------------------------------------------------------------
        // The autosave.
        //
        // SaveToFile is called by the game whenever it wants, and more than once
        // around a single visible save. The icon appears once, so the line is
        // spoken once — anything inside the window is the same save as far as the
        // player is concerned.
        // ----------------------------------------------------------------------
        private static float _lastSaveSpoken = -999f;
        private const float SAVE_DEDUPE_SECONDS = 3f;

        internal static void NoteSave()
        {
            // NOT ON THE WAY IN. (0.7.234.) Zamar: "remove this very first
            // Saving call out."
            //
            // The game autosaves as the Start scene loads, so the first thing a
            // blind player heard on launching was a chirp about a save file, on
            // a screen where the game has not started. Refused at the source
            // rather than dropped from the queue: a line that should never have
            // been said is not a line to un-say.
            //
            // GATED ON THE SCENE, NOT ON A READER. The obvious guard was
            // "is the boot screen up" — and it would have missed this, because
            // the launch save fires BEFORE StartScreenController.Start runs.
            // His 0.7.232 log has the order in black and white: MenuController
            // captured, panel root set, game saved, and only then the first-play
            // screen armed. The scene is the thing that is already true by then.
            //
            // _lastSaveSpoken is deliberately NOT stamped here — skipping this
            // one must not put the next real save inside the dedupe window.
            string scene = null;
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            if (scene == TitleScreenReader.TITLE_SCENE)
            {
                Plugin.Log?.LogInfo("IKMA SAVE: game saved — not announced, this is the front door.");
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastSaveSpoken < SAVE_DEDUPE_SECONDS) return;
            _lastSaveSpoken = now;

            Plugin.Log?.LogInfo("IKMA SAVE: game saved.");
            using (Speech.Event(EventKind.Saving)) Speech.Commentary(ReviewHistory.NotEvent(Vocabulary.Events.Saving));   // not a game event (Session 35)
        }
    }

    public class PlayerHand_AddCardToHand_Patch
    {
        static void Prefix(PlayableCard card) => EventNarrator.NoteCardAddedToHand(card);
    }

    public class ItemSlot_CreateItem_Patch
    {
        public static void Postfix(ItemSlot __instance, ItemData data)
            => EventNarrator.NoteItemCreated(__instance, data);
    }

    // ConsumableItemSlot_CreateItem_Patch IS GONE, 0.7.276. It was registered
    // alongside the base patch above on the assumption that an override hides
    // its base from a patch; ConsumableItemSlot.CreateItem opens with
    // base.CreateItem, so both postfixes fired for one event and every item
    // was reported twice. Deleted rather than left unregistered — an
    // unregistered patch class in this codebase reads as one that works.

    // The by-name overload. Nothing is spoken from here — an item NAME is an
    // internal id and internal ids are never display names.
    //
    // ANSWERED, 0.7.276: it hands off. ConsumableItemSlot.CreateItem(string)
    // is one line, CreateItem(GetConsumableByName(itemName)), so the ItemData
    // route above covers it and this patch exists only to say which door was
    // used. Behind the diagnostics flag now.
    public class ConsumableItemSlot_CreateItemByName_Patch
    {
        // 0.7.276 — behind the node-diagnostics flag. It records which of the
        // two creation routes ran, which mattered while that was being worked
        // out and is noise now that it is settled.
        public static void Postfix(String itemName)
        {
            if (!NodeScreenReader.VerboseNodeDiagnostics) return;
            Plugin.Log?.LogInfo($"IKMA ITEM CREATE: by-name route used for id '{itemName}'.");
        }
    }

    public class SaveManager_SaveToFile_Patch
    {
        static void Prefix() => EventNarrator.NoteSave();
    }
}
