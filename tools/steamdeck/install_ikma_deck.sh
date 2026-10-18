# install_ikma_deck.sh
#
# IKMA installer for Steam Deck (and Linux running the game through
# Proton). Session 32; first run on a real Deck in Session 63.
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
#   5. (Session 63) Downloads the Piper voice the helper speaks English with:
#      the Piper program from github.com/rhasspy/piper and one voice file
#      from huggingface.co/rhasspy/piper-voices. Every file is checked
#      against the exact checksum written in this script before it is used;
#      a file that does not match is deleted and never run. If Steam's own
#      "Proton Voice Files" already holds the same voice file, it is copied
#      from there instead of downloaded. --no-piper skips this step, and if
#      the download fails the helper still speaks, with the system voice.
#   6. (Session 64) Adds the "Inscryption Access Official" controller layout
#      to Steam (Your Layouts), for every Steam account on this machine, and
#      picks the published copy of it (Steam Workshop 3820918554) as
#      Inscryption's layout on this Steam Deck. Picking it means changing
#      one line of Steam's controller settings, so it is done only while
#      Steam is closed (Steam rewrites that file from memory while it runs);
#      the old file is kept as a backup. --no-controller-layout skips this.
#   7. Prints the two settings you make in Steam yourself.
#
# WHAT IT NEVER DOES
#   - Use sudo or root. SteamOS system files are read-only and replaced by
#     every update; everything here lives in your home folder or the game
#     folder, both of which survive updates.
#   - Edit any other Steam setting than the controller layout of step 6.
#   - Download anything except the Piper files in step 5, from the two
#     addresses written below, checked by checksum.
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

# The Piper voice (Session 63). Pinned to exact releases: the checksums
# (sha256) below are of the files as published, and a download that does not
# match them is refused. Must match PIPER_EXE / PIPER_MODEL in
# ikma_speech_helper.py.
PIPER_DIR="$HELPER_DIR/piper"
VOICES_DIR="$HELPER_DIR/voices"
PIPER_URL="https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_linux_x86_64.tar.gz"
PIPER_SHA256="a50cb45f355b7af1f6d758c1b360717877ba0a398cc8cbe6d2a7a3a26e225992"
VOICE_NAME="en_US-libritts_r-medium.onnx"
VOICE_URL="https://huggingface.co/rhasspy/piper-voices/resolve/c10ece1aade47bb51c153c893d14e5bf8e5b7117/en/en_US/libritts_r/medium"
VOICE_SHA256="10bb85e071d616fcf4071f369f1799d0491492ab3c5d552ec19fb548fac13195"
VOICE_JSON_SHA256="b471dc60d2d8335e819c393d196d6fbf792817f40051257b269878505bc9afb3"
SERVICE_DIR="$HOME/.config/systemd/user"
SERVICE_NAME="ikma-speech.service"

# The Steam Deck controller layout (Session 64). Steam lists a layout under
# "Your Layouts" when its file sits in
#   Steam Controller Configs/<account>/config/<app id>/<name>_0.vdf
# and remembers the one picked for a game, per Deck, in
#   Steam Controller Configs/<account>/config/configset_<Deck serial>.vdf
# as  "template" "CLOUD_<app id>/<name>_0",  or, for a published layout, as
# "workshop" "<id>".  All of this was read off a Deck after the layout was
# picked by hand and then published from it (Session 64).
# Zamar, Session 64: "Have the installer select the published layout
# (workshop 3820918554)."  The local copy is still added, under Your Layouts.
APP_ID="1092790"
LAYOUT_WORKSHOP_ID="3820918554"
LAYOUT_SRC="$HERE/ikma_controller_neptune.vdf"
LAYOUT_NAME="inscryption_access_official_0"

BEPINEX_ZIP=""
GAME_DIR=""
IKMA_DLL=""
PORT="$DEFAULT_PORT"
INSTALL_SERVICE=1
INSTALL_PIPER=1
INSTALL_LAYOUT=1
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
  --no-piper           do not download the Piper voice; the helper then
                       speaks with the system voice (eSpeak)
  --no-controller-layout
                       do not add or pick the "Inscryption Access Official"
                       Steam Deck controller layout
  --force             reinstall BepInEx even if it is already there
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

