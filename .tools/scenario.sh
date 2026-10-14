#!/bin/bash
# Run a seeded perf scenario in a real window and turn its [PERF] rows into
# frames.jsonl + summary.json.
#
#   .tools/scenario.sh <name> [--sim] [--measure N] [--iters N] [--hold S] [--cap FPS]
#                           [--shots LIST] [--no-shots] [--out DIR] [--compare] [--check-baseline]
#                           [--update-baseline]
#
# Exits non-zero when the run breaches its frame budget without being marked expected-over-budget, or
# (with --check-baseline) regresses past the committed baseline's limits.
#
# The game writes PNGs of --shots frames (default start,mid,end) into the run dir itself, so a run
# records what it looked like without anyone pressing F12 into a window that may not be focused.
#
# Default host is the real game window on whatever display this shell has. --sim runs ScenarioBench
# instead: no window, no renderer, so the numbers are pure ECS cost.
#
# This script deliberately does NOT source .tools/env.sh: that is the sandbox-only X11 + software
# Mesa setup (DISPLAY=localhost:99, LIBGL_ALWAYS_SOFTWARE=1), and on a real desktop it points the
# game at a display that does not exist. In the sandbox, source it first:
#   .tools/xvfb.sh                      # as a background job
#   . .tools/env.sh && .tools/scenario.sh ammo-spray --iters 5
#
# Logs outlive the terminal. Each run lands in .tools/run/scenario-<name>-<utc>/ (iter-N.log with the
# raw [PERF]/[RENDER-PASS]/[SIM] rows, frames.jsonl, summary.json); `.tools/run/latest` points at the
# newest one and `.tools/run/index.tsv` lists every run kept, with its headline numbers, so a later
# reader can find a log without being told its path. Anything older than a day is cleared on the next
# run. The logs are gitignored; these scripts are not.
_TOOLS="$(cd "$(dirname "$0")" && pwd)"

name="${1:-}"; shift || true
measure=""; iters=5; hold=0; cap=0; out=""; compare=0; update_baseline=0; sim=0; gate=0; gate_status=0
shots="start,mid,end"
while [ $# -gt 0 ]; do
    case "$1" in
        --sim) sim=1; shift ;;
        --shots) shots="$2"; shift 2 ;;
        --no-shots) shots=""; shift ;;
        --measure) measure="$2"; shift 2 ;;
        --iters) iters="$2"; shift 2 ;;
        --hold) hold="$2"; shift 2 ;;
        --cap) cap="$2"; shift 2 ;;
        --out) out="$2"; shift 2 ;;
        --compare) compare=1; shift ;;
        --check-baseline) check_baseline=1; shift ;;
        --update-baseline) update_baseline=1; shift ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
done
[ -n "$name" ] || { echo "usage: $0 <scenario> [options]" >&2; exit 2; }

if [ "$sim" = 1 ]; then
    APP="${APP:-$_TOOLS/../src/ScenarioBench/bin/Release/net10.0}"
    [ -f "$APP/ScenarioBench.dll" ] || { echo "build first: dotnet build src/ScenarioBench/ScenarioBench.csproj -c Release" >&2; exit 1; }
else
    APP="${APP:-$_TOOLS/../src/Game/bin/Release/net10.0}"
    [ -f "$APP/Game.dll" ] || { echo "build first: dotnet build src/Game/Game.csproj -c Release" >&2; exit 1; }
    if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
        echo "no display set: this shell has neither DISPLAY nor WAYLAND_DISPLAY." >&2
        echo "use --sim for sim-only numbers, or in the sandbox: . .tools/env.sh && $0 ..." >&2
        exit 1
    fi
fi

host=$([ "$sim" = 1 ] && echo sim || echo window)

logs="$_TOOLS/run"
keep_minutes=1440
mkdir -p "$logs" || exit 1

# A day is long enough to come back to a run in the next session; past that the raw rows are just
# disk. Cleared before the new run so the index never lists a directory that is already gone.
stale=$(find "$logs" -mindepth 1 -maxdepth 1 -mmin +"$keep_minutes" -print -exec rm -rf {} + | wc -l)
[ "$stale" = 0 ] || echo "logs: cleared $stale item(s) older than $((keep_minutes / 60)) h"

