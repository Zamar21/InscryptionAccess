// PauseProbe.cs
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// LOG ONLY. The Escape menu, surveyed. Speaks nothing. (Session 17.)
    ///
    /// Zamar: "we're missing the Escape Key menu. Pressing escape is supposed to
    /// pause the game, read how many rare's or shinies you have? (not sure what
    /// that value is) and allow you to return to main menu or end the whole
    /// run."
    ///
    /// He is not sure what the value is called, and neither is the assembly from
    /// the outside — so nothing is guessed. `PauseMenu3D` holds
    /// `ascensionRunInfoBar` and `ascensionChallengeArray` and both are
    /// NONPUBLIC GameObjects; what they DISPLAY is scene data that reflection
    /// cannot reach with the game closed. The same wall as the camera ladder and
    /// the cabin zones, and the same answer: probe it live.
    ///
    /// WHAT THIS NEEDS TO ANSWER BEFORE A READER CAN BE WRITTEN:
    ///   1. Every interactable on the menu, in the order the screen holds them,
    ///      with whatever text each one carries. Those are the options — return
    ///      to the map, end the run, options, and whatever else is there.
    ///   2. What the run info bar actually says. That is his "rares or shinies".
    ///   3. Whether MenuController drives the selection, since it is not in any
    ///      dump and would decide how a keyboard walks this menu.
    ///
    /// NO SCENE SEARCH. `PauseMenu.instance` is a PUBLIC STATIC, so the menu
    /// hands itself over; GetComponentsInChildren on that object is asking one
    /// object about its own children, the same justification NodeProbe uses.
    /// </summary>
    public static class PauseProbe
    {
        private static ManualLogSource _log;
        public static void Init(ManualLogSource log) => _log = log;

        // ONCE PER SESSION, not once per pause and certainly not once per
        // frame. LateUpdate on this menu runs constantly, and the log is a
        // player-facing artifact.
        private static bool _loggedThisSession;

        // NOT RESET ANY MORE. The survey describes the pause menu's STRUCTURE,
        // which does not change between pauses or between scenes — reprinting it
        // was 743 lines of Zamar's last 1,996-line log. Kept as a no-op so the
        // caller does not have to know that.
        public static void Reset() { }

        /// <summary>Pull any string a component exposes, the way MenuReader does.</summary>
        private static string TextOf(Component c)
        {
            if (c == null) return null;
            var t = c.GetType();

            foreach (var name in new[] { "text", "Text", "displayedText" })
            {
                try
                {
                    var pr = t.GetProperty(name, BindingFlags.Instance
                                               | BindingFlags.Public
                                               | BindingFlags.NonPublic);
                    if (pr != null && pr.PropertyType == typeof(string))
                    {
                        var v = pr.GetValue(c, null) as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }

                    var f = t.GetField(name, BindingFlags.Instance
                                           | BindingFlags.Public
                                           | BindingFlags.NonPublic);
                    if (f != null && f.FieldType == typeof(string))
                    {
                        var v = f.GetValue(c) as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
                catch { }
            }
            return null;
        }

        internal static void Survey(PauseMenu menu, bool paused)
        {
            // THE LOG IS READ WITH A SCREEN READER. Zamar: "I want someone with
            // a screenreader to be able to sort back through it to hear
            // something past announced if they want." Listening has no skim —
            // every wasted line is a line someone sits through. A log line has
            // to earn itself the same way a spoken one does, and a structure
            // dump earns itself exactly once.
            if (!paused) return;
            if (_loggedThisSession) return;
            _loggedThisSession = true;

            if (menu == null) { _log?.LogInfo("IKMA PAUSE: paused, but no menu instance."); return; }

            _log?.LogInfo($"IKMA PAUSE: paused. menu type = {menu.GetType().Name}");

            // --- every interactable the menu owns, in the screen's own order ---
            try
            {
                var items = menu.GetComponentsInChildren<MainInputInteractable>(true);
                _log?.LogInfo($"IKMA PAUSE: {items.Length} interactable(s).");

                for (int i = 0; i < items.Length; i++)
                {
                    var it = items[i];
                    if (it == null) continue;

                    string label = null;
                    foreach (var c in it.GetComponentsInChildren<Component>(true))
                    {
                        label = TextOf(c);
                        if (!string.IsNullOrEmpty(label)) break;
                    }

                    string objName = "?";
                    bool active = false;
                    try { objName = it.gameObject.name; active = it.gameObject.activeInHierarchy; } catch { }

                    _log?.LogInfo(
                        $"IKMA PAUSE:   item {i + 1}: {it.GetType().Name} obj='{objName}' " +
                        $"active={active} label={(label == null ? "<none>" : "'" + label + "'")}");
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA PAUSE: could not walk the menu ({e.GetType().Name}).");
            }

            // --- every string anywhere on the menu ---
            //
            // This is what answers his "rares or shinies" question. He does not
            // know what the value is called and IKMA must not name it from a
            // member name — so dump what the screen actually SAYS and let the
            // printed words settle it.
            try
            {
                var seen = new HashSet<string>();
                foreach (var c in menu.GetComponentsInChildren<Component>(true))
                {
                    string v = TextOf(c);
                    if (string.IsNullOrEmpty(v)) continue;
                    if (!seen.Add(v)) continue;
                    _log?.LogInfo($"IKMA PAUSE:   text: '{v}'");
                }
            }
            catch { }

            // --- the fields the dump named but could not see into ---
            const BindingFlags any = BindingFlags.Instance
                                   | BindingFlags.Public
                                   | BindingFlags.NonPublic;

            foreach (var fieldName in new[] { "ascensionRunInfoBar", "ascensionChallengeArray",
                                              "endRunCard", "modifyDeckCard", "menuController" })
            {
                try
                {
                    var f = menu.GetType().GetField(fieldName, any);
                    if (f == null) { _log?.LogInfo($"IKMA PAUSE:   {fieldName} = not on this type"); continue; }
                    var v = f.GetValue(menu);
                    _log?.LogInfo($"IKMA PAUSE:   {fieldName} = {(v == null ? "null" : v.ToString())}");
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// PauseMenu.OnPausedChange, and PauseMenu3D OVERRIDES it — so BOTH
    /// declarations are patched. A patch on the base alone would never fire for
    /// the 3D menu, which is the one Act 1 actually uses. That trap has cost
    /// this project a feature twice.
    ///
    /// No [HarmonyPatch] attribute: registered through TryPatch, and carrying
    /// the attribute as well is what killed PatchAll at 0.7.76.
    /// </summary>
    public static class PauseMenu_OnPausedChange_Patch
    {
        public static void Postfix(PauseMenu __instance, bool paused)
        {
            try { PauseProbe.Survey(__instance, paused); }
            catch { }

            // The reader owns the keyboard while the menu is up; the probe above
            // stays because the options panel is still unread and its survey is
            // what the next build needs.
            try { PauseMenuReader.OnPausedChanged(paused); }
            catch { }
        }
    }
}

// PauseProbe.cs
