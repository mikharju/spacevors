#!/bin/bash
# A/B the working tree against HEAD for lit-sprite render cost and pixels.
#
#   .tools/ab-lit.sh [scenario ...]        default: flat-200 lit-200-L0 lit-200-L16
#
# B = working tree, A = HEAD (the working tree is stashed while A builds), two timed reps each,
# then the probe screenshot of both sides is pixel-compared. The tree must be dirty, and a build
# failure fails its side instead of carrying on: a script that greps build output away goes on to
# run the *previous* side's dll and reports those numbers as the new side (TROUBLE_SHOOTING.md).
# The stash is always popped, so a failed A side cannot strand the change in the stash.
#
# Wall time is not the signal here - another tenant on the host can double it while the drawn work
# is byte-identical - so the comparison is the bench's per-frame CPU time, with llvmpipe pinned to
# a fixed thread count.
_TOOLS="$(cd "$(dirname "$0")" && pwd)"
repo="$_TOOLS/.."
cd "$repo" || exit 1
. "$_TOOLS/env.sh"
export DOTNET_CLI_HOME=/tmp/dotnet-home HOME=/tmp/dotnet-home
export LP_NUM_THREADS="${LP_NUM_THREADS:-4}"

proj="src/RenderBench/RenderBench.csproj"
dll="src/RenderBench/bin/Release/net10.0/RenderBench.dll"
scen="${*:-flat-200 lit-200-L0 lit-200-L16}"
shots="$_TOOLS/run"
mkdir -p "$shots"

if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
    echo "no display: start .tools/xvfb.sh as a background job first" >&2
    exit 1
fi
if [ "$(git status --porcelain | wc -l)" = 0 ]; then
    echo "working tree is clean: A and B would be the same build" >&2
    exit 1
fi

# Returns non-zero rather than exiting, so the caller can still pop the stash.
build() {
    dotnet build "$proj" -c Release -v q --nologo && return 0
    echo "BUILD FAILED - not measuring a stale dll" >&2
    return 1
}

measure() {
    dotnet "$dll" $scen probe 2>/dev/null | grep -E "ms wall|probe:"
    cp /tmp/probe_current.png "$1" || { echo "probe wrote no screenshot" >&2; return 1; }
}

echo "== B (working tree)"
build || exit 1
measure "$shots/probe-B.png" || exit 1
measure "$shots/probe-B2.png" || exit 1

echo "== A (HEAD)"
git stash push -q || { echo "stash failed" >&2; exit 1; }
build && measure "$shots/probe-A.png" && measure "$shots/probe-A2.png"
sideA=$?
git stash pop -q || { echo "FATAL: stash pop failed - the change lives in 'git stash list'" >&2; exit 1; }
[ "$sideA" = 0 ] || { echo "A side failed; the B numbers above stand alone" >&2; exit 1; }
build || exit 1

echo "== pixels A vs B (a packing or shader change must not move the image)"
bash "$_TOOLS/diff.sh" "$shots/probe-A.png" "$shots/probe-B.png" --expect-same
