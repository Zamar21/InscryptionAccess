// Program.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace IKMASetup
{
    // ======================================================================
    // IKMA_Manager.exe (was IKMA_Setup.exe; renamed Session 34, his name) - the Windows installer and settings. (Session 32. DRAFT, NEVER RUN.)
    //
    // WHAT A PLAYER DOES: download IKMA_Manager.exe, double-click it, press
    // Enter. That is the whole install. No zip to unpack, no folder to find.
    //
    // IKMA SETUP (Session 32, Zamar: one program with a menu). The same
    // Enter still installs; a numbered menu also offers settings (see
    // Settings.cs), turning IKMA off or on, uninstall, and quit.
    //
    // WHY A CONSOLE WINDOW AND NOT A WIZARD. A console is plain text, one line
    // at a time, and every screen reader reads it as it appears - NVDA,
    // JAWS and Narrator alike. A graphical wizard's accessibility depends on
    // the toolkit and is easy to get wrong. Every question here is answered by
    // typing and Enter; nothing needs a mouse.
    //
    // WHAT IS INSIDE. Everything the install needs is embedded in this one
    // file as resources (see IKMAInstaller.csproj): the official BepInEx 5
    // zip for Windows x86 (unmodified), IKMAccess.dll, the two 32-bit speech
    // DLLs, and the licence texts. One signed file carries all of it, so a
    // code signature on the .exe vouches for every byte it installs.
    //
    // WHAT IT NEVER DOES - the safety rules this file is built around:
    //   - No network. It downloads nothing and contacts nothing.
    //   - No administrator rights asked for. Steam's library folders are
    //     writable by the user; if one is not, it says so and stops.
    //   - It writes only inside the Inscryption folder it found, and only the
    //     files listed below. It never deletes a file it did not install.
    //   - It never touches Steam's settings or the registry (it only READS
    //     where Steam is installed).
    //
    // .NET Framework 4.8, which ships with Windows 10 and 11, so nothing has
    // to be installed first.
    // ======================================================================

    /// <summary>
    /// Every sentence the installer prints. PROVISIONAL - these are Claude's
    /// words, written so the installer works; Zamar rewrites them. Kept in one
    /// place so rewriting them touches nothing else.
    /// </summary>
    internal static class Text
    {
        // SESSION 32: EVERY SENTENCE IS WRAPPED in L.T (fixed) or L.F (with
        // blanks) - see SetupLoc.cs. In English each returns exactly the
        // sentence written here, so English Setup reads as before. The
        // "const"s became properties because a translation is looked up when
        // the sentence is printed, not when the program is compiled.
        internal static string Title(string v) => L.F($"IKMA Manager, version {v}.");   // renamed from IKMA Setup, Zamar Session 33
        internal static string Looking => L.T("Looking for Inscryption in your Steam library.");
        internal static string Found(string path) => L.F($"Found Inscryption at {path}.");
        internal static string NotFound => L.T("Inscryption was not found in your Steam library.");
        internal static string AskFolder => L.T("Type or paste the full path of your Inscryption folder, then press Enter. Or just press Enter to quit.");
        internal static string NotAGameFolder => L.T("That folder does not contain Inscryption.exe.");

        // Session 32 - the IKMA Setup menu (Zamar: one program with a menu).
        // Each variant is one whole sentence, so a translator never has to
        // glue "IKMA version 0.7" onto a sentence built in another language.
        internal static string StatusLine(Program.Status s, string v)
        {
            switch (s)
            {
                case Program.Status.On:
                    return v != null ? L.F($"IKMA version {v} is installed and turned on.")
                                     : L.T("IKMA is installed and turned on.");
                case Program.Status.Off:
                    return v != null ? L.F($"IKMA version {v} is installed but turned off. Inscryption starts without it.")
                                     : L.T("IKMA is installed but turned off. Inscryption starts without it.");
                default:
                    return L.T("IKMA is not installed in this game folder.");
            }
        }
        internal static string MainMenu(Program.Status s)
            => s == Program.Status.Off
                ? L.T("Main menu. Press Enter to install or update IKMA. Or type a number, then press Enter. 1, install or update. 2, settings. 3, turn IKMA back on. 4, uninstall. 5, quit.")
                : L.T("Main menu. Press Enter to install or update IKMA. Or type a number, then press Enter. 1, install or update. 2, settings. 3, turn IKMA off. 4, uninstall. 5, quit.");
        internal static string NotAChoice => L.T("Invalid choice.");
        internal static string NotInstalledNothingToDo => L.T("IKMA is not installed, so there is nothing to turn off.");
        internal static string TurnedOff => L.T("IKMA is turned off. Inscryption starts without it. BepInEx and other mods are unchanged, and IKMA's settings are kept.");
        internal static string TurnedOn => L.T("IKMA is turned back on. Please restart the game.");
        internal static string TurnedBackOnByInstall => L.T("IKMA was turned off. Installing turned it back on.");
        internal static string SettingsMenu => L.T("Settings. Type a number, then press Enter to change it. Press Enter to go back to the main menu. Changes take effect the next time you start the game.");
        internal static string SettingLine(int n, string title, string current) => L.F($"{n}, {title}: {current}");
        internal static string ChooseFor(string title) => L.F($"{title}. Type a number, then press Enter. Press Enter to keep it as it is.");
        internal static string OptionLine(int n, string name, bool current) => current ? L.F($"{n}, {name} This is the current setting.") : $"{n}, {name}";
        internal static string SettingSaved(string title, string name) => L.F($"{title} set to: {name}");
        internal static string CustomValue(string raw) => L.F($"a custom value, {raw}.");
        internal static string SettingUpdates => L.T("Updates");
        internal static string SettingSpeech => L.T("Speech engine");
        internal static string SettingNvdaTiming => L.T("NVDA line timing");   // Claude, Session 50
        internal static string SettingBraille => L.T("Braille display");      // Claude, Session 50
        internal static string SettingLanguage => L.T("Language");
        internal static string SettingFullLog => L.T("Full log for bug reports");
        internal static string SettingVibration => L.T("Controller vibration");   // Claude, Session 34; OK Zamar, Session 35
        internal static string SettingControlTemplate => L.T("Control template");   // Zamar, Session 59
        // Controller buttons menu (ControllerMap.cs). Claude, Session 34, ALL PROVISIONAL.
        internal static string SettingController => L.T("Controller buttons");
        internal static string ControllerSummary(int moved) => moved == 0 ? L.T("every action on its default button") : L.F($"{moved} moved");
        internal static string ControllerMenu => L.T("Controller buttons. Type a number, then press Enter to move that action to another button. Type R, then press Enter, to put every action back on its default button. Press Enter alone to go back to settings.");
        internal static string ActionLine(int n, string name, string current, string def) => def == null ? L.F($"{n}, {name}: {current}") : L.F($"{n}, {name}: {current}. Default {def}.");
        internal static string ControllerReset => L.T("Every action is back on its default button.");
        // Events menu (EventsMenu.cs), Session 37. "Events", "checked" and
        // "unchecked" are Say the Spire 2's; the sentences are PROVISIONAL.
        internal static string SettingEvents => L.T("Events");
        internal static string EventsSummary(int changed) => changed == 0 ? L.T("every event at its default") : L.F($"{changed} changed");
        internal static string EventsMenu => L.T("Events. Type a number, then press Enter to change that event. Press Enter alone to go back to settings.");
        internal static string EventLine(int n, string group, string label) => L.F($"{n}, {group}, {label}");
        internal static string EventMenu(string label) => L.F($"{label}. Type a number, then press Enter to switch it. Press Enter alone to go back to the events.");
        internal static string SwitchLine(int n, string label, bool on) => on ? L.F($"{n}, {label}: checked") : L.F($"{n}, {label}: unchecked");
        internal static string Checked => L.T("checked");
        internal static string Unchecked => L.T("unchecked");
        internal static string ChooseChord(string name, string current) => L.F($"{name}, now on {current}. Which shoulder buttons are held with it? Type a number, then press Enter. 1, none. 2, LB. 3, RB. 4, LB and RB together. Press Enter alone to keep it as it is.");
        internal static string ChooseButton(string name) => L.F($"Which button for {name}? Type a number, then press Enter. Press Enter alone to keep it as it is.");
        internal static string ButtonLine(int n, string gesture, string holder) => holder == null ? $"{n}, {gesture}" : L.F($"{n}, {gesture}, used by {holder}.");
        internal static string ThisAction => L.T("this action");
        internal static string Swapped(string name, string gesture, string other, string otherGesture) => L.F($"Swapped. {name} is now on {gesture}, and {other} is now on {otherGesture}.");
        internal static string Plus => L.T(" plus ");
        internal static string CloseGame => L.T("Inscryption is running. Please close the game, then press Enter.");
        internal static string WrongArch => L.T("This Inscryption is not the 32-bit version this installer was made for. Nothing was changed.");
        internal static string BepInExKept => L.T("BepInEx is already installed.");
        internal static string BepInExInstalled => L.T("Installed BepInEx, the mod loader.");
        internal static string IkmaInstalled => L.T("Installed IKMA and its speech files.");
        internal static string Done => L.T("Done. Start Inscryption from Steam as usual. IKMA will speak at the title screen.");
        internal static string Removed => L.T("IKMA is uninstalled. BepInEx was left in place, because other mods may use it.");
        internal static string NothingToRemove => L.T("IKMA was not installed in this folder.");
        internal static string NoAccess => L.T("Windows would not let the installer write to the game folder. Close this window, right-click IKMA_Manager and choose Run as administrator.");
        internal static string PayloadMissing => L.T("This installer is incomplete: a file it carries is missing. Download it again.");
        internal static string Failed(string why) => L.F($"Something went wrong, and the install stopped: {why}");
        internal static string PressEnterToClose => L.T("Press Enter to close.");

        // Session 32 - an existing mod setup (Zamar: "There's a chance they
        // already have bepinex though from other mods ... nothing breaks").
        internal static string BepInExKeptVersion(string v) => L.F($"BepInEx {v} is already installed.");
        internal static string BepInEx6 => L.T("This game has BepInEx 6 installed. IKMA needs BepInEx 5, and replacing it would break your other mods. Nothing was changed.");
        internal static string OtherLoader(string name) => L.F($"This game has another mod loader installed ({name}). Adding BepInEx beside it could break your other mods. Nothing was changed.");
        internal static string BepInEx64 => L.T("The BepInEx in this game folder is the 64-bit build, which cannot load in this 32-bit game. Nothing was changed; it needs the 32-bit BepInEx.");
        // The letter Y stays Y in every language: it is what the code checks.
        internal static string BepInExNoStarter => L.T("BepInEx is here, but the file that starts it with the game (winhttp.dll) is missing. Type Y and press Enter to add it. Press Enter alone to leave everything as it is.");
        internal static string BepInExStarterAdded => L.T("Added the file that starts BepInEx.");
        internal static string BepInExLeftAlone => L.T("Left BepInEx as it is. IKMA is installed but will not load until BepInEx starts with the game.");
        internal static string DoorstopDisabled => L.T("Note: BepInEx is switched off in doorstop_config.ini (enabled=false). IKMA will not load until it is switched on. The installer did not change it.");
        internal static string IkmaUpdatedAt(string path) => L.F($"Found an earlier IKMA at {path}. Updated it there.");
        internal static string IkmaSeveral(string list) => L.F($"More than one copy of IKMA is installed: {list}. Remove all but one, then run this again. Nothing was changed.");
        internal static string SpeechBackedUp(string name) => L.F($"{name} from another mod was saved as {name}.before-IKMA. Uninstalling IKMA puts it back.");
        internal static string Restored(string name) => L.F($"Put back the earlier {name}.");
        internal static string ModManagerNote => L.T("Note: a mod manager (r2modman or Thunderstore) is set up for Inscryption on this PC. If you start the game from the mod manager, add IKMA to that profile too; this installer set up the game started from Steam.");
    }

    internal static class Program
    {
        private const string GameExe = "Inscryption.exe";

        // The two speech DLLs sit next to Inscryption.exe (that is where
        // Windows looks for them). Another accessibility mod may have put its
        // own copy there too - see InstallSpeechDll.
        private static readonly string[] SpeechDlls = { "UniversalSpeech.dll", "nvdaControllerClient.dll" };
        private const string BackupSuffix = ".before-IKMA";

        // Turning IKMA off renames IKMAccess.dll to this. BepInEx loads only
        // files ending in .dll, so the game starts without IKMA and nothing
        // else - BepInEx, other mods, IKMA's settings - is touched.
        private const string DisabledSuffix = ".disabled";
        private const string LicenceFolder = @"BepInEx\plugins\IKMA_licenses";

        // Auto-update (Session 32): the preloader patcher that swaps in a
        // downloaded update at game start (IKMAccess\tools\patcher).
        private const string Patcher = @"BepInEx\patchers\IKMAUpdater.dll";

        // Files IKMA's own auto-update may leave next to IKMAccess.dll (see
        // AutoUpdate.cs). IKMA wrote them, so uninstall removes them.
        private static readonly string[] UpdateLeftovers =
        {
            "IKMAccess.dll.previous", "IKMAccess.dll.update", "IKMAccess.dll.update.sig",
            "IKMAccess.dll.update.part", "IKMAccess.dll.update.sig.part", "IKMA_updated.txt",
            "IKMAccess.dll.pending", "IKMAccess.dll.pending.sig", "IKMAccess.dll.disabled",
            "IKMAccess.dll.pending.part", "IKMAccess.dll.pending.sig.part",
        };

        // THE RECEIPT. Every file this installer wrote, one relative path per
        // line, plus "backup <path>" for every file it set aside. Uninstall
        // reads it, so it removes exactly what was installed - wherever an
        // earlier IKMA lived - and puts every set-aside file back.
        private const string Receipt = @"BepInEx\plugins\IKMA_install.txt";

        private static int Main(string[] args)
        {
            // Pick Setup's language before the first sentence (SetupLoc.cs).
            L.Start(args);
            try { return Run(); }
            catch (UnauthorizedAccessException) { Say(Text.NoAccess); }
            catch (Exception e) { Say(Text.Failed(e.Message)); }
            Close();
            return 1;
        }

        private static int Run()
        {
            Say(Text.Title(Version()));
            Say(Text.Looking);

            string game = FindGame();
            if (game == null)
            {
                Say(Text.NotFound);
                game = AskForFolder();
                if (game == null) return 1;
            }
            Say(Text.Found(game));

            // The payload is 32-bit; installing it into a different build of
            // the game would fail silently at launch. Refuse instead.
            if (PeMachine(Path.Combine(game, GameExe)) != "x86")
            {
                Say(Text.WrongArch);
                Close();
                return 1;
            }

            // THE MAIN MENU. Enter alone installs or updates - the whole
            // install is still "double-click, press Enter". Settings and
            // turning IKMA off or on come back to this menu; install and
            // uninstall end the program, so a second Enter cannot repeat them.
            while (true)
            {
                Status state = CurrentStatus(game, out string version);
                Say(Text.StatusLine(state, version));
                Say(Text.MainMenu(state));

                string answer = ReadLine().ToUpperInvariant();
                switch (answer)
                {
                    case "":
                    case "1":
                        WaitForGameToClose();
                        Install(game);
                        Close();
                        return 0;

                    case "2":
                        WaitForGameToClose();
                        Settings.Run(game);
                        break;

                    case "3":
                        if (state == Status.NotInstalled) { Say(Text.NotInstalledNothingToDo); break; }
                        WaitForGameToClose();
                        TurnOnOrOff(game, state);
                        break;

                    case "4":
                        WaitForGameToClose();
                        Uninstall(game);
                        Close();
                        return 0;

                    case "5":
                    case "Q":
                        return 0;

                    default:
                        Say(Text.NotAChoice);
                        break;
                }
            }
        }

        // Files in use cannot be replaced, and BepInEx rewrites the config
        // file while the game runs. Wait for the player, however long it takes.
        private static void WaitForGameToClose()
        {
            while (Process.GetProcessesByName("Inscryption").Length > 0)
            {
                Say(Text.CloseGame);
                Console.ReadLine();
            }
        }

        // ------------------------------------------------------------------
        // Status, and turning IKMA off or on
        // ------------------------------------------------------------------

        internal enum Status { NotInstalled, On, Off }

        private static Status CurrentStatus(string game, out string version)
        {
            version = null;
            string plugins = Path.Combine(game, @"BepInEx\plugins");
            if (!Directory.Exists(plugins)) return Status.NotInstalled;
            string[] on = Directory.GetFiles(plugins, "IKMAccess.dll", SearchOption.AllDirectories);
            if (on.Length > 0) { version = ReadIkmaVersion(on[0]); return Status.On; }
            string[] off = Directory.GetFiles(plugins, "IKMAccess.dll" + DisabledSuffix, SearchOption.AllDirectories);
            if (off.Length > 0) { version = ReadIkmaVersion(off[0]); return Status.Off; }
            return Status.NotInstalled;
        }

        private static void TurnOnOrOff(string game, Status state)
        {
            string plugins = Path.Combine(game, @"BepInEx\plugins");
            if (state == Status.On)
            {
                string[] on = Directory.GetFiles(plugins, "IKMAccess.dll", SearchOption.AllDirectories);
                if (on.Length > 1) { Say(Text.IkmaSeveral(string.Join("; ", on))); return; }
                string off = on[0] + DisabledSuffix;
                if (File.Exists(off)) File.Delete(off);   // a stale switched-off copy
                File.Move(on[0], off);
                Say(Text.TurnedOff);
            }
            else
            {
                string[] off = Directory.GetFiles(plugins, "IKMAccess.dll" + DisabledSuffix, SearchOption.AllDirectories);
                if (off.Length > 1) { Say(Text.IkmaSeveral(string.Join("; ", off))); return; }
                string on = off[0].Substring(0, off[0].Length - DisabledSuffix.Length);
                File.Move(off[0], on);
                Say(Text.TurnedOn);
            }
        }

        /// <summary>
        /// IKMA's version, read from the file as bytes. The attribute
        /// [BepInPlugin("com.zamar.ikma", name, version)] is stored in the DLL
        /// as three length-prefixed UTF-8 strings after the bytes 01 00.
        /// Reading bytes, rather than loading the DLL, keeps the file unlocked
        /// so it can be renamed or replaced a moment later. Null if not found.
        /// </summary>
        private static string ReadIkmaVersion(string dll)
        {
            try
            {
                byte[] b = File.ReadAllBytes(dll);
                byte[] id = Encoding.UTF8.GetBytes("com.zamar.ikma");
                for (int i = 3; i + id.Length < b.Length; i++)
                {
                    if (b[i - 3] != 0x01 || b[i - 2] != 0x00 || b[i - 1] != id.Length) continue;
                    bool match = true;
                    for (int j = 0; j < id.Length && match; j++) match = b[i + j] == id[j];
                    if (!match) continue;
                    int p = i + id.Length;
                    int nameLength = b[p];                 // names here are under 128 bytes
                    p += 1 + nameLength;
                    int versionLength = b[p];
                    return Encoding.UTF8.GetString(b, p + 1, versionLength);
                }
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------------
        // Install / uninstall
        // ------------------------------------------------------------------

        // ==================================================================
        // THE PLAYER MAY ALREADY HAVE A MOD SETUP. (Session 32, Zamar:
        // "There's a chance they already have bepinex though from other
        // mods. Account for that so nothing breaks if true.")
        //
        // The rule: an existing setup is never changed in a way that could
        // break it. Everything the installer finds falls into one case:
        //
        //   Nothing there         -> install the bundled BepInEx 5.
        //   BepInEx 5, working    -> keep it, config and plugins untouched.
        //   BepInEx 5, no starter -> ask before adding winhttp.dll (only the
        //                            missing files, nothing replaced).
        //   BepInEx 5, 64-bit     -> stop: it cannot run in a 32-bit game,
        //                            and "fixing" it is not ours to do.
        //   BepInEx 6             -> stop: IKMA is a BepInEx 5 plugin, and
        //                            downgrading would break their mods.
        //   another loader        -> stop (MelonLoader, or a winhttp.dll
        //                            that is not BepInEx's).
        //
        // And for IKMA's own files: an IKMA already in some plugins subfolder
        // (a mod-manager layout) is updated where it is, never duplicated -
        // two copies would patch every method twice. A speech DLL from
        // another mod is set aside, not overwritten, and put back on
        // uninstall. Nothing is ever deleted that this installer did not
        // write.
        // ==================================================================

        private enum Loader { None, BepInEx5, BepInEx5NoStarter, BepInEx5Wrong64, BepInEx6, Other }

        private static Loader Detect(string game, out string detail)
        {
            detail = null;
            string winhttp = Path.Combine(game, "winhttp.dll");
            string core = Path.Combine(game, @"BepInEx\core");

            if (Directory.Exists(Path.Combine(game, "MelonLoader")))
            { detail = "MelonLoader"; return Loader.Other; }

            if (File.Exists(Path.Combine(core, "BepInEx.Core.dll")) ||
                File.Exists(Path.Combine(core, "BepInEx.Unity.Mono.dll")))
                return Loader.BepInEx6;

            string bep = Path.Combine(core, "BepInEx.dll");
            if (File.Exists(bep))
            {
                try
                {
                    var v = AssemblyName.GetAssemblyName(bep).Version;
                    detail = v.ToString();
                    if (v.Major != 5) return Loader.BepInEx6;
                }
                catch { detail = "5"; }
                if (!File.Exists(winhttp)) return Loader.BepInEx5NoStarter;
                if (PeMachine(winhttp) == "x64") return Loader.BepInEx5Wrong64;
                return Loader.BepInEx5;
            }

            // A winhttp.dll with no BepInEx behind it is some other tool's
            // loader. Adding BepInEx's would replace... nothing, since we never
            // overwrite - but two loaders fighting over one entry point is
            // exactly the breakage to avoid.
            if (File.Exists(winhttp)) { detail = "winhttp.dll without BepInEx"; return Loader.Other; }
            return Loader.None;
        }

        private static bool Install(string game)
        {
            var receipt = new List<string>();

            // --- the mod loader ---
            Loader loader = Detect(game, out string detail);
            switch (loader)
            {
                case Loader.BepInEx6: Say(Text.BepInEx6); return false;
                case Loader.Other: Say(Text.OtherLoader(detail)); return false;
                case Loader.BepInEx5Wrong64: Say(Text.BepInEx64); return false;

                case Loader.BepInEx5:
                    Say(Text.BepInExKeptVersion(detail));
                    break;

                case Loader.BepInEx5NoStarter:
                    Say(Text.BepInExNoStarter);
                    if ((Console.ReadLine() ?? "").Trim().ToUpperInvariant() == "Y")
                    {
                        // Only the files missing from the game folder root -
                        // the starter and its config - and never over
                        // anything already there.
                        using (var zipStream = Resource("bepinex.zip"))
                        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
                            ExtractNoOverwrite(zip, game, rootFilesOnly: true);
                        Say(Text.BepInExStarterAdded);
                    }
                    else Say(Text.BepInExLeftAlone);
                    break;

                default: // None
                    using (var zipStream = Resource("bepinex.zip"))
                    using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
                        ExtractNoOverwrite(zip, game, rootFilesOnly: false);
                    Say(Text.BepInExInstalled);
                    break;
            }

            // A switched-off BepInEx is the player's setting. Say so; never edit it.
            string doorstop = Path.Combine(game, "doorstop_config.ini");
            if (File.Exists(doorstop) &&
                Regex.IsMatch(File.ReadAllText(doorstop), @"^\s*enabled\s*=\s*false", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                Say(Text.DoorstopDisabled);

            // --- IKMA itself: update an existing copy in place, never add a second ---
            string plugins = Path.Combine(game, @"BepInEx\plugins");
            Directory.CreateDirectory(plugins);
            string target = Path.Combine(plugins, "IKMAccess.dll");
            var existing = new List<string>(Directory.GetFiles(plugins, "IKMAccess.dll", SearchOption.AllDirectories));
            if (existing.Count > 1)
            {
                Say(Text.IkmaSeveral(string.Join("; ", existing.ToArray())));
                return false;
            }
            // A copy turned off in the menu (IKMAccess.dll.disabled) counts as
            // the existing copy when no switched-on one exists.
            var disabled = new List<string>(Directory.GetFiles(plugins, "IKMAccess.dll" + DisabledSuffix, SearchOption.AllDirectories));
            if (existing.Count == 0 && disabled.Count == 1)
                existing.Add(disabled[0].Substring(0, disabled[0].Length - DisabledSuffix.Length));
            if (existing.Count == 1 && !SamePath(existing[0], target))
            {
                target = existing[0];
                Say(Text.IkmaUpdatedAt(target));
            }
            WriteResource("IKMAccess.dll", target);
            receipt.Add(Relative(game, target));

            // Installing means the player wants IKMA: a switched-off copy
            // next to the new one would only confuse the next "turn on".
            string wasOff = target + DisabledSuffix;
            if (File.Exists(wasOff))
            {
                File.Delete(wasOff);
                Say(Text.TurnedBackOnByInstall);
            }

            // --- the auto-update patcher ---
            // Always BepInEx\patchers, wherever IKMA itself lives: BepInEx
            // looks for patchers only there. Its name is IKMA's own.
            string patcher = Path.Combine(game, Patcher);
            Directory.CreateDirectory(Path.GetDirectoryName(patcher));
            WriteResource("IKMAUpdater.dll", patcher);
            receipt.Add(Patcher);

            // --- the speech DLLs, next to the game ---
            foreach (string dll in SpeechDlls)
                InstallSpeechDll(game, dll, receipt);

            // --- licences ---
            string lic = Path.Combine(game, LicenceFolder);
            Directory.CreateDirectory(lic);
            foreach (string name in ResourceNames())
                if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResource(name, Path.Combine(lic, name));
                    receipt.Add(Relative(game, Path.Combine(lic, name)));
                }

            // Keep backups recorded by an earlier install: they are still the
            // other mod's files, and uninstall must still put them back.
            string receiptPath = Path.Combine(game, Receipt);
            if (File.Exists(receiptPath))
                foreach (string line in File.ReadAllLines(receiptPath))
                    if (line.StartsWith("backup ", StringComparison.Ordinal) && !receipt.Contains(line))
                        receipt.Add(line);
            // One line per file, whatever order they were added in.
            var unique = new List<string>();
            foreach (string line in receipt)
                if (!unique.Exists(u => string.Equals(u, line, StringComparison.OrdinalIgnoreCase)))
                    unique.Add(line);
            File.WriteAllLines(receiptPath, unique.ToArray());

            Say(Text.IkmaInstalled);
            Say(Text.Done);
            if (ModManagerPresent()) Say(Text.ModManagerNote);
            return true;
        }

        /// <summary>
        /// Put one speech DLL next to the game. Identical file already there:
        /// leave it. A DIFFERENT file there that IKMA did not put there (another
        /// accessibility mod's copy): set it aside as name.before-IKMA, once,
        /// and record it so uninstall restores it.
        /// </summary>
        private static void InstallSpeechDll(string game, string dll, List<string> receipt)
        {
            string target = Path.Combine(game, dll);
            string backup = target + BackupSuffix;
            byte[] ours;
            using (var s = Resource(dll)) using (var m = new MemoryStream()) { s.CopyTo(m); ours = m.ToArray(); }

            if (File.Exists(target))
            {
                byte[] theirs = File.ReadAllBytes(target);
                bool wasOurs = WasInstalledByUs(game, dll);
                if (SameBytes(theirs, ours))
                {
                    // The same file is already there. Only claim it if an
                    // earlier IKMA install wrote it: if another mod put the
                    // same DLL there, uninstalling IKMA must leave it alone.
                    if (wasOurs) receipt.Add(dll);
                    return;
                }
                if (!wasOurs && !File.Exists(backup))
                {
                    File.Copy(target, backup);
                    receipt.Add("backup " + dll);
                    Say(Text.SpeechBackedUp(dll));
                }
            }
            File.WriteAllBytes(target, ours);
            receipt.Add(dll);
        }

        private static void Uninstall(string game)
        {
            string receiptPath = Path.Combine(game, Receipt);
            var lines = new List<string>();
            if (File.Exists(receiptPath)) lines.AddRange(File.ReadAllLines(receiptPath));
            else
            {
                // No receipt (installed by hand, or by a draft installer):
                // the standard locations only.
                lines.Add(@"BepInEx\plugins\IKMAccess.dll");
                lines.Add(Patcher);
                foreach (string d in SpeechDlls) lines.Add(d);
            }

            bool any = false;
            foreach (string line in lines)
            {
                if (line.StartsWith("backup ", StringComparison.Ordinal)) continue;
                string p = Path.Combine(game, line);
                if (File.Exists(p)) { File.Delete(p); any = true; }

                // Next to IKMAccess.dll: whatever auto-update left there.
                if (Path.GetFileName(line).Equals("IKMAccess.dll", StringComparison.OrdinalIgnoreCase))
                    foreach (string extra in UpdateLeftovers)
                    {
                        string e = Path.Combine(Path.GetDirectoryName(p), extra);
                        // any = true: a copy turned off in the menu exists only
                        // as IKMAccess.dll.disabled, one of these leftovers,
                        // and removing it IS removing IKMA. (Session 32 bug
                        // hunt: it used to end with "nothing to remove".)
                        if (File.Exists(e)) { File.Delete(e); any = true; }
                    }
            }
            // Put back what was set aside.
            foreach (string line in lines)
            {
                if (!line.StartsWith("backup ", StringComparison.Ordinal)) continue;
                string rel = line.Substring("backup ".Length);
                string original = Path.Combine(game, rel);
                string backup = original + BackupSuffix;
                if (File.Exists(backup) && !File.Exists(original))
                {
                    File.Move(backup, original);
                    Say(Text.Restored(rel));
                }
            }
            string lic = Path.Combine(game, LicenceFolder);
            if (Directory.Exists(lic)) { Directory.Delete(lic, true); any = true; }
            if (File.Exists(receiptPath)) File.Delete(receiptPath);
            Say(any ? Text.Removed : Text.NothingToRemove);
        }

        private static bool WasInstalledByUs(string game, string rel)
        {
            string receiptPath = Path.Combine(game, Receipt);
            if (!File.Exists(receiptPath)) return false;
            foreach (string line in File.ReadAllLines(receiptPath))
                if (string.Equals(line, rel, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// r2modman / Thunderstore Mod Manager keep their own Inscryption
        /// profiles and start the game from there, with their own BepInEx. A
        /// player who launches that way needs IKMA in the profile, not here.
        /// Only reported, never touched.
        /// </summary>
        private static bool ModManagerPresent()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Directory.Exists(Path.Combine(appData, @"r2modmanPlus-local\Inscryption"))
                    || Directory.Exists(Path.Combine(appData, @"Thunderstore Mod Manager\DataFolder\Inscryption"));
            }
            catch { return false; }
        }

        private static string Relative(string game, string full)
        {
            string root = Path.GetFullPath(game).TrimEnd('\\') + "\\";
            string f = Path.GetFullPath(full);
            return f.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? f.Substring(root.Length) : f;
        }

        private static bool SamePath(string a, string b)
            => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// Unpack the BepInEx zip into the game folder, never replacing a file
        /// that is already there, and never writing outside the game folder
        /// (a zip entry like "..\..\x" is refused - the "zip slip" attack).
        ///
        /// rootFilesOnly = true is the repair case: BepInEx is already here but
        /// its starter (winhttp.dll and doorstop_config.ini, which sit next to
        /// Inscryption.exe) is missing. Only the loose files at the top of the
        /// zip are added; nothing inside the player's BepInEx folder is touched.
        /// </summary>
        private static void ExtractNoOverwrite(ZipArchive zip, string game, bool rootFilesOnly)
        {
            string root = Path.GetFullPath(game).TrimEnd('\\') + "\\";
            foreach (var entry in zip.Entries)
            {
                // A zip entry with a slash in its name lives in a subfolder.
                if (rootFilesOnly && entry.FullName.IndexOfAny(new[] { '/', '\\' }) >= 0) continue;

                string target = Path.GetFullPath(Path.Combine(game, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("the BepInEx archive contains a path outside the game folder");

                if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target)) continue;
                entry.ExtractToFile(target);
            }
        }

        // ------------------------------------------------------------------
        // Finding the game
        // ------------------------------------------------------------------

        /// <summary>
        /// Steam's own record of where it is (registry, read only), then every
        /// library folder Steam lists in libraryfolders.vdf - internal drives,
        /// second drives, all of them.
        /// </summary>
        private static string FindGame()
        {
            var steamRoots = new List<string>();
            AddIfDir(steamRoots, ReadRegistry(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath"));
            AddIfDir(steamRoots, ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath"));
            AddIfDir(steamRoots, @"C:\Program Files (x86)\Steam");

            var libraries = new List<string>();
            foreach (string steam in steamRoots)
            {
                AddIfDir(libraries, steam);
                string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                // Lines look like:   "path"		"D:\\SteamLibrary"
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    AddIfDir(libraries, m.Groups[1].Value.Replace(@"\\", @"\"));
            }

            foreach (string lib in libraries)
            {
                string game = Path.Combine(lib, @"steamapps\common\Inscryption");
                if (File.Exists(Path.Combine(game, GameExe))) return game;
            }
            return null;
        }

        private static string AskForFolder()
        {
            while (true)
            {
                Say(Text.AskFolder);
                string typed = (Console.ReadLine() ?? "").Trim().Trim('"');
                if (typed.Length == 0) return null;
                if (File.Exists(Path.Combine(typed, GameExe))) return typed;
                Say(Text.NotAGameFolder);
            }
        }

        private static string ReadRegistry(RegistryHive hive, RegistryView view, string key, string value)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var k = baseKey.OpenSubKey(key))
                    return k?.GetValue(value) as string;
            }
            catch { return null; }
        }

        private static void AddIfDir(List<string> list, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            path = path.Replace('/', '\\').TrimEnd('\\');
            if (!Directory.Exists(path)) return;
            foreach (string p in list)
                if (string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(path);
        }

        // ------------------------------------------------------------------
        // Small helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// The CPU a Windows program was built for, from its header:
        /// 0x14c = 32-bit x86, 0x8664 = 64-bit x64.
        /// </summary>
        private static string PeMachine(string path)
        {
            try
            {
                using (var f = File.OpenRead(path))
                using (var r = new BinaryReader(f))
                {
                    f.Seek(0x3c, SeekOrigin.Begin);
                    int pe = r.ReadInt32();
                    f.Seek(pe + 4, SeekOrigin.Begin);
                    ushort machine = r.ReadUInt16();
                    return machine == 0x14c ? "x86" : machine == 0x8664 ? "x64" : "unknown";
                }
            }
            catch { return "unknown"; }
        }

        // Embedded files are named "payload/<file>" by the .csproj.
        private const string Prefix = "payload/";

        private static IEnumerable<string> ResourceNames()
        {
            foreach (string n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (n.StartsWith(Prefix, StringComparison.Ordinal)) yield return n.Substring(Prefix.Length);
        }

        private static Stream Resource(string name)
        {
            var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + name);
            if (s == null) throw new FileNotFoundException(Text.PayloadMissing);
            return s;
        }

        private static void WriteResource(string name, string target)
        {
            using (var src = Resource(name))
            using (var dst = File.Create(target))
                src.CopyTo(dst);
        }

        private static string Version()
        {
            var a = Assembly.GetExecutingAssembly();
            var info = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                a, typeof(AssemblyInformationalVersionAttribute));
            return info?.InformationalVersion ?? a.GetName().Version.ToString();
        }

        internal static void Say(string line) => Console.WriteLine(line);

        internal static string ReadLine() => (Console.ReadLine() ?? "").Trim();

        private static void Close()
        {
            Say(Text.PressEnterToClose);
            Console.ReadLine();
        }
    }
}
