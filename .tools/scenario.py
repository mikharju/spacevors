#!/usr/bin/env python3
"""Aggregate the [PERF] rows of scenario runs into frames.jsonl + summary.json.

Iteration 1 is dropped (cold JIT) and so is the first frame of every remaining iteration:
the first frame of a process allocates every storage and shader, which is not steady state.
"""
import argparse
import json
import re
from pathlib import Path

import numpy as np

ROW = re.compile(r"\[PERF\] (.*)$")
FIELD = re.compile(r"(\w+)=(-?[\d.]+)")
BEGIN = re.compile(r"\[PERF-BEGIN\] scenario=(\S+) .*budgetFrameMs=([\d.]+)(?: expectedOverBudget=(\S+))?")
PASS = re.compile(r"\[RENDER-PASS\] ([\w.]+): ([\d.]+)ms")

# Which count divides which pass mean to give the cost of one drawn shape. This is the render metric
# that transfers between machines: absolute pass ms are not comparable across a real GPU and a
# software rasterizer, but "one spark costs 0.1us instead of 1.1us" is, and it is the signature of
# draw-call batching being lost.
SHAPE_PASSES = {"world.sparks": "sparks", "world.ammo": "ammo", "world.explosions": "explosions"}

# Gate factors. Deliberately wider than run-to-run noise and narrower than a real regression: the
# frame numbers tolerate 15-25% because a laptop is not a desktop, while a per-shape cost doubling
# means a structural change, not noise.
GATE_MEDIAN_FACTOR = 1.15
GATE_P99_FACTOR = 1.25
GATE_SHAPE_FACTOR = 2.0
SECONDS = ["dt", "sim", "apply", "render", "other"]
COUNTS = ["ents", "ammo", "sparks", "explosions", "beams", "mines", "ships", "asteroids", "pickups"]
GC = ["gen0", "gen1", "gen2"]


def read_frames(run: Path, logs: int) -> tuple[list[dict], float, bool]:
    frames, budget, expected_over_budget = [], 0.0, False
    for index in range(2, logs + 1):  # iteration 1 is the discarded cold run
        text = (run / f"iter-{index}.log").read_text(errors="replace")
        match = BEGIN.search(text)
        if match:
            budget = float(match.group(2))
            expected_over_budget = (match.group(3) or "False") == "True"
        rows = [dict(FIELD.findall(line)) for line in text.splitlines() if ROW.match(line)][1:]
        afterShot = False
        for row in rows:
            frame = {k: float(v) for k, v in row.items()}
            frame["afterShot"] = afterShot
            afterShot = frame.get("shot", 0.0) > 0
            frames.append(frame)
    return frames, budget, expected_over_budget


def read_passes(run: Path, logs: int) -> list[dict]:
    """Mean ms per render pass, worst first. Each pass is emitted once per frame, so its mean is its
    per-frame contribution and the shares show where the single [RENDER] total actually goes."""
    samples: dict[str, list[float]] = {}
    for index in range(2, logs + 1):  # iteration 1 is the discarded cold run
        for name, ms in PASS.findall((run / f"iter-{index}.log").read_text(errors="replace")):
            samples.setdefault(name, []).append(float(ms))

    means = {name: float(np.mean(values)) for name, values in samples.items()}
    total = sum(means.values()) or 1.0
    return [{"pass": name, "meanMs": round(ms, 3), "shareOfRender": round(ms / total, 4)}
            for name, ms in sorted(means.items(), key=lambda item: -item[1])]


def gc_summary(frames: list[dict]) -> dict:
    """Collection counts per frame plus the frame-time difference between frames that collected and
    frames that did not - the direct test of whether GC explains the sim p99 spikes."""
    if not frames or "gc0" not in frames[0]:
        return {}

    def collected(frame: dict) -> int:
        return int(frame.get("gc0", 0) + frame.get("gc1", 0) + frame.get("gc2", 0))

    with_gc = [f["dt"] for f in frames if collected(f) > 0]
    without_gc = [f["dt"] for f in frames if collected(f) == 0]
    alloc = [f.get("allocKb", 0.0) for f in frames]

    return {
        "collections": {label: int(sum(f.get(key, 0) for f in frames))
                        for label, key in [("gen0", "gc0"), ("gen1", "gc1"), ("gen2", "gc2")]},
        "framesWithGc": len(with_gc),
        "allocKbPerFrame": {"mean": round(float(np.mean(alloc)), 1),
                            "p99": round(float(np.percentile(alloc, 99)), 1),
                            "max": round(float(np.max(alloc)), 1)},
        "frameMsMedian": {
            "withGc": round(float(np.median(with_gc)), 3) if with_gc else None,
            "withoutGc": round(float(np.median(without_gc)), 3) if without_gc else None,
        },
    }


