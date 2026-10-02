// LogExport.cs
using System;
using System.IO;
using System.Text;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// One key that writes the whole BepInEx log to the desktop as a file the
    /// player can drag into Discord. (0.7.306.)
    /// </summary>
    /// <remarks>
    /// ZAMAR, planning the v0.5 beta: "let's create a one button for them to be
    /// able to print that entire log that they can just drag into discord as an
    /// attachment for me. (not have to copy paste anything cause that will get
    /// messy)"
    ///
    /// WHY A FILE AND NOT THE CLIPBOARD. A log runs to hundreds of lines. Asking
    /// a blind player to select it, copy it and paste it into a chat box is the
    /// mess he is describing — and a paste that long is unreadable in Discord
    /// anyway. A file on the desktop is one drag, and Discord keeps it as an
    /// attachment that opens in full.
    ///
    /// THE LOG IS OPEN WHILE THE GAME RUNS, so it is read with
    /// FileShare.ReadWrite. Opening it any other way throws IOException and the
    /// player gets nothing at the moment they most need it to work.
    ///
    /// LogOutput.log IS OVERWRITTEN ON EVERY LAUNCH. That is why this exists as
    /// a key rather than an instruction to go and find the file: by the time a
    /// player has read a bug report template and relaunched, the evidence is
    /// gone. Pressed during the session that went wrong, the copy survives.
    ///
    /// THE HEADER IS WHAT MAKES A REPORT ACTIONABLE. Version, time, screen
    /// reader engine and the scene, written at the top so the first thing read
    /// answers "which build, on what, with which reader" without anyone having
    /// to ask. Everything in it is already known to the mod; nothing is
    /// prompted for.
    /// </remarks>
    internal static class LogExport
    {
        /// <summary>Where BepInEx is writing its log right now, or null.</summary>
        private static string SourceLogPath()
        {
            try
            {
                string root = BepInEx.Paths.BepInExRootPath;
                if (string.IsNullOrEmpty(root)) return null;

                string path = Path.Combine(root, "LogOutput.log");
                return File.Exists(path) ? path : null;
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LOGEXPORT: could not locate the log: {e.GetType().Name}.");
                return null;
            }
        }

        private static string Header()
        {
            var sb = new StringBuilder();
            sb.AppendLine("IKMA bug report log");
            sb.AppendLine($"Mod version: {Plugin.PluginVersion}");
            sb.AppendLine($"Exported:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            string engine = null;
            try { engine = SpeechPump.CurrentEngineName(); } catch { }
            sb.AppendLine($"Speech:      {(string.IsNullOrEmpty(engine) ? "unknown" : engine)}");

            string scene = null;
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            sb.AppendLine($"Scene:       {(string.IsNullOrEmpty(scene) ? "unknown" : scene)}");

            sb.AppendLine($"OS:          {UnityEngine.SystemInfo.operatingSystem}");

            // ==============================================================
            // WHAT THE FIRST EXPORT WAS MISSING. (0.7.309.)
            //
            // Zamar sent one through and asked whether it had everything.
            // Three gaps, all of them things a triager asks first:
            //
            // OTHER MODS was IN the log but at line 165 of 750 — BepInEx
            // prints its plugin list at startup. "Which other mods" is the
            // first question for any report that is not reproducible, so it
            // is hoisted here rather than left to be found. Read from
            // BepInEx's own Chainloader list, not guessed.
            //
            // ACTIVE CHALLENGES were absent entirely, and a Kaycee's Mod
            // report without them is a report about a different game — every
            // challenge changes what the run does. AscensionSaveData.Data
            // .activeChallenges and .challengeLevel are the game's own.
            //
            // Nothing here is prompted for. A reporter drags the file in and
            // the file answers the questions.
            // ==============================================================
            sb.AppendLine($"Other mods:  {OtherPlugins()}");
            sb.AppendLine($"Challenges:  {ActiveChallenges()}");
            sb.AppendLine();
            // NO INSTRUCTION TO THE PLAYER IN HERE. (0.7.311.)
            //
            // 0.7.306 wrote "Describe what you expected to hear and what you
            // heard instead" at the top. Zamar: "This log is for our debugging
            // only and should never need to be opened by a playtester... This
            // log should never need to be manually edited by anyone. (and in
            // fact we dont want it to be, for it to remain truthful.)"
            //
            // He is right twice over. A reporter dragging the file into
            // Discord never opens it, so the line reached nobody — and a file
            // that invites opening invites editing, which is the one thing
            // that would stop it being evidence. What the player says goes in
            // their message; what the mod says goes in here, unaltered.
            sb.AppendLine(new string('-', 70));
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>Every other BepInEx plugin loaded, so a report names its own environment.</summary>
        private static string OtherPlugins()
        {
            try
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    var meta = kv.Value?.Metadata;
                    if (meta == null) continue;
                    if (meta.GUID == "com.zamar.ikma") continue;   // named on its own line
                    names.Add($"{meta.Name} {meta.Version}");
                }

                if (names.Count == 0) return "none";
                names.Sort(System.StringComparer.OrdinalIgnoreCase);
                return string.Join(", ", names.ToArray());
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LOGEXPORT: plugin list unreadable: {e.GetType().Name}.");
                return "unreadable";
            }
        }

        /// <summary>
        /// The challenges this run is under, by the game's own display names.
        /// An internal id is never a display name, so a challenge with no
        /// title is counted rather than printed by its enum.
        /// </summary>
        private static string ActiveChallenges()
        {
            try
            {
                if (!SaveFile.IsAscension) return "not a Kaycee's Mod run";

                var data = AscensionSaveData.Data;
                if (data == null) return "unknown";

                var active = data.activeChallenges;
                string level = $"level {data.challengeLevel}";

                if (active == null || active.Count == 0) return $"{level}, none active";

                var names = new System.Collections.Generic.List<string>();
                int unnamed = 0;
                foreach (var c in active)
                {
                    string title = null;
                    try { title = AscensionChallengesUtil.GetInfo(c)?.title; } catch { }
                    if (string.IsNullOrEmpty(title)) unnamed++;
                    else names.Add(title);
                }

                if (unnamed > 0) names.Add($"{unnamed} with no title in the data");
                return $"{level} — {string.Join(", ", names.ToArray())}";
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA LOGEXPORT: challenges unreadable: {e.GetType().Name}.");
                return "unreadable";
            }
        }

        /// <summary>
        /// Write the copy. Returns the file name on success, null on failure —
        /// the caller speaks either way, because a key that does nothing
        /// silently is the defect this project fixes everywhere else.
        /// </summary>
        internal static string Export(out string reason)
        {
            reason = null;

            string source = SourceLogPath();
            if (source == null)
            {
                reason = Vocabulary.LogExport.LogFileCouldNot;
                Plugin.Log?.LogWarning("IKMA LOGEXPORT: no LogOutput.log at the BepInEx root.");
                return null;
            }

            try
            {
                string body;
                // The game still holds this file open. ReadWrite sharing is not
                // optional here.
                using (var fs = new FileStream(source, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite))
                using (var reader = new StreamReader(fs))
                {
                    body = reader.ReadToEnd();
                }

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                {
                    reason = Vocabulary.LogExport.YourDesktopFolderCould;
                    Plugin.Log?.LogWarning("IKMA LOGEXPORT: no desktop folder.");
                    return null;
                }

                // .txt, not .log: Discord previews one and treats the other as
                // an unknown binary on some clients.
                string fileName = $"IKMA-log-{Plugin.PluginVersion}-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
                string target   = Path.Combine(desktop, fileName);

                File.WriteAllText(target, Header() + body, Encoding.UTF8);

                Plugin.Log?.LogInfo($"IKMA LOGEXPORT: written to '{target}' ({body.Length} characters).");
                return fileName;
            }
            catch (Exception e)
            {
                reason = Vocabulary.LogExport.FileCouldNotBe;
                Plugin.Log?.LogWarning($"IKMA LOGEXPORT: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }
    }
}

// LogExport.cs
