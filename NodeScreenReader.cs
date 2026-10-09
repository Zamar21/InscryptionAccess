// NodeScreenReader.cs
using System.Collections.Generic;
using BepInEx.Logging;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// The map-event screens — the sacrifice stone first, and the rest of the
    /// Act 1 nodes by the same mechanism. (Session 17.)
    ///
    /// Zamar: "Can we get this map event and all other future map events at
    /// least reading and navigate-able? I feel like we've got enough structure
    /// now with the H key and Space key to get these screens at least stood up."
    ///
    /// He is right that the structure exists, and this is deliberately the
    /// SHALLOW version of it: arrow across whatever the screen owns, highlight
    /// it the way a mouse would, press Enter. That is the layer he reported
    /// missing — "the arrow keys dont work, they need to highlight the different
    /// options as if using the mouse cursor, and enter selects them."
    ///
    /// WHAT IT DELIBERATELY DOES NOT DO YET: read the deck when a slot opens one.
    /// Choosing a card from your deck goes through SelectableCardArray, which is
    /// its own screen with its own browse — DeckViewReader already drives that
    /// shape and wiring the two together is a separate build. Until then the
    /// slots are reachable and the card array is not, which is honest and is
    /// better than the current state of nothing working at all.
    ///
    /// NAMES ARE ZAMAR'S. The screen's objects are called HostSlot,
    /// SacrificeSlot and ConfirmButton, and this project does not speak
    /// GameObject names — the Kaycee's Mod front end settled that. He supplied
    /// "Host Card", "Card to Sacrifice" and "Begin Ritual". Anything unnamed
    /// logs a request rather than inventing a word.
    /// </summary>
    public static class NodeScreenReader
    {
        private static ManualLogSource _log;
        public static void Init(ManualLogSource log) => _log = log;

        private static Component _sequencer;
        private static string    _screenName;
        private static int       _index;
        private static MainInputInteractable _hovered;

        /// <summary>
        /// A LIVENESS TEST, NOT A FLAG. (0.7.91 regression.)
        ///
        /// The reader was released only when NodeProbe ended its survey, so
        /// after a finished node screen it stayed "active" and — sitting above
        /// the pause menu in Update — swallowed every key. That is what broke
        /// the pause menu's arrows.
        ///
        /// This is the cached-state mistake the first rule warns about: never
        /// remember where IKMA put the player, ask the game. A destroyed Unity
        /// object compares equal to null, so this catches the screen going away
        /// however it went.
        /// </summary>
        public static bool Active
        {
            get
            {
                if (_sequencer == null) return false;
                try
                {
                    if (!_sequencer.gameObject.activeInHierarchy) return false;
                }
                catch { return false; }
                return true;
            }
        }
        public static string ScreenName => _screenName;

        // ==================================================================
        // THE PER-PART DIAGNOSTICS, BEHIND A FLAG. (0.7.276.)
        //
        // Zamar: "We need this log as cleaned up as possible." His 0.7.275 log
        // is 381 lines and 88 of them are the scaffolding that was written to
        // BUILD a node screen — every candidate enumerated on every rescan,
        // every empty slot skipped, every part's highlight route, plus the
        // probe's two full dumps. On the Woodcarver, which he has signed off.
        //
        // This is M1's standing item — "every diagnostic behind a category
        // flag that defaults off" — applied to the loudest category there is.
        // It is not deleted: the next node screen built from scratch needs all
        // of it, and one field turns it back on.
        //
        // WHAT STAYS UNCONDITIONAL, deliberately: reader armed and released,
        // the screen settling, an unnamed part (that is a worklist line), and
        // anything spoken. The log has to keep saying what the player heard
        // and what is still owed.
        // ==================================================================
        internal static bool VerboseNodeDiagnostics = false;

        /// <summary>
        /// Spoken names for the parts of a node screen, keyed by GameObject
        /// name. Every one of these is a word Zamar wrote.
        /// </summary>
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _partNames => Loc.PerLanguage(ref _partNamesCache, ref _partNamesLanguage, Build_partNames);
        private static Dictionary<string, string> Build_partNames() =>
            new Dictionary<string, string>
        {
            { "hostslot",      Vocabulary.NodeScreens.HostCard },
            { "sacrificeslot", Vocabulary.NodeScreens.CardToSacrifice },
            { "confirmbutton", Vocabulary.NodeScreens.BeginRitual },
        };
        private static Dictionary<string, string> _partNamesCache;
        private static string _partNamesLanguage;

        // ==================================================================
        // WHERE ONE GAMEOBJECT NAME MEANS TWO DIFFERENT THINGS. (0.7.124.)
        //
        // "confirmbutton" is the confirm on the sacrifice stone AND the confirm
        // at the campfire. One entry in the table above named both, so the
        // campfire announced "Begin Ritual" — a ritual that is not happening,
        // on a screen that is about warming a card by a fire.
        //
        // THIS IS THE SKULL PROBLEM AGAIN, and that is worth noticing: ten cabin
        // objects share the GameObject name "ZoomInteractable" and the fix there
        // was to key the word on what Zamar authored rather than on the name.
        // Same shape, same fix, second screen. A GameObject name is a prefab
        // label, not an identity, and this project has now been bitten by that
        // twice in one session.
        //
        // Looked up as "screen|rawname" and falling through to the shared table,
        // so a part that genuinely means the same thing everywhere still needs
        // only one entry.
        //
        // His line, Session 18: "the selection confirm shouldnt be 'Begin
        // Ritual' it should be 'Send card to rest by the fire.'"
        // ==================================================================
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _partNamesByScreen => Loc.PerLanguage(ref _partNamesByScreenCache, ref _partNamesByScreenLanguage, Build_partNamesByScreen);
        private static Dictionary<string, string> Build_partNamesByScreen() =>
            new Dictionary<string, string>
        {
            { Vocabulary.Campfire + "|confirmbutton", Vocabulary.NodeScreens.SendCardToRest },

            // THE TWO PARTS THAT HAVE BEEN INVISIBLE. (0.7.129.)
            //
            // His 0.7.128 candidate dump is unambiguous — the gate closing on
            // the campfire was never `active` or `enabled`, it was `named`:
            //
            //   'SelectionSlot' (SelectCardFromDeckSlot) active=True enabled=True named=False
            //   'DeckPile'      (CardPile)               active=True enabled=True named=False
            //   'ConfirmButton' (ConfirmStoneButton)     active=True enabled=True named=True
            //
            // Parts() only ever offered the confirm, so there was nothing to
            // browse BETWEEN — which is exactly what he reported: "the arrow
            // keys then dont let me browse between changing the selected card
            // and sending it to the campfire like it does with the mouse."
            //
            // *** BOTH OF THESE WORDS ARE PROVISIONAL AND ARE MINE, NOT HIS. ***
            // They are logged as provisional at speak time. They ship only
            // because the feature is unusable without something in this table
            // and he has now been blocked on it for four builds; the rule is
            // that a placeholder must be honest and obviously replaceable, and
            // each of these is one string.
            // "YOUR DECK" WAS NOT A CHOICE AND SHOULD NEVER HAVE BEEN OFFERED.
            // Zamar: "This is also an invalid option. You cannot look at your
            // deck here." DeckPile is scenery on this screen — the cards come
            // from it, but it is not a thing the player acts on. Offering it was
            // a key that does nothing, which is the defect this project fixes
            // everywhere else, introduced by me one build ago.
            //
            // The lesson: a part being present, active and enabled does not make
            // it an OPTION. The approved-name table is the list of things the
            // player can act on, and adding a name to it is a claim.
            { Vocabulary.Campfire + "|selectionslot", "CAMPFIRE_CHOOSE" },

            // ==============================================================
            // THE MYCOLOGISTS. (0.7.287.)
            //
            // ZAMAR'S WORDING for the slot, 0.7.286, verbatim: "'Select a
            // pair of cards to fuse together' should have been the hover
            // option with arrows." It replaces "Selection slot", which was
            // the fallback prettify of the GameObject name.
            //
            // THE PAIR IS READ FROM WHAT IT HOLDS, not from what it is
            // called. His report: "'Selectable card pair - clone' should
            // have been flying ants." SelectableCardPair carries LeftCard
            // and RightCard, both SelectableCard with their own CardInfo, so
            // the cards are there to be asked for — the same rule 0.7.249
            // applied at the Woodcarver, where a part that HOLDS something
            // is read as its contents.
            // ==============================================================
            // "Begin experiment..." not "Begin Ritual". Zamar, 0.7.290:
            // "That's just for the stone." The generic confirmbutton entry
            // belongs to the sacrifice stone and had been leaking onto every
            // screen that never overrode it — the campfire caught this once
            // already.
            { Vocabulary.Mycologists + "|confirmbutton", Vocabulary.NodeScreens.BeginExperiment },
            { Vocabulary.Mycologists + "|selectionslot", Vocabulary.NodeScreens.SelectAPairOf },
            { Vocabulary.Mycologists + "|selectablecardpair(clone)", "MYCO_PAIR" },

            // THE PULL-AWAY OPTION. (0.7.137.) It came up active and enabled in
            // his 0.7.135 log for the first time, so it is real and reachable —
            // it just had no word, which is the only reason it was not offered.
            //
            // "Withdraw from the campfire." — HIS WORDING, 0.7.154, replacing
            // his own "You withdraw [Card name]." placeholder from 0.7.137.
            { Vocabulary.Campfire + "|retrievecardinteractable", "CAMPFIRE_WITHDRAW" },

            // THE COPY CARD SCREEN, ALL HIS WORDS. (0.7.267.)
            //
            // "Begin Ritual" reached this screen the same way it once reached
            // the campfire: the shared "confirmbutton" entry names every
            // ConfirmStoneButton in the game, and only the sacrifice stone is
            // actually a ritual. That is the third screen to be bitten by one
            // GameObject name meaning three things, so the fix is the one this
            // table already exists for rather than a new mechanism.
            //
            // Zamar: "Begin Ritual" -> "Begin painting."; the selection slot is
            // "Choose a card to receive an artistic rendition of."
            { Vocabulary.NodeScreens.CopyCard + "|confirmbutton",  Vocabulary.NodeScreens.BeginPainting },
            { Vocabulary.NodeScreens.CopyCard + "|selectionslot",  Vocabulary.NodeScreens.ChooseACardTo },
        };
        private static Dictionary<string, string> _partNamesByScreenCache;
        private static string _partNamesByScreenLanguage;

        /// <summary>
        /// What each node screen is FOR, in Zamar's words. A screen with no
        /// entry falls back to its option count, which is true but thin.
        /// </summary>
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _screenBlurbs => Loc.PerLanguage(ref _screenBlurbsCache, ref _screenBlurbsLanguage, Build_screenBlurbs);
        private static Dictionary<string, string> Build_screenBlurbs() =>
            new Dictionary<string, string>
        {
            { Vocabulary.SacrificeStone,
              Vocabulary.NodeScreens.ChooseOneCardFrom },

            // His words, Session 18: "Should add the explanation for what you
            // are doing in this screen here on H." The ellipsis is his, and it
            // is the same warning the card-choice prompt carries — the campfire
            // is the one Act 1 node that can cost you the card you pick.
            { Vocabulary.Campfire,
              Vocabulary.NodeScreens.SelectACardFrom },

            // ==================================================================
            // PROVISIONAL - 0.7.214. THESE SIX ARE MINE, NOT HIS.
            //
            // The two above are Zamar's, written after hearing the screens. The
            // six below cover every remaining node screen so that H answers
            // with something on all of them instead of an option count, and
            // each is derived only from what the sequencer actually DOES - no
            // invented flavour. Every use logs "IKMA PROVISIONAL".
            //
            // Derivations, so a rewrite can check them:
            //   Mycologists   DuplicateMergeSequencer: pick one card from the
            //                 deck, receive a duplicate of it.
            //   Woodcarver    BuildTotemSequencer: choose a tribe top and a
            //                 sigil bottom; every card of that tribe in your
            //                 deck gains the sigil. (Zamar wrote this one
            //                 himself at 0.7.250 — no longer a derivation.)
            //   Trapper       BuyPeltsSequencer: buy pelts with teeth.
            //   Trader        TradePeltsSequencer: give pelts, take cards.
            //   Bone Lord     CardRemoveSequencer: remove one card from your
            //                 deck permanently; a boon follows.
            //   Copy card     CopyCardSequencer: pick one card from your deck
            //                 and a copy is added to it.
            // ==================================================================

            // 0.7.288 — REWRITTEN BECAUSE IT DESCRIBED THE WRONG NODE.
            // Zamar: "We also need to update the Mycologist H key to make
            // sure that info is accurate." It said the Mycologists give you a
            // second copy of a card, which is the COPY CARD node. This screen
            // fuses a pair you already have into one card.
            //
            // Derived from DuplicateMergeSequencer.MergeCards, and it claims
            // only what that method does: GetDuplicateMod(card2.Attack,
            // card2.Health) adds the second card's stats to the first, and
            // card2's merge-granted abilities are carried over while fewer
            // than four have been gained that way.
            //
            // 0.7.289 — THE CAP CLAUSE IS OUT, at his instruction, and the
            // DECK COST IS IN. Reading MergeCards left the RemoveCard call
            // ambiguous: it is passed the pair's own CardInfo rather than a
            // deck entry, so the source alone could not say whether a copy
            // actually leaves. Zamar answered it from the table — "yes the
            // deck loses a copy" — and a playtest report outranks a reading
            // of the code. It is the fact that decides whether the trade is
            // worth taking, so it is the last thing the line says.
            // Still PROVISIONAL.
            { Vocabulary.Mycologists,
              Vocabulary.NodeScreens.ChooseAPairOf },

            // ZAMAR'S WORDS, 0.7.250, VERBATIM — and the screen is named
            // "Woodcarver" now, which is what the map already called it on the
            // way in. It described two steps as one: you pick a NEW carving for
            // the backpack first, and only then, if the backpack holds a matched
            // pair, do you choose which pair to carry. No longer provisional.
            { Vocabulary.Woodcarver,
              Vocabulary.NodeScreens.PickANewWoodcarving },

            { Vocabulary.Trapper,
              // Zamar's wording, Session 25 (0.7.330), replacing his #28 — the
              // arrival line and H say the same thing.
              // 0.7.334: the Trader sentences cut, his call — "too much info."
              Vocabulary.NodeScreens.TradeTheTeethYouve },

            { Vocabulary.Trader,
              // Zamar's wording, 0.7.359 — the sentence his arrival line and
              // his card-layout H open with, so the screen is described one
              // way whichever H answers (this one runs while no offers are
              // on the table).
              Vocabulary.NodeScreens.TradePeltsFromYourDeck },

            { Vocabulary.BoneLord,
              // Zamar's wording, Session 25. He first wrote "at the start of each of your
              // turns"; the game (BoonsHandler.ActivatePreCombatBoons) gives
              // the bones once per battle, and he chose the game's version.
              Vocabulary.NodeScreens.SelectOneCardFrom },

            // 0.7.267 — HIS ADDITION, and it is the whole character of the
            // screen: CopyCardSequencer.CreateCloneCard rolls once and either
            // swaps a sigil for a random learned one, shifts attack by 1, or
            // shifts health by +2 or down toward 1. The copy is never
            // guaranteed to be the card you picked, and a player choosing
            // blind had no way to know that before choosing.
            { Vocabulary.NodeScreens.CopyCard,
              Vocabulary.NodeScreens.SelectOneCardFromYour },
        };
        private static Dictionary<string, string> _screenBlurbsCache;
        private static string _screenBlurbsLanguage;

        /// <summary>
        /// Reading order for a screen's parts — the order the player works
        /// through them, not where they sit on the table.
        /// </summary>
        private static readonly Dictionary<string, int> _partOrder
            = new Dictionary<string, int>
        {
            { "hostslot",      1 },
            { "sacrificeslot", 2 },

            // The campfire, in the order the player works through it: the card
            // sitting by the fire, the deck it can be swapped from, and then the
            // commit. Same argument as the stone — the confirm reads last even
            // though it sits between the other two on the table.
            { "selectionslot",            1 },
            { "retrievecardinteractable", 2 },

            { "confirmbutton", 3 },
        };

        /// <summary>
        /// Screens where Shift+Up shows the deck — because the SEQUENCER handles
        /// View.MapDeckReview and most of them do not.
        /// </summary>
        /// <remarks>
        /// 0.7.250. Zamar asked for "Shift Up displays your deck." in the
        /// Woodcarver's help line, and BuildTotemSequencer earns it: its
        /// OnViewChanged calls DeckReviewSequencer.SetDeckReviewShown, the same
        /// path CardChoicesSequencer uses for the binding that already works on
        /// the card choice screen.
        ///
        /// CardMergeSequencer, BuyPeltsSequencer and the rest do NOT handle that
        /// view — they lock it — so the key would do nothing there and the line
        /// would be advertising it. One set decides both the binding and the
        /// sentence, so neither can outlive the other.
        /// </remarks>
        //
        // 0.7.251 — AND THE WOODCARVER CAME BACK OUT OF IT, ONE BUILD LATER.
        //
        // Handling View.MapDeckReview was necessary and not sufficient. The key
        // does not switch views, it CLIMBS them: MapReader.ShowDeck presses
        // LookUp until the camera reaches the deck, starting from wherever the
        // player is. At the Woodcarver that start is View.TotemInventory — the
        // log the carvings sit on — which is not a rung on the Look ladder at
        // all. Zamar's log: three LookUp presses got him Default, MapDefault,
        // MapArial, MapDeckReview, so he heard the log zoom in before the deck
        // arrived, and the descent then walked one rung past the map and stood
        // him up at the table with nothing bound.
        //
        // The climb is guarded now (MapReader.Press, same build), but the guard
        // makes the key survivable rather than correct. Shift+Up at the
        // Woodcarver belongs on a direct view switch, not a climb, and that is
        // owed work rather than a line to keep advertising meanwhile.
        //
        // 0.7.267 — AND THE COPY CARD SCREEN IS ONE THAT IS. Zamar: "Also add
        // the Shift up line to this too since thats possible." Unlike the
        // Woodcarver, CopyCardSequence switches to View.Default and leaves the
        // player there, and Default IS a rung on the Look ladder — so
        // MapReader.ShowDeck's climb starts from a rung and the ordinary deck
        // route works, the same one the card choice screen uses.
        //
        // This set gates BOTH the help sentence and the Shift+Up binding in
        // HotkeyManager, so a screen cannot end up advertising a key it does
        // not bind or binding one it does not mention.
        // 0.7.318 — AND HE TOOK IT BACK OUT, one screen later. Zamar:
        // "Card copy, cant look at deck in this screen, disable Shift Up".
        //
        // The climb starting from a rung was never the question the key had to
        // answer. His log has it: three "Your deck." lines, the camera walking
        // the ladder, and then the GAME refusing outright — "THAT ONE'S ONLY
        // FOR CARD DUELING." The deck is not available at this node, so the
        // key was advertising something the game does not allow.
        //
        // THE SET IS EMPTY, NOT DELETED. It still gates both the binding and
        // the help sentence together, and the next screen that genuinely
        // supports the deck goes in here rather than growing a second route.
        //
        // 0.7.359 — THE TRADER IS ONE THAT IS. Zamar: "need to add the shift
        // up command to the trader screen." TradePeltsSequencer calls
        // EnableViewDeck(ControlMode.TradePelts) once the offers are laid out,
        // and that control mode allows exactly two views, TradingTopDown and
        // MapDeckReview — one click of the wheel up is the deck, and
        // CardChoicesSequencer.OnViewChanged lays it out. So the ordinary route
        // works: ShowDeck presses LookUp from TradingTopDown, Backspace presses
        // LookDown back to it. DeckViewAvailable also asks the game whether
        // that control mode is live, because it is only switched on while the
        // offers are on the table.
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static HashSet<string> _screensWithDeckView => Loc.PerLanguage(ref _screensWithDeckViewCache, ref _screensWithDeckViewLanguage, Build_screensWithDeckView);
        private static HashSet<string> Build_screensWithDeckView() =>
            new HashSet<string> { Vocabulary.Trader };
        private static HashSet<string> _screensWithDeckViewCache;
        private static string _screensWithDeckViewLanguage;

        // ==================================================================
        // A LABEL TABLE, BECAUSE 0.7.133'S LESSON CAME BACK. (0.7.318.)
        //
        // "ONE NAME CANNOT BE BOTH A LABEL AND A PROMPT" is written thirty
        // lines above the confirmation that just broke on it again. The Copy
        // card slot's approved name is a PROMPT — "Choose a card to receive an
        // artistic rendition of" — and the confirmation dropped it whole into
        // a sentence built for a noun, which gave Zamar:
        //
        //   "Mealworm selected as Choose a card to receive an artistic
        //    rendition of."
        //
        // His correction, verbatim: "Should be 'Mealworm selected as card to
        // receive an artistic rendition of.'"
        //
        // The campfire got its own bespoke sentence for this in 0.7.135. A
        // third screen hitting the same wall makes it a table, not a special
        // case: the prompt form stays in _partNames for browsing, the noun
        // form lives here, and the confirmation asks for the noun.
        //
        // KEYED BY SCREEN, and that is deliberate — it also answers the case
        // where the slot was never activated through the reader and
        // _slotBeingFilled is null. Each of these screens fills exactly one
        // slot, so the screen is enough to know what is being chosen.
        // ==================================================================
        // 0.7.320 — KEYED BY SLOT NAME FIRST, SCREEN NAME SECOND. The stone
        // fills TWO slots, so one entry per screen stopped being enough. The
        // screen keys stay for the case the slot was never activated through
        // the reader and _slotBeingFilled is null — a screen that fills one
        // slot knows what is being chosen without being told.
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _chosenAsLabels => Loc.PerLanguage(ref _chosenAsLabelsCache, ref _chosenAsLabelsLanguage, Build_chosenAsLabels);
        private static Dictionary<string, string> Build_chosenAsLabels() =>
            new Dictionary<string, string>
        {
            // Slot names, as _partNames approves them.
            // 0.7.319, his correction on hearing it: "Remove of at end."
            { Vocabulary.NodeScreens.ChooseACardTo,
                                    Vocabulary.NodeScreens.CardToReceiveAn },

            // 0.7.320. Zamar, on being told the stone still read
            // "Host Card: Flying Ant.": "Change this too. 'Flying Ant chosen
            // as host card. Enter to rechose.'" Lower case mid-sentence, his
            // capitalisation.
            { Vocabulary.NodeScreens.HostCard,          Vocabulary.NodeScreens.HostCardName },

            // The sibling slot on the same screen, in the shape he set twice.
            // Approved by Zamar, Session 25.
            { Vocabulary.NodeScreens.CardToSacrifice,  Vocabulary.NodeScreens.ChosenAsLabelsCardToSacrifice },

            // Screen names, used only when the slot has none.
            { Vocabulary.NodeScreens.CopyCard,          Vocabulary.NodeScreens.CardToReceiveAn },

            // Session 37, note D2 - Zamar: "Mealworm chosen as card to risk
            // resting by the campfire. Enter to rechoose." (was "...by 1.: Stoat.")
            { Vocabulary.Campfire,                      Vocabulary.NodeScreens.CardToRiskResting },
        };
        private static Dictionary<string, string> _chosenAsLabelsCache;
        private static string _chosenAsLabelsLanguage;

        /// <summary>
        /// The noun form for "X chosen as ___", from the slot if it has one and
        /// from the screen otherwise. Null when neither is listed.
        /// </summary>
        private static string ChosenAsLabel(string partName)
        {
            string label = null;
            if (!string.IsNullOrEmpty(partName) &&
                _chosenAsLabels.TryGetValue(partName, out label) &&
                !string.IsNullOrEmpty(label))
                return label;

            if (!string.IsNullOrEmpty(_screenName) &&
                _chosenAsLabels.TryGetValue(_screenName, out label) &&
                !string.IsNullOrEmpty(label))
                return label;

            return null;
        }

        /// <summary>True while the screen on show supports Shift+Up.</summary>
        public static bool DeckViewAvailable
            => DeckViewOffered && GameAllowsDeckView();

        /// <summary>
        /// The screen has a deck view at all, whether or not the game has
        /// switched it on yet. (0.7.359.)
        /// </summary>
        public static bool DeckViewOffered
            => _screenName != null && _screensWithDeckView.Contains(_screenName);

        /// <summary>
        /// The game's own answer to "would one click of the wheel up reach the
        /// deck right now": the camera is not locked, and the control mode in
        /// force allows View.MapDeckReview. Asked on the keypress, never
        /// remembered. (0.7.359.)
        /// </summary>
        private static bool GameAllowsDeckView()
        {
            try
            {
                var vc = Views()?.Controller;
                if (vc == null) return false;
                return vc.LockState != ViewLockState.Locked
                       && vc.CurrentControlModeAllowsView(View.MapDeckReview);
            }
            catch { return false; }
        }

        /// <summary>
        /// The rung the deck view sits above on a node screen that has one —
        /// where Backspace climbs back to. Null anywhere else. (0.7.359.)
        /// </summary>
        public static View? DeckViewOrigin
            => Active && _screenName == Vocabulary.Trader ? View.TradingTopDown : (View?)null;

        /// <summary>
        /// Parts that exist, are active, and are still NOT arrow options —
        /// because the player already has a key that goes there.
        /// </summary>
        /// <remarks>
        /// 0.7.281. Keyed "screen|rawname" like the approved-name table, so a
        /// GameObject name that means something else on another screen is
        /// unaffected. An entry here is a statement that the destination is
        /// reachable another way, not that it is unimportant.
        /// </remarks>
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static HashSet<string> _notBrowsable => Loc.PerLanguage(ref _notBrowsableCache, ref _notBrowsableLanguage, Build_notBrowsable);
        private static HashSet<string> Build_notBrowsable() =>
            new HashSet<string>
        {
            // A opens the backpack at the Woodcarver, so the on-table button
            // that does the same thing is not a fourth option between the
            // carvings.
            Vocabulary.Woodcarver + "|returntoinventoryinteractable",
        };
        private static HashSet<string> _notBrowsableCache;
        private static string _notBrowsableLanguage;

        /// <summary>
        /// Parts that are SCENERY — they carry a collider, so the probe finds
        /// them, and pressing Enter on one does nothing at all.
        /// </summary>
        /// <remarks>
        /// 0.7.286, and a different claim from _notBrowsable above: that set
        /// says "reachable another way", this one says "not a thing you can
        /// do".
        ///
        /// The Mycologists screen is the case that forced it.
        /// DuplicateMergeSequencer.MergeSequence pops up a ring of small
        /// mushrooms and a large one, and enables a Collider on every one of
        /// them (`mushroom.GetComponent&lt;Collider&gt;().enabled = true`), purely
        /// so they can be nudged. The probe counted nine of them as options.
        /// Zamar's 0.7.285 log has seven consecutive
        /// "activating 'Mushroom alt'" lines with nothing happening: he was
        /// pressing Enter on a prop.
        ///
        /// The screen has exactly two things to act on, both named in the
        /// sequencer's own fields: selectionSlot (SelectCardPairFromDeckSlot)
        /// and confirmStone (ConfirmStoneButton).
        ///
        /// MATCHED BY PREFIX, not exact name, because Unity's clone suffixes
        /// are unbounded — Mushroom, Mushroom (1)..(5), Mushroom_Alt,
        /// Mushroom_Alt (1)..(2) today, and no reason to think that count is
        /// fixed. Keyed "screen|prefix", lowercased, same as the tables above.
        /// Neither real part starts with "mushroom", so nothing reachable is
        /// caught by it.
        /// </remarks>
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static HashSet<string> _sceneryPrefixes => Loc.PerLanguage(ref _sceneryPrefixesCache, ref _sceneryPrefixesLanguage, Build_sceneryPrefixes);
        private static HashSet<string> Build_sceneryPrefixes() =>
            new HashSet<string>
        {
            Vocabulary.Mycologists + "|mushroom",
        };
        private static HashSet<string> _sceneryPrefixesCache;
        private static string _sceneryPrefixesLanguage;

        private static bool IsScenery(string raw)
        {
            if (string.IsNullOrEmpty(raw) || _screenName == null) return false;
            string name = raw.Trim().ToLowerInvariant();
            foreach (var entry in _sceneryPrefixes)
            {
                int bar = entry.IndexOf('|');
                if (bar <= 0) continue;
                if (!string.Equals(entry.Substring(0, bar), _screenName,
                                   System.StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith(entry.Substring(bar + 1),
                                    System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ==================================================================
        // THE BACKPACK. (0.7.256.)
        //
        // Zamar: "We also need a way to browse our previously selected
        // woodcarvings before making our choice."
        //
        // He is choosing between three carvings and the right answer depends
        // entirely on what he already owns — a second Insect head is worth
        // nothing, and a Sigil body is worth everything if he has a head and no
        // body. A sighted player reads that off the log the carvings sit on.
        //
        // NOT AS BROWSE OPTIONS. The inventory slots are inactive-to-input while
        // the picking phase runs, so putting them in the arrow list would offer
        // him things the game will not let him press. This is a READ, on its own
        // key, which is the shape the mod already uses for anything that is
        // information rather than a choice.
        // ==================================================================
        public static bool BackpackAvailable
            => _screenName == Vocabulary.Woodcarver && _sequencer != null;

        /// <summary>
        /// A, at the Woodcarver: go up to the log the backpack sits on and
        /// browse it, exactly as one scroll of the wheel does.
        /// </summary>
        /// <remarks>
        /// 0.7.257, replacing 0.7.256's spoken list. Zamar: "instead of reading
        /// a list, go to this screen pictured, essentially one mouse scroll
        /// wheel up, to browse with arrow keys and backspace to return."
        ///
        /// A LIST WAS THE WRONG SHAPE and the game says so. Switching to
        /// View.TotemInventory makes BuildTotemSequencer.OnViewChanged enable
        /// the inventory slots and disable the choice slots — so Parts() starts
        /// answering with the backpack of its own accord, the arrows browse it
        /// with the reader that already exists, and each carving reads through
        /// TotemPieceLine like every other carving on this screen. No list to
        /// keep in step with the table.
        ///
        /// SWITCHED, NOT CLIMBED. MapReader.ShowDeck walks the Look ladder, and
        /// this screen's views are not rungs on it — that is what stranded him
        /// at 0.7.250. SwitchToView is what the wheel does.
        /// </remarks>
        public static void OpenBackpackView()
        {
            if (!BackpackAvailable) return;

            // THE KEY THAT OPENS A SCREEN CLOSES IT. (0.7.260 — Zamar's rule,
            // stated as one: "a button to get into a screen should usually also
            // get you out of that screen.")
            //
            // Pressing A again used to re-read the position, which is what Space
            // is for. A door you can only leave by a different door is a thing
            // to remember; a door that swings both ways is not.
            if (InBackpackView) { CloseBackpackView(); return; }

            var vm = Views();
            if (vm == null) { Speech.Browse(Vocabulary.NodeScreens.BackpackCannotBeReached); return; }

            try { vm.SwitchToView(View.TotemInventory); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: opening the backpack threw {e.GetType().Name}.");
                return;
            }

            _index   = NOWHERE;
            _hovered = null;
            ReleaseHover();

            _log?.LogInfo("IKMA NODE: backpack view opened.");
            // IKMA PROVISIONAL — Zamar has not ruled on this wording.
            // "Backpack", not "Your backpack" — 0.7.275, his call. Every other
            // line on this screen already belongs to the player; the possessive
            // was a word that earned nothing.
            Speech.Browse(Vocabulary.NodeScreens.BackpackArrowsBrowseOrBackpackArrowsBrowse(BackspaceLeavesBackpack()));
        }

        /// <summary>
        /// Does Backspace mean anything in the backpack right now?
        /// </summary>
        /// <remarks>
        /// 0.7.278. Only while there are carvings to go back to. Once one is
        /// taken the three selection slots go inactive and the table behind is
        /// empty, so the key is unbound and the line stops offering it — the
        /// binding and the sentence change together, which is the standing rule
        /// for every key in this mod.
        /// </remarks>
        // ==================================================================
        // HOW MANY CARVINGS ARE STILL ON OFFER. (0.7.279.)
        //
        // 0.7.278 asked Parts().Count and Zamar softlocked again, because in
        // the backpack Parts() returns THE BACKPACK'S OWN SEVEN SLOTS. The
        // test was measuring whether anything was on show while the thing on
        // show was the wrong screen. A count is not an identity.
        //
        // The carving selection is BuildTotemSequencer's own `slots` list —
        // ItemSlot_Left, Center and Right — which the probe has been printing
        // all along ("field List`1 slots = count=3"). They go inactive the
        // moment a carving is taken, so their active count IS the phase:
        //
        //   more than zero -> still picking a carving
        //   zero           -> picked; the backpack is now the totem combination
        //
        // Asked by reflection off the live sequencer, with the three GameObject
        // names as the fallback, so a miss reads as "no carvings" rather than
        // throwing. Both of his rules hang on this one answer.
        // ==================================================================
        private static System.Reflection.FieldInfo _carvingSlotsField;
        private static bool _carvingSlotsResolved;

        internal static int CarvingsRemaining()
        {
            if (_sequencer == null) return 0;

            try
            {
                if (!_carvingSlotsResolved)
                {
                    _carvingSlotsResolved = true;
                    _carvingSlotsField = _sequencer.GetType().GetField("slots",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);

                    if (_carvingSlotsField == null)
                        _log?.LogInfo(
                            "IKMA NODE: no 'slots' field on this sequencer — falling back to " +
                            "the three carving slot names.");
                }

                var list = _carvingSlotsField?.GetValue(_sequencer) as System.Collections.IEnumerable;
                if (list != null)
                {
                    int live = 0;
                    foreach (var o in list)
                    {
                        var comp = o as Component;
                        if (comp == null) continue;
                        try { if (comp.gameObject.activeInHierarchy) live++; } catch { }
                    }
                    return live;
                }
            }
            catch { }

            // Fallback: the three names, which the probe has confirmed on every
            // visit to this screen.
            try
            {
                int live = 0;
                foreach (var it in _sequencer.GetComponentsInChildren<MainInputInteractable>(true))
                {
                    if (it == null) continue;
                    string raw = null;
                    try { raw = it.gameObject.name; } catch { }
                    if (raw == null) continue;
                    if (raw != "ItemSlot_Left" && raw != "ItemSlot_Center" && raw != "ItemSlot_Right")
                        continue;
                    try { if (it.gameObject.activeInHierarchy) live++; } catch { }
                }
                return live;
            }
            catch { return 0; }
        }

        /// <summary>Backspace means something in the backpack only while a carving is still to be picked.</summary>
        private static bool BackspaceLeavesBackpack() => CarvingsRemaining() > 0;

        /// <summary>
        /// Enter in the backpack is the TOTEM COMBINATION choice, so it is not
        /// bound until the carving has been picked.
        /// </summary>
        /// <remarks>
        /// 0.7.279, his rule: "Enter should NOT work on the backpack screen,
        /// until after making a selection."
        ///
        /// His log shows why it matters. He opened the backpack with the
        /// carvings still on the table, pressed Enter on two of his own items,
        /// and heard "You took the Insect head." and "You took the Bees Within
        /// sigil body." for things he already owned and had not just taken —
        /// two false lines and an action the screen was not offering yet.
        ///
        /// The two rules are one phase read from opposite ends: while carvings
        /// remain the backpack is a reference and Backspace goes back to them;
        /// once they are gone the backpack is the choice and Backspace has
        /// nowhere to return to.
        /// </remarks>
        internal static bool EnterChoosesInBackpack() => CarvingsRemaining() == 0 && !TotemAutoAssembles();

        /// <summary>
        /// Session 34. One head and one body: the game assembles the totem
        /// itself (BuildTotemSequencer.AutoAssembleTotem, PRIVATE bool, see
        /// dumps\dump_woodcarver_auto_from_decompile.txt), never enables the
        /// way back to the backpack and gives the backpack slots no select
        /// action. So there is nothing to reselect, and IKMA must not say
        /// "selected" for a click the game ignores. Asked of the game's own
        /// method, not recomputed from the run's counts.
        /// </summary>
        internal static bool TotemAutoAssembles()
        {
            try
            {
                if (_sequencer == null) return false;
                var m = _sequencer.GetType().GetMethod("AutoAssembleTotem",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                return m != null && (bool)m.Invoke(_sequencer, null);
            }
            catch { return false; }
        }

        /// <summary>Backspace out of the backpack, back down to the carvings.</summary>
        public static void CloseBackpackView()
        {
            if (!InBackpackView) return;

            // ==================================================================
            // NOT A REFUSAL — NOT AN OPTION. (0.7.278, his correction.)
            //
            // Zamar: "Can we just remove backspace as an option from the
            // backpack screen once you've chosen a woodcarving? You shouldnt be
            // in the choice window while it's empty anyways."
            //
            // 0.7.277 refused it out loud, which answered a key that should not
            // have been offered in the first place. The backpack's own line no
            // longer mentions Backspace once the carvings are gone, so this is
            // a key with no meaning here rather than a key that says no — and
            // the empty selection, which is what convinced the reader the
            // screen was over, is somewhere the player can no longer land.
            // ==================================================================
            if (!BackspaceLeavesBackpack())
            {
                _log?.LogInfo(
                    "IKMA NODE: Backspace is not bound in the backpack — the woodcarving " +
                    "selection is gone, so there is nowhere to return to.");
                return;
            }

            var vm = Views();
            if (vm == null) return;

            try { vm.SwitchToView(View.Default); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: leaving the backpack threw {e.GetType().Name}.");
                return;
            }

            _index   = NOWHERE;
            _hovered = null;
            ReleaseHover();

            _log?.LogInfo("IKMA NODE: backpack view closed.");
            Speech.Browse(BACK_TO_CARVINGS);
        }

        /// <summary>
        /// Shift+Up at the Woodcarver: two scrolls up, to the deck.
        /// </summary>
        /// <remarks>
        /// 0.7.257. Goes down to View.Default FIRST and then straight to the
        /// deck, deliberately: Backspace out of the deck climbs back to whatever
        /// origin it was given, and Default is a rung on that ladder while
        /// TotemInventory is not. Handing it a view the climb cannot find is
        /// exactly how 0.7.250 walked him off the table.
        /// </remarks>
        public static void OpenDeckView()
        {
            if (!BackpackAvailable) return;

            var vm = Views();
            if (vm == null) { Speech.Browse(Vocabulary.NodeScreens.DeckCannotBeReached); return; }

            try
            {
                if (InBackpackView) vm.SwitchToView(View.Default);

                MapReader.NoteDeckOrigin(View.Default);
                DeckViewReader.NotePlayerAsked();

                vm.SwitchToView(View.MapDeckReview);
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: opening the deck threw {e.GetType().Name}.");
                return;
            }

            _index   = NOWHERE;
            _hovered = null;
            ReleaseHover();

            _log?.LogInfo("IKMA NODE: deck view opened from the Woodcarver.");

            // MapReader's view watcher raises DeckViewReader.OnViewEntered by
            // any route in, so the deck reads itself from here. This is only the
            // instant acknowledgement, the same one ShowDeck speaks.
            // Bare from 0.7.276. The kin breakdown moved into
            // DeckViewReader's settled line, because this one is interrupted
            // by it half a second later and everything past "Your deck." was
            // being cut off mid-word.
            Speech.Browse(Vocabulary.YourDeck);
            DeckViewReader.NoteOpeningSpoken();

            // 0.7.277 — and make sure the settled read actually comes. A fast
            // close-and-reopen leaves the camera where it already was, so the
            // view watcher sees no change and the deck reader never rearms.
            DeckViewReader.ForceReannounce();
        }

        /// <summary>
        /// " Insect, 2 cards. Hooved, 3 cards." — what the deck is made of, by
        /// kin, for the one screen where that is the whole decision.
        /// </summary>
        /// <remarks>
        /// 0.7.260. Zamar: "only on this Woodcarver node, when it says 'Your
        /// Deck.' It should then also read which Kin types your deck currently
        /// contains and how many of each."
        ///
        /// A totem head is worth nothing without cards of that kin to put it on,
        /// and the deck view reads out one card at a time — so the number a
        /// player actually needs here is a count they would otherwise have to
        /// hold in their head across a dozen cards. A sighted player does the
        /// same arithmetic by eye; this is not information the screen hides.
        ///
        /// RunState.DeckList is the game's own list and CardInfo.tribes its own
        /// field, so nothing is inferred. A card with no kin is left out
        /// entirely: it can never be affected by a totem, so it is not part of
        /// this question. Ordered by count, because the biggest kin is the one
        /// the decision usually turns on.
        /// </remarks>
        /// <summary>
        /// The deck counted by kin, as " Insect, 7 cards. Reptile, 1 card."
        /// Empty when the deck has no tribed cards at all.
        /// </summary>
        /// <remarks>
        /// 0.7.275 — internal, because DeckViewReader says this too now.
        /// Zamar asked for kin amounts on Shift+Up and this is already the
        /// sentence for it; a second copy in the deck reader would be the
        /// duplicate-composer defect SPOKEN_STRINGS.md counts 34 of.
        /// </remarks>
        internal static string KinBreakdown()
        {
            try
            {
                var deck = RunState.DeckList;
                if (deck == null || deck.Count == 0) return "";

                var order  = new List<Tribe>();
                var counts = new Dictionary<Tribe, int>();

                foreach (var card in deck)
                {
                    var tribes = card?.tribes;
                    if (tribes == null) continue;

                    foreach (var t in tribes)
                    {
                        if (t == Tribe.None) continue;
                        if (!counts.ContainsKey(t)) { counts[t] = 0; order.Add(t); }
                        counts[t]++;
                    }
                }

                if (order.Count == 0) return "";

                // Biggest kin first; a stable tie-break on first-seen order so
                // the same deck always reads the same way.
                order.Sort((a, b) => counts[b].CompareTo(counts[a]));

                var sb = new System.Text.StringBuilder();
                foreach (var t in order)
                {
                    int n = counts[t];
                    sb.Append(Vocabulary.NodeScreens.CardS(t, n));
                }

                string line = sb.ToString();
                _log?.LogInfo($"IKMA NODE: deck by kin —{line}");
                return line;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: counting the deck by kin threw {e.GetType().Name}.");
                return "";
            }
        }

        /// <summary>Zamar's wording, 0.7.258, for every route back down.</summary>
        private static string BACK_TO_CARVINGS => Vocabulary.NodeScreens.WoodcarvingSelection;

        /// <summary>
        /// Backspace out of the deck, when the deck was opened from here.
        /// </summary>
        /// <remarks>
        /// 0.7.258. DeckViewReader.Close CLIMBS back down, and his log is a wall
        /// of the game refusing it:
        ///
        ///   IKMA VIEW: LookDown was not taken at MapDeckReview; retrying...
        ///   IKMA VIEW: LookDown refused at MapDeckReview; waiting 0.30s...
        ///   ...and on, until the camera ended up at the bench.
        ///
        /// The Woodcarver locks the view controller, so the ladder is not
        /// walkable in either direction here — the same fact that made A and
        /// Shift+Up switch rather than climb. This is the third time that has
        /// been the answer on this screen, so leaving now uses the same door it
        /// came in by.
        /// </remarks>
        public static void CloseDeckView()
        {
            var vm = Views();
            if (vm == null) return;

            try { vm.SwitchToView(View.Default); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: leaving the deck threw {e.GetType().Name}.");
                return;
            }

            _index   = NOWHERE;
            _hovered = null;
            ReleaseHover();

            _log?.LogInfo("IKMA NODE: deck view closed back to the Woodcarver.");
            Speech.Browse(BACK_TO_CARVINGS);
        }

        /// <summary>True while the backpack log is the view on show.</summary>
        public static bool InBackpackView
        {
            get
            {
                var vm = Views();
                if (vm == null) return false;
                try { return vm.CurrentView == View.TotemInventory; } catch { return false; }
            }
        }

        private static ViewManager Views()
        {
            try { return Singleton<ViewManager>.Instance; } catch { return null; }
        }

        private static int OrderOf(MainInputInteractable it)
        {
            string raw = null;
            try { raw = it.gameObject.name; } catch { }
            if (raw == null) return 99;

            int order;
            return _partOrder.TryGetValue(raw.Trim().ToLowerInvariant(), out order) ? order : 99;
        }

        private static string PartName(MainInputInteractable it)
        {
            if (it == null) return null;

            string raw = null;
            try { raw = it.gameObject.name; } catch { }
            if (string.IsNullOrEmpty(raw)) return null;

            string key = raw.Trim().ToLowerInvariant();

            // 0.7.330 — the Trapper's pelts and knife are named by the game.
            string ware = TrapperWareName(it);
            if (!string.IsNullOrEmpty(ware)) return ware;

            // 0.7.336 — an item pickup slot is named by the item sitting in
            // it. 0.7.335 read "Item slot left" off the Unity object name and
            // never said which item was there. Only consumables: a totem
            // piece on the same kind of slot has no rulebook name.
            try
            {
                var itemSlot = it as ItemSlot;
                var itemData = itemSlot?.Item?.Data as ConsumableItemData;
                if (itemData != null && !string.IsNullOrEmpty(itemData.rulebookName))
                    return HotkeyManager.ItemNameAndDescription(itemData);
            }
            catch { }

            // 0.7.328 — the boon card is named by the game (BoonData), so it
            // never reaches the "no approved name" warning below.
            try
            {
                var boonCard = it as SelectableCard;
                if (boonCard != null && boonCard.Info != null &&
                    boonCard.Info.boon != BoonData.Type.None)
                {
                    string boonName = CardReader.BoonName(boonCard.Info.boon);
                    if (!string.IsNullOrEmpty(boonName)) return boonName;
                }
            }
            catch { }

            string spoken;
            // The screen-scoped table wins: it exists precisely for names that
            // mean something different here than they do elsewhere.
            if (_partNamesByScreen.TryGetValue($"{_screenName}|{key}", out spoken))
            {
                // The campfire's two lines are composed rather than fixed:
                // they name the card that is actually there and the stat the
                // fire is actually offering, both read from the game.
                if (spoken == "CAMPFIRE_CHOOSE") return CampfireChooseLine();
                // HIS WORDING, 0.7.154 — no longer provisional, and no longer
                // naming the card. "You withdraw Mealworm." described the
                // OUTCOME of choosing this option; the option itself is a thing
                // you can pick, and a browse read names the option.
                if (spoken == "CAMPFIRE_WITHDRAW") return Vocabulary.NodeScreens.WithdrawFromTheCampfire;
                if (spoken == "CAMPFIRE_SLOT_LABEL") return Vocabulary.NodeScreens.Fire;
                if (_screenName == Vocabulary.Campfire && key == "confirmbutton") return CampfireConfirmLine();
                if (spoken == "MYCO_PAIR") return MycologistPairLine(it);

                return spoken;
            }
            if (_partNames.TryGetValue(key, out spoken)) return spoken;

            // ==================================================================
            // AN UNNAMED PART IS STILL A PART. (0.7.248.)
            //
            // Zamar, softlocked at the Woodcarver with three carvings in front
            // of him and every arrow answering "Nothing to choose here":
            //
            //   "Can you get this and all future nodes stood up please? I want
            //    to at least be able to interact with them once I encounter
            //    them."
            //
            // He is right and this was the wrong end to fail at. A name Zamar
            // has not written yet is a WORDING debt. Refusing to offer the
            // control at all turns it into a lockout — the player cannot leave
            // the screen and cannot finish it, on a node the game is plainly
            // waiting on. Withholding what a sighted player can reach is the
            // parity failure this file already names twice.
            //
            // So the fallback is a readable form of the object's own name, and
            // the warning below stays exactly as it was: the log line is still
            // the worklist, and every screen that reaches it still owes a word.
            // ==================================================================
            // ONCE PER NAME PER SCREEN. PartName is called on every browse, so
            // an unnamed screen printed this warning on every arrow press —
            // eight copies of it in his 0.7.248 log for three carvings. The
            // worklist wants one line per debt, not one per keystroke.
            if (_loggedCandidates.Add($"unnamed|{_screenName}|{key}"))
                _log?.LogWarning(
                    $"IKMA NODE: no approved name for '{raw}' on {_screenName}. " +
                    "Ask Zamar for a word before this screen ships.");

            return FallbackPartName(raw);
        }

        /// <summary>
        /// A Unity object name, said out loud as well as it can be without a
        /// word from Zamar: "ItemSlot_Left" becomes "Item slot left",
        /// "ReturnToInventoryInteractable" becomes "Return to inventory".
        /// Provisional by definition — every one of these is on the worklist.
        /// </summary>
        // ==================================================================
        // A PAIR OF CARDS, READ AS BOTH CARDS. (0.7.288.)
        //
        // ZAMAR'S SHAPE, 0.7.287, verbatim: "Pair of [card name]s. [Card
        // name] 1, [stat/sigil info]. [Card name] 2, [stat/sigil info]."
        //
        // Both halves are asked every time rather than assumed identical.
        // DuplicateMergeSequencer builds each choice from
        // CardLoader.GetCardByName, so today they always match — but the
        // merge itself reads LeftCard.Info and RightCard.Info separately and
        // adds their stats together, so a line that described one and spoke
        // for both would be a claim the game does not make.
        //
        // THE STATS COME FROM THE ONE COMPOSER. CardReader.DescribeCardInfo
        // is what every card read in the mod goes through, so the pair reads
        // in the same words as the same card anywhere else. Its output opens
        // with the card's own name; that opening is lifted off and replaced
        // with "<name> 1," so the numbering he asked for lands without a
        // second composer existing for it.
        // ==================================================================
        private static string MycologistPairLine(MainInputInteractable it)
        {
            try
            {
                var pair = it as SelectableCardPair;
                if (pair == null) return null;

                CardInfo left = null, right = null;
                try { left  = pair.LeftCard?.Info;  } catch { }
                try { right = pair.RightCard?.Info; } catch { }

                if (left == null && right == null)
                {
                    _log?.LogInfo("IKMA NODE: card pair holds no readable card — falling back to its object name.");
                    return null;
                }

                // 0.7.329, Zamar: "Fused should be ignored in this pair call.
                // Should have just been 'Pair of Flying Ants'." The pair is
                // named by the card KIND; each card's own read below still
                // says Fused where it is.
                string leadName = CardReader.BaseCardName((left ?? right));
                if (string.IsNullOrEmpty(leadName)) return null;

                var sb = new System.Text.StringBuilder();
                sb.Append(Vocabulary.NodeScreens.PairOf(Pluralise(leadName)));

                int n = 0;
                foreach (var info in new[] { left, right })
                {
                    if (info == null) continue;
                    n++;
                    string full = CardReader.DescribeCardInfo(info);
                    if (string.IsNullOrEmpty(full)) continue;
                    sb.Append($" {WithOrdinal(info, full, n)}");
                }

                return sb.ToString();
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: card pair read failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// "Flying Ant. Cost: 1 blood. 0, 1. ..." -> "Flying Ant 1, Cost: 1
        /// blood. 0, 1. ...". The name is lifted off the front of the shared
        /// composer's output rather than the rest being rebuilt here, so the
        /// two can never disagree about how a card is described.
        /// </summary>
        private static string WithOrdinal(CardInfo info, string full, int n)
        {
            string name = null;
            try { name = CardReader.CardName(info); } catch { }
            if (string.IsNullOrEmpty(name) || !full.StartsWith(name, System.StringComparison.Ordinal))
                return full;

            string rest = full.Substring(name.Length).TrimStart('.', ' ');
            return rest.Length == 0 ? $"{name} {n}." : Vocabulary.NodeScreens.NameNumberRest(name, n, rest);
        }

        /// <summary>
        /// The plural in "Pair of Flying Ants", which is Zamar's wording.
        /// </summary>
        /// <remarks>
        /// A bare "+s" is wrong on the card names that already end in one —
        /// Ouroboros, Bees, Mantis are all real cards and all of them can be
        /// offered here. Those keep their own form ("Pair of Mantis") rather
        /// than gaining a second s. That is the whole rule; nothing else is
        /// inflected, because every other case would be a guess about a name
        /// the game chose.
        /// </remarks>
        private static string Pluralise(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return Vocabulary.NodeScreens.Pluralise(name.EndsWith("s", System.StringComparison.OrdinalIgnoreCase), name);
        }

        private static string FallbackPartName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Vocabulary.UnnamedOption;

            // Underscores and camel humps are both word breaks.
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim())
            {
                if (c == '_' || c == '-') { sb.Append(' '); continue; }
                if (char.IsUpper(c) && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                    sb.Append(' ');
                sb.Append(c);
            }

            var words = new List<string>(
                sb.ToString().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));

            // Unity suffixes are plumbing, not words. Dropped from the end only,
            // so a screen that genuinely has a "Button" in its name keeps it.
            while (words.Count > 1)
            {
                string last = words[words.Count - 1].ToLowerInvariant();
                if (last == "interactable" || last == "interactible" ||
                    last == "object" || last == "obj" || last == "gameobject")
                {
                    words.RemoveAt(words.Count - 1);
                    continue;
                }
                break;
            }

            if (words.Count == 0) return Vocabulary.UnnamedOption;

            for (int i = 0; i < words.Count; i++) words[i] = words[i].ToLowerInvariant();
            string joined = string.Join(" ", words.ToArray());
            return char.ToUpperInvariant(joined[0]) + joined.Substring(1);
        }

        public static void Begin(Component sequencer, string screenName)
        {
            // ONE READER PER SEQUENCER. The stone's start patch fired twice in
            // his 0.7.101 log, which started two settle coroutines and read the
            // screen out twice over. Re-entering for the SAME sequencer is a
            // duplicate, not a new screen.
            if (ReferenceEquals(_sequencer, sequencer) && sequencer != null)
            {
                _log?.LogInfo($"IKMA NODE: {screenName} — already active, ignoring duplicate start.");
                return;
            }

            _carvingSlotsField    = null;
            _carvingSlotsResolved = false;

            _sequencer  = sequencer;
            _screenName = screenName;

            // ==================================================================
            // THE CURSOR STARTS NOWHERE. A STANDING RULE, NOT A PER-SCREEN ONE.
            // (0.7.249.)
            //
            // Zamar, at the Woodcarver: "This needs to be a standing rule, to
            // always default to -1 or nowhere. Hitting right brought me to item
            // slot center."
            //
            // _index = 0 on arrival means the cursor is already ON option one
            // without having said so, and the first arrow therefore SKIPS it.
            // He pressed right once and landed on the middle carving, never
            // having been told the left one existed. Every other reader in the
            // mod learned this in 0.7.245; this file was missed because its
            // arrival line was deleted in 0.7.134 and nothing here read _index
            // out loud, so the off-by-one had nothing to show it.
            //
            // NOWHERE is already this file's own constant — _cardIndex has used
            // it since the card layer was written. The parts index now agrees.
            // ==================================================================
            _index      = NOWHERE;
            _hovered    = null;
            LastTotemAssembledLine = null;   // Session 34

            _log?.LogInfo($"IKMA NODE: {screenName} — reader active.");

            // A fresh screen starts the I cycle from item 1, not from wherever
            // the last screen left it.
            BoardReader.ResetItemCycle();
            CombatAnnouncer.Instance?.StartCoroutine(AnnounceWhenSettled());
        }

        public static void End()
        {
            if (_sequencer == null) return;
            _log?.LogInfo($"IKMA NODE: {_screenName} — reader released.");
            ReleaseHover();
            ReleaseCardHover();
            _cardIndex  = NOWHERE;
            _sequencer  = null;
            _screenName = null;
            _hovered    = null;
            _partsSeen  = false;
            _emptyFor   = 0f;
            _ritualHost = null;
            _ritualHostName   = null;
            _ritualHostSigils = null;
            _sacrificedName     = null;
            _sacrificeAnnounced = false;
            _arrivalPending     = false;
            _boostApplied     = false;
            _campfireIsAttackMod = null;
            _loggedCandidates.Clear();
        }

        /// <summary>
        /// Everything on the screen that can be acted on, in a stable order.
        ///
        /// LEFT TO RIGHT, on world x — the order a sighted player reads them,
        /// and the same rule the map paths needed twice before it stuck.
        /// Inactive parts are excluded: the confirm stone is not interactable
        /// until both slots are filled, and offering it early would be a key
        /// that does nothing.
        /// </summary>
        // One line per candidate per screen. Cleared when the reader releases,
        // so the next visit to the same node reports afresh.
        private static readonly HashSet<string> _loggedCandidates = new HashSet<string>();

        /// <summary>
        /// Has this screen's confirm stone been pressed? The game's own
        /// answer — ConfirmStoneButton.SelectionConfirmed. (0.7.326.)
        /// </summary>
        // How many parts Parts() left out only because the stone was pressed.
        // 0.7.327: 0.7.326 dropped them from the list and Tick read the empty
        // list as "screen over", released the Bone Lord before the boon card
        // landed, and softlocked him. They still count as present for that test.
        private static int _donePartsPresent;

        private static bool StoneConfirmed()
        {
            if (_sequencer == null) return false;
            try
            {
                foreach (var stone in _sequencer.GetComponentsInChildren<ConfirmStoneButton>(true))
                    if (stone != null && stone.SelectionConfirmed) return true;
            }
            catch { }
            return false;
        }

        public static List<MainInputInteractable> Parts()
        {
            var result = new List<MainInputInteractable>();
            if (_sequencer == null) return result;

            bool ritualBegun = StoneConfirmed();
            _donePartsPresent = 0;

            try
            {
                foreach (var it in _sequencer.GetComponentsInChildren<MainInputInteractable>(true))
                {
                    if (it == null) continue;

                    string raw = null;
                    try { raw = it.gameObject.name; } catch { }
                    if (raw == null) continue;

                    bool active  = false;
                    bool enabled = false;
                    try { active  = it.gameObject.activeInHierarchy; } catch { }
                    try { enabled = it.Enabled; } catch { }

                    bool named = _partNames.ContainsKey(raw.Trim().ToLowerInvariant())
                              || _partNamesByScreen.ContainsKey($"{_screenName}|{raw.Trim().ToLowerInvariant()}");

                    // EVERY CANDIDATE IS NAMED IN THE LOG, ONCE. (0.7.127.)
                    //
                    // THIS FILTER HAS BEEN DROPPING THINGS IN SILENCE and that
                    // is why the campfire's confirm has cost three round trips.
                    // Zamar: "the arrow keys function now during conversation,
                    // but not afterwards. They need to be able to browse the
                    // option for sending a card to the campfire." Three separate
                    // reasons could produce that — the object is inactive, it is
                    // not Enabled yet, or its GameObject name is not in the
                    // table — and a silent `continue` makes all three look
                    // identical from outside.
                    //
                    // This project's own rule is that the log is how the name
                    // table grows, and this loop was the one place breaking it.
                    // Now every candidate reports its name and all three gates,
                    // so the next log says which one is closing.
                    //
                    // Once per name per screen: the walk runs every browse and
                    // every tick, and 82,761 lines of one warning is a mistake
                    // this project has already made.
                    // LOGGED ON STATE CHANGE, NOT ONCE PER NAME. (0.7.128.)
                    //
                    // 0.7.127 logged the first state each object was ever seen
                    // in, which turned out to hide the only thing worth seeing:
                    // ConfirmButton was reported active=False and then became
                    // usable later with no second line. A gate that opens and
                    // closes is exactly what this screen does, so the key
                    // includes the state and a transition prints.
                    //
                    // Still bounded — an object has three flags, so it can
                    // produce at most a handful of lines per screen, not one per
                    // frame.
                    string seenKey = $"{_screenName}|{raw}|{active}{enabled}{named}";
                    if (_loggedCandidates.Add(seenKey))
                    {
                        if (VerboseNodeDiagnostics)
                            _log?.LogInfo(
                                $"IKMA NODE: candidate '{raw}' ({it.GetType().Name}) on {_screenName} — " +
                                $"active={active} enabled={enabled} named={named}.");
                    }

                    // ==========================================================
                    // `Enabled` IS NOT "CAN BE ACTED ON", AND THAT IS WHY THE
                    // CAMPFIRE HAS NEVER BROWSED. (0.7.128.)
                    //
                    // The 0.7.127 candidate dump, which is what settled it:
                    //
                    //   'Card (Stoat)'    (SelectableCard) active=True enabled=False
                    //   'Card (Wolf)'     (SelectableCard) active=True enabled=False
                    //   ... every card in his deck, all enabled=False
                    //   'DeckPile'        (CardPile)       active=True enabled=False
                    //
                    // Those cards are on the table and a mouse click picks them
                    // up. `Enabled` reads false for all of them, so the gate was
                    // rejecting things the player can plainly use, and Parts()
                    // came back empty on a screen full of options.
                    //
                    // OpenCards() HAS ALWAYS BEEN RIGHT AND IS THE MODEL. It
                    // tests activeInHierarchy and nothing else, and it is the one
                    // path on this screen that has worked all along — his log
                    // reads "choosing card 7 of 9" through it while Parts() was
                    // reporting zero.
                    //
                    // activeInHierarchy alone is also sufficient for the case
                    // `Enabled` was added for. The confirm stone is not merely
                    // disabled before both slots are filled, it is INACTIVE —
                    // the same dump shows 'ConfirmButton' at active=False. The
                    // game hides it rather than greying it out, so the honest
                    // question was the simpler one the whole time.
                    //
                    // `Enabled` is still read and still logged, because it is
                    // real information about an object and the next screen may
                    // be the one where it means something. It just does not
                    // decide anything.
                    // ==========================================================
                    if (!active) continue;

                    // ==========================================================
                    // ONCE THE STONE IS PRESSED, THE SLOTS AND THE STONE ARE DONE.
                    // (0.7.326.)
                    //
                    // Zamar, at the Bone Lord: "the card to sacrifice and begin
                    // ritual thing should be disabled after selecting begin
                    // sacrifice. No arrows to browse needed, just enter to pick
                    // up the bone card." His log has Begin Ritual activated seven
                    // times after the ritual had already run.
                    //
                    // ASKED OF THE GAME: ConfirmStoneButton.SelectionConfirmed is
                    // public, set true by the press and set false again only by
                    // the next WaitUntilConfirmation — which is exactly when the
                    // game asks again (the campfire's second rest). Every screen
                    // with a confirm stone inherits this: Bone Lord, sacrifice
                    // stone, campfire, copy card, Mycologists.
                    // ==========================================================
                    if (ritualBegun && (it is SelectCardFromDeckSlot || it is ConfirmStoneButton))
                    {
                        // Still on the table, so the screen is not over — see Tick.
                        _donePartsPresent++;
                        if (_loggedCandidates.Add($"confirmed|{_screenName}|{raw}"))
                            _log?.LogInfo(
                                $"IKMA NODE: '{raw}' is done — the stone has been pressed.");
                        continue;
                    }

                    // ==========================================================
                    // A CARD PILE IS FURNITURE. (0.7.254.)
                    //
                    // Zamar, browsing the sacrifice stone: "remove this from a
                    // browse option." His log has 'Merge card pile' read three
                    // times and activated FIVE times in a row with nothing
                    // happening — Enter on it does nothing because there is
                    // nothing there to do.
                    //
                    // MergeCardPile is a CardPile: the stack of his own deck
                    // sitting on the table. A sighted player looks at it and
                    // clicks the CARDS, which IKMA already reads through
                    // OpenCards. The pile itself was never an option and only
                    // became one when 0.7.248 let unnamed-but-enabled objects
                    // through; DeckPile at the campfire is the same object under
                    // another name and would have followed.
                    //
                    // Excluded by TYPE, so the next screen with a pile on it
                    // inherits the answer instead of rediscovering it.
                    // ==========================================================
                    // ==========================================================
                    // AN EMPTY SHELF IS NOT AN OPTION. (0.7.258.)
                    //
                    // Zamar, in the backpack: "The empty item slots should not
                    // be read out loud, just skipped." His log has "Item slot 6"
                    // and "Item slot 7" read aloud — seven shelves of which two
                    // hold a carving, and a sighted player sees five empty
                    // hooks and never considers them.
                    //
                    // ItemSlot.Item is the game's own record of whether anything
                    // is on the shelf, so this asks rather than guesses. Scoped
                    // to ItemSlot on purpose: an EMPTY SelectCardFromDeckSlot at
                    // the campfire or the stone is exactly the option the player
                    // is meant to press, and that is a different class.
                    // ==========================================================
                    var shelf = it as ItemSlot;
                    if (shelf != null)
                    {
                        bool holdsSomething = false;
                        try { holdsSomething = shelf.Item != null; } catch { }

                        if (!holdsSomething)
                        {
                            if (_loggedCandidates.Add($"empty|{_screenName}|{raw}"))
                                if (VerboseNodeDiagnostics)
                                    _log?.LogInfo($"IKMA NODE: '{raw}' is an empty slot — skipped.");
                            continue;
                        }
                    }

                    if (it is CardPile)
                    {
                        if (_loggedCandidates.Add($"pile|{_screenName}|{raw}"))
                            _log?.LogInfo(
                                $"IKMA NODE: '{raw}' is a card pile — furniture, not an option.");
                        continue;
                    }

                    // ==========================================================
                    // REACHABLE BY ITS OWN KEY, SO NOT IN THE ARROW LIST.
                    // (0.7.281.)
                    //
                    // Zamar, on hearing "Return to backpack" while arrowing the
                    // carvings: "That line is inaccurate and not needed. dont
                    // make the backpack a browsable option on the select screen.
                    // Only the A key."
                    //
                    // This is the DeckPile ruling again — "a part being present,
                    // active and enabled does not make it an OPTION" — with a
                    // second reason on top: the backpack already HAS a key, and
                    // offering the same destination twice makes the arrow list
                    // longer without making anything reachable that was not.
                    //
                    // 0.7.280 gave this part a better word when what it needed
                    // was to not be in the list. Naming a thing is a claim that
                    // it belongs there.
                    // ==========================================================
                    // Scenery first: a prop is not an option, and it must
                    // not reach the approved-name warning below either —
                    // naming it would be a claim it belongs in the list.
                    if (IsScenery(raw))
                    {
                        if (_loggedCandidates.Add($"scenery|{_screenName}|{raw}"))
                            _log?.LogInfo(
                                $"IKMA NODE: '{raw}' is scenery — a collider with nothing behind it, not an option.");
                        continue;
                    }

                    if (_notBrowsable.Contains($"{_screenName}|{raw.Trim().ToLowerInvariant()}"))
                    {
                        if (_loggedCandidates.Add($"ownkey|{_screenName}|{raw}"))
                            _log?.LogInfo(
                                $"IKMA NODE: '{raw}' has its own key — not offered in the arrows.");
                        continue;
                    }

                    // ==========================================================
                    // NAMED, OR ENABLED. (0.7.248.)
                    //
                    // `if (!named) continue;` is what softlocked the Woodcarver:
                    // its three carvings are ItemSlot_Left / _Center / _Right,
                    // no approved name exists for any of them, and Parts() came
                    // back empty for the whole node. See PartName for why a
                    // missing word may not cost the player the control.
                    //
                    // But `active` alone is too wide HERE. That same screen has
                    // ItemSlot_1 through _7, CompletedTotemSlot and
                    // ReturnToInventoryInteractable all sitting active in the
                    // hierarchy, and his log separates them cleanly:
                    //
                    //   ItemSlot_Left/Center/Right   active=True enabled=True
                    //   ItemSlot_1..7, Completed...  active=True enabled=False
                    //
                    // Three carvings to choose between, nine other things that
                    // are merely present. So: a part IKMA has a word for is
                    // included on `active` exactly as before — no heard screen
                    // changes — and a part it has no word for must also be
                    // Enabled to be offered. That is the only signal an unnamed
                    // object carries, and 0.7.128's finding stands untouched:
                    // Enabled still decides nothing for anything named.
                    // ==========================================================
                    if (!named && !enabled) continue;

                    // ==========================================================
                    // ONCE A CARD IS BY THE FIRE, THE SLOT IS NOT A CHOICE.
                    // (0.7.133 — and this one is a parity violation IKMA caused.)
                    //
                    // Zamar: "By game rules youre not allowed to change the card
                    // by the campfire once you select the first one. I was able
                    // to with our mod."
                    //
                    // THIS IS THE CAGED WOLF AGAIN and it is the worse direction
                    // of the two. Parity means never withholding what a sighted
                    // player can reach AND never granting what they cannot; I
                    // put SelectionSlot in the approved-name table three builds
                    // ago and thereby offered a keyboard-only player a do-over
                    // the game does not give anyone.
                    //
                    // ASK THE GAME WHETHER THE SLOT IS STILL A QUESTION. A
                    // SelectCardFromDeckSlot with a Card in it has been answered
                    // — SelectCardFromDeckSlot.Card is PUBLIC and is the game's
                    // own record of that. No flag of IKMA's, nothing to strand.
                    //
                    // THE RULE, AND IT IS GENERAL: `active && enabled` says an
                    // object EXISTS and can be poked. It does not say the game is
                    // still asking. Before offering something, ask what the
                    // answer to it already is.
                    // ==========================================================
                    // SWAPPING IS ALLOWED UNTIL THE CARD IS SENT. (0.7.136.)
                    //
                    // 0.7.133 blocked a filled slot outright, on his line "youre
                    // not allowed to change the card by the campfire once you
                    // select the first one." I read that as "once you PICK one"
                    // and it means "once you REST one":
                    //
                    //   "I wasnt able to change my choice. I mechanically should
                    //    be able to until choosing to actually send one. From
                    //    then on I can only withdraw it or keep going."
                    //
                    // So the line between the two states is the first successful
                    // boost, not the first selection — and the game tells IKMA
                    // exactly when that happens, through the ApplyModToCard
                    // postfix that already exists for the amount cross-check.
                    //
                    // THE MISTAKE WORTH RECORDING: I turned one sentence of his
                    // into a rule without checking which event it hinged on, and
                    // shipped a restriction the game does not have. Withholding
                    // an action a sighted player has is the same parity failure
                    // as granting one they do not — it just looks tidier.
                    var deckSlot = it as SelectCardFromDeckSlot;
                    if (deckSlot != null && _boostApplied)
                    {
                        if (_loggedCandidates.Add($"rested|{_screenName}|{raw}"))
                            _log?.LogInfo(
                                $"IKMA NODE: '{raw}' has already rested — the choice is locked in.");
                        continue;
                    }

                    result.Add(it);
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: walking {_screenName} threw {e.GetType().Name}.");
            }

            // A FIXED ORDER, NOT A SORT ON POSITION. Zamar, 0.7.98: "the Begin
            // Ritual option seemed like it was out of order. Felt like it was
            // Host Card > Begin Ritual > Card to Sacrifice."
            //
            // World x is right for a ROW of things drawn left to right — the map
            // paths, a set of dials. It is wrong here: the confirm stone sits
            // between the two slots physically, so reading left to right puts
            // the commit action in the middle of the two choices that feed it.
            //
            // The order a screen should be read in is the order its steps
            // happen, and that is authored, not measured.
            result.Sort((a, b) => OrderOf(a).CompareTo(OrderOf(b)));

            // The Trapper's pelts in the game's own order, left to right, knife
            // last. A bought pelt is replaced by a NEW object, so hierarchy
            // order would shuffle after every purchase.
            if (TrapperSeq != null)
                result.Sort((a, b) => TrapperOrder(a).CompareTo(TrapperOrder(b)));

            if (_screenName == Vocabulary.Mycologists) MycologistsPhaseFilter(result);

            return result;
        }

        // ==================================================================
        // THE MYCOLOGISTS HAVE TWO PHASES AND THEY MUST NOT OVERLAP.
        // (0.7.291.)
        //
        // Zamar, 0.7.290: "Select a pair of cards cant be accessible on the
        // selection screen only the cards, or else I can multi select them
        // and break things." His screenshot is two pairs stacked on the
        // table, which is what fifteen presses of the slot while the picker
        // was open produced. And on the other side: "After choosing a card
        // there's 3 options. There should only be two."
        //
        // The two phases, and the game answers which one is live:
        //
        //   PICKER OPEN   SelectableCardPair objects exist on the table and
        //                 SelectedPair is null. The game is waiting for one
        //                 of the pairs to be clicked. Only the pairs are
        //                 options; the slot that opened the picker is not,
        //                 and neither is the confirm.
        //   PAIR PLACED   SelectedPair is set. The pairs are done — the one
        //                 that was chosen is sitting on the stone and the
        //                 rest are gone. The options are the slot (press it
        //                 again to choose differently, which the game allows)
        //                 and the confirm.
        //
        // This is the campfire slot rule one level up: `active && enabled`
        // says a thing EXISTS, not that the game is still asking about it.
        // Asked of SelectCardPairFromDeckSlot.SelectedPair, which is PUBLIC,
        // every time, so a pair placed with the mouse reads the same way.
        // ==================================================================
        private static void MycologistsPhaseFilter(List<MainInputInteractable> parts)
        {
            try
            {
                bool pairOnStone = false;
                bool sawPair = false;

                foreach (var part in parts)
                {
                    var slot = part as SelectCardPairFromDeckSlot;
                    if (slot != null)
                    {
                        try { pairOnStone = slot.SelectedPair != null; } catch { }
                    }
                    if (part is SelectableCardPair) sawPair = true;
                }

                if (!sawPair && !pairOnStone) return;   // nothing to choose yet

                int before = parts.Count;

                if (!pairOnStone)
                    parts.RemoveAll(x => !(x is SelectableCardPair));
                else
                    parts.RemoveAll(x => x is SelectableCardPair);

                if (before != parts.Count && VerboseNodeDiagnostics)
                    _log?.LogInfo(
                        $"IKMA NODE: Mycologists {(pairOnStone ? "pair placed" : "picker open")} — " +
                        $"{before} part(s) narrowed to {parts.Count}.");
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: Mycologists phase filter failed: {e.GetType().Name}.");
            }
        }

        private static System.Collections.IEnumerator AnnounceWhenSettled()
        {
            // The stone's parts appear on a coroutine. Wait for the count to
            // stop CHANGING rather than for a clock — the settled rule, and the
            // one the deck view had to learn twice.
            // STABLE AND NOT EMPTY. Zero parts is what the screen looks like
            // before it has built itself, so counting zero as settled announced
            // "Sacrifice stone. 0 options." a moment before the two slots
            // appeared — the same mistake the pause menu made and already
            // guards against with n > 0.
            int last = -1, stable = 0;
            for (int i = 0; i < 60 && stable < 3; i++)
            {
                yield return new WaitForSeconds(0.05f);
                if (_sequencer == null) yield break;
                int n = Parts().Count;
                if (n == last && n > 0) stable++; else { stable = 0; last = n; }
            }

            if (_sequencer == null) yield break;

            _index = NOWHERE;
            _log?.LogInfo($"IKMA NODE: {_screenName} settled with {Parts().Count} part(s).");

            // ==================================================================
            // THE ARRIVAL ANNOUNCEMENT IS DELETED. (0.7.134.)
            //
            // Zamar: "I think remove this line all together. It keeps delaying
            // the leshy speech, ruining that moment."
            //
            // FOUR BUILDS OF TRYING TO TIME IT IS THE ARGUMENT FOR CUTTING IT.
            // It interrupted Leshy mid-sentence (0.7.121), was queued behind him
            // (0.7.122), lost its "0 options" (0.7.122), lost its screen name
            // (0.7.129), and was finally held until the conversation ended
            // (0.7.133) — and it was still wrong, because there is no good
            // moment for it. Ahead of the speech it steps on a performance;
            // behind it, it is a delay before the player can act on a screen
            // they can already hear.
            //
            // NOTHING IS LOST. The map announced the node on the way in and
            // "Arrived at Campfire" was already spoken. H says what the screen is
            // and what the keys do, at any moment, on demand. The one thing this
            // line added was an option count — which is an offer, and the arrows
            // make that same offer the instant the player uses them.
            //
            // THE GENERAL SHAPE, worth carrying to the next reader: a line that
            // needs perfect timing to be welcome is usually a line that should
            // be a key.
            // ==================================================================
            CombatAnnouncer.DropCommentary($"arrived at {_screenName}");
            HoverCurrent(Parts());

            // 0.7.327, Zamar's wording: the Bone Lord was silent until an
            // arrow was pressed. "I want to hear 'Altar of the Bone Lord.
            // Arrows to browse.' When loading in."
            // 0.7.330 — said once the screen has something on it and nobody is
            // talking, from Tick. The Trapper settles at zero parts in the middle
            // of Leshy's lines, and speaking then would talk over him.
            _arrivalPending = _arrivalLines.ContainsKey(_screenName ?? "");
        }

        private static bool _arrivalPending;

        private static void SpeakArrivalWhenReady(int partCount)
        {
            if (!_arrivalPending || partCount <= 0 || DialogueWaiting()) return;
            _arrivalPending = false;
            string arrival;
            // 0.7.360 — QUEUED, NOT BROWSED. Zamar: "The You are gifted line
            // shouldnt have been stomped." Spoken as Browse it was the lowest
            // tier, and the Trapper's free-pelt line ("THE FIRST 'N'S FREE.")
            // arrived a moment later and cut it. As a queued result, dialogue
            // queues behind it instead.
            if (_arrivalLines.TryGetValue(_screenName ?? "", out arrival))
                Speech.Result(arrival);
        }

        /// <summary>
        /// The screen's name as spoken, where it differs from the internal key.
        /// 0.7.328, Zamar: the Bone Lord's H line leads "Altar of the Bone Lord".
        /// </summary>
        private static string ScreenTitle()
        {
            if (_screenName == Vocabulary.BoneLord) return Vocabulary.NodeScreens.AltarOfTheBoneLord;
            return _screenName;
        }

        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _arrivalLines => Loc.PerLanguage(ref _arrivalLinesCache, ref _arrivalLinesLanguage, Build_arrivalLines);
        private static Dictionary<string, string> Build_arrivalLines() =>
            new Dictionary<string, string>
        {
            // 0.7.328, his addition: "add Enter to confirm after arrows."
            { Vocabulary.BoneLord, Vocabulary.NodeScreens.AltarOfTheBone },
            // 0.7.330, Zamar's wording; 0.7.331 his addition of the free pelt.
            // GiveFreePeltSequence runs on every visit, so it is always true.
            { Vocabulary.Trapper,
              // 0.7.334: the Trader sentences cut, his call.
              Vocabulary.NodeScreens.TrapperYouAreGifted },
        };
        private static Dictionary<string, string> _arrivalLinesCache;
        private static string _arrivalLinesLanguage;

        private static void ReleaseHover()
        {
            ReleaseHighlight();
            if (_hovered == null) return;
            try { _hovered.CursorExit(); } catch { }
            _hovered = null;
        }

        /// <summary>
        /// Move the game's own cursor onto the browsed part. Zamar: "they need
        /// to highlight the different options as if using the mouse cursor".
        /// CursorEnter is the primitive the whole mod uses for this, and it
        /// dispatches into the part's own highlight and sound.
        /// </summary>
        // ==================================================================
        // A HOVER IS NOT A FACT IKMA GETS TO REMEMBER. (0.7.155.)
        //
        // Zamar: "'Choose a card to warm...' voice line should have had the
        // hover and doesnt." His 0.7.154 log has the hover applied and then
        // going stale:
        //
        //   NODE: Campfire settled with 1 part(s).
        //   NODE: 'SelectionSlot' highlights via the part itself.   <- applied
        //   QUEUE (dialogue): Leshy: "WARM A CREATURE BY THE FIRE?..."
        //   SPEAK: Choose a card to warm by the campfire...          x3
        //
        // SelectionSlot IS a HighlightedInteractable, CursorEnter reached it,
        // and the border still was not there by the time he arrowed to it. The
        // game had reset the slot's state in between — it runs Leshy's line and
        // puts the screen back to Interactable afterwards, which drops the
        // highlight.
        //
        // IKMA then refused to re-apply it, because `_hovered` still pointed at
        // that part and the identity check called it done. On a one-part screen
        // that is permanent: every arrow press lands on the same part and
        // returns early, so the border can never come back.
        //
        // THE CACHED-STATE RULE, AGAIN — the same one written thirty lines below
        // about the card layout: nothing is remembered about what the screen is
        // showing, the game is asked every time. A remembered hover is a
        // remembered claim about the game's state, and the game had changed it.
        //
        // So a BROWSE always re-asserts: exit, then enter. That is precisely
        // what a mouse leaving and re-entering does, so it cannot put the game
        // anywhere a mouse could not. The sound comes with it, which is correct
        // — a mouse re-entering plays it too.
        // ==================================================================
        private static void HoverCurrent(List<MainInputInteractable> parts, bool reassert = false)
        {
            if (parts == null || parts.Count == 0) return;
            if (_index < 0 || _index >= parts.Count) return;

            var target = parts[_index];
            if (!reassert && ReferenceEquals(target, _hovered)) return;

            if (reassert && ReferenceEquals(target, _hovered))
            {
                string n = "?";
                try { n = target.gameObject.name; } catch { }
                _log?.LogInfo($"IKMA NODE: re-asserting hover on '{n}'.");
            }

            ReleaseHover();

            try
            {
                target.CursorEnter();
                _hovered = target;
                HighlightWith(target);
                PointCursorAt(target);
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: hover threw {e.GetType().Name}.");
            }
        }

        // ==================================================================
        // THE BORDER. (0.7.154.)
        //
        // Zamar, at the campfire's withdraw option: "When I highlight this
        // option, the border around it does not change like it would when I
        // mouse hover over it."
        //
        // WHY THE EXISTING HOVER WAS NOT ENOUGH. The border is drawn by
        // HighlightedInteractable, which OVERRIDES OnCursorEnter/OnCursorExit
        // and tweens its renderers to highlightedColor (dump_input.txt; its
        // State enum is Hidden / NonInteractable / Interactable / Highlighted).
        // The campfire's slot and confirm stone both DERIVE from it, so
        // CursorEnter highlights them and always has.
        //
        // RetrieveCardInteractable does not. It is a
        // GenericMainInputInteractable — straight off MainInputInteractable,
        // with no fields, no renderers and no highlight of its own
        // (dump_act1_nodes_interaction.txt). So the part IKMA hovers is a bare
        // collider, and whatever the mouse lights up is a DIFFERENT object.
        //
        // Rather than guess which, ask Unity for the HighlightedInteractable
        // nearest this part — itself, then its children, then its parent — and
        // drive that with the game's own CursorEnter. GetComponent* on one
        // object is the permitted lookup; the banned ones are the scene-wide
        // searches.
        //
        // The log names what it found and what it drove, so if the border still
        // does not move his next log says exactly which object was hovered and
        // this stops being guesswork.
        // ==================================================================
        private static HighlightedInteractable _highlighted;

        private static void HighlightWith(MainInputInteractable part)
        {
            _highlighted = null;
            if (part == null) return;

            // Already one itself? Then CursorEnter above has done the job and a
            // second call would be the same object twice.
            if (part is HighlightedInteractable)
            {
                LogHighlightOnce(part, "the part itself");
                return;
            }

            HighlightedInteractable found = null;
            string where = null;
            try
            {
                found = part.GetComponentInChildren<HighlightedInteractable>();
                where = "a child";
                if (found == null)
                {
                    found = part.GetComponentInParent<HighlightedInteractable>();
                    where = "a parent";
                }
            }
            catch { found = null; }

            if (found == null)
            {
                LogHighlightOnce(part, "NOTHING — this part has no highlight anywhere near it");
                return;
            }

            try
            {
                found.CursorEnter();
                _highlighted = found;
                LogHighlightOnce(part, $"'{found.gameObject.name}' ({where})");
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: highlight threw {e.GetType().Name}.");
            }
        }

        // ==================================================================
        // THE CURSOR IS THE AUTHORITY, NOT THE INTERACTABLE. (0.7.158.)
        //
        // Two builds of mine went at this from the wrong end and he paid for
        // both. Writing it down properly.
        //
        // 0.7.157 read HighlightedInteractable.currentState and called the
        // game's own ShowState to put the border back. The reflection resolved,
        // the state read, ShowState ran -- his log says so -- and the border
        // still blinked out. Worse, after the first correction currentState
        // said Highlighted forever, so the hold stopped acting while the border
        // was plainly gone: "the border doesnt light after the first success."
        //
        // I TRUSTED A STATE FIELD OVER THE VISIBLE RESULT. Same mistake this
        // project has recorded twice already in a different costume -- a patch
        // registering proves nothing about whether it can run, a call
        // succeeding proves nothing about whether the game did the thing.
        //
        // WHAT ACTUALLY OWNS THE BORDER. InteractionCursor.ManagedUpdate runs
        // every frame and calls UpdateCurrentInteractable, which RAYCASTS from
        // the cursor's position (dump_cabin.txt). Whatever that ray hits gets
        // CursorEnter; whatever it stops hitting gets CursorExit. So the frame
        // after IKMA enters a part by hand, the cursor's own update finds the
        // real cursor somewhere else and exits it again. That is the blip,
        // exactly, and no amount of state-poking can win against a system that
        // recomputes the answer every frame from a position IKMA never moved.
        //
        // A mouse keeps the border because the CURSOR IS STILL THERE.
        //
        // So move the cursor. MatchWorldPosition(Vector3) is PUBLIC on
        // InteractionCursor and is the game's own way of putting it somewhere;
        // once it is over the part, the game does the entering, the border, the
        // sound, the cursor type and anything else in there, and it HOLDS them
        // because it re-derives them every frame from a position that is now
        // correct. The first rule, one layer further out than I had been
        // looking: IKMA was driving the effect instead of the cause.
        //
        // Checked against the game's own answer rather than assumed: if
        // CurrentInteractable is not the part IKMA is on, point the cursor
        // again. Correct only when wrong -- so a real mouse is not fought every
        // frame, and the log names it when the cursor cannot be made to land.
        // ==================================================================
        private static MainInputInteractable _cursorTarget;
        private static int _cursorMisses;

        private static void PointCursorAt(MainInputInteractable part)
        {
            _cursorTarget = part;
            _cursorMisses = 0;
            HoldCursor();
        }

        private static void HoldCursor()
        {
            var part = _cursorTarget;
            if (part == null) return;

            // NOT WHILE THE GAME IS TALKING. Parity runs both ways: if the game
            // takes the border down for Leshy's line, a sighted player does not
            // have it either. It returns the moment the line ends -- which is
            // the moment he asked about.
            if (CardReader.GameIsTalking()) return;

            // ==================================================================
            // IT WAS NEVER GIVING UP, AND THAT IS THE BUG. (0.7.259.)
            //
            // Zamar's game died at the Woodcarver with NO exception in either
            // log — BepInEx's and Unity's own both stop mid-keystroke, which is
            // a hard process kill rather than anything managed.
            //
            // THE ONE IKMA LOOP RUNNING THERE IS THIS ONE, and on that screen it
            // has never once succeeded: every browse logs "cursor will not land
            // ... CurrentInteractable is still 'nothing'". The Woodcarver locks
            // the view controller and the interaction cursor is not operating,
            // so the target is unreachable by construction.
            //
            // The old code logged that fact at exactly 30 misses AND THEN KEPT
            // CALLING, every frame, for the life of the screen. So IKMA was
            // writing InteractionCursor's transform position every frame,
            // indefinitely, across two view switches.
            //
            // WHY THAT IS DANGEROUS. MatchWorldPosition is:
            //
            //   Vector2 v  = Camera.main.WorldToScreenPoint(worldPosition);
            //   Vector2 v2 = rayCamera.ScreenToWorldPoint(v);
            //   transform.position = new Vector3(v2.x, v2.y, nearClipPlane);
            //
            // A world point behind a camera mid-transition sends
            // WorldToScreenPoint to infinity, and assigning a non-finite
            // transform.position every frame is a known way to take Unity down
            // natively rather than with an exception.
            //
            // I cannot prove that is what killed it. I can say this loop had no
            // business still running, and both fixes below are right on their
            // own terms: stop when the answer is in, and never hand the engine a
            // position that is not a real place.
            // ==================================================================
            if (_cursorMisses >= CURSOR_GIVE_UP) return;

            try
            {
                var cursor = Singleton<InteractionCursor>.Instance;
                if (cursor == null) return;

                if (ReferenceEquals(cursor.CurrentInteractable, part)) { _cursorMisses = 0; return; }

                var cam = Camera.main;
                if (cam == null) return;

                Vector3 world = part.transform.position;
                if (!IsFinite(world)) { GiveUpOnCursor(part, cursor, "its position is not a number"); return; }

                Vector3 screen = cam.WorldToScreenPoint(world);
                if (!IsFinite(screen)) { GiveUpOnCursor(part, cursor, "it does not project onto the screen"); return; }

                // ==================================================================
                // ASK BEFORE DRIVING. (0.7.276.)
                //
                // 0.7.259 diagnosed this correctly and stopped short of acting on
                // the diagnosis. Its own comment says the Woodcarver "locks the
                // view controller and the interaction cursor is not operating, so
                // the target is unreachable BY CONSTRUCTION" — and then the code
                // went on trying for thirty frames per part, per browse, and
                // logged a warning each time it failed. Zamar's 0.7.275 log has
                // ten of those, on a screen he has already signed off.
                //
                // THE GAME WILL ANSWER THIS IN ONE CALL.
                // InteractionCursor.CastForInteractableAtWorldPosition is PUBLIC
                // and its body is the same two lines of projection
                // MatchWorldPosition uses, followed by the raycast — same camera,
                // same maths, no side effects. So "would the cursor land here?"
                // is a question, not an experiment.
                //
                // InteractionDisabled is the other half: when it is set the game
                // has already dropped its current interactable and will not take
                // a new one, so nothing can land, whatever the raycast says.
                //
                // This turns thirty frames of writing InteractionCursor's
                // transform — which is what 0.7.259 suspected of killing the
                // game natively — into one raycast and one quiet line. It is the
                // project's own first rule: ask the game its own question rather
                // than reimplementing the answer.
                // ==================================================================
                // ==================================================================
                // THE GAME PUTS THE CURSOR BACK ON THE MOUSE EVERY FRAME.
                // (0.7.280 — and this is the whole answer, at last.)
                //
                // 0.7.276 checked the raycast, 0.7.277 checked the component was
                // running, and Zamar's 0.7.279 log still carried thirty-four
                // "30 frames of MatchWorldPosition". Both checks passed, and
                // they passed honestly: the component IS enabled and the
                // raycast DOES return the part.
                //
                // InteractionCursor.ManagedUpdate, in order:
                //
                //   if (!GamepadInputHandler.Instance.GamepadMode)
                //       UpdatePosition();          <- transform = Input.mousePosition
                //   ...
                //   UpdateMainInput();             <- raycasts from transform
                //
                // So outside gamepad mode the cursor is pinned to the physical
                // mouse every frame, and the raycast that decides
                // CurrentInteractable happens AFTER that. IKMA's write is
                // overwritten before it is ever read. Thirty frames of it is
                // thirty frames of losing the same race.
                //
                // This is Session 18's rule — "when an effect will not stick,
                // find what recomputes it every frame and move THAT" — and the
                // answer this time is that the thing recomputing it is the
                // player's mouse, which is not IKMA's to move.
                //
                // So ask the game which mode it is in and do not start.
                // NOTHING A BLIND PLAYER DEPENDS ON IS LOST: the border and the
                // hover sound are for a sighted onlooker, and the reader, the
                // arrows and Enter never went through the cursor at all.
                // ==================================================================
                bool mouseOwnsCursor = false;
                try { mouseOwnsCursor = !Singleton<GamepadInputHandler>.Instance.GamepadMode; }
                catch { mouseOwnsCursor = false; }

                if (mouseOwnsCursor)
                {
                    GiveUpOnCursor(part, cursor,
                        "the game pins the cursor to the mouse every frame outside gamepad mode",
                        expected: true);
                    return;
                }

                // THE COMPONENT HAS TO BE RUNNING AT ALL. (0.7.277.)
                //
                // 0.7.276 added the raycast question and Zamar's log still read
                // "30 frames of MatchWorldPosition" — so the raycast DID find
                // the part and the cursor still never adopted it. That is the
                // last piece: CurrentInteractable is only ever assigned inside
                // InteractionCursor.ManagedUpdate, and a disabled behaviour is
                // not updated. The game turns this component off on screens
                // that drive the cursor themselves (FirstPlaySceneController
                // does it by name), so the raycast can be perfect and nothing
                // will ever adopt the result.
                //
                // Asked as isActiveAndEnabled, which covers the behaviour being
                // switched off AND its GameObject being inactive.
                bool cursorRunning = false;
                try { cursorRunning = cursor.isActiveAndEnabled; } catch { }

                if (!cursorRunning)
                {
                    GiveUpOnCursor(part, cursor,
                        "the interaction cursor is not running on this screen", expected: true);
                    return;
                }

                bool interactionOff = false;
                try { interactionOff = cursor.InteractionDisabled; } catch { }

                if (interactionOff)
                {
                    GiveUpOnCursor(part, cursor, "this screen has interaction disabled", expected: true);
                    return;
                }

                MainInputInteractable wouldHit = null;
                try { wouldHit = cursor.CastForInteractableAtWorldPosition(world); } catch { }

                if (!ReferenceEquals(wouldHit, part))
                {
                    GiveUpOnCursor(part, cursor,
                        "the game's own raycast at that point returns " +
                        (wouldHit == null ? "nothing" : SafeObjectName(wouldHit)),
                        expected: true);
                    return;
                }

                cursor.MatchWorldPosition(world);

                // The raycast happens in the cursor's own update, so the result
                // is not known until next frame. Count the misses, and STOP once
                // the answer is in.
                _cursorMisses++;
                if (_cursorMisses >= CURSOR_GIVE_UP)
                    GiveUpOnCursor(part, cursor, $"{CURSOR_GIVE_UP} frames of MatchWorldPosition");
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: pointing the cursor threw {e.GetType().Name}.");
                _cursorTarget = null;
            }
        }

        /// <summary>
        /// How many frames of trying before the cursor is declared unreachable
        /// on this part. Was a log threshold; it is a stop now.
        /// </summary>
        private const int CURSOR_GIVE_UP = 30;

        /// <summary>
        /// Stop driving the cursor at a part it cannot reach. The border and the
        /// sound are presentation for a sighted onlooker; the reader, the arrows
        /// and Enter are untouched by this, so nothing a blind player depends on
        /// is lost.
        /// </summary>
        /// <summary>
        /// A helper for naming an interactable in a log line without throwing.
        /// </summary>
        private static string SafeObjectName(MainInputInteractable it)
        {
            try { return "'" + it.gameObject.name + "'"; }
            catch { return "something else"; }
        }

        /// <param name="expected">
        /// True when the cursor was never going to land — a screen that locks
        /// the view controller, or a part the game's own raycast does not
        /// return. That is not a fault, so it is logged as INFO. 0.7.276:
        /// ten warnings a visit on a signed-off screen is noise, and noise in
        /// this log is a defect of its own.
        /// </param>
        private static void GiveUpOnCursor(MainInputInteractable part, InteractionCursor cursor, string why,
                                           bool expected = false)
        {
            _cursorMisses = CURSOR_GIVE_UP;
            _cursorTarget = null;

            string n = "?";
            try { n = part.gameObject.name; } catch { }

            // ONCE PER PART PER SCREEN. (0.7.338.) His 0.7.337 log printed
            // this on every browse at the item pickup — 30 lines for three
            // items. Same fix as the unnamed-part warning above.
            if (!_loggedCandidates.Add($"cursor|{_screenName}|{n}")) return;

            string line =
                $"IKMA NODE: cursor will not land on '{n}' — {why}; " +
                $"CurrentInteractable is '{CurrentInteractableName(cursor)}'. " +
                "Logged once per screen.";

            if (expected) _log?.LogInfo(line);
            else          _log?.LogWarning(line);
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        private static string CurrentInteractableName(InteractionCursor cursor)
        {
            try
            {
                var cur = cursor.CurrentInteractable;
                return cur == null ? "nothing" : cur.gameObject.name;
            }
            catch { return "?"; }
        }

        private static void ReleaseHighlight()
        {
            _cursorTarget = null;
            if (_highlighted == null) return;
            try { _highlighted.CursorExit(); } catch { }
            _highlighted = null;
        }

        private static void LogHighlightOnce(MainInputInteractable part, string what)
        {
            string name = "?";
            try { name = part.gameObject.name; } catch { }
            if (_loggedCandidates.Add($"highlight|{_screenName}|{name}"))
                if (VerboseNodeDiagnostics)
                    _log?.LogInfo($"IKMA NODE: '{name}' highlights via {what}.");
        }

        // ==================================================================
        // THE DECK LAYOUT. (Session 17.)
        //
        // Zamar: "When selecting Host Card or Card to Sacrifice options, your
        // deck gets laid out of available options. The arrows need to hover as
        // if it's the cursor selecting theres, and enter to select it. No
        // backspace here."
        //
        // Every Act 1 node that shows you cards holds them in a
        // SelectableCardArray, and its displayedCards is the list — the same
        // field DeckViewReader already reads, so this is the established shape
        // rather than a new one. The slot wrapping it is the
        // SelectCardFromDeckSlot the probe found on the stone.
        //
        // A SUB-LAYER, NOT A MODE. Nothing is remembered about whether the cards
        // are up: the array is asked every time. When the game puts cards on the
        // table the arrows browse cards, and when it takes them away they go
        // back to browsing the stone's parts — so a card chosen by the mouse, or
        // a sequence the game ends itself, can never strand the reader. That is
        // the cached-state rule, and it is what broke the pause menu one build
        // ago.
        //
        // NO BACKSPACE, his instruction. The game does not offer a way out of
        // this choice and IKMA does not invent one.
        // ==================================================================
        private static System.Reflection.FieldInfo _selectorField;
        private static System.Reflection.FieldInfo _displayedField;
        private static bool _cardFieldsResolved;

        /// <summary>
        /// "The player is not on a card yet", as distinct from "the player is
        /// on card 1". Holding both in 0 is what made the first right arrow
        /// skip the first card. (0.7.218.)
        /// </summary>
        private const int NOWHERE = -1;

        private static int _cardIndex = NOWHERE;
        private static string _slotBeingFilled;
        private static SelectableCard _hoveredCard;

        private static void ResolveCardFields()
        {
            if (_cardFieldsResolved) return;
            _cardFieldsResolved = true;

            const System.Reflection.BindingFlags any =
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;

            _selectorField  = typeof(SelectCardFromDeckSlot).GetField("cardSelector", any);
            _displayedField = typeof(SelectableCardArray).GetField("displayedCards", any);

            if (_selectorField == null)
                _log?.LogWarning("IKMA NODE: SelectCardFromDeckSlot.cardSelector not found.");
            if (_displayedField == null)
                _log?.LogWarning("IKMA NODE: SelectableCardArray.displayedCards not found.");
        }

        // ==================================================================
        // THE TRADER NODE LAYS ITS CARDS OUT ITSELF. (0.7.239.)
        //
        // Zamar: "The trader screen is not currently working."
        //
        // His log said exactly how it failed — eight cards found by the probe,
        // and then eight presses answered "Nothing to choose here.":
        //
        //   IKMA NODE: candidate 'Card (Kingfisher)' (SelectableCard) on Trader
        //   IKMA NODE: Trader settled with 0 part(s).
        //   IKMA SPEAK (interrupt): Nothing to choose here.   x8
        //
        // OpenCards walks SelectCardFromDeckSlot -> SelectableCardArray ->
        // displayedCards, which is how every node screen built so far presents
        // cards. TradePeltsSequencer does not use that machinery at all: it
        // Instantiates the eight offers straight under its own transform and
        // keeps them in a private List<SelectableCard> tradeCards, wiring each
        // one's CursorSelectStarted to its own OnCardSelected. So the array was
        // empty and the reader correctly reported nothing — it was looking in
        // the one place the cards were not.
        //
        // Read the list the sequencer actually keeps. Everything downstream
        // already works on SelectableCard: browse, hover, DescribeCard, and
        // ActivateCard's CursorSelectStart/End, which is precisely what fires
        // the delegate the trade is wired to. Nothing else changes.
        //
        // NOT the pelts. peltCards is the other private list, and those cards
        // are laid out with SetInteractionEnabled(false) — they are what the
        // player is SPENDING, not what they are choosing. Offering them as
        // options would be a key that does nothing. They are counted for the
        // help line instead, because how many trades are left is on the table
        // in front of a sighted player and nowhere else for a blind one.
        // ==================================================================
        private static System.Reflection.FieldInfo _tradeCardsField, _peltCardsField;

        private static List<SelectableCard> SequencerCardList(
            string fieldName, ref System.Reflection.FieldInfo cache)
        {
            var trader = _sequencer as TradePeltsSequencer;
            if (trader == null) return null;

            try
            {
                if (cache == null)
                    cache = typeof(TradePeltsSequencer).GetField(
                        fieldName,
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic);

                if (cache == null)
                {
                    _log?.LogWarning($"IKMA NODE: TradePeltsSequencer.{fieldName} not found.");
                    return null;
                }

                return cache.GetValue(trader) as List<SelectableCard>;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: reading {fieldName} threw {e.GetType().Name}.");
                return null;
            }
        }

        /// <summary>How many pelts are still on the table to spend.</summary>
        internal static int PeltsOnTable()
        {
            var pelts = SequencerCardList("peltCards", ref _peltCardsField);
            if (pelts == null) return 0;

            int n = 0;
            foreach (var c in pelts)
            {
                if (c == null) continue;
                bool shown = false;
                try { shown = c.gameObject.activeInHierarchy; } catch { }
                if (shown) n++;
            }
            return n;
        }

        /// <summary>
        /// The cards currently laid out for choosing, or an empty list. Asked of
        /// the game every call — never cached.
        /// </summary>
        public static List<SelectableCard> OpenCards()
        {
            var result = new List<SelectableCard>();
            if (_sequencer == null) return result;

            // The Trader keeps its own list; see the note above.
            var traded = SequencerCardList("tradeCards", ref _tradeCardsField);
            if (traded != null)
            {
                foreach (var c in traded)
                {
                    if (c == null) continue;
                    bool shown = false;
                    try { shown = c.gameObject.activeInHierarchy; } catch { }
                    if (shown) result.Add(c);
                }
                return result;
            }

            ResolveCardFields();
            if (_selectorField == null || _displayedField == null) return result;

            try
            {
                foreach (var slot in _sequencer.GetComponentsInChildren<SelectCardFromDeckSlot>(true))
                {
                    if (slot == null) continue;

                    var array = _selectorField.GetValue(slot) as SelectableCardArray;
                    if (array == null) continue;

                    bool live = false;
                    try { live = array.gameObject.activeInHierarchy; } catch { }
                    if (!live) continue;

                    var cards = _displayedField.GetValue(array) as List<SelectableCard>;
                    if (cards == null) continue;

                    foreach (var c in cards)
                    {
                        if (c == null) continue;
                        bool shown = false;
                        try { shown = c.gameObject.activeInHierarchy; } catch { }
                        if (shown) result.Add(c);
                    }

                    if (result.Count > 0) break;
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: reading the card layout threw {e.GetType().Name}.");
            }

            return result;
        }

        private static void ReleaseCardHover()
        {
            if (_hoveredCard == null) return;
            try { _hoveredCard.CursorExit(); } catch { }
            _hoveredCard = null;
        }

        private static void HoverCard(List<SelectableCard> cards)
        {
            if (cards.Count == 0) return;
            if (_cardIndex < 0 || _cardIndex >= cards.Count) return;

            var target = cards[_cardIndex];
            if (ReferenceEquals(target, _hoveredCard)) return;

            ReleaseCardHover();
            try { target.CursorEnter(); _hoveredCard = target; }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: card hover threw {e.GetType().Name}.");
            }
        }

        private static string DescribeCard(List<SelectableCard> cards, int index)
        {
            if (index < 0 || index >= cards.Count) return Vocabulary.CardCouldNotBeRead;

            CardInfo info = null;
            try { info = cards[index].Info; } catch { }

            // 0.7.433 - two cards of one name in the layout are numbered.
            return Vocabulary.CardOrUnreadable(CardReader.DeckDisambiguated(cards, index, CardReader.DescribeCardInfo(info)));
        }

        /// <summary>
        /// Notice the deck appearing or going away and say so once. Without it
        /// the arrows silently change meaning under the player, which is the
        /// stale-model failure this project keeps guarding against — the same
        /// reason the board diff watcher exists.
        /// </summary>
        private static bool _cardsWereOpen;

        // ==================================================================
        // THE END OF THE SCREEN. (0.7.104.)
        //
        // Zamar: "After selecting Begin Ritual, it should have read '[Host Card]
        // gained the [x] Sigil(s)'. Then return context to the map when the game
        // does. Currently it just stayed on the Sacrifice Stone screen."
        //
        // His log shows exactly that — eleven "Nothing to choose here." in a
        // row, because the parts were gone and the reader still owned the
        // arrows. The sequencer's own end never reached NodeProbe.End.
        //
        // SO THE READER WATCHES FOR ITS OWN SCREEN EMPTYING. Once parts have
        // been seen and then stay gone with no cards laid out, the screen is
        // over. That is a liveness test on the game's own objects rather than a
        // flag IKMA sets — the rule that fixed this same class of bug on the
        // pause menu and the deck view.
        //
        // WHAT IS SPOKEN IS READ BACK, NOT PREDICTED. The host card's sigils are
        // noted the moment Begin Ritual is pressed and the SAME card is asked
        // again when the screen ends, so the line reports what the card actually
        // has now. Announcing the sacrifice's sigils at press time would be
        // guessing at the game's outcome, which this project does not do
        // anywhere else either.
        // ==================================================================
        private static bool  _partsSeen;
        private static float _emptyFor;
        private static SelectableCard _ritualHost;
        private static string _ritualHostName;
        private static List<string> _ritualHostSigils;

        private const float SCREEN_OVER_SECONDS = 1.25f;

        // ==================================================================
        // THE HAND-BACK WATCH IS GONE, 0.7.278, AND IT WAS MINE.
        //
        // 0.7.277 added a watchdog that re-armed this reader when a release
        // looked wrong. It hung the game: the retry called
        // NodeScreenReader.Begin directly, so NodeProbe._target stayed null,
        // and the next release reached NodeProbe.Finished() -> End(reason),
        // whose first line is `if (_target == null) return;`. The reader was
        // therefore never released, the release condition stayed true, and
        // "Woodcarver is over — handing the keyboard back" was written every
        // frame until Zamar killed it. His log is 2,184 lines and almost all
        // of it is that one sentence.
        //
        // NodeProbe's own comment warned about exactly this — "routed through
        // the probe rather than having the reader release itself so that ONE
        // place owns the lifetime of a node screen. Two owners is how the
        // pause menu ended up stranded a build ago." I added a second owner.
        //
        // The softlock it was written for is real and is fixed at its cause
        // instead: Backspace no longer walks into an empty selection. A guard
        // below also makes the release self-completing, so no future failure
        // of this kind can spin.
        // ==================================================================

        private static List<string> SigilsOf(SelectableCard card)
        {
            var names = new List<string>();
            if (card == null) return names;

            CardInfo info = null;
            try { info = card.Info; } catch { }
            if (info == null) return names;

            try
            {
                foreach (var a in info.Abilities)
                {
                    // The single choke point every sigil name in the mod goes
                    // through, so this one reads the same words as the rest.
                    string nm = CardReader.GetAbilityName(a);
                    if (!string.IsNullOrEmpty(nm)) names.Add(nm);
                }
            }
            catch { }
            return names;
        }

        // ==================================================================
        // THE TRAPPER'S TABLE. (0.7.330.)
        //
        // Zamar's answers, Session 25, to "what should each of these say":
        //   pelt browsed     — "pictured": the name, the price tag, the card.
        //   the knife        — "should be an option".
        //   teeth            — A reads them.
        //   the way out      — Backspace.
        //   bought           — "[pelt name] purchased for [x] teeth. You have
        //                       [x] remaining."
        //   can't afford     — "You are [x] teeth short from buying [pelt name]"
        //
        // THE WAY OUT IS THE PURCHASED PILE. BuyPeltsSequencer waits on
        // purchasedPile.CursorSelectEnded and nothing else; IKMA filters every
        // CardPile out of the arrows as furniture, which is why the node had no
        // exit. Backspace presses that pile.
        //
        // Prices and positions are the game's, asked by reflection:
        // PeltPrices (private int[]) indexed by peltsForSale (private list).
        // The knife is a literal 7 in OnPurchaseKnifePressed.
        // ==================================================================
        private const int KNIFE_PRICE = 7;

        private static BuyPeltsSequencer TrapperSeq
            => _screenName == Vocabulary.Trapper ? _sequencer as BuyPeltsSequencer : null;

        internal static bool TrapperActive => TrapperSeq != null;

        // 0.7.337 — the item pickup screen, for its I key and its help line.
        internal static bool ItemPickupActive => Active && _screenName == Vocabulary.ItemPickupName;
        internal static bool CampfireActive => Active && _screenName == Vocabulary.Campfire;   // Session 34

        /// <summary>
        /// Session 34. A card choice ran inside this screen (the Mycologists'
        /// no-pairs path). The screen's own parts may never appear, so the
        /// choice stands in for them: once it is done and the screen is empty,
        /// the ordinary release (SCREEN_OVER_SECONDS) hands the controls back.
        /// </summary>
        internal static void NoteInnerCardChoice()
        {
            if (!Active) return;
            _partsSeen = true;
            _emptyFor = 0f;
            _log?.LogInfo($"IKMA NODE: {_screenName} is running a card choice - its release is armed.");
        }
        internal static bool MycologistsActive => Active && _screenName == Vocabulary.Mycologists;   // Session 34

        /// <summary>
        /// Session 34. The last "The totem is assembled ..." line, as spoken
        /// (BuildTotemSequencer.AssembleTotem prefix in Plugin.cs), for X /
        /// Space to repeat. Cleared when a screen starts or ends.
        /// </summary>
        internal static string LastTotemAssembledLine;

        /// <summary>The finished totem is on its stand (a TotemItemData in a part).</summary>
        private static bool FinishedTotemShowing(List<MainInputInteractable> parts)
        {
            foreach (var p in parts)
            {
                try { if ((p as SelectableItemSlot)?.Item?.Data is TotemItemData) return true; }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// Session 34, Zamar: B / Backspace returns to the backpack to
        /// reselect, while the finished totem waits. True if it did.
        /// </summary>
        internal static bool ReturnToBackpackFromTotem()
        {
            if (_screenName != Vocabulary.Woodcarver || InBackpackView) return false;
            if (!BackpackAvailable || TotemAutoAssembles()) return false;
            if (!FinishedTotemShowing(Parts())) return false;
            OpenBackpackView();
            return true;
        }

        private static int Teeth()
        {
            try { return RunState.Run.currency; } catch { return 0; }
        }

        private static string TeethWord(int n) => Vocabulary.NodeScreens.TeethWord(n);

        private static T TrapperField<T>(string field) where T : class
        {
            try
            {
                var f = typeof(BuyPeltsSequencer).GetField(field,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                return f?.GetValue(TrapperSeq) as T;
            }
            catch { return null; }
        }

        private static int PeltIndex(MainInputInteractable it)
        {
            var card = it as SelectableCard;
            if (card == null) return -1;
            var forSale = TrapperField<List<SelectableCard>>("peltsForSale");
            return forSale == null ? -1 : forSale.IndexOf(card);
        }

        private static int PeltPrice(int index)
        {
            try
            {
                var prop = typeof(BuyPeltsSequencer).GetProperty("PeltPrices",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var prices = prop?.GetValue(TrapperSeq, null) as int[];
                if (prices != null && index >= 0 && index < prices.Length) return prices[index];
            }
            catch { }
            return -1;
        }

        private static bool IsKnife(MainInputInteractable it)
        {
            if (it == null) return false;
            var knife = TrapperField<GenericMainInputInteractable>("purchaseKnifeInteractable");
            return knife != null && ReferenceEquals(it, knife);
        }

        private static string KnifeName()
        {
            try
            {
                var data = ItemsUtil.GetConsumableByName("TrapperKnife");
                if (data != null && !string.IsNullOrEmpty(data.rulebookName)) return Loc.Game(data.rulebookName);
            }
            catch { }
            return Vocabulary.NodeScreens.KnifeName;
        }

        private static int TrapperOrder(MainInputInteractable it)
        {
            int i = PeltIndex(it);
            if (i >= 0) return i;
            if (IsKnife(it)) return 50;
            return 99;
        }

        private static string TrapperWareName(MainInputInteractable it)
        {
            if (TrapperSeq == null) return null;
            if (IsKnife(it)) return KnifeName();
            if (PeltIndex(it) < 0) return null;
            try { return CardReader.CardName((it as SelectableCard).Info); } catch { return null; }
        }

        private static int TrapperWarePrice(MainInputInteractable it)
        {
            if (TrapperSeq == null) return -1;
            if (IsKnife(it)) return KNIFE_PRICE;
            return PeltPrice(PeltIndex(it));
        }

        /// <summary>A pelt or the knife, read as it lies on the table.</summary>
        private static string TrapperWareLine(MainInputInteractable it)
        {
            if (TrapperSeq == null) return null;

            // 0.7.332, Zamar: "Teeth count before item name here."
            if (IsKnife(it)) return Vocabulary.NodeScreens.KnifeForTeeth(TeethWord(KNIFE_PRICE), KnifeName(), BrokeClause());

            int i = PeltIndex(it);
            if (i < 0) return null;
            CardInfo info = null;
            try { info = (it as SelectableCard).Info; } catch { }
            if (info == null) return null;

            // 0.7.332, Zamar: "2 teeth, Rabbit Pelt, 0, 1." — price first, then
            // name and stats, for all three pelts. No art words (Terrain border,
            // Golden glow) on this screen. Pelts carry no sigils.
            int price = PeltPrice(i);
            string name = CardReader.CardName(info);
            string stats = Vocabulary.NodeScreens.Name(info.Attack, info.Health);
            return (price > 0
                ? Vocabulary.NodeScreens.PriceNameStats(TeethWord(price), name, stats)
                : Vocabulary.NodeScreens.NameStats(name, stats)) + BrokeClause();
        }

        private static void TrapperReportPurchase(MainInputInteractable it, string name, int price, int before)
        {
            int after = Teeth();
            if (after < before)
            {
                using (Speech.Event(EventKind.NodeResults)) Speech.Confirm(Vocabulary.NodeScreens.PurchasedForYouHave(name, TeethWord(before - after), after, BrokeClause()));
                return;
            }

            // Not bought. The knife always gets the game's own line (no room,
            // or can't afford), so IKMA says nothing over it.
            if (IsKnife(it) || price <= 0) return;

            // Before the Snowline is cleared the game says "You'll need more
            // teeth for that one." itself; after it, nothing. His line fills
            // only the silence.
            bool gameSpeaks = true;
            try
            {
                var prop = typeof(BuyPeltsSequencer).GetProperty("SnowlineRegionCleared",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                object v = prop?.GetValue(TrapperSeq, null);
                if (v is bool) gameSpeaks = !(bool)v;
            }
            catch { }

            int shortBy = price - before;
            _log?.LogInfo($"IKMA NODE: '{name}' not bought — {shortBy} short, game speaks={gameSpeaks}.");
            if (!gameSpeaks && shortBy > 0)
                Speech.Confirm(Vocabulary.NodeScreens.YouAreShortFrom(TeethWord(shortBy), name, BrokeClause()));
        }

        // 0.7.333, Zamar: "if you have nothing else you can afford, on each
        // arrow and enter say 'Not enough teeth remaining for another purchase.
        // Backspace to leave the Trader.'" "Trapper" for "Trader": this is the
        // Trapper's node, and the arrival line he wrote names it so.
        //
        // "Can afford" = some ware still on the table costs no more than the
        // teeth in hand. The knife counts only while there is an item slot
        // free — the game refuses it otherwise (OnPurchaseKnifePressed).
        private static string BrokeClause()
        {
            if (TrapperSeq == null) return "";
            int teeth = Teeth();
            foreach (var it in Parts())
            {
                int price = TrapperWarePrice(it);
                if (price <= 0 || price > teeth) continue;
                if (IsKnife(it))
                {
                    bool room = false;
                    try { room = RunState.Run.consumables.Count < RunState.Run.MaxConsumables; } catch { }
                    if (!room) continue;
                }
                return "";
            }
            return Vocabulary.NodeScreens.NotEnoughTeethRemaining;
        }

        internal static void TrapperSpeakTeeth()
        {
            if (TrapperSeq == null) return;
            // PROVISIONAL wording — the key is his, the sentence is mine.
            Speech.Browse(Vocabulary.NodeScreens.YouHave(TeethWord(Teeth())));
        }

        internal static void TrapperLeave()
        {
            var pile = TrapperField<CardPile>("purchasedPile");
            bool enabled = false;
            try { enabled = pile != null && pile.Enabled; } catch { }
            if (!enabled)
            {
                _log?.LogInfo("IKMA NODE: Backspace at the Trapper — the pile is not taking clicks yet.");
                return;
            }
            _log?.LogInfo("IKMA NODE: leaving the Trapper through the purchased pile.");

            // Captured before the press: the game clears purchasedPelts once
            // they are shuffled into the deck.
            var seq    = TrapperSeq;
            var bought = TrapperField<List<SelectableCard>>("purchasedPelts");
            string summary = PeltsAddedLine(bought);

            try { pile.CursorSelectStart(); pile.CursorSelectEnd(); }
            catch (System.Exception e) { _log?.LogWarning($"IKMA NODE: leaving threw {e.GetType().Name}."); return; }

            // 0.7.360 — LEAVING STOMPS THE INFO LINES. Zamar: "Pressing
            // backspace to leave the trapper trader should stomp any info
            // lines being read." Queued commentary about the screen he just
            // left is dropped, and whatever is in the air is cut.
            CombatAnnouncer.DropCommentary("left the Trapper");
            Speech.Silence();

            if (!string.IsNullOrEmpty(summary) && CombatAnnouncer.Instance != null)
                CombatAnnouncer.Instance.StartCoroutine(SpeakWhenShuffled(seq, summary));
        }

        // ==================================================================
        // WHAT WENT INTO THE DECK. (0.7.335.)
        //
        // Zamar: "Can you read off how many of each pelt type gets added to
        // your deck upon leaving the Trapper?" His shapes, verbatim:
        //   "One Rabbit pelt added to your deck."
        //   "One Rabbit pelt and three Golden Pelts added to your deck."
        //   "One Rabbit pelt, one Wolf pelt, and two Golden Pelts added to your deck."
        // Names are the game's (so "Rabbit Pelt"); order is the game's own
        // CardLoader.PeltNames. Includes the free pelt — it is in the pile.
        // Spoken when the game has finished moving them: purchasedPelts is
        // cleared right after the shuffle loop.
        // ==================================================================
        private static string PeltsAddedLine(List<SelectableCard> bought)
        {
            if (bought == null || bought.Count == 0) return null;

            var counts = new Dictionary<string, int>();
            var order  = new Dictionary<string, int>();
            foreach (var c in bought)
            {
                CardInfo info = null;
                try { info = c?.Info; } catch { }
                if (info == null) continue;
                string name = CardReader.CardName(info);
                if (string.IsNullOrEmpty(name)) continue;
                int n; counts.TryGetValue(name, out n); counts[name] = n + 1;
                if (!order.ContainsKey(name))
                {
                    int idx = System.Array.IndexOf(CardLoader.PeltNames, info.name);
                    order[name] = idx < 0 ? 99 : idx;
                }
            }
            if (counts.Count == 0) return null;

            var names = new List<string>(counts.Keys);
            names.Sort((a, b) => order[a].CompareTo(order[b]));

            var parts = new List<string>();
            foreach (var name in names)
            {
                int n = counts[name];
                string word = Vocabulary.NumberWord(n);
                parts.Add($"{word} {(n == 1 ? name : Pluralise(name))}");
            }

            string list;
            if (parts.Count == 1) list = parts[0];
            else if (parts.Count == 2) list = Vocabulary.NodeScreens.PairList(parts[0], parts[1]);
            else list = Vocabulary.NodeScreens.ListOfThree(parts.GetRange(0, parts.Count - 1).ToArray(), parts[parts.Count - 1]);

            list = char.ToUpperInvariant(list[0]) + list.Substring(1);
            return Vocabulary.NodeScreens.PeltsAddedLine(list);
        }

        private static System.Collections.IEnumerator SpeakWhenShuffled(BuyPeltsSequencer seq, string line)
        {
            var f = typeof(BuyPeltsSequencer).GetField("purchasedPelts",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            float waited = 0f;
            while (waited < 5f)
            {
                List<SelectableCard> list = null;
                try { list = f?.GetValue(seq) as List<SelectableCard>; } catch { }
                if (list == null || list.Count == 0) break;
                yield return null;
                waited += Time.deltaTime;
            }
            _log?.LogInfo($"IKMA NODE: pelts shuffled in after {waited:0.00}s.");
            using (Speech.Event(EventKind.NodeResults)) Speech.Result(line);
        }

        // ==================================================================
        // "[CARD] SACRIFICED." WHEN THE BOON CARD LANDS. (0.7.327.)
        //
        // Zamar, his wording: "When this card drops it should also confirm
        // [card name] sacrificed." The name is captured from the game's own
        // sacrifice slot at the press, because DestroyCard empties it before
        // the boon arrives. "Drops" = the boon card is enabled, which the
        // game does 0.5s after PlaceCardOnSlot — the moment it lands.
        // ==================================================================
        private static string _sacrificedName;
        private static bool   _sacrificeAnnounced;

        private static void NoteSacrificed()
        {
            _sacrificedName = null;
            _sacrificeAnnounced = false;
            if (_screenName != Vocabulary.BoneLord) return;

            foreach (var it in Parts())
            {
                var slot = it as SelectCardFromDeckSlot;
                if (slot == null) continue;
                SelectableCard held = null;
                try { held = slot.Card; } catch { }
                if (held == null) continue;
                try { _sacrificedName = CardReader.CardName(held); } catch { }
                _log?.LogInfo($"IKMA NODE: '{_sacrificedName ?? "?"}' is on the Bone Lord's altar.");
                return;
            }
        }

        private static void AnnounceSacrificedOnBoon()
        {
            if (_sacrificeAnnounced || string.IsNullOrEmpty(_sacrificedName)) return;

            foreach (var it in Parts())
            {
                var card = it as SelectableCard;
                if (card == null) continue;
                CardInfo info = null;
                try { info = card.Info; } catch { }
                if (info == null || info.boon == BoonData.Type.None) continue;

                _sacrificeAnnounced = true;
                using (Speech.Event(EventKind.NodeResults)) Speech.Result(Vocabulary.NodeScreens.Sacrificed(_sacrificedName));

                // Session 46 (0.7.452). Found with the test driver: after
                // "[card] sacrificed." the boon card sat on the altar waiting
                // for Enter and nothing said so. Zamar's line; the name is the
                // game's, from the boon asset, as on the card read.
                string boonName = null;
                try { boonName = CardReader.BoonName(info.boon); } catch { }
                if (!string.IsNullOrEmpty(boonName))
                    using (Speech.Event(EventKind.NodeResults)) Speech.Result(Vocabulary.NodeScreens.BoonCardPlaced(boonName));
                else
                    _log?.LogInfo($"IKMA NODE: boon card of type {info.boon} landed with no displayed name - the placed line was not said.");
                return;
            }
        }

        private static void NoteRitualHost()
        {
            _ritualHost = null;
            _ritualHostName = null;
            _ritualHostSigils = null;

            foreach (var it in Parts())
            {
                var slot = it as SelectCardFromDeckSlot;
                if (slot == null) continue;

                string raw = null;
                try { raw = it.gameObject.name; } catch { }
                if (raw == null || raw.Trim().ToLowerInvariant() != "hostslot") continue;

                SelectableCard held = null;
                try { held = slot.Card; } catch { }
                if (held == null) continue;

                _ritualHost = held;
                try { _ritualHostName = CardReader.CardName(held); } catch { }
                _ritualHostSigils = SigilsOf(held);

                _log?.LogInfo($"IKMA NODE: ritual host '{_ritualHostName ?? "?"}' " +
                              $"with {_ritualHostSigils.Count} sigil(s) before.");
                return;
            }
        }

        /// <summary>What the host walked away with, or null if it cannot be read.</summary>
        private static string RitualResult()
        {
            if (_ritualHost == null || string.IsNullOrEmpty(_ritualHostName)) return null;

            var after  = SigilsOf(_ritualHost);
            var before = _ritualHostSigils ?? new List<string>();

            if (after.Count == 0)
            {
                _log?.LogInfo("IKMA NODE: the host card could not be read after the ritual.");
                return null;
            }

            var gained = new List<string>();
            foreach (var nm in after)
            {
                int had = 0, has = 0, already = 0;
                foreach (var b in before) if (b == nm) had++;
                foreach (var a in after)  if (a == nm) has++;
                foreach (var g in gained) if (g == nm) already++;
                if (has - had > already) gained.Add(nm);
            }

            if (gained.Count == 0)
            {
                _log?.LogInfo($"IKMA NODE: '{_ritualHostName}' gained no readable sigil.");
                return null;
            }

            // HIS FORMAT: "[Host Card] gained the [x] Sigil(s)."
            string list = string.Join(", ", gained.ToArray());
            string word = Vocabulary.NodeScreens.SigilCount(gained.Count);
            return Vocabulary.NodeScreens.RitualResult(_ritualHostName, list, word);
        }

        public static void Tick()
        {
            if (_sequencer == null)
            {
                _cardsWereOpen = false;
                _partsSeen     = false;
                _emptyFor      = 0f;
                return;
            }

            // Before anything else: keep the game's cursor on the part he is
            // standing on. Everything visual follows from that. See HoldCursor.
            HoldCursor();

            bool open = OpenCards().Count > 0;

            AnnounceSacrificedOnBoon();

            // A CONVERSATION IS NOT AN EMPTY SCREEN. (0.7.126.)
            //
            // Zamar: "The selection also didnt work after the second lines of
            // speech asking if you want to stay." His 0.7.125 log says why —
            // after "PUSH YOUR LUCK? OR PULL AWAY?" the arrows read "Card
            // removal" and "Campfire", which are MAP choices. The reader had
            // already handed the keyboard back, so the map layer answered for a
            // screen that was still very much up.
            //
            // The release test was "the parts went away and stayed away", and at
            // the campfire they do exactly that while Leshy talks between the
            // two phases. Parts being absent means the screen is finished only
            // when nothing else is going on.
            //
            // ASK THE GAME WHETHER IT IS STILL TALKING. While a line is waiting
            // on the player, the sequence has not ended by definition — nothing
            // can have moved on, because the game is holding for input.
            //

            // Has the screen finished with itself?
            var tickParts = Parts();
            SpeakArrivalWhenReady(tickParts.Count);
            if (tickParts.Count > 0 || _donePartsPresent > 0) { _partsSeen = true; _emptyFor = 0f; }
            else if (_partsSeen && !open && !DialogueWaiting())
            {
                _emptyFor += Time.deltaTime;
                if (_emptyFor >= SCREEN_OVER_SECONDS)
                {
                    // ==================================================================
                    // THE RELEASE IS A GUESS, SO IT HAS TO BE UNDOABLE. (0.7.277.)
                    //
                    // Zamar softlocked here: he took the Fecundity carving, backed out
                    // of the backpack, and every key died. His log:
                    //
                    //   NODE: backpack view closed.
                    //   SPEAK: Woodcarving selection.
                    //   NODE: Woodcarver is over — handing the keyboard back.
                    //   NODE: Woodcarver — reader released.
                    //   (nothing, until Escape)
                    //
                    // The Woodcarver's three slots go inactive the moment a piece is
                    // taken, while the game is still very much on the screen putting
                    // the totem together. Parts() reads zero, nothing is open, nobody
                    // is talking — every condition this test has — and the keyboard
                    // went to the map layer for a map that was not there.
                    //
                    // THE TEST IS EMPTINESS FOR A SECOND AND A QUARTER, which is a
                    // guess about the game dressed as a measurement. 0.7.125 already
                    // patched this shape once at the campfire (dialogue), and this is
                    // the same defect at a different screen. The honest fix is not a
                    // longer wait or a fourth condition — it is that a release built
                    // on a guess must be able to take itself back.
                    //
                    // So the sequencer is remembered, and HandBackWatch re-arms the
                    // reader if the map does not turn up. A wrong release then costs a
                    // second of silence instead of the run.
                    // ==================================================================
                    string result = RitualResult();
                    _log?.LogInfo($"IKMA NODE: {_screenName} is over — handing the keyboard back.");

                    if (!string.IsNullOrEmpty(result))
                    {
                        CombatAnnouncer.DropCommentary("node screen finished");
                        Speech.Browse(result);
                    }

                    // RELEASE, AND MAKE SURE IT ACTUALLY HAPPENED. (0.7.278.)
                    //
                    // NodeProbe.Finished() is the owner of this lifetime and is
                    // still how a screen ends — but its End() returns early when
                    // the probe has no target, and when that happens this branch
                    // stays true and logs every frame forever. That is what
                    // 0.7.277 did.
                    //
                    // So: ask the probe, then check. A release that did not
                    // release is the bug; finishing it here costs nothing when
                    // the probe did its job, because End() is idempotent.
                    NodeProbe.Finished();

                    if (_sequencer != null)
                    {
                        _log?.LogWarning(
                            "IKMA NODE: the probe did not own this screen's lifetime — " +
                            "releasing the reader directly so the keyboard cannot be stranded.");
                        End();
                    }
                    return;
                }
            }

            if (open == _cardsWereOpen) return;
            _cardsWereOpen = open;

            if (open)
            {
                var cards = OpenCards();
                _cardIndex = NOWHERE;
                // THIS COUNT IS THE FIRST CARD DEALT, NOT THE DECK. His log:
                // "opened with 1 card(s)" and then "choosing card 7 of 9" — the
                // layout deals onto the table over several frames, so a count
                // taken the instant it opens is always the beginning of the
                // deal. Diagnostic only, and left as-is deliberately: the
                // browse and the help line both re-read the live count, which is
                // the settle rule applied where it matters.
                _log?.LogInfo($"IKMA NODE: card layout opened with {cards.Count} card(s) " +
                              $"for '{_slotBeingFilled ?? "?"}'.");

                // NAME WHAT THE CARD IS FOR. Zamar: "This should read 'Choose a
                // card to become the host.'" The count is gone with it — the
                // deck is a browse, and how many cards are in it is what Space
                // is for. What the player needs at this moment is which of the
                // two slots they are filling, because the screen looks the same
                // for both and choosing wrong cannot be undone.
                // ONE PROMPT, NOT TWO. (0.7.157.)
                //
                // Zamar: "I need one less trigger of 'Choose a card to warm
                // by...' Too much redundancy." His log has the pair back to
                // back, a keypress apart:
                //
                //   SPEAK: Choose a card to warm by the campfire. This will
                //          permanently raise its Power by 1.     <- the part
                //   NODE:  activating 'Choose a card to warm by the campfire...'
                //   SPEAK: Choose a card to warm by the campfire. This will
                //          raise its stats but comes with risk...  <- this line
                //
                // He pressed Enter on a part whose own name is that sentence and
                // was answered with that sentence. The layout prompt exists to
                // say WHICH slot is being filled, which is a real question on
                // the sacrifice stone — two identical-looking screens, and the
                // wrong choice cannot be undone. The campfire has one slot, so
                // there is nothing to disambiguate and the prompt is pure echo.
                //
                // Cut where it is redundant, kept where it earns its place: the
                // screen's own blurb still carries the risk warning on H.
                if (_screenName != Vocabulary.Campfire)
                    Speech.Browse(CardPrompt());
                else
                    _log?.LogInfo("IKMA NODE: campfire layout prompt held — the part just said it.");

                HoverCard(cards);
            }
            else
            {
                ReleaseCardHover();
                _cardIndex = NOWHERE;
                _log?.LogInfo("IKMA NODE: card layout closed.");
            }
        }

        /// <summary>
        /// The line spoken when the deck is laid out, chosen by WHICH SLOT asked
        /// for it. `_slotBeingFilled` is already the part's approved name, so
        /// this stays in Zamar's vocabulary rather than the game's.
        ///
        /// The host line is his, word for word. The sacrifice line is built to
        /// match it from his own description of the stone — "Sacrifice another
        /// card from your deck to transfer it's sigils to the host" — and is
        /// logged as such so he can overrule it.
        /// </summary>
        // ======================================================================
        // THE CAMPFIRE'S TWO LINES, COMPOSED FROM THE GAME. (0.7.130.)
        //
        // Zamar wants both to say what the fire actually does:
        //   the slot   -> "Choose a card to warm by the campfire. This will
        //                  permanently [campfire effect]."
        //   the stone  -> "Send [card] to rest by the fire, increasing its
        //                  [x] by [y]."
        //
        // THE STAT IS THE GAME'S OWN WORD, NOT ONE OF MINE.
        // CardStatBoostSequencer.GetTranslatedStatText(Boolean isAttackMod)
        // returns the printed name of the stat this fire is offering, and
        // OnSlotSelected(slot, attackMod) is where the game says which. That is
        // the data object this project's rule keeps pointing at — the
        // alternative was inventing "power" and hoping every campfire agrees.
        //
        // *** THE AMOUNT IS NOT READABLE AND IS NOT CLAIMED. *** ApplyModToCard
        // declares no amount field; the "+1" is a literal inside a method body,
        // and reflection cannot see method bodies. So these lines name the stat
        // and stop. Saying "by 1" would be a number IKMA guessed, on a screen
        // that can cost the player a card — exactly the confidently wrong
        // announcement this project treats as worse than silence.
        // ======================================================================
        private static System.Reflection.MethodInfo _statTextMethod;
        private static bool _statTextResolved;
        private static bool? _campfireIsAttackMod;

        /// <summary>Told to IKMA by the game, from OnSlotSelected.</summary>
        internal static void NoteCampfireStat(bool attackMod)
        {
            _campfireIsAttackMod = attackMod;
            _log?.LogInfo($"IKMA NODE: campfire stat is {(attackMod ? "attack" : "health")}.");
        }

        // ==================================================================
        // WHICH FIRE THIS IS, BEFORE THE PLAYER TOUCHES ANYTHING. (0.7.136.)
        //
        // OnSlotSelected tells IKMA the stat, but only once the slot is chosen —
        // so the line that ASKS the player to choose could not name it, and
        // Zamar has now asked for that twice. A sighted player sees which fire
        // it is the moment the screen appears, from the icon on the slot.
        //
        // SO READ THE SAME THING THEY DO. CardStatBoostSequencer holds
        // attackModSlotTexture and healthModSlotTexture, and the slot is
        // rendered with one of them. Comparing the slot's live texture against
        // those two fields is the game answering its own question — the icon
        // IS the announcement, and this reads it rather than inferring it.
        //
        // Falls back to OnSlotSelected, which is still the surer signal once it
        // has fired, and to nothing at all if neither can answer. A fire whose
        // stat cannot be established says "one of its stats" rather than
        // guessing at the more common one.
        // ==================================================================
        private static System.Reflection.FieldInfo _attackTexField;
        private static System.Reflection.FieldInfo _healthTexField;
        private static bool _texFieldsResolved;

        private static bool? StatFromSlotTexture()
        {
            if (!_texFieldsResolved)
            {
                _texFieldsResolved = true;

                const System.Reflection.BindingFlags any =
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic;

                _attackTexField = typeof(CardStatBoostSequencer).GetField("attackModSlotTexture", any);
                _healthTexField = typeof(CardStatBoostSequencer).GetField("healthModSlotTexture", any);

                if (_attackTexField == null || _healthTexField == null)
                    _log?.LogWarning(
                        "IKMA NODE: the campfire slot textures did not resolve — " +
                        "the stat cannot be named before the slot is chosen.");
            }

            if (_attackTexField == null || _healthTexField == null) return null;

            try
            {
                var seq = _sequencer as CardStatBoostSequencer;
                if (seq == null) return null;

                var attackTex = _attackTexField.GetValue(seq) as Texture;
                var healthTex = _healthTexField.GetValue(seq) as Texture;
                if (attackTex == null && healthTex == null) return null;

                foreach (var r in seq.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;

                    Texture live = null;
                    try { live = r.sharedMaterial?.mainTexture; } catch { }
                    if (live == null) continue;

                    if (attackTex != null && ReferenceEquals(live, attackTex)) return true;
                    if (healthTex != null && ReferenceEquals(live, healthTex)) return false;
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: reading the slot texture threw {e.GetType().Name}.");
            }

            return null;
        }

        private static string CampfireStatWord()
        {
            // The game's word once it has spoken; the icon before that.
            if (_campfireIsAttackMod == null)
            {
                var fromIcon = StatFromSlotTexture();
                if (fromIcon != null)
                {
                    _campfireIsAttackMod = fromIcon;
                    _log?.LogInfo(
                        $"IKMA NODE: campfire stat read from the slot icon — " +
                        $"{(fromIcon.Value ? "attack" : "health")}.");
                }
            }

            if (_campfireIsAttackMod == null) return null;

            if (!_statTextResolved)
            {
                _statTextResolved = true;
                _statTextMethod = typeof(CardStatBoostSequencer).GetMethod(
                    "GetTranslatedStatText",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);

                if (_statTextMethod == null)
                    _log?.LogWarning(
                        "IKMA NODE: CardStatBoostSequencer.GetTranslatedStatText did not resolve — " +
                        "the campfire lines will not name the stat.");
            }

            if (_statTextMethod == null) return null;

            try
            {
                var seq = _sequencer as CardStatBoostSequencer;
                if (seq == null) return null;

                return _statTextMethod.Invoke(
                    seq, new object[] { _campfireIsAttackMod.Value }) as string;
            }
            catch { return null; }
        }

        /// <summary>The name of the card sitting in the selection slot, or null.</summary>
        private static string CampfireChosenCard()
        {
            try
            {
                foreach (var slot in _sequencer.GetComponentsInChildren<SelectCardFromDeckSlot>(true))
                {
                    if (slot?.Card?.Info == null) continue;
                    string n = CardReader.CardName(slot.Card);
                    if (!string.IsNullOrEmpty(n)) return n;
                }
            }
            catch { }
            return null;
        }

        // ======================================================================
        // HOW MUCH THE FIRE RAISES IT. (0.7.131.)
        //
        // Zamar: "I need that value the sighted player can read to be read out
        // loud before the choice. That's a non-negotiable parity difference."
        //
        // He is right. The fire card shows "+1" in plain view, so withholding it
        // is the parity gap this project exists to close, and "before the
        // choice" is the whole point — a number reported afterwards is a
        // receipt, not information.
        //
        // THE PROBLEM: THERE IS NO FIELD TO READ. CardStatBoostSequencer
        // declares two textures, two lights, a pile, a slot, a stone, a
        // GameObject and a list of figurines. No amount. ApplyModToCard builds
        // the modification inline, so the number is a literal in a method body,
        // and reflection cannot see method bodies. Every previous approach on
        // this project would have dead-ended here.
        //
        // THE SOLUTION: HARMONY CAN READ IL. PatchProcessor.ReadMethodBody
        // walks the method's instructions, and a literal small integer is an
        // Ldc_I4 operand sitting right there. So IKMA asks the game's own code
        // what number it is about to add, at load time, once.
        //
        // WHY THIS IS NOT A RULE BREAK. The standing rule forbids PUBLISHING or
        // COMMITTING decompiled game source, and nothing here is written down or
        // shipped — this reads one integer out of the assembly already loaded in
        // memory, the same way reflection reads a field, to report a number the
        // game is displaying on screen. No logic is reproduced and no method is
        // reimplemented.
        //
        // AND IT REFUSES RATHER THAN GUESSES. If the scan finds no candidate, or
        // more than one distinct plausible value, the amount is dropped and the
        // line names the stat alone. A number IKMA is unsure of, on a screen
        // that can cost the player a card, is exactly the confidently wrong
        // announcement this project treats as worse than silence.
        //
        // THE POSTFIX BELOW IS THE CHECK. Plugin patches ApplyModToCard and logs
        // the adjustment the game ACTUALLY applied, so the next log says whether
        // the IL read was right. Instrument the guess; never trust it silently.
        // ======================================================================
        // WHAT THE FIRE SHOWS, READ OFF THE SCREEN BY ZAMAR. (0.7.133.)
        //
        // The IL scan found candidates 1 and 2 in ApplyModToCard and refused to
        // pick, which was the right call — and his answer explains why BOTH are
        // in there: "Power is always +1, health or toughness is always +2."
        // Those are the two literals, one per branch, and no scan of constants
        // could ever have told them apart without knowing which branch it was
        // in. The refusal was correct and the resolution was always going to
        // come from him.
        //
        // Keyed on the stat the game reports through OnSlotSelected, so the
        // right one is chosen at runtime rather than assumed.
        private const int BOOST_IF_ATTACK = 1;
        private const int BOOST_IF_HEALTH = 2;

        private static int _boostAmount = -1;
        private static bool _boostResolved;

        private static int BoostAmount()
        {
            // The IL scan runs once for its log line and its cross-check; the
            // ANSWER comes from the stat, because the two branches differ.
            ScanBoostIL();

            if (_campfireIsAttackMod == null) return -1;
            return _campfireIsAttackMod.Value ? BOOST_IF_ATTACK : BOOST_IF_HEALTH;
        }

        private static void ScanBoostIL()
        {
            if (_boostResolved) return;
            _boostResolved = true;

            try
            {
                var method = typeof(CardStatBoostSequencer).GetMethod(
                    "ApplyModToCard",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);

                if (method == null)
                {
                    _log?.LogWarning("IKMA NODE: ApplyModToCard did not resolve.");
                    return;
                }

                var found = new List<int>();
                foreach (var pair in HarmonyLib.PatchProcessor.ReadMethodBody(method))
                {
                    // Small positive literals only. 0 and 1 both appear as bare
                    // opcodes and as operands depending on the compiler, so both
                    // forms are read; anything outside 1..9 is not a stat boost
                    // and is ignored rather than argued with.
                    int? v = null;
                    string op = pair.Key.ToString();

                    if (op == "ldc.i4" && pair.Value is int) v = (int)pair.Value;
                    else if (op == "ldc.i4.s" && pair.Value is sbyte) v = (sbyte)pair.Value;
                    else if (op.StartsWith("ldc.i4.") && op.Length == 8)
                    {
                        int parsed;
                        if (int.TryParse(op.Substring(7), out parsed)) v = parsed;
                    }

                    if (v != null && v.Value >= 1 && v.Value <= 9 && !found.Contains(v.Value))
                        found.Add(v.Value);
                }

                if (found.Count == 1)
                {
                    _boostAmount = found[0];
                    _log?.LogInfo($"IKMA NODE: campfire boost amount read from ApplyModToCard = {_boostAmount}.");
                }
                else
                {
                    // THE IL READ WAS AMBIGUOUS AND HIS EYES WERE NOT. (0.7.132.)
                    //
                    // His 0.7.131 log: "ApplyModToCard has 2 candidate amounts
                    // (1, 2)". The scan did the right thing by refusing — but
                    // refusing leaves the parity gap he called non-negotiable,
                    // and there is a better source sitting right here.
                    //
                    // ZAMAR READ THE FIRE. Twice, unprompted: "this will
                    // permanently increase it's power by 1" and "permanently
                    // raising its Power by 1." That is the same authority the
                    // puzzle codes and the ink-page fragments come from — the
                    // player reporting what is printed on the screen — and it is
                    // the one source this project trusts above its own
                    // inference.
                    //
                    // THE POSTFIX IS STILL THE CHECK. ApplyModToCard reports
                    // what the game actually applied on every use, next to what
                    // IKMA announced. If a challenge or a future patch ever
                    // makes this something other than 1, the log says so on the
                    // first campfire of the run rather than never.
                    _log?.LogInfo(
                        $"IKMA NODE: ApplyModToCard has {found.Count} candidate amounts " +
                        $"({string.Join(", ", found.ConvertAll(x => x.ToString()).ToArray())}) — " +
                        "one per branch. Using Zamar's rule: attack +1, health +2. " +
                        "The ApplyModToCard postfix checks it every use.");
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: reading the boost amount threw {e.GetType().Name}.");
            }
        }

        /// <summary>"its power by 1", "its power", or null.</summary>
        private static string BoostPhrase()
        {
            string stat = CampfireStatWord();
            if (stat == null) return null;

            int by = BoostAmount();
            return Vocabulary.NodeScreens.BoostPhrase(by, stat);
        }

        /// <summary>
        /// What the game actually applied, so the IL read can be checked against
        /// reality rather than believed.
        /// </summary>
        private static bool _boostApplied;

        internal static void NoteBoostApplied(int attack, int health)
        {
            _boostApplied = true;

            _log?.LogInfo(
                $"IKMA NODE: campfire applied attack {attack:+#;-#;0}, health {health:+#;-#;0} " +
                $"(IKMA announced {(BoostAmount() > 0 ? BoostAmount().ToString() : "no number")}).");
        }

        private static string CampfireChooseLine()
        {
            string stat = CampfireStatWord();
            string card = CampfireChosenCard();

            string phrase = BoostPhrase();
            string verb = Vocabulary.NodeScreens.ThisWillPermanentlyOrThisWillPermanently(phrase);

            // NO "CHOOSE A DIFFERENT CARD" LINE. It was written on my own
            // assumption that swapping was allowed, and it is not — see the
            // filled-slot guard in Parts(). A slot with a card in it is never
            // offered now, so this line only ever describes an empty one.
            return Vocabulary.NodeScreens.CampfireChooseLine(verb);
        }

        private static string CampfireConfirmLine()
        {
            // HIS WORDING, 0.7.156: "Anytime after the first time, say
            // 'Continue resting by the campfire, and raise the stat again.'"
            //
            // The second confirm is a different decision from the first. The
            // first sends a card; the second is Leshy's "PUSH YOUR LUCK? OR
            // PULL AWAY?" — the card is already at the fire and the question is
            // whether to risk it again. "Send Mealworm to..." described the
            // first choice and kept describing it.
            //
            // _boostApplied is the game's own answer, set by the ApplyModToCard
            // postfix — a boost has landed, so this cannot be the first confirm.
            // Same flag the "already rested" gate uses; one fact, one source.
            // ==============================================================
            // ONCE THE SURVIVORS ARE DEAD, THE FIRE IS NOT A RISK. (0.7.300.)
            //
            // ZAMAR'S WORDING, verbatim: "In this special state where the
            // survivors are killed by the Ringworm, it should change to 'Send
            // [card name] to peacefully rest by the campfire, permanently
            // raising its Health by 2.'" and "Continue resting peacefully by
            // the campfire, and raise the stat again."
            //
            // ASKED OF THE GAME, NOT REMEMBERED. RunState.Run.survivorsDead is
            // the flag CardStatBoostSequencer itself branches on — it is set
            // when a card with Trait.KillsSurvivors or Deathtouch is destroyed
            // at the fire, and from then on every death check in that
            // sequencer is gated behind !survivorsDead. So "risk" stops being
            // true at exactly the moment this returns true, and IKMA is
            // reading the same fact the game acts on rather than counting
            // visits or watching for a Ring Worm itself.
            //
            // A LINE CAN OUTLIVE ITS OWN TRUTH: "risk" was correct when it was
            // written and becomes a false claim about the table the moment the
            // survivors die. Same defect class as the standing help that went
            // on apologising for a feature that had shipped.
            // ==============================================================
            bool safe = SurvivorsDead();

            if (_boostApplied)
                return Vocabulary.NodeScreens.ContinueRestingPeacefullyOrContinueRestingBy(safe);

            string card = Vocabulary.NodeScreens.ChosenCard(CampfireChosenCard());
            string verb = Vocabulary.NodeScreens.ToPeacefullyRestOrToRiskResting(safe);

            string phrase = BoostPhrase();
            return Vocabulary.NodeScreens.SendTheCampfireOrSendTheCampfire(phrase, card, verb);
        }

        /// <summary>
        /// Has the Ring Worm already eaten the survivors? The game's own flag,
        /// asked every time. False if it cannot be read, because "risk" is the
        /// safer thing to be wrong about. (0.7.300.)
        /// </summary>
        private static bool SurvivorsDead()
        {
            try { return RunState.Run != null && RunState.Run.survivorsDead; }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: survivorsDead unreadable: {e.GetType().Name}.");
                return false;
            }
        }

        private static string CardPrompt()
        {
            if (_slotBeingFilled == Vocabulary.NodeScreens.HostCard)         return Vocabulary.NodeScreens.ChooseACardToBecome;
            if (_slotBeingFilled == Vocabulary.NodeScreens.CardToSacrifice) return Vocabulary.NodeScreens.ChooseACardToSacrifice;

            // THE CAMPFIRE ASKS WITH NO SLOT, so it is keyed on the screen.
            // (0.7.123.)
            //
            // The sacrifice stone fills named slots and the slot name is the
            // question; the campfire just lays the deck out and waits, so
            // _slotBeingFilled is null and the slot table can never answer for
            // it. His 0.7.122 log: "no card prompt written for slot '<none>'".
            //
            // _screenName is IKMA's own approved name for the node, from the
            // friendly-name table — not a GameObject name — so keying on it
            // stays inside the mod's vocabulary rather than the game's.
            //
            // HIS LINE, VERBATIM, ELLIPSIS INCLUDED. Zamar, Session 18. The
            // trailing "..." is his and is not a typo to tidy: the campfire is
            // the one node in Act 1 that can cost you the card you pick, and
            // the pause is the warning doing its job.
            if (_screenName == Vocabulary.Campfire)
                return Vocabulary.NodeScreens.ChooseACardToWarm;

            // THE TRADER SAYS WHAT IT IS. (0.7.359.) Zamar, on hearing "Choose a
            // card.": "This line should have explained what this screen is.
            // 'Trader. Trade pelts from your deck for new cards. You have [x]
            // pelts left to trade.'" The pelts are all down before the first
            // offer is dealt — CreatePeltCards, then the pelt dialogue, then
            // CreateTradeCards — so the count has settled by the time this
            // speaks, and each later tier opens a new layout and says it again.
            if (_screenName == Vocabulary.Trader)
                return Vocabulary.NodeScreens.TraderArrival(PeltsOnTable());

            // 0.7.446 - COPY CARD HAS ITS ANSWER. Zamar, Session 42: the Copy
            // card pick prompt (the game's own text, then "Choose a card.") is
            // "fine for now". The warning below is the worklist of prompts he
            // has not ruled on; this one he has, so it no longer logs here.
            if (_screenName == Vocabulary.NodeScreens.CopyCard)
                return Vocabulary.NodeScreens.ChooseACard;

            _log?.LogWarning(
                $"IKMA NODE: no card prompt written for slot '{_slotBeingFilled ?? "<none>"}' " +
                $"on screen '{_screenName ?? "<none>"}'. Ask Zamar for a line.");
            return Vocabulary.NodeScreens.ChooseACard;
        }

        // ----------------------------------------------------------------------
        // THE FIRST PRESS LANDS ON THE FIRST CARD. (0.7.218.)
        //
        // Zamar, sacrifice stone, choosing a host: "pressing the right arrow
        // chooses my second card first. It read Black Goat first instead of
        // Corpse Maggots here."
        //
        // _cardIndex started at 0 and the first press added the direction to
        // it, so a right arrow went straight to card 2 and card 1 could only be
        // reached by wrapping all the way round. The deck opens with nothing
        // announced, so 0 was not "the player is on card 1" — it was "the
        // player is nowhere", and the two states were being held in the same
        // value.
        //
        // -1 is now "nowhere", which the first press resolves to an END of the
        // row rather than a step from it: right lands on the first card, left
        // on the last. Every press after that steps as before. This is the same
        // shape CardChoiceReader and TradeReader use, and the same reason.
        // ----------------------------------------------------------------------
        private static bool BrowseCards(int direction)
        {
            var cards = OpenCards();
            if (cards.Count == 0) { ReleaseCardHover(); return false; }

            if (_cardIndex >= cards.Count) _cardIndex = NOWHERE;

            if (_cardIndex == NOWHERE)
                _cardIndex = direction >= 0 ? 0 : cards.Count - 1;
            else
            {
                _cardIndex += direction;
                if (_cardIndex < 0) _cardIndex = cards.Count - 1;
                if (_cardIndex >= cards.Count) _cardIndex = 0;
            }

            HoverCard(cards);
            Speech.Browse(DescribeCard(cards, _cardIndex));
            return true;
        }

        private static bool ActivateCard()
        {
            var cards = OpenCards();
            if (cards.Count == 0) return false;
            // Enter with nothing browsed yet takes the first card, which is
            // what the cursor is sitting on.
            if (_cardIndex < 0 || _cardIndex >= cards.Count) _cardIndex = 0;

            var card = cards[_cardIndex];
            _log?.LogInfo($"IKMA NODE: choosing card {_cardIndex + 1} of {cards.Count}.");

            // HIS LINE: "[card name] selected as Host card" — and it stomps the
            // pending commentary, because taking a game action outranks whatever
            // was still being said about the screen you were reading.
            string chosen = null;
            try { chosen = CardReader.CardName(card); } catch { }

            // THE CUT IS NOT CONDITIONAL ON HAVING A SENTENCE. (0.7.318.)
            //
            // Zamar: "When choosing the card to copy, stomp its info." His log
            // shows why it did not: the first time through, the slot had not
            // been activated by the reader, _slotBeingFilled was null, and the
            // whole block was skipped — so the long card read he had just
            // triggered carried on over the top of his choice. Dropping
            // pending commentary is what taking an action means here; whether
            // there is a word for the slot is a separate question.
            CombatAnnouncer.DropCommentary("card chosen at a node screen");

            // The screen's noun form first, then the slot's own name. See the
            // label table above.
            string chosenAs = ChosenAsLabel(_slotBeingFilled);
            if (string.IsNullOrEmpty(chosenAs)) chosenAs = _slotBeingFilled;

            if (!string.IsNullOrEmpty(chosen) && chosenAs != null)
            {
                // A LABEL, NOT A SENTENCE. (0.7.133.)
                //
                // His 0.7.132 log: "Mantis God selected as Choose a card to warm
                // by the campfire. This will permanently raise one of its
                // stats.." — the slot's browse line got substituted into a
                // sentence built for a short noun like "Host Card".
                //
                // My doing, and the lesson is worth keeping: ONE NAME CANNOT BE
                // BOTH A LABEL AND A PROMPT. Browsing a slot wants a sentence
                // that explains it; a confirmation wants two words. Composing
                // the long one into the short one's slot was always going to
                // read as a mangle.
                // THE CAMPFIRE'S CONFIRMATION IS ITS OWN SENTENCE. (0.7.135.)
                //
                // "Mantis God selected as the fire." was my label patch on a
                // sentence built for the sacrifice stone's named slots, and it
                // read as nonsense — his words: "what is this line."
                //
                // The stone fills slots, so "X selected as Host Card" is right
                // there. The campfire does not have slots, it has an act. His
                // line for it, verbatim.
                // HIS WORDING, 0.7.157: "Change this to just [card name] chosen."
                // The rest of the old sentence — "to risk resting by the
                // campfire" — is the screen he is standing on and the confirm
                // line he is about to hear. Said here it is a third telling.
                if (_screenName == Vocabulary.Campfire)
                {
                    Speech.Browse(Vocabulary.NodeScreens.Chosen(chosen));
                }
                else
                {
                    Speech.Browse(Vocabulary.NodeScreens.SelectedAs(chosen, chosenAs));
                }
            }

            // Session 32: which pelt pays, read BEFORE the click. The game's
            // TradePeltsSequencer.OnCardSelected spends the LAST entry of its
            // peltCards list (RemoveLastPelt).
            string peltName = null;
            if (_screenName == Vocabulary.Trader)
            {
                var pelts = SequencerCardList("peltCards", ref _peltCardsField);
                if (pelts != null && pelts.Count > 0)
                {
                    try { peltName = CardReader.CardName(pelts[pelts.Count - 1]?.Info); } catch { }
                }
            }

            try { card.CursorSelectStart(); card.CursorSelectEnd(); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: choosing a card threw {e.GetType().Name}.");
            }

            // A TRADE SAYS IT HAPPENED. (0.7.359.) The Trader has no slot and no
            // label, so the confirmation above never spoke there and Enter was
            // answered with silence. TradePeltsSequencer.OnCardSelected runs
            // inside CursorSelectStart and takes the card out of tradeCards when
            // the trade goes through, so the game's own list says whether it did
            // — announce what is true, not what was attempted. The words are the
            // Trader battle's trade line (Vocabulary.TradeTaken).
            if (_screenName == Vocabulary.Trader && !string.IsNullOrEmpty(chosen))
            {
                var offers = SequencerCardList("tradeCards", ref _tradeCardsField);
                if (offers != null && !offers.Contains(card))
                    using (Speech.Event(EventKind.CardObtained, EventSource.CurrentPlayer)) Speech.Confirm(Vocabulary.TradeTaken(peltName, chosen));
                else
                    _log?.LogInfo("IKMA NODE: Enter on a Trader offer, and the game did not take the trade.");
            }

            ReleaseCardHover();
            _cardIndex = NOWHERE;
            return true;
        }

        public static void Browse(int direction)
        {
            // Cards on the table? They own the arrows.
            if (BrowseCards(direction)) return;

            var parts = Parts();
            if (parts.Count == 0)
            {
                // Silent once the screen has emptied — it is on its way out and
                // Tick is about to hand the keyboard back. Zamar's 0.7.103 log
                // had this line eleven times in a row after Begin Ritual.
                if (!_partsSeen) Speech.Browse(NothingToChooseLine());
                return;
            }

            // Arrival named no option, so the first press lands ON option one
            // rather than past it. Same shape as BrowseCards above.
            if (_index >= parts.Count) _index = NOWHERE;

            if (_index == NOWHERE)
                _index = direction >= 0 ? 0 : parts.Count - 1;
            else
            {
                _index += direction;
                if (_index < 0) _index = parts.Count - 1;
                if (_index >= parts.Count) _index = 0;
            }

            HoverCurrent(parts, reassert: true);

            // Session 51 (0.7.463): "Host Card", "Card to Sacrifice" and
            // "Begin Ritual" were spoken with no stop after them. Zamar: a
            // stop after each.
            Speech.Browse(WithStop(DescribePart(parts[_index])));
        }

        private static string WithStop(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            char last = line[line.Length - 1];
            return (last == '.' || last == '!' || last == '?' || last == ':' || last == '\u2026') ? line : line + ".";
        }

        /// <summary>
        /// A part, and what is sitting in it. Zamar: "After a card is on the
        /// stone, I'd like it to read what it is. 'Host Card: Mantis God.'"
        ///
        /// SelectCardFromDeckSlot.Card is PUBLIC, so the slot is asked what it
        /// holds rather than IKMA remembering what was put there — a card the
        /// player moved with the mouse is reported correctly either way.
        /// </summary>
        private static string DescribePart(MainInputInteractable it)
        {
            // WHAT THE CARVING IS. (0.7.249.)
            //
            // Zamar, browsing the Woodcarver's three offerings and hearing
            // "Item slot left", "Item slot center", "Item slot right":
            //
            //   "This gives me zero information as to what I'm picking."
            //
            // He is right, and the information was in his own log the whole
            // time — "an item with no rulebook name (type TotemTopData)" and
            // "(type TotemBottomData)". 0.7.248 shipped a fallback built from
            // the Unity object's NAME when the thing to read was the object's
            // CONTENTS. A fallback name is for a control that holds nothing; a
            // slot with a carving in it should be read as the carving.
            //
            // Asked of the game, never guessed: ItemSlot.Item is public,
            // Item.Data is public, and BuildTotemSequencer itself branches on
            // exactly these two casts to decide what it just handed the player.
            string piece = TotemPieceLine(it as SelectableItemSlot);
            if (!string.IsNullOrEmpty(piece)) return piece + TotemHeadDeckCount(it as SelectableItemSlot);

            string ware = TrapperWareLine(it);
            if (!string.IsNullOrEmpty(ware)) return ware;

            string boon = BoonCardLine(it);
            if (!string.IsNullOrEmpty(boon)) return boon;

            // 0.7.411 — ANY OTHER CARD ON THE TABLE IS READ AS A CARD. The item
            // pickup with a full pack sets GainConsumablesSequencer.ratCard to
            // its fullConsumablesReward (Pack Rat); with no card branch here it
            // fell through to the Unity object name, "Card ( pack rat)".
            string plainCard = PlainCardLine(it);
            if (!string.IsNullOrEmpty(plainCard)) return plainCard;

            string name = Vocabulary.PartNameOrUnnamed(PartName(it));

            var slot = it as SelectCardFromDeckSlot;
            if (slot == null) return name;

            // THE MYCOLOGISTS' SLOT NAMES NO CARD. (0.7.291.) Zamar, hearing
            // "Select a pair of cards to fuse together: Flying Ant.": "drop
            // the card name there". What is on the stone is a PAIR, and the
            // pair has already been announced when it was chosen; appending
            // one of its two cards to the slot's own name describes neither
            // the option nor what is sitting in it.
            //
            // 0.7.329 — ONCE A PAIR IS ON THE STONE, THE SLOT SAYS WHICH.
            // Zamar, hearing "Select a pair of cards to fuse together" after
            // choosing: "This should have read 'Pair of Flying Ants chosen to
            // fuse together. Enter to rechose.'" His words. The pair is the
            // game's own SelectCardPairFromDeckSlot.SelectedPair (public).
            if (_screenName == Vocabulary.Mycologists)
            {
                var pairSlot = slot as SelectCardPairFromDeckSlot;
                SelectableCardPair onStone = null;
                try { onStone = pairSlot?.SelectedPair; } catch { }
                string pairBase = null;
                try { pairBase = CardReader.BaseCardName(onStone?.LeftCard?.Info ?? onStone?.RightCard?.Info); } catch { }
                if (!string.IsNullOrEmpty(pairBase))
                    return Vocabulary.NodeScreens.PairOfChosenTo(Pluralise(pairBase));
                return name;
            }

            SelectableCard held = null;
            try { held = slot.Card; } catch { }
            if (held == null) return name;

            CardInfo info = null;
            try { info = held.Info; } catch { }

            string cardName = null;
            try { cardName = CardReader.CardName(info); } catch { }

            if (string.IsNullOrEmpty(cardName)) return name;

            // A FILLED SLOT IS NOT A PROMPT EITHER. (0.7.319.)
            //
            // "Choose a card to receive an artistic rendition of: Mealworm."
            // reads as an instruction with a card stuck on the end — it tells
            // the player to choose something they have already chosen. Zamar's
            // line, verbatim: "Mealworm chosen as card to receive an artistic
            // rendition. Enter to rechose."
            //
            // THE CARD LEADS. (0.7.320.) Zamar, stating it as a general rule
            // rather than a fix to this line: "I want the card info first for
            // snappiness for screen reading. Always want to word so important
            // info is asap in the reading."
            //
            // "Host Card: Flying Ant." puts a label the player already knows in
            // front of the one thing they pressed the key to find out. Same
            // table as the confirmation, so the noun form has one source.
            string chosenAs = ChosenAsLabel(name);

            if (!string.IsNullOrEmpty(chosenAs))
                return Vocabulary.NodeScreens.ChosenAsEnterTo(cardName, chosenAs);

            return Vocabulary.NodeScreens.Word(name, cardName);
        }

        // ==================================================================
        // THE BONE LORD'S BOON CARD NAMES ITSELF. (0.7.324.)
        //
        // From his log: the reward the Bone Lord puts down was browsable as
        // "Selectable card( clone)" — the GameObject's name, prettified, which
        // is an internal id read aloud and a mangled one at that. It also
        // logged "no approved name ... Ask Zamar for a word", and that ask was
        // wrong: THERE IS NO WORD TO ASK FOR, because the game has its own.
        //
        // CardRemoveSequencer builds this card with
        // BoonsUtil.CreateCardForBoon, so CardInfo.boon carries the boon type
        // and BoonData has displayedName and description on it — PUBLIC, and
        // already confirmed in dump_rulebook_content.txt where the rulebook
        // reader uses the same two fields.
        //
        // THE SENTENCE IS THE GAME'S TOO. CardRemoveSequencer.ExamineBoon
        // formats exactly "A {displayedName}. {description}" when it shows the
        // card the first time, so this is the shape a sighted player is given.
        // Nothing here is invented and nothing is owed.
        //
        // Colour tags come off through RulebookReader.Clean, the one stripper.
        // ==================================================================
        private static string PlainCardLine(MainInputInteractable part)
        {
            var card = part as SelectableCard;
            if (card == null) return null;
            CardInfo info = null;
            try { info = card.Info; } catch { }
            if (info == null || info.boon != BoonData.Type.None) return null;
            return CardReader.DescribeCardInfo(info);
        }

        private static string BoonCardLine(MainInputInteractable part)
        {
            var card = part as SelectableCard;
            if (card == null) return null;

            CardInfo info = null;
            try { info = card.Info; } catch { }
            if (info == null) return null;

            if (info.boon == BoonData.Type.None) return null;

            // 0.7.325 — through CardReader.BoonName / BoonEffect, so the take
            // line below and the bone lines in Plugin.cs cannot drift from this
            // one.
            string name = CardReader.BoonName(info.boon);
            string desc = CardReader.BoonEffect(info.boon);

            if (string.IsNullOrEmpty(name))
            {
                _log?.LogInfo($"IKMA NODE: a boon card of type {info.boon} has no displayed name.");
                return null;
            }

            _log?.LogInfo($"IKMA NODE: boon card read — '{name}' [{info.boon}].");

            return Vocabulary.NodeScreens.BoonCardLine(string.IsNullOrEmpty(desc), name, desc);
        }

        // ==================================================================
        // TAKING THE BOON. (0.7.325.)
        //
        // Zamar: "Need callout for Minor boon of the bone lord recieved or
        // whatever it is called upon choosing sacrifice and picking up placed
        // boon card. It should also explain what that does."
        //
        // CardRemoveSequencer.OnBoonSelected is the pickup: it reads the boon
        // off the card, adds it to the run and disables the card. A POSTFIX,
        // so the line reports a boon the player HAS rather than one that was
        // about to be taken.
        //
        // AND IT STAYS QUIET THE FIRST TIME, because the game does this
        // itself. On a run that has not learned CardRemoval, OnBoonSelected
        // starts ExamineBoon, which shows "A {name}. {description}" as
        // dialogue — IKMA already narrates that. The flag is only set at the
        // very end of RemoveSequence, so it is still false here and answers
        // exactly the question "is the game about to say this".
        //
        // The NAME and the EFFECT are the game's, out of the boon asset; the
        // sentence shape is Zamar's (Session 25). His sketch had the effect as "at the start of each of
        // your turns receive one bone" — the asset says "You will start each
        // battle with 1 extra bone", which is not the same claim, and the
        // game's text is what ships.
        // ==================================================================
        internal static void NoteBoonTaken(MainInputInteractable boonCard)
        {
            try
            {
                var card = boonCard as SelectableCard;
                CardInfo info = null;
                try { info = card?.Info; } catch { }
                if (info == null) return;

                if (info.boon == BoonData.Type.None) return;

                bool gameWillExplain = false;
                try { gameWillExplain = !ProgressionData.LearnedMechanic(MechanicsConcept.CardRemoval); }
                catch { }

                string name = CardReader.BoonName(info.boon);
                if (string.IsNullOrEmpty(name))
                {
                    _log?.LogInfo($"IKMA NODE: a boon of type {info.boon} was taken but has no displayed name.");
                    return;
                }

                if (gameWillExplain)
                {
                    _log?.LogInfo(
                        $"IKMA NODE: '{name}' taken — not spoken, the game examines it itself " +
                        "on a first Bone Lord.");
                    return;
                }

                string effect = CardReader.BoonEffect(info.boon);

                // ZAMAR'S WORDING, Session 25: "when that SFX plays it should
                // read Recieved minor boon etc." "Received" leads. The SFX is
                // PlayQuickRiffleSound, the frame after OnBoonSelected sets
                // boonTaken — this postfix is that moment.
                // 0.7.329, Zamar: remove the effect sentence from this line —
                // the boon card already read it on the altar.
                using (Speech.Event(EventKind.NodeResults)) Speech.Result(Vocabulary.NodeScreens.ReceivedBoon(name));
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: boon taken threw {e.GetType().Name}.");
            }
        }

        /// <summary>
        /// A totem slot said as what is standing in it: a carved animal head, a
        /// carved sigil, or the two assembled. "" when the slot is not a totem
        /// slot or is empty, so the caller falls through to its ordinary naming.
        /// </summary>
        /// <remarks>
        /// IKMA PROVISIONAL — Zamar has not ruled on any of these three
        /// sentences. They describe what the carving IS, which is what the
        /// screen shows: BuildTotemSequencer reads TotemTopData.prerequisites
        /// .tribe and TotemBottomData.effectParams.ability and nothing else, so
        /// there is no fourth thing about a piece that is being held back.
        /// </remarks>
        /// <summary>
        /// " You have 3 Bird Kin cards in your deck." after a totem head's own
        /// line, "" for anything that is not a head with a kin.
        /// </summary>
        /// <remarks>
        /// 0.7.433. Zamar, Session 41: "on all the head hovers, can we have it
        /// say how many cards of that kin type are in your deck right away?"
        /// and, Session 42, the sentence. Counted with the game's own
        /// CardInfo.IsOfTribe over RunState.DeckList. Added at the hover only:
        /// the "You took the Bird head." line is built from TotemPieceLine
        /// and must not carry it.
        /// </remarks>
        private static string TotemHeadDeckCount(SelectableItemSlot slot)
        {
            try
            {
                var top = slot?.Item?.Data as TotemTopData;
                if (top == null) return "";

                Tribe tribe = top.prerequisites.tribe;
                if (tribe == Tribe.None) return "";

                var deck = RunState.DeckList;
                if (deck == null) return "";

                int n = 0;
                foreach (var card in deck)
                    if (card != null && card.IsOfTribe(tribe)) n++;

                return " " + Vocabulary.NodeScreens.YouHaveKinCards(tribe, n);
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: counting the deck for a totem head threw {e.GetType().Name}.");
                return "";
            }
        }

        private static string TotemPieceLine(SelectableItemSlot slot)
        {
            if (slot == null) return "";

            ItemData data = null;
            try { data = slot.Item?.Data; } catch { }
            if (data == null) return "";

            try
            {
                var top = data as TotemTopData;
                if (top != null)
                {
                    Tribe tribe = Tribe.None;
                    try { tribe = top.prerequisites.tribe; } catch { }
                    return Vocabulary.NodeScreens.CarvedHeadOrHead(tribe);
                }

                var bottom = data as TotemBottomData;
                if (bottom != null)
                {
                    Ability ability = Ability.None;
                    try { ability = bottom.effectParams.ability; } catch { }
                    string sigil = CardReader.GetAbilityName(ability);
                    // "SIGIL BODY", not "sigil". (0.7.275, his correction.)
                    // The screen's own help line already calls the two halves
                    // "a Kin type head or a Sigil body" — his words — and a
                    // carving read as "Bees Within sigil" left the player to
                    // work out which half of a totem they were holding. The
                    // head says "head"; this says "body".
                    return Vocabulary.NodeScreens.CarvedSigilBodyOrSigilBody(string.IsNullOrEmpty(sigil), sigil);
                }

                // The assembled totem on its stand. Said as what it will DO,
                // because that is the question the player is confirming.
                var whole = data as TotemItemData;
                if (whole != null)
                {
                    Tribe tribe = Tribe.None;
                    Ability ability = Ability.None;
                    try { tribe   = whole.top.prerequisites.tribe; }   catch { }
                    try { ability = whole.bottom.effectParams.ability; } catch { }

                    string sigil = CardReader.GetAbilityName(ability);
                    if (tribe != Tribe.None && !string.IsNullOrEmpty(sigil))
                        return Vocabulary.NodeScreens.TotemYourCardsGain(tribe, sigil);

                    return Vocabulary.NodeScreens.FinishedTotem;
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: reading a totem piece threw {e.GetType().Name}.");
            }

            return "";
        }

        /// <summary>Space — the settled shape: the thing, then the position.</summary>
        public static void SpeakPosition()
        {
            var openCards = OpenCards();
            if (openCards.Count > 0)
            {
                if (_cardIndex < 0 || _cardIndex >= openCards.Count) _cardIndex = 0;
                Speech.Browse(
                    Vocabulary.NodeScreens.OptionOf(DescribeCard(openCards, _cardIndex), _cardIndex + 1, openCards.Count));
                return;
            }

            var parts = Parts();
            if (parts.Count == 0) { Speech.Browse(NothingToChooseLine()); return; }

            // Session 34, Zamar: at the Woodcarver, with the finished totem on
            // its stand, X / Space repeats the "totem is assembled" line
            // instead of "Totem: ... Option 1 of 1."
            if (_screenName == Vocabulary.Woodcarver && !string.IsNullOrEmpty(LastTotemAssembledLine)
                && !InBackpackView && FinishedTotemShowing(parts))
            {
                Speech.Browse(LastTotemAssembledLine);
                return;
            }

            if (_index >= parts.Count) _index = NOWHERE;

            // AT NOWHERE, SPACE DESCRIBES THE SCREEN, NOT OPTION ONE. His
            // 0.7.242 rule: "If we're doing the -1 default thing, then we can't
            // read the first option on -1 as well." Clamping to 0 here would
            // have Space quietly move the cursor onto something.
            if (_index == NOWHERE)
            {
                // IKMA PROVISIONAL — Zamar has not ruled on this wording.
                Speech.Browse(
                    Vocabulary.NodeScreens.OptionSLeftAnd(ScreenTitle(), parts.Count));
                return;
            }

            Speech.Browse(
                Vocabulary.NodeScreens.OptionOfName(DescribePart(parts[_index]).TrimEnd('.'), _index + 1, parts.Count));
        }

        /// <summary>
        /// Enter. The click primitive, so the game decides what choosing this
        /// part does — opening the deck to pick a card, or beginning the ritual.
        /// </summary>
        public static void Activate()
        {
            if (ActivateCard()) return;

            // NOT YET. See EnterChoosesInBackpack — while a carving is still on
            // the table the backpack is somewhere to look, not somewhere to
            // choose, and Enter there produced a "You took" line about an item
            // he already owned.
            if (InBackpackView && !EnterChoosesInBackpack())
            {
                _log?.LogInfo(
                    "IKMA NODE: Enter is not bound in the backpack yet — " +
                    $"{CarvingsRemaining()} carving(s) still to pick.");
                return;
            }

            var parts = Parts();
            if (parts.Count == 0) return;

            // ENTER OFF THE ARRIVAL LINE DOES NOT PICK SOMETHING FOR HIM.
            // (0.7.249.) CardChoiceReader clamps NOWHERE to the first card here,
            // because turning a card over costs nothing. These screens are not
            // that: choosing a totem carving, a sacrifice or a card to remove is
            // final, and committing to an option the player has never heard is
            // not a reasonable reading of a keypress. He is told what to press
            // instead.
            // AFTER THE STONE, ENTER TAKES THE ONE THING LEFT. (0.7.326.)
            // Zamar: "No arrows to browse needed, just enter to pick up the
            // bone card." Scoped to a pressed stone so the -1 default rule of
            // 0.7.242 still holds on every screen still asking its question.
            if ((_index < 0 || _index >= parts.Count) && parts.Count == 1 && StoneConfirmed())
                _index = 0;

            if (_index < 0 || _index >= parts.Count)
            {
                Speech.Browse(Vocabulary.NodeScreens.NoOptionSelectedUse);
                return;
            }

            var target = parts[_index];

            // 0.7.424 - a plain card on the table (the rat's Pack Rat) is a
            // card, not an unnamed part: asking PartName for it logged "no
            // approved name... Ask Zamar for a word" for a card the game names.
            //
            // 0.7.446 - the same for a carving. A Woodcarver slot is spoken as
            // the piece in it (TotemPieceLine), never by its Unity name, so the
            // "no approved name for 'ItemSlot_Left'" warning asked Zamar for a
            // word nobody would ever hear. The name below is only logged and
            // compared; a carving slot is not a SelectCardFromDeckSlot.
            string carving = TotemPieceLine(target as SelectableItemSlot);
            string name = PlainCardLine(target) != null
                ? (CardReader.BaseCardName((target as SelectableCard)?.Info) ?? "?")
                : !string.IsNullOrEmpty(carving)
                    ? carving
                    : (PartName(target) ?? "?");
            _log?.LogInfo($"IKMA NODE: activating '{name}' on {_screenName}.");

            // 0.7.361 — CONFIRMING STOMPS THE OPTION LINE. Zamar, at the
            // campfire: "Continue resting by the campfire, and raise the stat
            // again." "should have been stomped after hitting confirm." The
            // line had been queued behind Leshy's and was still to come.
            CombatAnnouncer.DropCommentary("an option was confirmed");
            Speech.Silence();

            // WHAT HE JUST TOOK. (0.7.251.) Zamar: "When I pick a woodcarving,
            // read a line that say confirms which one I took."
            //
            // Captured BEFORE the click, because the click empties the slot —
            // the same rule the card choice screen learned at 0.7.246. The verb
            // comes from the sequencer's own phase: picking a new carving adds
            // it to the backpack, choosing one during the build only selects it
            // for the totem, and calling both "took" would be wrong about one.
            string takenLine = TotemTakeLine(target);

            // ==============================================================
            // THE RITUAL CANNOT BEGIN ON AN EMPTY STONE. (0.7.290.)
            //
            // Zamar, 0.7.289: "Mod broke this segment, i was able to do
            // illegal things." His log ends with fifty consecutive
            // "activating 'Select a pair of cards to fuse together'" and then
            // Begin Ritual, and his screenshot shows the merge stone with
            // both slots empty and a blank card on the table.
            //
            // WHAT HAPPENED, from DuplicateMergeSequencer.MergeSequence: the
            // slot's CursorSelectStarted is wired to OnSlotSelected for the
            // whole screen, so every Enter on it re-opens the deck picker —
            // legal with a mouse, because a player can change their mind.
            // But SelectCardFromDeckSlot.SelectFromCards begins by calling
            // FlyOffCard(), which clears the slot, and confirmStone's
            // WaitUntilConfirmation is running the entire time. Confirming
            // with the picker open runs CombinePair(selectionSlot
            // .SelectedPair) on nothing.
            //
            // GATED ON WHAT THE GAME STILL WANTS, not on what IKMA believes
            // it did — the sacrifice-gate rule. SelectCardPairFromDeckSlot
            // .SelectedPair is PUBLIC and is the game's own record of what is
            // on the stone. Null means there is nothing to fuse, and the
            // press is refused rather than swallowed: this choice is final
            // and destructive, which is the case the screen already refuses
            // Enter at NOWHERE for.
            //
            // This does NOT close the hole underneath it. While the deck
            // picker is open the game is waiting for a card to be chosen
            // THERE, and IKMA is still reading the node's own parts. Reading
            // the picker is the real fix and it is a build of its own.
            // ==============================================================
            if (_screenName == Vocabulary.Mycologists && name == Vocabulary.NodeScreens.BeginRitual && !PairIsOnTheStone())
            {
                _log?.LogWarning(
                    "IKMA NODE: Begin Ritual refused — SelectedPair is null, the stone is empty.");
                Speech.Browse(Vocabulary.NodeScreens.NoPairIsOn);
                return;
            }

            // "Pair of Flying Ants selected." — Zamar, 0.7.289, and captured
            // before the click for the same reason the carving line is: the
            // click hands the pair to the slot and the part stops being what
            // it was.
            // 0.7.435 - THE LIST CHANGES UNDER THE CURSOR HERE, TWICE.
            // Session 43, Zamar, at the Mycologists: "the pair selection needs
            // the -1 default thing. Bat should have been first on right arrow."
            // Pressing the slot swaps the parts for the pairs, and choosing a
            // pair swaps them back, while _index kept pointing into the old
            // list: the cursor sat on pair one without saying so and the first
            // arrow skipped it. Noted before the click, applied after it.
            bool mycologistsListChanges = _screenName == Vocabulary.Mycologists
                && (target is SelectCardPairFromDeckSlot || target is SelectableCardPair);

            string pairChosenLine = null;
            if (_screenName == Vocabulary.Mycologists && target is SelectableCardPair)
            {
                string pairName = null;
                try { pairName = CardReader.BaseCardName((target as SelectableCardPair)?.LeftCard?.Info); } catch { }
                if (!string.IsNullOrEmpty(pairName))
                    pairChosenLine = Vocabulary.NodeScreens.PairOfSelected(Pluralise(pairName));
            }

            // The last chance to read the host before the game takes the screen
            // apart. It is read again when the screen ends, and the difference
            // between the two is the answer.
            if (name == Vocabulary.NodeScreens.BeginRitual) { NoteRitualHost(); NoteSacrificed(); }

            // Which slot opened the deck, so the card chosen next can be
            // reported against the right one.
            _slotBeingFilled = (target is SelectCardFromDeckSlot) ? name : null;

            int teethBefore = TrapperSeq != null ? Teeth() : -1;
            string wareName = TrapperWareName(target);
            int warePrice   = TrapperWarePrice(target);

            try { target.CursorSelectStart(); target.CursorSelectEnd(); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: activating threw {e.GetType().Name}.");
                return;
            }

            if (teethBefore >= 0 && wareName != null)
                TrapperReportPurchase(target, wareName, warePrice, teethBefore);

            if (!string.IsNullOrEmpty(takenLine))
            {
                using (Speech.Event(EventKind.NodeResults)) Speech.Confirm(takenLine);

                // Session 34, Zamar: after a carving is selected in the
                // backpack, the next arrow skipped the first one left ("Morsel
                // should have been first"). The list just changed under the
                // cursor, so the cursor goes back to nowhere - the standing
                // -1 rule (0.7.249) applied after a pick, not only on arrival.
                _index = NOWHERE;
            }

            if (!string.IsNullOrEmpty(pairChosenLine))
            {
                _log?.LogInfo($"IKMA NODE: {pairChosenLine}");
                Speech.Confirm(pairChosenLine);
            }

            // 0.7.435 - the standing -1 rule (0.7.249), as for the carving
            // pick above: a new list starts with the cursor nowhere.
            if (mycologistsListChanges) _index = NOWHERE;
        }

        /// <summary>
        /// Is there actually a pair on the merge stone? Asked of the game's
        /// own public SelectedPair, every time, never remembered.
        /// </summary>
        private static bool PairIsOnTheStone()
        {
            try
            {
                foreach (var part in Parts())
                {
                    var slot = part as SelectCardPairFromDeckSlot;
                    if (slot != null) return slot.SelectedPair != null;
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA NODE: pair check failed: {e.GetType().Name}: {e.Message}");
            }

            // The slot is not among the parts — say so and do not block the
            // press on a question that could not be asked.
            _log?.LogInfo("IKMA NODE: no pair slot among the parts — Begin Ritual not gated.");
            return true;
        }

        /// <summary>
        /// "You took the Insect head." — said when Enter commits to a carving.
        /// "" for anything that is not a totem slot with a piece in it.
        /// </summary>
        /// <remarks>
        /// IKMA PROVISIONAL. The verb is asked of BuildTotemSequencer's own
        /// private `phase` field rather than guessed from the slot's name:
        /// PickingNewPiece adds a carving to the backpack, Building only marks
        /// one for the totem being assembled, and those are different things to
        /// tell a player. Reflection because the field and its enum are private;
        /// compared by ToString so a renamed enum degrades to the neutral line
        /// rather than to a wrong one.
        /// </remarks>
        private static string TotemTakeLine(MainInputInteractable it)
        {
            // Session 34, Zamar: taking the FINISHED totem is its own line -
            // "You take the Canine Kin totem with the Morsel Sigil." (it read
            // "Totem: your Canine cards gain Morsel selected.").
            try
            {
                var whole = (it as SelectableItemSlot)?.Item?.Data as TotemItemData;
                if (whole != null)
                {
                    Tribe tribe = Tribe.None;
                    Ability ability = Ability.None;
                    try { tribe   = whole.top.prerequisites.tribe; }   catch { }
                    try { ability = whole.bottom.effectParams.ability; } catch { }
                    string sigil = CardReader.GetAbilityName(ability);
                    if (tribe != Tribe.None && !string.IsNullOrEmpty(sigil))
                        return Vocabulary.NodeScreens.YouTakeTheTotem(tribe, sigil);
                }
            }
            catch { }

            string piece = TotemPieceLine(it as SelectableItemSlot);
            if (string.IsNullOrEmpty(piece)) return "";

            // "Insect head." -> "Insect head"
            string bare = piece.TrimEnd('.');

            string phase = null;
            try
            {
                var f = _sequencer?.GetType().GetField(
                    "phase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                phase = f?.GetValue(_sequencer)?.ToString();
            }
            catch { }

            if (phase == "PickingNewPiece") return Vocabulary.NodeScreens.YouTookThe(bare);
            if (phase == "Building")        return Vocabulary.NodeScreens.Selected(bare);

            _log?.LogInfo($"IKMA NODE: totem phase read as '{phase ?? "unknown"}'.");
            return Vocabulary.NodeScreens.Chosen(bare);
        }

        /// <summary>
        /// Why there is nothing to browse — and the answer is usually that a
        /// character is mid-sentence. (0.7.126.)
        ///
        /// Zamar: "When pressing the arrow keys while the game has a dialogue to
        /// advance, instead of Nothing to choose here, have it read
        /// 'Conversation in progress, press Space to proceed.'"
        ///
        /// THIS IS WHERE THE ADVANCE KEY IS TAUGHT NOW. The repeating "Press
        /// Space to proceed" prompt is gone — his call, "It ruins the moment" —
        /// so a player who does not know the key learns it the moment they reach
        /// for the keys they would naturally try. Pulled rather than pushed,
        /// which is the right shape for an instruction that is only needed once.
        ///
        /// "Nothing to choose here." stays for the case it was written for: a
        /// screen that genuinely has nothing on it.
        /// </summary>
        private static string NothingToChooseLine()
        {
            try
            {
                if (DialogueAdvancer.AwaitingInput())
                    return Vocabulary.ConversationInProgress;
            }
            catch { }

            return Vocabulary.NodeScreens.NothingToChooseLine;
        }

        private static bool DialogueWaiting()
        {
            try { return DialogueAdvancer.AwaitingInput(); }
            catch { return false; }
        }

        public static void SpeakHelp()
        {
            var openCards = OpenCards();
            if (openCards.Count > 0)
            {
                // THE TRADER'S OWN H. (0.7.359.) Zamar, on "Choosing a card. 8
                // cards. 2 pelts left to trade. Arrows browse, Enter chooses,
                // Space repeats your position.": "Incorrect. 'Trader. Trade pelts
                // from your deck for new cards. You have [x] pelts left to trade.
                // Arrows browse, Enter chooses, Space repeats your position. Shift
                // Up displays your deck.'" His line; the count is the pelts still
                // on the table, the read 0.7.239 added because a sighted player
                // counts them there and a blind one could not.
                if (_screenName == Vocabulary.Trader)
                {
                    Speech.Browse(Vocabulary.NodeScreens.TraderHelp(PeltsOnTable()));
                    return;
                }

                string cardWord = Vocabulary.CardCount(openCards.Count);
                Speech.Browse(Vocabulary.NodeScreens.ChoosingACardArrows(cardWord));
                return;
            }

            var parts = Parts();

            // NO ZERO COUNTS HERE EITHER. (0.7.124.) The arrival line stopped
            // saying "0 options" at 0.7.122 and this one kept doing it — the
            // same sentence living in two places, which is the defect that hid
            // Sprinter's direction for eighty builds. A screen showing something
            // and reporting zero means the reader has not found it.
            string countWord = Vocabulary.NodeScreens.OptionCount(parts.Count);

            // HIS WORDS, VERBATIM, replacing the bare option count. A count
            // tells a player how many things there are; it does not tell them
            // what the screen is FOR, and this screen needs that more than most
            // — the two slots mean nothing without knowing what a host is.
            string blurb;
            if (!_screenBlurbs.TryGetValue(_screenName ?? "", out blurb))
                blurb = countWord == null ? "" : countWord + ".";

            string deckKey = DeckViewAvailable ? Vocabulary.NodeScreens.ShiftUpDisplaysYour + " " : "";

            // IKMA PROVISIONAL — the keys are real, the sentence is mine. A
            // key nobody is told about is a key nobody has.
            string packKey = BackpackAvailable
                ? Vocabulary.NodeScreens.OpensYourBackpackShift
                : "";

            // 0.7.330 — the Trapper's two keys. PROVISIONAL: the keys are his
            // (A reads the teeth, Backspace is the way out), the sentence is mine.
            string trapperKeys = TrapperSeq != null
                ? Vocabulary.NodeScreens.CountsYourTeethBackspace
                : "";

            // 0.7.337 — the item pickup's help, his sentence verbatim. The
            // count is the screen's own, said as a word the way he wrote it.
            if (ItemPickupActive)
            {
                string[] words = { Vocabulary.NodeScreens.Zero, Vocabulary.NodeScreens.One, Vocabulary.NodeScreens.Two, Vocabulary.NodeScreens.Three, Vocabulary.NodeScreens.Four, Vocabulary.NodeScreens.Five };
                int count = parts.Count;
                string howMany = count >= 0 && count < words.Length ? words[count] : count.ToString();
                // Session 37, note D3: a full pack is his "Item slots full.",
                // asked of the run the way the pickup itself asks it.
                bool full = false;
                try { full = RunState.Run.consumables.Count >= RunState.Run.MaxConsumables; } catch { }
                Speech.Browse(full
                    ? Vocabulary.NodeScreens.ItemSlotsFull(ScreenTitle())
                    : Vocabulary.NodeScreens.SelectOneOfItems(ScreenTitle(), howMany));
                return;
            }

            // 0.7.335 — the Trapper's key sentence, his order verbatim.
            if (TrapperSeq != null)
            {
                Speech.Browse(
                    Vocabulary.NodeScreens.ArrowsBrowseEnterChooses(ScreenTitle(), blurb.Length, blurb));
                return;
            }

            Speech.Browse(
                Vocabulary.NodeScreens.ArrowsBrowseEnterChoosesSpace(ScreenTitle(), blurb.Length, blurb, trapperKeys, packKey, deckKey));
        }
    }
}

// NodeScreenReader.cs
