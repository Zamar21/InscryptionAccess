// BossNarrator.cs
using System.Collections.Generic;
using System.Reflection;
using DiskCardGame;

namespace IKMA
{
    // -------------------------------------------------------------------------
    // Boss lives, the player's candles, and Leshy's masks. (Session 14.)
    //
    // Written from dump_bosses_and_run_end.txt, which was run to answer one
    // question before Zamar started testing full runs: what does a boss fight
    // do that a normal fight does not, and is IKMA silent about any of it.
    //
    // It was silent about the most important thing in the mode.
    //
    // THE CANDLES ARE THE PLAYER'S LIVES. In Kaycee's Mod a lost battle blows
    // out a candle, and when the last one goes out the run is over. Nothing in
    // IKMA has ever said a word about it. A sighted player watches the flame go
    // out; a blind player has been playing a mode built around a resource they
    // could not observe. This is not a nicety on a boss screen — it is the
    // central stake of the whole game mode, and it has been invisible.
    //
    // THE BOSSES HAVE LIVES TOO, and that silence is dangerous in the other
    // direction. Knock a boss down and it comes back in a new phase. If IKMA
    // says nothing, the player believes they have won a fight that has just
    // restarted — which is the stale-board-model failure the project keeps
    // running into, applied to the whole encounter rather than to one slot.
    //
    // CONFIRMED BEFORE ANY OF THIS WAS WRITTEN:
    //   Opponent.NumLives            PUBLIC get/set, Int32
    //   Opponent.StartingLives       PUBLIC get
    //   Opponent.LifeLostSequence()  PUBLIC IEnumerator
    //   CandleHolder.BlowOutCandle(Int32 livesRemaining)   NONPUBLIC
    //   CandleHolder.AddExtraCandleSequence()              PUBLIC IEnumerator
    //   LeshyBossOpponent.AdvanceMaskState()               PUBLIC IEnumerator
    //
    // THE INHERITANCE DETAIL THAT DECIDES THE PATCHING. It was got wrong here
    // for five sessions, so this paragraph is the corrected version. (0.7.137.)
    //
    // WHAT THIS COMMENT USED TO SAY: that Part1BossOpponent does not declare
    // LifeLostSequence, so three bosses inherit Opponent's and only Leshy needs
    // a second patch. Every clause of that was wrong.
    //
    // WHAT THE DUMPS ACTUALLY SHOW:
    //   Part1BossOpponent DOES declare LifeLostSequence, [OVERRIDE of Opponent].
    //   Its IL contains no call to the base and the type declares no <>n__
    //   thunk, so the base implementation never runs for a boss. All four Act 1
    //   bosses run THIS declaration.
    //   LeshyBossOpponent overrides it as well, and calls base through a <>n__1
    //   thunk — so Leshy passes through Part1BossOpponent's on his way.
    //
    // CONSEQUENCE, and it is the reason this is worth a long comment: the old
    // patch on Opponent.LifeLostSequence could not fire for any boss, and the
    // 0.7.120 non-boss guard closed the only door it had left. The line had
    // never been spoken once. Nothing in any log said so, because a line that
    // never fires leaves no trace — the absence has to be reasoned about, not
    // observed.
    //
    // ONE patch, on Part1BossOpponent, covers all four. A second one on Leshy
    // would double his line rather than rescue it.
    //
    // GENERALISED, because this shape has now cost this project four patches:
    // an override audit says whether a base patch FIRES. It does not say
    // whether the override CALLS the base, and those two answers demand
    // opposite fixes. dumps/dump_basecalls.ps1 reads both out of the IL.
    //
    // All four go through Plugin.TryPatch rather than PatchAll: one wrong
    // member name inside PatchAll kills every patch registered after it.
    // -------------------------------------------------------------------------
    internal static class BossNarrator
    {
        // ----------------------------------------------------------------------
        // A boss has been knocked down and is coming back.
        //
        // Composed at SPEAK time. A coroutine prefix fires when the enumerator
        // is created, and whether NumLives has already been decremented by then
        // is not something reflection can answer — reading it late means
        // reading it after the sequence has actually run.
        // ----------------------------------------------------------------------
        internal static void AnnounceBossLifeLost(Opponent opponent)
        {
            if (opponent == null) return;

            // ONLY A BOSS HAS LIVES. (0.7.120.)
            //
            // Zamar's 0.7.119 log: a NORMAL encounter ended with "The boss loses
            // its last life." He is right that it does not belong there — "The
            // enemy has no candles/lives. That's only for boss encounters."
            //
            // WHY IT FIRED. Opponent.LifeLostSequence is what the base Opponent
            // runs when it is beaten, and Part1Opponent — a plain Act 1 encounter
            // — does NOT override it, so the base patch fired for every win. The
            // narrator then read NumLives, got 0, and said "its last life".
            //
            // 0.7.137: that base patch is GONE, and this method is now reached
            // only from Part1BossOpponent.LifeLostSequence, so the guard can no
            // longer be false in practice. It is kept deliberately — it costs a
            // type check, and it is the thing that makes the narrator safe to
            // call from any future declaration without re-deriving this.
            //
            // ZERO IS NOT AN ANSWER. NumLives == 0 means both "spent the last
            // one" and "never had any", and this read it as the first. Same
            // defect as the deck view announcing "0 cards" while it was still
            // being dealt, and it is now the third time this shape has cost a
            // wrong line.
            //
            // THE UNAMBIGUOUS QUESTION, from the dump's override audit:
            // LifeLostSequence is overridden by Part1BossOpponent (and
            // LeshyBossOpponent below it) and by nothing else in Act 1. So the
            // type IS the answer — a boss is a Part1BossOpponent and a normal
            // encounter is a plain Part1Opponent. Asked of the object rather
            // than inferred from a count.
            if (!(opponent is Part1BossOpponent))
            {
                Plugin.Log?.LogInfo(
                    "IKMA BOSS: life lost on a non-boss opponent " +
                    $"({opponent.GetType().Name}) — nothing announced.");
                return;
            }

            Plugin.Log?.LogInfo("IKMA BOSS: life lost sequence started.");

            using (Speech.Event(EventKind.Bosses)) Speech.Result(() =>
            {
                int remaining = -1;
                try { remaining = opponent.NumLives; }
                catch { remaining = -1; }

                // 0.7.448 - LESHY'S CANDLES ARE SAID BY LESHY'S OWN LINES.
                // Zamar, Session 45: "Leshy blows out his first of three
                // candles." / "Leshy blows out the second of his three
                // candles. ..." (LeshyNarrator.OnPhaseChange, which the game
                // reaches whenever a life is left). Saying this line as well
                // would be one candle told twice.
                if (opponent is LeshyBossOpponent && remaining > 0) return null;

                int starting = -1;
                try { starting = opponent.StartingLives; }
                catch { starting = -1; }

                // 0.7.206 — ZAMAR: "Should be The Prospector in this context."
            //
            // BossDisplayName returns the bare name ("Prospector"), which is
            // right for a dialogue tag ("Prospector: ...") and wrong inside a
            // sentence. ActorName is the one place that decides how the actor
            // is SAID, and it answers "The Prospector" — and "Leshy" whenever
            // the mask is off, which is the same question this line was
            // silently getting wrong.
            string who = ActorName();

                // ZAMAR'S WORDING, Session 19: "One of the [boss name's] two
                // candles is blown out."
                //
                // The old line was "The boss loses a life. 1 life left." He
                // named two things wrong with it. THE BOSS HAS A NAME and the
                // game has just been shouting it, so "the boss" is a step
                // backwards from what the player already knows. And the boss's
                // lives ARE candles on his skull — the same object the player's
                // own lives use, which the mod already calls candles. Two words
                // for one mechanic taught the player they were two mechanics.
                //
                // The count comes from StartingLives, the game's own number, so
                // this stays correct on a boss with a different number of them
                // rather than hard-coding the two Act 1 bosses happen to have.
                if (starting > 0 && !string.IsNullOrEmpty(who) && remaining > 0)
                    return Vocabulary.Bosses.OneOfSCandles(who, NumberWord(starting));

                // THE LAST CANDLE IS DELIBERATELY SILENT. Zamar, Session 19:
                //
                //   "dont need any call out for blowing out their last candle.
                //    There's a lot of clear audio and a dramatic moment we want
                //    to hear."
                //
                // This is the mod's own rule applied to its most tempting
                // exception. The boss dying is the biggest event in the fight,
                // which is exactly why the instinct is to announce it — and
                // exactly why announcing it would be wrong. The game already
                // gives it a skull animation, a music change and dialogue. A
                // line here would talk over the payoff.
                //
                // Returning null is a real answer, not a gap: the announcer
                // drops a null provider without a sound. Do not "fix" this by
                // filling it in.
                if (remaining == 0) return null;

                // Unknown count — the read failed rather than returned zero.
                // Named as a candle for consistency with the line above, with
                // no number claimed.
                if (remaining < 0)
                    return Vocabulary.Bosses.CandleIsBlownOrOneOfS(string.IsNullOrEmpty(who), who);

                // A boss whose starting count could not be read, or that has no
                // name IKMA recognises. Still true, just less specific.
                return Vocabulary.Bosses.CandleIsBlownOut(string.IsNullOrEmpty(who), remaining, who);
            });
        }

