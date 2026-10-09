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
    /// WHAT IT DOES NOT TAKE OVER: damage to any other card on the attacker's
    /// own side, and damage to a card still in the queue. Those keep their own
    /// lines, after this one.
    ///
    /// SHARP QUILLS IS IN THE LINE. (0.7.441.) Until then the attacker's own
    /// damage was left out too, and Zamar's 0.7.439 log read:
    ///
    ///   Mantis attacks 2 times. Raven Egg takes 1 damage, 1 health remaining.
    ///     Porcupine takes 1 damage, 1 health remaining. Received 1 bone.
    ///   Porcupine's Sharp Quills ability triggers, sending one damage back to
    ///     its attacker.
    ///   Mantis takes 1 damage and dies.
    ///
    /// The bone was the Mantis's own, paid when the quills killed it, and it
    /// was spoken two lines before the death that paid it. "collapse this
    /// sequence", his sentence:
    ///
    ///   "Mantis attacks 2 times. Raven Egg takes 1 damage, 1 health remaining.
    ///    Porcupine takes 1 damage, 1 health remaining. Porcupine's Sharp
    ///    Quills ability triggers, sending one damage back to its attacker.
    ///    Mantis takes 1 damage and dies. Received 1 bone."
    ///
    /// No new words: the Sharp Quills sentence and the attacker's damage
    /// sentence are the same two lines as before, composed by the same code
    /// (SigilTriggers' closure, DamageRecord.Compose), and placed after the
    /// target clauses and before the bones. With more than one answer from
    /// quills they follow each other in the order they happened.
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

            // 0.7.447 - this card's clause has been spoken already, in a line
            // said early because a character started talking mid-attack. It
            // is left out of what follows unless it is hit again.
            internal bool Said;

            // 0.7.448 - its Armored sigil took one of the strikes.
            internal bool Shielded;
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
            internal bool OpeningSaid;   // 0.7.447 - "X attacks N times." already spoken
            internal readonly List<Target> Targets = new List<Target>();
            internal readonly List<string> DefenderNamesAtStart = new List<string>();

            // 0.7.441 - sentences said after the target clauses, in the order
            // they happened: a defender's Sharp Quills line, then the
            // attacker's damage from it. Each is composed when the summary
            // is, so health and a death are final by then.
            internal readonly List<System.Func<string>> Tail = new List<System.Func<string>>();
            internal readonly List<DamageRecord> AttackerRecords = new List<DamageRecord>();
            internal int Quills;
            internal float FinishedAt = -1f;
        }

        // A lethal quill hit is followed by Die inside the same attack
        // sequence, so this is a safety net, not the normal path.
        private const float AttackerDeathSettle = 0.75f;

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
                AttackerName = Vocabulary.Submerged(attacker, CardReader.CardName(attacker))
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
                        if (c?.Info != null) a.DefenderNamesAtStart.Add(CardReader.CardName(c));
                    }
            }
            catch { }

            _current = a;

            using (Speech.Event(EventKind.Attacks)) Speech.ResultWhenReady(
                () => a.Finished && AttackerSettled(a),
                () => Compose(a, false),
                15f,
                $"[multi-strike summary: {CardReader.CardName(attacker)}, {strikes} strikes]",
                () => Compose(a, true));   // 0.7.447 - what has landed so far, if a character cuts in
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
            try { a.FinishedAt = UnityEngine.Time.unscaledTime; } catch { }
            if (ReferenceEquals(_current, a)) _current = null;
        }

        /// <summary>
        /// True once every quill hit on the attacker has its outcome: it
        /// died, or it has health left, or long enough has passed since the
        /// attack ended that no death is coming. (0.7.441.)
        /// </summary>
        private static bool AttackerSettled(Attack a)
        {
            if (a.AttackerRecords.Count == 0) return true;
            float now = 0f;
            try { now = UnityEngine.Time.unscaledTime; } catch { return true; }
            bool waitedOut = a.FinishedAt >= 0f && now - a.FinishedAt > AttackerDeathSettle;
            foreach (var r in a.AttackerRecords)
            {
                if (r.Died) continue;
                if (r.HealthAfter != int.MinValue && r.HealthAfter > 0) continue;
                if (!waitedOut) return false;
            }
            return true;
        }

        /// <summary>
        /// SigilTriggers, for Sharp Quills only. True = the summary says this
        /// trigger line, no line of its own. (0.7.441.) The card must be one
        /// this attack is striking: the other side, on the board.
        /// </summary>
        internal static bool TryFoldQuills(PlayableCard card, System.Func<string> line)
        {
            var a = _current;
            if (a == null || a.Finished || line == null || !IsDefender(a, card)) return false;

            a.Quills++;
            a.Tail.Add(line);
            Plugin.Log?.LogInfo("IKMA MULTI: Sharp Quills answers - said in the attack's line.");
            return true;
        }

        /// <summary>
        /// TakeDamage postfix, for the ATTACKER. True = the summary says this
        /// damage, no line of its own. (0.7.441.) Only after a Sharp Quills
        /// line has been folded: damage to the attacker from anything else
        /// keeps its own line, as before.
        /// </summary>
        internal static bool TryAttachQuillDamage(PlayableCard card, DamageRecord record)
        {
            var a = _current;
            if (a == null || a.Finished || record == null || a.Quills == 0) return false;
            if (!ReferenceEquals(card, a.Attacker)) return false;

            a.AttackerRecords.Add(record);
            a.Tail.Add(record.Compose);
            Plugin.Log?.LogInfo(
                $"IKMA MULTI: '{a.AttackerName}' hit back for {record.Damage} - said in the attack's line.");
            return true;
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
            try { nt.Info = card.Info; nt.Name = CardReader.CardName(card); } catch { }
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
            t.Said = false;   // 0.7.447 - hit again after an early line: it is news again
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
            t.Said = false;   // 0.7.447
            t.Burrowed = true;
            t.EndSlot = toSlot;
            t.Path.Add(toSlot);

            Plugin.Log?.LogInfo(
                $"IKMA MULTI: '{t.Name}' burrows slot {fromSlot + 1} -> {toSlot + 1}.");
            return true;
        }

        /// <summary>
        /// 0.7.448 - TakeDamage postfix, a strike the target's Armored sigil
        /// takes. True = the summary says it, in the target's place in the line.
        /// </summary>
        internal static bool TryRecordShield(PlayableCard card)
        {
            var a = _current;
            if (a == null || a.Finished || !IsDefender(a, card)) return false;

            int slot = -1;
            try { slot = card.Slot.Index; } catch { }
            var t = Touch(a, card, slot);
            t.Said = false;
            t.Shielded = true;

            Plugin.Log?.LogInfo($"IKMA MULTI: '{t.Name}' in slot {slot + 1} - the shield took the strike.");
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
                // 0.7.447 - its clause was already said and it has not been
                // hit since: this death is not the attack's, so it gets its
                // own line by the ordinary path.
                if (t.Said) return false;
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
                        if (CardReader.CardName(c) == t.Name) return true;
                    }
            }
            catch { }
            return false;
        }

        // 0.7.447 - TWO LINES WHEN A CHARACTER CUTS IN. Zamar's 0.7.446 log,
        // the Prospector: Hydra's five strikes, the Pack Mule dead on the
        // second, "DAAAG NAB IT!" holding the attack until Space, the Wolf
        // dead on the third. His ruling: the Mule and the pack before the
        // boss line, then after Space the rest of the attack -
        //   "Hydra attacks 5 times. Pack Mule takes 6 damage and dies."
        //   "Wolf takes 3 damage and dies."
        // No new words: the second line is the same clauses without the
        // opening sentence. early = the line said before the character;
        // it marks what it said so the closing line leaves it out.
        private static string Compose(Attack a, bool early)
        {
            if (a == null) return null;

            if (early)
            {
                bool anything = a.Tail.Count > 0 || a.Bones > 0;
                foreach (var t in a.Targets)
                    if (!t.Said && !string.IsNullOrEmpty(t.Name)) { anything = true; break; }
                if (!anything) return null;
            }

            var sb = new System.Text.StringBuilder();
            if (!a.OpeningSaid)

            // 0.7.349 — A GIANT HITS EVERY CARD YOU HAVE. Zamar's wording:
            // "The Limoncello attacks each of your [one,two,three,four]
            // slot[s]." Omni Strike aims once at each occupied slot, so the
            // strike count IS the number of slots.
            {
                if (a.AttackerIsGiant)
                    sb.Append(Vocabulary.MultiStrike.AttacksEachOfYour(a.AttackerName, a.Strikes));
                else
                    sb.Append(Vocabulary.MultiStrike.AttacksTimes(a.AttackerName, a.Strikes));
            }

            foreach (var t in a.Targets)
            {
                if (string.IsNullOrEmpty(t.Name)) continue;
                if (t.Said) continue;   // 0.7.447 - spoken in the early line

                // 0.7.442 - THE SLOT IS SAID ONCE. Zamar's 0.7.441 log, two
                // Leaping Traps on one side:
                //   "Mantis attacks 2 times. Leaping Trap in Slot 3 in slot 3
                //    takes 1 damage and is destroyed."
                // CombatLineName (Session 32) adds the slot for a same-side
                // twin, and so does inSlot below. When this line is going to
                // say the START slot itself, the name takes the side only.
                bool ambiguous = Ambiguous(a, t) && t.StartSlot >= 0;

                string name = t.Name;
                try
                {
                    name = ambiguous
                        ? DamageDeathMerger.SideQualifiedName(t.Card, t.Name)
                        : DamageDeathMerger.CombatLineName(t.Card, t.Name);
                }
                catch { }

                // 0.7.448 - Armored first, because it happened first: the
                // shield takes the first strike and any later one lands.
                if (t.Shielded)
                {
                    string shieldName = t.Name;
                    try { shieldName = DamageDeathMerger.CombatLineName(t.Card, t.Name); } catch { }
                    string shield = ShieldNarrator.Sentence(shieldName);
                    if (shield != null) sb.Append(' ').Append(shield);
                    if (t.Damage <= 0 && !t.Died && !t.Burrowed) continue;
                }

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

                string inSlot = ambiguous ? Vocabulary.MultiStrike.InStartSlot(t.StartSlot + 1) : "";

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

            // 0.7.441 - Sharp Quills and what it did to the attacker, after the
            // cards the attacker hit and before the bones (a bone here can be
            // the attacker's own, paid by the death this tail reports).
            foreach (var part in a.Tail)
            {
                string said = null;
                try { said = part(); } catch { }
                if (!string.IsNullOrEmpty(said)) sb.Append(' ').Append(said);
            }

            if (a.Bones > 0)
                sb.Append(Vocabulary.ReceivedBones(a.Bones));

            if (early)
            {
                // Everything in this line has now been said once. A card hit
                // again later starts a fresh clause from where it stands.
                a.OpeningSaid = true;
                a.Tail.Clear();
                a.Bones = 0;
                foreach (var t in a.Targets)
                {
                    if (t.Said) continue;
                    t.Said = true;
                    if (t.Burrowed)
                    {
                        // As Finish does: the board diff must not report the move again.
                        try { BoardWatcher.NoteAnnounced(t.Card); } catch { }
                    }
                    int at = t.EndSlot;
                    try { if (!t.Died && t.Card?.Slot != null) at = t.Card.Slot.Index; } catch { }
                    t.Damage = 0;
                    t.Shielded = false;
                    t.Burrowed = false;
                    t.StartSlot = at;
                    t.EndSlot = at;
                    t.Path.Clear();
                    t.Path.Add(at);
                }
                Plugin.Log?.LogInfo("IKMA MULTI: early line composed - a character cut in mid-attack.");
            }

            string line = sb.ToString().Trim();
            return line.Length == 0 ? null : line;
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
                try { v.GiantName = CardReader.CardName(defender); } catch { }
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
                $"IKMA VOLLEY: {CardReader.CardName(attacker)} strikes {v.GiantName} " +
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
            g.Names.Add(CardReader.CardName(card));
            g.Slots.Add(slot);
            g.Died.Add(false);
            Plugin.Log?.LogInfo($"IKMA BRITTLE: {CardReader.CardName(card)} in slot {slot}.");
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
