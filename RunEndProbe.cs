// RunEndProbe.cs
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The death flow, end to end, in the log and nowhere else. LOG ONLY —
    /// this file speaks not one word.
    /// </summary>
    /// <remarks>
    /// 0.7.224, Session 21. Zamar: "okay going to lose and end the run this
    /// next time. Set up any kind of log you need to for the whole death
    /// flow."
    ///
    /// Losing a run is the one sequence a playtester cannot repeat on demand —
    /// it costs a whole run to see once. So this is a probe, not a feature: it
    /// records what the game does at every step so the NEXT session can build
    /// the narration from evidence instead of from a second lost run.
    ///
    /// THE FLOW, from the decompiled source, so the log can be read against it:
    ///
    ///   TurnManager  (PlayerWon == false)
    ///     -> opponent.OutroSequence(false)
    ///     -> SpecialSequencer.GameEnd(false)
    ///     -> Part1GameFlowManager.PlayerLostBattleSequence(opponent)   PUBLIC
    ///          SpecialSequencer.CardBattleLost()
    ///          CandleHolder.BlowOutCandleSequence()      <- YOUR candle
    ///          SpecialSequencer.CandleBlownOut()
    ///          if playerLives  > 0: dialogue "PlayerLifeLost"  (run continues)
    ///          if playerLives <= 0:
    ///              RunState.Run.causeOfDeath = new CauseOfDeath(OpponentType)
    ///              opponent.DefeatedPlayerSequence()      <- the boss gloats
    ///              Part1GameFlowManager.KillPlayerSequence()   NONPUBLIC
    ///                  "eyes_opening", both arms "reach_at_player",
    ///                  TextDisplayer.Clear, screen to near-black,
    ///                  AscensionMenuScreens.ReturningFromFailedRun = true
    ///                  AscensionStat.Losses, SaveManager.SaveToFile()
    ///                  SceneLoader.Load("Ascension_Configure")
    ///     -> then the Ascension_Configure screens: run end, unlocks, stats,
    ///        journal — MenuReader covers those generically today.
    ///
    /// WHAT THIS ALREADY TELLS US WITHOUT PLAYING IT. There is NO death card
    /// on a Kaycee's Mod loss: DeathCardCreationSequencer appears nowhere in
    /// this path, because KM uses DefaultDeathCards instead. If a death card
    /// screen shows up in the log anyway, that assumption is wrong and it is
    /// the most valuable thing the run will have found.
    ///
    /// EVERY HOOK IS A PREFIX ON A COROUTINE, so each fires at enumerator
    /// creation — the start of its step, before the step runs. That is exactly
    /// what a flow probe wants: the log reads as the running order.
    ///
    /// TAKE THIS OUT, or put it behind a category flag, once the death flow is
    /// narrated. The log ships; this is a lot of lines for one event.
    /// </remarks>
    public static class RunEndProbe
    {
        private static int Lives()
        {
            try { return RunState.Run != null ? RunState.Run.playerLives : -1; }
            catch { return -1; }
        }

        private static string CauseOfDeath()
        {
            try
            {
                var cause = RunState.Run?.causeOfDeath;
                return cause == null ? "none yet" : cause.ToString();
            }
            catch { return "unreadable"; }
        }

        internal static void OnBattleLost(Opponent opponent)
        {
            try
            {
                string who = "?";
                try { who = opponent != null ? opponent.GetType().Name : "null"; } catch { }

                string type = "?";
                try { type = opponent != null ? opponent.OpponentType.ToString() : "null"; } catch { }

                int lives = Lives();

                Plugin.Log?.LogInfo(
                    "IKMA RUNEND ===== the player lost a battle =====");
                Plugin.Log?.LogInfo(
                    $"IKMA RUNEND: opponent={who} (OpponentType={type}), " +
                    $"playerLives BEFORE the candle = {lives}, IsAscension={SaveFile.IsAscension}.");
                Plugin.Log?.LogInfo(
                    lives > 1
                        ? "IKMA RUNEND: expect a candle out, then dialogue 'PlayerLifeLost', then the map."
                        : "IKMA RUNEND: expect a candle out, then the boss's DefeatedPlayerSequence, " +
                          "then KillPlayerSequence and the run ends.");
            }
            catch { }
        }

        internal static void OnPlayerCandleOut()
        {
            try
            {
                Plugin.Log?.LogInfo(
                    $"IKMA RUNEND: CandleHolder.BlowOutCandleSequence — the player's candle. " +
                    $"playerLives reads {Lives()} at this moment (the game may not have " +
                    "decremented it yet; that ordering is one of the things this probe is for).");
            }
            catch { }
        }

        internal static void OnBossGloat(Opponent opponent)
        {
            try
            {
                string who = "?";
                try { who = opponent != null ? opponent.GetType().Name : "null"; } catch { }

                Plugin.Log?.LogInfo(
                    $"IKMA RUNEND: {who}.DefeatedPlayerSequence — the boss's parting line. " +
                    $"causeOfDeath={CauseOfDeath()}. This is a ShowMessage/ShowUntilInput " +
                    "line and should already be read by the dialogue layer; confirm it is, " +
                    "and confirm who it is attributed to.");
            }
            catch { }
        }

        // 0.7.360 — THE LAST CANDLE IS NOT STOMPED. Zamar, on the candle line
        // and Leshy's "ALAS..." being cut by "Loading.": "These lines should
        // not be stomped." When the kill happened is recorded here so the load
        // that follows stays quiet and the run end screen follows, not cuts.
        internal static float KilledAt = -999f;
        internal static bool DeathJustHappened
            => UnityEngine.Time.unscaledTime - KilledAt < 20f;

        internal static void OnKillPlayer()
        {
            KilledAt = UnityEngine.Time.unscaledTime;
            try
            {
                Plugin.Log?.LogInfo(
                    "IKMA RUNEND ===== the run is over =====");
                Plugin.Log?.LogInfo(
                    $"IKMA RUNEND: KillPlayerSequence — playerLives={Lives()}, " +
                    $"causeOfDeath={CauseOfDeath()}. Leshy reaches for the player with BOTH " +
                    "arms ('reach_at_player'), TextDisplayer is CLEARED, the screen goes to " +
                    "near-black, and about 2 seconds later the scene loads " +
                    "Ascension_Configure. NOTHING IS SPOKEN over any of it today — roughly " +
                    "six seconds of a hand coming at you with no line. That is the gap.");
            }
            catch { }
        }

        internal static void OnMenuScreen(AscensionMenuScreens.Screen screen)
        {
            try
            {
                Plugin.Log?.LogInfo(
                    $"IKMA RUNEND: Ascension screen -> {screen}. " +
                    $"ReturningFromFailedRun={AscensionMenuScreens.ReturningFromFailedRun}, " +
                    $"ReturningFromSuccessfulRun={AscensionMenuScreens.ReturningFromSuccessfulRun}.");
            }
            catch { }
        }
    }

    // Registered by Plugin.TryPatch. No [HarmonyPatch] attribute — CHECK 1
    // fails a class that carries both. All log-only.

    public static class Part1GameFlowManager_PlayerLostBattleSequence_Patch
    {
        public static void Prefix(Opponent opponent) => RunEndProbe.OnBattleLost(opponent);
    }

    public static class CandleHolder_BlowOutCandleSequence_Patch
    {
        public static void Prefix() => RunEndProbe.OnPlayerCandleOut();
    }

    public static class Opponent_DefeatedPlayerSequence_Patch
    {
        public static void Prefix(Opponent __instance) => RunEndProbe.OnBossGloat(__instance);
    }

    public static class Part1BossOpponent_DefeatedPlayerSequence_Patch
    {
        public static void Prefix(Opponent __instance) => RunEndProbe.OnBossGloat(__instance);
    }

    public static class Part1GameFlowManager_KillPlayerSequence_Patch
    {
        public static void Prefix() => RunEndProbe.OnKillPlayer();
    }

    public static class AscensionMenuScreens_SwitchToScreen_Patch
    {
        public static void Prefix(AscensionMenuScreens.Screen screen)
            => RunEndProbe.OnMenuScreen(screen);
    }
}

// RunEndProbe.cs