run="${out:-$logs/scenario-$name-$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$run" || exit 1
run="$(cd "$run" && pwd)"
cd "$run" || exit 1

export SPACEVORS_DIAGNOSTIC=1 SPACEVORS_SCENARIO="$name"
export SPACEVORS_SCENARIO_HOLD="$hold" SPACEVORS_SCENARIO_CAP="$cap" SPACEVORS_SCENARIO_SHOTS="$shots"
if [ -n "$measure" ]; then export SPACEVORS_SCENARIO_TICKS="$measure"; fi

# One extra iteration beyond the pooled ones: the first run pays cold JIT and is discarded.
[ "$sim" = 1 ] || echo "display: ${DISPLAY:-$WAYLAND_DISPLAY}"
for i in $(seq 1 "$((iters + 1))"); do
    if [ "$sim" = 1 ]; then
        dotnet "$APP/ScenarioBench.dll" "$name" ${measure:-} > "iter-$i.log" 2>&1
    else
        dotnet "$APP/Game.dll" > "iter-$i.log" 2>&1
    fi
    status=$?
    if [ "$status" != 0 ]; then
        echo "iteration $i exited $status (raylib crashes rather than reporting a bad display). Last lines:" >&2
        tail -5 "iter-$i.log" >&2
        exit 1
    fi
    if ! grep -q '^\[PERF-BEGIN\]' "iter-$i.log"; then
        echo "iteration $i logged no [PERF-BEGIN]: unknown scenario '$name'? See $run/iter-$i.log" >&2
        exit 1
    fi
    echo "iteration $i: $(grep -c '^\[PERF\]' "iter-$i.log") frames"
done

export PYTHONPATH="$_TOOLS/pylibs${PYTHONPATH:+:}$PYTHONPATH"
# An unmarked budget breach exits 1; capture it rather than aborting, so the run's logs, shots and
# index row are still reported before the script returns the failure.
python3 "$_TOOLS/scenario.py" --run "$run" --name "$name" --iters "$iters" --logs "$((iters + 1))" \
    --index "$logs/index.tsv" --host "$host" || gate=1
[ -f "$run/summary.json" ] || { echo "no summary.json produced; baseline left untouched" >&2; exit 1; }

baseline="$_TOOLS/baselines/$name.json"
if [ "$update_baseline" = 1 ]; then
    mkdir -p "$(dirname "$baseline")" && cp "$run/summary.json" "$baseline"
    echo "baseline updated: $baseline"
fi
if [ "$compare" = 1 ]; then
    [ -f "$baseline" ] && python3 "$_TOOLS/scenario.py" --compare "$baseline" "$run/summary.json" \
        || echo "no baseline for $name yet ($baseline)" >&2
fi
if [ "$check_baseline" = 1 ]; then
    if [ -f "$baseline" ]; then
        python3 "$_TOOLS/scenario.py" --check-baseline "$baseline" "$run/summary.json" || gate=1
    else
        echo "no baseline for $name yet ($baseline): nothing to gate against" >&2
    fi
fi
ln -sfn "$run" "$logs/latest"
shots_written=$(find "$run" -maxdepth 1 -name 'scenario-*.png' | wc -l)
[ "$shots_written" = 0 ] || echo "shots:    $(cd "$run" && ls -1 scenario-*.png | tr '\n' ' ')"
echo "log dir:  $run"
echo "newest:   $logs/latest  (raw rows iter-N.log, per-frame frames.jsonl, headline summary.json)"
echo "index:    $logs/index.tsv"

# The one place that knows the run failed: every caller - a shell chain, a wrapper target, a hook, CI
# or a later agent - reads this instead of parsing the tables above.
if [ "$gate" = 1 ]; then
    echo "PERF COST: $name failed a perf gate (budget breach or baseline regression). Details above; data in $run" >&2
    exit 1
fi
