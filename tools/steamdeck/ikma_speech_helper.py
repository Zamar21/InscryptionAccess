# ikma_speech_helper.py
#
# The IKMA speech helper for Steam Deck and Linux. (Session 32; Piper voice
# added Session 63.)
#
# WHAT IT IS FOR. On a Steam Deck, Inscryption runs under Proton, a layer
# that lets Windows games run on Linux. The IKMA mod inside the game cannot
# reach a Linux speech engine directly, and there is no NVDA on a Deck. So
# the mod sends each line of text over a local network connection
# (127.0.0.1, "this same machine") and this small program, running natively
# on Linux, speaks it.
#
# HOW TO RUN IT BY HAND (Desktop Mode, Konsole):
#     python3 ~/.local/share/ikma-speech/ikma_speech_helper.py
# Then start the game. Ctrl+C stops the helper. Normally the install script
# sets it up to start by itself (a systemd user service), so you never do
# this.
#
# To check that speech works at all, without the game:
#     python3 ~/.local/share/ikma-speech/ikma_speech_helper.py --test "hello"
#
# THE VOICES (Session 63).
#   - Piper, a neural voice, speaks English. The install script downloads it
#     into ~/.local/share/ikma-speech/piper (the program) and .../voices (the
#     voice file). The voice file holds many speakers; IKMA offers two, set
#     from the mod menu: "female" (speaker 14, the default) and "male"
#     (speaker 3). Piper plays through pw-play (PipeWire), else paplay, else
#     aplay.
#   - Every other language, and English whenever Piper is missing or fails,
#     is spoken by the system's own engine: speech-dispatcher's `spd-say` if
#     present, otherwise `espeak-ng` (or the older `espeak`). A Piper failure
#     never means silence: that line, and every line after it if Piper keeps
#     failing, goes to the system engine instead.
#
# REQUIREMENTS, and why they are what they are:
#   - Python 3 standard library only. Nothing to install with pip, because
#     SteamOS's system folders are read-only and are wiped by updates.
#   - Runs from the user's home folder, as the normal user. No root, ever.
#   - Listens on 127.0.0.1 ONLY, never on the network, so nothing outside
#     the Deck can make it talk.
#
# THE PROTOCOL. One line of UTF-8 text per message, ending in "\n". It must
# match IKMAccess/LinuxBridgeBackend.cs, which is the other end:
#
#   HELLO 1              mod -> helper, once per connection.
#   OK ikma-speech 1 X   helper -> mod, the reply. X = engine in use
#                        (piper, spd-say, espeak-ng, espeak, or none).
#   SAY text             speak after whatever is already queued.
#   INTERRUPT text       stop now, drop the queue, then speak this.
#   STOP                 stop now, drop the queue, say nothing.
#   LANG name            the language the lines after this one are in, by
#                        IKMA's own name for it ("French", "Japanese", ...).
#                        Session 32. No reply. A helper that predates it
#                        ignores it, and speaks in the engine's default voice.
#   VOICE name           Session 63. "female" or "male": which Piper speaker
#                        reads English. No reply.
#   RATE percent         Session 63. Speaking speed, as a percentage of the
#                        voice's normal speed (100 = normal, 200 = twice as
#                        fast). Applies to every engine. No reply.
#   VOLUME percent       Session 63. 10 to 100; 100 = full. No reply. Below
#                        10 is read as 10 (Session 64): silent speech can't
#                        be turned back up.
#   BUSY?                helper -> "BUSY 1" or "BUSY 0". Reserved: the mod
#                        does not ask yet (it would change timing; see
#                        WEEKEND_NOTES.md).
#
# A helper ignores any message it does not know, so an older helper with a
# newer mod simply keeps its own defaults.
#
# RULE THE MOD DEPENDS ON: after the HELLO reply, the helper sends NOTHING
# unless asked. The mod detects a closed connection by "readable with zero
# bytes waiting", which only works if the helper never volunteers data.
#
# ORDER. Lines are spoken one at a time, in the order they arrived - the same
# strict first-in-first-out order IKMA uses on Windows. INTERRUPT is the only
# thing that skips ahead, and it does so by throwing away what was waiting,
# exactly as a screen reader's "interrupt" does.

