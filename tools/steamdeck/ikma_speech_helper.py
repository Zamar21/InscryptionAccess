# ikma_speech_helper.py
#
# The IKMA speech helper for Steam Deck and Linux. (Session 32. UNTESTED ON
# A DECK.)
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
# REQUIREMENTS, and why they are what they are:
#   - Python 3 standard library only. Nothing to install with pip, because
#     SteamOS's system folders are read-only and are wiped by updates.
#   - Runs from the user's home folder, as the normal user. No root, ever.
#   - Speaks with speech-dispatcher's `spd-say` if present, otherwise
#     `espeak-ng` (or the older `espeak`). It does not install either.
#   - Listens on 127.0.0.1 ONLY, never on the network, so nothing outside
#     the Deck can make it talk.
#
# THE PROTOCOL. One line of UTF-8 text per message, ending in "\n". It must
# match IKMAccess/LinuxBridgeBackend.cs, which is the other end:
#
#   HELLO 1              mod -> helper, once per connection.
#   OK ikma-speech 1 X   helper -> mod, the reply. X = engine in use
#                        (spd-say, espeak-ng, espeak, or none).
#   SAY text             speak after whatever is already queued.
#   INTERRUPT text       stop now, drop the queue, then speak this.
#   STOP                 stop now, drop the queue, say nothing.
#   LANG name            the language the lines after this one are in, by
#                        IKMA's own name for it ("French", "Japanese", ...).
#                        Session 32. No reply. A helper that predates it
#                        ignores it, and speaks in the engine's default voice.
#   BUSY?                helper -> "BUSY 1" or "BUSY 0". Reserved: the mod
#                        does not ask yet (it would change timing; see
#                        WEEKEND_NOTES.md).
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
import shutil
import signal
import socket
import socketserver
import subprocess
import sys
import threading
import time

PROTOCOL_VERSION = "1"

# Must match SpeechBackends.DEFAULT_BRIDGE_PORT in SpeechBackend.cs.
DEFAULT_PORT = 17432

# A line longer than this is almost certainly not speech. Cap it so a broken
# client cannot hand an engine a megabyte of text.
MAX_LINE_BYTES = 16384

ENGINE_ORDER = ("spd-say", "espeak-ng", "espeak")

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


