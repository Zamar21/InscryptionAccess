// SigilTriggers.cs
//
// -----------------------------------------------------------------------------
// ONE HOOK, FORTY-SIX SIGILS. (Session 19, v0.7.186.)
//
// Zamar, 2026-09-12:
//   "can you do an audit of the rulebook and safely add this functionality to
//    as many different abilities as you can with the info we have? ... I'd like
//    to at least have the functionality of all of the abilities stood up, and
//    encounter them to polish as bug fixes, instead of what we're currently
//    doing, which is the dont work at all until I encounter them, and then have
//    to fully make them work."
//
// -----------------------------------------------------------------------------
// THE FINDING THAT MADE THIS ONE FILE INSTEAD OF FORTY-SIX.
//
// dumps/dump_callers.txt: AbilityBehaviour.PreSuccessfulTriggerSequence is
// reached from 46 of the 84 ability behaviour classes, and every one of those
// call sites is `call`, not `callvirt` — a direct, non-virtual call to the base
// declaration. One Harmony patch on the base therefore fires for all 46.
//
// THAT AUDIT WAS RUN FIRST THIS TIME. The boss-life patch sat on the wrong type
// for five sessions and could never fire, because nobody asked whether the
// override called its base. dump_callers.ps1 exists so that question is cheap.
//
// -----------------------------------------------------------------------------
// WHAT IT SPEAKS. Zamar's wording, approved 2026-09-12:
//
//     "Wolf Cub's Bloodlust ability triggers."
//
// It names the CAUSE, and only the cause. The EFFECT is already narrated by
// whatever part of IKMA owns it — the damage line, the death line, the bone
// total, the board diff. A sigil firing and its consequence are two events, so
// they are two lines, and this file only ever speaks the first one.
//
// -----------------------------------------------------------------------------
// WHY THE EXCEPTION TABLE IS SHORT.
//
// The double-speak risk is NOT "IKMA already says something happened" — that is
// the effect, and this line is the cause. The risk is "IKMA already says THIS
// SIGIL BY NAME", and only three paths do that today:
//
//   - SigilNarrator.cs      — Guardian, Bloodlust, the Sprinter family
//   - Plugin.cs DeadlyNote  — Touch of Death, on the death line
//   - DrawCreatedCard       — names the sigil that made the card, in hand
//
// Those abilities are listed as silent below. EVERYTHING ELSE FALLS THROUGH TO
// ANNOUNCE, including abilities nobody has thought about and abilities that do
// not exist yet, so a sigil still says something the first time it fires. That
// is the whole point of the request: stood up first, polished on contact.
//
// -----------------------------------------------------------------------------
// THE RUNNING LIST HE ASKED FOR.
//
// He asked for a silent comment marking each ability not yet manually tested. A
// comment would be wrong the first time either of us forgot to move it. Instead
// every Ability logs itself the first time it ever triggers in a session:
//
//     IKMA SIGIL FIRST SEEN: Bloodlust [GainAttackOnKill] — policy Silent
//
// grep the log for FIRST SEEN and that is the encountered list, observed rather
// than asserted. Anything absent has not been met yet. It cannot rot, because
// nothing writes it but the game.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using DiskCardGame;

namespace IKMA
{
    internal static class SigilTriggers
    {
        /// <summary>
        /// Master switch. If the generic line turns out to be a firehose in a
        /// busy turn, this is the one place that stops it. Not bound to a key —
        /// controls are Zamar's to name, and he has not asked for one.
        /// </summary>
        internal static bool Enabled = true;

