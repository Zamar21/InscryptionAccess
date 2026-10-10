// ListStep.cs
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// ONE ARROW STEP THROUGH A LIST, STOPPING AT THE ENDS. (0.4.8.007.)
    ///
    /// Zamar, Session 58, from a beta tester's note ("Wrapping is confusing,
    /// especially for the map. I thought I had multiple encounters when I
    /// only had one option."): every list IKMA arrows through stops at its
    /// ends, and the press at an end says nothing. Tab's next-playable
    /// search still goes round; only the arrows stop.
    /// </summary>
    internal static class ListStep
    {
        /// <summary>
        /// 0.4.8.012 - HOME AND END, as directions. A beta tester asked for
        /// Home and End on the options pages; Zamar: "That should work
        /// everywhere, yes." Every list's Browse already takes a direction and
        /// hands it to Step, so the two jumps travel the same road as an arrow
        /// and land with the same hover and the same read.
        /// </summary>
        internal const int Home = -1000000;
        internal const int End  =  1000000;

        internal static bool IsJump(int direction) => direction == Home || direction == End;

        /// <summary>Home or End pressed this frame, as a direction; 0 when neither.</summary>
        internal static int JumpKey()
        {
            if (KeyIn.Down(KeyCode.Home)) return Home;
            if (KeyIn.Down(KeyCode.End))  return End;
            return 0;
        }

        /// <summary>
        /// For a walk that skips entries (empty item slots, cards that cannot
        /// be sacrificed): a jump starts outside the list and walks in from
        /// that end, so it lands on the first usable entry from the edge.
        /// </summary>
        internal static void JumpToScan(ref int index, ref int direction)
        {
            if (direction == Home)     { index = -1; direction =  1; }
            else if (direction == End) { index = -1; direction = -1; }
        }

        /// <summary>
        /// Move <paramref name="index"/> one step in <paramref name="direction"/>
        /// inside 0..count-1. False when it is already at that end: nothing
        /// moved, and the caller says nothing.
        /// </summary>
        internal static bool Step(ref int index, int direction, int count)
        {
            if (count <= 0) return false;
            // 0.4.8.012 - a jump always lands, and re-reads when already there.
            if (direction == Home) { index = 0;         return true; }
            if (direction == End)  { index = count - 1; return true; }
            // From nowhere, the first press lands on the end it points into;
            // a list that shrank under the cursor lands on its last item.
            if (index < 0) { index = direction < 0 ? count - 1 : 0; return true; }
            if (index >= count) { index = count - 1; return true; }
            int next = index + (direction < 0 ? -1 : 1);
            if (next < 0 || next >= count) return false;
            index = next;
            return true;
        }
    }
}
// ListStep.cs
