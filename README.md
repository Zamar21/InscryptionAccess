# README.md

# IKMA - Inscryption Kaycee's Mod Access

IKMA is a mod that makes Inscryption's game mode called "Kaycee's Mod" blind accessible. It adds screen reader support and controls for keyboard or controller.

Version 0.4.8 is a beta, ahead of the version 0.5 launch. It covers the "Kaycee's Mod" portion of Inscryption only. The story acts are not currently covered.

Every action in this game is turn based, there are zero timing based requirements. However there are also very few moments you can take something back after performing a game action, so be deliberate! 

This game exclusively utilizes Auto-saves, and you can have one active run ongoing at a time, which can be paused, suspended, or reset at nearly any time. 

Made by Zamar. Not affiliated with Daniel Mullins Games or Devolver Digital.

## Contents

- Quick links
- Features
- What you need
- Install
- Updating and uninstalling
- Keys to know
- First steps
- Troubleshooting
- Reporting a bug
- Language
- Not in this version
- Known issues
- Notes
- License and credits

## Quick links

- Download: https://github.com/Zamar21/InscryptionAccess/releases/latest
- Install guide: INSTALL.md
- Controls, keyboard: IKMA_Controls.md
- Controls, controller: IKMA_Controls_Gamepad.md
- Known issues: KNOWN_ISSUES.md
- Report a bug or ask a question: use the Inscryption text channel in the Accessibility Disco server, or DM Zamar directly on Discord or BlueSky! 

## Features

### Speech and braille

- Everything in Kaycee's Mod is spoken through your screen reader. NVDA is what IKMA is built for. JAWS is reached through the same speech library but has not yet been tested. With no screen reader running, IKMA uses the Windows built-in voice.
- IKMA only speaks public information given by the game. A face-down card stays unknown. A hidden ability stays hidden, etc. 
- Braille: each spoken line is also sent to a braille display, through your screen reader. It is on unless you switch it off, and it has not yet been tested by a braille reader. See INSTALL.md, Settings.

### Keyboard, no mouse

- Every screen is played from the keyboard. The mouse is turned off in the game while IKMA is installed: it does not hover and does not click.
- At any time, the H key gives the keys for the screen you are on. F1 gives the same help as a list you can step through.
- Space bar repeats your current selection, provides context, or describes where you are, almost everywhere. Space is used to advance conversations, but will never perform a game action.
- During a conversation only Space (next line), H and Escape work. Other keys answer "Conversation in progress, press Space to proceed."
- If you are ever lost, press the H key to hear your options.

### Controller

- The whole mod is playable on a gamepad.
- In one line: A confirms, B goes back, X repeats, Y opens the rulebook, View gives help. Hold LB for the reading buttons. Hold RB for the action buttons and the numbers.
- Button names follow your controller: Xbox, PlayStation, or Switch.
- Every action can be moved to another button, in Mod Settings or in IKMA Manager. If two actions end up on one button, they swap.
- Vibration defaults to High. It marks character voices, the bell, the scale tipping, items and refusals. It is off while the game is not in front.
- The first press on a controller says "Gamepad." A key pressed after that says "Keyboard." Help lines name the device you used last.

### Menus

- You will begin in the Story mode menu, which is currently blocked by IKMA. Please navigate to the Kaycee's Mod portion of the game, using the arrow keys and Enter, for a second title screen!
- Kaycee's Mod title screen: main menu, new run, starter deck select, challenge select (with the challenge level and points), the two confirm screens, stats, unlocks (cards, starter decks, challenges) and the devlog entries.
- Run end screen and victory screen: your stats are read out, then what each key does.
- Locked items say that they are locked and where they sit in the list.
- In vanilla Inscryption "Kaycee's Mod" is unlocked through story progression. While IKMA is enabled you will have access to it without finishing the story first, and IKMA does not write that to your save. With IKMA off, your save decides.

### The map

- Arrow keys browse the paths ahead. Each stop on the game map is named. Enter key travels.
- From the map you can view your current deck. Press H to hear how. 
- You can stand up from the table and sit back down. More on this later. 

### Battles: reading the table

