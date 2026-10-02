# ikma_drive.py
# Client for DevDriver.cs (dev builds only). Runs in Claude's shell on
# Zamar's PC, where the BepInEx folder is mounted.
#
#   python3 ikma_drive.py "key Space" "quiet 800 8000"
#       sends the commands in order, waits for the last one to finish, and
#       prints everything IKMA said meanwhile.
#   python3 ikma_drive.py --tail 40      the last 40 lines of out.txt
#
# Mailbox: BepInEx/ikma_dev/in.txt (commands) and out.txt (events). See
# DevDriver.cs for the verbs.
import os, sys, time, uuid

ROOT = os.environ.get("IKMA_DEV_DIR",
                      os.path.expanduser("~/mnt/BepInEx/ikma_dev"))
IN, OUT = os.path.join(ROOT, "in.txt"), os.path.join(ROOT, "out.txt")
# Where the last call stopped reading, so lines spoken BETWEEN calls are
# printed by the next call instead of being skipped. Lives outside the
# mailbox (the game rewrites the mailbox at launch).
CURSOR = os.path.expanduser("~/.ikma_drive_cursor")

def load_cursor():
    try:
        tag, n = open(CURSOR).read().rsplit(" ", 1)   # the tag has spaces
        return tag, int(n)
    except Exception:
        return None, 0

def save_cursor(tag, n):
    with open(CURSOR, "w") as f:
        f.write(f"{tag} {n}")

def read_out():
    try:
        with open(OUT, encoding="utf-8", errors="replace") as f:
            return [l.rstrip("\n").split("\t", 2) for l in f if l.strip()]
    except FileNotFoundError:
        return []

def main(argv):
    if not os.path.isdir(ROOT):
        sys.exit(f"no mailbox at {ROOT} - is a dev build of IKMA running?")
    if argv and argv[0] == "--tail":
        n = int(argv[1]) if len(argv) > 1 else 30
        for row in read_out()[-n:]:
            print("\t".join(row))
        return
    rows0 = read_out()
    tag = rows0[0][2] if rows0 and len(rows0[0]) == 3 else ""   # READY line = this launch
    ctag, cpos = load_cursor()
    start = cpos if ctag == tag and cpos <= len(rows0) else len(rows0)
    ids = []
    with open(IN, "a", encoding="utf-8") as f:
        for cmd in argv:
            cid = uuid.uuid4().hex[:6]
            ids.append(cid)
            f.write(f"{cid} {cmd}\n")
    last = ids[-1]
    deadline = time.time() + float(os.environ.get("IKMA_DEV_TIMEOUT", "60"))
    while time.time() < deadline:
        rows = read_out()[start:]
        if any(r[1] in ("DONE", "ERR") and r[2].split(" ")[0] == last
               for r in rows if len(r) == 3):
            break
        time.sleep(0.2)
    else:
        print("TIMEOUT waiting for the game (focused? runbg on?)")
    rows = read_out()
    for r in rows[start:]:
        if len(r) == 3 and r[1] != "DONE":
            print(f"{r[1]}\t{r[2]}")
    save_cursor(tag, len(rows))

if __name__ == "__main__":
    main(sys.argv[1:])
# ikma_drive.py
