// SoftlockWatchdog.cs
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// The safety net for the worst class of IKMA bug: a reader that still
    /// holds the controls after the game has moved on. (Session 34.)
    /// </summary>
    /// <remarks>
    /// ZAMAR, Session 34, after the Mycologists stranded him on the map: "We
    /// need a fall back for these softlocks ... softlocks like that should be
    /// treated very severely as they're direct failures of our mod's
    /// stability."
    ///
    /// Each of those softlocks had its own cause and got its own fix, and
    /// the next one will too. This is the layer underneath all of them: it
    /// does not need to know WHY a reader is stuck, only that the game has
    /// plainly left the reader's screen.
    ///
    /// THE TEST, asked of the game, not of IKMA's memory:
    ///   - the game says it is on the MAP (GameFlowManager.CurrentGameState,
    ///     PUBLIC), and
    ///   - the map is really there and waiting (MapReader.MapAvailable: an
    ///     active node, not moving, the board switched on), and
    ///   - a node screen or a card choice reader still claims the controls,
    /// continuously for HOLD_SECONDS. No node screen or card choice is ever
    /// legitimately up while the map is live and waiting, so this cannot
    /// release a screen the player is using.
    ///
    /// THEN: release both readers through their own exits, log it loudly
    /// (a release here is always a bug to fix at its source), and give the
    /// player the map line so they know the controls are back.
    ///
    /// COST: nothing unless a reader is active; then two singleton reads
    /// every half second (see ikma_singleton_cost - never per frame).
    /// </remarks>
    internal static class SoftlockWatchdog
    {
        private const float HOLD_SECONDS  = 2f;
        private const float CHECK_EVERY   = 0.5f;

        private static float _nextCheck;
        private static float _strandedSince = -1f;

        /// <summary>Once a frame, from HotkeyManager.UpdateOuter.</summary>
        internal static void Tick()
        {
            bool readerUp = NodeScreenReader.Active || CardChoiceReader.Active;
            if (!readerUp) { _strandedSince = -1f; return; }

            float now = Time.unscaledTime;
            if (now < _nextCheck) return;
            _nextCheck = now + CHECK_EVERY;

            bool onMap = false;
            try
            {
                var flow = Singleton<GameFlowManager>.Instance;
                onMap = flow != null && flow.CurrentGameState == GameState.Map && MapReader.MapAvailable();
            }
            catch { onMap = false; }

            if (!onMap) { _strandedSince = -1f; return; }
            if (_strandedSince < 0f) { _strandedSince = now; return; }
            if (now - _strandedSince < HOLD_SECONDS) return;

            _strandedSince = -1f;
            Plugin.Log?.LogWarning(
                "IKMA WATCHDOG: SOFTLOCK CAUGHT - the game is on the map but " +
                (NodeScreenReader.Active ? "a node screen reader" : "the card choice reader") +
                " still held the controls. Released. This is a bug: find why the reader did not end.");

            try { if (CardChoiceReader.Active) CardChoiceReader.End("watchdog: the game is on the map"); } catch { }
            try
            {
                if (NodeScreenReader.Active)
                {
                    NodeProbe.Finished();
                    if (NodeScreenReader.Active) NodeScreenReader.End();
                }
            }
            catch { }

            try { MapReader.AnnounceMapReady(); } catch { }
        }
    }
}