        // ----------------------------------------------------------------------
        // The boss's name, asked of the game rather than looked up. (0.7.162.)
        //
        // Same convention MapReader.BossNameOf already uses for map nodes:
        // take the game's own type name and strip the suffix, so a boss this
        // project has never met still gets named correctly and there is no
        // table of names IKMA believes in. MapReader reads Opponent.Type off a
        // BossBattleNodeData; here the live opponent object is in hand, so its
        // runtime type is the same answer from the other direction.
        //
        //   ProspectorBossOpponent      -> "Prospector"
        //   TrapperTraderBossOpponent   -> "Trapper Trader"
        //
        // Returns null rather than a guess if the shape is unfamiliar, and the
        // caller falls back to a line that names nobody.
        // ----------------------------------------------------------------------
        // ======================================================================
        // LESHY IS NOT THE BOSS UNTIL HE PUTS THE MASK ON. (0.7.204.)
        //
        // Zamar, 2026-09-13:
        //
        //   "Leshy does not become the prospector till he dons his mask. So
        //    'Arrived at Boss Battle: Prospector' is accurate. 'The Prospector
        //    blows out a candle' is inaccurate, that's still Leshy. Same with
        //    placing the Skull on the table."
        //
        // THE IL AGREES, AND IT IS UNAMBIGUOUS. ProspectorBossOpponent's intro
        // calls its base first (<>n__0 at IL_0061), so the whole of
        // Part1BossOpponent.IntroSequence runs before anything Prospector-shaped
        // happens (dumps/dump_prospector.txt, dumps/dump_bossintro.txt):
        //
        //   base:  IL_004D  ReducePlayerLivesSequence   <- the candle, and Smoke
        //          IL_00D0  Instantiate the skull
        //   then:  IL_018E  LeshyAnimationController.PutOnMask   <- HE BECOMES IT
        //          IL_020D  PlayDialogueEvent("ProspectorIntro") <- HEE-HAAW
        //
        // So both lines IKMA was attributing to the Prospector happen while the
        // mask is still off. This is the actor rule from
        // ikma-one-event-one-line: name the actor from the game, and the game is
        // explicit about when the actor changes.
        // ======================================================================
        private static bool _maskOn;

        /// <summary>
        /// Is Leshy wearing the mask RIGHT NOW? (0.7.206.)
        ///
        /// ZAMAR: "Can we more appropriately flip between Leshy and The
        /// Prospector based on if he has the mask on or not, instead of manual
        /// timing like we're doing now? Reading a Prospector line like this
        /// should be impossible to happen when he's not the prospector
        /// anymore."
        ///
        /// That is the whole fix. Two places were answering "who is talking"
        /// and only one of them knew about the mask: ActorName latched on
        /// PutOnMask, while the dialogue attribution in Plugin.cs asked whether
        /// the OPPONENT OBJECT was a boss — which stays true for the rest of
        /// the battle, mask or no mask. So "NEED A LIGHT?", a Leshy line spoken
        /// after the mask came off, was tagged "Prospector:".
        ///
        /// The opponent object is not the actor. The mask is. Anything that
        /// names a speaker asks THIS.
        /// </summary>
        internal static bool MaskIsOn => _maskOn;

        /// <summary>Set from the game's own mask events. See ActorName.</summary>
        internal static void NoteMaskOn()
        {
            if (_maskOn) return;
            _maskOn = true;
            Plugin.Log?.LogInfo("IKMA BOSS: mask on — lines now attribute to the boss.");
        }

        internal static void NoteMaskOff()
        {
            if (!_maskOn) return;
            _maskOn = false;
            Plugin.Log?.LogInfo("IKMA BOSS: mask off — lines attribute to Leshy again.");
        }

        /// <summary>
        /// The mask animation coming off, from anywhere. SILENT — no line.
        /// (0.7.213.)
        ///
        /// LeshyAnimationController.TakeOffMask is called ONLY by the five map
        /// node sequencers listed in AnnounceMaskPutOn; no Act 1 boss calls it
        /// at all (a boss leaves through BossDefeatedSequence, which is where
        /// the spoken "fades back into the shadows" line lives and must stay).
        /// So this is purely the belt to that fix's braces: whatever put the
        /// actor into a masked state, taking the mask off ends it.
        /// </summary>
        internal static void NoteMaskRemoved()
        {
            bool was = _maskOn || _maskIdentity != null;
            NoteMaskOff();
            SetMaskIdentity(null);
            if (was)
                Plugin.Log?.LogInfo("IKMA BOSS: TakeOffMask — actor state cleared, nothing spoken.");
        }

        /// <summary>
        /// Who is doing this, as the sentence should say it: "Leshy" or
        /// "The Prospector". Masked or not decides it, never the encounter.
        /// </summary>
        internal static string ActorName()
        {
            // 0.7.211 - THE MASK IDENTITY OUTRANKS THE OPPONENT OBJECT.
            //
            // In Leshy's own fight the opponent object is LeshyBossOpponent
            // for the whole battle while THREE different bosses take turns
            // wearing his face. BossDisplayName on that object answers
            // "Leshy", so the line below would have produced "The Leshy" the
            // moment a mask went on - the opponent-object-is-not-the-actor
            // bug of 0.7.206, in a new place.
            //
            // LeshyNarrator sets this from maskBossTypes[currentMaskIndex],
            // the game's own record of which mask is on, and clears it when
            // the mask comes off. Every other Act 1 boss leaves it null and
            // falls through to exactly the behaviour it had before.
            if (!string.IsNullOrEmpty(_maskIdentity)) return Vocabulary.Bosses.TheMask(_maskIdentity);

            if (!_maskOn) return Vocabulary.Leshy;

            string boss = null;
            try { boss = BossDisplayName(TurnManager.Instance?.Opponent as Part1BossOpponent); }
            catch { }

            return Vocabulary.Bosses.LeshyThe(string.IsNullOrEmpty(boss), boss);
        }

        // ----------------------------------------------------------------------
        // WHO IS BEHIND THE FACE RIGHT NOW, when the opponent object cannot
        // say. Set only by LeshyNarrator; null everywhere else. (0.7.211.)
        // ----------------------------------------------------------------------
        private static string _maskIdentity;

        /// <summary>
        /// The mask being worn, as the game's own enum names it ("Doctor"),
        /// or null. Bare, unlike ActorName's "The Doctor", because dialogue
        /// attribution prints a speaker name rather than a sentence subject.
        /// (0.7.286.)
        /// </summary>
        internal static string MaskIdentity => _maskIdentity;

        internal static void SetMaskIdentity(string mask)
        {
            if (_maskIdentity == mask) return;
            _maskIdentity = string.IsNullOrEmpty(mask) ? null : mask;

            Plugin.Log?.LogInfo(_maskIdentity == null
                ? "IKMA BOSS: no mask worn - lines attribute to Leshy again."
                : $"IKMA BOSS: mask identity '{_maskIdentity}' - lines attribute to it.");
        }

        /// <summary>
        /// Zamar's written description of a mask, or null if he has not
        /// written one. A mask with no entry says the plain fact instead; a
        /// description is never invented for it. See the design-authority
        /// rule in the session checklist - seventeen card appearances were
        /// worded by the assistant once and five of them were wrong.
        /// </summary>
        internal static string MaskDescription(string mask)
        {
            if (string.IsNullOrEmpty(mask)) return null;
            string line;
            return _maskDescriptions.TryGetValue(mask, out line) ? line : null;
        }

