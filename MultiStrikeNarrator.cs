// MultiStrikeNarrator.cs

using System.Collections.Generic;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// One line per multi-strike attacker: how many strikes, then what each
    /// card it hit ended up as. (0.7.347.)
    /// </summary>
    /// <remarks>
    /// Zamar, 0.7.346 Pirate log, two Mantis Gods (Trifurcated + Bifurcated,
    /// five strikes each) into two Mole Seamen with Burrower: "We have to
    /// condense that crazy double mantis god 5 attack ... way way down." And:
    /// "I dont need a play by play of every hit but I need it to be clear what
    /// happened."
    ///
    /// WHAT WAS WRONG WITH THE PLAY BY PLAY, besides length. Every per-hit
    /// line read Health LIVE AT SPEAK TIME, and ten hits were queued before
    /// the first was spoken, so the numbers were 6, 5, 3, 1, 1, 0, 0, 6, 6 for
    /// a card that went 8 to 3. The Burrower lines had the same defect — a
    /// card that hopped out and back read as "does nothing". The death line
    /// was so late that the enemy queue had already come down, and it claimed
    /// the arrival (see SlotArrival.QueueSnapshot).
    ///
    /// THE SHAPE, Claude's recommendation, approved by Zamar as written:
    ///
    ///   "Mantis God attacks 5 times. Mole Seaman burrows from slot 1 to
    ///    slot 3, taking 5 damage, 3 health remaining."
    ///   "Mantis God attacks 5 times. Mole Seaman in slot 3 burrows, taking 3
    ///    damage, and dies. Mole Seaman in slot 4 takes 2 damage, 6 health
    ///    remaining."
    ///
    /// - one clause per card hit, in the order it was first touched (strikes
    ///   run in slot order, so this is slot order);
    /// - a Burrower card names EVERY slot it passed through, 0.7.348. Zamar,
    ///   on hearing "burrows from slot 1 to slot 3": "This should list all the
    ///   slots he burrows to. 'Mole Seaman burrows from slot 1 to slot 2 to
    ///   slot 3, taking 5 damage, 3 health remaining.'" (0.7.347 said start and
    ///   end only, and a card that went out and back was just "burrows".)
    /// - "in slot N" only when another card on that side shares the name;
    /// - strikes into empty slots are not spoken here: the game adds them up
    ///   at the end of the phase and the existing direct-damage line reads
    ///   that total.
    ///
    /// HOW IT HOLDS ITS PLACE. The prefix on SlotAttackSequence fires at
    /// enumerator creation, before any strike. The summary is queued THERE, as
    /// an entry the announcer holds at the head until the attack finishes
    /// (Speech.ResultWhenReady), so it is spoken ahead of
    /// anything the attack triggers and composed only once every number is
    /// final. The finish is known exactly: the postfix wraps the enumerator
    /// and marks it done when the game has run the last instruction.
    ///
    /// WHAT IT DOES NOT TAKE OVER: damage to the ATTACKER (Sharp Quills) or to
    /// any card on the attacker's own side, and damage to a card still in the
    /// queue. Those keep their own lines, after this one.
    /// </remarks>
    internal static class MultiStrikeNarrator
    {
        private class Target
        {
            internal PlayableCard Card;
            internal string Name;
            internal CardInfo Info;
            internal int StartSlot = -1;   // 0-based
            internal int EndSlot = -1;     // 0-based, read at finish or death
            internal int Damage;
            internal bool Burrowed;
            internal readonly List<int> Path = new List<int>();   // 0-based slots, start first
            internal bool Died;
        }

        private class Attack
        {
            internal PlayableCard Attacker;
            internal string AttackerName;
            internal bool AttackerIsOpponent;
            internal int Strikes;
            internal int Bones;
            internal bool AttackerIsGiant;
            internal bool Finished;
            internal bool Wrapped;
            internal readonly List<Target> Targets = new List<Target>();
            internal readonly List<string> DefenderNamesAtStart = new List<string>();
        }

        private static Attack _current;

        /// <summary>
        /// Called from the SlotAttackSequence prefix with the game's own
        /// target list. Returns true if this attack will be summarised.
        /// </summary>
        internal static bool Begin(PlayableCard attacker, int strikes)
        {
            if (attacker?.Info == null || strikes <= 1) return false;

            // A previous attack that never reported finishing is closed now,
            // so its entry stops holding the queue.
            if (_current != null) _current.Finished = true;

            var a = new Attack
            {
                Attacker = attacker,
                AttackerName = Vocabulary.Submerged(attacker, CardReader.CardName(attacker.Info))
                               + DamageRecord.DeadlyNote(attacker),
                Strikes = strikes,
            };
            try { a.AttackerIsOpponent = attacker.OpponentCard; } catch { }
            a.AttackerIsGiant = GiantVolley.IsGiant(attacker);

            // Names on the defending side before any strike: a card that dies
            // mid-attack still made its name ambiguous.
            try
            {
                var bm = Singleton<BoardManager>.Instance;
                var side = a.AttackerIsOpponent ? bm?.PlayerSlotsCopy : bm?.OpponentSlotsCopy;
                if (side != null)
                    foreach (var s in side)
                    {
                        var c = BoardReader.LiveCard(s);
                        if (c?.Info != null) a.DefenderNamesAtStart.Add(CardReader.CardName(c.Info));
                    }
            }
            catch { }

            _current = a;

            using (Speech.Event(EventKind.Attacks)) Speech.ResultWhenReady(
                () => a.Finished,
                () => Compose(a),
                15f,
                $"[multi-strike summary: {CardReader.CardName(attacker.Info)}, {strikes} strikes]");
            return true;
        }

        /// <summary>Called from the SlotAttackSequence postfix.</summary>
        internal static System.Collections.IEnumerator Wrap(System.Collections.IEnumerator inner)
        {
            var a = _current;
            if (a == null || a.Wrapped || inner == null) return inner;
            a.Wrapped = true;
            return Run(inner, a);
        }

        private static System.Collections.IEnumerator Run(System.Collections.IEnumerator inner, Attack a)
        {
            try
            {
                while (inner.MoveNext()) yield return inner.Current;
            }
            finally
            {
                Finish(a);
            }
        }

        private static void Finish(Attack a)
        {
            if (a == null || a.Finished) return;

            foreach (var t in a.Targets)
            {
                if (t.Died) continue;
                try { t.EndSlot = t.Card?.Slot != null ? t.Card.Slot.Index : t.EndSlot; } catch { }
                // The board diff must not report a Burrower's move again.
                if (t.Burrowed)
                {
                    try { BoardWatcher.NoteAnnounced(t.Card); } catch { }
                }
            }

            a.Finished = true;
            if (ReferenceEquals(_current, a)) _current = null;
        }

        private static bool IsDefender(Attack a, PlayableCard card)
        {
            if (a == null || card == null || ReferenceEquals(card, a.Attacker)) return false;
            try
            {
                if (card.OpponentCard == a.AttackerIsOpponent) return false;
                return card.Slot != null;   // a queued card keeps its own line
            }
            catch { return false; }
        }

        private static Target Touch(Attack a, PlayableCard card, int startSlot)
        {
            foreach (var t in a.Targets)
                if (ReferenceEquals(t.Card, card)) return t;

            var nt = new Target { Card = card, StartSlot = startSlot, EndSlot = startSlot };
            nt.Path.Add(startSlot);
            try { nt.Info = card.Info; nt.Name = CardReader.CardName(card.Info); } catch { }
            a.Targets.Add(nt);
            return nt;
        }

        /// <summary>TakeDamage postfix. True = recorded here, no per-hit line.</summary>
        internal static bool TryRecordHit(PlayableCard card, int damage)
        {
            var a = _current;
            if (a == null || a.Finished || !IsDefender(a, card)) return false;

            int slot = -1;
            try { slot = card.Slot.Index; } catch { }
            var t = Touch(a, card, slot);
            t.Damage += damage;

            Plugin.Log?.LogInfo(
                $"IKMA MULTI: '{t.Name}' hit for {damage} in slot {slot + 1} " +
                $"({t.Damage} so far this attack).");
            return true;
        }

        /// <summary>Burrower. True = recorded here, no per-move line.</summary>
        internal static bool TryRecordBurrow(PlayableCard card, int fromSlot, int toSlot)
        {
            var a = _current;
            if (a == null || a.Finished || !IsDefender(a, card)) return false;

            var t = Touch(a, card, fromSlot);
            t.Burrowed = true;
            t.EndSlot = toSlot;
            t.Path.Add(toSlot);

            Plugin.Log?.LogInfo(
                $"IKMA MULTI: '{t.Name}' burrows slot {fromSlot + 1} -> {toSlot + 1}.");
            return true;
        }

        /// <summary>
        /// AddBones postfix. Bones paid by deaths inside the attack are said at
        /// the end of its line. (0.7.349.) Zamar, on the Limoncello's omni
        /// strike: "All my creatures dying to its omni attack need to resolve
        /// on one line" — the deaths were in it, and a "1 bone received." per
        /// death followed.
        /// </summary>
        internal static bool TryFoldBones(int amount)
        {
            var a = _current;
            if (a == null || a.Finished) return false;
            a.Bones += amount;
            return true;
        }

        /// <summary>PlayableCard.Die prefix. True = the summary speaks this death.</summary>
        internal static bool TryRecordDeath(PlayableCard card)
        {
            var a = _current;
            if (a == null || a.Finished) return false;

            foreach (var t in a.Targets)
            {
                if (!ReferenceEquals(t.Card, card)) continue;
                t.Died = true;
                try { if (card.Slot != null) t.EndSlot = card.Slot.Index; } catch { }
                Plugin.Log?.LogInfo($"IKMA MULTI: '{t.Name}' dies in slot {t.EndSlot + 1}.");
                return true;
            }
            return false;
        }

        private static bool Ambiguous(Attack a, Target t)
        {
            int count = 0;
            foreach (var n in a.DefenderNamesAtStart) if (n == t.Name) count++;
            if (count > 1) return true;

            foreach (var o in a.Targets)
                if (!ReferenceEquals(o, t) && o.Name == t.Name) return true;

            try
            {
                var bm = Singleton<BoardManager>.Instance;
                var side = a.AttackerIsOpponent ? bm?.PlayerSlotsCopy : bm?.OpponentSlotsCopy;
                if (side != null)
                    foreach (var s in side)
                    {
                        var c = BoardReader.LiveCard(s);
                        if (c?.Info == null || ReferenceEquals(c, t.Card)) continue;
                        if (CardReader.CardName(c.Info) == t.Name) return true;
                    }
            }
            catch { }
            return false;
        }

        private static string Compose(Attack a)
        {
            if (a == null) return null;

            var sb = new System.Text.StringBuilder();

            // 0.7.349 — A GIANT HITS EVERY CARD YOU HAVE. Zamar's wording:
            // "The Limoncello attacks each of your [one,two,three,four]
            // slot[s]." Omni Strike aims once at each occupied slot, so the
            // strike count IS the number of slots.
            if (a.AttackerIsGiant)
                sb.Append(Vocabulary.MultiStrike.AttacksEachOfYour(a.AttackerName, a.Strikes));
            else
                sb.Append(Vocabulary.MultiStrike.AttacksTimes(a.AttackerName, a.Strikes));

            foreach (var t in a.Targets)
            {
                if (string.IsNullOrEmpty(t.Name)) continue;

                string name = t.Name;
                // Session 32: CombatLineName - side AND, for a same-side twin, slot.
                try { name = DamageDeathMerger.CombatLineName(t.Card, t.Name); } catch { }

                string dmg = Vocabulary.DamageCount(t.Damage);

                string outcome;
                if (t.Died)
                {
                    string cause = "";
                    try { cause = DamageRecord.DeadlyCause(a.Attacker); } catch { }
                    outcome = Vocabulary.MultiStrike.AndDies(t.Info, cause);
                }
                else
                {
                    int h = -1;
                    try { h = t.Card != null ? t.Card.Health : -1; } catch { }
                    outcome = h < 0 ? null : (Vocabulary.HealthRemainingCount(h));
                }

                string inSlot = Ambiguous(a, t) && t.StartSlot >= 0 ? Vocabulary.MultiStrike.InStartSlot(t.StartSlot + 1) : "";

                string clause;
                if (t.Burrowed)
                {
                    var hops = new List<string>();
                    foreach (var p in t.Path) if (p >= 0) hops.Add(Vocabulary.MultiStrike.SlotHop(p + 1));
                    string head = hops.Count >= 2
                        ? Vocabulary.MultiStrike.BurrowsFromTo(name, hops.ToArray())
                        : Vocabulary.MultiStrike.Burrows(name, inSlot);

                    if (t.Damage <= 0 && !t.Died) clause = $"{head}.";
                    else if (t.Died)              clause = Vocabulary.MultiStrike.HopsTakingDies(head, dmg, outcome);
                    else if (outcome == null)     clause = Vocabulary.MultiStrike.HopsTaking(head, dmg);
                    else                          clause = Vocabulary.MultiStrike.HopsTakingDies(head, dmg, outcome);
                }
                else
                {
                    if (t.Died)               clause = Vocabulary.MultiStrike.TakesAndDies(name, inSlot, dmg, outcome);
                    else if (outcome == null) clause = Vocabulary.MultiStrike.TakesStrikes(name, inSlot, dmg);
                    else                      clause = Vocabulary.MultiStrike.TakesWithOutcome(name, inSlot, dmg, outcome);
                }

                sb.Append(' ').Append(clause);
            }

            if (a.Bones > 0)
                sb.Append(Vocabulary.ReceivedBones(a.Bones));

            return sb.ToString();
        }

        /// <summary>A battle ended or the scene changed.</summary>
        internal static void Reset()
        {
            if (_current != null) _current.Finished = true;
            _current = null;
        }
    }
    /// <summary>
    /// Every strike the player's side lands on a giant card in one combat
    /// phase, as one line. (0.7.348.)
    /// </summary>
    /// <remarks>
    /// Zamar, the Limoncello: four Worker Ants gave eight lines — four
    /// "Worker Ant attacks The Limoncello." and four damage lines, the numbers
    /// wrong because each read live health at speak time. "Condense all of
    /// these attacks into one line. Same for Moon fight."
    ///
    /// A giant fills every slot on its side, so every attacker on the other
    /// side is hitting the same card; the per-attacker lines carry nothing but
    /// the attacker's name. The line is reserved at the first strike (held at
    /// the head of the queue, like the multi-strike summary) and composed when
    /// the combat phase ends — or at once if the giant dies mid-phase, so its
    /// death line follows the volley and not the other way round.
    ///
    /// Only strikes the game actually aims at the giant. A flyer that goes
    /// over it attacks directly and keeps its own line; so does anything the
    /// giant does back.
    /// </remarks>
    // WORDING: "Your cards attack The Limoncello 4 times for 16 damage, 64
    // health remaining." — Claude's proposal, chosen by Zamar, 0.7.348. The
    // one-attacker form ("Worker Ant attacks The Limoncello for 4 damage, ...")
    // was proposed alongside it and not objected to; unheard.
    internal static class GiantVolley
    {
        private class Volley
        {
            internal PlayableCard Giant;
            internal string GiantName;
            internal readonly List<PlayableCard> Attackers = new List<PlayableCard>();
            internal int Strikes;
            internal int Damage;
            internal bool GiantDied;
            internal bool Finished;
        }

        private static Volley _current;

        internal static bool IsGiant(PlayableCard card)
        {
            try { return card?.Info != null && card.Info.HasTrait(Trait.Giant); }
            catch { return false; }
        }

        /// <summary>True if any live card on this side is a giant.</summary>
        internal static bool GiantOn(bool opponentSide)
        {
            try
            {
                var bm = Singleton<BoardManager>.Instance;
                var slots = opponentSide ? bm?.OpponentSlotsCopy : bm?.PlayerSlotsCopy;
                if (slots == null) return false;
                foreach (var s in slots)
                    if (IsGiant(BoardReader.LiveCard(s))) return true;
            }
            catch { }
            return false;
        }

        /// <summary>SlotAttackSlot prefix. True = this strike is the volley's.</summary>
        internal static bool TryNoteStrike(PlayableCard attacker, PlayableCard defender, CardSlot opposingSlot)
        {
            if (attacker == null || !IsGiant(defender)) return false;
            try
            {
                if (attacker.OpponentCard) return false;              // the giant's own side
                if (attacker.CanAttackDirectly(opposingSlot)) return false;
            }
            catch { return false; }

            var v = _current;
            if (v == null || v.Finished || !ReferenceEquals(v.Giant, defender))
            {
                if (v != null) v.Finished = true;
                v = new Volley { Giant = defender };
                try { v.GiantName = CardReader.CardName(defender.Info); } catch { }
                _current = v;

                var captured = v;
                using (Speech.Event(EventKind.Attacks)) Speech.ResultWhenReady(
                    () => captured.Finished,
                    () => Compose(captured),
                    30f,
                    $"[giant volley: {v.GiantName}]");
            }

            v.Strikes++;
            if (!v.Attackers.Contains(attacker)) v.Attackers.Add(attacker);
            Plugin.Log?.LogInfo(
                $"IKMA VOLLEY: {CardReader.CardName(attacker.Info)} strikes {v.GiantName} " +
                $"(strike {v.Strikes}).");
            return true;
        }

        /// <summary>TakeDamage postfix. True = summed here.</summary>
        internal static bool TryRecordHit(PlayableCard card, int damage)
        {
            var v = _current;
            if (v == null || v.Finished || !ReferenceEquals(v.Giant, card)) return false;
            v.Damage += damage;
            return true;
        }

        /// <summary>The giant is dying: close the volley so it speaks first.</summary>
        internal static void NoteDeath(PlayableCard card)
        {
            var v = _current;
            if (v == null || v.Finished || !ReferenceEquals(v.Giant, card)) return;
            v.GiantDied = true;
            v.Finished = true;
            _current = null;
        }

        /// <summary>DoCombatPhase has run its last instruction.</summary>
        internal static void EndPhase()
        {
            if (_current != null) _current.Finished = true;
            _current = null;
            BrittleGroup.End();
        }

        internal static System.Collections.IEnumerator WrapPhase(System.Collections.IEnumerator inner)
        {
            if (inner == null) return inner;
            return RunPhase(inner);
        }

        private static System.Collections.IEnumerator RunPhase(System.Collections.IEnumerator inner)
        {
            try
            {
                while (inner.MoveNext()) yield return inner.Current;
            }
            finally
            {
                EndPhase();
            }
        }

        internal static void Reset() => EndPhase();

        private static string Compose(Volley v)
        {
            if (v == null || string.IsNullOrEmpty(v.GiantName) || v.Strikes == 0) return null;

            string subject;
            if (v.Attackers.Count == 1)
            {
                string n = null;
                try { n = CardReader.CardName(v.Attackers[0].Info); } catch { }
                subject = v.Strikes == 1
                    ? Vocabulary.MultiStrike.AttacksGiant(n, v.GiantName)
                    : Vocabulary.MultiStrike.AttacksGiantTimes(n, v.GiantName, v.Strikes);
            }
            else
            {
                subject = Vocabulary.MultiStrike.YourCardsAttackTimes(v.GiantName, v.Strikes);
            }

            string dmg = Vocabulary.DamageCount(v.Damage);
            if (v.GiantDied) return Vocabulary.MultiStrike.GiantDiesFor(subject, dmg);

            int h = -1;
            try { h = v.Giant != null ? v.Giant.Health : -1; } catch { }
            if (h < 0) return Vocabulary.MultiStrike.GiantDiesFor(subject, dmg);
            return Vocabulary.MultiStrike.ForHealthRemaining(subject, dmg, h);
        }
    }

    /// <summary>
    /// Every Brittle card that dies after attacking in one combat phase, as
    /// one line. (0.7.349.)
    /// </summary>
    /// <remarks>
    /// Zamar: "all brittle abilities need to trigger and resolve on one line,
    /// not individual." Four Skeleton Crew gave twelve lines — "X's Brittle
    /// ability triggers.", "X dies.", "1 bone received." each.
    ///
    /// Brittle.OnAttackEnded runs from the AttackEnded loop at the END of
    /// CombatPhaseManager.DoCombatPhase, one card after another, so the whole
    /// group lives inside one phase: reserved at the first trigger, composed
    /// when the phase ends (the same wrapper that closes GiantVolley).
    ///
    /// WORDING: Claude's proposal, 0.7.349, modelled on his Morsel line —
    ///   one:   "Skeleton Crew's Brittle ability triggers, and it dies."
    ///   two:   "Both Brittle abilities trigger, Skeleton Crew in slots 2 and
    ///           4 die."
    ///   more:  "All four Brittle abilities trigger, ..."
    /// plus " Received N bones." when its deaths paid any.
    /// </remarks>
    internal static class BrittleGroup
    {
        private class Group
        {
            internal readonly List<PlayableCard> Cards = new List<PlayableCard>();
            internal readonly List<string> Names = new List<string>();
            internal readonly List<int> Slots = new List<int>();   // 1-based
            internal readonly List<bool> Died = new List<bool>();
            internal int Bones;
            internal bool Finished;
        }

        private static Group _current;

        /// <summary>Brittle.OnAttackEnded prefix.</summary>
        internal static void Note(PlayableCard card)
        {
            if (card?.Info == null) return;
            bool dead = false;
            try { dead = card.Dead; } catch { }
            if (dead) return;   // the game skips it too

            var g = _current;
            if (g == null || g.Finished)
            {
                g = new Group();
                _current = g;
                var captured = g;
                using (Speech.Event(EventKind.Attacks)) Speech.ResultWhenReady(
                    () => captured.Finished,
                    () => Compose(captured),
                    30f,
                    "[brittle group]");
            }

            int slot = -1;
            try { slot = card.Slot != null ? card.Slot.Index + 1 : -1; } catch { }
            g.Cards.Add(card);
            g.Names.Add(CardReader.CardName(card.Info));
            g.Slots.Add(slot);
            g.Died.Add(false);
            Plugin.Log?.LogInfo($"IKMA BRITTLE: {CardReader.CardName(card.Info)} in slot {slot}.");
        }

        /// <summary>Die prefix. True = this death is the group's to say.</summary>
        internal static bool TryRecordDeath(PlayableCard card)
        {
            var g = _current;
            if (g == null || g.Finished) return false;
            for (int i = 0; i < g.Cards.Count; i++)
            {
                if (!ReferenceEquals(g.Cards[i], card)) continue;
                g.Died[i] = true;
                return true;
            }
            return false;
        }

        internal static bool TryFoldBones(int amount)
        {
            var g = _current;
            if (g == null || g.Finished) return false;
            g.Bones += amount;
            return true;
        }

        internal static void End()
        {
            if (_current != null) _current.Finished = true;
            _current = null;
        }

        private static string Compose(Group g)
        {
            // Only the cards that actually died.
            var names = new List<string>();
            var slots = new List<int>();
            for (int i = 0; i < g.Cards.Count; i++)
                if (g.Died[i]) { names.Add(g.Names[i]); slots.Add(g.Slots[i]); }
            if (names.Count == 0) return null;

            string sigil = null;
            try { sigil = CardReader.GetAbilityName(Ability.Brittle); } catch { }
            if (string.IsNullOrEmpty(sigil)) sigil = Vocabulary.MultiStrike.BrittleFallback;

            string bones = g.Bones > 0 ? Vocabulary.ReceivedBones(g.Bones) : "";

            if (names.Count == 1)
                return Vocabulary.MultiStrike.SAbilityTriggersAnd(names[0], sigil, bones);

            // Group by name, in the order first met: "Skeleton Crew in slots
            // 1, 2 and 4 and Worker Ant in slot 3".
            var order = new List<string>();
            foreach (var n in names) if (!order.Contains(n)) order.Add(n);
            var groups = new List<string>();
            foreach (var n in order)
            {
                var nums = new List<string>();
                for (int i = 0; i < names.Count; i++)
                    if (names[i] == n && slots[i] > 0) nums.Add(slots[i].ToString());
                if (nums.Count == 0) { groups.Add(n); continue; }
                string list = nums.Count == 1 ? nums[0]
                    : Vocabulary.MultiStrike.NumberList(nums.GetRange(0, nums.Count - 1).ToArray(), nums[nums.Count - 1]);
                groups.Add(Vocabulary.MultiStrike.InSlots(n, nums.Count, list));
            }
            string who = groups.Count == 1 ? groups[0]
                : Vocabulary.MultiStrike.NumberList(groups.GetRange(0, groups.Count - 1).ToArray(), groups[groups.Count - 1]);

            string lead = Vocabulary.BothOrAll(names.Count);
            return Vocabulary.MultiStrike.AbilitiesTriggerDie(lead, sigil, who, bones);
        }
    }

    // Registered by Plugin.TryPatch. No [HarmonyPatch] attribute.
    public static class Brittle_OnAttackEnded_Patch
    {
        public static void Prefix(Brittle __instance)
        {
            try { BrittleGroup.Note(__instance.GetComponent<PlayableCard>()); } catch { }
        }
    }

    // Registered by Plugin.TryPatch. No [HarmonyPatch] attribute.
    public static class CombatPhaseManager_DoCombatPhase_Patch
    {
        public static void Postfix(ref System.Collections.IEnumerator __result)
        {
            __result = GiantVolley.WrapPhase(__result);
        }
    }
}

// MultiStrikeNarrator.cs
