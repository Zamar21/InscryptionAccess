// TalkingCardNarrator.cs
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// The Act 1 talking cards - Stoat, Stinkbug, Stunted Wolf - speak.
    /// (Session 32, plan A1-2. UNTESTED: they appear in the Act 1 story,
    /// which IKMA does not cover yet.)
    /// </summary>
    /// <remarks>
    /// WHY THIS WAS SILENT. docs/ACT1_STORY_SCOPE.md assumed talking cards
    /// speak "through TextDisplayer - already read and attributed". They do
    /// not. The decompile says otherwise: TalkingCard.PlayLine writes the line
    /// into the card's OWN text box (a SequentialText rendered on the card
    /// face, TalkingCard.Text.PlayMessage) and never calls TextDisplayer. So
    /// IKMA's dialogue patch never saw a single one of their lines.
    ///
    /// THE ONE PLACE A CARD'S LINE IS SHOWN. TalkingCard.PlayLine is public,
    /// not virtual, and is the only caller of Text.PlayMessage (grepped), so
    /// Stoat, Stinkbug, Wolf and the Angler's card all pass through it. A
    /// Prefix is right here even though PlayLine is a coroutine: the only
    /// caller, TalkingCardDialogueHandler.DialogueSequence, creates it with
    /// `yield return talkingCard.PlayLine(item)`, which starts it on the same
    /// step - creation and start are the same moment.
    ///
    /// THE NAME IS THE GAME'S. The speaker is the card itself, so its name is
    /// read off the live card (CardReader.CardName -> the card's displayed
    /// name: "Stoat", "Stinkbug", "Stunted Wolf"), never from the Speaker enum,
    /// whose "Wolf" is not what the card says. The enum is the fallback only.
    ///
    /// SPOKEN LIKE ANY CHARACTER LINE: same markup stripping, same "name only
    /// when the speaker changes" rule, same one-second wait for the voice,
    /// same rumble - all shared with TextDisplayer_ShowMessage_Patch, so a
    /// Leshy line and a Stoat line in one conversation read the same way.
    /// These lines advance on their own (no Space), so DialogueAdvancer is
    /// not told a line is waiting.
    /// </remarks>
    internal static class TalkingCardNarrator
    {
        internal static void OnLine(TalkingCard card, DialogueEvent.Line line)
        {
            if (card == null || line == null) return;

            // The same call the game makes to get the text it shows.
            string raw = Localization.Translate(line.text);
            string clean = TextDisplayer_ShowMessage_Patch.CleanDialogue(raw);
            if (clean.Length == 0) return;

            string speaker = card.SpeakerType.ToString();
            string name = null;
            try { name = CardReader.CardName(card.GetComponent<Card>()?.Info); } catch { }
            if (string.IsNullOrEmpty(name))
                name = TextDisplayer_ShowMessage_Patch.Prettify(speaker);

            Plugin.Log?.LogInfo($"IKMA TALKING: {speaker} card line.");

            // The unreadable-screen net and the idle prompts read this to
            // know the game is talking.
            TextDisplayer_ShowMessage_Patch.LastLineTime = Time.realtimeSinceStartup;

            string spoken = TextDisplayer_ShowMessage_Patch.AttributeLine(name, clean);
            try { Rumble.Dialogue(speaker); } catch { }
            using (Speech.Event(EventKind.Dialogue)) Speech.Dialogue(spoken, TextDisplayer_ShowMessage_Patch.CHARACTER_VOICE_DELAY);
        }
    }

    // Registered by Plugin.TryPatch as a Prefix. No [HarmonyPatch] attribute:
    // PatchAll would register it too and every line would be spoken twice.
    public static class TalkingCard_PlayLine_Patch
    {
        public static void Prefix(TalkingCard __instance, DialogueEvent.Line line)
        {
            try { TalkingCardNarrator.OnLine(__instance, line); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA TALKING: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
