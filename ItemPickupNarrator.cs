// ItemPickupNarrator.cs

using System.Collections.Generic;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The item pickup node's "Leshy shows you a new item" moment. (0.7.336.)
    /// </summary>
    /// <remarks>
    /// Zamar, Session 26, on the 0.7.335 log: Leshy's line "BETTER IN YOUR
    /// HANDS THAN... HIS..." was spoken with nothing before it saying WHAT was
    /// in his hands. A sighted player sees the item lifted up to the camera; a
    /// blind player heard a riddle about an unnamed object.
    ///
    /// WHERE THE MOMENT IS. GainConsumablesSequencer.LearnItemSequence(Item)
    /// (private IEnumerator) is the game lifting the item in front of the
    /// camera, and its body ends in TextDisplayer.ShowUntilInput(description)
    /// — that description IS Leshy's line. It runs only for an item the save
    /// has never been introduced to (ProgressionData.IntroducedConsumable), so
    /// an item already known (the Pliers in his log) is placed straight into
    /// its slot and never gets this line. That is the game's choice, and the
    /// slot browse now names every item anyway.
    ///
    /// WHY A PREFIX IS THE RIGHT TIME HERE. A prefix on an IEnumerator method
    /// fires when the enumerator is CREATED, not when it runs. Usually that is
    /// a trap. Here it is exactly what is wanted: the enumerator is created by
    /// "yield return LearnItemSequence(...)" and its first step starts on the
    /// same frame, so this line is queued before Leshy's line is. Leshy's line
    /// goes in as Dialogue, which inserts AFTER the last Action or Dialogue
    /// entry — so queuing this one as an Action keeps it in front.
    ///
    /// THE WORDS ARE ZAMAR'S. One line per item, keyed on the game's own
    /// rulebookName, with the name itself read from the game at speak time so
    /// the spelling is the game's ("Magickal", not "Magical"). An item he has
    /// not described yet gets the provisional fallback, which is on the ledger.
    /// </remarks>
    internal static class ItemPickupNarrator
    {
        // {0} is replaced with the item's rulebookName, read from the game.
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _presentLines => Loc.PerLanguage(ref _presentLinesCache, ref _presentLinesLanguage, Build_presentLines);
        private static Dictionary<string, string> Build_presentLines() =>
            new Dictionary<string, string>
        {
            // His line, Session 26.
            { "Magickal Bleach", Vocabulary.ItemPickup.LeshyOffersYouAn },
            // His lines, Session 32, applied Session 34. Keys are the game's
            // rulebookName (data\consumables\*.asset). Wiseclock and Special
            // Dagger are still NEED TO SEE and keep the fallback.
            { "Harpie's Birdleg Fan", Vocabulary.ItemPickup.PresentBirdlegFan },
            { "Squirrel In A Bottle", Vocabulary.ItemPickup.PresentSquirrelBottle },
            { "Black Goat Bottle", Vocabulary.ItemPickup.PresentGoatBottle },
            { "Frozen Opossum Bottle", Vocabulary.ItemPickup.PresentOpossumBottle },
            { "Boulder In A Bottle", Vocabulary.ItemPickup.PresentBoulderBottle },
            { "Pliers", Vocabulary.ItemPickup.PresentPliers },
            { "Scissors", Vocabulary.ItemPickup.PresentScissors },
            { "Hourglass", Vocabulary.ItemPickup.PresentHourglass },
            { "Magpie's Glass", Vocabulary.ItemPickup.PresentMagpiesGlass },
            { "Hoggy Bank", Vocabulary.ItemPickup.PresentHoggyBank },
            { "Fish Hook", Vocabulary.ItemPickup.PresentFishHook },
            { "Skinning Knife", Vocabulary.ItemPickup.PresentSkinningKnife },
            { "Failure", Vocabulary.ItemPickup.PresentFailure },
        };
        private static Dictionary<string, string> _presentLinesCache;
        private static string _presentLinesLanguage;

        // His wording, 0.7.359 — "presents" in place of Claude's "offers"
        // (PROVISIONAL_LINES.md). An item with a description of his own uses
        // that instead; this is the line for the ones still blank.
        private static string FallbackLine => Vocabulary.ItemPickup.LeshyPresentsYouThe;

        internal static void NoteItemPresented(Item item)
        {
            string name = null;
            try { name = (item?.Data as ConsumableItemData)?.rulebookName; } catch { }

            // No rulebook name is no display name. Silence beats an internal id.
            if (string.IsNullOrEmpty(name))
            {
                Plugin.Log?.LogInfo("IKMA ITEM PRESENT (not spoken): an item with no rulebook name.");
                return;
            }

            bool his = _presentLines.TryGetValue(name, out string template);
            if (!his)
            {
                template = FallbackLine;
                Plugin.Log?.LogWarning(
                    $"IKMA ITEM PRESENT: no line from Zamar for '{name}' — provisional line used (ledger #48).");
            }

            // 0.7.344 — Shift+R explains the item just held up. Noted when
            // the line is composed, which is when the latch looks for it.
            var consumable = item?.Data as ConsumableItemData;
            string presented = string.Format(template, Loc.Game(name));   // looked up in English, spoken in the game's language
            Speech.Result(() =>
            {
                CardReader.NoteItemInLine(consumable);
                return presented;
            });
        }
    }

    /// <summary>
    /// The pack is full, so a rat comes out with a card. (0.7.423.)
    /// </summary>
    /// <remarks>
    /// Zamar, Session 39: after Leshy's "BUT..." a description should play -
    /// "A large rat scampers from behind the large backpack resting on the
    /// table. It is offering you a Pack Rat card." A sighted player sees the
    /// rat; a blind player heard two words and then silence.
    ///
    /// WHERE THE MOMENT IS. GainConsumablesSequencer.FullConsumablesSequence
    /// (private IEnumerator) plays the "GainConsumablesFull" dialogue, waits
    /// half a second, then calls rat.SetActive(true). The enumerator is
    /// wrapped and the line is queued on the first step after the rat is
    /// active - a prefix would fire before Leshy has spoken.
    ///
    /// THE CARD'S NAME IS THE GAME'S: fullConsumablesReward, the same CardInfo
    /// the sequence puts on the rat's card. Both fields are private
    /// [SerializeField] on GainConsumablesSequencer (dumps\
    /// dump_itempickup_rat_from_decompile.txt).
    /// </remarks>
    internal static class PackFullRatNarrator
    {
        private static readonly System.Reflection.FieldInfo _ratField =
            HarmonyLib.AccessTools.Field(typeof(GainConsumablesSequencer), "rat");
        private static readonly System.Reflection.FieldInfo _rewardField =
            HarmonyLib.AccessTools.Field(typeof(GainConsumablesSequencer), "fullConsumablesReward");

        internal static System.Collections.IEnumerator Wrap(
            GainConsumablesSequencer seq, System.Collections.IEnumerator inner)
        {
            bool said = false;
            while (inner.MoveNext())
            {
                if (!said && RatIsOut(seq))
                {
                    said = true;
                    Announce(seq);
                }
                yield return inner.Current;
            }
        }

        private static bool RatIsOut(GainConsumablesSequencer seq)
        {
            try
            {
                var rat = _ratField?.GetValue(seq) as UnityEngine.GameObject;
                return rat != null && rat.activeSelf;
            }
            catch { return false; }
        }

        private static void Announce(GainConsumablesSequencer seq)
        {
            string name = null;
            try { name = CardReader.BaseCardName(_rewardField?.GetValue(seq) as CardInfo); } catch { }

            // No name from the game is no line. Silence beats a guessed card.
            if (string.IsNullOrEmpty(name))
            {
                Plugin.Log?.LogWarning("IKMA ITEM PICKUP: the rat is out but its card could not be read - not spoken.");
                return;
            }

            Plugin.Log?.LogInfo($"IKMA ITEM PICKUP: pack full - the rat offers {name}.");
            Speech.Result(string.Format(Vocabulary.ItemPickup.RatOffersCard, name));
        }
    }

    // Registered through Plugin.TryPatch as a Postfix. No HarmonyPatch
    // attribute: PatchAll would claim it as well and wrap it TWICE.
    public class GainConsumablesSequencer_FullConsumablesSequence_Patch
    {
        public static void Postfix(GainConsumablesSequencer __instance,
                                   ref System.Collections.IEnumerator __result)
        {
            __result = PackFullRatNarrator.Wrap(__instance, __result);
        }
    }

    // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the prefix would run TWICE.
    public class GainConsumablesSequencer_LearnItemSequence_Patch
    {
        public static void Prefix(Item item)
        {
            ItemPickupNarrator.NoteItemPresented(item);
        }
    }
}
