# NON_CIRCLE_COLLISION.md

Status: shipped (verified 2026-10-31)

Proposal: replace the circle-vs-circle narrow phase with shape-aware collision so hulls can
collide as hulls and not as discs (2026-10-24).

## Status: plan only — no code changed

No implementation, no tests, no measurement runs. Every cost below is an **estimate** meant to
choose an option, not to accept one; the acceptance rule is the harness measurement in *Cost and gates*.

Note: web search was unavailable this session (`DEEPSEEK_API_KEY` missing), so nothing here is
cited from outside the repo. The options are standard ones; the numbers come from this codebase
and `.tools/run/index.tsv`.

## What was asked

1. How to change circle↔circle into something that allows different shapes.
2. Alternatives, with performance considered.
3. Would pixel-to-pixel collision be workable or useful?
4. What simplifications keep it simple and fast while still allowing shapes more interesting
   than a circle or a square?

## Current state (verified in code)

Circle math is inlined in `src/Domain/Combat/CollisionSystem.cs`, in eight places:

| Where | Lines |
|---|---|
| `CheckPlayerCollision` (asteroid/mine/ship vs player) | 61–73, called at 83, 92, 102 |
| asteroid↔asteroid, asteroid↔mine | 134–163 |
| ship↔ship, ship↔mine | 169–198 |
| swept ammo vs grid candidates (segment vs centre, `rSum`) | 239–300 |
| laser beam first hit | 425–443 |
| enemy ammo vs player | 359–365 |
| `ResolveAmmoVsAsteroid` | 969–1029 |
| `ResolveMineVsPlayer` | 1031–1063 |

`ResolveCollision` (785) is the one resolution path for bodies and it **derives its whole
geometry from centres**: `normal = (b−a)/dist`, `penetration = rSum − dist`, contact point
`aPos + normal·aRadius` (921), friction lever arms `rA = normal·aRadius`, mass `π·r²`, MOI
`0.5·m·r²`.

Broad phase: `src/Domain/SpatialGrid.cs`, 128 px cells, insert and query both keyed on the
radius AABB, `SpatialItem` carries `Radius`.

`Radius` is load-bearing well beyond collision: draw diameter (`Radius · 2`), off-screen
culling (`RenderHelpers.HalfDiagonal`), diagnostics circles, explosion sizes, pickup radii,
spawn placement, the targeting/threat ring (`FiringRange + radius`), and impact-damage mass.

Measured today: `ammo-spray` sim median 1.29–1.34 ms, `impact-storm` 2.07–2.20 ms at ~5.5k
bullets; `CollisionSystem` is ~76% of sim in `ammo-spray` and `Collision: detection` owns the
slow-frame tail (PLAN.md, *Sim attribution*).

## The real constraint is not detection, it is resolution

Finding an overlap is cheap in any scheme. What the resolution code consumes — and gets for free
from circles — is four quantities:

- **contact normal** → impulse direction
- **penetration depth** → position correction (`Slop`, `CorrectionPercent`)
- **contact point** → impact sparks, death explosion placement, hit point in `_shipDeathData`
- **lever arm** → friction torque and angular velocity

Any shape scheme that cannot hand back `(normal, depth, contact)` cheaply will force a much bigger
rewrite than the shape math itself. So the first real work is a seam, not a polygon:

```
NarrowPhase.TryCollide(a, b, out normal, out depth, out contact) → bool
```

with circle↔circle as its first and only implementation. Everything else is additive.

## Sizes of the two hot paths

- Tick is fixed `1/120 s`. Ammo speed 400–4200 px/s → **3.3–35 px of travel per tick**; ammo
  radius 1.5–6 px; hulls 20–84 px radius.
- **Ammo pass** (dominant): ~5.5k bullets × 1–3 grid candidates ≈ 5–15k narrow-phase tests per tick.
- **Hull↔hull pass**: ≤100 live enemy ships plus rocks → tens to low thousands of tests per tick,
  most killed by the bounding-circle reject.
- Goal is 10k objects now, 100k later → the shape path must allocate nothing per entity.

## Alternatives

