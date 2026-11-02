#!/bin/bash
# One number for how far the project is from the 100k-object goal: the largest mixed world that still
# fits the frame budget. Design and the pinned entity mix: plans/CEILING_TEST.md.
#
#   .tools/ceiling.sh                        # sim host: headless, no GPU, a couple of seconds
#   .tools/ceiling.sh --window               # the real game window, renderer included (~10-20 s)
#   .tools/ceiling.sh --budget 16.67         # a 60 fps budget instead of 120
#   .tools/ceiling.sh --start 2000 --max 131072
#
# This is deliberately NOT a gate: no .tools/baselines file is written, .tools/scenario.sh never reads
# it, and the exit status is 0 unless the probe itself failed. Rows accumulate in
# .tools/run/ceiling.tsv (gitignored) so the number can be watched climbing.
#
# Comparable only between runs on the same host with nothing else loaded, so each row carries the date,
# the host tag and the mix version. In the sandbox, source .tools/env.sh before --window (that is the
# X11 + software Mesa setup; on a real desktop it points at a display that does not exist, which is why
# this script does not source it - same rule as .tools/scenario.sh).
_TOOLS="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$_TOOLS/.." && pwd)"
logs="$_TOOLS/run"
index="$logs/ceiling.tsv"

host=sim budget=8.33 start=2000 max=131072
while [ $# -gt 0 ]; do
    case "$1" in
        --window) host=window; shift ;;
        --sim) host=sim; shift ;;
        --budget) budget="$2"; shift 2 ;;
        --start) start="$2"; shift 2 ;;
        --max) max="$2"; shift 2 ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
done

if [ "$host" = window ]; then
    project="src/Game/Game.csproj"
    export SPACEVORS_CEILING="$budget" SPACEVORS_CEILING_START="$start" SPACEVORS_CEILING_MAX="$max"
    export SPACEVORS_DIAGNOSTIC=0
    unset SPACEVORS_SCENARIO SPACEVORS_SCENARIO_SHOTS SPACEVORS_SCENARIO_HOLD
else
    project="src/ScenarioBench/ScenarioBench.csproj"
fi

mkdir -p "$logs" || exit 1
run="$logs/ceiling-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$run" || exit 1

# The artifact the game ships: published ReadyToRun, as .tools/scenario.sh does, so both hosts price the
# same build rather than a plain one.
publish="dotnet publish $project --configuration Release --runtime linux-x64 --self-contained false"
(cd "$repo" && eval "$publish") >"$run/publish.log" 2>&1 || { echo "publish failed; see $run/publish.log" >&2; exit 1; }
app="$repo/${project%/*}/bin/Release/net10.0/linux-x64/publish"
dll="${project##*/}"
dll="${dll%.*}.dll"
[ -f "$app/$dll" ] || { echo "missing $app/$dll - run: (cd $repo && $publish)" >&2; exit 1; }

# raylib prints its whole startup banner at INFO level, which would bury the probe; the full output
# goes to the log, the screen gets the probe rows.
echo "ceiling: host=$host budgetMs=$budget start=$start max=$max mix=v1"
if [ "$host" = window ]; then
    dotnet "$app/$dll" 2>&1 | tee "$run/ceiling.log" | grep --line-buffered -E '^\[CEIL|^CEILING'
else
    dotnet "$app/$dll" ceiling --budget "$budget" --start "$start" --max "$max" 2>&1 \
        | tee "$run/ceiling.log" | grep --line-buffered -E '^\[CEIL|^CEILING'
fi
status=${PIPESTATUS[0]}

# One row per bracket: the full-budget ceiling, and the half-budget one that leaves room to render.
[ -f "$index" ] || printf 'date\thost\tmix\tbracket\tbudgetMs\tcount\tmedianMs\tents\tfailCount\thitMax\tseconds\tlog\n' >"$index"
seconds=$(sed -n 's/.*\[CEIL-END\].*seconds=\([0-9.]*\).*/\1/p' "$run/ceiling.log" | tail -1)
stamp=$(date -u +%Y-%m-%dT%H:%M:%SZ)

while read -r line; do
    field() { echo "$line" | sed -n "s/.*$1=\([^ ]*\).*/\1/p"; }
    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
        "$stamp" "$(field host)" "$(field mix)" "$(field bracket)" "$(field budgetMs)" \
        "$(field count)" "$(field medianMs)" "$(field ents)" "$(field failCount)" "$(field hitMax)" \
        "${seconds:-0}" "${run#"$repo"/}/ceiling.log" >>"$index"
done < <(grep '^CEILING ' "$run/ceiling.log")

echo "record:   $index"
echo "log:      $run/ceiling.log"
exit $status
