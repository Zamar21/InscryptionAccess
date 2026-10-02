// UpdatePatcher.cs
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using IKMA;
using Mono.Cecil;

namespace IKMAUpdater
{
    /// <summary>
    /// IKMAUpdater.dll - installs a downloaded IKMA update at game start.
    /// (Session 32. BUILT, NEVER RUN.)
    /// </summary>
    /// <remarks>
    /// WHY A SECOND DLL. Windows will not let a file be replaced while a
    /// program has it loaded, and IKMAccess.dll is loaded the whole time the
    /// game runs. BepInEx has an earlier stage than plugins: "preloader
    /// patchers", DLLs in BepInEx\patchers that run BEFORE any plugin is
    /// loaded. At that moment IKMAccess.dll is just a file, and this can swap
    /// it. AutoUpdate.cs (in the mod) does the downloading; this only swaps.
    ///
    /// THE PATCHER CONTRACT (BepInEx 5, checked against the game's
    /// BepInEx.Preloader.dll: it looks for TargetDLLs, Initialize, Patch,
    /// Finish by name). A patcher normally edits game DLLs as they load; this
    /// one edits none - TargetDLLs is empty and Patch does nothing - and does
    /// all its work in Initialize, which BepInEx calls once at startup.
    ///
    /// WHAT IT DOES, only if IKMAccess.dll.update is waiting next to
    /// IKMAccess.dll:
    ///   1. Checks the signature again (UpdateSignature.cs, the same code the
    ///      mod uses, compiled into this DLL too). The file sat on disk since
    ///      it was downloaded; it is checked again before it can run.
    ///   2. Reads the installed IKMA's version, and installs only if the
    ///      update is newer. (If the player installed a newer IKMA by hand in
    ///      between, a stale download must not take them backwards.)
    ///   3. Keeps the current DLL as IKMAccess.dll.previous (a manual way
    ///      back), copies the update in, and writes IKMA_updated.txt so the mod
    ///      can say it was updated.
    ///   4. Deletes the downloaded files, whatever happened.
    /// Any failure leaves the installed IKMA as it was.
    ///
    /// This file must never update ITSELF: it is loaded while it runs, for the
    /// same reason as above. A change to it needs the setup .exe.
    /// </remarks>
    public static class UpdatePatcher
    {
        // Patcher contract: the game DLLs to patch. None.
        public static IEnumerable<string> TargetDLLs => new string[0];

        // Patcher contract: called for each target DLL. There are none.
        public static void Patch(AssemblyDefinition assembly) { }

        // Patcher contract: called once, before any plugin loads.
        public static void Initialize()
        {
            ManualLogSource log = Logger.CreateLogSource("IKMA Updater");
            try
            {
                ApplyAll(Paths.PluginPath,
                    (text, problem) => { if (problem) log.LogWarning(text); else log.LogInfo(text); });
            }
            catch (Exception e)
            {
                log.LogWarning($"IKMA UPDATE: the update step failed; IKMA was left as it was. ({e.Message})");
            }
        }

        /// <summary>
        /// Every waiting update under the plugins folder. Normally zero or one.
        /// Separate from Initialize so it can be tested without BepInEx.
        /// </summary>
        internal static void ApplyAll(string pluginsFolder, Action<string, bool> log)
        {
            if (!Directory.Exists(pluginsFolder)) return;

            // Leftovers of a download that stopped half-way.
            foreach (string part in Directory.GetFiles(pluginsFolder, AutoUpdateNames.StagedDll + "*.part", SearchOption.AllDirectories))
                TryDelete(part);

            foreach (string staged in Directory.GetFiles(pluginsFolder, AutoUpdateNames.StagedDll, SearchOption.AllDirectories))
                ApplyOne(staged, log);
        }

        private static void ApplyOne(string staged, Action<string, bool> log)
        {
            string folder = Path.GetDirectoryName(staged);
            string target = Path.Combine(folder, AutoUpdateNames.Dll);
            string sigPath = Path.Combine(folder, AutoUpdateNames.StagedSig);
            string previous = target + ".previous";
            try
            {
                if (!File.Exists(sigPath))
                {
                    log("IKMA UPDATE: a downloaded update had no signature file; thrown away, nothing installed.", true);
                    return;
                }
                if (!File.Exists(target))
                {
                    log($"IKMA UPDATE: a downloaded update was waiting in {folder}, but IKMA is not there any more; thrown away.", true);
                    return;
                }

                byte[] update = File.ReadAllBytes(staged);
                if (!UpdateSignature.Verify(update, File.ReadAllText(sigPath), out string newVersion, out string why))
                {
                    log($"IKMA UPDATE: the downloaded update FAILED its safety check ({why}). Thrown away; IKMA was left as it was.", true);
                    return;
                }

                string installed = InstalledVersion(target);
                if (installed != null && UpdateSignature.Compare(newVersion, installed) <= 0)
                {
                    log($"IKMA UPDATE: the downloaded version {newVersion} is not newer than the installed {installed}; thrown away.", false);
                    return;
                }

                File.Copy(target, previous, true);
                try
                {
                    File.Copy(staged, target, true);
                }
                catch
                {
                    // A copy that failed half-way must not leave a broken
                    // IKMA behind: put the old one back, then report.
                    File.Copy(previous, target, true);
                    throw;
                }

                File.WriteAllText(Path.Combine(folder, AutoUpdateNames.AppliedNote),
                    (installed ?? "unknown") + "\n" + newVersion + "\n");
                log($"IKMA UPDATE: installed version {newVersion} (was {installed ?? "unknown"}). The old one is kept as {AutoUpdateNames.Dll}.previous.", false);
            }
            finally
            {
                TryDelete(staged);
                TryDelete(sigPath);
            }
        }

        /// <summary>
        /// The installed IKMA's version, read from its [BepInPlugin("id",
        /// "name", "version")] attribute with Mono.Cecil, which reads a DLL
        /// as data without loading it (so it is not locked afterwards).
        /// Null if it cannot be read.
        /// </summary>
        private static string InstalledVersion(string dll)
        {
            try
            {
                using (var asm = AssemblyDefinition.ReadAssembly(dll))
                    foreach (TypeDefinition type in asm.MainModule.Types)
                        foreach (CustomAttribute attr in type.CustomAttributes)
                            if (attr.AttributeType.FullName == "BepInEx.BepInPlugin" && attr.ConstructorArguments.Count == 3)
                                return attr.ConstructorArguments[2].Value as string;
            }
            catch { }
            return null;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    /// <summary>
    /// The file names, matching AutoUpdate.cs in the mod (which this DLL does
    /// not reference - it runs before the mod loads).
    /// </summary>
    internal static class AutoUpdateNames
    {
        internal const string Dll = "IKMAccess.dll";
        internal const string StagedDll = "IKMAccess.dll.update";
        internal const string StagedSig = "IKMAccess.dll.update.sig";
        internal const string AppliedNote = "IKMA_updated.txt";
    }
}
