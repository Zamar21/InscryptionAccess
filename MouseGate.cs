// MouseGate.cs
using BepInEx.Configuration;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// THE MOUSE IS A DEBUG TOOL. (Session 52, 0.7.464.)
    ///
    /// Zamar, 2026-10-08: "disable the mouse on all packaged builds. the mouse
    /// is purely a debug tool for me to use."
    ///
    /// HOW. The game's own switch is InteractionCursor.InteractionDisabled
    /// (public). While it is true the cursor neither hovers nor clicks.
    /// IKMA does not go through the cursor: it calls CursorEnter /
    /// CursorSelectStart on the interactable itself, and it has already
    /// turned off the game's own keyboard and pad maps, so nothing IKMA
    /// does depends on the mouse. MenuReader has done this for menus since
    /// Session 13; this is the same switch held for the whole game.
    ///
    /// WHY A PREFIX ON ManagedUpdate AND NOT A SETTING WE FLIP ONCE. The game
    /// sets InteractionDisabled itself (and back to false) around its own
    /// sequences, so a value set once is soon undone. Setting it before every
    /// cursor update keeps it true, and __instance is the cursor Harmony
    /// hands over - no Singleton lookup per frame (ikma_singleton_cost).
    ///
    /// PACKAGED vs DEV. A release build (IKMADevTools false) is always off.
    /// A dev build is off too, by default, because the mouse hovering over a
    /// card or node changed IKMA's focus under the keyboard (Zamar, Session
    /// 52: "I need it not to be able to hover which changes our focus"), and
    /// what he plays should be what ships. Set [Debug] MouseOff = false in
    /// com.zamar.ikma.cfg when he wants the mouse for debugging. That entry
    /// does not exist in a release build.
    /// </summary>
    internal static class MouseGate
    {
#if IKMA_DEV
        private static ConfigEntry<bool> _devMouseOff;

        internal static void BindDevConfig(ConfigFile config)
        {
            _devMouseOff = config.Bind("Debug", "MouseOff", true,
                "Dev builds only. true (the default) = the mouse is off in the whole game, as in every packaged build: no hover, no click. " +
                "false = the mouse works, for debugging.");
        }

        internal static bool Off { get { return _devMouseOff != null && _devMouseOff.Value; } }
#else
        internal static bool Off { get { return true; } }
#endif
    }

    /// <summary>
    /// Prefix on InteractionCursor.ManagedUpdate: hold the cursor disabled.
    /// Registered through Plugin.TryPatch, not PatchAll: a new patch, so a
    /// mistake costs only this.
    /// </summary>
    public static class InteractionCursor_ManagedUpdate_Patch
    {
        public static void Prefix(InteractionCursor __instance)
        {
            if (!MouseGate.Off) return;
            try
            {
                if (!__instance.InteractionDisabled) __instance.InteractionDisabled = true;
            }
            catch { }
        }
    }
}
