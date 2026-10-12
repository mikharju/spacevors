#!/usr/bin/env python3
"""Pixel-compare two screenshots taken with .tools/shot.sh.

    .tools/diff.sh a.png b.png [--expect-same|--expect-different]

Prints mean/max channel delta and the fraction of changed pixels, then exits
non-zero when the verdict does not match the expectation. "Changed" means a
channel differs by more than TOLERANCE; software rendering is deterministic,
so a real regression is always well above that.
"""

import sys
from pathlib import Path

import numpy as np
from PIL import Image

TOLERANCE = 6          # per-channel difference that counts as a change
CHANGED_PIXEL_LIMIT_SAME = 0.005   # <=0.5% of pixels may differ for "same"


def load(path: str) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.int16)


def main(argv: list[str]) -> int:
    expect = "different"
    paths = [a for a in argv if not a.startswith("--")]
    for a in argv:
        if a == "--expect-same":
            expect = "same"
        elif a == "--expect-different":
            expect = "different"
    if len(paths) != 2:
        print(__doc__)
        return 2

    a, b = load(paths[0]), load(paths[1])
    if a.shape != b.shape:
        print(f"{paths[0]} {a.shape[1]}x{a.shape[0]} != {paths[1]} {b.shape[1]}x{b.shape[0]}")
        return 1

    delta = np.abs(a - b)
    changed = float((delta.max(axis=2) > TOLERANCE).mean())
    print(
        f"{a.shape[1]}x{a.shape[0]}  mean={delta.mean():.3f}  max={delta.max()}  "
        f"changed={changed * 100:.3f}%  expect={expect}"
    )

    matches = changed <= CHANGED_PIXEL_LIMIT_SAME if expect == "same" else changed > CHANGED_PIXEL_LIMIT_SAME
    print("PASS" if matches else "FAIL")
    return 0 if matches else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