# sha256_of FILE - print the file's sha256 checksum, or nothing if unreadable.
sha256_of() {
    "$PYTHON" - "$1" <<'PY'
import hashlib, sys
h = hashlib.sha256()
try:
    with open(sys.argv[1], "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    print(h.hexdigest())
except OSError:
    pass
PY
}

# fetch URL DEST SHA256 - download URL to DEST, keeping it only if its
# checksum matches. Downloads to DEST.part first, so a cut-off download
# never sits where a good file belongs. Returns 1 on any failure.
fetch() {
    if [ "$DRY_RUN" -eq 1 ]; then
        printf '   would download: %s\n' "$1"
        return 0
    fi
    "$PYTHON" - "$1" "$2.part" <<'PY' || { rm -f "$2.part"; return 1; }
import sys, urllib.request
url, dest = sys.argv[1], sys.argv[2]
try:
    with urllib.request.urlopen(url, timeout=60) as response, open(dest, "wb") as out:
        total = int(response.headers.get("Content-Length") or 0)
        done, shown = 0, -1
        while True:
            block = response.read(1 << 16)
            if not block:
                break
            out.write(block)
            done += len(block)
            if total:
                tenth = done * 10 // total
                if tenth != shown:
                    shown = tenth
                    print("   %d percent" % (tenth * 10), flush=True)
except Exception as e:
    print("   download failed: %s" % e, file=sys.stderr)
    sys.exit(1)
PY
    if [ "$(sha256_of "$2.part")" != "$3" ]; then
        rm -f "$2.part"
        say "   checksum did NOT match - the download was deleted, not used."
        return 1
    fi
    mv -f "$2.part" "$2"
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
        --no-piper)    INSTALL_PIPER=0; shift ;;
        --no-controller-layout) INSTALL_LAYOUT=0; shift ;;
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

# ---------------------------------------------------------------------------
# 5b. The Piper voice (English)
# ---------------------------------------------------------------------------
step "Piper voice (English)"

