// BurrowBlock.cs
//
// ONE SENTENCE FOR AN ATTACK THAT A BURROWER MOVES IN TO BLOCK.
// (0.7.438, Session 43.)
//
// Zamar's log:
//   Bullfrog attacks empty Slot 4.
//   Mole's Burrower ability triggers: it moves right to slot 4.
//   Mole takes 1 damage, 3 health remaining.
// "It read that Bullfrog attacked empty slot 4, but that's misleading since
// the burrower triggers to block it right after. Can we collapse this
// interaction somehow?" His sentence:
//   "Bullfrog attacks empty Slot 4, triggering Mole's Burrower ability. Mole
//    moves right to slot 4 and takes 1 damage, 3 health remaining."
//
// HOW IT KNOWS, BEFORE THE MOVE. The attack line is composed when the slot is
// targeted, and the Burrower answers a moment later. So the game is asked its
// own question at that moment: WhackAMole.RespondsToSlotTargetedForAttack
// (PUBLIC) for each card on the defending side. Nothing about Burrower is
// reimplemented here.
//
// THREE THINGS BECOME ONE. If a Burrower will answer, the attack line is not
// queued. A single held line takes its place in the queue, and:
//   - SigilNarrator.NoteBurrow hands the move to it instead of speaking,
//   - the TakeDamage postfix hands the damage record to it instead of queueing,
//   - it speaks once the damage is known.
//
// EVERY WAY OUT STILL SPEAKS. No Burrower moved after all: the plain "attacks
// empty Slot" line. It moved and was not hit: the sentence ends at the move.
// The held line was cut from the queue: the block goes stale and the next
// burrow and hit speak the ordinary way.

using System.Collections.Generic;
using UnityEngine;
using DiskCardGame;

namespace IKMA
{
    internal sealed class BurrowBlock
    {
        private static BurrowBlock _open;

        private string _attackerName;
        private string _swingNote;
        private int _slotIndex;
        private bool _playerDefends;

        private PlayableCard _mover;
        private int _fromSlot = -1;
        private float _burrowedAt;

        private DamageRecord _record;
        private float _recordAt;

        private float _openedAt;

        private const float MaxWait        = 5f;     // the queue's own deadline
        private const float NoBurrowAfter  = 1.5f;   // the Burrower answers at once or not at all
        private const float NoHitAfter     = 3f;     // moved in, and nothing struck it
        private const float DeathSettle    = 0.75f;  // a lethal hit is followed by Die