| # | Option | New math | Est. narrow-phase cost (circle = 1) | What it buys | Verdict |
|---|---|---|---|---|---|
| A | **Multi-circle composite**: hull = 2–5 circles at local offsets | none | 2–5× the sub-pair, same unit cost | elongated / bulging silhouettes; normal still centre-to-centre per sub-pair, so `ResolveCollision` barely changes | cheapest real step; safe first cut; no corners |
| B | **Round-primitive composite**: capsules (segment + radius), i.e. A plus limbs | ~30 lines of closest-point helpers | ~2–4× per sub-pair | true limbs: wings, spine, nose; long thin hulls; normals/depth/contact analytic; swept bullet becomes segment↔segment | **recommended** |
| C | **Convex polygon + SAT** with reference-face clipping | substantial (axes, MTV, Voronoi regions, contact manifold) | 10–50× per test; hull↔hull ~100× | closest silhouette, hard edges | accurate but expensive and the usual source of jitter bugs; defer to a boss |
| D | **SDF per shape**: sample signed distance to the surface in local space | ~15 lines + one baked table per `GraphicsId` | ~1–2× for point/segment vs shape | arbitrary (even concave) silhouettes at fixed cost; normal free from the gradient; near-pixel fidelity cheaply | great for bullets-vs-hull, awkward for shape-vs-shape (no analytic pair test); depth jitters at texel scale |
| E | **Pixel / alpha mask** | — | ~10×, boolean only | exact art match | see next section — not usable for physics |
| F | **External physics engine** | none written here | n/a | shapes and manifolds for free | replaces the hand-tuned impulse, restitution, impact-damage and per-frame accumulator model; a dependency and determinism questions; against AGENTS.md — no |

Costs are orders of magnitude for *choosing*, and must be measured before keeping anything.

## Would pixel-to-pixel collision work? No — for physics

What it would give: hitboxes that match the painted silhouette exactly.

What blocks it here:

- **It returns a boolean.** No normal, no depth, no contact point. The impulse, the position
  correction, the friction torque and impact damage all need those. Recovering a normal from
  neighbouring set bits is more code and *less* stable than option B's closed form.
- **Rotation is the killer.** Hulls rotate continuously. A mask is per-angle: either regenerate it
  each tick (an O(pixels) pass per rotating body — a 64 px hull is ~4k pixel writes per tick,
  which alone dwarfs the current whole collision pass) or bake N angle buckets (64 buckets × ~200 B
  ≈ 13 KB per hull art) and accept stair-stepped angles.
- **The precision is finer than the simulation.** At 1/120 s a fast round moves 35 px per tick, and
  the current code already approximates that travel as a straight swept capsule. A 1 px hitbox
  cannot be resolved more accurately than the motion model that carries it.
- **Art silently becomes gameplay.** Hitboxes would follow painted pixels: decals, glows, and the
  anti-aliasing threshold (where is "solid"?) change what hits, and a texture repaint changes balance
  with no code diff. Bad for determinism and for tuning.
- **It buys nothing in the broad phase.** The grid stays circular, so the extra cost lands exactly
  where the count is highest — the bullet pass.

Where a mask *is* cheap and useful (non-physics, only if such a feature is wanted): "did the mouse
click land on the hull", placing hit sparks on painted armour, or a debug overlay showing where the
circle over- and under-covers the art.

If the real want is near-pixel fidelity without per-pixel cost, that is option **D**: one sampled
fetch per test, normal included.

## Simplifications that buy most of the visual win cheaply

- **Keep the broad phase circular, permanently.** `Radius` stays the bounding radius, so
  `SpatialGrid`, culling, spawn placement, pickup radii, targeting rings and explosions do not
  change at all. Shape only enters the narrow phase.
- **One shared shape per `GraphicsId`, authored as data in code** (local-space primitives, scaled
  by `Radius`). Entities store nothing new — `Position` and `Rotation` already exist. No per-entity
  allocation, so the 100k-object goal is safe.
- **Rotate the sample point into local space, never rotate vertices.** One 2×2 rotation per test;
  shape data stays static, shared, cache-friendly, and identical every tick (determinism).
- **Round every primitive.** A capsule is a segment plus radius, so all pair cases collapse to
  point–point, point–segment and segment–segment closest point — and
  `Vector2.ClosestPointOnSegment` already exists. Corners become rounded by construction, which is
  what removes the vertex-in-Voronoi-region special cases that make SAT implementations long and
  buggy. This single choice is what makes shapes affordable here.
