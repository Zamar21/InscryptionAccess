# install_ikma_deck.sh
#
# DRAFT - IKMA installer for Steam Deck (and Linux running the game through
# Proton). Session 32. NEVER RUN ON A REAL DECK YET.
#
# Run it from Desktop Mode, in Konsole, from the folder it sits in:
#
#     bash install_ikma_deck.sh --bepinex-zip ~/Downloads/BepInEx_x86_5.4.23.2.zip
#
# Add --dry-run first to see every step without changing anything.
#
# WHAT IT DOES
#   1. Finds Inscryption in your Steam library (internal storage or SD card).
#   2. Unpacks BepInEx (the mod loader) into the game folder, from a zip YOU
#      downloaded from the official page, github.com/BepInEx/BepInEx/releases.
#      This script never downloads anything. Inscryption is a 32-bit game, so
#      it needs the Windows x86 BepInEx 5 zip; the script checks.
#   3. Copies IKMAccess.dll into BepInEx/plugins, and the UniversalSpeech
#      DLLs next to the game if they are in this folder.
#   4. Installs the IKMA speech helper into your home folder and, unless you
#      say --no-service, sets it to start by itself when you log in.
#   5. Prints the two settings you make in Steam yourself. It does NOT edit
#      any Steam file.
#
# WHAT IT NEVER DOES
#   - Use sudo or root. SteamOS system files are read-only and replaced by
#     every update; everything here lives in your home folder or the game
#     folder, both of which survive updates.
#   - Edit Steam's configuration.
#   - Download anything.
#
# WHY "bash install_ikma_deck.sh" AND NOT "./install_ikma_deck.sh": the first
# line of every IKMA file is its own name, so there is no "#!/bin/bash" line
# to tell Linux which program runs it. Naming bash on the command line does
# the same job.

# Stop at the first failed command (-e), treat an unset variable as an error
# (-u), and make a failure anywhere in a pipeline count (-o pipefail). An
# installer that carries on after a failed step leaves a half-installed mod.
set -euo pipefail

# ---------------------------------------------------------------------------
# Settings and defaults
# ---------------------------------------------------------------------------

# Must match SpeechBackends.DEFAULT_BRIDGE_PORT (SpeechBackend.cs) and
# DEFAULT_PORT (ikma_speech_helper.py).
DEFAULT_PORT=17432

# Inscryption's Steam app id. Used only to name things in messages.
APP_NAME="Inscryption"

# The folder this script is in. Everything it installs is expected beside it.
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

HELPER_SRC="$HERE/ikma_speech_helper.py"
HELPER_DIR="$HOME/.local/share/ikma-speech"
SERVICE_DIR="$HOME/.config/systemd/user"
SERVICE_NAME="ikma-speech.service"

BEPINEX_ZIP=""
GAME_DIR=""
IKMA_DLL=""
PORT="$DEFAULT_PORT"
INSTALL_SERVICE=1
DRY_RUN=0
FORCE=0

