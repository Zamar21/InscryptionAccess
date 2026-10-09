// PlainAttack.cs
//
// VANILLA COMBAT AS ONE LINE. (0.7.439, Session 43.)
//
// Zamar's log:
//   Great White attacks Raven.
//   Raven takes 4 damage and dies.
// "can these lines also be collapsed into one? Attack declare and damage
// calculation when no relevant sigils are involved." His sentences:
//   "Great White attacks Raven, it takes 4 damage and dies."
//   "X attacks Y, it takes 2 damage, 3 health remaining."
// "just do that for vanilla combat. Leave the complicated sigil ones as they
// are."
//
// VANILLA is decided by the caller, from the branches it already had: the
// plain "X attacks Y." line with no attack sigil named on it. Flying over,
// passing by, a block in the air, Touch of Death, multi-strike and the
// Burrower block never come here.
//
// Same shape as BurrowBlock: the attack line is held in the queue, the
// TakeDamage postfix hands the damage record over instead of queueing it, and
// the record finishes the sentence. If no damage arrives the plain attack
// line is spoken, and a late damage line speaks on its own as before.

using UnityEngine;
using DiskCardGame;

namespace IKMA
{
    internal sealed class PlainAttack
    {
        private static PlainAttack _open;

        private string _attackerName;
        private string _targetName;
        private PlayableCard _attacker;
        private PlayableCard _target;

        private DamageRecord _record;
        private bool _shielded;   // 0.7.448 - the target's Armored took the hit
        private float _recordAt;
        private float _openedAt;

        private const float MaxWait     = 4f;     // the queue's own deadline
        private const float NoHitAfter  = 1.5f;   // the hit follows the swing at once or not at all
        private const float DeathSettle = 0.75f;  // a lethal hit is followed by Die

        internal static bool TryOpen(CardSlot attackingSlot, PlayableCard target,
                                     string attackerName, string targetName)
        {
            try
            {
                if (attackingSlot == null || attackingSlot.Card == null || target == null) return false;
                if (string.IsNullOrEmpty(attackerName) || string.IsNullOrEmpty(targetName)) return false;

                var a = new PlainAttack
                {
                    _attackerName = attackerName,
                    _targetName   = targetName,
                    _attacker     = attackingSlot.Card,
                    _target       = target,
                    _openedAt     = Time.unscaledTime,
                };
                _open = a;

                using (Speech.Event(EventKind.Attacks, EventTag.Side(attackingSlot.IsPlayerSlot)))
                    Speech.ResultWhenReady(a.Ready, a.Compose, MaxWait,
                        $"[{attackerName} attacks {targetName} - held for the damage]");
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA ATTACK: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>The hit. True when the held attack line will say it.</summary>
        internal static bool TryAttach(PlayableCard hit, PlayableCard attacker, DamageRecord record)
        {
            var a = _open;
            if (a == null || record == null || a._record != null || a._shielded) return false;
            if (Time.unscaledTime - a._openedAt > MaxWait + 1f) { _open = null; return false; }
            if (!ReferenceEquals(a._target, hit)) return false;
            if (attacker != null && !ReferenceEquals(a._attacker, attacker)) return false;

            a._record   = record;
            a._recordAt = Time.unscaledTime;
            return true;
        }

        /// <summary>
        /// 0.7.448 - the hit was taken by the target's Armored sigil. True when
        /// the held attack line will say so.
        /// </summary>
        internal static bool TryAttachShield(PlayableCard hit, PlayableCard attacker)
        {
            var a = _open;
            if (a == null || a._record != null || a._shielded) return false;
            if (Time.unscaledTime - a._openedAt > MaxWait + 1f) { _open = null; return false; }
            if (!ReferenceEquals(a._target, hit)) return false;
            if (attacker != null && !ReferenceEquals(a._attacker, attacker)) return false;

            a._shielded = true;
            return true;
        }

        private bool Ready()
        {
            if (_shielded) return true;
            float now = Time.unscaledTime;
            if (_record == null) return now - _openedAt > NoHitAfter;

            if (_record.Died) return true;
            if (_record.HealthAfter != int.MinValue && _record.HealthAfter > 0) return true;
            return now - _recordAt > DeathSettle;
        }

        private string Compose()
        {
            if (ReferenceEquals(_open, this)) _open = null;

            // 0.7.448 - Zamar's sentence: "Amalgam attacks Skunk. Skunk's
            // Armored ability triggers, preventing the damage."
            if (_shielded)
            {
                string attack = Vocabulary.Combat.Attacks(_attackerName, "", _targetName);
                string shield = ShieldNarrator.Sentence(_targetName);
                return shield == null ? attack : attack + " " + shield;
            }

            if (_record == null)
            {
                Plugin.Log?.LogInfo("IKMA ATTACK: no damage followed - the plain attack line is spoken.");
                return Vocabulary.Combat.Attacks(_attackerName, "", _targetName);
            }

            _record.Lead = Vocabulary.Combat.AttacksLead(_attackerName, _targetName);
            return _record.Compose();
        }
    }
}

// PlainAttack.cs
