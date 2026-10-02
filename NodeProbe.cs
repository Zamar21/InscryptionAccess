// NodeProbe.cs
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// A LOG-ONLY survey of the Act 1 nodes IKMA still cannot read. Nothing in
    /// this file ever speaks. (Session 16.)
    ///
    /// WHY THIS EXISTS INSTEAD OF A CAMPFIRE READER. The campfire was the next
    /// node to build and dump_act1_nodes_interaction.txt got most of the way
    /// there — CardStatBoostSequencer holds a selectionSlot
    /// (SelectCardFromDeckSlot, with a PUBLIC Card property), a confirmStone
    /// (ConfirmStoneButton), GetValidCards(bool) and GetTranslatedStatText(bool),
    /// which is the game's own wording for the stat being raised.
    ///
    /// What it did NOT answer is where the player chooses BETWEEN the two
    /// boosts. OnSlotSelected(MainInputInteractable slot, Boolean attackMod)
    /// proves two slots exist and that the choice is made by clicking one of
    /// them, and the sequencer has no field holding either. The only way to
    /// reach them from source alone would be to guess, and a wrong guess on a
    /// screen the game is blocking on is exactly the class of mistake that
    /// softlocks a node. The standing rule is to dump first; where a dump
    /// cannot answer, instrument.
    ///
    /// So this build asks the live scene the question the assembly could not,
    /// and one real run comes back as the map for building every remaining
    /// reader: campfire, sacrifice stone, mycologists, trader, trapper, item
    /// pickup, totem.
    ///
    /// THIS IS NOT A SCENE SEARCH. GetComponentsInChildren runs on the
    /// sequencer's OWN GameObject, handed over by its own patch. The banned
    /// lookups — FindObjectOfType, GameObject.Find — scan the whole scene for
    /// something IKMA has no reference to. This asks one object IKMA already
    /// holds about its own children, the same justification as
    /// GetComponent&lt;Strafe&gt;() on a card in CardReader.DescribeMoveDirection.
    ///
    /// COROUTINE PREFIXES FIRE AT ENUMERATOR CREATION, so nothing exists to
    /// look at when the patch runs. Every probe is therefore SAMPLED on a timer
    /// from HotkeyManager, twice: once after the screen has set itself up, and
    /// again later, because several of these sequencers reveal their
    /// interactables in stages and a single sample would record the wrong half.
    /// </summary>
    public static class NodeProbe
    {
        private static ManualLogSource _log;

        public static void Init(ManualLogSource log) => _log = log;

        // The sequencer currently being surveyed, and how long since it opened.
        private static object _target;
        private static string _targetName;
        private static float  _elapsed;
        private static int    _samplesTaken;

        // Two samples. The first catches the screen as it settles; the second
        // catches anything revealed after the opening dialogue, which on
        // several of these nodes is where the interactables actually appear.
        private static readonly float[] SAMPLE_AT = { 1.2f, 5.0f };

        // ------------------------------------------------------------------
        internal static void Begin(object sequencer, string friendlyName)
        {
            if (sequencer == null) return;

            _target       = sequencer;
            _targetName   = friendlyName;
            _elapsed      = 0f;
            _samplesTaken = 0;

            // ==================================================================
            // THE SURVEY IS SCAFFOLDING AND IT IS OFF BY DEFAULT. (0.7.276.)
            //
            // Zamar: "We need this log as cleaned up as possible." Two samples
            // of a settled screen is forty-odd lines listing interactables the
            // reader has already named and acted on — written to BUILD a node
            // screen, and still running on screens he has signed off.
            //
            // The probe itself is NOT disabled: it is also the doorbell that
            // hands the live sequencer to NodeScreenReader (see below), and
            // that has to keep working. Only the dump is gated, on the same
            // flag as the reader's per-part diagnostics, so building the next
            // node screen means turning one field on rather than restoring
            // deleted code.
            // ==================================================================
            if (NodeScreenReader.VerboseNodeDiagnostics)
                _log?.LogInfo($"IKMA PROBE: {friendlyName} sequence started " +
                              $"({sequencer.GetType().Name}). Sampling at 1.2s and 5.0s.");

            // THE PROBE IS ALSO THE DOORBELL. It already knows the exact moment
            // a node screen starts and holds the live sequencer, so the reader
            // is handed it here rather than hunting for one. The survey logging
            // stays — the deck-selection half of these screens is still unread
            // and its next log is what will describe it.
            var comp = sequencer as UnityEngine.Component;
            if (comp != null) NodeScreenReader.Begin(comp, friendlyName);
        }

        internal static void End(string reason)
        {
            if (_target == null) return;
            if (NodeScreenReader.VerboseNodeDiagnostics)
                _log?.LogInfo($"IKMA PROBE: {_targetName} survey ended ({reason}).");
            NodeScreenReader.End();
            _target       = null;
            _targetName   = null;
            _samplesTaken = 0;
        }

        public static void Reset() => End("scene change");

        /// <summary>
        /// The screen finished on its own. Called by NodeScreenReader when the
        /// parts it was reading have gone and stayed gone — the map layer takes
        /// the keyboard back from the next frame.
        ///
        /// Routed through the probe rather than having the reader release itself
        /// so that ONE place owns the lifetime of a node screen. Two owners is
        /// how the pause menu ended up stranded a build ago.
        /// </summary>
        public static void Finished() => End("screen over");

        /// <summary>Driven from HotkeyManager.UpdateInner. Speaks nothing.</summary>
        public static void Tick(float deltaTime)
        {
            if (_target == null) return;
            if (_samplesTaken >= SAMPLE_AT.Length) return;

            _elapsed += deltaTime;
            if (_elapsed < SAMPLE_AT[_samplesTaken]) return;

            int n = _samplesTaken;
            _samplesTaken++;

            try { Sample(n); }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA PROBE: sample {n + 1} threw: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        private static void Sample(int index)
        {
            var seq = _target;
            if (seq == null) return;

            // The whole dump, in one gate. Everything below this line is the
            // survey — reflection over the sequencer's interactables and
            // fields — and none of it is spoken. See Begin for why it is off.
            if (!NodeScreenReader.VerboseNodeDiagnostics) return;

            var behaviour = seq as MonoBehaviour;
            string typeName = seq.GetType().Name;

            _log?.LogInfo($"IKMA PROBE [{_targetName}] sample {index + 1} of {SAMPLE_AT.Length} " +
                          $"at {SAMPLE_AT[index]:0.0}s — {typeName}");

            // 1. Every interactable this sequencer owns. THE CENTRAL QUESTION:
            //    the campfire's two stat slots are clicked, not held in a
            //    field, so this is the only way to learn what they are.
            if (behaviour != null)
            {
                try
                {
                    var interactables = behaviour.gameObject
                        .GetComponentsInChildren<MainInputInteractable>(true);

                    _log?.LogInfo($"IKMA PROBE [{_targetName}]   interactables: {interactables.Length}");

                    for (int i = 0; i < interactables.Length; i++)
                    {
                        var it = interactables[i];
                        if (it == null) continue;

                        // Active state matters as much as existence: an
                        // interactable that is present but switched off is not
                        // an option a sighted player has either.
                        _log?.LogInfo(
                            $"IKMA PROBE [{_targetName}]     [{i}] {it.GetType().Name} " +
                            $"obj='{it.gameObject.name}' active={it.gameObject.activeInHierarchy}");
                    }
                }
                catch (System.Exception e)
                {
                    _log?.LogWarning($"IKMA PROBE [{_targetName}]   interactable walk failed: {e.Message}");
                }
            }

            // 2. Every field on the sequencer that holds something IKMA would
            //    need — cards, slots, stones, lists. Reported by SHAPE rather
            //    than by a list of names, so a field nobody thought to ask
            //    about still turns up.
            ReportInterestingFields(seq, typeName);

            // 3. The game's own questions, where this sequencer declares one.
            //    Asked, never reimplemented — the standing rule. All are
            //    read-shaped: they report what may be chosen and what the
            //    screen calls it, and none of them commits the player to
            //    anything.
            AskGameQuestions(seq, typeName);
        }

        // ------------------------------------------------------------------
        private static void ReportInterestingFields(object seq, string typeName)
        {
            try
            {
                var fields = seq.GetType().GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (var f in fields)
                {
                    if (f == null) continue;
                    string ft = f.FieldType.Name;

                    bool interesting =
                        ft == "SelectableCard"          || ft == "SelectableCardArray"  ||
                        ft == "SelectCardFromDeckSlot"  || ft == "SelectCardPairFromDeckSlot" ||
                        ft == "SelectableCardPair"      || ft == "SelectableCardPairArray"    ||
                        ft == "ConfirmStoneButton"      || ft == "SelectableItemSlot"  ||
                        ft == "CardPile"                ||
                        f.FieldType.IsSubclassOf(typeof(MainInputInteractable)) ||
                        f.FieldType == typeof(MainInputInteractable) ||
                        (f.FieldType.IsGenericType &&
                         f.FieldType.GetGenericTypeDefinition() == typeof(List<>));

                    if (!interesting) continue;

                    object value = null;
                    try { value = f.GetValue(seq); } catch { }

                    string described = DescribeValue(value);
                    _log?.LogInfo($"IKMA PROBE [{_targetName}]   field {ft} {f.Name} = {described}");
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA PROBE [{_targetName}]   field walk failed: {e.Message}");
            }
        }

        private static string DescribeValue(object value)
        {
            if (value == null) return "null";

            // Unity's fake-null: a destroyed object is not reference-null but
            // is not usable either, and reporting it as present would send the
            // next build looking for something that is gone.
            var uo = value as UnityEngine.Object;
            if (uo == null && value is UnityEngine.Object) return "destroyed (Unity null)";

            var card = value as SelectableCard;
            if (card != null)
            {
                string name = "?";
                bool flipped = true;
                try { name = CardReader.CardName(card.ChoiceInfo?.CardInfo) ?? "(no CardInfo)"; } catch { }
                try { flipped = card.Flipped; } catch { }
                // Flipped TRUE means FACE DOWN. This sense was backwards in the
                // mod for six sessions; it is spelled out anywhere it is read.
                return $"SelectableCard '{name}' faceDown={flipped}";
            }

            var slot = value as SelectCardFromDeckSlot;
            if (slot != null)
            {
                string held = "null";
                try { held = slot.Card == null ? "null" : DescribeValue(slot.Card); } catch { }
                return $"SelectCardFromDeckSlot Card={held} active={ActiveOf(slot)}";
            }

            var stone = value as ConfirmStoneButton;
            if (stone != null)
            {
                string confirmed = "?";
                try { confirmed = stone.SelectionConfirmed.ToString(); } catch { }
                return $"ConfirmStoneButton confirmed={confirmed} active={ActiveOf(stone)}";
            }

            var list = value as System.Collections.ICollection;
            if (list != null)
            {
                var sb = new StringBuilder();
                sb.Append($"count={list.Count}");
                int i = 0;
                foreach (var item in list)
                {
                    if (i >= 6) { sb.Append(", ..."); break; }
                    sb.Append($", [{i}] {ShortValue(item)}");
                    i++;
                }
                return sb.ToString();
            }

            var comp = value as Component;
            if (comp != null)
                return $"{comp.GetType().Name} obj='{comp.gameObject.name}' active={ActiveOf(comp)}";

            return value.ToString();
        }

        private static string ShortValue(object item)
        {
            if (item == null) return "null";

            var card = item as SelectableCard;
            if (card != null)
            {
                try { return CardReader.CardName(card.ChoiceInfo?.CardInfo) ?? "(no CardInfo)"; }
                catch { return "SelectableCard"; }
            }

            var info = item as CardInfo;
            if (info != null)
            {
                try { return CardReader.CardName(info); } catch { return "CardInfo"; }
            }

            var comp = item as Component;
            if (comp != null) return $"{comp.GetType().Name}:{comp.gameObject.name}";

            return item.GetType().Name;
        }

        private static string ActiveOf(Component c)
        {
            try { return c.gameObject.activeInHierarchy.ToString(); }
            catch { return "?"; }
        }

        // ------------------------------------------------------------------
        // The game's own answers. Every one of these is a method the sequencer
        // declares to answer a question IKMA would otherwise have to
        // reimplement — what may be chosen here, and what the screen calls it.
        // Called by name through reflection because most are NONPUBLIC.
        // ------------------------------------------------------------------
        private static void AskGameQuestions(object seq, string typeName)
        {
            switch (typeName)
            {
                case "CardStatBoostSequencer":
                    // The campfire's own words for the stat being raised. There
                    // is no other source for this wording, and inventing it is
                    // the mistake the card-appearance table already made once.
                    LogCall(seq, "GetTranslatedStatText", new object[] { true },  "stat text (attack)");
                    LogCall(seq, "GetTranslatedStatText", new object[] { false }, "stat text (health)");
                    LogCall(seq, "GetValidCards",         new object[] { true },  "valid cards (attack)");
                    LogCall(seq, "GetValidCards",         new object[] { false }, "valid cards (health)");
                    break;

                case "TradePeltsSequencer":
                    LogCall(seq, "GetTradingTiers", null, "trading tiers");
                    break;

                case "DuplicateMergeSequencer":
                    LogCall(seq, "GetValidDuplicateCards", null, "valid duplicate cards");
                    LogCall(seq, "GetDuplicateCardChoices", null, "duplicate card choices");
                    break;

                // CardMergeSequencer is deliberately not asked anything.
                // GetValidCardsForHost and GetValidCardsForSacrifice both take a
                // CardInfo, and supplying one would be IKMA proposing a choice
                // rather than observing the screen. The field walk above already
                // reports hostSlot and sacrificeSlot, which is what the next
                // build needs.
            }
        }

        private static void LogCall(object seq, string methodName, object[] args, string label)
        {
            try
            {
                var m = seq.GetType().GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (m == null)
                {
                    _log?.LogInfo($"IKMA PROBE [{_targetName}]   {label}: method {methodName} not found.");
                    return;
                }

                object result = m.Invoke(seq, args);
                _log?.LogInfo($"IKMA PROBE [{_targetName}]   {label}: {DescribeValue(result)}");
            }
            catch (System.Exception e)
            {
                // A throw here is itself a finding — it means the question
                // cannot be asked at this moment, which the next build needs to
                // know before it builds a reader that asks it.
                _log?.LogWarning($"IKMA PROBE [{_targetName}]   {label}: threw {e.GetType().Name}: {e.Message}");
            }
        }
    }
}

// NodeProbe.cs
