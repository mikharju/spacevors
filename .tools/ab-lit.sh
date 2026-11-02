#!/bin/bash
# A/B the working tree (B) against HEAD (A) for lit-sprite render cost and pixels.
#
#   .tools/ab-lit.sh [scenario ...]   default: flat-200 lit-200-L0 lit-200-L4 lit-200-L16
#   AB_REPS=6        interleaved reps per side
#   AB_PIXELS=same   (default) fail if the probe screenshot moved; "different" for a change that is
#                    meant to change the image, where the diff only reports how far it moved.
#
# Both sides are built first and their RenderBench.dll + Game.dll snapshotted, then the reps *alternate*
# A, B, A, B, ... Sequential sides are unusable on this host: one paired run measured the untouched flat
# control at +41 % on one side. Interleaving charges the same host interference to both sides. Every rep
# prints the md5 of the Game.dll it is about to run, so a build that silently produced the wrong binary
# shows up instead of becoming a confident wrong answer (TROUBLE_SHOOTING.md).
#
# Per-frame CPU time, minimum over the reps: interference only adds time, so the minimum is the closest
# thing to what the machine actually costs. `flat-200` draws the same base textures without the lit
# shader, so it is a control for *shader* changes only - a change to the textures themselves legitimately
# moves it.
#
# A build failure fails the run instead of carrying on with the other side's dll, and the working tree is
# restored even when the A side fails to build.
_TOOLS="$(cd "$(dirname "$0")" && pwd)"
repo="$_TOOLS/.."
cd "$repo" || exit 1
. "$_TOOLS/env.sh"
export DOTNET_CLI_HOME=/tmp/dotnet-home HOME=/tmp/dotnet-home
export LP_NUM_THREADS="${LP_NUM_THREADS:-4}"

proj="src/RenderBench/RenderBench.csproj"
rundir="src/RenderBench/bin/Release/net10.0"
scen="${*:-flat-200 lit-200-L0 lit-200-L4 lit-200-L16}"
reps="${AB_REPS:-6}"
shots="$_TOOLS/run"
log="$shots/ab-lit-cpu.tsv"
tmp="$shots/ab-lit-rep.txt"
snapshots="$shots/ab-snapshots"
mkdir -p "$shots"
: > "$log"

if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
    echo "no display: start .tools/xvfb.sh as a background job first" >&2
    exit 1
fi
if [ "$(git status --porcelain | wc -l)" = 0 ]; then
    echo "working tree is clean: A and B would be the same build" >&2
    exit 1
fi

build() {
    dotnet build "$proj" -c Release -v q --nologo && return 0
    echo "BUILD FAILED - not measuring a stale dll" >&2
    return 1
}

# The run dir holds the shared assets and native libraries; only these two dlls differ between sides.
snapshot() {
    rm -rf "$snapshots/$1" && mkdir -p "$snapshots/$1" &&
        cp "$rundir/RenderBench.dll" "$rundir/Game.dll" "$snapshots/$1/"
}

use() {
    cp "$snapshots/$1/RenderBench.dll" "$snapshots/$1/Game.dll" "$rundir/" || return 1
    md5sum "$rundir/Game.dll" | cut -c1-8 | sed "s/^/  $1 Game.dll md5 /"
}

echo "== building B (working tree)"
build || exit 1
snapshot B || exit 1

echo "== building A (HEAD)"
git stash push -q || { echo "stash failed" >&2; exit 1; }
build && snapshot A
sideA=$?
git stash pop -q || { echo "FATAL: stash pop failed - the change lives in 'git stash list'" >&2; exit 1; }
[ "$sideA" = 0 ] || { echo "A side failed; nothing measured" >&2; exit 1; }

echo "== interleaved reps, $reps per side"
for _ in $(seq "$reps"); do
    for side in A B; do
        use "$side" || exit 1
        dotnet "$rundir/RenderBench.dll" $scen probe > "$tmp" 2>&1 ||
            { echo "$side run failed (see $tmp)" >&2; exit 1; }
        awk -v side="$side" '/ms wall/ { print side, $1, $5 }' "$tmp" >> "$log"
        grep "^probe:" "$tmp" | tail -1 | sed "s/^/  $side /"
        cp /tmp/probe_current.png "$shots/probe-$side.png" ||
            { echo "probe wrote no screenshot" >&2; exit 1; }
    done
done
use B || exit 1

echo "== cpu ms per frame, min of $reps interleaved reps (B = working tree, A = HEAD)"
awk '{ key = $1 SUBSEP $2
       if (!(key in m) || $3 + 0 < m[key]) m[key] = $3 + 0
       if (!($2 in seen)) { seen[$2] = 1; order[++n] = $2 } }
     END {
       for (i = 1; i <= n; i++) {
           a = m["A" SUBSEP order[i]]; b = m["B" SUBSEP order[i]]
           if (a > 0 && b > 0) printf "  %-14s A %8.2f  B %8.2f  %+6.1f%%\n", order[i], a, b, (b - a) / a * 100
       }
     }' "$log"

echo "== pixels A vs B"
bash "$_TOOLS/diff.sh" "$shots/probe-A.png" "$shots/probe-B.png" "--expect-${AB_PIXELS:-same}"