- **Bullets and beams stay circles/segments forever.** They are drawn as 1.5–6 px dots and streaks;
  there is nothing to gain.
- **A rock is a circle until its art says otherwise.** The first three variants of each size paint
  2.4–3.2 R² of the 3.14 R² their collision circle covers, so they keep the disc and its one-test fast
  path. The lumpy variants added later paint 1.7–2.3 R², and each of those is two capsules across its
  lobes (`Stats/AsteroidShapes.cs`): 96–98% of the paint covered with 0.02–0.06 R² of collider sitting on
  air, against the 0.9–1.5 R² of air their disc was covering.
- **2–4 primitives per hull, core first, early exit.** Ammo only needs the earliest hit and already
  orders by `t`, so stopping at the first sub-pair hit is natural.
- **Derive mass and MOI from the shape definition once** and store scalars. Do not recompute `π·r²`
  per pair; impact damage keeps reading one mass per hull, so its balance is untouched by shape.
- **One deepest contact, not a two-point manifold.** Two-contact resolution would stop corner
  tumbling but is a much larger change with no angular damping to hide its noise.
- **Draw the real collider in the diagnostics pass.** `WorldRenderer` and `EnemyShipRenderer`
  already draw red/green circles; outline the shape instead so what you see is what collides.
- **Do not decompose into rectangles.** A square is not closer to a starfighter silhouette than a
  capsule is, and it needs many more primitives.

## Cost picture and gates

Measure each step with `.tools/scenario.sh <scenario> --sim --iters 3` and compare median, p99 and
`perShapeNs` against the previous row of `.tools/run/index.tsv`.

A gap to close first: the existing scenarios put an **asteroid** in the bullet stream and silence
turrets, so they price bullet-vs-circle and almost nothing of hull↔hull. A shape change would pass
them while the real cost went unmeasured. Add (as part of step 0):

- a **hull-crowding** scenario: many enemy ships and rocks pressed against the player, no bullets —
  prices hull↔hull narrow phase and resolution;
- a **stream-through-hull** variant: the seeded volley driven through an enemy hull, not a rock —
  prices bullet-vs-shape at the count that matters.

Expected order, to be confirmed: **A** ≈ noise; **B** ≈ a few percent in the ammo pass and in the
new hull scenario; **C** ≈ an order of magnitude in the hull pass and several × in the ammo pass —
i.e. plausibly a budget break at 120 fps.

Verdict per AGENTS.md: report "this change costs X% — keep, optimize, or revert".

## Recommended implementation order

Ordering principle: **never change two risky things in one commit**, and put the risky refactor of
the delicate resolution code while nothing else has moved. Steps 1, 3, 4, 5 cannot change gameplay
behaviour at all; 2, 6, 7, 8 are behaviour gates with a measurement and a stated way out.

**1. Baseline first: the two missing scenarios.** (no game code)
Add hull-crowding and stream-through-hull, run `--sim --iters 3`, record the circle's numbers in
`.tools/run/index.tsv`. Must come before anything else for two reasons: the refactor in step 2 is
only provably free against a pre-refactor measurement, and these scenarios price the *shape* work
that today's rock-only scenarios cannot see.
*Gate:* if hull↔hull is already a large share of sim at circle cost, aim at option A only.

**2. The seam, behaviour-neutral.** (the risky refactor — do it while the game is otherwise untouched)
Extract `NarrowPhase.TryCollide(a, b, out normal, out depth, out contact)`; point
`ResolveCollision`, `ResolveAmmoVsAsteroid` and `ResolveMineVsPlayer` at it instead of deriving
geometry from centres. Circle↔circle stays the only implementation. `CollisionSystemTest`,
`ImpactDamageTest`, `MissileCollisionTest` must pass **unedited**, and the scenario medians must sit
inside noise — that is the proof the seam is honest. It also deletes three copies of the same
centre-derived geometry, which AGENTS.md prefers to adding anything.
*Gate:* tests fail or scenarios drift → this is the cheapest place to back out. Nothing after it
depends on shapes existing.

**3. Shape definitions as inert data.** (compiles, nothing reads it)
`ShapeDefs`: per `GraphicsId`, local-space capsules, bounding radius = the existing `Radius`,
mass and MOI derived once. Authored by hand from the sprites — no asset pipeline.

