// AutoUpdate.cs
using System;
using System.Collections;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace IKMA
{
    /// <summary>
    /// The four update modes, set in [Updates] Mode or in IKMA Setup.
    /// </summary>
    internal enum UpdateMode
    {
        /// <summary>Download and install with no question. The default.</summary>
        Automatic,
        /// <summary>Download, then ask before installing.</summary>
        DownloadThenAsk,
        /// <summary>Ask before downloading anything.</summary>
        AskFirst,
        /// <summary>Never connect to the internet.</summary>
        Off,
    }

    /// <summary>
    /// Updates from GitHub. (Session 32. BUILT, NEVER RUN.)
    /// </summary>
    /// <remarks>
    /// Zamar's calls (Session 32): fully automatic by default; players can
    /// switch to "download, then ask", "ask before downloading", or off. The
    /// asking modes are answered IN GAME with two keys.
    ///
    /// HOW AN UPDATE HAPPENS, across two game starts:
    ///
    ///   Start 1 (this file). 20 seconds after the mod loads, IKMA asks
    ///   GitHub for the latest release of the repository below. If that
    ///   release carries a signed IKMAccess.dll NEWER than this one, then
    ///   depending on the mode IKMA downloads it, checks the signature
    ///   (UpdateSignature.cs), and saves it next to itself:
    ///     IKMAccess.dll.update   approved - installs at the next start
    ///     IKMAccess.dll.pending  downloaded, waiting for the player's yes
    ///   Nothing is swapped yet: Windows locks a DLL while the game uses it.
    ///
    ///   Start 2 (tools\patcher, IKMAUpdater.dll in BepInEx\patchers). BepInEx
    ///   runs patchers BEFORE it loads any plugin, so the old DLL is not in
    ///   use yet. The patcher installs ONLY a .update file - never a .pending
    ///   one - after checking its signature again. IKMA then loads as the new
    ///   version, reads the patcher's note, and says so.
    ///
    /// THE QUESTION (DownloadThenAsk and AskFirst). Said once when it arises,
    /// as a Quiet line, naming both keys. F9 answers yes, F10 no. Both keys
    /// work anywhere for the rest of that session; neither is used by IKMA or
    /// the game, and neither types a character into the death card name. It
    /// REPEATS on the idle rule (5.5 s after the last key, then every 15 s)
    /// only on the title screen, which has no idle prompt of its own, and
    /// never during a run. Unanswered, it is asked again next start.
    /// No = that version is skipped for good ([Updates] SkippedVersion); a
    /// later version is asked about as normal.
    ///
    /// WHAT IT WILL NOT DO:
    ///   - Install anything the release key did not sign (UpdateSignature.cs).
    ///   - Go backwards: the signed version must be newer than this one.
    ///   - Connect at all in Off mode, or when no key is built in
    ///     (UpdateKey.cs empty), or when the patcher is missing.
    ///   - Touch the speech DLLs or BepInEx. Only IKMAccess.dll updates this
    ///     way; a release that changes those needs IKMA Setup.
    ///
    /// GITHUB'S "LATEST RELEASE" is the newest one not marked pre-release, and
    /// THE REPOSITORY MUST BE PUBLIC (checked Session 32: it answered 404).
    /// </remarks>
    internal static class AutoUpdate
    {
        // The one place the repository is named.
        internal const string Repository = "Zamar21/InscryptionAccess";

        // The two release assets, named exactly this on the release page.
        internal const string DllAsset = "IKMAccess.dll";
        internal const string SigAsset = "IKMAccess.dll.sig";

        // Saved next to the running IKMAccess.dll. NOT ending in .dll on
        // purpose: BepInEx loads every *.dll under plugins, and a second IKMA
        // would patch every method twice.
        internal const string StagedDll  = "IKMAccess.dll.update";
        internal const string StagedSig  = "IKMAccess.dll.update.sig";
        internal const string PendingDll = "IKMAccess.dll.pending";
        internal const string PendingSig = "IKMAccess.dll.pending.sig";

        // Left by the patcher after it swaps: line 1 the old version, line 2 the new.
        internal const string AppliedNote = "IKMA_updated.txt";

        internal const string PatcherFile = "IKMAUpdater.dll";

        // The answer keys. PROVISIONAL (Claude's proposal; Zamar picks).
        internal const KeyCode YesKey = KeyCode.F9;
        internal const KeyCode NoKey  = KeyCode.F10;

        private const float StartDelaySeconds = 20f;
        private const int TimeoutSeconds = 30;
        private const int MaxDownloadBytes = 20 * 1024 * 1024;

        // The idle rule, same numbers as every IKMA idle loop.
        private const float IdleFirst  = 5.5f;
        private const float IdleRepeat = 15f;

        internal static ConfigEntry<UpdateMode> Mode;
        internal static ConfigEntry<string> SkippedVersion;
        private static ManualLogSource _log;
        private static MonoBehaviour _host;

        private enum Question { None, Download, Install }
        private static Question _question = Question.None;
        private static string _questionVersion;
        private static string _approvedDllUrl;   // AskFirst: fetched after a yes
        private static string _approvedSigText;
        private static float _idle;
        private static float _interval = IdleFirst;

        internal static void BindConfig(ConfigFile config)
        {
            Mode = config.Bind("Updates", "Mode", UpdateMode.Automatic,
                "Automatic = download and install new IKMA versions with no question. " +
                "DownloadThenAsk = download, then ask before installing. " +
                "AskFirst = ask before downloading. " +
                "Off = IKMA never connects to the internet. " +
                "The questions are answered in game: F9 yes, F10 no.");
            SkippedVersion = config.Bind("Updates", "SkippedVersion", "",
                "A version the player chose to skip (F10). IKMA does not offer it again. Set by IKMA; empty = none.");
        }

        /// <summary>
        /// Called once, at the end of Plugin.Awake, after the load line is
        /// queued - so "updated from" follows "IKMA version X loaded".
        /// </summary>
        internal static void Start(MonoBehaviour host, ManualLogSource log)
        {
            _host = host;
            _log = log;
            try
            {
                AnnounceAppliedUpdate();

                UpdateMode mode = Mode?.Value ?? UpdateMode.Automatic;
                if (mode == UpdateMode.Off)
                {
                    _log.LogInfo("IKMA UPDATE: off ([Updates] Mode = Off). IKMA did not connect to the internet.");
                    return;
                }
                if (string.IsNullOrEmpty(UpdateKey.PublicKeyXml))
                {
                    _log.LogInfo("IKMA UPDATE: no update key is built into this IKMA, so it cannot check a download. Nothing downloaded.");
                    return;
                }
                if (!PatcherInstalled())
                {
                    _log.LogWarning($"IKMA UPDATE: {PatcherFile} is not in BepInEx\\patchers, so an update could never be installed. Nothing downloaded; run IKMA Setup to add it.");
                    return;
                }
                _host.StartCoroutine(Check(mode));
            }
            catch (Exception e)
            {
                _log.LogWarning($"IKMA UPDATE: could not start the update check: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // After the patcher swapped: say it once.
        // ------------------------------------------------------------------

        private static void AnnounceAppliedUpdate()
        {
            string note = Path.Combine(OwnFolder(), AppliedNote);
            if (!File.Exists(note)) return;

            string previous = null;
            try
            {
                string[] lines = File.ReadAllLines(note);
                if (lines.Length > 0) previous = lines[0].Trim();
            }
            catch (Exception e)
            {
                _log.LogWarning($"IKMA UPDATE: could not read {AppliedNote}: {e.Message}");
            }
            TryDelete(note);

            if (string.IsNullOrEmpty(previous)) previous = Vocabulary.Mod.UnknownVersion;
            _log.LogInfo($"IKMA UPDATE: this start installed version {Plugin.PluginVersion}, replacing {previous}.");
            // Zamar, Session 32 (applied Session 34): SILENT. The log line
            // above keeps the record; nothing is spoken.
        }

        // ------------------------------------------------------------------
        // The check. A coroutine: Unity's web requests are waited on with
        // yield, so the game never stalls on the network.
        // ------------------------------------------------------------------

        private sealed class Download
        {
            internal byte[] Data;
            internal string Error;
        }

        // JsonUtility (Unity's JSON reader) fills public fields whose names
        // match the JSON keys, and ignores every other key GitHub sends.
        // The compiler cannot see JsonUtility writing these fields and warns
        // (CS0649) that they are never assigned; they are, at runtime.
#pragma warning disable CS0649
        [Serializable]
        private class ReleaseJson
        {
            public string tag_name;
            public AssetJson[] assets;
        }

        [Serializable]
        private class AssetJson
        {
            public string name;
            public string browser_download_url;
            public int size;
        }
#pragma warning restore CS0649

        private static IEnumerator Check(UpdateMode mode)
        {
            yield return new WaitForSecondsRealtime(StartDelaySeconds);

            string current = Plugin.PluginVersion;

            // 0. A download already waiting from an earlier start.
            if (ResolveWaitingDownload(mode, current)) yield break;

            // 1. Ask GitHub what the latest release is.
            var api = new Download();
            yield return _host.StartCoroutine(Fetch($"https://api.github.com/repos/{Repository}/releases/latest", api));
            if (api.Error != null)
            {
                // Offline, GitHub down, or the repository is private. Not the
                // player's problem to hear about; next start tries again.
                _log.LogInfo($"IKMA UPDATE: could not ask GitHub for the latest release ({api.Error}). Nothing downloaded, nothing spoken; the next start tries again.");
                yield break;
            }

            if (!FindAssets(api.Data, out string tag, out string dllUrl, out string sigUrl, out int dllSize)) yield break;

            // 2. The small signature file first: its version says whether the
            //    big download is worth making. (Only a hint until it verifies.)
            var sig = new Download();
            yield return _host.StartCoroutine(Fetch(sigUrl, sig));
            if (sig.Error != null)
            {
                _log.LogInfo($"IKMA UPDATE: could not download {SigAsset} from release {tag} ({sig.Error}). Nothing installed; the next start tries again.");
                yield break;
            }
            string sigText = Encoding.UTF8.GetString(sig.Data);
            string claimed = UpdateSignature.ClaimedVersion(sigText);
            if (claimed == null)
            {
                _log.LogWarning($"IKMA UPDATE: {SigAsset} in release {tag} is not in IKMA's format. Nothing downloaded.");
                yield break;
            }
            if (UpdateSignature.Compare(claimed, current) <= 0)
            {
                _log.LogInfo($"IKMA UPDATE: up to date (this is {current}; the latest release is {claimed}).");
                yield break;
            }
            // A skip holds in every mode: a player who said no to a version
            // did not want it, whichever mode they have switched to since.
            if (string.Equals(SkippedVersion?.Value, claimed, StringComparison.Ordinal))
            {
                _log.LogInfo($"IKMA UPDATE: version {claimed} was skipped by the player (F10). Not offered again; nothing spoken.");
                yield break;
            }
            if (HasVerified(StagedDll, StagedSig, claimed))
            {
                _log.LogInfo($"IKMA UPDATE: version {claimed} is already downloaded and installs at the next start. Nothing spoken again.");
                yield break;
            }
            if (dllSize > MaxDownloadBytes)
            {
                _log.LogWarning($"IKMA UPDATE: {DllAsset} in release {tag} is {dllSize} bytes, far larger than IKMA. Not downloaded.");
                yield break;
            }

            // AskFirst: stop here and ask. The download happens on a yes.
            if (mode == UpdateMode.AskFirst)
            {
                _approvedDllUrl = dllUrl;
                _approvedSigText = sigText;
                Ask(Question.Download, claimed);
                yield break;
            }

            // 3. The DLL.
            var dll = new Download();
            yield return _host.StartCoroutine(Fetch(dllUrl, dll));
            if (dll.Error != null)
            {
                _log.LogInfo($"IKMA UPDATE: could not download {DllAsset} from release {tag} ({dll.Error}). Nothing installed; the next start tries again.");
                yield break;
            }

            // 4. The check that decides, then save and (maybe) ask.
            bool approved = mode == UpdateMode.Automatic;
            string version = VerifyAndSave(dll.Data, sigText, current, approved);
            if (version == null) yield break;

            if (approved)
            {
                _log.LogInfo("IKMA PROVISIONAL: the update-downloaded line (wording is Claude's, not Zamar's).");
                Speech.Quiet(Vocabulary.Mod.UpdateDownloaded(version));
            }
            else Ask(Question.Install, version);
        }

        /// <summary>
        /// A .pending download left by an earlier start. True if it was
        /// handled (so there is no need to ask GitHub again this start).
        /// </summary>
        private static bool ResolveWaitingDownload(UpdateMode mode, string current)
        {
            string folder = OwnFolder();
            string dllPath = Path.Combine(folder, PendingDll);
            if (!File.Exists(dllPath)) return false;

            string version = VerifiedVersion(PendingDll, PendingSig);
            if (version == null || UpdateSignature.Compare(version, current) <= 0
                || string.Equals(SkippedVersion?.Value, version, StringComparison.Ordinal))
            {
                // Not valid, not newer, or skipped: it is no use to anyone.
                DeletePending();
                return false;
            }

            if (mode == UpdateMode.Automatic)
            {
                // The player switched to Automatic since: no question to ask.
                Approve(version);
                return true;
            }
            _log.LogInfo($"IKMA UPDATE: version {version} was downloaded at an earlier start and is still waiting for an answer.");
            Ask(Question.Install, version);
            return true;
        }

        // ------------------------------------------------------------------
        // The question, and its two keys.
        // ------------------------------------------------------------------

        private static void Ask(Question q, string version)
        {
            _question = q;
            _questionVersion = version;
            _idle = 0f;
            _interval = IdleFirst;
            _log.LogInfo($"IKMA UPDATE: asking the player about version {version} ({q}); F9 yes, F10 no.");
            _log.LogInfo("IKMA PROVISIONAL: the update question and its keys (Claude's proposal, not Zamar's).");
            Speech.Quiet(QuestionText());
        }

        private static string QuestionText()
            => _question == Question.Download
                ? Vocabulary.Mod.UpdateAvailableAsk(_questionVersion)
                : Vocabulary.Mod.UpdateDownloadedAsk(_questionVersion);

        /// <summary>
        /// Called every frame from HotkeyManager, before anything else reads
        /// keys. Returns at once when there is no question (the normal case).
        /// True only when F9 or F10 answered a question this frame.
        /// </summary>
        internal static bool Tick()
        {
            if (_question == Question.None) return false;

            if (Input.GetKeyDown(YesKey)) { Answer(true); return true; }
            if (Input.GetKeyDown(NoKey))  { Answer(false); return true; }

            // The idle repeat: title screen only, and not over its options panel.
            if (!TitleScreenReader.Active || TitleScreenReader.OptionsOpen || Input.anyKeyDown)
            {
                _idle = 0f;
                _interval = IdleFirst;
                return false;
            }
            _idle += Time.unscaledDeltaTime;
            if (_idle >= _interval)
            {
                _idle = 0f;
                _interval = IdleRepeat;
                Speech.Prompt(QuestionText);
            }
            return false;
        }

        private static void Answer(bool yes)
        {
            Question q = _question;
            string version = _questionVersion;
            _question = Question.None;
            _log.LogInfo($"IKMA UPDATE: the player answered {(yes ? "yes (F9)" : "no (F10)")} for version {version}.");

            if (!yes)
            {
                if (SkippedVersion != null) SkippedVersion.Value = version;
                DeletePending();
                Speech.Confirm(ReviewHistory.NotEvent(Vocabulary.Mod.UpdateSkipped(version)));   // Session 38: mod message
                return;
            }

            if (q == Question.Install)
            {
                Approve(version);
                return;
            }

            // Download question: fetch it now.
            Speech.Confirm(ReviewHistory.NotEvent(Vocabulary.Mod.UpdateDownloading(version)));
            _host.StartCoroutine(DownloadApproved());
        }

        private static IEnumerator DownloadApproved()
        {
            var dll = new Download();
            yield return _host.StartCoroutine(Fetch(_approvedDllUrl, dll));
            _safetyFailureSpoken = false;
            string version = dll.Error == null
                ? VerifyAndSave(dll.Data, _approvedSigText, Plugin.PluginVersion, approved: true)
                : null;
            if (dll.Error != null)
                _log.LogInfo($"IKMA UPDATE: the approved download failed ({dll.Error}).");

            if (version == null)
            {
                // True either way: nothing was installed, and not being
                // skipped, it is offered again next start. Not after the
                // safety-check line, which already said what happened.
                if (!_safetyFailureSpoken) Speech.Quiet(Vocabulary.Mod.UpdateDownloadFailed);
                yield break;
            }
            Speech.Quiet(Vocabulary.Mod.UpdateDownloaded(version));
        }

        // .pending -> .update: the patcher installs it at the next start.
        private static void Approve(string version)
        {
            try
            {
                string folder = OwnFolder();
                Replace(Path.Combine(folder, PendingSig), Path.Combine(folder, StagedSig));
                Replace(Path.Combine(folder, PendingDll), Path.Combine(folder, StagedDll));
                _log.LogInfo($"IKMA UPDATE: version {version} approved; it installs at the next start.");
                Speech.Confirm(ReviewHistory.NotEvent(Vocabulary.Mod.UpdateWillInstall(version)));
            }
            catch (Exception e)
            {
                _log.LogWarning($"IKMA UPDATE: could not mark version {version} for install: {e.Message}.");
                Speech.Quiet(Vocabulary.Mod.UpdateDownloadFailed);
            }
        }

        // ------------------------------------------------------------------
        // Network and files.
        // ------------------------------------------------------------------

        private static IEnumerator Fetch(string url, Download into)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                // GitHub's API refuses requests with no User-Agent.
                req.SetRequestHeader("User-Agent", "IKMA-updater");
                // The JSON "Accept" header is for the API question only. The
                // two downloads are plain files (github.com redirects them to
                // its file servers); asking a file link for JSON is at best
                // ignored and at worst refused, so it is not sent there.
                // (Session 32 bug-hunt change.)
                if (url.StartsWith("https://api.github.com/", StringComparison.OrdinalIgnoreCase))
                    req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();

                // Unity 2019 (this game): isNetworkError / isHttpError. The
                // newer "result" property does not exist here (checked against
                // the game's UnityEngine.UnityWebRequestModule.dll).
                if (req.isNetworkError || req.isHttpError)
                    into.Error = req.responseCode > 0 ? $"HTTP {req.responseCode}" : (req.error ?? "network error");
                else if (req.downloadHandler.data == null || req.downloadHandler.data.Length > MaxDownloadBytes)
                    into.Error = "empty or oversized reply";
                else
                    into.Data = req.downloadHandler.data;
            }
        }

        private static bool FindAssets(byte[] json, out string tag, out string dllUrl, out string sigUrl, out int dllSize)
        {
            tag = dllUrl = sigUrl = null;
            dllSize = 0;
            try
            {
                var release = JsonUtility.FromJson<ReleaseJson>(Encoding.UTF8.GetString(json));
                tag = release?.tag_name ?? "?";
                if (release?.assets != null)
                    foreach (var a in release.assets)
                    {
                        if (a == null) continue;
                        if (a.name == DllAsset) { dllUrl = a.browser_download_url; dllSize = a.size; }
                        else if (a.name == SigAsset) sigUrl = a.browser_download_url;
                    }
            }
            catch (Exception e)
            {
                _log.LogWarning($"IKMA UPDATE: GitHub's reply could not be read: {e.Message}. Nothing downloaded.");
                return false;
            }

            // Only HTTPS links from GitHub. Anything else is not a normal
            // release asset link and is not followed.
            if (!IsGitHub(dllUrl) || !IsGitHub(sigUrl))
            {
                _log.LogInfo($"IKMA UPDATE: the latest release ({tag}) has no signed update ({DllAsset} and {SigAsset}). Nothing downloaded.");
                return false;
            }
            return true;
        }

        private static bool IsGitHub(string url)
            => url != null && url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Verify, then save as .update (approved) or .pending (waiting for a
        /// yes). Returns the verified version, or null if nothing was saved.
        /// </summary>
        // Set when the safety-check line was spoken, so the approved-download
        // path does not follow it with its own "could not be downloaded" line.
        private static bool _safetyFailureSpoken;

        private static string VerifyAndSave(byte[] dll, string sigText, string current, bool approved)
        {
            try
            {
                if (!UpdateSignature.Verify(dll, sigText, out string version, out string why))
                {
                    _log.LogWarning($"IKMA UPDATE: the downloaded update FAILED its safety check ({why}). It was thrown away; nothing was installed.");
                    // Zamar, Session 32 (applied Session 34): speak it.
                    Speech.Quiet(Vocabulary.Mod.UpdateFailedSafety);
                    _safetyFailureSpoken = true;
                    return null;
                }
                if (UpdateSignature.Compare(version, current) <= 0)
                {
                    _log.LogInfo($"IKMA UPDATE: signed version {version} is not newer than {current}. Nothing installed.");
                    return null;
                }

                string folder = OwnFolder();
                string dllPath = Path.Combine(folder, approved ? StagedDll : PendingDll);
                string sigPath = Path.Combine(folder, approved ? StagedSig : PendingSig);

                // Write under temporary names, then rename: a crash half-way
                // leaves a .part file, never a half file under the real name.
                File.WriteAllText(sigPath + ".part", sigText);
                File.WriteAllBytes(dllPath + ".part", dll);
                Replace(sigPath + ".part", sigPath);
                Replace(dllPath + ".part", dllPath);

                _log.LogInfo(approved
                    ? $"IKMA UPDATE: version {version} downloaded and its signature checked. It installs the next time the game starts."
                    : $"IKMA UPDATE: version {version} downloaded and its signature checked. Waiting for the player's answer before it can install.");
                return version;
            }
            catch (Exception e)
            {
                _log.LogWarning($"IKMA UPDATE: could not save the update: {e.Message}. Nothing installed.");
                return null;
            }
        }

        private static string VerifiedVersion(string dllName, string sigName)
        {
            try
            {
                string folder = OwnFolder();
                string dllPath = Path.Combine(folder, dllName);
                string sigPath = Path.Combine(folder, sigName);
                if (!File.Exists(dllPath) || !File.Exists(sigPath)) return null;
                return UpdateSignature.Verify(File.ReadAllBytes(dllPath), File.ReadAllText(sigPath), out string v, out _)
                    ? v : null;
            }
            catch { return null; }
        }

        private static bool HasVerified(string dllName, string sigName, string version)
            => VerifiedVersion(dllName, sigName) == version;

        private static void DeletePending()
        {
            string folder = OwnFolder();
            foreach (string name in new[] { PendingDll, PendingSig, PendingDll + ".part", PendingSig + ".part" })
                TryDelete(Path.Combine(folder, name));
        }

        private static bool PatcherInstalled()
        {
            try
            {
                string patchers = Paths.PatcherPluginPath;
                return Directory.Exists(patchers)
                    && Directory.GetFiles(patchers, PatcherFile, SearchOption.AllDirectories).Length > 0;
            }
            catch { return false; }
        }

        // The folder IKMAccess.dll was loaded from: BepInEx\plugins, or a
        // subfolder of it in a mod-manager layout.
        private static string OwnFolder()
            => Path.GetDirectoryName(typeof(AutoUpdate).Assembly.Location);

        private static void Replace(string from, string to)
        {
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