        /// <summary>
        /// Abilities whose sigil name IKMA already speaks somewhere else.
        /// Adding the generic line for these would name one sigil twice for one
        /// event, which is regression pattern 4 and has cost four separate
        /// fixes on this project.
        /// </summary>
        private static readonly Ability[] AlreadyNamedElsewhere =
        {
            // SigilNarrator.cs — bespoke lines that carry the OUTCOME, which is
            // strictly better than the generic line. Do not trade down.
            Ability.GuardDog,                   // "Guardian"
            Ability.GainAttackOnKill,           // "Bloodlust"
            Ability.Strafe,                     // "Sprinter"
            Ability.SkeletonStrafe,
            Ability.SquirrelStrafe,
            Ability.StrafePush,                 // "Rampager"
            Ability.StrafeSwap,

            // SigilNarrator.cs — Burrower, added 0.7.225. The generic line
            // said the sigil fired and nothing else; the move then turned up a
            // turn later inside an unrelated board diff. Zamar: "That 'your
            // mole man moves from slot 1 to slot 4' line should have been
            // apart of the Burrower ability triggers line."
            Ability.WhackAMole,                 // "Burrower"

            // MultiStrikeNarrator.cs — BrittleGroup, added 0.7.349. Every
            // Brittle death in a combat phase is one line with its bones.
            Ability.Brittle,

            // SigilNarrator.cs — Brood Parasite, added 0.7.190. Names the egg
            // and the slot it lands in, which the generic line cannot.
            Ability.CreateEgg,                  // "Brood Parasite"

            // SigilNarrator.cs — the cage breaking, added 0.7.196. Names what
            // came out and where. The Caged Wolf carries this, not a sigil of
            // its own.
            Ability.IceCube,

            // SigilNarrator.cs — the transform, added 0.7.203. The generic line
            // read the card's name AFTER the change, so it had the Wolf
            // triggering the sigil that created it. Both sigils share Evolve's
            // one coroutine.
            Ability.Evolve,
            Ability.Transformer,

            // Plugin.cs — DamageRecord.DeadlyNote names it on the death line.
            // Zamar, 0.7.304, confirming this is right: "I don't think we need
            // this one since this is called out in the damage calculation. ToD
            // doesn't trigger on its own it just modifies combat damage."
            Ability.Deathtouch,                 // "Touch of Death"

            // SigilNarrator.cs — Corpse Eater, added 0.7.304. Its sentence
            // leads with the card that DIED, because the sigil fires from the
            // hand on another card's death; the generic shape cannot say that.
            Ability.CorpseEater,

            // SigilNarrator.cs — the rest of M5, added 0.7.316. Each of these
            // has a bespoke composer that names WHAT the sigil did, which the
            // generic line cannot say.
            //
            // TailOnHit is also a CORRECTION. It calls StrongNegationEffect on
            // SUCCESS — twice, to shake the card and the tail it leaves behind
            // — so while the generic line owned it, 0.7.305's fizzle watch read
            // a successful Loose Tail as a sigil that achieved nothing. No
            // record is opened for a silent ability, so that line is gone.
            Ability.TailOnHit,                  // "Loose Tail"
            Ability.CreateDams,                 // "Dam Builder"
            Ability.CreateBells,                // "Chime"
            Ability.RandomConsumable,           // "Trinket Bearer"

            // Plugin.cs — the DrawCreatedCard.CreateDrawnCard patch already
            // says "X is created in your hand by Y's <sigil>". Every ability
            // here derives from DrawCreatedCard (dumps/dump_abilities.txt).
            Ability.BeesOnHit,
            Ability.DrawAnt,
            Ability.DrawCopy,
            Ability.DrawCopyOnDeath,
            Ability.DrawRabbits,
            Ability.DrawRandomCardOnDeath,
            Ability.CellDrawRandomCardOnDeath,
            Ability.SteelTrap,
        };

        /// <summary>Every Ability seen trigger this session. The running list.</summary>
        private static readonly Dictionary<Ability, bool> _seen =
            new Dictionary<Ability, bool>();

        // One event is one line. A sigil that re-enters within this window on
        // the same card is the same event being dispatched twice, not two
        // triggers. Conduits and Sentry are the ones that do this.
        private const float REPEAT_WINDOW_SECONDS = 0.5f;

        private static Ability _lastAbility = Ability.None;
        private static PlayableCard _lastCard;
        private static float _lastTime = -99f;