        // ----------------------------------------------------------------------
        // THE MASK GOES ON. (0.7.204.)
        //
        // ZAMAR'S WORDING, 2026-09-13, verbatim, and it belongs to the
        // Prospector specifically — every Act 1 boss wears a different mask, so
        // this is a table and not one sentence. A boss with no entry yet says
        // nothing rather than something invented.
        // ----------------------------------------------------------------------
        // KEYED ON LeshyAnimationController.Mask, THE GAME'S OWN ENUM.
        // (0.7.213.) Its six values are the complete set of faces Leshy ever
        // wears: Prospector, Woodcarver, Angler, Trapper, Trader, Doctor.
        //
        // It used to be keyed on the boss opponent's class name, which could
        // only ever answer during a boss fight. Four of the six masks are
        // worn at MAP NODES with no opponent in existence at all:
        //
        //   Woodcarver -> BuildTotemSequencer      (the totem builder)
        //   Doctor     -> DuplicateMergeSequencer  (the Mycologists)
        //   Trapper    -> BuyPeltsSequencer
        //   Trader     -> TradePeltsSequencer
        //   Prospector -> BoulderChoiceSequencer   (and his own boss fight)
        //   Angler     -> AnglerBossOpponent
        //
        // The mask arrives as an ARGUMENT to PutOnMask, so it is known exactly
        // and needs nothing inferred.
        //
        // ONLY THE PROSPECTOR'S WORDS ARE ZAMAR'S. A mask with no entry here
        // says the plain fact that it went on (Vocabulary.LeshyMaskOn) and
        // logs a request. Its DESCRIPTION is not invented: nobody writing this
        // code can see the masks, and Session 14 spent an hour undoing
        // seventeen invented card-appearance descriptions of which five were
        // wrong. One line per mask from Zamar replaces each placeholder.
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _maskDescriptions => Loc.PerLanguage(ref _maskDescriptionsCache, ref _maskDescriptionsLanguage, Build_maskDescriptions);
        private static Dictionary<string, string> Build_maskDescriptions() =>
            new Dictionary<string, string>
            {
                { "Prospector",
                  Vocabulary.Bosses.LeshyPutsOnA },

                // { "Woodcarver", "..." },   <- Zamar
                // { "Angler",     "..." },   <- Zamar
                // { "Trapper",    "..." },   <- Zamar
                // { "Trader",     "..." },   <- Zamar
                // { "Doctor",     "..." },   <- Zamar
            };
        private static Dictionary<string, string> _maskDescriptionsCache;
        private static string _maskDescriptionsLanguage;

        internal static void AnnounceMaskPutOn(string maskName)
        {
            if (string.IsNullOrEmpty(maskName))
            {
                Plugin.Log?.LogInfo("IKMA BOSS: PutOnMask with no readable mask — nothing said.");
                return;
            }

            // THE MASK IS THE ACTOR, EVERYWHERE — not just in a boss fight.
            // (0.7.213.) Zamar, 0.7.204: "Can we more appropriately flip
            // between Leshy and The Prospector based on if he has the mask on
            // or not... Reading a Prospector line like this should be
            // impossible to happen when he's not the prospector anymore."
            //
            // Set from the argument, so it cannot disagree with what is on his
            // face. Cleared by NoteMaskRemoved on TakeOffMask (the map nodes)
            // and by AnnounceBossFades on BossDefeatedSequence (a boss).
            SetMaskIdentity(maskName);

            // The legacy flag stays in step for anything still reading it.
            // ActorName prefers the identity above, so this decides nothing on
            // its own any more.
            NoteMaskOn();

            // HIS CALL, 0.7.217: "Instead of saying this full intro line, just
            // say 'Leshy puts on the Prospector mask...'"
            //
            // The long visual description he wrote at 0.7.204 is no longer
            // spoken. It stays in _maskDescriptions because he may want it
            // back somewhere, but nothing reads it today — a mask going on is
            // one short line whichever mask it is, and the table is now only
            // consulted through MaskDescription for callers that ask by name.
            // Not "IKMA PROVISIONAL" (0.7.359): that marker belongs to a mask
            // with no words yet, and Vocabulary.LeshyMaskOn logs it by name.
            // This fired for described masks too — the Trader's, his words.
            Plugin.Log?.LogInfo($"IKMA BOSS: mask '{maskName}' on.");
            using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.LeshyMaskOn(maskName));
        }

