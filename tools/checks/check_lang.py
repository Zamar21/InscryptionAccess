# check_lang.py
#
# Checks every language file IKMA ships, with no game and no Windows.
# (Session 32.) Run from anywhere:
#     python3 tools/checks/check_lang.py
#
# Two sets of files, same format (English [TAB] translation, # comments):
#   lang/<Language>.tsv                  the mod's spoken lines (Loc.cs)
#   tools/installer/lang/<Language>.tsv  IKMA Setup's text (SetupLoc.cs)
#
# For each set, English.tsv is the list of keys. Every other language must:
#   - have exactly the same English keys, in any order, none missing or extra
#     (a missing line is said in English - allowed by the code, but here it
#     is reported, because it usually means a file fell behind);
#   - use only the {0} {1} blanks its English line has (the code says a
#     line with a blank too many in English - reported as a FAIL);
#   - contain no raw tab inside a translation and no leading/trailing space
#     the English does not have.
# Exit code 0 = all clean, 1 = something to fix. Prints one line per problem.

import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SETS = [os.path.join(ROOT, "lang"), os.path.join(ROOT, "tools", "installer", "lang")]
BLANK = re.compile(r"(?<!\{)\{(\d+)\}")


def unescape(s):
    out, i = [], 0
    while i < len(s):
        c = s[i]
        if c == "\\" and i + 1 < len(s):
            n = s[i + 1]
            out.append("\t" if n == "t" else "\n" if n == "n" else n)
            i += 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def read(path):
    rows = {}
    with open(path, encoding="utf-8-sig") as f:
        for n, line in enumerate(f, 1):
            line = line.rstrip("\r\n")
            if not line or line.startswith("#"):
                continue
            if "\t" not in line:
                yield ("warn", n, "no tab: %r" % line[:60])
                continue
            eng, tr = line.split("\t", 1)
            if "\t" in tr:
                yield ("fail", n, "second tab in the translation")
            rows[unescape(eng)] = (n, unescape(tr))
    yield ("rows", 0, rows)


def check_set(folder):
    problems = 0
    english_path = os.path.join(folder, "English.tsv")
    if not os.path.exists(english_path):
        return 0
    keys = None
    for kind, n, v in read(english_path):
        if kind == "rows":
            keys = v
    for name in sorted(os.listdir(folder)):
        if not name.endswith(".tsv") or name == "English.tsv":
            continue
        rows = None
        where = os.path.relpath(os.path.join(folder, name), ROOT)
        for kind, n, v in read(os.path.join(folder, name)):
            if kind == "rows":
                rows = v
            else:
                print("%s %s:%d %s" % (kind.upper(), where, n, v))
                problems += kind == "fail"
        missing = [k for k in keys if k not in rows or not rows[k][1]]
        extra = [k for k in rows if k not in keys]
        if missing:
            print("WARN %s: %d lines not translated (first: %r)" % (where, len(missing), missing[0][:60]))
        if extra:
            print("FAIL %s: %d lines whose English is not in English.tsv (first: %r)" % (where, len(extra), extra[0][:60]))
            problems += 1
        for eng, (n, tr) in rows.items():
            if not tr:
                continue
            allowed = max([int(b) for b in BLANK.findall(eng)] or [-1])
            used = max([int(b) for b in BLANK.findall(tr)] or [-1])
            if used > allowed:
                print("FAIL %s:%d blank {%d} not in the English" % (where, n, used))
                problems += 1
            if (tr[:1] == " ") != (eng[:1] == " ") or (tr[-1:] == " ") != (eng[-1:] == " "):
                print("FAIL %s:%d edge space differs from the English" % (where, n))
                problems += 1
    return problems


def main():
    total = sum(check_set(f) for f in SETS)
    print("check_lang: %d FAIL(s)." % total)
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())

# check_lang.py
