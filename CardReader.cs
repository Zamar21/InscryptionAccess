// CardReader.cs
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// Formats and speaks card information via UniversalSpeech -> NVDA.
    ///
    /// Hover format:  "Black Goat. 0, 1. Ability: Worthy Sacrifice."
    /// Browse format: "Black Goat. Costs 1 blood. 0, 1. Ability: Worthy Sacrifice. Canine."
    ///                "Mantis God. Costs 1 blood. 1, 1. Abilities: Trifurcated Strike and Waterborne. Insect."
    ///
    /// Tribe is last in browse mode — lowest priority info.
    /// Squirrel tribe suppressed (no visual indicator in-game).
    /// Base sigils labelled "Ability:" / "Abilities:".
    /// Temp mod sigils prefixed "Sigil Ability:".
    /// Cache is lazy-filled on first use (loader not ready at plugin Awake).
    /// </summary>
    public static class CardReader
    {
        // The P/Invoke that used to live here moved to SpeechPump in Session 15.
        // Every call into UniversalSpeech now happens on one dedicated thread,
        // because the library keeps its selected engine in an unlocked
        // file-scope variable and two threads inside it is a data race. This
        // class still owns the DECISION to speak; the pump owns the call.
        private static ManualLogSource _log;
        private static bool _cacheFilled = false;

        private static readonly Dictionary<Ability, string> _abilityNameCache
            = new Dictionary<Ability, string>();

        public static void Init(ManualLogSource log)
        {
            _log = log;
        }

        private static void EnsureCacheReady()
        {
            if (_cacheFilled) return;
            var allData = ScriptableObjectLoader<AbilityInfo>.AllData;
            if (allData == null || allData.Count == 0) return;
            foreach (var info in allData)
            {
                if (info != null && !_abilityNameCache.ContainsKey(info.ability))
                    _abilityNameCache[info.ability] = info.rulebookName;
            }
            _cacheFilled = true;
            _log?.LogInfo($"IKMA CACHE: ability names filled, {_abilityNameCache.Count} entries.");
        }

        /// <summary>
        /// Read a card aloud.
        /// includeCost=false (hover): name, stats, abilities.
        /// includeCost=true (browse): name, cost, stats, abilities, tribe (tribe last/lowest priority).
        /// </summary>
        // ======================================================================
        // TWO CARDS, ONE NAME. (0.7.137.)
        //
        // Zamar's 0.7.136 log, two consecutive hand reads:
        //
        //   Wolf. Cost: 2 blood. 3, 2. Ability: Mighty Leap. Kin type: Canine.
        //   Wolf. Cost: 2 blood. 3, 2. Kin type: Canine.
        //
        // Two different cards, and nothing in the sentence says which one the
        // browse is on. A sighted player tells them apart by POSITION — they are
        // sitting in different places in the hand — and a blind player arrowing
        // across has no equivalent. That is a parity gap, and it gets worse the
        // moment a sigil is stitched onto a copy of something already held.
        //
        // HIS SCOPE, AND IT IS DELIBERATELY NARROW: "This should only occur when
        // two of them are in your hand at the same time, and only when arrow key
        // browsing your hand." So a lone Wolf is a Wolf, the board is untouched,
        // and the number appears only where the ambiguity actually exists.
        //
        // NUMBERED IN HAND ORDER, so "Wolf 1" is the one the left arrow reaches
        // first. Positional, like the thing it replaces — not a claim about
        // which is better or which arrived when.
        //
        // Asked of the hand every read, never cached: cards enter and leave it
        // constantly, and a remembered index would be wrong within a turn.
        // ======================================================================
        // INTERNAL, because the hand is read in TWO places. C lists the whole
        // hand from BoardReader; the arrows read one card from here. A number
        // that appears while browsing and vanishes in the list is worse than no
        // number at all — the player would hear "Wolf 2" and then a list with
        // two plain Wolves in it.
        //
        // This is the third time this session that one fact had two formatters,
        // after Sprinter's direction and the "0 options" line. Worth checking
        // for by reflex now: before calling a read feature done, grep for every
        // place that composes that sentence.
        // ==================================================================
        // EVERY CARD NAME THE MOD SPEAKS COMES THROUGH HERE. (0.7.319.)
        //
        // Zamar: "When a card has been fused with the Mycosints, and has the
        // rip down the middle, can we Alter its name. Have its name be
        // 'Fused [card name]' so, Fused Flying Ant."
        //
        // WHAT THE RIP ACTUALLY IS, from the game rather than from the look of
        // it: DuplicateMergeSequencer.GetDuplicateMod builds the fusion's
        // CardModificationInfo with three decals on it —
        // AlternatingBloodDecal's, "decal_fungus" and "decal_stitches" — and
        // sets fromDuplicateMerge. The stitches ARE the rip, and the flag rides
        // with them.
        //
        // fromDuplicateMerge, NOT fromCardMerge, and the distinction matters.
        // fromCardMerge is set by eight different things — the sacrifice stone,
        // a Beaver's Dam, a totem, the Prospector, a pelt trade — and would
        // have called half the deck fused. fromDuplicateMerge is set in exactly
        // one place in the whole assembly, and the game itself uses it to
        // refuse fusing an already-fused card (DuplicateMergeSequencer, and
        // TradePeltsSequencer asks the same question). It is the game's own
        // word for what he is describing.
        //
        // PARITY: the stitched card is visibly different on the table, so this
        // is describing what is there, not granting anything.
        //
        // ONE COMPOSER, and this time the whole tree was swept to it —
        // 108 reads of CardInfo.DisplayedNameLocalized across nineteen files
        // now come through this method. SPOKEN_STRINGS.md counts the
        // duplicate-sentence problem; this is the same defect one rung down,
        // and a name that is spoken from 108 places is a name that will
        // eventually disagree with itself. The log lines were swept too,
        // deliberately: a log that calls the card something other than what the
        // player heard is a log that cannot be read against a playtest.
        // ==================================================================
        /// <summary>
        /// A card's name as the mod speaks it. The ONLY place
        /// CardInfo.DisplayedNameLocalized is read.
        /// </summary>
        public static string CardName(CardInfo info)
        {
            if (info == null) return null;

            string name = null;
            try { name = info.DisplayedNameLocalized; } catch { }
            if (string.IsNullOrEmpty(name)) return name;

            // ==================================================================
            // SESSION 46 - THE IJIRAQ IN DISGUISE AT A CARD CHOICE. (0.7.453.)
            //
            // The game's Shapeshifter shows the Ijiraq at a card choice as
            // another rare card: red glowing eyes, and a name with a question
            // mark on the end ("Mole Man?") from a card mod whose singletonId
            // is Shapeshifter.MOD_ID (PUBLIC const). That mod is the game's own
            // mark, and a sighted player reads it off the card. Zamar's word
            // for it: "Strange Mole Man".
            // ==================================================================
            // Session 47 (0.7.455), Zamar: "Anywhere that ? is visible should
            // be called Unusual." The game prints the question mark in two
            // places: the card choice mark above, and the card the unlock
            // screens show for the Ijiraq (Ijiraq_UnlockScreen, displayedName
            // "Mole Man?").
            try
            {
                if (HasShapeshifterMark(info) || info.name == IJIRAQ_UNLOCK_CARD)
                {
                    string plain = name.EndsWith("?", System.StringComparison.Ordinal)
                        ? name.Substring(0, name.Length - 1).TrimEnd()
                        : name;
                    return Vocabulary.Cards.Unusual(plain);
                }
            }
            catch { }

            bool fused = false;
            try
            {
                var mods = info.Mods;
                if (mods != null)
                    for (int i = 0; i < mods.Count; i++)
                        if (mods[i] != null && mods[i].fromDuplicateMerge) { fused = true; break; }
            }
            catch { }

            if (!fused) return name;

            NoteFusedOnce(name);
            return Vocabulary.Cards.Fused(name);
        }

        // The coverage report makes itself: one line the first time each fused
        // card is named, so a playtest log lists what this touched instead of
        // the list being asserted here.
        /// <summary>
        /// The card's name WITHOUT the "Fused" decoration — for naming a KIND of
        /// card, as the Mycologists' "Pair of Flying Ants" does (0.7.329). Every
        /// other caller wants CardName.
        /// </summary>
        public static string BaseCardName(CardInfo info)
        {
            if (info == null) return null;
            try { return info.DisplayedNameLocalized; } catch { return null; }
        }

        // ======================================================================
        // A GLITCHED CARD HIDES ITS FACE. (0.7.329.)
        //
        // Zamar, hearing ". Glitched portrait. Cost: Free. 0, 1.": "This card
        // info should just read 'Glitched card. This card is glowing white and
        // glitching. Card stats are unknown until played.'" His words.
        // StaticGlitch paints animated static over the stats layer, so a sighted
        // player cannot read the name or numbers either — parity.
        // ======================================================================
        public static string GlitchedCardLine =>
            Vocabulary.Cards.GlitchedCardThisCard;

        public static bool IsGlitched(CardInfo info)
        {
            try
            {
                var b = info?.appearanceBehaviour;
                return b != null && b.Contains(CardAppearanceBehaviour.Appearance.StaticGlitch);
            }
            catch { return false; }
        }

        private static readonly System.Collections.Generic.HashSet<string> _fusedSeen
            = new System.Collections.Generic.HashSet<string>();

        private static void NoteFusedOnce(string bare)
        {
            try
            {
                if (_fusedSeen.Contains(bare)) return;
                _fusedSeen.Add(bare);
                Plugin.Log?.LogInfo($"IKMA CARD: '{bare}' is stitched — spoken as 'Fused {bare}'.");
            }
            catch { }
        }

        // ==================================================================
        // A BOON'S NAME AND EFFECT, FROM THE GAME. (0.7.325.)
        //
        // Beside CardName for the same reason: the mod had three names for the
        // Bone Lord's two boons and none of them was the game's. The bone
        // lines in Plugin.cs said "Minor Bone Lord Boon" and "Major Bone Lord
        // Boon"; the asset says "Minor Boon of the Bone Lord" and "Boon of the
        // Bone Lord", and "Major" is not the game's word for anything.
        //
        // BoonData.displayedName and .description are PUBLIC and already
        // confirmed in dump_rulebook_content.txt. The description carries
        // colour tags, so it comes back through RulebookReader.Clean — the one
        // stripper.
        // ==================================================================
        internal static BoonData BoonInfo(BoonData.Type type)
        {
            if (type == BoonData.Type.None) return null;
            try { return BoonsUtil.GetData(type); } catch { return null; }
        }

        /// <summary>The boon's name as the game prints it, or null.</summary>
        internal static string BoonName(BoonData.Type type)
        {
            var data = BoonInfo(type);
            string name = null;
            try { name = data?.displayedName; } catch { }
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>What the boon does, in the game's own words, or null.</summary>
        internal static string BoonEffect(BoonData.Type type)
        {
            var data = BoonInfo(type);
            string desc = null;
            try { desc = RulebookReader.Clean(data?.description); } catch { }
            return string.IsNullOrEmpty(desc) ? null : desc;
        }

        // ======================================================================
        // THE SAME NUMBERS IN A DECK VIEW. (0.7.433.)
        //
        // Zamar, Session 42: "I have two Mantis gods and two pack rats. Can we
        // also add the numbering system here for some extra clarity? Pack Rat
        // 1, Pack Rat 2, Pack Rat 3, etc. Only when there's more than one card
        // name in your deck views."
        //
        // Same rule as the hand: a lone card keeps its plain name, and the
        // number is the card's place among its namesakes in the order the
        // arrows reach them. Counted from the list being browsed, every read.
        // The number goes straight after the name at the front of the one
        // composer's sentence, so "Pack Rat. Cost..." becomes "Pack Rat 2.
        // Cost..." and nothing else about the read changes.
        // ======================================================================
        // ======================================================================
        // SESSION 46 - THE IJIRAQ IN DISGUISE IN THE DECK VIEW. (0.7.453.)
        //
        // In the deck view the Ijiraq is drawn as one of the OTHER cards in the
        // deck, with red glowing eyes (Shapeshifter.OnShownInDeckReview). The
        // card object then carries that other card's own CardInfo, so nothing
        // in the info says which of two "Moles" is the impostor. What does is
        // the Shapeshifter component the game leaves on the card object while
        // its info is no longer the Ijiraq's. GetComponent on a card already in
        // hand, never a scene search.
        //
        // ONLY WHERE A SIGHTED PLAYER CAN SEE IT. The red eyes are shown in the
        // deck view and at a card choice. A card list that picks a card for a
        // node (campfire, altar, the stones) disguises it WITHOUT the eyes, and
        // in the hand during a battle there is no mark at all - there the
        // disguise is the point of the card, and IKMA reads what is shown.
        // ======================================================================
        internal static bool IsDisguisedIjiraq(Card card)
        {
            try
            {
                if (card == null || card.Info == null) return false;
                if (card.Info.name == "Ijiraq") return false;
                return card.GetComponent<Shapeshifter>() != null;
            }
            catch { return false; }
        }

        private const string IJIRAQ_UNLOCK_CARD = "Ijiraq_UnlockScreen";

        internal static bool HasShapeshifterMark(CardInfo info)
        {
            try
            {
                var marks = info?.Mods;
                if (marks == null) return false;
                for (int i = 0; i < marks.Count; i++)
                    if (marks[i] != null && marks[i].singletonId == Shapeshifter.MOD_ID) return true;
            }
            catch { }
            return false;
        }

        // ======================================================================
        // SESSION 47 - A CARD'S NAME, ASKED OF THE CARD. (0.7.455, 0.7.456.)
        //
        // 0.7.455 made a disguised Ijiraq "Unusual [name]" wherever a card
        // object was named - the hand, the draw and play lines, the node pick
        // lists. Zamar, the same day: "Incorrect. Only call it unusual if the
        // sighted indication is also there. No unfair advantages with our
        // mod." So this overload says exactly what CardName(CardInfo) says.
        // It stays because 88 call sites now hand it the card, and the card is
        // the only thing that can tell a disguise from the real one.
        //
        // WHERE THE MARK IS SHOWN, and "Unusual" is said:
        //   the deck view (red eyes)       DeckDisambiguated(redEyesShown: true)
        //   a card choice (red eyes, "?")  CardName(CardInfo), the card mod
        //   the unlock screens ("?")       CardName(CardInfo), Ijiraq_UnlockScreen
        //   the moment it is played        RevealedDisguiseName, below
        //
        // ReferenceEquals, not ==: a card Unity has destroyed still has its
        // Info, and death lines name cards after they are gone.
        // ======================================================================
        public static string CardName(Card card)
        {
            if (ReferenceEquals(card, null)) return null;

            CardInfo info = null;
            try { info = card.Info; } catch { }

            return CardName(info);
        }

        /// <summary>
        /// "Unusual [name]" for a disguised Ijiraq, the plain name for any
        /// other card. ONLY for the two lines spoken as the disguise comes
        /// off on the board: the transform line (his Session 46 sentence) and
        /// a sigil of the disguise firing in that same moment (his Session 47
        /// answer, "Strange pack rat").
        /// </summary>
        internal static string RevealedDisguiseName(Card card)
        {
            string name = CardName(card);
            if (string.IsNullOrEmpty(name)) return name;

            try
            {
                if (IsDisguisedIjiraq(card) && !HasShapeshifterMark(card.Info))
                    return Vocabulary.Cards.Unusual(name);
            }
            catch { }
            return name;
        }

        internal static string DeckDisambiguated(System.Collections.Generic.List<SelectableCard> cards, int index, string described,
                                                 bool redEyesShown = false)
        {
            if (cards == null || index < 0 || index >= cards.Count || string.IsNullOrEmpty(described))
                return described;

            try
            {
                CardInfo own = null;
                try { own = cards[index].Info; } catch { }

                string name = CardName(own);
                if (string.IsNullOrEmpty(name) || !described.StartsWith(name, System.StringComparison.Ordinal))
                    return described;

                // Session 46: in the deck view the impostor is "Strange Mole",
                // and it is not one of the Moles being numbered.
                if (redEyesShown && IsDisguisedIjiraq(cards[index]))
                    return Vocabulary.Cards.Unusual(name) + described.Substring(name.Length);

                int total = 0;
                int mine  = 0;

                for (int i = 0; i < cards.Count; i++)
                {
                    CardInfo info = null;
                    try { info = cards[i].Info; } catch { }
                    if (info == null || CardName(info) != name) continue;
                    if (redEyesShown && IsDisguisedIjiraq(cards[i])) continue;

                    total++;
                    if (i == index) mine = total;
                }

                if (total < 2 || mine == 0) return described;
                return name + " " + mine + described.Substring(name.Length);
            }
            catch { return described; }
        }

        internal static string HandDisambiguated(PlayableCard card, string name)
        {
            if (card == null || string.IsNullOrEmpty(name)) return name;

            try
            {
                if (!card.InHand) return name;

                var hand = Singleton<PlayerHand>.Instance;
                if (hand?.CardsInHand == null) return name;

                int total = 0;
                int mine  = 0;

                foreach (var c in hand.CardsInHand)
                {
                    if (c?.Info == null) continue;
                    if (CardReader.CardName(c) != name) continue;

                    total++;
                    if (ReferenceEquals(c, card)) mine = total;
                }

                if (total < 2 || mine == 0) return name;
                return $"{name} {mine}";
            }
            catch { return name; }
        }

        // ======================================================================
        // ONE SENTENCE, ONE HOME. (0.7.144.)
        //
        // The Shift+R line existed verbatim in three help strings across two
        // files. Three copies of one sentence get corrected once — which is the
        // shape that hid Sprinter's direction for eighty builds and kept the
        // "0 options" line alive after it was fixed.
        //
        // It lives on CardReader because CardReader owns the ability memory that
        // Shift+R actually reads back, so the sentence sits next to the thing it
        // describes.
        // ======================================================================
        internal static string ShiftRHelp => Vocabulary.Cards.ShiftPlusRLooks;

        public static void ReadCard(PlayableCard card, bool includeCost = false)
        {
            // Session 11: a Bat carrying a stitched-on sigil crashed the game
            // when browsed. Cause unknown — the exception was not captured — so
            // this is containment and instrumentation, not a fix.
            //
            // A card read is pure narration. Whatever is wrong with one card's
            // data, it must not be able to take the run down: a blind player
            // loses a Kaycee's Mod run they cannot get back, over a line of
            // speech. The catch logs the card and the full exception so the next
            // occurrence names its own cause instead of being reported as "it
            // crashed".
            try
            {
                // Session 13: the browse path is timed because Zamar can feel a
                // hitch on it. Compose and speak were reported separately —
                // string building and the TTS call are different problems with
                // different fixes, and only the numbers say which one this is.
                //
                // WHAT THIS NUMBER MEANS CHANGED AT 0.7.41. The speak half is
                // now an enqueue onto the speech thread, so this figure is the
                // main-thread cost of a card read and nothing else — which is
                // exactly the thing that was making arrow presses hitch. If it
                // stays under 8ms it will simply stop appearing in the log, and
                // that silence is the result rather than an instrument that
                // broke. The native call is still timed, on the pump, and still
                // logged as "IKMA PERF: speechSay took Xms".
                long t = Perf.Now();
                ReadCardInner(card, includeCost);
                Perf.Report("card read (main thread)", t);
            }
            catch (System.Exception e)
            {
                string what = "unknown card";
                try { what = card?.Info?.name ?? CardReader.CardName(card?.Info) ?? "unknown card"; }
                catch { }

                _log?.LogError($"IKMA READ FAILED on '{what}': {e}");
                Speak(Vocabulary.CardCouldNotBeRead, interrupt: true);
            }
        }

        private static void ReadCardInner(PlayableCard card, bool includeCost)
        {
            if (card?.Info == null) return;

            // 0.7.329 — glitched, and not yet played. "Unknown until played"
            // is his rule, so a card on the board reads normally.
            bool onBoard = false;
            try { onBoard = card.OnBoard; } catch { }
            if (!onBoard && IsGlitched(card.Info))
            {
                Speak(GlitchedCardLine, interrupt: true);
                return;
            }

            EnsureCacheReady();

            var info = card.Info;
            string name = HandDisambiguated(card, CardReader.CardName(card));

            // Session 9 (ability matrix): read stats off the LIVE PlayableCard,
            // not off CardInfo. CardInfo.Attack is the printed base value; it
            // ignores buffs, damage taken, totem/sigil mods, and — the reason
            // this matters most — the seven variable-stat behaviours (Ant,
            // BellProximity, CardsInHand, Mirror, SacrificesThisTurn,
            // Lammergeier, GreenMage). An Ant read from CardInfo announces its
            // printed 0 no matter how many ants are on the board.
            // BoardReader already read the live values; the hand and hover reads
            // did not, so a card said one thing in your hand and another on the
            // board.
            // Prime suspect for the stitched-sigil crash, and stated as a
            // suspicion because that is all it is. The crash was on a Bat
            // carrying a stitched-on sigil, and the two reads below are the
            // only thing in this method that asks the game a question rather
            // than formatting an answer — everything after this line is string
            // building on values already in hand. A variable stat is not a
            // stored number; asking for one makes the game work it out, and a
            // card whose sigils are freshly stitched is the case least likely
            // to have been exercised. So if a read of this card can hang or
            // throw, it happens here.
            //
            // What this comment does NOT claim is HOW the game works it out.
            // Nobody has confirmed that, and the containment below does not
            // depend on it: the try/catch logs the card and the exception so
            // the next occurrence names its own cause.
            int atk, hp;
            try
            {
                atk = card.Attack;
                hp  = card.Health;
            }
            catch (System.Exception e)
            {
                _log?.LogError($"IKMA READ: live stat read threw, falling back to printed values: {e.Message}");
                atk = info.Attack;
                hp  = info.Health;
            }

            // FormatCost returns null for free cards — omit cost entirely in that case.
            string costRaw = includeCost ? FormatCost(info) : null;
            string costPart = costRaw != null ? " " + costRaw : "";

            // Base sigils.
            var seenAbilities = new HashSet<Ability>();
            var baseSigils    = new List<string>();
            var abilities     = info.Abilities;   // stitched sigils land here
            if (abilities != null)
            {
                foreach (var ability in abilities)
                {
                    if (!NegatedOnCard(card, ability) && seenAbilities.Add(ability))
                    {
                        string n = AbilityNameWithDirection(ability, card);
                        n = WithHatchProgress(ability, n);   // 0.7.443
                        if (n != null) baseSigils.Add(n);
                    }
                }
            }

            // Temp mod sigils (woodcarving, totem, items).
            var tempSigils = new List<string>();
            if (card.TemporaryMods != null)
            {
                foreach (var mod in card.TemporaryMods)
                {
                    if (mod?.abilities == null) continue;
                    foreach (var ability in mod.abilities)
                    {
                        if (!NegatedOnCard(card, ability) && seenAbilities.Add(ability))
                        {
                            string n = GetAbilityName(ability);
                            if (n != null) tempSigils.Add(n);
                        }
                    }
                }
            }

            // Build ability part:
            // Base sigils: "Ability: X." or "Abilities: X and Y."
            // Temp sigils: appended as separate "Sigil Ability: X." sentences after.
            // Keeping them separate so the player knows which are native vs granted.
            string abilityPart = "";
            if (baseSigils.Count > 0)
            {
                string label = Vocabulary.Cards.AbilityCount(baseSigils.Count);
                abilityPart = Vocabulary.Cards.AbilitiesLine(label, baseSigils);
            }
            if (tempSigils.Count > 0)
            {
                foreach (var t in tempSigils)
                    abilityPart += Vocabulary.Cards.SigilAbility(t);
            }

            // Variable-stat disclosure (Session 9). A sighted player sees an
            // icon where the attack number should be and knows the value moves.
            // Without this the number sounds fixed, and a blind player plans
            // around a figure that changes the moment the board does.
            string statNote = DescribeSpecialStat(info.SpecialStatIcon);

            // Tribe — browse mode only, lowest priority (after abilities).
            // Squirrel tribe suppressed (no visual indicator in-game).
            string tribePart = "";
            if (includeCost && info.tribes != null)
            {
                var tribeNames = new List<string>();
                foreach (var tribe in info.tribes)
                {
                    if (tribe == Tribe.None) continue;
                    // Squirrel is suppressed (no visual indicator in-game).
                    // Compared as an enum: one fewer string per tribe per read.
                    if (tribe == Tribe.Squirrel) continue;
                    tribeNames.Add(tribe.ToString());
                }
                // Session 10 note 7: a bare "Hooved." at the end of a card read
                // gave no clue what the word referred to. Label it. Sighted
                // players see a tribe icon and know what kind of information it
                // is; the spoken version has to carry that framing itself.
                if (tribeNames.Count == 1)
                    tribePart = Vocabulary.Cards.KinType(tribeNames[0]);
                else if (tribeNames.Count > 1)
                    tribePart = Vocabulary.Cards.KinTypes(tribeNames);
            }

            // Browse: "Black Goat. Costs 1 blood. 0, 1. Ability: Worthy Sacrifice. Canine."
            // Hover:  "Black Goat. 0, 1. Ability: Worthy Sacrifice."
            // Session 14. Treatment goes straight after the name, which is
            // where Zamar asked for it and where a sighted player takes it in —
            // the border is the first thing about the card, not a footnote.
            // Direction goes after the abilities, because the arrow it names is
            // drawn inside the sigil.
            string lookPart = DescribeAppearance(info, card);

            // The direction is no longer a trailing clause — it rides the
            // mover's own sigil name, built above. (0.7.171.) Zamar: "The
            // moving right thing should be directly after Sprinter, before
            // other sigils."

            // 0.4.8.004 - MIRROR OFF THE BOARD READS "Star". Zamar, Session 56,
            // on Mirror Tentacle reading "Star, 3" at the card choice and "0, 3"
            // in hand: "Should also be Star in hand for that one specifically
            // since it doesnt get a value until on the board." Mirror only; the
            // other variable stats keep their live value off the board.
            string atkText = atk.ToString();
            try
            {
                if (!onBoard && info.SpecialStatIcon == SpecialStatIcon.Mirror)
                    atkText = Vocabulary.Cards.StarAttack;
            }
            catch { }

            string text = includeCost
                ? Vocabulary.Cards.CardWithCost(name, lookPart, costPart, atkText, hp, statNote, abilityPart, tribePart)
                : Vocabulary.Cards.CardWithoutCost(name, lookPart, atkText, hp, statNote, abilityPart);

            // Session 11: this used to log the composed line here and then hand
            // the identical string to Speak, which logged it again. Every
            // utterance appeared twice in the log, each line a near-repeat of
            // the one above it — a scrollback roughly double the length it
            // needed to be, which is a real cost to anyone reading the log with
            // a screen reader. One log line per utterance, at the point of
            // speech, in Speak.
            Speak(text, interrupt: true);
        }

        /// <summary>
        /// Describe a card from its CardInfo alone. (Session 13.)
        ///
        /// ReadCard above needs a live PlayableCard, because a card ON THE BOARD
        /// has stats that move and CardInfo would report the printed base — the
        /// Ant that always says zero. A card the player is being OFFERED is not
        /// on the board, has no live instance to read, and the printed values
        /// are exactly what a sighted player sees on its face. So this one reads
        /// CardInfo, and that is correct here rather than a shortcut.
        ///
        /// Used by CardChoiceReader for the post-battle reward.
        /// </summary>
        /// <param name="afterName">
        /// A note that belongs immediately after the card's name rather than at
        /// the end of the read — currently the card choice screen's duplicate
        /// mushroom. (0.7.152, his call: "Change Duplicate callout to be right
        /// after card name.") A fact about WHICH CARD THIS IS outranks its cost
        /// and stats, and at the end it arrived after he had stopped listening.
        /// Pass it with no leading space; the sentence break is added here.
        /// </param>
        public static string DescribeCardInfo(CardInfo info, bool includeCost = true,
                                              string afterName = null)
        {
            if (info == null) return null;
            if (IsGlitched(info)) return GlitchedCardLine;
            EnsureCacheReady();

            string name = CardReader.CardName(info);

            string costRaw  = includeCost ? FormatCost(info) : null;
            string costPart = costRaw != null ? " " + costRaw : "";

            var seen   = new HashSet<Ability>();
            var sigils = new List<string>();
            if (info.Abilities != null)
            {
                foreach (var ability in info.Abilities)
                {
                    if (!seen.Add(ability)) continue;

                    // NO DIRECTION HERE, AND THAT IS CORRECT. (0.7.171.) This
                    // overload describes a CardInfo with no live PlayableCard
                    // behind it — the queue, the choice screen, a deck list. No
                    // card means no Strafe component, so there is no direction
                    // to read and any word about one would be invented.
                    //
                    // The three reads that DO have a live card go through
                    // AbilityNameWithDirection.
                    string n = GetAbilityName(ability);
                    n = WithHatchProgress(ability, n);   // 0.7.443
                    if (n != null) sigils.Add(n);
                }
            }

            string abilityPart = "";
            if (sigils.Count > 0)
            {
                string label = Vocabulary.Cards.AbilityCount(sigils.Count);
                abilityPart = Vocabulary.Cards.AbilitiesLine(label, sigils);
            }

            string statNote = DescribeSpecialStat(info.SpecialStatIcon);

            // Zamar, Session 13: a variable-stat card does not PRINT a number
            // where its attack goes — it prints a symbol, and the printed value
            // behind it is 0. Reading "Flying Ant. 0, 1." announced a figure
            // that is on no card anywhere and is never that card's attack. The
            // statNote already explains what the number tracks; this stops the
            // lie that precedes it.
            //
            // "Star" is his word for the symbol, chosen so the player hears
            // "there is a symbol here" rather than a wrong number.
            //
            // Only for CardInfo reads — menus, starter decks, rewards, unlocks.
            // A card ON THE BOARD has a real, computed attack that the player
            // needs, and the live reads keep reporting it.
            bool variableAttack = info.SpecialStatIcon != SpecialStatIcon.None;
            string attackText = variableAttack ? Vocabulary.Cards.StarAttack : info.Attack.ToString();

            string tribePart = "";
            if (info.tribes != null)
            {
                var tribeNames = new List<string>();
                foreach (var tribe in info.tribes)
                {
                    if (tribe == Tribe.None) continue;
                    // Squirrel is suppressed (no visual indicator in-game).
                    // Compared as an enum: one fewer string per tribe per read.
                    if (tribe == Tribe.Squirrel) continue;
                    tribeNames.Add(tribe.ToString());
                }
                if (tribeNames.Count == 1)
                    tribePart = Vocabulary.Cards.KinType(tribeNames[0]);
                else if (tribeNames.Count > 1)
                    tribePart = Vocabulary.Cards.KinTypes(tribeNames);
            }

            // Treatment reads the same here as on the board — it comes off
            // CardInfo either way. Direction does NOT: there is no instance to
            // ask, which is exactly the split Zamar predicted when he raised
            // this. A deck-list entry has no direction to report.
            string lookPart = DescribeAppearance(info);

            string namePart = string.IsNullOrEmpty(afterName) ? "" : " " + afterName.Trim();

            return Vocabulary.Cards.CardInfoLine(name, namePart, lookPart, costPart, attackText, info.Health, statNote, abilityPart, tribePart);
        }

        // ----------------------------------------------------------------------
        // Which way a Sprinter or Rampager is moving. (Session 14.)
        //
        // Master Handoff B8's top item, and a straight parity gap rather than a
        // nicety: the direction is drawn as an ARROW inside the sigil, so a
        // sighted player reads it off the card and a blind player has never had
        // it at all.
        //
        // Confirmed in dump_movement_appearance.txt before a line of this was
        // written. `Strafe.movingLeft` is a NONPUBLIC instance bool declared on
        // `Strafe` and inherited by every mover that has a fixed direction:
        // StrafePush (Rampager), StrafeSwap, SkeletonStrafe, SquirrelStrafe.
        // Reflection because it is non-public; resolved once.
        //
        // GetComponent on the card itself, which is not the banned lookup — the
        // hard rule is against FindObjectOfType and GameObject.Find, searches
        // that scan the scene. This asks one card about itself. Asking for the
        // BASE type is deliberate: it returns whichever subclass is attached,
        // so one call covers all five sigils.
        //
        // THE NEGATIVE RESULT MATTERS TOO, and it is why this is not applied
        // more widely. WhackAMole, MoveBeside and GuardDog declare no state at
        // all. They move to a slot worked out at the moment they trigger, so
        // there is no direction to announce and no arrow on their sigils for a
        // sighted player either. Those three are covered by the board diff
        // watcher reporting where they ended up, which is the only thing anyone
        // gets to know in advance.
        //
        // ON THE BOARD ONLY. A card in hand carries the component with its
        // field at whatever the default is, and announcing that would be
        // reporting a value rather than an observation. Zamar's own framing
        // when he raised this: the direction may only exist once the card is on
        // the board. Returns empty for a card in hand.
        // ----------------------------------------------------------------------
        private static FieldInfo _movingLeftField;
        private static bool _movingLeftResolved;
        private static bool _strafeRouteWarned;

        // INTERNAL AS OF 0.7.115, AND THE REASON IS THE BUG IT FIXES.
        //
        // This was private, so only CardReader's own formatter could call it —
        // and that formatter is one of THREE that read a card's abilities aloud.
        // BoardReader.FormatSlots composes its own list for G, Shift+G and B;
        // HotkeyManager composes another for the slot read. Neither could reach
        // this, so the direction has never once been spoken since it was built
        // at 0.7.34, and the handoff carried it as "built, unverified in play"
        // for eighty builds.
        //
        // Zamar's 0.7.113 log is what settled it: four separate reads of a
        // Sprinter Cuckoo, not one of them carrying a direction, and no warning
        // either — because the code that would have warned was never reached.
        //
        // THE GENERAL LESSON: when a project has more than one formatter for the
        // same fact, a feature added to one of them is not shipped. Before
        // calling a read feature done, grep for every place that composes that
        // sentence.
        // ----------------------------------------------------------------------
        // THE DIRECTION BELONGS TO THE SIGIL, NOT TO THE END OF THE LINE.
        // (0.7.171.)
        //
        // Zamar: "The moving right thing should be directly after Sprinter,
        // before other sigils."
        //
        // 0.7.170 fixed the punctuation so the clause stopped running into the
        // last sigil name, and that was the wrong fix for the real complaint.
        // A trailing "Moving right." after a list of four sigils makes the
        // listener hold all four and then work out which one it belongs to. The
        // arrow is drawn INSIDE the Sprinter sigil, so the words go inside the
        // Sprinter sigil too:
        //
        //   "Abilities: Airborne and Brood Parasite and Sprinter moving right
        //    and Bifurcated Strike."
        //
        // GENERAL: a modifier goes next to the thing it modifies. Appending it
        // to the sentence is easy for the code and expensive for the listener,
        // who has to re-scan a list to find its owner.
        //
        // WHICH SIGIL GETS IT IS ASKED OF THE GAME. The Strafe component on the
        // card carries its own Ability property, so the direction attaches to
        // whichever of the five Strafe-family sigils is actually present —
        // Sprinter, Rampager, and the rest — with no table of ability ids here
        // to fall out of date.
        // ----------------------------------------------------------------------
        internal static string AbilityNameWithDirection(Ability ability, PlayableCard card)
        {
            string name = GetAbilityName(ability);
            if (card == null || string.IsNullOrEmpty(name)) return name;

            // Remember the pairing for Shift+R, for EVERY ability on this card
            // and not only the mover — the lookup wants to know the card behind
            // each sigil it explains, and a future direction-like fact on some
            // other sigil then needs no second mechanism.
            NoteAbilityCard(ability, card);

            try
            {
                var strafe = card.GetComponent<Strafe>();
                if (strafe == null || strafe.Ability != ability) return name;
            }
            catch { return name; }

            string dir = DescribeMoveDirection(card);
            if (string.IsNullOrEmpty(dir)) return name;

            // DescribeMoveDirection returns " Moving right." for the standalone
            // clause it was built for. Inline it wants neither the capital nor
            // the stop.
            dir = dir.Trim().TrimEnd('.');
            if (dir.Length == 0) return name;
            dir = char.ToLowerInvariant(dir[0]) + dir.Substring(1);

            return $"{name} {dir}";
        }

        /// <summary>
        /// Names for a list of abilities, with the mover's direction attached to
        /// the mover. Every ability list a player hears should come through
        /// here so the placement is the same everywhere.
        /// </summary>
        internal static List<string> AbilityNamesWithDirection(
            IList<Ability> abilities, PlayableCard card)
        {
            var names = new List<string>();
            if (abilities == null) return names;

            foreach (var a in abilities)
            {
                string n = AbilityNameWithDirection(a, card);
                if (!string.IsNullOrEmpty(n)) names.Add(n);
            }
            return names;
        }

        // ----------------------------------------------------------------------
        // THE CURIOUS EGG'S LIGHTS. (0.7.443.)
        //
        // Zamar, Session 44, on "Curious Egg. Rare. Cost: 1 bone. 2, 1.
        // Ability: Finical Hatchling.": "Update it to '... Ability: Finical
        // Hatchling, Powers met: [x], [x], and [x], Health met: [x] and [x],
        // Kins met: [x]'. I want the call out to read the deck's current
        // progress towards these milestones."
        //
        // A SIGHTED PLAYER SEES THIS ON THE CARD. The egg's portrait is a
        // HydraEggPortrait (HydraEggPortrait.cs:17): five lights for power,
        // five for health, and a row for kin, each switched on by the same
        // three PUBLIC static questions asked here:
        //   HydraEgg.PowerStatInDeck(n)   n = 1..5   HydraEgg.cs:11
        //   HydraEgg.HealthStatInDeck(n)  n = 1..5   HydraEgg.cs:16
        //   HydraEgg.GetNumTribesInDeck()            HydraEgg.cs:21
        // and they are the same three the sigil itself asks before it
        // hatches (RespondsToDrawn, HydraEgg.cs:39). So nothing is worked out
        // here: which powers and healths are lit, and HOW MANY kin lights.
        // The game does not say which kins, only the count, so the count is
        // what is spoken.
        //
        // IN THE TWO FULL CARD READS ONLY (a live card, and a CardInfo: the
        // hand, the deck view, the choice screens). The short board and
        // queue reads keep the bare sigil name. Outside a run the questions
        // throw (there is no deck) and the name is returned as it was.
        // ----------------------------------------------------------------------
        // How many kins the egg needs. A literal in the game, not a reading:
        // HydraEgg.RespondsToDrawn ends "return GetNumTribesInDeck() >= 5;"
        // (HydraEgg.cs:49), and the portrait has five kin lights. If that
        // ever becomes a variable, read it instead.
        private const int HatchKinsNeeded = 5;

        internal static string WithHatchProgress(Ability ability, string name)
        {
            if (ability != Ability.HydraEgg || string.IsNullOrEmpty(name)) return name;

            try
            {
                var powers = new List<string>();
                var health = new List<string>();
                for (int i = 1; i <= 5; i++)
                {
                    if (HydraEgg.PowerStatInDeck(i))  powers.Add(i.ToString());
                    if (HydraEgg.HealthStatInDeck(i)) health.Add(i.ToString());
                }
                int kins = HydraEgg.GetNumTribesInDeck();

                return name + Vocabulary.Sigils.HatchProgress(
                    MetList(powers), MetList(health), kins, HatchKinsNeeded);
            }
            catch { return name; }
        }

        // "1", "1 and 2", "1, 2, and 3" - the one list joiner - or the
        // provisional "none".
        private static string MetList(List<string> items)
        {
            if (items.Count == 0) return Vocabulary.Sigils.NoneMet;
            string last = items[items.Count - 1];
            items.RemoveAt(items.Count - 1);
            return Vocabulary.AndList(items, last);
        }

        /// <summary>
        /// The Sprinter family's arrow as the game holds it: true = left,
        /// false = right, null = no such sigil on this card or the field did
        /// not resolve. (0.7.443.) Same field DescribeMoveDirection reads;
        /// that one returns words for a card read, this one the bare answer.
        /// </summary>
        internal static bool? StrafeMovingLeft(PlayableCard card)
        {
            if (card == null) return null;
            try
            {
                var strafe = card.GetComponent<Strafe>();
                if (strafe == null) return null;

                if (!_movingLeftResolved)
                {
                    _movingLeftResolved = true;
                    _movingLeftField = typeof(Strafe).GetField(
                        "movingLeft", BindingFlags.Instance | BindingFlags.NonPublic);

                    if (_movingLeftField == null)
                        _log?.LogInfo("IKMA MOVE: Strafe.movingLeft did not resolve — direction stays silent.");
                }
                if (_movingLeftField == null) return null;

                return (bool)_movingLeftField.GetValue(strafe);
            }
            catch { return null; }
        }

        internal static string DescribeMoveDirection(PlayableCard card)
        {
            if (card == null) return "";

            try
            {
                // In hand, or nowhere. Nothing true to say yet.
                if (card.Slot == null) return "";

                var strafe = card.GetComponent<Strafe>();
                if (strafe == null)
                {
                    WarnIfStrafeRouteMissed(card);
                    return "";
                }

                if (!_movingLeftResolved)
                {
                    _movingLeftResolved = true;
                    _movingLeftField = typeof(Strafe).GetField(
                        "movingLeft", BindingFlags.Instance | BindingFlags.NonPublic);

                    if (_movingLeftField == null)
                        _log?.LogInfo("IKMA MOVE: Strafe.movingLeft did not resolve — direction stays silent.");
                }

                if (_movingLeftField == null) return "";

                bool movingLeft = (bool)_movingLeftField.GetValue(strafe);

                // Session 48 (0.7.458). The move line said "It will move left
                // next." and this read said "moving right" for the same card
                // at the end of the row, because the game only turns its
                // arrow when the next move starts. Zamar: "Make it whichever
                // is actually accurate." With no slot beyond it the card can
                // only go the other way (Strafe.DoStrafe), so that is what is
                // said - the same test SigilNarrator.NextStrafeDirection makes.
                try
                {
                    var bm = Singleton<BoardManager>.Instance;
                    if (bm != null && bm.GetAdjacent(card.Slot, movingLeft) == null)
                        movingLeft = !movingLeft;
                }
                catch { }

                return Vocabulary.Cards.MovingLeftOrMovingRight(movingLeft);
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA MOVE: direction read failed: {e.Message}");
                return "";
            }
        }

        // Logged at most once. If a card prints a strafe sigil but the component
        // is not on it, GetComponent is the wrong route and the next log says
        // so rather than the feature just being quiet forever.
        private static void WarnIfStrafeRouteMissed(PlayableCard card)
        {
            if (_strafeRouteWarned) return;

            var abilities = card.Info?.Abilities;
            if (abilities == null) return;

            for (int i = 0; i < abilities.Count; i++)
            {
                var a = abilities[i];
                if (a == Ability.Strafe || a == Ability.StrafePush || a == Ability.StrafeSwap
                 || a == Ability.SkeletonStrafe || a == Ability.SquirrelStrafe)
                {
                    _strafeRouteWarned = true;
                    _log?.LogInfo(
                        $"IKMA MOVE: {CardReader.CardName(card)} prints a strafe sigil but no Strafe " +
                        "component was found on it — GetComponent is the wrong route.");
                    return;
                }
            }
        }

        // ----------------------------------------------------------------------
        // Special art treatment. (Session 14, Zamar's ask from a Mantis God:
        // "a card with special art treatment should say so after its name".)
        //
        // `CardInfo.appearanceBehaviour` is PUBLIC and holds the list the card
        // renders itself from. Confirmed as List<CardAppearanceBehaviour.Appearance>
        // by the compiler resolving against Assembly-CSharp.dll — the dump
        // printed the list without its generic argument.
        //
        // WIDENED, and this is Zamar overruling the narrower first pass:
        // "If there's 14 visible treatments those should all be described."
        // All seventeen are announced now, not the three the first version
        // spoke. His standing principle decides it — more information for blind
        // players is always preferable — and every one of these is something a
        // sighted player can see on the card's face, so withholding fourteen of
        // them was the mod choosing what he was allowed to know.
        //
        // Reads from CardInfo, so it works identically on the board, in hand,
        // in a menu, in a starter deck and on a reward. Nothing to split here.
        //
        // WORDING IS PROVISIONAL ON SOME OF THESE, and the code says which.
        // Internal ids are never display names — that is the oldest rule on the
        // project and it cost a session on the rulebook when GooBottle turned
        // out to be titled "Failure". There is no display name for an
        // appearance anywhere in the assembly, so unlike an ability there is no
        // data object to go and find: every word below is a description of what
        // the treatment LOOKS like, written by someone who cannot see it.
        //
        // So each distinct treatment is logged the first time it is spoken,
        // paired with the word chosen for it. The log then says exactly which
        // internal member produced which spoken word, and Zamar can correct any
        // of them against what is actually on screen. That is the same method
        // that fixed the menu labels: do not guess twice, make the log answer.
        // ----------------------------------------------------------------------
        private static readonly HashSet<CardAppearanceBehaviour.Appearance> _loggedAppearances
            = new HashSet<CardAppearanceBehaviour.Appearance>();

        private static string DescribeAppearance(CardInfo info, PlayableCard card = null)
        {
            var behaviours = info?.appearanceBehaviour;
            if (behaviours == null || behaviours.Count == 0) return "";

            // Small lists, and duplicates are real — Rare arrives as both
            // RareCardBackground and RareCardColors on the same card, and both
            // map to one word. A List with a Contains check beats a HashSet at
            // this size and keeps the spoken order stable.
            var labels = new List<string>();

            for (int i = 0; i < behaviours.Count; i++)
            {
                var appearance = behaviours[i];
                string label = AppearanceLabel(appearance);

                if (appearance == CardAppearanceBehaviour.Appearance.DynamicPortrait)
                    ProbeDynamicPortrait(info, card);

                if (_loggedAppearances.Add(appearance))
                {
                    _log?.LogInfo(label == null
                        ? $"IKMA LOOK: appearance {appearance} has no wording — nothing spoken for it."
                        : $"IKMA LOOK: appearance {appearance} spoken as \"{label}\".");
                }

                if (label == null) continue;
                if (!labels.Contains(label)) labels.Add(label);
            }

            if (labels.Count == 0) return "";

            // Rare leads whatever else is there. It is the one that says
            // something about the card's standing rather than its art, and it
            // is the treatment Zamar raised this from.
            int rare = labels.IndexOf(Vocabulary.Cards.Rare);
            if (rare > 0)
            {
                labels.RemoveAt(rare);
                labels.Insert(0, Vocabulary.Cards.Rare);
            }

            return " " + string.Join(" ", labels.ConvertAll(l => l + ".").ToArray());
        }

        // ---------------------------------------------------------------------
        // WHAT DOES DynamicPortrait ACTUALLY LOOK LIKE? (Session 15.)
        //
        // Log-only. Nothing here is spoken and nothing here changes a line.
        //
        // DynamicPortrait went silent at 0.7.40 because the word chosen for it,
        // "Moving portrait", was mine and Zamar reported it as visually untrue
        // on the Curious Egg. There is no display name for an appearance
        // anywhere in the assembly, so the only honest way to word it is for him
        // to look at the cards that carry it — and he has seen exactly one.
        //
        // This gives him the list. Every card carrying DynamicPortrait is logged
        // once by name, so a run comes back as "here are the cards to look at"
        // rather than a single example.
        //
        // It also logs the portrait COMPONENTS on the live card, which is the
        // part that may answer the question outright. From inventory_all_types:
        // DiskCardGame.DynamicCardPortrait has five subclasses in the assembly —
        // BountyHunterPortrait, BuildACardPortrait, DeathCardPortrait,
        // HydraEggPortrait, OnlineFriendPortrait. If the Curious Egg turns out
        // to hold a HydraEggPortrait, then what DynamicPortrait marks is a face
        // ASSEMBLED at runtime rather than a painted one, and that is a fact he
        // can word from instead of a member name I guessed at.
        //
        // Matched on the type name ending in "Portrait" rather than on the type
        // itself, deliberately: nothing in the dumps confirms DynamicCardPortrait
        // is publicly accessible, and a probe is not worth risking the build for.
        //
        // GetComponentsInChildren is not the banned lookup — the hard rule bans
        // scene scans, and this asks one card about itself. It runs once per
        // distinct card name and never again, and compose is measured at 0.0ms.
        // ---------------------------------------------------------------------

        private static readonly HashSet<string> _probedDynamicCards = new HashSet<string>();

        private static void ProbeDynamicPortrait(CardInfo info, PlayableCard card)
        {
            string name = CardReader.CardName(info);
            if (string.IsNullOrEmpty(name)) return;
            if (!_probedDynamicCards.Add(name)) return;

            string components;
            if (card == null)
            {
                components = "not a live card, so there is nothing to read here";
            }
            else
            {
                try
                {
                    var found = new List<string>();
                    var all = card.GetComponentsInChildren<UnityEngine.MonoBehaviour>(true);
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null) continue;
                        string t = all[i].GetType().Name;
                        if (t.EndsWith("Portrait") && !found.Contains(t)) found.Add(t);
                    }
                    components = found.Count > 0
                        ? string.Join(", ", found.ToArray())
                        : "none found";
                }
                catch (System.Exception e)
                {
                    components = "probe threw: " + e.Message;
                }
            }

            _log?.LogInfo(
                $"IKMA LOOK PROBE: \"{name}\" carries DynamicPortrait. " +
                $"Portrait components on it: {components}.");
        }

        /// <summary>
        /// One appearance to the word spoken for it, or null to stay silent.
        ///
        /// Switched on the enum rather than on ToString: this runs on every
        /// card read, and Enum.ToString allocates and does a reflection-backed
        /// name lookup. It also means a member that does not exist is a COMPILE
        /// error rather than a silently unmatched string.
        ///
        /// CONFIRMED BY ZAMAR, Session 14 — he corrected five of these against
        /// what is actually on the cards, which is the only authority there is:
        ///   Terrain border, Glitched portrait, Snelk portrait, Bloodied
        ///   portrait, and GiantAnimatedPortrait folded into "Animated
        ///   portrait" rather than getting a word of its own.
        ///
        /// SILENT — DefaultEmission. It is what an ordinary card carries.
        ///
        /// STILL PROVISIONAL, not yet heard in play and not yet corrected:
        ///   Moving portrait (DynamicPortrait), Alternate art (SexyGoat),
        ///   Moon particles (MoonParticleEffects), and the two glows.
        ///   The IKMA LOOK log lines are how these get settled.
        /// </summary>
        private static string AppearanceLabel(CardAppearanceBehaviour.Appearance appearance)
        {
            switch (appearance)
            {
                // --- confident ---
                case CardAppearanceBehaviour.Appearance.RareCardBackground:
                case CardAppearanceBehaviour.Appearance.RareCardColors:
                    return Vocabulary.Cards.Rare;

                case CardAppearanceBehaviour.Appearance.TerrainBackground:
                case CardAppearanceBehaviour.Appearance.TerrainLayout:
                    return Vocabulary.Cards.TerrainBorder;

                case CardAppearanceBehaviour.Appearance.StaticGlitch:
                    return Vocabulary.Cards.GlitchedPortrait;

                case CardAppearanceBehaviour.Appearance.FullCardPortrait:
                    return Vocabulary.Cards.FullCardPortrait;

                case CardAppearanceBehaviour.Appearance.AnimatedPortrait:
                    return Vocabulary.Cards.AnimatedPortrait;

                case CardAppearanceBehaviour.Appearance.GiantAnimatedPortrait:
                    return Vocabulary.Cards.AnimatedPortrait;

                // Silent. Zamar on the Curious Egg, Session 14: it read
                // "Moving portrait" and "visually that was untrue". The word was
                // mine — there is no display name for an appearance anywhere in
                // the assembly, so DynamicPortrait was described from its member
                // name by someone who cannot see it, and the guess was wrong.
                //
                // A false description of a card's face is worse than no
                // description, so it says nothing until he tells me what the
                // treatment actually looks like.
                case CardAppearanceBehaviour.Appearance.DynamicPortrait:
                    return null;

                case CardAppearanceBehaviour.Appearance.HologramPortrait:
                    return Vocabulary.Cards.HologramPortrait;

                case CardAppearanceBehaviour.Appearance.GoldEmission:
                    return Vocabulary.Cards.GoldenGlow;

                case CardAppearanceBehaviour.Appearance.RedEmission:
                    return Vocabulary.Cards.RedGlow;

                // --- the baseline, and it was the baseline ---
                //
                // Zamar, Session 14: "There should be no mod description on a
                // default card." DefaultEmission is what an ordinary card
                // carries, so announcing it put a word on the end of nearly
                // everything and told the player nothing. Silent.
                //
                // The suspicion was in the source before the build and the
                // instruction arrived before the playtest, which is the cheapest
                // way this could have been settled.
                case CardAppearanceBehaviour.Appearance.DefaultEmission:
                    return null;

                // --- provisional wording ---
                case CardAppearanceBehaviour.Appearance.AddSnelkDecals:
                    // 0.7.312, Zamar: "Remove Snelk portrait." One of the
                    // seventeen art treatments from Session 14 that does not
                    // earn a word — it decorates the Long Elk's portrait and
                    // says nothing about the card. Silent, not renamed.
                    return null;

                case CardAppearanceBehaviour.Appearance.AlternatingBloodDecal:
                    return Vocabulary.Cards.BloodiedPortrait;

                // Silent. Zamar, Session 14: the standard Black Goat carries this,
                // so it is the goat's ordinary art rather than a treatment, and
                // "Alternate art" on every Black Goat is a word that means
                // nothing. Same call as DefaultEmission — no mod description on
                // a default card.
                case CardAppearanceBehaviour.Appearance.SexyGoat:
                    return null;

                case CardAppearanceBehaviour.Appearance.MoonParticleEffects:
                    return Vocabulary.Cards.MoonParticles;
            }

            // A member added by a future patch or by another mod. Logged once
            // by the caller and left unspoken rather than read out as an id.
            return null;
        }

        // SpecialStatIcon -> what the varying number actually tracks.
        // Members confirmed via reflection dump: None, Ants, Bones, Mirror,
        // Bell, GreenGems, CardsInHand, SacrificesThisTurn.
        // GreenGems is Act 2 only and will not appear in Kaycee's Mod, but is
        // covered so nothing goes unannounced if it ever does.
        internal static string DescribeSpecialStat(SpecialStatIcon icon)
        {
            // Session 13: this used to switch on icon.ToString(). Enum.ToString
            // allocates a string and does a reflection-backed name lookup, and
            // this runs on every card read — every arrow press through the hand,
            // every board read, every card in a starter deck. Switching on the
            // enum itself is free.
            //
            // The names are compared as an enum now, so a member that does not
            // exist would be a COMPILE error rather than a silently unmatched
            // string. Verified against the reflection dump: None, Ants, Bones,
            // Mirror, Bell, GreenGems, CardsInHand, SacrificesThisTurn.
            switch (icon)
            {
                // Zamar, Session 14. The rulebook page is the authority and it
                // says "the number of Ants that the owner has on their side of
                // the table" — not the whole board. IKMA had been saying "on
                // the board", which overcounts on any turn the opponent also
                // has ants out, and a blind player planning around it would be
                // planning around a number the card never had.
                //
                // "its owner's side" rather than "your side": this same line
                // reads on enemy cards, where "your" would be false.
                case SpecialStatIcon.Ants:               return Vocabulary.Cards.AttackEqualsTheNumber;
                case SpecialStatIcon.Bones:              return Vocabulary.Cards.AttackIsEqualTo;
                // THE THREE INKED SIGILS ARE SILENT. (0.7.50, Zamar's call.)
                //
                // "Although correct, that information is supposed to be hidden,
                // not in the card's description — that's why its page is inked
                // in the rulebook. It should just list its power as Star."
                //
                // These are exactly the three pages RulebookReader._obscuredPages
                // covers: Mirror, Bell, CardsInHand. The rulebook already refuses
                // to read them because the printed page is destroyed by ink, and
                // explaining the same rule in the card read handed a blind player
                // something no sighted player can get anywhere in the game.
                //
                // This is the standing parity rule, and it is the direction it
                // usually does not run: if the page hides it from everyone, IKMA
                // hides it too. The stat still reads "Star", which is what the
                // card prints.
                case SpecialStatIcon.Mirror:
                case SpecialStatIcon.Bell:
                case SpecialStatIcon.CardsInHand:        return "";
                case SpecialStatIcon.SacrificesThisTurn: return Vocabulary.Cards.AttackEqualsSacrificesMade;
                case SpecialStatIcon.GreenGems:          return Vocabulary.Cards.AttackVariesWithGreen;
                default:                                 return "";
            }
        }

        /// <summary>
        /// Returns a human-readable string describing why a card can't be played,
        /// or null if the card is affordable. Used by HotkeyManager before calling
        /// OnCardSelected, so the player gets immediate feedback instead of silence.
        /// </summary>
        public static string GetAffordabilityError(PlayableCard card)
        {
            if (card?.Info == null) return Vocabulary.NoCardSelected;
            var info = card.Info;
            var rm = ResourcesManager.Instance;
            if (rm == null) return null;

            if (info.BloodCost > 0)
            {
                // Session 8: use the game's own AvailableSacrificeValue
                // (confirmed via reflection dump) instead of counting cards —
                // it correctly values multi-blood sacrifices like Worthy
                // Sacrifice goats (worth 3).
                var bm = Singleton<BoardManager>.Instance;
                int availableBlood = bm != null ? bm.AvailableSacrificeValue : 0;

                if (availableBlood < info.BloodCost)
                {
                    int missing = info.BloodCost - availableBlood;
                    return $"{CardReader.CardName(info)} requires {missing} additional blood.";
                }
            }
            else if (info.BonesCost > 0)
            {
                if (rm.PlayerBones < info.BonesCost)
                {
                    int missing = info.BonesCost - rm.PlayerBones;
                    string boneWord = missing == 1 ? "bone" : "bones";
                    return $"{CardReader.CardName(info)} requires {missing} additional {boneWord}.";
                }
            }

            return null; // Card is affordable.
        }

        // Cost wording: "Costs X blood/bones." (Session 8 note 1 — revised from
        // Session 7's "Requires X" phrasing per in-game testing.)
        private static string FormatCost(CardInfo info)
        {
            // Session 13, Zamar: "Flying Ant costs 1 blood" ran the name and
            // the price into one breath, so the name — the part being scanned
            // for — arrived attached to something else. Its own sentence, with
            // the label leading, lets the ear drop the rest once it knows the
            // card.
            if (info.BloodCost > 0)
            {
                string s = Vocabulary.Cards.BloodCount(info.BloodCost);
                return Vocabulary.Cards.CostLine(s);
            }
            if (info.BonesCost > 0)
            {
                string s = Vocabulary.BoneCount(info.BonesCost);
                return Vocabulary.Cards.CostLine(s);
            }
            // Session 14, Zamar: a card that costs nothing should SAY so, unless
            // it is a Squirrel. "Tadpole. 0, 1." left him unable to tell a free
            // card from a card whose cost IKMA had failed to read — silence is
            // ambiguous, and "Cost: Free." is not.
            //
            // Squirrels stay silent because they are the resource rather than a
            // purchase; the whole economy assumes they are free, and saying it
            // on every one would be a word on the most-read card in the game.
            // Gated on the tribe, matching the existing Squirrel suppression in
            // the tribe list rather than inventing a second rule.
            try
            {
                bool squirrel = false;
                var tribes = info.tribes;
                if (tribes != null)
                    for (int i = 0; i < tribes.Count; i++)
                        if (tribes[i] == Tribe.Squirrel) { squirrel = true; break; }

                if (!squirrel) return Vocabulary.Cards.CostFree;
            }
            catch { /* fall through to silence, never to a wrong cost */ }

            // Squirrels — omit cost entirely, nothing useful to say.
            return null;
        }

        // Audit fix: previously this could NRE if called on a cache miss before
        // ScriptableObjectLoader populated (AllData null), and it permanently
        // cached null on a failed lookup — an ability queried too early would
        // then never resolve for the rest of the session. Now: null-guard the
        // loader, and only cache successful lookups so a later call can retry.
        // ------------------------------------------------------------------
        // Which sigils were named in the line the player just heard.
        // (Session 13 — Shift+R.)
        //
        // Zamar's reasoning, and it is the strongest argument for a feature this
        // project has had: a blind player has to LEARN every sigil name by ear,
        // with no card art to jog the memory and no way to glance at the
        // rulebook while a menu is open. "Mighty Leap" means nothing until you
        // have looked it up, and looking it up mid-menu was impossible.
        //
        // This is a deliberate, narrow exception to strict parity, made at his
        // call. It surfaces only text the rulebook already shows any player who
        // opens it — no hidden data, no advantage in play — and it removes a
        // memory burden the sighted player never carried.
        //
        // GetAbilityName is the one place every sigil name in the mod is
        // resolved, so recording here catches board reads, hand reads, hover
        // reads, sacrifice prompts, starter decks and card rewards without
        // touching any of them.
        private static readonly List<Ability> _abilitiesThisLine = new List<Ability>();
        private static List<Ability> _abilitiesLastSpoken = new List<Ability>();

        // WHICH CARD EACH ABILITY WAS HEARD FROM. (0.7.179.)
        //
        // Zamar: "Shift + R in the context of the card being in your hand or on
        // the board ... should read which direction Sprinter and Rampager are
        // moving. In the rulebook I think they both point right by default,
        // which doesn't matter, but anywhere else it does matter."
        //
        // Shift+R explains the abilities from the last line spoken, and until
        // now it only remembered WHICH abilities — so a Sprinter looked up from
        // a card read got the generic rulebook sentence with no direction, even
        // though the card read one breath earlier had said "moving right".
        //
        // THE DISTINCTION HE IS DRAWING IS EXACTLY RIGHT, and it is the same
        // line the direction feature has always been on: the rulebook page (R)
        // is generic text about a sigil and has no card behind it; everywhere
        // else the sigil belongs to a particular card, and for a mover that
        // card has a direction. So the lookup carries the direction if and only
        // if the ability was heard FROM a card.
        //
        // Parallel lists rather than a Dictionary, for the same reason
        // DamageDeathMerger uses them: these are Unity objects that may be
        // destroyed between insert and lookup, and hashed lookup on a destroyed
        // object is unreliable.
        private static readonly List<Ability>      _abilityCardKeys  = new List<Ability>();
        private static readonly List<PlayableCard> _abilityCardsThisLine = new List<PlayableCard>();
        private static List<Ability>      _abilityCardKeysLast  = new List<Ability>();
        private static List<PlayableCard> _abilityCardsLastSpoken = new List<PlayableCard>();

        /// <summary>
        /// The card the given ability was last heard from, or null if it was
        /// heard with no card behind it (the rulebook, a queue read of printed
        /// sigils, a line that named a sigil generically).
        /// </summary>
        public static PlayableCard CardForLastSpokenAbility(Ability ability)
        {
            for (int i = 0; i < _abilityCardKeysLast.Count; i++)
                if (_abilityCardKeysLast[i] == ability)
                    return i < _abilityCardsLastSpoken.Count ? _abilityCardsLastSpoken[i] : null;
            return null;
        }

        /// <summary>Sigils named in the most recently spoken line, in order.</summary>
        public static List<Ability> LastSpokenAbilities => _abilitiesLastSpoken;

        // ==================================================================
        // ITEMS FOR SHIFT+R. (0.7.344.)
        //
        // Zamar, Session 26: "Can we add item functionality to Shift + R as
        // well? Since items are in the rulebook. I want to Shift + R after
        // hearing an item to hear what it does, the same as a sigil."
        //
        // The same latch as the sigils: every read that names an item notes
        // it while the line is composed, and Speak latches it with the line.
        // Whichever of the two — sigils or items — was latched LAST is what
        // Shift+R explains, so it always answers about what he just heard.
        // ==================================================================
        private static readonly List<ConsumableItemData> _itemsThisLine = new List<ConsumableItemData>();
        private static List<ConsumableItemData> _itemsLastSpoken = new List<ConsumableItemData>();
        private static bool _itemsAreNewest;

        /// <summary>A read is naming this item in the line it is composing.</summary>
        public static void NoteItemInLine(ConsumableItemData data)
        {
            if (data != null && !_itemsThisLine.Contains(data)) _itemsThisLine.Add(data);
        }

        /// <summary>The items in the last line that named any, if that line is
        /// newer than the last line that named a sigil; otherwise null.</summary>
        public static List<ConsumableItemData> LastSpokenItemsIfNewest =>
            _itemsAreNewest && _itemsLastSpoken.Count > 0 ? _itemsLastSpoken : null;

        /// <summary>
        /// Drop the Shift+R memory. (Session 14.)
        ///
        /// For a line that is NOT about any sigil. The memory is only refreshed
        /// when a line resolved at least one ability name, which is right for
        /// card reads and wrong for the rulebook: browsing to an item or boon
        /// page left the memory holding whatever card was read minutes earlier,
        /// so Shift+R confidently explained an unrelated sigil.
        ///
        /// Zamar caught it on the Frozen Opossum Bottle and Black Goat Bottle
        /// pages, where it replayed Airborne and Brood Parasite from a Cuckoo he
        /// had browsed several screens back. Silence beats a wrong answer.
        /// </summary>
        /// <summary>
        /// Record every sigil NAMED inside a block of text, so Shift+R can
        /// explain it. (Session 14, Zamar's call.)
        ///
        /// The rulebook constantly names sigils in prose that are not the page's
        /// own subject — the Frozen Opossum Bottle page defines the creature as
        /// "0 Power, 5 Health, Frozen Away", and Made Of Stone's description
        /// names Touch of Death and Stinky. Those are exactly the moments a
        /// player wants the lookup, and they were the moments it had nothing.
        ///
        /// Scans the resolved name cache rather than guessing at word shapes, so
        /// only real ability names match and each one goes through the same
        /// GetAbilityName choke point everything else uses.
        ///
        /// Cost is a substring search per known ability over one short page.
        /// Safe here specifically because it runs on a page turn, not per frame
        /// — and because the 0.7.37 measurements put compose at a median of
        /// 0.0ms while the speech call itself runs to 765ms. Compose is not
        /// where this project's time goes.
        /// </summary>
        public static void NoteAbilitiesMentionedIn(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            EnsureCacheReady();
            if (_abilityNameCache == null) return;

            try
            {
                foreach (var pair in _abilityNameCache)
                {
                    string name = pair.Value;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (text.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!_abilitiesThisLine.Contains(pair.Key)) _abilitiesThisLine.Add(pair.Key);
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA ABILITY: mention scan failed: {e.Message}");
            }
        }

        // ----------------------------------------------------------------------
        // Is the last thing IKMA said a Shift+R explanation? (Session 14.)
        //
        // Zamar's rule: repeat Shift+R presses must not cut the explanation off,
        // but everything else still may. So this is not a timer — a timer would
        // guess at speech length, and the 0.7.37 measurements put a single
        // speech call anywhere between 12ms and 765ms. It is an exact question:
        // was the most recent utterance this lookup, and has nothing spoken
        // since.
        //
        // Speak clears it for any other line, which is what keeps the second
        // half of his rule true — the lookup remains stompable by a card read, a
        // death, or anything else that comes along.
        // ----------------------------------------------------------------------
        private static string _pendingAbilityLookup;
        private static bool _lastLineWasAbilityLookup;

        public static bool LastLineWasAbilityLookup => _lastLineWasAbilityLookup;

        /// <summary>Called just before queueing a Shift+R explanation.</summary>
        public static void MarkNextLineAsAbilityLookup(string line)
        {
            _pendingAbilityLookup = line;
        }

        public static void ForgetSpokenAbilities()
        {
            _abilitiesThisLine.Clear();
            _abilitiesLastSpoken = new List<Ability>();
            _abilityCardKeys.Clear();
            _abilityCardsThisLine.Clear();
            _abilityCardKeysLast = new List<Ability>();
            _abilityCardsLastSpoken = new List<PlayableCard>();
        }

        private static void NoteAbilityCard(Ability ability, PlayableCard card)
        {
            if (card == null) return;
            for (int i = 0; i < _abilityCardKeys.Count; i++)
                if (_abilityCardKeys[i] == ability) { _abilityCardsThisLine[i] = card; return; }

            _abilityCardKeys.Add(ability);
            _abilityCardsThisLine.Add(card);
        }

        public static string GetAbilityName(Ability ability)
        {
            if (!_abilitiesThisLine.Contains(ability)) _abilitiesThisLine.Add(ability);

            EnsureCacheReady();
            if (_abilityNameCache.TryGetValue(ability, out string cached))
                return cached;

            var allData = ScriptableObjectLoader<AbilityInfo>.AllData;
            if (allData == null) return null;

            var info = allData.Find(x => x != null && x.ability == ability);
            string result = info?.rulebookName;
            if (result != null)
                _abilityNameCache[ability] = result;
            return result;
        }

        // The one place an utterance is logged. Every other reader delegates
        // here and stays silent, so the log is one line per thing actually
        // said. Prefixed IKMA like every other line the mod emits, so the whole
        // mod's output can be filtered as a block.
        /// <summary>
        /// True when a temporary mod on this card negates the sigil. (Session
        /// 32, found in the M5 item audit.) Magickal Bleach works this way:
        /// it adds a temporary mod whose negateAbilities lists every sigil the
        /// card has (BleachPotItem.RemoveCardAbilities), and the game then
        /// draws NO icon for those sigils - CardAbilityIcons.
        /// GetDistinctShownAbilities removes any ability a mod negates, and
        /// PlayableCard.HasAbility answers false for it. IKMA's reads listed
        /// them anyway, so a bleached card was still described with its
        /// sigils: a confidently wrong read. Same test the game's icon code
        /// uses, asked of the same list.
        /// </summary>
        internal static bool NegatedOnCard(PlayableCard card, Ability ability)
        {
            try
            {
                // 0.7.448 - A SPENT SHIELD IS NOT ON THE CARD ANY MORE. Once
                // Armored has taken its one hit, PlayableCard.
                // UpdateFaceUpOnBoardEffects adds DeathShield to
                // Status.hiddenAbilities and re-renders: the icon is gone for
                // a sighted player. Zamar's 0.7.447 log still read "Stinky and
                // Armored and Fecundity" off the Skunk that had lost it.
                if (ability == Ability.DeathShield && card?.Status?.hiddenAbilities != null
                    && card.Status.hiddenAbilities.Contains(Ability.DeathShield)) return true;

                var mods = card?.TemporaryMods;
                if (mods == null) return false;
                foreach (var m in mods)
                    if (m?.negateAbilities != null && m.negateAbilities.Contains(ability)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Cut whatever is being spoken without saying anything in its place.
        /// (Session 11.)
        ///
        /// For the moment a player commits to something and the line still
        /// reading belongs to the moment before. Travelling on the map is the
        /// case that raised it: the idle prompt kept listing map controls while
        /// the piece was already moving. "Traveling to X." was removed in
        /// 0.7.13 precisely because the game says that better with the piece
        /// slide, so there is nothing to REPLACE the stale line with — it just
        /// needs to stop.
        /// </summary>
        public static void Silence()
        {
            _log?.LogInfo("IKMA SPEAK: silence (stale line cut).");
            // Through the pump, not straight to the library — same queue, same
            // order, same one thread. See SpeechPump's class comment.
            SpeechPump.Say(string.Empty, true);
            InterruptCount++;
            Speech.NoteInterrupted();

            // 0.7.435 - A CUT EXPLANATION IS NOT "STILL THE LAST THING SAID".
            // Session 43: Zamar pressed Shift+R after the enemy totem line; the
            // answer was queued behind the turn-start lines, he pressed Ctrl,
            // and every Shift+R after that was refused by the repeat guard -
            // which only another spoken line used to clear. The guard exists
            // so a repeat press cannot cut the explanation off; once speech
            // has been cut there is no explanation in the air to protect.
            _lastLineWasAbilityLookup = false;
        }

        /// <summary>
        /// How many times speech has been cut: every line handed over with
        /// interrupt=true, and every Silence. Speech's Alert row compares it to
        /// tell whether the overlay line it spoke has been cut since. Main
        /// thread only. (0.7.359.)
        /// </summary>
        internal static int InterruptCount;

        private static readonly System.Text.RegularExpressions.Regex _tribeWords =
            new System.Text.RegularExpressions.Regex(
                @"\b([Tt])ribes\b|\b([Tt])ribal\b|\b([Tt])ribe\b",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static string KinWording(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.IndexOf("rib", System.StringComparison.Ordinal) < 0) return text;
            return _tribeWords.Replace(text, Vocabulary.Kin);
        }

        // The last thing said, for the repeat guard above. Not a general
        // duplicate filter: he asks for the same read twice on purpose all the
        // time, and hearing it twice is correct when nothing is competing.
        private static string _lastSpoken;

        /// <summary>
        /// Was <paramref name="text"/> the last line handed to the speech pump?
        /// </summary>
        /// <remarks>
        /// 0.7.266, for the draw line. It must cut the draw-phase prompt and must
        /// not cut a combat result, and an interrupt cannot tell them apart — it
        /// flushes the pump's whole queue either way. So the caller asks what is
        /// actually in the air first. Only sound as long as the sentence has ONE
        /// composer, which is what forced Vocabulary.DrawPhasePrompt to exist.
        /// </remarks>
        internal static bool LastSpokenWas(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return string.Equals(_lastSpoken, text, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// True when saying this would be repeating the last line over the
        /// game's own voice. Callers that are NOT answering a keypress may use
        /// it to stay quiet; a caller answering a press must not — see the note
        /// in Speak.
        /// </summary>
        internal static bool WouldRepeatOverGameVoice(string text)
        {
            return text != null && text == _lastSpoken && GameIsTalking();
        }

        /// <summary>
        /// True while the game itself is speaking a scripted line. Same signal
        /// the conversation lock uses -- see DialogueAdvancer.
        /// </summary>
        internal static bool GameIsTalking()
        {
            try
            {
                // PlayingEvent is a scripted dialogue EVENT. A bare
                // ShowUntilInput hold is the other way the game waits on a
                // press, and it is not an event — see DialogueAdvancer for the
                // Prospector barks that stranded the keyboard. (0.7.169.)
                if (DialogueAdvancer.HoldingUntilInput) return true;

                var td = Singleton<TextDisplayer>.Instance;
                return td != null && td.PlayingEvent;
            }
            catch { return false; }
        }

        /// <summary>
        /// The choke point: kin/tribe, the Shift+R memory, the speech log, the
        /// one-shot result protection, and the hand-off to SpeechPump.
        /// </summary>
        /// <remarks>
        /// 0.7.207 — NOT CALLED FROM READERS ANY MORE. Every reader speaks
        /// through <see cref="Speech"/>, which names WHY a line is spoken and
        /// turns that into the <paramref name="interrupt"/> flag here. The
        /// flag is now an implementation detail of the policy in Speech.cs;
        /// the only callers of this method are Speech, CombatAnnouncer's
        /// queue, and Silence(). check_source.ps1 CHECK 8 is unchanged: this
        /// is still the one place speech leaves the mod.
        /// </remarks>
        public static void Speak(string text, bool interrupt)
        {
            // Session 34 - on a controller, key names become the buttons that
            // do them (Zamar's four rules; see PadWords). First, so the log,
            // the repeat memory and the speech all carry the words said.
            text = PadWords.ForSpeech(text);
            // Session 34: "Gamepad." / "Keyboard." when the player switches.
            string switchWord = KeyIn.TakeSwitchWord();
            if (switchWord != null && !string.Equals(text, switchWord, System.StringComparison.Ordinal))
                text = switchWord + " " + text;
            try { Rumble.NoteSpoken(text); } catch { }   // Session 34: browse tap only when the selection moved

            // Whatever sigils were resolved while composing THIS line are the
            // ones Shift+R should explain. Latched here, at the moment of
            // speech, so the memory always matches what the player last heard.
            // 0.7.344 — items latch the same way; see NoteItemInLine.
            if (_itemsThisLine.Count > 0)
            {
                _itemsLastSpoken = new List<ConsumableItemData>(_itemsThisLine);
                _itemsThisLine.Clear();
                _itemsAreNewest = true;
            }

            if (_abilitiesThisLine.Count > 0)
            {
                // A line naming sigils AND items leaves the items newest only
                // when it named no sigil; sigils on the same line win, as
                // before this change.
                _itemsAreNewest = false;
                _abilitiesLastSpoken = new List<Ability>(_abilitiesThisLine);
                _abilitiesThisLine.Clear();

                // Latched together with the abilities, at the same moment and
                // for the same reason: the memory must match what he just
                // heard, not what happened to be composed since.
                _abilityCardKeysLast    = new List<Ability>(_abilityCardKeys);
                _abilityCardsLastSpoken = new List<PlayableCard>(_abilityCardsThisLine);
                _abilityCardKeys.Clear();
                _abilityCardsThisLine.Clear();
            }

            // Latch or clear the Shift+R repeat guard. Matched on the exact
            // text so only the queued explanation itself sets it.
            _lastLineWasAbilityLookup =
                _pendingAbilityLookup != null && text == _pendingAbilityLookup;
            if (_lastLineWasAbilityLookup) _pendingAbilityLookup = null;

            // KIN, NEVER TRIBE. (0.7.49, Zamar's call.)
            //
            // "Change that word tribe to Kin. I don't care if that's actually
            // what it says in the rules. I want that continuity. Also, for the
            // same reason Magic moved away from Tribal, it's more politically
            // correct."
            //
            // Card reads have said "Kin type:" since Session 8, but the rulebook
            // renders the game's own strings and those say tribe — so the same
            // concept had two names depending on where the player heard it.
            //
            // Done HERE, at the single choke point every utterance passes
            // through, rather than in the rulebook alone: item text, boon text
            // and anything added later are covered without anyone remembering
            // to. Whole words only, so a word merely containing the letters is
            // untouched. The log is written AFTER, so what is logged is what was
            // said — the log is a player-facing artifact and the two must agree.
            text = KinWording(text);

            // ==================================================================
            // THIS GUARD USED TO LIVE HERE AND IT WAS WRONG. (0.7.151 ->
            // 0.7.156.)
            //
            // 0.7.151 suppressed, GLOBALLY, any interrupting line identical to
            // the one just spoken while the game was talking. It fixed the case
            // it was written for — Leshy explaining a refused play, stomped
            // seven times by IKMA's own worse answer — and then ate the
            // campfire's conversation lock:
            //
            //   DIALOGUE: key swallowed — a conversation is in progress.
            //   SPEAK: repeat suppressed: Conversation in progress, press Space
            //   ... eleven more times ...
            //
            // Zamar: "arrow keys arnt giving the warning during the
            // conversation." The lock was working perfectly. The warning was
            // being thrown away, so every arrow press after the first answered
            // with silence — which reads as a dead key, the one failure this
            // project treats as unacceptable.
            //
            // THE ERROR: a repeat is not always redundant. A line that ANSWERS A
            // KEYPRESS has to be said every time the key is pressed, however
            // many times that is, because the player has no other confirmation
            // the press was received. The two cases are indistinguishable from
            // inside Speak — both are an interrupting line repeating over the
            // game's voice — so the rule cannot live at the choke point.
            //
            // It now lives at the ONE call site that earned it, where the game's
            // line and IKMA's answer the same question. See
            // HotkeyManager.JumpToNextPlayableCard.
            //
            // THE GENERAL LESSON: the choke point is the right place for a rule
            // that is true of every utterance (Kin, never tribe). A rule that
            // depends on WHY a line is being spoken does not belong there, and
            // "it is a repeat" is never the whole reason.
            // ==================================================================
            _lastSpoken = text;

            // 0.7.266 — two consumers of "what was said last", both added for
            // the draw line. LastSpokenWas answers which sentence is in the
            // air; NoteSomethingSpoken tells the H watchdog that a keypress got
            // an answer. Both are recorded here because this is the one place
            // every utterance in the mod passes through.
            HotkeyManager.NoteSomethingSpoken();

            // 0.7.193 — DO NOT CUT A COMBAT RESULT THAT JUST LANDED.
            //
            // A one-shot armed by CombatAnnouncer when it speaks an Action or
            // Dialogue line. The first interrupting line after one is downgraded
            // to a queued one so it FOLLOWS the result instead of stomping it.
            //
            // WHY THIS ONE DOES BELONG AT THE CHOKE POINT, despite the rule
            // written directly above. That rule bans a test of WHY THIS line is
            // being spoken. This is not that: it is a test of what was spoken
            // LAST, which is a property of the announcer's state and is equally
            // true of every interrupting utterance, whatever asked for it.
            if (interrupt && Speech.ConsumeInterruptProtection())
            {
                interrupt = false;
                _log?.LogInfo("IKMA SPEAK: queued behind a combat result rather than cutting it.");
            }

            _log?.LogInfo($"IKMA SPEAK{(interrupt ? " (interrupt)" : "")}: {text}");

            // MEASURED AT 0.7.38, and it is settled: composing this line cost
            // 0.0ms median, and the P/Invoke that carries it to NVDA cost 75ms
            // median and 765ms at worst, blocking Unity's main thread every
            // time. So the call is handed to SpeechPump and this returns
            // immediately. The pump still times the native call and still logs
            // it as "IKMA PERF: speechSay took Xms", from the main thread, so
            // the numbers stay comparable with every earlier log.
            //
            // Strict FIFO in the pump: lines reach the screen reader in exactly
            // the order they were spoken here, which is what makes this a
            // performance change and not a narration change.
            SpeechPump.Say(text, interrupt);
            // Any interrupt=true call stomps NVDA's speech buffer. Tell the
            // CombatAnnouncer to hold off so queued messages (bones, damage, deaths)
            // wait for play-flow speech to finish before resuming.
            if (interrupt)
            {
                InterruptCount++;
                Speech.NoteInterrupted();
            }
        }
    }
}
