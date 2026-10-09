// DevCheats.cs

// ============================================================================
// DEVELOPMENT TOOL. NEVER SHIPS. (Session 46, 0.7.452.)
//
// Zamar, Session 46, on the unlock screens that follow a won run: "Build it
// yeah." And on the rare cards: "If you could cheat all those rare cards
// into a starting deck and run a driver on it for a while to check it out
// that would be ideal testing I think, just to get some coverage there
// before the beta."
//
// Five extra verbs for the test driver (DevDriver.cs). Like DevDriver, the
// whole file sits behind IKMA_DEV; a release build does not contain it.
//
//   deck rares            add one of every rare card to this run's deck.
//   deck onlyrares        the same, after emptying the deck first.
//   deck add <CardName>   add one card by the game's internal name
//                         ("Ijiraq", "LongElk"). "deck list" prints the deck.
//   hand <CardName>       in a battle: put that card in the hand, through
//                         CardSpawner.SpawnCardToHand - the door the game's
//                         own sigils use (Rabbit Hole, Bees Within).
//   bones <n>             in a battle: ResourcesManager.AddBones, so an
//                         expensive card can be played at once.
//   level <1-13>          set the save's challenge level and reload the
//                         Kaycee's Mod menu, so what is LOCKED at that level
//                         can be read (challenges, starter decks, cards).
//                         Unlock All left his save at 13, everything open.
//   unlocktest <1-12>     open the Kaycee's Mod menu as if a run had just
//                         been WON at that challenge level, so the unlock
//                         screens for the level can be read: devlog entry,
//                         new cards, new starter deck, new challenges.
//
// THE NAMED EXCEPTION TO "NEVER WRITE GAME STATE". IKMA itself never writes
// the game's flags; it injects input. unlocktest does write two: the save's
// challengeLevel and AscensionMenuScreens.ReturningFromSuccessfulRun - the
// same static Part1BossOpponent.EndAscensionRun sets before it loads the
// menu scene. Each level's screens otherwise need a whole won run, twelve
// times. It is a test fixture, in a build no player gets, on a save Zamar
// has called disposable.
//
// NO ACHIEVEMENTS FROM A CHEAT. The menu calls AchievementManager.Unlock as
// the level goes up. From the first unlocktest until the game is closed,
// that call is stopped (AchievementManager_Unlock_DevPatch below), so a test
// never puts an achievement on his Steam account.
// ============================================================================
#if IKMA_DEV

