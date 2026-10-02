// PageGate.cs
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// "If the page hides it from everyone, IKMA hides it too." Asked of the
    /// game for the things Act 1 keeps off the table until Leshy has taught
    /// them. (Session 32, plan A1-1.)
    /// </summary>
    /// <remarks>
    /// THE GAME'S OWN QUESTION. The Act 1 story hides whole systems from a
    /// new player: no bone tokens appear until the Bones lesson
    /// (Part1ResourcesManager.ShowAddBones returns early), no bell stands by
    /// the board until the ending-turn lesson (BoardManager3D.TryShowBell), no
    /// queue row is shown until the queue lesson (BoardManager3D.ShowSlots).
    /// Every one of those asks ProgressionData.LearnedMechanic(concept). So
    /// does this, and nothing else - IKMA keeps no list of its own.
    ///
    /// SAFE IN KAYCEE'S MOD, AND WHY. LearnedMechanic answers from a fixed
    /// list when the save is an Ascension (KM) save
    /// (AscensionStoryAndProgressFlags.MECHANICS_LEARNED), so whatever KM
    /// shows today it still shows: IKMA asks the same function the game uses
    /// to decide what to draw. A failure to ask answers "shown", so a broken
    /// lookup can only cost the new gate, never a line KM already speaks.
    /// </remarks>
    internal static class PageGate
    {
        internal static bool Learned(MechanicsConcept concept)
        {
            try { return ProgressionData.LearnedMechanic(concept); }
            catch { return true; }
        }

        /// <summary>Bone tokens are drawn on the table.</summary>
        internal static bool BonesShown => Learned(MechanicsConcept.Bones);
    }
}