import argparse
import collections
import json
import os
import select
import shutil
import signal
import socket
import socketserver
import subprocess
import sys
import tempfile
import threading
import time

PROTOCOL_VERSION = "1"

# Must match SpeechBackends.DEFAULT_BRIDGE_PORT in SpeechBackend.cs.
DEFAULT_PORT = 17432

# A line longer than this is almost certainly not speech. Cap it so a broken
# client cannot hand an engine a megabyte of text.
MAX_LINE_BYTES = 16384

ENGINE_ORDER = ("spd-say", "espeak-ng", "espeak")

# Where the install script puts Piper. Must match install_ikma_deck.sh.
HELPER_HOME = os.path.join(os.path.expanduser("~"), ".local", "share", "ikma-speech")
PIPER_EXE = os.path.join(HELPER_HOME, "piper", "piper")
PIPER_MODEL = os.path.join(HELPER_HOME, "voices", "en_US-libritts_r-medium.onnx")

# IKMA's voice names -> the speaker number inside the libritts_r voice file.
# Zamar picked both by ear on the Deck (Session 63); female is the default.
PIPER_SPEAKERS = {"female": 14, "male": 3}
DEFAULT_VOICE = "female"

# Session 64, Zamar: "We cant have the volume for the voice go to 0 or else it
# becomes impossible to use. Have 10 be the minimum."
MIN_VOLUME = 10

# The only IKMA language the Piper voice can read.
PIPER_LANGUAGE = "English"

# Programs that play Piper's sound files, best first.
PLAYERS = ("pw-play", "paplay", "aplay")

# After this many Piper failures in one run, stop trying it until the helper
# restarts. Each failed line is still spoken, by the system engine.
PIPER_MAX_FAILURES = 3

# With no game connected for this long, Piper is closed to give its memory
# back. It starts again on the next connection.
PIPER_IDLE_SECONDS = 60

# THE VOICE LANGUAGE (Session 32). IKMA speaks in the language set in its
# [Language] setting (or the game's), and tells the helper which with LANG.
# IKMA's language name -> (the code spd-say takes with -l, the espeak-ng
# voices to try with -v, best first). espeak-ng names Mandarin "cmn"; both
# Chinese scripts are read by the Mandarin voice.
LANGUAGES = {
    "English":             ("en",    ("en", "en-us")),
    "French":              ("fr",    ("fr", "fr-fr")),
    "Italian":             ("it",    ("it",)),
    "German":              ("de",    ("de",)),
    "Spanish":             ("es",    ("es",)),
    "BrazilianPortuguese": ("pt-BR", ("pt-br", "pt")),
    "Turkish":             ("tr",    ("tr",)),
    "Russian":             ("ru",    ("ru",)),
    "Japanese":            ("ja",    ("ja",)),
    "Korean":              ("ko",    ("ko",)),
    "ChineseSimplified":   ("zh",    ("cmn", "zh")),
    "ChineseTraditional":  ("zh",    ("cmn", "zh")),
}


def log(message):
    """Print one line to standard error, with a timestamp.

    Standard error rather than standard output because that is where
    diagnostics belong; when the helper runs as a systemd service both end up
    in the journal (`journalctl --user -u ikma-speech`) anyway.
    """
    sys.stderr.write(time.strftime("%H:%M:%S ") + "ikma-speech: " + message + "\n")
    sys.stderr.flush()


def find_engine(requested):
    """Return (engine name, full path) or ("none", None).

    shutil.which searches the PATH exactly as a shell would, so this finds
    the programs wherever SteamOS (or the user) put them.
    """
    candidates = ENGINE_ORDER if requested == "auto" else (requested,)
    for name in candidates:
        path = shutil.which(name)
        if path:
            return name, path
    return "none", None


def find_player():
    """Return (player name, full path) or (None, None)."""
    for name in PLAYERS:
        path = shutil.which(name)
        if path:
            return name, path
    return None, None