**4. Draw the defs over the still-circular collision, then judge.** (diagnostics only)
The diagnostics pass outlines the def instead of the red/green circle while collision is still
circle-based. This is the mismatch audit, and it is the cheapest answer to "is this worth doing at
all": the sprites paint to roughly `1.1R` along the long axis, and the fighter has open background
gaps between its wings and fuselage that the circle treats as solid.
*Gate:* if the misfit is only cosmetic (nose and tail tips), stop, or take the cheap option-A win.
If wings and gaps matter, continue — with a screenshot as the evidence.

**5. Capsule math with pure tests.** (not wired, so it cannot break the game)
Segment–segment closest points, point–capsule, and the stated degenerate rules; a table of
transforms → expected `(normal, depth, contact)` within ~1e-4. Pure functions, no graphics.

**6. Composite plumbing, proven with circles only.** (new structure, zero new math)
Give one enemy hull a **two-circle composite** — the degenerate capsule — and run the composite
loop: primitive pairs, deepest contact with index tie-break, per-primitive reject, one impulse per
body pair per tick. This exercises everything except the capsule math, so when step 7 swaps the
primitives in, only one thing is new. It also delivers a visible elongated silhouette early.
*Gate:* if the composite itself costs too much, option A is the end state and steps 7+ stop.

**7. Capsules for one hull.** (new math, plumbing already proven)
Swap the Interceptor's two circles for its spine plus wing capsules. Prove it with the outline and
an integration test where a shot that misses today hits a wing tip, and one where a shot into the
wing/fuselage gap now passes through. Measure both new scenarios plus `ammo-spray`.
*Gate:* "this change costs X% — keep, optimize, or revert".

**8. Roll out the remaining enemy hulls.** Measure again. Balance tuning is a separate change, after
playtesting — never mixed into these commits.

**9. Player hull last, deliberately.** It is a balance decision more than a code one: shaping the
player makes the wings easier to hit and nose-first ramming survivable, which changes the game's
core feel. Land it only once the pipeline is trusted and playtesting backs it.

**Independent follow-ups, any time after 7:** lumpy rocks as an option-A composite (no new math) — taken later,
two capsules per lumpy art variant in `Stats/AsteroidShapes.cs`; polygon or SDF only if a large boss hull
eventually needs hard edges.

Deliberately **not** in this order, and why: rocks before hulls (rock art is round-ish and rocks are
the cheap, plentiful bodies — the shape win is all in hulls); balance retuning interleaved (it would
mask the cost measurements and the collision change); diagnostics after shapes (the overlay is the
step-4 evidence, so it has to come before the decision it informs).

## Consequences to accept

- **Dodging changes, and that is the point.** Nose-first ramming becomes survivable, broadside
  becomes dangerous, and gaps between wings become threadable. Balance expressed in radii
  (`WeaponBalanceTest` turn-radius rules, the threat ring, spawn distances) still holds, but the
  effective hull grows at the wings and thins at the nose — playtest before re-tuning.
- **The player hull getting a shape makes the player easier to hit** where the wings stick out.
  Recommended anyway: dodging is the player's core skill, and the current circle mis-fits the
  46–84 px hulls the most.
- The per-entity-type loops in `CollisionSystem` (asteroid pass, ship pass, ammo pass) do not change
  shape — only the test inside them does.

## Decisions needed before implementing

- Which option (B recommended; A as a stepping stone inside it).
- Does the player hull get a shape, or only enemies?
- Rocks: stay circles now (recommended) or go lumpy with the first batch? — settled once the lumpy rock art
  arrived: discs for the round variants, two capsules for each lumpy one.
- Should enemy keep-distance/formation read the shape, or keep using radii (recommended: radii)?

## Option B in detail: capsules

### What a capsule is

A **capsule** (2D; also "stadium", "swept circle", "round segment") is *the set of points within
distance `r` of a line segment*:

```
      ______
    /        \        segment axis:  A ─────── B     (two points, local space)
   |          |       radius:        r                 (half-thickness)
    \______/         shape:          every point whose distance to segment [A,B] ≤ r

   = a disc of radius r dragged along the segment
   boundary = two half-circle caps at A and B + two straight sides offset r from the axis
```

