#!/bin/bash
# Run a seeded perf scenario in a real window and turn its [PERF] rows into
# frames.jsonl + summary.json.
#
#   .tools/scenario.sh <name> [--sim] [--measure N] [--iters N] [--hold S] [--cap FPS]
#                           [--out DIR] [--compare] [--update-baseline]
#
# Default host is the real game window. --sim runs ScenarioBench instead: no window, no renderer,
# so the numbers are pure ECS cost. Needs a display unless --sim. In the sandbox start the virtual
# one first, as a background job:  .tools/xvfb.sh
. "$(dirname "$0")/env.sh"

name="${1:-}"; shift || true
measure=""; iters=5; hold=0; cap=0; out=""; compare=0; update_baseline=0; sim=0
while [ $# -gt 0 ]; do
    case "$1" in
        --sim) sim=1; shift ;;
        --measure) measure="$2"; shift 2 ;;
        --iters) iters="$2"; shift 2 ;;
        --hold) hold="$2"; shift 2 ;;
        --cap) cap="$2"; shift 2 ;;
        --out) out="$2"; shift 2 ;;
        --compare) compare=1; shift ;;
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
fi

run="${out:-$_TOOLS/run/scenario-$name-$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$run" || exit 1
cd "$run" || exit 1

export SPACEVORS_DIAGNOSTIC=1 SPACEVORS_SCENARIO="$name"
export SPACEVORS_SCENARIO_HOLD="$hold" SPACEVORS_SCENARIO_CAP="$cap"
if [ -n "$measure" ]; then export SPACEVORS_SCENARIO_TICKS="$measure"; fi

# One extra iteration beyond the pooled ones: the first run pays cold JIT and is discarded.
for i in $(seq 1 "$((iters + 1))"); do
    if [ "$sim" = 1 ]; then
        dotnet "$APP/ScenarioBench.dll" "$name" ${measure:-} > "iter-$i.log" 2>&1
    else
        dotnet "$APP/Game.dll" > "iter-$i.log" 2>&1
    fi
    if ! grep -q '^\[PERF-BEGIN\]' "iter-$i.log"; then
        echo "iteration $i produced no [PERF-BEGIN]; unknown scenario or no display? See $run/iter-$i.log" >&2
        tail -5 "iter-$i.log" >&2
        exit 1
    fi
    echo "iteration $i: $(grep -c '^\[PERF\]' "iter-$i.log") frames"
done

export PYTHONPATH="$_TOOLS/pylibs${PYTHONPATH:+:}$PYTHONPATH"
python3 "$_TOOLS/scenario.py" --run "$run" --name "$name" --iters "$iters" --logs "$((iters + 1))"

baseline="$_TOOLS/baselines/$name.json"
if [ "$update_baseline" = 1 ]; then
    mkdir -p "$(dirname "$baseline")" && cp "$run/summary.json" "$baseline"
    echo "baseline updated: $baseline"
fi
if [ "$compare" = 1 ]; then
    [ -f "$baseline" ] && python3 "$_TOOLS/scenario.py" --compare "$baseline" "$run/summary.json" \
        || echo "no baseline for $name yet ($baseline)" >&2
fi
echo "run dir: $run"
