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
BEGIN = re.compile(r"\[PERF-BEGIN\] scenario=(\S+) .*budgetFrameMs=([\d.]+)")
SECONDS = ["dt", "sim", "apply", "render", "other"]
COUNTS = ["ents", "ammo", "sparks", "explosions", "beams", "mines", "ships", "asteroids", "pickups"]


def read_frames(run: Path, logs: int) -> tuple[list[dict], float]:
    frames, budget = [], 0.0
    for index in range(2, logs + 1):  # iteration 1 is the discarded cold run
        text = (run / f"iter-{index}.log").read_text(errors="replace")
        match = BEGIN.search(text)
        if match:
            budget = float(match.group(2))
        rows = [dict(FIELD.findall(line)) for line in text.splitlines() if ROW.match(line)]
        frames += [{k: float(v) for k, v in row.items()} for row in rows[1:]]
    return frames, budget


def stats(values: list[float]) -> dict:
    a = np.array(values)
    return {
        "mean": round(float(a.mean()), 3),
        "median": round(float(np.percentile(a, 50)), 3),
        "p90": round(float(np.percentile(a, 90)), 3),
        "p99": round(float(np.percentile(a, 99)), 3),
        "max": round(float(a.max()), 3),
    }


def summarize(frames: list[dict], name: str, iterations: int, budget: float) -> dict:
    ms = {key: [f[key] for f in frames] for key in SECONDS}
    fps = [1000.0 / f["dt"] for f in frames]
    entities = [f["ents"] for f in frames]
    total = sum(sum(ms[key]) for key in ["sim", "render", "other"]) or 1.0

    return {
        "scenario": name,
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
        "perEntityNs": {
            "sim": round(1e6 * np.mean(ms["sim"]) / np.mean(entities), 1),
            "render": round(1e6 * np.mean(ms["render"]) / np.mean(entities), 1),
        },
        "entities": {"start": int(entities[0]), "end": int(entities[-1]),
                     "mean": round(float(np.mean(entities)), 1)},
        "counts": {key: {"start": int(frames[0][key]), "end": int(frames[-1][key]),
                         "mean": round(float(np.mean([f[key] for f in frames])), 1)}
                   for key in COUNTS},
        "budget": {"frameMs": budget, "pass": bool(np.percentile(ms["dt"], 50) <= budget),
                   "framesOverBudget": int(np.count_nonzero(np.array(ms["dt"]) > budget))},
    }


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
    args = parser.parse_args()

    if args.compare:
        compare(json.loads(args.compare[0].read_text()), json.loads(args.compare[1].read_text()))
        return

    frames, budget = read_frames(args.run, args.logs)
    if not frames:
        raise SystemExit("no [PERF] frames found")

    with (args.run / "frames.jsonl").open("w") as out:
        for frame in frames:
            out.write(json.dumps(frame) + "\n")

    summary = summarize(frames, args.name, args.iters, budget)
    (args.run / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")

    print(json.dumps({k: summary[k] for k in ["frameMs", "fps", "fraction", "perEntityNs", "budget"]}, indent=2))


if __name__ == "__main__":
    main()