        // ==================================================================
        // WHAT IS HOLDING A DEATH OPEN. (0.7.323.)
        //
        // Zamar, on "Porcupine takes 2 damage, 0 health remaining": "Add to
        // that line the trigger as to why it's not just saying it dies then."
        //
        // A card at zero health that is not dead yet is not a survivor, and
        // the line read like one. The reason is always the same shape: the
        // game is part-way through a trigger on that card and the death
        // sequence has not run. The Porcupine's Sharp takes 0.55 seconds
        // before it even deals its damage back.
        //
        // ASKED OF THE GAME, TWICE, AND IT ANSWERS BOTH HALVES.
        // GlobalTriggerHandler.StackSize is the game's own "a trigger is
        // resolving right now" — the same test FizzleWatch used, and the only
        // part of that build worth keeping. The ability comes from the trigger
        // this file has ALREADY seen fire on this card, through
        // PreSuccessfulTriggerSequence, a beat earlier: not a sigil read off
        // the card's printed list and hoped to be the relevant one.
        //
        // So nothing is inferred. If no trigger is on the stack, or the last
        // one was on another card, or it was too long ago, this returns null
        // and the line is unchanged.
        // ==================================================================
        private const float RESOLVING_WINDOW_SECONDS = 2f;

        /// <summary>
        /// The sigil currently resolving on this card, by the game's own
        /// reckoning, or null.
        /// </summary>
        internal static string ResolvingAbilityOn(PlayableCard card)
        {
            if (card == null) return null;
            if (!ReferenceEquals(card, _lastCard)) return null;
            if (_lastAbility == Ability.None) return null;

            float now = 0f;
            try { now = Time.time; } catch { }
            if (now - _lastTime > RESOLVING_WINDOW_SECONDS) return null;

            int stack = 0;
            try
            {
                stack = Singleton<GlobalTriggerHandler>.Instance != null
                      ? Singleton<GlobalTriggerHandler>.Instance.StackSize : 0;
            }
            catch { }
            if (stack <= 0) return null;

            return SigilName(_lastAbility);
        }

        private static bool IsAlreadyNamedElsewhere(Ability a)
        {
            for (int i = 0; i < AlreadyNamedElsewhere.Length; i++)
                if (AlreadyNamedElsewhere[i] == a) return true;
            return false;
        }

        /// <summary>
        /// The sigil's own name, from the game. Returns null rather than a
        /// fallback: an internal id is never a display name on this project, so
        /// if the rulebook lookup fails the line is not spoken at all.
        /// </summary>
        private static string SigilName(Ability ability)
        {
            string n = null;
            try { n = CardReader.GetAbilityName(ability); } catch { }
            return string.IsNullOrEmpty(n) ? null : n;
        }

        private static void NoteFirstSighting(Ability ability, bool silent)
        {
            try
            {
                if (_seen.ContainsKey(ability)) return;
                _seen[ability] = true;

                string spoken = SigilName(ability);
                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL FIRST SEEN: {(spoken ?? "(no rulebook name)")} " +
                    $"[{ability}] — policy " +
                    $"{(silent ? "Silent, named elsewhere" : "Announce")}.");
            }
            catch { }
        }

