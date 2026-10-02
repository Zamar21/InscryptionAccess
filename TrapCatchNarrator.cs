// TrapCatchNarrator.cs
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// A trap springs: two lines, in Zamar's words. (0.7.424, Session 39.)
    /// </summary>
    /// <remarks>
    /// He heard four lines for one event:
    ///   "Leaping Trap is destroyed."
    ///   "Bat gets caught in the Leaping Trap and dies."
    ///   "Received 1 bone."
    ///   "Leaping Trap's Steel Trap ability triggers: A Wolf Pelt is added to your hand."
    /// and asked for:
    ///   "Leaping Trap is destroyed, its ability Steel Trap triggers."
    ///   "Bat gets caught in the Leaping Trap and dies. A Wolf Pelt is added to your hand."
    ///
    /// WHAT THE GAME DOES (SteelTrap, dumps\dump_steeltrap_from_decompile.txt).
    /// RespondsToDie is true when the death is not a sacrifice and the trap is
    /// on the board. OnDie then does nothing unless a card faces it; if one
    /// does, that card's Die is called with the trap as the killer, and half a
    /// second later CreateDrawnCard puts the pelt in the player's hand.
    ///
    /// LINE ONE is the trap's own death line. "Triggers" is only said when
    /// the game's RespondsToDie answers yes AND a card is facing it - the
    /// same two conditions the game uses. A trap that dies facing nothing
    /// keeps the plain "is destroyed."
    ///
    /// LINE TWO is the victim's death line. It takes its place in the queue
    /// when the victim dies and waits there for the pelt, which lands a
    /// second or two later. The pelt's arrival is recognised by the game's
    /// own objects - the DrawCreatedCard that is about to draw is the Steel
    /// Trap ability on the very card that was the killer - not by matching
    /// names. If no pelt arrives in time the line is spoken without it, and
    /// a pelt that turns up after that gets its usual line.
    /// </remarks>
    internal static class TrapCatchNarrator
    {
        private const float MAX_WAIT_SECONDS = 5f;

        private sealed class Catch
        {
            internal PlayableCard Trap;
            internal System.Func<string> Arrival;
            internal int Bones;
        }

        private static Catch _open;
        private static bool _nextArrivalIsThePelt;
        private static bool _loggedNoBehaviour;

        /// <summary>
        /// Will this dying card's Steel Trap spring? Asked from the Die prefix,
        /// while the card is still in its slot.
        /// </summary>
        internal static bool TrapWillFire(PlayableCard dying, bool wasSacrifice, PlayableCard killer)
        {
            try
            {
                if (dying == null || !dying.HasAbility(Ability.SteelTrap)) return false;

                var behaviour = dying.GetComponent<SteelTrap>();
                if (behaviour == null)
                {
                    if (!_loggedNoBehaviour)
                    {
                        _loggedNoBehaviour = true;
                        Plugin.Log?.LogWarning(
                            "IKMA TRAP: the card has Steel Trap but no SteelTrap behaviour was found on it - " +
                            "its death line stays plain.");
                    }
                    return false;
                }

                if (!behaviour.RespondsToDie(wasSacrifice, killer)) return false;

                // SteelTrap.OnDie's own condition.
                var slot = dying.Slot;
                return slot != null && slot.opposingSlot != null && slot.opposingSlot.Card != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// The victim's line. Reserves its place now, speaks when the pelt has
        /// landed. Call inside the caller's Speech.Event scope.
        /// </summary>
        internal static void SpeakCatch(PlayableCard trap, System.Func<string> caught, string victimName)
        {
            var c = new Catch { Trap = trap };
            _open = c;
            _nextArrivalIsThePelt = false;

            Speech.ResultWhenReady(
                () => c.Arrival != null,
                () =>
                {
                    if (ReferenceEquals(_open, c)) _open = null;

                    string sentence = caught();
                    string pelt = null;
                    try { pelt = c.Arrival?.Invoke(); } catch { }
                    if (c.Arrival == null)
                        Plugin.Log?.LogInfo("IKMA TRAP: no pelt arrived in time - the caught line is spoken alone.");
                    string line = string.IsNullOrEmpty(pelt) ? sentence : $"{sentence} {pelt}";

                    // 0.7.425 — "fold it in to the pelt line": the bones paid
                    // during the catch, last. ReceivedBones brings its own
                    // leading space.
                    return c.Bones > 0 ? line + Vocabulary.ReceivedBones(c.Bones) : line;
                },
                MAX_WAIT_SECONDS,
                $"[caught in a trap: {victimName}]");
        }

        /// <summary>
        /// From ResourcesManager.AddBones: true when a catch is still waiting
        /// to be spoken, so the bone is said on its line and not on its own.
        /// (0.7.425.) Whichever card paid it - the trap, when it is the
        /// player's, or the player's card that was caught.
        /// </summary>
        internal static bool TryFoldBones(int amount)
        {
            if (_open == null || amount <= 0) return false;
            _open.Bones += amount;
            Plugin.Log?.LogInfo($"IKMA TRAP: {amount} bone(s) folded into the caught line.");
            return true;
        }

        /// <summary>
        /// From the CreateDrawnCard prefix: a sigil is about to put a card in
        /// the hand. True only when it is Steel Trap on the trap whose catch
        /// is waiting.
        /// </summary>
        internal static void NoteDraw(PlayableCard owner, Ability ability)
        {
            _nextArrivalIsThePelt =
                _open != null && ability == Ability.SteelTrap && ReferenceEquals(owner, _open.Trap);
        }

        /// <summary>
        /// From EventNarrator, when a sigil-made card reaches the hand. True
        /// when the waiting caught line has taken it.
        /// </summary>
        internal static bool TryFold(System.Func<string> arrivalSentence)
        {
            bool mine = _nextArrivalIsThePelt && _open != null;
            _nextArrivalIsThePelt = false;
            if (!mine) return false;

            _open.Arrival = arrivalSentence;
            return true;
        }
    }
}
// TrapCatchNarrator.cs
