// LogFilter.cs
//
// Keeps the BepInEx log clean enough to scroll through with a screen reader.
//
// Zamar's standing rule, Session 11: the log is a player-facing artifact.
// People read back through it, and a run of near-identical engine warnings is
// genuinely hostile at speech rate. But a blanket suppression is not the
// answer — this project has found half its silent patch failures through that
// exact channel, and Harmony reports some of its own trouble as a plain Unity
// warning. So: nothing is suppressed until it has been individually looked at
// and shown harmless, and every suppressed pattern is named in the log once,
// with its reason, so the filter can never hide the fact that it is filtering.
//
// WHERE IT HOOKS  (from dump_bepinex_logging.txt)
//
//   BepInEx.Logging.Logger.Listeners : ICollection<ILogListener>
//       MUTABLE — Add / Remove / Clear
//   ILogListener
//       PUBLIC Void LogEvent(Object sender, LogEventArgs eventArgs)
//       implements IDisposable
//   ILogSource
//       PUBLIC String SourceName [get]
//
// The original plan was to swap out UnityLogSource. That was the wrong seam:
// the source-to-listener wiring lives inside Logger's non-public
// InternalLogEvent, so removing a source may or may not detach listeners
// already bound to it, and the dump cannot tell us which. Filtering at the
// LISTENER needs nothing hidden — one public method on one public interface,
// implemented by every listener, and it catches output from any source.
//
// It also means this file touches only members the dump confirmed. Part 2 of
// that dump matched HarmonyLib's LogEventArgs rather than BepInEx's, so
// LogEventArgs.Data and .Level are still unverified and are NOT used here. The
// `sender` argument IS the ILogSource — confirmed by the LogEvent delegate
// signature — so identification comes from SourceName, and the message text
// from ToString(), which is on Object and cannot be wrong.

