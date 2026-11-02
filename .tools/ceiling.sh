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
# it, and the exit status is 0 unless the probe itself failed.
#
# The record is .tools/ceiling-history.tsv - one row per bracket per run, and it is tracked, unlike
# .tools/run/ (which holds the per-probe logs). So the trend line is `git log -p -- .tools/ceiling-history.tsv`,
# and the run itself prints how it compares with the previous row of the same host and bracket. Direction:
# a bigger count is better, a smaller median is better. Commit the row together with the change that
# moved it and the delta explains itself.
#
# Comparable only between runs on the same machine with nothing else loaded, so every row carries the
# date, host tag, mix version and cpu; a row from a different cpu is printed without a percentage rather
# than as a trend. In the sandbox, source .tools/env.sh before --window (that is the X11 + software Mesa
# setup; on a real desktop it points at a display that does not exist, which is why this script does not
# source it - same rule as .tools/scenario.sh).
_TOOLS="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$_TOOLS/.." && pwd)"
logs="$_TOOLS/run"
history="$_TOOLS/ceiling-history.tsv"

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

# The machine identity that decides comparability. /proc/cpuinfo reports a masked model inside the
# sandbox, which is fine: a masked or missing tag simply never matches a real one.
cpu_tag() {
    local tag=""
    [ -r /proc/cpuinfo ] && tag=$(awk -F': ' '/model name/{print $2; exit}' /proc/cpuinfo)
    [ -n "$tag" ] || tag=$(sysctl -n machdep.cpu.brand_string 2>/dev/null || true)
    [ -n "$tag" ] || tag=$(uname -sm)
    printf '%s' "$tag"
}

# Percent change, with a guard for a previous value of zero or "na".
pct() { awk -v a="$1" -v b="$2" 'BEGIN { if (a+0 == 0) print "n/a"; else printf "%+.1f%%", (b-a)/a*100 }'; }

# How this run's row compares with the last row of the same host, mix, bracket and budget. A row from
# another cpu gets the old numbers without a percentage: two machines are not one trend line.
compare_row() {
    local host=$1 mix=$2 bracket=$3 budget=$4 count=$5 median=$6 previous
    previous=$(awk -F'\t' -v h="$host" -v m="$mix" -v b="$bracket" -v ms="$budget" '
        $2==h && $3==m && $4==b && $5==ms { c=$6; med=$7; cpu=$12 }
        END { if (c != "") printf "%s\t%s\t%s", c, med, (cpu == "" ? "unknown" : cpu) }' "$history")

    if [ -z "$previous" ]; then
        echo "compare:  $host/$bracket - no previous row, this is the first one on record"
        return
    fi

    local oldCount oldMedian oldCpu
    IFS=$'\t' read -r oldCount oldMedian oldCpu <<<"$previous"

    if [ "$oldCpu" != "$cpu" ]; then
        printf 'compare:  %s/%s - previous row (cpu "%s"): count=%s medianMs=%s; different cpu, no percentage\n' \
            "$host" "$bracket" "$oldCpu" "$oldCount" "$oldMedian"
        return
    fi

    printf 'compare:  %s/%s - count %s -> %s (%s), medianMs %s -> %s (%s)\n' \
        "$host" "$bracket" "$oldCount" "$count" "$(pct "$oldCount" "$count")" \
        "$oldMedian" "$median" "$(pct "$oldMedian" "$median")"
}

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
[ -f "$history" ] || printf 'date\thost\tmix\tbracket\tbudgetMs\tcount\tmedianMs\tents\tfailCount\thitMax\tseconds\tcpu\tlog\n' >"$history"
seconds=$(sed -n 's/.*\[CEIL-END\].*seconds=\([0-9.]*\).*/\1/p' "$run/ceiling.log" | tail -1)
stamp=$(date -u +%Y-%m-%dT%H:%M:%SZ)
cpu=$(cpu_tag)

while read -r line; do
    field() { echo "$line" | sed -n "s/.*$1=\([^ ]*\).*/\1/p"; }
    compare_row "$(field host)" "$(field mix)" "$(field bracket)" "$(field budgetMs)" "$(field count)" "$(field medianMs)"
    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
        "$stamp" "$(field host)" "$(field mix)" "$(field bracket)" "$(field budgetMs)" \
        "$(field count)" "$(field medianMs)" "$(field ents)" "$(field failCount)" "$(field hitMax)" \
        "${seconds:-0}" "$cpu" "${run#"$repo"/}/ceiling.log" >>"$history"
done < <(grep '^CEILING ' "$run/ceiling.log")

echo "record:   $history"
echo "log:      $run/ceiling.log"
exit $status
