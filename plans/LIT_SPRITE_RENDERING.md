# LIT_SPRITE_RENDERING.md — inspection + improvement proposals

Status: shipped (2026-11-02 — P1..P6 landed, each measured; P7 parked)

Written 2026-11-02. Scope: `Lighting.cs`, `LitGroupRenderer.cs`, `LightGatherer.cs`,
`ImageLoader.cs`, `LitSprite.cs`, and the lit call sites in `WorldRenderer` / `EnemyShipRenderer` /
`ShipSpriteRenderer`. Do not duplicate findings already recorded in `plans/GRAPHICS_REVIEW.md`
(that file is the frozen review; this one is the forward-looking render-quality/cost list).

## What the path does today

- One fragment shader (`Lighting.cs`): tangent-space normal map rotated by a per-instance angle,
  depth-map self-shadow (2 depth taps), one fixed directional light, up to 16 screen-space point
  lights, and a procedural Voronoi hull-damage block gated on `damage > 0`.
- Per-instance data rides in the vertex colour (RG = 16-bit angle, B = damage, A = opacity) because
  uniforms are global at batch-flush time in this raylib build (TROUBLE_SHOOTING #14).
- `LitGroupRenderer` batches per sprite variant: one `BeginShaderMode` block + one light-array
  upload per variant, then N `DrawTexturePro` calls.
- Per-fragment texture fetches: base + normal + depth(here) + depth(toward light) = **4 fetches**
  (3 distinct textures) for an undamaged sprite.
- Every lit quad is drawn as the **full texture rectangle** (`FullSource`), scaled to
  `diameter = 2 × radius`.

## Measurements taken for this inspection

Render cost (sandbox, llvmpipe software GL, 1920×1024, `RenderBench`, log
`.tools/run/renderbench-lights.log`). Absolute ms here are ~10× a real GPU; use the ratios.

| scenario | ms/frame | delta |
|---|---|---|
| flat-200 (no lighting code) | 4.82 | — |
| lit-200-L0 (200 lit sprites, 0 lights) | 10.63 | +5.81 vs flat |
| lit-200-L16 (same, 16 live lights) | 12.17 | +1.54 vs L0 |

Read: the **fixed per-fragment cost of the base lighting path** (3 textures, self-shadow, normal
rotate) is ~4× the whole 16-light loop. So the biggest levers are *fewer shaded fragments* and
*fewer/cheaper fetches*, not the light loop.

Real-GPU window baselines (`.tools/baselines/*.json`, host=window) put the lit pass where it
matters: `impact-storm` `world.asteroids` = 2.773 ms mean of a 5.31 ms frame with only **35 rocks**
alive — the lit shader, not the traversal, is what that pass is paying for.

Asset facts (measured from `assets/`, 25 lit sets):

- **VRAM 270 MB** at RGBA8 (3 maps × ~1024² × 4 B × 25 sets). Six sets are 12.6 MB each.
- **Minification without mipmaps.** Nothing in `src/` calls `GenMipmaps` or `SetTextureFilter`, so
  raylib's bilinear default samples 4 texels of a 19×-minified map:
  enemy-1 762 px texture drawn at 40 px = **19×**; streak 14×; shadow 11×; interceptor 10.5×;
  small rocks 5–15×; large rocks 2–4×. That is silhouette shimmer/moiré while rocks spin, plus
  poor texture-cache locality.
- **Transparent margin inside the quad** (alpha > 8 coverage of the base map; bbox = tight opaque box):
  raptor 43 % bbox / 24 % covered, streak 44 % / 22 %, manta-ray 45 % / 19 %, falcon 53 % / 27 %,
  carmine 73 % / 33 %, eagle 88 % / 28 %, shadow 84 % / 72 %, large rocks 64–99 % / 47–78 %.
  So 20–80 % of shaded fragments on ship sprites land on fully transparent texels.

## Proposals, ranked

### P1 — Skip shaded work on transparent texels (look: unchanged; perf: best ratio)

Early in `main()`: `if (baseColor.a <= 0.004) { finalColor = vec4(0.0); return; }` after the base
fetch. Saves both depth fetches, the self-shadow math and up to 16 light iterations on the 20–80 %
of ship-quad fragments that are bare background. One line, no visual change (blend result is
identical), no batching change.

- Tradeoff: on a GPU the win is quantised to 2×2 quad granularity, so it is smaller than the raw
  coverage numbers; still free to have.
- Verify: `RenderBench lit-200-L0/L16` + `.tools/scenario.sh impact-storm --iters 3` (window host).

### P2 — Pack depth into the normal map's alpha at load (perf + memory)

Both maps are RGB PNGs today (alpha unused). At load, build one RGBA map: RGB = normal, A = depth.
Shader then does `vec4 nd = texture(map, uv)` → normal from RGB, depth from `nd.a`; the
toward-light tap reuses the same sampler. **Fetches per fragment 4 → 3, distinct maps 3 → 2,
VRAM 270 → 180 MB**, and one fewer sampler. Implementation is a handful of `Image` calls
(`LoadImage`, `ImageAlphaFromColor`-style channel copy or a small loop) plus `LitSprite` losing one
field — net code is roughly neutral.

- Tradeoff: load-time pixel work (one pass per map, once at startup); the warning in `LoadLitSet`
  must compare the merged map instead of two.
- Verify: `RenderBench probe` must stay `PASS` (pixel parity), plus scenario shots.

### P3 — Mipmaps + a load-time resolution cap (look: removes shimmer; perf: bandwidth)

`GenMipmaps` + `SetTextureFilter(MIPMAP_LINEAR)` on the lit maps, and/or downscale each set at load
to a cap tied to how big it can ever be drawn (ships ≤ 256 px, small rocks ≤ 256, large rocks ≤ 512).
At 5–19× minification this is the single biggest **visual** fix: a spinning rock stops crawling and
swarmers stop glittering; it also cuts VRAM ~4–16× and makes every fetch hit cache. The existing
`normalize()` of the sampled normal is what makes mipmapped normals safe.

- Tradeoff: mipmapping the **base colour** can fringe silhouettes (RGB of transparent texels bleeds
  into the edge). Mitigations, cheapest first: mip the normal/depth map only; cap the chain depth;
  or dilate RGB into transparent texels once at load (~15 lines).
- Tradeoff: downscaling is a quality ceiling — pick the cap from the largest on-screen size, not a
  guess, and check the scenario shots.
- Verify: pixel-diff a start/mid shot before and after (`.tools/diff.sh`), expect *deliberate*
  differences; measure `impact-storm`.

### P4 — Cheaper point-light loop (perf, small)

Measured at +1.54 ms/200 sprites in software GL, so a second-order lever, but all three changes are
nearly free:

- Early-out on squared distance: compute `dGl` once, `dot(dGl, dGl)`, `continue` when `> r²`, then
  take `sqrt` once for the few lights that matter. Currently `distance()` + `smoothstep` +
  `normalize` run for every one of the 16 slots.
- A `uLightCount` uniform so trailing slots are never read. Frame-global, so it is legal to set per
  block; the generated-source trick already keeps the C# and GLSL limits in step.
- Consider `MaxPointLights` 16 → 8 once P7 makes the choice of *which* eight matter.

### P5 — Make the 16 slots go to the lights you can see (look + sim-side perf)

`LightGatherer` fills slots in scan order: explosions, player, enemies, mines, and `AddLight`
silently drops the rest. With hundreds of ships alive, which 16 you get is an artefact of entity
order, and the loops keep scanning after the list is full.

- Expose `Lighting.IsFull` and break out of the gather loops once full.
- Keep the priority tiers, but within the enemy/mine tiers prefer nearer / brighter, so a distant
  swarmer cannot steal a slot from an explosion at the player's elbow.
- Optional look win: per-light tint. `PointLightTint` is one hard-coded orange for everything, so
  thrusters glow the same as explosions. Add `uniform vec3 uLightTint[MaxPointLights]` (trivial
  upload) and let `LightGatherer` set thruster blue vs explosion orange.

### P6 — Look: fix clipping and give the hulls a material read (shader only)

Small, tunable changes in the lighting line, all procedural, no new assets:

- **Clip:** `AmbientLevel + diffuse` peaks at 1.30, so the lit side of a bright hull clips to white
  and the texture detail you paid for disappears. Scale diffuse into the headroom
  (`Ambient + diffuse * (1 - Ambient)`), same intent as the existing `MaxPointLightContribution` cap.
- **Albedo-modulated point light:** `PointLightTint * pointLight` is added flat, so a flame paints
  uniform orange over any hull. Multiply by `baseColor.rgb` (keeping a small flat term for the hot
  core) keeps plate detail inside the glow.
- **Cool fill instead of grey:** `AmbientLevel` is a scalar; a dim blue fill (space) reads better
  than 30 % grey and separates the dark side from the background.
- **Rim light:** `pow(1 - normalScreen.z, k)` × cool colour, no new texture fetch, ~4 ALU ops.
  This is the cheapest legibility win on the list — overlapping dark hulls currently merge.
- **Specular:** a Blinn-Phong term against `LightDir` gives the hulls a metal read; costs one `pow`
  per fragment. Apply to hulls only if the cost shows up.
- **Nearby lights should be shaded too:** point light ignores `shadowed`, so a flame lights the side
  of a hull facing away from it. Multiply by `(1 - shadowed * 0.7)`.
- **Softer self-shadow:** single tap with `ShadowUvOffset` 0.02 gives a hard, sometimes blocky
  terminator; a second tap at half offset (take the max) smooths it for one more fetch — free if
  P3 mipmapping is in, since the wide tap then reads an averaged level.

### P7 — Draw only the opaque box (perf: up to ~2× fewer fragments on some hulls; more code)

Trim the source rect to the precomputed alpha bbox (raptor/streak/manta-ray are at 43–45 % bbox
area) and draw that sub-rect instead of the full texture. Fragments, fetches and the light loop all
fall with the area.

- Tradeoff: this is the only proposal with real complexity. The bbox centre is not the texture
  centre, and the hull's collision centre is the texture centre, so `LitSprite` must carry
  `(uvMin, uvMax)` plus an offset that is rotated by the sprite angle at draw time. Get it wrong and
  sprites shift off their colliders. Keep world scale by sizing the dest rect from the untrimmed
  texture width. Do it after P1/P2 and only if `impact-storm` still shows the lit pass dominating.

## Explicitly not now

- **One atlas / texture array so all lit draws share one block.** Only buys CPU dispatch; the
  visible variant count is small and the cost is GPU-bound. Fights the "prefer deleting code" rule.
- **A low-res light-buffer render target** (deferred lighting). Would make light count free, but adds
  an offscreen pass, a second shader and ordering rules — over-engineering for 16 lights.
- **Uploading the light array only when it changes.** Per-block upload is required by flush
  semantics (TROUBLE_SHOOTING #14); the saved bytes are noise.
- **Dropping `discard` for the perforation holes.** Fine on desktop; revisit only if a target GPU
  penalises it (the soft-alpha variant is trivial: write `finalColor.a` instead of discarding, which
  also removes the hard aliased hole edge).

## Order of work

1. P1 (one line) → measure.
2. P2 (merged map) → measure + `RenderBench probe` parity.
3. P3 (mipmaps/cap) → shots + pixel diff + measure. This is the one that changes how the game looks.
4. P4 + P5 (light loop, gatherer, per-light tint).
5. P6 (look tuning) as one small commit, judged from scenario shots.
6. P7 only if the lit pass is still the top render cost.

Each step: `.tools/scenario.sh impact-storm --iters 3` and `hull-damage --iters 3` on the real-GPU
window host (sim host cannot see render changes), `RenderBench probe` for pixel parity, and the run's
start/mid/end shots for anything that changes look. A pass that got cheaper than its geometry can
explain means the geometry is not being drawn — check the shots before believing the number.

## Shipped (2026-11-02):

P1-P6 landed, one commit each, each with its own paired measurement. P7 (alpha-bbox trim) stayed
parked: after P1 and P4 the transparent margin no longer costs anything worth trimming.

| stage | change | L0 | L4 | L16 | flat control | pixels |
|---|---|---|---|---|---|---|
| P1 | early-out on transparent texels | -26.5% | - | -28.6% | - | identical |
| P2 | depth folded into the normal map's alpha (3 maps -> 2) | -6% | - | - | - | identical (cross-build diff max 0) |
| P3 | 512 px cap + mip chain, base uploaded premultiplied | -0.7% | - | - | -25.7% | softer at 2-19x minification, no dark fringe |
| P4 | `uLightCount` loop bound + squared-distance light cull | -47% | -39% | -22.8% | -0.4% | identical |
| P5 | per-light colour, gatherer priority, `IsFull` early exit | +0.9% | +3.0% | +6.6% | -0.9% | identical |
| P6 | key + cool fill + rim + Blinn-Phong, albedo-modulated point light | +3.1% | +2.4% | +1.3% | +0.4% | 8.6% of pixels moved, mean 1.5/255 |

Cumulative, measured the same way with the pre-series files checked back out (B = before P1, A = shipped):
`lit-200-L0` 97.4 -> 36.0 ms cpu (-63%), `L4` 99.7 -> 41.6 (-58%), `L16` 105.4 -> 58.4 (-45%),
`flat-200` 23.7 -> 17.2 (-27%, the cap on the shared base textures).

VRAM, from the PNG headers of all 25 lit sets: 272 MB native (3 maps) -> 181 MB after P2 -> 77 MB after
the 512 px cap, **103 MB including the mip chains**. That is the art budget, not a per-frame cost.

Look, judged from seeded screenshots (`hull-damage`, `impact-storm`, frame 60) rather than numbers:
before P6 a fireball turned a charcoal hull into a white blob and a rock under a fireball lost its
surface; after it the plating and the rock texture stay readable while the fireball sprite itself is
untouched. The scene as a whole reads slightly dimmer and cooler - deliberate, the old rig peaked at
1.30 and clipped wherever the key light landed. The rim is deliberately subtle at gameplay zoom.

Deviations from the plan:
- P6 skipped the 2-tap self-shadow softening listed in the proposal: it spends back the fetch that P2
  bought, and the craters already read. It added the optional highlight and rim instead.
- The `RenderBench` probe could not stay a frozen copy of the pre-batching shader. It now mirrors the
  live light rig and differs only in taking the angle from a uniform, so it still checks the vertex-angle
  encoding and variant grouping; a look change has to be copied into it or the probe reports the change
  itself as a mismatch. It also refuses to time a `lit-*` scenario when the shader did not load, because
  a silent flat fallback once measured as a -74% win.
- Verification ran on the sandbox's llvmpipe (`RenderBench`, `.tools/ab-lit.sh`), not on the real-GPU
  window host as the order of work assumed: `scenario.sh --sim` cannot see render changes and there is no
  GPU here. These numbers price CPU fragment work, not GPU bandwidth. One direction to re-check on real
  hardware: mip filtering costs a little CPU under llvmpipe while saving a lot of texture traffic on a
  GPU, so P3's verdict on real hardware should be no worse than neutral.

Measurement method, since the numbers above are only meaningful with it: `.tools/ab-lit.sh` builds both
sides, snapshots the dlls, and interleaves 6 reps per side, reporting the minimum per-frame CPU time with
llvmpipe pinned to 4 threads. Sequential sides once put the untouched control at +41%; wall time on this
host swings by 50% while the drawn work is byte-identical (TROUBLE_SHOOTING 65, 69).
