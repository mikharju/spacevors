# DAMAGE_SHADING.md

Proposal: procedural hull-damage shading (2026-09-16). Complements the done smoke/spark
plan (`DAMAGE_GRAPHICS.md`); this changes the hull itself, not particles.

## Requirements

- Damaged ships visibly differ on the hull; undamaged ships look **exactly** as now.
- Damaged ships stay lit: a turning ship still shows the bright and shadowy side.
- Fully procedural — no new bitmaps.
- Must not break the existing lighting shader.

## Why this is cheap in the current architecture (verified in code)

- `Lighting.cs` fragment shader already computes lighting from **tangent-space normal
  maps rotated by a per-instance angle**, with a depth-map self-shadow pass. Anything
  that perturbs albedo / normal / depth *before* the lighting math keeps lighting intact
  for free — that is the whole trick.
- Per-instance data travels in the vertex color: RG = 16-bit angle, A = opacity.
  **B is written (255) but never read by the shader — a free per-instance byte.**
- UVs (`fragTexCoord`) are sprite-local, so any pattern generated from them rotates
  with the ship automatically, while lighting stays screen-space. This is exactly the
  "turning damaged ship shows lit/shadow sides" behavior.
- `LitGroupRenderer` batches per sprite variant; damage riding on the vertex color
  needs **no batching, grouping, or uniform changes**.
- Renderers already iterate `Health` alongside `Position`/`Rotation`
  (`EnemyShipRenderer.Draw`), so hp ratio is one subtraction away.

## Per-instance damage transport (two options)

### T1 — Vertex-color B channel (recommended)

`EncodeAngle` sets B = round(damage × 255); shader reads `fragColor.b`.

- Pros: zero new batches, zero new uniforms, works per-instance inside existing
  variant groups; damage = 0 → shader skips all damage code → output is
  **bit-identical** to today for undamaged ships.
- Cons: 8 bits total; if a per-ship pattern seed is wanted later, pack 4-bit damage
  tier + 4-bit seed (fine — damage is tiered anyway).

### T2 — Damage tier as batch key

Group key becomes `(LitSprite, tier)`; tier passed as a uniform in `Lighting.BeginDraw`.

- Pros: more data per group (seed, per-tier params), no channel packing.
- Cons: up to 3× shader-mode blocks and draw-order churn; uniform is per-group, so
  all ships in a group share it (that is the point, but it coarsens transitions).

Both keep the undamaged path untouched. T1 is simpler and per-instance; start there.

## Alternatives for the damage look (all procedural, all pre-lighting)

All patterns are pure functions of `fragTexCoord` (+ optional seed), so they rotate
with the hull and need no textures.

### A. Grime & scorch (value-noise albedo darkening)

2–3 octaves of hash-based value noise over UV; where `noise > threshold(damage)`,
multiply `baseColor.rgb` by a soot factor and slightly flatten the normal.

- Pros: cheapest to implement; continuous "wear" feel; subtle.
- Cons: reads as dirty, not as *hit*; no strong silhouette change.
- Cost: ~8 hash lookups per fragment, damaged ships only.

### B. Voronoi impact craters (recommended core)

One Voronoi cell field over UV (3×3 cell search, hash-based). Each cell center = one
bullet impact. Damage scalar grows crater radius `r` around centers:

- rim: dark scorch ring `smoothstep(r, 0.6r, dist)` × soot color (albedo),
- dent: analytic gradient of the crater bowl perturbs `normalLocal` → dents catch the
  directional and point lights correctly (bright rim on the lit side, dark inside),
- self-shadow: subtract the same bowl from `depthHere` before the existing shadow test
  → craters self-shadow with the mechanism already in the shader.

- Pros: unmistakably "shot"; fully lit (dents shade); reuses the depth pass; one field
  drives everything.
- Cons: most shader code of the set; Voronoi ≈ 9 hashes/fragment (negligible on
  ≤170 px sprites, and only for damaged ships).

### C. Hull perforation (alpha punch-through)