using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace IKMA
{
    internal static class LogFilter
    {
        // Flip to false to see the raw firehose again. Deliberately a constant
        // and not a config entry: turning the filter off is a debugging act,
        // not a player preference.
        private const bool ENABLED = true;

        private static ManualLogSource _log;
        private static bool _installed = false;

        // Patterns are matched against the message text, case-sensitively,
        // anywhere in the line.
        private class Rule
        {
            internal string Pattern;
            internal string Reason;
            internal bool   Announced;
        }

        // ------------------------------------------------------------------
        // THE LIST. Nothing goes in here that has not been read and understood.
        // Each entry records WHY it is safe, so a future session can re-judge
        // the call instead of inheriting it.
        // ------------------------------------------------------------------
        private static readonly List<Rule> _rules = new List<Rule>
        {
            new Rule
            {
                // Fires for TotemItemData, TotemTopData, TotemBottomData and
                // CardInfo, several times per encounter. Unity complaining that
                // the GAME constructs ScriptableObjects with `new` instead of
                // CreateInstance. Entirely game-side, entirely cosmetic — the
                // objects work, they just skip Unity's serialisation setup, and
                // Inscryption has shipped this way for years. Nothing IKMA does
                // can cause or fix it.
                Pattern = "must be instantiated using the ScriptableObject.CreateInstance method",
                Reason  = "Unity scolding the game for constructing ScriptableObjects with new; cosmetic, game-side",
            },

            new Rule
            {
                // NOTE THE NARROWNESS. The tempting pattern was
                // "Got null in Singleton<", which would also swallow a genuinely
                // missing BoardManager or MapNodeManager — and a silently absent
                // manager is precisely the kind of failure this log exists to
                // catch. ConduitCircuitManager is an Act 2 system; the game's
                // lazy FindInstance looks for it during Act 1 combat, does not
                // find it, and shrugs. That specific miss is expected. No other
                // singleton is covered, and any new one has to be judged on its
                // own before being added.
                Pattern = "Got null in Singleton<DiskCardGame.ConduitCircuitManager>",
                Reason  = "Act 2 manager, absent in Act 1 by design; the game's own lazy lookup",
            },

            new Rule
            {
                // IKMA'S OWN DOING, and the reason it is here rather than only
                // fixed at the source. Singleton<T>.Instance runs FindInstance
                // when it holds nothing and logs a warning on every miss.
                // DialogueAdvancer asks for the TextDisplayer to find out
                // whether the game is holding on a line, and MenuReader asks for
                // the InteractionCursor to switch the mouse off — both every
                // frame, both guaranteed to miss in scenes that have neither.
                // Zamar's 0.7.47 log carried 2,909 of the first and 106 of the
                // second, in a file he reads back with a screen reader.
                //
                // The polling was fixed in 0.7.48 (cached instance, two-second
                // backoff, skipped entirely while a menu is up). This is the
                // belt and braces: these two names only, so a genuinely missing
                // BoardManager or MapNodeManager still shouts, which is exactly
                // the kind of failure this log exists to catch.
                Pattern = "Got null in Singleton<DiskCardGame.TextDisplayer>",
                Reason  = "IKMA polling for the dialogue state in scenes that have no TextDisplayer",
            },

            new Rule
            {
                // See the note above — same cause, same judgement, named
                // separately so either can be removed on its own.
                Pattern = "Got null in Singleton<DiskCardGame.InteractionCursor>",
                Reason  = "IKMA polling to switch the mouse off in scenes that have no cursor",
            },

            new Rule
            {
                // THE WORST ONE YET, and again IKMA's own. RulebookReader
                // .TickOpenState asks for the RuleBookController every frame,
                // above every layer, so a book opened with the MOUSE is noticed
                // — and most scenes have no controller to find.
                //
                // Zamar's 0.7.74 log was 83,513 lines and 82,761 of them were
                // this one warning. 99.1% of a 7.5 MB file, in an artifact he
                // reads back with a screen reader.
                //
                // Fixed at the source in 0.7.75 (cached instance, two-second
                // backoff, skipped while a menu is up). This is the belt and
                // braces, and it is named specifically — a genuinely missing
                // BoardManager or MapNodeManager still shouts.
                Pattern = "Got null in Singleton<DiskCardGame.RuleBookController>",
                Reason  = "IKMA polling for a mouse-opened rulebook in scenes that have none",
            },

            new Rule
            {
                // THE FOURTH OF THE SAME FAMILY, FOUND 0.7.268 IN A LOG WITH NO
                // PLAYTEST IN IT AT ALL. Zamar's 511-line log carried 300 of
                // these — 59% of the file — from a session that never left the
                // menus.
                //
                // Same cause as its three neighbours above: MapReader.Views()
                // asks for the ViewManager to answer "which camera rung are we
                // on", and the front end, the challenge screen and the title
                // screen have no ViewManager at all. The SOURCE is already
                // right — Views() caches the instance and backs off two seconds
                // between misses, which is why 300 and not 30,000 — so this is
                // the belt and braces, exactly as it was for the other three.
                //
                // Named specifically rather than filtering the shape. A
                // genuinely missing BoardManager or MapNodeManager still
                // shouts, and that is the whole point of keeping these as four
                // rules instead of one wildcard.
                Pattern = "Got null in Singleton<DiskCardGame.ViewManager>",
                Reason  = "IKMA polling for the camera view in scenes that have no ViewManager",
            },
        };

        public static void Init(ManualLogSource log)
        {
            _log = log;
        }

        /// <summary>
        /// Wrap every listener currently registered so their output passes
        /// through the filter. Safe to call twice. Called early in Awake, before
        /// PatchAll, so the wrapping is in place for anything the patches emit.
        /// </summary>
        public static void Install()
        {
            if (!ENABLED || _installed) return;

            try
            {
                var listeners = BepInEx.Logging.Logger.Listeners;
                if (listeners == null)
                {
                    _log?.LogWarning("IKMA LOG FILTER: no listener collection — filter not installed.");
                    return;
                }

                // Snapshot first: we are about to mutate the collection we would
                // otherwise be iterating.
                var originals = new List<ILogListener>(listeners);
                int wrapped = 0;

                foreach (var listener in originals)
                {
                    if (listener == null) continue;
                    if (listener is FilteringListener) continue;   // already ours

                    if (listeners.Remove(listener))
                    {
                        listeners.Add(new FilteringListener(listener));
                        wrapped++;
                    }
                }

                _installed = true;
                _log?.LogInfo($"IKMA LOG FILTER: installed over {wrapped} listener(s), {_rules.Count} pattern(s).");
            }
            catch (Exception e)
            {
                // A broken log filter must never cost us the log. Leave whatever
                // state we reached and say so loudly.
                _log?.LogError($"IKMA LOG FILTER: install FAILED, logging is unfiltered: {e.Message}");
            }
        }

        /// <summary>
        /// True if this line is one of the vetted harmless ones. The first time
        /// a pattern matches, it announces itself with its reason — so the log
        /// always states what it is hiding, once, rather than quietly thinning
        /// itself out.
        /// </summary>
        internal static bool ShouldSuppress(object sender, LogEventArgs eventArgs)
        {
            if (eventArgs == null) return false;

            string text;
            try { text = eventArgs.ToString(); }
            catch { return false; }

            if (string.IsNullOrEmpty(text)) return false;

            // Absolute guard: never suppress our own output, whatever the rules
            // say. If an IKMA line ever matched a pattern, the pattern is wrong.
            if (text.IndexOf("IKMA", StringComparison.Ordinal) >= 0) return false;

            foreach (var rule in _rules)
            {
                if (text.IndexOf(rule.Pattern, StringComparison.Ordinal) < 0) continue;

                if (!rule.Announced)
                {
                    rule.Announced = true;
                    string source = (sender as ILogSource)?.SourceName ?? "unknown source";
                    _log?.LogInfo(
                        $"IKMA LOG FILTER: suppressing repeats of \"{rule.Pattern}\" " +
                        $"from [{source}] — {rule.Reason}.");
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sits in front of a real listener and drops vetted noise. Forwards
        /// everything else untouched, including the original eventArgs object —
        /// nothing is reformatted or rebuilt on the way through.
        /// </summary>
        private class FilteringListener : ILogListener
        {
            private readonly ILogListener _inner;

            internal FilteringListener(ILogListener inner) { _inner = inner; }

            public void LogEvent(object sender, LogEventArgs eventArgs)
            {
                // Session 32: IKMA's own diagnostics, hidden unless
                // [Diagnostics] Show asks for them. A separate question from
                // ShouldSuppress below, which is only ever about the GAME's
                // noise and still never touches an IKMA line.
                if (DiagnosticGate.Hide(eventArgs)) return;
                if (ShouldSuppress(sender, eventArgs)) return;
                _inner?.LogEvent(sender, eventArgs);
            }

            public void Dispose() { _inner?.Dispose(); }
        }
    }
}

// LogFilter.cs
