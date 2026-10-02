// LeshyNarrator.cs
using System.Collections.Generic;
using System.Reflection;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// The final boss. Leshy's three masks, his two phase changes, and the
    /// moon — plus the two boss behaviours the masks borrow, the Angler's
    /// hook and the Trader's trade.
    /// </summary>
    /// <remarks>
    /// 0.7.211, Session 21. Written from the decompiled
    /// <c>LeshyBossOpponent</c>, <c>LeshyBattleSequencer</c>,
    /// <c>LeshyMaskOrbiter</c>, <c>FishHookGrab</c>, <c>PickAxeSlam</c> and
    /// <c>TradeCardsForPelts</c>; the step-by-step script is
    /// <c>docs/BOSS_SCRIPTS_KM.md</c>. Every reflected member is listed in
    /// <c>dumps/dump_leshy_from_decompile.txt</c>.
    ///
    /// WHY THIS IS ITS OWN FILE AND NOT MORE OF BossNarrator. BossNarrator
    /// answers ONE question for every Act 1 boss — who is acting, and what
    /// happened to the candles. Leshy's fight is a different shape: he is
    /// three bosses in sequence inside one encounter, and the thing that
    /// decides the actor is a private index on his own class. Keeping that
    /// here leaves BossNarrator's rule ("the mask is the actor, never the
    /// opponent object") intact and gives it its Leshy answer through one
    /// method, <see cref="BossNarrator.SetMaskIdentity"/>.
    ///
    /// THE MASK EVENT IS NOT PutOnMask. Every other Act 1 boss arrives
    /// through <c>LeshyAnimationController.PutOnMask</c>, which is what
    /// BossNarrator.AnnounceMaskPutOn hangs on. Leshy's own fight never calls
    /// it: <c>SwitchToMask</c> detaches a mask from the orbiter and calls
    /// <c>ParentObjectToFace</c>, which sets no state and raises no event, so
    /// <c>LeshyAnimationController.CurrentMask</c> stays null for the whole
    /// battle. Anything waiting on PutOnMask here waits forever. That is why
    /// the hooks below are on Leshy's own private methods.
    ///
    /// THE COROUTINE-PREFIX RULE, AND WHY EACH HOOK IS STILL SAFE. Prefixes
    /// fire when the enumerator is CREATED, not when it finishes, so a prefix
    /// may only read state that is already settled:
    ///   SwitchToMask(index)  — the index is an ARGUMENT. Settled by
    ///                          definition.
    ///   CleanUpCurrentMask() — reads currentMask/currentMaskIndex, both set
    ///                          on a previous turn by SwitchToMask.
    ///   StartNewPhaseSequence() — reads NumLives, which the life-loss
    ///                          sequence has already decremented (it is what
    ///                          selects the phase inside the method).
    ///   FishHookGrab.PullHook() — reads hookTargetSlot, aimed a turn ago.
    /// The one hook that could NOT satisfy it, GiantCard.OnResolveOnBoard,
    /// moved to GiantCardNarrator.cs and composes its line deferred instead.
    ///
    /// WORDING IS PROVISIONAL. Every new sentence here is in Vocabulary.cs
    /// under the Leshy heading and every use logs "IKMA PROVISIONAL", so the
    /// whole set can be pulled out of one playtest log and replaced with
    /// Zamar's words. The two lines that are NOT provisional are his
    /// already: the mask-off line reuses AnnounceBossFades, and the skull
    /// line reuses AnnounceBossSkullPlaced, which reads StartingLives and so
    /// says "three lit candles" for Leshy with no change.
    ///
    /// WHAT IS DELIBERATELY NOT HERE. The stumps and trees of phase 2 get no
    /// line of their own: they are created with CreateCardInSlot and the
    /// board differ reports them at the next turn boundary, in the same
    /// words as any other card arriving. One event is one line.
    /// </remarks>
    public static class LeshyNarrator
    {
        // ------------------------------------------------------------------
        // Reflection handles. Resolved once, each guarded, each listed in
        // dumps/dump_leshy_from_decompile.txt.
        // ------------------------------------------------------------------
        private static bool      _resolved;
        private static FieldInfo _maskIndexField;
        private static FieldInfo _maskTypesField;
        private static FieldInfo _currentMaskField;
        private static FieldInfo _hookSlotField;
        private static FieldInfo _hookCardField;

        private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            try
            {
                _maskIndexField   = typeof(LeshyBossOpponent).GetField("currentMaskIndex", Priv);
                _maskTypesField   = typeof(LeshyBossOpponent).GetField("maskBossTypes",    Priv);
                _currentMaskField = typeof(LeshyBossOpponent).GetField("currentMask",      Priv);
                _hookSlotField    = typeof(FishHookGrab).GetField("hookTargetSlot",        Priv);
                _hookCardField    = typeof(FishHookGrab).GetField("hookTargetCard",        Priv);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LESHY: reflection setup failed — {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // WHICH MASK. The game's own answer: maskBossTypes[currentMaskIndex].
        //
        // The list is built in LeshyBossOpponent's field initialiser as
        // Prospector, Angler, Trapper/Trader — but it is READ here rather
        // than assumed, because a list that is reordered in a patch would
        // otherwise make every actor name in the fight a lie, silently.
        // ------------------------------------------------------------------
        private static string MaskNameAt(LeshyBossOpponent leshy, int index)
        {
            Resolve();
            if (leshy == null || index < 0) return null;

            try
            {
                var types = _maskTypesField?.GetValue(leshy) as System.Collections.IList;
                if (types == null || index >= types.Count) return null;

                string raw = types[index]?.ToString();
                if (string.IsNullOrEmpty(raw)) return null;

                // Opponent.Type names, mapped to the word the mask is called
                // by. "ProspectorBoss" is an internal id; it is never spoken
                // as one.
                switch (raw)
                {
                    case "ProspectorBoss":    return Vocabulary.Prospector;
                    case "AnglerBoss":        return Vocabulary.Angler;
                    case "TrapperTraderBoss": return Vocabulary.Trader;
                    default:
                        Plugin.Log?.LogInfo($"IKMA LESHY: mask type '{raw}' has no spoken name — not named.");
                        return null;
                }
            }
            catch { return null; }
        }

        private static int CurrentIndex(LeshyBossOpponent leshy)
        {
            Resolve();
            try
            {
                if (_maskIndexField == null || leshy == null) return -1;
                return (int)_maskIndexField.GetValue(leshy);
            }
            catch { return -1; }
        }

        // ------------------------------------------------------------------
        // THE MASK GOES ON.  LeshyBossOpponent.SwitchToMask(int index)
        //
        // The index arrives as an argument, so this is the one mask event
        // that can be named with no settle at all.
        //
        // The sentence is two facts and nothing invented: which mask, and
        // that it is now on his face. Zamar wrote a full visual description
        // for the Prospector's mask (BossNarrator._maskDescriptions) and it
        // is reused verbatim when that mask comes up here, because it is the
        // same mask on the same face. The Angler's and the Trader's have no
        // description written, and one is NOT invented for them — see the
        // design-authority rule. They get the plain fact until he writes
        // them.
        // ------------------------------------------------------------------
        internal static void OnMaskOn(LeshyBossOpponent leshy, int index)
        {
            try
            {
                string mask = MaskNameAt(leshy, index);
                if (string.IsNullOrEmpty(mask))
                {
                    Plugin.Log?.LogInfo($"IKMA LESHY: mask on at index {index}, name unreadable — not spoken.");
                    return;
                }

                // The actor flips here, before the line, so the line and
                // everything the mask then does name the same character.
                BossNarrator.SetMaskIdentity(mask);

                // TWO PATHS ANSWERED THIS AND ONLY ONE WAS FIXED. (0.7.219.)
                //
                // 0.7.218 retired the long mask description in
                // BossNarrator.AnnounceMaskPutOn, per Zamar: "Instead of saying
                // this full intro line, just say 'Leshy puts on the Prospector
                // mask...'" — and he heard the old sentence again anyway,
                // because THIS method had its own copy of the decision and
                // still preferred the description:
                //
                //   IKMA LESHY: mask on — Prospector (Zamar's description).
                //   IKMA SPEAK: Leshy puts on a wooden mask of a dirty wild...
                //
                // The Angler, having no description written, correctly used the
                // new line in the same fight — which is how a half-applied
                // change hides: the path with no data looks fixed.
                //
                // This is the trap DialogueAdvancer already documents in this
                // codebase: "Two methods answer 'is the game talking' and they
                // are consulted by different callers." Same shape, same cost.
                // When changing what a line SAYS, grep for every caller that
                // composes it, not just the one in the file being edited.
                Plugin.Log?.LogInfo($"IKMA PROVISIONAL: Leshy mask on — {mask}.");
                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.LeshyMaskOn(mask));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LESHY: mask-on — {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // THE MASK COMES OFF.  LeshyBossOpponent.CleanUpCurrentMask()
        //
        // Guarded on currentMask exactly as the game guards it: the method is
        // also called from StartMoonPhase, where there may be no mask on at
        // all, and "the Angler fades back into the shadows" with no Angler
        // present is the false-line defect, not a stray.
        //
        // The words are Zamar's, from 0.7.204, reached through
        // AnnounceBossFades so there is one sentence and one home.
        // ------------------------------------------------------------------
        internal static void OnMaskOff(LeshyBossOpponent leshy)
        {
            try
            {
                Resolve();

                object mask = null;
                try { mask = _currentMaskField?.GetValue(leshy); } catch { }

                // A Unity object compares to null through its own operator;
                // the boxed reference does not. Ask it as Unity asks it.
                bool wearing = !(mask == null || mask.Equals(null));
                if (!wearing)
                {
                    Plugin.Log?.LogInfo("IKMA LESHY: cleanup with no mask on — nothing said.");
                    BossNarrator.SetMaskIdentity(null);
                    return;
                }

                // floatsAway: TRUE here and only here. This is Leshy's own
                // fight, where the mask is taken back by the orbiter circling
                // his head — the thing the clause describes. See
                // Vocabulary.LeshyMaskOff.
                Plugin.Log?.LogInfo($"IKMA LESHY: mask off — {BossNarrator.ActorName()}.");
                BossNarrator.AnnounceBossFades(floatsAway: true);
                BossNarrator.SetMaskIdentity(null);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LESHY: mask-off — {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // THE THREE MASKS APPEAR.  LeshyBossOpponent.InitializeMaskOrbiter()
        //
        // A sighted player watches three masks start circling his head at the
        // end of the intro, and that is the whole statement of what this
        // fight is. It is on screen and it makes no sound.
        //
        // The three are read from maskBossTypes, in the order the orbiter
        // spawned them, rather than named from this comment.
        // ------------------------------------------------------------------
        internal static void OnOrbiterSpawned(LeshyBossOpponent leshy)
        {
            try
            {
                var names = new List<string>();
                for (int i = 0; i < 8; i++)
                {
                    string n = MaskNameAt(leshy, i);
                    if (string.IsNullOrEmpty(n)) break;
                    names.Add(n);
                }

                if (names.Count == 0)
                {
                    Plugin.Log?.LogInfo("IKMA LESHY: orbiter spawned, masks unreadable — not spoken.");
                    return;
                }

                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.LeshyOrbiter(names));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LESHY: orbiter — {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // THE PHASE CHANGES.  LeshyBossOpponent.StartNewPhaseSequence()
        //
        // NumLives selects the phase inside the method, so it is already the
        // new value when the enumerator is created. 2 = the death cards,
        // 1 = the moon.
        //
        // Neither line describes what is about to arrive — the stumps and the
        // death cards reach the board and the queue through the game's own
        // CreateCardInSlot and QueueNewCards, which the board differ and the
        // queue reader already report in their own words. This says only
        // that the fight has changed shape, which nothing else does.
        // ------------------------------------------------------------------
        internal static void OnPhaseChange(LeshyBossOpponent leshy)
        {
            try
            {
                int lives = -1;
                try { lives = leshy != null ? leshy.NumLives : -1; } catch { }

                Plugin.Log?.LogInfo($"IKMA LESHY: new phase at NumLives={lives}.");

                // The mask is cleaned up inside the moon phase; drop the
                // actor here so nothing that follows is attributed to a mask
                // that has flown away.
                if (lives == 1) BossNarrator.SetMaskIdentity(null);

                if (lives == 2)
                {
                    Plugin.Log?.LogInfo("IKMA PROVISIONAL: Leshy deathcard phase.");
                    using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.LeshyDeathcardPhase());
                    return;
                }

                if (lives == 1)
                {
                    Plugin.Log?.LogInfo("IKMA PROVISIONAL: Leshy moon phase.");
                    using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.LeshyMoonPhase());
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LESHY: phase — {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // THE MOON IS NOT IN THIS FILE.  (0.7.212.)
        //
        // It was, for one build. Then the Pirate Skull's ship turned out to be
        // the same thing - a plain GiantCard whose OnResolveOnBoard writes one
        // PlayableCard into every opponent slot - which proved the moon was
        // never a Leshy feature at all. Both giants, their arrival and their
        // death, live in GiantCardNarrator.cs and are registered from there
        // for both sequencers.
        //
        // What stayed behind in the readers is the rule itself: BoardReader
        // and BoardWatcher collapse a card holding more than one slot by
        // object identity, so the moon reads once as "Slots 1 to 4" rather
        // than four times as four creatures.
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // THE ANGLER'S HOOK.  FishHookGrab.AimHook / PullHook / CancelHook
        //
        // Hooked here rather than in a file of its own because the Angler
        // mask is the first place a Kaycee's Mod player meets the hook, and
        // the behaviour object is the same one the Angler boss uses — so
        // these three lines serve that fight too when it is built. The actor
        // is asked of the mask, never of the encounter, so the same code says
        // "the Angler" in both.
        //
        // A sighted player sees the hook hanging over one of their cards for
        // a whole turn. It is the single most important piece of information
        // in the mask's turn and it makes no sound.
        // ------------------------------------------------------------------
        // ------------------------------------------------------------------
        // THE COMBAT IS OVER, SO THE HOOK IS NOT NEWS. (0.7.263.)
        //
        // Zamar, on the turn that won the fight: "After the scales hit 5 no
        // further hook callouts should happen, the combat is over." His log:
        //
        //   You deal 5 direct damage. The scale hits 5...
        //   Victory. 5 extra teeth received.
        //   The Angler loses their hook.
        //
        // AND THE GAME AGREES, IN ITS OWN WORDS. AnglerBattleSequencer's very
        // first branch in OpponentCombatEnd is
        //     if (Singleton<TurnManager>.Instance.GameEnding) CancelHook();
        // — the cancel exists to tidy the arm away, not to tell the player
        // anything. So this reads the same flag the game branches on rather
        // than inventing a window or a timer, which is the standing rule for
        // any line that describes a decision the game made.
        //
        // All three hook lines are gated, not just the cancel: a fight can end
        // on the opponent's own combat too, and an aim or a pull announced into
        // a defeat screen is the same defect.
        // ------------------------------------------------------------------
        private static bool CombatIsOver()
        {
            try
            {
                var tm = Singleton<TurnManager>.Instance;
                if (tm == null) return false;

                if (tm.GameEnding) return true;

                // GameEnding WAS THE WRONG FLAG AND THE LOG SAID SO. (0.7.266.)
                //
                // 0.7.263 gated these lines on TurnManager.GameEnding because
                // AnglerBattleSequencer.OpponentCombatEnd branches on it. It
                // never fired, and Zamar heard the hook line after Victory a
                // second time: "That 'The Angler loses his hook' still played
                // after Victory fired. That shouldn't happen."
                //
                // GameEnding is set in TurnManager.CleanupPhase, which runs
                // AFTER the combat phase — but the cancel that produced his
                // line does not come from OpponentCombatEnd at all. It comes
                // from OpponentLifeLost, which the game raises the moment the
                // last candle goes out, well before cleanup. The gate was
                // reading a flag that is only true later than the thing it was
                // meant to stop.
                //
                // THE SCALE IS THE CONDITION HE ACTUALLY NAMED: "After the
                // scales hit 5 no further hook callouts should happen, the
                // combat is over." Five either way — the fight is equally over
                // when it is the player who ran out.
                //
                // This is read rather than remembered, so nothing has to be
                // reset between encounters. LifeManager.Balance is the same
                // member the damage line already reads.
                var lm = LifeManager.Instance;
                if (lm != null)
                {
                    int balance = lm.Balance;
                    if (balance >= 5 || balance <= -5) return true;
                }
            }
            catch { return false; }

            return false;
        }

        internal static void OnHookAimed(CardSlot slot)
        {
            try
            {
                if (slot == null) return;

                if (CombatIsOver())
                {
                    Plugin.Log?.LogInfo("IKMA ANGLER: hook aimed while the game is ending — not spoken.");
                    return;
                }

                string who  = BossNarrator.ActorName();
                string card = null;
                try { card = CardReader.CardName(slot.Card?.Info); } catch { }
                int number = slot.Index + 1;


                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.HookAimed(who, card, number));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LESHY: hook aim — {e.GetType().Name}: {e.Message}");
            }
        }

        internal static void OnHookPulled(FishHookGrab hook)
        {
            try
            {
                Resolve();

                if (CombatIsOver())
                {
                    Plugin.Log?.LogInfo("IKMA ANGLER: hook pulled while the game is ending — not spoken.");
                    return;
                }

                // ASK THE SLOT, NOT THE REMEMBERED CARD. (0.7.218 — a false
                // line, reported in play, and the worst class of bug there is.)
                //
                // 0.7.216 read hookTargetCard, which is a field the game set a
                // turn ago and does NOT clear when the card dies. Zamar's
                // Cuckoo was hooked, then killed in combat before the pull:
                //
                //   The Angler aims the hook at Cuckoo in slot 4.
                //   Cuckoo takes 3 damage and dies.
                //   The Angler hooks Cuckoo and pulls it to his side.   <- LIE
                //
                // Nothing was pulled. PullHook's FIRST statement is
                //   if (hookTargetSlot != null && hookTargetSlot.Card != null)
                // and with the slot empty the whole body is skipped — the hook
                // comes up with nothing and the game shows exactly that.
                //
                // So this asks the game's own question, off the SLOT, through
                // BoardReader.LiveCard so a corpse still sitting in its slot
                // does not count either. Announce what is true, not what was
                // attempted.
                CardSlot slot = null;
                try { slot = _hookSlotField?.GetValue(hook) as CardSlot; } catch { }

                var card = BoardReader.LiveCard(slot);

                string name = null;
                try { name = CardReader.CardName(card?.Info); } catch { }

                string who = BossNarrator.ActorName();

                if (string.IsNullOrEmpty(name))
                {
                    // The hook fires and takes nothing. A sighted player watches
                    // it come up empty, so silence here is a gap of its own.
                    using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.HookEmpty(who));
                    return;
                }

                // WHERE IT LANDS, IN THE SAME SENTENCE. (0.7.263.)
                //
                // PullHook ends with AssignCardToSlot(card, hookTargetSlot
                // .opposingSlot) — the game names the destination itself, so
                // this asks the slot rather than assuming the mirror. The
                // opposing slot carries the same index, which is why the number
                // reads the same on both sides of the table.
                CardSlot landing = null;
                try { landing = slot?.opposingSlot; } catch { }

                int landingIndex = -1;
                try { landingIndex = landing != null ? landing.Index : (slot != null ? slot.Index : -1); }
                catch { landingIndex = -1; }

                // AND THE BOARD DIFFER MUST NOT SAY IT AGAIN. The differ's move
                // branch honours the announced list keyed on the slot a card
                // ends up in (0.7.228) — but this prefix runs before the card
                // has moved, so the destination is stated rather than read.
                // Verified at the CONSUMING side, which is the whole point of
                // that rule: BoardWatcher's move loop compares AnnouncedInSlot
                // against the card's current slot index and skips the line when
                // they match, and the side-change branch sits BELOW that check,
                // so "moves to enemy slot 2" never gets composed.
                if (landingIndex >= 0)
                    BoardWatcher.NoteAnnouncedAtSlot(card, landingIndex);

                // AND THE CARD IT SHOVES OUT OF THE WAY. (0.7.265.)
                //
                // Zamar, on "Enemy Kingfisher has left slot 3": "Those lines
                // for Kingfisher has left slot 3 didn't need to be there. We
                // know since the hook drag."
                //
                // PullHook's second statement is
                //     if (hookTargetSlot.opposingSlot.Card != null)
                //         ReturnCardToQueue(hookTargetSlot.opposingSlot.Card);
                // — the destination has to be empty for the stolen card to land
                // in it, so whatever was standing there goes back to the queue.
                // That departure is not its own event; it is the hook making
                // room, and the pull line has already told him the slot changed
                // hands.
                //
                // NOTED, NOT SILENCED WHOLESALE. The differ's VANISHED branch
                // skips an announced card outright — verified at the consuming
                // side, it is the first test in that loop — while the queue
                // branch does not consult the list at all. So "has left slot 3"
                // goes and "Enemy Kingfisher is queued targeting slot 3" stays,
                // which is the half he cannot deduce from the hook: it says the
                // card is coming back, and where.
                //
                // Read BEFORE the pull runs, because by the time anything else
                // looks the slot is occupied by the stolen card instead.
                try
                {
                    var displaced = landing?.Card;
                    if (displaced != null)
                    {
                        Plugin.Log?.LogInfo(
                            $"IKMA ANGLER: '{CardReader.CardName(displaced.Info)}' is pushed off " +
                            $"slot {landingIndex + 1} to make room — its departure rides the hook line.");
                        BoardWatcher.NoteAnnounced(displaced);
                    }
                }
                catch { }

                Plugin.Log?.LogInfo(
                    $"IKMA ANGLER: hook pulls '{name}' to opponent slot {landingIndex + 1}.");
                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.HookPulled(who, name, landingIndex + 1));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LESHY: hook pull — {e.GetType().Name}: {e.Message}");
            }
        }

        internal static void OnHookCancelled()
        {
            try
            {
                if (CombatIsOver())
                {
                    Plugin.Log?.LogInfo("IKMA ANGLER: hook cancelled while the game is ending — not spoken.");
                    return;
                }

                string who = BossNarrator.ActorName();
                using (Speech.Event(EventKind.Bosses)) Speech.Result(Vocabulary.HookCancelled(who));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LESHY: hook cancel — {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // THE TRADER'S TRADE.  TradeCardsForPelts.TradePhase(...)
        //
        // THE BELL GOES DEAD, AND THAT IS THE POINT. The first thing
        // TradePhase does is disable the bell, and it stays disabled until
        // the player trades or the offered cards are gone. A blind player who
        // does not know this has a fight that has stopped responding.
        //
        // "Any state where the game waits on player input needs an audible
        // prompt" — so this is a Prompt, not a Result: it jumps ahead of
        // pending commentary, because a prompt that arrives after the player
        // has already pressed the bell twice is not late, it is false.
        //
        // The counts come from the call's own arguments. Leshy's mask trades
        // one card; the Trapper/Trader boss's own phase trades four.
        // ------------------------------------------------------------------
        internal static void OnTradePhase(int numQueueCards, int numOpponentSlotCards)
        {
            try
            {
                // ARM THE TRADE READER HERE, AND ONLY HERE. (0.7.217.)
                // It used to decide for itself by asking the board every frame
                // from Update, which outside a battle is a FindObjectOfType per
                // frame — it flooded the log and killed the game. This is the
                // game telling us the screen exists; nothing polls before it.
                TradeReader.Arm();

                string who = BossNarrator.ActorName();


                Speech.Prompt(new System.Func<string>(delegate
                {
                    // THE PELT IS NAMED FROM THE HAND, AT SPEAK TIME.
                    // (0.7.302.) TradePhase spawns a PeltWolf before this
                    // line composes, so the card is there to be asked for —
                    // and if the spawn ever fails, the clause drops rather
                    // than claiming a gift that did not arrive. Trait.Pelt is
                    // the game's own marker, so a second pelt card would be
                    // found the same way.
                    string peltName = null;
                    try
                    {
                        var hand = Singleton<PlayerHand>.Instance?.CardsInHand;
                        if (hand != null)
                            foreach (var c in hand)
                            {
                                if (c?.Info == null) continue;
                                if (!c.Info.HasTrait(Trait.Pelt)) continue;
                                peltName = CardReader.CardName(c.Info);
                                break;
                            }
                    }
                    catch { }

                    return Vocabulary.TradeOffered(who, numOpponentSlotCards, numQueueCards, peltName);
                }));
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LESHY: trade — {e.GetType().Name}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // THE MASK ACTS.  LeshyBossOpponent.ActivateCurrentMask()  (NONPUBLIC)
        //
        // Speaks nothing itself. Its whole job is to open the Prospector's
        // wipe window when it is HIS mask's turn to act. (0.7.218.)
        //
        // Zamar, 0.7.217 playtest: "I need the call out that the Prospector
        // used his pickaxe to kill my cards."
        //
        // 0.7.212 fixed the GATE — IsProspectorFight now answers true while
        // Leshy wears the mask, so the per-strike log lines appeared. But the
        // spoken wipe line is guarded on _expectedStrikes > 0, and the only
        // thing that ever set that was ProspectorBossOpponent's own
        // StartNewPhaseSequence, which never runs in Leshy's fight. Half a fix
        // reads as a whole one in the log and as silence in the room.
        //
        // The count must be taken BEFORE the first blow lands, and this
        // enumerator is created before any of them — the same moment the
        // Prospector's own phase prefix uses. ActivateProspector walks every
        // occupied player slot, which is exactly what the window counts.
        //
        // Gated on the mask actually being his: the Angler's and the Trader's
        // turns come through this same method and must not open a pickaxe
        // window.
        // ------------------------------------------------------------------
        internal static void OnMaskActivates(LeshyBossOpponent leshy)
        {
            try
            {
                string mask = MaskNameAt(leshy, CurrentIndex(leshy));
                Plugin.Log?.LogInfo($"IKMA LESHY: mask activating — {mask ?? "unreadable"}.");

                if (mask == Vocabulary.Prospector)
                    ProspectorNarrator.OpenWipeWindow("Leshy's Prospector mask");
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LESHY: mask activate — {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Cleared between encounters so a mask identity cannot outlive the
        /// fight it belonged to. Called from the same place BoardWatcher is
        /// reset.
        /// </summary>
        internal static void Reset()
        {
            BossNarrator.SetMaskIdentity(null);
        }
    }

    // -------------------------------------------------------------------------
    // Registered by Plugin.TryPatch, outside PatchAll, every one of them: this
    // whole file is new, and six of the nine targets are NONPUBLIC, where one
    // wrong name inside PatchAll would silently kill every patch after it.
    //
    // No [HarmonyPatch] attribute on any class here — check_source.ps1 CHECK 1
    // fails a class that carries both.
    // -------------------------------------------------------------------------

    public static class LeshyBossOpponent_SwitchToMask_Patch
    {
        public static void Prefix(LeshyBossOpponent __instance, int index)
            => LeshyNarrator.OnMaskOn(__instance, index);
    }

    public static class LeshyBossOpponent_CleanUpCurrentMask_Patch
    {
        public static void Prefix(LeshyBossOpponent __instance)
            => LeshyNarrator.OnMaskOff(__instance);
    }

    public static class LeshyBossOpponent_InitializeMaskOrbiter_Patch
    {
        public static void Postfix(LeshyBossOpponent __instance)
            => LeshyNarrator.OnOrbiterSpawned(__instance);
    }

    public static class LeshyBossOpponent_StartNewPhaseSequence_Patch
    {
        public static void Prefix(LeshyBossOpponent __instance)
            => LeshyNarrator.OnPhaseChange(__instance);
    }

    public static class LeshyBossOpponent_ActivateCurrentMask_Patch
    {
        public static void Prefix(LeshyBossOpponent __instance)
            => LeshyNarrator.OnMaskActivates(__instance);
    }

    public static class FishHookGrab_AimHook_Patch
    {
        public static void Prefix(CardSlot slot)
            => LeshyNarrator.OnHookAimed(slot);
    }

    public static class FishHookGrab_PullHook_Patch
    {
        public static void Prefix(FishHookGrab __instance)
            => LeshyNarrator.OnHookPulled(__instance);
    }

    public static class FishHookGrab_CancelHook_Patch
    {
        public static void Prefix()
            => LeshyNarrator.OnHookCancelled();
    }

    public static class TradeCardsForPelts_TradePhase_Patch
    {
        public static void Prefix(int numQueueCards, int numOpponentSlotCards)
            => LeshyNarrator.OnTradePhase(numQueueCards, numOpponentSlotCards);
    }
}

// LeshyNarrator.cs