        /// <summary>
        /// Called from the prefix on AbilityBehaviour.PreSuccessfulTriggerSequence,
        /// which every one of the 46 behaviour classes calls directly.
        /// </summary>
        internal static void NoteTriggered(AbilityBehaviour behaviour)
        {
            if (!Enabled || behaviour == null) return;

            Ability ability   = Ability.None;
            PlayableCard card = null;

            try
            {
                ability = behaviour.Ability;

                // AbilityBehaviour.Card is PROTECTED and cannot be read from
                // here (0.7.180 failed to build on exactly that). A behaviour
                // is a component on the card's own GameObject, so the card is
                // asked for itself. This is NOT the banned lookup — the hard
                // rule bans FindObjectOfType and GameObject.Find, which scan
                // the scene. GetComponent asks one object about itself.
                card = behaviour.GetComponent<PlayableCard>();
            }
            catch { }

            if (ability == Ability.None) return;

            bool silent = IsAlreadyNamedElsewhere(ability);
            NoteFirstSighting(ability, silent);
            if (silent || card == null) return;

            // The same sigil on the same card inside the window is one event.
            float now = 0f;
            try { now = Time.time; } catch { }

            if (ability == _lastAbility &&
                ReferenceEquals(card, _lastCard) &&
                now - _lastTime < REPEAT_WINDOW_SECONDS)
            {
                return;
            }

            _lastAbility = ability;
            _lastCard    = card;
            _lastTime    = now;

            var capturedCard    = card;
            var capturedAbility = ability;

            // DEFERRED, because a prefix on a coroutine fires when the
            // enumerator is CREATED, not when it runs. The card's name is read
            // at speak time so a card that has already been replaced or
            // destroyed drops its line instead of speaking a stale one.
            // ==================================================================
            // THE FIZZLE RECORD. (0.7.305.)
            //
            // Opened before the ability's own coroutine continues, and read by
            // the closure below at speak time. Eight of the ten abilities that
            // can fizzle call PreSuccessfulTriggerSequence FIRST and negate
            // after (dumps/dump_negation.txt), so a record opened here is
            // already listening when the shake comes.
            //
            // THE TWO IT MISSES, NAMED SO NOBODY RE-DERIVES THEM: MoveBeside
            // and StrafeSwap negate before they reach this hook. Covering them
            // means moving the primary hook to
            // GlobalTriggerHandler.TriggerSequence, which fires for all 84
            // behaviour classes instead of 46 — more coverage, more noise.
            // Zamar, 0.7.305: "If the fizzles get out of control I'll likely
            // encounter them, or my playtesters will." So the narrow hook
            // ships first and widening it stays a known, separate move.
            // ==================================================================
            // ==================================================================
            // THE SHAKE IS NOT A FIZZLE. WITHDRAWN 0.7.322, FROM HIS LOG.
            //
            // The 0.7.305 design above is wrong at its root and the totem
            // battle log proved it:
            //
            //   IKMA SPEAK: Coyote takes 1 damage and dies.
            //   IKMA SPEAK: Coyote's Sharp Quills ability does nothing.
            //   IKMA SPEAK: Worker Ant takes 1 damage, 1 health remaining.
            //
            // The quills landed. IKMA announced that they did nothing and then
            // read out the damage they dealt, twice in one fight.
            //
            // WHY, read back out of the source rather than guessed: the shake
            // is not the game saying "negated". It is a generic emphasis
            // animation, and four of the five abilities this hook can see play
            // it ON SUCCESS.
            //
            //   Sharp            PreSuccessful -> shake -> 0.55s -> deals 1 damage
            //   SwapStats        PreSuccessful -> swaps the stats -> shake
            //   DrawVesselOnHit  PreSuccessful -> shake -> draws the vessel
            //   TailOnHit        PreSuccessful -> moves -> shake, twice
            //
            // and the one that really does mean it, MoveBeside, shakes and
            // yield-breaks BEFORE PreSuccessfulTriggerSequence, so no record
            // is ever open when it comes. StrafeSwap is the same shape.
            //
            // So this hook's true-positive rate is zero and always was. The
            // comment it shipped with — "Sharp waits 0.55s before it negates"
            // — was a misreading of that wait: Sharp waits, then RETALIATES.
            //
            // DO NOT REWIRE THIS TO StrongNegationEffect AGAIN. A fizzle line
            // has to read the game's own outcome, which is what the bespoke
            // ones already do and why they are right: Brood Parasite asks
            // whether the slot was occupied, Guardian asks whether the move
            // was refused, Dam Builder counts the slots the game spawned into,
            // Trinket Bearer asks whether an item was ever created.
            // Vocabulary.SigilFizzles is still Zamar's approved wording and is
            // still spoken from all four.
            //
            // FizzleWatch is left in the file with its evidence, called from
            // nowhere. Same treatment as NoteEggAbility.
            // ==================================================================

            // 0.7.341 — Morsel's gain, read NOW from the card Morsel reads.
            // See SacrificeRecord.MorselGainFor.
            string morselGain = capturedAbility == Ability.Morsel
                ? SacrificeRecord.MorselGainFor(capturedCard)
                : null;

            System.Func<string> line = () =>
            {
                string name = null;
                try { name = CardReader.CardName(capturedCard.Info); } catch { }
                if (string.IsNullOrEmpty(name)) return null;

                string sigil = SigilName(capturedAbility);
                if (string.IsNullOrEmpty(sigil)) return null;

                // The fizzle branch was here until 0.7.322. See the block
                // above: the shake it keyed on is played on SUCCESS by four of
                // the five abilities this hook can see.

                // ZAMAR'S WORDING, approved 2026-09-12.
                string triggerLine = Vocabulary.SigilTriggers(name, sigil);

                // MORSEL SAYS WHAT IT GAVE, ON ITS OWN LINE. (0.7.295.)
                // Zamar: "Ouroboros gaining 2 health should have been on the
                // ability triggers line, not the line before it." The
                // sentence is his, composed where the numbers are known
                // (SacrificeRecord) and spoken here. Taken, not peeked, so it
                // can only ever be said once.
                if (capturedAbility == Ability.Morsel)
                {
                    if (!string.IsNullOrEmpty(morselGain)) return $"{triggerLine} {morselGain}";
                }

                return triggerLine;
            };

            // SOME SIGILS ARE NOT ANNOUNCING THEMSELVES WHEN THEY FIRE.
            // (0.7.252.)
            //
            // Zamar, on two Kingfishers: "Waterborne abilities were called
            // before damage was even dealt. Waterborne triggering callout should
            // happen right before the Scales callout, after everything else.
            // It's an end of turn ability trigger."
            //
            // He is describing the board, not the code. Submerge answers
            // RespondsToUpkeep and fires at the TOP of the turn, so its line was
            // correctly first in the queue and wrong in the ear: what the player
            // needs to know is that the bird was underwater when the attacks
            // came, which only makes sense once the attacks have been read.
            //
            // So these lines are HELD and released with the turn's result. Not
            // delayed by a timer — a timer is a guess about how many lines are
            // in front of it. Flush is called from the one place that knows the
            // turn's damage has resolved, immediately before the scales.
            // THE RESURFACE TAKES THE ORDINARY PATH. (0.7.282 — CLOSED.)
            //
            // Zamar, asked what a card coming back up should say: "no line
            // needed for resurfacing. The ability explains what it's doing.
            // The trigger marks when it is."
            //
            // 0.7.283 — and having heard the bare line he added the words:
            // "It resurfaces." So the generic sentence IS the answer, with the
            // resurface flavour on it, and Waterborne is back in
            // Vocabulary._sigilFlavour — which is only reached by this path,
            // never by the dive.
            //
            // 0.7.266 silenced this half while the wording was open. Nothing
            // special-cases Submerge here now; it is held until the scales
            // like any other entry in _heldUntilScales, which is his 0.7.252
            // ruling and is unchanged.
            //
            // The DIVE keeps his flavour sentence, because that one describes
            // something that happens on the table. See Vocabulary.WaterborneDive.

            // 0.7.361 — THE RESURFACE IS SILENT AGAIN. Zamar: "Remove this
            // resurfaces callout, it's unneeded." This path is only reached by
            // Submerge's resurface (the dive has its own patch), so the whole
            // line goes.
            if (capturedAbility == Ability.Submerge || capturedAbility == Ability.SubmergeSquid)
            {
                Plugin.Log?.LogInfo("IKMA SIGIL: Waterborne resurface — not spoken.");
                return;
            }

            if (_heldUntilScales.Contains(capturedAbility))
            {
                // THE CARD, NOT ITS SENTENCE. (0.7.253.) 0.7.252 held a
                // composed line each, which released three lines for three
                // birds. Zamar: "add all three of those to one single call right
                // before scales." Holding the cards instead lets the flush say
                // it once, and the names are still read at speak time so a card
                // destroyed in the meantime drops out of the list rather than
                // being named from a stale capture.
                _held.Add(new HeldTrigger { Card = capturedCard, Ability = capturedAbility });
                Plugin.Log?.LogInfo(
                    $"IKMA SIGIL: holding {SigilName(capturedAbility)} on " +
                    $"{SafeName(capturedCard) ?? "a card"} until the turn's damage is read.");
                return;
            }

            // MORSEL IS CAUSED BY THE PRESS THAT IS ABOUT TO BE CONFIRMED.
            // (0.7.293.) Zamar: "Morsel is an exception since it triggers on
            // play. So Morsel should read." Morsel.OnSacrifice runs while the
            // player is paying for a card, and "X played in Slot N" lands a
            // fraction later and used to cut it in half. Shielding here rather
            // than at the placement call site keeps the reason with the line
            // that earned it.
            if (capturedAbility == Ability.Morsel)
            {
                Speech.ShieldFromNextConfirmation("Morsel triggers on the play being confirmed");
                // 0.7.341 — not queued on its own: the sacrifice line says it,
                // after "X is sacrificed", because Morsel fires as the card is
                // given up. See SacrificeRecord.AddMorselLine.
                SacrificeRecord.AddMorselTrigger(capturedCard, line());
                return;
            }

            // ANY TRIGGER FIRED BY A SACRIFICE PAYMENT. (0.7.339.) His 0.7.338
            // log: The Smoke was sacrificed to pay for a Cuckoo, Bone King
            // fired off that death, and "The Smoke's Bone King ability
            // triggers, granting four bones." was cut by "Cuckoo played in Slot
            // 2." Zamar: "This should not have gotten stomped." Same shape as
            // Morsel — the trigger and the confirmation are two halves of one
            // press — but the line is spoken seconds after the trigger, while
            // he is still choosing a slot. So the shield is armed when the line
            // is SPOKEN, not when it is queued, or it would expire before the
            // confirmation it is meant to hold back.
            bool fromSacrifice = UnityEngine.Time.unscaledTime - SacrificeBoneMerger.LastSacrificeAt < 0.5f;

            // 0.7.342 — AND WHILE THE SACRIFICE LINE IS STILL UNSAID, IT RIDES
            // THAT LINE, exactly as Morsel does. His 0.7.341 log:
            //   "Squirrel in Slot 2 and The Smoke in Slot 3 sacrificed.
            //    Received 5 bones." / "Ouroboros played in Slot 3." /
            //   "The Smoke's Bone King ability triggers, granting four bones."
            // The shield below only arms when the trigger line is SPOKEN, and
            // this one was still queued behind the sacrifice line when the
            // placement confirmation arrived. One entry cannot be split.
            if (fromSacrifice && SacrificeBoneMerger.LinePending)
            {
                SacrificeRecord.AddMorselLine(line());
                return;
            }

            if (fromSacrifice)
            {
                var inner = line;
                using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
                {
                    string said = inner();
                    if (!string.IsNullOrEmpty(said))
                        Speech.ShieldFromNextConfirmation(
                            $"{SigilName(capturedAbility)} fired off a sacrifice payment");
                    return said;
                });
                return;
            }