using System;
using System.Collections.Generic;
using System.Text;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    internal static class DevCheats
    {
        /// <summary>Set by unlocktest; stays set until the game closes.</summary>
        internal static bool BlockAchievements;

        /// <summary>
        /// Runs one verb. True with a note for the driver's DONE line, or
        /// false with the reason for its ERR line.
        /// </summary>
        internal static bool Handle(string verb, string arg, out string note, out string error)
        {
            note = ""; error = null;
            try
            {
                switch (verb)
                {
                    case "deck":       return Deck(arg, out note, out error);
                    case "hand":       return Hand(arg, out note, out error);
                    case "bones":      return Bones(arg, out note, out error);
                    case "unlocktest": return UnlockTest(arg, out note, out error);
                    case "level":      return Level(arg, out note, out error);
                }
                error = "not a cheat verb";
                return false;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        // Every Act 1 rare, locked or not: the point is coverage, and a
        // fresh save has almost none of them unlocked.
        private static List<CardInfo> AllRares()
        {
            var all = ScriptableObjectLoader<CardInfo>.AllData;
            var rares = new List<CardInfo>();
            if (all == null) return rares;
            foreach (var c in all)
            {
                if (c == null || c.temple != CardTemple.Nature) continue;
                if (c.metaCategories == null || !c.metaCategories.Contains(CardMetaCategory.Rare)) continue;
                rares.Add(c);
            }
            return rares;
        }

        private static CardInfo ByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var all = ScriptableObjectLoader<CardInfo>.AllData;
            if (all == null) return null;
            // Checked here first: CardLoader.GetCardByName clones whatever it
            // finds, and cloning nothing throws.
            if (!all.Exists(x => x != null && x.name == name)) return null;
            return CardLoader.GetCardByName(name);
        }

        private static bool Deck(string arg, out string note, out string error)
        {
            note = ""; error = null;
            var run = RunState.Run;
            if (run == null || run.playerDeck == null) { error = "no run in progress"; return false; }
            var deck = run.playerDeck;

            string[] parts = arg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string what = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";

            if (what == "list")
            {
                var sb = new StringBuilder();
                foreach (var c in deck.Cards) { if (sb.Length > 0) sb.Append(", "); sb.Append(c != null ? c.name : "?"); }
                note = deck.Cards.Count + " cards: " + sb;
                return true;
            }

            if (what == "add")
            {
                var info = ByName(parts.Length > 1 ? parts[1].Trim() : "");
                if (info == null) { error = "no card with that internal name"; return false; }
                deck.AddCard(info);
                note = "added " + info.name + "; deck is " + deck.Cards.Count + " cards";
                return true;
            }

            if (what == "rares" || what == "onlyrares")
            {
                if (what == "onlyrares")
                    foreach (var old in new List<CardInfo>(deck.Cards)) deck.RemoveCard(old);

                var names = new StringBuilder();
                int n = 0;
                foreach (var rare in AllRares())
                {
                    var info = ByName(rare.name);
                    if (info == null) continue;
                    deck.AddCard(info);
                    if (names.Length > 0) names.Append(", ");
                    names.Append(rare.name);
                    n++;
                }
                note = "added " + n + " rares; deck is " + deck.Cards.Count + " cards: " + names;
                return true;
            }

            error = "'rares', 'onlyrares', 'add <CardName>' or 'list'";
            return false;
        }

        private static bool InBattle()
        {
            var tm = Singleton<TurnManager>.Instance;
            return tm != null && tm.Opponent != null && !tm.GameEnded;
        }

        private static bool Hand(string arg, out string note, out string error)
        {
            note = ""; error = null;
            if (!InBattle()) { error = "not in a battle"; return false; }
            var info = ByName(arg.Trim());
            if (info == null) { error = "no card with that internal name"; return false; }
            var spawner = Singleton<CardSpawner>.Instance;
            if (spawner == null) { error = "no CardSpawner"; return false; }
            spawner.StartCoroutine(spawner.SpawnCardToHand(info));
            note = info.name + " on its way to the hand";
            return true;
        }

        private static bool Bones(string arg, out string note, out string error)
        {
            note = ""; error = null;
            int n;
            if (!int.TryParse(arg.Trim(), out n) || n < 1 || n > 99) { error = "a number from 1 to 99"; return false; }
            if (!InBattle()) { error = "not in a battle"; return false; }
            var rm = Singleton<ResourcesManager>.Instance;
            if (rm == null) { error = "no ResourcesManager"; return false; }
            rm.StartCoroutine(rm.AddBones(n));
            note = n + " bones on their way";
            return true;
        }

        private static bool Level(string arg, out string note, out string error)
        {
            note = ""; error = null;
            int level;
            if (!int.TryParse(arg.Trim(), out level) || level < 1 || level > AscensionUnlockSchedule.MAX_CHALLENGE_LEVEL + 1)
            { error = "a challenge level from 1 to 13"; return false; }

            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene != "Ascension_Configure")
            { error = "only from the Kaycee's Mod menu (scene is " + scene + ")"; return false; }

            var data = AscensionSaveData.Data;
            if (data == null) { error = "no Kaycee's Mod save data"; return false; }

            int was = data.challengeLevel;
            data.challengeLevel = level;
            // Session 47 (0.7.454): the menu scene's Awake reloads the save from
            // disk (AscensionMenuScreens.Awake -> SaveManager.LoadFromFile), so a
            // level set only in memory was thrown away by the reload and the
            // verb did nothing. Write it first.
            SaveManager.SaveToFile(saveActiveScene: false);
            Plugin.Log?.LogInfo($"IKMA DEV: challenge level {was} -> {level}; reloading the menu.");
            SceneLoader.Load("Ascension_Configure");
            note = "challenge level " + was + " -> " + level + "; menu reloading";
            return true;
        }

        private static bool UnlockTest(string arg, out string note, out string error)
        {
            note = ""; error = null;
            int level;
            if (!int.TryParse(arg.Trim(), out level) || level < 1 || level > AscensionUnlockSchedule.MAX_CHALLENGE_LEVEL)
            { error = "a challenge level from 1 to 12"; return false; }

            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene != "Ascension_Configure")
            { error = "only from the Kaycee's Mod menu (scene is " + scene + ")"; return false; }

            var data = AscensionSaveData.Data;
            if (data == null) { error = "no Kaycee's Mod save data"; return false; }

            // The menu only runs the unlock screens when the points of the
            // challenges that were on meet the level's requirement
            // (AscensionSaveData.ChallengeLevelIsMet). Switch challenges on,
            // in the game's own order, until they do.
            data.challengeLevel = level;
            if (data.activeChallenges == null) data.activeChallenges = new List<AscensionChallenge>();
            int need = AscensionSaveData.GetChallengePointsForLevel(level);
            for (int c = 1; c < (int)AscensionChallenge.NUM_TYPES && data.GetActiveChallengePoints() < need; c++)
            {
                var ch = (AscensionChallenge)c;
                // Session 47 (0.7.454): the enum has a member with no data object
                // (HarderDeckTrials, 11). GetActiveChallengePoints throws on it,
                // and so does the menu's own post-run check.
                if (AscensionChallengesUtil.GetInfo(ch) == null) continue;
                if (!data.activeChallenges.Contains(ch)) data.activeChallenges.Add(ch);
            }
            int have = data.GetActiveChallengePoints();
            if (have < need) { error = "could not reach " + need + " challenge points (have " + have + ")"; return false; }

            BlockAchievements = true;
            AscensionMenuScreens.ReturningFromSuccessfulRun = true;
            // Session 47 (0.7.454): the menu scene's Awake reloads the save from
            // disk (AscensionMenuScreens.Awake -> SaveManager.LoadFromFile), so a
            // level set only in memory was thrown away by the reload and the
            // verb did nothing. Write it first.
            SaveManager.SaveToFile(saveActiveScene: false);
            Plugin.Log?.LogInfo($"IKMA DEV: unlocktest level {level} - {have} of {need} points; achievements are blocked until the game closes; reloading the menu as a won run.");
            SceneLoader.Load("Ascension_Configure");
            note = "level " + level + ", " + have + " of " + need + " points; menu reloading as a won run";
            return true;
        }
    }

    // Registered by Plugin.TryPatch, dev builds only. No HarmonyPatch attribute.
    public static class AchievementManager_Unlock_DevPatch
    {
        public static bool Prefix(Achievement achievementID)
        {
            if (!DevCheats.BlockAchievements) return true;
            Plugin.Log?.LogInfo($"IKMA DEV: achievement {achievementID} NOT unlocked - an unlock test was run this session.");
            return false;
        }
    }
}

#endif
// DevCheats.cs
