#!/bin/bash
# run_nvda_standin.sh
#
# Runs the mod's NvdaDirect.cs against a stand-in for NVDA, with no Windows
# and no NVDA. (Session 50.) Linux with the .NET SDK (the Cowork cloud
# container: apt-get install -y dotnet-sdk-8.0):
#     bash tools/checks/run_nvda_standin.sh
# Takes about 40 seconds. Prints one line per check and ends with
# ALL PASSED, or the number that failed. See NvdaStandInTest.cs for what
# the stand-in is and is not.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d)"
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | head -1)
[ -n "$CSC" ] || CSC=$(ls "$HOME"/.dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
RT=$(dirname "$(ls "${CSC%%/sdk/*}"/shared/Microsoft.NETCore.App/*/System.Runtime.dll | head -1)")
refs=""; for f in "$RT"/*.dll; do case "$f" in *Native*) ;; *) refs="$refs -r:$f";; esac; done
dotnet "$CSC" -nologo -langversion:latest -define:IKMA_TEST -out:"$WORK/t.dll" $refs \
    "$HERE/NvdaStandInTest.cs" "$HERE/../../NvdaDirect.cs" >/dev/null
V=$(basename "$RT")
printf '{"runtimeOptions":{"tfm":"net%s","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' "${V%.*.*}.0" "$V" > "$WORK/t.runtimeconfig.json"
dotnet "$WORK/t.dll"
# run_nvda_standin.sh