Same Voronoi field: when damage is heavy, `dist < holeR(damage)` → `finalColor.a = 0`
with a hot-metal rim ring just outside. Space shows through the hole; the hole rotates
with the ship; the rest of the hull keeps full lighting.

- Pros: dramatic, cheap (reuses B's field), reads at a glance even at small sprite size.
- Cons: alpha holes + raylib batching are fine (no sorting issue — hole is inside the
  sprite's own pixels), but holes near the sprite edge can clip; keep hole centers
  away from UV border.

### D. Glowing cracks / embers (heavy tier only)

Thin procedural crack lines (hash-warped sine bands or the Voronoi edge distance)
add `emberColor × glow(damage)` **after** the lighting line, pulsing slowly with time.

- Pros: sells "critical" state; near-zero cost.
- Cons: emission is by definition unlit — keep it a small additive term on top of the
  darkened albedo (A/B), never the only damage cue, or the ship reads flat again.

### Recommended combination

One shared Voronoi damage field, three damage tiers keyed to the existing HP thresholds:

- ≥ 2/3 hp: nothing (identical to today),
- < 2/3 hp: A (light scorch around impacts) + B (small craters),
- < 1/3 hp: bigger craters + C (one or two holes) + D (ember glow in craters).

Same thresholds as the smoke/spark system, so the effect language stays consistent.

## Rejected alternatives

- **Raylib shapes drawn over the hull** (circles/triangles as scorch marks): zero
  shader risk, but the overlay is flat — it darkens the lit side equally, which is
  precisely the "loses its lighting" failure the requirement rules out.
- **Pre-rendered damaged sprite variants**: violates "no new bitmaps".
- **Vertex displacement / silhouette deformation**: raylib batches plain quads; no
  per-sprite geometry to dent.
- **Separate full-screen damage pass**: would need a hull mask G-buffer; far heavier
  than a branch in the existing shader.
- **Second shader variant compiled without damage code**: the `damage == 0` early
  branch already gives identical output and near-zero cost; two programs double the
  shader-mode toggles for no gain.

## Why the lighting shader is not broken

- Damage code runs between `texture(texture0, ...)` / normal decode and the lighting
  math; `LightDir`, ambient, shadow, and point-light blocks are untouched.
- `if (damage > 0) { ... }` wraps every damage line → undamaged fragments execute the
  identical instruction sequence as today (bit-identical pixels, verifiable by
  screenshot diff).
- No new textures, no new uniforms (T1), no new batches, no changes to
  `LitGroupRenderer` / `LightGatherer`.

## Performance & determinism notes

- Extra fragment cost only on damaged ships' own pixels; ships are 15–170 px sprites
  and the 10k-object goal is dominated by undamaged ships (branch not taken).
- Pattern is a pure function of `(uv, damage, seed)` — no RNG, no simulation state,
  no determinism surface. Renderer-only change; game logic untouched.
- Time-based ember pulse (D) uses frame time only; harmless for determinism.

## Open questions (decide before implementing)

1. Seed variation: all ships of one type share their crater layout (variant-seeded),
   or pack a 4-bit per-entity seed into B for variety? (Recommend: start shared,
   rotation already differs per ship.)
2. Apply to asteroids/mines too? Their draws go through the same shader; enabling
   them is a parameter, but PLAN.md only asks for ships.
3. Tier transitions: hard switch at 2/3 and 1/3 (consistent with smoke tiers) vs
   continuous morph with hp ratio (nicer, slightly more code).
4. Diagnostic hook: extend the K key or add a key that cycles a test ship through
   damage tiers for screenshots (per TROUBLE_SHOOTING.md headless recipe).

## Suggested stages (when approved)

1. Shader damage field + T1 transport, tier A+B only; screenshot-diff proof that
   damage = 0 is pixel-identical.
2. Tier C holes + D embers; tune thresholds/rates visually.
3. Wire hp ratio from `Health` in `EnemyShipRenderer` / player ship draw path;
   diagnostic key; docs update.
