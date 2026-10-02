# sync_lang_keys.py
#
# Finds the spoken lines the CODE has that English.tsv does not. (Session 34.)
# check_lang.py compares the language files with English.tsv; nothing
# compared English.tsv with the code, so it fell behind (Sessions 33-34).
# Run from anywhere:
#     python3 tools/checks/sync_lang_keys.py          report only
#     python3 tools/checks/sync_lang_keys.py --write  also add the missing
#                                                    lines to English.tsv
#
# Two sets, same as check_lang.py:
#   Vocabulary.cs + Loc.cs, Loc.T / Loc.F   -> lang/English.tsv
#   tools/installer/*.cs, L.T / L.F         -> tools/installer/lang/English.tsv
#
# The key is what Loc.T / Loc.F look up: the English text, and for Loc.F
# ($"..." strings) the format string the compiler makes, where each {expr}
# becomes {0}, {1} ... in order, keeping any ,alignment or :format.
#
# --write only ADDS (with an empty translation column and a "# where" note).
# Lines in English.tsv the code no longer has are REPORTED, never removed:
# removing one would make every translated file FAIL check_lang.py, and
# some may be lines whose wording is still being decided.
# Calls with no string literal (Loc.T(someVariable)) are counted, not read.

import glob
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SETS = [
    ("mod", [os.path.join(ROOT, "Vocabulary.cs"), os.path.join(ROOT, "Loc.cs")],
     r"\bLoc\.(T|F)\(", os.path.join(ROOT, "lang", "English.tsv")),
    ("installer", sorted(glob.glob(os.path.join(ROOT, "tools", "installer", "*.cs"))),
     r"(?<![\w.])L\.(T|F)\(", os.path.join(ROOT, "tools", "installer", "lang", "English.tsv")),
]
MEMBER = re.compile(r"^\s*(?:public|internal|private)\s+static\s+[\w<>\[\],. ]+?\s+(\w+)\s*(?:\(|=>|$|\{)")


def unescape_tsv(s):
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


def escape_tsv(s):
    return s.replace("\\", "\\\\").replace("\t", "\\t").replace("\n", "\\n")


def read_literal(src, i):
    """Parse one C# string literal at src[i]. Returns (text, interpolated, end) or None."""
    j = i
    interp = verbatim = False
    while j < len(src) and src[j] in "$@":
        interp |= src[j] == "$"
        verbatim |= src[j] == "@"
        j += 1
    if j >= len(src) or src[j] != '"':
        return None
    j += 1
    out, hole = [], 0
    while j < len(src):
        c = src[j]
        if verbatim and c == '"' and src[j + 1:j + 2] == '"':
            out.append('"'); j += 2; continue
        if c == '"':
            return "".join(out), interp, j + 1
        if not verbatim and c == "\\":
            n = src[j + 1]
            out.append({"n": "\n", "t": "\t", "r": "\r", "0": "\0"}.get(n, n)); j += 2; continue
        if interp and c == "{":
            if src[j + 1:j + 2] == "{":
                out.append("{{"); j += 2; continue
            # A hole: skip the expression, keep ,alignment and :format.
            k, depth, tail = j + 1, 0, None
            while k < len(src):
                d = src[k]
                if d in "([{": depth += 1
                elif d in ")]": depth -= 1
                elif d == "}":
                    if depth == 0: break
                    depth -= 1
                elif d == '"' or (d in "$@" and src[k + 1:k + 2] in '"$@'):
                    lit = read_literal(src, k)
                    if lit: k = lit[2]; continue
                elif d == "'":
                    k = src.index("'", k + 2 if src[k + 1] == "\\" else k + 1) + 1; continue
                elif d in ",:" and depth == 0 and tail is None:
                    tail = k
                k += 1
            spec = src[tail:k] if tail is not None else ""
            out.append("{%d%s}" % (hole, spec)); hole += 1
            j = k + 1; continue
        if interp and c == "}" and src[j + 1:j + 2] == "}":
            out.append("}}"); j += 2; continue
        out.append(c); j += 1
    return None


def keys_in(files, call):
    found, skipped = {}, 0
    for path in files:
        src = open(path, encoding="utf-8-sig").read()
        lines = src.split("\n")
        starts, pos = [], 0
        for ln in lines:
            starts.append(pos); pos += len(ln) + 1
        for m in re.finditer(call, src):
            i = m.end()
            while i < len(src) and src[i].isspace(): i += 1
            parts, ok = [], True
            while True:
                lit = read_literal(src, i)
                if not lit: ok = False; break
                parts.append(lit[0]); i = lit[2]
                k = i
                while k < len(src) and src[k].isspace(): k += 1
                if src[k:k + 1] != "+": break
                k += 1
                while k < len(src) and src[k].isspace(): k += 1
                i = k
            if not ok or not parts:
                skipped += 1; continue
            key = "".join(parts)
            if key in found: continue
            line_no = sum(1 for s in starts if s <= m.start()) - 1
            where = os.path.basename(path)
            for back in range(line_no, max(-1, line_no - 40), -1):
                mm = MEMBER.match(lines[back])
                if mm: where = os.path.splitext(os.path.basename(path))[0] + "." + mm.group(1); break
            found[key] = where
    return found, skipped


def tsv_keys(path):
    keys = set()
    for ln in open(path, encoding="utf-8-sig").read().split("\n"):
        ln = ln.rstrip("\r")
        if not ln or ln.startswith("#"): continue
        keys.add(unescape_tsv(ln.split("\t", 1)[0]))
    return keys


def main():
    write = "--write" in sys.argv
    for name, files, call, tsv in SETS:
        code, skipped = keys_in(files, call)
        have = tsv_keys(tsv)
        missing = [k for k in code if k not in have]
        # A line that is a string literal anywhere in the set's folder is a
        # table string (reaches Loc.T through a variable): not stale.
        folder = os.path.dirname(files[0])
        src = "".join(open(f, encoding="utf-8-sig").read() for f in glob.glob(os.path.join(folder, "*.cs")))
        src = re.sub(r'"\s*\+\s*"', "", src)   # "a " + "b" split across lines reads as "a b"
        stale = [k for k in have if k not in code
                 and '"%s"' % k.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n") not in src]
        print("%s: %d lines in code, %d in English.tsv. MISSING from English.tsv: %d. In English.tsv, not found as a literal: %d. Calls without a literal: %d."
              % (name, len(code), len(have), len(missing), len(stale), skipped))
        for k in missing:
            print("  MISSING  %s: %s" % (code[k], escape_tsv(k)[:100]))
        # Not proof a line is dead: strings in Vocabulary's tables (sigil
        # sentences, mask descriptions) reach Loc.T through a variable and
        # are not seen here. Check each by hand before removing anything.
        for k in stale:
            print("  NOT FOUND AS A LITERAL (check by hand)  %s" % escape_tsv(k)[:100])
        if write and missing:
            text = open(tsv, encoding="utf-8-sig").read().rstrip("\n")
            add = "".join("\n# %s (added by sync_lang_keys.py)\n%s\t" % (code[k], escape_tsv(k)) for k in missing)
            open(tsv, "w", encoding="utf-8", newline="\n").write(text + add + "\n")
            print("  %d lines added to %s" % (len(missing), tsv))
    return 0


if __name__ == "__main__":
    sys.exit(main())
