// CardChoiceReader.cs
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The card reward after a battle. (Session 13, roadmap item 5.)
    ///
    /// Every encounter ends here, and until now the screen said nothing at all,
    /// so a run could not be played past its first fight without sight. First of
    /// the run-loop sequencers.
    ///
    /// WHAT THIS SCREEN IS. The game lays several cards out FACE DOWN. Turning
    /// one over is the choice — the others are then cleaned up. So the same key
    /// does two different things in sequence and the second is irreversible.
    ///
    /// THE PARITY RULE, which decides everything else here. A face-down card
    /// cannot be read by a sighted player, so IKMA does not read it either. It
    /// says the card is face down and nothing about what it is. (Session 16: it
    /// no longer gives the position either — that moved to M along with every
    /// other count in the mod, so three face-down cards now read alike and M is
    /// the only way to tell which one the cursor is on.) SelectableCard.Flipped (PUBLIC, confirmed in dump_card_choice.txt) is
    /// the gate. This is the ink-obscured rulebook page again — the test is
    /// never "can we reach the data", it is "can a sighted player see it". The
    /// suspense of the turn is part of the game, and handing a blind player the
    /// answer early would be taking the game away from them while calling it
    /// help.
    ///
    /// NOT SCENE-SEARCHED. The sequencer hands itself over from its own patch,
    /// the same way MenuReader receives a live AscensionMenuScreenTransition.
    /// </summary>
    public static class CardChoiceReader
    {
        private static ManualLogSource _log;

        private static CardChoicesSequencer _sequencer;

        // selectableCards is NONPUBLIC (confirmed), so it needs a cached
        // FieldInfo — the same pattern MenuReader uses for screenInteractables.
        private static FieldInfo _selectableCardsField;

        // THE LUCKY CLOVER. (Session 15, Zamar's request.)
        //
        // CardSingleChoicesSequencer.rerollInteractable is a NONPUBLIC
        // MainInputInteractable — confirmed in dump_card_choice.txt. Being a
        // MainInputInteractable is the whole answer: CursorSelectStart /
        // CursorSelectEnd already drives it, the same primitive as the cards
        // themselves, so it becomes one more stop on the arrow keys rather than
        // a special case.
        private static FieldInfo _rerollField;

        // The mushroom that appears under a card. NONPUBLIC members, both on
        // CardSingleChoicesSequencer:
        //   showMushrooms  Boolean   — whether this screen uses them at all
        //   DuplicateInDeck(SelectableCard) -> Boolean
        //
        // NOTHING IS SPOKEN FOR THIS YET, ON PURPOSE. Zamar described the
        // mushroom as marking a card that shares a kin type with his deck; the
        // question the game actually asks is named DuplicateInDeck. Those are
        // not the same claim, and announcing the wrong one would be a confident
        // description of something the player cannot check. So this build asks
        // the game's own question and LOGS the answer next to the card, which
        // settles the correspondence in one playtest — and the word he wants is
        // his to choose once it is settled.
        private static FieldInfo _showMushroomsField;
        private static MethodInfo _duplicateInDeckMethod;

        private static int _index = -1;
        private static bool _announced;
        private static float _settleFor;

        // THE CHOSEN CARD, WHILE THE GAME IS STILL DECIDING WHAT IT IS.
        // (0.7.247.) A cost or tribe node does not hand the map back when the
        // player picks a card — CardSingleChoicesSequencer then rolls a
        // CardInfo, turns the card face up and WAITS FOR A THIRD CLICK in
        // WaitForCardToBeTaken. Until 0.7.246 IKMA ended the screen at the
        // pick, so Enter fell through to the map while the node was still
        // running: the game object 'Nodes' was inactive, the click threw inside
        // Unity, and Zamar was told "Cannot travel there" for every path on the
        // board. That was the softlock, and it was IKMA letting go too early.
        private static SelectableCard _awaitingTake;
        private static bool _takeAnnounced;

        // The clover replaced the spread, so the screen is re-announcing itself
        // to a player who has not gone anywhere. Zamar, 0.7.246: "After using
        // Clover, the Card Choice blurb should not repeat."
        private static bool _redealt;

        // Session 16: the count has to hold still, not just the clock run out.
        // This screen never showed the bug because three cards spawn fast, but
        // the code was the same as the deck view's, which read a half-spawned
        // deck as "6 cards" and then "3 cards" in one run. A reroll respawns the
        // spread, so this is not purely theoretical here either.
        private static int _stableCount = -1;
        private static SelectableCard _hovered;

        // The screen spawns its cards inside the coroutine, so nothing exists to
        // count at patch time. Wait for the count to hold steady rather than
        // guessing a delay. Settle windows are free.
        private const float SETTLE_SECONDS = 0.45f;

        // Standing rule: any state where the game waits on player input needs an
        // audible prompt, at 5.5s then every 15s, with the full controls every
        // time. Someone who took their headphones off needs the keys, not a
        // reminder that they once heard them.
        // Zamar, 0.7.47: the opening line was still being spoken when the idle
        // prompt cut it off. That line is long by design — it describes the
        // whole screen — so the map's 5.5s does not fit here. Twenty seconds, and
        // the prompt is the SAME line rather than a shorter reminder, which is
        // the standing rule about stating the full controls every time.
        // Zamar, Session 16: "This line plays twice as often as it should."
        // The opening line is the longest in the mod — it describes the whole
        // screen — so hearing it every twenty seconds while thinking about three
        // cards is too much. First prompt still lands at twenty; the repeat is
        // forty.
        private const float IDLE_FIRST  = 20f;
        private const float IDLE_REPEAT = 40f;
        private static float _idleTimer;
        private static bool _idleSpokenOnce;
        private static bool _idlePending;

        public static void Init(ManualLogSource log) => _log = log;

        /// <summary>True while the reward screen owns the keyboard.</summary>
        public static bool Active => _sequencer != null;

        // ------------------------------------------------------------------
        // Handed the live sequencer by the patch. Nothing is announced yet:
        // CardSelectionSequence is a coroutine and the Prefix fires at
        // enumerator creation, when no cards exist.
        // ------------------------------------------------------------------
        internal static void Begin(CardChoicesSequencer sequencer)
        {
            _sequencer      = sequencer;
            _index          = -1;
            _announced      = false;
            _settleFor      = 0f;
            _hovered        = null;
            _awaitingTake   = null;
            _takeAnnounced  = false;
            _redealt        = false;
            ResetIdle();
            _log?.LogInfo("IKMA CHOICE: card selection started.");

            // Session 34: a card choice run INSIDE a node screen (the
            // Mycologists with no pairs) counts as that screen having shown
            // its parts, so the screen's own "it is over" release can fire
            // afterwards. Without it the reader held the controls on the map.
            try { NodeScreenReader.NoteInnerCardChoice(); } catch { }
        }

        internal static void End(string reason)
        {
            if (_sequencer == null) return;
            _log?.LogInfo($"IKMA CHOICE: card selection ended ({reason}).");
            _sequencer  = null;
            _index      = -1;
            _announced  = false;
            _hovered    = null;
            _lastFlipped = null;
            _awaitingTake  = null;
            _takeAnnounced = false;
            _redealt       = false;
        }

        private static List<SelectableCard> Cards()
        {
            var live = new List<SelectableCard>();
            if (_sequencer == null) return live;

            try
            {
                if (_selectableCardsField == null)
                {
                    _selectableCardsField = typeof(CardChoicesSequencer).GetField(
                        "selectableCards",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }

                var raw = _selectableCardsField?.GetValue(_sequencer) as List<SelectableCard>;
                if (raw == null) return live;

                foreach (var c in raw)
                    if (c != null) live.Add(c);
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CHOICE: selectableCards read failed: {e.Message}");
            }

            return live;
        }

        // ==================================================================
        // THE DECK TRIAL IS TWO SCREENS IN ONE NODE. (0.7.317.)
        //
        // DeckTrialSequencer is a SIBLING of CardSingleChoicesSequencer, not a
        // descendant — both derive from CardChoicesSequencer — and it has its
        // own entry point, DeckTrialSequence. So this reader was driving it
        // with every assumption the ordinary card choice makes, and two of
        // them were false.
        //
        // PHASE ONE, the trial cards. Three cards Initialize()d from a TEXTURE
        // with no CardInfo behind them. Nothing is added to the deck here — the
        // card picks which TRIAL is run against the deck. The ordinary opening
        // line said "Choose 1 to add to your deck", which is not what the
        // screen does.
        //
        // PHASE TWO, the reward. Only if the trial passes: three real cards,
        // startFlipped, and the ordinary card choice line is exactly right.
        //
        // WHICH PHASE IS ASKED OF THE GAME, not inferred from a counter. The
        // sequencer fills its private trialChoices list at the top of each
        // iteration and CLEARS it the moment a trial is chosen, so a live list
        // is the game's own answer to "are these trial cards".
        //
        // PARITY, AND IT IS THE WHOLE RISK ON THIS SCREEN. trialChoices names
        // every trial on the table, including the two the player has not
        // touched. It is read ONLY for a card whose back has already been
        // turned — the same click that paints the trial's picture onto the card
        // for a sighted player and makes Leshy read the trial out. An untouched
        // trial card is three identical backs to everyone, and stays "Face
        // down." here.
        // ==================================================================
        private static FieldInfo _trialChoicesField;

        /// <summary>
        /// The trials currently laid out, or an empty list when this is not a
        /// deck trial or the choice has already been made.
        /// </summary>
        private static List<DeckTrialSequencer.Trial> TrialChoices()
        {
            var none = new List<DeckTrialSequencer.Trial>();
            if (!(_sequencer is DeckTrialSequencer)) return none;

            try
            {
                if (_trialChoicesField == null)
                    _trialChoicesField = typeof(DeckTrialSequencer).GetField(
                        "trialChoices",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                var raw = _trialChoicesField?.GetValue(_sequencer)
                          as List<DeckTrialSequencer.Trial>;
                return raw ?? none;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CHOICE: trialChoices read failed: {e.Message}");
                return none;
            }
        }

        /// <summary>Is the screen currently offering TRIALS rather than cards?</summary>
        internal static bool InTrialSelection()
        {
            return TrialChoices().Count > 0;
        }

        /// <summary>
        /// The trial on one card, or null. Callers must already have
        /// established that this card's back has been turned — see the parity
        /// note above.
        /// </summary>
        private static DeckTrialSequencer.Trial TrialFor(SelectableCard card)
        {
            if (card == null) return null;

            var trials = TrialChoices();
            if (trials.Count == 0) return null;

            var cards = Cards();
            int i = -1;
            for (int n = 0; n < cards.Count; n++)
                if (ReferenceEquals(cards[n], card)) { i = n; break; }

            if (i < 0 || i >= trials.Count) return null;
            return trials[i];
        }

        /// <summary>
        /// The reroll interactable, if this screen has one and it is on screen.
        /// </summary>
        private static MainInputInteractable Reroll()
        {
            if (_sequencer == null) return null;

            // NOT EVERY CHOICE SCREEN HAS ONE. (0.7.317.) rerollInteractable is
            // declared on CardSingleChoicesSequencer, and the deck trial is a
            // DeckTrialSequencer — a SIBLING subclass of CardChoicesSequencer,
            // not a descendant of that one. Reading the field off it threw on
            // every browse and wrote twelve warnings into one screen's log.
            // Asked of the type, not caught after the fact.
            if (!(_sequencer is CardSingleChoicesSequencer)) return null;

            try
            {
                if (_rerollField == null)
                    _rerollField = typeof(CardSingleChoicesSequencer).GetField(
                        "rerollInteractable",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                var reroll = _rerollField?.GetValue(_sequencer) as MainInputInteractable;
                if (reroll == null) return null;

                // NO CLOVER MEANS NO CLOVER. (0.7.244.) Zamar: "If the Lucky
                // Clover Challenge is enabled skip saying the clover part here."
                //
                // The activeInHierarchy test below is not enough — his log has
                // the clover sentence spoken on a run with the challenge on, so
                // the object stays live and the game simply refuses the reroll.
                // Asked of the run's own challenge list, which is where that
                // decision actually lives.
                //
                // Gated HERE rather than in the opening line, so the clover
                // stops being a browse option as well. A key that reaches
                // something the run has taken away is the same defect as a
                // sentence about it.
                if (NoCloverChallengeActive()) return null;

                // Parity: if the clover is not on screen it is not an option,
                // and a run that has already rerolled loses it.
                return reroll.gameObject.activeInHierarchy ? reroll : null;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CHOICE: reroll read failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Is the No Clover challenge switched on for this run? Read from
        /// AscensionSaveData's own active list every call — a run's challenges
        /// do not change mid-run, but nothing here is worth caching for that.
        /// </summary>
        private static bool NoCloverChallengeActive()
        {
            try
            {
                var data = AscensionSaveData.Data;
                if (data?.activeChallenges == null) return false;
                return data.activeChallenges.Contains(AscensionChallenge.NoClover);
            }
            catch { return false; }
        }

        /// <summary>Cards, plus the clover if it is there. The browse list.</summary>
        private static int OptionCount(List<SelectableCard> cards)
            => cards.Count + (Reroll() != null ? 1 : 0);

        /// <summary>
        /// Is a Lucky Clover actually on the table? Asked so the help text does
        /// not make the player carry a conditional IKMA can answer.
        /// </summary>
        public static bool CloverAvailable => Reroll() != null;

        /// <summary>
        /// "3 cards" — the whole spread, however many of them are still turned
        /// over. The opening line counts FACE-DOWN cards because that is what is
        /// in front of the player at that moment; H is asked at any point and
        /// describes the screen, so it counts the lot.
        /// </summary>
        public static string CardCountWord
        {
            get
            {
                int n = Cards().Count;
                return Vocabulary.CardCount(n);
            }
        }

        /// <summary>
        /// What the back of a face-down choice card is showing, as the WHOLE
        /// line — or "" for a plain back, which is what a Random node deals.
        /// Never the card itself.
        /// </summary>
        /// <remarks>
        /// 0.7.248 — IT IS NOT "FACE DOWN" ANY MORE ONCE IT IS TURNED.
        ///
        /// Zamar: "The cost shouldnt read face down. It should just say '2 blood
        /// cost card.' or '3 blood cost card.' etc."
        ///
        /// He is describing what is actually on the table. A turned back is not
        /// a blank — it is a card whose PRICE is showing and whose name is not,
        /// and "Face down. Costs 2 blood." led with the one thing about it that
        /// had stopped being the point. So this returns the sentence rather than
        /// a clause hung off one.
        /// </remarks>
        private static string CardbackLine(SelectableCard card)
        {
            CardChoice choice = null;
            try { choice = card?.ChoiceInfo; } catch { }
            if (choice == null) return "";

            try
            {
                if (choice.isDeathcardChoice) return Vocabulary.CardChoices.DeathCard;

                if (choice.resourceType == ResourceType.Blood)
                {
                    int n = choice.resourceAmount;
                    return Vocabulary.CardChoices.BloodCostCard(n);
                }

                // THE BONE BACK CARRIES NO NUMBER. GetCardbackTexture asks for
                // card_rewardback_bones with no amount in the name, so the back
                // says "bones" and nothing more — and so does this. Reading
                // resourceAmount here would be telling a blind player something
                // the picture does not say.
                if (choice.resourceType == ResourceType.Bone) return Vocabulary.CardChoices.BonesCostCard;

                // The tribe back shows that kin's symbol. "Kin type:" is the
                // label CardReader already uses everywhere a tribe is spoken,
                // so the word means the same thing on both screens.
                if (choice.tribe != Tribe.None)
                    return Vocabulary.CardChoices.KinCard(choice.tribe);
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CHOICE: reading the cardback threw {e.GetType().Name}.");
            }

            return "";
        }

        private static bool IsRerollIndex(List<SelectableCard> cards, int index)
            => index == cards.Count && Reroll() != null;

        /// <summary>
        /// Does the game consider this card a duplicate of one already in the
        /// deck? Asked of the game, never reimplemented — the same rule as
        /// CanAttackDirectly and CanActivate.
        /// </summary>
        private static bool? DuplicateInDeck(SelectableCard card)
        {
            if (_sequencer == null || card == null) return null;
            try
            {
                if (_duplicateInDeckMethod == null)
                    _duplicateInDeckMethod = typeof(CardSingleChoicesSequencer).GetMethod(
                        "DuplicateInDeck",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (_duplicateInDeckMethod == null) return null;
                return (bool)_duplicateInDeckMethod.Invoke(_sequencer, new object[] { card });
            }
            catch { return null; }
        }

        private static bool? ShowMushrooms()
        {
            if (_sequencer == null) return null;
            try
            {
                if (_showMushroomsField == null)
                    _showMushroomsField = typeof(CardSingleChoicesSequencer).GetField(
                        "showMushrooms",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (_showMushroomsField == null) return null;
                return (bool)_showMushroomsField.GetValue(_sequencer);
            }
            catch { return null; }
        }

        /// <summary>
        /// Log-only, one line per card. This is what settles whether the
        /// mushroom means "duplicate" or "shares a kin type" before a word is
        /// spoken about it.
        /// </summary>
        private static void LogMushrooms(List<SelectableCard> cards)
        {
            bool? show = ShowMushrooms();
            _log?.LogInfo($"IKMA CHOICE MUSHROOM: showMushrooms={(show.HasValue ? show.ToString() : "unreadable")}.");

            for (int i = 0; i < cards.Count; i++)
            {
                // 0.7.200 — THE PARITY RULE APPLIES TO THE LOG TOO.
                //
                // Zamar, boss reward chest: "don't reveal the card names in our
                // log until flipped up. I havent flipped them in game yet but
                // can see in the log it's Mole Man, Pack Rat and Geck."
                //
                // This line had the Flipped sense BACKWARDS — the exact defect
                // the comments in OpeningLine and Describe were written to stop
                // recurring. Flipped is TRUE while the card is FACE DOWN, so
                // `if (Flipped) name = <the real name>` printed the name of
                // every card the player could not see, and only those.
                //
                // THE LOG IS A PLAYER-FACING ARTIFACT. He reads it back with a
                // screen reader, which makes it part of the game's presentation,
                // not a debugging side channel. A face-down card is face down in
                // the log as well.
                string name = "face down";
                // FaceDown, not Flipped — 0.7.247. See DescribeCard.
                try { if (!cards[i].FaceDown) name = CardReader.CardName(cards[i].ChoiceInfo?.CardInfo) ?? "?"; }
                catch { }

                bool? dup = DuplicateInDeck(cards[i]);
                _log?.LogInfo(
                    $"IKMA CHOICE MUSHROOM: card {i + 1} ({name}) " +
                    $"DuplicateInDeck={(dup.HasValue ? dup.ToString() : "unreadable")}.");
            }
        }

        /// <summary>
        /// True once the screen has laid its cards out and has not been
        /// announced. Driven from HotkeyManager.Update, which supplies the
        /// elapsed time — same shape as MenuReader.NeedsScreenAnnouncement.
        /// </summary>
        public static bool NeedsAnnouncement(float deltaTime)
        {
            if (_sequencer == null || _announced) return false;

            int count = Cards().Count;
            if (count == 0)
            {
                _settleFor   = 0f;
                _stableCount = -1;
                return false;
            }

            if (count != _stableCount)
            {
                _stableCount = count;
                _settleFor   = 0f;
                return false;
            }

            _settleFor += deltaTime;
            return _settleFor >= SETTLE_SECONDS;
        }

        public static void Announce()
        {
            var cards = Cards();
            if (cards.Count == 0) return;

            _announced = true;

            // THE CURSOR STARTS NOWHERE. (0.7.245, his standing rule.) Browse
            // already resolves -1 to the first card on the first press, so this
            // is the whole change: arrival stops claiming a position.
            _index     = -1;
            ResetIdle();

            RememberCardSet(cards);
            LogMushrooms(cards);

            // A RE-DEAL IS NOT AN ARRIVAL. (0.7.247.)
            //
            // Zamar: "After using Clover, the Card Choice blurb should not
            // repeat." He has not left the screen and does not need its name,
            // its description or its controls a second time — but the cards DID
            // change underneath him, and silence would leave him unsure whether
            // the clover had done anything at all.
            //
            // Only the clover sets this. Any other mid-screen replacement still
            // gets the full line, because IKMA does not know what happened and
            // the player may not either.
            if (_redealt)
            {
                _redealt = false;
                // IKMA PROVISIONAL — Zamar asked for the blurb to stop, not for
                // this line. It exists so the clover has an audible result.
                Speech.Browse(Vocabulary.CardChoices.NewCardsDealt);
            }
            else
            {
                Speech.Browse(OpeningLine(cards));
            }

            Hover(cards, 0);
        }

        /// <summary>
        /// What the screen is, in one line. Spoken on arrival and repeated
        /// verbatim by the idle prompt — Zamar's rule that a repeat states the
        /// full controls every time, because someone who took their headphones
        /// off needs the keys rather than a reminder that they once heard them.
        ///
        /// No card is described here. They are all face down, and the opening
        /// line says what is in front of the player rather than reading them one
        /// of the things they cannot see.
        /// </summary>
        private static string OpeningLine(List<SelectableCard> cards, bool idle = false)
        {
            // THE COUNT IS OF CARDS STILL FACE DOWN, not of cards on the table.
            // (Session 16.) Zamar: "the 3 of facedown cards should be dynamic.
            // It said 3 face down when I had already flipped one."
            //
            // This line is spoken on arrival AND repeated verbatim by M and by
            // the idle prompt, so after a flip it was describing a table that no
            // longer existed. Flipped is TRUE while a card is face down — the
            // sense that was backwards in this file for six sessions, spelled
            // out everywhere it is read.
            int faceDown = 0;
            foreach (var c in cards)
            {
                // FaceDown, not Flipped — 0.7.247. See DescribeCard: on a cost
                // or tribe node Flipped goes false while the card is still
                // face down, so this counted three face-down cards as none.
                try { if (c.FaceDown) faceDown++; }
                catch { }
            }


            // NO COUNT OF CLOVERS. He asked for "[x] Lucky Clover(s)", and
            // nothing in any dump holds that number — the sequencer has a single
            // rerollInteractable and a choicesRerolled bool, which answers "is a
            // reroll available here" and nothing more. A number would be
            // invented, so the line states what is actually known.
            //
            // "Discard them all and for a new selection" is his wording, and the
            // reason for it is that the clover sweeps the whole spread rather
            // than the card under the cursor.
            // Zamar's wording, Session 16.
            string cloverPart = Reroll() != null
                ? Vocabulary.CardChoices.UseTheLuckyClover
                : "";

            // Once every card is face up the opening clause would read "Before
            // you lay 0 face-down cards", which is true and useless. The table
            // is then described by what is on it rather than by what is hidden.
            // 0.7.200 — "This should be called a Rare card choice, not just a
            // card choice." (Zamar, at the boss reward chest.)
            //
            // It is a different screen with different stakes: one rare card, out
            // of a chest, once per boss. Asked of the sequencer rather than of
            // the cards, because rarity is a property of the OFFER and the cards
            // are face down — reading it off a card would both be wrong and
            // break parity.
            string lead = LeadClause(faceDown, idle);

            // Session 34, Zamar: at the Mycologists the game offers neither the
            // rulebook nor the deck view (opening the deck softlocked him), so
            // neither is named here.
            if (NodeScreenReader.MycologistsActive)
                return Vocabulary.CardChoices.OpeningLineNoDeck(lead, cloverPart);

            return Vocabulary.CardChoices.OpeningLine(lead, cloverPart);
        }

        /// <summary>
        /// The screen named and described, in the words Zamar approved — the
        /// one sentence that opens this screen, wherever it is spoken from.
        ///
        /// ONE SOURCE, because 0.7.205 proved two was one too many. H on the
        /// boss reward chest answered with the ORDINARY card choice line:
        /// "Card Choice. Before you lay 3 cards." — a different screen name and
        /// a different description of the same table, on the one screen where
        /// the distinction is the point. Zamar: "H key should have done the
        /// rare card line not the normal one."
        ///
        /// The help handler in HotkeyManager had its own copy of this sentence
        /// and that copy had never learned about rare choices. It now calls
        /// HelpLead, so a wording change lands in both places or in neither.
        /// This is the same defect SPOKEN_STRINGS.md counts 34 more of.
        ///
        /// 0.7.200's rule still holds: rarity is a property of the OFFER, so it
        /// is asked of the sequencer, never of the cards — they are face down.
        /// </summary>
        private static string LeadClause(int faceDown, bool idle = false)
        {
            // ZAMAR'S WORDING, 2026-09-22, verbatim, and it replaces the
            // whole sentence rather than the screen name — nothing is added to
            // the deck in this phase, so "Choose 1 to add to your deck" was a
            // false claim about what the player is doing.
            //
            // He set the level deliberately: "We dont need to explain this one
            // too much since Leshy does a lot of it." Leshy reads the trial's
            // own rule aloud on every flip, so this line describes the table
            // and stops.
            //
            // The count is not in it, so unlike the other two leads this one
            // does not change as cards are turned. The controls clause is
            // appended by the caller exactly as before.
            //
            // Session 40, Zamar, of the stone arch sentence: "this should not
            // be repeated in the idle prompt." The idle prompt names the
            // screen and gives the keys; the arrival line still describes
            // the table.
            if (InTrialSelection())
                return idle ? Vocabulary.CardChoices.DeckTrialTitle
                            : Vocabulary.CardChoices.DeckTrialLeshyPuts;

            string what = Vocabulary.CardChoices.RareCardChoiceOrCardChoice(_sequencer is RareCardChoicesSequencer);

            string cardWord = Vocabulary.CardChoices.FaceDownCardCount(faceDown);

            // Session 34, Zamar: the Mycologists' card choice (no pairs in
            // your deck) is its own screen, titled like the Rare one, and says
            // what the cards are. The game builds them in DuplicateMergeSequencer
            // .GetDuplicateCardChoices: fresh copies of cards in your deck. The
            // face-down count stays, as on the ordinary screen.
            if (NodeScreenReader.MycologistsActive)
                return Vocabulary.CardChoices.MycologistCardSelection(faceDown, cardWord);

            // ZAMAR'S WORDING, 2026-09-13, verbatim:
            //
            //   "Rare Card Choice. Leshy presents you a wooden box. He pops a
            //    lock and opens it, revealing three face-down cards..." then
            //    the rest.
            //
            // "Ornate" was in the first draft and he cut it at 0.7.205:
            // "remove the word ornate. Distracting and not true."
            //
            // The count stays dynamic — Session 16 established that this line
            // is repeated by Space and by the idle prompt, so after a flip it
            // has to describe the table as it is now.
            if (_sequencer is RareCardChoicesSequencer)
            {
                // 0.7.266, his correction: "change to 'revealing 3 face-down
                // rare cards...'". The word belongs in the count and not only
                // in the screen name — the screen name is said once on arrival
                // and this sentence is also what Space and the idle prompt
                // repeat, so without it a player who came back to the line
                // mid-screen is told only that there are cards in a box.
                //
                // Rarity is still asked of the SEQUENCER and never of the
                // cards, which are face down. 0.7.200's rule, unchanged.
                string rareWord = Vocabulary.CardChoices.FaceDownRareCardCount(faceDown);

                return Vocabulary.CardChoices.LeshyPresentsYouOrEveryCardIs(faceDown, what, rareWord);
            }

            return Vocabulary.CardChoices.BeforeYouLayOrEveryCardIs(faceDown, what, cardWord);
        }

        /// <summary>
        /// The same opening sentence, composed from the table as it stands, for
        /// the H help to lead with.
        ///
        /// Zamar, Session 16: "Make the opening of the H key the same as the
        /// intro one." Two different descriptions of one screen make a player
        /// who pressed H wonder whether they are somewhere else.
        /// </summary>
        internal static string HelpLead
        {
            get
            {
                var cards = Cards();
                int faceDown = 0;
                foreach (var c in cards)
                {
                    // FaceDown, not Flipped — 0.7.247. See DescribeCard.
                    try { if (c.FaceDown) faceDown++; }
                    catch { }
                }
                return LeadClause(faceDown);
            }
        }

        // ------------------------------------------------------------------
        // What one card may be said to be. Face down: that it is face down, and
        // nothing else — Session 16 took the position off browse everywhere.
        // ------------------------------------------------------------------
        private static string Describe(List<SelectableCard> cards, int index)
        {
            // The clover sits after the cards, so it is one more stop on the
            // arrows rather than a key the player has to know about. Zamar's
            // words for it — it is what the screen shows.
            // ZAMAR, 0.7.47: "they're cards, cards aren't re-rolled, that's
            // dice. I don't want to use mechanically re-rolling as our in game
            // descriptions, I want to always keep the theme over the mechanics."
            //
            // Applies to every line in the mod, not just this one. Describe what
            // happens at the table; never name the system underneath it.
            //
            // Position goes LAST here, unlike the cards, because the clover's
            // name and what it does are the part being scanned for and its
            // number in the list is orientation.
            // Session 16: no position on browse, here or anywhere. Zamar's
            // general rule.
            if (IsRerollIndex(cards, index))
                return Vocabulary.CardChoices.LuckyCloverDiscardAll;

            if (index < 0 || index >= cards.Count) return "";
            return DescribeCard(cards[index]);
        }

        /// <summary>
        /// One card, said as the table shows it. Split out of Describe in
        /// 0.7.247 so the flip callback and the take prompt read the card
        /// through the same sentence rather than each keeping their own.
        /// </summary>
        internal static string DescribeCard(SelectableCard card, bool noteDuplicate = true)
        {
            if (card == null) return "";

            // ==================================================================
            // FaceDown IS THE FLAG. Flipped IS NOT. (0.7.247.)
            //
            // Zamar's 0.7.246 log, on a Cost node: "Face up, but the card could
            // not be read." five times, with a Flying Ant lying face up in the
            // screenshot — except it was not face up, and the card he was
            // hearing about was not the Flying Ant.
            //
            // Card and SelectableCard carry TWO different bits and this reader
            // had been treating them as one:
            //
            //   Card.FaceDown             is the card face down on the table
            //   SelectableCard.Flipped    is there still a click owed on it
            //
            // On a Random node they move together — OnCursorSelectStart takes
            // the branch that does `Flipped = false; SetFaceDown(false);` — which
            // is why reading Flipped worked on every screen this was tested on.
            //
            // On a COST or TRIBE node they come apart. Initialize(Texture, ...)
            // stores the reward-back art and the first click takes the OTHER
            // branch: FlipCardbackTexture swaps the back to the blood or kin
            // picture and sets `Flipped = false` — and never touches FaceDown.
            // The card is still face down. It has no CardInfo and will not have
            // one until the player picks it. So IKMA read Flipped == false as
            // "face up", went looking for a name, found none, and said the card
            // could not be read — of a card that was lying face down exactly as
            // the game intended.
            //
            // FaceDown answers the question actually being asked: is there a
            // card here to read. Flipped now answers only the narrower one below.
            // ==================================================================
            bool faceDown = true;
            try { faceDown = card.FaceDown; } catch { }

            // THE BACK OF THE CARD IS NOT BLANK — ONCE IT HAS BEEN TURNED OVER.
            // (0.7.245, corrected 0.7.247.)
            //
            // Zamar: "All the card choice costs, (and kins, etc, etc) need to
            // also be read. Got nothing read for cost here."
            //
            // The old comment here said a face-down card "has no property a
            // sighted player can see either" — and on a Random node that is
            // true. On a Cost node it is not: CardSingleChoicesSequencer calls
            // GetCardbackTexture and prints card_rewardback_2blood, _3blood or
            // _bones on the back, and on a Tribe node it prints the tribe's
            // symbol. His screenshot is three backs showing two blood, three
            // blood and a bone. That is the whole basis of choosing, and IKMA
            // was saying "Face down" three times.
            //
            // SelectableCard.ChoiceInfo is PUBLIC and holds exactly what the
            // back was drawn from — resourceType and resourceAmount, or tribe,
            // or the deathcard flag. So this reads the picture, not the card
            // underneath it: the name and stats stay hidden, as they are for a
            // sighted player.
            //
            // AND THE COST IS NOT ON IT UNTIL THE PLAYER TURNS IT. Nothing sets
            // a cardback at spawn — SpawnCards leaves the default on, and
            // GetCardbackTexture's blood or kin art is painted on by
            // FlipCardbackTexture, inside the first click. So three untouched
            // backs look identical to a sighted player, and 0.7.246 was reading
            // all three costs off ChoiceInfo before he had touched any of them.
            // Flipped is exactly the bit that says whether that click has
            // happened, which is the one question it is still good for.
            if (faceDown)
            {
                bool backTurned = false;
                try { backTurned = !card.Flipped; } catch { }
                if (!backTurned) return Vocabulary.CardChoices.FaceDown;

                // A TURNED TRIAL CARD IS NOT A BLANK BACK. (0.7.317.)
                //
                // Trial cards carry no ChoiceInfo, so CardbackLine has nothing
                // to read and every one of them came back "Face down." — of a
                // card the player had turned over, that is showing its trial's
                // picture, and that Leshy had just read out. Three of them read
                // identically with no way to tell them apart again.
                //
                // THE NAME IS THE GAME'S OWN. GetTypeStringLocalized is what
                // the game prints in "Let the Trial of Blood begin", and the
                // sentence shape is the game's too. Logged PROVISIONAL because
                // the sentence is still Zamar's to set.
                var trial = TrialFor(card);
                if (trial != null)
                {
                    string typeName = null;
                    try { typeName = trial.GetTypeStringLocalized(); } catch { }

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        // Session 40, Zamar: "Hovering should restate the
                        // conditions." The number is the game's own
                        // (Trial.threshold, a public field) - the one Leshy
                        // read out when this card was turned. Only a turned
                        // card reaches here, so nothing is said that the
                        // table has not already shown.
                        string condition = null;
                        try { condition = Vocabulary.CardChoices.TrialCondition(trial.type, trial.threshold); } catch { }
                        return string.IsNullOrEmpty(condition)
                            ? Vocabulary.CardChoices.TrialOf(typeName)
                            : Vocabulary.CardChoices.TrialOf(typeName) + " " + condition;
                    }

                    _log?.LogInfo("IKMA CHOICE: a turned trial card has no readable type name.");
                }

                string back = CardbackLine(card);
                // A Random node's back stays blank however many times it is
                // clicked, so there is nothing to say about it but the one thing.
                return Vocabulary.CardChoices.FaceDownName(string.IsNullOrEmpty(back), back);
            }

            // A COST NODE'S CardChoice HAS NO CardInfo. (0.7.246.)
            //
            // His 0.7.245 log: "Face up, but the card could not be read." five
            // times in a row on a Cost node, and Enter therefore looked dead —
            // he turned each card over and heard nothing about it.
            //
            // CardSingleChoicesSequencer branches on it:
            //
            //   if (cardChoice.CardInfo != null)          card.Initialize(cardChoice.CardInfo, ...)
            //   else if (resourceType != None)            card.Initialize(GetCardbackTexture(...), ...)
            //
            // A cost or tribe node takes the SECOND branch: the CardChoice
            // carries a resource or a tribe and NO card, because which card it
            // is has not been decided yet. It lands on Card.Info when the game
            // assigns it. Only an override choice — a Deathcard node, a scripted
            // spread — fills CardChoice.CardInfo, which is why every screen this
            // reader had been tested on happened to work.
            //
            // Card.Info first, because it is the card that is actually lying
            // there face up. ChoiceInfo stays as the fallback so nothing that
            // worked before can stop working.
            CardInfo info = null;
            try { info = card.Info; } catch { }
            if (info == null)
                try { info = card.ChoiceInfo?.CardInfo; } catch { }

            string described = CardReader.DescribeCardInfo(
                info, true, noteDuplicate ? MushroomNote(card) : "");
            if (described == null) return Vocabulary.CardChoices.FaceUpButThe;

            return described;
        }

        /// <summary>
        /// The option AND where it sits in the spread. M only, and it is now the
        /// only way to tell three face-down cards apart. Position at the END,
        /// the settled rule for counts.
        /// </summary>
        private static string DescribeWithPosition(List<SelectableCard> cards, int index)
        {
            string body = Describe(cards, index);
            if (string.IsNullOrEmpty(body)) return "";
            return Vocabulary.OptionOf(body, index + 1, OptionCount(cards));
        }

        /// <summary>
        /// " Duplicate of card in your deck." for a card the screen has put a
        /// mushroom under, or "" for everything else. Zamar's wording.
        ///
        /// CONFIRMED IN HIS 0.7.45 LOG rather than assumed. He described the
        /// mushroom as a kin-type marker; the question the game asks is
        /// DuplicateInDeck, and the run bore the game out — Black Goat, the one
        /// card already in his deck, was the one that came back True. Asked of
        /// the game every time, never cached: a reroll replaces the cards.
        ///
        /// Gated on showMushrooms as well, because a screen that draws no
        /// mushrooms is showing the player nothing, and IKMA does not report
        /// what the page hides from everyone.
        /// </summary>
        private static string MushroomNote(SelectableCard card)
        {
            if (ShowMushrooms() != true) return "";
            return DuplicateInDeck(card) == true ? Vocabulary.CardChoices.DuplicateOfCardIn : "";
        }

        // ------------------------------------------------------------------
        // Move the game's own cursor, so the screen matches the narration. Same
        // reason as HoverTargetSlot: blind players stream to sighted audiences,
        // and the raised card must be the one being read out.
        //
        // SelectableCard derives from Card -> MainInputInteractable, so
        // CursorEnter/CursorExit are public and inherited (confirmed).
        // ------------------------------------------------------------------
        private static void Hover(List<SelectableCard> cards, int index)
        {
            if (IsRerollIndex(cards, index))
            {
                try
                {
                    if (_hovered != null) { _hovered.CursorExit(); _hovered = null; }
                    Reroll()?.CursorEnter();
                }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA CHOICE: clover hover failed: {e.Message}");
                }
                return;
            }

            if (index < 0 || index >= cards.Count) return;
            var entering = cards[index];

            try
            {
                if (_hovered != null && _hovered != entering) _hovered.CursorExit();
                if (entering != null) entering.CursorEnter();
                _hovered = entering;
            }
            catch (System.Exception e)
            {
                // Presentation must never break the choice itself.
                _log?.LogWarning(
                    $"IKMA CHOICE: hover sync failed: {e.GetType().Name}: {e.Message}");
            }
        }

        public static void Browse(int direction)
        {
            // Nothing to browse while one card is lying face up waiting to be
            // taken — the other two are already off the table. An arrow re-reads
            // the one card there is, rather than answering nothing.
            if (_awaitingTake != null) { ResetIdle(); Speech.Browse(TakeLine()); return; }

            var cards = Cards();
            if (cards.Count == 0) return;

            ResetIdle();
            int count = OptionCount(cards);
            _index = _index < 0 ? 0 : (_index + direction + count) % count;

            Hover(cards, _index);
            Speech.Browse(Describe(cards, _index));
        }

        public static void AnnounceCurrent()
        {
            if (_awaitingTake != null) { ResetIdle(); Speech.Browse(TakeLine()); return; }

            var cards = Cards();
            if (cards.Count == 0) return;

            // SPACE AT NOWHERE NAMES THE SCREEN. (0.7.266.)
            //
            // Zamar: "space during the rare card choice before browsing didnt
            // read anything. Should just repeat Rare card choice, arrow keys to
            // browse."
            //
            // The cursor starts at NOWHERE mod-wide (0.7.245) so that the first
            // arrow lands ON option one, and the standing rule that came with it
            // is that Space there describes the SCREEN rather than option one.
            // This reader took the first half of that rule and not the second:
            // `_index < 0` returned without a word, which is a dead key — the
            // one failure this project treats as unacceptable.
            //
            // His sentence, not the full arrival line: the arrival line is long
            // and he asked for the short form here.
            if (_index < 0)
            {
                ResetIdle();
                string what = Vocabulary.CardChoices.RareCardChoiceOrCardChoice(_sequencer is RareCardChoicesSequencer);
                _log?.LogInfo($"IKMA CHOICE: Space at NOWHERE — naming the screen ({what}).");
                Speech.Browse(Vocabulary.CardChoices.ArrowKeysToBrowse(what));
                return;
            }

            ResetIdle();
            Speech.Browse(DescribeWithPosition(cards, _index));
        }

        // ------------------------------------------------------------------
        // Enter. Click the card through the game's own primitive and let the
        // game decide what that means — turning it over the first time, taking
        // it the second. IKMA does not pre-judge which and announces nothing
        // here: OnFlipped and OnChosen report what actually happened.
        // ------------------------------------------------------------------
        public static void Select()
        {
            // The third click on a cost or tribe node: take the card the game is
            // holding out. Same primitive as everywhere else — the game's own
            // CursorSelectEnded is what WaitForCardToBeTaken is listening for.
            if (_awaitingTake != null)
            {
                var reward = _awaitingTake;
                try
                {
                    reward.CursorEnter();
                    reward.CursorSelectStart();
                    reward.CursorSelectEnd();
                    _log?.LogInfo("IKMA CHOICE: reward taken via CursorSelectStart/End.");
                }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA CHOICE: take failed: {e.Message}");
                    Speech.Browse(Vocabulary.CardChoices.ThatCardCouldNot);
                }
                return;
            }

            var cards = Cards();

            if (IsRerollIndex(cards, _index))
            {
                ResetIdle();
                var clover = Reroll();
                try
                {
                    clover.CursorEnter();
                    clover.CursorSelectStart();
                    clover.CursorSelectEnd();
                    _log?.LogInfo("IKMA CHOICE: Lucky Clover clicked via CursorSelectStart/End.");

                    // Nothing announced here. The reroll replaces the cards and
                    // the screen re-announces itself; a line from IKMA would be
                    // describing an intention rather than a result.
                    _announced = false;
                    _settleFor = 0f;
                    _index     = 0;
                    _redealt   = true;
                }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA CHOICE: clover click failed: {e.Message}");
                    Speech.Browse(Vocabulary.CardChoices.LuckyCloverCouldNot);
                }
                return;
            }

            if (cards.Count == 0 || _index >= cards.Count)
            {
                Speech.Browse(Vocabulary.NoCardSelected);
                return;
            }

            // Enter straight off the arrival line turns over the first card,
            // the same clamp every other reader uses for a cursor at NOWHERE.
            if (_index < 0) _index = 0;

            ResetIdle();

            // CAPTURED BEFORE THE CLICK. (0.7.246.) CursorSelectEnd can choose
            // the reward, which calls End(), which resets _index to -1 — so the
            // log line below used to read a field the callback had already
            // cleared and printed "card 0 clicked". Nothing was wrong with the
            // click; the record of it was written after the fact.
            int clicked = _index;
            var card = cards[clicked];

            try
            {
                card.CursorEnter();
                card.CursorSelectStart();
                card.CursorSelectEnd();
                _log?.LogInfo($"IKMA CHOICE: card {clicked + 1} clicked via CursorSelectStart/End.");
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CHOICE: click failed: {e.Message}");
                Speech.Browse(Vocabulary.CardChoices.ThatCardCouldNotBe);
            }
        }

        // ------------------------------------------------------------------
        // The game turned a card over. Now it is face up, a sighted player can
        // read it, and so may we.
        // ------------------------------------------------------------------
        private static SelectableCard _lastFlipped;

        internal static void OnFlipped(SelectableCard card)
        {
            if (_sequencer == null || card == null) return;

            // The game raises this twice for one turn — confirmed in the 0.7.45
            // log, where every card logged "flipped" in consecutive pairs. A
            // duplicate line is a bug by this project's own standard, so the
            // second one is dropped on card identity rather than on a timer.
            if (ReferenceEquals(_lastFlipped, card)) return;
            _lastFlipped = card;

            ResetIdle();

            // THROUGH THE SAME SENTENCE THE ARROWS USE. (0.7.247.)
            //
            // This read ChoiceInfo.CardInfo directly, which is null on a cost or
            // tribe node — so the one moment that screen exists for, the player
            // turning a back over and the cost appearing on it, was SILENT. He
            // pressed Enter three times and heard nothing about any of them.
            // DescribeCard answers for every kind of back there is.
            string described = DescribeCard(card);
            if (string.IsNullOrEmpty(described)) return;

            // 0.7.202 — A FLIP IS A GAME ACTION, NOT INFORMATION.
            //
            // Zamar: "Flipping a new card should interrupt the info being read
            // from the other ones. Information vs. Game action taken."
            //
            // He pressed Enter and the card turned over; the read of the card he
            // was looking at a moment ago has no claim on the next second. The
            // one-shot protection is dropped rather than consumed, so this line
            // cuts in as an Action line normally would.
            // 0.7.207 — spoken as a Confirmation: Speech.cs owns the rule
            // above now, and every other "game action taken" line shares it.
            Speech.Confirm(described);
            _log?.LogInfo($"IKMA CHOICE: turned over card — \"{described}\"");
        }

        // ------------------------------------------------------------------
        // The reward was taken. Outcome line, and the end of this screen's
        // ownership of the keyboard.
        // ------------------------------------------------------------------
        internal static void OnChosen(SelectableCard card)
        {
            // ==================================================================
            // ON A COST OR TRIBE NODE, PICKING IS NOT TAKING. (0.7.247.)
            //
            // CardSingleChoicesSequencer.RewardChosenSequence branches on the
            // SAME two fields the cardback was drawn from:
            //
            //   if (ChoiceInfo.resourceType != None)  -> CostChoiceChosen
            //   else if (ChoiceInfo.tribe != None)    -> TribeChoiceChosen
            //
            // Both roll a CardInfo, SetInfo it, SetFaceDown(false) — and then
            // yield WaitForCardToBeTaken, which enables the card and sits there
            // until it is clicked AGAIN. Three clicks on these nodes, not two:
            // turn the back over, pick it, take it. Zamar counted them himself.
            //
            // IKMA used to End() here, on the second. The screen let go of the
            // keyboard while the node was still running, Enter reached the map,
            // and the map's own 'Nodes' object was still inactive — hence the
            // Unity error in his log and "Cannot travel there" on every path.
            // He could not leave and could not finish. Holding on until the card
            // is actually in the deck is the fix for both halves.
            // ==================================================================
            bool rollsItsFaceLater = false;
            try
            {
                var choice = card?.ChoiceInfo;
                rollsItsFaceLater = choice != null &&
                    (choice.resourceType != ResourceType.None || choice.tribe != Tribe.None);
            }
            catch { }

            if (rollsItsFaceLater && card != null)
            {
                _awaitingTake  = card;
                _takeAnnounced = false;
                ResetIdle();
                _log?.LogInfo(
                    "IKMA CHOICE: card picked — the game is still deciding what it is. " +
                    "Holding the keyboard until it is taken.");
                return;
            }

            string name = null;
            try { name = CardReader.CardName(card?.ChoiceInfo?.CardInfo); } catch { }

            // IT ANSWERS THE KEY HE JUST PRESSED, SO IT CUTS. (0.7.275.)
            //
            // Zamar: "Raven Egg added to deck line should have stomped info
            // line." His log has the card read — name, cost, stats, sigil,
            // "Press Enter to add it to your deck" — still draining when he
            // pressed Enter, and the confirmation queued behind an instruction
            // he had already carried out.
            //
            // Same rule as the draw line and the same reason: a line reporting
            // a game action the player just took outranks anything in the air,
            // because the player is waiting to hear whether the press landed.
            // Speech.Confirm is the policy point for that (0.7.207) — it cuts,
            // and it is itself protected from being cut by whatever comes next.
            using (Speech.Event(EventKind.CardObtained, EventSource.CurrentPlayer)) Speech.Confirm(Vocabulary.CardChoices.AddedToYourOrCardAddedTo(name));

            End("reward chosen");
        }

        // ------------------------------------------------------------------
        // The card the game rolled, turned face up and is waiting to be handed
        // over. Read in full, because it is face up and a sighted player is
        // looking straight at it, and closed with the key that takes it.
        // ------------------------------------------------------------------
        private static string TakeLine()
        {
            var card = _awaitingTake;
            if (card == null) return "";

            // NO DUPLICATE NOTE HERE. (0.7.248.) Zamar, on "Flying Ant.
            // Duplicate of card in your deck. Cost: 1 blood...": "Should not
            // call out duplicate here."
            //
            // And the game agrees. The mushroom is a thing the table GROWS, and
            // it only grows in RegularChoiceFlipped, which the sequencer runs
            // only when the card already had a CardInfo at flip time. A cost or
            // tribe card does not, so there is no mushroom under this card and
            // nothing for a sighted player to see. showMushrooms was answering a
            // question about the screen when the question was about this card.
            string described = DescribeCard(card, noteDuplicate: false);
            if (string.IsNullOrEmpty(described)) return "";

            // IKMA PROVISIONAL — Zamar has not ruled on this closing clause.
            return Vocabulary.CardChoices.PressEnterToAdd(described);
        }

        private static void TickTake(float deltaTime)
        {
            var card = _awaitingTake;

            bool gone = true;
            try { gone = card == null || card.gameObject == null; } catch { }
            if (gone) { AnnounceTaken(); return; }

            if (_takeAnnounced) return;

            // Say it when there is something to say AND something to press.
            // Info and the face-up flag land inside CostChoiceChosen; Enabled
            // lands later still, in WaitForCardToBeTaken. Announcing before the
            // card will accept a click would offer him a key that does nothing.
            CardInfo info = null;
            try { info = card.Info; } catch { }
            if (info == null) return;

            bool faceUp = false;
            try { faceUp = !card.FaceDown; } catch { }
            if (!faceUp) return;

            bool takeable = false;
            try { takeable = card.Enabled; } catch { }
            if (!takeable) return;

            string line = TakeLine();
            if (string.IsNullOrEmpty(line)) return;

            _takeAnnounced = true;
            ResetIdle();
            Speech.Confirm(line);
            _log?.LogInfo($"IKMA CHOICE: reward revealed — {CardReader.CardName(info)}.");
        }

        /// <summary>
        /// The game put the chosen card in the deck. Patched onto
        /// CardSingleChoicesSequencer.AddChosenCardToDeck, which is the exact
        /// moment and the only one that is true for every node type.
        /// </summary>
        internal static void OnAddedToDeck()
        {
            if (_sequencer == null || _awaitingTake == null) return;
            AnnounceTaken();
        }

        // ==================================================================
        // THE DECK TRIAL ENDS THROUGH ITS OWN DOORS. (Session 33.)
        //
        // DeckTrialSequencer never calls OnRewardChosen or AddChosenCardToDeck
        // - those belong to its sibling, CardSingleChoicesSequencer. Its reward
        // pick is a local callback, and the card goes into the deck through its
        // own AddRewardCardToDeck. So the patches that end an ordinary card
        // choice never fired here, this reader stayed Active after a passed
        // trial, and in the next battle it owned the keyboard: "No card
        // selected." and the Card Choice help instead of the board.
        //
        // Two exits, both patched on DeckTrialSequencer itself:
        //   AddRewardCardToDeck - the reward is in the deck: say so, end.
        //   ReturnToMap         - the node is over, pass OR fail: end.
        // FinaleDeckTrialSequencer overrides both without calling base (its
        // reward is a boon, not a card), so neither patch reaches the Act 1
        // finale; that screen needs its own wording when it is built.
        // ==================================================================
        internal static void OnDeckTrialRewardAdded(SelectableCard card)
        {
            if (!(_sequencer is DeckTrialSequencer)) return;

            string name = null;
            try { name = CardReader.CardName(card?.Info); } catch { }

            // The same sentence as every other card choice, from the same
            // Vocabulary member, so the two can never disagree.
            using (Speech.Event(EventKind.CardObtained, EventSource.CurrentPlayer)) Speech.Confirm(Vocabulary.CardChoices.AddedToYourOrCardAddedTo(name));
            End("deck trial reward added");
        }

        internal static void OnDeckTrialReturnToMap()
        {
            if (!(_sequencer is DeckTrialSequencer)) return;
            End("deck trial over");
        }

        private static void AnnounceTaken()
        {
            string name = null;
            try { name = CardReader.CardName(_awaitingTake?.Info); } catch { }

            _awaitingTake  = null;
            _takeAnnounced = false;

            // Cuts, for the reason spelled out at the other call site above —
            // and BOTH paths change together, because one sentence spoken from
            // two places is a sentence that will disagree with itself.
            using (Speech.Event(EventKind.CardObtained, EventSource.CurrentPlayer)) Speech.Confirm(Vocabulary.CardChoices.AddedToYourOrCardAddedTo(name));

            End("reward taken");
        }

        // ------------------------------------------------------------------
        // Idle prompt. The game is waiting and nothing else says so.
        // ------------------------------------------------------------------
        // The card objects this screen last laid out. A reroll — and, from the
        // 0.7.45 log, whatever else replaces the choices mid-screen — swaps them
        // for new ones without the sequencer starting again, so Begin does not
        // fire and nothing announces. Zamar got three new cards in silence.
        //
        // Identity, not count: three cards replaced by three cards is the case
        // that matters and a count would miss it entirely.
        private static readonly List<SelectableCard> _lastSeen = new List<SelectableCard>();

        private static bool CardSetChanged(List<SelectableCard> cards)
        {
            if (cards.Count != _lastSeen.Count) return true;
            for (int i = 0; i < cards.Count; i++)
                if (!ReferenceEquals(cards[i], _lastSeen[i])) return true;
            return false;
        }

        private static void RememberCardSet(List<SelectableCard> cards)
        {
            _lastSeen.Clear();
            _lastSeen.AddRange(cards);
        }

        public static void Tick(float deltaTime)
        {
            if (_sequencer == null) return;

            // Ahead of the _announced gate on purpose: a card picked before the
            // arrival line has landed still has to be taken, and a take that is
            // never ticked is the softlock this whole phase exists to stop.
            if (_awaitingTake != null) { TickTake(deltaTime); return; }

            if (!_announced) return;

            var live = Cards();
            if (live.Count > 0 && CardSetChanged(live))
            {
                _log?.LogInfo($"IKMA CHOICE: the cards were replaced ({live.Count} new) — re-announcing.");
                _announced = false;
                _settleFor = 0f;
                _index     = 0;
                _hovered   = null;
                _lastFlipped = null;
                RememberCardSet(live);
                return;
            }

            _idleTimer += deltaTime;
            float threshold = _idleSpokenOnce ? IDLE_REPEAT : IDLE_FIRST;
            if (_idleTimer < threshold) return;

            _idleTimer      = 0f;
            _idleSpokenOnce = true;

            if (Cards().Count == 0) return;

            // LOWEST PRIORITY, AND IT NEVER INTERRUPTS. (0.7.49.)
            //
            // Zamar: "it should never stomp anything, this idle loop is the
            // lowest priority. It's still cutting off the Black Goat's
            // explanation." It was a direct interrupt:true call, which cuts
            // whatever is in the air — including the card description he had
            // just asked for.
            //
            // Info tier now, so it waits its turn behind everything, and
            // deferred so it withdraws itself if the screen has moved on or
            // something else is mid-sentence by the time it reaches the front.
            if (_idlePending) return;
            _idlePending = true;

            int generation = _idleGeneration;

            Speech.Commentary(() =>
            {
                _idlePending = false;
                if (_sequencer == null || !_announced) return null;

                // The player did something while this waited its turn, so it is
                // describing a screen state they have already moved past.
                if (generation != _idleGeneration) return null;

                var live = Cards();
                return live.Count == 0 ? null : ReviewHistory.AsPrompt(OpeningLine(live, idle: true));   // a prompt: kept while History > Prompts is on (Session 38)
            });
        }

        public static void ResetIdle()
        {
            _idleTimer      = 0f;
            _idleSpokenOnce = false;

            // A PROMPT ALREADY IN THE QUEUE IS NOW STALE. (Session 16.)
            //
            // Deferring stopped the idle line INTERRUPTING, but not this:
            // Zamar's log has it queued, then him flipping a card, and then the
            // prompt spoken AHEAD of the card he had just turned over —
            //
            //   QUEUE (action): Tadpole. Cost: Free. ...
            //   SPEAK: Card Choice. Every card is face up. ...
            //   SPEAK: Tadpole. ...
            //
            // A plain Action appends rather than inserting ahead of commentary,
            // so an Info line queued first is spoken first. The fix is not to
            // reorder the queue — it is that the prompt stopped being TRUE the
            // moment he acted. Bumping the generation makes the pending
            // provider return null, and the announcer drops an empty provider
            // silently.
            _idleGeneration++;
        }

        private static int _idleGeneration;
    }
}

// CardChoiceReader.cs