The project already uses one: an `Ammo` round *is* a capsule — `Components/CombatComponents.cs:7`
says collision treats a bullet as "the segment `[Position − Velocity·dt, Position]` inflated by"
its radius. So the moving object in the hot path is already a capsule; this option just gives the
hulls the same kind of primitive, which is why the math lines up so well.

A capsule is described by 5 numbers: `A(x,y)`, `B(x,y)`, `r`. Authored in the hull's **local space**
and scaled by the hull's `Radius`, so one definition per `GraphicsId` serves every instance.

### A hull is a small composite of capsules

Not one shape per hull — 2–4 capsules, the way a physics engine's "composite collider" works:

```
 fighter (851×937 art, drawn 2R wide)              interceptor (945×926 art)
        ▲ nose                                            ▲   ▲  two nose pods
       ╱|╲                                               ╲ ╲ ╲╱ ╱ ╱
      ╱ | ╲      spine capsule (long, r ≈ 0.35R)          ╲ ╲ ╲▲╱ ╱ ╱
     ╱  |  ╲     two swept-wing capsules (r ≈ 0.15R)       ╲ ╲ ╲│╱ ╱ ╱   4 wing capsules
    ◀───┼───▶    two pod capsules (r ≈ 0.12R)               ╲ ╲ ╲│╱ ╱ ╱   + 1 short spine
        ▼ tail
```

Wing tips get the cap's round end, which reads correctly for this art. The silhouette error is
bounded by the cap radius: a wing authored as `r = 0.15R` on a 46 px hull rounds its tip off by
about 7 px, which is at the scale of one bullet step (3.3–35 px per tick) — invisible in play.

### The math: three cases, one new helper

The existing helper does case 1 already:

1. **circle vs capsule** — closest point `q` on `[A,B]` to the circle centre
   (`Vector2.ClosestPointOnSegment`, already in `Domain/Vector2.cs`), then a plain circle test
   between `q` and the circle. ~10 flops.
2. **capsule vs capsule** — closest points `p`,`q` on the two segments; overlap iff
   `|q−p| ≤ rA + rB`. **This is the only genuinely new function**: segment–segment closest points,
   ~20–30 flops, no square roots or trig until the overlap is confirmed, one clamp-based branch
   structure.
3. **swept round (bullet/beam) vs capsule** — the bullet is already a segment, so it is exactly
   case 2 with `rSum = rBullet + rCapsule`. The segment parameter of the closest point on the
   bullet is the same `t` the current code uses to order hits along travel (239–300), so earliest-hit
   selection keeps working unchanged.

Each overlapping pair returns the three quantities resolution needs, for free and analytically:

```
normal   = (q − p) / |q − p|
depth    = rA + rB − |q − p|
contact  = p + normal · rA        (midpoint of the chord is the other sensible choice)
```

Only degenerate cases need a rule: if `|q − p| = 0` (axes crossed exactly), pick a deterministic
normal — the perpendicular of the longer axis. Stated as a tie-break so CI and replays cannot flip.

### Rotation is a per-test transform, not per-shape data

The shape vertices never move. Per narrow-phase test, transform the *other* body's sample points
into this shape's local frame: `pLocal = Rot(−θ)·(p − Position)`. One rotation (4 mul, 2 add) per
test; the def stays static, shared, in cache, and byte-identical every tick.

For capsule↔capsule only one body needs transforming: capsule A's axis is already its local
constant (it comes from the def), so transform B's two endpoints into A's frame and the test is
frame-free.

### What resolution then consumes

`ResolveCollision` stops deriving geometry from centres and takes it from the pair test:

| Today | With capsules |
|---|---|
| `normal = (b−a)/dist` | `normal` from the pair test |
| `penetration = rSum − dist` | `depth` from the pair test |
| contact `aPos + normal·aRadius` (921) | `contact` from the pair test |
| lever arms `rA = normal·aRadius`, `rB = normal·bRadius` | real arms `rA = contact − aPos`, `rB = contact − bPos` |
| mass `π·r²`, MOI `0.5·m·r²` computed per pair | mass/MOI stored once per shape def (area sum with the parallel-axis term) |

Composites need one rule: **test the primitive pairs and keep the deepest contact** (max depth,
ties broken by lowest primitive index), so a body pair still gets exactly one impulse per tick —
the same shape the current pair loops and `_frameRemainingHealth` accumulator already assume.

### Why "round primitives" is the simplification that makes this cheap

