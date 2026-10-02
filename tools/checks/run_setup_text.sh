#!/bin/bash
# run_setup_text.sh
#
# Checks IKMA Setup's text in every language, with no Windows. (Session 32.)
# Linux with the .NET SDK (the Cowork cloud container has it):
#     bash tools/checks/run_setup_text.sh          check all languages
#     bash tools/checks/run_setup_text.sh French   also print French in full
#
# It compiles Setup's source (renaming its Main, so the test's Main runs)
# with the language files built in, prints every sentence in English and
# in each language, and FAILS a language when a sentence comes out in
# English that should have been translated - the sign of a key that no
# longer matches (a sentence edited in Program.cs but not in the .tsv
# files). The few sentences that are the same in every language (the
# product name, "1, TITLE: VALUE") are allowed.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$HERE/../installer"
WORK="$(mktemp -d)"
sed 's/private static int Main(string\[\] args)/private static int SetupMain(string[] args)/' "$SRC/Program.cs" > "$WORK/Program.cs"
cp "$SRC/Settings.cs" "$SRC/SetupLoc.cs" "$HERE/SetupTextTest.cs" "$WORK/"

CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | head -1)
[ -n "$CSC" ] || CSC=$(ls "$HOME"/.dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
RT=$(dirname "$(ls "${CSC%%/sdk/*}"/shared/Microsoft.NETCore.App/*/System.Runtime.dll | head -1)")
refs=""; for f in "$RT"/*.dll; do case "$f" in *Native*) ;; *) refs="$refs -r:$f";; esac; done
res=""; for f in "$SRC"/lang/*.tsv; do res="$res -resource:$f,setup_lang/$(basename "$f")"; done
dotnet "$CSC" -nologo -langversion:latest -out:"$WORK/t.dll" $refs $res "$WORK"/*.cs >/dev/null
V=$(basename "$RT")
printf '{"runtimeOptions":{"tfm":"net%s","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' "${V%.*.*}.0" "$V" > "$WORK/t.runtimeconfig.json"

dotnet "$WORK/t.dll" --language English > "$WORK/English.txt"
fails=0
for f in "$SRC"/lang/*.tsv; do
  L=$(basename "$f" .tsv); [ "$L" = English ] && continue
  dotnet "$WORK/t.dll" --language "$L" > "$WORK/$L.txt"
  # Lines identical to English that are allowed to be: the product name
  # line in languages that keep "version", and the numbers-only lines.
  same=$(paste -d'\t' "$WORK/English.txt" "$WORK/$L.txt" | awk -F'\t' '$1==$2' \
         | grep -v -E '^IKMA Setup, version|^[0-9]+, [A-Z]+: [A-Z]+|^Updates	' || true)
  if [ -n "$same" ]; then
    echo "FAIL $L: still English:"; echo "$same" | cut -f1 | sed 's/^/    /'; fails=$((fails+1))
  else
    echo "ok   $L"
  fi
  [ "$1" = "$L" ] && cat "$WORK/$L.txt"
done
rm -rf "$WORK"
echo "run_setup_text: $fails FAIL(s)."
[ $fails -eq 0 ]
# run_setup_text.sh