- During an encounter, press H to hear your encounter controls.
- The game board is made up of 12 slots. 4 slots on your side, 8 slots on their side. 4 of their slots align with yours up front and is their active board, and 4 slots behind that which is their Queue.
- Life totals are tracked by a single Scale which begins at 0. Each direct damage point dealt adds a Tooth to the opponent's scale. If a scale ever hits five, the encounter is over.
- You win a combat by having five more teeth on your opponent's scale than on yours. You lose if they get 5 more teeth on your scale.

### Bosses

- There are five bosses, each with their own phases and mechanics to master.
- Boss dialogue is read in order with what happens on the table.

### Stops on the map

- There are many different stops on the game map. Card choices, Campfires, Sacrifice stones, Mycologists, etc. Press the H key during each one to learn more about it. 

### Items

- Items are powerful single use abilities that can quickly turn the tide of battle. You begin each run with a Squirrel in a Bottle, Pliers, and a Fish Hook.
- You have three item slots available. 
- Every item found in Kaycee's Mod is named from the game's own rulebook, and described to you the first time Leshy presents it.

### Challenges

- Challenges are additional rules you can enhance your run with. Completing more challenging runs leads to newly unlocked rewards...
- Each Challenge will contribute an individual amount of Challenge Points. The Challenges you have completed a run with will be tracked. 

### The rulebook

- The game has an included rulebook describing every ability and item found in Kaycee's Mod. Press the R key during most moments in game to open it. 
- Additionally, anytime you hear an ability or item read, press Shift and the R key to quickly look up what that ability does.  

### The cabin

- While at the game map, you can press Shift and the Down arrow to stand up from the game table. This enters you into a first person mode. 
- As in the unmodded game, your character can only ever look North, South, East, and West in this first person mode. The game works on a square grid system. 
- The standing mode in Kaycee's Mod is an optional mostly scenic mode, but will give you a taste of the escape room style puzzles you will encounter in the main story mode later next year with version 1.0!  


### Review history and silence 

- Review speech history with Control and Up or Down arrows.
- Tap Control to silence the current line and everything queued.

### Mod Settings

- Opened in the game with Control plus M. It navigates like the game's own menus.
- Events: twenty kinds of announcements, each with its own switch for being spoken and for being kept in the history. Some also split into your side and the enemy's. The groups are Combat (Death, HP Changes, Enemy Moves, Turns, Powers, Attacks, Your Turn, Challenges and Totems, Bosses), Cards (Card Drawn, Card Played, Card Obtained), Resources (Teeth, Bones, Item Obtained, Item Used) and Other (Dialogue, Node Entered, Node Results, Saving).
- History: whether reads and prompts are kept.
- Also: Updates, Speech engine, Braille display, Controller vibration, Controller buttons, and Full log for bug reports.
- Language says "Feature coming soon." and will be added to the mod later. 
- The same settings are in IKMA Manager.

### IKMA Manager and updates

- IKMA Manager is one file. It installs or updates IKMA, changes settings, turns IKMA off and on without uninstalling, and uninstalls the mod.
- It finds the game through Steam. It installs the mod loader (BepInEx 5) if the game has none. It leaves an existing BepInEx and your other mods exactly as they are, and it stops and changes nothing if it finds a setup it would break.
- IKMA checks for new versions itself automatically. Updates are signed and checked before they are installed. You choose: automatic, download then ask, ask first, or never connect.
- It is a console window: it prints a line, you type a number and press Enter.

### Bug reports

- Anytime during gameplay you can press Shift plus L (LB plus RB plus View on a controller) to save a log to your desktop, with the version, speech engine, screen and other mods at the top.
- The log holds what IKMA said and anything that went wrong.

## What you need to get started

- Inscryption on Steam, on Windows.
- A screen reader. NVDA is what IKMA is built for. With no screen reader active, IKMA uses the Windows built-in voice.
- Nothing else if you use IKMA Manager. Installing by hand also needs BepInEx 5, the 32-bit (x86) build, from the official page only: https://github.com/BepInEx/BepInEx/releases
- Optional: a controller, and a braille display.

## Install