class Speaker:
    """Speaks queued lines one at a time on a background thread.

    WHY A THREAD. Speaking a line takes seconds. If the network code waited
    for it, a STOP arriving mid-sentence could not be read until the sentence
    ended - which defeats the point of STOP. So the network side only ever
    puts lines on a queue or clears it, and returns at once; this thread does
    the slow part.

    HOW STOP WORKS. Each line is spoken by starting the engine as a separate
    process and waiting for it to finish. To stop, we (1) empty the queue,
    (2) kill the process that is speaking right now, and (3) for
    speech-dispatcher, also tell the dispatcher itself to cancel, because
    killing the little `spd-say` client does not always stop the dispatcher
    from finishing the sentence it was already given.
    """

    def __init__(self, engine, path, rate=None):
        self.engine = engine
        self.path = path
        self.rate = rate
        # The engine option for the current language: spd-say's -l code or an
        # espeak voice, or None = the engine's default voice. Each queued line
        # carries the value current when it ARRIVED, so a LANG never changes
        # the voice of a line that was already waiting.
        self._language = None
        self._voice_cache = {}
        self._queue = collections.deque()
        # A Condition is a lock plus a way for one thread to sleep until
        # another says "something changed". Every touch of _queue or
        # _process happens while holding it.
        self._cond = threading.Condition()
        self._process = None
        # Set by a stop; the speaker thread clears it by telling
        # speech-dispatcher to cancel. See _stop_locked.
        self._cancel_pending = False
        self._running = True
        self._thread = threading.Thread(target=self._loop, name="speaker", daemon=True)
        self._thread.start()

    # ---- called from the network threads -------------------------------

    def say(self, text):
        with self._cond:
            self._queue.append((text, self._language))
            self._cond.notify()

    def set_language(self, name):
        """LANG from the game: pick the engine option for that language.

        Unknown names, and languages the engine has no voice for, fall back to
        the engine's default voice with one line in the log - never to silence.
        """
        option = None
        entry = LANGUAGES.get(name.strip())
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
        with self._cond:
            changed = option != self._language
            self._language = option
        if changed:
            log("language: %s (%s)" % (name, option or "default voice"))

    def interrupt(self, text):
        # Stop and enqueue under ONE hold of the lock, so the speaker thread
        # cannot slip an old queued line in between the two.
        with self._cond:
            self._stop_locked()
            self._queue.append((text, self._language))
            self._cond.notify()

    def stop(self):
        with self._cond:
            self._stop_locked()

    def busy(self):
        with self._cond:
            return self._process is not None or len(self._queue) > 0

    def shutdown(self):
        with self._cond:
            self._running = False
            self._stop_locked()
            self._cond.notify()
        # The speaker thread is exiting and will not run the pending cancel,
        # so do it here: a helper that is stopped must not leave the
        # dispatcher finishing a sentence.
        if self.engine == "spd-say" and self.path is not None:
            self._cancel_dispatcher()

    # ---- internals -----------------------------------------------------

    def _stop_locked(self):
        self._queue.clear()
        proc = self._process
        if proc is not None and proc.poll() is None:
            try:
                proc.kill()
            except OSError:
                pass
        if self.engine == "spd-say":
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

    def _command(self, text, language=None):
        """The command line that speaks one line with the chosen engine."""
        if self.engine == "spd-say":
            # -w = wait until the line has been SPOKEN before exiting, which
            #      is how this thread knows when to start the next one.
            # --  = "no more options": a line starting with "-" is text.
            cmd = [self.path, "-w"]
            if self.rate is not None:
                cmd += ["-r", str(self.rate)]      # -100 .. 100
            if language is not None:
                cmd += ["-l", language]            # e.g. fr, pt-BR, ja
            return cmd + ["--", text], None
        # espeak-ng / espeak already block until done. Text goes in on
        # standard input (--stdin) so no line is ever mistaken for an option.
        # -b 1 = the text is UTF-8 (needed for accents, Cyrillic, Chinese...).
        cmd = [self.path, "--stdin", "-b", "1"]
        if self.rate is not None:
            cmd += ["-s", str(self.rate)]          # words per minute
        if language is not None:
            cmd += ["-v", language]                # a voice checked to exist
        return cmd, text

    def _loop(self):
        while True:
            with self._cond:
                while self._running and not self._queue and not self._cancel_pending:
                    self._cond.wait()
                if not self._running:
                    return
                cancel = self._cancel_pending
                self._cancel_pending = False
            if cancel:
                self._cancel_dispatcher()
            with self._cond:
                # Re-check: a STOP may have arrived while cancelling.
                if self._cancel_pending or not self._queue:
                    continue
                text, language = self._queue.popleft()
                if self.path is None:
                    # No engine found. Accept and drop, so the mod keeps
                    # running; the startup log already said why.
                    continue
                cmd, stdin_text = self._command(text, language)
                try:
                    self._process = subprocess.Popen(
                        cmd,
                        stdin=subprocess.PIPE if stdin_text is not None else subprocess.DEVNULL,
                        stdout=subprocess.DEVNULL,
                        stderr=subprocess.DEVNULL)
                except OSError as e:
                    log("could not start %s: %s" % (self.engine, e))
                    self._process = None
                    continue
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
                reply = "OK ikma-speech %s %s\n" % (PROTOCOL_VERSION, speaker.engine)
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
            elif command == "BUSY?":
                self.wfile.write(("BUSY %d\n" % (1 if speaker.busy() else 0)).encode("utf-8"))
                self.wfile.flush()
            elif self.server.verbose:
                log("ignored unknown message: %r" % line[:80])

            if self.server.verbose and command in ("SAY", "INTERRUPT"):
                log("%s %s" % (command, text[:120]))
        log("game disconnected (%s)" % peer)


class Server(socketserver.ThreadingTCPServer):
    # Lets the helper restart immediately on the same port instead of
    # waiting a minute for the operating system to release it.
    allow_reuse_address = True
    # Connection threads die with the program instead of holding it open.
    daemon_threads = True


def main():
    parser = argparse.ArgumentParser(description="IKMA speech helper for Steam Deck / Linux.")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT,
                        help="port on 127.0.0.1 to listen on (default %d; must match BridgePort in IKMA's config)" % DEFAULT_PORT)
    parser.add_argument("--engine", default="auto", choices=("auto",) + ENGINE_ORDER,
                        help="speech program to use (default: spd-say, then espeak-ng, then espeak)")
    parser.add_argument("--rate", type=int, default=None,
                        help="speech rate passed to the engine as-is (spd-say: -100..100; espeak-ng: words per minute). Default: the engine's own.")
    parser.add_argument("--test", metavar="TEXT",
                        help="speak TEXT once and exit - checks that speech works without the game")
    parser.add_argument("--verbose", action="store_true",
                        help="log every line received")
    args = parser.parse_args()

    engine, path = find_engine(args.engine)
    if path is None:
        log("NO SPEECH PROGRAM FOUND. Looked for: %s. The helper will run so the game "
            "can connect, but it cannot speak." % ", ".join(
                ENGINE_ORDER if args.engine == "auto" else (args.engine,)))
    else:
        log("speaking with %s (%s)" % (engine, path))

    speaker = Speaker(engine, path, args.rate)

    if args.test is not None:
        speaker.say(args.test)
        # Wait for the line to be picked up and finished, up to 30 seconds.
        deadline = time.time() + 30
        time.sleep(0.2)
        while speaker.busy() and time.time() < deadline:
            time.sleep(0.1)
        speaker.shutdown()
        return 0 if path is not None else 1

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
        speaker.shutdown()
        server.server_close()
        log("stopped")
    return 0


if __name__ == "__main__":
    sys.exit(main())

# ikma_speech_helper.py
