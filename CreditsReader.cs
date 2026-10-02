// CreditsReader.cs
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// THE CREDITS ROLL. (0.7.232.)
    ///
    /// Zamar: "The credits should all read. but not the URLs for the assets."
    ///
    /// WHAT THE GAME BUILDS. CreditsDisplayer.GenerateCredits walks
    /// CreditsData.credits once and instantiates one object per entry from one
    /// of two prefabs. Each object carries exactly three text components, and
    /// the order is fixed by the game's own code:
    ///
    ///     componentsInChildren[0].text = title;   // "Creator"
    ///     componentsInChildren[1].text = name;    // "Daniel Mullins"
    ///     componentsInChildren[2].text = link;    // the asset's URL
    ///
    /// The first five entries are full-screen cards shown one at a time on a
    /// beat of the music; everything after them scrolls. The two lists are
    /// private but they ARE the screen, in the order it shows them, so IKMA
    /// reads the objects rather than re-deriving the order from the data — the
    /// same choice the run-end stats made, and for the same reason: what is
    /// read is then what is shown, by construction.
    ///
    /// THE THIRD COMPONENT IS NEVER SPOKEN. That is Zamar's line above, and it
    /// is the right call for more than length: a sketchfab URL read character
    /// by character is forty seconds of noise per model, and the screen shows
    /// it as a courtesy to the artist rather than as something to be read out.
    ///
    /// SPOKEN AS COMMENTARY, so the queue paces the list and nothing cuts
    /// anything. Leaving the scene clears the queue through the transition
    /// path that already exists, so a player who skips out does not get read to
    /// on the title screen afterwards.
    /// </summary>
    public static class CreditsReader
    {
        private static ManualLogSource _log;
        private static FieldInfo _fullScreenField, _scrollingField;

        private static bool _active;

        public static void Init(ManualLogSource log) => _log = log;

        public static void Reset() { _active = false; }

        /// <summary>
        /// True while the credits roll is on screen. Set when the objects are
        /// built and cleared by the scene change, so nothing has to guess at
        /// when the roll ends.
        /// </summary>
        public static bool Active => _active;

        /// <summary>
        /// BACKSPACE LEAVES, AND SO DOES ESCAPE. (0.7.236.) Zamar: "Have
        /// backspace also exit the credits, like Escape does."
        ///
        /// Escape already worked by accident — CreditsDisplayer watches the raw
        /// key itself — but it left two hundred queued credit lines draining
        /// behind the title screen, which is the stale-queue defect Backspace
        /// fixes everywhere else in the mod. Both keys now do the same two
        /// things: drop the rest of the roll, then press the game's own Menu
        /// button, which is what MenuCreditsSceneController.ManagedUpdate is
        /// already watching for. IKMA does not load the scene itself.
        /// </summary>
        public static void Leave()
        {
            if (!_active) return;
            _active = false;

            _log?.LogInfo("IKMA CREDITS: leaving — the rest of the roll is dropped.");

            CombatAnnouncer.DropCommentary("left the credits");
            Speech.Silence();

            HotkeyManager.InjectButton("Menu");
        }

        /// <summary>
        /// Called from the GenerateCredits postfix — the one moment when every
        /// credit object exists and none of them has moved yet.
        /// </summary>
        internal static void OnCreditsBuilt(CreditsDisplayer displayer)
        {
            if (displayer == null) return;

            try
            {
                var lines = new List<string>();

                // Full-screen cards first: the game shows them before the
                // scroll starts, one per beat.
                Collect(displayer, ref _fullScreenField, "fullScreenCredits", lines);
                Collect(displayer, ref _scrollingField,  "creditObjects",     lines);

                _active = true;
                _log?.LogInfo($"IKMA CREDITS: {lines.Count} line(s) to read.");

                if (lines.Count == 0)
                {
                    _log?.LogWarning("IKMA CREDITS: nothing to read — the credit objects were not found.");
                    return;
                }

                foreach (string line in lines)
                    Speech.Commentary(line);
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CREDITS: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Collect(CreditsDisplayer displayer, ref FieldInfo cache,
                                    string fieldName, List<string> into)
        {
            if (cache == null)
                cache = typeof(CreditsDisplayer).GetField(
                    fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            if (cache == null)
            {
                _log?.LogWarning($"IKMA CREDITS: CreditsDisplayer.{fieldName} not found.");
                return;
            }

            var objects = cache.GetValue(displayer) as List<GameObject>;
            if (objects == null) return;

            foreach (var obj in objects)
            {
                string line = LineFor(obj);
                if (line != null) into.Add(line);
            }
        }

        /// <summary>
        /// One credit object as one sentence. The components are read in
        /// hierarchy order and the THIRD one — the link — is dropped, which is
        /// the whole of the URL rule. Reading positionally rather than by
        /// content means a name that happens to look like a URL is still read
        /// and a link field that happens to hold a note is still skipped: the
        /// screen's own layout decides, not a guess about the text.
        /// </summary>
        // ==================================================================
        // THE GAME'S OWN THREE FIELDS, IN THE GAME'S OWN ORDER. (0.7.357.)
        //
        // Zamar's only credits log (0.7.356, after the Pirate Skull):
        // "Creator. Creator." "Composer and Sound Designer. Composer and Sound
        // Designer." — every title twice and no name at all. This took the
        // first two strings from ANY component that carried text, and the
        // credit prefab has more than one reading the same title.
        //
        // CreditsDisplayer.GenerateCredits writes exactly three
        // UnityEngine.UI.Text components, found with
        // GetComponentsInChildren<Text>(true): [0] = title, [1] = name,
        // [2] = link. So the same query is made here (by type name, since the
        // csproj does not reference UnityEngine.UI) and the same indices read.
        // The link is never spoken. Kept as a guard: consecutive identical
        // strings are said once.
        // ==================================================================
        private static bool IsUiText(Component c)
        {
            for (var t = c?.GetType(); t != null; t = t.BaseType)
                if (t.FullName == "UnityEngine.UI.Text") return true;
            return false;
        }

        private static string LineFor(GameObject obj)
        {
            if (obj == null) return null;

            var texts = new List<string>();
            try
            {
                foreach (var c in obj.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || !IsUiText(c)) continue;
                    string v = UiText.Of(c);
                    texts.Add(v == null ? "" : v.Trim());
                }
            }
            catch { return null; }

            string title = texts.Count > 0 ? texts[0] : null;
            string name  = texts.Count > 1 ? texts[1] : null;
            if (LooksLikeUrl(title)) title = null;
            if (LooksLikeUrl(name))  name  = null;

            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(name)) return null;

            if (string.IsNullOrEmpty(title)) return Sentence(name);
            if (string.IsNullOrEmpty(name) || name == title) return Sentence(title);

            return Sentence(title) + " " + Sentence(name);
        }

        private static bool LooksLikeUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string low = s.ToLowerInvariant();
            return low.StartsWith("http://")
                || low.StartsWith("https://")
                || low.StartsWith("www.")
                || low.Contains("://");
        }

        private static string Sentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            char last = s[s.Length - 1];
            if (last == '.' || last == '!' || last == '?' || last == ':') return s;
            return s + ".";
        }
    }

    // Registered through Plugin.TryPatch. NO [HarmonyPatch] ATTRIBUTE:
    // PatchAll would claim it as well and the postfix would run TWICE.
    public static class CreditsDisplayer_GenerateCredits_Patch
    {
        public static void Postfix(CreditsDisplayer __instance)
            => CreditsReader.OnCreditsBuilt(__instance);
    }
}