usage() {
    cat <<'USAGE'
Usage: bash install_ikma_deck.sh [options]

  --bepinex-zip PATH   BepInEx 5, Windows x86 zip, from
                       github.com/BepInEx/BepInEx/releases (needed unless
                       BepInEx is already in the game folder)
  --game-dir PATH      Inscryption folder, if it is not found automatically
  --ikma-dll PATH      IKMAccess.dll (default: the one next to this script)
  --port N             speech helper port (default 17432; if you change it,
                       set the same BridgePort in IKMA's config)
  --no-service         install the helper but do not start it automatically
  --force              reinstall BepInEx even if it is already there
  --dry-run            print every step, change nothing
  -h, --help           this text
USAGE
}

# ---------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------

say()  { printf '%s\n' "$*"; }
step() { printf '\n== %s\n' "$*"; }
fail() { printf '\nERROR: %s\n' "$*" >&2; exit 1; }

# run CMD...  - do it, or in --dry-run only print it. Every change the script
# makes to disk goes through here, so --dry-run is a true preview.
run() {
    if [ "$DRY_RUN" -eq 1 ]; then
        printf '   would run: %s\n' "$*"
    else
        "$@"
    fi
}

# pe_machine FILE - print a Windows program's CPU type: x86, x64 or unknown.
# A Windows .exe/.dll records it in its header ("PE" header, machine field:
# 0x14c = 32-bit x86, 0x8664 = 64-bit x64). Python reads it; no extra tools.
pe_machine() {
    "$PYTHON" - "$1" <<'PY'
import struct, sys
try:
    with open(sys.argv[1], "rb") as f:
        head = f.read(4096)
    offset = struct.unpack("<I", head[0x3c:0x40])[0]
    machine = struct.unpack("<H", head[offset + 4:offset + 6])[0]
    print({0x14c: "x86", 0x8664: "x64"}.get(machine, "unknown"))
except Exception:
    print("unknown")
PY
}

# ---------------------------------------------------------------------------
# Arguments
# ---------------------------------------------------------------------------

while [ $# -gt 0 ]; do
    case "$1" in
        --bepinex-zip) BEPINEX_ZIP="${2:-}"; shift 2 ;;
        --game-dir)    GAME_DIR="${2:-}"; shift 2 ;;
        --ikma-dll)    IKMA_DLL="${2:-}"; shift 2 ;;
        --port)        PORT="${2:-}"; shift 2 ;;
        --no-service)  INSTALL_SERVICE=0; shift ;;
        --force)       FORCE=1; shift ;;
        --dry-run)     DRY_RUN=1; shift ;;
        -h|--help)     usage; exit 0 ;;
        *)             usage; fail "unknown option: $1" ;;
    esac
done

case "$PORT" in
    ''|*[!0-9]*) fail "--port must be a number" ;;
esac
if [ "$PORT" -lt 1024 ] || [ "$PORT" -gt 65535 ]; then
    fail "--port must be between 1024 and 65535"
fi

[ "$DRY_RUN" -eq 1 ] && say "DRY RUN - nothing will be changed."

# ---------------------------------------------------------------------------
# 0. Refuse to run as root
# ---------------------------------------------------------------------------
# Running as root would put files in root's home and make the game folder
# root-owned, which breaks Steam updates. There is no reason to need it.
if [ "$(id -u)" -eq 0 ]; then
    fail "do not run this with sudo or as root. Run it as your normal user (deck)."
fi

# ---------------------------------------------------------------------------
# 1. Python (the helper and a few checks here need it)
# ---------------------------------------------------------------------------
step "Checking for Python 3"
PYTHON="$(command -v python3 || true)"
[ -n "$PYTHON" ] || fail "python3 not found. The speech helper needs it."
say "   $PYTHON"

# ---------------------------------------------------------------------------
# 2. Find the game
# ---------------------------------------------------------------------------
# Steam keeps a list of every library folder (internal drive, SD card,
# external drives) in libraryfolders.vdf. It is READ here, never written.
step "Finding $APP_NAME"

find_game() {
    local steam_root lib candidate
    for steam_root in "$HOME/.local/share/Steam" "$HOME/.steam/steam"; do
        [ -d "$steam_root" ] || continue
        candidate="$steam_root/steamapps/common/Inscryption"
        [ -f "$candidate/Inscryption.exe" ] && { printf '%s\n' "$candidate"; return 0; }
        if [ -f "$steam_root/steamapps/libraryfolders.vdf" ]; then
            # Lines look like:   "path"		"/run/media/mmcblk0p1"
            while IFS= read -r lib; do
                candidate="$lib/steamapps/common/Inscryption"
                [ -f "$candidate/Inscryption.exe" ] && { printf '%s\n' "$candidate"; return 0; }
            done < <(sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)".*/\1/p' \
                        "$steam_root/steamapps/libraryfolders.vdf")
        fi
    done
    return 1
}

if [ -z "$GAME_DIR" ]; then
    GAME_DIR="$(find_game || true)"
    [ -n "$GAME_DIR" ] || fail "could not find Inscryption. Is it installed? If so, pass --game-dir with its folder."
fi

# Inscryption.exe is the WINDOWS game. Its presence is also how we know Steam
# downloaded the Windows version (see the Proton note at the end).
[ -f "$GAME_DIR/Inscryption.exe" ] || fail "$GAME_DIR has no Inscryption.exe. Steam may have installed the native Linux version - see the Proton step printed at the end, set it, let Steam update the game, then run this again."
say "   $GAME_DIR"