install_piper() {
    local tarball scratch src

    # The program. Unpacked to a scratch folder and checked before it
    # replaces anything.
    if [ -x "$PIPER_DIR/piper" ] && [ -f "$PIPER_DIR/.ikma-sha256" ] \
            && [ "$(cat "$PIPER_DIR/.ikma-sha256")" = "$PIPER_SHA256" ]; then
        say "   Piper program already installed."
    else
        say "   downloading the Piper program (about 26 MB) from github.com/rhasspy/piper"
        run mkdir -p "$HELPER_DIR"
        tarball="$HELPER_DIR/piper_linux_x86_64.tar.gz"
        fetch "$PIPER_URL" "$tarball" "$PIPER_SHA256" || return 1
        if [ "$DRY_RUN" -eq 0 ]; then
            scratch="$(mktemp -d)"
            tar -xzf "$tarball" -C "$scratch" || { rm -rf "$scratch"; return 1; }
            if [ ! -x "$scratch/piper/piper" ]; then
                rm -rf "$scratch"
                say "   the archive has no piper program in it."
                return 1
            fi
            rm -rf "$PIPER_DIR"
            mv "$scratch/piper" "$PIPER_DIR"
            rm -rf "$scratch" "$tarball"
            printf '%s\n' "$PIPER_SHA256" > "$PIPER_DIR/.ikma-sha256"
        fi
        say "   Piper program -> $PIPER_DIR"
    fi

    # The voice file and its settings file.
    run mkdir -p "$VOICES_DIR"
    if [ "$(sha256_of "$VOICES_DIR/$VOICE_NAME")" = "$VOICE_SHA256" ]; then
        say "   voice file already installed."
    else
        # Steam's "Proton Voice Files" may already hold this exact file.
        for src in "$(dirname "$GAME_DIR")/Proton Voice Files" \
                   "$HOME/.local/share/Steam/steamapps/common/Proton Voice Files"; do
            if [ -f "$src/$VOICE_NAME" ] && [ "$(sha256_of "$src/$VOICE_NAME")" = "$VOICE_SHA256" ]; then
                run cp "$src/$VOICE_NAME" "$VOICES_DIR/$VOICE_NAME"
                say "   voice file copied from Steam's Proton Voice Files (same file, checked)."
                break
            fi
        done
        if [ "$DRY_RUN" -eq 1 ] || [ "$(sha256_of "$VOICES_DIR/$VOICE_NAME")" != "$VOICE_SHA256" ]; then
            say "   downloading the voice file (about 79 MB) from huggingface.co/rhasspy/piper-voices"
            fetch "$VOICE_URL/$VOICE_NAME" "$VOICES_DIR/$VOICE_NAME" "$VOICE_SHA256" || return 1
        fi
    fi
    if [ "$(sha256_of "$VOICES_DIR/$VOICE_NAME.json")" != "$VOICE_JSON_SHA256" ]; then
        fetch "$VOICE_URL/$VOICE_NAME.json" "$VOICES_DIR/$VOICE_NAME.json" "$VOICE_JSON_SHA256" || return 1
    fi

    # Credit where the licenses ask for it.
    if [ "$DRY_RUN" -eq 0 ]; then
        cat > "$VOICES_DIR/ATTRIBUTION.txt" <<'TXT'
ATTRIBUTION.txt

The IKMA speech helper reads English with Piper.

Piper, by Michael Hansen (rhasspy): https://github.com/rhasspy/piper
  MIT License. The Piper release also contains espeak-ng (GPL-3.0,
  https://github.com/espeak-ng/espeak-ng) and ONNX Runtime (MIT,
  https://github.com/microsoft/onnxruntime).

Voice: en_US-libritts_r-medium, from https://huggingface.co/rhasspy/piper-voices
  Trained on the LibriTTS-R dataset (https://www.openslr.org/141/),
  licensed CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/).
  IKMA uses speakers 14 and 3 of this voice.

None of these projects is connected with IKMA or endorses it.

ATTRIBUTION.txt
TXT
    fi
    return 0
}

if [ "$INSTALL_PIPER" -eq 0 ]; then
    say "   skipped (--no-piper). The helper will use the system voice."
elif ! command -v tar >/dev/null 2>&1; then
    say "   WARNING: tar not found, so Piper cannot be unpacked. The helper will use the system voice."
elif install_piper; then
    say "   Piper voice ready."
else
    say "   WARNING: the Piper voice could not be installed (see the lines above)."
    say "   IKMA still speaks, with the system voice. To try again later, run this"
    say "   script again with the same options."
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
# 6. The Steam Deck controller layout (Session 64)
# ---------------------------------------------------------------------------
# Zamar, Session 64: "Make sure the mod sets the players controls to this IA
# official control scheme if possible."  The mod cannot do it from inside the
# game - Steam is running then, and keeps the layout choice in memory - so
# the installer does it. Copying the layout file is safe with Steam open
# (Steam lists it under Your Layouts straight away); picking it is not, so
# that part waits for Steam to be closed.
step "Steam Deck controller layout"

LAYOUT_PICK_SKIPPED=0
if [ "$INSTALL_LAYOUT" -eq 0 ]; then
    say "   skipped (--no-controller-layout)."
elif [ ! -f "$LAYOUT_SRC" ]; then
    say "   WARNING: $LAYOUT_SRC is missing, so the layout was not added."
else
    STEAM_OPEN=0
    pgrep -x steam >/dev/null 2>&1 && STEAM_OPEN=1
    # The Python below prints what it did, line by line. Exit code 3 means
    # the layout was added but not picked because Steam is open.
    set +e
    "$PYTHON" - "$LAYOUT_SRC" "$APP_ID" "$LAYOUT_NAME" "$DRY_RUN" "$STEAM_OPEN" "$LAYOUT_WORKSHOP_ID" \
        "$HOME/.local/share/Steam" "$HOME/.steam/steam" <<'PY'
import os, re, shutil, sys

src, app_id, name, dry, steam_open, workshop_id = sys.argv[1:7]
dry = dry == "1"
steam_open = steam_open == "1"

# The same Steam folder is often reachable by two paths (~/.steam/steam is
# a link to ~/.local/share/Steam), so count each real folder once.
roots = []
for r in sys.argv[7:]:
    if os.path.isdir(r) and os.path.realpath(r) not in roots:
        roots.append(os.path.realpath(r))
if not roots:
    print("   WARNING: no Steam folder found, so the layout was not added.")
    sys.exit(0)

# --- Steam's text settings format (KeyValues): "key" "value" and
# "key" { ... }. Read into nested lists of [key, value] so the order and
# every entry we do not touch are written back as they were.
def parse(text):
    tokens = re.findall(r'"((?:[^"\\]|\\.)*)"|([{}])', text)
    pos = 0
    def block():
        nonlocal pos
        items = []
        while pos < len(tokens):
            s, brace = tokens[pos]
            if brace == "}":
                pos += 1
                return items
            if brace == "{":
                raise ValueError("a { with no name")
            pos += 1
            if pos >= len(tokens):
                raise ValueError("a name with no value")
            v, vbrace = tokens[pos]
            pos += 1
            if vbrace == "{":
                items.append([s, block()])
            elif vbrace == "}":
                raise ValueError("a name with no value")
            else:
                items.append([s, v])
        return items
    return block()

def dump(items, depth=0):
    out = []
    tab = "\t" * depth
    for k, v in items:
        if isinstance(v, list):
            out.append('%s"%s"\n%s{\n%s%s}\n' % (tab, k, tab, dump(v, depth + 1), tab))
        else:
            out.append('%s"%s"\t\t"%s"\n' % (tab, k, v))
    return "".join(out)

def child(items, key):
    for kv in items:
        if kv[0].lower() == key.lower() and isinstance(kv[1], list):
            return kv[1]
    new = [key, []]
    items.append(new)
    return new[1]

def deck_serial(root):
    try:
        with open(os.path.join(root, "config", "config.vdf"), encoding="utf-8", errors="replace") as f:
            m = re.search(r'"SteamDeckRegisteredSerialNumber"\s+"([^"]+)"', f.read())
        return m.group(1) if m else ""
    except OSError:
        return ""

added = picked = 0
waiting = False
for root in roots:
    configs = os.path.join(root, "steamapps", "common", "Steam Controller Configs")
    if not os.path.isdir(configs):
        continue
    serial = deck_serial(root)
    for account in sorted(os.listdir(configs)):
        cfg = os.path.join(configs, account, "config")
        if not account.isdigit() or not os.path.isdir(cfg):
            continue
        # 1. The layout file, so it shows under Your Layouts.
        dest = os.path.join(cfg, app_id, name + ".vdf")
        if dry:
            print("   would copy the layout to " + dest)
        else:
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            shutil.copyfile(src, dest)
            print("   layout added for Steam account " + account)
        added += 1
        # 2. Pick the published copy for this game on this Deck.
        if not serial:
            continue
        if steam_open:
            waiting = True
            continue
        cs = os.path.join(cfg, "configset_%s.vdf" % serial)
        try:
            with open(cs, encoding="utf-8") as f:
                old = f.read()
        except FileNotFoundError:
            old = '"controller_config"\n{\n}\n'
        try:
            items = parse(old)
        except ValueError as e:
            print("   WARNING: could not read %s (%s); left it alone." % (cs, e))
            continue
        # Exactly what Steam itself wrote once the layout was published and
        # picked: the workshop line alone, so no old "template" or
        # "autosave" line is kept.
        game = child(child(items, "controller_config"), app_id)
        game[:] = [["workshop", workshop_id]]
        new = dump(items)
        if new == old:
            print("   already picked for Steam account " + account)
            picked += 1
            continue
        if dry:
            print("   would set Inscryption's layout in " + cs)
        else:
            if os.path.exists(cs):
                shutil.copyfile(cs, cs + ".ikma-backup")
            with open(cs + ".part", "w", encoding="utf-8", newline="\n") as f:
                f.write(new)
            os.replace(cs + ".part", cs)
            print("   picked as Inscryption's layout for Steam account " + account)
        picked += 1

if added == 0:
    print("   WARNING: Steam has no controller settings folder yet, so the layout was not added.")
    print("   Open Steam once, then run this script again.")
elif not any(deck_serial(r) for r in roots):
    print("   This is not a registered Steam Deck, so the layout was added but not picked.")
if waiting:
    sys.exit(3)
PY
    LAYOUT_RESULT=$?
    set -e
    if [ "$LAYOUT_RESULT" -eq 3 ]; then
        LAYOUT_PICK_SKIPPED=1
        if [ "$DRY_RUN" -eq 1 ]; then
            say "   Steam is open, so the layout would be added but not picked."
        else
            say "   Steam is open, so the layout was added but not picked: Steam would"
            say "   overwrite the choice when it closes. See the end of this message."
        fi
    elif [ "$LAYOUT_RESULT" -ne 0 ]; then
        say "   WARNING: the controller layout step failed (see the lines above)."
        say "   Everything else is installed."
    fi
fi

# ---------------------------------------------------------------------------
# 7. What only you can do: two settings in Steam
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

if [ "$LAYOUT_PICK_SKIPPED" -eq 1 ]; then
    cat <<EOF

 Controller layout: Steam was open, so "Inscryption Access Official" is in
 Your Layouts but not yet picked for Inscryption. Either pick the
 published one yourself (Steam > Inscryption > the controller icon >
 Controller Layout > Community Layouts), or exit Steam completely and run
 this script again.
EOF
fi

say ""
say "Test speech without the game:  $PYTHON $HELPER_DIR/ikma_speech_helper.py --test \"your words here\""

# install_ikma_deck.sh