A capsule is a segment inflated by a disc, so every pair collapses to point/segment distance:
capsule↔capsule → segment↔segment, capsule↔circle → point↔segment. Because the round cap is part
of the primitive, corners are rounded *by construction* and the contact normal is always unique.
That is precisely the case a polygon implementation must special-case (vertex-in-Voronoi-region,
reference-face vs incident-face clipping), which is where SAT code gets long and jittery. Rounding
the corners is not a loss here — the art's wing tips and the sim's tolerance both live at that scale.

### Cost

New flops land only on pairs that already pass the unchanged bounding-circle reject:

- hull↔hull: 3–4 capsule pairs each → up to 9–16 segment–segment tests ≈ 100–400 flops per hull
  pair, against a hull-pair count in the tens-to-low-thousands per tick.
- bullet↔hull: `primitives per hull` segment–segment tests ≈ 75–100 flops vs ~10 today — but only
  for bullets that reach a hull. `ammo-spray` (rock in the stream) and `ammo-spray-miss` (nothing)
  should not move at all; only the new stream-through-hull scenario prices it.
- Mitigations if it does: 2–3 primitives per hull; order the widest first and stop at the first
  hit for ammo; per-primitive bounding-circle reject (3 flops) before the segment math; or fall
  back to option A (all-circle composite), which needs no new math at all.

### Memory and allocation

Per hull art: ~4 capsules × 5 floats = 20 floats ≈ 80 B; all hulls together well under 1 KB, in
static arrays. `SpatialItem`, the grid, and every entity component are **unchanged** — entities keep
only `Position` + `Rotation`, which is what keeps the 100k-object goal intact.

### New failure modes to expect

- **Inner-corner catching**: a composite's notch between two capsules can snag a bullet or wedge a
  body grinding along it. Mitigate by authoring capsules that overlap at a shared junction rather
  than merely touching, and rely on the existing `Slop` / `CorrectionPercent` softening.
- **More contacts per grind** along a rock (2–3 primitives overlapping) — bounded by the
  one-deepest-contact rule above.
- **AI separation** still steers by radius, so enemy wings may visually overlap while their colliders
  do not. Cosmetic; fix later only if it reads wrong.

### Proof that it works

- Pure unit tests of segment–segment closest points: parallel, collinear, zero-length (a circle),
  T-junction, endpoint-only overlap, exact-axis-crossing degenerate — expected
  `(normal, depth, contact)` within ~1e-4.
- One integration test where a shot that misses today **hits**: a bullet placed in the nose region
  beyond the current radius (the fighter art paints to ~1.1R along its long axis) and one placed in
  the painted gap between a wing and the fuselage, which must now pass through.
- Diagnostics outline drawn instead of the circle, so the mismatch is visible in a screenshot.

## Authoring the capsule layouts

**Decision for v1: hand-authored numbers committed in code, one table.** Not derived from art at
run time.

The data is small: 7 hull arts exist (scout, fighter, heavy, shadow, enemy-1, interceptor,
heavy-cannon; mines and rocks stay circles), needing 2–4 capsules each at 5 numbers per capsule —
roughly 70–140 numbers, in one static table. Authored with a **mirror convention** (write the
portside half, mirror to starboard), which halves the numbers and catches most typos by symmetry.

Why hand-authored rather than fitted to the sprite:

- **A collider should be tuned, not traced.** A capsule cover of the exact paint would include
  antennae, sensor pods, decal overhang and anti-aliased edges. Gameplay usually wants a slightly
  simplified, slightly forgiving silhouette. A fit optimised for coverage optimises the wrong thing.
- **It would re-introduce the objection this plan raises against pixel masks:** hitboxes becoming a
  function of painted pixels, so a repaint changes balance with no code diff. Committing the numbers
  makes every collider change a reviewable diff.
- **Architecture rule.** Decoding a PNG to derive a collider inside `Domain` would make game logic
  depend on rendering assets, which AGENTS.md forbids. The sim must be runnable headless from data
  alone, and it is today.
- **Cost of the code.** A fitter is far more code than the table it replaces — against "prefer
  deleting code over adding code" while there are only seven hulls.