def append_index(index: Path, run: Path, summary: dict, host: str) -> None:
    """One TSV row per run. Logs are disposable and timestamped, so without an index the only way to
    find a past run is shell history; an index makes "read the scenario logs" enough for a reader who
    was not present when they were produced."""
    if not index.exists():
        index.write_text("scenario\thost\tframes\tmedianMs\tp99Ms\tsimShare\tbudget\trunDir\n")

    frame = summary["frameMs"]
    row = [summary["scenario"], host, str(summary["frames"]),
           f"{frame['median']:.2f}", f"{frame['p99']:.2f}", f"{summary['fraction']['sim']:.3f}",
           summary["budget"]["verdict"], str(run.resolve())]

    with index.open("a") as handle:
        handle.write("\t".join(row) + "\n")


def stats(values: list[float]) -> dict:
    a = np.array(values)
    return {
        "mean": round(float(a.mean()), 3),
        "median": round(float(np.percentile(a, 50)), 3),
        "p90": round(float(np.percentile(a, 90)), 3),
        "p99": round(float(np.percentile(a, 99)), 3),
        "max": round(float(a.max()), 3),
    }


def summarize(frames: list[dict], name: str, iterations: int, budget: float, passes: list[dict],
              host: str, expected_over_budget: bool) -> dict:
    ms = {key: [f[key] for f in frames] for key in SECONDS}
    fps = [1000.0 / f["dt"] for f in frames]
    entities = [f["ents"] for f in frames]
    total = sum(sum(ms[key]) for key in ["sim", "render", "other"]) or 1.0
    median_ms = float(np.percentile(ms["dt"], 50))

    # A scenario can be over budget knowingly (impact-storm) or because something regressed. The
    # harness has to tell those apart or the honest worst case makes every run look like a failure.
    over_budget = median_ms > budget
    verdict = "pass" if not over_budget else ("expected-over-budget" if expected_over_budget else "regression")

    return {
        "scenario": name,
        "host": host,
        "iterations": iterations,
        "frames": len(frames),
        "discarded": {"iterations": 1, "firstFramePerIteration": True},
        "frameMs": stats(ms["dt"]),
        "fps": {
            "mean": round(float(np.mean(fps)), 1),
            "median": round(float(np.percentile(fps, 50)), 1),
            "low1": round(float(np.percentile(fps, 1)), 1),
            "low10": round(float(np.percentile(fps, 10)), 1),
        },
        "fraction": {key: round(sum(ms[key]) / total, 4) for key in ["sim", "render", "other"]},
        "simMs": stats(ms["sim"]),
        "applyMs": stats(ms["apply"]),
        "renderMs": stats(ms["render"]),
        "renderPasses": passes[:12],
        "perShapeNs": shape_costs(passes, frames),
        "gc": gc_summary(frames),
        "perEntityNs": {
            "sim": round(1e6 * np.mean(ms["sim"]) / np.mean(entities), 1),
            "render": round(1e6 * np.mean(ms["render"]) / np.mean(entities), 1),
        },
        "entities": {"start": int(entities[0]), "end": int(entities[-1]),
                     "mean": round(float(np.mean(entities)), 1)},
        "counts": {key: {"start": int(frames[0][key]), "end": int(frames[-1][key]),
                         "mean": round(float(np.mean([f[key] for f in frames])), 1)}
                   for key in COUNTS},
        "budget": {"frameMs": round(budget, 2), "pass": not over_budget, "verdict": verdict,
                   "expectedOverBudget": expected_over_budget, "medianMs": round(median_ms, 2),
                   "framesOverBudget": int(np.count_nonzero(np.array(ms["dt"]) > budget))},
    }


def shape_costs(passes: list[dict], frames: list[dict]) -> dict:
    """Nanoseconds per drawn shape for the particle passes: mean pass ms divided by the mean entity
    count it drew. Comparable across machines, unlike absolute ms."""
    means = {row["pass"]: row["meanMs"] for row in passes}
    costs = {}

    for name, count_key in SHAPE_PASSES.items():
        count = float(np.mean([frame[count_key] for frame in frames])) if frames else 0.0
        if name in means and count > 0:
            costs[name] = round(1e6 * means[name] / count, 1)

    return costs