            using (Speech.Event(EventKind.Powers)) Speech.Result(line);
        }

        /// <summary>
        /// Sigils whose line belongs with the turn's result rather than with the
        /// moment the behaviour ran. Ability.Submerge is the enum behind the
        /// sigil the rulebook calls Waterborne.
        /// </summary>
        private static readonly HashSet<Ability> _heldUntilScales
            = new HashSet<Ability> { Ability.Submerge, Ability.SubmergeSquid };

        private struct HeldTrigger
        {
            public PlayableCard Card;
            public Ability      Ability;
        }

        private static readonly List<HeldTrigger> _held = new List<HeldTrigger>();

        private static string SafeName(PlayableCard card)
        {
            try { return CardReader.CardName(card?.Info); } catch { return null; }
        }

        /// <summary>
        /// Drop whatever was held, unsaid. (0.7.359.) Zamar, on "Kingfisher's
        /// Waterborne ability triggers. It resurfaces." arriving after "You deal
        /// 3 direct damage. The scale hits 5...": "This line should be stomped
        /// by The Scale hits 5." These lines are held to land right before the
        /// scales, and when the scale hits 5 that moment is the damage line
        /// itself, which already stomps every combat line before it (0.7.247).
        /// </summary>
        internal static void DropHeld(string reason)
        {
            if (_held.Count == 0) return;
            Plugin.Log?.LogInfo($"IKMA SIGIL: dropping {_held.Count} held trigger(s) unsaid — {reason}.");
            _held.Clear();
        }

        /// <summary>
        /// Speak whatever was held, now. Called immediately before the scales
        /// line, and again on a turn change so nothing can be stranded by a turn
        /// that dealt no damage.
        /// </summary>
        internal static void FlushHeld(string reason)
        {
            if (_held.Count == 0) return;

            Plugin.Log?.LogInfo($"IKMA SIGIL: releasing {_held.Count} held trigger(s) — {reason}.");

            // ONE SENTENCE PER SIGIL, however many cards did it. Grouped by
            // ability rather than flattened, because two different sigils firing
            // in the same turn are two different things to say — this only
            // collapses the repetition, never the content.
            var order  = new List<Ability>();
            var groups = new Dictionary<Ability, List<PlayableCard>>();

            foreach (var h in _held)
            {
                List<PlayableCard> cards;
                if (!groups.TryGetValue(h.Ability, out cards))
                {
                    cards = new List<PlayableCard>();
                    groups[h.Ability] = cards;
                    order.Add(h.Ability);
                }
                cards.Add(h.Card);
            }

            _held.Clear();

            foreach (var ability in order)
            {
                var captured        = groups[ability];
                var capturedAbility = ability;

                using (Speech.Event(EventKind.Powers)) Speech.Result(() =>
                {
                    // Names at speak time, the rule this file already follows: a
                    // card that has been destroyed since it dived is not named.
                    var names = new List<string>();
                    foreach (var c in captured)
                    {
                        string n = SafeName(c);
                        if (!string.IsNullOrEmpty(n)) names.Add(n);
                    }

                    if (names.Count == 0) return null;

                    string sigil = SigilName(capturedAbility);
                    if (string.IsNullOrEmpty(sigil)) return null;

                    return Vocabulary.SigilTriggers(names, sigil);
                });
            }
        }
    }

    // Registered through Plugin.TryPatch, never PatchAll. The target is the
    // BASE declaration; see the header of this file for why one patch reaches
    // all 46 behaviour classes.
    public class AbilityBehaviour_PreSuccessfulTriggerSequence_Patch
    {
        static void Prefix(AbilityBehaviour __instance)
            => SigilTriggers.NoteTriggered(__instance);
    }

    // =========================================================================
    // THE DIVE.  Submerge.OnTurnEnd()  (PUBLIC override)
    //
    // The half of Waterborne the generic sigil hook cannot see. Its body is
    // SetCardbackSubmerged / SetFaceDown(true) / LearnAbility, with no call to
    // PreSuccessfulTriggerSequence anywhere in it, so nothing in IKMA has ever
    // fired on a card going under. See Vocabulary.WaterborneDive.
    //
    // A PREFIX FIRES AT ENUMERATOR CREATION, so the card is still face up here
    // and the name still reads. That is also why this is deferred: the line is
    // composed at speak time, after the dive has actually happened, and a card
    // that left the board in between drops out instead of being named from a
    // stale capture — the same shape the held triggers use above.
    //
    // Registered through Plugin.TryPatch, not by attribute: one wrong member
    // name in PatchAll kills every patch after it.
    // =========================================================================
    public static class Submerge_OnTurnEnd_Patch
    {
        public static void Prefix(Submerge __instance)
        {
            try
            {
                // AbilityBehaviour.Card is PROTECTED — its body is just
                // GetComponent<PlayableCard>(), so ask the component directly
                // rather than reflecting a property that is the same call.
                var card = __instance != null
                    ? __instance.GetComponent<PlayableCard>()
                    : null;
                if (card == null) return;

                string name = null;
                try { name = CardReader.CardName(card.Info); } catch { }
                if (string.IsNullOrEmpty(name)) return;

                Plugin.Log?.LogInfo($"IKMA SIGIL: '{name}' dives (Submerge.OnTurnEnd).");

                var captured = card;
                string captuedName = name;
                using (Speech.Event(EventKind.Powers)) Speech.Result(delegate
                {
                    // Gone from the board between the dive starting and this
                    // line reaching the front of the queue: say nothing rather
                    // than report a card that is not there.
                    try { if (captured == null || captured.Dead) return null; }
                    catch { return null; }

                    return Vocabulary.WaterborneDive(captuedName);
                });
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA SIGIL: dive — {e.GetType().Name}: {e.Message}");
            }
        }
    }
    /// <summary>
    /// Which sigil is resolving, and whether the game negated it. (0.7.305.)
    /// </summary>
    /// <remarks>
    /// A sigil that fires and achieves nothing shakes the card — that shake is
    /// the only cue a sighted player gets, and IKMA was announcing the trigger
    /// and then saying nothing more. Zamar approved "Bloodhound's Guardian
    /// ability does nothing." on 2026-09-12; this is what decides when to say
    /// it.
    ///
    /// CardAnimationController.StrongNegationEffect is PUBLIC and has 21
    /// callers, of which only ten are abilities — the rest are ShopUI,
    /// HammerButton, CardDrawPiles, TalkingCard and the combat manager.
    /// Unguarded this would say "does nothing" in the shop. Two guards keep it
    /// honest: the shake must land on a card with an OUTSTANDING record, and
    /// GlobalTriggerHandler.StackSize must be above zero, which is the game's
    /// own answer to "is a trigger resolving right now".
    /// </remarks>
    internal static class FizzleWatch
    {
        internal class Record
        {
            internal PlayableCard Card;
            internal Ability Ability;
            internal bool Fizzled;
            internal float OpenedAt;
        }

        // Long enough to cover a coroutine's own waits (Sharp waits 0.55s
        // before it negates), short enough that a shake from something else
        // seconds later cannot reach back into it.
        private const float WINDOW_SECONDS = 3f;

        private static readonly System.Collections.Generic.List<Record> _open
            = new System.Collections.Generic.List<Record>();

        internal static Record Open(PlayableCard card, Ability ability)
        {
            if (card == null) return null;

            float now = 0f;
            try { now = UnityEngine.Time.unscaledTime; } catch { }

            Prune(now);

            var r = new Record { Card = card, Ability = ability, OpenedAt = now };
            _open.Add(r);
            return r;
        }

        /// <summary>The game shook a card. If it owns a live record, that sigil achieved nothing.</summary>
        internal static void NoteNegation(PlayableCard card)
        {
            if (card == null) return;

            int stack = 0;
            try { stack = Singleton<GlobalTriggerHandler>.Instance != null
                        ? Singleton<GlobalTriggerHandler>.Instance.StackSize : 0; } catch { }
            if (stack <= 0) return;

            float now = 0f;
            try { now = UnityEngine.Time.unscaledTime; } catch { }
            Prune(now);

            for (int i = _open.Count - 1; i >= 0; i--)
            {
                if (_open[i].Card != card) continue;
                if (_open[i].Fizzled) continue;
                _open[i].Fizzled = true;
                return;
            }
        }

        private static void Prune(float now)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
                if (now - _open[i].OpenedAt > WINDOW_SECONDS) _open.RemoveAt(i);
        }
    }

    public static class CardAnimationController_StrongNegationEffect_Patch
    {
        public static void Prefix(CardAnimationController __instance)
        {
            try
            {
                PlayableCard card = null;
                try { card = __instance != null ? __instance.GetComponentInParent<PlayableCard>() : null; } catch { }
                FizzleWatch.NoteNegation(card);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: negation prefix threw {e.GetType().Name}.");
            }
        }
    }

}

// SigilTriggers.cs