def espeak_has_voice(path, voice):
    """True if espeak-ng / espeak has a voice for this language.

    `espeak-ng --voices=fr` prints a header line, then one line per voice.
    Only the header means no voice. Asked, not assumed: espeak-ng given a
    voice it does not have prints an error and says NOTHING, and silence is
    the one outcome that must not happen.
    """
    try:
        out = subprocess.run([path, "--voices=" + voice], stdout=subprocess.PIPE,
                             stderr=subprocess.DEVNULL, timeout=5).stdout
    except (OSError, subprocess.SubprocessError):
        return False
    return len([l for l in out.decode("utf-8", "replace").splitlines() if l.strip()]) > 1


def parse_percent(text, low, high):
    """The whole number in text, kept within low..high; None if not a number."""
    try:
        value = int(text.strip())
    except ValueError:
        return None
    return max(low, min(high, value))


class Piper:
    """One long-running Piper process that turns lines into sound files.

    Piper loads its voice once (about half a second) and then makes each line
    in a few hundredths of a second, so it is kept running rather than
    started per line. In --json-input mode it reads one JSON object per line
    on standard input - {"text": ..., "speaker_id": ..., "output_file": ...}
    - writes that sound file, and prints the file's path on standard output.

    Used ONLY by the speaker thread, so it needs no lock of its own.
    """

    def __init__(self, exe, model, out_dir):
        self.exe = exe
        self.model = model
        self.out_dir = out_dir
        self.length_scale = None
        self._proc = None
        self._buffer = b""
        self._counter = 0
        # Piper's own messages, kept so a failure can say why.
        self._stderr_tail = collections.deque(maxlen=12)

    def running(self):
        return self._proc is not None and self._proc.poll() is None

    def start(self, length_scale):
        """Start Piper at this speed. True if it started."""
        self.close()
        cmd = [self.exe, "--model", self.model, "--json-input", "--quiet"]
        # 1.0 is the voice file's own speed; leave Piper's default alone then.
        if abs(length_scale - 1.0) > 0.001:
            cmd += ["--length_scale", "%.3f" % length_scale]
        try:
            self._proc = subprocess.Popen(
                cmd,
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                # Piper finds its libraries and espeak-ng-data beside itself.
                cwd=os.path.dirname(self.exe))
        except OSError as e:
            log("could not start Piper: %s" % e)
            self._proc = None
            return False
        self.length_scale = length_scale
        self._buffer = b""
        self._stderr_tail.clear()
        # Piper writes to standard error now and then. Something must read
        # it, or once its pipe fills Piper stops and waits forever.
        threading.Thread(target=self._drain_stderr, args=(self._proc,),
                         name="piper-stderr", daemon=True).start()
        return True

    def _drain_stderr(self, proc):
        try:
            for raw in proc.stderr:
                self._stderr_tail.append(raw.decode("utf-8", "replace").rstrip())
        except (OSError, ValueError):
            pass

    def why(self):
        """Piper's last few messages, for the log after a failure."""
        code = self._proc.poll() if self._proc is not None else None
        tail = " | ".join(l for l in self._stderr_tail if l)
        return "exit code %s; %s" % (code, tail or "no messages")

    def synthesize(self, text, speaker):
        """Make a sound file for text. Its path, or None if Piper failed.

        Each request names its own file, so an answer that comes late can
        never be mistaken for the answer to a later line.
        """
        if not self.running():
            return None
        self._counter += 1
        path = os.path.join(self.out_dir, "line%06d.wav" % self._counter)
        request = json.dumps({"text": text, "speaker_id": speaker, "output_file": path})
        try:
            self._proc.stdin.write((request + "\n").encode("utf-8"))
            self._proc.stdin.flush()
        except (OSError, ValueError):
            return None
        # A long line can take a second or so on a Deck; far longer than this
        # means Piper is stuck.
        deadline = time.time() + 10 + len(text) / 20.0
        fd = self._proc.stdout.fileno()
        while True:
            while b"\n" in self._buffer:
                line, _, self._buffer = self._buffer.partition(b"\n")
                answered = line.decode("utf-8", "replace").strip()
                if answered == path:
                    return path if os.path.exists(path) else None
                # A late answer to an earlier, abandoned request: tidy it.
                if answered.startswith(self.out_dir):
                    remove_quietly(answered)
            left = deadline - time.time()
            if left <= 0:
                log("Piper took too long on one line")
                return None
            try:
                ready, _, _ = select.select([fd], [], [], left)
            except (OSError, ValueError):
                return None
            if not ready:
                continue
            try:
                chunk = os.read(fd, 4096)
            except OSError:
                return None
            if not chunk:
                return None                    # Piper exited
            self._buffer += chunk

    def close(self):
        proc = self._proc
        self._proc = None
        if proc is None:
            return
        try:
            proc.stdin.close()
        except (OSError, ValueError):
            pass
        try:
            proc.wait(timeout=2)
        except subprocess.TimeoutExpired:
            try:
                proc.kill()
                proc.wait(timeout=2)
            except (OSError, subprocess.SubprocessError):
                pass
        except OSError:
            pass