def check_baseline(baseline: dict, current: dict) -> bool:
    """Gate a run against its committed baseline. Absolute ms are only comparable between runs on the
    same host, so a sim-host run against a window baseline is reported as informational and not
    failed; the deterministic entity counts are compared either way."""
    same_host = baseline.get("host", "window") == current.get("host", "window")
    checks = [("frameMs.median", baseline["frameMs"]["median"], current["frameMs"]["median"], GATE_MEDIAN_FACTOR),
              ("frameMs.p99", baseline["frameMs"]["p99"], current["frameMs"]["p99"], GATE_P99_FACTOR)]

    for name, cost in current.get("perShapeNs", {}).items():
        base = baseline.get("perShapeNs", {}).get(name)
        if base:
            checks.append((f"perShapeNs.{name}", base, cost, GATE_SHAPE_FACTOR))

    print(f"gate vs baseline ({'same host' if same_host else 'host differs: ms informational only'})")
    print(f"{'metric':<26} {'baseline':>10} {'current':>10} {'factor':>8} {'limit':>7}  verdict")

    failures = []
    for metric, base, now, limit in checks:
        factor = now / base if base else float("inf")
        bad = same_host and factor > limit
        if bad:
            failures.append(metric)
        print(f"{metric:<26} {base:>10.2f} {now:>10.2f} {factor:>8.2f} {limit:>7.2f}  "
              f"{'FAIL' if bad else 'ok'}")

    entities = f"entities baseline {baseline['entities']['mean']} vs {current['entities']['mean']}"
    print(entities + ("  (deterministic seed: a difference means the world changed, not the speed)"
                      if abs(baseline["entities"]["mean"] - current["entities"]["mean"]) > 0.02 * baseline["entities"]["mean"]
                      else ""))

    if failures:
        print(f"GATE FAIL: {', '.join(failures)} exceeded the baseline by more than its limit")
        return False
    return True


def compare(baseline: dict, current: dict) -> None:
    print(f"{'metric':<22} {'baseline':>10} {'current':>10} {'delta':>9}")
    for path in [("frameMs", "median"), ("frameMs", "p99"), ("fps", "low1"),
                 ("fraction", "sim"), ("fraction", "render"), ("perEntityNs", "sim")]:
        section, key = path
        old, new = baseline[section][key], current[section][key]
        arrow = "+" if new > old else ""
        print(f"{section}.{key:<12} {old:>10} {new:>10} {arrow}{round(new - old, 3):>8}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--run", type=Path)
    parser.add_argument("--name", default="")
    parser.add_argument("--iters", type=int, default=5)
    parser.add_argument("--logs", type=int, default=6)
    parser.add_argument("--compare", nargs=2, type=Path, metavar=("BASELINE", "CURRENT"))
    parser.add_argument("--check-baseline", nargs=2, type=Path, metavar=("BASELINE", "CURRENT"))
    parser.add_argument("--index", type=Path)
    parser.add_argument("--host", default="window")
    args = parser.parse_args()

    if args.compare:
        compare(json.loads(args.compare[0].read_text()), json.loads(args.compare[1].read_text()))
        return

    if args.check_baseline:
        if not check_baseline(json.loads(args.check_baseline[0].read_text()),
                              json.loads(args.check_baseline[1].read_text())):
            raise SystemExit(1)
        return

    frames, budget, expected_over_budget = read_frames(args.run, args.logs)
    if not frames:
        raise SystemExit("no [PERF] frames found")

    with (args.run / "frames.jsonl").open("w") as out:
        for frame in frames:
            out.write(json.dumps(frame) + "\n")

    # Shot frames and the frame that pays for their PNG encode are kept in frames.jsonl for
    # inspection but excluded from the statistics, so a 100ms screenshot cannot become a p99.
    frames = [f for f in frames if not f.get("shot") and not f.get("afterShot")]

    summary = summarize(frames, args.name, args.iters, budget, read_passes(args.run, args.logs),
                        args.host, expected_over_budget)
    (args.run / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    if args.index:
        append_index(args.index, args.run, summary, args.host)

    print(json.dumps({k: summary[k] for k in ["frameMs", "fps", "fraction", "perEntityNs", "budget"]}, indent=2))

    if summary["gc"]:
        print("gc:", json.dumps(summary["gc"]))

    if summary["renderPasses"]:
        print(f"{'pass':<24} {'meanMs':>8} {'share':>7}")
        for row in summary["renderPasses"]:
            print(f"{row['pass']:<24} {row['meanMs']:>8} {row['shareOfRender'] * 100:>6.1f}%")

    budget_info = summary["budget"]
    print(f"budget: {budget_info['verdict']}  median {budget_info['medianMs']}ms against "
          f"{budget_info['frameMs']}ms, {budget_info['framesOverBudget']} of {summary['frames']} frames over")

    if summary["perShapeNs"]:
        print("per-shape cost: " + "  ".join(f"{name.split('.')[1]} {ns}ns"
                                             for name, ns in summary["perShapeNs"].items()))

    # Non-zero only for an unmarked budget breach: the exit code is what lets a wrapper, a hook or a
    # later agent notice a regression without reading tables.
    if budget_info["verdict"] == "regression":
        raise SystemExit(1)


if __name__ == "__main__":
    main()
