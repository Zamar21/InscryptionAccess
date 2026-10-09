// Plugin.cs
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using DiskCardGame;
using Rewired;

namespace IKMA
{
    [BepInPlugin("com.zamar.ikma", "IKMA - Inscryption Kaycee's Mod Access", "0.4.8.001")]
    public class Plugin : BaseUnityPlugin
    {
        // Static log handle so Harmony patch classes can log diagnostics
        // (used by the Bug 2 hover-scene diagnostic below).
        internal static ManualLogSource Log;

        /// <summary>
        /// The version, read from the BepInPlugin attribute rather than typed
        /// again. Three copies existed and they drifted; now there is one.
        /// </summary>
        // ==================================================================
        // THE VERBOSE SWITCH. (0.7.315.)
        //
        // THE LOG IS PART OF THE MOD. It ships, a playtester attaches it to a
        // bug report, and every line in it is read by somebody. Zamar: "We've
        // gotta keep that log as cleaned up as possible."
        //
        // What stays on: anything that happened on the table, anything IKMA
        // said, and anything that went wrong. What goes behind this: timings,
        // state-machine breadcrumbs, and the running commentary of the
        // dialogue and view layers. None of those describe the game; they
        // describe the mod thinking.
        //
        // OFF BY DEFAULT and flipped in source, not at runtime — there is no
        // setting for it, because a player who can turn it on is a player who
        // can send a log nobody can read.
        // ==================================================================
        internal static bool VerboseDiagnostics = false;

        internal static string PluginVersion
        {
            get
            {
                try
                {
                    // Session 50 (0.7.460) - THE VERSION AS IT IS TYPED.
                    // BepInEx keeps the attribute's version as a
                    // System.Version, and that prints a last number of 000
                    // as 0 and 012 as 12. The Manager, the update files and
                    // the download names all carry the typed text, so the
                    // mod says the typed text too: one build, one name.
                    string typed = TypedVersion();
                    if (typed != null) return typed;

                    var attr = (BepInPlugin)System.Attribute.GetCustomAttribute(
                        typeof(Plugin), typeof(BepInPlugin));
                    return Vocabulary.Mod.VersionOrUnknown(attr?.Version?.ToString());
                }
                catch { return Vocabulary.Mod.UnknownVersion; }
            }
        }

        // The third argument of the BepInPlugin attribute on this class,
        // exactly as written in the source. Read from the attribute's stored
        // constructor arguments (CustomAttributeData), which is the only place
        // the typed text survives. Read once. Null if it cannot be read; the
        // caller then falls back to BepInEx's parsed copy, as before 0.7.460.
        private static string _typedVersion;
        private static bool _typedVersionRead;
        private static string TypedVersion()
        {
            if (_typedVersionRead) return _typedVersion;
            _typedVersionRead = true;
            try
            {
                foreach (var data in System.Reflection.CustomAttributeData.GetCustomAttributes(typeof(Plugin)))
                {
                    if (data.Constructor == null || data.Constructor.DeclaringType != typeof(BepInPlugin)) continue;
                    if (data.ConstructorArguments.Count < 3) continue;
                    string v = data.ConstructorArguments[2].Value as string;
                    if (!string.IsNullOrEmpty(v)) { _typedVersion = v.Trim(); break; }
                }
            }
            catch { _typedVersion = null; }
            return _typedVersion;
        }

