// DeathCardChoiceReader.cs
using System;
using System.Collections;
using System.Collections.Generic;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// Building the death card, the three choices before the name: which card
    /// gives its cost, which its power and health, which its sigils.
    /// (Session 32, plan A1-4. UNTESTED - Act 1 story only.)
    /// </summary>
    /// <remarks>
    /// WHAT THE GAME DOES (DeathCardCreationSequencer.SelectCardFromChoices,
    /// read from the decompile): it throws up to three cards from your deck
    /// onto the floor, face up, with interaction OFF. The first time only,
    /// Leshy explains (dialogue, waits for Space). Then it turns the cards on
    /// (InteractableBase.SetEnabled(true)), Leshy asks for the choice - "draw
    /// the cost from", "the numbers", "the sigils" - and it waits until a card
    /// reports CursorSelectEnded. The two it did not pick die; Leshy names
    /// what was taken; the next step starts. Three steps at most, in that
    /// order, any of which the game skips when the deck is too small.
    ///
    /// A FOURTH SHAPE. The cards live in a LOCAL list inside that coroutine,
    /// not a field (the three shapes in the sequencer notes are all fields).
    /// So IKMA finds them the way they were made: SpawnCard parents each one
    /// under the sequencer's own transform, and SetInfo gives it the exact
    /// CardInfo object from the choices list the coroutine was called with.
    /// "A SelectableCard under this sequencer holding one of these CardInfos"
    /// is therefore exactly the choice cards, in the game's order - the
    /// death card itself holds a newly built CardInfo and never matches.
    /// GetComponentsInChildren on a known instance is not a scene search.
    ///
    /// WHEN IT OWNS THE KEYS. Only while the cards are turned on. Before that
    /// Leshy is talking and the conversation lock answers keys, as anywhere;
    /// after a pick the cards are turned off and the game's own lines play.
    ///
    /// CHOOSING is the same two calls every IKMA reader uses to stand in for a
    /// click (CursorSelectStart / CursorSelectEnd), so the game's own
    /// CursorSelectEnded delegate is what records the choice. IKMA writes
    /// nothing.
    ///
    /// WHAT IT SAYS. A browsed card is read exactly as on a card choice node
    /// (CardChoiceReader.DescribeCard), and Space adds the "x of y" position
    /// the same way. The one new line - what the screen is, said when the
    /// cards turn on and on H and the idle timer - is PROVISIONAL and asked.
    /// </remarks>
    internal static class DeathCardChoiceReader
    {
        private const float IDLE_FIRST = 5.5f;
        private const float IDLE_REPEAT = 15f;

        private static DeathCardCreationSequencer _sequencer;
        private static List<CardInfo> _choices;
        private static string _step;          // the game's ChoiceType name: Cost, Stats, Abilities
        private static List<SelectableCard> _cards;
        private static bool _announced;
        private static int _index = -1;
        private static SelectableCard _hovered;
        private static float _idleFor;
        private static float _idleInterval = IDLE_FIRST;

        internal static bool Active => _sequencer != null;

        /// <summary>Wraps SelectCardFromChoices; Begin on its first step, End when it finishes.</summary>
        internal static IEnumerator Wrap(DeathCardCreationSequencer sequencer, List<CardInfo> choices,
                                         string step, IEnumerator inner)
        {
            Begin(sequencer, choices, step);
            try
            {
                while (inner.MoveNext())
                    yield return inner.Current;
            }
            finally
            {
                End();
            }
        }

        private static void Begin(DeathCardCreationSequencer sequencer, List<CardInfo> choices, string step)
        {
            _sequencer = sequencer;
            _choices = choices != null ? new List<CardInfo>(choices) : new List<CardInfo>();
            _step = step;
            _cards = null;
            _announced = false;
            _index = -1;
            _hovered = null;
            _idleFor = 0f;
            _idleInterval = IDLE_FIRST;
            Plugin.Log?.LogInfo($"IKMA DEATHCARD: choice step '{step}', {_choices.Count} card(s).");
        }

        private static void End()
        {
            if (_sequencer == null) return;
            ReleaseHover();
            Plugin.Log?.LogInfo($"IKMA DEATHCARD: choice step '{_step}' ended.");
            _sequencer = null;
            _choices = null;
            _cards = null;
        }

        /// <summary>
        /// The choice cards, in the game's order, once they exist. Looked up
        /// until all of them are found, then cached for the step.
        /// </summary>
        private static List<SelectableCard> Cards()
        {
            if (_cards != null && _cards.Count == _choices.Count) return _cards;
            var found = new SelectableCard[_choices.Count];
            int n = 0;
            try
            {
                foreach (var card in _sequencer.GetComponentsInChildren<SelectableCard>(true))
                {
                    if (card == null || card.Info == null) continue;
                    // First still-empty position holding this exact CardInfo -
                    // so the same object twice in the list still maps to two cards.
                    for (int i = 0; i < _choices.Count; i++)
                    {
                        if (found[i] == null && ReferenceEquals(_choices[i], card.Info))
                        { found[i] = card; n++; break; }
                    }
                }
            }
            catch { }
            var list = new List<SelectableCard>();
            foreach (var c in found) if (c != null) list.Add(c);
            _cards = list;
            return list;
        }

        /// <summary>Choosable right now: every card is out and the game has turned them on.</summary>
        private static bool Ready(List<SelectableCard> cards)
        {
            if (cards.Count == 0 || cards.Count < _choices.Count) return false;
            foreach (var c in cards)
            {
                try { if (c == null || !c.Enabled) return false; }
                catch { return false; }
            }
            return true;
        }

        /// <summary>
        /// Called by HotkeyManager every frame. True = the choice cards are up
        /// and this reader answered the frame.
        /// </summary>
        internal static bool Tick()
        {
            if (_sequencer == null) return false;
            var cards = Cards();
            if (!Ready(cards)) return false;

            if (!_announced)
            {
                _announced = true;
                // Queued, so it follows Leshy's prompt rather than cutting it.
                Speech.Prompt(() => Vocabulary.DeathCardChoice.Screen(_step, cards.Count));
            }

            bool key = false;
            if (KeyIn.Down(KeyCode.LeftArrow) || KeyIn.Down(KeyCode.UpArrow))
            { key = true; Browse(cards, -1); }
            else if (KeyIn.Down(KeyCode.RightArrow) || KeyIn.Down(KeyCode.DownArrow))
            { key = true; Browse(cards, 1); }
            else if (ListStep.JumpKey() is int jump && jump != 0)   // 0.4.8.012 - Home / End
            { key = true; Browse(cards, jump); }
            else if (KeyIn.Down(KeyCode.Return) || KeyIn.Down(KeyCode.KeypadEnter))
            { key = true; Choose(cards); }
            else if (KeyIn.Down(KeyCode.Space))
            {
                key = true;
                Speech.Browse(_index < 0
                    ? Vocabulary.DeathCardChoice.Screen(_step, cards.Count)
                    : Vocabulary.OptionOf(CardChoiceReader.DescribeCard(cards[_index]), _index + 1, cards.Count));
            }
            else if (KeyIn.Down(KeyCode.H))
            { key = true; Speech.Browse(Vocabulary.DeathCardChoice.Screen(_step, cards.Count)); }

            if (key) { _idleFor = 0f; _idleInterval = IDLE_FIRST; }
            else
            {
                _idleFor += Time.unscaledDeltaTime;
                if (_idleFor >= _idleInterval)
                {
                    _idleFor = 0f;
                    _idleInterval = IDLE_REPEAT;
                    Speech.Prompt(() => Vocabulary.DeathCardChoice.Screen(_step, cards.Count));
                }
            }
            return true;
        }

        private static void Browse(List<SelectableCard> cards, int direction)
        {
            int count = cards.Count;
            if (_index < 0 && !ListStep.IsJump(direction)) _index = 0;
            else if (!ListStep.Step(ref _index, direction, count)) return;   // 0.4.8.007 - stops at the ends
            Hover(cards[_index]);
            Speech.Browse(CardChoiceReader.DescribeCard(cards[_index]));
        }

        private static void Choose(List<SelectableCard> cards)
        {
            if (_index < 0 || _index >= cards.Count)
            {
                // Nothing browsed yet: say what the screen is rather than
                // silently picking the first card.
                Speech.Browse(Vocabulary.DeathCardChoice.Screen(_step, cards.Count));
                return;
            }
            var card = cards[_index];
            try
            {
                card.CursorEnter();
                card.CursorSelectStart();
                card.CursorSelectEnd();
                Plugin.Log?.LogInfo($"IKMA DEATHCARD: chose option {_index + 1} for '{_step}' via CursorSelectStart/End.");
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA DEATHCARD: choosing failed: {e.GetType().Name}: {e.Message}");
            }
            // No confirmation line: Leshy says what was taken, in the game's
            // own words, straight after (ExamineCardWithDialogue).
        }

        private static void Hover(SelectableCard entering)
        {
            try
            {
                if (_hovered != null && _hovered != entering) _hovered.CursorExit();
                entering?.CursorEnter();
                _hovered = entering;
            }
            catch { }
        }

        private static void ReleaseHover()
        {
            try { if (_hovered != null) _hovered.CursorExit(); } catch { }
            _hovered = null;
        }
    }

    // Registered by Plugin.TryPatch as a Postfix. No [HarmonyPatch] attribute.
    // SelectCardFromChoices is PRIVATE; its third parameter is a private enum
    // (ChoiceType), so it is read from Harmony's argument array by position
    // and named with ToString() - "Cost", "Stats", "Abilities".
    public static class DeathCardCreationSequencer_SelectCardFromChoices_Patch
    {
        public static void Postfix(DeathCardCreationSequencer __instance, object[] __args, ref IEnumerator __result)
        {
            List<CardInfo> choices = null;
            string step = "Unknown";
            try
            {
                choices = __args != null && __args.Length > 0 ? __args[0] as List<CardInfo> : null;
                if (__args != null && __args.Length > 2 && __args[2] != null) step = __args[2].ToString();
            }
            catch { }
            __result = DeathCardChoiceReader.Wrap(__instance, choices, step, __result);
        }
    }
}