GAME_ARCH="$(pe_machine "$GAME_DIR/Inscryption.exe")"
say "   game is $GAME_ARCH"

# ---------------------------------------------------------------------------
# 3. BepInEx
# ---------------------------------------------------------------------------
step "BepInEx (mod loader)"

if [ -f "$GAME_DIR/BepInEx/core/BepInEx.dll" ] && [ -f "$GAME_DIR/winhttp.dll" ] && [ "$FORCE" -eq 0 ]; then
    say "   already installed - leaving it alone (use --force to reinstall)."
else
    [ -n "$BEPINEX_ZIP" ] || fail "BepInEx is not installed. Download the BepInEx 5 Windows x86 zip from github.com/BepInEx/BepInEx/releases and pass it with --bepinex-zip."
    [ -f "$BEPINEX_ZIP" ] || fail "no such file: $BEPINEX_ZIP"

    # Unpack to a scratch folder first and check it BEFORE touching the game:
    # it must be BepInEx, and it must match the game's CPU type. A 64-bit
    # BepInEx in a 32-bit game fails silently - the mod just never loads.
    SCRATCH="$(mktemp -d)"
    trap 'rm -rf "$SCRATCH"' EXIT
    "$PYTHON" -m zipfile -e "$BEPINEX_ZIP" "$SCRATCH" || fail "could not unpack $BEPINEX_ZIP"

    if [ ! -f "$SCRATCH/winhttp.dll" ] || [ ! -f "$SCRATCH/BepInEx/core/BepInEx.dll" ]; then
        fail "$BEPINEX_ZIP does not look like BepInEx 5 for Windows (no winhttp.dll or BepInEx/core/BepInEx.dll)."
    fi

    ZIP_ARCH="$(pe_machine "$SCRATCH/winhttp.dll")"
    if [ "$ZIP_ARCH" != "$GAME_ARCH" ]; then
        fail "this BepInEx is $ZIP_ARCH but the game is $GAME_ARCH. Download the Windows $GAME_ARCH zip instead."
    fi
    say "   zip checked: BepInEx 5 for Windows, $ZIP_ARCH."

    # cp -r copies folders with their contents. The trailing "/." copies what
    # is INSIDE the scratch folder rather than the folder itself.
    run cp -r "$SCRATCH/." "$GAME_DIR/"
    say "   unpacked into the game folder."
fi

# ---------------------------------------------------------------------------
# 4. IKMA itself
# ---------------------------------------------------------------------------
step "IKMA"

[ -n "$IKMA_DLL" ] || IKMA_DLL="$HERE/IKMAccess.dll"
[ -f "$IKMA_DLL" ] || fail "IKMAccess.dll not found at $IKMA_DLL (use --ikma-dll)."

run mkdir -p "$GAME_DIR/BepInEx/plugins"
run cp "$IKMA_DLL" "$GAME_DIR/BepInEx/plugins/IKMAccess.dll"
say "   IKMAccess.dll -> BepInEx/plugins"

# Session 32: auto-update. IKMAUpdater.dll installs a downloaded IKMA update
# when the game starts; BepInEx only looks for it in BepInEx/patchers.
# Copied if present; without it IKMA never downloads an update.
if [ -f "$HERE/IKMAUpdater.dll" ]; then
    run mkdir -p "$GAME_DIR/BepInEx/patchers"
    run cp "$HERE/IKMAUpdater.dll" "$GAME_DIR/BepInEx/patchers/IKMAUpdater.dll"
    say "   IKMAUpdater.dll -> BepInEx/patchers"
fi

# UniversalSpeech is IKMA's Windows speech path. Under Proton, IKMA's Auto
# setting uses the Linux helper first and falls back to UniversalSpeech only
# if the helper is not answering. Copied if present; not required.
for native in UniversalSpeech.dll nvdaControllerClient.dll; do
    if [ -f "$HERE/$native" ]; then
        NATIVE_ARCH="$(pe_machine "$HERE/$native")"
        if [ "$NATIVE_ARCH" = "$GAME_ARCH" ]; then
            run cp "$HERE/$native" "$GAME_DIR/$native"
            say "   $native -> game folder"
        else
            say "   skipped $native: it is $NATIVE_ARCH, the game is $GAME_ARCH."
        fi
    fi