        private void Awake()
        {
            Log = Logger;

            // Session 32 - THE LOG TRIM (plan M1), and the filter moved to the
            // very top so it covers every line IKMA writes, the speech
            // startup included. Neither speaks, so nothing is lost by running
            // them before the speech pump. See DiagnosticGate.cs.
            DiagnosticGate.BindConfig(Config);
#if IKMA_DEV
            MouseGate.BindDevConfig(Config);
#endif
            EventSettings.BindConfig(Config);   // Session 37, M9
            ReviewHistory.BindConfig(Config);   // Session 38
            VerboseDiagnostics = DiagnosticGate.ShowAll;
            LogFilter.Init(Logger);
            LogFilter.Install();
            Logger.LogInfo(DiagnosticGate.StartupLine());

            // FIRST, before anything that could speak. The pump owns the only
            // thread that is ever allowed inside UniversalSpeech, so it has to
            // exist before the first line goes out. (Session 15.)
            // Session 32: it also takes Config, to read [Speech] Backend -
            // UniversalSpeech (NVDA) or the Steam Deck's Linux bridge.
            SpeechPump.Init(Logger, Config);

            CardReader.Init(Logger);
            BoardReader.Init(Logger);

            // 0.7.209 — gamepad: config only here; the map load and rumble
            // run from HotkeyManager.Update once Rewired is ready.
            GamepadSupport.Init(Config);
            // Session 32 - IKMA's own read of the free pad buttons (M2
            // plumbing, binds nothing yet) and its press log switch.
            PadInput.BindConfig(Config);
            // Session 34 - [Gamepad] IKMADrivesPad: IKMA reads the pad and
            // maps it onto its keys (Zamar's map). See KeyIn.cs.
            KeyIn.BindConfig(Config);
            // Session 34 - [Gamepad] ButtonNames: which controller's button
            // words IKMA says (Auto from the pad's name). See PadWords.cs.
            PadWords.BindConfig(Config);
            // Session 32 - [Updates] AutoUpdate (on by default, Zamar's call).
            // Config only here; the check starts at the end of Awake.
            AutoUpdate.BindConfig(Config);
            // Session 32 - [Language] Language (default Game = follow the
            // game's own language). See Loc.cs.
            Loc.BindConfig(Config, Logger);
            // (Session 32: LogFilter.Init/Install moved to the top of Awake.
            // It suppresses only individually-vetted engine noise and names
            // each pattern once; it now also hosts DiagnosticGate.)

            CombatAnnouncer.Init(Logger);
            MapReader.Init(Logger);
            MenuReader.Init(Logger);
            RulebookReader.Init(Logger);
            CabinProbe.Init(Logger);
            PauseProbe.Init(Logger);
            PauseMenuReader.Init(Logger);
            OptionsReader.Init(Logger);
            NodeScreenReader.Init(Logger);
            CardChoiceReader.Init(Logger);
            DeckViewReader.Init(Logger);
            RunEndReader.Init(Logger);
            TitleScreenReader.Init(Logger);
            BootScreenReader.Init(Logger);
            CreditsReader.Init(Logger);
            NodeProbe.Init(Logger);
            CabinMap.Init(Logger);

            // Banner FIRST, before PatchAll. Project history: one bad patch
            // parameter name throws inside PatchAll and every patch after it
            // silently fails to register. With the banner ahead of it and a
            // completion line behind it, a broken patch is now one glance in
            // the log — banner present, "PatchAll complete" absent — and the
            // exception message is captured instead of swallowed.
            Logger.LogInfo("==================================================");
            Logger.LogInfo($"==========   IKMA v{PluginVersion} LOADED   ================");
            Logger.LogInfo("==================================================");

            var harmony = new Harmony("com.zamar.ikma");
            try
            {
                harmony.PatchAll();
                Logger.LogInfo("IKMA: PatchAll complete — all patches registered.");
            }
            catch (System.Exception e)
            {
                Logger.LogError("IKMA: PatchAll FAILED — patches after the failure point are NOT active.");
                Logger.LogError($"IKMA: {e}");
            }

            // Session 9: the two newest hooks are patched MANUALLY and
            // individually, outside PatchAll. PatchAll aborts at its first
            // failure, so one bad member name silently disables every patch
            // after it — the exact failure this project has already lost a test
            // cycle to. Patched this way, a mistake here costs only the feature
            // it belongs to and says so in the log.
            // Session 13 — the post-battle card reward, roadmap item 5. All
            // three go through TryPatch rather than PatchAll: they are new
            // patches on freshly dumped members, and two of the three
            // (OnCardFlipped, OnRewardChosen) are NONPUBLIC, so a signature
            // that has moved should cost this one feature and not every patch
            // registered after it.
            // Session 13: the devlog. InitializeWithEntry is PUBLIC and takes
            // the entry data directly (confirmed, dump_km_frontend.txt), so this
            // is the game handing over exactly what it is about to display.
            // Session 34 - the controller map. While IKMA drives the pad the
            // game is told no pad button is down, so it never switches to
            // its console cursor. See KeyIn.cs.
            // Session 52 - the mouse is off in every packaged build. See MouseGate.cs.
            TryPatch(harmony,
                "mouse off (packaged builds)",
                AccessTools.Method(typeof(InteractionCursor), "ManagedUpdate"),
                typeof(InteractionCursor_ManagedUpdate_Patch), "Prefix");

            TryPatch(harmony,
                "controller: keep the game in keyboard mode",
                AccessTools.Method(typeof(InputButtons), "AnyGamepadButton"),
                typeof(InputButtons_AnyGamepadButton_Patch), "Prefix");

            // Session 34 - rumble that follows each voice sound. See
            // Rumble's voice follower in GamepadSupport.cs.
            TryPatch(harmony,
                "controller: rumble follows the voice",
                AccessTools.Method(typeof(TextDisplayer), "PlayVoiceSound"),
                typeof(TextDisplayer_PlayVoiceSound_Patch), "Postfix");

            // Session 34 - rumble on the bell (see Rumble.Bell).
            TryPatch(harmony,
                "controller: rumble on the bell",
                AccessTools.Method(typeof(CombatBell3D), "OnBellPressed"),
                typeof(CombatBell3D_OnBellPressed_Patch), "Postfix");

            // Session 34 - Kaycee's Mod always on the start menu while IKMA
            // is loaded; nothing written to the save. See Kaycees.cs.
            TryPatch(harmony,
                "Kaycee's Mod always available",
                AccessTools.Method(typeof(StoryEventsData), "EventCompleted", new[] { typeof(StoryEvent) }),
                typeof(StoryEventsData_EventCompleted_Patch), "Postfix");

            // Session 34 - his item lines: Scissors cut, Hourglass skip. See
            // ItemUseNarrator.cs.
            TryPatch(harmony,
                "item: scissors cut line",
                AccessTools.Method(typeof(ScissorsItem), "OnValidTargetSelected"),
                typeof(ScissorsItem_OnValidTargetSelected_Patch), "Prefix");
            // (The Hourglass line lives in the existing TurnManager_OpponentTurn_Patch,
            // right after "Enemy turn.".)

            // Session 37 - why a refused item was refused, when Leshy is
            // silent. See ItemRefusal.cs.
            TryPatch(harmony,
                "item: refusal reason",
                AccessTools.Method(typeof(HintsHandler.Hint), "TryPlayDialogue"),
                typeof(HintsHandler_Hint_TryPlayDialogue_Patch), "Prefix");

            // Session 51 (0.7.463) - a card placed with the mouse gets the
            // same "X played in Slot N." a keyboard or pad placement gets.
            // PlayerHand.PlayCardOnSlot is PUBLIC (IEnumerator; PlayableCard,
            // CardSlot), declared on PlayerHand
            // (dumps/dump_s51_from_decompile.txt). See MousePlay.cs.
            TryPatch(harmony,
                "card played without IKMA's keys (mouse)",
                AccessTools.Method(typeof(PlayerHand), "PlayCardOnSlot"),
                typeof(PlayerHand_PlayCardOnSlot_Patch), "Prefix");

            // Session 34 - a rumble pattern per item (see Rumble.Item).
            TryPatch(harmony,
                "controller: rumble on item use",
                AccessTools.Method(typeof(ConsumableItemSlot), "CompleteItemActivation"),
                typeof(ConsumableItemSlot_CompleteItemActivation_Patch), "Prefix");

            TryPatch(harmony,
                "devlog entry",
                AccessTools.Method(typeof(AscensionJournalEntryScreen), "InitializeWithEntry"),
                typeof(AscensionJournalEntryScreen_InitializeWithEntry_Patch), "Postfix");

#if IKMA_DEV
            // Session 46. Dev builds only: the test driver's "unlocktest"
            // must never put an achievement on his Steam account. See
            // DevCheats.cs.
            TryPatch(harmony,
                "dev: no achievements after an unlock test",
                // By name: the class is not public (the method is).
                AccessTools.Method(AccessTools.TypeByName("AchievementManager"), "Unlock"),
                typeof(AchievementManager_Unlock_DevPatch), "Prefix");
#endif

            // THE CABIN SURVEY. (Session 17.) Log only — speaks nothing.
            //
            // Nothing owns the cabin's interactables: CabinManager holds two
            // walls and two puzzle GameObjects and no interactables at all. So
            // the game hands each one over through its own SetEnabled instead,
            // and IKMA never goes looking. See CabinProbe.cs.
            // THE ESCAPE MENU, surveyed. Log only — see PauseProbe.cs.
            // BOTH declarations: PauseMenu3D overrides OnPausedChange, and the
            // 3D menu is the one Act 1 uses.
            TryPatch(harmony,
                "pause menu survey (base)",
                AccessTools.Method(typeof(PauseMenu), "OnPausedChange"),
                typeof(PauseMenu_OnPausedChange_Patch), "Postfix");

            TryPatch(harmony,
                "pause menu survey (PauseMenu3D override)",
                AccessTools.Method(typeof(PauseMenu3D), "OnPausedChange"),
                typeof(PauseMenu_OnPausedChange_Patch), "Postfix");

            TryPatch(harmony,
                "cabin interactable survey",
                AccessTools.Method(typeof(InteractableBase), "SetEnabled"),
                typeof(InteractableBase_SetEnabled_Patch), "Postfix");

            // THE TICK HALF, and it is what makes the number keys work without
            // a mouse. ZoomInteractable and GlobeInteractable each OVERRIDE
            // ManagedUpdate, so both declarations are patched — the base's
            // would not fire for either. See CabinProbe.cs.
            TryPatch(harmony,
                "cabin survey (ZoomInteractable tick)",
                AccessTools.Method(typeof(ZoomInteractable), "ManagedUpdate"),
                typeof(Interactable_ManagedUpdate_Patch), "Postfix");

            TryPatch(harmony,
                "cabin survey (GlobeInteractable tick)",
                AccessTools.Method(typeof(GlobeInteractable), "ManagedUpdate"),
                typeof(Interactable_ManagedUpdate_Patch), "Postfix");

            // THE HOVER HALF OF THE SURVEY. Two declarations, because
            // CursorEnter is declared on InteractableBase and OVERRIDDEN on
            // MainInputInteractable — the base alone would never fire for
            // anything the player can actually point at. Same trap that made
            // the card choice screen dead for six sessions.
            TryPatch(harmony,
                "cabin survey (hover, base)",
                AccessTools.Method(typeof(InteractableBase), "CursorEnter"),
                typeof(InteractableBase_CursorEnter_Patch), "Postfix");

            TryPatch(harmony,
                "cabin survey (hover, MainInputInteractable override)",
                AccessTools.Method(typeof(MainInputInteractable), "CursorEnter"),
                typeof(InteractableBase_CursorEnter_Patch), "Postfix");

            // WHAT CREATED A CARD IN YOUR HAND, and Brood Parasite's egg.
            // (0.7.115.) Both go through TryPatch rather than PatchAll: they
            // are new members, and one wrong name inside PatchAll kills every
            // patch after it silently.
            // OVERKILL AND THE VICTORY LINE. (0.7.120.)
            //
            // VisualizeExcessLethalDamage is registered on BOTH declarations:
            // CombatPhaseManager3D overrides it and is what Act 1 runs, so the
            // base alone would never fire. dump_combat_end.txt's audit is what
            // said so — this is the override trap that cost six sessions once.
            TryPatch(harmony,
                "campfire boost applied (check on the IL read)",
                AccessTools.Method(typeof(CardStatBoostSequencer), "ApplyModToCard"),
                typeof(CardStatBoostSequencer_ApplyModToCard_Patch), "Postfix");

            TryPatch(harmony,
                "campfire stat (CardStatBoostSequencer)",
                AccessTools.Method(typeof(CardStatBoostSequencer), "OnSlotSelected"),
                typeof(CardStatBoostSequencer_OnSlotSelected_Patch), "Prefix");

            TryPatch(harmony,
                "overkill amount",
                AccessTools.Method(typeof(CombatPhaseManager), "DealOverkillDamage"),
                typeof(CombatPhaseManager_DealOverkillDamage_Patch), "Prefix");

            TryPatch(harmony,
                "overkill victim",
                AccessTools.Method(typeof(CombatPhaseManager), "PreOverkillDamage"),
                typeof(CombatPhaseManager_PreOverkillDamage_Patch), "Prefix");

            TryPatch(harmony,
                "victory / excess damage (base)",
                AccessTools.Method(typeof(CombatPhaseManager), "VisualizeExcessLethalDamage"),
                typeof(CombatPhaseManager_VisualizeExcessLethalDamage_Patch), "Prefix");

            TryPatch(harmony,
                "victory / excess damage (CombatPhaseManager3D override)",
                AccessTools.Method(typeof(CombatPhaseManager3D), "VisualizeExcessLethalDamage"),
                typeof(CombatPhaseManager_VisualizeExcessLethalDamage_Patch), "Prefix");

            // BLOOD PROGRESS. (0.7.148.) Both declarations -- BoardManager3D
            // overrides this and is what Act 1 runs.
            TryPatch(harmony,
                "blood progress (base)",
                AccessTools.Method(typeof(BoardManager), "SetSacrificeMarkersValue"),
                typeof(BoardManager_SetSacrificeMarkersValue_Patch), "Postfix");

            TryPatch(harmony,
                "blood progress (BoardManager3D override)",
                AccessTools.Method(typeof(BoardManager3D), "SetSacrificeMarkersValue"),
                typeof(BoardManager_SetSacrificeMarkersValue_Patch), "Postfix");

            TryPatch(harmony,
                "creation cause (DrawCreatedCard)",
                AccessTools.Method(typeof(DrawCreatedCard), "CreateDrawnCard"),
                typeof(DrawCreatedCard_CreateDrawnCard_Patch), "Prefix");

            TryPatch(harmony,
                "brood parasite egg (CreateEgg)",
                AccessTools.Method(typeof(CreateEgg), "OnResolveOnBoard"),
                typeof(CreateEgg_OnResolveOnBoard_Patch), "Prefix");

            // 0.7.305 — THE FIZZLE, 0.7.322 — AND WITHDRAWN. The premise was
            // that StrongNegationEffect means "this sigil achieved nothing".
            // It does not: it is a generic emphasis shake, and Sharp,
            // SwapStats, DrawVesselOnHit and TailOnHit all play it on SUCCESS.
            // His totem battle log has IKMA saying Sharp Quills did nothing and
            // then reading out the damage it dealt. The patch is not registered
            // and the handler class is left in place unreferenced — the full
            // write-up is in SigilTriggers, next to FizzleWatch.

            // 0.7.304 — Corpse Eater. Its line leads with the card that died,
            // which the generic sigil line cannot say. See SigilNarrator.
            TryPatch(harmony,
                "corpse eater (CorpseEater)",
                AccessTools.Method(typeof(CorpseEater), "OnOtherCardDie"),
                typeof(CorpseEater_OnOtherCardDie_Patch), "Prefix");


            // 0.7.316 — THE REST OF M5. Five composers that have been Zamar's
            // approved wording since 0.7.304/305 with nothing calling them.
            // SigilNarrator says what each hook is and why it is that one.
            TryPatch(harmony,
                "dam builder / chime (CreateCardsAdjacent resolve)",
                AccessTools.Method(typeof(CreateCardsAdjacent), "OnResolveOnBoard"),
                typeof(CreateCardsAdjacent_OnResolveOnBoard_Patch), "Prefix");

            TryPatch(harmony,
                "dam builder / chime (CreateCardsAdjacent spawn)",
                AccessTools.Method(typeof(CreateCardsAdjacent), "SpawnCardOnSlot"),
                typeof(CreateCardsAdjacent_SpawnCardOnSlot_Patch), "Prefix");

            TryPatch(harmony,
                "loose tail (TailOnHit)",
                AccessTools.Method(typeof(TailOnHit), "OnCardGettingAttacked"),
                typeof(TailOnHit_OnCardGettingAttacked_Patch), "Prefix");

            TryPatch(harmony,
                "amorphous (RandomAbility)",
                AccessTools.Method(typeof(RandomAbility), "AddMod"),
                typeof(RandomAbility_AddMod_Patch), "Postfix");

            TryPatch(harmony,
                "tidal lock (SquirrelOrbit upkeep)",
                AccessTools.Method(typeof(SquirrelOrbit), "OnUpkeep"),
                typeof(SquirrelOrbit_OnUpkeep_Patch), "Prefix");

            TryPatch(harmony,
                "tidal lock (moon portrait)",
                AccessTools.Method(typeof(MoonAnimatedPortrait), "InstantiateOrbitingObject"),
                typeof(MoonAnimatedPortrait_InstantiateOrbitingObject_Patch), "Prefix");

            TryPatch(harmony,
                "trinket bearer (RandomConsumable)",
                AccessTools.Method(typeof(RandomConsumable), "OnResolveOnBoard"),
                typeof(RandomConsumable_OnResolveOnBoard_Patch), "Prefix");
            TryPatch(harmony,
                "card choice screen (base)",
                AccessTools.Method(typeof(CardChoicesSequencer), "CardSelectionSequence"),
                typeof(CardChoicesSequencer_CardSelectionSequence_Patch), "Prefix");

            // THIS IS WHY THE CARD CHOICE SCREEN NEVER WORKED. (Fixed 0.7.45,
            // from Zamar's 0.7.43 log: he travelled to a Card Choice node, IKMA
            // said "Arrived at Card choice" and then went on reading MAP
            // options, and no IKMA CHOICE line was ever written.)
            //
            // CardSingleChoicesSequencer DECLARES CardSelectionSequence as an
            // OVERRIDE of CardChoicesSequencer's — confirmed in
            // dump_card_choice.txt, which marks it "[OVERRIDE of
            // CardChoicesSequencer]". A Harmony patch on a base declaration does
            // not fire for a subclass override, so the patch above registered
            // cleanly, reported success, and could never run.
            //
            // The same trap as the boss lives in Session 14, and it is now the
            // second time it has cost a feature. The base patch is kept because
            // other sequencers derive from CardChoicesSequencer without
            // overriding; both are needed, neither is redundant.
            TryPatch(harmony,
                "card choice screen (CardSingleChoicesSequencer override)",
                AccessTools.Method(typeof(CardSingleChoicesSequencer), "CardSelectionSequence"),
                typeof(CardChoicesSequencer_CardSelectionSequence_Patch), "Prefix");

            // THE BOSS REWARD CHEST. (0.7.198.)
            //
            // Zamar, having just beaten the Prospector: "The boss rewards
            // screen needs to also be built. It's pretty broken now." His log
            // is worse than unread — IKMA decided the encounter was over, gave
            // the keyboard to the map reader, and read "Boss battle: Prospector"
            // four times while he was looking at a chest.
            //
            // IT IS NOT A DIFFERENT SCREEN AND IT IS NOT AN OVERRIDE TRAP.
            // dumps/dump_rarechoice.txt: RareCardChoicesSequencer derives from
            // CardSingleChoicesSequencer, but it does NOT override
            // CardSelectionSequence. It declares its own public entry point,
            // ChooseRareCard(), and that is the only method it declares besides
            // the chest transform and its generator.
            //
            // So both existing patches applied at load and neither could ever
            // fire. The expected shape — a third override — would have been
            // caught by the base-call audit; this one is a SIBLING entry point,
            // which no override audit finds. Worth remembering: "the patch did
            // not fire" has two causes, and only one of them is an override.
            //
            // Everything downstream already works. RareCardChoicesSequencer IS
            // a CardChoicesSequencer, it fills the same selectableCards list,
            // and CardChoiceReader already reads face-down cards correctly —
            // the chest's cards are SetFaceDown, and the parity rule means the
            // readout is a count and a position, never a name.
            TryPatch(harmony,
                "boss reward chest (RareCardChoicesSequencer)",
                AccessTools.Method(typeof(RareCardChoicesSequencer), "ChooseRareCard"),
                typeof(RareCardChoicesSequencer_ChooseRareCard_Patch), "Prefix");

            TryPatch(harmony,
                "card choice flip",
                AccessTools.Method(typeof(CardSingleChoicesSequencer), "OnCardFlipped"),
                typeof(CardSingleChoicesSequencer_OnCardFlipped_Patch), "Postfix");

            TryPatch(harmony,
                "card choice reward",
                AccessTools.Method(typeof(CardSingleChoicesSequencer), "OnRewardChosen"),
                typeof(CardSingleChoicesSequencer_OnRewardChosen_Patch), "Postfix");

            // THE END OF A CARD CHOICE, FOR EVERY KIND OF ONE. (0.7.247.)
            //
            // OnRewardChosen is the PICK. On a cost or tribe node the game then
            // rolls the card, turns it up and waits for a third click, so the
            // pick is not the end and IKMA may not let go of the keyboard there
            // — see CardChoiceReader.OnChosen for the softlock that caused.
            // AddChosenCardToDeck is the end, on every path through
            // AddCardToDeckAndCleanUp, and it is where the card is really in the
            // deck. PROTECTED, so AccessTools rather than GetMethod.
            TryPatch(harmony,
                "card choice added to deck",
                AccessTools.Method(typeof(CardSingleChoicesSequencer), "AddChosenCardToDeck"),
                typeof(CardSingleChoicesSequencer_AddChosenCardToDeck_Patch), "Postfix");

            // THE SACRIFICE STONE'S RESULT. (0.7.247.)
            //
            // Zamar: "After the ritual is complete I want to hear 'Card name
            // received [x] Sigil(s)'."
            //
            // ModifyHostCard is where the sigils actually change hands —
            // CardMergeSequencer builds a CardModificationInfo from the
            // sacrifice and hands it to playerDeck.ModifyCard. PREFIX, because
            // the count only means anything BEFORE the modification lands: after
            // it, the host already has every ability being asked about.
            TryPatch(harmony,
                "sacrifice stone result",
                AccessTools.Method(typeof(CardMergeSequencer), "ModifyHostCard"),
                typeof(CardMergeSequencer_ModifyHostCard_Patch), "Prefix");

            // THE TOTEM GOING TOGETHER. (0.7.256.)
            //
            // Zamar: "When I took the bees within sigil it auto assembled the
            // totem. This all needs to be read."
            //
            // AutoAssembleTotem fires whenever the backpack holds exactly one
            // head and one body, so the game skips the choosing step entirely
            // and puts the finished totem on its stand. Nothing announced it and
            // nothing said the stand was now waiting to be pressed.
            //
            // PREFIX on AssembleTotem, and it has to be: the method is an
            // iterator, so a postfix would fire at enumerator creation exactly
            // as a prefix does, and the two slots it is handed are the whole
            // answer. Reading them here needs no reflection and no guess about
            // which slot ended up holding what.
            TryPatch(harmony,
                "totem assembled",
                AccessTools.Method(typeof(BuildTotemSequencer), "AssembleTotem"),
                typeof(BuildTotemSequencer_AssembleTotem_Patch), "Prefix");

            // Session 16 — the run end screen. Every Kaycee's Mod run finishes
            // here, win or lose, and until now it was TOTAL SILENCE: the
            // unreadable-screen net needs a live map node to name where the
            // player is standing, and a finished run has torn the map down.
            //
            // Initialize(Boolean victory) is PUBLIC and the override audit in
            // dump_act1_nodes_interaction.txt confirms AscensionRunEndScreen is
            // the only type that declares it with that signature — no second
            // declaration to patch.
            //
            // Postfix, not Prefix: the screen sets isVictory and its title from
            // inside Initialize, so a Prefix would read the previous run's.
            TryPatch(harmony,
                "run end screen",
                AccessTools.Method(typeof(AscensionRunEndScreen), "Initialize"),
                typeof(AscensionRunEndScreen_Initialize_Patch), "Postfix");

            // The death card screen, which a LOSING run always passes through
            // before the run end screen. Not a reader — it hands the
            // unreadable-screen net a name so the screen stops being silent.
            // CreateCardSequence is PUBLIC and undeclared anywhere else.
            TryPatch(harmony,
                "death card screen (unreadable net)",
                AccessTools.Method(typeof(DeathCardCreationSequencer), "CreateCardSequence"),
                typeof(DeathCardCreationSequencer_CreateCardSequence_Patch), "Prefix");

            // Session 32 - naming the death card: IKMA speaks what is typed.
            // EnterNameForCard is PRIVATE (AccessTools finds it anyway) and
            // not overridden anywhere. Postfix wraps the coroutine.
            TryPatch(harmony,
                "death card naming",
                AccessTools.Method(typeof(DeathCardCreationSequencer), "EnterNameForCard"),
                typeof(DeathCardCreationSequencer_EnterNameForCard_Patch), "Postfix");

            // Session 32 - the death card's three choices (cost, stats, sigils).
            // SelectCardFromChoices is PRIVATE and unique by name.
            TryPatch(harmony,
                "death card choices",
                AccessTools.Method(typeof(DeathCardCreationSequencer), "SelectCardFromChoices"),
                typeof(DeathCardCreationSequencer_SelectCardFromChoices_Patch), "Postfix");

            // Session 32 - Act 1 talking cards (Stoat, Stinkbug, Stunted Wolf).
            // Their lines never reach TextDisplayer; TalkingCard.PlayLine is
            // public, non-virtual, and the only place a card's line is shown.
            TryPatch(harmony,
                "talking cards",
                AccessTools.Method(typeof(TalkingCard), "PlayLine"),
                typeof(TalkingCard_PlayLine_Patch), "Prefix");

            // Session 16 — THE NODE SURVEY. Every one of these is log-only;
            // NodeProbe never speaks. They exist because the assembly could not
            // answer where the campfire's two stat slots live (OnSlotSelected
            // proves two exist, no field holds either), and guessing on a screen
            // the game is blocking on is what softlocks a node.
            //
            // All seven confirmed PUBLIC with no override anywhere in the
            // assembly — section 5 of dump_act1_nodes_interaction.txt — so one
            // patch each is correct here, unlike the boss lives and the card
            // choice screen. Individually through TryPatch so a signature that
            // has moved costs one probe and not the survey.
            TryPatch(harmony,
                "node probe (campfire)",
                AccessTools.Method(typeof(CardStatBoostSequencer), "StatBoostSequence"),
                typeof(CardStatBoostSequencer_StatBoostSequence_Patch), "Prefix");

            TryPatch(harmony,
                "node probe (sacrifice stone)",
                AccessTools.Method(typeof(CardMergeSequencer), "MergeSequence"),
                typeof(CardMergeSequencer_MergeSequence_Patch), "Prefix");

            TryPatch(harmony,
                "node probe (mycologists)",
                AccessTools.Method(typeof(DuplicateMergeSequencer), "MergeSequence"),
                typeof(DuplicateMergeSequencer_MergeSequence_Patch), "Prefix");

            TryPatch(harmony,
                "node probe (trader)",
                AccessTools.Method(typeof(TradePeltsSequencer), "TradePelts"),
                typeof(TradePeltsSequencer_TradePelts_Patch), "Prefix");

            TryPatch(harmony,
                "node probe (trapper)",
                AccessTools.Method(typeof(BuyPeltsSequencer), "BuyPelts"),
                typeof(BuyPeltsSequencer_BuyPelts_Patch), "Prefix");

            TryPatch(harmony,
                "node probe (item pickup)",
                AccessTools.Method(typeof(GainConsumablesSequencer), "ReplenishConsumables"),
                typeof(GainConsumablesSequencer_ReplenishConsumables_Patch), "Prefix");

            // 0.7.336 — what Leshy is holding up, before he describes it.
            TryPatch(harmony,
                "item pickup presentation",
                AccessTools.Method(typeof(GainConsumablesSequencer), "LearnItemSequence"),
                typeof(GainConsumablesSequencer_LearnItemSequence_Patch), "Prefix");

            // 0.7.423 - the rat that comes out when the pack is full.
            TryPatch(harmony,
                "item pickup rat (pack full)",
                AccessTools.Method(typeof(GainConsumablesSequencer), "FullConsumablesSequence"),
                typeof(GainConsumablesSequencer_FullConsumablesSequence_Patch), "Postfix");

            // 0.7.339 — choosing a card from the deck mid-battle 
            // Magpie's Glass, the Magpie boon). See DeckPickReader.
            TryPatch(harmony,
                "deck pick (Hoarder, Magpie's Glass, Magpie's Eye boon)",
                AccessTools.Method(typeof(Deck), "ChooseCard"),
                typeof(Deck_ChooseCard_Patch), "Prefix");

            TryPatch(harmony,
                "node probe (totem)",
                AccessTools.Method(typeof(BuildTotemSequencer), "BuildTotem"),
                typeof(BuildTotemSequencer_BuildTotem_Patch), "Prefix");

            // ------------------------------------------------------------------
            // THE LAST FOUR KAYCEE'S MOD NODES. (0.7.214, Session 21.)
            //
            // docs/KM_NODE_SCRIPTS.md scoped these and nothing was built. With
            // them, every node a KM run can generate has a reader.
            //
            // NONE OF THEM NEEDED A NEW READER, which is the whole point of
            // the shapes the earlier sessions settled on. Each screen is one of
            // two existing kinds:
            //
            //   SelectableCard rows  -> CardChoiceReader, which already browses
            //                           them, already speaks a face-down card
            //                           as "face down" and nothing more, and
            //                           already asks the SEQUENCER for the
            //                           count rather than the cards.
            //   Deck slot + confirm  -> NodeProbe/NodeScreenReader, the
            //                           sacrifice-stone shape.
            //
            // All four entry coroutines are PUBLIC on their own class and none
            // is an override, so each is patched where it is declared.
            // ------------------------------------------------------------------

            // Boulder choice. Three identical "Boulder" cards, face UP, and
            // identical to a sighted player too — so there is nothing to hide
            // and CardChoiceReader reads them as it reads any row.
            TryPatch(harmony,
                "boulder choice",
                AccessTools.Method(typeof(BoulderChoiceSequencer), "BoulderChoiceSequence"),
                typeof(BoulderChoiceSequencer_BoulderChoiceSequence_Patch), "Prefix");

            // Deck trial. PARITY IS THE WHOLE RISK HERE and it is already
            // handled: the trial cards are Initialize()d from a TEXTURE, not a
            // CardInfo, so there is no identity on them to leak, and
            // CardChoiceReader's face-down rule covers what is left. IKMA must
            // never read `trialChoices` to name a trial before the game does.
            TryPatch(harmony,
                "deck trial",
                AccessTools.Method(typeof(DeckTrialSequencer), "DeckTrialSequence"),
                typeof(DeckTrialSequencer_DeckTrialSequence_Patch), "Prefix");

            // Session 33 - the Deck Trial never ended the card choice reader.
            // Both are protected virtual on DeckTrialSequencer; AccessTools
            // finds non-public members.
            TryPatch(harmony,
                "deck trial reward added",
                AccessTools.Method(typeof(DeckTrialSequencer), "AddRewardCardToDeck"),
                typeof(DeckTrialSequencer_AddRewardCardToDeck_Patch), "Postfix");
            TryPatch(harmony,
                "deck trial over",
                AccessTools.Method(typeof(DeckTrialSequencer), "ReturnToMap"),
                typeof(DeckTrialSequencer_ReturnToMap_Patch), "Postfix");

            // THE DECK TRIAL'S REWARD CARDS TURNED OVER IN SILENCE. (0.7.430.)
            //
            // Zamar, Session 41: "Flipping these cards face up didnt read what
            // they were. I had to rehover over them to hear it."
            //
            // The flip patch above is on CardSingleChoicesSequencer.OnCardFlipped.
            // The Deck Trial hands its reward cards a different callback,
            // DeckTrialSequencer.OnRewardCardFlipped (protected virtual, one
            // SelectableCard argument, empty body - _gamesource
            // DeckTrialSequencer.cs), so that patch never ran here.
            // FinaleDeckTrialSequencer overrides it without calling the base and
            // answers with Leshy's own dialogue; that override is left alone.
            TryPatch(harmony,
                "deck trial reward flip",
                AccessTools.Method(typeof(DeckTrialSequencer), "OnRewardCardFlipped"),
                typeof(DeckTrialSequencer_OnRewardCardFlipped_Patch), "Postfix");

            // Bone Lord card removal. sacrificeSlot (SelectCardFromDeckSlot) +
            // confirmStone (ConfirmStoneButton) — the sacrifice stone's exact
            // shape, so the node reader drives it unchanged.
            TryPatch(harmony,
                "Bone Lord card removal",
                AccessTools.Method(typeof(CardRemoveSequencer), "RemoveSequence"),
                typeof(CardRemoveSequencer_RemoveSequence_Patch), "Prefix");

            // 0.7.325 — the boon being picked up. PRIVATE, and it is handed the
            // card, so the postfix reads the boon off the game's own argument.
            TryPatch(harmony,
                "bone lord boon taken (OnBoonSelected)",
                AccessTools.Method(typeof(CardRemoveSequencer), "OnBoonSelected"),
                typeof(CardRemoveSequencer_OnBoonSelected_Patch), "Postfix");

            // Copy card, Kaycee's Mod only. Same two parts as the Bone Lord.
            // NOTE it is a plain ManagedBehaviour, NOT a CardChoicesSequencer
            // like the other three — KM_NODE_SCRIPTS.md flagged that, and it is
            // why this one goes to the node reader rather than the choice
            // reader even though it also ends in a card.
            TryPatch(harmony,
                "copy card",
                AccessTools.Method(typeof(CopyCardSequencer), "CopyCardSequence"),
                typeof(CopyCardSequencer_CopyCardSequence_Patch), "Prefix");

            // 0.7.321 — the copy read out when the easels turn. PRIVATE and it
            // returns the CardInfo, so the postfix is handed the finished card
            // rather than going looking for it. See CopyCardNarrator.
            TryPatch(harmony,
                "copy card reveal (CreateCloneCard)",
                AccessTools.Method(typeof(CopyCardSequencer), "CreateCloneCard"),
                typeof(CopyCardSequencer_CreateCloneCard_Patch), "Postfix");

            // Session 19 — ONE patch, and it replaces the two that were here.
            //
            // The Session 14 comment this replaces said Part1BossOpponent "does
            // NOT declare LifeLostSequence". IT DOES, as an override of
            // Opponent's, and dump_prospector.txt shows it. So the patch on
            // Opponent could never fire for ANY Act 1 boss, and the 0.7.120
            // guard (opponent is Part1BossOpponent) then suppressed the only
            // case it COULD still reach — a plain encounter. The announcement
            // has never once fired. No log ever showed it, because a line that
            // does not exist leaves nothing behind.
            //
            // dump_basecalls.txt settled where it belongs by reading the IL:
            //   Part1BossOpponent.LifeLostSequence   NO call to base, and the
            //                                        type declares no <>n__
            //                                        thunk. Patch it directly.
            //   LeshyBossOpponent.LifeLostSequence   calls base through <>n__1,
            //                                        so it REACHES the patch
            //                                        below on its own.
            //
            // Which makes this one patch for all four bosses — and means
            // keeping the old Leshy patch would have spoken his line TWICE.
            TryPatch(harmony,
                "boss life lost",
                AccessTools.Method(typeof(Part1BossOpponent), "LifeLostSequence"),
                typeof(Part1BossOpponent_LifeLostSequence_Patch), "Prefix");

            // The candles ARE the player's lives in Kaycee's Mod, and IKMA has
            // never said a word about losing one. BlowOutCandle is NONPUBLIC and
            // takes the count as its own parameter, named livesRemaining in the
            // assembly — the game's answer rather than an inference.
            TryPatch(harmony,
                "candle blown out",
                AccessTools.Method(typeof(CandleHolder), "BlowOutCandle"),
                typeof(CandleHolder_BlowOutCandle_Patch), "Prefix");

            // A CARD ARRIVING IN THE PLAYER'S HAND, by the route Session 18
            // missed. (0.7.164, corrected 0.7.167.) See
            // BossNarrator.AnnounceCardSpawnedToHand.
            //
            // ONE OVERLOAD, NOT ALL THREE. THIS IS A FIXED BUG, NOT A CHOICE.
            //
            // 0.7.166 patched every overload named SpawnCardToHand, reasoning
            // that patching all of them was safer than guessing which one
            // callers use. Zamar's log:
            //
            //   IKMA HAND: card spawned to hand — 'The Smoke'.
            //   IKMA HAND: card spawned to hand — 'The Smoke'.
            //   IKMA SPEAK: The Smoke is added to your hand.
            //   IKMA SPEAK: The Smoke is added to your hand.
            //
            // The three overloads CHAIN. dump_prospector_il.txt, reading all
            // three state machines:
            //
            //   (CardInfo, Single)                            -> calls the next
            //   (CardInfo, List, Single, Action)              -> calls the next
            //   (CardInfo, List, Vector3, Single, Action)     -> SpawnPlayableCard,
            //                                                    AddTemporaryMod,
            //                                                    PlayerHand.AddCardToHand
            //
            // So one logical spawn crosses two or three patched methods and
            // speaks once per crossing. The last overload is the only one that
            // actually puts a card in a hand, and it is the only one patched.
            //
            // GENERALISE IT: patching every overload of a method is not the
            // safe option, it is the option that multiplies. Find the terminal
            // one — the overload that does the work rather than forwarding —
            // and patch only that. Overload chains are invisible in a member
            // dump and only show up in the IL.
            //
            // SELECTED BY PARAMETER COUNT rather than by naming the signature,
            // because the dump prints the generic arguments only as "List`1"
            // and "Action`1" and writing those out would be a guess. Five
            // parameters identifies it unambiguously among the three, and the
            // count is verified below rather than assumed — if the assembly
            // ever changes shape this says so in the log instead of silently
            // patching nothing or patching two.
            var spawnOverloads = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
            foreach (var m in typeof(CardSpawner).GetMethods(
                         System.Reflection.BindingFlags.Instance |
                         System.Reflection.BindingFlags.Public |
                         System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (m.Name == "SpawnCardToHand" && m.GetParameters().Length == 5)
                    spawnOverloads.Add(m);
            }

            if (spawnOverloads.Count == 1)
            {
                TryPatch(harmony,
                    "card spawned to hand",
                    spawnOverloads[0],
                    typeof(CardSpawner_SpawnCardToHand_Patch), "Prefix");
            }
            else
            {
                // Loud, and deliberately does NOT fall back to patching them
                // all — that fallback is the bug this replaced.
                Log?.LogWarning(
                    "IKMA: expected exactly one 5-parameter CardSpawner.SpawnCardToHand, " +
                    $"found {spawnOverloads.Count}. Cards arriving in hand will not be " +
                    "announced. Re-run dumps/dump_prospector_il.ps1 and re-check the " +
                    "overload chain before changing this.");
            }

            // FLEDGLING AND TRANSFORMER. (0.7.203.) Evolve.OnUpkeep is the one
            // coroutine both use — Transformer derives from Evolve and overrides
            // only GetTransformCardInfo and RespondsToUpkeep, never OnUpkeep
            // (dumps/dump_cagedwolf.txt). Prefix, because the line needs the
            // card's name BEFORE it changes.
            TryPatch(harmony,
                "transform (Evolve / Transformer)",
                AccessTools.Method(typeof(Evolve), "OnUpkeep"),
                typeof(Evolve_OnUpkeep_Patch), "Prefix");

            // 0.7.459 (Session 49) - WHEN each card's change has happened, so
            // two or more of one card evolving in the same upkeep can be said
            // as one line (Zamar, Session 48: "Both enemy Elk Fawn's Fledgling
            // abilities trigger. They each become 2/4 Elks, with Sprinter.").
            // Same method, a Postfix that wraps the enumerator. If this does
            // not apply, every card keeps its own line as before. See
            // SigilNarrator.EvolveBatch. 0.7.460: the Postfix lives in the
            // same class as the Prefix above (one class per patched method,
            // the pre-build doublePatch warning); still its own TryPatch call.
            SigilNarrator.EvolveEndWatched = TryPatch(harmony,
                "transform, several at once (Evolve / Transformer)",
                AccessTools.Method(typeof(Evolve), "OnUpkeep"),
                typeof(Evolve_OnUpkeep_Patch), "Postfix");

            // Session 46 (0.7.453): the Ijiraq dropping its disguise when it
            // is played. Shapeshifter.OnResolveOnBoard is PUBLIC and declared
            // on Shapeshifter (dumps/dump_shapeshifter_from_decompile.txt).
            TryPatch(harmony,
                "Ijiraq reveal",
                AccessTools.Method(typeof(Shapeshifter), "OnResolveOnBoard"),
                typeof(Shapeshifter_OnResolveOnBoard_Patch), "Prefix");

            // Session 47 (0.7.455). Strafe.PostSuccessfulMoveSequence is
            // NONPUBLIC (protected virtual IEnumerator, one CardSlot), declared
            // on Strafe. TradePeltsSequencer.NoPeltsSequence is NONPUBLIC
            // (private IEnumerator, one bool). CurrencyBowl.ShowGain is PUBLIC
            // (IEnumerator; int, bool, bool). All three confirmed in
            // dumps/dump_s47_from_decompile.txt.
            TryPatch(harmony,
                "Long Elk vertebrae",
                AccessTools.Method(typeof(Strafe), "PostSuccessfulMoveSequence"),
                typeof(Strafe_PostSuccessfulMoveSequence_Patch), "Prefix");

            TryPatch(harmony,
                "Trader with no pelts",
                AccessTools.Method(typeof(TradePeltsSequencer), "NoPeltsSequence"),
                typeof(TradePeltsSequencer_NoPeltsSequence_Patch), "Prefix");

            TryPatch(harmony,
                "Trader teeth gift",
                AccessTools.Method(typeof(CurrencyBowl), "ShowGain"),
                typeof(CurrencyBowl_ShowGain_Patch), "Prefix");

            // Session 48 (0.7.458). JerseyDevil.OnSacrifice is PUBLIC
            // (override IEnumerator, no arguments), declared on JerseyDevil.
            // SubmergeSquid.OnResurface is NONPUBLIC (protected override void,
            // no arguments), declared on SubmergeSquid. Both confirmed in
            // dumps/dump_s48_from_decompile.txt.
            TryPatch(harmony,
                "Child 13 sacrificed",
                AccessTools.Method(typeof(JerseyDevil), "OnSacrifice"),
                typeof(JerseyDevil_OnSacrifice_Patch), "Prefix");

            TryPatch(harmony,
                "Great Kraken resurfaces",
                AccessTools.Method(typeof(SubmergeSquid), "OnResurface"),
                typeof(SubmergeSquid_OnResurface_Patch), "Prefix");

            // THE CAGE BREAKS — the Caged Wolf releasing its Wolf. (0.7.196.)
            // IceCube.OnDie is declared on IceCube as an override of
            // TriggerReceiver and nothing derives from it, so this one patch is
            // every cage in the game. Silent in SigilTriggers so the generic
            // line does not fire alongside it.
            TryPatch(harmony,
                "cage broken (IceCube)",
                AccessTools.Method(typeof(IceCube), "OnDie"),
                typeof(IceCube_OnDie_Patch), "Prefix");

            // ONE HOOK, FORTY-SIX SIGILS. (0.7.186.) See SigilTriggers.cs.
            //
            // dumps/dump_callers.txt: 46 of the 84 ability behaviour classes
            // reach AbilityBehaviour.PreSuccessfulTriggerSequence, and every
            // call site is `call`, not `callvirt` — a direct, non-virtual call
            // to the base declaration. One patch therefore covers all 46, and
            // the base-call audit was run BEFORE the patch this time rather
            // than five sessions after it.
            //
            // The declaration count is verified rather than assumed, exactly
            // like SpawnCardToHand above. If this ever finds more than one, the
            // feature stands down loudly instead of patching an overload chain
            // and speaking every line twice.
            var preTriggerDecls = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
            foreach (var m in typeof(AbilityBehaviour).GetMethods(
                         System.Reflection.BindingFlags.Instance |
                         System.Reflection.BindingFlags.Public |
                         System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (m.Name == "PreSuccessfulTriggerSequence")
                    preTriggerDecls.Add(m);
            }

            if (preTriggerDecls.Count == 1)
            {
                TryPatch(harmony,
                    "sigil triggered (all abilities)",
                    preTriggerDecls[0],
                    typeof(AbilityBehaviour_PreSuccessfulTriggerSequence_Patch), "Prefix");
            }
            else
            {
                Log?.LogWarning(
                    "IKMA: expected exactly one AbilityBehaviour.PreSuccessfulTriggerSequence, " +
                    $"found {preTriggerDecls.Count}. Generic sigil narration disabled. " +
                    "Re-run dumps/dump_callers.ps1 before changing this.");
            }

            // THE OTHER WAY THE GAME WAITS ON A PRESS. (0.7.169.) See
            // DialogueAdvancer.NoteShowUntilInput for why PlayingEvent is not
            // enough. Selected by declaring type so GBC.TextBox's identically
            // named method is not caught.
            TryPatch(harmony,
                "ShowUntilInput hold",
                AccessTools.Method(typeof(TextDisplayer), "ShowUntilInput"),
                typeof(TextDisplayer_ShowUntilInput_Patch), "Prefix");

            // THE BOSS PUTS HIS SKULL ON THE TABLE. (0.7.185.)
            //
            // A short cutscene a sighted player watches, and the only thing
            // that establishes what the skull IS.
            //
            // MOVED IN 0.7.188, from a prefix on Part1BossOpponent.IntroSequence
            // to a postfix on BossSkull.Start. The prefix fired at enumerator
            // creation and so led the entire cutscene; the real order, read from
            // IL in dumps/dump_bossintro.txt, is candle, then Smoke, then skull,
            // then dialogue. Start runs the frame the skull is instantiated,
            // which is the beat the game itself marks with boss_skull_appear.
            //
            // Start is NONPUBLIC, which AccessTools finds and a plain GetMethod
            // would not.
            TryPatch(harmony,
                "boss skull placed",
                AccessTools.Method(typeof(BossSkull), "Start"),
                typeof(BossSkull_Start_Patch), "Postfix");

            // GUARDIAN — both halves: it moved, or it tried and could not.
            // (0.7.180.) See SigilNarrator.cs, which also records why this
            // patches GuardDog rather than the shared StrongNegationEffect
            // animation the failing branch plays.
            TryPatch(harmony,
                "guardian triggered",
                AccessTools.Method(typeof(GuardDog), "OnOtherCardResolve"),
                typeof(GuardDog_OnOtherCardResolve_Patch), "Prefix");

            // EVERY STRAFE-FAMILY MOVER, announced when it moves rather than a
            // turn later by the board diff. (0.7.184.) See SigilNarrator.
            TryPatch(harmony,
                "strafe move",
                AccessTools.Method(typeof(Strafe), "OnTurnEnd"),
                typeof(Strafe_OnTurnEnd_Patch), "Prefix");

            TryPatch(harmony,
                "extra candle lit",
                AccessTools.Method(typeof(CandleHolder), "AddExtraCandleSequence"),
                typeof(CandleHolder_AddExtraCandleSequence_Patch), "Prefix");

            // LESHY PUTS THE MASK ON. (0.7.204.) The moment he becomes the
            // boss — every line before it attributes to Leshy. See
            // BossNarrator.ActorName.
            TryPatch(harmony,
                "boss mask on",
                AccessTools.Method(typeof(LeshyAnimationController), "PutOnMask"),
                typeof(LeshyAnimationController_PutOnMask_Patch), "Prefix");

            // THE MASK COMING OFF, ANYWHERE. (0.7.213.) Silent — it only
            // clears the actor state.
            //
            // PutOnMask is not a boss-only event: BuildTotemSequencer
            // (Woodcarver), DuplicateMergeSequencer (Doctor), BuyPeltsSequencer
            // (Trapper), TradePeltsSequencer (Trader) and BoulderChoiceSequencer
            // (Prospector) all call it at a map node with no opponent alive.
            // The mask-on handler latched the actor before checking for a boss,
            // and nothing cleared it, so a totem or Trapper node renamed the
            // NEXT boss from the moment his skull hit the table. No Act 1 boss
            // calls TakeOffMask — only those node screens do — so patching it
            // costs nothing and closes the latch from the other side.
            TryPatch(harmony,
                "mask off (map nodes)",
                AccessTools.Method(typeof(LeshyAnimationController), "TakeOffMask"),
                typeof(LeshyAnimationController_TakeOffMask_Patch), "Prefix");

            // THE MASK FLIPS. (0.7.215.) A DIFFERENT method from PutOnMask,
            // used by exactly one fight: the Trapper becoming the Trader
            // halfway through. docs/COVERAGE_KM.md flagged it and it was real
            // — nothing touched it, so the actor stayed "The Trapper" for the
            // whole of phase two, naming a character who had already left.
            // Same description table, same Mask enum, different sentence.
            // ------------------------------------------------------------------
            // BURROWER. (0.7.225.) PUBLIC IEnumerator
            // WhackAMole.OnSlotTargetedForAttack(CardSlot slot, PlayableCard
            // attacker) — the destination slot arrives as an argument, so the
            // move is nameable before it happens. Ability.WhackAMole is
            // excluded from the generic trigger line in the same build.
            TryPatch(harmony,
                "burrower move",
                AccessTools.Method(typeof(WhackAMole), "OnSlotTargetedForAttack"),
                typeof(WhackAMole_OnSlotTargetedForAttack_Patch), "Prefix");

            // INSCRYPTION'S OWN TITLE SCREEN. (0.7.231.)
            //
            // Zamar wants the non-Kaycee's-Mod title screen navigable for v0.5,
            // with Enter on CONTINUE saying that the main game is still being
            // built rather than opening it.
            //
            // PRIVATE void MenuController.Start() — it does one line of setup
            // and is the earliest point at which the live instance certainly
            // exists. Captured rather than looked up: Singleton<T>.Instance
            // falls back to Object.FindObjectOfType inside a lock when the
            // instance is absent, and polling for it from Update is what killed
            // 0.7.216. TitleScreenReader records which SCENE it was captured
            // in, because this same class runs the pause menus.
            TryPatch(harmony,
                "title screen (MenuController)",
                AccessTools.Method(typeof(MenuController), "Start"),
                typeof(MenuController_Start_Patch), "Postfix");

            // THE FRONT DOOR, AND WHICH GAME IT BELONGS TO. (0.7.233.)
            //
            // PRIVATE void StartScreenController.Start() — prefix, so
            // `startedGame` still reads as the value Start is about to branch
            // on. Hands the controller to BootScreenReader for the title card
            // and answers the Kaycee's-Mod-or-base-game question for Plugin.
            TryPatch(harmony,
                "start screen (StartScreenController)",
                AccessTools.Method(typeof(StartScreenController), "Start"),
                typeof(StartScreenController_Start_Patch), "Prefix");

            // THE FIRST SCREEN OF ALL. (0.7.232.) Zamar: "when you open the
            // game from outside KM's menu, it opens on a small play button. We
            // need this to read and have input."
            //
            // PUBLIC void FirstPlaySceneController.Initialize() — called by
            // StartScreenController.StartSequence when the boot sequence is
            // going to run. Postfix: the panel is up and the button is one
            // second behind it, which BootScreenReader waits for by asking the
            // button rather than by counting.
            TryPatch(harmony,
                "boot screen (FirstPlaySceneController)",
                AccessTools.Method(typeof(FirstPlaySceneController), "Initialize"),
                typeof(FirstPlaySceneController_Initialize_Patch), "Postfix");

            // PUBLIC void FirstPlaySceneController.OnStartButtonPressed() — the
            // button's own onClick target. Patched rather than assumed, so the
            // intro timeline starts from the press whether IKMA pressed it or a
            // mouse did.
            TryPatch(harmony,
                "boot screen intro (OnStartButtonPressed)",
                AccessTools.Method(typeof(FirstPlaySceneController), "OnStartButtonPressed"),
                typeof(FirstPlaySceneController_OnStartButtonPressed_Patch), "Postfix");

            // THE CREDITS ROLL. (0.7.232.) Zamar: "The credits should all read.
            // but not the URLs for the assets."
            //
            // NONPUBLIC void CreditsDisplayer.GenerateCredits() — postfix, the
            // one moment when every credit object exists and none has scrolled.
            // CreditsReader reads the objects, which is what the screen shows,
            // rather than the data behind them.
            TryPatch(harmony,
                "credits roll",
                AccessTools.Method(typeof(CreditsDisplayer), "GenerateCredits"),
                typeof(CreditsDisplayer_GenerateCredits_Patch), "Postfix");

            // THE RUN'S STAT ROWS. (0.7.229.) Postfix, so the PixelText
            // objects hold their final strings when it fires. RunEndReader
            // reads the rendered text rather than rebuilding the numbers from
            // the save, so it reports exactly the rows the screen shows.
            TryPatch(harmony,
                "run end stats screen",
                AccessTools.Method(typeof(AscensionStatsScreen), "FillStatsText"),
                typeof(AscensionStatsScreen_FillStatsText_Patch), "Postfix");

            // THE DEATH FLOW PROBE. (0.7.224.) RunEndProbe.cs — LOG ONLY.
            //
            // Zamar is about to lose a run deliberately so the sequence can be
            // seen once. Losing costs a whole run, so nothing here speaks —
            // every hook records what the game does, in running order, for the
            // next session to build from.
            //
            // Take it out, or put it behind a category flag, once the death
            // flow is narrated. The log ships.
            // ------------------------------------------------------------------

            // PUBLIC IEnumerator Part1GameFlowManager.PlayerLostBattleSequence(Opponent)
            TryPatch(harmony,
                "run end probe: battle lost",
                AccessTools.Method(typeof(Part1GameFlowManager), "PlayerLostBattleSequence"),
                typeof(Part1GameFlowManager_PlayerLostBattleSequence_Patch), "Prefix");

            // PUBLIC IEnumerator CandleHolder.BlowOutCandleSequence(Boolean fromBoss)
            // — the PLAYER's candle, not the boss's.
            TryPatch(harmony,
                "run end probe: player candle",
                AccessTools.Method(typeof(CandleHolder), "BlowOutCandleSequence"),
                typeof(CandleHolder_BlowOutCandleSequence_Patch), "Prefix");

            // The boss's parting line. Two patches for the same reason the
            // surrender pair needs two: Part1BossOpponent DECLARES its own
            // override, so a patch on the base would not fire for it.
            TryPatch(harmony,
                "run end probe: boss gloat (Opponent)",
                AccessTools.Method(typeof(Opponent), "DefeatedPlayerSequence"),
                typeof(Opponent_DefeatedPlayerSequence_Patch), "Prefix");

            TryPatch(harmony,
                "run end probe: boss gloat (Part1BossOpponent)",
                AccessTools.Method(typeof(Part1BossOpponent), "DefeatedPlayerSequence"),
                typeof(Part1BossOpponent_DefeatedPlayerSequence_Patch), "Prefix");

            // NONPUBLIC IEnumerator Part1GameFlowManager.KillPlayerSequence()
            // — the six seconds nothing is spoken over.
            TryPatch(harmony,
                "run end probe: kill player",
                AccessTools.Method(typeof(Part1GameFlowManager), "KillPlayerSequence"),
                typeof(Part1GameFlowManager_KillPlayerSequence_Patch), "Prefix");

            // PUBLIC void AscensionMenuScreens.SwitchToScreen(Screen) — which
            // Kaycee's Mod screens come up after the run, and in what order.
            TryPatch(harmony,
                "run end probe: ascension screens",
                AccessTools.Method(typeof(AscensionMenuScreens), "SwitchToScreen"),
                typeof(AscensionMenuScreens_SwitchToScreen_Patch), "Prefix");

            // THE THIRD CANDLE. (0.7.218.) Leshy's skull arrives with two and
            // grows a third after his line; the skull line used to claim all
            // three at placement, which was one candle too many and a beat too
            // early. EnterHand has exactly one caller in the assembly.
            TryPatch(harmony,
                "boss skull extra candle",
                AccessTools.Method(typeof(BossSkull), "EnterHand"),
                typeof(BossSkull_EnterHand_Patch), "Prefix");

            // THE MASK ACTS. (0.7.218.) Speaks nothing; opens the Prospector's
            // wipe window when it is his mask's turn, which is what was missing
            // for "the Prospector used his pickaxe to kill my cards" — the gate
            // was fixed at 0.7.212 but nothing ever set the expected count in
            // Leshy's fight, so the line stayed silent.
            TryPatch(harmony,
                "Leshy mask activates",
                AccessTools.Method(typeof(LeshyBossOpponent), "ActivateCurrentMask"),
                typeof(LeshyBossOpponent_ActivateCurrentMask_Patch), "Prefix");

            TryPatch(harmony,
                "mask flip (Trapper to Trader)",
                AccessTools.Method(typeof(LeshyAnimationController), "FlipMask"),
                typeof(LeshyAnimationController_FlipMask_Patch), "Prefix");

            // ------------------------------------------------------------------
            // THE REST OF THE ACT 1 BOSSES. (0.7.215, Session 21.)
            // AnglerNarrator.cs, TrapperTraderNarrator.cs. With these and the
            // Leshy/Pirate work, every boss moment in docs/COVERAGE_KM.md has
            // a hook.
            //
            // BOTH PHASE LINES HANG ON THE PRIVATE INNER METHOD, NOT ON
            // StartNewPhaseSequence, and that is deliberate. Each boss's phase
            // override checks HasGrizzlyGlitchPhase FIRST and yield-breaks
            // into the Grizzly sequence with none of its own phase having
            // happened. A line on the phase method would announce bait that
            // was never cast and pelts that never came back — a confidently
            // wrong announcement, which the narration rules call worse than
            // silence. The private methods run only on the path that does the
            // thing they describe.
            // ------------------------------------------------------------------

            // NONPUBLIC IEnumerator AnglerBossOpponent.PlaceBaitSequence().
            // The bucket count is read at enumerator creation, while the board
            // is still intact — the same trick as the Prospector's expected
            // strike count.
            TryPatch(harmony,
                "angler bait phase",
                AccessTools.Method(typeof(AnglerBossOpponent), "PlaceBaitSequence"),
                typeof(AnglerBossOpponent_PlaceBaitSequence_Patch), "Prefix");

            // PUBLIC IEnumerator AnglerBattleSequencer.OnOtherCardDie(...) —
            // a Bait Bucket dying and a Shark taking its slot. LOG ONLY: the
            // Shark arrives through CreateCardInSlot, which the board differ
            // already reports, and a duplicate line is a bug. The log says
            // what the differ cannot reconstruct, and the playtest decides.
            TryPatch(harmony,
                "angler shark (log)",
                AccessTools.Method(typeof(AnglerBattleSequencer), "OnOtherCardDie"),
                typeof(AnglerBattleSequencer_OnOtherCardDie_Patch), "Prefix");

            // NONPUBLIC IEnumerator
            // TrapperTraderBossOpponent.ClearBoardAndReturnPlayedPelts().
            // The pelt count is read at creation for the same reason: by the
            // time the coroutine ends they are in hand and the board is empty.
            TryPatch(harmony,
                "trapper phase 2",
                AccessTools.Method(typeof(TrapperTraderBossOpponent), "ClearBoardAndReturnPlayedPelts"),
                typeof(TrapperTraderBossOpponent_ClearBoardAndReturnPlayedPelts_Patch), "Prefix");

            // NONPUBLIC IEnumerator Part1BossOpponent.GrizzlyGlitchSequence()
            // — the Grizzly Bosses challenge replacing a boss's phase two.
            // Declared on the base, shared by all four Act 1 bosses, so one
            // patch covers every fight it can happen in.
            TryPatch(harmony,
                "grizzly phase",
                AccessTools.Method(typeof(Part1BossOpponent), "GrizzlyGlitchSequence"),
                typeof(Part1BossOpponent_GrizzlyGlitchSequence_Patch), "Prefix");

            // THE POST-BOSS SCRIPT IS RUNNING. (0.7.202.) Opens the latch that
            // keeps the map reader quiet from the boss dying until
            // TurnManager.PostBattleSpecialNode exists — which the IL shows is
            // the LAST thing BossDefeatedSequence does. See
            // HotkeyManager.PostBattleScreenPending.
            TryPatch(harmony,
                "post-boss script latch",
                AccessTools.Method(typeof(Part1BossOpponent), "BossDefeatedSequence"),
                typeof(Part1BossOpponent_BossDefeatedSequence_Patch), "Prefix");

            // THE FLAMES COMING BACK. (0.7.201.) A DIFFERENT EVENT from the
            // extra candle above, and the caller lists in dumps/dump_candle.txt
            // are what say so: AddExtraCandleSequence is called only from
            // <OnTakenToGameTable>d__0 — the boon item that raises the maximum —
            // while ReplenishFlamesSequence is the one that lights activeFlames
            // back up to playerLives, playing "candle_light" on each.
            TryPatch(harmony,
                "candles replenished",
                AccessTools.Method(typeof(CandleHolder), "ReplenishFlamesSequence"),
                typeof(CandleHolder_ReplenishFlamesSequence_Patch), "Prefix");

            // ------------------------------------------------------------------
            // Session 19 — THE PROSPECTOR. Every line these produce is a
            // PLACEHOLDER; see the header of ProspectorNarrator.cs. Zamar asked
            // for provisional wording so he can hear the fight once and then
            // decide what each moment should actually say.
            //
            // All seven go through TryPatch individually. This is the largest
            // block of new patches added in one session, on a boss that cannot
            // be reached on demand, so a single bad signature must cost its own
            // announcement and nothing else.
            //
            // The base-call verdicts behind these targets are in
            // dumps/dump_basecalls.txt — StartNewPhaseSequence in particular
            // MUST be patched on ProspectorBossOpponent, because its override
            // never calls Part1BossOpponent's and a patch there would register
            // cleanly and never fire.
            // ------------------------------------------------------------------
            TryPatch(harmony,
                "prospector phase change",
                AccessTools.Method(typeof(ProspectorBossOpponent), "StartNewPhaseSequence"),
                typeof(ProspectorBossOpponent_StartNewPhaseSequence_Patch), "Prefix");

            TryPatch(harmony,
                "prospector pickaxe strike",
                AccessTools.Method(typeof(PickAxeSlam), "StrikeCardSlot"),
                typeof(PickAxeSlam_StrikeCardSlot_Patch), "Prefix");

            TryPatch(harmony,
                "prospector queue cleared",
                AccessTools.Method(typeof(Opponent), "ClearQueue"),
                typeof(Opponent_ClearQueue_Patch), "Prefix");

            TryPatch(harmony,
                "prospector blueprint replaced",
                AccessTools.Method(typeof(Opponent), "ReplaceBlueprint"),
                typeof(Opponent_ReplaceBlueprint_Patch), "Prefix");

            // The only prefix+postfix pair in the block. ModifyQueuedCard is not
            // a coroutine, so the postfix genuinely runs after the sigil lands.
            TryPatch(harmony,
                "prospector hound buff",
                AccessTools.Method(typeof(ProspectorBossOpponent), "ModifyQueuedCard"),
                typeof(ProspectorBossOpponent_ModifyQueuedCard_Patch), "Prefix");

            TryPatch(harmony,
                "prospector hound buff (result)",
                AccessTools.Method(typeof(ProspectorBossOpponent), "ModifyQueuedCard"),
                typeof(ProspectorBossOpponent_ModifyQueuedCard_Patch), "Postfix");

            TryPatch(harmony,
                "pack mule resolved",
                AccessTools.Method(typeof(PackMule), "OnResolveOnBoard"),
                typeof(PackMule_OnResolveOnBoard_Patch), "Prefix");

            TryPatch(harmony,
                "pack mule pack opened",
                AccessTools.Method(typeof(PackMule), "OnDie"),
                typeof(PackMule_OnDie_Patch), "Prefix");

            // Logged, not spoken — the mask change and the life loss probably
            // coincide, and a duplicate line is a bug. The log settles it.
            TryPatch(harmony,
                "Leshy mask state",
                AccessTools.Method(typeof(LeshyBossOpponent), "AdvanceMaskState"),
                typeof(LeshyBossOpponent_AdvanceMaskState_Patch), "Prefix");

            // ------------------------------------------------------------------
            // THE LESHY FIGHT. (0.7.211, Session 21.) LeshyNarrator.cs.
            //
            // Ten patches, every one through TryPatch and none in PatchAll:
            // six of the targets are NONPUBLIC, this is the first build that
            // has ever narrated the final boss, and a single wrong name inside
            // PatchAll would kill every patch registered after it — which
            // here would mean the rest of the mod, silently, in the fight
            // that matters most.
            //
            // AdvanceMaskState above stays as it is. It still logs the index
            // at enumerator-creation time, which is the only place that
            // ordering question can be answered from a playtest log; the
            // SPOKEN mask events hang on SwitchToMask and CleanUpCurrentMask
            // instead, because those know WHICH mask and whether one is on.
            //
            // Every member here is listed in
            // dumps/dump_leshy_from_decompile.txt with its visibility and the
            // file it was read from.
            // ------------------------------------------------------------------

            // NONPUBLIC IEnumerator SwitchToMask(Int32 index) — the index is an
            // argument, so the mask can be named at enumerator creation with
            // no settle at all. This is the mask going ON.
            TryPatch(harmony,
                "Leshy mask on",
                AccessTools.Method(typeof(LeshyBossOpponent), "SwitchToMask"),
                typeof(LeshyBossOpponent_SwitchToMask_Patch), "Prefix");

            // NONPUBLIC IEnumerator CleanUpCurrentMask() — the mask coming OFF.
            // Also called from StartMoonPhase with no mask on, so the handler
            // repeats the game's own currentMask null check before speaking.
            TryPatch(harmony,
                "Leshy mask off",
                AccessTools.Method(typeof(LeshyBossOpponent), "CleanUpCurrentMask"),
                typeof(LeshyBossOpponent_CleanUpCurrentMask_Patch), "Prefix");

            // NONPUBLIC void InitializeMaskOrbiter() — not a coroutine, so a
            // postfix really does run after SpawnMasks has built the three.
            TryPatch(harmony,
                "Leshy mask orbiter",
                AccessTools.Method(typeof(LeshyBossOpponent), "InitializeMaskOrbiter"),
                typeof(LeshyBossOpponent_InitializeMaskOrbiter_Patch), "Postfix");

            // NONPUBLIC IEnumerator StartNewPhaseSequence() — Leshy's OWN
            // override. It does not call base, exactly as the Prospector's
            // does not, so a patch on Part1BossOpponent would register
            // cleanly and never fire. NumLives already holds the new value.
            TryPatch(harmony,
                "Leshy phase change",
                AccessTools.Method(typeof(LeshyBossOpponent), "StartNewPhaseSequence"),
                typeof(LeshyBossOpponent_StartNewPhaseSequence_Patch), "Prefix");

            // 0.7.448 - NONPUBLIC void StartMoonPhaseAudio(). StartMoonPhase
            // calls it on the line after the "I WONDER..." conversation
            // returns and on the line before the arm plays "takephoto_high".
            // Not a coroutine, so a prefix runs at that moment.
            TryPatch(harmony,
                "Leshy aims the camera",
                AccessTools.Method(typeof(LeshyBossOpponent), "StartMoonPhaseAudio"),
                typeof(LeshyBossOpponent_StartMoonPhaseAudio_Patch), "Prefix");

            // A CARD THAT LIES ACROSS MORE THAN ONE SLOT. (0.7.212.)
            // GiantCardNarrator.cs — shared, because Leshy's moon and the
            // Pirate Skull's ship are the same mechanism. Three patches: the
            // arrival, and each sequencer's own death hook.
            //
            // PUBLIC IEnumerator GiantCard.OnResolveOnBoard() — one card
            // writing itself into every opponent slot. Its line composes
            // deferred, because that fan-out is the body of this coroutine.
            TryPatch(harmony,
                "giant card resolves",
                AccessTools.Method(typeof(GiantCard), "OnResolveOnBoard"),
                typeof(GiantCard_OnResolveOnBoard_Patch), "Prefix");

            // PUBLIC IEnumerator LeshyBattleSequencer.OnOtherCardDie(...) —
            // the sequencer only responds for a card named "moon" killed in
            // combat, so the handler needs no test of its own.
            TryPatch(harmony,
                "moon destroyed",
                AccessTools.Method(typeof(LeshyBattleSequencer), "OnOtherCardDie"),
                typeof(LeshyBattleSequencer_OnOtherCardDie_Patch), "Prefix");

            // ------------------------------------------------------------------
            // THE PIRATE SKULL. (0.7.212.) PirateSkullNarrator.cs.
            //
            // Kaycee's Mod's fifth boss: with AscensionChallenge.FinalBoss on,
            // the last region is Pirateville and its authored layout ends in
            // him INSTEAD of Leshy. A run meets one or the other.
            //
            // Four patches. Two of the targets are NONPUBLIC; all four go
            // through TryPatch for the same reason the Leshy set does.
            // Members are listed in dumps/dump_pirateskull_from_decompile.txt.
            // ------------------------------------------------------------------

            // PUBLIC IEnumerator PirateSkullBattleSequencer.OnOtherCardDie(...)
            // — responds only for Trait.Giant killed in combat, i.e. the ship.
            // Same handler as the moon.
            TryPatch(harmony,
                "pirate ship destroyed",
                AccessTools.Method(typeof(PirateSkullBattleSequencer), "OnOtherCardDie"),
                typeof(PirateSkullBattleSequencer_OnOtherCardDie_Patch), "Prefix");

            // NONPUBLIC IEnumerator ChooseCannonTargetsSequence() — THE line of
            // this fight. Two slots get a crosshair, one his and one YOURS,
            // and the cannons put 10 damage into whatever still stands there
            // on the next upkeep. The crosshair is purely visual and makes no
            // sound, and the player gets exactly one turn to answer it.
            //
            // The targets are picked inside the coroutine, so the handler
            // schedules a delayed read of cannonTargetSlots rather than
            // reading at creation — neither a prefix nor a Harmony postfix
            // sees the body of an iterator method.
            TryPatch(harmony,
                "pirate cannons aimed",
                AccessTools.Method(typeof(PirateSkullBattleSequencer), "ChooseCannonTargetsSequence"),
                typeof(PirateSkullBattleSequencer_ChooseCannonTargets_Patch), "Prefix");

            // NONPUBLIC IEnumerator FireCannonsSequence() — LOG ONLY. The
            // cannon has its own loud sound and PlayableCard.TakeDamage
            // already narrates the hit, so a spoken line here would be a
            // second line for one event. The log still records which targets
            // the game SKIPPED because the card moved or died, which nothing
            // else can reconstruct.
            TryPatch(harmony,
                "pirate cannons fire (log)",
                AccessTools.Method(typeof(PirateSkullBattleSequencer), "FireCannonsSequence"),
                typeof(PirateSkullBattleSequencer_FireCannons_Patch), "Prefix");

            // NONPUBLIC IEnumerator StartNewPhaseSequence() — his OWN override.
            // Reached from Part1BossOpponent.PostResetScalesSequence, not from
            // LifeLostSequence, which he overrides without ever calling base.
            TryPatch(harmony,
                "pirate phase change",
                AccessTools.Method(typeof(PirateSkullBossOpponent), "StartNewPhaseSequence"),
                typeof(PirateSkullBossOpponent_StartNewPhaseSequence_Patch), "Prefix");

            // THE COPY CARD NODE'S ARRIVAL. Two of Zamar's sentences, placed
            // by the game's own dialogue ids rather than by a timer. See
            // CopyCardNarrator for why the position needs both hooks.
            TryPatch(harmony,
                "copy card intro descriptions",
                AccessTools.Method(typeof(TextDisplayer), "PlayDialogueEvent"),
                typeof(TextDisplayer_PlayDialogueEvent_Patch), "Prefix");
            // 0.7.348 — the end of a combat phase closes the giant volley.
            TryPatch(harmony,
                "giant volley (combat phase end)",
                AccessTools.Method(typeof(CombatPhaseManager), "DoCombatPhase"),
                typeof(CombatPhaseManager_DoCombatPhase_Patch), "Postfix");

            // 0.7.357 — both decks empty.
            TryPatch(harmony,
                "draw phase skipped (both decks empty)",
                AccessTools.Method(typeof(CardDrawPiles), "DrawPhaseSequence"),
                typeof(CardDrawPiles_DrawPhaseSequence_Patch), "Prefix");

            // 0.7.352 — the Skinning Knife destroys without Die.
            TryPatch(harmony,
                "skinning knife kill",
                AccessTools.Method(typeof(TrapperKnifeItem), "OnValidTargetSelected"),
                typeof(TrapperKnifeItem_OnValidTargetSelected_Patch), "Prefix");

            // 0.7.349 — every Brittle death in a combat phase, one line.
            TryPatch(harmony,
                "brittle group",
                AccessTools.Method(typeof(Brittle), "OnAttackEnded"),
                typeof(Brittle_OnAttackEnded_Patch), "Prefix");

            // 0.7.348 — skeletons jumping off the pirate ship, one line.
            TryPatch(harmony,
                "pirate ship mutiny",
                AccessTools.Method(typeof(GiantShip), "OnUpkeep"),
                typeof(GiantShip_OnUpkeep_Patch), "Postfix");

            TryPatch(harmony,
                "pirate ship line after the skull's conversation",
                AccessTools.Method(typeof(TextDisplayer), "PlayDialogueEvent"),
                typeof(TextDisplayer_PlayDialogueEvent_Patch), "Postfix");

            // WATERBORNE'S OTHER HALF — the dive. Submerge.OnTurnEnd is a
            // PUBLIC override and runs no trigger sequence, so the generic
            // sigil hook never saw it. See SigilTriggers.Submerge_OnTurnEnd_Patch.
            TryPatch(harmony,
                "waterborne dive",
                AccessTools.Method(typeof(Submerge), "OnTurnEnd"),
                typeof(Submerge_OnTurnEnd_Patch), "Prefix");

            // THE ANGLER'S HOOK. Three patches on the behaviour object, which
            // Leshy's Angler mask and the Angler boss both instantiate — so
            // these serve the Angler fight too when M3 reaches it. The actor
            // in each line is asked of the mask, never of the encounter.
            //
            // NONPUBLIC IEnumerator AimHook(CardSlot slot). The public
            // AimHookAtRandomSlot() picks the slot INSIDE its own body, so a
            // prefix there could not name it; this one takes it as an
            // argument.
            TryPatch(harmony,
                "angler hook aimed",
                AccessTools.Method(typeof(FishHookGrab), "AimHook"),
                typeof(FishHookGrab_AimHook_Patch), "Prefix");

            // PUBLIC IEnumerator PullHook(). Reads hookTargetCard at creation
            // because the body clears it.
            TryPatch(harmony,
                "angler hook pulled",
                AccessTools.Method(typeof(FishHookGrab), "PullHook"),
                typeof(FishHookGrab_PullHook_Patch), "Prefix");

            // PUBLIC IEnumerator CancelHook().
            TryPatch(harmony,
                "angler hook cancelled",
                AccessTools.Method(typeof(FishHookGrab), "CancelHook"),
                typeof(FishHookGrab_CancelHook_Patch), "Prefix");

            // PUBLIC IEnumerator TradeCardsForPelts.TradePhase(...). The FIRST
            // thing it does is disable the bell, and it stays disabled until
            // the trade ends — so this is a Prompt, not a commentary line. A
            // blind player without it has a fight that has stopped answering.
            TryPatch(harmony,
                "trade phase offered",
                AccessTools.Method(typeof(TradeCardsForPelts), "TradePhase"),
                typeof(TradeCardsForPelts_TradePhase_Patch), "Prefix");

            // Session 14 — a card created in your hand, and the autosave.
            // Both PUBLIC and confirmed; both through TryPatch because a wrong
            // signature here should cost the announcement and nothing else.
            TryPatch(harmony,
                "card added to hand",
                AccessTools.Method(typeof(PlayerHand), "AddCardToHand"),
                typeof(PlayerHand_AddCardToHand_Patch), "Prefix");

            // Session 15. Two patches, not one: Part1Opponent DECLARES its own
            // VisualizeOfferSurrender, so a patch on the base would not fire for
            // it. Same trap as the boss lives in Session 14.
            TryPatch(harmony,
                "surrender offer (Opponent)",
                AccessTools.Method(typeof(Opponent), "VisualizeOfferSurrender"),
                typeof(Opponent_VisualizeOfferSurrender_Patch), "Prefix");

            TryPatch(harmony,
                "surrender offer (Part1Opponent)",
                AccessTools.Method(typeof(Part1Opponent), "VisualizeOfferSurrender"),
                typeof(Part1Opponent_VisualizeOfferSurrender_Patch), "Prefix");

            // 0.7.364 — hold the queue through the Grizzly sound.
            TryPatch(harmony,
                "Grizzly sound hold",
                AccessTools.Method(typeof(Tutorial4BattleSequencer), "BearGlitchSequence"),
                typeof(BearGlitchSequence_Patch), "Prefix");

            // 0.7.360 — a new battle starts from a clean board snapshot.
            TryPatch(harmony,
                "battle setup (board snapshot reset)",
                AccessTools.Method(typeof(TurnManager), "SetupPhase"),
                typeof(TurnManager_SetupPhase_Patch), "Prefix");

            // 0.7.431 - "Victory." when a battle is won with no spare damage.
            TryPatch(harmony,
                "battle cleanup (Victory with no excess damage)",
                AccessTools.Method(typeof(TurnManager), "CleanupPhase"),
                typeof(TurnManager_CleanupPhase_Patch), "Prefix");

            // 0.7.360 — the olive branch line, when the offer sequence ends.
            TryPatch(harmony,
                "surrender offer ended (Opponent)",
                AccessTools.Method(typeof(Opponent), "OfferSurrenderSequence"),
                typeof(Opponent_OfferSurrenderSequence_Patch), "Postfix");

            // 0.7.360 — Hoarder with an empty deck. Tutor declares its own
            // RespondsToResolveOnBoard (PUBLIC override, _gamesource Tutor.cs).
            TryPatch(harmony,
                "Hoarder fizzle (Tutor)",
                AccessTools.Method(typeof(Tutor), "RespondsToResolveOnBoard"),
                typeof(Tutor_RespondsToResolveOnBoard_Patch), "Postfix");

            // ==================================================================
            // EVERY ITEM WAS LOGGED TWICE, AND THIS IS THE OVERRIDE TRAP IN ITS
            // MIRROR FORM. (0.7.276.)
            //
            // Session 15 patched the base AND ConsumableItemSlot's override
            // "for the same reason as the pair above" — the reason being that
            // a patch on a base declaration cannot fire for a subclass that
            // overrides it. True in general, and the wrong question here.
            //
            // THE RIGHT QUESTION IS WHETHER THE OVERRIDE CALLS BASE, and this
            // project has a script for it (dumps/dump_basecalls.ps1) precisely
            // because the two answers demand opposite fixes. It was not run.
            // The decompiled source answers it outright — all three subclasses
            // open with base.CreateItem(data, skipDropAnimation):
            //
            //   ConsumableItemSlot.CreateItem  -> base.CreateItem   (line 35)
            //   SelectableItemSlot.CreateItem  -> base.CreateItem   (line 21)
            //   TotemItemSlot.CreateItem       -> base.CreateItem   (line 15)
            //
            // So the base patch already fires for every slot in the game, and
            // the second registration was a duplicate postfix on the same
            // event — six "ITEM CREATE (not spoken)" lines for three items in
            // Zamar's log. It is only the LOG that doubled because this path
            // mostly does not speak; had it spoken, he would have heard every
            // item announced twice.
            //
            // Base only, from here.
            // ==================================================================
            TryPatch(harmony,
                "item created (ItemSlot)",
                AccessTools.Method(typeof(ItemSlot), "CreateItem",
                    new System.Type[] { typeof(ItemData), typeof(bool) }),
                typeof(ItemSlot_CreateItem_Patch), "Postfix");

            TryPatch(harmony,
                "item created by name (ConsumableItemSlot)",
                AccessTools.Method(typeof(ConsumableItemSlot), "CreateItem",
                    new System.Type[] { typeof(string), typeof(bool) }),
                typeof(ConsumableItemSlot_CreateItemByName_Patch), "Postfix");

            TryPatch(harmony,
                "autosave",
                AccessTools.Method(typeof(SaveManager), "SaveToFile"),
                typeof(SaveManager_SaveToFile_Patch), "Prefix");

            TryPatch(harmony,
                "item targeting",
                AccessTools.Method(typeof(BoardManager), "ChooseTarget"),
                typeof(BoardManager_ChooseTarget_Patch), "Prefix");

            TryPatch(harmony,
                "Kaycee's Mod menu",
                AccessTools.Method(typeof(AscensionMenuScreenTransition), "OnEnable"),
                typeof(AscensionMenuScreenTransition_OnEnable_Patch), "Postfix");

            // Session 10: the two confirmed entry points for a scene load, both
            // PUBLIC STATIC per dump_rulebook.txt:
            //   SceneLoader.Load(String sceneName)
            //   LoadingScreenManager.LoadScene(String sceneName)
            // These replace v0.7.10's guess at WHEN a load happens (Unity's
            // sceneUnloaded), which could only ever fire after teardown had
            // already begun. Patched individually so a signature change costs
            // the announcement and nothing else.
            TryPatch(harmony,
                "loading announcement (SceneLoader)",
                AccessTools.Method(typeof(SceneLoader), "Load"),
                typeof(SceneLoader_Load_Patch), "Prefix");

            TryPatch(harmony,
                "loading announcement (LoadingScreenManager)",
                AccessTools.Method(typeof(LoadingScreenManager), "LoadScene"),
                typeof(LoadingScreenManager_LoadScene_Patch), "Prefix");

            // 0.7.208 — the two most frequent silent moments in a Kaycee's Mod
            // run (docs/COVERAGE_KM.md). One static method announces every
            // challenge that bites; one intro assembles every opponent totem.
            // See ChallengeNarrator.cs. Wording provisional.
            TryPatch(harmony,
                "challenge activation line",
                AccessTools.Method(typeof(ChallengeActivationUI), "TryShowActivation"),
                typeof(ChallengeActivationUI_TryShowActivation_Patch), "Postfix");

            // 0.7.284 - the OTHER entry point into the same red overlay, and
            // the one IKMA has been one method away from since 0.7.208.
            // ShowTextLines is called directly, bypassing TryShowActivation;
            // its only caller in the game is the Fecundity sigil nerf at the
            // end of DrawCopy.OnResolveOnBoard. See ChallengeNarrator.
            TryPatch(harmony,
                "overlay text lines",
                AccessTools.Method(typeof(ChallengeActivationUI), "ShowTextLines"),
                typeof(ChallengeActivationUI_ShowTextLines_Patch), "Postfix");

            // 0.7.284 - the player's woodcarving inscribing its sigil.
            // CardGainAbility's two trigger entry points (OnOtherCardDrawn,
            // OnOtherCardAssignedToSlot) both end in the private
            // AddModToCard, so one patch covers both - and it fires where the
            // grant HAPPENS, which is the only place that can tell whether it
            // took. See ChallengeNarrator for why it sometimes does not.
            TryPatch(harmony,
                "woodcarving sigil grant",
                AccessTools.Method(typeof(CardGainAbility), "AddModToCard"),
                typeof(CardGainAbility_AddModToCard_Patch), "Postfix");

            // 0.7.210 — the live-action footage: subtitles spoken as they appear,
            // and the seam for Zamar's audio descriptions. See VideoNarrator.cs.
            TryPatch(harmony,
                "footage subtitles",
                AccessTools.Method(typeof(FootageController), "PrepareClip"),
                typeof(FootageController_PrepareClip_Patch), "Prefix");

            TryPatch(harmony,
                "opponent totem line",
                AccessTools.Method(typeof(TotemOpponent), "IntroSequence"),
                typeof(TotemOpponent_IntroSequence_Patch), "Prefix");

            // 0.7.440 - the map's node manager is handed to MapReader by the
            // game's own "the map is set up" call, so MapAvailable never has
            // to ask the Singleton for a manager that is not there. If this
            // patch does not go on, MapReader keeps asking the old way. See
            // MapReader.NoteManager.
            MapReader.ManagerGateInstalled = TryPatch(harmony,
                "map: node manager known from the game's own call",
                AccessTools.Method(typeof(MapNodeManager), "FindAndSetActiveNodeInteractable"),
                typeof(MapNodeManager_FindAndSetActiveNodeInteractable_Patch), "Postfix");

            // 0.7.443 - the Curious Egg hatching into a Hydra when drawn. Its
            // behaviour never calls PreSuccessfulTriggerSequence, so the one
            // generic sigil hook does not see it. See SigilNarrator.WatchHatch.
            TryPatch(harmony,
                "curious egg hatch line",
                AccessTools.Method(typeof(HydraEgg), "OnDrawn"),
                typeof(HydraEgg_OnDrawn_Patch), "Postfix");

            // 0.7.445 - the Glitched card becoming a random card when drawn.
            // A SpecialCardBehaviour, not a sigil, so no sigil hook sees it.
            // See SigilNarrator.WatchGlitch.
            TryPatch(harmony,
                "glitched card draw line",
                AccessTools.Method(typeof(RandomCard), "OnDrawn"),
                typeof(RandomCard_OnDrawn_Patch), "Postfix");

            // Session 9: any scene change is a transition. Whatever was queued
            // describes a place the player has already left, so drop it rather
            // than let the previous screen narrate over the new one.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;

            var go = new GameObject("IKMA_Managers");
            go.AddComponent<HotkeyManager>();
            go.AddComponent<CombatAnnouncer>();
            GameObject.DontDestroyOnLoad(go);

            // Rewired isn't initialized at Awake() time. Hook its InitializedEvent
            // so we disable the keyboard map as soon as Rewired is ready.
            if (ReInput.isReady)
                DisableGameKeyboard(); // Already ready (shouldn't happen at Awake but handle it)
            else
                ReInput.InitializedEvent += OnRewiredInitialized;

            // Log the game's Button enum once so the cancel-injection name
            // match can be verified from the log ("IKMA BUTTONS:").
            try
            {
                Logger.LogInfo("IKMA BUTTONS: " + string.Join(", ", System.Enum.GetNames(typeof(Button))));
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"IKMA BUTTONS: enum dump failed: {e.Message}");
            }

            // Spoken load confirmation — early "first light" feedback so blind
            // players know the mod is alive before reaching a battle. Queued
            // (interrupt=false) so it doesn't stomp NVDA's own startup speech.
            // (Session 8 request; replaces the old Class1.cs plugin's greeting.)
            // THE VERSION LIVES IN ONE PLACE NOW. (Session 16.)
            //
            // This string was a third hand-written copy of the version, and it
            // was missed on the 0.7.53, 0.7.54 and 0.7.55 bumps — Zamar's
            // 0.7.55 log opens with the mod announcing itself as 0.7.52, which
            // is worse than useless: the first line of every playtest log is how
            // Claude confirms which build is actually running, and it was lying.
            //
            // Read off the BepInPlugin attribute instead, so the attribute is
            // the only place a version number is ever typed.
            Speech.Quiet(Vocabulary.Mod.IkmaVersionLoaded(PluginVersion));

            // Session 32 - auto-update. After the load line, so "Updated from
            // version X" (said only on the start that installed an update)
            // follows it. The GitHub check itself waits 20 seconds and runs
            // as a coroutine; it never holds up loading. See AutoUpdate.cs.
            AutoUpdate.Start(this, Logger);
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                   UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Logger.LogInfo($"IKMA SCENE LOADED: {scene.name}");
            TextDisplayer_ShowMessage_Patch.ResetSpeaker();   // 0.7.356
            CombatAnnouncer.ClearQueue();
            MenuReader.BeginTransition();

            // Session 14: hand browse now moves the game's cursor onto a card,
            // so IKMA holds a PlayableCard reference between keypresses. Across
            // a scene change that reference belongs to a battle that no longer
            // exists. Same reason MenuReader releases its menu hover on a screen
            // change — a hover left standing points at nothing on screen.
            HotkeyManager.ClearHandHover();

            // And the board snapshot with it — it describes a battle that is
            // over. See BoardWatcher.Reset for why a stale one could not
            // produce a false movement line even if this were missed.
            BoardWatcher.Reset();

            // 0.7.262 — and any pending "another narrator owns this cause"
            // registration. A slot registered by a sequence the player walked
            // out of would otherwise silence one correct ability clause in the
            // next battle, which is the harder bug to notice of the two.
            SlotArrival.ResetSuppressions();
            MultiStrikeNarrator.Reset();
            GiantVolley.Reset();
            TextDisplayer_ShowMessage_Patch.ResetSpeaker();   // 0.7.356
            PirateSkullNarrator.Reset();

            // 0.7.211 — and the mask identity with it. A mask name that
            // outlived its encounter would rename the actor in the NEXT
            // fight, which is the false-attribution bug 0.7.206 fixed in the
            // other direction.
            LeshyNarrator.Reset();

            // 0.7.217 — and the trade reader with it. An armed trade that
            // outlived its battle would put a per-frame Singleton lookup back
            // into Update on the map, which is what killed 0.7.216.
            TradeReader.Reset();
            SurrenderReader.Reset();
            DeckViewReader.Reset();
            DialogueAdvancer.Reset();

            // Session 16. The run end screen and the node survey both belong to
            // a context that a scene change has just ended.
            RunEndReader.Reset();
            NodeProbe.Reset();

            // 0.7.231 — the base title screen's MenuController belongs to the
            // scene that just went away, and the same class runs the in-game
            // pause menus. Holding the old reference across a load is how a
            // title-screen reader ends up answering keys in a battle.
            TitleScreenReader.Reset();

            // 0.7.232 — the boot screen and the credits roll are each one
            // scene's worth of state and neither survives it.
            BootScreenReader.Reset();
            CreditsReader.Reset();

            // The cached ViewManager and the last-seen view belong to the scene
            // that just went away. Holding either across a load would report a
            // camera position from a place the player has left.
            MapReader.ResetViewLog();
            RulebookReader.ResetForScene();
            CabinProbe.Reset();
            PauseProbe.Reset();

            // The explicit unreadable context has no end hook of its own — the
            // death card screen does not announce when it is finished. A scene
            // change is the one thing that certainly ends it, and leaving it set
            // would nag the player about a screen they have already left.
            HotkeyManager.ClearExplicitUnreadableContext("scene change");

            // THE TITLE SCREEN NAMES ITSELF. (0.7.43, Zamar's request: "when it
            // first loads into the main menu, and each time they return to the
            // title screen, I want it to read Inscryption: Kaycee's Mod.")
            //
            // AND SINCE 0.7.233 IT NAMES ITSELF FROM ONE SCENE LATER. See
            // OnStartScreenEntered below: the decision needs to know whether
            // this Start scene is a pass-through into Kaycee's Mod or the base
            // game's own front end, and the game answers that in
            // StartScreenController.Start — not at scene load.
            //
            // "Start" is the title scene, confirmed from the 0.7.42 log's own
            // SCENE LOADED lines rather than guessed. It is the one screen with
            // no incoming context to announce itself a moment later, so without
            // this the player arrives somewhere with a name and hears nothing
            // that says which game they are looking at.
            //
            // Delayed rather than immediate: the start screen builds its options
            // after the scene loads, and MenuReader's own entry read follows.
            // This goes in front of it and does not interrupt it.
            // SPOKEN, NOT QUEUED. 0.7.43 log: the line was queued as Info and
            // then cleared by the very next transition — Kaycee's Mod moves off
            // the title screen fast enough that a queued line never reached the
            // front. A title card that only sometimes announces itself is worse
            // than one that does not.
            // (The line itself moved to OnStartScreenEntered at 0.7.233.)

            // The load finished. Nothing else is said here — the incoming context
            // announces itself a moment later (map, menu screen, encounter) and
            // that announcement interrupts, which is exactly the "stomp it when
            // it finishes" Zamar asked for. Saying "Loaded." as well would just
            // be a word in the way.
            //
            // Session 11 fix: the latch is released only when the scene we were
            // actually loading TOWARD arrives. The old version released it on
            // any scene load, and the loading screen is itself a scene named
            // "Loading" — so the sequence went: announce for Part1_Cabin, latch
            // set; "Loading" scene arrives, latch cleared; "Loading" scene
            // unloads, backstop fires, "Loading." spoken a second time. Matching
            // on the recorded target instead of special-casing the name means
            // this holds for any load path, including ones we have not seen.
            if (_loadingTargetScene == null || scene.name == _loadingTargetScene)
            {
                LoadingAnnounced = false;
                _loadingTargetScene = null;
            }
        }

        internal static bool LoadingAnnounced = false;

        // The scene name the announced load is heading for. Recorded at the
        // moment we speak, so the latch knows what "the load finished" means.
        private static string _loadingTargetScene = null;

        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            Logger.LogInfo($"IKMA SCENE UNLOADED: {scene.name}");

            // Session 11: this used to call AnnounceLoading as a backstop, from
            // before SceneLoader.Load and LoadingScreenManager.LoadScene were
            // confirmed. Both fire correctly and in the right order — verified
            // in the Session 11 log — so the backstop now only ever produces a
            // duplicate. A log line that does not correspond to exactly one real
            // event is a bug, not noise; the same goes for a spoken one.
        }

