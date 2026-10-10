// MycologistResult.cs
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The Mycologists' result, said after the game's own result line.
    /// (0.4.8.004, Session 56.)
    /// </summary>
    /// <remarks>
    /// The 2026-10-09 driver run fused a pair and nothing named what came out:
    /// the doctor's "WH- WHAT HAVE WE DONE?" was the last thing spoken before
    /// the map. Zamar: after that line it should read "Fused [Card Name] is
    /// added to your deck." His words.
    ///
    /// DuplicateMergeSequencer.CombinePair (private IEnumerator) plays the
    /// result dialogue and slides the merged card off the table, then ends.
    /// The name is read when the coroutine is created, while the pair still
    /// exists (the selection slot destroys it part way through), and spoken
    /// when it ends, behind the dialogue. MergeCards keeps the LEFT card's
    /// CardInfo and adds the right one's stats to it, so the left card's name
    /// is the fused card's name.
    /// </remarks>
    internal static class MycologistResult
    {
        // 0.4.8.006 - true while the fusion is playing out (stone pressed to
        // the result line). The game waits on nothing in between, so Space
        // and the idle prompt stay quiet. Timed out so a scene change that
        // kills the coroutine cannot strand it.
        private static float _startedAt = -100f;
        internal static bool Busy => UnityEngine.Time.unscaledTime - _startedAt < 15f;

        internal static System.Collections.IEnumerator Wrap(
            System.Collections.IEnumerator inner, SelectableCardPair pair)
        {
            _startedAt = UnityEngine.Time.unscaledTime;
            string name = null;
            CardInfo info = null;
            try { info = pair?.LeftCard?.Info; name = CardReader.CardName(info); } catch { }

            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext()) break;
                    current = inner.Current;
                }
                catch (System.Exception e)
                {
                    Plugin.Log?.LogWarning($"IKMA MYCOLOGISTS: CombinePair threw {e.GetType().Name}: {e.Message}");
                    _startedAt = -100f;
                    yield break;
                }
                yield return current;
            }
            _startedAt = -100f;

            if (string.IsNullOrEmpty(name))
            {
                Plugin.Log?.LogWarning("IKMA MYCOLOGISTS: fused card has no name - result not spoken.");
                yield break;
            }
            // Zamar, Session 56: the name, its new power and health, and the
            // sigils it now has ("Yes all that"). Read at the end, from the same
            // CardInfo MergeCards modified, so the numbers are the fused ones.
            string line = Vocabulary.NodeScreens.FusedIsAddedToYourDeck(name) + StatsAndSigils(info);
            Plugin.Log?.LogInfo($"IKMA MYCOLOGISTS: {line}");
            using (Speech.Event(EventKind.NodeResults)) Speech.Result(line);
        }

        // " 6, 6. Abilities: Trinket Bearer and Stinky." - the card read's own
        // stat and sigil shapes (Vocabulary.Cards), nothing new.
        private static string StatsAndSigils(CardInfo info)
        {
            if (info == null) return "";
            try
            {
                string atk = info.SpecialStatIcon != SpecialStatIcon.None
                    ? Vocabulary.Cards.StarAttack
                    : info.Attack.ToString();
                var seen = new System.Collections.Generic.HashSet<Ability>();
                var sigils = new System.Collections.Generic.List<string>();
                if (info.Abilities != null)
                    foreach (var ab in info.Abilities)
                    {
                        if (!seen.Add(ab)) continue;
                        string n = CardReader.GetAbilityName(ab);
                        if (!string.IsNullOrEmpty(n)) sigils.Add(n);
                    }
                string abilityPart = sigils.Count > 0
                    ? Vocabulary.Cards.AbilitiesLine(Vocabulary.Cards.AbilityCount(sigils.Count), sigils)
                    : "";
                return Vocabulary.NodeScreens.FusedStats(atk, info.Health) + abilityPart;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA MYCOLOGISTS: stats unreadable - {e.GetType().Name}.");
                return "";
            }
        }
    }

    // Registered by Plugin.TryPatch. No HarmonyPatch attribute.
    public static class DuplicateMergeSequencer_CombinePair_Patch
    {
        public static void Postfix(SelectableCardPair pair, ref System.Collections.IEnumerator __result)
            => __result = MycologistResult.Wrap(__result, pair);
    }
}

// MycologistResult.cs