def remove_quietly(path):
    try:
        os.remove(path)
    except OSError:
        pass


class Speaker:
    """Speaks queued lines one at a time on a background thread.

    WHY A THREAD. Speaking a line takes seconds. If the network code waited
    for it, a STOP arriving mid-sentence could not be read until the sentence
    ended - which defeats the point of STOP. So the network side only ever
    puts lines on a queue or clears it, and returns at once; this thread does
    the slow part.

    HOW STOP WORKS. Each line is spoken by a separate process - the system
    engine, or for Piper the program playing its sound file - and this thread
    waits for it to finish. To stop, we (1) empty the queue, (2) kill the
    process that is speaking right now, (3) count the stop, so a Piper line
    that was still being made when the stop came is thrown away instead of
    played, and (4) for speech-dispatcher, also tell the dispatcher itself to
    cancel, because killing the little `spd-say` client does not always stop
    the dispatcher from finishing the sentence it was already given.
    """

    def __init__(self, engine, path, piper=None, player=(None, None),
                 voice=DEFAULT_VOICE, rate=100, volume=100):
        self.engine = engine
        self.path = path
        self.piper = piper
        self.player_name, self.player_path = player
        self.voice = voice
        self.rate = rate
        self.volume = volume
        # The system engine's option for the current language: spd-say's -l
        # code or an espeak voice, or None = the engine's default voice. Each
        # queued line carries the value current when it ARRIVED, so a LANG
        # never changes the voice of a line that was already waiting.
        self._language = None
        # True while the current language is one Piper reads.
        self._piper_language = True
        self._voice_cache = {}
        self._queue = collections.deque()
        # A Condition is a lock plus a way for one thread to sleep until
        # another says "something changed". Every touch of _queue,
        # _process and the settings happens while holding it.
        self._cond = threading.Condition()
        self._process = None
        # Goes up by one on every stop. See HOW STOP WORKS.
        self._stops = 0
        # Set by a stop; the speaker thread clears it by telling
        # speech-dispatcher to cancel. See _stop_locked.
        self._cancel_pending = False
        # True once speech-dispatcher has been given a line since the last
        # cancel, so a stop while only Piper was speaking skips the cancel.
        self._dispatcher_used = False
        self._piper_failures = 0
        self._connections = 0
        self._idle_since = time.time()
        self._warm_wanted = False
        self._running = True
        self._thread = threading.Thread(target=self._loop, name="speaker", daemon=True)
        self._thread.start()

    def describe(self):
        """The engine name the HELLO reply gives the mod."""
        return "piper" if self.piper is not None else self.engine

    # ---- called from the network threads -------------------------------

    def say(self, text):
        with self._cond:
            self._queue.append((text, self._language, self._piper_language))
            self._cond.notify()

    def set_language(self, name):
        """LANG from the game: pick the engine option for that language.

        Unknown names, and languages the engine has no voice for, fall back to
        the engine's default voice with one line in the log - never to silence.
        """
        name = name.strip()
        option = None
        entry = LANGUAGES.get(name)
        if entry is None:
            log("unknown language %r; using the default voice" % name[:40])
        elif self.engine == "spd-say":
            # speech-dispatcher picks a voice for the code itself, and falls
            # back to its default voice if it has none.
            option = entry[0]
        elif self.path is not None:
            for voice in entry[1]:
                if voice not in self._voice_cache:
                    self._voice_cache[voice] = espeak_has_voice(self.path, voice)
                if self._voice_cache[voice]:
                    option = voice
                    break
            if option is None:
                log("%s has no %s voice; using the default voice" % (self.engine, name))
        # An unknown name gets the default voice, which for Piper is English.
        piper_language = entry is None or name == PIPER_LANGUAGE
        with self._cond:
            changed = (option, piper_language) != (self._language, self._piper_language)
            self._language = option
            self._piper_language = piper_language
        if changed:
            via = "Piper" if piper_language and self.piper is not None else (option or "default voice")
            log("language: %s (%s)" % (name, via))

    def set_voice(self, name):
        name = name.strip().lower()
        if name not in PIPER_SPEAKERS:
            log("unknown voice %r; keeping %s" % (name[:40], self.voice))
            return
        with self._cond:
            changed = name != self.voice
            self.voice = name
        if changed:
            log("voice: %s" % name)

    def set_rate(self, text):
        value = parse_percent(text, 25, 400)
        if value is None:
            log("ignored RATE %r" % text[:40])
            return
        with self._cond:
            changed = value != self.rate
            self.rate = value
        if changed:
            log("speed: %d%%" % value)

    def set_volume(self, text):
        value = parse_percent(text, MIN_VOLUME, 100)
        if value is None:
            log("ignored VOLUME %r" % text[:40])
            return
        with self._cond:
            changed = value != self.volume
            self.volume = value
        if changed:
            log("volume: %d%%" % value)

    def interrupt(self, text):
        # Stop and enqueue under ONE hold of the lock, so the speaker thread
        # cannot slip an old queued line in between the two.
        with self._cond:
            self._stop_locked()
            self._queue.append((text, self._language, self._piper_language))
            self._cond.notify()

    def stop(self):
        with self._cond:
            self._stop_locked()

    def busy(self):
        with self._cond:
            return self._process is not None or len(self._queue) > 0

    def connected(self):
        """A game connected: have Piper loaded before its first line."""
        with self._cond:
            self._connections += 1
            self._warm_wanted = True
            self._cond.notify()

    def disconnected(self):
        with self._cond:
            self._connections = max(0, self._connections - 1)
            if self._connections == 0:
                self._idle_since = time.time()
            self._cond.notify()

    def shutdown(self):
        with self._cond:
            self._running = False
            self._stop_locked()
            self._cond.notify()
        self._thread.join(timeout=5)
        # The speaker thread is exiting and will not run the pending cancel,
        # so do it here: a helper that is stopped must not leave the
        # dispatcher finishing a sentence.
        if self.engine == "spd-say" and self.path is not None:
            self._cancel_dispatcher()

    # ---- internals -----------------------------------------------------

    def _stop_locked(self):
        self._queue.clear()
        self._stops += 1
        proc = self._process
        if proc is not None and proc.poll() is None:
            try:
                proc.kill()
            except OSError:
                pass
        if self.engine == "spd-say" and self._dispatcher_used:
            # The dispatcher-side cancel is NOT run here. It is a separate
            # process, and if it were started here it could reach the
            # dispatcher AFTER the next line (an INTERRUPT's new text) and
            # cancel that too. Instead the speaker thread runs it, to
            # completion, before it starts anything else.
            self._cancel_pending = True
            self._cond.notify()

    def _cancel_dispatcher(self):
        """spd-say -C: cancel every message speech-dispatcher holds.

        Called only by the speaker thread, outside the lock, and waited on,
        so it is guaranteed to land before the next line is sent.
        """
        try:
            subprocess.run([self.path, "-C"], stdout=subprocess.DEVNULL,
                           stderr=subprocess.DEVNULL, timeout=3)
        except (OSError, subprocess.SubprocessError) as e:
            log("could not cancel speech-dispatcher: %s" % e)

    def _command(self, text, language, rate, volume):
        """The command line that speaks one line with the system engine."""
        if self.engine == "spd-say":
            # -w = wait until the line has been SPOKEN before exiting, which
            #      is how this thread knows when to start the next one.
            # --  = "no more options": a line starting with "-" is text.
            cmd = [self.path, "-w"]
            if rate != 100:
                # spd-say's -r runs -100 .. 100 with 0 = normal and 100 about
                # three times as fast; slower than normal maps one to one.
                spd = (rate - 100) // 2 if rate > 100 else rate - 100
                cmd += ["-r", str(max(-100, min(100, spd)))]
            if volume != 100:
                cmd += ["-i", str(volume * 2 - 100)]    # -100 .. 100
            if language is not None:
                cmd += ["-l", language]            # e.g. fr, pt-BR, ja
            return cmd + ["--", text], None
        # espeak-ng / espeak already block until done. Text goes in on
        # standard input (--stdin) so no line is ever mistaken for an option.
        # -b 1 = the text is UTF-8 (needed for accents, Cyrillic, Chinese...).
        cmd = [self.path, "--stdin", "-b", "1"]
        if rate != 100:
            # espeak's normal speed is 175 words per minute.
            cmd += ["-s", str(max(80, min(500, 175 * rate // 100)))]
        if volume != 100:
            cmd += ["-a", str(volume)]             # 0 .. 200, 100 = normal
        if language is not None:
            cmd += ["-v", language]                # a voice checked to exist
        return cmd, text

    def _player_command(self, wav, volume):
        if self.player_name == "pw-play":
            return [self.player_path, "--volume", "%.2f" % (volume / 100.0), wav]
        if self.player_name == "paplay":
            return [self.player_path, "--volume=%d" % (65536 * volume // 100), wav]
        return [self.player_path, "-q", wav]       # aplay: no volume control

    def _piper_failed(self, why):
        self._piper_failures += 1
        log("Piper failed (%s)" % why)
        self.piper.close()
        if self._piper_failures >= PIPER_MAX_FAILURES:
            log("Piper failed %d times; using %s for the rest of this run"
                % (self._piper_failures, self.engine))
            self.piper = None

    def _piper_ready(self, rate):
        """Piper running at this speed, starting it if needed. False if not."""
        length_scale = 100.0 / rate
        if self.piper.running() and abs(self.piper.length_scale - length_scale) < 0.001:
            return True
        if not self.piper.start(length_scale):
            self._piper_failed("would not start")
            return False
        return True

    def _wait_locked(self):
        """Sleep until there is work. Runs with the lock held."""
        while self._running and not self._queue and not self._cancel_pending \
                and not self._warm_wanted:
            timeout = None
            if self.piper is not None and self.piper.running() and self._connections == 0:
                timeout = self._idle_since + PIPER_IDLE_SECONDS - time.time()
                if timeout <= 0:
                    return "idle"
            self._cond.wait(timeout)
        return None

    def _loop(self):
        while True:
            with self._cond:
                idle = self._wait_locked()
                if not self._running:
                    break
                cancel = self._cancel_pending
                self._cancel_pending = False
                if cancel:
                    self._dispatcher_used = False
                warm = self._warm_wanted
                self._warm_wanted = False
                rate = self.rate
            if idle and self.piper is not None:
                self.piper.close()
                log("no game connected; Piper closed until the next one")
                continue
            if cancel:
                self._cancel_dispatcher()
            if warm and self.piper is not None and not self.piper.running():
                self._piper_ready(rate)
            with self._cond:
                # Re-check: a STOP may have arrived while cancelling.
                if self._cancel_pending or not self._queue:
                    continue
                text, language, piper_language = self._queue.popleft()
                stops = self._stops
                voice, rate, volume = self.voice, self.rate, self.volume
            if piper_language and self.piper is not None and self.player_path is not None:
                if self._speak_piper(text, stops, voice, rate, volume):
                    continue
                # Piper failed on this line: fall through, so the line is
                # still spoken.
            self._speak_system(text, language, stops, rate, volume)
        if self.piper is not None:
            self.piper.close()

    def _speak_piper(self, text, stops, voice, rate, volume):
        """Speak one line with Piper. False if Piper failed (nothing played)."""
        if not self._piper_ready(rate):
            return False
        wav = self.piper.synthesize(text, PIPER_SPEAKERS.get(voice, PIPER_SPEAKERS[DEFAULT_VOICE]))
        if wav is None:
            self._piper_failed(self.piper.why())
            return False
        self._piper_failures = 0
        try:
            with self._cond:
                if self._stops != stops:
                    return True                # stopped while it was being made
                try:
                    self._process = subprocess.Popen(
                        self._player_command(wav, volume),
                        stdin=subprocess.DEVNULL,
                        stdout=subprocess.DEVNULL,
                        stderr=subprocess.DEVNULL)
                except OSError as e:
                    log("could not start %s: %s" % (self.player_name, e))
                    self._process = None
                    return False
                proc = self._process
            # Outside the lock while playing, so STOP can get in and kill it.
            try:
                proc.wait()
            except OSError:
                pass
            with self._cond:
                if self._process is proc:
                    self._process = None
            return True
        finally:
            remove_quietly(wav)

    def _speak_system(self, text, language, stops, rate, volume):
        """Speak one line with spd-say / espeak-ng / espeak."""
        if self.path is None:
            # No engine found. Accept and drop, so the mod keeps running; the
            # startup log already said why.
            return
        cmd, stdin_text = self._command(text, language, rate, volume)
        with self._cond:
            if self._stops != stops:
                return
            if self.engine == "spd-say":
                self._dispatcher_used = True
            try:
                self._process = subprocess.Popen(
                    cmd,
                    stdin=subprocess.PIPE if stdin_text is not None else subprocess.DEVNULL,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL)
            except OSError as e:
                log("could not start %s: %s" % (self.engine, e))
                self._process = None
                return
            proc = self._process
        # Outside the lock while speaking, so STOP can get in and kill it.
        try:
            if stdin_text is not None:
                proc.communicate(stdin_text.encode("utf-8"))
            else:
                proc.wait()
        except (OSError, ValueError):
            pass
        with self._cond:
            if self._process is proc:
                self._process = None


class Handler(socketserver.StreamRequestHandler):
    """One connection from the game. Reads messages line by line."""

    def handle(self):
        speaker = self.server.speaker
        peer = "%s:%s" % self.client_address
        log("game connected (%s)" % peer)
        speaker.connected()
        try:
            self._read_messages(speaker)
        finally:
            speaker.disconnected()
        log("game disconnected (%s)" % peer)

    def _read_messages(self, speaker):
        while True:
            try:
                raw = self.rfile.readline(MAX_LINE_BYTES + 1)
            except OSError:
                break
            if not raw:
                break                      # the game closed the connection
            line = raw.decode("utf-8", errors="replace").rstrip("\r\n")

            # Split "COMMAND rest-of-line" at the first space only.
            command, _, text = line.partition(" ")

            if command == "HELLO":
                reply = "OK ikma-speech %s %s\n" % (PROTOCOL_VERSION, speaker.describe())
                self.wfile.write(reply.encode("utf-8"))
                self.wfile.flush()
            elif command == "SAY":
                if text:
                    speaker.say(text)
            elif command == "INTERRUPT":
                if text:
                    speaker.interrupt(text)
                else:
                    speaker.stop()
            elif command == "STOP":
                speaker.stop()
            elif command == "LANG":
                speaker.set_language(text)
            elif command == "VOICE":
                speaker.set_voice(text)
            elif command == "RATE":
                speaker.set_rate(text)
            elif command == "VOLUME":
                speaker.set_volume(text)
            elif command == "BUSY?":
                self.wfile.write(("BUSY %d\n" % (1 if speaker.busy() else 0)).encode("utf-8"))
                self.wfile.flush()
            elif self.server.verbose:
                log("ignored unknown message: %r" % line[:80])

            if self.server.verbose and command in ("SAY", "INTERRUPT"):
                log("%s %s" % (command, text[:120]))


class Server(socketserver.ThreadingTCPServer):
    # Lets the helper restart immediately on the same port instead of
    # waiting a minute for the operating system to release it.
    allow_reuse_address = True
    # Connection threads die with the program instead of holding it open.
    daemon_threads = True


def setup_piper(args):
    """(Piper, player) if the Piper voice can be used, else (None, player)."""
    player = find_player()
    if args.no_piper:
        log("Piper turned off (--no-piper)")
        return None, player
    missing = [p for p in (args.piper, args.piper_model, args.piper_model + ".json")
               if not os.path.isfile(p)]
    if missing:
        log("Piper voice not installed (missing %s); using the system voice. "
            "Run the IKMA install script again to add it." % ", ".join(missing))
        return None, player
    if player[1] is None:
        log("Piper is installed but nothing can play its sound (looked for %s); "
            "using the system voice" % ", ".join(PLAYERS))
        return None, player
    # Piper's sound files are made in memory-backed storage when there is
    # some ($XDG_RUNTIME_DIR, normally /run/user/1000) and deleted once played.
    base = os.environ.get("XDG_RUNTIME_DIR")
    out_dir = tempfile.mkdtemp(prefix="ikma-piper-", dir=base if base and os.path.isdir(base) else None)
    log("English voice: Piper (%s), played with %s" % (args.piper_model, player[0]))
    return Piper(args.piper, args.piper_model, out_dir), player


def main():
    parser = argparse.ArgumentParser(description="IKMA speech helper for Steam Deck / Linux.")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT,
                        help="port on 127.0.0.1 to listen on (default %d; must match BridgePort in IKMA's config)" % DEFAULT_PORT)
    parser.add_argument("--engine", default="auto", choices=("auto",) + ENGINE_ORDER,
                        help="system speech program, for languages Piper does not read and if Piper fails (default: spd-say, then espeak-ng, then espeak)")
    parser.add_argument("--no-piper", action="store_true",
                        help="do not use the Piper voice, even if it is installed")
    parser.add_argument("--piper", default=PIPER_EXE, help="the Piper program (default %(default)s)")
    parser.add_argument("--piper-model", default=PIPER_MODEL, help="the Piper voice file (default %(default)s)")
    parser.add_argument("--voice", default=DEFAULT_VOICE, choices=sorted(PIPER_SPEAKERS),
                        help="Piper voice until the game says otherwise (default %(default)s)")
    parser.add_argument("--speed", type=int, default=100,
                        help="speaking speed in percent until the game says otherwise (default 100)")
    parser.add_argument("--volume", type=int, default=100,
                        help="volume in percent until the game says otherwise (default 100)")
    parser.add_argument("--test", metavar="TEXT",
                        help="speak TEXT once and exit - checks that speech works without the game")
    parser.add_argument("--verbose", action="store_true",
                        help="log every line received")
    args = parser.parse_args()

    engine, path = find_engine(args.engine)
    if path is None:
        log("NO SYSTEM SPEECH PROGRAM FOUND. Looked for: %s. Without it only English "
            "can be spoken, and only by Piper." % ", ".join(
                ENGINE_ORDER if args.engine == "auto" else (args.engine,)))
    else:
        log("system voice: %s (%s)" % (engine, path))

    piper, player = setup_piper(args)
    speaker = Speaker(engine, path, piper, player, args.voice,
                      max(25, min(400, args.speed)), max(MIN_VOLUME, min(100, args.volume)))

    try:
        if args.test is not None:
            speaker.say(args.test)
            # Wait for the line to be picked up and finished, up to 30 seconds.
            deadline = time.time() + 30
            time.sleep(0.2)
            while speaker.busy() and time.time() < deadline:
                time.sleep(0.1)
            return 0 if (path is not None or piper is not None) else 1

        try:
            server = Server(("127.0.0.1", args.port), Handler)
        except OSError as e:
            log("cannot listen on 127.0.0.1:%d (%s). Is another copy already running?" % (args.port, e))
            return 1
        server.speaker = speaker
        server.verbose = args.verbose

        # systemd stops a service with SIGTERM. Turn that into a clean exit:
        # stop speaking, close the port.
        def on_term(signum, frame):
            raise KeyboardInterrupt
        signal.signal(signal.SIGTERM, on_term)

        log("listening on 127.0.0.1:%d" % args.port)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
        finally:
            server.server_close()
        return 0
    finally:
        speaker.shutdown()
        if piper is not None:
            shutil.rmtree(piper.out_dir, ignore_errors=True)
        log("stopped")


if __name__ == "__main__":
    sys.exit(main())

# ikma_speech_helper.py