        /// <summary>
        /// Say "Loading." exactly once per load, from whichever hook sees it
        /// first. Nothing announces the END of the load: the incoming context
        /// — map, menu screen, encounter — announces itself and interrupts,
        /// which is the stomp-when-finished behaviour without an extra word in
        /// the way. (Session 10; latch corrected Session 11.)
        /// </summary>
        // When the title card was last spoken, and how long it is protected for.
        // A single speech call has been measured between 12ms and 765ms, so this
        // is not an estimate of how long the line takes — it is a window in
        // which "Loading." has nothing to add.
        private static float _titleSpokenAt = -999f;

        // ------------------------------------------------------------------
        // WHICH FRONT DOOR IS THIS? (0.7.233.)
        //
        // Zamar: "When starting up from the main game's side it should not say
        // anything before the play button line."
        //
        // The Start scene is TWO screens wearing one name. With an ascension
        // save it is a pass-through — StartScreenController.Start loads
        // Ascension_Configure within the second and the player never sees it,
        // which is the case the 0.7.43 title card was written for. With a base
        // game save it is Inscryption's own front end: the play button, the
        // developer parade, the title card. Saying "Inscryption: Kaycee's Mod."
        // over that is naming the wrong game, before the first thing the player
        // is supposed to hear.
        //
        // The game's own two values decide it, read in a PREFIX so `startedGame`
        // is still what Start is about to branch on rather than what it sets.
        // Nothing here guesses: these are the same two reads
        // StartScreenController.Start makes on its next line.
        // ------------------------------------------------------------------
        internal static void OnStartScreenEntered(bool alreadyStarted, bool ascension)
        {
            if (alreadyStarted || !ascension)
            {
                Log?.LogInfo("IKMA TITLE: Start is the base game's own front end " +
                             $"(startedGame={alreadyStarted}, ascension={ascension}) — " +
                             "the Kaycee's Mod title card is not spoken.");
                return;
            }

            // SPOKEN, NOT QUEUED. 0.7.43 log: the line was queued as Info and
            // then cleared by the very next transition — Kaycee's Mod moves off
            // this scene fast enough that a queued line never reached the front.
            // A title card that only sometimes announces itself is worse than
            // one that does not.
            _titleSpokenAt = Time.unscaledTime;
            Log?.LogInfo("IKMA TITLE: Start is a pass-through into Kaycee's Mod — title card spoken.");
            Speech.Browse(Vocabulary.ModTitleCard());
        }
        private const float TITLE_PROTECT_SECONDS = 3f;

        internal static void AnnounceLoading(string via, string targetScene)
        {
            if (LoadingAnnounced) return;
            LoadingAnnounced = true;
            _loadingTargetScene = targetScene;

            Log?.LogInfo($"IKMA LOADING: announcing (via {via}, target {targetScene ?? "unknown"}).");

            // Queued speech describes the world being dismantled — drop it.
            CombatAnnouncer.ClearQueue();

            // THE TITLE CARD IS NOT STOMPED. (0.7.48, Zamar's report.)
            //
            // Kaycee's Mod leaves the title screen within about a second, and
            // "Loading." was cutting "Inscryption: Kaycee's Mod." mid-word every
            // time. Nothing is lost by staying quiet here: the screen being
            // loaded announces itself on arrival, which is what the player
            // actually needs, and "Loading." is a courtesy rather than
            // information.
            //
            // Narrow on purpose — a window measured from the title line only, so
            // every other load still says so.
            if (Time.unscaledTime - _titleSpokenAt < TITLE_PROTECT_SECONDS)
            {
                Log?.LogInfo("IKMA LOADING: not spoken — the title card is still being read.");
                return;
            }

            // 0.7.360 — nor the last candle. See RunEndProbe.KilledAt.
            if (RunEndProbe.DeathJustHappened)
            {
                Log?.LogInfo("IKMA LOADING: not spoken — the run just ended and its last lines are still being read.");
                return;
            }

            Speech.BrowseUnrecorded(Vocabulary.Mod.Loading);   // Session 38: a mod message, not kept in history
        }