        // ----------------------------------------------------------------------
        // THE MASK FLIPS.  LeshyAnimationController.FlipMask(Mask)  (0.7.215.)
        //
        // A DIFFERENT METHOD FROM PutOnMask, and the only fight that uses it
        // is the Trapper/Trader — halfway through, the face he is already
        // wearing is swapped for the Trader's. docs/COVERAGE_KM.md flagged
        // this ("FlipMask is not PutOnMask — check attribution") and it was
        // real: nothing touched it, so the actor stayed "The Trapper" through
        // the whole of phase two, naming a character who had already gone.
        //
        // Same description table, same Mask enum, so one mask has one
        // description wherever it appears. Only the sentence differs, because
        // a flip is not a mask going on and saying so would be false.
        // ----------------------------------------------------------------------
        internal static void AnnounceMaskFlipped(string maskName)
        {
            if (string.IsNullOrEmpty(maskName))
            {
                Plugin.Log?.LogInfo("IKMA BOSS: FlipMask with no readable mask — nothing said.");
                return;
            }

            SetMaskIdentity(maskName);
            NoteMaskOn();

            string line = MaskDescription(maskName);
            if (!string.IsNullOrEmpty(line))
            {
                Plugin.Log?.LogInfo($"IKMA BOSS: mask flipped to '{maskName}' — Zamar's description.");
                using (Speech.Event(EventKind.Bosses)) Speech.Result(line);
                return;
            }

            Plugin.Log?.LogInfo(
                $"IKMA PROVISIONAL: mask flipped to '{maskName}' — no description written.");
            using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.LeshyMaskFlip(maskName));
        }

        // ----------------------------------------------------------------------
        // THE GRIZZLIES.  Part1BossOpponent.GrizzlyGlitchSequence()  (0.7.215.)
        //
        // The Grizzly Bosses challenge (50 points, the most expensive in
        // Kaycee's Mod). At the boss's phase change, INSTEAD of his own phase
        // two, the board and queue are cleared, Leshy's eyes turn red, and
        // Tutorial4BattleSequencer.BearGlitchSequence queues a wall of
        // Grizzlies.
        //
        // The Angler checks for it at his phase two and the Trapper at his,
        // and both yield-break out of their normal phase when it fires — which
        // is why their own phase lines hang on the private methods that only
        // run on the other path, and why this line is the one that must speak
        // when the Grizzlies come instead.
        //
        // The challenge flash already announces the challenge itself
        // (0.7.208), and the board differ reports the wipe. This says the one
        // thing neither can: that what is arriving is not the fight the player
        // was expecting.
        // ----------------------------------------------------------------------
        internal static void AnnounceGrizzlyPhase()
        {
            try
            {
                string who = ActorName();
                Plugin.Log?.LogInfo($"IKMA PROVISIONAL: Grizzly phase, actor='{who}'.");
                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.GrizzlyPhase(who));
                BoardWatcher.NoteGrizzlyWipe();   // 0.7.361
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA BOSS: grizzly — {e.GetType().Name}: {e.Message}");
            }
        }

        // ----------------------------------------------------------------------
        // THE MASK COMES OFF. (0.7.204.)
        //
        // ZAMAR'S WORDING: "[Boss name] fades back into the shadows..."
        //
        // Spoken BEFORE the attribution flips back, so the sentence names the
        // thing that is leaving rather than the thing left behind.
        // ----------------------------------------------------------------------
        internal static void AnnounceBossFades(bool floatsAway)
        {
            // HIS WORDING, 0.7.217, replacing "The Prospector fades back into
            // the shadows...": "just say 'Leshy removes the Prospector's mask.
            // It floats away.' Same with the other masks."
            //
            // The sentence now names the MASK rather than the actor, so it is
            // built from the mask identity and not from ActorName's "The X"
            // form. When no mask is on there is nothing to remove and nothing
            // is said, which is what the old actor test was really asking.
            string mask = _maskIdentity;

            if (!string.IsNullOrEmpty(mask))
                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.LeshyMaskOff(mask, floatsAway));

            NoteMaskOff();
            // 0.7.211 - the identity goes with the mask. LeshyNarrator also
            // clears it, on purpose: this method is reached from two places
            // and neither may leave a departed boss named as the actor.
            SetMaskIdentity(null);
        }

        internal static string BossDisplayName(Opponent opponent)
        {
            if (opponent == null) return null;

            string n;
            try { n = opponent.GetType().Name; }
            catch { return null; }

            if (string.IsNullOrEmpty(n)) return null;

            if (n.EndsWith("BossOpponent"))
                n = n.Substring(0, n.Length - "BossOpponent".Length);
            else if (n.EndsWith("Opponent"))
                n = n.Substring(0, n.Length - "Opponent".Length);

            if (n.Length == 0) return null;

            var sb = new System.Text.StringBuilder();
            foreach (char c in n)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Small counts as words, because "One of the Prospector's 2 candles"
        /// reads as a digit to a screen reader mid-sentence. Anything larger
        /// than the words below falls back to the numeral, which is the right
        /// tradeoff: a wrong-sounding big number is better than a missing one.
        /// </summary>
        private static string NumberWord(int n)
        {
            switch (n)
            {
                case 1: return Vocabulary.Bosses.One;
                case 2: return Vocabulary.Bosses.Two;
                case 3: return Vocabulary.Bosses.Three;
                case 4: return Vocabulary.Bosses.Four;
                case 5: return Vocabulary.Bosses.Five;
                case 6: return Vocabulary.Bosses.Six;
                default: return n.ToString();
            }
        }

        // ----------------------------------------------------------------------
        // THE BOSS PUTS HIS SKULL ON THE TABLE. (0.7.185.)
        //
        // Zamar's wording and his reason, both:
        //
        //   "we need to add an informational '[Boss Name] places a skull
        //    holding [boss health number] lit candles, onto the table.'
        //    This is to visually match the short cutscene that happens, which
        //    tells a sighted player that context of this skull is this bosses
        //    life."
        //
        // WHY THIS ONE MATTERS MORE THAN IT LOOKS. IKMA already announces the
        // boss losing a candle — but until now nothing ever explained what the
        // candles WERE. A sighted player is shown it once, in a cutscene, at
        // the start of the fight; a blind player was hearing about a resource
        // whose existence had never been established. It is the same gap as
        // the player's own candles in Session 14, on the other side of the
        // table.
        //
        // WHERE IT IS HOOKED, AND WHY IT MOVED. (0.7.188.)
        //
        // 0.7.185 hung this on a PREFIX on Part1BossOpponent.IntroSequence.
        // That was wrong, and Zamar caught it in the Prospector fight:
        //
        //   "Visually this was out of order. Visually it is Blows out your
        //    candle > adds smoke to hand > skull > Hee Haw. Change our order
        //    to match the games visuals."
        //
        // A PREFIX ON A COROUTINE FIRES WHEN THE ENUMERATOR IS CREATED, before
        // a single frame of the intro has played — so the line led the whole
        // cutscene instead of sitting inside it. The line was never wrong; it
        // was early. Ordering inside a readout is a causality claim on this
        // project, and this one claimed the skull arrived first.
        //
        // dumps/dump_bossintro.txt settles the real order. Inside
        // <IntroSequence>d__12.MoveNext, in offset order:
        //
        //   IL_004D  Part1BossOpponent.ReducePlayerLivesSequence   <- candle,
        //                                                             then Smoke
        //   IL_00D0  Object.Instantiate                            <- the skull
        //   IL_00DD  stfld Part1BossOpponent.bossSkull
        //   IL_0215  TextDisplayer.PlayDialogueEvent               <- HEE-HAAW
        //
        // Exactly the order he described. So the hook is now BossSkull.Start,
        // which Unity runs on the skull the frame it is instantiated — the
        // game's own "the skull is here" moment, and the same beat at which it
        // plays boss_skull_appear. It fires once per skull, so the count
        // question that OnLightHandCandleKeyframe raised does not arise.
        //
        // The skull carries no back-reference to its boss, so the opponent is
        // asked of the game the same way AnnounceCandleOut asks for the actor.
        //
        // The COUNT is StartingLives, the game's own number, so a boss with a
        // different number of candles reads correctly with no change here.
        // ----------------------------------------------------------------------
        internal static void AnnounceBossSkullPlaced()
        {
            // Part1BossOpponent, not Opponent: every Act 1 boss runs this base,
            // and a skull belonging to anything else is not this line's event.
            Opponent opponent = null;
            try { opponent = TurnManager.Instance?.Opponent as Part1BossOpponent; } catch { }
            if (opponent == null) return;

            // 0.7.204 — the mask is still OFF here. See ActorName.
            string who = ActorName();
            int lives = -1;
            try { lives = opponent.StartingLives; } catch { }

            Plugin.Log?.LogInfo(
                $"IKMA BOSS: intro — skull placed, actor='{who}', StartingLives={lives}.");

            if (lives <= 0) return;

            // THE SKULL DOES NOT ALWAYS ARRIVE WITH ALL ITS CANDLES. (0.7.218.)
            //
            // Zamar, 0.7.217: "Leshy places the skull with two candles on the
            // table as usual, says a line, then adds a third. The extra candle
            // shouldnt be announced until after Leshys line."
            //
            // StartingLives is 3 for Leshy, so this line said "three lit
            // candles" while two were burning — a false count, and it spent the
            // third candle's moment before the game had reached it.
            //
            // LeshyBossOpponent.IntroSequence, in order:
            //     base.IntroSequence(encounter)                 <- skull placed
            //     PlayDialogueEvent("LeshyBossAddCandle")       <- his line
            //     bossSkull.EnterHand()                         <- the third
            //
            // bossSkull.EnterHand() is called from exactly one place in the
            // whole assembly (LeshyBossOpponent.cs:59), so "this boss grows a
            // candle after his intro line" is answerable here without a flag:
            // it is true of Leshy and of nobody else. AnnounceThirdCandle
            // speaks the third, on that method, after the dialogue.
            // THE PIRATE SKULL IS NOT A CANDLE SKULL. (0.7.347.)
            //
            // Zamar, 0.7.346, on "Leshy places a skull holding three lit
            // candles onto the table." in the Pirate fight: "Line is
            // inaccurate." PirateSkullBossOpponent.BossSkullPrefabId is
            // "PirateBossSkull" — a different object on the table, and his
            // line describes what is on it. HIS WORDING, verbatim.
            bool isPirate = false;
            try { isPirate = opponent is PirateSkullBossOpponent; } catch { }
            if (isPirate)
            {
                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.PirateSkullPlaced(who));
                return;
            }

            bool growsOne = false;
            try { growsOne = opponent is LeshyBossOpponent; } catch { }
            int onTable = growsOne ? lives - 1 : lives;

            if (onTable <= 0) return;

            string candleWord = Vocabulary.Bosses.CandleCount(onTable);
            using (Speech.Event(EventKind.Bosses)) Speech.Result(
                Vocabulary.Bosses.PlacesASkullHolding(who, NumberWord(onTable), candleWord));
        }

        // ----------------------------------------------------------------------
        // THE THIRD CANDLE.  BossSkull.EnterHand()  (0.7.218.)
        //
        // Called once in the assembly, by Leshy, after his "PERHAPS... ONE
        // MORE. TO BE SAFE." line. The candle count is the whole stake of the
        // final fight and it was previously folded into the skull line a beat
        // too early and one candle too high.
        // ----------------------------------------------------------------------
        internal static void AnnounceThirdCandle()
        {
            try
            {
                Opponent opponent = null;
                try { opponent = TurnManager.Instance?.Opponent as Part1BossOpponent; } catch { }
                if (opponent == null) return;

                int lives = -1;
                try { lives = opponent.StartingLives; } catch { }


                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.LeshyThirdCandle());
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA BOSS: third candle — {e.GetType().Name}: {e.Message}");
            }
        }

        // ----------------------------------------------------------------------
        // LESHY RELIGHTS THE CANDLE. (0.7.201.)
        //
        // Zamar, after beating the Prospector:
        //
        //   "After the Need a Light? line, we need to call out that Leshy lights
        //    your blown out candle. You now have two candles lit."
        //
        // IT IS NOT THE CANDLE PATCH IKMA ALREADY HAD, and the caller lists say
        // so plainly (dumps/dump_candle.txt):
        //
        //   AddExtraCandleSequence   <- called from <OnTakenToGameTable>d__0
        //                               (the boon item that raises your MAXIMUM)
        //   ReplenishFlamesSequence  <- lights activeFlames up to playerLives,
        //                               playing "candle_light" on each
        //
        // Two different events wearing the same word. "Add an extra candle"
        // raises the ceiling; "replenish the flames" restores what went out.
        // Assuming they were the same call is how the boss-life patch sat on the
        // wrong type for five sessions, so the caller list was read first this
        // time and the existing patch left exactly where it was — it is correct
        // for its own event.
        //
        // THE COUNT IS READ, NOT PREDICTED, and read at SPEAK time: the sequence
        // lights one flame at a time with a wait between each, so a number taken
        // when the enumerator is created describes a table mid-animation.
        //
        // RunState.playerLives is reflected rather than named directly. The dump
        // prints it as a FIELD without visibility, and 0.7.198 already lost a
        // build to exactly that gap on TurnManager.PostBattleSpecialNode.
        // ----------------------------------------------------------------------
        private static System.Reflection.FieldInfo _playerLivesField;
        private static bool _playerLivesResolved;

        private static System.Reflection.FieldInfo _maxLivesField;
        private static bool _maxLivesResolved;

        private static int MaxPlayerLives()
        {
            try
            {
                if (!_maxLivesResolved)
                {
                    _maxLivesResolved = true;
                    _maxLivesField = typeof(RunState).GetField(
                        "maxPlayerLives",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);

                    if (_maxLivesField == null)
                        Plugin.Log?.LogWarning(
                            "IKMA CANDLE: RunState.maxPlayerLives did not resolve — the " +
                            "relight line will not carry a count.");
                }

                if (_maxLivesField == null) return -1;

                var run = RunState.Run;
                if (run == null) return -1;

                object v = _maxLivesField.GetValue(run);
                return v is int ? (int)v : -1;
            }
            catch { return -1; }
        }

        private static int PlayerLives()
        {
            try
            {
                if (!_playerLivesResolved)
                {
                    _playerLivesResolved = true;
                    _playerLivesField = typeof(RunState).GetField(
                        "playerLives",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);

                    if (_playerLivesField == null)
                        Plugin.Log?.LogWarning(
                            "IKMA CANDLE: RunState.playerLives did not resolve — the relight " +
                            "line will not carry a count.");
                }

                if (_playerLivesField == null) return -1;

                var run = RunState.Run;
                if (run == null) return -1;

                object v = _playerLivesField.GetValue(run);
                return v is int ? (int)v : -1;
            }
            catch { return -1; }
        }

        // ----------------------------------------------------------------------
        // THE CANDLE COUNTS, FOR THE A KEY. (0.7.219.)
        //
        // Zamar: "We need to add the candle count to A. Our candle count should
        // come directly after bone count. Then Enemy candle count (if
        // applicable)."
        //
        // Both numbers already had a home here — the player's from
        // RunState.playerLives, the boss's from the game's own Opponent.NumLives
        // — and BoardReader had no business reaching for either by itself. The
        // two accessors below are the only new surface.
        //
        // "If applicable" is the boss half: an ordinary encounter has no
        // candles on the opponent's side at all, and Opponent.NumLives is
        // meaningless for a non-boss. Asking `is Part1BossOpponent` is the same
        // test every other boss line in this file uses.
        // ----------------------------------------------------------------------

        // ----------------------------------------------------------------------
        // WHAT LEADS AN ENEMY BOARD READ IN LESHY'S FIGHT. (0.7.222.)
        //
        // His ask, and the reason it is only HIS fight: Leshy is three bosses
        // taking turns behind one face, and which one is on decides what the
        // next opponent turn does. In every other Act 1 boss fight the mask
        // goes on once in the intro and stays there for the whole battle — a
        // constant, and repeating a constant on every G press is the noise
        // this project spends most of its time removing.
        //
        // So the test is the ENCOUNTER, not the mask: ask whether this is
        // Leshy, then report whatever the face currently is, including nothing.
        // "No mask" is a real answer here and the most actionable one — it
        // means a mask is about to go ON rather than about to act.
        //
        // Returns "" for every other fight and outside combat, so the callers
        // concatenate it unconditionally, the way they already do with the
        // surrender prefix.
        //
        // ORDER: the surrender prefix still leads. Session 15, his rule — the
        // opponent holding its hand out is "the only action on the table that
        // ends the fight", so it outranks a mask. Mask second, board third.
        // ----------------------------------------------------------------------
        internal static string MaskPrefix()
        {
            try
            {
                var leshy = TurnManager.Instance?.Opponent as LeshyBossOpponent;
                if (leshy == null) return "";

                string mask = _maskIdentity;
                return (string.IsNullOrEmpty(mask)
                    ? Vocabulary.LeshyNoMask()
                    : Vocabulary.LeshyWearingMask(mask)) + " ";
            }
            catch { return ""; }
        }

        /// <summary>The player's lit candles, or -1 if it cannot be read.</summary>
        internal static int PlayerCandles() => PlayerLives();

        /// <summary>
        /// The boss's remaining candles, or -1 when the opponent is not a boss
        /// and therefore has none.
        /// </summary>
        internal static int BossCandles()
        {
            try
            {
                var boss = TurnManager.Instance?.Opponent as Part1BossOpponent;
                if (boss == null) return -1;

                // 0.7.352 — THE PIRATE SKULL HAS NO CANDLES ON THE TABLE.
                // Zamar: "The amount of enemy candles is hidden in this Pirate
                // fight. Pull that from the H and anywhere else." His skull
                // prefab carries none, so a sighted player cannot count them.
                if (boss is PirateSkullBossOpponent) return -1;

                return boss.NumLives;
            }
            catch { return -1; }
        }

        internal static void AnnounceCandlesReplenished()
        {
            Plugin.Log?.LogInfo("IKMA CANDLE: flames replenished.");

            using (Speech.Event(EventKind.Bosses)) Speech.Result(() =>
            {
                // 0.7.202 — THE COUNT WAS THE WRONG FIELD, AND THE IL SAYS WHY.
                //
                // 0.7.201 read playerLives and spoke "one candle lit" when he
                // had two. Part1BossOpponent.BossDefeatedSequence, in order
                // (dumps/dump_candle.txt):
                //
                //   IL_0129  PlayDialogueEvent("ReplenishLives")   <- NEED A LIGHT?
                //   IL_0192  CandleHolder.ReplenishFlamesSequence  <- the relight
                //   IL_01BB  RunState.playerLives = maxPlayerLives <- the count,
                //                                                    set AFTER
                //
                // playerLives is still the POST-DEFEAT value while the flames
                // are being lit. maxPlayerLives is the number the game is
                // restoring to, read from the same field the game reads one line
                // later — so this is the game's own answer, not a prediction of
                // it.
                int lit = MaxPlayerLives();
                if (lit <= 0) return null;

                string count = NumberWord(lit);
                string word  = Vocabulary.Bosses.CandleCount(lit);

                // ZAMAR'S WORDING, 2026-09-13, verbatim:
                //   "Leshy relights your candle. You now have two candles lit."
                return Vocabulary.Bosses.LeshyRelightsYourCandle(count, word);
            });
        }

        // ----------------------------------------------------------------------
        // A candle goes out — the player has lost a life.
        //
        // Not deferred, and not composed late. The count arrives as the game's
        // OWN parameter, named livesRemaining in the assembly, so this is
        // reading the game's answer rather than inferring one from state that
        // may still be settling. Logged as well as spoken so the number can be
        // checked against the screen the first time it happens.
        // ----------------------------------------------------------------------
        internal static void AnnounceCandleOut(int livesRemaining)
        {
            // OPEN THE GRANT WINDOW. (0.7.218.) The game hands the player The
            // Smoke a beat after this; that card is a consequence of the candle
            // and must be announced as its own event rather than folded into
            // the next battle's opening-hand read. See AnnounceCardSpawnedToHand.
            _grantWindowUntil = Now() + GRANT_WINDOW_SECONDS;

            // ZAMAR'S WORDING, Session 19, arrived at over three exchanges.
            // The final form, his:
            //
            //   "Leshy blows out a candle. You have [x] remaining."
            //   "Leshy blows out your last candle. The room goes dark..."
            //
            // THREE THINGS THIS FIXES over "A candle goes out. 1 candle left."
            //
            // 1. SOMEONE DOES IT. A candle going out on its own is a weaker
            //    account of the same event than a person blowing it out, and
            //    the game is a person doing things to you.
            // 2. THE COUNT IS BACK, and phrased as the player's ("You have 1
            //    remaining") rather than the board's ("1 candle left"). He had
            //    dropped it in the previous round and asked for it back in this
            //    one — it is the number a sighted player reads at a glance.
            // 3. THE LAST ONE IS ITS OWN SENTENCE. Running out is the end of
            //    the run, not a smaller version of losing one.
            //
            // WHO IS NAMED IS READ FROM THE GAME, not typed into the literal.
            // He corrected an earlier draft with "It should be 'The Prospector'
            // not 'Leshy' for these lines" — in a boss fight the boss is the
            // one doing it — and then asked for ordinary encounters to read the
            // same way, where Leshy is. So the actor is the boss when there is
            // one and Leshy when there is not, which is the same rule read off
            // the opponent rather than two hard-coded lines.
            //
            // NOTE THIS IS THE PLAYER'S CANDLE, NOT THE BOSS'S. The boss losing
            // its last life is deliberately SILENT — see AnnounceBossLifeLost.
            // Two different candles, opposite decisions, both his: the boss
            // dying has the game's own drama to listen to, and the player
            // running out has none.
            Plugin.Log?.LogInfo($"IKMA CANDLE: blown out, game reports livesRemaining={livesRemaining}.");

            // 0.7.360 — ALWAYS LESHY. Zamar: "Leshy always blows out the
            // candles and the hands are his too." This used ActorName (0.7.204),
            // which named the boss while its mask was on.
            string who = Vocabulary.Leshy;

            if (livesRemaining <= 0)
            {
                using (Speech.Event(EventKind.Bosses)) Speech.Result(
                    Vocabulary.Bosses.BlowsOutYourLast(who));
                return;
            }

            using (Speech.Event(EventKind.Bosses)) Speech.Result(
                // HIS WORDING, 0.7.220: "should be rephrased to 'one of your
                // candles.'" — "a candle" left it ambiguous whose went out, in
                // a fight where both sides have them on the table.
                Vocabulary.Bosses.BlowsOutOneOf(who, livesRemaining));
        }

        // ----------------------------------------------------------------------
        // A CARD ARRIVES IN THE PLAYER'S HAND. (0.7.164.)
        //
        // CardSpawner.SpawnCardToHand was ENTIRELY UNPATCHED until now, and two
        // separate confirmed callers run through it:
        //
        //   Part1BossOpponent.ReducePlayerLivesSequence  the Smoke, handed over
        //                                                after a candle is out
        //   PackMule.SpawnAndOpenPack                    four or five cards at
        //                                                once when a mule dies
        //
        // Session 18 patched DrawCreatedCard.CreateDrawnCard, which is the
        // SIGIL route into the hand. This is a second route and nothing had
        // ever been written for it, so both of the above happened in silence.
        //
        // WHY THIS ANSWERS THE SMOKE QUESTION PROPERLY. Zamar asked for "The
        // Smoke is added to your hand". dump_findstring.txt shows the game
        // choosing between "Smoke", "Smoke_Improved" and "Smoke_NoBones" by run
        // progression, so a fixed line naming one of them would be wrong on the
        // other two runs. The CardInfo arrives as the method's OWN PARAMETER,
        // so this names whichever card the game actually handed over — and it
        // reads correctly for the mule's pack and anything else on this route
        // without either being special-cased.
        //
        // KNOWN CONSEQUENCE, FLAGGED RATHER THAN DESIGNED AROUND: a Pack Mule
        // dying spawns four or five cards through here, so it will produce four
        // or five of these lines in a row. Whether that wants batching into one
        // line is Zamar's call and he has not made it yet.
        //
        // Deferred: the prefix fires at enumerator creation, before the card is
        // actually in hand.
        // ----------------------------------------------------------------------
        internal static void AnnounceCardSpawnedToHand(CardInfo info)
        {
            if (info == null) return;

            string name = null;
            try { name = CardReader.CardName(info); } catch { }

            Plugin.Log?.LogInfo($"IKMA HAND: card spawned to hand — '{name ?? "?"}'.");

            // Session 47 (0.7.455): see DisguisedIjiraqName.
            try { if (info.name == "Ijiraq") { _ijiraqSpawnedAt = Now(); _ijiraqSpawnName = name; } } catch { }

            if (string.IsNullOrEmpty(name)) return;

            // DEALT, NOT GIVEN. (0.7.194.)
            //
            // Zamar, after 0.7.193 collapsed the opening deal into one line:
            //
            //   "Drawn cards should never be 'added to hand' like this."
            //
            // Batching it was the wrong fix to the wrong half. The opening hand
            // is not an EVENT that happens to the player — it is the starting
            // position, and IKMA already reads it as one: the first PlayerTurn
            // of an encounter enqueues BoardReader.GetHandNamesText alongside
            // the enemy queue and the player's board, in the order he asked for.
            // The arrival lines were a second, worse telling of something
            // already told properly a moment later.
            //
            // THE GATE IS THE ENCOUNTER, NOT A TIMER OR A TURN COUNT.
            // TurnManager builds a fresh Opponent per battle, so "have we
            // reached the first player turn of THIS battle" is answerable by
            // reference identity — the same trick TurnManager_PlayerTurn_Patch
            // already uses to decide when to read the opening queue. Nothing to
            // reset between battles, and nothing to guess.
            //
            // A card arriving mid-combat — a Pack Mule's prize, a Cuckoo's egg,
            // a Squirrel drawn on D — is a real event and still announced.
            // A CARD GIVEN FOR LOSING A CANDLE IS NOT PART OF A DEAL. (0.7.218.)
            //
            // Zamar, 0.7.217, on The Smoke: "This line should have been read
            // before the placing skull line. I never heard it."
            //
            // He is right, and the gate below was doing its job — The Smoke
            // arrives between battles, so ArmedForCurrentBattle is false and
            // the opening-hand read "covers" it. But The Smoke is not part of
            // an opening hand: it is what the game hands you FOR losing a life,
            // one line after the candle goes out, and hearing it thirty seconds
            // later inside a five-card list is not hearing it.
            //
            // AnnounceCandleOut opens a short window here rather than this
            // method trying to recognise the card, because the cause is what
            // makes it an event and the cause is known at the candle, not at
            // the hand.
            if (_grantWindowUntil > 0f && Now() < _grantWindowUntil)
            {
                Plugin.Log?.LogInfo(
                    $"IKMA HAND: '{name}' arrived just after a candle went out — " +
                    "announced as its own event, not folded into the hand read.");
            }
            else if (!ArmedForCurrentBattle())
            {
                Plugin.Log?.LogInfo(
                    $"IKMA HAND: '{name}' arrived before the first player turn — " +
                    "the opening hand read covers it.");
                return;
            }

            // A DRAW IS NOT AN ARRIVAL. (0.7.195.)
            //
            // Zamar:
            //   "IKMA SPEAK (interrupt): Drew Squirrel."
            //   "IKMA SPEAK: Squirrel is added to your hand."
            //   "Why is it announcing this added to hand thing still? Crush this
            //    bug please."
            //
            // Pressing D is a player ACTION and "Drew Squirrel." is its answer.
            // The card then arrives through CardSpawner.SpawnCardToHand like
            // anything else, and this file announced the same card a second
            // time under a different sentence.
            //
            // NO TIMER AND NO WINDOW. HotkeyManager.DrawInFlight is true from
            // the moment the pile is clicked until the drawn card has been found
            // and spoken — IKMA's own record of "a draw is happening right now",
            // which is exactly the question being asked. The 0.7.193 suppression
            // window below stays for the created-card case, where there is no
            // such flag to read.
            if (HotkeyManager.DrawInFlight)
            {
                Plugin.Log?.LogInfo(
                    $"IKMA HAND: '{name}' arrived from a draw — \"Drew {name}.\" covers it.");
                return;
            }

            // A PHASE OR A SCREEN IS SPEAKING FOR ITS OWN CARDS. (0.7.312.)
            // Asked HERE, where the line is composed, because the ordering
            // between the two arrival paths is not guaranteed — see
            // HandArrivalGate.
            if (HandArrivalGate.Closed(name)) return;

            // ALREADY SPOKEN BY A RICHER LINE. (0.7.193.) See SuppressNextHandArrival.
            if (_suppressUntil > 0f && Now() < _suppressUntil)
            {
                // SPENT ON THE FIRST ARRIVAL. (0.7.252.) One richer line stands
                // in for one card, not for everything that lands in the next
                // three seconds.
                _suppressUntil = -99f;
                Plugin.Log?.LogInfo(
                    $"IKMA HAND: '{name}' suppressed here — EventNarrator is naming its cause.");
                return;
            }

            // BATCHED, IF SOMETHING OPENED A WHOLE PACK. See BeginHandBatch.
            if (_batchingToHand)
            {
                _batchedToHand.Add(name);
                HandFollowUps.Reserve(name);   // 0.7.341
                return;
            }

            // ---------------------------------------------------------------
            // ONE LINE PER DEAL, NOT ONE PER CARD. (0.7.193.)
            //
            // Zamar, opening hand of the Prospector fight:
            //
            //   "Squirrel is added to your hand."
            //   "Wolf is added to your hand."
            //   "Caged Wolf is added to your hand."
            //   "Cuckoo is added to your hand."
            //   "Why did it individually say all those cards were added to my
            //    hand? Same bug we've hit before with this."
            //
            // He is right that it is the same bug. 0.7.165 fixed it for the Pack
            // Mule by having PackMule.OnDie call BeginHandBatch, and that fix
            // only ever covered the one caller that asked for it. The opening
            // deal never called it, so four cards were four sentences.
            //
            // THE FIX IS TO STOP REQUIRING A CALLER TO ASK. Every arrival now
            // gathers for a fraction of a second; whatever arrived in that
            // window becomes one line. One card gives the singular sentence he
            // already approved, so nothing changes for an ordinary draw.
            //
            // THE DELAY IS FREE IN PRACTICE. The announcer already drips at
            // ANNOUNCE_INTERVAL between queued lines, so a gather shorter than
            // that costs nothing a player can hear — the line was never going to
            // be spoken sooner than that anyway.
            //
            // GENERALISE, because this is the third time: a batching mechanism
            // that must be switched on by each caller will be missed by the next
            // caller. Make the batching the default and let the rare caller that
            // needs a longer window ask for one (BeginHandBatch still does).
            // ---------------------------------------------------------------
            _gathering.Add(name);
            HandFollowUps.Reserve(name);       // 0.7.341
            _gatherUntil = Now() + HAND_GATHER_SECONDS;
        }

        // ----------------------------------------------------------------------
        // THE AUTOMATIC GATHER. (0.7.193.)
        // ----------------------------------------------------------------------
        private const float HAND_GATHER_SECONDS = 0.35f;

        private static readonly System.Collections.Generic.List<string> _gathering =
            new System.Collections.Generic.List<string>();
        private static float _gatherUntil = -99f;

        private static float _suppressUntil = -99f;

        // ----------------------------------------------------------------------
        // THE GROUP LINE IS THE ONLY LINE. (0.7.348.)
        //
        // Zamar, Pirate Skull phase 2, four rodents from the pack:
        //
        //   Rabbit is created in your hand.       <- EventNarrator, per card
        //   Squirrel is created in your hand.
        //   Opossum is created in your hand.
        //   Mole is created in your hand.
        //   Rabbit, Squirrel, Opossum and Mole are added to your hand.
        //
        // "only that last line should happen, not all the individual ones.
        //  This is true for every situation like this, including the Mule."
        //
        // The two routes into the hand again (see SuppressNextHandArrival):
        // this file gathers on CardSpawner.SpawnCardToHand, EventNarrator
        // speaks on PlayerHand.AddCardToHand, and the spawn calls the add. A
        // card this gather is holding, or held a moment ago, already has its
        // line — so EventNarrator's cause-less line stands down for it. A card
        // with a CAUSE is untouched: that line already makes this one stand
        // down, the other way round.
        // ----------------------------------------------------------------------
        private static readonly System.Collections.Generic.List<string> _recentlyGathered =
            new System.Collections.Generic.List<string>();
        private static float _recentlyGatheredAt = -99f;
        private const float GATHER_CLAIM_SECONDS = 3f;

        internal static bool GatherHolds(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (_gathering.Contains(name)) return true;
            if (Now() - _recentlyGatheredAt > GATHER_CLAIM_SECONDS) return false;
            int i = _recentlyGathered.IndexOf(name);
            if (i < 0) return false;
            _recentlyGathered.RemoveAt(i);
            return true;
        }

        /// <summary>
        /// A card arriving inside this window was GIVEN, not dealt — see
        /// AnnounceCardSpawnedToHand. Opened by AnnounceCandleOut. (0.7.218.)
        /// </summary>
        private static float _grantWindowUntil = -99f;
        private const float GRANT_WINDOW_SECONDS = 4f;

        // The Opponent whose battle has reached its first player turn. Held as
        // object so nothing is assumed about the concrete Opponent type.
        private static object _armedForOpponent;

        /// <summary>
        /// Called from TurnManager_PlayerTurn_Patch once the opening reads for
        /// this battle have been queued.
        /// </summary>
        internal static void ArmHandAnnouncements(object opponent)
        {
            _armedForOpponent = opponent;
        }

        private static bool ArmedForCurrentBattle()
        {
            try
            {
                var tm = TurnManager.Instance;
                object now = tm != null ? (object)tm.Opponent : null;
                if (now == null) return true;   // not in a battle: say it.
                return ReferenceEquals(now, _armedForOpponent);
            }
            catch { return true; }
        }

        // This file has no `using UnityEngine` — see ikma-perf-and-regressions.
        private static float Now()
        {
            try { return UnityEngine.Time.unscaledTime; } catch { return 0f; }
        }

        /// <summary>
        /// Swallow the next plain "X is added to your hand" line, because
        /// something that knows WHY the card arrived is about to say so.
        /// </summary>
        /// <remarks>
        /// 0.7.193. Zamar's log, one Rabbit, two sentences:
        ///
        ///   "Rabbit is added to your hand."
        ///   "Rabbit is created in your hand."
        ///
        /// TWO ROUTES INTO THE HAND AGAIN, and EventNarrator's own comment
        /// already names the shape: CardSpawner.SpawnCardToHand is what this
        /// file listens on, PlayerHand.AddCardToHand is what EventNarrator
        /// listens on, and one card trips both. The pack-mule case was fixed by
        /// EventNarrator standing down; this is the same fix pointing the other
        /// way, because here EventNarrator has the better line — it can name the
        /// sigil that made the card.
        ///
        /// Suppressed at ARRIVAL rather than at speak time, for the reason
        /// EventNarrator gives for doing the same: the window is open now and
        /// will have closed by the time a deferred line composes.
        /// </remarks>
        internal static void SuppressNextHandArrival()
        {
            // ITS OWN WINDOW, AND A WIDE ONE. (0.7.252.)
            //
            // This borrowed HAND_GATHER_SECONDS (0.35s), which is how long to
            // wait for a DEAL to finish — a different question, and too short
            // for this one. BeesOnHit waits 0.4s between the prefix that calls
            // this and the card actually spawning, so the suppression had always
            // expired by the time the Bee arrived and Zamar heard both lines.
            // See EventNarrator.CurrentCause for the full timing.
            //
            // Safe to widen because it is now spent by the first arrival.
            _suppressUntil = Now() + SUPPRESS_WINDOW_SECONDS;
        }

        private const float SUPPRESS_WINDOW_SECONDS = 3f;

        // Session 47 (0.7.455). An Ijiraq spawned straight into the hand is
        // named here before the game disguises it, so the line said "Ijiraq
        // is added to your hand." and the hand then held a Pack Rat - the
        // line told the player what the card really was. By the time the
        // gathered line is spoken the disguise is on; the name is read off
        // the card in the hand then, and it is the disguise's plain name
        // (0.7.456: no mark is shown in the hand, so no "Unusual").
        private static float _ijiraqSpawnedAt = -99f;
        private static string _ijiraqSpawnName;

        private static string DisguisedIjiraqName(string gathered)
        {
            if (_ijiraqSpawnName == null || gathered != _ijiraqSpawnName) return gathered;
            if (Now() - _ijiraqSpawnedAt > 5f) return gathered;

            try
            {
                var hand = Singleton<PlayerHand>.Instance;
                if (hand?.CardsInHand == null) return gathered;
                for (int i = hand.CardsInHand.Count - 1; i >= 0; i--)
                    if (CardReader.IsDisguisedIjiraq(hand.CardsInHand[i]))
                        return CardReader.CardName(hand.CardsInHand[i]);
            }
            catch { }
            return gathered;
        }

        /// <summary>Driven from HotkeyManager.Update, beside the other tickers.</summary>
        internal static void Tick()
        {
            if (_gathering.Count == 0) return;
            if (Now() < _gatherUntil) return;

            for (int gi = 0; gi < _gathering.Count; gi++)
                _gathering[gi] = DisguisedIjiraqName(_gathering[gi]);

            string line;
            if (_gathering.Count == 1)
            {
                line = Vocabulary.Bosses.IsAddedToYour(_gathering[0]);
            }
            else
            {
                string last = _gathering[_gathering.Count - 1];
                var rest = _gathering.GetRange(0, _gathering.Count - 1);
                line = Vocabulary.Bosses.AndAreAddedTo(rest.ToArray(), last);
            }

            // 0.7.341 — what the woodcarving did to these cards, after them.
            var gatheredNames = new List<string>(_gathering);
            _recentlyGathered.Clear();
            _recentlyGathered.AddRange(_gathering);
            _recentlyGatheredAt = Now();
            _gathering.Clear();
            _gatherUntil = -99f;

            using (Speech.Event(EventKind.Bosses)) Speech.Commentary(() =>
            {
                string after = "";
                foreach (var n in gatheredNames) after += HandFollowUps.Take(n);
                return line + after;
            });
        }

        // ----------------------------------------------------------------------
        // ONE LINE FOR A WHOLE PACK. (0.7.165.)
        //
        // Zamar, Session 19, after being told a dying Pack Mule would spawn its
        // cards one at a time: "Make the pack mule into one row."
        //
        // Same judgment he made about the pickaxe board wipe an hour earlier —
        // one event the player experiences as a single thing should be one
        // sentence, not one sentence per internal step. Worth generalising: the
        // count of announcements should match the count of EVENTS, not the
        // count of method calls.
        //
        // HOW IT WORKS. PackMule.OnDie opens a window; every card that arrives
        // through SpawnCardToHand while it is open is collected instead of
        // spoken; the flush composes one line from whatever was collected. The
        // window closes in the flush itself, so a burst that spawns nothing
        // still ends cleanly.
        //
        // WHY A TIMED WINDOW RATHER THAN A CLEANER HOOK. SpawnAndOpenPack
        // tweens the pack, plays an "open" animation and switches the camera
        // per card, and it exposes no completion event a patch could hang on.
        // The window has to outlast that sequence, which is why it is seconds
        // rather than frames. If a future dump finds a real end-of-pack hook,
        // it should replace this.
        //
        // THE FAILURE MODE, STATED SO IT IS RECOGNISED IF IT HAPPENS: a card
        // arriving from some UNRELATED source while the window is open would be
        // swept into the pack line and attributed to the mule. Nothing else is
        // known to spawn to hand during a mule's death, so this is accepted
        // rather than solved — but a pack line naming a card that was not in
        // the pack is the symptom to look for.
        // ----------------------------------------------------------------------
        private static bool _batchingToHand;
        private static readonly List<string> _batchedToHand = new List<string>();

        /// <summary>
        /// True while a pack is being collected into one line. EventNarrator
        /// checks this so its own per-card "created in your hand" line does not
        /// duplicate the batch — see there for why that is a separate path.
        /// </summary>
        internal static bool HandBatchOpen => _batchingToHand;

        internal static void BeginHandBatch(float windowSeconds)
        {
            _batchingToHand = true;
            _batchedToHand.Clear();

            using (Speech.Event(EventKind.Bosses)) Speech.Result(() =>
            {
                _batchingToHand = false;

                if (_batchedToHand.Count == 0)
                {
                    Plugin.Log?.LogInfo("IKMA HAND: batch window closed with nothing in it.");
                    return null;
                }

                var names = new List<string>(_batchedToHand);
                _batchedToHand.Clear();

                Plugin.Log?.LogInfo(
                    $"IKMA HAND: batch of {names.Count} — {string.Join(", ", names.ToArray())}.");

                // "X is added to your hand" is his approved shape; this is that
                // sentence in the plural, with nothing else added to it.
                // 0.7.341 — what the woodcarving did to these cards, after
                // the cards. Zamar: "card names are added to your hand should
                // have played before Mealworm (from that pack) gains Fecundity."
                string after = "";
                foreach (var n in names) after += HandFollowUps.Take(n);

                if (names.Count == 1)
                    return Vocabulary.Bosses.IsAddedToYourHand(names[0], after);

                string last = names[names.Count - 1];
                names.RemoveAt(names.Count - 1);
                return Vocabulary.Bosses.AndAreAddedToYour(names.ToArray(), last, after);
            }, windowSeconds);
        }

        internal static void AnnounceCandleLit()
        {
            Plugin.Log?.LogInfo("IKMA CANDLE: extra candle sequence started.");
            using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.Bosses.ExtraCandleIsLit);
        }

        // ----------------------------------------------------------------------
        // Leshy changing mask. LOGGED, NOT SPOKEN — deliberately.
        //
        // The mask change and the life loss almost certainly happen together,
        // since the mask IS the phase. Speaking both would be a duplicate, and
        // a duplicate line is a bug by this project's standard. Whether they
        // actually coincide, and in what order, is not something the source can
        // answer — so this writes what it WOULD have said and the next run's log
        // settles it. Same method as the board diff watcher's unspoken half.
        //
        // The mask is not NAMED even in the log line's spoken form. maskBossTypes
        // and currentMaskIndex are both NONPUBLIC and it is not established
        // whether the index has advanced by the time this fires; naming the
        // wrong boss would be worse than naming none. The index is logged so
        // that question can be answered too.
        // ----------------------------------------------------------------------
        private static FieldInfo _maskIndexField;
        private static bool _maskIndexResolved;

        internal static void NoteMaskAdvance(LeshyBossOpponent leshy)
        {
            int index = -1;
            try
            {
                if (!_maskIndexResolved)
                {
                    _maskIndexResolved = true;
                    _maskIndexField = typeof(LeshyBossOpponent).GetField(
                        "currentMaskIndex", BindingFlags.Instance | BindingFlags.NonPublic);
                }

                if (_maskIndexField != null && leshy != null)
                    index = (int)_maskIndexField.GetValue(leshy);
            }
            catch { index = -1; }

            Plugin.Log?.LogInfo(
                $"IKMA BOSS (not spoken): Leshy advances mask state. currentMaskIndex={index}.");
        }
    }

    // -------------------------------------------------------------------------
    // Patch classes. Registered through Plugin.TryPatch, never PatchAll.
    // -------------------------------------------------------------------------

    // ALL FOUR ACT 1 BOSSES, from this one declaration. (0.7.137.)
    //
    // This replaces a patch on Opponent.LifeLostSequence and a second one on
    // LeshyBossOpponent.LifeLostSequence. Both are gone. See Plugin.cs at the
    // registration for the IL evidence; the short version:
    //
    //   Part1BossOpponent overrides LifeLostSequence and never calls base, so
    //   the old Opponent patch could not fire for a boss. Leshy overrides it
    //   too but DOES call base, through a <>n__1 thunk — so he arrives here
    //   without a patch of his own, and giving him one again would speak his
    //   line twice.
    //
    // Prospector, Angler and Trapper/Trader run this declaration directly.
    public class Part1BossOpponent_LifeLostSequence_Patch
    {
        static void Prefix(Opponent __instance) => BossNarrator.AnnounceBossLifeLost(__instance);
    }

    // The player losing a life. The single most important announcement in
    // Kaycee's Mod, and it has never existed.
    public class CandleHolder_BlowOutCandle_Patch
    {
        static void Prefix(int livesRemaining) => BossNarrator.AnnounceCandleOut(livesRemaining);
    }

    // Every overload of SpawnCardToHand. There are three, and which one each
    // caller uses is not visible in the IL dump — but all three take the card
    // as a first parameter named "info", which is what Harmony binds a prefix
    // argument by. Registered by enumerating the overloads rather than naming
    // signatures, so the List<> generic argument never has to be guessed.
    public class CardSpawner_SpawnCardToHand_Patch
    {
        static void Prefix(CardInfo info) => BossNarrator.AnnounceCardSpawnedToHand(info);
    }

    // The boss's skull arriving on the table. POSTFIX on BossSkull.Start, which
    // Unity runs the frame the skull is instantiated — after the candle and the
    // Smoke, before the opening dialogue. See the note above for why this is no
    // longer a prefix on IntroSequence.
    public class BossSkull_Start_Patch
    {
        static void Postfix() => BossNarrator.AnnounceBossSkullPlaced();
    }

    public class CandleHolder_AddExtraCandleSequence_Patch
    {
        static void Prefix() => BossNarrator.AnnounceCandleLit();
    }

    // The flames coming back after a boss. A DIFFERENT event from the extra
    // candle above — see AnnounceCandlesReplenished for the caller evidence.
    // Opens the post-boss latch. A prefix on a coroutine fires at enumerator
    // creation, which is exactly when the script begins — and the node the map
    // gate really wants is not set until that script's last line.
    public class Part1BossOpponent_BossDefeatedSequence_Patch
    {
        static void Prefix()
        {
            Plugin.Log?.LogInfo("IKMA BOSS: defeated sequence started — holding the map reader.");
            HotkeyManager.PostBossScriptRunning = true;

            // 0.7.204 — the boss leaving, before the candle is relit. Enqueued
            // here rather than on the mask animation because this coroutine IS
            // the post-boss script, and the relight it queues three lines later
            // lands behind this one without either needing a timer.
            //
            // floatsAway: FALSE. 0.7.263, Zamar: a defeated boss's mask simply
            // comes off — there is no orbiter here for it to float back to, and
            // the clause was describing something not on his screen. The float
            // belongs to Leshy's own fight, which passes true from
            // LeshyNarrator.OnMaskOff.
            BossNarrator.AnnounceBossFades(floatsAway: false);
        }
    }

    // The mask going on. This is the moment Leshy becomes the boss, and every
    // line before it says Leshy.
    public class LeshyAnimationController_PutOnMask_Patch
    {
        // The mask is a PARAMETER of PutOnMask(Mask mask, Boolean quick), so
        // Harmony hands it over by name and nothing has to be inferred from
        // the opponent — which was the old way and could not answer at a map
        // node. (0.7.213.)
        static void Prefix(LeshyAnimationController.Mask mask)
            => BossNarrator.AnnounceMaskPutOn(mask.ToString());
    }

    // The mask coming off, at a map node. Silent; it only clears the actor so
    // a node's mask cannot rename the next boss. (0.7.213.)
    // The third candle Leshy adds after his intro line. BossSkull.EnterHand is
    // called from exactly one place in the assembly (LeshyBossOpponent.cs:59).
    public class BossSkull_EnterHand_Patch
    {
        public static void Prefix() => BossNarrator.AnnounceThirdCandle();
    }

    public class LeshyAnimationController_TakeOffMask_Patch
    {
        static void Prefix() => BossNarrator.NoteMaskRemoved();
    }

    // The Grizzly Bosses challenge replacing a boss's phase two. Patched on
    // Part1BossOpponent, which declares it — all four Act 1 bosses share it.
    public class Part1BossOpponent_GrizzlyGlitchSequence_Patch
    {
        public static void Prefix() => BossNarrator.AnnounceGrizzlyPhase();
    }

    public class CandleHolder_ReplenishFlamesSequence_Patch
    {
        static void Prefix() => BossNarrator.AnnounceCandlesReplenished();
    }

    public class LeshyBossOpponent_AdvanceMaskState_Patch
    {
        static void Prefix(LeshyBossOpponent __instance) => BossNarrator.NoteMaskAdvance(__instance);
    }
}
