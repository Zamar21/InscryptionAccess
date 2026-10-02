# test_speech_helper.py
#
# Tests the Steam Deck speech helper (tools/steamdeck/ikma_speech_helper.py)
# with no Deck and no speech engine. (Session 32.) Linux or macOS:
#     python3 tools/checks/test_speech_helper.py
#
# HOW. A fake "espeak-ng" (a tiny shell script in a temporary folder) is put
# first on the PATH. It writes the command line and text it was given to a
# file instead of speaking. The helper is started on a spare port, and this
# script talks to it exactly as IKMA's LinuxBridgeBackend does (HELLO, SAY,
# LANG, STOP), then checks what the fake engine was asked to say.
#
# The fake engine has French, Japanese and plain Portuguese voices only, so
# the test also covers the fallbacks: Brazilian Portuguese falls back to
# "pt", Korean (no voice) and an unknown name fall back to the default voice.

import os
import socket
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
HELPER = os.path.join(HERE, "..", "steamdeck", "ikma_speech_helper.py")
PORT = 17998

FAKE = r'''#!/bin/bash
for a in "$@"; do case $a in
  --voices=fr|--voices=ja|--voices=pt) echo "Pty Language Age/Gender VoiceName"; echo " 5  xx  M  voice";;
  --voices=*) echo "Pty Language Age/Gender VoiceName";;
esac; done
case "$*" in *--voices*) exit 0;; esac
echo "ARGS: $* | $(cat)" >> "$SAID"
'''

EXPECT = [
    ("SAY one", "--stdin -b 1 | one"),
    ("LANG French", None),
    ("SAY deux", "--stdin -b 1 -v fr | deux"),
    ("LANG BrazilianPortuguese", None),
    ("SAY três", "--stdin -b 1 -v pt | três"),
    ("LANG Korean", None),
    ("SAY 넷", "--stdin -b 1 | 넷"),
    ("LANG Klingon", None),
    ("SAY five", "--stdin -b 1 | five"),
]


def main():
    tmp = tempfile.mkdtemp()
    fake = os.path.join(tmp, "espeak-ng")
    with open(fake, "w") as f:
        f.write(FAKE)
    os.chmod(fake, 0o755)
    said = os.path.join(tmp, "said.txt")
    env = dict(os.environ, PATH=tmp + os.pathsep + os.environ.get("PATH", ""), SAID=said)
    helper = subprocess.Popen([sys.executable, HELPER, "--engine", "espeak-ng", "--port", str(PORT)],
                              env=env, stderr=subprocess.PIPE)
    try:
        for _ in range(50):
            try:
                s = socket.create_connection(("127.0.0.1", PORT), timeout=1)
                break
            except OSError:
                time.sleep(0.1)
        else:
            print("FAIL: the helper did not start")
            return 1
        f = s.makefile("rwb")
        f.write(b"HELLO 1\n"); f.flush()
        hello = f.readline().decode().strip()
        ok = hello == "OK ikma-speech 1 espeak-ng"
        print(("ok  " if ok else "FAIL") + " handshake: " + hello)
        for msg, _ in EXPECT:
            f.write((msg + "\n").encode("utf-8")); f.flush()
        time.sleep(2)
        s.close()
    finally:
        helper.terminate()
        helper.wait(5)
    got = open(said, encoding="utf-8").read().splitlines() if os.path.exists(said) else []
    want = [w for _, w in EXPECT if w]
    fails = 0 if ok else 1
    for i, w in enumerate(want):
        g = got[i][len("ARGS: "):] if i < len(got) else "(nothing)"
        good = g == w
        fails += not good
        print(("ok  " if good else "FAIL") + " %s   (got: %s)" % (w, g))
    if len(got) != len(want):
        print("FAIL: %d lines spoken, %d expected" % (len(got), len(want)))
        fails += 1
    print("test_speech_helper: %d FAIL(s)." % fails)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())

# test_speech_helper.py
