# THRUSTER_FLAME_ANCHORS.md

Status: shipped (2026-11-02)

Anchor every thruster flame and its point light to the hull's actual surface instead of its bounding
radius, so a non-circular hull's fire comes out of the hull and not out of the air behind it.

## Problem (verified in code, confirmed by screenshot)

Non-circular hulls landed (plans/NON_CIRCLE_COLLISION.md), but the flame pass still anchors where
circular hulls ended: the base of every flame sits at exactly `Radius` from the hull centre.

- `ThrusterFlameRenderer.cs:10` — `ThrustBaseOffsetRatio = 1.0f`, applied as `pos + dir * radius * 1.0`
  for player main/side/reverse (75–87), enemy (101) and mine (52) flames.
- `LightGatherer.cs:103` — the thrust light sits at `pos + dir * radius`, the same anchor.
- `ThrusterFlameRenderer.cs:124-128` — the turn RCS pair sits at authored diagonal offsets
  (0.8 fore / 0.7 aft / 0.6 side), which are points *on or near* the bounding disc, not on a shaped hull.

For a hull whose paint stops short of the bounding radius in the thrust direction, the plume visibly
starts in empty space (user screenshot: Heavy thrusting, blue plume detached from the tail).

## Measured extents (`.tools/measure-hull.py` on the shipped textures, in R)

| Hull | paint aft | paint side | anchor today | shape support aft | shape support side |
|---|---|---|---|---|---|
| Scout | 1.02 | 1.00 | 1.00 | 1.00 | 1.00 |
| Fighter | 1.01 | 1.00 | 1.00 | 1.00 | 1.00 |
| Heavy | 0.86 | 1.00 | 1.00 | 1.00 | 1.00 |
| Shadow | 0.91 | 0.93 | 1.00 | 1.00 | 1.00 |
| Falcon | 0.83 | 0.63 | 1.00 | 0.87 | 0.71 |
| Eagle | 0.94 | 0.92 | 1.00 | 0.80 | 0.96 |
| Manta | 0.51 | 0.79 | 1.00 | 0.54 | 0.79 |
| Raptor | 0.75 | 0.62 | 1.00 | 0.71 | 0.68 |
| swarmer (enemy-1) | 1.21 | 1.00 | 1.00 | 1.00 (disc) | 1.00 |
| Interceptor | 0.94 | 1.00 | 1.00 | 0.96 | 1.00 |
| HeavyCannon | 0.89 | 0.91 | 1.00 | 0.88 | 0.90 |
| Streak | 0.80 | 0.49 | 1.00 | 0.80 | 0.60 |
| Carmine | 0.87 | 0.81 | 1.00 | 0.71 | 0.79 |

The worst floats: Manta aft 0.49 R (≈55 px at R 111), Streak side 0.51 R, Falcon/Raptor side 0.37 R,
Carmine aft 0.13 R. The four original hulls (Scout/Fighter/Heavy/Shadow) were fit to ~1.0 R along
their axes, which is why the old 1.0 anchor looked right until the new shapes arrived.

## Proposal: procedural anchor from the hull shape (no per-hull configuration)

The authored hull shapes already encode each hull's extent in every direction — the flame pass just
has to read it. The anchor is the **support distance** of the shape along the flame direction: how far
the shape reaches along that direction from centre.

    support(localDir) = max over capsules of
        max(dot(From, localDir), dot(To, localDir)) + HalfThickness

### Domain (`Domain/Stats`)

- `Capsule.SupportAlong(Vector2 localDir)` and `HullShape.SupportAlong(Vector2 localDir)`: a few dot
  products over ≤ 7 capsules, allocation-free, pure. This is the same frame the shapes are authored in
  (u across, v along, nose at −v), so the renderer rotates the world flame direction by −Rotation.Angle
  to get `localDir`, then anchors at `support * Radius`.
- Where the hurtbox reaches past the paint (Heavy aft 1.00 vs paint 0.86, Streak side 0.60 vs 0.49),
  one shared constant `FlameBaseInsetRatio` (~0.10) pulls every base slightly inside the surface.
  Flames draw *before* ships (WorldRenderer order), so an inset base hides under the hull; only the
  plume shows. One tuned constant, not eight hulls' worth of numbers.

### Renderer (`Game`)

- `ThrusterFlameRenderer`: replace `radius * ThrustBaseOffsetRatio` with
  `SurfaceReach(shape, worldDir, angle) = max(0, shape.SupportAlong(localDir) - FlameBaseInsetRatio) * radius`.
  - Player: `player.Shape ?? HullShape.Circle` (same fallback as `CollisionSystem.cs:889`).
  - Enemy: `HullShapes.ForEnemy(ship.GraphicsId)`; the main-flame loop also reads `Rotation` for the
    local frame (already available on these entities).
  - Mine: circle — support is 1.0 in every direction, behaviour unchanged.
  - Turn RCS pair: keep the authored mount *directions* (0.8 fore / 0.6 side, 0.7 aft / 0.6 side),
    normalize each, and place the mount at `mountDir * support(mountDir)` so the pair sits on the hull.
  - Delete `ThrustBaseOffsetRatio` entirely.
