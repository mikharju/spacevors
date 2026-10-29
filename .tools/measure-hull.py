#!/usr/bin/env python3
"""Print a hull sprite's silhouette in collision units, to author its capsules from.

    PYTHONPATH=.tools/pylibs python3 .tools/measure-hull.py assets/enemy-ships/interceptor-texture.png
    PYTHONPATH=.tools/pylibs python3 .tools/measure-hull.py assets/player-ships/scout-texture.png \
        --fit "0 -0.70 0 0.70 0.30 ; -0.70 0 0.70 0 0.30"

Units are multiples of the hull's collision radius R, in the sprite's own frame: u is across
(-1..1 is the texture width, which is drawn as the diameter), v is along it (positive toward the
sprite's bottom, scaled by the *width* because the renderer scales the whole texture by
diameter/texture-width — a tall texture therefore reaches past v=1 and past the collision circle).
A shape authored in those numbers is multiplied by the hull's Radius and rotated by its `Rotation`,
exactly as `HullShapes` stores them and as the diagnostics overlay draws them.

Why measure: the capsule layout is a *tuned* fit, not a tracing, and eyeballed coordinates get the
painted extent of thin features and the transparent margin inside the texture box wrong.

With `--fit` the capsules (u0 v0 u1 v1 thickness, separated by `;`) are scored against the paint:
how much of the paint the shape covers, how much of the shape sits on visible air, and how far the
shape reaches from the centre — which must stay within 1.0, since the broad phase indexes hulls by
their collision radius and a shape that grows past it stops being found by the queries that need it.
"""
import sys

import numpy as np
from PIL import Image

argv = sys.argv[1:]
path = argv[0]
fit = None
if "--fit" in argv:
    fit = argv[argv.index("--fit") + 1]

alpha = np.array(Image.open(path).convert("RGBA"))[:, :, 3]
height, width = alpha.shape
solid = alpha > 128
ys, xs = np.nonzero(solid)

per_px = 2 / width  # the drawn diameter is the texture width, on both axes
u = lambda x: (x - width / 2) * per_px
v = lambda y: (y - height / 2) * per_px

print(f"{path}  {width}x{height}  painted={solid.mean():.3f}  drawn height={2 * height / width:.3f} radii")
print(f"  extents  u [{u(xs.min()):+.3f},{u(xs.max()):+.3f}]  v [{v(ys.min()):+.3f},{v(ys.max()):+.3f}]")
print(f"  painted area in R^2: {solid.sum() * per_px * per_px:.2f}  (a collision circle would be 3.14)")
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

if fit is None:
    sys.exit(0)

capsules = []
for part in fit.split(";"):
    numbers = [float(x) for x in part.replace(",", " ").split()]
    if len(numbers) != 5:
        sys.exit(f"--fit: expected 5 numbers per capsule (u0 v0 u1 v1 thickness), got: {part!r}")
    capsules.append(numbers)

reach = max(np.hypot(x0, y0) + t for x0, y0, x1, y1, t in capsules)
reach = max(reach, max(np.hypot(x1, y1) + t for x0, y0, x1, y1, t in capsules))

# One sample per texture pixel, in shape units: the paint mask and the shape mask land on the same grid.
gy, gx = np.mgrid[0:height, 0:width].astype(np.float32)
px = (gx - width / 2) * per_px
py = (gy - height / 2) * per_px

shape = np.zeros((height, width), dtype=bool)
for x0, y0, x1, y1, t in capsules:
    ax, ay = x1 - x0, y1 - y0
    length_sq = ax * ax + ay * ay
    s = np.ones_like(px) if length_sq == 0 else np.clip(((px - x0) * ax + (py - y0) * ay) / length_sq, 0, 1)
    dx = px - (x0 + s * ax)
    dy = py - (y0 + s * ay)
    shape |= dx * dx + dy * dy <= t * t

cell = per_px * per_px
painted_area = solid.sum() * cell
shape_area = shape.sum() * cell
overlap = (shape & solid).sum() * cell
print(f"  shape: {len(capsules)} capsules, area {shape_area:.2f} R^2, reach {reach:.3f} radii")
print(f"    covers {100 * overlap / painted_area:.0f}% of the paint "
      f"({painted_area - overlap:.2f} R^2 of paint left uncovered)")
print(f"    sits on visible air for {shape_area - overlap:.2f} R^2, {100 * (shape_area - overlap) / shape_area:.0f}% of the shape")
print(f"    a collision circle would leave {3.14 - painted_area:.2f} R^2 of air and cover nothing beyond the paint")

print("  per slice, paint span vs shape span (gaps are the misses that look wrong on screen)")
for frac in [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9]:
    y = int(frac * (height - 1))
    paint = np.nonzero(solid[y])[0]
    row = np.nonzero(shape[y])[0]
    span = lambda idx: "—" if idx.size == 0 else f"[{u(idx[0]):+.2f},{u(idx[-1]):+.2f}]"
    print(f"   v={v(y):+.3f} : paint {span(paint):<18} shape {span(row)}")
