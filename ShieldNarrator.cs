// ShieldNarrator.cs
//
// ARMORED. (0.7.448, Session 45.)
//
// Zamar: "My skunk's ability that blocks one instance of damage triggered but
// wasnt called out, armor." His log had worse than silence in it:
//   "Amalgam attacks Skunk, it takes 2 damage, 3 health remaining."
// The Skunk took nothing. PlayableCard.TakeDamage (PUBLIC IEnumerator) opens:
//   if (HasShield()) { Status.lostShield = true; Anim.StrongNegationEffect();
//                      ... UpdateFaceUpOnBoardEffects(); yield break; }
// and IKMA's TakeDamage postfix never asked. HasShield() is PUBLIC and is the
// game's own question; the postfix runs when the enumerator is created, before
// that first line has run, so the answer is still "yes" for the hit that is
// about to be absorbed.
//
// His sentence, picked Session 45:
//   "Amalgam attacks Skunk. Skunk's Armored ability triggers, preventing the
//    damage."
// "Armored" is the game's rulebook name for Ability.DeathShield, read from the
// game, never typed here.
//
// Where the second sentence goes:
//   - inside a multi-strike summary, in the target's place (MultiStrikeNarrator)
//   - on the end of the held vanilla attack line (PlainAttack) - his sentence
//   - on its own for anything else (Sharp Quills coming back at an armored
//     attacker, a Burrower block, a giant's volley)

using DiskCardGame;

namespace IKMA
{
    internal static class ShieldNarrator
    {
        /// <summary>"Skunk's Armored ability triggers, preventing the damage."</summary>
        internal static string Sentence(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string sigil = null;
            try { sigil = CardReader.GetAbilityName(Ability.DeathShield); } catch { }
            if (string.IsNullOrEmpty(sigil)) return null;
            return Vocabulary.Combat.ShieldPrevents(name, sigil);
        }

        /// <summary>
        /// TakeDamage postfix, ahead of every damage path. True = the shield is
        /// about to take this hit, so no damage line of any kind is built.
        /// </summary>
        internal static bool TryAbsorb(PlayableCard card, PlayableCard attacker)
        {
            bool shielded = false;
            try { shielded = card != null && card.HasShield(); } catch { }
            if (!shielded) return false;

            string bare = null;
            try { bare = CardReader.CardName(card); } catch { }
            Plugin.Log?.LogInfo($"IKMA SHIELD: '{bare}' has its shield up - this hit does no damage.");

            if (MultiStrikeNarrator.TryRecordShield(card)) return true;
            if (PlainAttack.TryAttachShield(card, attacker)) return true;

            string name = bare;
            try { name = DamageDeathMerger.CombatLineName(card, bare); } catch { }
            string line = Sentence(name);
            if (line != null)
                using (Speech.Event(EventKind.Powers)) Speech.Result(line);
            return true;
        }
    }
}

// ShieldNarrator.cs
