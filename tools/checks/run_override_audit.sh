#!/bin/bash
# run_override_audit.sh
#
# Builds and runs OverrideAudit.cs (see its header). Linux, with the .NET SDK
# (the Cowork cloud container has it). Session 32.
#
#   bash tools/checks/run_override_audit.sh <folder with Assembly-CSharp.dll and Mono.Cecil.dll> [name regex]
#
# The folder is usually a copy of the game's Managed folder with BepInEx's
# core\Mono.Cecil.dll added. The optional regex keeps only overriding
# classes whose name matches, e.g. 'Part3|Holo' for Act 3, 'Pixel|GBC' for
# Act 2. Nothing is written into the repo.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
REFS="$1"; FILTER="${2:-.}"
[ -f "$REFS/Assembly-CSharp.dll" ] && [ -f "$REFS/Mono.Cecil.dll" ] || { echo "need Assembly-CSharp.dll and Mono.Cecil.dll in $REFS"; exit 2; }
WORK="$(mktemp -d)"

# 1. Every method IKMA patches, from its own source: AccessTools.Method(typeof(T), "M")
#    and [HarmonyPatch(typeof(T), "M")] (or nameof(...)).
python3 - "$REPO" > "$WORK/targets.txt" <<'PY'
import glob, os, re, sys
t = set()
pat = [r'AccessTools\.(?:Method|PropertyGetter|DeclaredMethod)\(\s*typeof\(([\w.]+)\)\s*,\s*(?:"(\w+)"|nameof\([\w.]*?(\w+)\))',
       r'HarmonyPatch\(\s*typeof\(([\w.]+)\)\s*,\s*(?:"(\w+)"|nameof\([\w.]*?(\w+)\))']
for f in glob.glob(os.path.join(sys.argv[1], "*.cs")):
    s = open(f, encoding="utf-8").read()
    for p in pat:
        for m in re.finditer(p, s):
            t.add((m.group(1).split(".")[-1], m.group(2) or m.group(3)))
for a, b in sorted(t):
    print(a, b)
PY
echo "IKMA patches $(wc -l < "$WORK/targets.txt") methods."

# 2. Build the audit with the SDK's own compiler, against the runtime's assemblies.
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | head -1)
[ -n "$CSC" ] || CSC=$(ls "$HOME"/.dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
RT=$(dirname "$(ls "${CSC%%/sdk/*}"/shared/Microsoft.NETCore.App/*/System.Runtime.dll | head -1)")
refs=""; for f in "$RT"/*.dll; do case "$f" in *Native*) ;; *) refs="$refs -r:$f";; esac; done
dotnet "$CSC" -nologo -out:"$WORK/audit.dll" -r:"$REFS/Mono.Cecil.dll" $refs "$HERE/OverrideAudit.cs" >/dev/null
cp "$REFS/Mono.Cecil.dll" "$WORK/"
V=$(basename "$RT")
printf '{"runtimeOptions":{"tfm":"net%s","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' "${V%.*.*}.0" "$V" > "$WORK/audit.runtimeconfig.json"

# 3. Run.
dotnet "$WORK/audit.dll" "$REFS/Assembly-CSharp.dll" "$WORK/targets.txt" | awk -F'\t' -v f="$FILTER" '$2 ~ f'
rm -rf "$WORK"
# run_override_audit.sh
