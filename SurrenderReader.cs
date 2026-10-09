// SurrenderReader.cs
using System;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// Accepting a surrender without a mouse. (Session 15, Zamar's 0.7.42
    /// playtest: "when Leshy has his surrender hand offered we need to left
    /// click it currently to accept. Let's make that the T key, and have that
    /// read first and foremost on every G, Shift+G, B, or A press for the rest
    /// of the combat.")
    ///
    /// WHAT THE DUMP ESTABLISHED:
    ///   Opponent.OfferingSurrender                                  PUBLIC get
    ///   Opponent.Surrendered                                        PUBLIC get
    ///   Opponent.VisualizeOfferSurrender(Action surrenderAcceptedCallback)
    ///                                                               NONPUBLIC
    ///   Part1Opponent.VisualizeOfferSurrender(Action)               NONPUBLIC
    ///   OpponentArmController.handshakeInteractable  GenericMainInputInteractable
    ///
    /// THE ROUTE CHOSEN, AND WHY IT IS NOT THE OBVIOUS ONE. The offered hand is
    /// a GenericMainInputInteractable, so CursorSelectStart/CursorSelectEnd
    /// would drive it — the universal primitive, and the first thing to reach
    /// for. But getting to that instance means going through
    /// OpponentArmController, and nothing in any dump gives a route from the
    /// Opponent to its arm controller. The only way there would be a scene
    /// search, which is banned.
    ///
    /// VisualizeOfferSurrender takes the accept callback as its own parameter.
    /// A Harmony prefix hands it over for free, with no lookup of any kind, and
    /// invoking it is the same thing the click handler does. So T runs the
    /// game's own accept action rather than simulating a click on a thing IKMA
    /// cannot legitimately find.
    ///
    /// TWO PATCHES, NOT ONE. Part1Opponent DECLARES its own
    /// VisualizeOfferSurrender, so it overrides the base — and a Harmony patch
    /// on a base declaration does not fire for a subclass override. This is the
    /// same trap that would have left one of the four bosses silent in Session
    /// 14, and it is the reason both are patched here.
    ///
    /// THE OFFER CAN BE WITHDRAWN. Opponent.TryRevokeSurrender exists, so the
    /// prompt asks OfferingSurrender live every time rather than latching on the
    /// offer — a line telling the player to press T after the offer is gone is
    /// exactly the stale-prompt bug this project has fixed four times.
    /// </summary>
    internal static class SurrenderReader
    {
        private static Action _acceptCallback;

        internal static void NoteOffer(Action surrenderAcceptedCallback)
        {
            _acceptCallback = surrenderAcceptedCallback;
            Plugin.Log?.LogInfo(surrenderAcceptedCallback == null
                ? "IKMA SURRENDER: offer seen but the accept callback was null — Shift+E cannot accept."
                : "IKMA SURRENDER: offer seen, accept callback captured.");
            // 0.7.360 — the next dialogue line (his "I CONCEDE.") names Leshy.
            // The olive branch line itself is spoken when the offer sequence
            // ENDS — see Opponent_OfferSurrenderSequence_Patch.
            _concedeLineNext = true;
        }

        internal static void Reset()
        {
            _acceptCallback = null;
            _linePending = false;
        }

        // ==================================================================
        // THE OLIVE BRANCH IS SAID, NOT ONLY FOUND. (0.7.360.)
        //
        // Zamar: "I dont believe we have a way to accept Leshy's olive branch
        // when he concedes. ... After drawing each turn and right after
        // finishing his conversation saying he concedes, we need a line."
        // The offer was only ever mentioned inside a board read. Now the line
        // is queued once the conversation is over — from the offer itself,
        // and from every draw while the offer stands. One pending line at a
        // time, and never twice within a few seconds, so the first turn's
        // offer and draw do not both say it.
        // ==================================================================
        private static bool _concedeLineNext;

        /// <summary>True once, for the first dialogue line after the offer.</summary>
        internal static bool TakeConcedeLine()
        {
            if (!_concedeLineNext) return false;
            _concedeLineNext = false;
            return true;
        }

        /// <summary>
        /// The offer sequence has finished: the conversation is over, Leshy's
        /// arm is out, and the game has just set OfferingSurrender.
        /// </summary>
        internal static void OnOfferSequenceEnded()
        {
            Plugin.Log?.LogInfo($"IKMA SURRENDER: offer sequence ended, standing={OfferStanding()}.");
            if (!OfferStanding()) return;
            _lineSpokenAt = UnityEngine.Time.unscaledTime;
            using (Speech.Event(EventKind.EnemyMoves, EventSource.Enemies)) Speech.Result(Vocabulary.Surrender.OliveBranch);
        }

        private static bool  _linePending;
        private static float _lineSpokenAt = -999f;

        internal static void SpeakOfferWhenQuiet(string why)
        {
            if (_linePending) return;
            var host = CombatAnnouncer.Instance;
            if (host == null) return;
            _linePending = true;
            host.StartCoroutine(OfferLineRoutine(why));
        }

        private static System.Collections.IEnumerator OfferLineRoutine(string why)
        {
            float waited = 0f, quietFor = 0f;
            while (waited < 30f && quietFor < 0.5f)
            {
                bool talking = false;
                try { talking = DialogueAdvancer.ConversationRunning(); } catch { }
                quietFor = talking ? 0f : quietFor + UnityEngine.Time.unscaledDeltaTime;
                waited += UnityEngine.Time.unscaledDeltaTime;
                yield return null;
            }
            _linePending = false;

            if (!OfferStanding()) yield break;
            if (UnityEngine.Time.unscaledTime - _lineSpokenAt < 5f) yield break;
            _lineSpokenAt = UnityEngine.Time.unscaledTime;
            Plugin.Log?.LogInfo($"IKMA SURRENDER: olive branch line queued ({why}).");
            using (Speech.Event(EventKind.EnemyMoves, EventSource.Enemies)) Speech.Result(Vocabulary.Surrender.OliveBranch);
        }

        /// <summary>Is the opponent holding its hand out right now?</summary>
        internal static bool OfferStanding()
        {
            try
            {
                var opponent = TurnManager.Instance?.Opponent;
                return opponent != null && opponent.OfferingSurrender;
            }
            catch { return false; }
        }

        /// <summary>
        /// The line that leads every board read while an offer is standing.
        /// Empty when there is nothing to say, so callers can concatenate it
        /// unconditionally.
        ///
        /// NAMED ONLY WHERE THE NAME IS TRUE. Zamar's wording says "Leshy", and
        /// in an ordinary Act 1 encounter that is who it is. A boss is a
        /// different opponent, and Part1BossOpponent has its own
        /// CanOfferSurrender, so a boss offer is at least possible — naming
        /// Leshy there would be confidently wrong about who is in front of the
        /// player. Bosses get the neutral word instead.
        /// </summary>
        internal static string Prefix()
        {
            if (!OfferStanding()) return "";

            string who = Vocabulary.Surrender.YourOpponent;
            try
            {
                if (!(TurnManager.Instance?.Opponent is Part1BossOpponent)) who = Vocabulary.Leshy;
            }
            catch { }

            return Vocabulary.Surrender.IsOfferingToSurrender(who);
        }

        /// <summary>
        /// T. Returns true if the key was consumed.
        /// </summary>
        internal static bool TryAccept()
        {
            if (!OfferStanding())
            {
                Speech.Browse(Vocabulary.Surrender.NoSurrenderIsBeing);
                return true;
            }

            if (_acceptCallback == null)
            {
                // Honest rather than silent: the player pressed a key IKMA
                // advertised and it cannot do the thing.
                Speech.Browse(Vocabulary.SurrenderNotAccepted);
                Plugin.Log?.LogWarning("IKMA SURRENDER: T pressed with no captured callback.");
                return true;
            }

            try
            {
                Plugin.Log?.LogInfo("IKMA SURRENDER: accepting via the game's own callback.");
                var callback = _acceptCallback;
                _acceptCallback = null;

                // Nothing spoken. The game runs AcceptSurrenderSequence and
                // Leshy has a voice for this; talking over it would bury the
                // one moment the player has been working toward.
                callback();
                // 0.7.360 — his line on accepting.
                Speech.Confirm(Vocabulary.Surrender.ConcedeAccepted);
                EventNarrator.NoteVictoryAlreadySaid();   // 0.7.451 - no second "Victory."
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError($"IKMA SURRENDER: accept threw: {e}");
                Speech.Browse(Vocabulary.SurrenderNotAccepted);
            }
            return true;
        }
    }

    public class Opponent_VisualizeOfferSurrender_Patch
    {
        public static void Prefix(Action surrenderAcceptedCallback)
            => SurrenderReader.NoteOffer(surrenderAcceptedCallback);
    }

    public class Part1Opponent_VisualizeOfferSurrender_Patch
    {
        public static void Prefix(Action surrenderAcceptedCallback)
            => SurrenderReader.NoteOffer(surrenderAcceptedCallback);
    }

    // 0.7.364 — Tutorial4BattleSequencer.BearGlitchSequence (PUBLIC static
    // IEnumerator) opens with PlaySound2D("broken_hum"): the loud distorted
    // sound. GrizzlyGlitchSequence creates it at its yield, so a prefix fires
    // as the sound starts. The queue is held 3 seconds from there.
    public class BearGlitchSequence_Patch
    {
        public static void Prefix()
        {
            try { CombatAnnouncer.HoldQueueFor(3f, "the Grizzly sound is playing"); } catch { }
        }
    }

    public class TurnManager_SetupPhase_Patch
    {
        public static void Prefix()
        {
            try { BoardWatcher.OnBattleSetup(); } catch { }
        }
    }

    // ======================================================================
    // WHEN THE OFFER HAS FINISHED BEING MADE. (0.7.360.)
    //
    // Opponent.OfferSurrenderSequence (private, non-virtual, the base) is
    //   yield return VisualizeOfferSurrender(...);  OfferingSurrender = true;
    // A postfix on an IEnumerator fires at creation, so the enumerator is
    // wrapped and the line is spoken when it runs out — after "I CONCEDE."
    // and after the game has set OfferingSurrender.
    // ======================================================================
    public class Opponent_OfferSurrenderSequence_Patch
    {
        public static void Postfix(ref System.Collections.IEnumerator __result)
        {
            __result = Wrap(__result);
        }

        private static System.Collections.IEnumerator Wrap(System.Collections.IEnumerator inner)
        {
            while (inner.MoveNext()) yield return inner.Current;
            try { SurrenderReader.OnOfferSequenceEnded(); } catch { }
        }
    }

    // ======================================================================
    // HOARDER WITH AN EMPTY DECK. (0.7.360.)
    //
    // Zamar: "Coyote's hoarder ability should have had a fizzle call since
    // there were no cards in deck." Tutor.RespondsToResolveOnBoard is the
    // game's own answer — it returns Deck.CardsInDeck > 0 — so a false here
    // is the game saying the sigil will not run. His approved fizzle wording.
    // One line per card per play: the trigger handler can ask twice.
    // ======================================================================
    public class Tutor_RespondsToResolveOnBoard_Patch
    {
        private static PlayableCard _last;
        private static float _lastAt = -999f;

        public static void Postfix(Tutor __instance, bool __result)
        {
            if (__result) return;
            try
            {
                var card = __instance != null ? __instance.GetComponent<PlayableCard>() : null;
                if (card == null || card.Slot == null) return;
                float now = UnityEngine.Time.unscaledTime;
                if (ReferenceEquals(card, _last) && now - _lastAt < 3f) return;
                _last = card; _lastAt = now;

                string name  = CardReader.CardName(card);
                string sigil = CardReader.GetAbilityName(Ability.Tutor);
                if (string.IsNullOrEmpty(sigil)) sigil = "Hoarder";
                Plugin.Log?.LogInfo($"IKMA SIGIL: Hoarder on '{name}' — the deck is empty, the game declined to trigger it.");
                using (Speech.Event(EventKind.Powers)) Speech.Result(Vocabulary.SigilFizzles(name, sigil));
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA SIGIL: Hoarder fizzle threw {e.GetType().Name}.");
            }
        }
    }
}
