// DeathCardNameReader.cs
using System;
using System.Collections;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// Naming the death card: speaks what the player types. (Session 32.
    /// UNTESTED - the death card only appears in the Act 1 story, after
    /// dying, which IKMA does not cover yet. Kaycee's Mod never shows it.)
    /// </summary>
    /// <remarks>
    /// HOW THE GAME TAKES THE NAME (read from the decompile,
    /// DeathCardCreationSequencer.EnterNameForCard and KeyboardInputHandler):
    /// after Leshy's line asking the player's name, the sequencer resets its
    /// KeyboardInputHandler and loops until EnteredInput is true. Every frame
    /// the handler reads Unity's Input.inputString - the characters typed
    /// this frame - appends printable ones up to maxInputLength (10), removes
    /// the last on Backspace, and sets EnteredInput on Enter only if the
    /// name is not empty. Anything past 10 is dropped without a sound.
    ///
    /// SO IKMA ONLY WATCHES. It never writes the name and never presses a key;
    /// the game's own handler does all of it, exactly as for a sighted player.
    /// Each frame this compares the handler's KeyboardInput with the last value
    /// seen and says what changed. That comparison works whether the game's
    /// handler runs before or after IKMA in the frame: a change not visible
    /// this frame is visible the next.
    ///
    /// IKMA'S OWN KEYS MUST STAY QUIET WHILE THE NAME IS TYPED. Every letter
    /// and number the player types is also an IKMA hotkey somewhere (H is
    /// help, 1-3 examine, Space repeats...). While Active is true,
    /// HotkeyManager hands the frame here and returns, so nothing but this
    /// reader answers a key - except Escape and the pause menu, which sit
    /// above it and stay reachable. The game's handler does not run while
    /// paused (ManagedBehaviour.UpdateWhenPaused is false), so typing in the
    /// pause menu cannot land in the name.
    ///
    /// WHEN IT IS ACTIVE. Exactly while EnterNameForCard runs. That method is
    /// a coroutine, and a Harmony prefix on a coroutine fires when the
    /// enumerator is CREATED, not when it runs (hard-won fact 1). So a postfix
    /// wraps the enumerator: Begin runs on its first step, End in a finally
    /// when it finishes or is abandoned.
    ///
    /// EVERY WORD IS PROVISIONAL (Vocabulary.DeathCardName) until Zamar's
    /// answers to the six Session 32 questions are in.
    /// </remarks>
    internal static class DeathCardNameReader
    {
        // The two keys that do not type anything. KeyboardInputHandler reads
        // Input.inputString, which never contains Tab or F-keys - so these can
        // never put a character into the name. Tab and F1 are Zamar's
        // (Session 32; he kept F1 once told that 1 would type a "1").
        private const KeyCode READ_BACK_KEY = KeyCode.Tab;
        private const KeyCode HELP_KEY = KeyCode.F1;

        // The standing idle rule: 5.5s, then every 15s, while the game waits.
        private const float IDLE_FIRST = 5.5f;
        private const float IDLE_REPEAT = 15f;

        // The sequencer's handler is a private field. FieldRefAccess compiles
        // a direct accessor once; null if the field is ever renamed, which the
        // log reports and which leaves the name typable but unspoken.
        private static readonly AccessTools.FieldRef<DeathCardCreationSequencer, KeyboardInputHandler> _handlerField
            = SafeFieldRef();

        private static KeyboardInputHandler _handler;
        private static string _last = string.Empty;
        private static float _idleFor;
        private static float _idleInterval = IDLE_FIRST;

        internal static bool Active { get; private set; }

        private static AccessTools.FieldRef<DeathCardCreationSequencer, KeyboardInputHandler> SafeFieldRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<DeathCardCreationSequencer, KeyboardInputHandler>("keyboardInput");
            }
            catch { return null; }
        }

        /// <summary>
        /// Wraps EnterNameForCard's enumerator. Begin runs on its first step
        /// (when the game actually starts it), End when it finishes. Nothing is
        /// changed about what the game's coroutine yields.
        /// </summary>
        internal static IEnumerator Wrap(DeathCardCreationSequencer sequencer, IEnumerator inner)
        {
            Begin(sequencer);
            try
            {
                while (inner.MoveNext())
                    yield return inner.Current;
            }
            finally
            {
                End();
            }
        }

        private static void Begin(DeathCardCreationSequencer sequencer)
        {
            _handler = null;
            try { if (_handlerField != null) _handler = _handlerField(sequencer); } catch { }

            Active = true;
            _last = string.Empty;
            _idleFor = 0f;
            _idleInterval = IDLE_FIRST;

            if (_handler == null)
                Plugin.Log?.LogWarning("IKMA DEATHCARD: the name field could not be found; typing will not be spoken.");
            else
                Plugin.Log?.LogInfo("IKMA DEATHCARD: name entry started.");

            // Queued, so it follows Leshy's line rather than cutting it.
            Speech.Prompt(() => Vocabulary.DeathCardName.Instructions);
        }

        private static void End()
        {
            if (!Active) return;
            Active = false;

            string name = null;
            bool entered = false;
            try
            {
                if (_handler != null)
                {
                    name = _handler.KeyboardInput;
                    entered = _handler.EnteredInput;
                }
            }
            catch { }
            _handler = null;

            Plugin.Log?.LogInfo($"IKMA DEATHCARD: name entry ended{(entered ? $", named '{name}'" : "")}.");
            if (entered && !string.IsNullOrEmpty(name))
                Speech.Confirm(Vocabulary.DeathCardName.Named(name));
        }

        /// <summary>
        /// Called by HotkeyManager every frame, below Escape and the pause
        /// menu. True = naming is in progress and this reader owned the frame;
        /// HotkeyManager then returns without looking at any other key.
        /// </summary>
        internal static bool Tick()
        {
            if (!Active) return false;
            if (_handler == null) return true;   // still swallow keys: they type

            string now;
            try { now = _handler.KeyboardInput ?? string.Empty; }
            catch { return true; }

            string typed = Input.inputString ?? string.Empty;
            bool keyActivity = typed.Length > 0
                || Input.GetKeyDown(READ_BACK_KEY) || Input.GetKeyDown(HELP_KEY);

            if (now != _last)
            {
                if (now.Length > _last.Length && now.StartsWith(_last, StringComparison.Ordinal))
                {
                    string added = now.Substring(_last.Length);
                    Speech.Browse(added.Length == 1
                        ? Vocabulary.DeathCardName.Typed(added[0])
                        : Vocabulary.DeathCardName.NameSoFar(now));   // a paste, or two keys in one frame
                }
                else if (now.Length < _last.Length && _last.StartsWith(now, StringComparison.Ordinal))
                {
                    Speech.Browse(Vocabulary.DeathCardName.Deleted(_last.Substring(now.Length), now));
                }
                else
                {
                    Speech.Browse(Vocabulary.DeathCardName.NameSoFar(now));
                }
                _last = now;
            }
            else if (typed.Length > 0)
            {
                // Nothing changed although keys were typed. Either the name
                // was already full and the game dropped the character, or
                // Enter was pressed on an empty name. "Already full before this
                // frame" is checked against _last, so this holds whichever of
                // IKMA and the game ran first.
                bool enter = typed.IndexOf('\n') >= 0 || typed.IndexOf('\r') >= 0;
                bool printable = false;
                foreach (char c in typed)
                    if (c != '\b' && c != '\n' && c != '\r') { printable = true; break; }

                int max = 10;
                try { max = _handler.maxInputLength; } catch { }

                if (printable && _last.Length >= max)
                    Speech.Browse(Vocabulary.DeathCardName.Full(max));
                else if (enter && _last.Length == 0)
                    Speech.Browse(Vocabulary.DeathCardName.EmptyEnter);
            }

            if (Input.GetKeyDown(READ_BACK_KEY))
                Speech.Browse(Vocabulary.DeathCardName.NameSoFar(now));
            else if (Input.GetKeyDown(HELP_KEY))
                Speech.Browse(Vocabulary.DeathCardName.Instructions);

            // The idle prompt: any key restarts the wait.
            if (keyActivity)
            {
                _idleFor = 0f;
                _idleInterval = IDLE_FIRST;
            }
            else
            {
                _idleFor += Time.unscaledDeltaTime;
                if (_idleFor >= _idleInterval)
                {
                    _idleFor = 0f;
                    _idleInterval = IDLE_REPEAT;
                    Speech.Prompt(() => Vocabulary.DeathCardName.Instructions);
                }
            }
            return true;
        }
    }

    // Registered by Plugin.TryPatch as a Postfix. No [HarmonyPatch] attribute:
    // PatchAll would register it too and every wrap would happen twice.
    // EnterNameForCard is PRIVATE and nothing derives from
    // DeathCardCreationSequencer (grepped), so there is no override to miss.
    public static class DeathCardCreationSequencer_EnterNameForCard_Patch
    {
        public static void Postfix(DeathCardCreationSequencer __instance, ref IEnumerator __result)
        {
            __result = DeathCardNameReader.Wrap(__instance, __result);
        }
    }
}
