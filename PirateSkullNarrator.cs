// PirateSkullNarrator.cs
using System.Collections.Generic;
using System.Reflection;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The Pirate Skull — Kaycee's Mod's fifth boss, and the one a player only
    /// ever meets with the Final Boss challenge switched on. His cannons, his
    /// rodent pack, and the ship.
    /// </summary>
    /// <remarks>
    /// 0.7.212, Session 21. Written from the decompiled
    /// <c>PirateSkullBossOpponent</c>, <c>PirateSkullBattleSequencer</c> and
    /// <c>PirateSkullBossCannons</c>; the step-by-step script is
    /// <c>docs/BOSS_SCRIPTS_KM.md</c>. Every reflected member is listed in
    /// <c>dumps/dump_pirateskull_from_decompile.txt</c>.
    ///
    /// WHERE HE COMES FROM. With <c>AscensionChallenge.FinalBoss</c> active the
    /// last Kaycee's Mod region is Pirateville instead of Midnight, and its
    /// authored layout ends in him rather than in Leshy. He REPLACES Leshy; a
    /// run meets one or the other, never both.
    ///
    /// THE ONE LINE THAT MATTERS. Every player upkeep after turn one, two
    /// slots get a crosshair — one of his, one of YOURS — and on the next
    /// upkeep the cannons put 10 damage into whatever is still standing
    /// there. Ten kills nearly everything in Act 1. The crosshair is purely
    /// visual and makes no sound, and the player has exactly one turn to move
    /// the card or accept the loss. Without <see cref="OnTargetsChosen"/> a
    /// blind player loses a creature a turn to something they were never told
    /// about.
    ///
    /// THE CANNONS FIRING IS DELIBERATELY NOT SPOKEN. It has its own loud
    /// cannon sound, and PlayableCard.TakeDamage is already patched, so the
    /// hit itself arrives as a damage line naming the card and — when 10 is
    /// lethal, which it usually is — a merged death line. A "the cannons
    /// fire" line on top of that is a second line for one event, and it would
    /// talk over the game's own voice. The firing is LOGGED instead, so the
    /// order can still be read out of a playtest. If Zamar wants it spoken it
    /// is one call in <see cref="OnCannonsFire"/>.
    ///
    /// NO CANDLE LINE HERE, AND THAT IS CORRECT. PirateSkullBossOpponent
    /// overrides LifeLostSequence and never calls base — so no candle is
    /// blown out on screen either, and BossNarrator's patch on
    /// Part1BossOpponent.LifeLostSequence rightly does not fire. Parity: if
    /// the page hides it from everyone, IKMA hides it too. What a sighted
    /// player DOES see at each life lost is the phase changing, which is what
    /// the two phase lines below carry.
    ///
    /// THE SHIP IS NOT IN THIS FILE. It is a plain GiantCard, exactly like
    /// Leshy's moon, so GiantCardNarrator handles its arrival and its death
    /// for both bosses. Only his sequencer's OnOtherCardDie is registered
    /// from here, and it points at the same handler.
    ///
    /// WORDING IS PROVISIONAL. Every sentence is in Vocabulary.cs under the
    /// Pirate Skull heading and every use logs "IKMA PROVISIONAL".
    /// </remarks>
    public static class PirateSkullNarrator
    {
        private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

        private static bool      _resolved;
        private static FieldInfo _cannonTargetsField;
        private static FieldInfo _targetIconsField;

        /// <summary>
        /// How long to wait before reading the chosen targets. The choice
        /// happens about 0.3s into ChooseCannonTargetsSequence and the
        /// crosshairs are placed by about 0.65s; this reads at 1.25s, well
        /// clear of both.
        ///
        /// "Settle before speaking. Settle windows are free; nothing here is
        /// reaction-timed" — and this one especially is not: the player has a
        /// whole turn to answer it.
        /// </summary>
        private const float TARGET_SETTLE_SECONDS = 1.25f;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                _cannonTargetsField =
                    typeof(PirateSkullBattleSequencer).GetField("cannonTargetSlots", Priv);
                _targetIconsField =
                    typeof(PirateSkullBattleSequencer).GetField("targetIcons", Priv);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA PIRATE: reflection setup failed — {e.GetType().Name}: {e.Message}");
            }
        }

        // ==================================================================
        // THE CROSSHAIRS STAY ON THE TABLE. (0.7.344.)
        //
        // The aim line is said once, when the cannons choose. A sighted
        // player keeps SEEING the two crosshairs for the whole turn, including
        // while choosing where to put a card. So every slot read asks this:
        // is the slot being read one the cannons are aimed at. Read live off
        // the running sequencer (TurnManager.SpecialSequencer, a property,
        // no search), so it is never stale and is false outside this fight.
        // ==================================================================
        // ==================================================================
        // THE EYE GLOWS. (0.7.347.)
        //
        // Zamar, 0.7.346: before "Pirate Skull: Y-YAR?" there should be "The
        // eye of the skull begins to glow."
        //
        // PirateSkullBossOpponent.IntroSequence, in order:
        //     PlayDialogueEvent("PirateSkullIntro1")   <- Leshy: HM? ...
        //     Animator.SetTrigger("wake_up")           <- the eye lights
        //     PlayDialogueEvent("PirateSkullIntro2")   <- Y-YAR?
        //
        // Hung on the dialogue id, the same way the copy card's scene lines
        // are: the game names the moment, so nothing is timed. The shared
        // PlayDialogueEvent prefix lives in CopyCardNarrator.cs.
        // ==================================================================
        internal const string INTRO_2 = "PirateSkullIntro2";

        // ==================================================================
        // THE SHIP'S ARRIVAL LINE WAITS FOR THE SKULL. (0.7.348.)
        //
        // Zamar: "YAR, ME CREW BE THE FINEST ON THE SEAS!" "should come
        // before the Limoncello line."
        //
        // StartGiantCardPhase, in order:
        //     CreateCardInSlot(!GIANTCARD_SHIP)          <- giant line queued
        //     ... ~3.5s of camera ...
        //     PlayDialogueEvent("PirateSkullShipSpawned") (first time only)
        //
        // So the giant line is held, and released when that conversation has
        // run to its end — the postfix wraps the dialogue's enumerator, which
        // finishes after the player has pressed through the last line. The
        // game skips the conversation once it has been played, and then the
        // line is not held at all.
        // ==================================================================
        internal const string SHIP_SPAWNED = "PirateSkullShipSpawned";
        private static System.Func<string> _heldShipLine;

        /// <summary>True = held; GiantCardNarrator must not speak it now.</summary>
        internal static bool HoldShipLine(System.Func<string> line)
        {
            try
            {
                if (!(TurnManager.Instance?.Opponent is PirateSkullBossOpponent)) return false;
                if (DialogueEventsData.EventIsPlayed(SHIP_SPAWNED)) return false;
            }
            catch { return false; }

            _heldShipLine = line;
            Plugin.Log?.LogInfo("IKMA PIRATE: ship line held until the skull's conversation ends.");
            return true;
        }

        // 0.7.350 — Zamar: after "BUT I'VE GOT YE ONE LAST TRICK!" is
        // progressed, "The pirate skull fades backwards... Something is
        // coming..." PirateSkullPreCharge is that conversation; when it ends
        // the skull tweens 200 units away and the ship begins. HIS WORDING.
        internal const string PRE_CHARGE = "PirateSkullPreCharge";

        internal static System.Collections.IEnumerator WrapDialogue(string eventId,
            System.Collections.IEnumerator inner)
        {
            if (inner == null) return inner;
            if (eventId == PRE_CHARGE) return SayAfter(inner, Vocabulary.PirateSkullRecedes());
            if (eventId != SHIP_SPAWNED || _heldShipLine == null) return inner;
            return ReleaseAfter(inner);
        }

        private static System.Collections.IEnumerator SayAfter(System.Collections.IEnumerator inner, string line)
        {
            try { while (inner.MoveNext()) yield return inner.Current; }
            finally
            {
                Plugin.Log?.LogInfo("IKMA PIRATE: pre-charge conversation over.");
                using (Speech.Event(EventKind.Bosses)) Speech.Result(line);
            }
        }

        private static System.Collections.IEnumerator ReleaseAfter(System.Collections.IEnumerator inner)
        {
            try
            {
                while (inner.MoveNext()) yield return inner.Current;
            }
            finally
            {
                var line = _heldShipLine;
                _heldShipLine = null;
                if (line != null)
                {
                    Plugin.Log?.LogInfo("IKMA PIRATE: conversation over — ship line released.");
                    using (Speech.Event(EventKind.Bosses)) Speech.Result(line);
                }
            }
        }

        /// <summary>A held line never outlives its battle.</summary>
        internal static void Reset()
        {
            _heldShipLine = null;
            _mutinyBefore = null;
            _ship = null;
            _shipSceneSaid = false;
            _cannonVictims.Clear();
            _cannonSlots.Clear();
            _openHits.Clear();
        }

        // ==================================================================
        // THE SKELETONS JUMP SHIP. (0.7.348.)
        //
        // Zamar: "we need a line for when a skeleton crew jumps off the ship
        // and into our slots. All in one line." The board diff was reading
        // each as "Your Skeleton Crew is played in slot 4.", one turn-boundary
        // at a time.
        //
        // GiantShip.OnUpkeep -> MutinySequence: for each skeleton, a free
        // PLAYER slot is picked and CreateCardInSlot("SkeletonPirate") puts
        // it there; then, every other mutiny, PlayDialogueEvent(
        // "PirateSkullShipMutinee") — "M-ME CREW? MUTINEERS?!". The line goes
        // BEFORE that dialogue when there is one, and at the end of the
        // upkeep when there is not. Which cards are new is read off the
        // player's slots against a snapshot taken when the upkeep began.
        // ==================================================================
        internal const string MUTINEE = "PirateSkullShipMutinee";
        private static List<PlayableCard> _mutinyBefore;
        private static string _mutinyShipName;

        internal static System.Collections.IEnumerator WrapMutiny(GiantShip ship,
            System.Collections.IEnumerator inner)
        {
            if (inner == null) return inner;
            _mutinyBefore = new List<PlayableCard>();
            _mutinyShipName = null;
            _ship = ship;
            try
            {
                var pc = ship != null ? ship.GetComponent<PlayableCard>() : null;
                if (pc?.Info != null) _mutinyShipName = CardReader.CardName(pc);
                foreach (var s in Singleton<BoardManager>.Instance.PlayerSlotsCopy)
                {
                    var c = BoardReader.LiveCard(s);
                    if (c != null) _mutinyBefore.Add(c);
                }
            }
            catch { }
            return RunMutiny(inner);
        }

        private static System.Collections.IEnumerator RunMutiny(System.Collections.IEnumerator inner)
        {
            try
            {
                while (inner.MoveNext()) yield return inner.Current;
            }
            finally
            {
                FlushMutiny();
            }
        }

        // ==================================================================
        // HOW MANY ARE LEFT ON DECK. (0.7.351.)
        //
        // Zamar: "Each time a single or multiple pirates jump off the
        // Limoncello, add how many remain on deck afterwards." And the ship's
        // arrival: "It has 15 dancing Skeleton Pirates on board".
        //
        // GiantShip.MAX_SKELES (15, private const) less skelesSpawned (private
        // int, incremented once per skeleton placed). Both asked of the ship,
        // not counted by IKMA, so a mutiny IKMA missed cannot skew it.
        // ==================================================================
        private static GiantShip _ship;

        internal static int SkeletonsOnDeck(GiantShip ship)
        {
            if (ship == null) return -1;
            try
            {
                var t = typeof(GiantShip);
                int max = 15;
                var maxField = t.GetField("MAX_SKELES", BindingFlags.NonPublic | BindingFlags.Static);
                if (maxField != null) max = System.Convert.ToInt32(maxField.GetRawConstantValue());
                var spawned = t.GetField("skelesSpawned", BindingFlags.NonPublic | BindingFlags.Instance);
                if (spawned == null) return -1;
                int n = (int)spawned.GetValue(ship);
                return System.Math.Max(0, max - n);
            }
            catch { return -1; }
        }

        private static bool _shipSceneSaid;

        /// <summary>
        /// The ship crashing down, once per battle, before the skull's line
        /// about his crew. HIS WORDING, 0.7.351.
        /// </summary>
        internal static void SayShipScene()
        {
            if (_shipSceneSaid) return;
            _shipSceneSaid = true;

            string name = null;
            int onDeck = 15;
            try
            {
                foreach (var s in Singleton<BoardManager>.Instance.OpponentSlotsCopy)
                {
                    var c = BoardReader.LiveCard(s);
                    var ship = c != null ? c.GetComponent<GiantShip>() : null;
                    if (ship == null) continue;
                    name = CardReader.CardName(c);
                    int left = SkeletonsOnDeck(ship);
                    if (left >= 0) onDeck = left;
                    break;
                }
            }
            catch { }
            if (string.IsNullOrEmpty(name)) name = Vocabulary.PirateSkull.Limoncello;

            Plugin.Log?.LogInfo("IKMA PIRATE: ship scene line.");
            using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.PirateShipCrashes(name, onDeck));
        }

        /// <summary>Speak the jump line now, if there is one owed.</summary>
        internal static void FlushMutiny()
        {
            var before = _mutinyBefore;
            _mutinyBefore = null;
            if (before == null) return;

            var slots = new List<int>();
            string name = null;
            try
            {
                var player = Singleton<BoardManager>.Instance.PlayerSlotsCopy;
                for (int i = 0; i < player.Count; i++)
                {
                    var c = BoardReader.LiveCard(player[i]);
                    if (c?.Info == null || before.Contains(c)) continue;
                    if (c.Info.name != "SkeletonPirate") continue;
                    slots.Add(i + 1);
                    if (name == null) name = CardReader.CardName(c);
                    BoardWatcher.NoteAnnounced(c);
                }
            }
            catch { }

            if (slots.Count == 0 || string.IsNullOrEmpty(name)) return;
            int onDeck = SkeletonsOnDeck(_ship);
            Plugin.Log?.LogInfo($"IKMA PIRATE: mutiny — {slots.Count} x {name} into your slots, {onDeck} on deck.");
            using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.PirateMutiny(name, _mutinyShipName, slots)
                          + Vocabulary.PirateSkeletonsOnDeck(onDeck));
        }

        internal static void OnEyeGlow()
        {
            try
            {
                Plugin.Log?.LogInfo("IKMA PIRATE: the skull wakes, ahead of PirateSkullIntro2.");
                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.PirateSkullEyeGlows());
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: pirate eye glow threw {e.GetType().Name}.");
            }
        }

        // 0.7.348 — THE CROSSHAIR IS THE ICON, NOT THE LIST. Zamar's Limoncello
        // read ended "The cannons are aimed at slot 4": "either inaccurate or
        // hidden info". StartGiantCardPhase calls CleanupTargetIcons, which
        // destroys the crosshairs and clears targetIcons — but never clears
        // cannonTargetSlots, so the list still named last turn's slot while
        // nothing was drawn on it. A slot is a target when a crosshair icon
        // is sitting on it, which is what a sighted player sees.
        internal static bool IsCannonTarget(CardSlot slot)
        {
            if (slot == null) return false;
            try
            {
                var seq = TurnManager.Instance?.SpecialSequencer as PirateSkullBattleSequencer;
                if (seq == null) return false;
                var targets = Targets(seq);
                if (targets == null || !targets.Contains(slot)) return false;

                var icons = _targetIconsField?.GetValue(seq) as List<UnityEngine.GameObject>;
                if (icons == null) return false;
                foreach (var icon in icons)
                    if (icon != null && icon.transform.parent == slot.transform) return true;
                return false;
            }
            catch { return false; }
        }

        private static List<CardSlot> Targets(PirateSkullBattleSequencer sequencer)
        {
            Resolve();
            try { return _cannonTargetsField?.GetValue(sequencer) as List<CardSlot>; }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        // THE CROSSHAIRS GO DOWN.
        // PirateSkullBattleSequencer.ChooseCannonTargetsSequence()  (NONPUBLIC)
        //
        // The targets are picked INSIDE this coroutine, so neither a prefix
        // nor a Harmony postfix can read them — on an iterator method both
        // run at enumerator CREATION, before a single line of the body. The
        // read is therefore scheduled, not taken: EnqueueActionDeferredDelayed
        // composes the sentence after the settle window, off the same private
        // field the game itself uses to decide where to fire.
        //
        // WHICH SIDE EACH SLOT IS ON IS ASKED, NOT ASSUMED. The game adds the
        // opponent's slot first and the player's second, but that order is an
        // implementation detail of one method; CardSlot.IsPlayerSlot is the
        // board's own answer and cannot drift. The player's own slot leads the
        // sentence because it is the half they can act on.
        // ------------------------------------------------------------------
        internal static void OnTargetsChosen(PirateSkullBattleSequencer sequencer)
        {
            try
            {
                Plugin.Log?.LogInfo(
                    $"IKMA PIRATE: cannons aiming — targets read in {TARGET_SETTLE_SECONDS}s.");

                using (Speech.Event(EventKind.Bosses)) Speech.Result(
                    new System.Func<string>(delegate
                    {
                        try
                        {
                            var targets = Targets(sequencer);
                            if (targets == null || targets.Count == 0)
                            {
                                Plugin.Log?.LogInfo("IKMA PIRATE: no cannon targets readable — not spoken.");
                                return null;
                            }

                            string yours = null, his = null;

                            for (int i = 0; i < targets.Count; i++)
                            {
                                var slot = targets[i];
                                if (slot == null) continue;

                                bool playerSide;
                                int  number;
                                try { playerSide = slot.IsPlayerSlot; number = slot.Index + 1; }
                                catch { continue; }

                                // BoardReader.LiveCard, not slot.Card: a card
                                // already destroyed but still sitting in its
                                // slot must not be named as the thing under
                                // the crosshair.
                                string card = null;
                                try { card = CardReader.CardName(BoardReader.LiveCard(slot)?.Info); }
                                catch { }

                                string phrase = string.IsNullOrEmpty(card)
                                    ? Vocabulary.CannonTargetEmpty(number)
                                    : Vocabulary.CannonTargetCard(card, number);

                                if (playerSide) yours = phrase; else his = phrase;
                            }

                            if (yours == null && his == null) return null;

                            Plugin.Log?.LogInfo(
                                $"IKMA PROVISIONAL: cannon targets — yours='{yours ?? "none"}', his='{his ?? "none"}'.");

                            return Vocabulary.CannonsAimed(yours, his);
                        }
                        catch { return null; }
                    }),
                    TARGET_SETTLE_SECONDS);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA PIRATE: aim — {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // THE CANNONS FIRE.
        // PirateSkullBattleSequencer.FireCannonsSequence()  (NONPUBLIC)
        //
        // LOG ONLY, on purpose — see the file header. The targets were chosen
        // a turn ago, so they are fully settled at enumerator creation and
        // this can record exactly what was about to be hit, which is the one
        // thing a playtest log cannot otherwise reconstruct: the game SKIPS a
        // target whose card has moved or died, silently, and without this
        // line a missing damage report looks like a narration bug rather than
        // a card that got away.
        // ------------------------------------------------------------------
        // ==================================================================
        // THE CANNONBALL LINE. (0.7.350.)
        //
        // Zamar: "Change to 'Cannons fire at your [slot 3], [Mantis God] is
        // hit by cannonball and dies.' Hide that it does 10 damage. That's not
        // shown visually."
        //
        // FireCannonsSequence ends each shot with slot.Card.TakeDamage(10,
        // null). The cards in the crosshairs are captured here; their hit
        // (attacker null) is claimed by TryCannonHit instead of the ordinary
        // damage line, and the death folds in. The line waits, holding its
        // place, until that TakeDamage has run to the end — death included.
        // "their slot" for his side mirrors his "your slot".
        // ==================================================================
        private static readonly List<PlayableCard> _cannonVictims = new List<PlayableCard>();
        private static readonly List<CardSlot> _cannonSlots = new List<CardSlot>();

        private class CannonHit
        {
            internal PlayableCard Card;
            internal CardSlot Slot;
            internal bool Died;
            internal bool Done;
        }
        private static readonly List<CannonHit> _openHits = new List<CannonHit>();

        /// <summary>TakeDamage postfix. True = the cannon line owns this hit.</summary>
        internal static bool TryCannonHit(PlayableCard card, PlayableCard attacker,
            ref System.Collections.IEnumerator result)
        {
            if (card == null || attacker != null) return false;
            int i = _cannonVictims.IndexOf(card);
            if (i < 0) return false;

            var hit = new CannonHit { Card = card, Slot = _cannonSlots[i] };
            _cannonVictims.RemoveAt(i);
            _cannonSlots.RemoveAt(i);
            _openHits.Add(hit);

            using (Speech.Event(EventKind.Bosses)) Speech.ResultWhenReady(() => hit.Done, () => ComposeCannon(hit), 10f,
                "[cannonball]");
            if (result != null) result = RunHit(result, hit);
            else hit.Done = true;
            return true;
        }

        private static System.Collections.IEnumerator RunHit(System.Collections.IEnumerator inner, CannonHit hit)
        {
            try { while (inner.MoveNext()) yield return inner.Current; }
            finally { hit.Done = true; _openHits.Remove(hit); }
        }

        /// <summary>Die prefix. True = the cannon line says this death.</summary>
        internal static bool TryCannonDeath(PlayableCard card)
        {
            foreach (var h in _openHits)
                if (ReferenceEquals(h.Card, card)) { h.Died = true; return true; }
            return false;
        }

        private static string ComposeCannon(CannonHit h)
        {
            string name = null;
            CardInfo info = null;
            try { info = h.Card?.Info; name = CardReader.CardName(info); } catch { }
            if (string.IsNullOrEmpty(name)) return null;

            string where = "";
            try { where = Vocabulary.PirateSkull.YourTheirSlot(h.Slot.IsPlayerSlot, h.Slot.Index + 1); } catch { }

            if (h.Died)
                return Vocabulary.PirateSkull.CannonsFireAtIs(where, name, info);

            int hp = -1;
            try { hp = h.Card.Health; } catch { }
            return Vocabulary.PirateSkull.CannonsFireAtOrCannonsFireAt(hp, where, name);
        }

        internal static void OnCannonsFire(PirateSkullBattleSequencer sequencer)
        {
            _cannonVictims.Clear();
            _cannonSlots.Clear();
            try
            {
                var targets = Targets(sequencer);
                if (targets == null || targets.Count == 0) return;

                for (int i = 0; i < targets.Count; i++)
                {
                    var slot = targets[i];
                    if (slot == null) continue;

                    string side = "?";
                    int number = -1;
                    try { side = slot.IsPlayerSlot ? "yours" : "his"; number = slot.Index + 1; }
                    catch { }

                    var live = BoardReader.LiveCard(slot);
                    string card = null;
                    try { card = CardReader.CardName(live?.Info); } catch { }

                    Plugin.Log?.LogInfo(card == null
                        ? $"IKMA PIRATE: cannon at {side} slot {number} — nothing there, the game skips it."
                        : $"IKMA PIRATE: cannon at {side} slot {number} — '{card}' is the target.");

                    if (live != null)
                    {
                        _cannonVictims.Add(live);
                        _cannonSlots.Add(slot);
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA PIRATE: fire — {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // THE PHASE CHANGES.
        // PirateSkullBossOpponent.StartNewPhaseSequence()  (NONPUBLIC override)
        //
        // Reached from Part1BossOpponent.PostResetScalesSequence, NOT from
        // LifeLostSequence — which matters here, because his LifeLostSequence
        // override never calls base. NumLives is already the new value by the
        // time this enumerator is created; it is what the method switches on.
        //
        // Phase 2's rodent pack arrives as queued cards and two Mole Seamen
        // created in slots, all of which the queue reader and the board differ
        // already report in their own words — so this line says only that the
        // fight changed shape.
        //
        // Phase 3 is the one that needs saying. The charge is roughly fifteen
        // seconds of tweens, camera shake and a screen flash with no dialogue
        // awaited at the end of it. A sighted player is watching a ship bear
        // down on the cabin; a blind player has a long stretch of noise and
        // no statement of what it is.
        // ------------------------------------------------------------------
        internal static void OnPhaseChange(PirateSkullBossOpponent boss)
        {
            try
            {
                int lives = -1;
                try { lives = boss != null ? boss.NumLives : -1; } catch { }

                Plugin.Log?.LogInfo($"IKMA PIRATE: new phase at NumLives={lives}.");

                // 0.7.344 — BOTH PHASES CLEAR HIS BOARD AND QUEUE, and the
                // phase line says so. Same ruling as the Trapper's sweep, his
                // words: "Nothing from phase 1 needs to be called out here."
                // The cards that ARRIVE (Mole Seamen, the pack, the ship) are
                // still read by the diff.
                if (lives == 2 || lives == 1)
                    BoardWatcher.SuppressDeparturesFor(4f, "the Pirate Skull's new phase cleared his side");

                if (lives == 2)
                {
                    using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.PirateRodentPhase());
                    return;
                }

                // 0.7.348 — THE SHIP LINE IS GONE. Zamar, on hearing it:
                // "Pirate ship line too early, not till after conversation.
                // remove this line." The ship arriving is read by the giant
                // card's own line, which now waits for the skull's
                // conversation (HoldShipLine).
                if (lives == 1)
                    Plugin.Log?.LogInfo("IKMA PIRATE: ship phase begins — no line of its own.");
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA PIRATE: phase — {e.GetType().Name}: {e.Message}");
            }
        }
    }

    // Registered by Plugin.TryPatch. No [HarmonyPatch] attribute — CHECK 1
    // fails a class that carries both.

    public static class PirateSkullBattleSequencer_ChooseCannonTargets_Patch
    {
        public static void Prefix(PirateSkullBattleSequencer __instance)
            => PirateSkullNarrator.OnTargetsChosen(__instance);
    }

    public static class PirateSkullBattleSequencer_FireCannons_Patch
    {
        public static void Prefix(PirateSkullBattleSequencer __instance)
            => PirateSkullNarrator.OnCannonsFire(__instance);
    }

    public static class PirateSkullBossOpponent_StartNewPhaseSequence_Patch
    {
        public static void Prefix(PirateSkullBossOpponent __instance)
            => PirateSkullNarrator.OnPhaseChange(__instance);
    }

    // 0.7.348 — GiantShip.OnUpkeep is declared on GiantShip (PUBLIC override
    // of SpecialCardBehaviour's), and that is the class that runs. Registered
    // by Plugin.TryPatch, no attribute.
    public static class GiantShip_OnUpkeep_Patch
    {
        public static void Postfix(GiantShip __instance, ref System.Collections.IEnumerator __result)
        {
            __result = PirateSkullNarrator.WrapMutiny(__instance, __result);
        }
    }
}

// PirateSkullNarrator.cs