The short version. INSTALL.md has the full steps, including the Windows warning you will see the first time.

1. Download IKMA-v(version)-windows-manager.exe from the latest release.
2. Run it. Windows warns that the file is unsigned. INSTALL.md explains how to get past that dialog with the keyboard.
3. Press Enter to install.
4. Start Inscryption normally from Steam. IKMA speaks at the title screen.
5. Start your screen reader before the game, or within 30 seconds after it.

## Updating and uninstalling

- IKMA updates itself unless you change that setting. Running IKMA Manager again also updates.
- To turn IKMA off without uninstalling it: IKMA Manager, option 3.
- To Uninstall: IKMA Manager, option 4. BepInEx is left in place for your other mods.

## Keys to know

The full lists are in IKMA_Controls.md and IKMA_Controls_Gamepad.md. Press H (keyboard) or View (controller) in the game for the keys on the screen you are on.

- Help for this screen: H, or View.
- Repeat or describe: Space, or X.
- Confirm and go back: Enter and Backspace, or A and B.
- Rulebook: R, or Y. The entry for what you just heard: Shift plus R, or L3.
- In a battle: C reads your hand, G the enemy board, Shift plus G your board, U the queue, B the full board, A the scales and bones. D and S draw. E rings the bell. On a controller these are LB or RB with another button.
- Review history: Control plus Up and Control plus Down.
- Silence: tap Control, or R3.
- Mod Settings: Control plus M, or LB plus Menu. Help as a list: F1.
- Save a log for a bug report: Shift plus L.

## First steps

1. Start the game. IKMA speaks at the title screen.
2. Press H to hear the keys for the screen you are on. Arrow keys, Enter, and Backspace to navigate. 
3. From the first main menu, use the arrows and Enter to reach the Kaycee's Mod menu. Space repeats where you are at any point.

## Troubleshooting

- No speech at all: start your screen reader first, check that winhttp.dll sits next to Inscryption.exe, and check that BepInEx is the x86 build. INSTALL.md has the full list.
- Windows or your antivirus warns about a file: the files are new and not code-signed. Check your download against SHA256SUMS.txt. INSTALL.md shows how.
- IKMA Manager stops and says it found another mod loader: it did that on purpose and changed nothing.
- Or just ping Zamar on Discord or BlueSky! 

## Reporting a bug

At any point during gameplay, press Shift plus L (LB plus RB plus View on a controller) to save a log to your desktop. Attach it to your report and say what you pressed and what you expected to hear. A softlock or inaccurate info on the H key is the most important thing to report. Post the printed log it in the Inscryption channel in the Accessibility Disco Discord server, @ Zamar, and give a brief description of what happened.

## Language

This version is English only. The game's own text, such as card and ability names, supports additional languages. Other languages are planned for support! 

## Not in this version

- The story acts (Acts 1, 2 and 3). This version is Kaycee's Mod only.
- Languages other than English.

## Known issues

The current list is in KNOWN_ISSUES.md. The ones most players will meet:

- IKMA Manager is unsigned, so Windows warns the first time it runs.
- Braille output has not been confirmed by a braille reader.
- Speech timing under NVDA is estimated, not measured.

## Notes

- This is a mod, used at your own risk. Keep a copy of your save if you care about it. The saves are SaveFile.gwsave and ModdedSaveFile.gwsave in the game folder.
- It is built to sit beside other BepInEx mods, but it has not been tested widely with them yet.
- Not affiliated with Daniel Mullins Games or Devolver Digital.
- IKMA was developed by Zamar with Claude, an AI model made by Anthropic. Claude wrote much of the code. Zamar sets the design, decides every word the mod speaks, plays and tests every build, and edits the documentation.

## License and credits

- IKMA: MIT license, copyright Zamar. The LICENSE file is in the repository.
- UniversalSpeech, by Quentin Cosendey. MIT license.
- NVDA Controller Client, by NV Access. LGPL 2.1, unmodified.
- BepInEx, LGPL 2.1. The three notices are installed with the mod.
- Design references: Hearthstone Access and Say The Spire 2, whose conventions IKMA's keys follow where they could.

<!-- README.md -->
