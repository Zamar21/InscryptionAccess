// CopyCardNarrator.cs

using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The copy card node's arrival — the bottle, the easels, and the goo that
    /// paints. Zamar's words, placed between the game's own dialogue lines.
    /// </summary>
    /// <remarks>
    /// 0.7.267, Session 22.
    ///
    /// WHAT A SIGHTED PLAYER GETS AND A BLIND ONE DID NOT. The screen opens
    /// with a bottle and two easels dropping onto the table, and only then does
    /// Leshy speak. `CopyCardSequencer.CopyCardSequence` in order:
    ///
    ///   DropItemAnim(selectedCardEaselAnim)  + copiedCardEaselAnim
    ///   DropItemAnim(bottleAnim)
    ///   PlayDialogueEvent("CopyCardIntro1")   <- "OH NO..."
    ///   JumpBottle, gooAnim "speaking"
    ///   PlayDialogueEvent("CopyCardIntro2")   <- "I THOUGHT I HAD TOSSED..."
    ///   pile.SpawnCards, selectionSlot.RevealAndEnable
    ///
    /// Everything before the first line is picture only, and the beat between
    /// the two lines is the bottle jumping and its face starting to move.
    /// Zamar's two sentences go exactly there, and his ordering is the reason
    /// this hangs on the dialogue ids rather than on the sequencer's start: the
    /// scene description has to arrive BEFORE "OH NO..." and the bottle's
    /// description BETWEEN the two, which is a position no single prefix on
    /// CopyCardSequence could express.
    ///
    /// THE GAME NAMES THE MOMENT, so IKMA does not have to time it.
    /// TextDisplayer.PlayDialogueEvent takes the event id as an argument and is
    /// declared once, PUBLIC, with no override anywhere — checked before
    /// patching, because a patch on a base declaration a subclass overrides is
    /// this project's most expensive recurring mistake. A prefix fires at
    /// enumerator creation, which is the instant before that dialogue plays.
    ///
    /// ONCE PER SCREEN. The ids are unique to this sequencer, but a player who
    /// meets a second copy card node in the same run gets the description
    /// again, which is correct — it is a new arrival at a new node.
    /// </remarks>
    public static class CopyCardNarrator
    {
        internal const string INTRO_1 = "CopyCardIntro1";
        internal const string INTRO_2 = "CopyCardIntro2";

        /// <summary>
        /// The table, before Leshy says anything. HIS WORDING, verbatim.
        /// </summary>
        internal static void OnIntro1()
        {
            try
            {
                Plugin.Log?.LogInfo("IKMA COPY CARD: scene description, ahead of CopyCardIntro1.");
                using (Speech.Event(EventKind.NodeResults)) Speech.Result(
                    Vocabulary.CopyCardNode.CorkedGlassBottleSuddenly);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA COPY CARD: intro 1 — {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// What is in the bottle, after his first line and before his second.
        /// HIS WORDING, verbatim.
        /// </summary>
        internal static void OnIntro2()
        {
            try
            {
                Plugin.Log?.LogInfo("IKMA COPY CARD: bottle description, ahead of CopyCardIntro2.");
                using (Speech.Event(EventKind.NodeResults)) Speech.Result(
                    Vocabulary.CopyCardNode.InsideTheGlassBottle);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA COPY CARD: intro 2 — {e.GetType().Name}: {e.Message}");
            }
        }

        // ==================================================================
        // THE COPY, READ WHEN IT IS REVEALED. (0.7.321.)
        //
        // Zamar: "I want the newly created card to read it's full info after
        // the easels turn around and it is revealed. Ideally before the goo's
        // lines."
        //
        // HOOKED ON CreateCloneCard, WHICH IS WHERE THE CARD BECOMES REAL.
        // The sequencer paints for 3.5 seconds against a card whose attack and
        // health are rendered HIDDEN, then calls CreateCloneCard, adds the
        // result to the deck, turns the hidden flags off, SetInfo's it onto
        // the easel and only then tweens the easels back round. A postfix here
        // hands over the finished CardInfo — including the paint decal and the
        // one random alteration the goo makes — at the first instant it
        // exists, and before the easel is facing the player.
        //
        // WHY NOT A DIALOGUE HOOK LIKE THE TWO INTROS. CopyCardPresentResult
        // is the goo's line and he wants this AHEAD of it. A prefix on that
        // event would fire at the right moment but would have to go looking
        // for the card; the card is this method's return value.
        //
        // THE DELAY IS THE EASEL TURN, NOT A GUESS AT THE DIALOGUE. The two
        // Tween.LocalRotation calls that swing the easels round run 0.2s with
        // a 0.02s stagger, so this waits them out and no more. A delayed line
        // holds everything behind it in the queue, which is what puts it in
        // front of the goo — his ordering, taken rather than hoped for.
        //
        // NO LEAD-IN WORD. "I want the card info first for snappiness for
        // screen reading" (0.7.320) — the card IS the announcement, and
        // "The copy:" in front of it would be a word he has not approved
        // delaying the part he asked to hear.
        // ==================================================================
        private const float EASEL_TURN_SECONDS = 0.35f;

        internal static void OnCopyRevealed(CardInfo copy)
        {
            try
            {
                if (copy == null)
                {
                    Plugin.Log?.LogInfo("IKMA COPY CARD: the copy was revealed but could not be read.");
                    return;
                }

                var captured = copy;

                using (Speech.Event(EventKind.CardObtained, EventSource.CurrentPlayer)) Speech.Result(() =>
                {
                    string described = null;
                    try { described = CardReader.DescribeCardInfo(captured, true, ""); } catch { }

                    if (string.IsNullOrEmpty(described))
                    {
                        Plugin.Log?.LogInfo("IKMA COPY CARD: the copy could not be described.");
                        return null;
                    }

                    Plugin.Log?.LogInfo($"IKMA COPY CARD: the copy is {described}");
                    // 0.7.433 - his lead-in, approved: "Painted Card: Wolf..."
                    return Vocabulary.CopyCardNode.PaintedCard(described);
                }, EASEL_TURN_SECONDS);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA COPY CARD: reveal — {e.GetType().Name}: {e.Message}");
            }
        }
    }

    public static class CopyCardSequencer_CreateCloneCard_Patch
    {
        // POSTFIX — the card does not exist until this returns.
        public static void Postfix(CardInfo __result)
        {
            try { CopyCardNarrator.OnCopyRevealed(__result); }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA: copy reveal postfix threw {e.GetType().Name}.");
            }
        }
    }

    // Registered by Plugin.TryPatch. No [HarmonyPatch] attribute — a class
    // carrying both registers twice and speaks every line twice.
    //
    // PlayDialogueEvent is called for every conversation in the game, so this
    // prefix does nothing at all but compare two strings on any other id. The
    // alternative — a patch on the sequencer plus a timer — would be a guess
    // about how long a drop animation takes.
    public static class TextDisplayer_PlayDialogueEvent_Patch
    {
        public static void Prefix(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return;

            if (eventId == CopyCardNarrator.INTRO_1) CopyCardNarrator.OnIntro1();
            else if (eventId == CopyCardNarrator.INTRO_2) CopyCardNarrator.OnIntro2();
            else if (eventId == PirateSkullNarrator.INTRO_2) PirateSkullNarrator.OnEyeGlow();
            else if (eventId == PirateSkullNarrator.MUTINEE) PirateSkullNarrator.FlushMutiny();
            else if (eventId == PirateSkullNarrator.SHIP_SPAWNED) PirateSkullNarrator.SayShipScene();
        }

        // 0.7.348 — the ship's arrival line waits for this conversation.
        public static void Postfix(string eventId, ref System.Collections.IEnumerator __result)
        {
            __result = PirateSkullNarrator.WrapDialogue(eventId, __result);
        }
    }
}

// CopyCardNarrator.cs
