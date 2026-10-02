// Kaycees.cs
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// Kaycee's Mod is always on the start menu while IKMA is loaded.
    /// (Session 34; Zamar's decision, Session 33.)
    /// </summary>
    /// <remarks>
    /// HIS DECISION, and a named exception to "if the page hides it, IKMA
    /// hides it": unlocking Kaycee's Mod is a base part of IKMA. The
    /// mechanism is his too: always shown while IKMA is on, NOTHING written
    /// to the save; with IKMA off, the player's own save decides.
    ///
    /// HOW. The game asks one question in two places before it shows the
    /// Kaycee's Mod card: StoryEventsData.EventCompleted(ChapterSelectUnlocked)
    /// in MenuController.TweenInCards and in StartMenuAscensionCardInitializer.
    /// Start. This answers that question "yes" - it does not SET the event,
    /// so the save file never changes. Grepped the whole decompile: the only
    /// other readers of ChapterSelectUnlocked are LukeMurderedScene (which
    /// sets it, at the end of the story) and a developer camera cheat in
    /// VideoCameraRig. EventCompleted is PUBLIC STATIC bool (StoryEventsData.
    /// cs:42). Registered through TryPatch.
    /// </remarks>
    public static class StoryEventsData_EventCompleted_Patch
    {
        public static void Postfix(StoryEvent storyEvent, ref bool __result)
        {
            if (!__result && storyEvent == StoryEvent.ChapterSelectUnlocked)
                __result = true;
        }
    }
}
