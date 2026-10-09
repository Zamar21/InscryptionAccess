# INSTALL.md

# Installing IKMA

IKMA (Inscryption Kaycee's Mod Access) lets you play Inscryption's Kaycee's Mod with a screen reader, from the keyboard or a controller. This guide covers installing, checking that it works, changing settings, updating, uninstalling, and fixing the problems people hit most.

Version 0.4.8 (beta) is for Windows only.

## Contents

- What you need
- Which download to pick
- Install with IKMA Manager (recommended)
- Install by hand
- Check that it works
- Settings
- Updating
- Uninstalling
- Troubleshooting
- Reporting a bug
- Credits and licenses

## What you need

- Inscryption on Steam, on Windows.
- A screen reader. NVDA is what IKMA is built for. JAWS reaches IKMA through the same speech library, but it has not been tested. With no screen reader running, IKMA speaks through the Windows built-in voice.
- Optional: any controller. Optional: a braille display (see Settings).
- Nothing else if you use IKMA Manager. If you install by hand you also need BepInEx 5, the Windows x86 (32-bit) build. Inscryption is a 32-bit game, and the 64-bit build will not load.

If manually getting BepInEx, please only download it from its official page: https://github.com/BepInEx/BepInEx/releases. Copies of it on other sites may have been tampered with so only download from the official source if doing this manually.

## Which download to pick

Everything is on the latest release page: https://github.com/Zamar21/InscryptionAccess/releases/latest

- IKMA-v(version)-windows-manager.exe is the one most people want. It is a single file that installs everything. The version number is in the file name.
- IKMA-v(version)-windows.zip is for installing by hand. It does not contain BepInEx.
- SHA256SUMS.txt lists a fingerprint for each download, so you can check yours is the file that was published. See Troubleshooting.
- IKMAccess.dll and IKMAccess.dll.sig are used by the automatic updater. You do not download them yourself.

## Install with IKMA Manager (recommended)

1. Download IKMA-v(version)-windows-manager.exe from the release page.
2. Run it. Windows will stop it the first time. See "The Windows warning" just below.
3. A console window opens. Press Enter to install, or to update if running again.
4. IKMA Manager finds Inscryption through Steam. It asks you to type the game folder only if Steam's common records do not lead to it.
5. If the game has no BepInEx, the Manager installs BepInEx 5 first from the official source, then IKMA. If BepInEx 5 is already there for other mods, it is kept exactly as it is. If it finds BepInEx 6 or another mod loader, it stops and changes nothing.
6. Start your screen reader, then start Inscryption from Steam. If the screen reader starts after the game, IKMA should find it within 30 seconds.

The Manager is a console program: it prints a line, and you type an answer and press Enter. Its main menu is:

- Enter alone: install or update.
- 1: install or update.
- 2: settings.
- 3: turn IKMA off, or back on, without uninstalling.
- 4: uninstall.
- 5: quit.

Changes made in the Manager's settings take effect the next time the game starts.

### The Windows warning

IKMA Manager is not code-signed, so Windows SmartScreen stops it the first time you run it. This happens because the file is new and unsigned, not because it is harmful. If you want to check your download first, see "Checking a download" under Troubleshooting. Your browser may also warn you at download time; its wording differs from browser to browser.

The dialog is titled "Windows protected your PC". It appears when you run the file, not when you download it. To get past it:

1. The dialog opens on the button "Don't run". Pressing Enter now closes the dialog and installs nothing, so do not press Enter yet.
2. Press Tab to reach the "More info" link, then press Enter.
3. A "Run anyway" button now appears. Press Tab to reach it, then press Enter.
4. The Manager's console window opens.


## Install by hand

1. In Steam, right-click Inscryption, choose Manage, then Browse local files. File Explorer opens on the game folder, the one that holds Inscryption.exe.
2. Unzip BepInEx 5 (Windows x86) into that folder. When it is done, winhttp.dll sits next to Inscryption.exe.
3. Start the game once, then quit. BepInEx creates its own folders on that first run.
4. Unzip IKMA-v(version)-windows.zip into the same folder. It puts IKMAccess.dll in BepInEx\plugins, and puts UniversalSpeech.dll and nvdaControllerClient.dll next to Inscryption.exe.
5. Start your screen reader, then start Inscryption.

## Check that it works

- IKMA speaks at the title screen.
- Press H on any screen to hear that screen's keys. Press F1 to get the same help as a list you can step through.
- The game's log, BepInEx\LogOutput.log in the game folder, names the mod and its version.

If you hear nothing, see Troubleshooting.

## Settings

Most settings can be changed in the game: press Control plus M for Mod Settings (LB plus Menu on a controller). They can also be changed in IKMA Manager's settings menu. Every setting is also in a file, BepInEx\config\com.zamar.ikma.cfg in the game folder, which IKMA creates the first time the game runs. Close the game before editing that file by hand.

[Speech]
- Backend: Auto or NVDA. Auto picks for you and is the default. Both use the Windows speech library (NVDA, JAWS or the Windows voice).
- Braille: true or false. See below.

[Gamepad]
- LoadGameLayout: on by default. Loads the gamepad layout the PC game ships but never turns on. Off means the controller does nothing, as in the unmodded game.
- VibrationLevel: Off, Low, Medium or High. High is the default. The game itself never vibrates; this is IKMA's.
- ButtonNames: Auto, Xbox, PlayStation, or Switch. Auto picks the button names from your controller's name. Use it if Steam Input makes your controller look like a different type.

[Updates]
- Mode: Automatic, DownloadThenAsk, AskFirst or Off. Off means IKMA never connects to the internet. When IKMA asks a question, answer in the game: F9 for yes, F10 for no.

[Language]
- Language: Game follows the game's own language. English is the only language in this version.

[History]
- Reads and Prompts: whether the review history keeps what you read and the prompts you hear.
- Each kind of announcement has its own section, [Events.name], with two settings: Announce (speak it) and AddToHistory (keep it in the review history).
- [ControllerMap] holds any controller buttons you have moved.

### Braille

IKMA sends every line it speaks to a braille display too, through your screen reader (NVDA or JAWS). It is on unless you set Braille = false under [Speech]. For now the switch is in the config file only; it is not yet in Mod Settings or the Manager.

- With no braille display attached, it does nothing.
- With the Windows built-in voice and no screen reader, nothing is sent to braille.
- Each new line replaces the one before it. In a run of quick lines, such as a battle, the display shows the newest. To step back, use the review history: Control plus Up and Control plus Down.
- On NVDA each line arrives as a braille message. How long it stays is set by NVDA's own Braille settings, Show messages and Message timeout. If Show messages is disabled, nothing appears.

Braille has not been tested on a real display, so it may not behave as described. If you use one, a bug report about it is welcome.

## Updating

IKMA checks for new versions when the game starts. Updates are signed, and IKMA checks the signature before installing one. What it does is the [Updates] Mode setting: install by itself (Automatic, the default), download then ask, ask first, or never connect. Running IKMA Manager again also updates.

## Uninstalling

If you installed with IKMA Manager: run it again, type 4 and press Enter. It removes only what it installed, puts back any speech DLL it set aside, and leaves BepInEx in place for your other mods.

If you installed by hand: delete BepInEx\plugins\IKMAccess.dll, and delete UniversalSpeech.dll and nvdaControllerClient.dll from the game folder, next to Inscryption.exe. BepInEx and your other mods are not affected.

To turn IKMA off without removing it, use IKMA Manager, option 3.

## Troubleshooting

### I hear nothing at all

1. Start your screen reader before the game, or within 30 seconds after it starts.
2. Check that winhttp.dll sits next to Inscryption.exe. If it does not, BepInEx is not installed in the right place.
3. Check that the BepInEx you installed is the x86 (32-bit) build. The 64-bit build does not load in Inscryption.
4. Open BepInEx\LogOutput.log in the game folder. If it does not exist, BepInEx did not run. If it exists, send it with your bug report.
5. With no screen reader running, IKMA uses the Windows built-in voice. Check that Windows has a voice installed and the volume is up.

### Windows or my antivirus complains about a file

IKMA is a normal BepInEx plugin. It is not injected into the game from outside. But the files are new and not code-signed, and some antivirus programs distrust new unsigned files. Check your download against SHA256SUMS.txt first (next entry). If it matches, you can allow it.

### Checking a download

Open SHA256SUMS.txt from the release page. Then, in PowerShell:

Get-FileHash -Algorithm SHA256 "$env:USERPROFILE\Downloads\IKMA-v(version)-windows-manager.exe"

Or in Command Prompt:

certutil -hashfile "%USERPROFILE%\Downloads\IKMA-v(version)-windows-manager.exe" SHA256

The long string of letters and numbers it prints must match the line for that file in SHA256SUMS.txt. If it does not, delete the file and download it again from the release page.

### IKMA Manager says it found BepInEx 6 or another mod loader

It stops on purpose and changes nothing, because installing BepInEx 5 over it would break your setup. Install by hand only if you know your other mods will keep working.

### The game updated and IKMA stopped working

Send a bug report (next section) with the log. Say the game version if you know it.

### I use other mods

IKMA is built to sit beside other BepInEx mods, but it has not been tested widely with them yet. The bug report log lists the other mods you have installed.

## Reporting a bug

1. Press Shift plus L (LB plus RB plus View on a controller) at any point. IKMA saves a log to your desktop as a .txt file. At the top it lists the IKMA version, the speech engine, the screen you were on, Windows, other mods, and any challenges that were on.
2. Attach that file when you report. Say what you pressed and what you expected to hear.
3. If you can, use Control plus Up and Control plus Down first to find the exact line you heard, and quote it.
4. Post it in the Inscryption text channel of the Accessibility Disco server, and @ zamar with a sentence on what you pressed and what you expected to hear.

A softlock, where the game stops responding to you, is the most important thing to report. Followed by misinformation on an H key read. 

By default the log holds what IKMA said and anything that went wrong. [Diagnostics] Show in the config file adds every diagnostic line, if you are asked for one.

## Credits and licenses

IKMA is released under the MIT license. Copyright Zamar.

- UniversalSpeech, by Quentin Cosendey. MIT license.
- NVDA Controller Client, by NV Access. LGPL 2.1, included unmodified.
- BepInEx 5. LGPL 2.1. IKMA Manager installs it when the game has none. The zip does not include it.

All three license notices are installed with IKMA, in BepInEx\plugins\IKMA_licenses.

<!-- INSTALL.md -->
