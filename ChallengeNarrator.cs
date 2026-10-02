// ChallengeNarrator.cs
using System.Collections.Generic;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// Four Kaycee's Mod events a sighted player sees and a blind player did
    /// not: the red overlay (both of its entry points), the opponent's totem
    /// in a totem battle, and the player's own woodcarving inscribing a sigil
    /// on a card. All registered through Plugin.TryPatch, outside PatchAll.
    /// </summary>
    /// <remarks>
    /// THE RED OVERLAY. ChallengeActivationUI is the command-line panel that
    /// flashes red for 1.25 seconds. It has TWO entry points in the game and
    /// they are patched separately:
    ///
    ///   TryShowActivation(AscensionChallenge)   13 call sites, one per
    ///       challenge: the Trapper's pelt shop, a candle going out, the
    ///       opening hand, the card choice reroll, the consumables node,
    ///       arriving in a new region, a boss intro, the Grizzly glitch, an
    ///       ordinary battle's setup, the boss reward chest, the run-intro
    ///       deck review, the end of the run intro, and the first turn of a
    ///       battle. The method itself checks that the run is Kaycee's Mod,
    ///       the UI exists and the challenge is ACTIVE before showing
    ///       anything, so the postfix repeats that last check rather than
    ///       speaking for a challenge that is off.
    ///
    ///   ShowTextLines(string[])                 ONE call site in the whole
    ///       game: the Fecundity nerf at the end of
    ///       DrawCopy.OnResolveOnBoard. It bypasses TryShowActivation
    ///       entirely, which is why IKMA said nothing about it for months
    ///       while the challenge patch sat one method away. Sibling entry
    ///       point - the same shape as the boss reward chest ignoring two
    ///       correct patches on CardChoicesSequencer (Session 19).
    ///
    /// BOTH FUNNEL INTO THE PRIVATE ShowLinesSequence, AND THAT IS NOT WHERE
    /// THE PATCH GOES. Patching the choke point would cover both in one
    /// place, but the two need different text - the challenge path keeps
    /// only two of its four lines and the nerf path reads all three - and
    /// the raw lines are all ShowLinesSequence can see. Two entry points,
    /// two patches, no doubling.
    ///
    /// Provenance.Alert on both. Zamar, 0.7.284: the overlay "needs to read
    /// as high prio non-stompable". ONE utterance per overlay, never one per
    /// line: an interrupt flushes the speech pump's whole FIFO, so three
    /// lines sent as three Alerts would leave only the last one audible.
    ///
    /// BOTH opened with "Challenge Activation." (0.7.285), through
    /// Vocabulary.OverlayAnnouncement - one composer, so the two entry points
    /// cannot say it differently. Zamar: the panel arrives "suddenly and
    /// unexpectedly", and the first words out of 0.7.284 were a drive path.
    ///
    /// 0.7.359 — THE CHALLENGE PATH NO LONGER DOES. Zamar, hearing two at one
    /// battle's start: "remove Challenge Activation. Z Drive, Floppy Disk,
    /// Kaycee Mod from these calls, too much." A challenge now reads its two
    /// game lines bare (Vocabulary.ChallengeOverlay). The Fecundity nerf path
    /// is unchanged and asked about, not assumed. And an overlay line no
    /// longer cuts another overlay line, or a result still being read — see
    /// the Alert row in Speech.
    ///
    /// THE OPPONENT TOTEM. TotemOpponent.IntroSequence assembles the
    /// opponent's totem from encounter.opponentTotem: top = the kin
    /// (TotemTopData.prerequisites.tribe), bottom = the sigil
    /// (TotemBottomData.effectParams.ability). The game speaks its own
    /// explanation exactly once per save ("Behold my totem...", gated on
    /// MechanicsConcept.OpponentTotems); after that the totem is silent to a
    /// blind player for the rest of their life. This line queues as a Result
    /// so it lands after the game's own line the first time and stands alone
    /// thereafter. The prefix reads the encounter argument, which is fully
    /// built before the coroutine is created - not state that settles later -
    /// so the coroutine-prefix rule is satisfied.
    ///
    /// THE PLAYER'S WOODCARVING (0.7.284, and the reason this session
    /// existed). CardGainAbility is the TotemTriggerReceiver behind every
    /// woodcarving whose effect is CardGainAbility. It has two trigger
    /// entry points - OnOtherCardDrawn (a card reaches the hand) and
    /// OnOtherCardAssignedToSlot (a card is played to a player slot) - and
    /// both end in the same private AddModToCard. That is where the patch
    /// goes: one patch, both paths, and it fires at the moment the grant
    /// actually happens rather than at the moment the totem decides to try.
    ///
    /// THAT DISTINCTION IS THE WHOLE POINT, because the two come apart:
    ///
    ///   1. RespondsToOtherCardDrawn asks !card.HasAbility(ability).
    ///   2. In Kaycee's Mod, DrawCopy.CardToDrawTempMods gives every copy it
    ///      creates a CardModificationInfo whose negateAbilities contains
    ///      DrawCopy. Permanent, every copy, every run.
    ///   3. PlayableCard.HasAbility returns false whenever ANY temporary mod
    ///      negates the ability, whatever else grants it. So the copy answers
    ///      "no, I do not have Fecundity" and the totem says yes.
    ///   4. AddTemporaryMod then refuses to register the ability, for the
    ///      same reason: it skips TriggerHandler.AddAbility when a negate for
    ///      that ability already exists.
    ///
    /// So the totem plays its sound and animation, the card gains nothing,
    /// HasAbility stays false, and it all happens again the next time that
    /// card is drawn and every time it is played. Zamar hit this in the
    /// 0.7.283 log and called it: "The totem triggered and nothing
    /// happened... I assume this is a unique loop interaction."
    ///
    /// ANNOUNCE WHAT IS TRUE, NOT WHAT WAS ATTEMPTED. The postfix asks
    /// HasAbility AFTER the mod is added and speaks one of two lines from
    /// the answer. A patch written on the totem firing would have said "gains
    /// Fecundity" about a card that gained nothing.
    ///
    /// THE DRAW PATH IS ALREADY SPOKEN, so the success line stays silent
    /// while HotkeyManager.DrawInFlight is set: ComposeDrawLine folds the
    /// grant into "Drew X. It gains Sigil Ability: Y." after a 0.4s settle,
    /// and two announcements of one event is the defect this project keeps
    /// paying for. The FIZZLE line is not gated, because nothing else says
    /// it - and it cannot occur on a pile draw anyway, since only cards
    /// created straight into the hand carry a negate.
    ///
    /// WORDING. TotemGrant and TotemGrantNegated are Zamar's, 0.7.284, to
    /// the letter. The negated line names Fecundity because DrawCopy's negate
    /// is the only one reachable in Kaycee's Mod; any other ability that
    /// fizzles the same way is LOGGED rather than spoken, because no words
    /// exist for it yet and inventing them is not this file's call.
    ///
    /// Vocabulary.OpponentTotem is still PROVISIONAL and every use logs
    /// "IKMA PROVISIONAL" so it can be found in a playtest log.
    /// </remarks>
    public static class ChallengeNarrator
    {
        // ------------------------------------------------------------------
        // The red overlay, entry point 1 of 2: a challenge taking effect.
        // ------------------------------------------------------------------
        internal static void OnChallengeShown(AscensionChallenge challenge)
        {
            try
            {
                if (!SaveFile.IsAscension) return;
                if (AscensionSaveData.Data == null) return;
                if (!AscensionSaveData.Data.ChallengeIsActive(challenge)) return;

                // An internal id is never a display name, so no title means
                // no line rather than a spoken enum.
                string title = null;
                try { title = AscensionChallengesUtil.GetInfo(challenge)?.title; } catch { }
                if (string.IsNullOrEmpty(title))
                {
                    Plugin.Log?.LogInfo($"IKMA OVERLAY: {challenge} activated, no title in data - not spoken.");
                    return;
                }

                // The game translates the title before formatting it in.
                string localisedTitle = title;
                try { localisedTitle = Localization.Translate(title); } catch { }

                // 0.7.359 — no lead-in on a challenge. See the class remarks.
                string line = Vocabulary.ChallengeOverlay(localisedTitle);
                Plugin.Log?.LogInfo($"IKMA OVERLAY: challenge {challenge} - \"{line}\"");
                using (Speech.Event(EventKind.Challenges)) Speech.Alert(line);

                // Session 34 - the challenge's "error buzz" on the controller.
                // Only with the spoken line (Zamar, 0.7.373).
                try { Rumble.ChallengeBuzz(); } catch { }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA OVERLAY: {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // The red overlay, entry point 2 of 2: free text lines. One caller in
        // the game - the Fecundity sigil nerf. Read verbatim, in one
        // utterance, with comment markers spoken as comments.
        // ------------------------------------------------------------------
        internal static void OnOverlayTextLines(string[] lines)
        {
            try
            {
                if (lines == null || lines.Length == 0) return;

                var spoken = new List<string>();
                foreach (var raw in lines)
                {
                    string line = Vocabulary.SpokenOverlayLine(raw);
                    if (!string.IsNullOrEmpty(line)) spoken.Add(line);
                }
                if (spoken.Count == 0) return;

                string text = Vocabulary.OverlayAnnouncement(
                    string.Join(" ", spoken.ToArray()));
                Plugin.Log?.LogInfo($"IKMA OVERLAY: text lines - \"{text}\"");
                using (Speech.Event(EventKind.Challenges)) Speech.Alert(text);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA OVERLAY: {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // The opponent's totem, announced once at the battle intro.
        // ------------------------------------------------------------------
        internal static void OnTotemIntro(EncounterData encounter)
        {
            try
            {
                var totem = encounter?.opponentTotem;
                if (totem == null || totem.top == null || totem.bottom == null) return;

                string kin = totem.top.prerequisites != null
                    ? totem.top.prerequisites.tribe.ToString()
                    : null;
                string sigil = totem.bottom.effectParams != null
                    ? CardReader.GetAbilityName(totem.bottom.effectParams.ability)
                    : null;
                if (string.IsNullOrEmpty(kin) || string.IsNullOrEmpty(sigil))
                {
                    Plugin.Log?.LogInfo("IKMA TOTEM: opponent totem present but kin or sigil unreadable - not spoken.");
                    return;
                }

                string actor = null;
                try { actor = BossNarrator.ActorName(); } catch { }
                if (string.IsNullOrEmpty(actor)) actor = Vocabulary.Leshy;

                using (Speech.Event(EventKind.Challenges)) Speech.Result(Vocabulary.OpponentTotem(actor, kin, sigil));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA TOTEM: {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // The player's woodcarving inscribing its sigil - or failing to.
        //
        // Read the granted ability off the card rather than off the totem's
        // protected Data property: AddTemporaryMod appends, so the mod the
        // game just built is the last fromTotem entry on the card. That is
        // the game's own record of what it just did, asked at the moment it
        // did it.
        // ------------------------------------------------------------------
        private static bool NotOnBoard(PlayableCard card)
        {
            try { return !card.OnBoard; } catch { return false; }
        }

        internal static void OnTotemGrant(PlayableCard card)
        {
            try
            {
                if (card?.Info == null) return;

                var mods = card.TemporaryMods;
                if (mods == null || mods.Count == 0) return;

                CardModificationInfo granted = null;
                for (int i = mods.Count - 1; i >= 0; i--)
                {
                    if (mods[i] != null && mods[i].fromTotem)
                    {
                        granted = mods[i];
                        break;
                    }
                }
                if (granted?.abilities == null || granted.abilities.Count == 0) return;

                string cardName = CardReader.CardName(card.Info);
                if (string.IsNullOrEmpty(cardName)) return;

                foreach (var ability in granted.abilities)
                {
                    string sigil = null;
                    try { sigil = CardReader.GetAbilityName(ability); } catch { }
                    if (string.IsNullOrEmpty(sigil))
                    {
                        Plugin.Log?.LogInfo($"IKMA WOODCARVING: {ability} granted to '{cardName}', no rulebook name - not spoken.");
                        continue;
                    }

                    // The game's own question, asked after the grant.
                    if (card.HasAbility(ability))
                    {
                        if (HotkeyManager.DrawInFlight)
                        {
                            Plugin.Log?.LogInfo($"IKMA WOODCARVING: '{cardName}' gains {sigil} - silent, the draw line carries it.");
                            continue;
                        }

                        Plugin.Log?.LogInfo($"IKMA WOODCARVING: '{cardName}' gains {sigil}.");
                        // 0.7.341 — said AFTER the line announcing the card's
                        // arrival, not before it. See HandFollowUps.
                        if (NotOnBoard(card)) HandFollowUps.Add(cardName, Vocabulary.TotemGrant(cardName, sigil));
                        else                  using (Speech.Event(EventKind.Challenges)) Speech.Quiet(Vocabulary.TotemGrant(cardName, sigil));
                        continue;
                    }

                    // The totem fired and the card gained nothing.
                    if (ability == Ability.DrawCopy)
                    {
                        Plugin.Log?.LogInfo($"IKMA WOODCARVING: '{cardName}' - {sigil} grant negated by the Kaycee's Mod copy nerf.");
                        if (NotOnBoard(card)) HandFollowUps.Add(cardName, Vocabulary.TotemGrantNegated(sigil));
                        else                  using (Speech.Event(EventKind.Challenges)) Speech.Commentary(Vocabulary.TotemGrantNegated(sigil));
                        continue;
                    }

                    // No approved words for any other negated sigil. Log it;
                    // this line is the worklist if it ever appears.
                    Plugin.Log?.LogWarning(
                        $"IKMA WOODCARVING: '{cardName}' - {sigil} grant did not take, and no approved line exists for it. Not spoken.");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA WOODCARVING: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    // Registered by Plugin.TryPatch. No [HarmonyPatch] attribute on purpose:
    // check_source.ps1 CHECK 1 fails a class that carries both.
    public static class ChallengeActivationUI_TryShowActivation_Patch
    {
        public static void Postfix(AscensionChallenge challenge)
            => ChallengeNarrator.OnChallengeShown(challenge);
    }

    public static class ChallengeActivationUI_ShowTextLines_Patch
    {
        public static void Postfix(string[] lines)
            => ChallengeNarrator.OnOverlayTextLines(lines);
    }

    public static class TotemOpponent_IntroSequence_Patch
    {
        public static void Prefix(EncounterData encounter)
            => ChallengeNarrator.OnTotemIntro(encounter);
    }

    public static class CardGainAbility_AddModToCard_Patch
    {
        public static void Postfix(PlayableCard card)
            => ChallengeNarrator.OnTotemGrant(card);
    }
}