        // Apply one patch on its own, logging success or failure by name.
        // Returns true when the patch went on (0.7.440), for the one caller
        // that must fall back to older behaviour when it did not. Every
        // other caller ignores the answer, as before.
        private bool TryPatch(Harmony harmony, string featureName,
                              System.Reflection.MethodBase target,
                              System.Type patchClass, string patchMethodName)
        {
            if (target == null)
            {
                Logger.LogError($"IKMA: {featureName} — target method not found. Feature disabled.");
                return false;
            }

            // Session 13: this was a plain GetMethod(name), which searches
            // PUBLIC members only. Every patch class here happens to declare
            // `public static void Prefix`, so it worked for years — and the one
            // time a new patch class used C#'s default private visibility, the
            // lookup returned null, Harmony threw "Value cannot be null.
            // Parameter name: method", and the error read as though the TARGET
            // method was missing. Three features were reported as disabled for a
            // reason that had nothing to do with the game.
            //
            // Include NonPublic so visibility on the patch method is no longer
            // load-bearing, and name the patch method explicitly when it is the
            // thing that is missing.
            var found = patchClass.GetMethod(
                patchMethodName,
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

            if (found == null)
            {
                Logger.LogError($"IKMA: {featureName} — patch method '{patchMethodName}' not found on {patchClass.Name}. Feature disabled.");
                return false;
            }

            try
            {
                var patchMethod = new HarmonyMethod(found);
                if (patchMethodName == "Prefix")
                    harmony.Patch(target, prefix: patchMethod);
                else
                    harmony.Patch(target, postfix: patchMethod);

                Logger.LogInfo($"IKMA: {featureName} patch applied.");
                return true;
            }
            catch (System.Exception e)
            {
                Logger.LogError($"IKMA: {featureName} patch FAILED — feature disabled. {e.Message}");
                return false;
            }
        }

        private void OnRewiredInitialized()
        {
            ReInput.InitializedEvent -= OnRewiredInitialized;
            DisableGameKeyboard();
        }

        private void DisableGameKeyboard()
        {
            try
            {
                var player = ReInput.players.GetPlayer(0);
                if (player == null)
                {
                    Logger.LogWarning("IKMA: Rewired player 0 not found — keyboard suppression skipped.");
                    return;
                }
                // Disable all keyboard controller maps.
                // We iterate directly rather than using SetMapsEnabled(bool, ControllerType)
                // to avoid version-specific overload mismatches in Rewired.
                var keyboard = player.controllers.Keyboard;
                if (keyboard != null)
                {
                    foreach (var map in player.controllers.maps.GetMaps(keyboard))
                        map.enabled = false;
                    Logger.LogInfo("IKMA: Rewired keyboard maps disabled. Game keyboard controls suppressed.");
                }
                else
                {
                    Logger.LogWarning("IKMA: Rewired keyboard controller not found — keyboard suppression skipped.");
                }
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"IKMA: Could not disable Rewired keyboard map: {e.Message}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Card hover — suppressed during keyboard/gamepad play.
    //
    // BUG 2 DIAGNOSTIC: all of Kaycee's Mod reports scene "Part1_Cabin", so this
    // gate should suppress every hover read — yet reads still leak in combat.
    // That means when the leak happens the active scene is NOT reporting as
    // Part1_Cabin (additive load / active-scene swap). Log the scene name every
    // time a read actually fires so the next leak tells us what to gate on.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(PlayableCard), "OnCursorEnter")]
    public class PlayableCard_OnCursorEnter_Patch
    {
        static void Postfix(PlayableCard __instance)
        {
            // Session 13: IKMA now moves the game's cursor itself when a card
            // is played, so this patch fires from inside the mod for the first
            // time. Its scene gate would suppress the read anyway — but that is
            // two string literals in two files agreeing by coincidence, and a
            // double-read on every play is the exact failure mode we were told
            // to think through first. This says it explicitly instead.
            //
            // It also comes BEFORE the diagnostic below, so IKMA's own calls
            // never appear in the Bug 2 hover-leak log and send us chasing a
            // leak we caused ourselves.
            if (HotkeyManager.SuppressHoverRead)
                return;

            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            // Suppress hover reads during combat — keyboard/gamepad players
            // use comma/period to browse, not mouse hover.
            if (scene == "Part1_Cabin")
                return;

            Plugin.Log?.LogInfo($"IKMA HOVER fired in scene \"{scene}\" — card: {CardReader.CardName(__instance?.Info) ?? "unknown"}");
            CardReader.ReadCard(__instance);
        }
    }

    // -------------------------------------------------------------------------
    // Multi-strike combined announcement. (Session 8.)
    //
    // SlotAttackSequence runs once per attacking slot, before its sub-attacks.
    // PlayableCard.GetOpposingSlots() (confirmed via reflection dump) is the
    // game's own target computation — Trifurcated Strike, Bi-Strike, etc. When
    // a card hits multiple slots, announce one combined line here and suppress
    // the individual per-attack lines that follow. Damage and death
    // announcements still play per hit. Single-target cards are untouched.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(CombatPhaseManager), "SlotAttackSequence")]
    public class CombatPhaseManager_SlotAttackSequence_Patch
    {
        // Number of upcoming SlotAttackSlot announcements to suppress because
        // a combined line already covered them.
        internal static int SuppressCount = 0;

        static void Prefix(CardSlot slot)
        {
            var card = slot?.Card;
            if (card?.Info == null) return;

            // 0.7.348 — against a giant, every strike goes to GiantVolley, one
            // line for the whole phase. The per-strike handler routes them.
            bool attackerIsOpponent = true;
            try { attackerIsOpponent = card.OpponentCard; } catch { }
            if (!attackerIsOpponent && GiantVolley.GiantOn(true)) return;

            System.Collections.Generic.List<CardSlot> targets;
            try { targets = card.GetOpposingSlots(); }
            catch { return; }

            if (targets == null || targets.Count <= 1) return;

            // Session 9: the combined line names each target as if it were hit.
            // If this attacker flies, some or all of those "hits" may sail past
            // into the scales instead. Rather than compose a line that is right
            // for some targets and wrong for others, stand down and let the
            // per-attack handler announce each strike accurately.
            foreach (var t in targets)
            {
                if (BoardReader.LiveCard(t) == null) continue;
                bool flies = false;
                try { flies = card.CanAttackDirectly(t); } catch { }
                if (flies)
                {
                    // 0.7.359 — unless both strikes are the same strike. See
                    // RepeatedDirectLine below.
                    if (RepeatedDirectLine(card, targets)) SuppressCount = targets.Count;
                    return;
                }
            }

            var bm = Singleton<BoardManager>.Instance;
            var parts = new System.Collections.Generic.List<string>();
            foreach (var t in targets)
            {
                if (t == null) continue;
                int idx = -1;
                if (bm != null)
                {
                    idx = bm.OpponentSlotsCopy.IndexOf(t);
                    if (idx < 0) idx = bm.PlayerSlotsCopy.IndexOf(t);
                }
                string slotWord = idx >= 0 ? Vocabulary.Combat.SlotNumber(idx + 1) : Vocabulary.Combat.ASlot;

                // Session 13: LiveCard, not t.Card. A destroyed card still sits
                // in its slot, and this line announced one as a target.
                var occupant = BoardReader.LiveCard(t);
                parts.Add(occupant?.Info != null
                    ? Vocabulary.Combat.CardInSlot(CardReader.CardName(occupant), slotWord)
                    : Vocabulary.Combat.EmptySlot(slotWord));
            }
            if (parts.Count == 0) return;

            string targetList = parts.Count == 1
                ? parts[0]
                : Vocabulary.Combat.TargetListThree(parts.GetRange(0, parts.Count - 1), parts[parts.Count - 1]);
            if (parts.Count == 2)
                targetList = Vocabulary.Combat.TargetListTwo(parts[0], parts[1]);

            // 0.7.264 — the multi-strike attacker names itself here rather than
            // through SideQualifiedName, so the decoration is applied by hand.
            // A Waterborne card cannot strike while it is under, but Submerge is
            // not the only way a card could be face down in a later act and the
            // two lines must not disagree about what to call the same card.
            // 0.7.347 — THE SUMMARY REPLACES THE TARGET LIST. Zamar asked for
            // the multi-strike readout condensed; MultiStrikeNarrator holds one
            // line per attacker and speaks it when the attack has finished.
            // The target list is still logged, since it is how the log shows
            // which slots the game aimed at.
            if (MultiStrikeNarrator.Begin(card, targets.Count))
            {
                Plugin.Log?.LogInfo($"IKMA MULTI: {CardReader.CardName(card)} aims at {targetList}.");
            }
            else
            {
                using (Speech.Event(EventKind.Attacks, EventTag.Side(card))) Speech.Result(
                    Vocabulary.Combat.AttacksTargets(card, CardReader.CardName(card), targetList));
            }
            SuppressCount = targets.Count;
        }

        // The prefix fires at enumerator creation; this wraps the enumerator so
        // the summary knows the moment the game has run the last strike.
        static void Postfix(ref System.Collections.IEnumerator __result)
        {
            __result = MultiStrikeNarrator.Wrap(__result);
        }

        // ==================================================================
        // DOUBLE STRIKE PAST A CARD IS ONE LINE. (0.7.359.)
        //
        // Zamar: "A card with double strike should read all it's damage on one
        // line when possible. 'Cuckoo flies over Broken Egg and attacks
        // directly twice.'" His 0.7.358 log had that sentence twice in a row,
        // and the damage line after it already carries the total.
        //
        // WHEN IT IS POSSIBLE: GetOpposingSlots lists one slot twice (Double
        // Strike, nothing spreading it), a card is in that slot, and the
        // game's own questions say the strike goes straight past it —
        // CanAttackDirectly and not AttackIsBlocked, the ones SlotAttackSlot
        // asks. Going past a card does not touch it, so the second strike meets
        // exactly what the first did and the two lines would be identical. A
        // strike that lands on the card changes what the second one meets, and
        // those keep their own lines (the multi-strike summary, as before).
        // The sentence is the per-strike handler's, with his "twice".
        // ==================================================================
        private static bool RepeatedDirectLine(PlayableCard card, System.Collections.Generic.List<CardSlot> targets)
        {
            try
            {
                if (targets.Count != 2 || !ReferenceEquals(targets[0], targets[1])) return false;

                var slot = targets[0];
                var blocker = BoardReader.LiveCard(slot);
                if (blocker?.Info == null) return false;
                if (!card.CanAttackDirectly(slot) || card.AttackIsBlocked(slot)) return false;

                bool overTheTop = card.HasAbility(Ability.Flying) && !blocker.HasAbility(Ability.Reach);

                string attackerName = DamageDeathMerger.CombatLineName(card, CardReader.CardName(card));
                string targetName   = DamageDeathMerger.CombatLineName(blocker, CardReader.CardName(blocker));

                DamageRecord.NoteAttacker(card);
                Plugin.Log?.LogInfo(
                    $"IKMA COMBAT: '{attackerName}' strikes twice past '{targetName}' — one line for both.");
                using (Speech.Event(EventKind.Attacks, EventTag.Side(card))) Speech.Result(overTheTop
                    ? Vocabulary.Combat.FliesOverAndAttacks(attackerName, targetName, twice: true)
                    : Vocabulary.Combat.PassesByAndAttacks(attackerName, targetName, twice: true));
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning(
                    $"IKMA COMBAT: double strike unreadable — {e.GetType().Name}. Each strike keeps its own line.");
                return false;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Card attacks: "Stoat attacks Wolf." / "Stoat attacks empty Slot 3."
    // Suppressed when a combined multi-strike line already covered this attack.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(CombatPhaseManager), "SlotAttackSlot")]
    public class CombatPhaseManager_SlotAttackSlot_Patch
    {
        static void Prefix(CardSlot attackingSlot, CardSlot opposingSlot)
        {
            if (CombatPhaseManager_SlotAttackSequence_Patch.SuppressCount > 0)
            {
                CombatPhaseManager_SlotAttackSequence_Patch.SuppressCount--;
                return;
            }

            if (attackingSlot?.Card?.Info == null) return;
            string attackerName = DamageDeathMerger.CombatLineName(
                attackingSlot.Card, CardReader.CardName(attackingSlot.Card));

            // Session 13: the DEFENDER goes through LiveCard so a destroyed card
            // cannot be announced as blocking. The attacker does not — it is
            // mid-attack by definition, and a card that dies as it swings should
            // still be named as the one that swung.
            var defender = BoardReader.LiveCard(opposingSlot);

            // 0.7.348 — a strike on a giant is folded into that phase's volley.
            if (GiantVolley.TryNoteStrike(attackingSlot.Card, defender, opposingSlot))
            {
                DamageRecord.NoteAttacker(attackingSlot.Card);
                return;
            }

            if (defender?.Info != null)
            {
                string targetName = DamageDeathMerger.CombatLineName(
                    defender, CardReader.CardName(defender));

                // Session 9 fix: an Airborne attacker flies PAST the card in
                // front of it and hits the scales — announcing "Sparrow attacks
                // Black Goat" told the player their block worked when it did
                // not, which is worse than saying nothing. Ask the game its own
                // question rather than reimplementing sigil logic:
                //   CanAttackDirectly    — flying, and not blocked by Reach
                //   IsFlyingAttackingReach — flying, and Mighty Leap stopped it
                // (Both confirmed on PlayableCard via reflection dump.)
                // Who is swinging, for the death line's "dies to Touch of
                // Death". Taken from the attack IKMA is already announcing,
                // one step before the damage resolves.
                DamageRecord.NoteAttacker(attackingSlot.Card);

                bool fliesPast = false;
                bool blockedInAir = false;
                try { fliesPast    = attackingSlot.Card.CanAttackDirectly(opposingSlot); } catch { }
                try { blockedInAir = attackingSlot.Card.IsFlyingAttackingReach(); }        catch { }

                // ==================================================================
                // "CAN ATTACK DIRECTLY" IS NOT "IS FLYING". (0.7.265.)
                //
                // Zamar: "This was inaccurate my ant didnt have airborne. Should
                // have read 'Worker Ant passes by Submerged Kingfisher and
                // attacks directly.'"
                //
                // The GATE was right and the REASON was invented. Session 9 was
                // correct to ask the game CanAttackDirectly rather than
                // reimplement the rule — but the sentence then asserted the
                // first of the two reasons that question answers to. The game's
                // own body, PlayableCard.CanAttackDirectly:
                //
                //   if (opposingSlot.Card != null &&
                //       (!HasAbility(Flying) || opposingSlot.Card.HasAbility(Reach)))
                //       return opposingSlot.Card.FaceDown;
                //   return true;
                //
                // Flying past a blocker and walking past a card that is under
                // the water are different things and the second one had been
                // wearing the first one's words since Session 9. It only
                // surfaced now because nothing in this fight before the Angler
                // put a submerged card in front of an ordinary attacker.
                //
                // THE PRECEDENCE IS THE GAME'S, NOT A GUESS. Flying with no
                // Reach against it short-circuits to `return true` without the
                // FaceDown test ever running, so a flying attacker over a
                // submerged card is still flying over it. This branch is asked
                // in that same order, off the same object the game asked.
                //
                // Same defect class as "by Ability: Waterborne" two builds ago:
                // a true fact with a cause bolted on that nobody checked.
                // ==================================================================
                bool overTheTop = false;
                try
                {
                    var blocker = opposingSlot.Card;
                    overTheTop = attackingSlot.Card.HasAbility(Ability.Flying)
                                 && (blocker == null || !blocker.HasAbility(Ability.Reach));
                }
                catch { overTheTop = false; }

                if (fliesPast && overTheTop)
                    using (Speech.Event(EventKind.Attacks, EventTag.Side(attackingSlot.IsPlayerSlot))) Speech.Result(Vocabulary.Combat.FliesOverAndAttacks(attackerName, targetName));
                else if (fliesPast)
                {
                    // HIS WORDING. The card in front cannot block — it is face
                    // down, which in Act 1 means it is under the water — so the
                    // attacker simply walks past it.
                    Plugin.Log?.LogInfo(
                        $"IKMA COMBAT: '{attackerName}' attacks directly past '{targetName}' — " +
                        "the blocker cannot block, and the attacker is not Airborne.");
                    using (Speech.Event(EventKind.Attacks, EventTag.Side(attackingSlot.IsPlayerSlot))) Speech.Result(Vocabulary.Combat.PassesByAndAttacks(attackerName, targetName));
                }
                else if (blockedInAir)
                {
                    // 0.7.152, HIS WORDING: "Wolf blocks Airborne Sparrow due to
                    // Mighty Leap." The old line said a block happened and left
                    // the player to work out why; both sigils are the answer and
                    // both are already known here.
                    //
                    // The NAMES come from the game (CardReader.GetAbilityName ->
                    // AbilityInfo.rulebookName), never typed as literals. The
                    // enums are Flying and Reach; the game calls them Airborne
                    // and Mighty Leap, and if it ever renames one, IKMA follows.
                    // GetAbilityName also arms Shift+R for both, so he can ask
                    // what either sigil does straight after hearing the line.
                    string air  = Vocabulary.Combat.Airborne(CardReader.GetAbilityName(Ability.Flying));
                    string leap = Vocabulary.Combat.MightyLeap(CardReader.GetAbilityName(Ability.Reach));
                    using (Speech.Event(EventKind.Attacks, EventTag.Side(attackingSlot.IsPlayerSlot))) Speech.Result(
                        Vocabulary.Combat.BlocksDueTo(targetName, air, attackerName, leap));
                }
                else
                {
                    // 0.7.439 - VANILLA COMBAT IS ONE LINE. Session 43, Zamar:
                    // "Great White attacks Raven, it takes 4 damage and dies."
                    // Only this plain branch with no attack sigil to name;
                    // every other attack shape above is left as it was.
                    string deadlyNote = DamageRecord.DeadlyNote(attackingSlot.Card);
                    if (deadlyNote.Length > 0
                        || !PlainAttack.TryOpen(attackingSlot, opposingSlot.Card, attackerName, targetName))
                    using (Speech.Event(EventKind.Attacks, EventTag.Side(attackingSlot.IsPlayerSlot))) Speech.Result(
                        Vocabulary.Combat.Attacks(attackerName, deadlyNote, targetName));
                }
            }
            else
            {
                // Session 8: name the empty slot being hit instead of just
                // "attacks directly" — resolve the slot index from the game's
                // own slot lists (either side, depending on who attacks).
                int idx = -1;
                var bm = Singleton<BoardManager>.Instance;
                if (bm != null && opposingSlot != null)
                {
                    idx = bm.OpponentSlotsCopy.IndexOf(opposingSlot);
                    if (idx < 0) idx = bm.PlayerSlotsCopy.IndexOf(opposingSlot);
                }
                // 0.7.190 — THE SIGIL IS NAMED ON A DIRECT ATTACK TOO.
                //
                // Zamar, Prospector fight, 2026-09-12:
                //
                //   "I know it doesn't matter here, but I still want creatures
                //    with Touch of Death and Bloodlust, etc to call out their
                //    abilities even on direct attacks. Helps remind the player
                //    how important those creatures are so you don't throw a
                //    bulky Caged Wolf in front of a 1 power ToD Adder just to
                //    stop it from hitting you directly without remembering it
                //    has ToD since it hasn't been announced on its repeated
                //    direct attacks."
                //
                // That is a board-reading argument, not a completeness one. A
                // sighted player sees the sigil on the card every time it
                // swings; a blind player was only told about it on the turns it
                // happened to be blocked. The threat was being forgotten
                // precisely on the turns it was going unanswered.
                //
                // Same DeadlyNote the blocked branch above already uses, so one
                // construction covers both — "X with Touch of Death attacks".
                string swingNote = DamageRecord.DeadlyNote(attackingSlot.Card);

                // 0.7.438 - "ATTACKS EMPTY SLOT" WAS MISLEADING WHEN A BURROWER
                // IS ABOUT TO FILL IT. Session 43, Zamar. The game is asked its
                // own question first; if a Burrower will answer, BurrowBlock
                // holds this line and speaks his one collapsed sentence.
                if (idx < 0 || !BurrowBlock.TryOpen(attackingSlot, opposingSlot, attackerName, swingNote, idx))
                using (Speech.Event(EventKind.Attacks, EventTag.Side(attackingSlot.IsPlayerSlot))) Speech.Result(idx >= 0
                    ? Vocabulary.Combat.AttacksEmptySlot(attackerName, swingNote, idx + 1)
                    : Vocabulary.Combat.AttacksDirectly(attackerName, swingNote));
            }
        }
    }

    // -------------------------------------------------------------------------
    // Damage + death merging. (Session 9 note 2.)
    //
    // The game resolves a lethal hit as TakeDamage -> Die inside one combat
    // step, but the announcer drips at 0.5s, so "Mealworm takes 2 damage." and
    // "Mealworm dies." landed as two lines a beat apart. A sighted player sees
    // one event; a blind player should hear one line.
    //
    // The damage line is queued DEFERRED, carrying a mutable record. If Die
    // fires for the same card before the announcer has spoken that line, the
    // record is amended in place and the death queues nothing of its own. If
    // the card survives, the record composes as a plain damage line. If the
    // damage line was already spoken by the time death lands, the death falls
    // back to its own line. No game state is written — this is bookkeeping on
    // our side only.
    // -------------------------------------------------------------------------
    // -------------------------------------------------------------------------
    // A card arriving in a slot the player did not put it in.
    //
    // Session 13. Corpse Eater plays Corpse Maggots free into the slot of a
    // creature that just died. IKMA said nothing, so the card left the hand and
    // joined the board in silence and the only way to find out was to browse the
    // board later and meet a creature you did not remember playing. Zamar hit
    // this three times and on the third reported it as a suspected bug in the
    // GAME — the clearest possible demonstration of what a silently stale board
    // model costs.
    //
    // Composed at SPEAK time, not at death time: the replacement lands while the
    // death line is still queued, so by the time anyone is listening the slot
    // already holds its new occupant. That is also why this reads as one
    // sentence after the death rather than arriving half a second late.
    // -------------------------------------------------------------------------
    // -------------------------------------------------------------------------
    // Timing instrumentation. (Session 13.)
    //
    // Zamar: arrowing between cards in an encounter hitches, and it feels like
    // something is being loaded on every press. It might be. It might also not
    // be IKMA at all — the rulebook page hitch was chased on exactly that
    // instinct and turned out to be the GAME rebuilding TextMeshPro layout,
    // with IKMA's compose costing 0 to 1ms. Guessing optimised the wrong thing
    // once already on this project, so this measures first.
    //
    // Threshold-gated: silent unless a step is slow enough for a player to
    // feel, so it costs nothing in the normal case and does not turn the log
    // into a stream of numbers. The log is a player-facing artifact.
    //
    // Stopwatch timestamps rather than DateTime — monotonic, and no allocation.
    // -------------------------------------------------------------------------
    internal static class Perf
    {
        // ~half a frame at 60fps. Below this nobody feels anything.
        private const double WARN_MS = 8.0;

        internal static long Now() => System.Diagnostics.Stopwatch.GetTimestamp();

        // ======================================================================
        // THE HITCH WATCH. (0.7.338.)
        //
        // Zamar, Session 26: "Noticed some performance hitches after leaving
        // the game running." The 0.7.337 log could not answer it: Report()
        // above is behind VerboseDiagnostics, so IKMA's frame timing was never
        // printed, and nothing measured the frame as a whole.
        //
        // This is NOT behind the flag, because a felt hitch is news rather
        // than routine: it logs only a frame over HITCH_MS, at most one line
        // every HITCH_GAP_S seconds, and counts what it held back. Each line
        // says whether IKMA's input work was the cost and whether .NET's
        // garbage collector ran in that frame — which splits "IKMA did it"
        // from "the game or the collector did it" in one read.
        //
        // Time.unscaledDeltaTime in frame N is the length of frame N-1, so it
        // is compared against the IKMA time and GC count saved from N-1.
        // Scene loads produce long frames by nature; the SCENE LOADED lines
        // around a hitch say when that is the reason.
        // ======================================================================
        private const float HITCH_MS    = 150f;
        private const float HITCH_GAP_S = 5f;
        private static double _prevIkmaMs;
        private static int    _prevGc = -1;
        private static float  _lastHitchLineAt = -999f;
        private static int    _hitchesHeldBack;
        private static bool   _skipNextFrame;

        internal static void NoteFocusLost() => _skipNextFrame = true;

        /// <summary>Called once per frame from HotkeyManager.Update with the
        /// time IKMA's input handling took in THIS frame.</summary>
        internal static void NoteFrame(double ikmaMsThisFrame)
        {
            float frameMs = Time.unscaledDeltaTime * 1000f;
            int gcNow = System.GC.CollectionCount(0);

            // 0.7.339 — HIS 0.7.338 LOG: 43, 22 and 52 SECOND "HITCHES". Those
            // were the game standing still while he was in another window
            // writing notes, not a stall. The frame after focus comes back is
            // skipped, and so is anything the game spent unfocused.
            bool focused = true;
            try { focused = Application.isFocused; } catch { }
            if (_skipNextFrame || !focused)
            {
                _skipNextFrame = !focused;
                _prevGc = gcNow;
                _prevIkmaMs = ikmaMsThisFrame;
                return;
            }
            bool gcRan = _prevGc >= 0 && gcNow != _prevGc;
            double ikmaMs = _prevIkmaMs;

            _prevGc = gcNow;
            _prevIkmaMs = ikmaMsThisFrame;

            if (frameMs < HITCH_MS) return;

            if (Time.unscaledTime - _lastHitchLineAt < HITCH_GAP_S)
            {
                _hitchesHeldBack++;
                return;
            }
            _lastHitchLineAt = Time.unscaledTime;

            string held = _hitchesHeldBack > 0 ? $" {_hitchesHeldBack} more since the last line." : "";
            _hitchesHeldBack = 0;
            Plugin.Log?.LogInfo(
                $"IKMA HITCH: a frame took {frameMs:F0}ms. IKMA input work {ikmaMs:F1}ms. " +
                $"Garbage collection {(gcRan ? "ran" : "did not run")}. " +
                $"Up {Time.realtimeSinceStartup / 60f:F0} min.{held}");
        }

        internal static double MsSince(long start)
            => (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0
               / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>Log only if this step was slow enough to be felt.</summary>
        internal static void Report(string what, long start)
        {
            // 0.7.315 — BEHIND THE VERBOSE FLAG. Thirty-six of these in one
            // 1,176-line log, and every one is a measurement rather than
            // something that happened on the table. SAPI routinely takes more
            // than the 250ms floor, so the line is normal rather than news.
            // Turn Plugin.VerboseDiagnostics on to get them back when the
            // question is "why does this feel slow".
            if (!Plugin.VerboseDiagnostics) return;

            double ms = MsSince(start);
            if (ms < WARN_MS) return;
            Plugin.Log?.LogInfo($"IKMA PERF: {what} took {ms:F1}ms.");
        }

        /// <summary>
        /// Two-part report for a path with a compose half and a speak half —
        /// which is the whole question here. If compose is fast and speak is
        /// slow, the cost is the TTS call reaching NVDA and no amount of
        /// tidying IKMA's string building will change it.
        /// </summary>
        internal static void ReportSplit(string what, double composeMs, double speakMs)
        {
            if (composeMs + speakMs < WARN_MS) return;
            Plugin.Log?.LogInfo(
                $"IKMA PERF: {what} — compose {composeMs:F1}ms, speak {speakMs:F1}ms.");
        }
    }

    // -------------------------------------------------------------------------
    // What was in the player's hand a moment ago. (Session 14.)
    //
    // SlotArrival composes at speak time, by which point a card that was played
    // out of the hand is on the board and the hand no longer remembers it. So
    // the hand is snapshotted when a death is registered — the instant before
    // any replacement can arrive — and the arrival line checks against that.
    //
    // Reference identity over a handful of entries, and a short validity window,
    // for the same reason DamageDeathMerger uses lists rather than a Dictionary:
    // these are Unity objects that may be destroyed between insert and lookup.
    // -------------------------------------------------------------------------
    internal static class HandSnapshot
    {
        private static readonly System.Collections.Generic.List<PlayableCard> _cards
            = new System.Collections.Generic.List<PlayableCard>();
        private static float _takenAt = -999f;
        private const float VALID_SECONDS = 6f;

        internal static void Take()
        {
            _cards.Clear();
            _takenAt = Time.unscaledTime;
            try
            {
                var hand = Singleton<PlayerHand>.Instance?.CardsInHand;
                if (hand == null) return;
                for (int i = 0; i < hand.Count; i++)
                    if (hand[i] != null) _cards.Add(hand[i]);
            }
            catch { /* no snapshot is a quieter line, never a broken one */ }
        }

        internal static bool Contains(PlayableCard card)
        {
            if (card == null) return false;
            if (Time.unscaledTime - _takenAt > VALID_SECONDS) return false;
            for (int i = 0; i < _cards.Count; i++)
                if (ReferenceEquals(_cards[i], card)) return true;
            return false;
        }
    }

    /// <summary>
    /// Counts placements IKMA has made, so a slot prompt raised before one can
    /// tell that it is stale. See the slot prompt for why the game's own
    /// ChoosingSlotCard is not enough on its own.
    /// </summary>
    internal static class SlotPromptState
    {
        internal static int Generation;

        /// <summary>Called the moment a placement is injected.</summary>
        internal static void NotePlaced() => Generation++;

        // Session 36, Zamar: once the player has arrowed onto a slot, that
        // browse line already says "Choose a slot to play X", so the full
        // prompt still queued behind a sacrifice is withdrawn.
        internal static int Browsed;
        internal static void NoteBrowsed() => Browsed++;
    }

    internal static class SlotArrival
    {
        // ------------------------------------------------------------------
        // WHEN SOMETHING ELSE IS KNOWN TO HAVE CAUSED THE ARRIVAL. (0.7.262.)
        //
        // The "by Ability: X" clause below is an INFERENCE — it names the
        // arriving card's own printed ability and assumes that ability is why
        // the card is there. That is true for Corpse Eater and false for a
        // card another actor put on the board, and 0.7.261 produced exactly
        // the predicted wrong line in Zamar's Angler fight:
        //
        //   "Great White is played in Slot 2 by Ability: Waterborne."
        //
        // Waterborne is how the Great White fights, not how it arrived. The
        // Angler's own sequencer created it.
        //
        // A narrator that is patched onto the thing DOING the creating knows
        // the real cause, so it registers the slot here and the guess is
        // dropped for that one arrival. This is the project's standing rule
        // applied to a cause instead of to a count: read the game's own branch
        // condition at the moment it branches, rather than re-deriving an
        // answer from the world afterwards.
        //
        // Slot-keyed, not a bare flag: two bait buckets can die in one combat,
        // and both prefixes run before either death line composes. Entries are
        // consumed on use, and the list is cleared with the board so a slot
        // registered by a sequence that never completed cannot silence a clause
        // a turn later.
        // ------------------------------------------------------------------
        private static readonly System.Collections.Generic.List<CardSlot> _causeKnown =
            new System.Collections.Generic.List<CardSlot>();

        /// <summary>
        /// Declare that the arrival about to land in <paramref name="slot"/> was
        /// caused by something other than the card itself, so its printed
        /// ability must not be named as the reason.
        /// </summary>
        internal static void SuppressInferredCause(CardSlot slot)
        {
            if (slot == null) return;
            for (int i = 0; i < _causeKnown.Count; i++)
                if (ReferenceEquals(_causeKnown[i], slot)) return;
            _causeKnown.Add(slot);
        }

        /// <summary>Drops every pending suppression. Called when a battle ends.</summary>
        internal static void ResetSuppressions() { _causeKnown.Clear(); _arrivalOwned.Clear(); _arrivalOwnedAt.Clear(); }

        // 0.7.438 - THE WHOLE ARRIVAL CLAUSE BELONGS TO ANOTHER NARRATOR.
        // Session 43, Zamar: "The Corpse Maggot getting played callout happened
        // twice. The first one was wrong and should be removed, the second one
        // was correct." The first was this clause on the death line ("Corpse
        // Maggots is played from your hand in Slot 3 by Ability: Corpse
        // Eater."); the second is SigilNarrator.NoteCorpseEater's own line.
        // Timed, so a claim nobody collected cannot swallow a later arrival.
        private static readonly System.Collections.Generic.List<CardSlot> _arrivalOwned =
            new System.Collections.Generic.List<CardSlot>();
        private static readonly System.Collections.Generic.List<float> _arrivalOwnedAt =
            new System.Collections.Generic.List<float>();
        private const float ARRIVAL_OWNED_SECONDS = 8f;

        internal static void SuppressArrival(CardSlot slot)
        {
            if (slot == null) return;
            _arrivalOwned.Add(slot);
            _arrivalOwnedAt.Add(UnityEngine.Time.unscaledTime);
        }

        private static bool TakeArrivalSuppression(CardSlot slot)
        {
            if (slot == null) return false;
            for (int i = _arrivalOwned.Count - 1; i >= 0; i--)
            {
                bool stale = UnityEngine.Time.unscaledTime - _arrivalOwnedAt[i] > ARRIVAL_OWNED_SECONDS;
                bool match = ReferenceEquals(_arrivalOwned[i], slot);
                if (!stale && !match) continue;
                _arrivalOwned.RemoveAt(i);
                _arrivalOwnedAt.RemoveAt(i);
                if (match && !stale) return true;
            }
            return false;
        }

        private static bool TakeSuppression(CardSlot slot)
        {
            if (slot == null) return false;
            for (int i = 0; i < _causeKnown.Count; i++)
                if (ReferenceEquals(_causeKnown[i], slot))
                {
                    _causeKnown.RemoveAt(i);
                    return true;
                }
            return false;
        }

        /// <summary>
        /// A sentence describing whatever now occupies <paramref name="slot"/>,
        /// or null if nothing new is there.
        /// </summary>
        // ------------------------------------------------------------------
        // A QUEUED CARD MOVING DOWN IS NOT CAUSED BY THE DEATH. (0.7.347.)
        //
        // Zamar's 0.7.346 Pirate log, after a long multi-strike combat:
        //
        //   "Mole Seaman takes 1 damage and dies. Skeleton Crew is played in
        //    Slot 3 by Ability: Brittle."
        //
        // False twice. The Skeleton Crew came down from the QUEUE at the start
        // of the enemy turn, and Brittle is how it dies, not how it arrived.
        // The death line was composed so late — behind a long readout — that
        // the queue had already advanced into the dead card's slot by the time
        // this looked. A card that was sitting in Opponent.Queue when the hit
        // or death happened cannot have been put there by that death, so it
        // is left for the board diff, which reports queue arrivals correctly.
        // ------------------------------------------------------------------
        internal static System.Collections.Generic.List<PlayableCard> QueueSnapshot()
        {
            var list = new System.Collections.Generic.List<PlayableCard>();
            try
            {
                var q = TurnManager.Instance?.Opponent?.Queue;
                if (q != null) list.AddRange(q);
            }
            catch { }
            return list;
        }

        internal static string Describe(CardSlot slot, PlayableCard previous,
            System.Collections.Generic.List<PlayableCard> queuedBefore = null)
        {
            if (slot == null) return null;

            var arrival = BoardReader.LiveCard(slot);
            if (arrival?.Info == null) return null;

            // The slot still holds the card we were talking about — it has not
            // finished leaving yet. Nothing arrived.
            if (ReferenceEquals(arrival, previous)) return null;

            if (TakeArrivalSuppression(slot))
            {
                Plugin.Log?.LogInfo(
                    $"IKMA ARRIVAL: '{CardReader.CardName(arrival)}' not added to the death line — " +
                    "another narrator speaks its arrival.");
                BoardWatcher.NoteAnnounced(arrival);
                return null;
            }

            if (queuedBefore != null)
            {
                for (int i = 0; i < queuedBefore.Count; i++)
                {
                    if (!ReferenceEquals(queuedBefore[i], arrival)) continue;
                    Plugin.Log?.LogInfo(
                        $"IKMA ARRIVAL: '{CardReader.CardName(arrival)}' came from the queue, " +
                        "not from this death — left to the board diff.");
                    return null;
                }
            }

            string name = CardReader.CardName(arrival);

            string where = "";
            var bm = Singleton<BoardManager>.Instance;
            if (bm != null)
            {
                int idx = bm.PlayerSlotsCopy.IndexOf(slot);
                if (idx < 0) idx = bm.OpponentSlotsCopy.IndexOf(slot);
                if (idx >= 0) where = Vocabulary.Combat.InSlotNumber(idx + 1);
            }

            // Zamar's call: name the arriving card's own ability as the cause.
            // Claude's preference was to name a cause only when the game itself
            // reported the trigger, and the risk of doing it this way is on
            // record — this is IKMA inferring why, and it will be wrong if the
            // arrival was caused by something other than the card that arrived.
            //
            // The one place his rule does not decide for us is a card with more
            // than one printed ability: there is no single "the ability" to
            // name, and picking one would be inventing the cause rather than
            // inferring it. Those announce the arrival with no cause attached.
            //
            // 0.7.262 — AND THE RISK ON RECORD ABOVE CAME TRUE, so a narrator
            // that knows the real cause can now say so and the guess is
            // dropped for that arrival. See SuppressInferredCause.
            string cause = null;
            bool causeIsKnownElsewhere = TakeSuppression(slot);

            // Session 46 (0.7.452) - STARVATION. His 0.7.450 log, both decks
            // empty: "River Otter dies. Starvation is played in Slot 3 by
            // Ability: Repulsive." The same false clause as the Angler's
            // shark (0.7.262): Repulsive is how the card fights, not why it
            // is there. CardDrawPiles.ExhaustedSequence puts it there, by
            // this name, and when the enemy row is full it kills a random
            // enemy card first to make room - so the death half of that
            // line is true and stays. His ruling for the shark applies:
            // "Just remove 'by ability waterborne'. No need to explain why."
            try { if (arrival.Info.name == "Starvation") causeIsKnownElsewhere = true; } catch { }
            try
            {
                var abilities = arrival.Info?.Abilities;
                if (!causeIsKnownElsewhere && abilities != null && abilities.Count == 1)
                    cause = CardReader.GetAbilityName(abilities[0]);
            }
            catch { cause = null; }

            if (causeIsKnownElsewhere)
                Plugin.Log?.LogInfo(
                    $"IKMA ARRIVAL: '{name}' announced with no cause — another narrator " +
                    "owns the reason it is here.");

            // This line is about to be spoken, so the diff watcher must not
            // report the same arrival again at the next turn boundary. A
            // duplicate line is a bug by this project's own standard.
            BoardWatcher.NoteAnnounced(arrival);

            // Session 14, Zamar's wording: "Corpse Maggots is played from your
            // hand into Slot 3 by Ability: Corpse Eater."
            //
            // "from your hand" is EVIDENCE, not decoration. It is spoken only if
            // this card was actually in hand a moment ago — HandSnapshot records
            // that at death time, before the arrival lands. Corpse Eater plays
            // from hand and will say so; Frozen Away releases a creature that was
            // never in hand and will not, which is the case a blanket phrase
            // would have got wrong every time.
            string from = HandSnapshot.Contains(arrival) ? Vocabulary.Combat.FromYourHand : "";

            // "by Ability: X" rather than "by X", matching the label a card read
            // uses for the same information.
            return Vocabulary.Combat.PlayedLine(cause, name, from, where);
        }
    }

    internal class DamageRecord
    {
        internal string Name;
        internal int Damage;
        internal bool Died;
        internal PlayableCard Card;

        /// <summary>Who dealt the hit, when one was being announced. (0.7.175.)</summary>
        internal PlayableCard Killer;

        // ------------------------------------------------------------------
        // TOUCH OF DEATH, NAMED WHEREVER IT ACTS. (0.7.175.)
        //
        // Zamar: "Anytime a creature with Touch of Death attacks, that sigil
        // should be announced with it", and on the death: "Caged Wolf takes 2
        // damage and dies to Touch of Death." He added: "You can say that even
        // if they would have died due to the damage regardless."
        //
        // NOTE WHAT THAT PERMITS, because it cuts against the usual rule. IKMA
        // never claims a cause the game did not state, and a 6-health card
        // dying to 2 damage is only explicable BY the sigil — but a 1-health
        // card would have died anyway, and "dies to Touch of Death" there names
        // a cause that was not decisive. He asked for it anyway and gave the
        // reason: the line is about the ATTACKER'S THREAT, not the arithmetic
        // of one hit. A sighted player sees that sigil on the board the whole
        // time; this is the audible equivalent of it being visible.
        //
        // Asked of the GAME (PlayableCard.HasAbility) and NAMED by the game
        // (AbilityInfo.rulebookName via CardReader.GetAbilityName), so a rename
        // follows through without a build. Kept in one place so the attack line
        // and the death line can never drift apart.
        // ------------------------------------------------------------------
        internal static string DeadlyName()
        {
            string n = null;
            try { n = CardReader.GetAbilityName(Ability.Deathtouch); } catch { }
            return Vocabulary.Combat.DeadlyName(string.IsNullOrEmpty(n), n);
        }

        internal static bool IsDeadly(PlayableCard card)
        {
            if (card == null) return false;
            try { return card.HasAbility(Ability.Deathtouch); }
            catch { return false; }
        }

        /// <summary>
        /// " with Touch of Death", " with Bloodlust", or " with Touch of Death
        /// and Bloodlust" — every sigil on the attacker that changes what the
        /// attack MEANS. (0.7.185.)
        ///
        /// <para>Zamar asked for Bloodlust "similar (and in addition to) Touch
        /// of Death", which is the point: these are not two features, they are
        /// one list. A third will be a line in the array, not another method.
        /// </para>
        ///
        /// <para>The test for adding one: does knowing this sigil is on the
        /// attacker change how the defender should feel about the hit? Touch of
        /// Death means it dies regardless of health; Bloodlust means the
        /// attacker gets stronger if it does. Sigils that only affect WHERE an
        /// attack lands (Airborne, Bifurcated Strike) already have their own
        /// lines and do not belong here.</para>
        /// </summary>
        private static readonly Ability[] ATTACK_SIGILS =
        {
            Ability.Deathtouch,
            Ability.GainAttackOnKill,
        };

        internal static string DeadlyNote(PlayableCard attacker)
        {
            if (attacker == null) return "";

            var names = new System.Collections.Generic.List<string>();
            foreach (var a in ATTACK_SIGILS)
            {
                bool has = false;
                try { has = attacker.HasAbility(a); } catch { }
                if (!has) continue;

                string n = null;
                try { n = CardReader.GetAbilityName(a); } catch { }
                if (!string.IsNullOrEmpty(n)) names.Add(n);
            }

            if (names.Count == 0) return "";
            if (names.Count == 1) return Vocabulary.Combat.WithOne(names[0]);

            string last = names[names.Count - 1];
            names.RemoveAt(names.Count - 1);
            return Vocabulary.Combat.WithMany(names.ToArray(), last);
        }

        /// <summary>" to Touch of Death", for the killer in a death line.</summary>
        internal static string DeadlyCause(PlayableCard killer)
            => IsDeadly(killer) ? Vocabulary.Combat.ToDeadly(DeadlyName()) : "";

        // The card currently swinging, with the time it was noted. Combat
        // resolves one attack at a time, so "the attacker" is well defined
        // while its own damage lands — but only for as long as that takes.
        //
        // WHY NOT TAKE IT FROM TakeDamage'S PARAMETERS: those names are not in
        // any dump on this project, and a wrong injection name inside a
        // PatchAll class makes Harmony throw and kills every patch after it.
        private static PlayableCard _currentAttacker;
        private static float _currentAttackerAt = -999f;

        /// <summary>
        /// How long an attacker stays credited. Long enough for the hit it is
        /// about to land, short enough that an unrelated death a moment later
        /// cannot inherit its name. A wrong cause is worse than no cause.
        /// </summary>
        private const float ATTACKER_WINDOW_SECONDS = 2f;

        internal static void NoteAttacker(PlayableCard attacker)
        {
            _currentAttacker   = attacker;
            _currentAttackerAt = UnityEngine.Time.unscaledTime;
        }

        internal static PlayableCard CurrentAttacker()
        {
            if (_currentAttacker == null) return null;
            if (UnityEngine.Time.unscaledTime - _currentAttackerAt > ATTACKER_WINDOW_SECONDS)
                return null;
            return _currentAttacker;
        }

        // Where the card stood when the hit landed, captured before it dies so
        // the slot can be checked for a replacement afterwards.
        internal CardSlot Slot;

        // The opponent's queue when the hit landed. See SlotArrival.QueueSnapshot.
        internal System.Collections.Generic.List<PlayableCard> QueuedAtHit;

        // Health straight after this hit, or int.MinValue if not captured.
        // See PlayableCard_TakeDamage_Patch.CaptureHealth. (0.7.348.)
        internal int HealthAfter = int.MinValue;

        // 0.7.438 - words that stand in for the card's name at the start of
        // the line. Set by BurrowBlock; null everywhere else.
        internal string Lead;

        // ------------------------------------------------------------------
        // A CARD HIT WHILE STILL IN THE QUEUE. (0.7.175.)
        //
        // Zamar: "I think it said the name of the cards from que doing damage,
        // before it said they moved."
        //
        // WHAT HIS LOG ACTUALLY SHOWS, and it is worse than an ordering slip:
        //
        //   Excess damage carries to the queue, targeting Slot 1.
        //   Wolf takes 1 damage, 1 health left.
        //   ...a full combat later...
        //   Enemy Wolf moves down to slot 1.
        //
        // His Wolf overkilled the Pack Mule, the excess carried into the QUEUE,
        // and the card it hit was announced by name as plain "Wolf" - a card he
        // had never been told existed in that position, because it had not come
        // down yet. He had two Wolves of his own on the board at the time.
        //
        // NOT FIXABLE BY REORDERING. The queue card genuinely takes the damage
        // before it arrives; the game does it in that order. Announcing the
        // arrival early would be a worse lie than the one being fixed. What was
        // missing is WHERE the card is, and the game answers that directly -
        // Opponent.Queue holds exactly the cards not yet on the board.
        //
        // GENERAL: when a line names something the player cannot place, the fix
        // is usually to say where it is, not to move the line.
        // ------------------------------------------------------------------
        /// <summary>
        /// Which board slot this card is queued for, or -1 if it is not in the
        /// opponent's queue at all. (0.7.183.)
        ///
        /// The queue and QueuedSlots are parallel: the card at Queue[i] is
        /// heading for QueuedSlots[i]. That slot number is what makes the line
        /// placeable — "queued in slot 1" tells him WHERE it will land, which
        /// is the thing he could not work out when it was named bare.
        /// </summary>
        private static int OpponentQueueSlot(PlayableCard card)
        {
            if (card == null) return -1;
            try
            {
                var opponent = TurnManager.Instance?.Opponent;
                var queue    = opponent?.Queue;
                if (queue == null) return -1;

                for (int i = 0; i < queue.Count; i++)
                {
                    if (!ReferenceEquals(queue[i], card)) continue;

                    var slots = opponent.QueuedSlots;
                    if (slots != null && i < slots.Count && slots[i] != null)
                        return slots[i].Index;

                    // In the queue, but the slot could not be read. Caller
                    // falls back to a line with no slot number rather than
                    // claiming one.
                    return -2;
                }
            }
            catch { }
            return -1;
        }

        internal string Compose()
        {
            DamageDeathMerger.Release(this);
            string dmg = Vocabulary.DamageCount(Damage);

            // PROVISIONAL WORDING. "The queued X" is Claude's phrasing, not
            // his - the INFORMATION is his ask, the words are a placeholder -
            // so it logs itself as provisional and can be corrected from the
            // log rather than shipping quietly.
            // ZAMAR'S WORDING, 0.7.183:
            //
            //   "The Wolf queued in slot [x] behind it takes 1 damage"
            //
            // Replaces Claude's provisional "The queued Wolf". Two things his
            // version does that mine did not: it gives the SLOT, so the card
            // can be placed on the board he is holding in his head, and
            // "behind it" says the relationship to the card that was just
            // overkilled — which is the whole reason this card is taking
            // damage at all.
            //
            // HE FLAGGED THE RISK HIMSELF and accepted it:
            //
            //   "The behind it might cause a continuity error in some cases
            //    that I'll have to keep an ear out for but I think it should be
            //    fine and provides a bit more clarity."
            //
            // Recorded here so a future session does not "fix" it silently.
            // THE SYMPTOM TO LISTEN FOR: "behind it" refers to whatever the
            // previous line was about, so if excess damage ever reaches the
            // queue by a route where the preceding line was NOT the card in
            // front of it, the pronoun points at the wrong thing. If that turns
            // up in a log, the fix is to name the card it is behind rather than
            // to drop the clause — the clarity is the point.
            string who = Lead ?? Name;
            int queuedSlot = OpponentQueueSlot(Card);
            if (queuedSlot != -1 && Lead == null)
            {
                // "behind it" REMOVED. (0.7.185.) He added it, heard it in
                // play, and took it back out: "remove that Behind It decision I
                // made earlier for the que thing. in this log, the que callout
                // before it provided extra clarity."
                //
                // The queue-arrival line (0.7.177) now says the card is queued
                // and where, one event earlier, so by the time it takes damage
                // the player already knows where it is. "behind it" was solving
                // a problem that got solved better upstream — and it carried a
                // continuity risk he had flagged himself.
                //
                // GENERAL: when a clause exists to compensate for missing
                // context, check whether the context has since arrived.
                who = queuedSlot >= 0
                    ? Vocabulary.Combat.QueuedInSlot(Name, queuedSlot + 1)
                    : Vocabulary.Combat.Queued(Name);

                Plugin.Log?.LogInfo(
                    $"IKMA DAMAGE: '{Name}' was hit while still in the queue " +
                    (queuedSlot >= 0 ? $"(targeting slot {queuedSlot + 1})." : "(slot unknown)."));
            }

            if (Died)
            {
                string cause = DeadlyCause(Killer);
                if (cause.Length > 0)
                    Plugin.Log?.LogInfo(
                        $"IKMA DEATH: '{Name}' credited to " +
                        $"{CardReader.CardName(Killer?.Info)}'s Touch of Death.");

                // 0.7.219 — a thing is destroyed, a creature dies.
                // Vocabulary.IsNonLiving asks the game's Trait.Terrain first.
                // 0.7.220 — "and dies" / "and destroyed" / "and the cage is
                // destroyed", decided by what the card IS. See Vocabulary.
                // 0.7.224 — DeathClause, not DeathTail. THE TWO FORMS ARE NOT
                // THE SAME and 0.7.220 used the wrong one here:
                //
                //   "Gold Nugget takes 3 damage and destroyed."   <- what he heard
                //   "Gold Nugget takes 3 damage and is destroyed." <- correct
                //
                // DeathTail exists for "X is cut and ___", where the "is" is
                // already supplied by the item's own verb. "takes 3 damage
                // and ___" supplies no verb, so it needs the whole predicate.
                // Containers read correctly either way: "takes 6 damage and the
                // cage is destroyed".
                string death = Vocabulary.Combat.TakesAnd(who, dmg, Card?.Info, cause);
                string arrival = SlotArrival.Describe(Slot, Card, QueuedAtHit);
                return arrival == null ? death : $"{death} {arrival}";
            }

            // Session 11: a survived hit said only what was lost, never what is
            // left. A sighted player reads the new number off the card the
            // instant it changes; without it the player has to hold every card's
            // health in their head and do the subtraction, and one missed line
            // puts the whole board model out. Read LIVE, at speak time, so it is
            // the health the card actually has and not a figure computed before
            // the rest of the turn resolved.
            int remaining = -1;
            if (HealthAfter != int.MinValue) remaining = HealthAfter;
            else
            {
                try { if (Card != null) remaining = Card.Health; }
                catch { remaining = -1; }
            }

            if (remaining < 0) return Vocabulary.Combat.TakesDamage(who, dmg);

            // "remaining", not "left". (0.7.185.) Zamar: "A bit more
            // dramatic." Same word his candle line already uses — "You have 1
            // remaining" — so the mod now says it one way everywhere.
            string left = Vocabulary.HealthRemainingCount(remaining);

            // ZERO HEALTH IS NOT A SURVIVED HIT. (0.7.323.)
            //
            // Zamar: "Add to that line the trigger as to why it's not just
            // saying it dies then." His log had the Porcupine reading
            // "takes 2 damage, 0 health remaining" and only dying four lines
            // later, which sounds like a card that lived.
            //
            // Died is false here because the game has not run the death
            // sequence yet, and the reason it has not is always a trigger
            // part-way through on this card. SigilTriggers asks
            // GlobalTriggerHandler whether one is on the stack and names the
            // one it already watched fire — see ResolvingAbilityOn.
            //
            // Wording approved by Zamar, Session 25, on hearing it read back:
            // "Its <sigil> ability resolves before it dies."
            if (remaining == 0)
            {
                string holding = null;
                try { holding = SigilTriggers.ResolvingAbilityOn(Card); } catch { }

                if (!string.IsNullOrEmpty(holding))
                {
                    return Vocabulary.Combat.TakesItsAbilityResolves(who, dmg, left, holding);
                }

                Plugin.Log?.LogInfo(
                    $"IKMA DAMAGE: '{Name}' is at 0 health and not dead, and no trigger " +
                    "is resolving on it — nothing named.");
            }

            return Vocabulary.Combat.TakesDamageLeft(who, dmg, left);
        }
    }

    internal static class DamageDeathMerger
    {
        // ------------------------------------------------------------------
        // "Your Wolf" / "Enemy Wolf", BUT ONLY WHEN IT IS AMBIGUOUS. (0.7.185.)
        //
        // Zamar: "When an enemy card attacks my card, or when my card attacks
        // an enemy card, and they have the same name, add 'Your' or 'Enemy'
        // before the card name, to help clarify which is attacking which, and
        // which is dying."
        //
        // His log, and it is unreadable in exactly the way he describes:
        //
        //     Wolf attacks Wolf.
        //     Wolf takes 3 damage and dies.
        //
        // NOTE THE "ONLY WHEN" — this deliberately does NOT qualify every card.
        // The board reads already lead with "Your board" / "Enemy board", and
        // putting a side on every combat line would add a word to most
        // sentences to solve a problem that only exists when two names collide.
        // The ambiguity is the trigger, not the possibility of it.
        //
        // The question is asked of the board each time rather than cached: a
        // duplicate can appear or die at any point in a turn.
        // ------------------------------------------------------------------
        // ------------------------------------------------------------------
        // TWO OF THE SAME CARD ON ONE SIDE: NAME THE SLOT. (Session 32.)
        //
        // SideQualifiedName below settles a twin on the OTHER side ("Your" /
        // "Enemy"). A twin on the SAME side was still ambiguous: Zamar's two
        // Kingfishers in slots 1 and 4 both "flies over..." Asked whether to
        // name the slot like the sacrifice line does, he said yes:
        // "Kingfisher in Slot 1 flies over Rabbit and attacks directly."
        //
        // FIRST BUILT FOR ATTACK LINES ONLY, because that is what was asked.
        // Asked next whether damage and death lines should name the slot
        // too, Zamar: "yes". So every combat line that names a card goes
        // through here: attacks, blocks, the damage record (which the damage
        // and death lines read), and the multi-strike summary.
        //
        // Asked of the board every time, like the side check: a twin can
        // arrive or die mid-turn.
        // ------------------------------------------------------------------
        internal static string CombatLineName(PlayableCard card, string name)
        {
            string qualified = SideQualifiedName(card, name);
            if (card == null || string.IsNullOrEmpty(name)) return qualified;
            try
            {
                var slot = card.Slot;
                var bm = Singleton<BoardManager>.Instance;
                if (slot == null || bm == null) return qualified;

                var sameSide = slot.IsPlayerSlot ? bm.PlayerSlotsCopy : bm.OpponentSlotsCopy;
                if (sameSide == null) return qualified;

                foreach (var s in sameSide)
                {
                    var other = s?.Card;
                    if (other == null || ReferenceEquals(other, card)) continue;
                    if (CardReader.CardName(other) != name) continue;

                    int idx = sameSide.IndexOf(slot);
                    return idx >= 0 ? qualified + Vocabulary.Combat.InSlotNumber(idx + 1) : qualified;
                }
            }
            catch { }
            return qualified;
        }

        internal static string SideQualifiedName(PlayableCard card, string name)
        {
            if (card == null || string.IsNullOrEmpty(name)) return name;

            // UNDER THE WATER FIRST, THEN THE SIDE. (0.7.264.)
            //
            // "Enemy Submerged Kingfisher", not "Submerged Enemy Kingfisher" —
            // the side is a qualifier on the whole thing being pointed at, and
            // Submerged is part of what that thing is right now. Applied here
            // rather than at each caller because this method is already the one
            // place a live combat line asks for a card's spoken name, so the
            // decoration reaches the attack, damage and death lines together
            // and cannot drift between them. See Vocabulary.Submerged.
            name = Vocabulary.Submerged(card, name);

            try
            {
                var slot = card.Slot;
                if (slot == null) return name;

                var bm = Singleton<BoardManager>.Instance;
                if (bm == null) return name;

                bool mine = slot.IsPlayerSlot;
                var otherSide = mine ? bm.OpponentSlotsCopy : bm.PlayerSlotsCopy;
                if (otherSide == null) return name;

                foreach (var s in otherSide)
                {
                    var other = s?.Card;
                    if (other == null || ReferenceEquals(other, card)) continue;
                    if (CardReader.CardName(other) != name) continue;

                    return Vocabulary.Combat.SideQualifiedName(mine, name);
                }
            }
            catch { }

            return name;
        }

        // Parallel lists rather than a Dictionary: keys here are Unity objects
        // that may be destroyed between insert and lookup, and Unity's == /
        // Equals overloads make hashed lookup on destroyed objects unreliable.
        // Reference identity, scanned linearly over at most a handful of
        // entries, is both safer and cheaper at this size.
        private static readonly System.Collections.Generic.List<PlayableCard> _cards
            = new System.Collections.Generic.List<PlayableCard>();
        private static readonly System.Collections.Generic.List<DamageRecord> _records
            = new System.Collections.Generic.List<DamageRecord>();

        private const int MAX_PENDING = 12;

        internal static DamageRecord Open(PlayableCard card, string name, int damage)
        {
            // Session 32: CombatLineName, so a same-side twin gets its slot.
            var record = new DamageRecord { Name = CombatLineName(card, name), Damage = damage, Card = card };

            // Only meaningful if this hit belongs to an attack currently being
            // announced — see DamageRecord.CurrentAttacker.
            record.Killer = DamageRecord.CurrentAttacker();
            try { record.Slot = card?.Slot; } catch { record.Slot = null; }
            record.QueuedAtHit = SlotArrival.QueueSnapshot();

            // A second hit on the same card before the first was spoken is its
            // own event: stop tracking the older entry (its queued line still
            // composes normally as a damage-only line) and track the newer one,
            // so a death folds into the hit that actually killed the card.
            int existing = IndexOf(card);
            if (existing >= 0)
            {
                _cards.RemoveAt(existing);
                _records.RemoveAt(existing);
            }

            _cards.Add(card);
            _records.Add(record);

            // Safety valve: records are normally released as they compose, but
            // a scene teardown mid-combat could strand some. Never let the
            // lists grow without bound.
            while (_cards.Count > MAX_PENDING)
            {
                _cards.RemoveAt(0);
                _records.RemoveAt(0);
            }

            return record;
        }

        /// <summary>
        /// True if the death was folded into a pending, not-yet-spoken damage
        /// line — in which case the caller should not queue a death line.
        /// </summary>
        internal static bool TryMarkDeath(PlayableCard card)
        {
            int i = IndexOf(card);
            if (i < 0) return false;

            _records[i].Died = true;
            _cards.RemoveAt(i);
            _records.RemoveAt(i);
            return true;
        }

        internal static void Release(DamageRecord record)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                if (ReferenceEquals(_records[i], record))
                {
                    _cards.RemoveAt(i);
                    _records.RemoveAt(i);
                    return;
                }
            }
        }

        private static int IndexOf(PlayableCard card)
        {
            for (int i = 0; i < _cards.Count; i++)
                if (ReferenceEquals(_cards[i], card)) return i;
            return -1;
        }
    }

    // -------------------------------------------------------------------------
    // Sacrifice + bone gain, merged into one line. (Session 10 note 3.)
    //
    // One sacrifice used to produce three separate announcements: HotkeyManager's
    // "Sacrificing Squirrel." (intent), the Die patch's "Squirrel is sacrificed."
    // (result), and AddBones' "1 bone received." Two of those say the same thing
    // and the third is part of the same event. The intent line is gone from
    // HotkeyManager entirely — it was announcing a keypress, which the hard rules
    // forbid — and the remaining two fold here into:
    //
    //     "Squirrel is sacrificed. Received 1 bone."
    //
    // Folding is bounded by FRAME_WINDOW rather than by wall time. The bone from
    // a sacrifice is granted inside the same death flow, so it lands within a
    // frame or two; a bone from any other source arriving later opens no record
    // to fold into and announces itself normally.
    // -------------------------------------------------------------------------
    // -------------------------------------------------------------------------
    // BLOOD PROGRESS. (0.7.148.)
    //
    // Zamar, after ending a fight with two Wolves on the board and no idea how:
    // a Black Goat carries Worthy Sacrifice and is worth 3 blood, so one goat
    // legitimately paid for a 2-blood Wolf and left the sequence over-paid. The
    // log showed no cheat. What it showed was that IKMA never told him how much
    // blood he had, so a legal outcome was indistinguishable from a broken one.
    //
    // The sighted player reads this off the sacrifice markers under the card.
    // SetSacrificeMarkersValue is the game's own call that sets them, so the
    // number spoken here is literally the number drawn on screen -- parity, not
    // arithmetic of ours. Declared on BoardManager and OVERRIDDEN on
    // BoardManager3D (dump_sacrifice.txt marks it "[OVERRIDE of BoardManager]"),
    // so BOTH are registered. Act 1 runs the 3D one; the base alone would never
    // fire. Third time the override trap has come up.
    // -------------------------------------------------------------------------
    internal static class SacrificeProgress
    {
        private static int _cost;
        private static int _value = -1;

        /// <summary>How much blood the markers currently show.</summary>
        internal static int Value { get { return _value; } }

        /// <summary>How much blood the demanding card costs.</summary>
        internal static int Cost { get { return _cost; } }

        /// <summary>
        /// True once the markers have reached the cost -- the game has what it
        /// asked for and every further press is a press too many. See
        /// HotkeyManager's sacrifice branch for what that press was doing.
        /// </summary>
        internal static bool Satisfied()
        {
            return _cost > 0 && _value >= _cost;
        }

        /// <summary>A sacrifice sequence has begun for a card costing this much.</summary>
        internal static void Begin(int cost)
        {
            _cost  = cost;
            _value = 0;
            Plugin.Log?.LogInfo($"IKMA SACPROG: sequence begins, cost {cost} blood.");
        }

        /// <summary>
        /// The game just redrew the markers with this running total.
        /// Called EVERY FRAME, not only on change -- 0.7.148's log has runs of
        /// nineteen identical lines -- so only a change is worth writing down.
        /// </summary>
        internal static void NoteMarkerValue(int value)
        {
            if (value == _value) return;
            _value = value;
            Plugin.Log?.LogInfo($"IKMA SACPROG: markers now {value} of {_cost}.");
        }
    }

    // Registered through Plugin.TryPatch on BOTH declarations; no
    // [HarmonyPatch] attribute, or PatchAll would claim it as well.
    public class BoardManager_SetSacrificeMarkersValue_Patch
    {
        static void Postfix(int value) => SacrificeProgress.NoteMarkerValue(value);
    }

    internal class SacrificeRecord
    {
        internal string Name;
        internal int Bones;
        internal bool Spoken;

        // Session 14 — the Morsel transfer, Zamar's wording:
        // "[card receiving stats] gained [x stats] from [card with morsel]."
        //
        // Morsel adds the sacrificed card's stats to the card it was sacrificed
        // FOR, and IKMA said nothing about it — the Black Goat quietly became a
        // different card and the only way to find out was to browse the board.
        // Same class of silent-stale-board-model failure as the movers.
        //
        // Recorded here rather than announced separately because he asked for it
        // on the same line as the bone, and because it IS the same moment.
        internal PlayableCard Receiver;
        internal string ReceiverName;
        internal int AttackBefore;
        internal int HealthBefore;
        internal bool WatchMorsel;

        // What Morsel actually hands over: the dying card's own live Attack
        // and Health, read off it at trigger time. (0.7.292.)
        internal int MorselAttack;
        internal int MorselHealth;

        // ==================================================================
        // THE SENTENCE MORSEL'S OWN LINE WILL SAY. (0.7.295.)
        //
        // Set when the sacrifice line composes, which is where the receiver,
        // the source and the numbers are all known; read and cleared by
        // SigilTriggers when the Morsel trigger line composes a moment later.
        // Static because the two live in different objects and the pairing is
        // always one-to-one: Morsel fires once per sacrifice that carries it.
        //
        // Cleared on read, so a Morsel line that never gets composed (the card
        // was gone by speak time) cannot attach itself to the next one.
        // ==================================================================
        internal static string MorselGainLine;

        internal static string TakeMorselGainLine()
        {
            string line = MorselGainLine;
            MorselGainLine = null;
            return line;
        }

        // Which sacrificed card carried Morsel. On a merged line that is not
        // necessarily the card the record was opened for.
        internal string MorselSource;

        // ==================================================================
        // ONE CALLOUT FOR THE WHOLE SEQUENCE. (0.7.152.)
        //
        // Zamar: "I expect those squirrel sacrificed lines to be on the same
        // callout. Something like 'Squirrel in Slot 1 and Squirrel in slot 3
        // sacrificed. Received 2 bones.'"
        //
        // Two Squirrels paying for one Wolf is ONE event, and it was arriving as
        // two identical sentences with no way to tell which Squirrel each meant.
        // The slot is what tells them apart -- the same answer as "Wolf 1, Wolf
        // 2" in hand: when two cards share a name, name where they are.
        //
        // Held in the record rather than counted, because the bone fold already
        // gives this line a settle window and folding a second death into it is
        // the same mechanism one step further.
        internal readonly List<string> Subjects = new List<string>();

        internal string Compose()
        {
            Spoken = true;
            SacrificeBoneMerger.Close(this);

            string bonePart = Bones <= 0
                ? ""
                : Vocabulary.ReceivedBones(Bones);

            // 0.7.153, his call: the slot is named however many cards there
            // are. One event, one shape -- a line that changes form depending
            // on how many cards happened to be involved teaches the player
            // nothing they can rely on (the Session 13 lesson, again).
            //
            // Name is the fallback for the case the slot could not be read at
            // death time; a bare name is still a true subject.
            string subject = Subjects.Count == 1 ? Subjects[0] : Name;
            string line = Subjects.Count <= 1
                ? Vocabulary.Combat.IsSacrificed(subject, bonePart)
                : Vocabulary.Combat.Sacrificed(JoinSubjects(), bonePart);

            // 0.7.152: the spoken blood count is GONE at his request ("We
            // don't need to call out the 2 of 2 blood thing, remove that.").
            // SacrificeProgress itself stays and is load-bearing — it is the
            // gate that stops a press past the cost un-marking a sacrifice.
            // The number was only ever the diagnostic that found that bug.
            // 0.7.295 — THE GAIN MOVES TO MORSEL'S OWN TRIGGER LINE.
            //
            // Zamar, 0.7.294: "Ouroboros gaining 2 health should have been on
            // the ability triggers line, not the line before it." The
            // sacrifice line reports the sacrifice; what Morsel did belongs
            // to Morsel, which announces itself one line later.
            //
            // The sentence is composed here anyway, because this is where the
            // numbers are known, and handed to SigilTriggers to say. His
            // words are unchanged — only where they are spoken.
            // 0.7.341 — the Morsel triggers this payment set off, after it.
            return line + TakeMorselLines();
        }

        /// <summary>"A in Slot 1, B in Slot 2 and C in Slot 3" — Oxford-less,
        /// the same joining the ability list already uses.</summary>
        private string JoinSubjects()
        {
            if (Subjects.Count == 2) return Vocabulary.Combat.TargetListTwo(Subjects[0], Subjects[1]);
            var head = Subjects.GetRange(0, Subjects.Count - 1);
            return Vocabulary.Combat.SubjectList(head, Subjects[Subjects.Count - 1]);
        }

        // Read LIVE at speak time and diffed against what was recorded before
        // the sacrifice resolved, so this reports what the card ACTUALLY gained
        // rather than what Morsel is supposed to grant. A zero component is
        // dropped: "gained 2 health" beats "gained 0 power and 2 health".
        internal string ComposeMorselGain()
        {
            if (!WatchMorsel || Receiver == null || ReceiverName == null) return null;

            // ==============================================================
            // 0.7.292 — THE DIFF WAS ATTRIBUTING SOMEBODY ELSE'S CHANGE TO
            // MORSEL, AND IT SHIPPED A FALSE NUMBER.
            //
            // Zamar's 0.7.291 log: "Mealworm in Slot 1 is sacrificed.
            // Received 1 bone. Flying Ant gained 3 power from Mealworm." His
            // verdict: "That mealworm thing was inaccurate." The Mealworm was
            // 0/2, so Morsel granted no power at all.
            //
            // WHY THE DIFF LIED. Flying Ant's attack is "equal to the number
            // of ants on its owner's side" — a stat the game recomputes from
            // the board. Ants arrived while the sacrifice resolved, the
            // receiver's Attack went up by three on its own, and reading
            // before-and-after put that on Morsel's bill. The comment above
            // this method argued the diff "reports what the card ACTUALLY
            // gained rather than what Morsel is supposed to grant" — true of
            // a static stat, and exactly backwards for a dynamic one.
            //
            // ASK MORSEL. Morsel.OnSacrifice builds
            // new CardModificationInfo(Card.Attack, Card.Health) from the
            // DYING card and adds it to the demanding card. That is the whole
            // grant, it is knowable at trigger time, and it cannot be
            // confused with anything else happening on the board. Captured
            // where the watch is armed; a zero component is still dropped.
            //
            // The project rule this broke, twice now: never recompute later
            // from the world what the game settled at the moment it acted.
            // ==============================================================
            int dAtk = MorselAttack;
            int dHp  = MorselHealth;
            if (dAtk <= 0 && dHp <= 0) return null;

            string parts;
            if (dAtk > 0 && dHp > 0) parts = Vocabulary.Combat.PowerAndHealth(dAtk, dHp);
            else if (dAtk > 0)       parts = Vocabulary.Combat.Power(dAtk);
            else                     parts = Vocabulary.Combat.Health(dHp);

            // 0.7.295b — NO "from <source>". Zamar, once the line moved onto
            // Morsel's own trigger sentence: "'Mealworm's Morsel ability
            // triggers. Ouroboros gained 2 health.' ... thats what I meant,
            // yeah do that." The source is already the subject of the
            // sentence this clause follows, so naming it again said Mealworm
            // twice. MorselSource is kept because it is what identifies the
            // carrier on a merged sacrifice, and the log still prints it.
            return Vocabulary.Combat.MorselGainFor(ReceiverName, parts);
        }

        // ==================================================================
        // MORSEL RIDES THE SACRIFICE LINE, ONE SENTENCE PER TRIGGER. (0.7.341.)
        //
        // Zamar's 0.7.340 log, two Mealworms sacrificed for one Ouroboros:
        //   SPEAK (interrupt): Mealworm's Morsel ability triggers.
        //   SPEAK: Mealworm in Slot 3 and Mealworm in Slot 4 sacrificed. ...
        //   SPEAK: Mealworm's Morsel ability triggers. Ouroboros gained 2 health.
        // "I only heard gains 2 health but I believe it was four since there
        //  was two morsel triggers." He was right: each Morsel hands over the
        // dying card's own stats, so two 0/2 Mealworms are +4 health — the
        // Fledgling line that followed (8/8 to a 9/14 Elder) agrees. The gain
        // lived in ONE static slot, filled by the first sacrifice only, so one
        // trigger said nothing and the other said half.
        //
        // And Morsel.OnSacrifice runs BEFORE the card dies, so its line was
        // queued ahead of "X is sacrificed". Now each trigger composes its own
        // sentence at trigger time, from the card Morsel reads, and waits
        // here; the sacrifice line says them after itself, in firing order.
        // Still Morsel's own sentence (his 0.7.294 rule), one per trigger.
        // ==================================================================
        private class HeldTriggerLine
        {
            internal string Line;
            internal float At;
            // Set for a Morsel grant, so two can be added together. (0.7.349.)
            internal string Receiver;
            internal int Atk;
            internal int Hp;
        }

        private static readonly List<HeldTriggerLine> _pendingMorsel = new List<HeldTriggerLine>();

        internal static void AddMorselLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            _pendingMorsel.Add(new HeldTriggerLine { Line = line, At = Time.unscaledTime });
            Plugin.Log?.LogInfo($"IKMA SACRIFICE: trigger held for the sacrifice line — \"{line}\"");
        }

        /// <summary>
        /// A Morsel trigger, with what it grants, so that two of them paying
        /// for one card can be said as one sentence. (0.7.349.)
        /// </summary>
        internal static void AddMorselTrigger(PlayableCard dying, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            var h = new HeldTriggerLine { Line = line, At = Time.unscaledTime };
            try
            {
                var receiver = Singleton<BoardManager>.Instance?.CurrentSacrificeDemandingCard;
                h.Receiver = CardReader.CardName(receiver?.Info);
                h.Atk = dying != null ? dying.Attack : 0;
                h.Hp  = dying != null ? dying.Health : 0;
            }
            catch { h.Receiver = null; }
            _pendingMorsel.Add(h);
            Plugin.Log?.LogInfo($"IKMA SACRIFICE: trigger held for the sacrifice line — \"{line}\"");
        }

        // ==================================================================
        // TWO MORSELS, ONE SENTENCE. (0.7.349.)
        //
        // Zamar, two Mealworms for one Ouroboros: "Mealworm's Morsel ability
        // triggers. Ouroboros gained 2 health. Mealworm's Morsel ability
        // triggers. Ouroboros gained 2 health." — "This should have been both
        // morsel abilities trigger, Ouro gains [4] health. Condense these
        // battle logs where we can like this."
        // ==================================================================
        internal static string TakeMorselLines()
        {
            if (_pendingMorsel.Count == 0) return "";
            float now = Time.unscaledTime;
            var live = new List<HeldTriggerLine>();
            foreach (var h in _pendingMorsel)
            {
                if (now - h.At > 5f)
                {
                    Plugin.Log?.LogWarning($"IKMA SACRIFICE: a held trigger line went stale and was dropped — \"{h.Line}\"");
                    continue;
                }
                live.Add(h);
            }
            _pendingMorsel.Clear();

            // Morsel grants to the same card, with something to grant.
            var morsels = new List<HeldTriggerLine>();
            foreach (var h in live)
                if (!string.IsNullOrEmpty(h.Receiver) && (h.Atk > 0 || h.Hp > 0)) morsels.Add(h);

            bool merge = morsels.Count >= 2;
            if (merge)
                foreach (var h in morsels)
                    if (h.Receiver != morsels[0].Receiver) { merge = false; break; }

            var sb = new System.Text.StringBuilder();
            bool mergedSaid = false;
            foreach (var h in live)
            {
                if (merge && morsels.Contains(h))
                {
                    if (mergedSaid) continue;
                    mergedSaid = true;
                    int atk = 0, hp = 0;
                    foreach (var m in morsels) { atk += m.Atk; hp += m.Hp; }
                    string parts;
                    if (atk > 0 && hp > 0) parts = Vocabulary.Combat.PowerAndHealth(atk, hp);
                    else if (atk > 0)      parts = Vocabulary.Combat.Power(atk);
                    else                   parts = Vocabulary.Combat.Health(hp);
                    string sigil = null;
                    try { sigil = CardReader.GetAbilityName(Ability.Morsel); } catch { }
                    if (string.IsNullOrEmpty(sigil)) sigil = Vocabulary.Combat.MorselFallback;
                    string lead = Vocabulary.BothOrAll(morsels.Count);
                    sb.Append(' ').Append(Vocabulary.Combat.AbilitiesTriggerGains(lead, sigil, morsels[0].Receiver, parts));
                    continue;
                }
                sb.Append(' ').Append(h.Line);
            }
            return sb.ToString();
        }

        /// <summary>
        /// What one Morsel trigger grants, asked of the card Morsel reads:
        /// Morsel.OnSacrifice builds new CardModificationInfo(Card.Attack,
        /// Card.Health) from the dying card and gives it to the card the
        /// sacrifice is for. Null when it grants nothing.
        /// </summary>
        internal static string MorselGainFor(PlayableCard dying)
        {
            try
            {
                var receiver = Singleton<BoardManager>.Instance?.CurrentSacrificeDemandingCard;
                string receiverName = CardReader.CardName(receiver?.Info);
                if (dying == null || string.IsNullOrEmpty(receiverName)) return null;

                int dAtk = dying.Attack;
                int dHp  = dying.Health;
                if (dAtk <= 0 && dHp <= 0) return null;

                string parts;
                if (dAtk > 0 && dHp > 0) parts = Vocabulary.Combat.PowerAndHealth(dAtk, dHp);
                else if (dAtk > 0)       parts = Vocabulary.Combat.Power(dAtk);
                else                     parts = Vocabulary.Combat.Health(dHp);
                return Vocabulary.Combat.MorselGainFor(receiverName, parts);
            }
            catch { return null; }
        }
    }

    internal static class SacrificeBoneMerger
    {
        /// <summary>
        /// How long the sacrifice line is held before it composes, so the bone
        /// the sacrifice grants has landed and can fold into the same sentence.
        /// </summary>
        internal const float SettleSeconds = 0.3f;

        private static SacrificeRecord _open;
        private static float _openedAt = -999f;

        // Session 13: this was a 3-FRAME window, and frames are the wrong unit.
        // At a variable frame rate three frames is anywhere from 25ms to 100ms,
        // so whether the bone folded into the sacrifice line came down to what
        // the frame rate happened to be doing. Zamar heard both in one session:
        // "Squirrel is sacrificed. Received 1 bone." and then, minutes later,
        // "Corpse Maggots is sacrificed." followed separately by "1 bone
        // received." Same event, two different shapes, for no reason the player
        // could perceive. Continuity is the point — a line that changes form at
        // random teaches the player nothing they can rely on.
        //
        // Wall clock instead, wide enough to cover any frame rate and still far
        // short of an unrelated bone from a later death folding in by accident.
        private const float FOLD_WINDOW_SECONDS = 0.25f;

        /// <summary>
        /// Start a sacrifice line, or fold this death into the one already
        /// open. Either way the record comes back so the caller can still arm
        /// the Morsel watch on it; `folded` says whether a line is already
        /// queued for it, in which case the caller queues nothing.
        ///
        /// 0.7.152. The fold window is the SAME one the bone uses: cards
        /// sacrificed for one card die inside a single coroutine step, so they
        /// land together or they are not the same event.
        /// </summary>
        /// <summary>When a sacrifice line was last opened. Never cleared, so a
        /// trigger fired by the same payment can ask. (0.7.339.)</summary>
        internal static float LastSacrificeAt = -999f;

        /// <summary>True while a sacrifice line is queued and not yet said.
        /// (0.7.340.)</summary>
        internal static bool LinePending => _open != null && !_open.Spoken;

        /// <summary>The line was dropped unsaid (the bell cleared it). (0.7.352.)</summary>
        internal static void Abandon() => _open = null;

        internal static SacrificeRecord OpenOrFold(string name, string subject, out bool folded)
        {
            if (_open != null && !_open.Spoken &&
                Time.unscaledTime - _openedAt <= FOLD_WINDOW_SECONDS)
            {
                _open.Subjects.Add(subject);
                Plugin.Log?.LogInfo($"IKMA SACRIFICE: folded '{subject}' into the open line ({_open.Subjects.Count} so far).");
                folded = true;
                return _open;
            }

            folded = false;
            LastSacrificeAt = Time.unscaledTime;

            _open = new SacrificeRecord { Name = name, Bones = 0 };
            _open.Subjects.Add(subject);
            _openedAt = Time.unscaledTime;
            return _open;
        }

        /// <summary>
        /// True if the bone gain was folded into a pending, not-yet-spoken
        /// sacrifice line — in which case the caller queues nothing.
        /// </summary>
        internal static bool TryFoldBones(int amount)
        {
            if (_open == null || _open.Spoken) return false;
            if (Time.unscaledTime - _openedAt > FOLD_WINDOW_SECONDS) return false;

            _open.Bones += amount;
            return true;
        }

        internal static void Close(SacrificeRecord record)
        {
            if (ReferenceEquals(_open, record))
            {
                _open = null;
                _openedAt = -999f;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Item-caused deaths get the item's own verb. (Session 10, last note.)
    //
    // HotkeyManager registers the card it is about to target, along with the
    // item doing the targeting, immediately before the click goes through. The
    // Die patch checks that registration first, so a scissored card reports
    // "Coyote is cut and dies." instead of a bare "Coyote dies." — the player
    // hears the connection between their keypress and the corpse.
    //
    // This is still narrating an observed death, not an intended one: nothing is
    // spoken unless the card actually dies. If the item fizzles or the player
    // backs out, the registration simply expires unused.
    // -------------------------------------------------------------------------
    // ==================================================================
    // THE SKINNING KNIFE NEVER CALLS Die. (0.7.352.)
    //
    // TrapperKnifeItem.OnValidTargetSelected plays the death animation and
    // Object.Destroys the card, then spawns a pelt. PlayableCard.Die is never
    // reached, so the "is skinned" line below never fired and the card was
    // announced a turn later by the board diff as "Enemy Mole Seaman has
    // left slot 4." Zamar: "Mole didnt leave slot 4 it died". The line is
    // said here, from the target the game hands over, in the wording the
    // knife already had.
    // ==================================================================
    public static class TrapperKnifeItem_OnValidTargetSelected_Patch
    {
        public static void Prefix(CardSlot target)
        {
            try
            {
                var card = target != null ? target.Card : null;
                if (card?.Info == null) return;
                string name = CardReader.CardName(card);
                BoardWatcher.NoteAnnounced(card);
                Plugin.Log?.LogInfo($"IKMA ITEM: '{name}' skinned — the card is destroyed without dying.");
                using (Speech.Event(EventKind.Death, EventTag.Side(card))) Speech.Result(Vocabulary.Combat.IsSkinnedAnd(name, card.Info));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA ITEM: skin line — {e.GetType().Name}");
            }
        }
    }

    internal static class ItemTargetOutcome
    {
        private static PlayableCard _card;
        private static string _itemName;
        private static float _registeredAt = -999f;

        // Generous — some items animate before the kill lands.
        private const float VALID_SECONDS = 6f;

        internal static void Register(PlayableCard card, string itemName)
        {
            _card = card;
            _itemName = itemName;
            _registeredAt = Time.unscaledTime;
        }

        internal static void Clear()
        {
            _card = null;
            _itemName = null;
            _registeredAt = -999f;
        }

        /// <summary>
        /// Returns an item-specific death line for this card, or null if the
        /// card was not the pending item target.
        /// </summary>
        internal static string DeathLineFor(PlayableCard card, string name)
        {
            if (_card == null || card == null) return null;
            if (!ReferenceEquals(_card, card)) return null;
            if (Time.unscaledTime - _registeredAt > VALID_SECONDS) { Clear(); return null; }

            string item = _itemName;
            Clear();

            CardInfo info = null;
            try { info = card.Info; } catch { }

            switch (item)
            {
                // 0.7.219 — "destroyed" for a thing. The verbs the ITEM
                // supplies (cut, smashed, skinned) are unchanged; only the
                // outcome word follows what the card is.
                case "Scissors":      return Vocabulary.Combat.IsCutAnd(name, info);
                case "Hammer":        return Vocabulary.Combat.IsSmashedAnd(name, info);
                case "Trapper Knife": return Vocabulary.Combat.IsSkinnedAnd(name, info);
                default:              return Vocabulary.DeathSentence(name, info);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Card takes damage: "Wolf takes 2 damage." / "Wolf takes 2 damage and dies."
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(PlayableCard), "TakeDamage")]
    public class PlayableCard_TakeDamage_Patch
    {
        static void Postfix(PlayableCard __instance, int damage, PlayableCard attacker,
                            ref System.Collections.IEnumerator __result)
        {
            if (__instance?.Info == null || damage <= 0) return;

            // 0.7.350 — a cannonball has its own line, without the number.
            if (PirateSkullNarrator.TryCannonHit(__instance, attacker, ref __result)) return;

            // 0.7.448 - ARMORED. The game's TakeDamage begins "if (HasShield())
            // { lostShield = true; ... yield break; }": the hit does nothing.
            // Every path below assumed the damage landed, and Zamar's 0.7.447
            // log has "Amalgam attacks Skunk, it takes 2 damage, 3 health
            // remaining." for a hit the shield took. ShieldNarrator.cs.
            if (ShieldNarrator.TryAbsorb(__instance, attacker)) return;

            // 0.7.348 — a hit on a giant inside a volley is summed there.
            if (GiantVolley.TryRecordHit(__instance, damage)) return;

            // 0.7.347 — a hit inside a multi-strike attack is summed there.
            if (MultiStrikeNarrator.TryRecordHit(__instance, damage)) return;

            var record = DamageDeathMerger.Open(
                __instance, CardReader.CardName(__instance), damage);
            // 0.7.438 - a Burrower that moved in to block: its damage is the
            // end of BurrowBlock's sentence, not a line of its own.
            // 0.7.441 - a multi-strike attacker hit back by Sharp Quills: its
            // damage is said inside that attack's summary, not after it.
            if (!BurrowBlock.TryAttach(__instance, record) && !PlainAttack.TryAttach(__instance, attacker, record)
                && !MultiStrikeNarrator.TryAttachQuillDamage(__instance, record))
            using (Speech.Event(EventTag.DamageOrDeath(record, __instance))) Speech.Result(record.Compose);

            if (__result != null) __result = CaptureHealth(__result, record);
        }

        // ------------------------------------------------------------------
        // THE HEALTH AFTER THIS HIT, NOT AFTER EVERY HIT. (0.7.348.)
        //
        // Zamar's Limoncello turn: four Worker Ants, 4 damage each, 80 health.
        // The lines said 68, 64, 64, 64. Compose read Health LIVE at speak
        // time, and all four hits had landed before the first line spoke.
        //
        // PlayableCard.TakeDamage applies the damage (Status.damageTaken +=)
        // before its first yield, so the health straight after the first
        // MoveNext is exactly this hit's result. Captured there, spoken later.
        // ------------------------------------------------------------------
        private static System.Collections.IEnumerator CaptureHealth(
            System.Collections.IEnumerator inner, DamageRecord record)
        {
            bool more = inner.MoveNext();
            try { record.HealthAfter = record.Card != null ? record.Card.Health : -1; }
            catch { record.HealthAfter = -1; }
            if (!more) yield break;
            yield return inner.Current;
            while (inner.MoveNext()) yield return inner.Current;
        }
    }

    // -------------------------------------------------------------------------
    // Card dies: "Stoat dies." / "Stoat is sacrificed."
    // A death caused by damage we have not spoken yet folds into that damage
    // line instead (see DamageDeathMerger). Sacrifices never fold — no damage
    // precedes them.
    // Parameter is "wasSacrifice" (no trailing 'd') — confirmed via reflection.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(PlayableCard), "Die")]
    public class PlayableCard_Die_Patch
    {
        // `killer` is the game's own answer to "what killed this" — the third
        // argument of PlayableCard.Die. Taken here so a death whose cause the
        // game states can say it. (0.7.301.)
        static void Prefix(PlayableCard __instance, bool wasSacrifice, PlayableCard killer)
        {
            if (__instance?.Info == null) return;
            string name = CardReader.CardName(__instance);

            // 0.7.348 — a giant dying mid-volley closes the volley first, so
            // the strikes that killed it are said before it is.
            GiantVolley.NoteDeath(__instance);

            // Session 14: every path out of this method ends in a spoken death
            // — sacrifice, damage-merged, an item's own verb, or the plain
            // line — so the diff watcher must not also report this card as
            // having left the board. Noted once, here, rather than four times
            // below, because the guarantee is the method's, not the branch's.
            BoardWatcher.NoteAnnounced(__instance);

            // Before anything can replace this card, record what is in hand —
            // SlotArrival needs it to say "from your hand" truthfully.
            HandSnapshot.Take();

            if (wasSacrifice)
            {
                // Deferred so the bone granted by this same sacrifice can fold
                // into the line before it is spoken. (Session 10 note 3.)
                // "Squirrel in Slot 1" — the slot is read HERE, before the
                // card leaves the board. By the time the line composes it has
                // no slot to report.
                string subject = name;
                try
                {
                    var dyingSlot = __instance.Slot;
                    if (dyingSlot != null) subject = Vocabulary.Combat.InSlot(name, dyingSlot.Index + 1);
                }
                catch { /* a bare name is still a true subject */ }

                bool folded;
                var record = SacrificeBoneMerger.OpenOrFold(name, subject, out folded);

                // If this card carries Morsel, the card it is being sacrificed
                // FOR is about to change. Record what it was, before it does.
                // BoardManager.CurrentSacrificeDemandingCard is the game's own
                // answer to "what is this sacrifice for".
                try
                {
                    var abilities = __instance.Info?.Abilities;
                    bool morsel = false;
                    if (abilities != null)
                        for (int i = 0; i < abilities.Count; i++)
                            if (abilities[i] == Ability.Morsel) { morsel = true; break; }

                    if (morsel && !record.WatchMorsel)
                    {
                        var receiver = Singleton<BoardManager>.Instance?.CurrentSacrificeDemandingCard;
                        if (receiver?.Info != null)
                        {
                            record.Receiver      = receiver;
                            record.ReceiverName  = CardReader.CardName(receiver);
                            record.AttackBefore  = receiver.Attack;
                            record.HealthBefore  = receiver.Health;
                            record.MorselSource  = name;
                            record.WatchMorsel   = true;

                            // 0.7.292 — WHAT MORSEL GRANTS, FROM MORSEL.
                            // Morsel.OnSacrifice builds
                            // new CardModificationInfo(Card.Attack, Card.Health)
                            // off THIS card and adds it to the demanding card.
                            // So the grant is the dying card's own live stats,
                            // and they are read here, at the moment the game
                            // would read them.
                            record.MorselAttack = __instance.Attack;
                            record.MorselHealth = __instance.Health;

                            // 0.7.297 — COMPOSED HERE, NOT WHEN THE SACRIFICE
                            // LINE SPEAKS.
                            //
                            // 0.7.295 set MorselGainLine inside Compose(), on
                            // the assumption that the sacrifice line always
                            // speaks first. His 0.7.296 log says otherwise:
                            //
                            //   364  Mealworm's Morsel ability triggers.
                            //   368  Mealworm in Slot 2 is sacrificed. ...
                            //
                            // The sacrifice line carries a 0.3s bone-merge
                            // settle; the trigger line does not, so it
                            // overtook it and found nothing to attach. Every
                            // value the sentence needs is already known HERE,
                            // at the moment the game decides what Morsel
                            // grants, so there is no ordering to get right.
                            SacrificeRecord.MorselGainLine = record.ComposeMorselGain();
                        }
                    }
                }
                catch { /* a missing gain line is quieter, never broken */ }

                // Folded into a sacrifice already queued. The Morsel watch above
                // was still worth arming — the SECOND card of a pair can be the
                // one carrying Morsel — but the line itself is already in the
                // queue and speaks for both. Queueing another would undo the
                // merge he asked for.
                if (folded) return;

                // Session 13: DELAYED as well as deferred, and widening the fold
                // window was not enough on its own. Zamar's 0.7.24 log has both
                // shapes again — "Squirrel is sacrificed. Received 1 bone." on
                // one sacrifice and "Mealworm is sacrificed." with the bone as a
                // separate line on the next. The window was never the problem:
                // when the queue is empty this line composes on the very next
                // frame, BEFORE the game has granted the bone, and a fold window
                // cannot help a line that has already been spoken.
                //
                // So hold it briefly. Settle before speaking — the standing
                // rule, and the settle costs the player nothing.
                using (Speech.Event(EventKind.Death, EventSource.CurrentPlayer)) Speech.Result(record.Compose, SacrificeBoneMerger.SettleSeconds);
                return;
            }

            // 0.7.350 — a giant's death is GiantCardNarrator's "is destroyed"
            // line. Zamar heard "The Limoncello dies." then "The Limoncello is
            // destroyed.": "Only the destroyed line is needed here."
            if (GiantVolley.IsGiant(__instance))
            {
                Plugin.Log?.LogInfo($"IKMA DEATH: '{name}' is a giant — its destroyed line covers it.");
                return;
            }

            // 0.7.350 — a cannonball kill is said on the cannon line.
            if (PirateSkullNarrator.TryCannonDeath(__instance))
                return;

            // 0.7.349 — a Brittle card dying after its attack dies in the
            // phase's Brittle line.
            if (BrittleGroup.TryRecordDeath(__instance))
                return;

            // 0.7.347 — a card killed inside a multi-strike attack dies in
            // that attack's summary line.
            if (MultiStrikeNarrator.TryRecordDeath(__instance))
                return;

            if (DamageDeathMerger.TryMarkDeath(__instance))
                return; // Folded into the pending damage line.

            // An item killed this card outright: use that item's own verb.
            // Checked AFTER the damage merger on purpose — if the kill did come
            // with a damage event, "X takes 2 damage and dies." is the more
            // informative line and should win. Scissors and Hammer kill without
            // damage, so they fall through to here. (Session 10.)
            string itemLine = ItemTargetOutcome.DeathLineFor(__instance, name);
            if (itemLine != null)
            {
                using (Speech.Event(EventKind.Death, EventTag.Side(__instance))) Speech.Result(itemLine);
                return;
            }

            // Deferred for the same reason DamageRecord.Compose is: a
            // replacement arriving in this slot has not landed yet at death
            // time, but will have by the time this reaches the front of the
            // queue.
            CardSlot diedIn = null;
            try { diedIn = __instance.Slot; } catch { diedIn = null; }
            var dying = __instance;
            var queuedAtDeath = SlotArrival.QueueSnapshot();

            // THE TRAP NAMES ITSELF. (0.7.301.) SteelTrap.OnDie passes the
            // trap as the killer, so this is the game saying what happened,
            // captured at the moment it says it rather than re-derived from
            // the board at speak time.
            string trapName = null;
            try
            {
                if (killer != null && killer.Info != null &&
                    killer.Info.HasAbility(Ability.SteelTrap))
                    trapName = CardReader.CardName(killer);
            }
            catch { }

            // THE PROSPECTOR'S PICKAXE OWNS ITS OWN NARRATION. (0.7.168.)
            //
            // Phase 2 destroys every card on the player's board and replaces
            // each with a Gold Nugget, and Zamar chose ONE line for the whole
            // sequence. Each of those deaths is an ordinary death from here, so
            // without this the summary line and the per-card lines both fire —
            // which is exactly what his 0.7.167 log showed, and the per-card
            // ones stomped each other badly enough that he never heard one of
            // them at all.
            //
            // Logged, not spoken: the record of every card is unchanged.
            if (ProspectorNarrator.WipeInProgress)
            {
                Plugin.Log?.LogInfo(
                    $"IKMA DEATH: '{name}' died inside the Prospector's wipe — " +
                    "not announced, the wipe line covers it.");
                return;
            }

            // 0.7.424 — a trap that is about to spring says so on its own
            // death line. Asked of the game at the moment it dies. See
            // TrapCatchNarrator.
            bool trapFires = TrapCatchNarrator.TrapWillFire(dying, wasSacrifice, killer);

            System.Func<string> compose = () =>
            {
                string arrival = SlotArrival.Describe(diedIn, dying, queuedAtDeath);
                // 0.7.219 — a thing is destroyed, a creature dies.
                CardInfo dyingInfo = null;
                try { dyingInfo = dying?.Info; } catch { }

                // 0.7.220 — DeathSentence, not a verb spliced onto the name: a
                // container's clause carries its own subject ("The cage is
                // destroyed.") and the card's name is dropped.
                string sentence;
                if (trapName != null)
                    sentence = Vocabulary.CaughtInTrapSentence(name, dyingInfo, trapName);
                else if (trapFires)
                    sentence = Vocabulary.TrapDestroyedTriggers(
                        name, dyingInfo, CardReader.GetAbilityName(Ability.SteelTrap));
                else
                    sentence = Vocabulary.DeathSentence(name, dyingInfo);
                return arrival == null ? sentence : $"{sentence} {arrival}";
            };

            // 0.7.424 — caught in a trap: the line keeps its place in the
            // queue and waits for the pelt, so one sentence says both.
            if (trapName != null)
            {
                using (Speech.Event(EventKind.Death, EventTag.Side(dying)))
                    TrapCatchNarrator.SpeakCatch(killer, compose, name);
                return;
            }

            using (Speech.Event(EventKind.Death, EventTag.Side(dying))) Speech.Result(compose);
        }
    }

    // -------------------------------------------------------------------------
    // Direct damage / scale tips.
    //
    // Scale value: reads LifeManager.Balance directly rather than recomputing
    // from OpponentDamage/PlayerDamage. Balance is the same live value the
    // game's own scale UI reads from, so it stays correct across mid-combat
    // scale resets that the cumulative damage counters don't reflect.
    // Sign convention unchanged from before: positive = player favor.
    // (Session 7 scale-tracking fix.)
    //
    // TEST CAVEAT: if ShowDamageSequence is a coroutine, this Postfix fires at
    // enumerator creation — before the weights drop — so Balance may read one
    // hit behind the screen. If testing confirms that lag, fold damage/toPlayer
    // into the announced value.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(LifeManager), "ShowDamageSequence")]
    public class LifeManager_ShowDamageSequence_Patch
    {
        // 0.7.362 — see the volume warning below.
        private static bool GrizzliesAreNext()
        {
            try
            {
                if (!SaveFile.IsAscension) return false;
                if (!AscensionSaveData.Data.ChallengeIsActive(AscensionChallenge.GrizzlyMode)) return false;
                var opp = Singleton<TurnManager>.Instance?.Opponent;
                if (!(opp is AnglerBossOpponent || opp is ProspectorBossOpponent || opp is TrapperTraderBossOpponent))
                    return false;
                return opp.NumLives >= 2;
            }
            catch { return false; }
        }

        static void Postfix(int damage, bool toPlayer)
        {
            if (damage <= 0) return;

            // Session 9: when an item moves the scales — Pliers puts a weight on
            // them — "You deal 1 direct damage" is the wrong frame for what the
            // player just did, and it duplicates the scales line that follows.
            // Suppress the damage sentence while an item is being used and let
            // the scales report the result. Grounded in ItemsManager's own
            // ActivatingItem flag, not in what key was pressed.
            var itemsManager = Singleton<ItemsManager>.Instance;
            bool duringItemUse = itemsManager != null && itemsManager.ActivatingItem;

            var lm = LifeManager.Instance;

            // ==================================================================
            // WHY THAT HIT WAS ONLY WORTH ONE. (0.7.194.)
            //
            // Zamar, Prospector fight:
            //
            //   "That one direct damage is only 1 because the scale hit 5. I'd
            //    like to call that out. 'You deal 1 direct damage. The scale
            //    hits 5...' Same for if you take that damage. 'You receive 1
            //    damage. The scale hits 5...' Ellipsis for emphasis."
            //
            // THIS IS THE SAME GAP 0.7.169 OPENED AND DID NOT CLOSE. That build
            // stopped speaking "Scales: you lead by 5." on his instruction — a
            // five is never a reading, it is the trigger for a candle going out,
            // and the number in front of the event was noise. Correct. But it
            // left the moment with NO marker at all, so a hit that ends a phase
            // sounds exactly like a hit that does not.
            //
            // A sighted player watches the scale slam to its stop. This is that,
            // in the damage line itself rather than as a second sentence,
            // because the cap is the REASON the number was one — one event, one
            // line, with the cause attached.
            //
            // The balance is computed first now, above the damage sentence
            // instead of below it. Order matters here in a way it did not
            // before: the sentence cannot carry the clause until the clause is
            // known.
            // ==================================================================
            bool atEdge = false;
            int balance = 0;

            if (lm != null)
            {
                // Session 8 fix (confirmed in test): ShowDamageSequence is a
                // coroutine, so this Postfix fires BEFORE the weights drop —
                // lm.Balance still holds the pre-hit value. Predict the post-hit
                // balance from the same damage/toPlayer the game is about to
                // apply. Positive = player favor; damage to the player moves the
                // balance down, damage dealt moves it up. Clamped at 5, where the
                // game ends regardless.
                balance = lm.Balance + (toPlayer ? -damage : damage);
                if (balance > 5) balance = 5;
                if (balance < -5) balance = -5;

                atEdge = balance >= 5 || balance <= -5;

                // Session 34 - rumble when the scale actually slams (Zamar:
                // left leads when you lose, right when you win). Armed here,
                // fired by Rumble when Balance reaches the edge.
                if (atEdge) { try { Rumble.ArmScaleEdge(playerLoses: toPlayer); } catch { } }
            }

            if (!duringItemUse)
            {
                // ==================================================================
                // THE NUMBER ON THE KILLING BLOW IS THE WHOLE SWING. (0.7.263.)
                //
                // Zamar: "For the direct damage I want to hear the full damage
                // amount for that big moment, not just the amount to get the
                // scale to hit 5." His log said "You deal 5 direct damage" for
                // a turn that dealt ten.
                //
                // THE GAME SPLITS THE NUMBER AND HANDS US THE SMALLER HALF.
                // CombatPhaseManager.DoCombatPhase:
                //
                //   excessDamage = Balance + DamageDealtThisPhase - 5;
                //   int damage   = DamageDealtThisPhase - excessDamage;
                //   ShowDamageSequence(damage, damage, ...)
                //
                // So the argument this postfix receives is what the scale could
                // still accept, and the remainder goes to the teeth as excess
                // lethal damage — which IKMA already reports in the victory
                // line. Ten swung, five weighed, five paid in teeth, and the
                // only number spoken was the five that fit.
                //
                // DamageDealtThisPhase is the game's own running total for the
                // phase and is PUBLIC, so this asks rather than reconstructs.
                // Only consulted at the edge: away from it the two figures are
                // identical, and this keeps a TurnManager lookup off the path
                // every ordinary hit takes. It is also floored at `damage`, so
                // a stale total from a phase that is not this one can never
                // shrink the number the game just passed in.
                // ==================================================================
                int spokenDamage = damage;
                if (atEdge)
                {
                    try
                    {
                        var cpm = Singleton<TurnManager>.Instance?.CombatPhaseManager;
                        int dealt = cpm != null ? cpm.DamageDealtThisPhase : 0;
                        if (dealt > spokenDamage)
                        {
                            Plugin.Log?.LogInfo(
                                $"IKMA DAMAGE: scale capped the hit at {damage}; the phase dealt " +
                                $"{dealt}. Speaking the full amount.");
                            spokenDamage = dealt;
                        }
                    }
                    catch (System.Exception e)
                    {
                        Plugin.Log?.LogWarning(
                            $"IKMA DAMAGE: phase total unreadable — {e.GetType().Name}. " +
                            "Speaking the capped amount.");
                    }
                }

                // 0.7.360 — TIPPED SCALES SAYS ONLY THE SCALE. Zamar, on the
                // StartingDamage challenge's opening hit: "Remove these lines.
                // Just keep the Scales: enemy leads by 1 line." TurnManager's
                // setup phase deals it (ShowDamageSequence under
                // ChallengeIsActive(StartingDamage)), so the setup phase with
                // that challenge active is the test. The scale reading below
                // still speaks.
                bool tippedScalesOpening = false;
                try
                {
                    var tmSetup = Singleton<TurnManager>.Instance;
                    tippedScalesOpening = toPlayer && tmSetup != null && tmSetup.IsSetupPhase
                        && AscensionSaveData.Data != null
                        && AscensionSaveData.Data.ChallengeIsActive(AscensionChallenge.StartingDamage);
                }
                catch { }
                if (tippedScalesOpening)
                {
                    Plugin.Log?.LogInfo("IKMA DAMAGE: Tipped Scales opening hit — damage line not spoken, the scale reading carries it.");
                    goto ScaleReading;
                }

                string dmgWord = Vocabulary.Combat.DirectDamageCount(spokenDamage);

                // ZAMAR'S WORDING, 2026-09-12, ellipsis included and deliberate.
                // Five either way — he asked for the same clause on both sides,
                // and the scale hitting its stop is the same event whichever
                // direction it tipped.
                string edge = atEdge ? Vocabulary.Combat.ScaleHits : "";

                string damageLine = Vocabulary.Combat.EnemyDealsOrYouDeal(toPlayer, dmgWord, edge);

                // 0.7.362 — A VOLUME WARNING BEFORE THE GRIZZLIES. Zamar: "If
                // the grizzlies challenge is active and the scales hit 5 and is
                // about to trigger the grizzlies spawning, I want this line to
                // instead say 'VOLUME WARNING: You deal 4 direct damage. The
                // scale hits 5...'" The game's own test: Angler, Prospector and
                // Trapper/Trader run GrizzlyGlitchSequence from
                // StartNewPhaseSequence when HasGrizzlyGlitchPhase (in KM:
                // ChallengeIsActive(GrizzlyMode)), and a new phase starts when a
                // candle goes out with at least one left, i.e. NumLives >= 2 now.
                if (!toPlayer && atEdge && GrizzliesAreNext())
                {
                    Plugin.Log?.LogInfo("IKMA DAMAGE: this hit starts the Grizzly phase — volume warning.");
                    damageLine = Vocabulary.Combat.VolumeWarning + damageLine;
                }

                // ==================================================================
                // A FIVE OUTRANKS EVERYTHING BEHIND IT. (0.7.247.)
                //
                // Zamar: "The scale hitting 5 should stomp all combat lines
                // before it still playing. (Pretty much all lines period)"
                //
                // His log, the turn that won the fight:
                //
                //   Flying Ant attacks empty Slot 1.
                //   Ant Queen attacks empty Slot 2.
                //   Worker Ant attacks empty Slot 4.
                //   You deal 4 direct damage. The scale hits 5...
                //   Victory. 5 extra teeth received.
                //
                // Three swings at empty slots, paced half a second apart, and
                // the line that ends the combat waiting its turn behind them.
                // The scale reaching its stop is not one more blow-by-blow — it
                // is the phase ending, and a sighted player sees it happen the
                // instant it happens.
                //
                // ClearQueue first, because the lines still pending describe the
                // swing-by-swing of a turn that is now over; then Confirmation,
                // which cuts whatever is in the air and is itself protected from
                // being cut by whatever the queue does next. Commentary the rest
                // of the time — unchanged, and most damage is not a five.
                // ==================================================================
                if (atEdge)
                {
                    // Session 46 (0.7.452): was ClearQueue. Still cut, no
                    // longer lost - see CombatAnnouncer.QuietQueueIntoHistory.
                    CombatAnnouncer.QuietQueueIntoHistory();
                    using (Speech.Event(EventKind.HpChanges, EventTag.Side(toPlayer))) Speech.Confirm(damageLine);
                }
                else
                {
                    using (Speech.Event(EventKind.HpChanges, EventTag.Side(toPlayer))) Speech.Commentary(damageLine);
                }

                // THE HELD SIGIL LINES GO HERE. (0.7.252.) Zamar: Waterborne
                // "should happen right before the Scales callout, after
                // everything else." The damage line is the last thing before the
                // scales, so this is that gap, named rather than timed.
                //
                // 0.7.359 — AND WHEN THE SCALE HITS 5 THERE IS NO GAP. The damage
                // line carries the scale clause and stomps every combat line
                // before it (0.7.247, above), so a held line released here was
                // the one line that outlived the stomp. Zamar, on "Kingfisher's
                // Waterborne ability triggers. It resurfaces." landing after "The
                // scale hits 5...": "This line should be stomped by The Scale
                // hits 5."
                if (atEdge) SigilTriggers.DropHeld("the scale hit 5, which stomps the lines before it");
                else        SigilTriggers.FlushHeld("the turn's damage has been read");
            }

            ScaleReading:
            if (lm == null) return;

            // AT THE EDGE OF THE SCALES, SAY NOTHING. (0.7.169.)
            //
            // Zamar, on "Scales: you lead by 5.": "This line shouldnt exist
            // cause if this is true either the game has ended the combat or a
            // phase transition is happening or something and this line is
            // uneeded."
            //
            // He is right, and the clamp three lines up is the proof — the
            // game ends the phase at five either way, which is why the value is
            // pinned there. So a five is never a scale READING, it is the
            // trigger for something the mod already announces: a boss losing a
            // candle, or the player losing one. Speaking it puts a redundant
            // number in front of the event that actually matters.
            //
            // Both edges, symmetrically. Minus five is the player's candle
            // going out and has its own line too.
            //
            // 0.7.194: still no scale READING here, but the moment is no longer
            // silent — the damage line above now carries "The scale hits 5...".
            if (atEdge)
            {
                Plugin.Log?.LogInfo(
                    $"IKMA SCALES: balance hit {balance} — no reading spoken; the " +
                    "damage line carries the clause.");
                return;
            }

            string scaleMessage = Vocabulary.Scales(balance);

            using (Speech.Event(EventKind.HpChanges)) Speech.Commentary(scaleMessage);
        }
    }

    // A bare ShowUntilInput — the game holding for a press outside a dialogue
    // event. Prefix only: the hold opens at enumerator creation, which is
    // exactly when the text goes up, and DialogueAdvancer owns closing it.
    public class TextDisplayer_ShowUntilInput_Patch
    {
        static void Prefix() => DialogueAdvancer.NoteShowUntilInput();
    }

    // -------------------------------------------------------------------------
    // Bones received.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(ResourcesManager), "AddBones")]
    public class ResourcesManager_AddBones_Patch
    {
        static void Postfix(int amount)
        {
            if (amount <= 0) return;

            // Session 32 (Act 1): before the Bones lesson the game adds bones
            // but draws no tokens (Part1ResourcesManager.ShowAddBones), so a
            // sighted player sees nothing. Returning HERE, before the folds,
            // also keeps the bone clause off the sacrifice, multi-strike,
            // Brittle and Prospector lines - they only learn of bones from
            // this postfix. Kaycee's Mod: unchanged (see PageGate).
            if (!PageGate.BonesShown) return;

            // A bone granted by a sacrifice belongs to the sacrifice's own line,
            // not to a second announcement a beat later. (Session 10 note 3.)
            if (SacrificeBoneMerger.TryFoldBones(amount)) return;

            // 0.7.349 — deaths inside one attack pay their bones on its line;
            // Brittle deaths on the Brittle line.
            if (MultiStrikeNarrator.TryFoldBones(amount)) return;
            if (BrittleGroup.TryFoldBones(amount)) return;

            // 0.7.425 — a bone paid while a trap's catch is waiting to be
            // spoken goes on that line. Zamar: "fold it in to the pelt line."
            if (TrapCatchNarrator.TryFoldBones(amount)) return;

            // Same idea, one event larger. Every card the Prospector's pickaxe
            // destroys pays a bone, so a three-card board wipe produced three
            // separate "1 bone received." lines interleaved with the deaths.
            // Zamar: "All the bone receives should be grouped together for this
            // sequence." Accumulated here and spoken as one line behind the
            // wipe line — the count is kept, the interruptions are not.
            if (ProspectorNarrator.TryFoldBones(amount)) return;

            // 0.7.352 — THE BOON PAYS DURING SETUP ONLY. This also tested
            // IsPlayerUpkeep, so the bone from a card the Pirate Skull's
            // cannons killed at the start of his turn was credited to the
            // boon. Zamar: "This wasnt true was it? Why was that there?"
            // BoonsHandler.ActivatePreCombatBoons runs inside
            // TurnManager.SetupPhase, where IsSetupPhase is true.
            var tm = TurnManager.Instance;
            bool isUpkeep = tm != null && tm.IsSetupPhase;

            string message;
            if (isUpkeep || amount == 8)
            {
                // 0.7.325 — THE GAME'S NAME FOR ITS OWN BOON. These two read
                // "Minor Bone Lord Boon" and "Major Bone Lord Boon"; the asset
                // says "Minor Boon of the Bone Lord" and "Boon of the Bone
                // Lord", and "Major" is not the game's word for anything. Same
                // rule as every other display name on this project. The
                // fallback keeps the old sentence if the lookup ever fails, so
                // a missing asset costs the name and not the line.
                if (amount == 1)
                    message = Vocabulary.Combat.ReceivedBoneFromThe(CardReader.BoonName(BoonData.Type.MinorStartingBones));
                else if (amount == 8)
                    message = Vocabulary.Combat.ReceivedBonesFromThe(CardReader.BoonName(BoonData.Type.StartingBones));
                else
                    message = Vocabulary.Combat.ReceivedBonesAtBattle(amount);
            }
            else
            {
                // Session 29, his pick of the near-duplicate pair: "Received 3
                // bones.", the sentence every other bone gain already used.
                message = Vocabulary.ReceivedBonesLine(amount);
            }

            using (Speech.Event(EventKind.Bones, EventSource.CurrentPlayer)) Speech.Commentary(message);
        }
    }

    // -------------------------------------------------------------------------
    // Turn announcements. (Session 8.)
    //
    // PlayerTurn/OpponentTurn are the coroutines TurnManager runs for each
    // side's turn (confirmed via reflection dump). A Prefix on a coroutine
    // method fires at enumerator creation — i.e. the moment the turn begins,
    // after the previous side's combat has fully resolved — so
    // LifeManager.Balance is settled here, unlike mid-combat reads.
    // Enqueued so they play after any pending death/damage announcements.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(TurnManager), "PlayerTurn")]
    public class TurnManager_PlayerTurn_Patch
    {
        // Identity of the Opponent we last announced an opening queue for.
        // TurnManager builds a fresh Opponent per battle, so a reference change
        // means "this is a new encounter" — no turn counter to guess at, and it
        // self-resets without needing a battle-start patch. Held as object so
        // no assumption is made about the concrete Opponent type.
        private static object _lastAnnouncedOpponent;

        static void Prefix()
        {
            // Scale phrase removed (Session 8 test): the post-damage combat
            // line already announces the scales; repeating it at turn start
            // was redundant.
            // A new turn ends any layer the player was left sitting in. Item
            // mode surviving the turn boundary meant H read the item help at the
            // start of the next turn, with every key still meaning what it means
            // in items.
            HotkeyManager.TurnStartedFlag = true;

            // BACKSTOP. A turn that deals no damage never reaches the flush
            // beside the scales, and a held line must never be stranded into the
            // next turn where it would describe the wrong board.
            SigilTriggers.FlushHeld("the turn changed");

            // THE DIFF DESCRIBES THE TURN THAT JUST ENDED, SO IT COMES FIRST.
            // (0.7.268.)
            //
            // Zamar, on a Rampager and a Rabbit swapping places at the end of
            // the enemy's turn: "The rabbit move should have read after the
            // Wild Bull's but before my turn."
            //
            // He is right and the fix is an ordering, not a timing. Both lines
            // report the same instant — the Wild Bull's own sigil narrates
            // itself as it moves, and the differ picks up the Rabbit that was
            // pushed aside — but "Your turn." was enqueued between them, so a
            // single event arrived split across a turn boundary. A line that
            // reports the previous turn, spoken after the announcement of the
            // next one, describes a sequence that did not happen; the same
            // defect class as the Porcupine ordering bug.
            //
            // Still deferred, and that is unchanged: it composes at speak time
            // because the turn-end sequence may still be settling as this
            // enumerator is created. Returns null on a turn where nothing
            // moved, and the announcer drops an empty provider without a sound
            // — so a quiet turn still opens on "Your turn." with nothing in
            // front of it.
            using (Speech.Event(EventKind.EnemyMoves)) Speech.Commentary(BoardWatcher.ComposeDiff);

            using (Speech.Event(EventKind.Turns, EventSource.CurrentPlayer)) Speech.Result(Vocabulary.Turns.YourTurn);

            // Session 9 note 2: on the FIRST player turn of an encounter — by
            // which point the opening hand is dealt and Leshy's opening queue
            // is set — read what's coming. Enqueued DEFERRED so the text is
            // built at speak time, not now: the queue can still be filling as
            // this enumerator is created, and a snapshot taken here could read
            // empty. It lands after the bone/boon and "Your turn." lines
            // already in the queue.
            var tm = TurnManager.Instance;
            object opponent = tm != null ? (object)tm.Opponent : null;
            if (opponent == null) return;
            if (ReferenceEquals(opponent, _lastAnnouncedOpponent)) return;

            _lastAnnouncedOpponent = opponent;

            // 0.7.194 — from here on, a card arriving in hand is an EVENT and
            // gets its own line. Everything before this point in the battle is
            // the opening deal, which the hand read three lines below covers.
            // See BossNarrator.ArmHandAnnouncements.
            BossNarrator.ArmHandAnnouncements(opponent);

            using (Speech.Event(EventKind.TurnStartReads)) Speech.Commentary(BoardReader.GetOpponentQueueText);

            // AND ANYTHING ALREADY ON THE ENEMY'S SIDE. (0.7.268.)
            //
            // Zamar: "If there is something that starts on the enemy board it
            // needs to also read here before my board after que. Frozen
            // Opposum would have been a surprise to players."
            //
            // 0.7.162 below is the same fix for the player's own row and it
            // only ever looked at one side of the table. His order, and it
            // reads outermost to nearest: what is coming, what is already
            // across from you, what is already yours, what you can play.
            using (Speech.Event(EventKind.TurnStartReads)) Speech.Commentary(BoardReader.GetOpponentBoardTextIfOccupied);

            // ANYTHING ALREADY ON THE PLAYER'S SIDE AT BATTLE START. (0.7.162.)
            //
            // Zamar, Session 19: "If a card starts on my field, like this
            // boulder, that should be read after the que on battle start."
            //
            // His 0.7.161 log has a Boulder sitting in Slot 4 from the opening
            // bell — a Kaycee's Mod challenge can seed the player's board — and
            // the mod read the enemy queue and the hand and said nothing about
            // the board he already had. A sighted player takes all three in at
            // once; two out of three is an incomplete opening picture, and the
            // missing third is the half he can act on immediately.
            //
            // Ordered here deliberately: AFTER the queue, BEFORE the hand.
            // Queue is what is coming, board is what is already down, hand is
            // what he can play — his stated order, and it reads outermost to
            // nearest.
            //
            // Deferred, like its neighbours, because the board can still be
            // settling as this enumerator is created. Returns null on an empty
            // board and the announcer drops it silently, so an ordinary
            // encounter gains no line at all.
            using (Speech.Event(EventKind.TurnStartReads)) Speech.Commentary(BoardReader.GetPlayerBoardTextIfOccupied);

            // Session 10: and then the other half of the picture. A sighted
            // player takes in Leshy's queue and their own opening hand in the
            // same glance; the mod was giving one and making the player ask for
            // the other. Names only — this is orientation, not a full read, and
            // C is still there for detail. Deferred for the same reason as the
            // queue: the hand is still being dealt as this enumerator is made.
            using (Speech.Event(EventKind.TurnStartReads)) Speech.Commentary(BoardReader.GetHandNamesText);
        }
    }

    [HarmonyPatch(typeof(TurnManager), "OpponentTurn")]
    public class TurnManager_OpponentTurn_Patch
    {
        static void Prefix(TurnManager __instance)
        {
            SigilTriggers.FlushHeld("the turn changed");

            // The other half of the diff pass. Strafe and its relatives answer
            // RespondsToTurnEnd, so the player's own movers resolve on the way
            // OUT of his turn and are reported here, at the top of the enemy's.
            //
            // 0.7.268 — ahead of "Enemy turn.", for the same reason as the
            // player's side above: these moves happened during the turn that
            // just finished, and naming the next turn before describing the
            // last one puts a consequence after the events that followed it.
            // The two halves of this pass must stay in step, so a change to
            // one is a change to both.
            //
            // 0.7.346 — NOW AFTER "Enemy turn.", by his ruling. Session 26:
            // the diff composes at speak time, and by then the enemy's queue
            // has moved down onto the board, so "Enemy X moves down to slot
            // N" was being said BEFORE "Enemy turn." — the enemy's first act
            // announced ahead of his turn. Asked, Zamar: "Should enemy board
            // changes be read after 'Enemy turn.'?" — "Yes". The player's
            // own end-of-turn movers are unaffected: their sigil lines were
            // queued when they fired, ahead of this, and the diff leaves a
            // pending mover alone (BoardWatcher.NoteMoverPending). The
            // "Your turn." side keeps the 0.7.268 order.
            using (Speech.Event(EventKind.Turns, EventSource.Enemies)) Speech.Result(Vocabulary.Turns.EnemyTurn);

            // Session 34 - Zamar's Hourglass line, "when the skip happens":
            // this prefix runs as the opponent's turn begins, which is when
            // the game reads Opponent.SkipNextTurn and skips. See
            // ItemUseNarrator.
            ItemUseNarrator.NoteOpponentTurnStarting(__instance);

            using (Speech.Event(EventKind.EnemyMoves)) Speech.Commentary(BoardWatcher.ComposeDiff);
        }
    }

    // -------------------------------------------------------------------------
    // Draw phase prompt. (Session 8.)
    //
    // CardDrawPiles3D.ChooseDraw is the coroutine the game runs when it opens
    // the player's draw choice (confirmed via reflection dump; CardDrawPiles3D
    // declares its own override, so we patch it rather than the base class).
    // Grounds the prompt in the game actually asking for a draw.
    // -------------------------------------------------------------------------
    // -------------------------------------------------------------------------
    // BOTH DECKS EMPTY: THE DRAW PHASE IS SKIPPED. (0.7.357.)
    //
    // CardDrawPiles.DrawPhaseSequence goes straight to ExhaustedSequence when
    // Exhausted (main deck AND side deck empty) and ChooseDraw — where the draw
    // prompt hangs — is never reached, so his turn began with nothing said
    // about the missing draw. Zamar's line. Registered by TryPatch.
    // -------------------------------------------------------------------------
    public static class CardDrawPiles_DrawPhaseSequence_Patch
    {
        private static System.Reflection.PropertyInfo _exhausted;

        public static void Prefix(CardDrawPiles __instance)
        {
            try
            {
                if (_exhausted == null)
                    _exhausted = typeof(CardDrawPiles).GetProperty("Exhausted",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                if (_exhausted == null || __instance == null) return;
                if (!(_exhausted.GetValue(__instance, null) is bool b) || !b) return;

                Plugin.Log?.LogInfo("IKMA DRAW: both decks empty — draw phase skipped.");
                using (Speech.Event(EventKind.Turns)) Speech.Result(Vocabulary.Turns.DrawPhaseSkippedDue);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA DRAW: exhausted check — {e.GetType().Name}");
            }
        }
    }

    [HarmonyPatch(typeof(CardDrawPiles3D), "ChooseDraw")]
    public class CardDrawPiles3D_ChooseDraw_Patch
    {
        static void Prefix()
        {
            // Session 13, from Zamar's 0.7.22 playtest: he pressed D before this
            // reached the front of the queue, then heard it tell him to press D.
            // A prompt is composed when it is queued and spoken later, so a
            // prompt that INSTRUCTS can be false by the time it is heard in a
            // way that a line which REPORTS never is.
            //
            // Deferred, so it composes at speak time and returns null — silently
            // dropped by the announcer — if the draw already happened.
            // PlayerHand.CardsDrawnThisTurn is the game's own counter (PUBLIC,
            // confirmed in dump_card_play.txt), so this asks the game whether the
            // player has drawn rather than tracking it ourselves.
            //
            // Any other queued line that tells the player to press something
            // wants this same treatment.
            int drawnAtPrompt = Singleton<PlayerHand>.Instance?.CardsDrawnThisTurn ?? 0;

            Speech.Commentary(() =>
            {
                int drawnNow = Singleton<PlayerHand>.Instance?.CardsDrawnThisTurn ?? 0;
                if (drawnNow > drawnAtPrompt)
                {
                    Plugin.Log?.LogInfo("IKMA DRAW: draw-phase prompt withdrawn — already drawn.");
                    return null;
                }
                return Vocabulary.DrawPhasePrompt();
            });
        }
    }

    // -------------------------------------------------------------------------
    // Dialogue narration. (Roadmap item 1, Session 8.)
    //
    // TextDisplayer.ShowMessage returns the final composed line of every piece
    // of dialogue in the game (confirmed via reflection dump) — Leshy, events,
    // tutorials. One Postfix narrates it all. Markup codes like [c:bR] color
    // tags and [w:1] waits are stripped; consecutive duplicates are skipped.
    // Enqueued through the announcer so dialogue interleaves cleanly with
    // combat events.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(TextDisplayer), "ShowMessage")]
    public class TextDisplayer_ShowMessage_Patch
    {
        private static string _lastMessage;

        // ==================================================================
        // THE NAME ONLY WHEN THE SPEAKER CHANGES. (0.7.356.)
        //
        // Zamar: "When the same character is speaking, don't re announce
        // their name every time. Only when it changes characters speaking (or
        // starts speaking as a character the first time.)" Three Pirate Skull
        // lines in a row each opened "Pirate Skull:". The last named speaker
        // is remembered until another character speaks, a scene loads, or a
        // battle ends.
        // ==================================================================
        private static string _lastSpeakerName;
        internal static void ResetSpeaker() => _lastSpeakerName = null;

        // When the game last displayed a fresh line of dialogue.
        // realtimeSinceStartup, so a slowed or paused moment cannot skew it.
        //
        // Read by HotkeyManager.WatchPlayOutcome, which needs to know whether
        // the game answered a refused card play in its own voice before it
        // considers saying anything itself. Stamped only for lines that are
        // actually spoken — a line suppressed as a duplicate was never heard,
        // so as far as the player is concerned the game stayed silent.
        internal static float LastLineTime = -999f;

        // Speaker enum value -> spoken name. Session 9: the raw enum value is
        // logged on every line ("IKMA DIALOGUE: speaker=...") so this table can
        // be corrected against what the game actually reports rather than
        // guessed at. Unmapped values fall through to a CamelCase prettify.
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static System.Collections.Generic.Dictionary<string, string> _speakerNames => Loc.PerLanguage(ref _speakerNamesCache, ref _speakerNamesLanguage, Build_speakerNames);
        private static System.Collections.Generic.Dictionary<string, string> Build_speakerNames() =>
            new System.Collections.Generic.Dictionary<string, string>
        {
            { "Leshy",      Vocabulary.Leshy },
            // Session 10: Act 1 routes every Leshy line through Speaker.Single —
            // confirmed from "IKMA DIALOGUE: speaker=Single" on his draw-refusal
            // barks. It read unattributed, so the game shouted at the player
            // with no indication of who was talking.
            //
            // "ACT 1 HAS EXACTLY ONE SPEAKER" WAS WRONG, AND A BOSS FIGHT IS
            // WHERE IT BREAKS. (0.7.167.) Zamar's log:
            //
            //   IKMA DIALOGUE: speaker=Single
            //   Leshy: HEEEEEEEE-HAAW! 'TWAS THE PROSPECTOR!
            //
            // His report: "This voice line is attributed to Leshy but should be
            // Prospector." Leshy puts on a mask and BECOMES the boss — the
            // intro sequence literally calls LeshyAnimationController.PutOnMask
            // — so during a boss fight every Single line is the boss talking.
            //
            // This entry is now the FALLBACK only. SingleSpeakerName() below
            // resolves it against the live opponent first, so the table is
            // still what answers when there is no boss.
            //
            // The Acts 2 and 3 caveat in the original comment still stands and
            // is still unaddressed.
            { "Single",     Vocabulary.Leshy },
            { "Trader",     Vocabulary.Trapper },
            { "Prospector", Vocabulary.Prospector },
            { "Angler",     Vocabulary.Angler },
            { "Trapper",    Vocabulary.Trapper },
            { "Bonelord",   Vocabulary.BoneLord },
            { "PirateSkull", Vocabulary.Dialogue.PirateSkull },   // 0.7.344 — his boss, KM Final Boss challenge
        };
        private static System.Collections.Generic.Dictionary<string, string> _speakerNamesCache;
        private static string _speakerNamesLanguage;

        /// <summary>
        /// Who Speaker.Single is RIGHT NOW. (0.7.167.)
        ///
        /// Returns the boss's name during a boss fight and null everywhere
        /// else, so the caller falls through to the table. Leshy wears a mask
        /// and speaks AS the boss, and the enum does not distinguish them —
        /// the opponent does.
        ///
        /// Same rule the candle line uses, read off the same object: one
        /// question asked of the game beats a table of names IKMA believes in.
        /// LeshyBossOpponent resolves to "Leshy" on its own, so the final boss
        /// needs no special case.
        ///
        /// Only Single is redirected. A line that names its own speaker —
        /// Prospector, Angler, Trapper — is already the game being explicit and
        /// is left alone.
        /// </summary>
        private static string SingleSpeakerName(string rawSpeaker)
        {
            // 0.7.286 — "Leshy" IS ALSO A DEFAULT, NOT ALWAYS A CLAIM.
            //
            // Zamar's 0.7.285 Mycologists log:
            //
            //   IKMA BOSS: mask identity 'Doctor' - lines attribute to it.
            //   IKMA DIALOGUE: speaker=Leshy
            //   IKMA SPEAK: Leshy: WE ARE THE MYCOLOGISTS, YES?
            //
            // His report: "when the doctor was talking it still said it was
            // Leshy." Two separate holes let that through.
            //
            // FIRST, only Speaker.Single was redirected, on the reasoning
            // that a line naming its own speaker is the game being explicit.
            // That reasoning holds for Prospector, Angler and Trapper, which
            // exist ONLY as those characters. It does not hold for Leshy: he
            // is the default speaker of Act 1 and he is also the man wearing
            // every mask, so "Leshy" is what the enum says whether or not a
            // face is on. The mask is the more specific fact.
            //
            // SECOND, the redirect needed a Part1BossOpponent, and four of
            // the six masks are worn at MAP NODES where no opponent exists at
            // all — the Mycologists among them. The mask identity answers
            // without one, which is exactly why it was introduced in 0.7.211.
            //
            // So: a worn mask outranks both. Any speaker the game is willing
            // to call Leshy becomes whoever is behind the face.
            //
            // Speaker.Mushroom is untouched, and that is the point of doing
            // this by name rather than blanket. The Mycologists mask has two
            // faces and the game distinguishes them itself — the doctor's
            // lines arrive as Leshy, the small face's as Mushroom. Rewriting
            // Mushroom would put the doctor's name on the other face's words.
            if (rawSpeaker == "Single" || rawSpeaker == "Leshy")
            {
                string mask = BossNarrator.MaskIdentity;

                // Session 34, Zamar: THE WOODCARVER NEVER SPEAKS. "I don't
                // believe the woodcarver ever speaks ... it should be Leshy,
                // even though he has the mask on. I think she's a special
                // case." Her node's lines are Leshy narrating ("THE DECREPIT
                // WOODCARVER APPEARED BEFORE YOU."), so they stay his.
                if (string.Equals(mask, "Woodcarver", System.StringComparison.OrdinalIgnoreCase))
                    return Vocabulary.Leshy;

                if (!string.IsNullOrEmpty(mask)) return mask;
            }

            if (rawSpeaker != "Single") return null;

            // THE MASK DECIDES, NOT THE OPPONENT OBJECT. (0.7.206.)
            //
            // Zamar's 0.7.205 log:
            //
            //   IKMA BOSS: mask off — lines attribute to Leshy again.
            //   IKMA SPEAK: Prospector: NEED A LIGHT?
            //
            // His report: "Need a light is a Leshy line." He is right, and the
            // bug is that this method never asked about the mask. The opponent
            // is still a ProspectorBossOpponent for the whole rest of the
            // battle — the reward chest, the relit candle, everything after the
            // mask comes off — so every one of those lines was being put in the
            // boss's mouth.
            //
            // BossNarrator.MaskIsOn is now the single source both this and
            // ActorName() read, which is what he asked for: the flip is the
            // game's own mask event, never our timing.
            if (!BossNarrator.MaskIsOn) return null;

            try
            {
                var boss = TurnManager.Instance?.Opponent as Part1BossOpponent;
                if (boss == null) return null;
                return BossNarrator.BossDisplayName(boss);
            }
            catch { return null; }
        }

        // Values that identify "no particular speaker" rather than a character.
        // Lines from these are spoken unattributed.
        private static readonly System.Collections.Generic.HashSet<string> _unattributed
            = new System.Collections.Generic.HashSet<string> { "None", "Default" };

        // Session 9 build fix: taking the speaker as a typed `Speaker`
        // parameter failed to compile — the enum is not a top-level type in
        // DiskCardGame (it is nested inside another class). Rather than guess
        // at the qualified name, take Harmony's boxed argument array and read
        // the speaker positionally. Verified signature:
        //   ShowMessage(String message, Emotion emotion,
        //               LetterAnimation letterAnimation, Speaker speaker,
        //               String[] variableStrings)
        // so the speaker sits at index 3. No type reference needed, and the
        // length guard means a signature change degrades to unattributed
        // dialogue rather than an exception.
        static void Postfix(string __result, object[] __args)
        {
            if (string.IsNullOrEmpty(__result)) return;

            string clean = CleanDialogue(__result);

            if (clean.Length == 0) return;

            // Session 13, Zamar's call: a character repeating themselves is the
            // game answering the player again, and a sighted player watches the
            // text reappear every time. Blanket duplicate suppression made the
            // second identical refusal silent, so pressing Enter twice on an
            // unaffordable card gave a bark and then nothing.
            //
            // The dedup still exists, but only inside a short window, which is
            // the part it was actually earning: the engine can fire ShowMessage
            // more than once for a single displayed line, and that double should
            // never be spoken twice. A genuine repeat seconds later is a new
            // event and is spoken again.
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (clean == _lastMessage && now - LastLineTime < DUPLICATE_WINDOW) return;

            _lastMessage = clean;
            LastLineTime = now;

            string rawSpeaker = (__args != null && __args.Length > 3 && __args[3] != null)
                ? __args[3].ToString()
                : Vocabulary.Dialogue.None;
            // 0.7.315 — the raw speaker enum, kept for correcting the attribution table. Behind Plugin.VerboseDiagnostics.
            if (Plugin.VerboseDiagnostics)
                Plugin.Log?.LogInfo($"IKMA DIALOGUE: speaker={rawSpeaker}");

            // A NEW LINE MEANS THE GAME IS HOLDING AGAIN. (0.7.134.) This is
            // what makes DialogueAdvancer's "the last press did nothing" latch
            // safe: the game clears it, IKMA never does, so the lockout can
            // never outlive the conversation and can never miss the next one.
            try { DialogueAdvancer.NoteNewLine(); } catch { }

            string spoken = clean;
            bool attributed = !_unattributed.Contains(rawSpeaker);
            if (attributed)
            {
                string name = SingleSpeakerName(rawSpeaker);
                if (name == null && !_speakerNames.TryGetValue(rawSpeaker, out name))
                    name = Prettify(rawSpeaker);
                spoken = name == _lastSpeakerName ? clean : Vocabulary.Dialogue.SpeakerLine(name, clean);
                _lastSpeakerName = name;

                // 0.7.449 - A LINE THAT IS ONLY DOTS. Zamar, Session 45, on
                // Leshy's "..." before "GO ON." in the last fight: "Have this
                // read as 'Leshy waits silently.'" A speech engine reads the
                // dots as nothing or as "dot dot dot"; on screen it is a pause.
                // 0.7.463 - Session 51, Zamar, asked about the other
                // characters' "..." lines: "same with their names though."
                string dots = clean.Trim();
                if (dots.Length > 0 && dots.Trim('.', '\u2026', ' ').Length == 0)
                    spoken = name == Vocabulary.Leshy
                        ? Vocabulary.Dialogue.LeshyWaitsSilently
                        : Vocabulary.Dialogue.WaitsSilently(name);
            }

            // 0.7.360 — THE CONCEDE LINE ALWAYS NAMES LESHY. Zamar: "This should
            // say Leshy as the speaker, every time, even if he previously spoke.
            // Since it's so sudden and unpredictable when he says it."
            if (SurrenderReader.TakeConcedeLine())
            {
                spoken = Vocabulary.Dialogue.SpeakerLine(Vocabulary.Leshy, clean);
                _lastSpeakerName = Vocabulary.Leshy;
            }

            if (attributed)
            {

                // 0.7.209 — a character spoke: rumble, at the moment the line
                // appears (with the sting), not when the words are read a
                // second later. Zamar's ask; pattern provisional. See
                // GamepadSupport.cs / Rumble.Dialogue.
                try { Rumble.Dialogue(rawSpeaker); } catch { }
            }

            // Session 11: an attributed line waits a second before it is spoken.
            // Leshy and the other characters have voice stings that play as the
            // text appears, and narration landing on top of them buries a piece
            // of audio design that is one of the best things about the game.
            // Zamar's call, and clearly right — a blind player should get the
            // performance and then the words, not the words over the
            // performance. Unattributed system text has no sting to wait for.
            //
            // Session 13, from Zamar's 0.7.20 test log: an attributed line also
            // takes priority now. It interrupts speech from outside the queue —
            // a card cost read, a prompt — and is inserted ahead of pending
            // commentary instead of appended behind it. A character speaking is
            // the game answering the player directly, and it was arriving after
            // the thing it was answering. It still never overtakes a combat
            // result, and the sting delay above still applies.
            if (attributed)
                using (Speech.Event(EventKind.Dialogue)) Speech.Dialogue(spoken, CHARACTER_VOICE_DELAY);
            else
                Speech.Commentary(spoken);
        }

        // How long an attributed line waits so the character's voice sting can
        // play first. Internal since Session 32: the talking cards use it too.
        // Session 52 (0.7.464), Zamar: "remove the delay from Leshy's speech"
        // - he wants it snappier. This was 1.0 from Session 11, held so the
        // words did not land on top of the voice sting. At 0 the line is
        // spoken as soon as it is queued (still ahead of commentary, still
        // never over a combat result). Put 1.0f back to restore the wait.
        internal const float CHARACTER_VOICE_DELAY = 0f;

        /// <summary>
        /// Game dialogue text made speakable. Moved out of Postfix in Session
        /// 32, unchanged, so the talking cards strip exactly the same markup.
        /// </summary>
        internal static string CleanDialogue(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            // Two markup families to strip, not one:
            //   [c:bR] / [w:1]  — the game's own inline codes (square brackets)
            //   <color=#D93846> — Unity rich text (angle brackets)
            // Session 9: only the square-bracket family was stripped, so NVDA
            // read colour hex codes aloud mid-sentence.
            string clean = System.Text.RegularExpressions.Regex
                .Replace(raw, @"\[[^\]]*\]", "");
            clean = System.Text.RegularExpressions.Regex
                .Replace(clean, @"<[^>]*>", "");
            clean = System.Text.RegularExpressions.Regex
                .Replace(clean, @"\s+", " ").Trim();
            return clean;
        }

        /// <summary>
        /// "Name: line", or just the line when the same character spoke last -
        /// the rule the Postfix applies, shared with the talking cards
        /// (Session 32) so a Stoat line after a Stoat line drops the name too,
        /// and a Leshy line after a Stoat line gets his back.
        /// </summary>
        internal static string AttributeLine(string name, string clean)
        {
            string spoken = name == _lastSpeakerName ? clean : Vocabulary.Dialogue.SpeakerLine(name, clean);
            _lastSpeakerName = name;
            return spoken;
        }

        // Identical text inside this window is the engine firing ShowMessage
        // twice for one displayed line. Outside it, the character said it again.
        private const float DUPLICATE_WINDOW = 0.5f;

        // "BoneLord" -> "Bone Lord". Fallback for any speaker not in the table.
        internal static string Prettify(string raw)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

    }

    // -------------------------------------------------------------------------
    // Manually-applied patches (see Awake). Not annotated, so PatchAll leaves
    // them alone and each failure is contained to its own feature.
    // -------------------------------------------------------------------------
    public static class BoardManager_ChooseTarget_Patch
    {
        // Signature confirmed via reflection dump:
        //   ChooseTarget(List<CardSlot> allTargets, List<CardSlot> validTargets,
        //                Action<CardSlot> targetSelectedCallback,
        //                Action<CardSlot> invalidTargetCallback,
        //                Action<CardSlot> slotCursorEnterCallback,
        //                Func<bool> cancelCondition, CursorType cursorType)
        // Fires when an item (Fish Hook, Scissors, Hammer, Trapper Knife — all
        // TargetSlotItem subclasses) asks which card to act on.
        public static void Prefix(List<CardSlot> allTargets, List<CardSlot> validTargets)
        {
            HotkeyManager.BeginTargeting(allTargets, validTargets);
        }
    }

    public static class AscensionMenuScreenTransition_OnEnable_Patch
    {
        // Each Kaycee's Mod screen carries one of these; OnEnable fires as that
        // screen becomes the active one. The game hands us the live instance,
        // so no scene searching is needed.
        public static void Postfix(AscensionMenuScreenTransition __instance)
        {
            MenuReader.SetActiveScreen(__instance);
        }
    }

    // -------------------------------------------------------------------------
    // Map arrival announcement. (Roadmap item 2, Session 8 draft.)
    // OnArriveAtNode is the coroutine run when the player lands on a node
    // (confirmed via reflection dump) — a Prefix fires as the arrival begins.
    //
    // Session 9 note 1: the old line ended with "Press M for paths ahead," but
    // arriving at a node hands control to that node's event — the map keys are
    // inactive at that moment, so the hint pointed at a key that does nothing.
    // The map-key hint now lives on the map-return announcement in
    // HotkeyManager, where it is actually true. Browse index is reset here so
    // the next map browse starts at option 1.
    // -------------------------------------------------------------------------
    // -------------------------------------------------------------------------
    // A devlog entry is opened. (Session 13.)
    //
    // Postfix, so the screen has taken the data before we read it back. Nothing
    // is spoken here — MenuReader holds the text and folds it into the screen's
    // own announcement, so the player gets one utterance instead of two racing.
    // -------------------------------------------------------------------------
        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class AscensionJournalEntryScreen_InitializeWithEntry_Patch
    {
        public static void Postfix(AscensionJournalData entryData)
        {
            if (entryData == null) return;
            MenuReader.SetPendingEntry(entryData.entryId, entryData.BodyText);
        }
    }

    // -------------------------------------------------------------------------
    // The base game's title screen hands over its controller. (0.7.231.)
    //
    // Postfix on MenuController.Start, which is PRIVATE — TryPatch's lookup
    // includes NonPublic on both sides, so this needs no attribute and no
    // reflection here. Nothing is spoken from the patch: it hands the instance
    // to TitleScreenReader, which waits for the cards to settle the same way
    // every other screen reader does.
    // -------------------------------------------------------------------------
    // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    public class MenuController_Start_Patch
    {
        public static void Postfix(MenuController __instance)
        {
            TitleScreenReader.Capture(__instance);
        }
    }

    // -------------------------------------------------------------------------
    // The boot screen hands over its controller, and then its press. (0.7.232.)
    // Registered through Plugin.TryPatch; no [HarmonyPatch] attribute.
    // -------------------------------------------------------------------------
    public class FirstPlaySceneController_Initialize_Patch
    {
        public static void Postfix(FirstPlaySceneController __instance)
        {
            BootScreenReader.Capture(__instance);
        }
    }

    public class FirstPlaySceneController_OnStartButtonPressed_Patch
    {
        public static void Postfix()
        {
            BootScreenReader.OnPressed();
        }
    }

    // -------------------------------------------------------------------------
    // The Start scene, and which of its two lives this one is. (0.7.233.)
    // Registered through Plugin.TryPatch; no [HarmonyPatch] attribute.
    // -------------------------------------------------------------------------
    public class StartScreenController_Start_Patch
    {
        public static void Prefix(StartScreenController __instance)
        {
            BootScreenReader.OnStartScreen(__instance);

            bool started   = false;
            bool ascension = false;
            try { started   = StartScreenController.startedGame; } catch { }
            try { ascension = SaveFile.IsAscension; }              catch { }

            Plugin.OnStartScreenEntered(started, ascension);
        }
    }

    // -------------------------------------------------------------------------
    // The post-battle card reward. (Session 13, roadmap item 5.)
    //
    // CardSelectionSequence is PUBLIC and virtual on CardChoicesSequencer, and
    // CardSingleChoicesSequencer overrides it (confirmed, dump_card_choice.txt).
    // Patching the BASE declaration catches the single-choice screen through its
    // override and leaves room for the other choice sequencers later.
    //
    // This is a coroutine, so the Prefix fires at enumerator creation — before
    // any cards are spawned. It only hands the live sequencer over;
    // CardChoiceReader waits for the layout to settle before saying anything.
    // -------------------------------------------------------------------------
        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    // The boss reward chest. A SIBLING entry point, not an override — see the
    // registration for why neither existing patch could reach it.
    public class RareCardChoicesSequencer_ChooseRareCard_Patch
    {
        public static void Prefix(RareCardChoicesSequencer __instance)
        {
            Plugin.Log?.LogInfo("IKMA CHOICE: boss reward chest opening.");
            CardChoiceReader.Begin(__instance);
        }
    }

    public class CardChoicesSequencer_CardSelectionSequence_Patch
    {
        // PUBLIC, and it has to be: TryPatch resolves this by name with a
        // plain GetMethod, which only sees public members. Declared private
        // (the C# default) it resolves to null and Harmony throws on the null
        // patch method, which reads like the TARGET was missing. Session 13
        // lost a build to exactly that.
        public static void Prefix(CardChoicesSequencer __instance)
        {
            CardChoiceReader.Begin(__instance);
        }
    }

    // A card was turned over. Postfix, not Prefix: let the game do the flip and
    // then report what it turned out to be.
        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class CardSingleChoicesSequencer_OnCardFlipped_Patch
    {
        public static void Postfix(SelectableCard card)
        {
            CardChoiceReader.OnFlipped(card);
        }
    }

    // The reward was taken and goes into the deck.
        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class CardSingleChoicesSequencer_OnRewardChosen_Patch
    {
        public static void Postfix(SelectableCard card)
        {
            CardChoiceReader.OnChosen(card);
        }
    }

    // The chosen card is in the deck and the node is over. The only end signal
    // that is true for a Random node and a Cost node alike.
        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    public class CardSingleChoicesSequencer_AddChosenCardToDeck_Patch
    {
        public static void Postfix()
        {
            CardChoiceReader.OnAddedToDeck();
        }
    }

    // -------------------------------------------------------------------------
    // THE SACRIFICE STONE, AFTER THE RITUAL.
    //
    // Zamar, 0.7.246: "After the ritual is complete I want to hear 'Card name
    // received [x] Sigil(s)'."
    //
    // The stone's whole point is the sigils that move from the sacrifice to the
    // host, and a blind player watching the host card re-render learns nothing.
    // CardMergeSequencer.SacrificeOffersNewAbility is the game's own test for
    // which abilities count — asked of the game rather than reimplemented, the
    // same rule as DuplicateInDeck and CanAttackDirectly — so this counts the
    // sacrifice's abilities the host does not already have.
    //
    // PREFIX. ModifyHostCard mutates the host through playerDeck.ModifyCard, so
    // by the time a Postfix ran, HasAbility would answer true for every one of
    // them and the count would always be zero.
        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the prefix would run TWICE.
    // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the prefix would run TWICE.
    public class BuildTotemSequencer_AssembleTotem_Patch
    {
        public static void Prefix(SelectableItemSlot topSlot, SelectableItemSlot bottomSlot)
        {
            try
            {
                Tribe   tribe   = Tribe.None;
                Ability ability = Ability.None;

                try { tribe   = (topSlot?.Item?.Data as TotemTopData)?.prerequisites?.tribe ?? Tribe.None; }
                catch { }
                try { ability = (bottomSlot?.Item?.Data as TotemBottomData)?.effectParams?.ability ?? Ability.None; }
                catch { }

                string sigil = CardReader.GetAbilityName(ability);

                if (tribe == Tribe.None || string.IsNullOrEmpty(sigil))
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA TOTEM: assembled, but it could not be read (tribe={tribe}, ability={ability}).");
                    return;
                }

                // IKMA PROVISIONAL — Zamar has not ruled on this wording. It
                // uses his own words for the screen: Kin type, Sigil, backpack.
                // Session 34: no reselect offer when the game assembled it
                // itself - it gives no way back (NodeScreenReader.TotemAutoAssembles).
                string line = NodeScreenReader.TotemAutoAssembles()
                    ? Vocabulary.NodeScreens.TotemIsAssembledAuto(tribe, sigil)
                    : Vocabulary.NodeScreens.TotemIsAssembledYour(tribe, sigil);


                // A Result, so it takes its turn behind the carving being taken
                // rather than cutting across it.
                using (Speech.Event(EventKind.NodeResults)) Speech.Result(line);
                NodeScreenReader.LastTotemAssembledLine = line;   // Session 34: X / Space repeats it
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning(
                    $"IKMA TOTEM: reading the assembled totem threw {e.GetType().Name}: {e.Message}");
            }
        }
    }

    public class CardMergeSequencer_ModifyHostCard_Patch
    {
        public static void Prefix(CardInfo hostCardInfo, CardInfo sacrificeCardInfo)
        {
            try
            {
                if (hostCardInfo == null || sacrificeCardInfo == null) return;

                // NAME THEM. (0.7.255.) Zamar: "It should say which specific
                // sigil(s) it received."
                //
                // A count answers "did anything happen"; the whole point of the
                // stone is WHICH sigil moved, and that is what the host card now
                // carries for the rest of the run. Names come through
                // CardReader.GetAbilityName, the one place every sigil name in
                // the mod is resolved, so this reads the same words the card
                // itself will read from here on.
                //
                // Duplicates collapse: the same sigil twice on the sacrifice is
                // one thing the host gains.
                var gainedNames = new System.Collections.Generic.List<string>();
                var abilities = sacrificeCardInfo.Abilities;
                if (abilities != null)
                {
                    foreach (var a in abilities)
                    {
                        if (hostCardInfo.HasAbility(a)) continue;

                        string sigil = CardReader.GetAbilityName(a);
                        if (string.IsNullOrEmpty(sigil)) continue;
                        if (gainedNames.Contains(sigil)) continue;

                        gainedNames.Add(sigil);
                    }
                }

                string name = CardReader.CardName(hostCardInfo);

                string line;
                if (gainedNames.Count == 0)
                {
                    line = Vocabulary.NodeScreens.ReceivedNoNewSigils(name);
                }
                else if (gainedNames.Count == 1)
                {
                    line = Vocabulary.NodeScreens.Received(name, gainedNames[0]);
                }
                else
                {
                    // "A, B and C" — the shape every list in the mod uses.
                    var rest = gainedNames.GetRange(0, gainedNames.Count - 1);
                    line = Vocabulary.NodeScreens.ReceivedAnd(name, rest.ToArray(), gainedNames[gainedNames.Count - 1]);
                }

                Plugin.Log?.LogInfo($"IKMA MERGE: {line}");

                // A Result, so it takes its turn behind the stone's own sounds
                // rather than cutting across the transformation the player is
                // listening to.
                using (Speech.Event(EventKind.NodeResults)) Speech.Result(line);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning(
                    $"IKMA MERGE: reading the ritual result threw {e.GetType().Name}: {e.Message}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // SESSION 16 — THE RUN END SCREEN.
    //
    // Postfix so isVictory and the title are already set. The victory flag is
    // taken from the method's own parameter rather than read back off the
    // instance: never read state back immediately after an async call, and
    // Initialize starts work that has not necessarily finished.
    // -------------------------------------------------------------------------
        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class AscensionRunEndScreen_Initialize_Patch
    {
        public static void Postfix(AscensionRunEndScreen __instance, bool victory)
        {
            RunEndReader.Begin(__instance, victory);
        }
    }

    // -------------------------------------------------------------------------
    // THE DEATH CARD SCREEN. Not a reader — this only tells the
    // unreadable-screen net where the player is, so the screen says something
    // honest instead of nothing.
    //
    // Naming a card there is free text entry through the game's own
    // KeyboardInputHandler, which is a different accessibility problem from
    // anything IKMA has solved, and Zamar's call was to make it not silent now
    // and build the real reader later.
    // -------------------------------------------------------------------------
        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class DeathCardCreationSequencer_CreateCardSequence_Patch
    {
        public static void Prefix()
        {
            HotkeyManager.SetExplicitUnreadableContext(Vocabulary.DeathCard);
        }
    }

    // -------------------------------------------------------------------------
    // SESSION 16 — THE NODE SURVEY. Every patch below is log-only.
    //
    // A coroutine Prefix fires at ENUMERATOR CREATION, so nothing exists to
    // look at yet — the screen has not laid anything out. That is why these
    // hand the sequencer to NodeProbe and NodeProbe samples it on a timer
    // instead of reading anything here.
    // -------------------------------------------------------------------------
        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class CardStatBoostSequencer_StatBoostSequence_Patch
    {
        public static void Prefix(CardStatBoostSequencer __instance)
        {
            NodeProbe.Begin(__instance, Vocabulary.Campfire);
        }
    }

        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class CardMergeSequencer_MergeSequence_Patch
    {
        public static void Prefix(CardMergeSequencer __instance)
        {
            NodeProbe.Begin(__instance, Vocabulary.SacrificeStone);
        }
    }

    // DuplicateMergeSequencer.MergeSequence is a DIFFERENT declaration on a
    // different class, not an override of the one above — confirmed in the
    // override audit. Two classes, two patches, and neither is redundant.
        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class DuplicateMergeSequencer_MergeSequence_Patch
    {
        public static void Prefix(DuplicateMergeSequencer __instance)
        {
            NodeProbe.Begin(__instance, Vocabulary.Mycologists);
        }
    }

        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class TradePeltsSequencer_TradePelts_Patch
    {
        public static void Prefix(TradePeltsSequencer __instance)
        {
            NodeProbe.Begin(__instance, Vocabulary.Trader);
        }
    }

        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class BuyPeltsSequencer_BuyPelts_Patch
    {
        public static void Prefix(BuyPeltsSequencer __instance)
        {
            NodeProbe.Begin(__instance, Vocabulary.Trapper);
        }
    }

    // ---------------------------------------------------------------------
    // THE LAST FOUR KM NODES. (0.7.214.) Two shapes, four screens, no new
    // reader. See the registrations for why each goes where it goes.
    // ---------------------------------------------------------------------

    public class BoulderChoiceSequencer_BoulderChoiceSequence_Patch
    {
        public static void Prefix(BoulderChoiceSequencer __instance)
        {
            Plugin.Log?.LogInfo("IKMA NODE: boulder choice opening.");
            CardChoiceReader.Begin(__instance);
        }
    }

    public class DeckTrialSequencer_DeckTrialSequence_Patch
    {
        public static void Prefix(DeckTrialSequencer __instance)
        {
            Plugin.Log?.LogInfo("IKMA NODE: deck trial opening.");
            CardChoiceReader.Begin(__instance);
        }
    }

    // The Deck Trial's two exits - see CardChoiceReader.OnDeckTrialRewardAdded.
    // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE.
    public class DeckTrialSequencer_AddRewardCardToDeck_Patch
    {
        public static void Postfix(SelectableCard rewardCard)
        {
            CardChoiceReader.OnDeckTrialRewardAdded(rewardCard);
        }
    }

    public class DeckTrialSequencer_ReturnToMap_Patch
    {
        public static void Postfix()
        {
            CardChoiceReader.OnDeckTrialReturnToMap();
        }
    }

    // A Deck Trial reward card was turned over. Same reader call as the
    // ordinary card choice flip, so the line and its dedupe are shared.
    // Registered through Plugin.TryPatch. No HarmonyPatch attribute.
    public class DeckTrialSequencer_OnRewardCardFlipped_Patch
    {
        public static void Postfix(SelectableCard rewardCard)
        {
            CardChoiceReader.OnFlipped(rewardCard);
        }
    }

    public class CardRemoveSequencer_RemoveSequence_Patch
    {
        public static void Prefix(CardRemoveSequencer __instance)
        {
            NodeProbe.Begin(__instance, Vocabulary.BoneLord);
        }
    }

    public static class CardRemoveSequencer_OnBoonSelected_Patch
    {
        public static void Postfix(MainInputInteractable boonCard)
        {
            try { NodeScreenReader.NoteBoonTaken(boonCard); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: boon taken postfix threw {e.GetType().Name}.");
            }
        }
    }

    public class CopyCardSequencer_CopyCardSequence_Patch
    {
        public static void Prefix(CopyCardSequencer __instance)
        {
            NodeProbe.Begin(__instance, Vocabulary.NodeScreens.CopyCard);
        }
    }

        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class GainConsumablesSequencer_ReplenishConsumables_Patch
    {
        public static void Prefix(GainConsumablesSequencer __instance)
        {
            NodeProbe.Begin(__instance, Vocabulary.ItemPickupName);
        }
    }

        // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    // (0.7.152 — his "Sparrow added to your deck." said twice.)
    public class BuildTotemSequencer_BuildTotem_Patch
    {
        public static void Prefix(BuildTotemSequencer __instance)
        {
            NodeProbe.Begin(__instance, Vocabulary.Woodcarver);
        }
    }

    [HarmonyPatch(typeof(MapNode), "OnArriveAtNode")]
    public class MapNode_OnArriveAtNode_Patch
    {
        static void Prefix(MapNode __instance)
        {
            MapReader.ResetChoiceIndex();
            string name = MapReader.GetNodeFriendlyName(__instance);
            using (Speech.Event(EventKind.NodeEntered)) Speech.Result(Vocabulary.Map.ArrivedAt(name));
        }
    }

    // -------------------------------------------------------------------------
    // Backspace cancel injection. (Bug 3, final approach — Session 8.)
    //
    // InputButtons.GetButtonDown(Button) is the static poll the game's wait
    // loops use to ask "was this button pressed?" (confirmed via reflection
    // dump). For two frames after Backspace is pressed during play flow,
    // answer YES for the cancel-named button. The game then runs its own
    // cancel logic with all its own guards — no state is ever written, so the
    // flag-poisoning softlock from the previous approach is structurally
    // impossible. Right-click was confirmed non-functional in test, so this
    // injection is the real cancel path.
    // -------------------------------------------------------------------------
    // Session 8 test: the slot-choice wait ignored GetButtonDown injection, so
    // the loop must poll one of the other accessors — inject into all three
    // (GetButtonDown, GetButton, GetButtonRepeating) during the same window.
    // If cancel STILL refuses, the wait doesn't go through InputButtons at all
    // and the next dive targets what ChooseSlot actually polls.
    //
    // SESSION 13 — IT STILL REFUSES, AND THAT PREDICTION IS NOW A RESULT.
    // Zamar's 0.7.21 log: slot placement for Squirrel, canCancel true, three
    // separate Backspace presses, three "CANCEL: state unchanged — game did not
    // accept the cancel." All three accessors were patched and answering yes.
    // So the slot-choice wait does not read cancel through InputButtons at all,
    // and no amount of further injection here will change that.
    //
    // These patches stay: they are observe-and-inject only, they write no game
    // state, and they remain the right mechanism if another wait DOES poll
    // InputButtons. The next dive is what ChooseSlot actually reads.
    // dump_menus_targeting.txt names one candidate worth starting from:
    // BoardManager holds a NONPUBLIC bool `cancelledPlacementWithInput`.
    //
    // SESSION 14 CORRECTION. This comment used to pair that with
    // `cancelledByClickingCard` and conclude that placement "may be cancelled
    // by CLICKING rather than by a button". That does not hold.
    // `cancelledByClickingCard` is a compiler-generated closure local inside
    // CardStatBoostSequencer.StatBoostSequence — the CAMPFIRE, not placement.
    // Two fields with similar names in the same dump folder, and the
    // inference was drawn across them. The click theory may still be right,
    // but nothing here is evidence for it.
    //
    // So `cancelledPlacementWithInput` is the only lead that is actually about
    // placement, and HotkeyManager.WatchCancelOutcome now watches it while a
    // cancel is in flight rather than guessing. Do not write it directly (see
    // the hard rules) and do not try a second cancel route until the log says
    // something — a wrong guess here softlocks a placement.
    [HarmonyPatch(typeof(InputButtons), "GetButtonDown")]
    public class InputButtons_GetButtonDown_Patch
    {
        internal static void InjectCancel(Button button, ref bool __result)
        {
            if (__result) return;

            // GENERALISED, Session 16. This used to match only on "cancel" as a
            // substring. It now answers for whichever button HotkeyManager has
            // armed, by EXACT name — because the camera ladder is driven by
            // pressing Button.LookUp and Button.LookDown, and a substring match
            // on "look" would also answer for LookLeft and LookRight.
            //
            // Why this is the right mechanism and not a workaround: a scroll of
            // the wheel IS this button. Injecting it asks the game to do
            // whatever a wheel click does — move the camera, lay the deck out,
            // change the control mode, anything nobody has found yet — instead
            // of IKMA reproducing a part of it and getting the rest wrong. Three
            // builds were lost to reproducing it. See MapReader's ladder block.
            if (UnityEngine.Time.frameCount > HotkeyManager.InjectUntilFrame) return;

            string armed = HotkeyManager.InjectButtonName;
            if (string.IsNullOrEmpty(armed)) return;

            if (!string.Equals(button.ToString(), armed, System.StringComparison.OrdinalIgnoreCase))
                return;

            __result = true;
        }

        static void Postfix(Button button, ref bool __result) => InjectCancel(button, ref __result);

        /// <summary>
        /// For the HELD queries (GetButton / GetButtonRepeating). A press-only
        /// injection deliberately answers nothing here, so a single keystroke
        /// cannot read as a button being held down.
        /// </summary>
        internal static void InjectHeld(Button button, ref bool __result)
        {
            if (HotkeyManager.InjectDownOnly) return;
            InjectCancel(button, ref __result);
        }
    }

    [HarmonyPatch(typeof(InputButtons), "GetButton")]
    public class InputButtons_GetButton_Patch
    {
        static void Postfix(Button button, ref bool __result)
            => InputButtons_GetButtonDown_Patch.InjectHeld(button, ref __result);
    }

    [HarmonyPatch(typeof(InputButtons), "GetButtonRepeating")]
    public class InputButtons_GetButtonRepeating_Patch
    {
        static void Postfix(Button button, ref bool __result)
            => InputButtons_GetButtonDown_Patch.InjectHeld(button, ref __result);
    }

    // -------------------------------------------------------------------------
    // Sacrifice selection begins.
    // Backspace hint removed (audit): Backspace handling was pulled in Session 6
    // (Bug 3) and Rewired keyboard is suppressed, so the key is currently dead.
    // Re-add the hint when Bug 3 gets a real cancel implementation.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(BoardManager), "ChooseSacrificesForCard")]
    public class BoardManager_ChooseSacrificesForCard_Patch
    {
        static void Prefix(List<CardSlot> validSlots, PlayableCard card)
        {
            // ==================================================================
            // THE GAME'S OWN LIST OF UNCHOSEN SACRIFICES. (0.7.143.)
            //
            // Zamar: "Once a card is chosen to be sacrificed ... It should no
            // longer be included in Tab's next valid target selection."
            //
            // dump_sacrifice.txt answered it from the compiler's generated
            // iterator, which is not anywhere I would have guessed:
            //
            //   BoardManager+<ChooseSacrificesForCard>d__80
            //     PUBLIC  PlayableCard  card
            //     PUBLIC  List`1        validSlots     <- hoisted local
            //     NONPUBLIC Enumerator  <>7__wrap1     <- the foreach that crashed
            //
            // validSlots is passed IN by the caller, so taking it here gives IKMA
            // a reference to THE SAME LIST the coroutine walks and mutates. As
            // each sacrifice is chosen the game drops its slot and the card falls
            // out of Tab on its own — nothing cached, nothing counted, and a
            // mouse click reflected instantly because there is only one list.
            //
            // ADDED TO THE EXISTING PATCH RATHER THAN A SECOND ONE. I wrote a
            // whole new class for this and the compiler rejected it: the method
            // was already patched, right here, for the focus snap. Two patches on
            // one method is two places to keep in step for no reason. Grep for
            // the target before writing a patch class.
            // ==================================================================
            HotkeyManager.NoteSacrificeCandidates(validSlots, card);

            if (card?.Info == null) return;

            // Session 9: the browse cursor used to stay wherever card placement
            // left it — usually slot 1 — so pressing Enter on a board whose only
            // creature sat in slot 3 answered "No card in this slot." Snap to a
            // slot that can actually be sacrificed before the prompt is spoken.
            HotkeyManager.FocusFirstSacrificeSlot();

            int cost = card.Info.BloodCost;
            string sacrificeWord = Vocabulary.PlayFlow.SacrificeCount(cost);

            SacrificeProgress.Begin(cost);

            // Session 10 note 2: this prompt fires synchronously inside
            // OnCardSelected, one instant after HotkeyManager says "Playing
            // Mantis God." — and an interrupt=true prompt cut that line off
            // mid-word every single time. Queued instead, so the announcer's
            // post-interrupt holdoff lets the play line finish first. Nothing
            // here is reaction-timed; the half-second costs the player nothing.
            // Deferred for the same reason as the slot prompt below: an
            // instructing line must be true when it is SPOKEN, not when it is
            // queued. BoardManager.CurrentSacrificeDemandingCard is the game's
            // own answer to "am I still waiting for sacrifices for this card".
            string sacrificeMessage =
                Vocabulary.PlayFlow.CostsBloodChooseLeft(CardReader.CardName(card), cost, sacrificeWord);
            var demanding = card;

            // Session 14: Prompt tier. It was already deferred and already
            // withdrew itself; what it could not do was get past orientation
            // reads queued ahead of it at a turn boundary. See
            // Speech.Prompt.
            Speech.Prompt(() =>
            {
                var bm = Singleton<BoardManager>.Instance;
                if (bm != null && !ReferenceEquals(bm.CurrentSacrificeDemandingCard, demanding))
                {
                    Plugin.Log?.LogInfo("IKMA PROMPT: sacrifice prompt withdrawn — sacrifices already chosen.");
                    return null;
                }
                return sacrificeMessage;
            });
        }
    }

    // -------------------------------------------------------------------------
    // Slot selection begins.
    //
    // If CombatAnnouncer has pending messages (e.g. a sacrifice just queued
    // "X is sacrificed." / "N bone(s) received."), enqueue this prompt so it
    // plays after them in order. Otherwise (normal card play, no sacrifice),
    // speak it immediately as the player's next instruction -- nothing queued
    // to preserve ordering against. (Session 7, note 2.)
    //
    // Backspace hint removed (audit) — same reason as above; canCancel is kept
    // in the signature for when Bug 3 restores a working cancel.
    // -------------------------------------------------------------------------
    // -------------------------------------------------------------------------
    // Loading announcements. (Session 10.)
    //
    // Both targets are PUBLIC STATIC and confirmed by reflection dump, so the
    // scene name arrives as a plain string argument. It is logged but not
    // spoken — "Loading." is what the player needs; "Loading Part1_Cabin" is
    // an internal identifier and reading it out would be noise.
    //
    // Nothing announces completion. The arriving context announces itself and
    // interrupts, which is the stomp Zamar asked for without an extra word.
    // -------------------------------------------------------------------------
    public class SceneLoader_Load_Patch
    {
        public static void Prefix(string sceneName)
        {
            Plugin.AnnounceLoading($"SceneLoader.Load({sceneName})", sceneName);
        }
    }

    public class LoadingScreenManager_LoadScene_Patch
    {
        public static void Prefix(string sceneName)
        {
            Plugin.AnnounceLoading($"LoadingScreenManager.LoadScene({sceneName})", sceneName);
        }
    }

    [HarmonyPatch(typeof(BoardManager), "ChooseSlot")]
    public class BoardManager_ChooseSlot_Patch
    {
        static void Prefix(System.Collections.Generic.List<CardSlot> validSlots, bool canCancel)
        {
            // Session 14, Zamar: "when playing a card that can only be played in
            // certain slots, I'd like tab to cycle between the slots available
            // to play it in."
            //
            // ChooseSlot is handed the valid list by the game itself, so this is
            // the game's own answer rather than IKMA deciding what is legal —
            // the standing rule about asking rather than reimplementing. Handed
            // straight to HotkeyManager for Tab to walk.
            HotkeyManager.SetValidPlacementSlots(validSlots);

            // Record the game's own declaration of whether this slot selection
            // can be cancelled. HotkeyManager's Backspace narration branches on
            // this so what we say is grounded in actual game state. (Session 8.)
            HotkeyManager.LastChooseSlotCanCancel = canCancel;

            // Name the card being played, pulled live from PlayerHand (the same
            // source ConfirmSelection uses). Generic fallback if unavailable.
            string cardName = CardReader.CardName(Singleton<PlayerHand>.Instance?.ChoosingSlotCard?.Info);
            // Session 10: "Playing Squirrel." used to be spoken separately by
            // HotkeyManager an instant before this prompt, so the two lines
            // raced and the first was always half-eaten. They are one line now,
            // composed here where the card name is already to hand.
            //
            // The cancel hint says Backspace, not right-click. Zamar's rule, and
            // it is the right one: a control scheme that falls back to the mouse
            // for even one action is not a keyboard-playable game, and "aim at
            // the thing and right-click" is not something a blind player can do
            // at all. Backspace injection is wired (IKMA CANCEL markers); if the
            // game refuses the cancel, the injection path says so out loud
            // rather than this line promising something that does not happen.
            // Session 13: this used to be `canCancel ? " Backspace to cancel." : ""`,
            // taking the game at its word. The 0.7.21 log shows the game's word
            // is not the whole story — canCancel came back true for Squirrel,
            // IKMA promised Backspace, and three injections in a row were
            // ignored ("CANCEL: state unchanged"). Offering a blind player a key
            // that does nothing is the same defect as the number-key jumps and
            // the menu Backspace, and this one we were advertising on the
            // game's authority, which made it more convincing and no less false.
            //
            // So the hint is withheld until IKMA can actually deliver it. This
            // is a stopgap, not the answer: a sighted player CAN back out of a
            // placement, so parity is not restored by going quiet about it. See
            // the InputButtons patches above — the next dive is what ChooseSlot
            // really polls. Restore this the moment cancel works.
            //
            // Backspace itself is unchanged: it still attempts the injection and
            // WatchCancelOutcome still narrates whatever actually happens. Only
            // the promise made in advance is gone.
            string cancelHint = "";
            string lead = cardName != null ? Vocabulary.PlayFlow.Playing(cardName) : "";
            string message = Vocabulary.PlayFlow.ChooseASlotOrChooseASlot(cardName, lead, cancelHint);

            // Action priority: this follows straight on from a card being
            // chosen, so it should cut the hand-browse line still in the air.
            // Still queued rather than spoken directly, so a sacrifice's
            // "X is sacrificed. Received 1 bone." is heard before it.
            //
            // Session 13: DEFERRED, so it withdraws itself if the player has
            // already chosen. From the 0.7.23 log — "Mantis God played in Slot
            // 3." was heard, and six lines later "Playing Mantis God. Choose a
            // slot..." finally arrived, prompting for a card already on the
            // board. Browse reads fire instantly off the keypress while prompts
            // queue, so a fast player outruns the queue and the prompt is false
            // by the time it is spoken.
            //
            // This is the draw-prompt fix generalised. A line that INSTRUCTS
            // has to be true at speak time in a way a line that REPORTS never
            // does, so every instructing line asks the game whether it is still
            // waiting before it says anything. PlayerHand.ChoosingSlotCard is
            // the game's own answer.
            //
            // Session 14: and Prompt tier, which is the other half of the same
            // complaint. From the 0.7.22 log — Squirrel was clicked and this
            // line was heard only after "Your turn.", "Upcoming: 3 cards..."
            // and "Your hand: 4 cards..." had drained, with Zamar pressing
            // Backspace three times in the gap. Withdrawing correctly does not
            // help a prompt that is merely stuck behind three reads. It now
            // inserts ahead of pending commentary and still never overtakes a
            // combat result.
            // THE WITHDRAWAL HAD A HOLE, AND IT WAS THE COMMON CASE. (0.7.219.)
            //
            // Zamar, 0.7.218 log:
            //
            //   IKMA SPEAK (interrupt): Mole Man played in Slot 1.
            //   IKMA SPEAK: Playing Mole Man. Choose a slot to play Mole Man...
            //
            // "This line should have be stomped when I hit E."
            //
            // The check below only ran when `awaited` was non-null — and when
            // this prompt is raised during a SACRIFICE, the game has not yet
            // set ChoosingSlotCard, so `awaited` is null, the whole guard
            // short-circuits and the prompt speaks unconditionally however
            // stale it is. That is the path every blood cost takes.
            //
            // Ask the LIVE question instead, and only use the captured card to
            // catch the rarer case of a different card now being placed. An
            // instructing line has to be true at speak time; "choose a slot"
            // when the game is not waiting for one is an instruction that
            // cannot be followed.
            // THE GAME'S ANSWER IS TRUE AND IT IS LATE. (0.7.268.)
            //
            // Zamar, 0.7.267 log:
            //
            //   IKMA SPEAK (interrupt): Worker Ant played in Slot 2.
            //   IKMA SPEAK: Playing Worker Ant. Choose a slot to play Worker Ant...
            //
            // "Info about a game action shouldnt follow the game action."
            //
            // Every withdrawal test below still passed, because they all ask
            // PlayerHand.ChoosingSlotCard and the game had not cleared it yet.
            // IKMA injects the placement and speaks its confirmation in the same
            // frame; the game clears that field inside the coroutine it then
            // starts. So the prompt asked an honest question, got an honest
            // answer, and the answer was one frame stale.
            //
            // IKMA'S OWN ACT IS THE EARLIER AND SURER SIGNAL. The mod knows it
            // pressed the slot, and it knows before the game finishes agreeing.
            // This is the generation-counter shape the idle prompt already
            // uses: capture the count when the prompt is raised, and withdraw
            // if a placement has been made since. Re-testing later is not the
            // same as withdrawing now.
            int raisedAtGeneration = SlotPromptState.Generation;
            int raisedAtBrowse = SlotPromptState.Browsed;

            var awaited = Singleton<PlayerHand>.Instance?.ChoosingSlotCard;
            Speech.Prompt(() =>
            {
                if (SlotPromptState.Generation != raisedAtGeneration)
                {
                    Plugin.Log?.LogInfo(
                        "IKMA PROMPT: slot prompt withdrawn — the card was placed before this " +
                        "line reached the front of the queue.");
                    return null;
                }

                if (SlotPromptState.Browsed != raisedAtBrowse)
                {
                    Plugin.Log?.LogInfo(
                        "IKMA PROMPT: slot prompt withdrawn — a slot was already browsed, " +
                        "and its line carries the reminder.");
                    return null;
                }

                var ph = Singleton<PlayerHand>.Instance;
                if (ph == null) return null;

                var choosing = ph.ChoosingSlotCard;

                if (choosing == null)
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA PROMPT: slot prompt withdrawn — the game is no longer waiting " +
                        $"for a slot ({cardName ?? "the card"} is placed or cancelled).");
                    return null;
                }

                if (awaited != null && !ReferenceEquals(choosing, awaited))
                {
                    Plugin.Log?.LogInfo(
                        $"IKMA PROMPT: slot prompt withdrawn — a different card is being placed now.");
                    return null;
                }

                return message;
            });
        }
    }

    // =========================================================================
    // WHAT CREATED THE CARD IN YOUR HAND. (0.7.115.)
    //
    // DrawCreatedCard.CreateDrawnCard is NONPUBLIC and, per dump_creators.txt's
    // override audit, declared EXACTLY ONCE — none of its seven subclasses
    // redeclares it. So this single patch fires for Rabbit Hole (DrawRabbits),
    // Bees Within (BeesOnHit), DrawAnt, DrawCopy, DrawCopyOnDeath,
    // DrawRandomCardOnDeath and SteelTrap.
    //
    // THE PREFIX ONLY RECORDS. It speaks nothing and changes nothing — the line
    // is composed by EventNarrator when the card actually lands, and the cause
    // expires after three frames so it can never attach itself to a card that
    // arrived some other way. A coroutine prefix fires at enumerator creation,
    // which is exactly the moment wanted here and would be the wrong moment for
    // anything that needed the result.
    //
    // AbilityBehaviour.Card is a NONPUBLIC property, so it is read by cached
    // reflection; Ability is public. Both confirmed in dump_creators.txt before
    // a line of this was written.
    // =========================================================================
    // NO [HarmonyPatch] ATTRIBUTE. This class is registered through
    // Plugin.TryPatch, and the attribute would ALSO make PatchAll claim it —
    // with no target specified, PatchAll throws and every patch after it in
    // the run is silently dead. check_source.ps1 fails the build on exactly
    // this pairing, and it caught it here.
    public static class DrawCreatedCard_CreateDrawnCard_Patch
    {
        private static System.Reflection.PropertyInfo _cardProp;
        private static bool _resolved;

        internal static PlayableCard OwnerCard(AbilityBehaviour behaviour)
        {
            if (behaviour == null) return null;

            if (!_resolved)
            {
                _resolved = true;
                _cardProp = typeof(AbilityBehaviour).GetProperty(
                    "Card",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);

                if (_cardProp == null)
                    Plugin.Log?.LogWarning(
                        "IKMA: AbilityBehaviour.Card did not resolve — creation causes stay unnamed.");
            }

            if (_cardProp == null) return null;
            try { return _cardProp.GetValue(behaviour, null) as PlayableCard; }
            catch { return null; }
        }

        public static void Prefix(DrawCreatedCard __instance)
        {
            try
            {
                var owner = OwnerCard(__instance);
                if (owner == null) return;

                string cardName = CardReader.CardName(owner);
                string ability  = CardReader.GetAbilityName(__instance.Ability);

                if (string.IsNullOrEmpty(cardName) || string.IsNullOrEmpty(ability)) return;

                EventNarrator.NoteCreationCause(cardName, ability);

                // 0.7.424 — is this the pelt of a trap that just caught a card?
                TrapCatchNarrator.NoteDraw(owner, __instance.Ability);

                // 0.7.193 — one card, one line. EventNarrator is about to say
                // "Rabbit is created in your hand by Cuckoo's Brood Parasite",
                // which is strictly better than the plain arrival line, so the
                // plain one stands down. See BossNarrator.SuppressNextHandArrival.
                BossNarrator.SuppressNextHandArrival();
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: creation-cause prefix threw {e.GetType().Name}.");
            }
        }
    }

    // =========================================================================
    // BROOD PARASITE. (0.7.115.)
    //
    // CreateEgg declares OnResolveOnBoard as an OVERRIDE of TriggerReceiver and
    // nothing else declares it below CreateEgg, so this one patch is the whole
    // ability. It records the opposing slot's state and hands the question of
    // whether an egg actually appeared to EventNarrator, which asks the board
    // at speak time.
    //
    // Zamar's requirement is that nothing is said unless an egg was laid, and
    // reflection cannot see inside the coroutine to know. The board can.
    // =========================================================================
    // NO [HarmonyPatch] ATTRIBUTE. This class is registered through
    // Plugin.TryPatch, and the attribute would ALSO make PatchAll claim it —
    // with no target specified, PatchAll throws and every patch after it in
    // the run is silently dead. check_source.ps1 fails the build on exactly
    // this pairing, and it caught it here.
    // =========================================================================
    // OVERKILL, IN TWO HALVES. (0.7.120.)
    //
    // DealOverkillDamage carries the AMOUNT and PreOverkillDamage carries the
    // VICTIM. Neither has both, and the victim is a QUEUED card, which is not in
    // a slot and so cannot be found from the slot arguments. So the amount is
    // stamped by the first and the line is composed by the second.
    //
    // Both are NONPUBLIC and neither is overridden by CombatPhaseManager3D,
    // which is what Act 1 runs — confirmed in dump_combat_end.txt's override
    // audit. PostOverkillDamage and VisualizeExcessLethalDamage ARE overridden
    // there, which is exactly why that audit gets run every time.
    //
    // Prefixes on coroutines fire at enumerator creation. That is right for
    // both: neither needs a result, only the arguments.
    // =========================================================================
    public static class CombatPhaseManager_DealOverkillDamage_Patch
    {
        public static void Prefix(int damage, CardSlot opposingSlot)
        {
            try { EventNarrator.NoteOverkillAmount(damage, opposingSlot); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: overkill amount prefix threw {e.GetType().Name}.");
            }
        }
    }

    public static class CombatPhaseManager_PreOverkillDamage_Patch
    {
        public static void Prefix(PlayableCard queuedCard)
        {
            try { EventNarrator.NoteOverkillVictim(queuedCard); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: overkill victim prefix threw {e.GetType().Name}.");
            }
        }
    }

    // =========================================================================
    // THE END OF A WON COMBAT. (0.7.120.)
    //
    // VisualizeExcessLethalDamage is the game's own count of damage dealt beyond
    // what winning required — the figure its teeth animation is driven from.
    //
    // *** PATCHED ON BOTH DECLARATIONS AND THE OVERRIDE IS THE ONE THAT RUNS. ***
    // CombatPhaseManager declares it and CombatPhaseManager3D overrides it; Act 1
    // uses the 3D manager. A patch on the base alone would register cleanly, log
    // success and never fire once.
    //
    // One class, registered twice. The narrator is idempotent per call, and the
    // two declarations are on different types so only one of them can run for a
    // given manager.
    // =========================================================================
    public static class CombatPhaseManager_VisualizeExcessLethalDamage_Patch
    {
        public static void Prefix(int excessDamage)
        {
            try { EventNarrator.NoteVictory(excessDamage); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: victory prefix threw {e.GetType().Name}.");
            }
        }
    }

    // =========================================================================
    // THE BATTLE IS OVER. (0.7.431.)
    //
    // TurnManager.CleanupPhase() - NONPUBLIC, no parameters, IEnumerator.
    // GameSequence calls it once after its loop ends, so this prefix fires at
    // the moment the game has decided the battle is finished. Registered
    // through TryPatch; no attribute on this class.
    //
    // PlayerIsWinner() - NONPUBLIC, no parameters, bool - is the same question
    // CleanupPhase asks on its own first line to set PlayerWon. Asked here
    // directly rather than reading PlayerWon back, which is not set until the
    // coroutine body starts.
    //
    // Both confirmed in dumps\dump_battle_cleanup_from_decompile.txt.
    // =========================================================================
    public static class TurnManager_CleanupPhase_Patch
    {
        private static System.Reflection.MethodInfo _playerIsWinner;
        private static bool _looked;

        public static void Prefix(TurnManager __instance)
        {
            try
            {
                if (!_looked)
                {
                    _looked = true;
                    _playerIsWinner = AccessTools.Method(typeof(TurnManager), "PlayerIsWinner");
                    if (_playerIsWinner == null)
                        Plugin.Log?.LogWarning("IKMA: TurnManager.PlayerIsWinner not found - a win with no spare damage stays silent.");
                }

                bool won = false;
                if (_playerIsWinner != null && __instance != null)
                    won = (bool)_playerIsWinner.Invoke(__instance, null);

                EventNarrator.NoteBattleCleanup(won);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: battle cleanup prefix threw {e.GetType().Name}.");
            }
        }
    }

    // =========================================================================
    // WHICH STAT THIS CAMPFIRE IS OFFERING. (0.7.130.)
    //
    // CardStatBoostSequencer.OnSlotSelected(MainInputInteractable slot, Boolean
    // attackMod) is the game saying which of the two fires the player is at.
    // IKMA records it and asks GetTranslatedStatText for the printed word.
    //
    // Records only — speaks nothing, changes nothing. Declared once on
    // CardStatBoostSequencer per dump_sequencers.txt, with no override anywhere.
    // =========================================================================
    // =========================================================================
    // WHAT THE FIRE ACTUALLY DID. (0.7.131.) The check on the IL read.
    //
    // NodeScreenReader reads the boost amount out of ApplyModToCard's own
    // instructions so it can be spoken BEFORE the choice — which is what parity
    // requires, since the fire card shows "+1" on screen. A number derived that
    // way must never be believed on its own, so this Postfix reports the
    // adjustment the game really applied and the log carries both.
    //
    // If those two ever disagree, the announced number is wrong and this line is
    // how anyone finds out — rather than a player losing a card to a promise
    // IKMA could not keep.
    // =========================================================================
    public static class CardStatBoostSequencer_ApplyModToCard_Patch
    {
        public static void Postfix(CardInfo card)
        {
            try
            {
                if (card?.Mods == null || card.Mods.Count == 0) return;

                var last = card.Mods[card.Mods.Count - 1];
                if (last == null) return;

                NodeScreenReader.NoteBoostApplied(last.attackAdjustment, last.healthAdjustment);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: boost postfix threw {e.GetType().Name}.");
            }
        }
    }

    public static class CardStatBoostSequencer_OnSlotSelected_Patch
    {
        public static void Prefix(bool attackMod)
        {
            try { NodeScreenReader.NoteCampfireStat(attackMod); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: campfire stat prefix threw {e.GetType().Name}.");
            }
        }
    }

    public static class CorpseEater_OnOtherCardDie_Patch
    {
        // 0.7.304. Prefix, because the arguments ARE the sentence: the dead
        // card and the slot it died in. A postfix on a coroutine would fire
        // at enumerator creation anyway, and by speak time the eater has
        // already been played into that slot and the board no longer shows
        // what happened.
        public static void Prefix(CorpseEater __instance, PlayableCard card, CardSlot deathSlot)
        {
            try
            {
                SigilNarrator.NoteCorpseEater(__instance, card, deathSlot);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: corpse eater prefix threw {e.GetType().Name}.");
            }
        }
    }

    public static class CreateEgg_OnResolveOnBoard_Patch
    {
        // 0.7.190 — ONE LINE, NOT TWO.
        //
        // This used to call EventNarrator.NoteEggAbility, which spoke a separate
        // provisional line ("Broken Egg is created in enemy Slot 2 by Cuckoo's
        // Brood Parasite."). With the 0.7.186 generic sigil line in front of it
        // that became two lines for one event, and Zamar asked for one:
        //
        //   "Cuckoo's Brood Parasite ability triggers."
        //   At the end add "placing a Broken Egg in enemy Slot [x]."
        //
        // SigilNarrator.NoteBroodParasite composes the whole sentence, and
        // Ability.CreateEgg is silent in SigilTriggers so the generic line does
        // not fire alongside it. NoteEggAbility is left in place but is no
        // longer called from anywhere.
        public static void Prefix(CreateEgg __instance)
        {
            try
            {
                SigilNarrator.NoteBroodParasite(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: brood parasite prefix threw {e.GetType().Name}.");
            }
        }
    }

    // ==================================================================
    // THE REST OF M5. (0.7.316.) Seven patches, five sentences. All seven
    // go through TryPatch: two of the targets are PRIVATE and one is on an
    // abstract base, so a signature that has moved costs these lines and
    // nothing registered after them. See SigilNarrator for each hook.
    // ==================================================================

    public static class CreateCardsAdjacent_OnResolveOnBoard_Patch
    {
        // Opens the batch. Prefix on a coroutine fires at enumerator creation,
        // which is exactly right here — it is the "this sigil is resolving"
        // moment, before the game has decided anything.
        public static void Prefix(CreateCardsAdjacent __instance)
        {
            try { SigilNarrator.NoteAdjacentSpawnStart(__instance); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: adjacent spawn prefix threw {e.GetType().Name}.");
            }
        }
    }

    public static class CreateCardsAdjacent_SpawnCardOnSlot_Patch
    {
        // One call per side, and the game only makes it for a neighbour it has
        // already found empty. So this is the game reporting its own choice.
        public static void Prefix(CardSlot slot)
        {
            try { SigilNarrator.NoteAdjacentSpawnSlot(slot); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: adjacent spawn slot prefix threw {e.GetType().Name}.");
            }
        }
    }

    public static class TailOnHit_OnCardGettingAttacked_Patch
    {
        public static void Prefix(TailOnHit __instance)
        {
            try { SigilNarrator.NoteLooseTail(__instance); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: loose tail prefix threw {e.GetType().Name}.");
            }
        }
    }

    public static class RandomAbility_AddMod_Patch
    {
        // POSTFIX. The granted sigil does not exist until this method returns.
        public static void Postfix(RandomAbility __instance)
        {
            try { SigilNarrator.NoteAmorphous(__instance); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: amorphous postfix threw {e.GetType().Name}.");
            }
        }
    }

    public static class SquirrelOrbit_OnUpkeep_Patch
    {
        // Latches the card that owns the sigil. The portrait component that
        // gets handed the pulled card has no way back to it.
        public static void Prefix(SquirrelOrbit __instance)
        {
            try { SigilNarrator.NoteTidalLockUpkeep(__instance); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: tidal lock upkeep prefix threw {e.GetType().Name}.");
            }
        }
    }

    public static class MoonAnimatedPortrait_InstantiateOrbitingObject_Patch
    {
        // One call per card pulled into orbit, with that card's CardInfo.
        public static void Prefix(CardInfo cardInfo)
        {
            try { SigilNarrator.NoteTidalLockPull(cardInfo); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: tidal lock pull prefix threw {e.GetType().Name}.");
            }
        }
    }

    public static class RandomConsumable_OnResolveOnBoard_Patch
    {
        public static void Prefix(RandomConsumable __instance)
        {
            try { SigilNarrator.NoteTrinketBearer(__instance); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: trinket bearer prefix threw {e.GetType().Name}.");
            }
        }
    }

}