**When to automate:** when the hull count grows, or when the PLAN.md Future item *mod-friendly data
definitions* lands. Then add an **offline generator** (a tool under `.tools/`, like `scenario.sh`)
that reads the texture's alpha and **proposes** capsules, writing the table for a human to review as
a diff. Never in the game, never at load time. The pipeline, if built:

```
alpha threshold → coarse grid mask (e.g. 64×64, art scaled to the bounding radius)
→ distance transform → medial-axis ridges (each ridge point carries its distance = a candidate radius)
→ prune short spurs, merge ridges into N straight strokes (ships are elongated, so the medial axis
  is naturally a small tree of strokes — one per wing, one per hull spine)
→ each stroke becomes a capsule: endpoints = stroke ends, radius = mean ridge distance
→ greedy top-up: while uncovered silhouette area > tolerance, add the best extra capsule or bump a radius
→ round every number to 2 decimals and clamp the count to a budget (4 capsules)
```

Every step here is individually easy; the cost is in **tuning the fit** (spur pruning, how many
strokes to merge, what to ignore) and in reviewing generated hulls, not in writing it. Deferred on
purpose.

### Doing the authoring with an LLM

- **The capsule math and its tests: easy.** Segment–segment closest points is standard, and the pure
  unit tests in step 5 pin the degenerate cases (parallel, collinear, zero-length, exact crossing)
  that are where an LLM would otherwise quietly guess.
- **A first-draft layout: easy, if it works from measurements rather than from looking.** An LLM can
  read the sprite and name the lobes correctly (spine, swept wings, pods), but eyeballed coordinates
  are the weak part — painted extent of thin features, transparent margins inside the texture box,
  and where symmetry actually holds. The reliable workflow is a throwaway script that prints the
  silhouette's extents and widths at several heights (max |x| and |y| where alpha > threshold, then
  the alpha span at 10 height slices), and the layout gets fitted from those numbers. No new game
  code; the script does not have to be kept.
- **The correction loop is what makes it safe, and the plan already builds it:** the diagnostics
  outline from step 4 draws the collider over the art, so a wrong layout is a screenshot away from
  being obvious, and step 7's two tests (a shot that misses today must hit a wing tip; a shot into
  the wing/fuselage gap must pass through) fail loudly on a bad fit. Author → overlay → correct is a
  short loop.
- **The automatic fitter: medium, and the review burden is quality, not code.** An LLM can write the
  distance transform and greedy cover in one sitting; getting ship-like silhouettes to fit in four
  capsules without eating the wings or sprouting spurs at convex corners is iterative tuning, which
  is exactly why it is deferred.

## Explicitly not doing

Pixel/alpha masks in the physics path · polygon manifolds and contact clipping · GJK/EPA ·
an external physics engine · per-entity collider allocation · shape-based grid indexing ·
rectangle decomposition.

## What landed

Steps 1-8 of the implementation order are in, in the order below, each measured:

1. `hull-stream` and `hull-crowding` (the two worlds that can price hull contact).
2. `NarrowPhase` (`Contact`, `TryCollide`, `TrySweep`) and the eight call sites moved onto it. Cost: zero
   (ammo-spray 1.36 → 1.35 ms, impact-storm 2.18 → 2.14).
3. `HullShapes` — three authored layouts, measured off the silhouette with `.tools/measure-hull.py`.
4. The diagnostics overlay (collider in cyan over the bounding circle), judged against the art on the
   virtual display before any collision changed.
5. `CapsuleGeometry` with 10 unit tests, unwired.
6. `CollisionBody` through the static and swept call sites, shapes still circles → no behaviour change.
7. The interceptor's cross wired in, and the two scenarios changed to hulls that actually have shapes.
8. The heavy cannon's four primitives (they arrived with step 3's table; the gap test came with step 7).

Two deliberate deviations from the plan as written:

- The plan expected "a shot that misses today must hit a wing tip". Impossible: the shapes are inscribed
  in the bounding radius, so a shape can only take hits away. The tests are the opposite pair instead —
  the gap a shape opens, and the tip a shape still covers — always with the same radius and circle art as
  the control.
- Steps 6 and 7 landed as separate commits but the interim "two discs" shape was skipped: step 5's tests
  already pin the disc case, so the plumbing was verified by measuring shapes-on against shapes-off on
  the same worlds (hull-stream 0.75 / 0.72 ms, hull-crowding 0.37 / 0.31).

## Player hulls — the same seam, one body later