- `LightGatherer.AddThrustLight`: place the light at the same anchored point instead of `pos + dir * radius`,
  so a hull is lit where its fire actually is. Needs the shape passed in; mine path stays circle.

## Answer: procedural, not manual

No per-hull flame configuration is needed. The support function is exact for every authored shape,
handles arbitrary thrust directions (enemy accel points any way), stays correct if a shape is re-fitted,
and adds zero data. Manual per-hull mount tables would be a second source of truth that drifts from
the hurtbox table on every art change.

## Alternatives considered

- **Manual per-hull mount offsets** (aft/side scalar per hull, authored from the sprites): exact to the
  paint, but ~13 new numbers duplicating what `HullShapes`/`PlayerHullShapes` already carry, to be
  re-touched whenever art or shape changes. Rejected as duplicate data.
- **Shrink the global ratio** (e.g. 1.0 → 0.8): moves the anchor wrong for round hulls (swarmer's paint
  reaches 1.21 R; its flame would sink into the blob) and still floats 0.2–0.4 R off Manta/Streak. Rejected.
- **Runtime paint/pixel sampling**: reading texture alpha at draw time ties a per-frame pass to art
  pixels, contradicts the authored-table rule in `HullShapes` (game logic must not depend on art
  assets), and costs more. Rejected.
- **Offline paint-extent table** (measure-hull.py output committed per hull): the support numbers above
  are already within ~0.15 R of paint everywhere; the inset constant absorbs the rest. Not worth the data.

## Cost and gates

Flame and light passes are per visible hull; support is ≤ 7 capsules × 2 dot products × ≤ 3 anchors —
noise. The sim does not draw flames, so `--sim` runs must be **unchanged**; verify anyway:
`.tools/scenario.sh hull-stream --sim --iters 3` and `hull-crowding --sim --iters 3`, compare to
`.tools/run/index.tsv` (expect inside run-to-run spread). Visually verify with a render run plus the
hull-shape diagnostics overlay (cyan collider under the flames): flames should start under the paint
edge for Manta, Falcon, Raptor, Streak and the screenshot's Heavy.

## Tests

- `HullShapeSupportTest` (pure, deterministic): circle support = 1.0 in sampled directions; Heavy
  aft/side = 1.00; Manta aft = 0.54; Streak side = 0.60 (pins the table above); support ≥ any capsule
  endpoint projection (definition check); rotated-direction consistency for an axis-aligned shape.
- No renderer test exists for this pass; the diagnostics overlay and a scenario screenshot are the check.

## Out of scope (deliberate)

- ECM arcs (`EcmRenderer`, 1.15 R) and targeting brackets stay on the bounding radius — they read as
  rings around the hull's disc, and neither currently reads wrong.
- Paint beyond the bounding radius (fighter nose 1.10 R, swarmer 1.21 R) still cannot be covered by
  anything; flames anchoring under that paint stay hidden behind the hull, which is the right look.

## Shipped (2026-11-02):

- `Capsule.SupportAlong` / `HullShape.SupportAlong` in `Domain/Stats/HullShapes.cs`: the far endpoint's
  projection plus the round cap, per capsule, no allocation, pure.
- `ThrusterFlameRenderer.FlameBase` is now the one anchor for every base — player main/side/reverse,
  enemy (accel direction, arbitrary angle), mine (circle) and the RCS pair, whose authored mount
  *directions* survive while their length becomes the surface distance. One shared inset constant
  `FlameBaseInsetRatio = 0.10` (clamped at 0); `ThrustBaseOffsetRatio` deleted.
- `LightGatherer` places each thrust light at the same `FlameBase` point; its enemy query now also
  reads `Rotation` (every enemy the flame pass already lit has it).
- Deviations from the plan: the table above listed Eagle side support 0.96; measured from the authored
  capsules it is 0.97 — the swept leading-edge capsule reaches past the wing bar — and the test pins
  0.97. `FlameBase` lives on `ThrusterFlameRenderer` as the shared anchor the light gathers through
  (the plan left placement open).
- `HullShapeSupportTest` pins: the support definition, circle = R in 16 directions, aft/across support
  against the measured paint extents for all 5 enemy and all 8 player hulls, every shape ≤ 1.001 R in
  16 directions, and `FlameBase` anchoring + rotation (Manta 0.44 R, circle 0.9 R at any heading).
- Perf verdict (`--sim --iters 3`, medians vs the same day's rows): hull-stream **0.82 vs 0.82**,
  hull-crowding **0.27 vs 0.41** — flames and lights are render-only passes, the sim never runs them,
  so keep; the crowding delta is run-to-run noise. Both gates pass.
- Visual check under Xvfb: the Manta (R 111, the worst old float — 0.46 R of empty air behind the
  tail) thrusts with the plume starting at the engine-pod tails, the base hidden under the paint.
