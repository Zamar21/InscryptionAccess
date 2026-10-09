// DevAutopilot.cs

#if IKMA_DEV

using System;
using System.Collections.Generic;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// THE AUTOPILOT. (Session 36, dev builds only.) Zamar: automated testing
    /// for Acts 1-3 going into 1.0, worth building only if it saves tokens.
    ///
    /// Driver verb: "autoplay battle [seconds]". Plays the current battle to
    /// its end through IKMA's OWN keys (arrows, Enter, number keys, D, S, E,
    /// Space), so every line IKMA speaks is still under test, and Claude reads
    /// one transcript per battle instead of steering every key.
    ///
    /// Choosing a play asks the game: BoardState.GenerateFromCurrentBoard,
    /// BoardStateSimulator.SimulateCombatPhase and BoardStateEvaluator - the
    /// same scorer Leshy's AI uses to place his cards (AI.SelectSlotsForCards).
    /// Each legal play (card x minimal sacrifice set x slot) is simulated for
    /// my attack, the enemy queue landing, the enemy attack and my next
    /// attack; the lowest score wins (the evaluator scores for the opponent).
    /// A play is made only if it beats passing. Items are never used.
    ///
    /// One action per settle: it acts only once speech has been quiet a
    /// moment, so narration is not cut by the autopilot's own speed.
    /// </summary>
    internal static class DevAutopilot
    {
        private class Plan
        {
            internal int HandIndex;
            internal string Name;
            internal List<int> Sacs = new List<int>();
            internal int Slot;
            internal int SacPtr;
            internal int Phase;   // 0 select card, 1 sacrifices, 2 slot
        }

        internal static bool Active { get; private set; }
        internal static string Summary { get; private set; }

        private static float _startAt, _maxSeconds, _lastActAt;
        private static bool _sawBattle;
        private static int _turns, _plays, _lastTurnSeen = -1;
        private static Plan _plan;
        private static int _deckPickStep;
        private static bool _runMode;
        private static string _screen;
        private static int _screenSteps, _battles;
        private static string _lastSig;
        private static int _sameSigCount;

        private const float QUIET_S = 0.7f;
        private const float MIN_GAP_S = 0.4f;
        private const float FORCE_AFTER_S = 8f;

        internal static void Start(string arg, Action<string, string> emit)
        {
            _emit = emit;
            string[] a = (arg ?? "").Split(' ');
            float secs;
            _maxSeconds = a.Length > 1 && float.TryParse(a[1], out secs) ? secs : 900f;
            _startAt = _lastActAt = Time.unscaledTime;
            _sawBattle = false;
            _turns = _plays = 0;
            _lastTurnSeen = -1;
            _plan = null;
            _lastSig = null; _sameSigCount = 0;
            _runMode = a.Length > 0 && a[0].Equals("run", StringComparison.OrdinalIgnoreCase);
            _screen = null; _screenSteps = 0; _battles = 0;
            Summary = null;
            Active = true;
            Say("started: " + (_runMode ? "run" : "battle") + ", up to " + _maxSeconds + " s");
        }

        private static Action<string, string> _emit;
        private static void Say(string text) { try { _emit?.Invoke("AUTO", text); } catch { } }

        internal static void Stop(string why)
        {
            if (!Active) return;
            Active = false;
            Summary = $"{why}; battles={_battles} turns={_turns} plays={_plays} time={(int)(Time.unscaledTime - _startAt)}s";
            Say("stopped: " + Summary);
        }

        /// <summary>One key to press this frame, or None.</summary>
        internal static KeyCode Tick(float now, float lastSpeechAt)
        {
            if (!Active) return KeyCode.None;
            if (now - _startAt > _maxSeconds) { Stop("time limit"); return KeyCode.None; }
            if (now - _lastActAt < MIN_GAP_S) return KeyCode.None;

            bool quiet = now - lastSpeechAt >= QUIET_S
                         && (SpeechPump.PendingCount == 0 || SpeechPump.BackgroundHold)
                         && !CombatAnnouncer.HasPendingMessages;
            if (!quiet && now - _lastActAt < FORCE_AFTER_S) return KeyCode.None;

            KeyCode key;
            string sig;
            try { key = Decide(out sig); }
            catch (Exception e) { Stop("error: " + e.GetType().Name + ": " + e.Message); return KeyCode.None; }
            if (!Active || key == KeyCode.None) return KeyCode.None;

            // Stuck: the same decision in the same state, over and over.
            if (sig == _lastSig) { if (++_sameSigCount >= 8) { Stop("stuck: " + sig); return KeyCode.None; } }
            else { _lastSig = sig; _sameSigCount = 0; }

            _lastActAt = now;
            return key;
        }

        private static KeyCode Decide(out string sig)
        {
            sig = "";
            if (DialogueAdvancer.AwaitingInput()) { sig = "dialogue"; return KeyCode.Space; }

            var tm = Singleton<TurnManager>.Instance;
            var bm = Singleton<BoardManager>.Instance;
            var hand = Singleton<PlayerHand>.Instance;

            // RUN MODE: everything between battles, by a simple rule per screen.
            if (_runMode)
            {
                if (RunEndReader.Active) { Stop("run over"); return KeyCode.None; }
                bool inBattle = tm != null && bm != null && hand != null
                                && !tm.GameEnded && !tm.IsSetupPhase && tm.Opponent != null;
                if (!inBattle)
                {
                    if (_sawBattle) { _sawBattle = false; _battles++; Say("battle over: " + (tm != null && tm.PlayerWon ? "WON" : "LOST")); }
                    return OffBattle(out sig);
                }
                _screen = null;
            }

            if (tm == null || bm == null || hand == null)
            {
                // The battle scene is gone (a lost run ends in a new scene).
                if (_sawBattle) Stop("battle over: left the battle scene");
                return KeyCode.None;
            }

            if (tm.GameEnded)
            {
                if (_sawBattle) Stop("battle over: " + (tm.PlayerWon ? "WON" : "LOST"));
                return KeyCode.None;
            }
            if (tm.IsSetupPhase || tm.Opponent == null) return KeyCode.None;
            _sawBattle = true;

            if (!tm.IsPlayerTurn) return KeyCode.None;

            // A draw still landing: the game ignores the bell (and IKMA says
            // nothing - playtest note 10), so wait for the card.
            if (HotkeyManager.DrawInFlight) return KeyCode.None;
            if (tm.TurnNumber != _lastTurnSeen) { _lastTurnSeen = tm.TurnNumber; _turns++; _plan = null; }

            string state = $"turn {tm.TurnNumber} hand {hand.CardsInHand.Count} bal {BalanceNow()}";

            // A deck search (Hoarder) is open: take the first card offered.
            if (DeckPickReader.Active)
            {
                sig = state + " deck pick " + _deckPickStep;
                if (_deckPickStep++ == 0) { Say("deck search: taking the first card"); return KeyCode.RightArrow; }
                return KeyCode.Return;
            }
            _deckPickStep = 0;

            if (bm.ChoosingSacrifices)
            {
                if (_plan != null && _plan.SacPtr < _plan.Sacs.Count)
                {
                    int s = _plan.Sacs[_plan.SacPtr++];
                    _plan.Phase = 1;
                    sig = state + " sac " + s + " #" + _plan.SacPtr;
                    return NumberKey(s);
                }
                sig = state + " sacrifice without a plan";
                int any = FirstSacrificable(bm);
                return any >= 0 ? NumberKey(any) : KeyCode.None;
            }

            if (bm.ChoosingSlot && hand.ChoosingSlotCard != null)
            {
                int slot = _plan != null ? _plan.Slot : FirstEmpty(bm);
                sig = state + " slot " + slot;
                if (_plan != null) { Say($"place {_plan.Name} in slot {slot + 1}"); _plan = null; _plays++; }
                return slot >= 0 ? NumberKey(slot) : KeyCode.None;
            }

            if (tm.IsPlayerDrawPhase)
            {
                int left = -1;
                try { left = Singleton<CardDrawPiles>.Instance.Deck.CardsInDeck; } catch { }
                bool squirrel = left <= 0 || WantSquirrel(bm, hand);
                sig = state + " draw " + left;
                Say(squirrel ? "draw: Squirrel pile" : "draw: deck");
                return squirrel ? KeyCode.S : KeyCode.D;
            }

            if (!tm.IsPlayerMainPhase) return KeyCode.None;

            if (_plan != null && _plan.Phase == 0)
            {
                int idx = HandIndexNow();
                if (idx == _plan.HandIndex && _plan.HandIndex < hand.CardsInHand.Count)
                {
                    _plan.Phase = _plan.Sacs.Count > 0 ? 1 : 2;
                    sig = state + " enter " + idx;
                    return KeyCode.Return;
                }
                sig = state + " browse " + idx + "->" + _plan.HandIndex;
                return KeyCode.RightArrow;
            }

            // A plan past selection with nothing to choose: the game refused
            // it, or it is still animating. Drop it and think again.
            _plan = null;

            int passScore;
            var best = BestPlay(out passScore);
            if (best != null)
            {
                Say($"plan: {best.Name} (hand {best.HandIndex + 1})" +
                    (best.Sacs.Count > 0 ? " sacrificing slot(s) " + string.Join(",", best.Sacs.ConvertAll(x => (x + 1).ToString()).ToArray()) : "") +
                    $" -> slot {best.Slot + 1}; pass would score {passScore}");
                _plan = best;
                sig = state + " plan " + best.Name;
                return KeyCode.None;   // act on the next settle
            }

            sig = state + " bell";
            Say("ring the bell");
            return KeyCode.E;
        }

        // ------------------------------------------------------------------
        // Between battles (run mode). Card choice: first card, turned over,
        // then taken. Deck search: first card. Every other node screen and
        // the map: Right then Enter, repeated. Screens it does not know are
        // left alone; 30 s of that gets a Space, and repeats stop it.
        private static KeyCode OffBattle(out string sig)
        {
            sig = "";
            if (DeckPickReader.Active)
            {
                sig = "deck pick " + _deckPickStep;
                return _deckPickStep++ == 0 ? KeyCode.RightArrow : KeyCode.Return;
            }
            _deckPickStep = 0;

            string screen = CardChoiceReader.Active ? "card choice"
                          : NodeScreenReader.Active ? "node screen"
                          : TradeReader.Active ? "trade"
                          : DeathCardChoiceReader.Active ? "death card choice"
                          : OnMap() ? "map" : null;
            if (screen == null)
            {
                _screen = null;
                if (Time.unscaledTime - _lastActAt > 30f) { sig = "stall"; return KeyCode.Space; }
                return KeyCode.None;
            }
            if (screen != _screen) { _screen = screen; _screenSteps = 0; Say("screen: " + screen); }
            if (++_screenSteps > 40) { Stop("stuck on " + screen); return KeyCode.None; }
            sig = screen + " " + _screenSteps;
            if (screen == "card choice") return _screenSteps == 1 ? KeyCode.RightArrow : KeyCode.Return;
            return _screenSteps % 2 == 1 ? KeyCode.RightArrow : KeyCode.Return;
        }

        private static bool OnMap()
        {
            try
            {
                var vm = Singleton<ViewManager>.Instance;
                var mgr = MapNodeManager.Instance;
                return vm != null && vm.CurrentView == View.MapDefault && mgr != null && !mgr.MovingNodes;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        private static Plan BestPlay(out int passScore)
        {
            var bm = Singleton<BoardManager>.Instance;
            var hand = Singleton<PlayerHand>.Instance;
            var pslots = bm.PlayerSlotsCopy;
            passScore = Score(null, null, -1, pslots);

            Plan best = null;
            int bestScore = passScore;
            var cards = hand.CardsInHand;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c == null || c.Info == null) continue;
                bool can = false;
                try { can = c.CanPlay(); } catch { }
                if (!can) continue;

                var sets = c.Info.BloodCost > 0 ? SacrificeSets(pslots, c.Info.BloodCost) : new List<List<int>> { new List<int>() };
                foreach (var sac in sets)
                {
                    for (int s = 0; s < pslots.Count; s++)
                    {
                        var occ = pslots[s].Card;
                        bool free = occ == null || (sac.Contains(s) && !occ.HasAbility(Ability.Sacrificial));
                        if (!free) continue;
                        int sc = Score(c, sac, s, pslots);
                        if (sc < bestScore)
                        {
                            bestScore = sc;
                            best = new Plan { HandIndex = i, Name = CardReader.CardName(c), Sacs = new List<int>(sac), Slot = s };
                        }
                    }
                }
            }
            return best;
        }

        private static List<List<int>> SacrificeSets(List<CardSlot> pslots, int blood)
        {
            var cand = new List<int>();
            for (int s = 0; s < pslots.Count; s++)
                if (pslots[s].Card != null && pslots[s].Card.CanBeSacrificed) cand.Add(s);

            var result = new List<List<int>>();
            for (int mask = 1; mask < (1 << cand.Count); mask++)
            {
                var set = new List<int>();
                int sum = 0, minV = 99;
                for (int b = 0; b < cand.Count; b++)
                {
                    if ((mask & (1 << b)) == 0) continue;
                    set.Add(cand[b]);
                    int v = pslots[cand[b]].Card.HasAbility(Ability.TripleBlood) ? 3 : 1;
                    sum += v; if (v < minV) minV = v;
                }
                if (sum >= blood && sum - minV < blood) result.Add(set);   // minimal
            }
            return result;
        }

        private static int Score(PlayableCard c, List<int> sac, int slot, List<CardSlot> pslots)
        {
            var bs = BoardState.GenerateFromCurrentBoard();
            if (c != null)
            {
                foreach (int s in sac)
                    if (!pslots[s].Card.HasAbility(Ability.Sacrificial)) bs.playerSlots[s].card = null;
                var ss = bs.playerSlots[slot];
                ss.card = new BoardState.CardState(c.Info, c.Attack, c.Health, c.TemporaryMods, null, ss);
            }

            BoardStateSimulator.SimulateCombatPhase(bs, true);                      // my attack
            if (bs.opponentDamage - bs.playerDamage >= LifeManager.GOAL_BALANCE) return -100000;

            var opp = Singleton<TurnManager>.Instance?.Opponent;                  // his queue lands
            if (opp != null && opp.Queue != null)
                foreach (var q in opp.Queue)
                {
                    if (q == null || q.QueuedSlot == null || q.Info == null) continue;
                    int i = q.QueuedSlot.Index;
                    if (i < 0 || i >= bs.opponentSlots.Count) continue;
                    var os = bs.opponentSlots[i];
                    if (os.card == null) os.card = new BoardState.CardState(q.Info, q.Attack, q.Health, q.TemporaryMods, null, os);
                }

            BoardStateSimulator.SimulateCombatPhase(bs, false);                     // his attack
            if (bs.playerDamage - bs.opponentDamage >= LifeManager.GOAL_BALANCE) return 100000;
            BoardStateSimulator.SimulateCombatPhase(bs, true);                      // my next attack
            if (bs.opponentDamage - bs.playerDamage >= LifeManager.GOAL_BALANCE) return -50000;
            return BoardStateEvaluator.EvaluateBoardState(bs);
        }

        // Squirrels are the fodder: draw one when nothing in hand is free to
        // play and the board cannot pay for the cheapest blood card, or the
        // board is nearly empty.
        private static bool WantSquirrel(BoardManager bm, PlayerHand hand)
        {
            int bones = 0;
            try { bones = Singleton<ResourcesManager>.Instance.PlayerBones; } catch { }
            bool free = false;
            int needBlood = 99;
            foreach (var c in hand.CardsInHand)
            {
                if (c?.Info == null) continue;
                if (c.Info.BloodCost == 0 && c.Info.BonesCost <= bones) free = true;
                if (c.Info.BloodCost > 0 && c.Info.BloodCost < needBlood) needBlood = c.Info.BloodCost;
            }
            int fodder = 0, onBoard = 0;
            foreach (var sl in bm.PlayerSlotsCopy)
            {
                if (sl.Card == null) continue;
                onBoard++;
                if (sl.Card.CanBeSacrificed) fodder += sl.Card.HasAbility(Ability.TripleBlood) ? 3 : 1;
            }
            return !free && (needBlood > fodder || onBoard < 2);
        }

        private static int BalanceNow()
        {
            try { return Singleton<LifeManager>.Instance.Balance; } catch { return 0; }
        }

        private static int FirstSacrificable(BoardManager bm)
        {
            var p = bm.PlayerSlotsCopy;
            for (int s = 0; s < p.Count; s++) if (p[s].Card != null && p[s].Card.CanBeSacrificed) return s;
            return -1;
        }

        private static int FirstEmpty(BoardManager bm)
        {
            var p = bm.PlayerSlotsCopy;
            for (int s = 0; s < p.Count; s++) if (p[s].Card == null) return s;
            return -1;
        }

        private static int HandIndexNow()
        {
            var hm = HotkeyManager.Current;
            return hm != null ? hm.HandIndexNow : -1;
        }

        private static KeyCode NumberKey(int slotIndex) => KeyCode.Alpha1 + slotIndex;
    }
}

#endif
// DevAutopilot.cs