done

# ---------------------------------------------------------------------------
# 5. The speech helper
# ---------------------------------------------------------------------------
step "IKMA speech helper"

[ -f "$HELPER_SRC" ] || fail "ikma_speech_helper.py not found next to this script."
run mkdir -p "$HELPER_DIR"
run cp "$HELPER_SRC" "$HELPER_DIR/ikma_speech_helper.py"
say "   installed to $HELPER_DIR"

# Which speech program will it use? The helper decides at start-up; this is
# only an early warning, because SteamOS cannot have packages added without
# unlocking the system, which this project will not ask a player to do.
ENGINE=""
for e in spd-say espeak-ng espeak; do
    if command -v "$e" >/dev/null 2>&1; then ENGINE="$e"; break; fi
done
if [ -n "$ENGINE" ]; then
    say "   will speak with: $ENGINE"
else
    say "   WARNING: no speech program found (spd-say, espeak-ng or espeak)."
    say "   The helper will run, but it cannot make sound until one exists."
fi

if [ "$INSTALL_SERVICE" -eq 1 ]; then
    # A systemd "user service" is a program your own login session starts
    # and keeps running - no root needed, and it lives in your home folder,
    # so SteamOS updates leave it alone. Restart=on-failure brings the helper
    # back if it ever crashes.
    SERVICE_TEXT="[Unit]
Description=IKMA speech helper for Inscryption

[Service]
ExecStart=$PYTHON $HELPER_DIR/ikma_speech_helper.py --port $PORT
Restart=on-failure
RestartSec=3

[Install]
WantedBy=default.target
"
    run mkdir -p "$SERVICE_DIR"
    if [ "$DRY_RUN" -eq 1 ]; then
        say "   would write $SERVICE_DIR/$SERVICE_NAME:"
        printf '%s' "$SERVICE_TEXT" | sed 's/^/      /'
    else
        printf '%s' "$SERVICE_TEXT" > "$SERVICE_DIR/$SERVICE_NAME"
    fi

    if command -v systemctl >/dev/null 2>&1 && { [ "$DRY_RUN" -eq 1 ] || systemctl --user show-environment >/dev/null 2>&1; }; then
        run systemctl --user daemon-reload
        run systemctl --user enable "$SERVICE_NAME"
        # restart, not start: on a re-install the old helper may be running
        # and must pick up the new file.
        run systemctl --user restart "$SERVICE_NAME"
        say "   the helper now starts by itself at login, and is running."
        say "   Its messages: journalctl --user -u ikma-speech"
    else
        say "   Could not reach systemd for your user. Start the helper by hand"
        say "   before playing:  $PYTHON $HELPER_DIR/ikma_speech_helper.py --port $PORT"
    fi
else
    say "   not set to start automatically (--no-service). Start it before playing:"
    say "   $PYTHON $HELPER_DIR/ikma_speech_helper.py --port $PORT"
fi

# ---------------------------------------------------------------------------
# 6. What only you can do: two settings in Steam
# ---------------------------------------------------------------------------
cat <<EOF

== Done with files. Two settings to make in Steam yourself:

 1. Run the Windows version through Proton.
    Inscryption also has a native Linux version, and IKMA is a Windows mod.
    Steam > Inscryption > Properties > Compatibility >
    tick "Force the use of a specific Steam Play compatibility tool",
    and pick Proton.

 2. Let BepInEx load. Steam > Inscryption > Properties > General >
    Launch Options, set exactly:

WINEDLLOVERRIDES="winhttp=n,b" %command%

    (winhttp.dll is how BepInEx gets into the game. The setting tells Proton
    to use the game folder's copy first.)
EOF

if [ "$PORT" != "$DEFAULT_PORT" ]; then
    cat <<EOF

 3. You chose port $PORT. After the game has run once with IKMA, open
    $GAME_DIR/BepInEx/config/com.zamar.ikma.cfg
    and set  BridgePort = $PORT  under [Speech].
EOF
fi

say ""
say "Test speech without the game:  $PYTHON $HELPER_DIR/ikma_speech_helper.py --test \"your words here\""

# install_ikma_deck.sh
