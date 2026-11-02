#!/bin/bash
# A/B the working tree (B) against HEAD (A) for lit-sprite render cost and pixels.
#
#   .tools/ab-lit.sh [scenario ...]        default: flat-200 lit-200-L0 lit-200-L16
#   AB_REPS=4        timed reps per side
#   AB_PIXELS=same   (default) fail if the probe screenshot moved; set it to "different" for a change
#                    that is meant to change how the game looks, where the diff only reports how far.
#
# The tree must be dirty. A build failure fails that side instead of carrying on: a script that greps
# build output away goes on to run the *previous* side's dll and reports those numbers as the new side
# (TROUBLE_SHOOTING.md). The stash is always popped, so a failed A side cannot strand the change.
#
# Wall time is not the signal here - another tenant on the host can double it while the drawn work is
# byte-identical - so each side takes the *minimum* per-frame CPU time over its reps, with llvmpipe
# pinned to a fixed thread count: interference only ever adds time, so the minimum is the closest thing
# to the machine's real cost. `flat-200` draws the same asteroid base textures without the lit shader, so
# it is a control for *shader* changes only: a change to the textures themselves (size, packing) moves it
# legitimately, and a large move there is not drift.
_TOOLS="$(cd "$(dirname "$0")" && pwd)"
repo="$_TOOLS/.."
cd "$repo" || exit 1
. "$_TOOLS/env.sh"
export DOTNET_CLI_HOME=/tmp/dotnet-home HOME=/tmp/dotnet-home
export LP_NUM_THREADS="${LP_NUM_THREADS:-4}"

proj="src/RenderBench/RenderBench.csproj"
dll="src/RenderBench/bin/Release/net10.0/RenderBench.dll"
scen="${*:-flat-200 lit-200-L0 lit-200-L16}"
reps="${AB_REPS:-4}"
shots="$_TOOLS/run"
log="$shots/ab-lit-cpu.tsv"
tmp="$shots/ab-lit-rep.txt"
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

# Returns non-zero rather than exiting, so the caller can still pop the stash.
build() {
    dotnet build "$proj" -c Release -v q --nologo && return 0
    echo "BUILD FAILED - not measuring a stale dll" >&2
    return 1
}

side=""
measure() {
    dotnet "$dll" $scen probe 2>/dev/null > "$tmp" || { echo "$side run failed" >&2; return 1; }
    awk -v side="$side" '/ms wall/ { print side, $1, $5 }' "$tmp" >> "$log"
    grep "^probe:" "$tmp" | tail -1
    cp /tmp/probe_current.png "$1" || { echo "probe wrote no screenshot" >&2; return 1; }
}

side=B
echo "== B (working tree)"
build || exit 1
for _ in $(seq "$reps"); do measure "$shots/probe-B.png" || exit 1; done

side=A
echo "== A (HEAD)"
git stash push -q || { echo "stash failed" >&2; exit 1; }
build && for _ in $(seq "$reps"); do measure "$shots/probe-A.png" || break; done
sideA=$?
git stash pop -q || { echo "FATAL: stash pop failed - the change lives in 'git stash list'" >&2; exit 1; }
[ "$sideA" = 0 ] || { echo "A side failed; the B numbers above stand alone" >&2; exit 1; }
build || exit 1

echo "== cpu ms per frame, min of $reps reps (B = working tree, A = HEAD)"
awk '{ key = $1 SUBSEP $2
       if (!(key in m) || $3 + 0 < m[key]) m[key] = $3 + 0
       if (!($2 in seen)) { seen[$2] = 1; order[++n] = $2 } }
     END {
       for (i = 1; i <= n; i++) {
           a = m["A" SUBSEP order[i]]; b = m["B" SUBSEP order[i]]
           if (a > 0 && b > 0) printf "  %-14s A %8.2f  B %8.2f  %+6.1f%%\n", order[i], a, b, (b - a) / a * 100
       }
     }' "$log"

# AB_PIXELS=same (default) for a change that must not move the image; AB_PIXELS=different for a look
# change, where the diff is a report of how far the image moved.
echo "== pixels A vs B"
bash "$_TOOLS/diff.sh" "$shots/probe-A.png" "$shots/probe-B.png" "--expect-${AB_PIXELS:-same}"
