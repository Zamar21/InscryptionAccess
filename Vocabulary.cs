// Vocabulary.cs

using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// Every line IKMA speaks, each with exactly one home. (Spec A8.)
    /// </summary>
    /// <remarks>
    /// 0.7.207 - graph-a11y spec A8: "one vocabulary module for everything the
    /// layer says; hardcoded English at call sites is forbidden, because it
    /// is unfindable later." 0.7.207 moved the sentences said at two or more
    /// sites. M7 (0.7.358) moved the rest: every player-facing literal and
    /// every sentence composed from pieces now lives in this file, and the
    /// readers ask for it by name. Nothing the player hears changed in the
    /// move; each call site was proven to build the same text as before.
    ///
    /// THE RULE FROM HERE ON: a new spoken line is added here, never written
    /// at a call site, and a line a second screen wants is asked for by name,
    /// not copied. check_source.ps1 warns when a literal is passed straight
    /// into Speech or CombatAnnouncer. Log text (IKMA ...) stays with the code
    /// that logs it.
    ///
    /// LAYOUT. The members before SHARED LINES are the ones from before M7,
    /// kept as they were. SHARED LINES holds what more than one screen says.
    /// Then one nested class per screen or system, in the order a run meets
    /// them, Mod and Boot first and RunEnd last. A constant is a line that
    /// never changes; a method is a line with holes, and its parameters are
    /// the holes. A condition inside a member is part of the wording ("1 card"
    /// or "3 cards"); a decision to say nothing at all stays at the call site.
    ///
    /// WHO WROTE IT. Every member carries one comment line:
    ///   Zamar, [version]  - a comment in the source records the words as his.
    ///   Game, [version]   - the words are the game's own.
    ///   Claude, [version] - everything else, including lines Zamar heard and
    ///                       approved without wording them himself.
    /// The version is the first build whose source held those words, read
    /// from git history. History starts at 0.7.42, so "0.7.42 or earlier"
    /// means before it, and a range like "0.7.110-0.7.206" is one commit that
    /// covered those builds. Where a comment names a session instead
    /// ("Session 25"), that is what the source recorded.
    ///
    /// NOT HERE, ON PURPOSE:
    /// - His cabin descriptions. CabinMap._descriptions is compiled from his
    ///   sheet, and check_source.ps1 CHECK 2 reads them where they are.
    /// - Key vocabulary that is spoken as it is, like "north" from
    ///   CabinMap.FacingAt: it is half of every survey key, so it stays beside
    ///   the table it has to match.
    /// - Punctuation-only glue (", ", ". ") joining two members at a call site.
    /// - Whatever the game supplies while running: card names, sigil text,
    ///   character dialogue.
    ///
    /// A screen name that is also a lookup key (NodeScreenReader's tables are
    /// keyed by the screen and slot names it speaks) uses the SAME member as
    /// the key, so rewording a screen can never quietly break its lookups.
    ///
    /// SPOKEN_LINES.md, beside PROVISIONAL_LINES.md in the IKM Access folder,
    /// is this file made readable: every line, grouped the same way, with its
    /// member name in square brackets. It is generated from this file.
    ///
    /// Naming: a member says what the line is FOR where that could be told,
    /// otherwise its first words. A trailing underscore marks a fragment that
    /// ends in a space because it is joined to more words.
    /// </remarks>
    public static class Vocabulary
    {
        /// <summary>
        /// "A and B", "A, B, and C": every spoken list of names or numbers.
        /// Session 32, Zamar: Oxford comma, one style for all lists. Before
        /// this, half the lists said "A, B and C". head = everything but the
        /// last item.
        /// </summary>
        // Zamar, Session 32 (the Oxford comma; the joiner is Claude's).
        internal static string AndList(System.Collections.Generic.IList<string> head, string last)
        {
            if (head == null || head.Count == 0) return last;
            if (head.Count == 1) return Loc.F($"{head[0]} and {last}");
            var items = new string[head.Count];
            head.CopyTo(items, 0);
            return Loc.F($"{string.Join(", ", items)}, and {last}");
        }

        // Empty and unavailable
        // Claude, 0.7.42 or earlier.
        public static string HandEmpty => Loc.T("Hand is empty.");
        // Claude, 0.7.42 or earlier.
        public static string BoardUnavailable => Loc.T("Board unavailable.");
        // Claude, 0.7.42 or earlier.
        public static string ItemsUnavailable => Loc.T("Items unavailable.");
        // Claude, 0.7.42 or earlier.
        public static string NoItems => Loc.T("You have no items.");
        // Claude, 0.7.53-0.7.109.
        public static string NoOptions => Loc.T("No options.");
        // Claude, 0.7.42 or earlier.
        public static string NoOptionsAvailable => Loc.T("No options available.");
        // Claude, 0.7.53-0.7.109.
        public static string NoSettingsOnPage => Loc.T("No settings on this page.");
        // Claude, 0.7.42 or earlier.
        public static string NoPathsAhead => Loc.T("No paths ahead.");
        // Claude, 0.7.53-0.7.109.
        public static string NothingInFront => Loc.T("Nothing is directly in front of you.");
        // Claude, 0.7.53-0.7.109.
        public static string NothingToExamine => Loc.T("Nothing to examine here.");

        /// <summary>
        /// A number key with nothing mapped to it where the player is standing.
        /// PROVISIONAL. (0.7.219 — it used to press an arbitrary nearby object
        /// and claim a focus that never happened.)
        /// </summary>
        // Claude, 0.7.217-0.7.221.
        public static string NothingOnThatKey(int digit)
            => Loc.F($"Nothing on {digit} from here.");

        /// <summary>
        /// The keys on the run end screen. HIS WORDING, 0.7.242, replacing
        /// "Enter for a new run with the same starter deck. Backspace for the
        /// menu." — the old line said what Enter did but not that the run's
        /// challenges come with it, which is the part that decides whether a
        /// player wants it.
        /// </summary>
        // Zamar, 0.7.242.
        public static string RunEndControls()
            => Loc.T("Press Enter to begin a new run with the same Starter Deck and Challenges enabled. "
             + "Backspace to return to the main menu. Space to repeat.");

        /// <summary>
        /// Enter on the greyed-out CONTINUE RUN. His wording, 0.7.230.
        /// </summary>
        // Zamar, 0.7.230.
        public static string NoRunToContinue()
            => Loc.T("No current run available.");

        /// <summary>
        /// Enter on CONTINUE (or NEW GAME) at Inscryption's own title screen —
        /// the base game, not Kaycee's Mod. Zamar wrote this line himself and
        /// signed it, so it is not provisional and is not to be reworded.
        /// (0.7.231.)
        ///
        /// It exists because the key had to do SOMETHING. Reading the card and
        /// then dropping a blind player into an unnarrated Act 1 is worse than
        /// a closed door, and a door that opens onto silence is the defect this
        /// whole mod is against.
        /// </summary>
        // Zamar, 0.7.231.
        public static string MainGameNotSupported()
            => Loc.T("Inscryption Main game support still in development. "
             + "I'll get it done don't worry! -Zamar :)");

        /// <summary>
        /// The very first thing the game shows: a black screen with one small
        /// play button in the middle of it. Zamar's description, 0.7.232 —
        /// "In the center of the screen is a small play button. Press
        /// Enter..." — finished with the only thing Enter can mean here.
        /// PROVISIONAL only in its last four words.
        /// </summary>
        // Zamar, Session 25.
        public static string BootPlayButton()
            => Loc.T("In the center of the screen is a small play button. Press Enter...");   // Zamar's wording, Session 25.

        /// <summary>
        /// The two logos in the opening parade, in the order they appear.
        /// Zamar named both; the TIMING is IKMA's best reading of
        /// FirstPlaySceneController's own waits and is logged so he can move
        /// them. PROVISIONAL as to when, not as to what.
        /// </summary>
        // Zamar, 0.7.232.
        public static string BootStudioLogo()    => Loc.T("Daniel Mullins Games.");

        /// <summary>
        /// Enter on the glitched NEW GAME card. The game answers with a glitch
        /// sound and a screen-glitch effect; the sound carries, the picture
        /// does not, so this is the half a blind player was missing. Zamar's
        /// words, 0.7.234.
        /// </summary>
        // Zamar, 0.7.234.
        public static string TitleCardGlitches() => Loc.T("The screen glitches for a second.");

        /// <summary>
        /// Escape on Inscryption's own title screen. It opens the video footage
        /// menu, which is not built for v0.5 — so the key answers instead of
        /// doing nothing, which is what Zamar asked for over swallowing it:
        /// "have it instead not open the menu and read ...". His words.
        /// </summary>
        // Zamar, 0.7.222-0.7.261.
        public static string VideoLogsNotAvailable() => Loc.T("Video logs not yet available. Coming soon!");
        // Zamar, 0.7.232.
        public static string BootPublisherLogo() => Loc.T("Devolver Digital.");

        // THE INTRO'S AUDIO DESCRIPTION LINE IS GONE, 0.7.274. Zamar closed
        // it: "let's remove the AD from this intro scene since it's just a
        // black screen anyways." Nothing on screen to describe, so nothing is
        // said — parity, in the direction he has enforced against himself
        // before. See BootScreenReader for the full note.

        /// <summary>
        /// The title card, when the "PRESS ANY BUTTON" text appears under it.
        /// Zamar, 0.7.233: "When the titlecard hits and the Press Any Button
        /// text appears, then say Inscryption" — and 0.7.234: "After this it
        /// needs to read 'Press any button to start.'"
        ///
        /// ONE UTTERANCE, not two. The name and the instruction are both Browse
        /// lines, and a second Browse cuts the first — so said separately the
        /// player would hear "Insc—" and then the prompt. The screen shows both
        /// at once anyway.
        /// </summary>
        // Zamar, 0.7.234.
        public static string BaseTitleCard() => Loc.T("Inscryption. Press any button to start.");

        /// <summary>
        /// Kaycee's Mod naming itself, spoken ONLY where the Start scene is a
        /// pass-through into the mod's own menus. His line from 0.7.43; the
        /// change at 0.7.233 is where it is allowed to fire, not what it says.
        /// </summary>
        // Zamar, 0.7.43.
        public static string ModTitleCard() => Loc.T("Inscryption: Kaycee's Mod.");
        // Claude, 0.7.42 or earlier.
        public static string RulebookNoPages => Loc.T("The rulebook has no pages to read.");

        // Refusals - the press was heard and nothing happened
        // Claude, 0.7.42 or earlier.
        // Note D5: every refused E, whatever the reason.
        // Zamar, 0.7.414.
        public static string CannotEndTurn => Loc.T("Now is not the time to ring the bell.");
        // Claude, 0.7.42 or earlier.
        public static string NoCardSelected => Loc.T("No card selected.");

        // The review history with nothing in it yet. PROVISIONAL: the shape
        // is Say the Spire 2's ("{buffer}: empty").
        // Claude, Session 35.
        public static string HistoryEmpty => Loc.T("History: empty.");

        // Y opens the history as a list (Hearthstone Access's key), and it
        // closes again. PROVISIONAL: the opening shape is Say the Spire 2's
        // "name: current line"; the closing line is Accessible Arena's.
        // Claude, Session 35.
        public static string HistoryOpened(string newest) => Loc.F($"History: {newest}");
        public static string HistoryClosed => Loc.T("History closed.");

        // Zamar, Session 34. Said when the player switches to the controller,
        // and back to the keyboard (not at a keyboard start).
        public static string GamepadSwitch => Loc.T("Gamepad.");
        public static string KeyboardSwitch => Loc.T("Keyboard.");

        // Zamar, Session 33 (built Session 34). Enter on a locked devlog entry.
        public static string DevLogLocked => Loc.T("Dev Log Locked.");
        // Claude, 0.7.53-0.7.109.
        public static string MustSelectCardFirst => Loc.T("You must first select a card to add to your deck.");
        // Claude, 0.7.42 or earlier.
        public static string OptionCouldNotBeSelected => Loc.T("That option could not be selected.");
        // Claude, 0.7.43-0.7.52.
        public static string SurrenderNotAccepted => Loc.T("The surrender could not be accepted.");
        // Claude, 0.7.53-0.7.109.
        public static string CouldNotReachTable => Loc.T("Could not reach the table.");
        // Claude, 0.7.110-0.7.206.
        public static string WoodcarvingCouldNotBeRead => Loc.T("The woodcarving could not be read.");

        // Help fragments
        // Claude, 0.7.42 or earlier.
        public static string PressHForWhatStillWorks => Loc.T("Press H for what still works.");
        // Claude, 0.7.53-0.7.109.
        public static string SpaceDescribesAhead_ => Loc.T("Space describes what is directly in front of you. ");
        // Claude, 0.7.110-0.7.206.
        public static string ConversationInProgress => Loc.T("Conversation in progress, press Space to proceed.");

        // Composed lines - one method per sentence shape, so the shape has one
        // home even though the numbers and names change.

        /// <summary>The scales, from the player's side. Positive = player leads.</summary>
        // Claude, 0.7.42 or earlier.
        public static string Scales(int balance)
        {
            if (balance > 0) return Loc.F($"Scales: you lead by {balance}.");
            if (balance < 0) return Loc.F($"Scales: enemy leads by {System.Math.Abs(balance)}.");
            return Loc.T("Scales: even.");
        }

        /// <summary>The generic sigil-trigger line. Both sigil narrators use it.</summary>
        /// <remarks>
        /// 0.7.251 — WHAT THE TRIGGER LOOKS LIKE, WHERE ZAMAR HAS SAID.
        ///
        /// He asked for flavour after "Kingfisher's Waterborne ability
        /// triggers": "It dives underwater..." A sighted player watches the bird
        /// drop below the table; the sigil's name alone says a rule fired and
        /// not that anything happened on the board.
        ///
        /// A table rather than a special case, because this is the one line both
        /// sigil narrators compose and the next flavoured sigil should be one
        /// entry rather than one branch. Empty means no flavour, which is every
        /// sigil but this one until he writes more.
        /// </remarks>
        // 0.7.253 — TWO FORMS, because three birds diving is not three events.
        // Zamar: "add all three of those to one single call right before scales.
        // If doing it for multiple creatures, instead of it dives under water,
        // it should be they dive."
        private static readonly System.Collections.Generic.Dictionary<string, string> _sigilFlavour
            = new System.Collections.Generic.Dictionary<string, string>(
                System.StringComparer.OrdinalIgnoreCase)
        {
            // WATERBORNE IS BACK, AND IT NOW SAYS THE RIGHT HALF. (0.7.283.)
            //
            // Zamar heard the bare line and gave the words: "Add 'It
            // resurfaces.' after triggers."
            //
            // WHY THIS IS SAFE WHEN "It dives underwater..." WAS NOT. This
            // table is consulted on the generic trigger path, and that path is
            // only ever reached by Submerge's RESURFACE — its OnTurnEnd runs no
            // trigger sequence at all and carries its own sentence through
            // Submerge_OnTurnEnd_Patch. So the entry cannot leak onto the dive.
            //
            // The 0.7.282 note was right about the principle and wrong about
            // the conclusion: the flavour does not have to be true of every
            // trigger of the SIGIL, only of every trigger that reaches this
            // table. Waterborne is a fine tenant; it was the dive's sentence
            // living here that was the bug.
            { "Waterborne", "It resurfaces." },
        };

        private static readonly System.Collections.Generic.Dictionary<string, string> _sigilFlavourPlural
            = new System.Collections.Generic.Dictionary<string, string>(
                System.StringComparer.OrdinalIgnoreCase)
        {
            // Several birds coming up at once is still one event — his 0.7.253
            // ruling on the dive, applied to the other half.
            { "Waterborne", "They resurface." },
        };

        // ------------------------------------------------------------------
        // WHAT THE SIGIL DID, IN THE SAME SENTENCE. (0.7.303.)
        //
        // ZAMAR'S WORDING, verbatim:
        //   "The Smoke's Bone King ability triggers, granting four bones."
        //   "Sparrow's Sharp Quills ability triggers, sending one damage back
        //    to its attacker."
        //
        // DIFFERENT FROM _sigilFlavour ABOVE, and deliberately: a flavour is
        // a second sentence about the table ("It resurfaces."), while these
        // finish the first one. The generic line was true for both of these
        // and told the player nothing — four bones and a point of damage are
        // the whole content of what happened.
        //
        // THE NUMBERS ARE THE GAME'S CONSTANTS, not readings:
        //   QuadrupleBones.OnDie -> ResourcesManager.AddBones(4, Slot)
        //   Sharp.OnTakeDamage   -> source.TakeDamage(1, Card)
        // Both are literals in the behaviour, so the words can carry them. If
        // either ever becomes a variable, this table is the wrong home for it
        // and the line needs composing from the value instead.
        //
        // A clause replaces the full stop, so the sentence reads as one. Any
        // sigil with an entry here takes this path; everything else is
        // unchanged.
        // ------------------------------------------------------------------
        private static readonly System.Collections.Generic.Dictionary<string, string> _sigilClause
            = new System.Collections.Generic.Dictionary<string, string>(
                System.StringComparer.OrdinalIgnoreCase)
        {
            { "Bone King",    ", granting four bones." },
            { "Sharp Quills", ", sending one damage back to its attacker." },

            // 0.7.304, Zamar's wording. Tutor.RespondsToResolveOnBoard is
            // gated on CardsInDeck > 0, so the sigil cannot fire on an empty
            // deck and the line never promises a choice that is not there.
            { "Hoarder",      ", choose a card from your deck to add to your hand. Arrows browse, Enter to confirm." },
        };

        // Claude, 0.7.284-0.7.315.
        public static string SigilTriggers(string cardName, string sigilName)
        {
            if (!string.IsNullOrEmpty(sigilName))
            {
                string clause;
                if (_sigilClause.TryGetValue(sigilName, out clause))
                    return Loc.F($"{cardName}'s {Loc.Game(sigilName)} ability triggers{Loc.T(clause)}");
            }

            string line = Loc.F($"{cardName}'s {Loc.Game(sigilName)} ability triggers.");

            string flavour;
            if (!string.IsNullOrEmpty(sigilName) &&
                _sigilFlavour.TryGetValue(sigilName, out flavour))
                return $"{line} {Loc.T(flavour)}";

            return line;
        }

        /// <summary>
        /// The same event on several cards at once, as one sentence. One name
        /// falls through to the singular form, so callers never have to choose.
        /// </summary>
        // Claude, 0.7.222-0.7.261.
        public static string SigilTriggers(
            System.Collections.Generic.IList<string> cardNames, string sigilName)
        {
            if (cardNames == null || cardNames.Count == 0) return null;
            if (cardNames.Count == 1) return SigilTriggers(cardNames[0], sigilName);

            // "A, B, and C" - Oxford comma, Session 32 (AndList).
            var rest = new string[cardNames.Count - 1];
            for (int i = 0; i < cardNames.Count - 1; i++) rest[i] = cardNames[i];
            string who = AndList(rest, cardNames[cardNames.Count - 1]);

            string line = Loc.F($"{who}'s {Loc.Game(sigilName)} abilities trigger.");

            string flavour;
            if (!string.IsNullOrEmpty(sigilName) &&
                _sigilFlavourPlural.TryGetValue(sigilName, out flavour))
                return $"{line} {Loc.T(flavour)}";

            return line;
        }

        // ------------------------------------------------------------------
        // THE RED OVERLAY. (0.7.284. No longer provisional - Zamar ruled on
        // both halves.)
        //
        // ChallengeActivationUI is the red command-line panel that flashes
        // for 1.25 seconds. It has exactly two entry points in the whole
        // game and they want different treatment:
        //
        //   TryShowActivation(challenge)  13 call sites, one per challenge.
        //                                 Shows FOUR lines, of which the
        //                                 middle two are a fake progress bar
        //                                 with a random percentage in them.
        //   ShowTextLines(string[])       ONE call site: the Fecundity nerf
        //                                 in DrawCopy.OnResolveOnBoard.
        //
        // ZAMAR, 0.7.284, on the challenge four: "First and last only,
        // that's more of a visual blurb but don't want it to confuse." The
        // two percentage lines are the blurb. The two kept lines are built
        // here the same way the game builds them - Localization.Translate on
        // the same format strings - so the spoken words are the game's own
        // and other languages come free.
        //
        // On the Fecundity three: "Full lines yes, that's exciting lore
        // content that very occasionally stomps on the game being played."
        // So those read verbatim, through SpokenOverlayLine below.
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // WHAT THE PANEL IS, SAID FIRST. (0.7.285.)
        //
        // ZAMAR, after hearing 0.7.284: "Whenever a red text overlay appears,
        // I expect the first line to say 'Challenge Activation.' before
        // getting into it, for clarity as to what just suddenly and
        // unexpectedly happened."
        //
        // The overlay arrives unannounced, on top of whatever is happening,
        // and the first words out of it were a drive path. A sighted player
        // gets the icon, the blink and the red panel before a single word is
        // read; this is that, in one phrase.
        //
        // It leads BOTH entry points, including the Fecundity nerf, which is
        // not a challenge. That is deliberate and it is his call: the phrase
        // names the PANEL - ChallengeActivationUI is the game's own name for
        // it - not the kind of event that summoned it.
        // ------------------------------------------------------------------
        // SESSION 32 - THE LEAD-IN IS GONE FROM THE FECUNDITY PANEL TOO.
        // Asked whether the Fecundity nerf panel should lose "Challenge
        // Activation." and the drive path like the challenge lines did
        // (0.7.359), Zamar: "Yes". So no overlay speaks either any more; the
        // panel is its own lines. OverlayAnnouncement stays as the one
        // composer both entry points share.

        /// <summary>
        /// Everything spoken for one appearance of the red panel: its lines.
        /// One composer, so both entry points cannot drift.
        /// </summary>
        // Claude, 0.7.347-0.7.357. Lead-in cut by Zamar, Session 32.
        public static string OverlayAnnouncement(string lines)
            => lines;

        /// <summary>
        /// The two lines a challenge overlay keeps: its first and its last.
        /// Title is the game's own (AscensionChallengeInfo.title).
        /// </summary>
        /// <remarks>
        /// 0.7.359 — NO LEAD-IN AND NO DRIVE PATH ON A CHALLENGE. Zamar, on
        /// hearing two of them at one battle's start: "remove Challenge
        /// Activation. Z Drive, Floppy Disk, Kaycee Mod from these calls, too
        /// much." So a challenge is its two game lines and nothing else:
        /// "CHALLENGE TRIGGERED: Squirrel Fish. CHALLENGE ACTIVATED: Squirrel
        /// Fish." The Fecundity nerf panel (ShowTextLines) still reads through
        /// OverlayAnnouncement and SpokenOverlayLine with both, because those
        /// were his explicit words for that panel — whether it follows is his
        /// call, asked in PROVISIONAL_LINES.md rather than assumed here.
        /// </remarks>
        // 0.7.360 — ONE LINE PER CHALLENGE. Zamar: "It read the challenge
        // activated lines twice. Just need 1 each. Challenge triggered should
        // remain." So the ACTIVATED half is dropped.
        // Claude, 0.7.284-0.7.315. Lead-in and prefix cut by Zamar, 0.7.359.
        public static string ChallengeOverlay(string title)
        {
            string triggered = string.Format(
                Localization.Translate("CHALLENGE TRIGGERED: {0}"), title);
            return OverlayLineText(triggered);
        }

        // ------------------------------------------------------------------
        // THE LINE PREFIX, AND WHY IT IS A LITERAL HERE.
        //
        // It is NOT in the assembly. It is a serialized field on the
        // SequentialPixelTextLines component of the ChallengeActivationUI
        // prefab (Resources/prefabs/ui/ascension/ChallengeActivationUI.prefab):
        //
        //     linePrefix: 'Z:/FloppyDisk/KAYCEEMOD: '
        //
        // SequentialPixelTextLines.ShowText writes linePrefix + line into
        // every PixelText, so a sighted player sees it on EVERY line of the
        // panel. It is the thing that says what the panel is: a terminal
        // reading a floppy disk on drive Z labelled KAYCEEMOD - Kaycee's own
        // mod console, writing to the disk while the run is played.
        //
        // Dropping it would hide that from a blind player, so it is read.
        // Reading the raw path string would be worse. ZAMAR, 0.7.284, gave
        // the spoken form and the repetition: "Read each line as such.
        // 'Z Drive, Floppy Disk, Kaycee Mod, DEPLOY SIGIL NERF: FECUNDITY.'"
        // Every line, including the repeat, because that is what is on
        // screen.
        //
        // A reflection read of the prefab's serialized field would make this
        // follow the game if it ever changed, at the cost of a member
        // check_source.ps1 would flag [undumped] and a lookup on a path that
        // is only ever one string. Not worth it - but if the prefix ever
        // stops matching the screen, this is the line to change.
        // ------------------------------------------------------------------
        // SESSION 32: no longer spoken - see the lead-in note above. The
        // comment is kept because it explains what the prefix on screen is.

        /// <summary>
        /// One line of an overlay, made speakable without changing its words.
        ///
        /// Two rules, both Zamar's, 0.7.284. The prefix above, on every line.
        /// And the comment marker:
        ///
        ///   "the // it had to be done line in the fecundity thing, or any
        ///    other player facing 'comment line' like that, dont read slash
        ///    slash, make sure it reads as 'comment line: it had to be
        ///    done...'"
        ///
        /// That rule is about the MARKER, not that one line: any overlay line
        /// the game writes as a source comment is announced as one.
        ///
        /// The only other change is a full stop when the line has no closing
        /// punctuation of its own, so the reader pauses between lines instead
        /// of running two of them together. The words themselves stay the
        /// game's.
        /// </summary>
        // Claude, 0.7.284-0.7.315.
        public static string SpokenOverlayLine(string raw)
        {
            // Session 32: the drive-path prefix is no longer read (Zamar).
            return OverlayLineText(raw);
        }

        /// <summary>
        /// One overlay line without the drive-path prefix: the comment marker
        /// said as a comment and a closing full stop, the game's words
        /// otherwise untouched. The challenge overlay reads its two lines this
        /// way from 0.7.359; SpokenOverlayLine puts the prefix in front.
        /// </summary>
        // Claude, 0.7.284-0.7.315.
        public static string OverlayLineText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            string t = raw.Trim();
            if (t.Length == 0) return t;

            if (t.StartsWith("//"))
                t = Loc.F($"Comment line: {t.Substring(2).Trim()}");

            char last = t[t.Length - 1];
            if (last != '.' && last != '!' && last != '?') t += ".";

            return t;
        }

        // ------------------------------------------------------------------
        // THE WOODCARVING GRANT. (0.7.284.)
        //
        // ZAMAR: "When the totem sigil triggers and makes it's sfx by adding
        // the sigil to the kin cards, it should read '[Card name] gains
        // [woodcarving sigil ability].'" Then, on the exact shape:
        // "Flying Ant gains ability: Fecundity." - this is right.
        //
        // THE SECOND LINE IS NOT A VARIANT OF THE FIRST. It reports the
        // opposite outcome: the totem fired, played its sound, and the card
        // gained nothing. See ChallengeNarrator for why that happens and why
        // it repeats forever once it starts.
        // ------------------------------------------------------------------

        /// <summary>The woodcarving inscribed its sigil on a kin card, and it took.</summary>
        // Zamar, 0.7.284.
        public static string TotemGrant(string cardName, string sigil)
            => Loc.F($"{cardName} gains ability: {Loc.Game(sigil)}.");

        /// <summary>
        /// The woodcarving fired and the card gained nothing, because the
        /// copy carries a negate for that sigil. Zamar's words, 0.7.284 -
        /// "Kaycee override", not "developer override": the game frames the
        /// nerf as Kaycee's own mod console deploying it, not a developer's.
        /// </summary>
        // Zamar, 0.7.284.
        public static string TotemGrantNegated(string sigil)
            => Loc.F($"Copy of {Loc.Game(sigil)} sigil negated by Kaycee override.");

        // ------------------------------------------------------------------
        // PROVISIONAL - 0.7.208. Zamar has not approved this one yet; it
        // ships so the event stops being silent, and the log marks every use
        // "IKMA PROVISIONAL". Replace the words, keep the method.
        // ------------------------------------------------------------------

        /// <summary>A totem battle: the opponent's totem inscribes a sigil on a kin.</summary>
        // Claude, 0.7.222-0.7.261.
        public static string OpponentTotem(string actor, string kin, string sigil)
            => Loc.F($"{actor}'s totem gives enemy {Loc.Game(kin)} cards {Loc.Game(sigil)}.");

        /// <summary>Footage starts; the date is the game's own label for the clip.</summary>
        // Claude, 0.7.210.
        public static string FootagePlaying(string date) => Loc.F($"Footage, {date}.");

        // ------------------------------------------------------------------
        // PROVISIONAL - 0.7.211, the Leshy fight. NONE of these are Zamar's
        // words yet. They ship so the final boss is not silent on his first
        // playthrough of it, and every use logs "IKMA PROVISIONAL" so the
        // whole set can be lifted out of one log and rewritten. Replace the
        // words; keep the methods and the facts they carry.
        //
        // The facts each line is allowed to state, and nothing beyond them:
        //   LeshyMaskOn        which mask went on. NOT what it looks like -
        //                      Zamar wrote the Prospector mask's description
        //                      himself and the other two are his to write.
        //   LeshyOrbiter       that three masks now circle his head, and
        //                      which three. Visible; silent before this.
        //   LeshyDeathcardPhase / LeshyMoonPhase
        //                      that the fight changed shape. What ARRIVES is
        //                      left to the board differ and the queue reader.
        //   GiantArrives       one card, how many slots it covers, its live
        //                      attack and health. The width is counted, not
        //                      assumed.
        //   GiantDestroyed     it died. The game's own dialogue follows.
        //   HookAimed / HookPulled / HookCancelled
        //                      where the hook is and what it took.
        //   TradeOffered       that a trade is open AND that the bell is
        //                      dead until it ends. The second half is the
        //                      part a blind player cannot otherwise learn.
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // NO HE/HIM FOR LESHY. Zamar, 2026-09-14: "I do not want to use
        // He/Him pronouns for Leshy." Name Leshy, or recast the sentence.
        // This applies to every line in the mod, not only the ones below —
        // grep this file and the narrators for " he ", " his " and " him "
        // before adding any line about ANY of them.
        //
        // 0.7.220 WIDENED THIS TO THE MASKED BOSSES TOO. He asked for "their
        // hook" on an Angler line, so it is not only Leshy: no actor in this
        // mod takes a gendered pronoun. Every remaining "his" in the Angler,
        // Trapper, Trader and Pirate Skull lines was swept in the same pass.
        //
        // THE ANGLER IS THE ONE EXCEPTION, AND THE GAME MADE IT, 0.7.263.
        // Zamar: "Leshy says 'A large man approaches you' when describing the
        // Angler, so we can safely give the Angler he/him pronouns
        // throughout." That is the project's first rule doing its job — where
        // the GAME has its own word, the game's word wins, and IKMA is not
        // being more careful than the text it is reading aloud.
        //
        // IT IS THE ANGLER ONLY. The sweep still stands for Leshy, the
        // Prospector, the Trapper, the Trader, the Woodcarver (whom the game
        // calls SHE, recorded with her mask below), the Doctor and the Pirate
        // Skull. Do not generalise from this one to the rest; each would need
        // its own line from the game.
        // ------------------------------------------------------------------

        /// <summary>
        /// Leshy takes a mask off the orbiter and wears it. HIS WORDING,
        /// 0.7.217: "Instead of saying this full intro line, just say 'Leshy
        /// puts on the Prospector mask...'" — so the long visual description
        /// he wrote for the Prospector BOSS intro is no longer spoken here.
        /// </summary>
        /// <remarks>
        /// 0.7.239 — AND A LINE ABOUT THE FACE, WHERE ZAMAR HAS WRITTEN ONE.
        /// "I want a bit more description", with the Trader's words attached:
        /// "Leshy puts on the Trader mask. It is a rugged face with a bandana
        /// wrapped tightly below the eyes."
        ///
        /// A mask with no description keeps the trailing "..." form, which is
        /// what 0.7.217 cut the Prospector's long boss intro down to. The
        /// ellipsis is doing real work there: it says a beat is coming and
        /// stops the line sounding finished when it is not.
        ///
        /// Each mask with no words yet logs IKMA PROVISIONAL by name, so one
        /// playtest lists exactly what is still owed rather than "the masks".
        /// A description is never invented here — what a face looks like is
        /// Zamar's to write, and a wrong one would be the mod telling a blind
        /// player something no sighted player is being shown.
        /// </remarks>
        private static readonly System.Collections.Generic.Dictionary<string, string> _maskFaces
            = new System.Collections.Generic.Dictionary<string, string>(
                System.StringComparer.OrdinalIgnoreCase)
        {
            { "Trader", "It is a rugged face with a bandana wrapped tightly below the eyes." },

            // Zamar, 0.7.301, amended 0.7.310 to "They". The Trapper is NOT
            // an exception to the no-gendered-pronouns sweep of 0.7.220 — the
            // Angler still is, and only because the game's own line calls him
            // a man. Asked which way this should go, Zamar moved the mask
            // line rather than the rule, so the rule now holds everywhere it
            // is not the game speaking.
            // 0.7.343 — HE, because the game says so. Zamar, Session 26:
            // "Trapper trader can remain they/them for now unless you can find
            // canonical He's for either of them in the game's dialogue." The
            // Trapper has them: "a Trapper looking to liquidate his pelts",
            // "The Trapper sat hunched beside one of his traps", "But he
            // mentioned that The Trader...". The Act 1 Trader has none, so
            // the Trader keeps "they".
            { "Trapper", "He has a leathery face with deep wrinkles across his forehead." },

            // Zamar, 0.7.248, with the correction that came with it: the
            // Woodcarver is SHE. The game's own line says so — "WHO FIXED HER
            // INTENSE GAZE UPON YOU" — and IKMA had no face to put to it.
            { "Woodcarver", "She has a strong aged face with a patterned headband." },

            // Zamar, 0.7.262, written after seeing the mask in his own Angler
            // fight. Two sentences rather than one, which is his call: the
            // hooks are the thing the mask is named for and they earn their
            // own beat.
            { "Angler",
              "He has deep seated eyes under a heavy brow. " +
              "Multiple fishhooks pierce through his lower lip." },
        };

        // ------------------------------------------------------------------
        // A MASK WHOSE WHOLE SENTENCE IS HIS, NOT JUST ITS FACE. (0.7.286.)
        //
        // _maskFaces above supplies the second half and IKMA composes the
        // first: "Leshy puts on the <mask> mask. <face>". That works while
        // the game's enum name is also what the thing should be CALLED.
        //
        // The Doctor is the case where it is not. Zamar's line names the mask
        // "the two-faced Mycologists mask" and then describes both faces, so
        // composing around the enum value would have produced "the Doctor
        // mask" and contradicted the sentence that follows it. An entry here
        // is spoken exactly as written and nothing is built around it.
        //
        // ZAMAR, 0.7.286, verbatim.
        // ------------------------------------------------------------------
        private static readonly System.Collections.Generic.Dictionary<string, string> _maskWholeLine
            = new System.Collections.Generic.Dictionary<string, string>(
                System.StringComparer.OrdinalIgnoreCase)
        {
            { "Doctor",
              "Leshy puts on the two-faced Mycologist mask. " +
              "The doctor's face is a frizzled hair man with large round glasses " +
              "and a head mirror. Attached to his neck is a second smaller face. " +
              "The mushroom has the same facial features, with a proportionally " +
              "large mushroom fused onto its head." },
        };

        // ------------------------------------------------------------------
        // A CARD THAT IS UNDER THE WATER SAYS SO. (0.7.264.)
        //
        // Zamar: "When a card is underwater due to Waterborne (flipped over)
        // add the adjective 'Submerged' to its card name while it's flipped."
        // His example: "Flying Ant flies over Submerged Kingfisher and attacks
        // directly."
        //
        // WHY IT IS A NAME DECORATION AND NOT A CLAUSE. Being under is not an
        // event that happens once and is then remembered — it is a property of
        // the card for as long as it lasts, and it changes what the card can
        // do in every sentence the card appears in. A trailing clause would
        // have to be repeated on every line or dropped from most of them.
        //
        // ASKED OF THE GAME, TWICE OVER. Submerge.RespondsToUpkeep tests
        // Card.FaceDown and nothing else, and HasAbility(Ability.Submerge)
        // names the ability that put it there. Both are public. The FaceDown
        // half alone would be wrong: the Angler's hook flips a card too, by
        // rotating it 180 degrees on Z rather than by SetFaceDown, and a future
        // face-down mechanic would collect the adjective for free.
        //
        // THE ENUM IS Submerge; THE RULEBOOK CALLS IT Waterborne. Both words
        // are the game's, used where the game uses them — the ability is spoken
        // as "Waterborne", the state as "Submerged", which is his wording.
        //
        // WHERE IT APPLIES: everywhere a LIVE board card is named — combat and
        // damage lines through DamageDeathMerger.SideQualifiedName, the board
        // differ through BoardWatcher.NameOf, and the board read. Not the hand,
        // the queue, the deck or the rulebook: nothing there has a live card to
        // be under water, and a CardInfo read is a printed card, not a
        // position on the table.
        // ------------------------------------------------------------------
        /// <summary>
        /// The draw-phase prompt, in ONE place. (0.7.266.)
        /// </summary>
        /// <remarks>
        /// It was written out twice — Plugin.cs speaks it when the phase opens,
        /// HotkeyManager repeats it on Space — with a comment at the second
        /// copy accepting the duplication so the repeat would match. That was a
        /// fair trade when nothing else needed to recognise the sentence.
        ///
        /// Something does now. The draw line has to cut this prompt and must
        /// NOT cut a combat result, and telling those apart means asking "was
        /// the last thing spoken this exact sentence?" — a question no caller
        /// can ask about a literal that exists in two places.
        /// </remarks>
        // Session 37 (note C9): with the main deck empty the D half is not
        // true, so it is not said - announce what is true. Asked of the pile
        // itself (CardDrawPiles.Deck.CardsInDeck, as the D key does).
        // Claude, 0.7.42 or earlier; the empty-deck form 0.7.413.
        public static string DrawPhasePrompt()
            => MainDeckEmpty()
               ? Loc.T("Draw phase. Press S to draw from the Squirrel deck.")
               : Loc.T("Draw phase. Press D to draw from your deck, or S to draw from the Squirrel deck.");

        private static bool MainDeckEmpty()
        {
            try
            {
                var piles = Singleton<CardDrawPiles>.Instance;
                return piles != null && piles.Deck != null && piles.Deck.CardsInDeck == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// A Waterborne card goes under at the end of its own turn.
        /// </summary>
        /// <remarks>
        /// 0.7.266. HIS SENTENCE, MOVED TO THE EVENT IT DESCRIBES. Zamar:
        /// "there was no audible callout that the Great White submerged at the
        /// end of turn that it killed the opposum."
        ///
        /// THE DIVE HAS NEVER BEEN HEARD, AND THE LINE THAT SAYS "IT DIVES" WAS
        /// FIRING ON THE WAY BACK UP. Submerge has two halves and only one of
        /// them goes through the trigger sequence IKMA listens to:
        ///
        ///   OnTurnEnd  -> SetCardbackSubmerged, SetFaceDown(true), LearnAbility
        ///                 — the dive. NO PreSuccessfulTriggerSequence, so the
        ///                 generic sigil hook cannot see it at all.
        ///   OnUpkeep   -> PreSuccessfulTriggerSequence, SetFaceDown(false)
        ///                 — the RESURFACE, and the only half that fired.
        ///
        /// So "Kingfisher's Waterborne ability triggers. It dives underwater..."
        /// was being spoken at the moment the bird came up. It read as correct
        /// because the bird spends most of the cycle under, and because 0.7.252
        /// moved the line next to the scales, where "it was underwater for the
        /// attacks" is what a player takes from it.
        ///
        /// The sentence is his and it is true of a dive, so it moves to the
        /// dive. The resurface now logs and says nothing: there is no approved
        /// line for a card coming back up, and silence beats a sentence that
        /// describes the opposite of what happened.
        /// </remarks>
        // Zamar, 0.7.266.
        public static string WaterborneDive(string card)
            => Loc.F($"{card}'s Waterborne ability triggers. It dives underwater...");

        // Claude, 0.7.262-0.7.281.
        public static string Submerged(PlayableCard card, string name)
        {
            if (card == null || string.IsNullOrEmpty(name)) return name;

            try
            {
                if (!card.FaceDown) return name;
                if (!card.HasAbility(Ability.Submerge)) return name;
            }
            catch { return name; }

            return Loc.F($"Submerged {name}");
        }

        // 0.7.329, Zamar: "Can we have the node mask descriptions only trigger
        // the first time you encounter those masks each run?" After that, just
        // "Leshy puts on the two-faced Mycologist mask." — the first sentence.
        // A run is RunState.Run itself: a new run is a new object.
        private static object _maskRun;
        private static readonly System.Collections.Generic.HashSet<string> _masksDescribed
            = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        private static bool MaskSeenThisRun(string mask)
        {
            object run = null;
            try { run = RunState.Run; } catch { }
            if (!ReferenceEquals(run, _maskRun)) { _maskRun = run; _masksDescribed.Clear(); }
            return !_masksDescribed.Add(mask ?? "");
        }

        // Claude, 0.7.326-0.7.335.
        public static string LeshyMaskOn(string mask)
        {
            bool seen = MaskSeenThisRun(mask);

            string whole;
            if (!string.IsNullOrEmpty(mask) && _maskWholeLine.TryGetValue(mask, out whole))
            {
                whole = Loc.T(whole);   // Session 32: translated first, so the first-sentence cut works in every language
                if (!seen) return whole;
                int stop = whole.IndexOf(". ");
                return stop > 0 ? whole.Substring(0, stop + 1) : whole;
            }

            if (seen) return Loc.F($"Leshy puts on the {mask} mask.");

            string face;
            if (!string.IsNullOrEmpty(mask) && _maskFaces.TryGetValue(mask, out face))
                return Loc.F($"Leshy puts on the {mask} mask. {Loc.T(face)}");

            Plugin.Log?.LogInfo(Loc.F($"IKMA PROVISIONAL: no face description for the {mask} mask."));
            return Loc.F($"Leshy puts on the {mask} mask...");
        }

        /// <summary>
        /// Leshy takes the mask off again. HIS WORDING, 0.7.217, replacing
        /// "The Prospector fades back into the shadows...": "just say 'Leshy
        /// removes the Prospector's mask. It floats away.' Same with the
        /// other masks."
        /// </summary>
        /// <remarks>
        /// 0.7.263 — THE FLOAT IS LESHY'S FIGHT ONLY. Zamar, after the Angler
        /// was defeated: "The Floats Away addition is only for the Leshy moon
        /// fight. Just 'Leshy removes the Angler's mask.' is fine here."
        ///
        /// In Leshy's own fight the masks orbit his head and one is taken back
        /// by the orbiter, which is the thing "it floats away" describes. At
        /// the end of a boss fight there is no orbiter and nothing floats, so
        /// the clause was describing something not on the screen — the same
        /// defect as "ornate", one rung up.
        ///
        /// One composer, two forms, the caller says which: the boss-defeated
        /// patch passes false and Leshy's own mask cleanup passes true.
        /// </remarks>
        // Zamar, 0.7.217.
        public static string LeshyMaskOff(string mask, bool floatsAway)
            => floatsAway
                ? Loc.F($"Leshy removes the {mask}'s mask, it floats away.")
                : Loc.F($"Leshy removes the {mask}'s mask.");

        /// <summary>
        /// The masks begin circling at the end of the intro. HIS WORDING,
        /// 0.7.217, verbatim including the moon, which IKMA had never
        /// mentioned and which is on screen for the whole fight.
        /// </summary>
        // Zamar, 0.7.217.
        public static string LeshyOrbiter(System.Collections.Generic.List<string> masks)
        {
            if (masks == null || masks.Count == 0) return null;

            string list;
            if (masks.Count == 1)
                list = Loc.F($"the {masks[0]}");
            else
            {
                var head = string.Join(", ", masks.GetRange(0, masks.Count - 1).ToArray());
                list = Loc.F($"the {head}, and the {masks[masks.Count - 1]}");
            }

            return Loc.F($"{NumberWord(masks.Count)} masks circle around Leshy's head: {list}. ") +
                   Loc.T("Behind Leshy is a large bright full moon...");
        }

        /// <summary>
        /// Two lives left: stumps on the board, death cards in the queue.
        /// Recast 0.7.218 to name Leshy rather than use a pronoun.
        /// </summary>
        // Claude, 0.7.217-0.7.221.
        public static string LeshyDeathcardPhase()
            => Loc.T("Leshy takes back a second candle, blocks your board, and calls up the dead.");

        /// <summary>
        /// One life left: the masks fly away and the moon comes down. Recast
        /// 0.7.218 to name Leshy rather than use a pronoun.
        /// </summary>
        // Claude, 0.7.217-0.7.221.
        public static string LeshyMoonPhase()
            => Loc.T("Leshy takes back a last candle. The masks fly away, and Leshy reaches for the moon.");

        // ------------------------------------------------------------------
        // WHICH FACE IS ON, ASKED ON DEMAND. (0.7.222.) PROVISIONAL.
        //
        // Zamar: "During this fight, pressing G, or B for enemy info should
        // first tell you if Leshy is currently wearing a mask and which one."
        //
        // Every other line about the masks is an EVENT — it went on, it came
        // off — and an event is heard once. A player who looked away, or who
        // is deciding what to play three lines later, has no way back to the
        // single most important fact about the enemy side of the table. A
        // sighted player just looks at his face.
        // ------------------------------------------------------------------

        /// <summary>Leshy has a mask on right now.</summary>
        // Claude, 0.7.222-0.7.261.
        public static string LeshyWearingMask(string mask)
            => Loc.F($"Leshy is wearing the {mask}'s mask.");

        /// <summary>Leshy's own face, between masks. His wording, 0.7.223.</summary>
        // Zamar, 0.7.223.
        public static string LeshyNoMask()
            => Loc.T("Leshy is not wearing a mask.");

        /// <summary>
        /// The third candle Leshy adds after the intro line. PROVISIONAL.
        /// (0.7.218 — the skull arrives with two and grows a third a beat
        /// later, so this is its own event and its own line.)
        /// </summary>
        // Claude, 0.7.217-0.7.221.
        public static string LeshyThirdCandle()
            => Loc.T("Leshy lowers a third candle to the flame and lights it...");

        /// <summary>
        /// Skeletons jumping off the pirate ship into the player's slots.
        /// Claude's wording, 0.7.348, approved by Zamar as proposed.
        /// </summary>
        // Claude, 0.7.347-0.7.357.
        public static string PirateMutiny(string card, string ship, System.Collections.Generic.List<int> slots)
        {
            string from = string.IsNullOrEmpty(ship) ? Loc.T("the ship") : ship;
            if (slots.Count == 1)
                return Loc.F($"A {card} jumps off {from} and joins your side in slot {slots[0]}.");
            var parts = new System.Collections.Generic.List<string>();
            foreach (var n in slots) parts.Add(n.ToString());
            string list = AndList(parts.GetRange(0, parts.Count - 1), parts[parts.Count - 1]);
            string count = NumberWord(slots.Count);
            count = char.ToUpper(count[0]) + count.Substring(1);
            return Loc.F($"{count} {card} jump off {from} and join your side in slots {list}.");
        }

        /// <summary>
        /// The ship arrives. Zamar's wording, 0.7.351 ("It has a 15 dancing"
        /// in his message; the stray "a" dropped). The count is the game's.
        /// </summary>
        // Zamar, 0.7.351.
        public static string PirateShipCrashes(string ship, int onDeck)
            => Loc.F($"A massive ship called {ship} crashes onto the board. ") +
               Loc.F($"It has {onDeck} dancing Skeleton Pirates on board making a ruckus.");

        /// <summary>
        /// Appended to the jump line. Zamar asked for the count; the words are
        /// Claude's, 0.7.351.
        /// </summary>
        // Claude, 0.7.347-0.7.357.
        public static string PirateSkeletonsOnDeck(int onDeck)
        {
            if (onDeck < 0) return "";
            // Zamar, 0.7.351: "eleven what. Say skeleton pirates."
            if (onDeck == 0) return Loc.T(" No Skeleton Pirates remain on deck.");
            if (onDeck == 1) return Loc.T(" 1 Skeleton Pirate remains on deck.");
            return Loc.F($" {onDeck} Skeleton Pirates remain on deck.");
        }

        /// <summary>
        /// Item mode, 0.7.354 — Zamar's layout (I to enter, arrows, Enter,
        /// Backspace back to the hand). The words are Claude's proposal.
        /// </summary>
        // 0.7.357 — "Item menu" instead of "Items", Zamar.
        // 0.7.359 — the controls are his Space line's now. His pick of the
        // near-duplicate pair: "both to 'Arrows to Browse, Enter to select,
        // Backspace to return to your hand.'"
        // Claude, 0.7.347-0.7.357.
        public static string ItemModeEntered(string firstItem)
            => Loc.F($"Item menu. {Loc.Game(firstItem)} ") + Vocabulary.ItemModeControls;

        /// <summary>The item menu's keys, said on opening and on Space.</summary>
        // Zamar, 0.7.357. Part of ItemModeEntered and ItemModeWhere.
        public static string ItemModeControls => Loc.T("Arrows to Browse, Enter to select, Backspace to return to your hand.");

        /// <summary>Space inside the item menu. Zamar's wording, 0.7.357.</summary>
        // Zamar, 0.7.357.
        public static string ItemModeWhere => Loc.T("Item menu. ") + Vocabulary.ItemModeControls;

        // Zamar, 0.7.354.
        public static string ItemModeLeft => Loc.T("Your hand.");   // Zamar, 0.7.354

        // His rewrite of Claude's 0.7.357 line, verbatim.
        // Zamar, 0.7.359.
        public static string ItemModeHelp => Loc.T("Item menu. Left and right arrows browse the items you are carrying. " +
            "Enter uses the item you are on. Shift plus I reads all three slots including empty ones. " +
            "Backspace returns you to your hand. Shift plus 1, 2, or 3 quick uses the item in that slot.");

        /// <summary>The skull pulls back before the ship. Zamar's wording, 0.7.350.</summary>
        // Zamar, 0.7.350.
        public static string PirateSkullRecedes()
            => Loc.T("The pirate skull fades backwards... Something is coming...");

        /// <summary>The Pirate Skull fight's skull. Zamar's wording, 0.7.347.</summary>
        // Zamar, 0.7.347.
        public static string PirateSkullPlaced(string who)
            => Loc.F($"{who} places a skull with an eye patch and a pirate hat onto the table.");

        /// <summary>
        /// The skull's "wake_up", just before its first line. Zamar's wording,
        /// 0.7.347.
        /// </summary>
        // Zamar, 0.7.347.
        public static string PirateSkullEyeGlows()
            => Loc.T("The eye of the skull begins to glow.");

        /// <summary>
        /// A single card lying across more than one enemy slot. Leshy's moon
        /// and the Pirate Skull's ship both arrive through this - renamed off
        /// "Leshy" in 0.7.212 when the ship proved it was not his.
        /// </summary>
        // Claude, 0.7.211-0.7.212.
        public static string GiantArrives(string name, int slots, int attack, int health)
        {
            if (slots > 1)
                return Loc.F($"{name} fills the enemy board, one card across all {NumberWord(slots)} slots, {attack}/{health}.");
            return Loc.F($"{name} arrives, {attack}/{health}.");
        }

        /// <summary>The giant is killed in combat. The game speaks next.</summary>
        // Claude, 0.7.211-0.7.212.
        public static string GiantDestroyed(string name)
            => Loc.F($"{name} is destroyed.");

        /// <summary>
        /// The hook hangs over one of the player's slots for the turn.
        /// "their hook" is his wording, 0.7.220.
        /// </summary>
        // Zamar, 0.7.220.
        public static string HookAimed(string who, string card, int slotNumber)
        {
            if (string.IsNullOrEmpty(card))
                return Loc.F($"{who} aims his hook at your empty slot {slotNumber}.");
            return Loc.F($"{who} aims his hook at {card} in slot {slotNumber}.");
        }

        /// <summary>
        /// The hook takes the card across to the opponent's side, and says
        /// where it lands.
        /// </summary>
        /// <remarks>
        /// ONE EVENT, ONE LINE, 0.7.263. Zamar heard the pull and then heard
        /// the board differ report the same card arriving:
        ///
        ///   "The Angler hooks The Smoke and pulls it to their side."
        ///   "The Smoke moves to enemy slot 2."
        ///
        /// His ruling: "Should be 'The Angler hooks The Smoke and pulls it to
        /// his slot 2.'. No second callout for it moving." So the destination
        /// moves into this sentence and the differ is told to stay quiet about
        /// the move — the same shape as Sprinter, Guardian and Burrower, whose
        /// suppression is keyed on the slot the card ends up in.
        /// </remarks>
        // Claude, 0.7.262-0.7.281.
        public static string HookPulled(string who, string card, int slotNumber)
            => Loc.F($"{who} hooks your {card} and pulls it to his slot {slotNumber}.");

        /// <summary>The hooked card was replaced, so the hook comes back empty.</summary>
        // Zamar's wording, Session 25.
        // Zamar, Session 25.
        public static string HookCancelled(string who)
            => Loc.F($"{who} lowers his hook.");

        /// <summary>
        /// The hook is pulled and the slot is empty — the hooked card died
        /// before the pull. PROVISIONAL. (0.7.218, after a false line: IKMA
        /// announced a card being hooked that had been dead for two turns.)
        /// </summary>
        // Zamar's wording, Session 25. Same words as HookCancelled, his call.
        // Zamar, Session 25.
        public static string HookEmpty(string who)
            => Loc.F($"{who} lowers his hook.");

        /// <summary>
        /// A trade is open and the bell is disabled until it ends. The second
        /// sentence is the one that stops a blind player thinking the game has
        /// frozen.
        /// </summary>
        // Zamar, 0.7.302.
        public static string TradeOffered(string who, int onBoard, int inQueue, string peltName)
        {
            string what;
            if (onBoard > 0 && inQueue > 0)
                what = Loc.F($"{NumberWord(onBoard)} on the board and {NumberWord(inQueue)} in the queue");
            else if (onBoard > 0)
                what = Loc.F($"{NumberWord(onBoard)} on the board");
            else if (inQueue > 0)
                what = Loc.F($"{NumberWord(inQueue)} in the queue");
            else
                what = Loc.T("cards");

            // ZAMAR'S WORDING, 0.7.302, verbatim — and it absorbs the pelt
            // the Trader hands over, which used to arrive as two separate
            // lines of its own ("Wolf Pelt is created in your hand." and
            // "Wolf Pelt is added to your hand."). His call: "both should be
            // removed and that should be added to opening line."
            //
            // The pelt is named rather than assumed: TradeCardsForPelts
            // spawns a PeltWolf, so it is always the Wolf Pelt today, but the
            // caller reads the card the game actually created.
            //
            // The last sentence repeats the second. That is how he wrote it
            // and it ships that way; flagged to him rather than tidied.
            string given = string.IsNullOrEmpty(peltName)
                ? Loc.F($"{who} offers a trade.")
                : Loc.F($"{who} gives you a {Loc.Game(peltName)} and offers a trade.");

            return Loc.F($"{given} Trade pelts in your hand for one of their cards. ") +
                   Loc.F($"They have {what}. ") +
                   Loc.T("The bell is disabled until you trade at least once. ") +
                   // Session 29, his pick of the near-duplicate pair: "on their
                   // board and queue", the same words as H.
                   Loc.T("Trade pelts from your hand for cards on their board and queue.");
        }

        // ------------------------------------------------------------------
        // PROVISIONAL - 0.7.212, the Pirate Skull. None of these are Zamar's
        // words yet. He is Kaycee's Mod's fifth boss and only appears with
        // the Final Boss challenge on, so these will sit unheard longer than
        // the Leshy set; every use still logs "IKMA PROVISIONAL".
        // ------------------------------------------------------------------

        /// <summary>One crosshair on a slot holding a card.</summary>
        // Claude, 0.7.211-0.7.212.
        public static string CannonTargetCard(string card, int slotNumber)
            => Loc.F($"{card} in slot {slotNumber}");

        /// <summary>One crosshair on an empty slot.</summary>
        // Claude, 0.7.211-0.7.212.
        public static string CannonTargetEmpty(int slotNumber)
            => Loc.F($"empty slot {slotNumber}");

        /// <summary>
        /// The crosshairs are down and the cannons fire on the next upkeep.
        /// The player's own slot leads, because it is the half they can act
        /// on - one turn to move the card or lose it to ten damage.
        /// </summary>
        // Claude, 0.7.347-0.7.357.
        public static string CannonsAimed(string yours, string theirs)
        {
            if (yours != null && theirs != null)
                return Loc.F($"The cannons aim at {yours} on your side, and {theirs} on theirs.");
            if (yours != null)
                return Loc.F($"The cannons aim at {yours} on your side.");
            return Loc.F($"The cannons aim at {theirs} on their side.");
        }

        /// <summary>Two lives left: the pack is opened and the queue refilled.</summary>
        // PROVISIONAL — 0.7.344, PROVISIONAL_LINES #54. The crosshairs, in
        // the board read and in the slot you are browsing.
        // Claude, 0.7.336-0.7.345.
        public static string CannonAimedAtSlot(int slotNumber)
            => Loc.F($"The cannons are aimed at slot {slotNumber}.");
        // Claude, 0.7.336-0.7.345.
        public static string CannonAimedHere()
            => Loc.T("The cannons are aimed here.");

        // Zamar, Session 25.
        public static string PirateRodentPhase()
            => Loc.T("The Pirate Skull opens a pack of cards and calls in their crew.");   // Zamar's wording, Session 25.

        /// <summary>
        /// One life left. The charge that follows is about fifteen seconds of
        /// noise with nothing spoken over it, so this says what the noise is.
        /// </summary>
        // Claude, 0.7.217-0.7.221.
        public static string PirateShipPhase()
            => Loc.T("The Pirate Skull calls their ship. It bears down on the cabin.");

        // ------------------------------------------------------------------
        // PROVISIONAL - 0.7.215. The rest of the Act 1 bosses. None of these
        // are Zamar's words. Every use logs "IKMA PROVISIONAL".
        // ------------------------------------------------------------------

        /// <summary>The mask he is already wearing is swapped for another.</summary>
        // Zamar, 0.7.302.
        public static string LeshyMaskFlip(string mask)
            // ZAMAR'S WORDING, 0.7.302, verbatim. The old line said what the
            // state change WAS; his says what is on the table.
            => Loc.F($"Leshy removes the mask, flips it upside-down, and puts it back on. He becomes the {mask}.");

        /// <summary>
        /// The Angler's phase two, first half: the table clears.
        /// </summary>
        /// <remarks>
        /// HIS CORRECTION, 0.7.262: "This should only say clears his side of
        /// the table." He is right and the source says so — Opponent.ClearBoard
        /// walks OpponentSlotsCopy and nothing else, so the player's creatures
        /// never move. "Clears the table" claimed a board wipe that does not
        /// happen, and it contradicted the next clause in the same breath,
        /// because the buckets are placed opposite those very creatures.
        ///
        /// THE BUCKETS ARE NO LONGER NAMED HERE, and this is the other half of
        /// his note: "the sets up bait bucket part should come after the Go
        /// Fish conversation." The board differ already does that, by name and
        /// by slot, at the next turn boundary —
        ///   "Enemy Bait Bucket is in slot 2. Enemy Bait Bucket is in slot 3."
        /// — so a summary sentence moved down to sit beside it would be the
        /// same event spoken twice, which is a bug by this project's own rule.
        /// The count is still written to the log, where it costs nothing and
        /// proves the differ reported every bucket that was placed.
        ///
        /// "their", not "his": the no-gendered-pronouns sweep of 0.7.220 covers
        /// every actor in the mod, and the hook lines beside this one already
        /// say "their hook".
        /// </remarks>
        // Claude, 0.7.262-0.7.281.
        public static string AnglerBaitPhase(string who)
            => Loc.F($"{who} clears his side of the table.");

        /// <summary>
        /// Ready if the board differ turns out NOT to speak the shark's
        /// arrival. Not called today - see AnglerNarrator.OnSharkFromBait.
        /// </summary>
        // Claude, 0.7.213-0.7.216.
        public static string AnglerShark(int slotNumber)
            => Loc.F($"A Shark takes the bait's place in slot {slotNumber}.");

        /// <summary>The Trapper's phase two: the table clears and played pelts come back.</summary>
        // 0.7.308, Zamar: "The Trapper sweeps [pronoun] side of the table
        // clean, and one played pelt returns to your hand."
        //
        // "their", not "his" — the no-gendered-pronouns sweep of 0.7.220 is
        // still the standing rule, and the Angler is its only exception
        // because the game's own line calls him a man. Flagged to Zamar,
        // because he wrote "He has a leathery face" for this same character's
        // mask; one of the two should move.
        //
        // And the verb agrees now: "one played pelt RETURNS", "two played
        // pelts RETURN". The old line said "come back" for one pelt.
        // 0.7.343 — the possessive follows the actor: the game calls the
        // Trapper (and Leshy) "his"; the Act 1 Trader has no pronoun in the
        // game's own lines, so keeps "their".
        private static string ActorPossessive(string who)
            => (who ?? "").IndexOf("Trader", System.StringComparison.OrdinalIgnoreCase) >= 0 ? Loc.T("their") : Loc.T("his");

        // Claude, 0.7.336-0.7.345.
        public static string TrapperPhaseTwo(string who, int pelts)
        {
            if (pelts <= 0)
                return Loc.F($"{who} sweeps {ActorPossessive(who)} side of the table clean.");

            string word = pelts == 1 ? Loc.T("pelt") : Loc.T("pelts");
            string verb = pelts == 1 ? Loc.T("returns") : Loc.T("return");
            return Loc.F($"{who} sweeps {ActorPossessive(who)} side of the table clean, and ") +
                   Loc.F($"{NumberWord(pelts)} played {word} {verb} to your hand.");
        }

        /// <summary>The Grizzly Bosses challenge taking over a boss's second phase.</summary>
        // Zamar, Session 32.
        public static string GrizzlyPhase(string who)
            => Loc.F($"The screen glitches and {who}'s eyes glow red. A wall of Grizzlies swarms you...");

        // Zamar, 0.7.361. Replaces the board diff's enemy side after the wipe.
        public static string GrizzliesFillEverySlot => Loc.T("Every opposing slot on the board is filled with Grizzlies...");

        // ------------------------------------------------------------------
        // PROVISIONAL - 0.7.216, the Trader's trade screen. Not Zamar's words.
        // This is the one screen that was a WALL rather than a gap, so these
        // ship to make it finishable; every one is one string to replace.
        // ------------------------------------------------------------------

        /// <summary>The trade opens. Says what to press, because the bell is dead.</summary>
        // Claude, 0.7.284-0.7.315.
        public static string TradeScreenOpen(int options, int pelts)
        {
            string pelt = pelts == 1 ? Loc.T("pelt") : Loc.T("pelts");
            string opt  = options == 1 ? Loc.T("card") : Loc.T("cards");
            // "Begin trading." leads it. Zamar, 0.7.308: "Once you able to
            // arrow to browse, I need a line callout 'Begin trading.'" This
            // composer runs at the moment Options() is populated and the
            // cursor is armed, which is exactly that moment.
            return Loc.T("Begin trading. ") +
                   Loc.F($"Trade: {NumberWord(options)} {opt} to choose from, ") +
                   Loc.F($"{NumberWord(pelts)} {pelt} in hand. ") +
                   Loc.T("Arrow keys to browse, Enter to trade. Press H for help.");
        }

        /// <summary>One card on the trade screen, browsed.</summary>
        // Claude, 0.7.284-0.7.315.
        public static string TradeOption(int index, int total, string described,
                                         bool queued, int slotNumber)
        {
            string where = queued
                ? Loc.F($"in the queue, slot {slotNumber}")
                : Loc.F($"on the board, slot {slotNumber}");
            // "Option" leads the number. Zamar, 0.7.302: "Add 'Option' before
            // those first numbers." A bare "1 of 7" opened a card read with
            // two numbers that were not the card's.
            return Loc.F($"Option {index} of {total}, {where}. {described}");
        }

        /// <summary>A tradable slot whose card cannot be read. Honest, not silent.</summary>
        // Claude, 0.7.284-0.7.315.
        public static string TradeOptionUnreadable(int index, int total)
            => Loc.F($"Option {index} of {total}. {Vocabulary.CardCouldNotBeRead}");

        /// <summary>Enter pressed with no pelt. The game would do nothing; say why.</summary>
        // Claude, 0.7.213-0.7.216.
        public static string TradeNoPelt()
            => Loc.T("You have no pelt to trade with.");

        // ------------------------------------------------------------------
        // CORPSE EATER. (0.7.304.)
        //
        // ZAMAR'S WORDING, verbatim: "[Card that died] dying triggers [Card]'s
        // Corpse Eater ability from your hand, playing itself into slot 3."
        //
        // A DIFFERENT SHAPE FROM EVERY OTHER SIGIL LINE, and that is the
        // point: this sigil fires from the HAND, on something that happened to
        // another card, so the sentence leads with the death rather than with
        // the card that owns the sigil. The generic composer cannot make this,
        // which is why CorpseEater joins the named-elsewhere list.
        //
        // Every value comes from the game's own OnOtherCardDie arguments —
        // the dead card and the slot it died in — captured in the prefix.
        // ------------------------------------------------------------------
        // ------------------------------------------------------------------
        // THE REST OF M5. (0.7.305.) All four are Zamar's words, verbatim.
        // ------------------------------------------------------------------

        /// <summary>
        /// "Beaver's Dam Builder ability triggers, placing a Dam in slot 1 and
        /// slot 3." One slot or two; CreateCardsAdjacent spawns into whichever
        /// neighbours were empty, and none at all is a fizzle, not this line.
        /// </summary>
        // Zamar, 0.7.305.
        public static string AdjacentSpawnTriggers(string card, string sigil,
                                                   string spawned, System.Collections.Generic.List<int> slots)
        {
            if (slots == null || slots.Count == 0) return null;

            string where = slots.Count == 1
                ? Loc.F($"slot {slots[0]}")
                : Loc.F($"slot {slots[0]} and slot {slots[1]}");

            return Loc.F($"{card}'s {Loc.Game(sigil)} ability triggers, placing a {spawned} in {where}.");
        }

        /// <summary>Zamar, 0.7.304.</summary>
        // Zamar, 0.7.304.
        public static string LooseTailTriggers(string card, int slotNumber)
            => Loc.F($"{card}'s Loose Tail ability triggers, it drops its tail as a decoy ") +
               Loc.F($"and moves to slot {slotNumber}.");

        /// <summary>Zamar, 0.7.304.</summary>
        // Zamar, 0.7.304.
        public static string AmorphousTriggers(string card, string gained)
            => Loc.F($"{card}'s Amorphous ability triggers, it mutates and gains {gained}.");

        /// <summary>
        /// Zamar, 0.7.304. The actor is read from the game rather than
        /// hardcoded to the Moon — SquirrelOrbit is the Moon's today and the
        /// line should not assume it stays that way.
        /// </summary>
        // Zamar, 0.7.304.
        public static string TidalLockTriggers(string card, string pulled)
            => Loc.F($"{card}'s Tidal Lock ability triggers, pulling your {pulled} into orbit, obliterating it.");

        /// <summary>Zamar, 0.7.304.</summary>
        // Zamar, 0.7.304.
        public static string TrinketBearerTriggers(string card, string item, int slotNumber)
            => Loc.F($"{card}'s Trinket Bearer ability triggers, a {Loc.Game(item)} is added to item slot {slotNumber}.");

        // ------------------------------------------------------------------
        // THE FIZZLE. (0.7.305.)
        //
        // ZAMAR APPROVED THIS WORDING 2026-09-12 and it has waited since:
        // "Bloodhound's Guardian ability does nothing."
        //
        // It is the SAME line, not a second one. One event is one line, and
        // the outcome picks which — the shake on screen is what a sighted
        // player gets, and without this IKMA announced a trigger that
        // achieved nothing and then went quiet.
        // ------------------------------------------------------------------
        // ------------------------------------------------------------------
        // THE BUG-REPORT EXPORT. (0.7.306.) PROVISIONAL — mine, not his.
        //
        // The file name is spoken in full because that is what the player
        // looks for on the desktop, and a blind player dragging it into
        // Discord needs to be able to find it by name. It carries the version
        // and a timestamp, so both are said whether or not anyone reads the
        // file.
        //
        // The failure line says WHY, because a key that answers with a bare
        // "that didn't work" leaves a player with nothing to tell anyone.
        // ------------------------------------------------------------------
        /// <summary>
        /// The bug-report key, said on every screen that teaches keys.
        /// (0.7.307.)
        /// </summary>
        /// <remarks>
        /// Zamar: "Add this command to the H key of the Pause Screen and main
        /// menus of the main game and KM. Specify that it works at any point
        /// during gameplay too."
        ///
        /// ONE COMPOSER, THREE CALLERS — the rule this project learned when the
        /// card choice screen's opening sentence existed in two places and only
        /// one of them was taught the rare-card wording. A key described three
        /// different ways is three chances to be wrong about it.
        ///
        /// PROVISIONAL: these are my words, not his.
        /// </remarks>
        // ZAMAR'S WORDING, 0.7.308, verbatim — no longer provisional.
        // 0.7.423 - his rewrite, verbatim, adding the history keys.
        // Zamar, 0.7.423.
        public static string BugReportKeyHelp()
            => Loc.T("Shift L saves a log file to your desktop for a bug report, " +
               "and Control plus Up or Down arrow scrolls the log history. " +
               "Those commands work here and at any point during gameplay.");

        // 0.7.312, Zamar: the file name is a version, a date and a time read
        // out digit by digit, and it tells the player nothing they need —
        // theirs is the only one on the desktop and they are about to drag
        // it. The name is still in the log for us.
        // Claude, 0.7.284-0.7.315.
        public static string LogExported(string fileName)
            => Loc.T("Log saved to your desktop as a .txt file. Attach it to your bug report.");

        // Claude, 0.7.284-0.7.315.
        public static string LogExportFailed(string reason)
            => Loc.F($"The log could not be saved because {reason}.");

        // Claude, 0.7.110-0.7.206.
        // 0.7.360: "ability fizzles.", his rewording (was "does nothing.").
        public static string SigilFizzles(string cardName, string sigilName)
            => Loc.F($"{cardName}'s {Loc.Game(sigilName)} ability fizzles.");

        // Zamar, 0.7.304.
        public static string CorpseEaterTriggers(string deadCard, string eater, int slotNumber)
            => Loc.F($"{deadCard} dying triggers {eater}'s Corpse Eater ability from your hand, ") +
               Loc.F($"playing itself into slot {slotNumber}.");

        /// <summary>A trade went through. The pelt is the game's card name
        /// (Rabbit Pelt, Wolf Pelt...); "Pelt" only if IKMA could not read it.</summary>
        // "Pelt" fallback PROVISIONAL (Claude, Session 32); the sentence is his.
        // Zamar, Session 32: "[Pelt type] traded for [card name]".
        public static string TradeTaken(string pelt, string card)
            => Loc.F($"{(string.IsNullOrEmpty(pelt) ? PeltFallback() : pelt)} traded for {card}.");

        // Session 37 - Zamar: the bare "Pelt" fallback is OK to say, but if it
        // ever fires it is a bug, so it is logged as one.
        private static string PeltFallback()
        {
            Plugin.Log?.LogWarning("IKMA BUG: a trade line could not read which pelt paid - said \"Pelt\".");
            return Loc.T("Pelt");
        }

        /// <summary>H on the trade screen.</summary>
        // ZAMAR'S WORDING, 0.7.308, verbatim. The option count is gone from
        // it at his instruction — counts and positions belong to one key, and
        // on this screen that is Space. "their", per the standing rule; see
        // TrapperPhaseTwo.
        // Zamar, 0.7.308.
        public static string TradeHelp(int options, int pelts)
        {
            string pelt = pelts == 1 ? Loc.T("pelt") : Loc.T("pelts");
            return Loc.T("Trade pelts from your hand for cards on their board and queue. ") +
                   Loc.T("Arrow keys browse, Enter trades. ") +
                   Loc.F($"You have {NumberWord(pelts)} {pelt} in hand. ") +
                   Loc.T("Trade until you run out of pelts, then the battle will continue.");
        }

        // ==================================================================
        // A THING IS DESTROYED. A CREATURE DIES. (0.7.219.)
        //
        // Zamar: "make a list of any non-living cards, like the robots,
        // boulders, the smoke, gold nuggets, etc. When cards die, it should say
        // 'is destroyed' instead of dies. I'd still like dies for the living
        // creatures though."
        //
        // THE GAME ALREADY ANSWERS MOST OF THIS. Trait.Terrain is on 32 of the
        // 33 cards that carry either Terrain or Structure, and it is the game's
        // own word for a thing rather than a creature: Boulder, Gold Nugget,
        // Stump, Grand Fir, Tombstone, Broken Bot, Bridge Rails, Dam, Chime,
        // The Stones, Broken Egg, Annoy FM, Conduit Tower, every Mox, and the
        // Act 2/3 pieces. So the trait is the RULE and not a list to maintain.
        //
        // TWO CORRECTIONS ON TOP, AND BOTH ARE JUDGMENT CALLS THAT ARE HIS —
        // the table is where he moves the line, one name at a time:
        //
        //   ADDED, because the trait misses them. The Smoke carries NO traits
        //   at all (data\cards\specialpart1\Smoke.asset), so nothing marks it
        //   as the object it plainly is.
        //
        //   EXCLUDED, because the trait is too broad. Caged Wolf, Strange Frog
        //   and Frozen Opossum are all Structure+Terrain in the data and all
        //   contain a live animal; Beehive is Structure and full of bees. A
        //   sighted player watching a Caged Wolf go down is not watching a
        //   rock break.
        //
        // Card NAMES here are the game's internal ids, which is the one place
        // this project uses them — an id is stable and a display name is
        // localised. They are never spoken.
        // ==================================================================
        private static readonly System.Collections.Generic.HashSet<string> _nonLivingIds =
            new System.Collections.Generic.HashSet<string>
            {
                "Smoke", "Smoke_Improved", "Smoke_NoBones",

                // Beehive carries Structure but NOT Terrain, so the trait rule
                // alone misses it. His call, 0.7.220: "Beehive is also
                // destroyed."
                "Beehive",
            };

        private static readonly System.Collections.Generic.HashSet<string> _livingDespiteTrait =
            new System.Collections.Generic.HashSet<string>
            {
                // Strange Frog is a 1/2 with Reach and the Squirrel tribe — an
                // animal that LEAVES a Leaping Trap behind when it dies
                // (SpecialTriggeredAbility.TrapSpawner). The frog is the
                // creature and the trap is the leftover, so it dies.
                "TrapFrog",
            };

        // ==================================================================
        // A CONTAINER BREAKS AND SOMETHING COMES OUT. (0.7.220.)
        //
        // Zamar: "Caged wolf dying should say 'the cage is destroyed' or 'the
        // ice is destroyed'."
        //
        // His wording, and the code says it is exactly right — neither card is
        // a creature dying, and neither is a thing being destroyed. Both are a
        // BOX opening:
        //
        //   Caged Wolf   SpecialTriggeredAbility.CagedWolf -> BreakCage():
        //                plays "wolf_cage_break", swaps the card in the deck
        //                for a Wolf, and CREATES A WOLF IN THE SAME SLOT. The
        //                wolf does not die; it gets out.
        //
        //   Frozen Opossum  Ability.IceCube ("Frozen Away") -> releases the
        //                creature inside into the slot. SigilNarrator already
        //                names what came out; this names what broke.
        //
        // So the SUBJECT of the sentence changes, not just the verb, which is
        // why these carry a whole clause rather than a word. A clause that
        // begins "the " supplies its own subject and the card's name is
        // dropped from the standalone form — see DeathSentence.
        // ==================================================================
        private static readonly System.Collections.Generic.Dictionary<string, string> _containerDeathClause =
            new System.Collections.Generic.Dictionary<string, string>
            {
                { "CagedWolf",     "the cage is destroyed" },
                { "FrozenOpossum", "the ice is destroyed"  },
            };

        /// <summary>
        /// Is this card a thing rather than a creature? Asked of the game's own
        /// Trait.Terrain first, then corrected by the two tables above.
        /// </summary>
        public static bool IsNonLiving(CardInfo info)
        {
            if (info == null) return false;

            string id = null;
            try { id = info.name; } catch { }

            if (!string.IsNullOrEmpty(id))
            {
                if (_livingDespiteTrait.Contains(id)) return false;
                if (_nonLivingIds.Contains(id)) return true;
            }

            try { return info.HasTrait(Trait.Terrain); }
            catch { return false; }
        }

        /// <summary>
        /// What happens to this card, as the predicate of a sentence whose
        /// subject is the card: "dies", "is destroyed", or a container's own
        /// clause like "the cage is destroyed".
        /// </summary>
        // Claude, 0.7.217-0.7.221.
        public static string DeathClause(CardInfo info)
        {
            string id = null;
            try { id = info?.name; } catch { }

            string clause;
            if (!string.IsNullOrEmpty(id) && _containerDeathClause.TryGetValue(id, out clause))
                return Loc.T(clause);

            return IsNonLiving(info) ? Loc.T("is destroyed") : Loc.T("dies");
        }

        /// <summary>
        /// The whole sentence. A clause that brings its own subject ("the cage
        /// is destroyed") replaces the card's name rather than trailing it —
        /// "Caged Wolf the cage is destroyed." is not a sentence.
        /// </summary>
        // Claude, 0.7.217-0.7.221.
        public static string DeathSentence(string name, CardInfo info)
        {
            string clause = DeathClause(info);

            // Session 32: decided by WHERE the clause came from (a container's
            // own clause brings its own subject), not by its first word, which
            // is "the " only in English. Same result in English.
            string sentence = ClauseHasOwnSubject(info)
                ? char.ToUpperInvariant(clause[0]) + clause.Substring(1) + "."
                : $"{name} {clause}.";

            return sentence + DeathFlavour(info);
        }

        // ------------------------------------------------------------------
        // WHAT A PARTICULAR CARD'S DEATH LOOKS LIKE. (0.7.301.)
        //
        // Zamar: "'Leaping Trap is destroyed. It quickly snaps shut...'"
        //
        // Keyed on the card's own id, so it cannot leak onto anything else,
        // and appended rather than replacing the sentence — the death itself
        // is still reported in the shape every other card uses. A card with
        // no entry gets nothing; this is not a place to invent atmosphere for
        // cards he has not written.
        // ------------------------------------------------------------------
        private static readonly System.Collections.Generic.Dictionary<string, string> _deathFlavour
            = new System.Collections.Generic.Dictionary<string, string>
        {
            { "SteelTrap", " It quickly snaps shut..." },
        };

        // Session 32. True when the card's death clause is a container's own
        // ("the cage is destroyed"), which replaces the card's name.
        private static bool ClauseHasOwnSubject(CardInfo info)
        {
            string id = null;
            try { id = info?.name; } catch { }
            return !string.IsNullOrEmpty(id) && _containerDeathClause.ContainsKey(id);
        }

        private static string DeathFlavour(CardInfo info)
        {
            string id = null;
            try { id = info?.name; } catch { }
            if (string.IsNullOrEmpty(id)) return "";

            string flavour;
            return _deathFlavour.TryGetValue(id, out flavour) ? Loc.T(flavour) : "";
        }

        // ------------------------------------------------------------------
        // KILLED BY THE TRAP IT WAS FACING. (0.7.301.)
        //
        // Zamar: "'Elder Ouroboros gets caught in the Leaping Trap and
        // dies.'"
        //
        // THE GAME STATES THIS CAUSE ITSELF, which is the only reason the
        // line is allowed to claim it. SteelTrap.OnDie calls
        //   Card.Slot.opposingSlot.Card.Die(wasSacrifice: false, base.Card)
        // passing the trap as the KILLER argument. So IKMA is reading the
        // game's own answer to "what killed this", not inferring one from
        // what happened to be on the board.
        //
        // The verb still comes from DeathClause, so a non-living victim reads
        // "is destroyed" here exactly as it does everywhere else.
        // ------------------------------------------------------------------
        // Zamar, 0.7.301.
        public static string CaughtInTrapSentence(string name, CardInfo info, string trapName)
        {
            if (string.IsNullOrEmpty(trapName)) return DeathSentence(name, info);
            return Loc.F($"{name} gets caught in the {trapName} and {DeathClause(info)}.");
        }

        // ------------------------------------------------------------------
        // THE TRAP'S OWN DEATH, WHEN IT SPRINGS. (0.7.424.)
        //
        // Zamar, Session 39: "Leaping Trap is destroyed, its ability Steel
        // Trap triggers." His words. The sigil's name is the game's.
        // ------------------------------------------------------------------
        // Zamar, 0.7.424.
        public static string TrapDestroyedTriggers(string name, CardInfo info, string sigil)
            => Loc.F($"{name} {DeathClause(info)}, its ability {Loc.Game(sigil)} triggers.") + DeathFlavour(info);

        /// <summary>
        /// The tail for a sentence that already supplies a verb — "X is cut
        /// and ___", "X takes 3 damage and ___".
        /// </summary>
        // Claude, 0.7.217-0.7.221.
        public static string DeathTail(CardInfo info)
        {
            string clause = DeathClause(info);
            if (clause == Loc.T("is destroyed")) return Loc.T("destroyed");
            return clause;
        }

        /// <summary>"dies" or "is destroyed", for one card. Kept for callers
        /// that want the bare predicate.</summary>
        public static string DiesVerb(CardInfo info) => DeathClause(info);

        /// <summary>The tail form. See DeathTail.</summary>
        public static string DiesWord(CardInfo info) => DeathTail(info);

        /// <summary>
        /// Small counts as words mid-sentence, for the same reason
        /// BossNarrator does it: a digit inside a sentence reads badly.
        /// </summary>
        // Claude, 0.7.347-0.7.357.
        internal static string NumberWord(int n)
        {
            switch (n)
            {
                case 1: return Loc.T("one");
                case 2: return Loc.T("two");
                case 3: return Loc.T("three");
                case 4: return Loc.T("four");
                case 5: return Loc.T("five");
                case 6: return Loc.T("six");
                case 7: return Loc.T("seven");
                case 8: return Loc.T("eight");
                default: return n.ToString();
            }
        }

        // ==================================================================
        // SHARED LINES (M7, 0.7.358). Sentences more than one screen says,
        // collapsed from their duplicate copies into one home each.
        // ==================================================================
        // Claude, 0.7.42 or earlier. Key: Angler. Said from 2 places.
        public static string Angler => Loc.T("Angler");
        // Claude, 0.7.42 or earlier. Said from 2 places.
        public static string BoneCount(int bones)
            => bones == 1 ? Loc.T("1 bone") : Loc.F($"{bones} bones");
        // Claude, 0.7.42 or earlier. Key: Bonelord. Said from 2 places.
        public static string BoneLord => Loc.T("Bone Lord");
        // Claude, 0.7.42 or earlier. Said from 2 places.
        public static string BoneOrBones(int amount)
            => amount == 1 ? Loc.T("bone") : Loc.T("bones");
        // Claude, 0.7.347-0.7.357. Said from 2 places.
        public static string BothOrAll(int count)
            => count == 2 ? Loc.T("Both") : Loc.T("All ") + Vocabulary.NumberWord(count);
        // Zamar, 0.7.337. Part of DeckPick.HelpLine, NodeScreens.SelectOneOfItems, NodeScreens.ArrowsBrowseEnterChoosesSpace.
        public static string BrowseChooseRepeat_ => Loc.T("Arrows browse, Enter chooses, Space repeats your position. ");
        // Claude, 0.7.42 or earlier. Key: CardStatBoost. Said from 2 places.
        public static string Campfire => Loc.T("Campfire");
        // Claude, 0.7.53-0.7.109. Every unreadable card, Session 29 — his pick
        // of the near-duplicate pair, which also retired Cards.ThisCardCouldNot.
        public static string CardCouldNotBeRead => Loc.T("This card could not be read.");
        // Claude, 0.7.42 or earlier. Said from 7 places.
        public static string CardCount(int count)
            => count == 1 ? Loc.T("1 card") : Loc.F($"{count} cards");
        // Claude, 0.7.53-0.7.109. Said from 3 places.
        public static string CardOrUnreadable(string describeCardInfo)
            => describeCardInfo ?? Vocabulary.CardCouldNotBeRead;
        // Claude, 0.7.42 or earlier. Said from 3 places.
        public static string DamageCount(int damage)
            => damage == 1 ? Loc.T("1 damage") : Loc.F($"{damage} damage");
        // Claude, 0.7.53-0.7.109. Said from 2 places.
        public static string DeathCard => Loc.T("Death card");
        // Claude, 0.7.110-0.7.206. Said from 2 places.
        public static string HealthRemainingCount(int h)
            => h == 1 ? Loc.T("1 health remaining") : Loc.F($"{h} health remaining");
        // Claude, 0.7.42 or earlier. Key: GainConsumables. Said from 2 places.
        public static string ItemPickupName => Loc.T("Item pickup");
        // Zamar, 0.7.49. His call, 0.7.49: "Change that word tribe to Kin." CardReader.KinWording puts it into every line. Said from 2 places.
        public static string Kin => Loc.T("Kin");
        // Claude, 0.7.42 or earlier. Key: Leshy, Single. Said from 5 places.
        public static string Leshy => Loc.T("Leshy");
        // Claude, 0.7.42 or earlier. Key: DuplicateMerge. Said from 2 places.
        public static string Mycologists => Loc.T("Mycologists");
        // Claude, 0.7.53-0.7.109. Said from 5 places.
        public static string OptionOf(string body, int number, int optionCount)
            => Loc.F($"{body} Option {number} of {optionCount}.");
        // Claude, 0.7.53-0.7.109. Said from 2 places.
        public static string PartNameOrUnnamed(string partName)
            => partName ?? Vocabulary.UnnamedOption;
        // Claude, 0.7.42 or earlier. Key: Prospector. Said from 2 places.
        public static string Prospector => Loc.T("Prospector");
        // Claude, 0.7.110-0.7.206. Said from 3 places.
        public static string ReceivedBones(int bones)
            => " " + Vocabulary.ReceivedBonesLine(bones);
        // Claude, 0.7.110-0.7.206. The one bone sentence. Session 29, his pick of
        // the near-duplicate pair: every bone gain says "Received 3 bones.",
        // including the ones that said "3 bones received." (mid-battle, and
        // after the Prospector's pickaxe).
        public static string ReceivedBonesLine(int bones)
            => Loc.F($"Received {bones} {Vocabulary.BoneOrBones(bones)}.");
        // Claude, 0.7.42 or earlier. Key: CardMerge. Said from 2 places.
        public static string SacrificeStone => Loc.T("Sacrifice stone");
        // Claude, 0.7.42 or earlier. Key: TradePelts. Said from 3 places.
        public static string Trader => Loc.T("Trader");
        // Claude, 0.7.42 or earlier. Key: BuyPelts, Trader, Trapper. Said from 5 places.
        public static string Trapper => Loc.T("Trapper");
        // Claude, 0.7.53-0.7.109. Said from 2 places.
        public static string UnnamedOption => Loc.T("Unnamed option");
        // Claude, 0.7.53-0.7.109. Key: BuildTotem. Said from 2 places.
        public static string Woodcarver => Loc.T("Woodcarver");
        // Claude, 0.7.53-0.7.109. Said from 2 places.
        public static string YourDeck => Loc.T("Your deck.");

        /// <summary>The mod itself: loading and version.</summary>
        public static class Mod
        {
            // Claude, 0.7.42 or earlier.
            public static string VersionOrUnknown(string attr)
                => attr ?? Loc.T("unknown");

            // Claude, 0.7.42 or earlier.
            public static string UnknownVersion => Loc.T("unknown");

            // Claude, 0.7.53-0.7.109.
            public static string IkmaVersionLoaded(string pluginVersion)
                => Loc.F($"IKMA version {pluginVersion} loaded.");

            // Claude, 0.7.42 or earlier.
            public static string Loading => Loc.T("Loading.");

            // Claude, Session 32. PROVISIONAL. Auto-update: a signed update
            // was downloaded; it installs at the next game start.
            public static string UpdateDownloaded(string version)
                => Loc.F($"IKMA version {version} downloaded. It installs the next time you start the game.");

            // Claude, Session 32. PROVISIONAL. Auto-update: said once, right
            // after "IKMA version X loaded", on the start that installed it.
            public static string UpdatedFrom(string previousVersion)
                => Loc.F($"Updated from version {previousVersion}.");

            // Claude, Session 32. PROVISIONAL, keys included. AskFirst mode:
            // a newer version exists; nothing downloaded yet.
            public static string UpdateAvailableAsk(string version)
                => Loc.F($"IKMA version {version} is available. Press F9 to download it, or F10 to skip this version.");

            // Claude, Session 32. PROVISIONAL, keys included. DownloadThenAsk
            // mode: downloaded and checked, waiting for a yes.
            public static string UpdateDownloadedAsk(string version)
                => Loc.F($"IKMA version {version} is downloaded. Press F9 to install it the next time you start the game, or F10 to skip this version.");

            // Claude, Session 32. PROVISIONAL. F9 on the AskFirst question.
            public static string UpdateDownloading(string version)
                => Loc.F($"Downloading IKMA version {version}.");

            // Claude, Session 32. PROVISIONAL. F9 on the DownloadThenAsk question.
            public static string UpdateWillInstall(string version)
                => Loc.F($"IKMA version {version} installs the next time you start the game.");

            // Zamar, Session 32 (applied Session 34). F10 on either question.
            public static string UpdateSkipped(string version)
                => Loc.F($"Skipping IKMA version {version}.");

            // Zamar, Session 32 (applied Session 34). A downloaded update that
            // failed its signature check; it was thrown away.
            public static string UpdateFailedSafety => Loc.T("The IKMA update failed its safety check and was not installed.");

            // Claude, Session 32. PROVISIONAL. A download the player said yes
            // to did not arrive or did not verify.
            public static string UpdateDownloadFailed => Loc.T("The IKMA update could not be downloaded. IKMA will offer it again the next time you start the game.");
        }

        /// <summary>The opening black screen.</summary>
        public static class Boot
        {
            // Claude, 0.7.222-0.7.261.
            public static string OpeningScreenEnterPresses => Loc.T("Opening screen. Enter presses the play button. Space repeats the description.");
        }

        /// <summary>Inscryption's own title screen.</summary>
        public static class Title
        {
            // Claude, 0.7.53-0.7.109. Said from 2 places.
            public static string OptionFallback => Loc.T("Option");

            // Game, 0.7.222-0.7.261. The game's own menu word.
            public static string NewGame => Loc.T("New Game");

            // Game, 0.7.222-0.7.261. The game's own menu word.
            public static string Continue => Loc.T("Continue");

            // Game, 0.7.53-0.7.109. The game's own menu word.
            public static string Options => Loc.T("Options");

            // Game, 0.7.222-0.7.261. The game's own menu word.
            public static string Credits => Loc.T("Credits");

            // Game, 0.7.222-0.7.261. The game's own menu word.
            public static string Quit => Loc.T("Quit");

            // Game, 0.7.222-0.7.261. The game's own menu word.
            public static string CardLibrary => Loc.T("Card Library");

            // Game, 0.7.222-0.7.261. The game's own menu word.
            public static string EditDeck => Loc.T("Edit Deck");

            // Game, 0.7.222-0.7.261. The game's own menu word.
            public static string KayceesMod => Loc.T("Kaycee's Mod");

            // Game, 0.7.222-0.7.261. The game's own menu word.
            public static string ReturnToStartMenu => Loc.T("Return To Start Menu");

            // Game, 0.7.222-0.7.261. The game's own menu word.
            public static string EndRun => Loc.T("End Run");

            // Game, 0.7.222-0.7.261. The game's own menu word.
            public static string Concede => Loc.T("Concede");

            // Claude, 0.7.222-0.7.261.
            public static string InscryptionTitleScreenArrows => Loc.T("Inscryption title screen. Arrows to navigate, Enter to confirm.");

            // Claude, 0.7.284-0.7.315.
            public static string InscryptionTitleScreenArrow()
                => Loc.T("Inscryption title screen. Arrow keys browse the cards. ") +
                   Loc.T("Enter chooses the one you are on. Space repeats it. ") +
                   Vocabulary.BugReportKeyHelp();
        }

        /// <summary>Kaycee's Mod menus (MenuReader).</summary>
        public static class Menus
        {
            // Zamar, 0.7.105. Key: AscensionStartScreen, StartScreen. Said from 2 places.
            public static string InscryptionKayceesModMain => Loc.T("Inscryption. Kaycee's Mod main menu");

            // Claude, 0.7.42 or earlier. Key: AscensionUnlocksSummaryScreen.
            public static string Unlocks => Loc.T("Unlocks");

            // Claude, 0.7.222-0.7.261. Key: AscensionCardsSummaryScreen.
            public static string CardsUnlocked => Loc.T("Cards Unlocked");

            // Claude, 0.7.42 or earlier. Key: AscensionCardsScreen.
            public static string Cards => Loc.T("Cards");

            // Claude, 0.7.42 or earlier. Key: AscensionChallengeScreen.
            public static string Challenges => Loc.T("Challenges");

            // Claude, 0.7.42 or earlier. Key: AscensionChallengeConfirmScreen.
            public static string ConfirmChallenges => Loc.T("Confirm Challenges");

            // Zamar, 0.7.236. Key: AscensionChooseStarterDeckScreen, AscensionStarterDeckScreen. Said from 2 places.
            public static string SelectStarterDeck => Loc.T("Select Starter Deck");

            // Claude, 0.7.42 or earlier. Key: AscensionStatsScreen.
            public static string Stats => Loc.T("Stats");

            // Claude, 0.7.42 or earlier. Key: AscensionRunEndScreen.
            public static string RunComplete => Loc.T("Run Complete");

            // Claude, 0.7.42 or earlier. Key: AscensionJournalEntryScreen, AscensionJournalSummaryScreen. Said from 2 places.
            public static string Devlog => Loc.T("Devlog");

            // Claude, 0.7.42 or earlier. Key: AscensionChallengeUnlockScreen.
            public static string NewChallenges => Loc.T("New Challenges");

            // Claude, 0.7.42 or earlier. Key: AscensionStarterDeckUnlockScreen.
            public static string NewStarterDeck => Loc.T("New Starter Deck");

            // His words, confirmed Session 32 ("Mine"). No full stop: screen
            // names are joined into sentences that add their own.
            // Zamar, 0.7.237. Key: AscensionStarterDeckSummaryScreen.
            public static string StarterDecksUnlocked => Loc.T("Starter Deck Unlocked");

            // Claude, 0.7.222-0.7.261. Key: AscensionNewRunConfirmScreen.
            public static string NewRun => Loc.T("New run");

            // Claude, 0.7.42 or earlier.
            public static string MenuOrPrettyName(bool noPretty, string pretty)
                => noPretty ? Loc.T("Menu") : pretty;

            // Claude, 0.7.222-0.7.261.
            public static string CoordsExpanded => Loc.T("coordinates");

            // Claude, 0.7.42 or earlier.
            public static string EntryLine(int entryId, string clean)
                => Loc.F($"Entry {entryId}. {clean}");

            // Claude, 0.7.42 or earlier.
            public static string NoOptionsAvailable(string screenName)
                => Loc.F($"{screenName}. No options available.");

            // Claude, 0.7.42 or earlier.
            public static string BackspaceToGoBack => Loc.T("Backspace to go back.");

            // Claude, 0.7.42 or earlier.
            public static string ArrowsToNavigate => Loc.T("Arrows to navigate");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string AnnounceScreenBackspaceToGo(bool hasBack)
                => hasBack ? Loc.T(", Backspace to go back.") : ".";

            // Claude, 0.7.42 or earlier.
            public static string ArrowsToNavigateOrEnterToConfirm(int count)
                => count > 1
                   ? Loc.T("Arrows to navigate, Enter to confirm")
                   : Loc.T("Enter to confirm");

            // Claude, 0.7.347-0.7.357.
            public static string ScreenEntryControls(string screenName, string entry, string controls)
                => $"{screenName}. {entry} {controls}";

            // Claude, 0.7.347-0.7.357.
            public static string ScreenThenControls(string screenName, string controls)
                => $"{screenName}. {controls}";

            // Claude, 0.7.347-0.7.357.
            public static string Name(string screenName, string total, string controls)
                => $"{screenName}. {total} {controls}";

            // Claude, 0.7.347-0.7.357.
            public static string ScreenBodyControls(string screenName, string body, string controls)
                => $"{screenName}. {body} {controls}";

            // Claude, 0.7.347-0.7.357.
            public static string ScreenThenControlsNoBody(string screenName, string controls)
                => $"{screenName}. {controls}";

            // Zamar, 0.7.53-0.7.109.
            public static string PositionNounFor(bool isAscensionStarterDeckIcon)
                => isAscensionStarterDeckIcon ? Loc.T("Deck") : Loc.T("Option");

            // Claude, 0.7.43-0.7.52.
            public static string OfCount(string noun, int index, int count)
                => noun != null
                   ? Loc.F($"{noun} {index} of {count}.")
                   : Loc.F($"{index} of {count}.");

            // Claude, 0.7.222-0.7.261.
            public static string EnabledOrDisabled(bool nowActive)
                => nowActive ? Loc.T("enabled") : Loc.T("disabled");

            // Claude, 0.7.222-0.7.261.
            public static string ChallengeFallback => Loc.T("Challenge");

            // Claude, 0.7.222-0.7.261.
            public static string ChallengePointCount(int points)
                => points == 1 ? Loc.T("1 challenge point.") : Loc.F($"{points} challenge points.");

            // Claude, 0.7.222-0.7.261.
            public static string OfChallengePoints(int points, int needed)
                => Loc.F($"{points} of {needed} challenge points.");

            // Claude, 0.7.42 or earlier.
            public static string BackPressed => Loc.T("Back.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string UnknownOption => Loc.T("unknown option");

            // Claude, 0.7.43-0.7.52. Said from 2 places.
            public static string NumberSign => Loc.T("number ");

            // Claude, 0.7.222-0.7.261.
            public static string StartRun => Loc.T("Start Run");

            // Claude, 0.7.42 or earlier.
            public static string LockedStarterDeck => Loc.T("Locked starter deck");

            // Zamar, 0.7.43-0.7.52.
            public static string StarterDeck(string title)
                => Loc.F($"Starter Deck: {title}.");

            // Claude, 0.7.42 or earlier.
            public static string CardOf(string positionFirst, int count)
                => Loc.F($"Card {positionFirst} of {count}");

            // Claude, 0.7.42 or earlier.
            public static string CardsAndOf(System.Collections.Generic.List<string> range, string lastPosition, int count)
                => Loc.T("Cards ") +
                   string.Join(", ", range) +
                   Loc.F($" and {lastPosition} of {count}");

            // Claude, 0.7.347-0.7.357.
            public static string WhereDescribed(string where, string described)
                => $" {where}, {described}";

            // Claude, 0.7.42 or earlier.
            public static string PreviousPage => Loc.T("Previous page");

            // Claude, 0.7.42 or earlier.
            public static string NextPage => Loc.T("Next page");

            // Claude, 0.7.42 or earlier.
            public static string LockedCard => Loc.T("Card locked");   // Zamar, Session 33: the game prints CARD LOCKED

            // Claude, Session 32. PROVISIONAL, from the game's own panel text
            // "CHALLENGE LOCKED" (the name shows as "???"). M6 audit.
            public static string ChallengeLocked => Loc.T("Challenge locked.");

            // Claude, 0.7.42 or earlier.
            public static string PlusChallengePointCount(int pointValue)
                => pointValue == 1
                   ? Loc.T("plus 1 challenge point")
                   : Loc.F($"plus {pointValue} challenge points");

            // Claude, 0.7.347-0.7.357.
            public static string ChallengeWithPoints(string name, string points)
                => $"{name}, {points}";

            // Zamar, Session 37 (after "Active").
            // Zamar, 0.7.417.
            public static string ChallengeComplete => Loc.T("Challenge complete.");

            // Zamar, Session 32 ("Challenge level [4]."); on arrival and Space, Session 37.
            // Zamar, 0.7.417.
            public static string ChallengeLevel(int level) => Loc.F($"Challenge level {level}.");

            // The game's own header text above level 12 (ChallengeLevelText),
            // in sentence case.
            // Game text, 0.7.425.
            public static string AllChallengeLevelsCleared => Loc.T("All challenge levels cleared.");

            // Claude, 0.7.222-0.7.261.
            public static string ActiveLabel(string label)
                => Loc.T("Active, ") + label;

            // Claude, 0.7.222-0.7.261.
            public static string RandomStarterDeck => Loc.T("Random Starter Deck");

            // Claude, 0.7.42 or earlier.
            public static string UnlabelledOrText(int length, string s)
                => length == 0 ? Loc.T("unlabelled option") : s;
        }

        /// <summary>The options screen.</summary>
        public static class Options
        {
            // Claude, 0.7.53-0.7.109.
            public static string DisplayIsNowOrDisplayResolutionChanged(bool modeChanged, bool full)
                => modeChanged
                   ? (full ? Loc.T("Display is now full screen") : Loc.T("Display is now windowed"))
                   : Loc.T("Display resolution changed");

            // Claude, 0.7.53-0.7.109.
            public static string ResolutionChanged(int w, int h)
                => Loc.F($", {w} by {h}");

            // Claude, 0.7.53-0.7.109. Key: incrementalfield_language.
            public static string TextLanguage => Loc.T("Text Language");

            // Zamar, Session 33 (built Session 34). The game's language button,
            // which the game prints in the NEW language; spoken in IKMA's.
            public static string ResetWith(string language)
                => Loc.F($"Reset with {language}");

            // Claude, 0.7.53-0.7.109. Said from 2 places.
            public static string FullScreenOrWindowed(bool fullScreen)
                => fullScreen ? Loc.T("Full screen") : Loc.T("Windowed");

            // Claude, 0.7.53-0.7.109.
            public static string Resolution(int width, int height)
                => Loc.F($"{width} by {height}");

            // Claude, 0.7.53-0.7.109.
            public static string OnOrOff(bool on)
                => on ? Loc.T("On") : Loc.T("Off");

            // Claude, 0.7.222-0.7.261.
            public static string GammaZero => Loc.T("Zero");

            // Claude, 0.7.222-0.7.261.
            public static string DefaultMark => Loc.T(", default");

            // Claude, 0.7.53-0.7.109.
            public static string UnnamedSetting => Loc.T("Unnamed setting.");

            // Claude, 0.7.53-0.7.109. Key: english.
            public static string English => Loc.T("English");

            // Claude, 0.7.53-0.7.109. Key: français.
            public static string French => Loc.T("French");

            // Claude, 0.7.53-0.7.109. Key: italiano.
            public static string Italian => Loc.T("Italian");

            // Claude, 0.7.53-0.7.109. Key: deutsch.
            public static string German => Loc.T("German");

            // Claude, 0.7.53-0.7.109. Key: español.
            public static string Spanish => Loc.T("Spanish");

            // Claude, 0.7.53-0.7.109. Key: português br.
            public static string BrazilianPortuguese => Loc.T("Brazilian Portuguese");

            // Claude, 0.7.53-0.7.109. Key: türkçe.
            public static string Turkish => Loc.T("Turkish");

            // Claude, 0.7.53-0.7.109. Key: русский.
            public static string Russian => Loc.T("Russian");

            // Claude, 0.7.53-0.7.109. Key: 日本語.
            public static string Japanese => Loc.T("Japanese");

            // Claude, 0.7.53-0.7.109. Key: 한국어.
            public static string Korean => Loc.T("Korean");

            // Claude, 0.7.53-0.7.109. Key: 简体中文.
            public static string SimplifiedChinese => Loc.T("Simplified Chinese");

            // Claude, 0.7.53-0.7.109. Key: 繁體中文.
            public static string TraditionalChinese => Loc.T("Traditional Chinese");

            // Claude, 0.7.53-0.7.109.
            public static string UnnamedSettingPressEnter => Loc.T("Unnamed setting. Press Enter to adjust.");

            // Zamar, 0.7.53-0.7.109.
            public static string PressEnterToAdjust(string sentence)
                => Loc.F($"{sentence} Press Enter to adjust.");

            // Claude, 0.7.53-0.7.109.
            public static string OptionsMenu(string pendingName)
                => pendingName ?? Loc.T("Options Menu");

            // Claude, 0.7.53-0.7.109.
            public static string PageOf(int page, int pageCount)
                => Loc.F($" Page {page} of {pageCount}.");

            // Claude, 0.7.222-0.7.261.
            public static string PressHForHelp(string name, string where)
                => Loc.F($"{name}.{where} Press H for help.");

            // Claude, 0.7.53-0.7.109.
            public static string ThatSettingCannotBe => Loc.T("That setting cannot be changed.");

            // Claude, 0.7.53-0.7.109.
            public static string SettingFallback(string label)
                => label ?? Loc.T("Setting");

            // Zamar, 0.7.53-0.7.109.
            public static string ArrowsToAdjustBackspace(bool noCurrent, string current)
                => (noCurrent ? "" : current + ". ") +
                   Loc.T("Arrows to adjust. Backspace to return.");

            // Claude, 0.7.53-0.7.109.
            public static string BackThenRowPrompt(string rowPrompt)
                => Loc.F($"Back. {rowPrompt}");
        }

        /// <summary>The pause menu.</summary>
        public static class Pause
        {
            // Claude, 0.7.53-0.7.109.
            public static string LockedMark(bool locked, string title)
                => locked ? title + Loc.T(", locked") : title;

            // Claude, 0.7.42 or earlier.
            public static string OptionCount(int count)
                => count == 1 ? Loc.T("1 option") : Loc.F($"{count} options");

            // Zamar, Session 17.
            public static string PauseMenu(string countWord, bool noInfo, string info)
                => Loc.F($"Pause menu. {countWord}.")
                   + (noInfo ? "" : $" {info}.");

            // Claude, 0.7.53-0.7.109.
            public static string GameSettings => Loc.T("Game settings.");

            // Claude, 0.7.53-0.7.109.
            public static string DisplaySettings => Loc.T("Display settings.");

            // Claude, 0.7.53-0.7.109.
            public static string AudioSettings => Loc.T("Audio settings.");

            // Claude, 0.7.53-0.7.109.
            public static string Page(int number)
                => Loc.F($"Page {number}.");

            // Claude, 0.7.244.
            public static string NoActiveChallenges => Loc.T("No active challenges.");

            // Zamar, 0.7.244.
            public static string ActiveChallenges(string[] names)
                => Loc.F($"Active Challenges. {string.Join(", ", names)}.");

            // Claude, 0.7.53-0.7.109.
            public static string UnnamedOption => Loc.T("Unnamed option.");

            // Claude, 0.7.53-0.7.109.
            public static string OptionOf(string label, int number, int count, bool noInfo, string info)
                => Loc.F($"{label}. Option {number} of {count}.")
                   + (noInfo ? "" : $" {info}.");

            // Claude, 0.7.53-0.7.109.
            public static string ThatOptionIsLocked => Loc.T("That option is locked.");

            // Zamar, 0.7.103.
            public static string OptionsAndSwitchPages()
                => Loc.T("Options. 1, 2, and 3 switch pages. ") +
                   Loc.T("Page 1 game settings, page 2 display settings, page 3 audio settings. ") +
                   Loc.T("Arrows browse the settings, Enter selects a setting to adjust, ") +
                   Loc.T("Backspace returns. ") + Vocabulary.BugReportKeyHelp();

            // Claude, 0.7.222-0.7.261.
            public static string TheyAreTheLast(string challengeRowLine)
                => " " + challengeRowLine + Loc.T(" They are the last stop past the options.");

            // Claude, 0.7.53-0.7.109.
            public static string PauseMenuArrowsBrowse(string challenges)
                => Loc.T("Pause menu. Arrows browse the options, Enter chooses, ") +
                   Loc.T("Space repeats your position and the run information, ") +
                   Loc.T("Backspace or Escape closes the menu.") + challenges +
                   " " + Vocabulary.BugReportKeyHelp();
        }

        /// <summary>The map, the cabin free-roam, and arriving at nodes.</summary>
        public static class Map
        {
            // Claude, 0.7.42 or earlier. Key: CardBattle.
            public static string Encounter => Loc.T("Encounter");

            // Claude, 0.7.42 or earlier. Key: TotemBattle.
            public static string TotemBattle => Loc.T("Totem battle");

            // Claude, 0.7.42 or earlier. Key: BossBattle.
            public static string BossBattle => Loc.T("Boss battle");

            // Claude, 0.7.42 or earlier. Key: CardChoices.
            public static string CardChoice => Loc.T("Card choice");

            // Claude, 0.7.42 or earlier. Key: CardBundleChoices.
            public static string CardBundleChoice => Loc.T("Card bundle choice");

            // Claude, 0.7.42 or earlier. Key: ChooseRareCard.
            public static string RareCardChoice => Loc.T("Rare card choice");

            // Claude, 0.7.42 or earlier. Key: CardRemove.
            public static string CardRemoval => Loc.T("Card removal");

            // Claude, 0.7.42 or earlier. Key: RecycleCard.
            public static string CardRecycling => Loc.T("Card recycling");

            // Claude, 0.7.42 or earlier. Key: CopyCard.
            public static string CardCopying => Loc.T("Card copying");

            // Claude, 0.7.42 or earlier. Key: BoulderChoice.
            public static string BoulderChoice => Loc.T("Boulder choice");

            // Claude, 0.7.42 or earlier. Key: ModifySideDeck.
            public static string SideDeckChange => Loc.T("Side deck change");

            // Claude, 0.7.42 or earlier. Key: DeckTrial.
            public static string DeckTrial => Loc.T("Deck trial");

            // Claude, 0.7.42 or earlier. Key: VictoryFeast.
            public static string VictoryFeast => Loc.T("Victory feast");

            // Claude, 0.7.222-0.7.261.
            public static string RandomChoice => Loc.T("Random");

            // Claude, 0.7.222-0.7.261.
            public static string CostChoice => Loc.T("Cost");

            // Claude, 0.7.53-0.7.109. Said from 2 places.
            public static string CannotRightNow(string what)
                => Loc.F($"Cannot {what} right now.");

            // Claude, 0.7.53-0.7.109.
            public static string BringYourDeckUp => Loc.T("bring your deck up");

            // Claude, 0.7.53-0.7.109.
            public static string YouAreAlreadyStanding => Loc.T("You are already standing.");

            // Claude, 0.7.53-0.7.109.
            public static string StandUp => Loc.T("stand up");

            // Claude, 0.7.53-0.7.109.
            public static string StandingUpFromThe => Loc.T("Standing up from the table. Press H for help.");

            // Claude, 0.7.53-0.7.109.
            public static string YouAreAt(string placeName)
                => Loc.F($"You are at {placeName}.");

            // Claude, 0.7.53-0.7.109.
            public static string ReturnTo(string placeName)
                => Loc.F($"return to {placeName}");

            // Claude, 0.7.53-0.7.109.
            public static string BackAt(string placeName)
                => Loc.F($"Back at {placeName}.");

            // Claude, 0.7.53-0.7.109.
            public static string YouAreAtThe => Loc.T("You are at the map.");

            // Claude, Session 17.
            public static string ReturnToTheMap => Loc.T("return to the map");

            // Zamar, Session 17.
            public static string GameMap => Loc.T("Game map.");

            // Zamar, 0.7.53-0.7.109.
            public static string ReturningToTheTable => Loc.T("Returning to the table.");

            // Claude, 0.7.53-0.7.109.
            public static string YouStepBackPress => Loc.T("You step back. Press Backspace again to return to the table.");

            // Claude, 0.7.53-0.7.109.
            public static string Counterclockwise => Loc.T("counterclockwise");

            // Claude, 0.7.53-0.7.109.
            public static string Clockwise => Loc.T("clockwise");

            // Claude, 0.7.53-0.7.109.
            public static string TurnedDegrees(string direction)
                => Loc.F($"Turned {direction} 90 degrees.");

            // Claude, 0.7.53-0.7.109. Said from 2 places.
            public static string ClockHandTurned(string hand)
                => hand + Loc.T(" turned.");

            // Claude, 0.7.53-0.7.109.
            public static string ToOclock(string hand, int face)
                => Loc.F($"{hand} to {face} o'clock.");

            // Claude, 0.7.53-0.7.109.
            public static string MechanismCracksAndThe => Loc.T("The mechanism cracks and the safe door swings open. " +
                "Inside is a large mass of bloodied mangled flesh.");

            // Claude, 0.7.53-0.7.109.
            public static string YouStepBack => Loc.T("You step back.");

            // Claude, 0.7.53-0.7.109.
            public static string YouStepBackFrom(string spoken)
                => Loc.F($"You step back from the {spoken}.");

            // Claude, 0.7.53-0.7.109.
            public static string YouFocusInOrYouFocusOn(string looseName)
                => looseName == null ? Vocabulary.Map.YouFocusIn : Loc.F($"You focus on the {looseName}.");

            // Claude, 0.7.53-0.7.109.
            public static string YouFocusIn => Loc.T("You focus in.");

            // Claude, 0.7.53-0.7.109.
            public static string YouFocusOnThe(string spoken)
                => Loc.F($"You focus on the {spoken}.");

            // Claude, 0.7.53-0.7.109.
            public static string RulebookIsNotWithin => Loc.T("The rulebook is not within reach.");

            // Zamar, 0.7.108.
            public static string InsideTheSafeIs => Loc.T("Inside the safe is a large mass of bloodied mangled flesh.");

            // Claude, 0.7.110-0.7.206.
            public static string Facing(string direction)
                => Loc.F($"Facing {direction}.");

            // Claude, 0.7.110-0.7.206.
            public static string NothingIsDirectlyIn(string facingLine)
                => facingLine == null
                   ? Vocabulary.NothingInFront
                   : Loc.F($"Nothing is directly in front of you. {facingLine}");

            // Claude, 0.7.53-0.7.109.
            public static string FocusControlsAndExamine()
                => Loc.T("Focus controls. ") +
                   Vocabulary.SpaceDescribesAhead_ +
                   Vocabulary.Map.ExamineKeys_ +
                   Loc.T("Backspace steps back from your focused object. ") +
                   Loc.T("Backspace again returns you to the game table and opens the map.");

            // Claude, 0.7.53-0.7.109. Part of Map.FocusControlsAndExamine, Map.StandingControlsUpArrow.
            public static string ExamineKeys_ => Loc.T("1, 2 and 3 examine what is in front of you. ");

            // Claude, 0.7.53-0.7.109.
            // Shift+R still works here. It is deliberately not named:
            // Zamar, Session 16, "Remove that Shift+R reminder, but keep it
            // silently true." It works everywhere in the mod, so it does not
            // need saying on every screen.
            //
            // The old closing line said IKMA could not describe the cabin
            // and that finding anything needed the mouse. It was true when
            // it was written and it is not any more — the room is described
            // now. A line that outlives its own truth is the same defect as
            // a key that does nothing.
            public static string StandingControlsUpArrow()
                => Loc.T("Standing controls. Up arrow steps forward, down arrow steps back. ") +
                   Loc.T("Left and right arrows step to the side. ") +
                   Loc.T("A turns you 90 degrees counterclockwise, D turns you 90 degrees clockwise. ") +
                   Vocabulary.SpaceDescribesAhead_ +
                   Vocabulary.Map.ExamineKeys_ +
                   Loc.T("Backspace returns you to the game table and opens the map.");

            // Game, 0.7.345. Leshy's own region narration. Key: Forest.
            public static string Woodlands => Loc.T("The Woodlands");

            // Game, 0.7.345. Leshy's own region narration. Key: Wetlands.
            public static string Wetlands => Loc.T("The Wetlands");

            // Game, 0.7.345. Leshy's own region narration. Key: Alpine.
            public static string SnowLine => Loc.T("The Snow Line");

            // Zamar, Session 26. Key: Pirateville.
            public static string Beach => Loc.T("The Beach");

            // Zamar, Session 26. Key: Midnight, Midnight_Ascension. Said from 2 places.
            public static string PathToTheCabin => Loc.T("the path to the Cabin");

            // Zamar, Session 26.
            public static string YouHaveArrivedAt(string region)
                => Loc.F($"You have arrived at {region}");

            // Claude, 0.7.336-0.7.345.
            public static string WhereSentenceYouAreAt(string placeName)
                => Loc.F($"You are at {placeName}");

            // Claude, 0.7.217-0.7.221.
            public static string PlaceName => Loc.T("the beginning of a new map");

            // Claude, 0.7.42 or earlier.
            public static string UnknownLocation => Loc.T("unknown location");

            // Zamar, 0.7.159.
            public static string NodeWithBoss(string friendly, string boss)
                => $"{friendly}: {boss}";

            // Claude, 0.7.42 or earlier.
            public static string MapReady => Loc.T("Map ready.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string PathCount(int count)
                => count == 1 ? Loc.T("1 path") : Loc.F($"{count} paths");

            // Claude, 0.7.336-0.7.345.
            public static string MapAheadArrowsTo(string whereSentence, string pathWord)
                => Loc.F($"Map. {whereSentence}. {pathWord} ahead. Arrows to browse, Space for details, Enter to travel, Shift+Up to display deck.");

            // Claude, 0.7.53-0.7.109.
            public static string MapAwaitingInputPress => Loc.T("Map. Awaiting input. Press Space for your position, H for help.");

            // Zamar, 0.7.122.
            public static string AwaitingPathSelectionH => Loc.T("Awaiting path selection. H, for help.");

            // Claude, 0.7.53-0.7.109.
            public static string First => Loc.T("First");

            // Claude, 0.7.53-0.7.109.
            public static string Second => Loc.T("Second");

            // Claude, 0.7.53-0.7.109.
            public static string Third => Loc.T("Third");

            // Claude, 0.7.53-0.7.109.
            public static string Fourth => Loc.T("Fourth");

            // Claude, 0.7.53-0.7.109.
            public static string Fifth => Loc.T("Fifth");

            // Claude, 0.7.53-0.7.109.
            public static string Sixth => Loc.T("Sixth");

            // Claude, 0.7.53-0.7.109.
            public static string Seventh => Loc.T("Seventh");

            // Claude, 0.7.53-0.7.109.
            public static string Eighth => Loc.T("Eighth");

            // Claude, 0.7.53-0.7.109.
            public static string PathNumber(int number)
                => Loc.F($"Path {number}");

            // Claude, 0.7.53-0.7.109.
            public static string PathThen(string ordinal, string[] route)
                => Loc.F($"{ordinal} path: {string.Join(Loc.T(", then "), route)}");

            // Claude, 0.7.53-0.7.109.
            public static string MorePathsNotRead(int count)
                => Loc.F($" {count} more paths not read.");

            // Claude, 0.7.42 or earlier.
            public static string MapUnavailable => Loc.T("Map unavailable.");

            // Claude, 0.7.336-0.7.345.
            public static string NoPathsAhead(string where)
                => Loc.F($"{where}. No paths ahead.");

            // Claude, 0.7.42 or earlier.
            public static string OptionNode(int number, string nodeFriendlyName)
                => Loc.F($"Option {number}: {nodeFriendlyName}");

            // Claude, 0.7.53-0.7.109.
            public static string YouAreOnOption(int choiceNumber, int count)
                => Loc.F($" You are on Option {choiceNumber} of {count}.");

            // Claude, 0.7.222-0.7.261.
            public static string LeftAndRightArrows => Loc.T("Left and right arrows to browse, ");

            // Claude, 0.7.336-0.7.345.
            public static string AheadEnterToTravel(string where, string pathWord, System.Collections.Generic.List<string> parts, string standingOn, string browse)
                => Loc.F($"{where}. {pathWord} ahead. {string.Join(". ", parts)}.{standingOn} ") + browse + Loc.T("Enter to travel, Shift+Up to display deck.");

            // Claude, 0.7.42 or earlier.
            public static string ArrivedAt(string name)
                => Loc.F($"Arrived at {name}.");
        }

        /// <summary>Cabin objects and directions (CabinMap).</summary>
        public static class Cabin
        {
            // Claude, 0.7.53-0.7.109. Key: safe.
            public static string Safe => Loc.T("Safe");

            // Claude, 0.7.53-0.7.109. Key: pastfigurines.
            public static string Woodcarving => Loc.T("woodcarving");

            // Zamar, Session 18. Key: largepictureframe, 3|south|1. Said from 2 places.
            public static string LargePictureFrame => Loc.T("Large Picture Frame");

            // Zamar, 0.7.110-0.7.206. Key: gramophoneinteractable, 7|east|1. Said from 2 places.
            public static string Gramophone => Loc.T("Gramophone");

            // Zamar, 0.7.110-0.7.206. Key: 6|north|2.
            public static string Skull => Loc.T("skull");

            // Space while zoomed on the skull. His words, Session 39.
            // Zamar, 0.7.423. Key: 6|north|2.
            public static string SkullRestsOnShelf => Loc.T("The human skull rests on the shelf.");

            // Claude, 0.7.110-0.7.206. Key: settlerman.
            public static string SettlerMan => Loc.T("Settler Man");

            // Claude, 0.7.110-0.7.206. Key: settlerwoman.
            public static string SettlerWoman => Loc.T("Settler Woman");

            // Claude, 0.7.110-0.7.206. Key: chief.
            public static string Chief => Loc.T("Chief");

            // Claude, 0.7.110-0.7.206. Key: wildling.
            public static string Wildling => Loc.T("Wildling");

            // Claude, 0.7.42 or earlier. Key: prospector.
            public static string Prospector => Loc.T("Prospector");

            // Claude, 0.7.110-0.7.206. Key: enchantress.
            public static string Enchantress => Loc.T("Enchantress");

            // Claude, 0.7.110-0.7.206. Key: gravedigger.
            public static string Gravedigger => Loc.T("Gravedigger");

            // Claude, 0.7.110-0.7.206. Key: robot.
            public static string Robot => Loc.T("Robot");

            // Zamar, Session 18. Key: head.
            public static string Head => Loc.T("head");

            // Zamar, Session 18. Key: arms.
            public static string Arms => Loc.T("arms");

            // Zamar, Session 18. Key: body.
            public static string Body => Loc.T("body");

            // Zamar, 0.7.53-0.7.109. Key: clockknobinteractable_hour.
            public static string HourHand => Loc.T("Hour hand");

            // Zamar, 0.7.53-0.7.109. Key: clockknobinteractable_minute.
            public static string MinuteHand => Loc.T("Minute hand");

            // Zamar, 0.7.53-0.7.109. Key: clockknobinteractable_second.
            public static string SecondHand => Loc.T("Second hand");
        }

        /// <summary>Keys during play: help, items, hand, slots, refusals (HotkeyManager).</summary>
        public static class Hotkeys
        {
            // Claude, 0.7.262-0.7.281.
            public static string NothingHereCanBe => Loc.T("Nothing here can be read yet. Escape opens the pause menu.");

            // Claude, 0.7.42 or earlier.
            public static string NoPathSelectedUse => Loc.T("No path selected. Use left and right arrows to browse paths.");

            // Claude, 0.7.222-0.7.261.
            public static string MapIsStillSettling => Loc.T("The map is still settling. Try again in a moment.");

            // Claude, 0.7.42 or earlier.
            public static string CardPlacementCannotBe => Loc.T("Card placement cannot be cancelled. Choose a slot.");

            // Zamar, 0.7.114.
            public static string YouAreInAn => Loc.T("You are in an encounter. Press H for help.");

            // Claude, 0.7.42 or earlier.
            public static string UnableToEndTurn => Loc.T("Unable to end turn: bell method not found.");

            // Claude, 0.7.42 or earlier.
            public static string DrawPilesUnavailable => Loc.T("Draw piles unavailable.");

            // Zamar, 0.7.349.
            public static string NoCardsRemainingIn => Loc.T("No cards remaining in your deck. Press S to draw from the Squirrel deck.");

            // Claude, 0.7.349. Mirrors his line for your own deck.
            public static string NoCardsRemainingInThe => Loc.T("No cards remaining in the Squirrel deck. Press D to draw from your deck.");

            // Claude, 0.7.42 or earlier.
            public static string DrawPileUnavailable => Loc.T("Draw pile unavailable.");

            // Claude, 0.7.42 or earlier.
            public static string Drew(string name)
                => Loc.F($"Drew {name}.");

            // Claude, 0.7.42 or earlier.
            public static string SigilAbilityCount(int count)
                => count == 1 ? Loc.T("Sigil Ability") : Loc.T("Sigil Abilities");

            // Claude, 0.7.42 or earlier.
            public static string DrewItGainsAnd(string name, string label, System.Collections.Generic.List<string> granted)
                => Loc.F($"Drew {name}. It gains {label}: {string.Join(Loc.T(" and "), granted)}.");

            // Claude, 0.7.42 or earlier.
            public static string FinishPlacingYourCard => Loc.T("Finish placing your card before using an item.");

            // Claude, 0.7.42 or earlier.
            public static string NoItems => Loc.T("No items.");

            // Claude, 0.7.42 or earlier.
            public static string ItemUnavailable => Loc.T("Item unavailable.");

            // Claude, 0.7.42 or earlier.
            public static string ItemEmpty(int number)
                => Loc.F($"Item {number}: empty.");

            // Claude, 0.7.42 or earlier.
            public static string ItemLine(int number, string itemNameAndDescription)
                => Loc.F($"Item {number}: {itemNameAndDescription}");

            // Claude, 0.7.347-0.7.357.
            public static string ItemNameAndDescription(string rulebookName, string description)
                => $"{rulebookName}.{description}";

            // Claude, 0.7.42 or earlier; "It is destroyed." Zamar, Session 35
            // (ScissorsItem calls Object.Destroy on the card - no death).
            // Key: Scissors.
            public static string CutUpOneOf => Loc.T("Cut up one of your adversary's cards. It is destroyed.");

            // Claude, 0.7.42 or earlier. Key: Pliers.
            public static string PlaceAWeightOn => Loc.T("Place a weight on the scales. The pain is temporary.");

            // Claude, 0.7.42 or earlier. Key: Fish Hook.
            public static string HookACardTo => Loc.T("Hook a card to take it as your own. Card must enter an empty space.");

            // Zamar, Session 32 (applied Session 34): Harpie's Birdleg Fan used.
            public static string YourCardsGainTemporaryAirborne => Loc.F($"Your cards gain temporary {Loc.Game("Airborne")}.");

            // Zamar, Session 32 (applied Session 34): said when the skip happens.
            public static string OpponentTurnSkippedHourglass => Loc.F($"Opponent turn skipped due to {Loc.Game("Hourglass")}.");

            // The game bleaches the field only, never the queue. Also the use line.
            // Zamar, Session 33. Key: Magickal Bleach.
            public static string OpponentCardsOnThe => Loc.T("Opponent cards in slots 1 through 4 will lose all of their sigils.");

            // Zamar, Session 27. Key: Skinning Knife.
            public static string SkinOneOfYour => Loc.T("Skin one of your opponent's cards. It is killed and a Pelt card is added to your hand.");

            // Zamar, 0.7.357. Key: Magpie's Glass.
            public static string SearchYourDeckFor => Loc.T("Search your deck for any card and add it into your hand.");

            // Zamar, 0.7.359. Key: Hoggy Bank.
            public static string UseToImmediatelyGain => Loc.T("Use to immediately gain 4 Bones.");

            // Zamar, 0.7.359. Key: Special Dagger.
            public static string PlaceASignificantWeight => Loc.T("Place a significant weight on the scales. The pain will linger...");

            // Claude, 0.7.42 or earlier.
            public static string ItemIsAlreadyBeing => Loc.T("An item is already being used.");

            // Claude, 0.7.42 or earlier.
            public static string ThereIsNoItem(int number)
                => Loc.F($"There is no item {number}.");

            // Claude, 0.7.42 or earlier.
            public static string ItemIsEmpty(int number)
                => Loc.F($"Item {number} is empty.");

            // Claude, 0.7.42 or earlier.
            public static string CouldNotBeUsed(string name)
                => Loc.F($"{name} could not be used. Item activation is not yet supported for this slot.");

            // Claude, 0.7.42 or earlier.
            public static string Using(string name)
                => Loc.F($"Using {name}.");

            // Claude, 0.7.42 or earlier.
            public static string IsStillWaitingFor(string name)
                => Loc.F($"{name} is still waiting for input.");

            // Claude, 0.7.42 or earlier.
            public static string WasNotUsed(string name)
                => Loc.F($"{name} was not used.");

            // Zamar, 0.7.45.
            public static string UsingTheLuckyClover => Loc.T("Using the Lucky Clover discards all of these cards for new ones. ");

            // Session 34: the card choice H line without the rulebook and deck
            // sentences, for the Mycologists (Zamar: the game offers neither).
            public static string LeftAndRightArrowsNoBookNoDeck(string helpLead, string cloverHelp, string shiftRHelp)
                => helpLead + " " +
                   Loc.T("Left and right arrows browse. Enter selects the card. ") +
                   Loc.T("Space repeats the current option. ") + cloverHelp + shiftRHelp;

            // Claude, 0.7.53-0.7.109.
            public static string LeftAndRightArrows(string helpLead, string cloverHelp, string shiftRHelp)
                => helpLead + " " +
                   Loc.T("Left and right arrows browse. Enter selects the card. ") +
                   Loc.T("Space repeats the current option. ") + cloverHelp +
                   Loc.T("R opens the rulebook. Shift plus Up arrow displays your deck. ") + shiftRHelp;

            // Claude, 0.7.42 or earlier.
            public static string NoBackOptionOn => Loc.T("No back option on this screen.");

            // Claude, 0.7.284-0.7.315.
            public static string MenuNavigationArrowKeys()
                => Loc.T("Menu navigation. Arrow keys browse the options. Enter chooses the one you are on. ") +
                   Loc.T("Backspace or Escape goes back. Space repeats the current option. ") +
                   Loc.T("Shift R looks up the last ability you heard. ") +
                   Vocabulary.BugReportKeyHelp();

            // Claude, 0.7.42 or earlier.
            public static string NoValidTargets => Loc.T("No valid targets.");

            // Claude, 0.7.42 or earlier.
            public static string ChooseATargetAvailable(int offered, string describeTargetSlot)
                => Loc.F($"Choose a target. {offered} available. {describeTargetSlot} Arrows to browse, Enter to select.");

            // Claude, 0.7.42 or earlier.
            public static string CannotCancelItemUse => Loc.T("Cannot cancel item use, please choose a target.");

            // Claude, 0.7.42 or earlier.
            public static string ChoosingATargetOrChoosingATarget(bool noActiveItemName, string activeItemName)
                => noActiveItemName
                   ? Loc.T("Choosing a target.")
                   : Loc.F($"Choosing a target for {Loc.Game(activeItemName)}.");

            // Claude, 0.7.42 or earlier.
            public static string ArrowKeysMoveBetween(string what)
                => Loc.F($"{what} Arrow keys move between the cards this item can affect. Enter selects. An item cannot be cancelled once used.");

            // Claude, 0.7.42 or earlier.
            public static string ThatTargetCouldNot => Loc.T("That target could not be selected.");

            // Zamar, Session 14.
            public static string IsDraggedDownTo(string name, int number)
                => Loc.F($"{name} is dragged down to your side into slot {number}.");

            // Claude, 0.7.42 or earlier.
            public static string UnknownSlot => Loc.T("unknown slot.");

            // Claude, 0.7.42 or earlier.
            public static string YourSlotOrOpponentSlot(bool isPlayerSlot)
                => isPlayerSlot ? Loc.T("Your slot") : Loc.T("Opponent slot");

            // Claude, 0.7.42 or earlier.
            public static string TargetSlotEmpty(string side, int number)
                => Loc.F($"{side} {number}: empty.");

            // Claude, 0.7.347-0.7.357.
            public static string TargetSlotCard(string side, int number, string cardName, int attack, int health, string targetAbilities)
                => $"{side} {number}: {cardName}, " +
                   $"{attack}, {health}{targetAbilities}.";

            // Claude, 0.7.110-0.7.206.
            public static string ThereIsNoIkma(string shiftRHelp)
                => Loc.T("There is no IKMA reader for this screen yet, so the usual controls do not apply here. ") +
                   Loc.T("Space describes what is in front of you. Space repeats where you are. ") + shiftRHelp + " " +
                   Loc.T("This screen still needs the mouse until a reader is built for it.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string IkmaCannotReadThis(string lead)
                => Loc.F($"{lead}IKMA cannot read this screen yet. The game is waiting for you here. ") +
                   Vocabulary.PressHForWhatStillWorks;

            // Zamar, Session 16. Reordered by him, 0.7.339.
            // Zamar's ordering and wording, Session 16, verbatim.
            // The shape of it is the point: what you do most often
            // first, then what you look at, then the keys that leave
            // this screen.
            public static string MapControlsArrowsBrowse => Loc.T("Map controls. Arrows, browse the choices ahead. Enter, to travel. " +
                "Space, lists all current choices. B, read the paths ahead. " +
                "Shift plus Up arrow to browse your deck. " +
                "I, hear your current items. R, open the rulebook. " +
                "Shift plus Down arrow, to stand up from the table. H, for help.");

            // Zamar, Session 18.
            // HIS REWRITE, VERBATIM. (Session 18.) Reordered so the reads group
            // together and E sits with them, "read" spelled out on each
            // one, and Shift+R named here for the first time — it was
            // previously silent everywhere by his Session 16 call.
            // Session 34: reordered to Zamar's order (he wrote the pad version:
            // play keys first, then the reads, then items, then the draw
            // phase), and "At the start of your turn only" is his "During your
            // Draw Phase". The pad hears it through PadWords.
            public static string EncounterControlsLeftAnd => Loc.T("Encounter controls. Left and right arrows browse hand. Tab, next playable card in your hand. Enter, play card. E, ring bell to end turn. R, open the rulebook. Shift R to look up recently heard ability. C, read your hand. U, read enemy queue. G, read enemy board. Shift G, read your board. B, read full board and queue. A, read scales, bones, and other game info. I, press repeatedly to cycle through your items, Enter to use. During your Draw Phase: D, draw from your deck, or S, draw from the Squirrel deck. H, for help.");

            // Claude, 0.7.110-0.7.206.
            public static string PlacingACardLeft => Loc.T("Placing a card. Left and right arrows navigate slots. Enter, confirm. Board reading keys still work. H, for help.");

            // Claude, 0.7.110-0.7.206.
            public static string ChoosingSacrificesLeftAnd => Loc.T("Choosing sacrifices. Left and right arrows navigate your board. Enter, sacrifice the card you are on. A sacrifice cannot be cancelled once started. Board reading keys still work. H, for help.");

            // Claude, 0.7.42 or earlier.
            public static string CannotTravelThere => Loc.T("Cannot travel there.");

            // Claude, 0.7.42 or earlier.
            public static string CancelledReturnedToOrCancelledCardReturned(string cardName)
                => cardName != null
                   ? Loc.F($"Cancelled. {cardName} returned to hand.")
                   : Loc.T("Cancelled. Card returned to hand.");

            // Claude, 0.7.42 or earlier.
            public static string SacrificeCannotBeOrCannotCancelRight(bool wasSacrifice)
                => wasSacrifice
                   ? Loc.T("A sacrifice cannot be cancelled once started.")
                   : Loc.T("Cannot cancel right now.");

            // Claude, 0.7.42 or earlier. Said by the arrows too from Session 29,
            // his pick of the near-duplicate pair ("No cards left to sacrifice."
            // is gone).
            public static string NoCardsToSacrifice => Loc.T("No cards to sacrifice.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string NoSlot(int number)
                => Loc.F($"No slot {number}.");

            // Claude, 0.7.42 or earlier.
            public static string OnlyInHand(string cardWord)
                => Loc.F($"Only {cardWord} in hand.");

            // Zamar, Session 18.
            public static string NoCardsInHand => Loc.T("No cards in hand are currently playable.");

            // Claude, 0.7.42 or earlier.
            public static string CardOrCardWord(string cardName)
                => cardName ?? Loc.T("Card");

            // Claude, 0.7.42 or earlier.
            public static string GainsFromYourTotem(string name, System.Collections.Generic.List<string> gained)
                => Loc.F($"{name} gains {string.Join(Loc.T(" and "), gained)} from your totem.");

            // Claude, 0.7.336-0.7.345.
            public static string RecentItemCount(int count)
                => count == 1 ? Loc.T("Recent item:") : Loc.T("Recent items:");

            // Claude, 0.7.42 or earlier.
            public static string NoRecentAbilityTo => Loc.T("No recent ability to look up.");

            // Claude, 0.7.42 or earlier.
            public static string NoDescriptionAvailableFor => Loc.T("No description available for that ability.");

            // Claude, 0.7.42 or earlier.
            public static string RecentAbilityCount(int count)
                => count == 1 ? Loc.T("Recent ability:") : Loc.T("Recent abilities:");

            // Claude, 0.7.42 or earlier.
            public static string CannotPlayACard => Loc.T("Cannot play a card right now.");

            // Claude, 0.7.42 or earlier.
            public static string NoCardSelectedUse => Loc.T("No card selected. Use left and right arrows to browse your hand first.");

            // Claude, 0.7.42 or earlier.
            public static string ThatCard(string cardName)
                => cardName ?? Loc.T("That card");

            // Claude, 0.7.42 or earlier.
            public static string ThatCardCouldNot => Loc.T("That card could not be played.");

            // Claude, 0.7.42 or earlier.
            public static string WasNotPlayed(string cardName)
                => Loc.F($"{cardName} was not played.");

            // Claude, 0.7.42 or earlier.
            public static string NoCardInThis => Loc.T("No card in this slot.");

            // Claude, 0.7.42 or earlier.
            public static string SlotIsOccupiedBy(int slotNumber, string cardName)
                => Loc.F($"Slot {slotNumber} is occupied by {cardName}. Choose an empty slot.");

            // Claude, 0.7.42 or earlier.
            public static string CardOrLowercaseCard(string cardName)
                => cardName ?? Loc.T("card");

            // Note D4 - one line per sacrifice press while more are needed:
            // "Mole in slot [1] sacrificed. Choose [x] additional sacrifice[s]."
            // Zamar, 0.7.414.
            public static string SacrificedChooseMore(string cardName, int slotNumber, int more)
                => more == 1
                   ? Loc.F($"{cardName} in slot {slotNumber} sacrificed. Choose {more} additional sacrifice.")
                   : Loc.F($"{cardName} in slot {slotNumber} sacrificed. Choose {more} additional sacrifices.");

            // Claude, 0.7.42 or earlier.
            public static string PlayedInSlot(string cardName, int slotNumber)
                => Loc.F($"{cardName} played in Slot {slotNumber}.");

            // Claude, 0.7.42 or earlier.
            public static string NoSlotRestrictionsTo => Loc.T("No slot restrictions to step through.");

            // Claude, 0.7.42 or earlier.
            public static string NoOtherSlotAvailable => Loc.T("No other slot available for this card.");

            // Claude, 0.7.42 or earlier.
            public static string Ability(string abilityNameFirst)
                => Loc.F($" Ability: {Loc.Game(abilityNameFirst)}.");

            // Claude, 0.7.42 or earlier.
            public static string AbilitiesAnd(System.Collections.Generic.List<string> abilityNames)
                => Loc.F($" Abilities: {string.Join(Loc.T(" and "), abilityNames)}.");

            // Claude, 0.7.336-0.7.345.
            public static string SlotWithCard(int slotNum, string cardName, int attack, int health, string sigilPart, string cannonHere)
                => Loc.F($"Slot {slotNum}: {cardName}, {attack}/{health}.{sigilPart}{cannonHere}");

            // Claude, 0.7.42 or earlier.
            public static string SlotEmpty(int slotNum, string cannonHere)
                => Loc.F($"Slot {slotNum}: empty.{cannonHere}");

            // Claude, 0.7.42 or earlier.
            public static string ChooseASlotTo(string placing)
                => Loc.F($" Choose a slot to play {placing}.");

            // Claude, 0.7.336-0.7.345.
            public static string SlotOccupiedBy(int slotNum, string cardName, string cannonHere, string reminder)
                => Loc.F($"Slot {slotNum}: occupied by {cardName}.{cannonHere}{reminder}");

            // Claude, 0.7.336-0.7.345.
            public static string SlotEmptyName(int slotNum, string cannonHere, string reminder)
                => Loc.F($"Slot {slotNum}: empty.{cannonHere}{reminder}");

            // An empty slot next to a card with Leader. His words, Session 39:
            // "Leader Enhanced Slot 3: empty. Cards played in this slot will
            // gain [+1 power] from [card] in adjacent slot [x]. Choose a slot
            // to play Squirrel." The sigil's name is the game's.
            // Zamar, 0.7.425.
            public static string SlotEmptyLeader(string leader, int slotNum, string cannonHere, string gain, string reminder)
                => Loc.F($"{Loc.Game(leader)} Enhanced Slot {slotNum}: empty.{cannonHere} Cards played in this slot will gain {gain}.{reminder}");

            // Zamar, 0.7.425. Part of Hotkeys.SlotEmptyLeader.
            public static string LeaderGain(int power, string sources)
                => Loc.F($"+{power} power from {sources}");

            // Zamar, 0.7.425. Part of Hotkeys.SlotEmptyLeader.
            public static string LeaderSource(string cardName, int slotNum)
                => Loc.F($"{cardName} in adjacent slot {slotNum}");
        }

        /// <summary>Playing a card: sacrifice and slot prompts.</summary>
        public static class PlayFlow
        {
            // Claude, 0.7.42 or earlier.
            public static string SacrificeCount(int cost)
                => cost == 1 ? Loc.T("sacrifice") : Loc.T("sacrifices");

            // Claude, 0.7.42 or earlier.
            public static string CostsBloodChooseLeft(string cardName, int cost, string sacrificeWord)
                => Loc.F($"{cardName} costs {cost} blood. Choose {cost} {sacrificeWord}. Left and right arrows to navigate, Enter to confirm. A sacrifice cannot be cancelled once started.");

            // Claude, 0.7.42 or earlier.
            public static string Playing(string cardName)
                => Loc.F($"Playing {cardName}. ");

            // Claude, 0.7.42 or earlier.
            public static string ChooseASlotOrChooseASlot(string cardName, string lead, string cancelHint)
                => cardName != null
                   ? Loc.F($"{lead}Choose a slot to play {cardName}. Left and right arrows to navigate, Enter to confirm.{cancelHint}")
                   : Loc.F($"Choose a slot. Left and right arrows to navigate, Enter to confirm.{cancelHint}");
        }

        /// <summary>Board, hand, queue and resource reads.</summary>
        public static class Board
        {
            // Claude, 0.7.347-0.7.357.
            public static string HandCardWithCost(string cardName, string costPart)
                => $"{cardName}, {costPart}";

            // Claude, 0.7.42 or earlier.
            public static string Hand(string countWord, System.Collections.Generic.List<string> parts)
                => Loc.F($"Hand: {countWord}. {string.Join(". ", parts)}.");

            // Claude, 0.7.42 or earlier. Said from 4 places.
            public static string YourBoard => Loc.T("Your board");

            // Claude, 0.7.42 or earlier. Said from 3 places.
            public static string EnemyBoard => Loc.T("Enemy board");

            // Claude, 0.7.347-0.7.357.
            public static string BoardSummary(string surrender, string mask, string player, string opponent, string queue)
                => $"{surrender}{mask}{player}. {opponent}. {queue}";

            // Claude, 0.7.42 or earlier.
            public static string ResourcesUnavailable => Loc.T("Resources unavailable.");

            // Claude, 0.7.217-0.7.221.
            public static string CandleLitCount(int mine)
                => mine == 1
                   ? Loc.T(" 1 candle lit.")
                   : Loc.F($" {mine} candles lit.");

            // Claude, 0.7.217-0.7.221.
            public static string EnemyHasCandleCount(int theirs)
                => theirs == 1
                   ? Loc.T(" Enemy has 1 candle.")
                   : Loc.F($" Enemy has {theirs} candles.");

            // Claude, 0.7.347-0.7.357.
            public static string Name(string surrender, string scaleText, string boneText, string candleText, string totemText, string deckText)
                => $"{surrender}{scaleText} {boneText}.{candleText}{totemText}{deckText}";

            // Zamar, 0.7.110-0.7.206.
            public static string WoodcarvingKinTypeSigil(string kin, string sigil)
                => Loc.F($" Woodcarving, Kin type: {Loc.Game(kin)}, Sigil Ability: {Loc.Game(sigil)}.");

            // Claude, 0.7.42 or earlier.
            public static string YourDeckRemainingCard(int count)
                => Loc.F($" Your deck: {(count == 1 ? Loc.T("1 card") : count + Loc.T(" cards"))} remaining.");

            // Claude, 0.7.42 or earlier.
            public static string SquirrelDeckRemainingCard(int count)
                => Loc.F($" Squirrel deck: {(count == 1 ? Loc.T("1 card") : count + Loc.T(" cards"))} remaining.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string ItemEmpty => Loc.T("empty");

            // Claude, 0.7.262-0.7.281.
            public static string ItemAtCursor(int itemCursorNumber, string what)
                => Loc.F($"Item {itemCursorNumber}: {what}.");

            // Claude, 0.7.42 or earlier.
            public static string ItemEmptyName(int number, string text)
                => Loc.F($"Item {number}: {text}");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string OpponentUnavailable => Loc.T("Opponent unavailable.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string NoCardsQueued => Loc.T("No cards queued.");

            // Zamar, 0.7.42.
            public static string BlockedForSlot(int slotNumber)
                => Loc.F($"Blocked for Slot {slotNumber}: ");

            // Zamar, 0.7.42.
            public static string EnteringSlot(int slotNumber)
                => Loc.F($"Entering Slot {slotNumber}: ");

            // Claude, 0.7.347-0.7.357.
            public static string Word(string slotLabel, string cardName, string statPart, string abilityPart)
                => $"{slotLabel}{cardName}, {statPart}{abilityPart}";

            // Claude, 0.7.43-0.7.52.
            public static string UpcomingQueue(string countWord, System.Collections.Generic.List<string> parts)
                => Loc.F($"Upcoming queue: {countWord}. {string.Join(". ", parts)}.");

            // Session 37 (note C12): the same "Hand: N cards." shape C uses -
            // Say the Spire 2 names a pile one way ("Hand" + count).
            // Claude, 0.7.413.
            public static string GetHandNamesText(string countWord, System.Collections.Generic.List<string> names)
                => Loc.F($"Hand: {countWord}. {string.Join(", ", names)}.");

            // Claude, 0.7.42 or earlier.
            public static string SideEmpty(string label)
                => Loc.F($"{label}: empty");

            // Claude, 0.7.211-0.7.212.
            public static string SlotsList(string joinSlotNumbers)
                => Loc.T("Slots ") + joinSlotNumbers;

            // Claude, 0.7.42 or earlier.
            public static string SlotNumber(int number)
                => Loc.F($"Slot {number}");

            // Claude, 0.7.347-0.7.357.
            public static string SlotsOneTo(int count)
                => Loc.F($"Slots 1 to {count}");

            // Claude, 0.7.222-0.7.261.
            public static string CurrentlyStats(int attack, int health)
                => Loc.F($"currently {attack}/{health}.");

            // Claude, 0.7.347-0.7.357.
            public static string FormatSlots(string where, DiskCardGame.PlayableCard card, string cardName, string stats, string statNote, string abilityPart)
                => $"{where}: {Vocabulary.Submerged(card, cardName)}, " +
                   $"{stats}{statNote}{abilityPart}";

            // Claude, 0.7.336-0.7.345.
            public static string SideEmptyCannon(string label, string cannon)
                => Loc.F($"{label}: empty{cannon}");

            // Claude, 0.7.347-0.7.357.
            public static string SideCards(string label, System.Collections.Generic.List<string> parts, string cannon)
                => $"{label}: {string.Join(". ", parts)}{cannon}";

            // Claude, 0.7.211-0.7.212.
            public static string NumberRange(int numberFirst, int lastNumber)
                => Loc.F($"{numberFirst} to {lastNumber}");

            // Claude, 0.7.42 or earlier.
            public static string NumberList(System.Collections.Generic.List<string> head, int lastNumber)
                => Vocabulary.AndList(head, lastNumber.ToString());

            // Claude, 0.7.42 or earlier.
            public static string Ability(string nameFirst)
                => Loc.F($". Ability: {nameFirst}");

            // Claude, 0.7.42 or earlier.
            public static string AbilitiesAnd(System.Collections.Generic.List<string> names)
                => Loc.F($". Abilities: {string.Join(Loc.T(" and "), names)}");

            // Claude, 0.7.42 or earlier.
            public static string CostsBloodCount(int bloodCost)
                => bloodCost == 1 ? Loc.T("costs 1 blood") : Loc.F($"costs {bloodCost} blood");

            // Claude, 0.7.42 or earlier.
            public static string CostsBoneCount(int bonesCost)
                => bonesCost == 1 ? Loc.T("costs 1 bone") : Loc.F($"costs {bonesCost} bones");

            // Claude, 0.7.42 or earlier.
            public static string GetScaleText => Loc.T("Scales unavailable.");
        }

        /// <summary>Cards moving, arriving and leaving (BoardWatcher).</summary>
        public static class BoardChanges
        {
            // Claude, 0.7.42 or earlier.
            public static string MovesToSlot(string name, string sideWord, int slotNumber)
                => Loc.F($"{name} moves to {sideWord} slot {slotNumber}.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string RightOrLeft(int slotIndex, int slotIndex2)
                => slotIndex > slotIndex2 ? Loc.T("right, ") : Loc.T("left, ");

            // Claude, 0.7.42 or earlier.
            public static string MovesFromSlotTo(string possessive, string name, string dir, int slotNumber, int slotNumber2)
                => Loc.F($"{possessive} {name} moves {dir}from slot {slotNumber} to slot {slotNumber2}.");

            // Claude, 0.7.110-0.7.206.
            public static string MovesFromSlotToSlot(string possessive, string name, string movedDir, int notedNumber, int slotNumber)
                => Loc.F($"{possessive} {name} moves {movedDir}") +
                   Loc.F($"from slot {notedNumber} to slot {slotNumber}.");

            // Claude, 0.7.110-0.7.206.
            public static string MovesDownToSlot(string possessive, string name, int slotNumber, string pack)
                => Loc.F($"{possessive} {name} moves down to slot {slotNumber}{pack}.");

            // Claude, 0.7.262-0.7.281.
            public static string IsPlayedInSlot(string possessive, string name, int slotNumber, string pack)
                => Loc.F($"{possessive} {name} is played in slot {slotNumber}{pack}.");

            // Claude, 0.7.110-0.7.206.
            public static string TargetingSlot(int slotNumber)
                => Loc.F($" targeting slot {slotNumber}");

            // Claude, 0.7.110-0.7.206.
            public static string EnemyIsQueued(string qName, string where)
                => Loc.F($"Enemy {qName} is queued{where}.");

            // Claude, 0.7.42 or earlier.
            public static string HasLeftSlot(string possessive, string name, int slotNumber)
                => Loc.F($"{possessive} {name} has left slot {slotNumber}.");

            // Claude, 0.7.336-0.7.345.
            public static string ProspectorClearsHisQueue => Loc.T("The Prospector clears his queue.");

            // Claude, 0.7.110-0.7.206.
            public static string ItIsCarryingA => Loc.T(", it is carrying a pack of cards");

            // Claude, 0.7.42 or earlier.
            public static string Possessive(bool playerSide)
                => playerSide ? Loc.T("Your") : Loc.T("Enemy");

            // Claude, 0.7.42 or earlier.
            public static string SideWord(bool playerSide)
                => playerSide ? Loc.T("your") : Loc.T("enemy");
        }

        /// <summary>Reading a card.</summary>
        public static class Cards
        {
            // Claude, 0.7.316-0.7.325.
            public static string Fused(string name)
                => Loc.F($"Fused {name}");

            // Zamar, 0.7.329.
            public static string GlitchedCardThisCard => Loc.T("Glitched card. This card is glowing white and glitching. Card stats are unknown until played.");

            // Claude, 0.7.53-0.7.109.
            public static string ShiftPlusRLooks => Loc.T("Shift plus R looks up the last ability you heard.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string AbilityCount(int count)
                => count == 1 ? Loc.T("Ability") : Loc.T("Abilities");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string AbilitiesLine(string label, System.Collections.Generic.List<string> baseSigils)
                => $" {label}: {string.Join(Loc.T(" and "), baseSigils)}.";

            // Claude, 0.7.42 or earlier.
            public static string SigilAbility(string t)
                => Loc.F($" Sigil Ability: {t}.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string KinType(string tribeNameFirst)
                => Loc.F($" Kin type: {tribeNameFirst}.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string KinTypes(System.Collections.Generic.List<string> tribeNames)
                => Loc.F($" Kin types: {string.Join(", ", tribeNames)}.");

            // Claude, 0.7.347-0.7.357.
            public static string CardWithCost(string name, string lookPart, string costPart, int atk, int hp, string statNote, string abilityPart, string tribePart)
                => $"{name}.{lookPart}{costPart} {atk}, {hp}.{statNote}{abilityPart}{tribePart}";

            // Claude, 0.7.347-0.7.357.
            public static string CardWithoutCost(string name, string lookPart, int atk, int hp, string statNote, string abilityPart)
                => $"{name}.{lookPart} {atk}, {hp}.{statNote}{abilityPart}";

            // Claude, 0.7.42 or earlier.
            public static string StarAttack => Loc.T("Star");

            // Claude, 0.7.347-0.7.357.
            public static string CardInfoLine(string name, string namePart, string lookPart, string costPart, string attackText, int health, string statNote, string abilityPart, string tribePart)
                => $"{name}.{namePart}{lookPart}{costPart} {attackText}, {health}.{statNote}{abilityPart}{tribePart}";

            // Claude, 0.7.42 or earlier.
            public static string MovingLeftOrMovingRight(bool movingLeft)
                => movingLeft ? Loc.T(" Moving left.") : Loc.T(" Moving right.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string Rare => Loc.T("Rare");

            // Zamar, Session 14.
            public static string TerrainBorder => Loc.T("Terrain border");

            // Zamar, Session 14.
            public static string GlitchedPortrait => Loc.T("Glitched portrait");

            // Claude, 0.7.42 or earlier.
            public static string FullCardPortrait => Loc.T("Full-card portrait");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string AnimatedPortrait => Loc.T("Animated portrait");

            // Claude, 0.7.42 or earlier.
            public static string HologramPortrait => Loc.T("Hologram portrait");

            // Claude, 0.7.42 or earlier.
            public static string GoldenGlow => Loc.T("Golden glow");

            // Claude, 0.7.42 or earlier.
            public static string RedGlow => Loc.T("Red glow");

            // Zamar, Session 14.
            public static string BloodiedPortrait => Loc.T("Bloodied portrait");

            // Claude, 0.7.42 or earlier.
            public static string MoonParticles => Loc.T("Moon particles");

            // Claude, 0.7.42 or earlier.
            public static string AttackEqualsTheNumber => Loc.T(" Attack equals the number of ants on its owner's side.");

            // Claude, 0.7.42 or earlier.
            public static string AttackIsEqualTo => Loc.T(" Attack is equal to half the Bones of the owner.");

            // Claude, 0.7.42 or earlier.
            public static string AttackEqualsSacrificesMade => Loc.T(" Attack equals sacrifices made this turn.");

            // Claude, 0.7.42 or earlier.
            public static string AttackVariesWithGreen => Loc.T(" Attack varies with green gems.");

            // Claude, 0.7.42 or earlier.
            public static string BloodCount(int bloodCost)
                => bloodCost == 1 ? Loc.T("1 blood") : Loc.F($"{bloodCost} blood");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string CostLine(string s)
                => Loc.F($"Cost: {s}.");

            // Claude, 0.7.42 or earlier.
            public static string CostFree => Loc.T("Cost: Free.");
        }

        /// <summary>Attacks, damage, deaths, sacrifices and bones.</summary>
        public static class Combat
        {
            // Claude, 0.7.42 or earlier.
            public static string SlotNumber(int number)
                => Loc.F($"Slot {number}");

            // Claude, 0.7.42 or earlier.
            public static string ASlot => Loc.T("a slot");

            // Claude, 0.7.42 or earlier.
            public static string CardInSlot(string cardName, string slotWord)
                => Loc.F($"{cardName} in {slotWord}");

            // Claude, 0.7.42 or earlier.
            public static string EmptySlot(string slotWord)
                => Loc.F($"empty {slotWord}");

            // Claude, 0.7.42 or earlier.
            public static string TargetListThree(System.Collections.Generic.List<string> range, string lastPart)
                => string.Join(", ", range) + Loc.T(", and ") + lastPart;

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string TargetListTwo(string first, string second)
                => Loc.F($"{first} and {second}");

            // Claude, 0.7.42 or earlier.
            public static string AttacksTargets(DiskCardGame.PlayableCard card, string cardName, string targetList)
                => Loc.F($"{Vocabulary.Submerged(card, cardName)} attacks {targetList}.");

            // Claude, 0.7.42 or earlier. "twice" is Zamar's, 0.7.359: "A card
            // with double strike should read all it's damage on one line when
            // possible. 'Cuckoo flies over Broken Egg and attacks directly
            // twice.'" Said once for both strikes when both go past the card in
            // front.
            public static string FliesOverAndAttacks(string attackerName, string targetName, bool twice = false)
                => Loc.F($"{attackerName} flies over {targetName} and attacks directly{(twice ? Loc.T(" twice") : "")}.");

            // Zamar, 0.7.262-0.7.281. "twice" as above, 0.7.359.
            public static string PassesByAndAttacks(string attackerName, string targetName, bool twice = false)
                => Loc.F($"{attackerName} passes by {targetName} and attacks directly{(twice ? Loc.T(" twice") : "")}.");

            // Game, 0.7.152. Fallback copy of the game's own sigil name.
            public static string Airborne(string abilityName)
                => abilityName ?? Loc.T("Airborne");

            // Game, 0.7.110-0.7.206. Fallback copy of the game's own sigil name.
            public static string MightyLeap(string abilityName)
                => abilityName ?? Loc.T("Mighty Leap");

            // Zamar, 0.7.152.
            public static string BlocksDueTo(string targetName, string air, string attackerName, string leap)
                => Loc.F($"{targetName} blocks {air} {attackerName} due to {leap}.");

            // Claude, 0.7.110-0.7.206.
            public static string Attacks(string attackerName, string deadlyNote, string targetName)
                => Loc.F($"{attackerName}{deadlyNote} attacks {targetName}.");

            // Claude, 0.7.110-0.7.206.
            public static string AttacksEmptySlot(string attackerName, string swingNote, int number)
                => Loc.F($"{attackerName}{swingNote} attacks empty Slot {number}.");

            // Claude, 0.7.110-0.7.206.
            public static string AttacksDirectly(string attackerName, string swingNote)
                => Loc.F($"{attackerName}{swingNote} attacks directly.");

            // Claude, 0.7.42 or earlier.
            public static string InSlotNumber(int number)
                => Loc.F($" in Slot {number}");

            // Zamar, Session 14.
            public static string FromYourHand => Loc.T(" from your hand");

            // Zamar, Session 14.
            public static string PlayedLine(string cause, string name, string from, string where)
                => cause != null
                   ? Loc.F($"{name} is played{from}{where} by Ability: {cause}.")
                   : Loc.F($"{name} is played{from}{where}.");

            // Claude, 0.7.110-0.7.206.
            public static string DeadlyName(bool noN, string n)
                => noN ? Loc.T("Touch of Death") : n;

            // Claude, 0.7.110-0.7.206.
            public static string WithOne(string nameFirst)
                => Loc.F($" with {nameFirst}");

            // Claude, 0.7.110-0.7.206.
            public static string WithMany(string[] names, string last)
                => Loc.F($" with {Vocabulary.AndList(names, last)}");

            // Claude, 0.7.110-0.7.206.
            public static string ToDeadly(string deadlyName)
                => Loc.F($" to {deadlyName}");

            // Claude, 0.7.110-0.7.206.
            public static string QueuedInSlot(string name, int queuedSlotNumber)
                => Loc.F($"The {name} queued in slot {queuedSlotNumber}");

            // Claude, 0.7.110-0.7.206.
            public static string Queued(string name)
                => Loc.F($"The {name} queued");

            // Claude, 0.7.217-0.7.221.
            public static string TakesAnd(string who, string dmg, DiskCardGame.CardInfo info, string cause)
                => Loc.F($"{who} takes {dmg} and {Vocabulary.DeathClause(info)}{cause}.");

            // Claude, 0.7.42 or earlier.
            public static string TakesDamage(string who, string dmg)
                => Loc.F($"{who} takes {dmg}.");

            // Claude, 0.7.316-0.7.325.
            public static string TakesItsAbilityResolves(string who, string dmg, string left, string holding)
                => Loc.F($"{who} takes {dmg}, {left}. ") +
                   Loc.F($"Its {holding} ability resolves before it dies.");

            // Claude, 0.7.42 or earlier.
            public static string TakesDamageLeft(string who, string dmg, string left)
                => Loc.F($"{who} takes {dmg}, {left}.");

            // Claude, 0.7.110-0.7.206.
            public static string SideQualifiedName(bool mine, string name)
                => mine ? Loc.F($"Your {name}") : Loc.F($"Enemy {name}");

            // Claude, 0.7.110-0.7.206.
            public static string IsSacrificed(string subject, string bonePart)
                => Loc.F($"{subject} is sacrificed.{bonePart}");

            // Claude, 0.7.110-0.7.206.
            public static string Sacrificed(string joinSubjects, string bonePart)
                => Loc.F($"{joinSubjects} sacrificed.{bonePart}");

            // Claude, 0.7.42 or earlier.
            public static string SubjectList(System.Collections.Generic.List<string> head, string lastSubject)
                => Vocabulary.AndList(head, lastSubject);

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string PowerAndHealth(int atk, int hp)
                => Loc.F($"{atk} power and {hp} health");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string Power(int atk)
                => Loc.F($"{atk} power");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string Health(int hp)
                => Loc.F($"{hp} health");

            // Claude, 0.7.347-0.7.357.
            public static string MorselFallback => Loc.T("Morsel");

            // Claude, 0.7.347-0.7.357.
            public static string AbilitiesTriggerGains(string lead, string sigil, string receiver, string parts)
                => Loc.F($"{lead} {Loc.Game(sigil)} abilities trigger, {receiver} gains {parts}.");

            // Claude, 0.7.284-0.7.315.
            public static string MorselGainFor(string receiverName, string parts)
                => Loc.F($"{receiverName} gained {parts}.");

            // Claude, 0.7.217-0.7.221. Said from 2 places.
            public static string IsSkinnedAnd(string name, DiskCardGame.CardInfo info)
                => Loc.F($"{name} is skinned and {Vocabulary.DeathTail(info)}.");

            // Zamar, Session 32 (applied Session 34). "[killed/destroyed]"
            // is whichever the card is, as for the Skinning Knife.
            public static string IsCutInHalfAnd(string name, DiskCardGame.CardInfo info)
                => Loc.F($"{name} is cut in half and {Vocabulary.DeathTail(info)}.");

            // Claude, 0.7.217-0.7.221.
            public static string IsCutAnd(string name, DiskCardGame.CardInfo info)
                => Loc.F($"{name} is cut and {Vocabulary.DeathTail(info)}.");

            // Claude, 0.7.217-0.7.221.
            public static string IsSmashedAnd(string name, DiskCardGame.CardInfo info)
                => Loc.F($"{name} is smashed and {Vocabulary.DeathTail(info)}.");

            // Claude, 0.7.110-0.7.206.
            public static string InSlot(string name, int number)
                => Loc.F($"{name} in Slot {number}");

            // Claude, 0.7.42 or earlier.
            public static string DirectDamageCount(int spokenDamage)
                => spokenDamage == 1 ? Loc.T("1 direct damage.") : Loc.F($"{spokenDamage} direct damage.");

            // Zamar, 0.7.110-0.7.206.
            public static string ScaleHits => Loc.T(" The scale hits 5...");

            // Zamar, 0.7.362. Leads the damage line that starts the Grizzly phase.
            public static string VolumeWarning => Loc.T("VOLUME WARNING: ");

            // Claude, 0.7.110-0.7.206.
            public static string EnemyDealsOrYouDeal(bool toPlayer, string dmgWord, string edge)
                => toPlayer
                   ? Loc.F($"Enemy deals {dmgWord}{edge}")
                   : Loc.F($"You deal {dmgWord}{edge}");

            // Claude, 0.7.316-0.7.325.
            public static string ReceivedBoneFromThe(string boonName)
                => Loc.F($"Received 1 bone from ") +
                   $"{boonName ?? Vocabulary.Combat.TheBoneLordsBoon}.";

            // Claude, 0.7.316-0.7.325. Part of Combat.ReceivedBoneFromThe, Combat.ReceivedBonesFromThe.
            public static string TheBoneLordsBoon => Loc.T("the Bone Lord's boon");

            // Claude, 0.7.316-0.7.325.
            public static string ReceivedBonesFromThe(string boonName)
                => Loc.F($"Received 8 bones from ") +
                   $"{boonName ?? Vocabulary.Combat.TheBoneLordsBoon}.";

            // Claude, 0.7.42 or earlier.
            public static string ReceivedBonesAtBattle(int amount)
                => Loc.F($"Received {amount} bones at battle start.");
        }

        /// <summary>Multi-strike, giant volley and Brittle summaries.</summary>
        public static class MultiStrike
        {
            // Zamar, 0.7.349.
            public static string AttacksEachOfYour(string attackerName, int strikes)
                => Loc.F($"{attackerName} attacks each of your {Vocabulary.NumberWord(strikes)} ") +
                   $"{(strikes == 1 ? Loc.T("slot") : Loc.T("slots"))}.";

            // Claude, 0.7.349.
            public static string AttacksTimes(string attackerName, int strikes)
                => Loc.F($"{attackerName} attacks {strikes} times.");

            // Claude, 0.7.347-0.7.357.
            public static string AndDies(DiskCardGame.CardInfo info, string cause)
                => Loc.F($"and {Vocabulary.DeathClause(info)}{cause}");

            // Claude, 0.7.347-0.7.357.
            public static string InStartSlot(int startSlotNumber)
                => Loc.F($" in slot {startSlotNumber}");

            // Claude, 0.7.284-0.7.315.
            public static string SlotHop(int number)
                => Loc.F($"slot {number}");

            // Claude, 0.7.347-0.7.357.
            public static string BurrowsFromTo(string name, string[] hops)
                => Loc.F($"{name} burrows from {string.Join(Loc.T(" to "), hops)}");

            // Claude, 0.7.347-0.7.357.
            public static string Burrows(string name, string inSlot)
                => Loc.F($"{name}{inSlot} burrows");

            // Claude, 0.7.347-0.7.357. Said from 2 places.
            public static string HopsTakingDies(string head, string dmg, string outcome)
                => Loc.F($"{head}, taking {dmg}, {outcome}.");

            // Claude, 0.7.347-0.7.357.
            public static string HopsTaking(string head, string dmg)
                => Loc.F($"{head}, taking {dmg}.");

            // Claude, 0.7.347-0.7.357.
            public static string TakesAndDies(string name, string inSlot, string dmg, string outcome)
                => Loc.F($"{name}{inSlot} takes {dmg} {outcome}.");

            // Claude, 0.7.347-0.7.357.
            public static string TakesStrikes(string name, string inSlot, string dmg)
                => Loc.F($"{name}{inSlot} takes {dmg}.");

            // Claude, 0.7.347-0.7.357.
            public static string TakesWithOutcome(string name, string inSlot, string dmg, string outcome)
                => Loc.F($"{name}{inSlot} takes {dmg}, {outcome}.");

            // Claude, 0.7.347-0.7.357.
            public static string AttacksGiant(string n, string giantName)
                => Loc.F($"{n} attacks {giantName}");

            // Claude, 0.7.347-0.7.357.
            public static string AttacksGiantTimes(string n, string giantName, int strikes)
                => Loc.F($"{n} attacks {giantName} {strikes} times");

            // Claude, 0.7.347-0.7.357.
            public static string YourCardsAttackTimes(string giantName, int strikes)
                => Loc.F($"Your cards attack {giantName} {strikes} times");

            // Claude, 0.7.347-0.7.357. Said from 2 places.
            public static string GiantDiesFor(string subject, string dmg)
                => Loc.F($"{subject} for {dmg}.");

            // Claude, 0.7.347-0.7.357.
            public static string ForHealthRemaining(string subject, string dmg, int h)
                => Loc.F($"{subject} for {dmg}, {h} health remaining.");

            // Claude, 0.7.347-0.7.357.
            public static string BrittleFallback => Loc.T("Brittle");

            // Claude, 0.7.347-0.7.357.
            public static string SAbilityTriggersAnd(string nameFirst, string sigil, string bones)
                => Loc.F($"{nameFirst}'s {Loc.Game(sigil)} ability triggers, and it dies.{bones}");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string NumberList(string[] range, string lastNum)
                => Vocabulary.AndList(range, lastNum);

            // Claude, 0.7.347-0.7.357.
            public static string InSlots(string n, int count, string list)
                => Loc.F($"{n} in {(count == 1 ? Loc.T("slot") : Loc.T("slots"))} {list}");

            // Claude, 0.7.347-0.7.357.
            public static string AbilitiesTriggerDie(string lead, string sigil, string who, string bones)
                => Loc.F($"{lead} {Loc.Game(sigil)} abilities trigger, {who} die.{bones}");
        }

        /// <summary>Turn changes and the draw phase.</summary>
        public static class Turns
        {
            // Claude, 0.7.42 or earlier.
            public static string YourTurn => Loc.T("Your turn.");

            // Claude, 0.7.42 or earlier.
            public static string EnemyTurn => Loc.T("Enemy turn.");

            // Claude, 0.7.347-0.7.357.
            public static string DrawPhaseSkippedDue => Loc.T("Draw Phase skipped due to no cards remaining in either deck...");
        }

        /// <summary>Sigils triggering.</summary>
        public static class Sigils
        {
            // Claude, 0.7.110-0.7.206.
            public static string GuardianName(bool noN, string n)
                => noN ? Loc.T("Guardian") : n;

            // Claude, 0.7.222-0.7.261.
            public static string SAbilityTriggersIt(string name, string sigil, int capturedTargetNumber)
                => Loc.F($"{name}'s {Loc.Game(sigil)} ability triggers: it is blocked from ") +
                   Loc.F($"moving to slot {capturedTargetNumber}.");

            // Claude, 0.7.222-0.7.261.
            public static string SAbilityTriggersItMoves(string name, string sigil, int capturedTargetNumber)
                => Loc.F($"{name}'s {Loc.Game(sigil)} ability triggers: it moves to slot {capturedTargetNumber}.");

            // Claude, 0.7.222-0.7.261.
            public static string Burrower => Loc.T("Burrower");

            // Claude, 0.7.110-0.7.206. Said from 2 places.
            public static string RightOrLeft(int nowSlot, int capturedFrom)
                => nowSlot > capturedFrom ? Loc.T("right") : Loc.T("left");

            // Zamar, 0.7.226. Said from 2 places.
            public static string SAbilityTriggersItMovesTo(string name, string sigilName, string dir, int nowSlotNumber)
                => Loc.F($"{name}'s {Loc.Game(sigilName)} ability triggers: it moves {dir} to slot {nowSlotNumber}.");

            // Claude, 0.7.110-0.7.206.
            public static string Sprinter => Loc.T("Sprinter");

            // Claude, 0.7.110-0.7.206.
            public static string Bloodlust => Loc.T("Bloodlust");

            // Zamar, 0.7.222-0.7.261.
            public static string SAbilityTriggersItNow(string name, string sigil, int now)
                => Loc.F($"{name}'s {Loc.Game(sigil)} ability triggers: it now has {now} power.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string EnemyOrYour(bool capturedEnemy)
                => capturedEnemy ? Loc.T("enemy") : Loc.T("your");

            // Zamar, Session 19.
            public static string SAbilityTriggersItPlaces(string name, string sigil, string egg, string side, int capturedNumber)
                => Loc.F($"{name}'s {Loc.Game(sigil)} ability triggers: it places a {egg} in ") +
                   Loc.F($"{side} slot {capturedNumber}.");

            // Zamar, Session 25.
            public static string SAbilityTriggersA(string capturedCage, string sigil, string freedName, string side, int capturedNumber)
                => Loc.F($"{capturedCage}'s {Loc.Game(sigil)} ability triggers: a ") +
                   Loc.F($"{freedName} is played in {side} slot {capturedNumber}.");

            // Claude, 0.7.110-0.7.206.
            public static string WithAbilities(string abilities)
                => Loc.T(", with ") + abilities;

            // Claude, 0.7.110-0.7.206.
            public static string Enemy => Loc.T("Enemy ");

            // Zamar, 0.7.222-0.7.261.
            public static string SAbilityTriggersItBecomes(string who, string capturedOld, string sigilName, int atk, int hp, string newName, string withClause)
                => Loc.F($"{who}{capturedOld}'s {Loc.Game(sigilName)} ability triggers: ") +
                   Loc.F($"it becomes a {atk}/{hp} {newName}{withClause}.");

            // Zamar, 0.7.339.
            public static string SAbilityFizzlesDue(string name, string sigil)
                => Loc.F($"{name}'s {Loc.Game(sigil)} ability fizzles due to having no empty item slots.");
        }

        /// <summary>Cards created, eggs, overkill, victory, items made.</summary>
        public static class Events
        {
            // Claude, 0.7.42 or earlier.
            public static string IsCreatedInYour(string name, string take)
                => Loc.F($"{name} is created in your hand.") + take;

            // Claude, 0.7.284-0.7.315.
            public static string AbilityTriggersIsAdded(string causeAtArrival, string article, string name)
                => Loc.F($"{causeAtArrival} ability triggers: {article} {name} is added to your hand.");

            // The second half of "Bat gets caught in the Leaping Trap and
            // dies. A Wolf Pelt is added to your hand." His words, Session 39.
            // Zamar, 0.7.424.
            public static string AIsAddedToYourHand(string article, string name)
                => Loc.F($"{article} {name} is added to your hand.");

            // Claude, 0.7.284-0.7.315.
            public static string OuroborosComesBackStronger => Loc.T(" Ouroboros comes back stronger than before...");

            // Claude, 0.7.284-0.7.315.
            public static string ArticleA => Loc.T("A");

            // Claude, 0.7.284-0.7.315.
            public static string AnOrA(int vowelAt)
                => vowelAt >= 0 ? Loc.T("An") : Loc.T("A");

            // Claude, 0.7.110-0.7.206.
            public static string CardsAbility(string causeCardName, string causeAbilityName)
                => Loc.F($"{causeCardName}'s {Loc.Game(causeAbilityName)}");

            // Claude, 0.7.110-0.7.206.
            public static string BySourcesAbility(string sourceName, string abilityName)
                => Loc.F($" by {sourceName}'s {Loc.Game(abilityName)}");

            // Claude, 0.7.110-0.7.206.
            public static string IsCreatedInOrIsCreatedOn(int slotNumber, string laid, string by)
                => slotNumber > 0
                   ? Loc.F($"{laid} is created in enemy Slot {slotNumber}{by}.")
                   : Loc.F($"{laid} is created on the opposing space{by}.");

            // Claude, 0.7.110-0.7.206.
            public static string ExcessDamageCarriesOrExcessDamageCarries(int slot)
                => slot > 0
                   ? Loc.F($"Excess damage carries to the queue, targeting Slot {slot}.")
                   : Loc.T("Excess damage carries to the queue.");

            // Zamar, 0.7.314.
            // 0.7.314, Zamar: "Shouldn't 1 be 'tooth'?" It should.
            public static string VictoryExtraReceivedOrVictory(int excessDamage)
                => excessDamage > 0
                   ? Loc.F($"Victory. {excessDamage} extra {(excessDamage == 1 ? Loc.T("tooth") : Loc.T("teeth"))} received.")
                   : Loc.T("Victory.");

            // Zamar, Session 34. An item arriving outside battle and outside
            // the item pickup (the campfire's Hoggy Bank).
            public static string AddedInItemSlot(string name, string where)
                => Loc.F($"{name} added in {where}.");

            // Claude, 0.7.43-0.7.52.
            public static string ItemSlotCount(int number)
                => number > 0 ? Loc.F($"item slot {number}") : Loc.T("an item slot");

            // Zamar, 0.7.338.
            public static string Chosen(string name)
                => Loc.F($"{name} chosen.");

            // Claude, 0.7.43-0.7.52.
            public static string IsCreatedIn(string name, string where)
                => Loc.F($"{name} is created in {where}.");

            // Claude, 0.7.42 or earlier.
            public static string Saving => Loc.T("Saving.");

            // Claude, 0.7.110-0.7.206.
            public static string IsAddedToYour(string cardName, string take)
                => Loc.F($"{cardName} is added to your hand.") + take;
        }

        /// <summary>Who is talking.</summary>
        public static class Dialogue
        {
            // Claude, 0.7.336-0.7.345. Key: PirateSkull.
            public static string PirateSkull => Loc.T("Pirate Skull");

            // Claude, 0.7.42 or earlier.
            public static string None => Loc.T("None");

            // Claude, 0.7.347-0.7.357.
            public static string SpeakerLine(string name, string clean)
                => $"{name}: {clean}";
        }

        /// <summary>Boss candles, skulls and hands.</summary>
        public static class Bosses
        {
            // Zamar, Session 19.
            public static string OneOfSCandles(string who, string numberWord)
                => Loc.F($"One of {who}'s {numberWord} candles is blown out.");

            // Claude, 0.7.110-0.7.206.
            public static string CandleIsBlownOrOneOfS(bool noWho, string who)
                => noWho
                   ? Loc.T("A candle is blown out.")
                   : Loc.F($"One of {who}'s candles is blown out.");

            // Claude, 0.7.110-0.7.206.
            public static string CandleIsBlownOut(bool noWho, int remaining, string who)
                => noWho
                   ? Loc.F($"A candle is blown out. {remaining} left.")
                   : Loc.F($"One of {who}'s candles is blown out. {remaining} left.");

            // Claude, 0.7.110-0.7.206.
            public static string TheMask(string maskIdentity)
                => Loc.F($"The {maskIdentity}");

            // Claude, 0.7.110-0.7.206.
            public static string LeshyThe(bool noBoss, string boss)
                => noBoss ? Loc.T("Leshy") : Loc.F($"The {boss}");

            // Zamar, 0.7.204. Key: Prospector.
            public static string LeshyPutsOnA => Loc.T("Leshy puts on a wooden mask of a dirty wild face, with a large mangled " +
                "beard and a crooked golden tooth. He holds a large pickaxe in a shaky hand...");

            // Claude, 0.7.110-0.7.206.
            public static string One => Loc.T("one");

            // Claude, 0.7.110-0.7.206.
            public static string Two => Loc.T("two");

            // Claude, 0.7.110-0.7.206.
            public static string Three => Loc.T("three");

            // Claude, 0.7.110-0.7.206.
            public static string Four => Loc.T("four");

            // Claude, 0.7.110-0.7.206.
            public static string Five => Loc.T("five");

            // Claude, 0.7.110-0.7.206.
            public static string Six => Loc.T("six");

            // Claude, 0.7.110-0.7.206. Said from 2 places.
            public static string CandleCount(int onTable)
                => onTable == 1 ? Loc.T("candle") : Loc.T("candles");

            // Claude, 0.7.217-0.7.221.
            public static string PlacesASkullHolding(string who, string numberWord, string candleWord)
                => Loc.F($"{who} places a skull holding {numberWord} lit {candleWord} ") +
                   Loc.T("onto the table.");

            // Zamar, 0.7.110-0.7.206.
            public static string LeshyRelightsYourCandle(string count, string word)
                => Loc.F($"Leshy relights your candle. You now have {count} {word} lit.");

            // 0.7.360: the reach added, his wording.
            // Zamar, 0.7.360.
            public static string BlowsOutYourLast(string who)
                => Loc.F($"{who} blows out your last candle. The room goes dark as {who} reaches both hands across the table...");

            // Zamar, 0.7.220.
            // HIS WORDING, 0.7.220: "should be rephrased to 'one of your
            // candles.'" — "a candle" left it ambiguous whose went out, in
            // a fight where both sides have them on the table.
            public static string BlowsOutOneOf(string who, int livesRemaining)
                => Loc.F($"{who} blows out one of your candles. You have {livesRemaining} remaining.");

            // Claude, 0.7.110-0.7.206.
            public static string IsAddedToYour(string gatheringFirst)
                => Loc.F($"{gatheringFirst} is added to your hand.");

            // Claude, 0.7.110-0.7.206.
            public static string AndAreAddedTo(string[] rest, string last)
                => Loc.F($"{Vocabulary.AndList(rest, last)} are added to your hand.");

            // Claude, 0.7.336-0.7.345.
            public static string IsAddedToYourHand(string nameFirst, string after)
                => Loc.F($"{nameFirst} is added to your hand.{after}");

            // Claude, 0.7.336-0.7.345.
            public static string AndAreAddedToYour(string[] names, string last, string after)
                => Loc.F($"{Vocabulary.AndList(names, last)} are added to your hand.{after}");

            // Claude, 0.7.42 or earlier.
            public static string ExtraCandleIsLit => Loc.T("An extra candle is lit.");
        }

        /// <summary>The Prospector.</summary>
        public static class ProspectorBoss
        {
            // Claude, 0.7.110-0.7.206.
            public static string ProspectorUsesHisPickaxe => Loc.T("The Prospector uses his pickaxe to strike all of your creatures, " +
                "turning them into Gold Nuggets.");

            // Zamar, 0.7.340.
            public static string ProspectorClearsHisQueue => Loc.T("The Prospector clears his queue, ");
        }

        /// <summary>The Pirate Skull.</summary>
        public static class PirateSkull
        {
            // Claude, 0.7.347-0.7.357.
            public static string Limoncello => Loc.T("The Limoncello");

            // Claude, 0.7.347-0.7.357.
            public static string YourTheirSlot(bool isPlayerSlot, int number)
                => (isPlayerSlot ? Loc.T("your") : Loc.T("their")) + Loc.F($" slot {number}");

            // Claude, 0.7.347-0.7.357.
            public static string CannonsFireAtIs(string where, string name, DiskCardGame.CardInfo info)
                => Loc.F($"Cannons fire at {where}, {name} is hit by cannonball and {Vocabulary.DeathClause(info)}.");

            // Claude, 0.7.347-0.7.357.
            public static string CannonsFireAtOrCannonsFireAt(int hp, string where, string name)
                => hp >= 0
                   ? Loc.F($"Cannons fire at {where}, {name} is hit by cannonball, {hp} health remaining.")
                   : Loc.F($"Cannons fire at {where}, {name} is hit by cannonball.");
        }

        /// <summary>The card choice screen.</summary>
        public static class CardChoices
        {
            // Claude, 0.7.222-0.7.261.
            public static string DeathCard => Loc.T("Death card.");

            // Claude, 0.7.222-0.7.261.
            public static string BloodCostCard(int n)
                => Loc.F($"{n} blood cost card.");

            // Claude, 0.7.222-0.7.261.
            public static string BonesCostCard => Loc.T("Bones cost card.");

            // Claude, 0.7.222-0.7.261.
            public static string KinCard(DiskCardGame.Tribe tribe)
                => Loc.F($"{Loc.Game(tribe.ToString())} kin card.");

            // Claude, 0.7.222-0.7.261.
            public static string NewCardsDealt => Loc.T("New cards dealt.");

            // Zamar, Session 16.
            public static string UseTheLuckyClover => Loc.T(" Use the Lucky Clover to discard them all and be dealt new cards.");

            // Session 34: the same without the deck view, for the Mycologists.
            public static string OpeningLineNoDeck(string lead, string cloverPart)
                => $"{lead}{cloverPart} " +
                   Loc.T("Left and right arrows to browse, Enter to turn a card over, ") +
                   Loc.T("Space to repeat, H for help.");

            // 0.7.329, Zamar: "This line needs Shift Up callout."
            // Claude, 0.7.326-0.7.335.
            public static string OpeningLine(string lead, string cloverPart)
                => $"{lead}{cloverPart} " +
                   Loc.T("Left and right arrows to browse, Enter to turn a card over, ") +
                   Loc.T("Shift Up displays your deck, Space to repeat, H for help.");

            // Title Zamar's (Session 34). Face-down sentence Zamar's, Session 35:
            // "Before you lay 3 face-down duplicate cards. Choose 1 to add to
            // your deck." The face-up case (no count to give) keeps the
            // Session 34 wording, still provisional.
            // Zamar, Session 35.
            public static string MycologistCardSelection(int faceDown, string cardWord)
                => Loc.T("Mycologist Card Selection. ")
                   + (faceDown > 0
                        ? (faceDown == 1
                            ? Loc.T("Before you lay 1 face-down duplicate card. ")
                            : Loc.F($"Before you lay {faceDown} face-down duplicate cards. "))
                          + Loc.T("Choose 1 to add to your deck.")
                        : Loc.T("Every card is face up. ")
                          + Loc.T("Each one is a copy of a card already in your deck. Choose 1 to add to your deck."));

            // Zamar, Session 33 (built Session 34). The Deck Trial's H line.
            public static string DeckTrialHelp => Loc.T("Deck Trial. Leshy offers you three conditions to choose from. " +
                "Pick the one most likely to be true for your current deck. He will then reveal three random cards from your deck. " +
                "If you succeed you will receive a reward. Left and right arrows to browse, Enter to turn a card over, " +
                "Shift Up displays your deck, Space to repeat, H for help.");

            // 0.7.341: he/him for Leshy, Zamar Session 26
            // Zamar, 0.7.336-0.7.345.
            public static string DeckTrialLeshyPuts => Loc.T("Deck Trial. Leshy puts a stone arch prop on the table, " +
                "making it appear like his glowing eyes are coming from " +
                "a dark cave entrance.");

            // The Deck Trial's idle prompt: the title without the stone arch
            // sentence (Zamar, Session 40: "this should not be repeated in
            // the idle prompt").
            // Zamar, 0.7.427.
            public static string DeckTrialTitle => Loc.T("Deck Trial.");

            // Zamar, 0.7.200. Said from 2 places.
            public static string RareCardChoiceOrCardChoice(bool isRareCardChoicesSequencer)
                => (isRareCardChoicesSequencer)
                   ? Loc.T("Rare Card Choice")
                   : Loc.T("Card Choice");

            // Claude, 0.7.43-0.7.52.
            public static string FaceDownCardCount(int faceDown)
                => faceDown == 1 ? Loc.T("1 face-down card") : Loc.F($"{faceDown} face-down cards");

            // Zamar, 0.7.266.
            public static string FaceDownRareCardCount(int faceDown)
                => faceDown == 1
                   ? Loc.T("1 face-down rare card")
                   : Loc.F($"{faceDown} face-down rare cards");

            // Zamar, Session 19. The face-up branch is Claude's.
            public static string LeshyPresentsYouOrEveryCardIs(int faceDown, string what, string rareWord)
                => faceDown > 0
                   ? Loc.F($"{what}. Leshy presents you a wooden box. He pops a lock and ") +
                   Loc.F($"opens it, revealing {rareWord}... Choose 1 to add to your deck.")
                   : Loc.F($"{what}. Every card is face up. Choose 1 to add to your deck.");

            // Claude, 0.7.110-0.7.206.
            public static string BeforeYouLayOrEveryCardIs(int faceDown, string what, string cardWord)
                => faceDown > 0
                   ? Loc.F($"{what}. Before you lay {cardWord}. Choose 1 to add to your deck.")
                   : Loc.F($"{what}. Every card is face up. Choose 1 to add to your deck.");

            // Zamar, 0.7.47.
            public static string LuckyCloverDiscardAll => Loc.T("Lucky Clover. Discard all cards and be dealt new ones.");

            // Claude, 0.7.53-0.7.109.
            public static string FaceDown => Loc.T("Face down.");

            // Claude, 0.7.316-0.7.325.
            public static string TrialOf(string typeName)
                => Loc.F($"Trial of {typeName}.");

            // A turned trial card, hovered again. Zamar, Session 40: "Hovering
            // should restate the conditions. 'Trial of Power. Your 3 cards
            // require [x] combined Power.' 'Trial of Health....' etc etc."
            // Power and Health are his words. Wisdom, Blood and Bones follow
            // his shape and he OK'd them as drafted (Session 40: "14-16 are
            // good"). Kin is his: "2 of your 3 cards must be the same kin
            // type." The number is the game's. Any other trial type says
            // its name only.
            // Zamar, 0.7.428.
            public static string TrialCondition(DeckTrialSequencer.Trial.Type type, int threshold)
            {
                switch (type)
                {
                    case DeckTrialSequencer.Trial.Type.Power:  return Loc.F($"Your 3 cards require {threshold} combined Power.");
                    case DeckTrialSequencer.Trial.Type.Health: return Loc.F($"Your 3 cards require {threshold} combined Health.");
                    case DeckTrialSequencer.Trial.Type.Wisdom: return Loc.F($"Your 3 cards require {threshold} combined sigils.");
                    case DeckTrialSequencer.Trial.Type.Blood:  return Loc.F($"Your 3 cards require {threshold} combined Blood cost.");
                    case DeckTrialSequencer.Trial.Type.Bones:  return Loc.F($"Your 3 cards require {threshold} combined Bone cost.");
                    case DeckTrialSequencer.Trial.Type.Kin:    return Loc.F($"{threshold} of your 3 cards must be the same kin type.");
                    default: return null;
                }
            }

            // Claude, 0.7.53-0.7.109.
            public static string FaceDownName(bool noBack, string back)
                => noBack ? Vocabulary.CardChoices.FaceDown : back;

            // Claude, 0.7.53-0.7.109.
            public static string FaceUpButThe => Loc.T("Face up, but the card could not be read.");

            // Claude, 0.7.43-0.7.52.
            public static string DuplicateOfCardIn => Loc.T(" Duplicate of card in your deck.");

            // Zamar, 0.7.262-0.7.281.
            public static string ArrowKeysToBrowse(string what)
                => Loc.F($"{what}. Arrow keys to browse.");

            // Claude, 0.7.222-0.7.261.
            public static string ThatCardCouldNot => Loc.T("That card could not be taken.");

            // Claude, 0.7.43-0.7.52.
            public static string LuckyCloverCouldNot => Loc.T("The Lucky Clover could not be used.");

            // Claude, 0.7.42 or earlier.
            public static string ThatCardCouldNotBe => Loc.T("That card could not be selected.");

            // Claude, 0.7.42 or earlier. Said from 2 places.
            public static string AddedToYourOrCardAddedTo(string name)
                => !string.IsNullOrWhiteSpace(name)
                   ? Loc.F($"{name} added to your deck.")
                   : Loc.T("Card added to your deck.");

            // Claude, 0.7.222-0.7.261.
            public static string PressEnterToAdd(string described)
                => described + Loc.T(" Press Enter to add it to your deck.");
        }

        /// <summary>Your deck (Shift Up).</summary>
        public static class DeckView
        {
            // Claude, 0.7.53-0.7.109.
            public static string DeckReturned => Loc.T("Deck returned.");

            // Claude, 0.7.53-0.7.109. The node screen form is 0.7.359: a deck
            // opened from a node screen sits above that screen, and Backspace
            // goes back to it — "the Trader", not "the map".
            public static string PlaceBelow(bool active, string nodeScreen = null)
                => !string.IsNullOrEmpty(nodeScreen) ? Loc.F($"the {nodeScreen}")
                   : (active ? Loc.T("the card choice") : Loc.T("the map"));

            // Claude, 0.7.53-0.7.109.
            public static string YourDeck => Loc.T("Your deck. ");

            // Claude, 0.7.262-0.7.281.
            public static string LeftAndRightArrows(string lead, string countWord, string kinBreakdown, string currencyPart, string placeBelow)
                => $"{lead}{countWord}.{kinBreakdown}" +
                   $"{currencyPart} " +
                   Loc.F($"Left and right arrows to browse, Space to repeat, A for teeth, I cycles through your items, H for help, Backspace to return to {placeBelow}.");

            // Claude, 0.7.53-0.7.109.
            public static string YourDeckIsStill => Loc.T("Your deck is still being laid out.");

            // Claude, 0.7.53-0.7.109.
            public static string YourDeckName(string countWord)
                => Loc.F($"Your deck. {countWord}.");

            // Claude, 0.7.262-0.7.281.
            // 0.7.275 — "cycles", because it does now. A key that outlives
            // its own description is the same defect as a key that does
            // nothing; the sentence and the binding change together.
            public static string LeftAndRightArrowsBrowse(string sizePart, string currencyPart, string shiftRHelp, string placeBelow)
                => $"{sizePart}{currencyPart} " +
                   Loc.T("Left and right arrows browse the cards. Space repeats the card you are on. ") +
                   Loc.T("A reads the amount of teeth collected. I cycles through your items, one per press. ") +
                   Loc.T("R opens the rulebook. ") + shiftRHelp + " " +
                   Loc.F($"Backspace closes your deck and returns you to {placeBelow}.");

            // Claude, 0.7.43-0.7.52.
            public static string NoTeethAreShown => Loc.T("No teeth are shown here.");

            // Claude, 0.7.53-0.7.109.
            public static string ToothCollectedCount(int teeth)
                => teeth == 1 ? Loc.T(" 1 tooth collected.") : Loc.F($" {teeth} teeth collected.");
        }

        /// <summary>Picking a card from your deck.</summary>
        public static class DeckPick
        {
            // Verbatim; the count is the deck's own.
            // Zamar, 0.7.359.
            public static string OpeningLine(string countWord)
                => Loc.F($"Your deck. Choose a card to add to your hand. Arrows browse, Enter chooses. {countWord}.");

            // Verbatim; the count is the deck's own.
            // Zamar, 0.7.359.
            public static string HelpLine(string countWord)
                => Loc.T("Choose a card from your deck to add to your hand. ") +
                   Vocabulary.BrowseChooseRepeat_ +
                   Loc.F($"{countWord}. G, B and A to read the boardstate.");
        }

        /// <summary>Node screens: campfire, stone, mycologists, woodcarver, trader, trapper, Bone Lord, copy card, item pickup.</summary>
        public static class NodeScreens
        {
            // Zamar, 0.7.53-0.7.109. Key: hostslot.
            public static string HostCard => Loc.T("Host Card");

            // Zamar, 0.7.53-0.7.109. Key: sacrificeslot.
            public static string CardToSacrifice => Loc.T("Card to Sacrifice");

            // Zamar, 0.7.53-0.7.109. Key: confirmbutton.
            public static string BeginRitual => Loc.T("Begin Ritual");

            // Zamar, 0.7.110-0.7.206. Key: Campfire|confirmbutton.
            public static string SendCardToRest => Loc.T("Send card to rest by the fire");

            // Zamar, 0.7.287. Key: Mycologists|confirmbutton.
            public static string BeginExperiment => Loc.T("Begin experiment...");

            // Zamar, 0.7.284-0.7.315. Key: Mycologists|selectionslot.
            public static string SelectAPairOf => Loc.T("Select a pair of cards to fuse together");

            // Zamar, 0.7.267. Key: Copy card|confirmbutton.
            public static string BeginPainting => Loc.T("Begin painting");

            // Zamar, 0.7.262-0.7.281. Key: Copy card|selectionslot.
            public static string ChooseACardTo => Loc.T("Choose a card to receive an artistic rendition of");

            // Zamar, 0.7.53-0.7.109. Key: Sacrifice stone.
            public static string ChooseOneCardFrom => Loc.T("Choose one card from your deck to be the host. " +
                "Sacrifice another card from your deck to transfer its sigils to the host.");

            // Zamar, Session 18. Key: Campfire.
            public static string SelectACardFrom => Loc.T("Select a card from your deck to warm by the campfire. " +
                "Doing so will permanently raise one of its stats, but comes with risk...");

            // Zamar, 0.7.250. Key: Mycologists.
            public static string ChooseAPairOf => Loc.T("Choose a pair of matching cards from your deck to fuse into one. Their attack " +
                "and health are added together, and sigils from both are kept.");

            // Zamar, 0.7.250. Key: Woodcarver.
            public static string PickANewWoodcarving => Loc.T("Pick a new woodcarving to add to your backpack, either a Kin type head " +
                "or a Sigil body. Then if you have both a head and body, choose a totem " +
                "combination to bring with you. Each card in your deck that matches the " +
                "Kin type head, will gain the totem's Sigil, for as long as the totem is " +
                "in use.");

            // Zamar, Session 25. Key: Trapper.
            // Zamar's wording, Session 25 (0.7.330), replacing his #28 — the
            // arrival line and H say the same thing.
            // 0.7.334: the Trader sentences cut, his call — "too much info."
            public static string TradeTheTeethYouve => Loc.T("Trade the teeth you've collected for pelt cards to add to your deck.");

            // Zamar, 0.7.359. Key: Trader. Replaces his Session 25 "Trade pelts
            // for cards." — the Trader's arrival line and both of its H lines
            // describe the screen with this sentence now.
            public static string TradePeltsFromYourDeck => Loc.T("Trade pelts from your deck for new cards.");

            // Zamar, Session 25. Key: Bone Lord.
            // Zamar's wording, Session 25. He first wrote "at the start of each of your
            // turns"; the game (BoonsHandler.ActivatePreCombatBoons) gives
            // the bones once per battle, and he chose the game's version.
            public static string SelectOneCardFrom => Loc.T("Select one card from your deck to be removed from it permanently. " +
                "The Bone Lord offers a boon in return, a passive effect that grants you " +
                "bones at the start of each battle.");

            // Zamar, 0.7.262-0.7.281. Key: Copy card.
            public static string SelectOneCardFromYour => Loc.T("Select one card from your deck. A copy of it is added to your deck, " +
                "although it might be slightly off.");

            // Claude, 0.7.316-0.7.325. Key: Choose a card to receive an artistic rendition of, Copy card. Said from 2 places.
            public static string CardToReceiveAn => Loc.T("card to receive an artistic rendition");

            // The campfire's filled slot (note D2): "Mealworm chosen as card to
            // risk resting by the campfire. Enter to rechoose."
            // Zamar, 0.7.414.
            public static string CardToRiskResting => Loc.T("card to risk resting by the campfire");

            // Claude, 0.7.316-0.7.325. Key: Host Card.
            public static string HostCardName => Loc.T("host card");

            // Claude, 0.7.316-0.7.325. Key: Card to Sacrifice.
            public static string ChosenAsLabelsCardToSacrifice => Loc.T("card to sacrifice");

            // Claude, 0.7.222-0.7.261.
            public static string BackpackCannotBeReached => Loc.T("The backpack cannot be reached right now.");

            // Claude, 0.7.262-0.7.281.
            public static string BackpackArrowsBrowseOrBackpackArrowsBrowse(bool backspaceLeavesBackpack)
                => backspaceLeavesBackpack
                   ? Loc.T("Backpack. Arrows browse, Backspace returns to woodcarving selection.")
                   : Loc.T("Backpack. Arrows browse, Enter chooses a totem combination.");

            // Claude, 0.7.222-0.7.261.
            public static string DeckCannotBeReached => Loc.T("The deck cannot be reached right now.");

            // Claude, 0.7.222-0.7.261.
            public static string CardS(DiskCardGame.Tribe t, int n)
                => Loc.F($" {t}, {n} card{(n == 1 ? "" : "s")}.");

            // Zamar, 0.7.258.
            public static string WoodcarvingSelection => Loc.T("Woodcarving selection.");

            // Zamar, 0.7.154.
            public static string WithdrawFromTheCampfire => Loc.T("Withdraw from the campfire.");

            // Claude, 0.7.110-0.7.206.
            public static string Fire => Loc.T("the fire");

            // Claude, 0.7.284-0.7.315.
            public static string PairOf(string plural)
                => Loc.F($"Pair of {plural}.");

            // Claude, 0.7.347-0.7.357.
            public static string NameNumberRest(string name, int n, string rest)
                => $"{name} {n}, {rest}";

            // Claude, 0.7.222-0.7.261.
            public static string Pluralise(bool endsInS, string name)
                => endsInS
                   ? name
                   : name + Loc.T("s");

            // Claude, 0.7.326-0.7.335.
            public static string AltarOfTheBoneLord => Loc.T("Altar of the Bone Lord");

            // Claude, 0.7.326-0.7.335. Key: Bone Lord.
            public static string AltarOfTheBone => Loc.T("Altar of the Bone Lord. Arrows to browse, Enter to confirm.");

            // 0.7.334: the Trader sentences cut, his call.
            // 0.7.360: "given a Rabbit Pelt", his rewording.
            // Zamar, 0.7.330. Key: Trapper.
            public static string TrapperYouAreGifted => Loc.T("Trapper. You are given a Rabbit Pelt. Trade the teeth you've collected " +
                "for pelt cards to add to your deck.");

            // Claude, 0.7.326-0.7.335.
            public static string TeethWord(int n)
                => n == 1 ? Loc.T("1 tooth") : Loc.F($"{n} teeth");

            // Claude, 0.7.326-0.7.335.
            public static string KnifeName => Loc.T("Skinning Knife");

            // Claude, 0.7.347-0.7.357.
            public static string KnifeForTeeth(string teethWord, string knifeName, string brokeClause)
                => $"{teethWord}, {knifeName}.{brokeClause}";

            // Claude, 0.7.347-0.7.357.
            public static string Name(int attack, int health)
                => $"{attack}, {health}";

            // Claude, 0.7.347-0.7.357.
            public static string PriceNameStats(string teethWord, string name, string stats)
                => $"{teethWord}, {name}, {stats}.";

            // Claude, 0.7.347-0.7.357.
            public static string NameStats(string name, string stats)
                => $"{name}, {stats}.";

            // Claude, 0.7.326-0.7.335.
            public static string PurchasedForYouHave(string name, string teethWord, int after, string brokeClause)
                => Loc.F($"{name} purchased for {teethWord}. You have {after} remaining.{brokeClause}");

            // Claude, 0.7.326-0.7.335.
            public static string YouAreShortFrom(string teethWord, string name, string brokeClause)
                => Loc.F($"You are {teethWord} short from buying {name}.{brokeClause}");

            // Claude, 0.7.326-0.7.335.
            public static string NotEnoughTeethRemaining => Loc.T(" Not enough teeth remaining for another purchase. Backspace to leave the Trapper.");

            // Claude, 0.7.326-0.7.335.
            public static string YouHave(string teethWord)
                => Loc.F($"You have {teethWord}.");

            // Claude, 0.7.42 or earlier.
            public static string PairList(string first, string second)
                => Loc.F($"{first} and {second}");

            // Claude, 0.7.326-0.7.335.
            public static string ListOfThree(string[] range, string lastPart)
                => string.Join(", ", range) +
                   Loc.F($", and {lastPart}");

            // Claude, 0.7.42 or earlier.
            public static string PeltsAddedLine(string list)
                => Loc.F($"{list} added to your deck.");

            // Claude, 0.7.326-0.7.335.
            public static string Sacrificed(string sacrificedName)
                => Loc.F($"{sacrificedName} sacrificed.");

            // Claude, 0.7.53-0.7.109.
            public static string SigilCount(int count)
                => count == 1 ? Loc.T("sigil") : Loc.T("sigils");

            // Zamar, 0.7.53-0.7.109. His format.
            public static string RitualResult(string ritualHostName, string list, string word)
                => Loc.F($"{ritualHostName} gained the {list} {word}.");

            // Claude, 0.7.110-0.7.206.
            public static string BoostPhrase(int by, string stat)
                => by > 0 ? Loc.F($"its {stat} by {by}") : Loc.F($"its {stat}");

            // Claude, 0.7.110-0.7.206.
            public static string ThisWillPermanentlyOrThisWillPermanently(string phrase)
                => phrase == null
                   ? Loc.T("This will permanently raise one of its stats")
                   : Loc.F($"This will permanently raise {phrase}");

            // Claude, 0.7.110-0.7.206.
            public static string CampfireChooseLine(string verb)
                => Loc.F($"Choose a card to warm by the campfire. {verb}.");

            // Claude, 0.7.284-0.7.315.
            public static string ContinueRestingPeacefullyOrContinueRestingBy(bool safe)
                => safe
                   ? Loc.T("Continue resting peacefully by the campfire, and raise the stat again.")
                   : Loc.T("Continue resting by the campfire, and raise the stat again.");

            // Claude, 0.7.110-0.7.206.
            public static string ChosenCard(string campfireChosenCard)
                => campfireChosenCard ?? Loc.T("the chosen card");

            // Claude, 0.7.284-0.7.315.
            public static string ToPeacefullyRestOrToRiskResting(bool safe)
                => safe ? Loc.T("to peacefully rest by") : Loc.T("to risk resting by");

            // Claude, 0.7.284-0.7.315.
            public static string SendTheCampfireOrSendTheCampfire(string phrase, string card, string verb)
                => phrase == null
                   ? Loc.F($"Send {card} {verb} the campfire")
                   : Loc.F($"Send {card} {verb} the campfire, permanently raising {phrase}.");

            // Claude, 0.7.53-0.7.109.
            public static string ChooseACardToBecome => Loc.T("Choose a card to become the host.");

            // Claude, 0.7.53-0.7.109.
            public static string ChooseACardToSacrifice => Loc.T("Choose a card to sacrifice.");

            // Zamar, Session 18.
            public static string ChooseACardToWarm => Loc.T("Choose a card to warm by the campfire. This will raise its stats but comes with risk...");

            // Claude, 0.7.53-0.7.109.
            public static string ChooseACard => Loc.T("Choose a card.");

            // Zamar, 0.7.157. Said from 2 places.
            public static string Chosen(string chosen)
                => Loc.F($"{chosen} chosen.");

            // Claude, 0.7.135.
            public static string SelectedAs(string chosen, string chosenAs)
                => Loc.F($"{chosen} selected as {chosenAs}.");

            // "rechose" corrected to "rechoose" - his typo, his call (Session 32).
            // Zamar, 0.7.329.
            public static string PairOfChosenTo(string plural)
                => Loc.F($"Pair of {plural} chosen to fuse together. Enter to rechoose.");

            // "rechose" corrected to "rechoose" - his typo, his call (Session 32).
            // Zamar, 0.7.319.
            public static string ChosenAsEnterTo(string cardName, string chosenAs)
                => Loc.F($"{cardName} chosen as {chosenAs}. Enter to rechoose.");

            // Claude, 0.7.347-0.7.357.
            public static string Word(string name, string cardName)
                => $"{name}: {cardName}.";

            // Claude, 0.7.316-0.7.325.
            public static string BoonCardLine(bool noDesc, string name, string desc)
                => noDesc ? Loc.F($"A {name}.") : Loc.F($"A {name}. {desc}");

            // Zamar, Session 25.
            public static string ReceivedBoon(string name)
                => Loc.F($"Received {name}.");

            // Claude, 0.7.222-0.7.261.
            public static string CarvedHeadOrHead(DiskCardGame.Tribe tribe)
                => tribe == Tribe.None
                   ? Loc.T("A carved head.")
                   : Loc.F($"{Loc.Game(tribe.ToString())} head.");

            // Zamar, 0.7.275.
            public static string CarvedSigilBodyOrSigilBody(bool noSigil, string sigil)
                => noSigil
                   ? Loc.T("A carved sigil body.")
                   : Loc.F($"{Loc.Game(sigil)} sigil body.");

            // Claude, 0.7.222-0.7.261.
            public static string TotemYourCardsGain(DiskCardGame.Tribe tribe, string sigil)
                => Loc.F($"Totem: your {Loc.Game(tribe.ToString())} cards gain {Loc.Game(sigil)}.");

            // Claude, 0.7.222-0.7.261.
            public static string FinishedTotem => Loc.T("The finished totem.");

            // Claude, 0.7.43-0.7.52.
            public static string OptionOf(string describeCard, int cardNumber, int count)
                => $"{describeCard} " +
                   Loc.F($"Option {cardNumber} of {count}.");

            // Claude, 0.7.222-0.7.261.
            public static string OptionSLeftAnd(string screenTitle, int count)
                => Loc.F($"{screenTitle}. {count} option{(count == 1 ? "" : "s")}. ") +
                   Loc.T("Left and right arrows to browse, Enter to choose, Space to repeat, H for help.");

            // Claude, 0.7.53-0.7.109.
            public static string OptionOfName(string describePart, int number, int count)
                => Loc.F($"{describePart}. Option {number} of {count}.");

            // Claude, 0.7.222-0.7.261.
            public static string NoOptionSelectedUse => Loc.T("No option selected. Use left and right arrows to browse.");

            // Claude, 0.7.284-0.7.315.
            public static string NoPairIsOn => Loc.T("No pair is on the stone yet. Choose a pair of cards first.");

            // Claude, 0.7.284-0.7.315.
            public static string PairOfSelected(string plural)
                => Loc.F($"Pair of {plural} selected.");

            // Zamar, Session 34. Taking the finished totem at the Woodcarver.
            public static string YouTakeTheTotem(DiskCardGame.Tribe tribe, string sigil)
                => Loc.F($"You take the {Loc.Game(tribe.ToString())} Kin totem with the {Loc.Game(sigil)} Sigil.");

            // Claude, 0.7.222-0.7.261.
            public static string YouTookThe(string bare)
                => Loc.F($"You took the {bare}.");

            // Claude, 0.7.222-0.7.261.
            public static string Selected(string bare)
                => Loc.F($"{bare} selected.");

            // Claude, 0.7.53-0.7.109.
            public static string NothingToChooseLine => Loc.T("Nothing to choose here.");

            // The Trader's arrival line, verbatim. The count is the pelts still
            // on the table (TradePeltsSequencer.peltCards), and "pelt" when it
            // is one. A table with no pelts on it leaves the count sentence out
            // rather than saying zero.
            // Zamar, 0.7.359.
            public static string TraderArrival(int pelts)
                => pelts > 0
                   ? Loc.F($"Trader. {Vocabulary.NodeScreens.TradePeltsFromYourDeck} You have {pelts} {(pelts == 1 ? Loc.T("pelt") : Loc.T("pelts"))} left to trade.")
                   : Loc.F($"Trader. {Vocabulary.NodeScreens.TradePeltsFromYourDeck}");

            // H on the Trader while its offers are on the table, verbatim.
            // Zamar, 0.7.359.
            public static string TraderHelp(int pelts)
                => $"{Vocabulary.NodeScreens.TraderArrival(pelts)} " +
                   Vocabulary.BrowseChooseRepeat_ + Vocabulary.NodeScreens.ShiftUpDisplaysYour;

            // Claude, 0.7.222-0.7.261. The pelt clause moved to the Trader's own
            // line, 0.7.359.
            public static string ChoosingACardArrows(string cardWord)
                => Loc.F($"Choosing a card. {cardWord}. ") +
                   Loc.T("Arrows browse, Enter chooses, Space repeats your position.");

            // Claude, 0.7.42 or earlier.
            public static string OptionCount(int count)
                => count == 1
                   ? Loc.T("1 option")
                   : (count <= 0 ? null : Loc.F($"{count} options"));

            // Claude, 0.7.222-0.7.261. The Trader's H ends on it too, 0.7.359 —
            // his line, the same sentence, so it lives here once.
            public static string ShiftUpDisplaysYour => Loc.T("Shift Up displays your deck.");

            // Claude, 0.7.222-0.7.261.
            public static string OpensYourBackpackShift => Loc.T("A opens your backpack, Shift Up displays your deck. ");

            // Claude, 0.7.326-0.7.335.
            public static string CountsYourTeethBackspace => Loc.T("A counts your teeth, Backspace leaves the Trapper at anytime. ");

            // Claude, 0.7.337.
            public static string Zero => Loc.T("zero");

            // Claude, 0.7.337.
            public static string One => Loc.T("one");

            // Claude, 0.7.337.
            public static string Two => Loc.T("two");

            // Claude, 0.7.337.
            public static string Three => Loc.T("three");

            // Claude, 0.7.337.
            public static string Four => Loc.T("four");

            // Claude, 0.7.337.
            public static string Five => Loc.T("five");

            // A full pack (note D3): his three words in place of the count.
            // Zamar, 0.7.414.
            public static string ItemSlotsFull(string screenTitle)
                => Loc.F($"{screenTitle}. Item slots full. ") +
                   Vocabulary.BrowseChooseRepeat_ +
                   Loc.T("I cycles through your current items. R opens the rulebook.");

            // Zamar, 0.7.337.
            public static string SelectOneOfItems(string screenTitle, string howMany)
                => Loc.F($"{screenTitle}. Select one of {howMany} items to add to your backpack. ") +
                   Vocabulary.BrowseChooseRepeat_ +
                   Loc.T("I cycles through your current items. R opens the rulebook.");

            // Zamar, 0.7.335.
            public static string ArrowsBrowseEnterChooses(string screenTitle, int length, string blurb)
                => $"{screenTitle}. {(length > 0 ? blurb + " " : "")}" +
                   Loc.T("Arrows browse, Enter chooses, Backspace leaves the Trapper at anytime, ") +
                   Loc.T("Space repeats your position, A counts your teeth, R opens the rulebook.");

            // Claude, 0.7.53-0.7.109.
            public static string ArrowsBrowseEnterChoosesSpace(string screenTitle, int length, string blurb, string trapperKeys, string packKey, string deckKey)
                => $"{screenTitle}. {(length > 0 ? blurb + " " : "")}" +
                   Vocabulary.BrowseChooseRepeat_ + trapperKeys + packKey + deckKey +
                   Loc.T("R opens the rulebook.");

            // Claude, 0.7.280.
            // 0.7.280, his words: the combination is not final until it
            // is taken, and a screen that offers only the way forward
            // reads as one with no way back.
            public static string TotemIsAssembledYour(DiskCardGame.Tribe tribe, string sigil)
                => Loc.F($"The totem is assembled. Your {Loc.Game(tribe.ToString())} cards will gain {Loc.Game(sigil)}. ") +
                   Loc.T("Enter to take the totem with you. Backspace to return to backpack and reselect.");   // Session 34, Zamar: B on the pad (was A)

            // Session 34: the same line when the game assembled the totem
            // itself (one head, one body) - the game offers no way back, so
            // the reselect sentence is dropped rather than promised.
            public static string TotemIsAssembledAuto(DiskCardGame.Tribe tribe, string sigil)
                => Loc.F($"The totem is assembled. Your {Loc.Game(tribe.ToString())} cards will gain {Loc.Game(sigil)}. ") +
                   Loc.T("Enter to take the totem with you.");

            // Claude, 0.7.222-0.7.261.
            public static string ReceivedNoNewSigils(string name)
                => Loc.F($"{name} received no new sigils.");

            // Claude, 0.7.222-0.7.261.
            public static string Received(string name, string gainedNameFirst)
                => Loc.F($"{name} received {gainedNameFirst}.");

            // Claude, 0.7.222-0.7.261.
            public static string ReceivedAnd(string name, string[] rest, string lastGainedName)
                => Loc.F($"{name} received {string.Join(", ", rest)} ") +
                   Loc.F($"and {lastGainedName}.");

            // Claude, 0.7.213-0.7.216.
            public static string CopyCard => Loc.T("Copy card");
        }

        /// <summary>The copy card node's introduction.</summary>
        public static class CopyCardNode
        {
            // Zamar, 0.7.262-0.7.281.
            public static string CorkedGlassBottleSuddenly => Loc.T("A corked glass bottle suddenly jumps onto the game table. " +
                "Landing next to it are two tiny painting easels with blank cards on them.");

            // Zamar, 0.7.262-0.7.281.
            public static string InsideTheGlassBottle => Loc.T("Inside the glass bottle is glowing green goo. It has a misshapen face. " +
                "Attached to the bottle is a glowing tipped paintbrush.");
        }

        /// <summary>Item pickup.</summary>
        public static class ItemPickup
        {
            // Zamar, Session 26; "presents" his change, Session 32 (applied
            // Session 34). Key: Magickal Bleach.
            public static string LeshyOffersYouAn => Loc.T("Leshy presents you an ornate inkwell with {0} inside and a paintbrush.");

            // Zamar, Session 32 (applied Session 34). Key: Harpie's Birdleg Fan.
            public static string PresentBirdlegFan => Loc.T("Leshy presents you a collapsible fan with bird talon feet.");

            // Zamar, Session 32 (applied Session 34). Key: Squirrel In A Bottle.
            public static string PresentSquirrelBottle => Loc.F($"Leshy presents you a corked glass bottle with a small {Loc.Game("Squirrel")} card inside of it.");

            // Zamar, Session 32 (applied Session 34). Key: Black Goat Bottle.
            public static string PresentGoatBottle => Loc.F($"Leshy presents you a corked glass bottle with a small {Loc.Game("Black Goat")} card inside of it.");

            // Zamar, Session 32 (applied Session 34). Key: Frozen Opossum Bottle.
            public static string PresentOpossumBottle => Loc.F($"Leshy presents you a corked glass bottle with a small {Loc.Game("Frozen Opossum")} card inside of it.");

            // Zamar, Session 32 (applied Session 34). Key: Boulder In A Bottle.
            public static string PresentBoulderBottle => Loc.F($"Leshy presents you a corked glass bottle with a small {Loc.Game("Boulder")} card inside of it.");

            // Zamar, Session 32 (applied Session 34). Key: Pliers.
            public static string PresentPliers => Loc.T("Leshy presents you a rusty pair of pliers.");

            // Zamar, Session 32 (applied Session 34). Key: Scissors.
            public static string PresentScissors => Loc.T("Leshy presents you a chipped pair of scissors.");

            // Zamar, Session 32 (applied Session 34). Key: Hourglass.
            public static string PresentHourglass => Loc.T("Leshy presents you an ornate wooden hourglass.");

            // Zamar, Session 32 (applied Session 34). Key: Magpie's Glass.
            public static string PresentMagpiesGlass => Loc.T("Leshy presents you a large magnifying glass.");

            // Zamar, Session 32 (applied Session 34). Key: Hoggy Bank.
            public static string PresentHoggyBank => Loc.T("Leshy presents you a ceramic piggy bank.");

            // Zamar, Session 32 (applied Session 34). Key: Fish Hook.
            public static string PresentFishHook => Loc.T("Leshy presents you a large curved fish hook.");

            // Zamar, Session 32 (applied Session 34). Key: Skinning Knife.
            public static string PresentSkinningKnife => Loc.T("Leshy presents you a razor sharp straight knife.");

            // Zamar, Session 32 (applied Session 34). Key: Failure.
            public static string PresentFailure => Loc.T("Leshy presents you a corked glass bottle with green goo with a misshapen face inside of it.");

            // "presents" is his change to Claude's 0.7.336 "offers".
            // Zamar, 0.7.359.
            public static string LeshyPresentsYouThe => Loc.T("Leshy presents you the {0}.");

            // The pack is full: the rat comes out with a card. {0} is the
            // card's name, read from the game. His words, Session 39.
            // Zamar, 0.7.423.
            // 0.7.424, his edit: the second "large" dropped, "Arrows to browse." added.
            public static string RatOffersCard => Loc.T("A large rat scampers from behind the backpack resting on the table. It is offering you a {0} card. Arrows to browse.");
        }

        /// <summary>The rulebook.</summary>
        public static class Rulebook
        {
            // Zamar, Session 11. His transcription of the page. Key: Reach.
            public static string CombinationCode => Loc.T("Combination code: 273.");

            // Zamar, Session 11. Written from the page itself. Key: Mirror.
            public static string ThisPageIsDestroyed => Loc.T("This page is destroyed by ink. You can make out the following words: Mirror... The Power and...Equals...the...bearing...");

            // Zamar, Session 11. Written from the page itself. Key: Bell.
            public static string ThisPageIsDestroyedBy => Loc.T("This page is destroyed by ink. You can make out the following words: Bell Ring...The value represented by this.....equal to....the....");

            // Zamar, Session 11. Written from the page itself. Key: CardsInHand.
            public static string ThisPageIsDestroyedByInk => Loc.T("This page is destroyed by ink. You can make out the following words: Card Count....The value represent....equal to....number of....");

            // Claude, 0.7.42 or earlier.
            public static string RulebookIsNotAvailable => Loc.T("The rulebook is not available here.");

            // Zamar, Session 16.
            public static string OpenedRulebookFromOrRulebookOpened(bool fromShelf)
                => fromShelf ? Loc.T("Opened Rulebook from shelf.") : Loc.T("Rulebook opened.");

            // Claude, 0.7.53-0.7.109.
            public static string LeftAndRightArrows(string lead, string page)
                => Loc.F($"{lead} {page} Left and right arrows turn pages. Backspace closes the book.");

            // Claude, 0.7.42 or earlier.
            public static string RulebookClosed => Loc.T("Rulebook closed.");

            // Claude, 0.7.347-0.7.357.
            public static string NameAfterName(string name, string afterName)
                => $"{name}, {afterName}";

            // Claude, 0.7.347-0.7.357.
            public static string NameAndDescription(string name, string clean)
                => $"{name}. {clean}";

            // Claude, 0.7.53-0.7.109.
            // Zamar, Session 16: the wrap-around clause went (it is a
            // property of the book, not a control), and the two closing
            // sentences became one.
            public static string RulebookLeftAndRight => Loc.T("Rulebook. Left and right arrows turn pages. Space repeats the current page. Backspace or R closes the book.");

            // Claude, 0.7.42 or earlier. Key: ABILITIES.
            public static string Ability => Loc.T("Ability");

            // Claude, 0.7.42 or earlier. Key: VARIABLE STATS.
            public static string VariableStat => Loc.T("Variable stat");

            // Claude, 0.7.42 or earlier. Key: BOONS.
            public static string Boon => Loc.T("Boon");

            // Claude, 0.7.42 or earlier. Key: ITEMS.
            public static string Item => Loc.T("Item");

            // Claude, 0.7.42 or earlier. Key: RULES.
            public static string Rule => Loc.T("Rule");

            // Claude, 0.7.42 or earlier.
            public static string PageUnavailable => Loc.T("Page unavailable.");

            // Claude, 0.7.42 or earlier.
            public static string BlankPageOf(int number, int count)
                => Loc.F($"Blank. Page {number} of {count}.");

            // Claude, 0.7.347-0.7.357.
            public static string Name(string name, string clean)
                => $"{name}. {clean}";

            // Claude, 0.7.42 or earlier.
            public static string DescriptionUnavailable(string name)
                => Loc.F($"{name}. Description unavailable.");

            // Claude, 0.7.42 or earlier.
            public static string BlankPage => Loc.T("Blank page.");

            // Claude, 0.7.42 or earlier.
            public static string Of(string singular, int ordinalByPage, int sectionTotal)
                => Loc.F($"{singular} {ordinalByPage} of {sectionTotal}.");

            // Claude, 0.7.42 or earlier.
            public static string PageOf(int number, int count)
                => Loc.F($"Page {number} of {count}.");

            // Claude, 0.7.42 or earlier.
            public static string DescriptionOnThisPage(string shownName)
                => Loc.F($"{shownName}. The description on this page is obscured by ink.");

            // Claude, 0.7.347-0.7.357.
            public static string Word(string name, string desc)
                => $"{name}. {desc}";

            // Claude, 0.7.347-0.7.357.
            public static string ComposeStatIcon(string name, string desc)
                => $"{name}. {desc}";

            // Claude, 0.7.42 or earlier.
            public static string ToTheUser(string desc)
                => Loc.T("To the user: ") + desc;

            // Claude, 0.7.347-0.7.357.
            public static string ComposeItem(string name, string desc)
                => $"{name}. {desc}";
        }

        /// <summary>Surrender offers.</summary>
        public static class Surrender
        {
            // Claude, 0.7.43-0.7.52.
            public static string YourOpponent => Loc.T("Your opponent");

            // 0.7.360: the key is now Shift+E, his choice.
            // Zamar, 0.7.43-0.7.52.
            public static string IsOfferingToSurrender(string who)
                => Loc.F($"{who} is offering to surrender, press Shift and E to accept. ");

            // Said after his concede conversation and after each draw while
            // the offer stands.
            // Zamar, 0.7.360.
            public static string OliveBranch => Loc.T("Leshy is offering an olive branch, he concedes the battle. Press Shift and E to accept his surrender.");

            // Claude, 0.7.43-0.7.52.
            public static string NoSurrenderIsBeing => Loc.T("No surrender is being offered.");

            // Zamar, 0.7.360.
            public static string ConcedeAccepted => Loc.T("Concede accepted. Victory.");
        }

        /// <summary>The run end screen.</summary>
        public static class RunEnd
        {
            // Claude, 0.7.53-0.7.109.
            public static string OutcomeLine(bool victory)
                => victory ? Loc.T("Victory") : Loc.T("Defeat");

            // Claude, 0.7.53-0.7.109.
            public static string IkmaCannotFindAn(string outcome, string stats)
                => $"{outcome}. {stats}" +
                   Loc.T("IKMA cannot find an option on this screen, so it still needs the mouse here.");

            // Claude, 0.7.347-0.7.357.
            public static string OutcomeStatsControls(string outcome, string stats)
                => $"{outcome}. {stats}{Vocabulary.RunEndControls()}";

            // Claude, 0.7.347-0.7.357.
            public static string OutcomeStatsLabelControls(string outcome, string stats, string label)
                => $"{outcome}. {stats}{label}. {Vocabulary.RunEndControls()}";

            // Claude, 0.7.53-0.7.109.
            public static string ThereIsNoOption => Loc.T("There is no option to select on this screen.");

            // Claude, 0.7.53-0.7.109.
            public static string RunEndControlsSpace(string shiftRHelp)
                => Loc.T("Run end controls. Space repeats the screen. Enter takes the option on it. ") + shiftRHelp;
        }

        /// <summary>Saving a bug report log.</summary>
        public static class LogExport
        {
            // Claude, 0.7.284-0.7.315.
            public static string LogFileCouldNot => Loc.T("the log file could not be found");

            // Claude, 0.7.284-0.7.315.
            public static string YourDesktopFolderCould => Loc.T("your desktop folder could not be found");

            // Claude, 0.7.284-0.7.315.
            public static string FileCouldNotBe => Loc.T("the file could not be written");
        }

        /// <summary>
        /// Naming the death card (Act 1 story). Session 32. Zamar's answers
        /// are in; the lines still marked PROVISIONAL are the ones he has not
        /// ruled on (listed in PROVISIONAL_LINES.md).
        /// </summary>
        public static class DeathCardChoice
        {
            // What each step takes from the chosen card, in the words Leshy's
            // own prompt uses ("cost", "power and health", "sigils"). The key
            // is the game's private ChoiceType name.
            // Game, Session 32.
            public static string Part(string step)
                => step == "Cost" ? Loc.T("cost")
                 : step == "Stats" ? Loc.T("power and health")
                 : step == "Abilities" ? Loc.T("sigils")
                 : step;

            // PROVISIONAL - asked Session 32. Said when the cards turn on, on
            // H, Space before browsing, and as the idle prompt.
            // Claude, Session 32.
            public static string Screen(string step, int count)
                => Loc.F($"Death card, choose its {Part(step)}. {CardCount(count)}. Arrow keys to browse, Enter to choose.");
        }

        public static class DeathCardName
        {
            // PROVISIONAL, question 1 (Zamar: "Unsure") - also the idle
            // prompt and the F1 help key.
            // Claude, Session 32.
            public static string Instructions => Loc.T("Type a name for your card, up to 10 characters. Enter to finish. Tab reads the name so far.");

            // Zamar, Session 32: "Say capitals, say space".
            public static string Typed(char c)
                => c == ' ' ? Loc.T("space")
                 : char.IsUpper(c) ? Loc.F($"capital {c}")
                 : c.ToString();

            // His answer: Just "backspace."
            // Zamar, Session 32.
            public static string Deleted(string removed, string nameNow)
                => Loc.T("backspace.");

            // Zamar, Session 32, on every keystroke the game drops.
            public static string Full(int max)
                => Loc.T("Character limit reached.");

            // Tab reads it (Zamar, Session 32). The words are PROVISIONAL.
            // Claude, Session 32.
            public static string NameSoFar(string name)
                => string.IsNullOrEmpty(name) ? Loc.T("No name yet.") : name;

            // PROVISIONAL - Enter on an empty name; not answered yet.
            // Claude, Session 32.
            public static string EmptyEnter => Loc.T("Type at least one character first.");

            // Zamar, Session 32: "[Name]".
            public static string Named(string name)
                => name;
        }

        // ==================================================================
        // THE MOD SETTINGS MENU (Session 37, M9). Every word here is Say the
        // Spire 2's own (Localization/eng/ui.json: SCREENS.MOD_SETTINGS,
        // SETTINGS.EVENTS_ROOT, TYPES.BUTTON, TYPES.CHECKBOX, CHECKBOX.*,
        // POSITIONS.LIST, SPEECH.CLOSED, EVENTS.COMMON.*), by Zamar's rule:
        // "I defer all of our starting design decisions to their
        // implementation." Their focus line is label, type, status, position,
        // joined with ", " (AnnouncementComposer), and a container's label
        // leads when focus crosses into it (FocusContext).
        // Two exceptions, both PROVISIONAL: "Add to history" (theirs is "Add
        // to buffer"; IKMA's buffer is its review history), and the event
        // names with no Say the Spire 2 event (EventSettings.cs).
        // ==================================================================
        public static class ModSettings
        {
            // Say the Spire 2, 0.7.412.
            public static string Title => Loc.T("Mod Settings");

            // Say the Spire 2, 0.7.412.
            public static string Events => Loc.T("Events");

            // Say the Spire 2, 0.7.412.
            public static string Button => Loc.T("button");

            // Say the Spire 2, 0.7.412.
            public static string Checkbox => Loc.T("checkbox");

            // Say the Spire 2, 0.7.412.
            public static string Checked => Loc.T("checked");

            // Say the Spire 2, 0.7.412.
            public static string Unchecked => Loc.T("unchecked");

            // Say the Spire 2, 0.7.412.
            public static string Position(int position, int total) => Loc.F($"{position} of {total}");

            // Say the Spire 2, 0.7.412.
            public static string Closed => Loc.T("Closed");

            // Say the Spire 2, 0.7.412.
            public static string Announce => Loc.T("Announce");

            // PROVISIONAL: theirs is "Add to buffer".
            // Claude, 0.7.412.
            public static string AddToHistory => Loc.T("Add to history");

            // Say the Spire 2, 0.7.412.
            public static string Sources => Loc.T("Sources");

            // Say the Spire 2, 0.7.412.
            public static string CurrentPlayer => Loc.T("Current Player");

            // Say the Spire 2, 0.7.412.
            public static string Enemies => Loc.T("Enemies");

            // ---- Session 38: History switches. Zamar, 0.7.419: names kept.
            public static string History => Loc.T("History");
            public static string HistoryReads => Loc.T("Reads");
            public static string HistoryPrompts => Loc.T("Prompts");

            // ---- Session 38: IKMA Manager's settings, in the game too
            // ---- (Zamar: "Exist in both"). Every word is the Manager's own
            // ---- (tools/installer/Program.cs and Settings.cs).
            public static string Updates => Loc.T("Updates");
            public static string UpdatesAutomatic => Loc.T("Automatic: new versions download and install with no question.");
            public static string UpdatesDownloadThenAsk => Loc.T("Download, then ask before installing.");
            public static string UpdatesAskFirst => Loc.T("Ask before downloading.");
            public static string UpdatesOff => Loc.T("Off: IKMA never connects to the internet.");

            public static string SpeechEngine => Loc.T("Speech engine");
            public static string SpeechAuto => Loc.T("Automatic: your screen reader on Windows, the IKMA speech helper on a Steam Deck.");
            public static string SpeechNvda => Loc.T("Always your screen reader, through UniversalSpeech: NVDA, JAWS, or Windows speech.");
            public static string SpeechLinux => Loc.T("Always the IKMA speech helper, for Steam Deck and Linux.");

            public static string Language => Loc.T("Language");
            public static string LanguageGame => Loc.T("Follow the game's language setting.");
            // The Manager's language names, in IkmaLanguage order after Game.
            // Names of languages, not translated.
            public static readonly string[] LanguageNames =
            {
                "English", "French, Français", "Italian, Italiano", "German, Deutsch",
                "Spanish, Español", "Brazilian Portuguese, Português do Brasil", "Turkish, Türkçe",
                "Russian, Русский", "Japanese, 日本語", "Korean, 한국어",
                "Simplified Chinese, 简体中文", "Traditional Chinese, 繁體中文",
            };

            public static string Vibration => Loc.T("Controller vibration");
            public static string Off => Loc.T("Off");
            public static string Low => Loc.T("Low");
            public static string Medium => Loc.T("Medium");
            public static string High => Loc.T("High");

            public static string FullLog => Loc.T("Full log for bug reports");
            public static string FullLogOff => Loc.T("Off: the short log, what IKMA said and anything that went wrong.");
            public static string FullLogOn => Loc.T("On: every diagnostic line, for a bug report. The log gets much longer.");

            // ---- Session 40: Controller buttons, the Manager's menu in the
            // ---- game too (Zamar: "Exist in both"). The title, the action
            // ---- names, "Default", "Swapped" and the reset line are IKMA
            // ---- Manager's own words (tools/installer/ControllerMap.cs and
            // ---- Program.cs) - Claude's drafts from Session 34, still
            // ---- PROVISIONAL there and so here. Keep the two in step.

            // IKMA Manager's. Claude, 0.7.427.
            public static string ControllerButtons => Loc.T("Controller buttons");

            // IKMA Manager's action names, by the action's name in the config
            // file (KeyIn.Defaults). PROVISIONAL.
            // Claude, 0.7.427.
            public static string ControllerActionName(string action)
            {
                switch (action)
                {
                    case "Rulebook":           return Loc.T("Open the rulebook");
                    case "Help":               return Loc.T("Help");
                    case "Repeat":             return Loc.T("Repeat, describe, or advance a conversation");
                    case "NextPlayable":       return Loc.T("Next playable card, slot, or sacrifice");
                    case "LastAbilitiesQuick": return Loc.T("Rulebook for the last heard abilities");
                    case "ReadHand":           return Loc.T("Read your hand");
                    case "ReadEnemyQueue":     return Loc.T("Read the enemy queue");
                    case "ReadEnemyBoard":     return Loc.T("Read the enemy board");
                    case "ReadYourBoard":      return Loc.T("Read your board");
                    case "GameInfo":           return Loc.T("Game info: scales, bones, and teeth");
                    case "FullBoard":          return Loc.T("Full board and queue");
                    case "AllItemSlots":       return Loc.T("All three item slots");
                    case "LastAbilities":      return Loc.T("Rulebook for the last heard abilities, second button");
                    case "ModSettings":        return Loc.T("Mod Settings");
                    case "HelpList":           return Loc.T("Help list");
                    case "DrawFromDeck":       return Loc.T("Draw from your deck");
                    case "DrawSquirrel":       return Loc.T("Draw from the Squirrel deck");
                    case "ItemMenu":           return Loc.T("Item menu");
                    case "AcceptSurrender":    return Loc.T("Accept surrender");
                    case "Number1":            return Loc.T("Number 1");
                    case "Number2":            return Loc.T("Number 2");
                    case "Number3":            return Loc.T("Number 3");
                    case "Number4":            return Loc.T("Number 4");
                    case "SaveLog":            return Loc.T("Save a bug report log");
                    case "HistoryOlder":       return Loc.T("Review history, older");
                    case "HistoryNewer":       return Loc.T("Review history, newer");
                    case "Silence":            return Loc.T("Silence speech");
                }
                return action;
            }

            // One action in the list: "Help: View". Say the Spire 2's shape
            // ("label: its bindings").
            // Say the Spire 2, 0.7.427.
            public static string ControllerAction(string name, string button) => Loc.F($"{name}: {button}");

            // The same once it is off its default. IKMA Manager's ("Default X.").
            // Claude, 0.7.427.
            public static string ControllerActionMoved(string name, string button, string defaultButton)
                => Loc.F($"{name}: {button}. Default {defaultButton}.");

            // An action a hand-edited file left with no button. PROVISIONAL.
            // Claude, 0.7.427.
            public static string ControllerNoButton => Loc.T("no button");

            // The last row. IKMA Manager's wording for its R choice. PROVISIONAL.
            // Claude, 0.7.427.
            public static string ControllerResetRow => Loc.T("Put every action back on its default button");

            // IKMA Manager's. Claude, 0.7.427.
            public static string ControllerResetDone => Loc.T("Every action is back on its default button.");

            // Enter on an action. The first sentence pair is theirs, word for
            // word (LISTEN.PRESS_BUTTON + LISTEN.CANCEL_HINT: "Press a
            // button... {key} to cancel.", the key being the button the
            // action already has).
            // Say the Spire 2, 0.7.427.
            public static string ControllerListen(string current) => Loc.F($"Press a button... {current} to cancel.");

            // What theirs has no words for: IKMA's chords, and the way out
            // for a player with no controller in hand. PROVISIONAL.
            // Claude, 0.7.427.
            public static string ControllerListenChords(string lb, string rb)
                => Loc.F($"Hold {lb}, {rb}, or both with it for a chord. Backspace on the keyboard also cancels.");

            // The same for an action with no button, so nothing to press to
            // cancel. PROVISIONAL.
            // Claude, 0.7.427.
            public static string ControllerListenNoButton(string lb, string rb)
                => Loc.F($"Press a button... Hold {lb}, {rb}, or both with it for a chord. Backspace on the keyboard cancels.");

            // A button the game uses on its own (A, B, the D-pad, Menu, RT)
            // pressed with no shoulder held. PROVISIONAL.
            // Claude, 0.7.427.
            public static string ControllerGameButton(string button, string lb, string rb)
                => Loc.F($"{button} is the game's own button. Hold {lb} or {rb} with it, or press another button.");

            // The right stick. PROVISIONAL.
            // Claude, 0.7.427.
            public static string ControllerRightStick => Loc.T("The right stick is the game's own. Press another button.");

            // Say the Spire 2, 0.7.427.
            public static string Cancelled => Loc.T("Cancelled");

            // Theirs: SPEECH.BOUND_TO.
            // Say the Spire 2, 0.7.427.
            public static string ControllerBound(string button) => Loc.F($"Bound to {button}");

            // A button another action has swaps the two (Zamar, Session 34;
            // theirs refuses instead). IKMA Manager's sentence. PROVISIONAL.
            // Claude, 0.7.427.
            public static string ControllerSwapped(string name, string button, string other, string otherButton)
                => Loc.F($"Swapped. {name} is now on {button}, and {other} is now on {otherButton}.");

            // After choosing a setting that is read only when the game starts
            // (Updates, Speech engine). Claude's draft, kept by Zamar.
            // Zamar, 0.7.420.
            public static string NextStart => Loc.T("Changes the next time you start the game.");

            // Language, while translations are not switched on
            // (Loc.LocalizationShipped). Zamar, Session 38.
            // Zamar, 0.7.420.
            public static string ComingSoon => Loc.T("Feature coming soon.");

            // H inside Mod Settings (Session 40). The keys are Zamar's Session
            // 38 calls; the sentence is PROVISIONAL.
            // Claude, 0.7.428.
            public static string Help => Loc.T("Mod Settings. Arrows to browse, Enter to open or change, Backspace to go back one level, Escape to close.");

            // Say the Spire 2, 0.7.412.
            public static string Join(params string[] parts) => string.Join(", ", parts);
        }

        // ==================================================================
        // WHY AN ITEM WAS REFUSED (Session 37, ItemRefusal.cs), spoken only
        // when Leshy's own hint is silent. Zamar: "Give the reason."
        // ==================================================================
        public static class ItemRefusals
        {
            // Zamar, Session 37: "Birdleg Fan already used this turn." (the game's item name)
            // Zamar, 0.7.417.
            public static string AlreadyUsedThisTurn(string item) => Loc.F($"{item} already used this turn.");

            // PROVISIONAL.
            // Claude, 0.7.417.
            public static string NoTargets(string item) => Loc.F($"{item} has no targets right now.");

            // PROVISIONAL.
            // Claude, 0.7.417.
            public static string DrawPhase(string item) => Loc.F($"{item} cannot be used during the draw phase.");

            // PROVISIONAL.
            // Claude, 0.7.417.
            public static string OutsideBattle(string item) => Loc.F($"{item} can only be used during a battle.");

            // PROVISIONAL.
            // Claude, 0.7.417.
            public static string DeckEmpty(string item) => Loc.F($"{item} cannot be used, your deck is empty.");

            // PROVISIONAL.
            // Claude, 0.7.417.
            public static string GiantCard(string item) => Loc.F($"{item} cannot be used while a giant card is on the board.");

            // PROVISIONAL: the shape of his bell line.
            // Claude, 0.7.417.
            public static string NotNow(string item) => Loc.F($"Now is not the time to use {item}.");
        }

        // ==================================================================
        // THE HELP LIST (Session 37, M9). The list's name is Say the Spire
        // 2's (CONTAINERS.HELP). The rows that work everywhere are
        // PROVISIONAL: their row shape "action, keys", IKMA Manager's action
        // names, IKMA's key spelling ("Shift plus Up arrow").
        // ==================================================================
        public static class HelpListWords
        {
            // Say the Spire 2, 0.7.412.
            public static string Help => Loc.T("Help");

            // PROVISIONAL.
            // Claude, 0.7.412.
            public static string[] Everywhere() => new[]
            {
                Loc.T("Help list, F1."),
                Loc.T("Mod Settings, Control plus M."),
                Loc.T("Review history, older, Control plus Down arrow."),
                Loc.T("Review history, newer, Control plus Up arrow."),
                Loc.T("Review history as a list, Y."),
                Loc.T("Silence speech, tap Control."),
            };
        }
    }
}