The enemy hulls proved the seam, so the player hulls went through it unchanged: no new geometry, no new
contact rule, one `CollisionBody` built per tick and queued behind the same bounding-circle reject.

- One authored layout per hull type in `Stats/PlayerHullShapes.cs` (Scout 4 capsules, Fighter 4, Heavy 5,
  Shadow 6), mounted on `ShipType.Shape` and copied onto the `Player` component the way `Radius` is. A
  `Player` built without one keeps the disc: that is the state of the 30 existing player fixtures, which is
  how they stayed honest while the game's player got a shape (`WorldFactory` sets it, and a test pins it).
- `.tools/measure-hull.py` grew `--fit "u0 v0 u1 v1 t ; …"`, which rasterises a layout against the paint and
  prints the two numbers that matter: how much of the paint the shape covers, and how much of the shape sits
  over transparent pixels. Eyeballing gets the fit roughly right and the reach rule wrong; scoring gets both.
  It also fixed the tool's v axis, which was scaled by the texture's **height**: the renderer scales the whole
  texture by `diameter / texture-width`, so on a 936×975 sprite the shape's v units are 4% taller than the old
  printout claimed. The enemy layouts had been authored off that printout — their textures are within 2%
  square, so none of them moved.
- Scores: Scout 90% of the paint covered / 9% of the shape over air, Fighter 79 / 12, Heavy 90 / 14,
  Shadow 90 / 8. The Fighter's number is capped by the bounding radius, not by the layout: 0.34 R² of its paint
  is at radius > 1, and no shape inside the radius can cover any of it — the collision circle never could either.
- For a hurtbox the honest error direction is stingy, so coverage was bought before margin: the Heavy's fore
  pods get a thin bar down each pod rather than one bar across the transparent slot between them, and the
  Shadow takes two extra bars to cover its slab's fore and aft edges.
- Cost, same worlds: ammo-spray 1.32 vs 1.28 ms, impact-storm 2.11 vs 2.12, hull-stream 0.75 vs 0.78,
  hull-crowding 0.36 vs 0.36. No scenario fires at the player, so the swept-round-vs-player step is priced by
  proxy through hull-stream (+0.03 ms for a 2-capsule hull under a 5500-round volley); a 5-6 capsule player at
  a similar density of enemy fire lands in the same fraction of a millisecond.

### One radius per end — priced, not taken

Asked whether a capsule whose ends have different half-thicknesses (the convex hull of two discs, i.e. a cone
frustum capped by two discs) would make the math much heavier. Not free, not ruinous:

- A capsule with equal ends is a segment ⊕ a disc, and that identity is why the pair test is one closed form:
  closest points on the axes, minus `rA + rB`. Unequal ends are not a Minkowski sum of a segment and a disc, so
  there is no single radius to subtract and the depth is no longer `rSum − distance`.
- Round body (a round, a beam) against a tapered primitive: a 2-parameter minimisation, or 4×4 feature-pair
  enumeration (2 tangent lines + 2 arcs per side) — roughly 4-10× the current pair test. Deep overlap stays
  sane with feature enumeration and bad with GJK/EPA, which is iterative and blows up on penetration.
- The grid and the bounding circle are untouched and the reject still does most of the work, so the frame cost
  would be a fraction of that multiplier: 4-10× on the tests that already passed a reject is ~+0.15 ms on
  hull-stream's delta, not on the frame.
- The boundary stays C¹ (the tangent line is tangent to both arcs), so normals stay continuous — the
  feature-jitter that ruled polygons out does not return. New failure modes: one disc swallowing the other
  (the `asin` argument exceeds 1), equal-depth feature pairs needing index tie-breaks, and a pointed end
  (r = 0), whose apex is C⁰ and genuinely discontinuous.
- Not worth taking now: the residual mismatch it would buy is a few percent of painted area (the interceptor's
  aft fins), the composites are already 2-6 primitives, and the same fit is available today with no new math by
  stacking two or three collinear capsules of decreasing thickness — a staircase within Δr/2 of a taper, at
  +1 primitive. Where tapering would earn its keep is a big hull the player judges by eye, not a swarmer.

Still open on purpose: mass and inertia following the silhouette (a balance change to measure on its own),
and an offline fitter that proposes layouts from a sprite instead of scoring the ones a human typed.