        /// <summary>
        /// Called where "X attacks empty Slot N." is about to be queued. True
        /// when a Burrower will answer and the line is now held here.
        /// </summary>
        internal static bool TryOpen(CardSlot attackingSlot, CardSlot target,
                                     string attackerName, string swingNote, int slotIndex)
        {
            try
            {
                if (attackingSlot == null || target == null) return false;
                var attacker = attackingSlot.Card;
                if (attacker == null) return false;

                // Asked on an attack, so the board is known to be there.
                var board = Singleton<BoardManager>.Instance;
                if (board == null) return false;

                bool willAnswer = false;
                foreach (var slot in board.GetSlots(target.IsPlayerSlot))
                {
                    var card = slot != null ? slot.Card : null;
                    if (card == null || card.Dead) continue;

                    var burrower = card.GetComponent<WhackAMole>();
                    if (burrower != null && burrower.RespondsToSlotTargetedForAttack(target, attacker))
                    {
                        willAnswer = true;
                        break;
                    }
                }
                if (!willAnswer) return false;

                var block = new BurrowBlock
                {
                    _attackerName  = attackerName,
                    _swingNote     = swingNote,
                    _slotIndex     = slotIndex,
                    _playerDefends = target.IsPlayerSlot,
                    _openedAt      = Time.unscaledTime,
                };
                _open = block;

                using (Speech.Event(EventKind.Attacks, EventTag.Side(attackingSlot.IsPlayerSlot)))
                    Speech.ResultWhenReady(block.Ready, block.Compose, MaxWait,
                        $"[{attackerName} attacks empty Slot {slotIndex + 1} - held, a Burrower will answer]");
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA BURROW: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        private static BurrowBlock Live()
        {
            var b = _open;
            if (b == null) return null;
            if (Time.unscaledTime - b._openedAt > MaxWait + 1f) { _open = null; return null; }
            return b;
        }

        /// <summary>The Burrower's move. True when the held line will say it.</summary>
        internal static bool ClaimBurrow(PlayableCard mover, int fromSlot, int toSlot)
        {
            var b = Live();
            if (b == null || mover == null || b._mover != null) return false;
            if (toSlot != b._slotIndex) return false;

            bool moverIsPlayers;
            try { moverIsPlayers = !mover.OpponentCard; } catch { return false; }
            if (moverIsPlayers != b._playerDefends) return false;

            b._mover      = mover;
            b._fromSlot   = fromSlot;
            b._burrowedAt = Time.unscaledTime;
            Plugin.Log?.LogInfo(
                $"IKMA BURROW: '{CardReader.CardName(mover)}' moves from slot {fromSlot + 1} to {toSlot + 1} " +
                "to block - held for the one attack line.");
            return true;
        }

        /// <summary>The hit on the Burrower. True when the held line will say it.</summary>
        internal static bool TryAttach(PlayableCard hit, DamageRecord record)
        {
            var b = Live();
            if (b == null || record == null || b._record != null) return false;
            if (b._mover == null || !ReferenceEquals(b._mover, hit)) return false;

            b._record   = record;
            b._recordAt = Time.unscaledTime;
            return true;
        }

        private bool Ready()
        {
            float now = Time.unscaledTime;
            if (_mover == null) return now - _openedAt > NoBurrowAfter;
            if (_record == null) return now - _burrowedAt > NoHitAfter;

            if (_record.Died) return true;
            if (_record.HealthAfter != int.MinValue && _record.HealthAfter > 0) return true;
            return now - _recordAt > DeathSettle;
        }

        private string Compose()
        {
            if (ReferenceEquals(_open, this)) _open = null;

            string plain = Vocabulary.Combat.AttacksEmptySlot(_attackerName, _swingNote, _slotIndex + 1);
            if (_mover == null)
            {
                Plugin.Log?.LogInfo("IKMA BURROW: no Burrower moved - the plain attack line is spoken.");
                return plain;
            }

            string name = null;
            try { name = CardReader.CardName(_mover); } catch { }

            // Where it stands now; the slot it was claimed for if it can no
            // longer be read (a Burrower that died of the hit has no slot).
            int nowSlot = _slotIndex;
            try { if (_mover.Slot != null) nowSlot = _mover.Slot.Index; } catch { }

            // The differ must not report the same move again.
            try { BoardWatcher.NoteAnnounced(_mover); } catch { }

            if (string.IsNullOrEmpty(name) || nowSlot == _fromSlot)
            {
                Plugin.Log?.LogInfo("IKMA BURROW: the move could not be read - the lines are spoken apart.");
                return _record != null ? plain + " " + _record.Compose() : plain;
            }

            string sigil = null;
            try { sigil = CardReader.GetAbilityName(Ability.WhackAMole); } catch { }
            if (string.IsNullOrEmpty(sigil)) sigil = Vocabulary.Sigils.Burrower;

            string dir = Vocabulary.Sigils.RightOrLeft(nowSlot, _fromSlot);

            if (_record == null)
                return Vocabulary.Combat.BurrowBlockNoHit(
                    _attackerName, _swingNote, _slotIndex + 1, name, sigil, dir, nowSlot + 1);

            _record.Lead = Vocabulary.Combat.BurrowBlockLead(
                _attackerName, _swingNote, _slotIndex + 1, name, sigil, dir, nowSlot + 1);
            return _record.Compose();
        }
    }
}

// BurrowBlock.cs
