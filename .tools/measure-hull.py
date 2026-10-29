#!/usr/bin/env python3
"""Print a hull sprite's silhouette in collision units, to author its capsules from.

    PYTHONPATH=.tools/pylibs python3 .tools/measure-hull.py assets/enemy-ships/interceptor-texture.png

Units are multiples of the hull's collision radius R, in the sprite's own frame: u is across
(-1..1 is the texture width, which is drawn as the diameter), v is along it (positive toward the
sprite's bottom). A shape authored in those numbers is multiplied by the hull's Radius and rotated
by its `Rotation`, exactly as `HullShapes` stores them and as the diagnostics overlay draws them.

Why measure: the capsule layout is a *tuned* fit, not a tracing, and eyeballed coordinates get the
painted extent of thin features and the transparent margin inside the texture box wrong.
"""
import sys

import numpy as np
from PIL import Image

path = sys.argv[1]
alpha = np.array(Image.open(path).convert("RGBA"))[:, :, 3]
height, width = alpha.shape
solid = alpha > 128
ys, xs = np.nonzero(solid)

u = lambda x: (x - width / 2) * 2 / width
v = lambda y: (y - height / 2) * 2 / height

print(f"{path}  {width}x{height}  painted={solid.mean():.3f}")
print(f"  extents  u [{u(xs.min()):+.3f},{u(xs.max()):+.3f}]  v [{v(ys.min()):+.3f},{v(ys.max()):+.3f}]")
print(f"  painted area in R^2: {solid.sum() * 4 / width / width:.2f}  (a collision circle would be 3.14)")
print("  every row is one slice of the hull, nose at v<0; runs are the painted spans across it")
for frac in [0.02, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.98]:
    y = int(frac * (height - 1))
    idx = np.nonzero(solid[y])[0]
    if idx.size == 0:
        print(f"   v={v(y):+.3f} : (empty)")
        continue
    groups = np.split(idx, np.nonzero(np.diff(idx) > 2)[0] + 1)
    runs = ", ".join(f"[{u(g[0]):+.2f},{u(g[-1]):+.2f}]" for g in groups)
    print(f"   v={v(y):+.3f} : {runs}")
