// SilenceKey.cs
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// THE SILENCE KEY. (Session 36, MASTER_PLAN M9, mandatory for v0.5.)
    ///
    /// Zamar's calls: a Ctrl tap on the keyboard (press and release Ctrl with
    /// nothing else pressed, so Ctrl+Up / Ctrl+Down still scroll the review
    /// history), R3 on the pad, and it kills EVERYTHING: the line in the air
    /// and every line still queued. "I found no scouted mod that cuts only the
    /// current line" - Say the Spire 2 calls Tolk.Silence (the screen reader's
    /// whole queue) and SnapAccess's S "stops all speech".
    ///
    /// Queued game events are not lost: each one is still composed at its
    /// normal moment and written to the review history, unspoken
    /// (CombatAnnouncer.SilenceQueue). Prompts and character dialogue are
    /// dropped, the same rule the history already follows.
    /// </summary>
    internal static class SilenceKey
    {
        // The pad's R3 (and the test driver) arrive as this key through
        // KeyIn's binding table. No keyboard has an F15 key in practice; it
        // is a token, not a promise.
        internal const KeyCode PadToken = KeyCode.F15;

        private static bool _ctrlClean;

        /// <summary>Every frame, ahead of every reader. Speaks nothing.</summary>
        internal static void Tick()
        {
            if (KeyIn.Down(PadToken)) { Silence("pad"); return; }

            // The keyboard's Ctrl tap, read off the real keyboard: the pad's
            // LB+RB chord also counts as Ctrl held (KeyIn.Held), and a chord
            // released without a D-pad press must not silence anything.
            bool ctrlDown = Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl);
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool ctrlUp   = Input.GetKeyUp(KeyCode.LeftControl) || Input.GetKeyUp(KeyCode.RightControl);

            if (ctrlDown) _ctrlClean = true;
            // Any other key while Ctrl is down makes it a chord, not a tap.
            else if (ctrlHeld && Input.anyKeyDown) _ctrlClean = false;

            if (ctrlUp && !ctrlHeld)
            {
                bool tap = _ctrlClean;
                _ctrlClean = false;
                if (tap) Silence("Ctrl");
            }
        }

        internal static void Silence(string how)
        {
            int queued = CombatAnnouncer.SilenceQueue();
            Plugin.Log?.LogInfo($"IKMA SILENCE: {how} - speech cut, {queued} queued line(s) silenced.");
            Speech.Silence();
        }
    }
}
// SilenceKey.cs
