# OFF_SCREEN_INDICATORS.md

Status: shipped (landed 2026-11-01) — see `## Shipped` at the end for deviations and the perf verdict.

Edge-of-screen indicators for what the player cannot see: off-screen threats (enemy ships, racks,
guided rounds) and off-screen loot (health orbs, salvage, XP). Fulfils the PLAN.md roadmap line
"Off-screen threat indicators for enemy racks and missiles" and extends it to loot.

## The hole this fills

- The camera follows the player with up to 0.5 half-viewport mouse drift (CameraSystem), so
  pursuing ships and inbound missiles routinely live just past the edge with no cue. A missile
  launched from outside the launch ring enters invisibly-fast.
- Loot has a 90 s lifetime and scatters where ships died — often off-screen. Without a cue, the
  lull (the loot-collection window) becomes a blind search.
- plans/REROLL_TOKENS.md's "Scan pulse" item says it "needs the indicator renderer first". This plan
  is that renderer; the pulse becomes a thin follow-up.

## Constraints to respect

- **Hexagonal:** indicators are a rendering adapter. No sim change, no new components, no domain
  system. The renderer reads ECS read-only every frame, exactly like `TargetingRenderer`.
- **Bounded draw count (TROUBLE_SHOOTING #61):** scenario worlds inflate counts (missile-1024,
  thousands of `XpPickup`). The indicator pass must draw a *fixed maximum number of markers per
  frame* whatever the entity count. A per-entity chevron loop is forbidden for the many-entity classes.
- No new allocations in the frame loop: reuse static arrays (the `_litDraws` reuse pattern).
- Deterministic: markers are pure functions of world state; "nearest" caps break storage-order
  dependence.

## Design: one marker language, two collection rules

**Marker:** a small chevron (3-vertex triangle, ~10 px) sitting on the viewport edge, pointing
outward along the ray from the camera centre to the thing. One shared clamp helper places it where
that ray crosses the edge rectangle, inset ~28 px from the true edge.

**Draw order:** in `Renderer.DrawScene` after `WorldRenderer.Draw`, *before* the HUD bars, so the
health/XP/reroll widgets paint over any marker that lands under them. Only in the live
`Renderer.Render` path — not the paused stats/card/mount screens (world is dimmed there).

Two collection rules per class, chosen by expected population size:

| Class | Component filter | Rule | Colour | Cap |
|---|---|---|---|---|
| Enemy missiles | `Missile, Ammo, Position`, `Ammo.IsEnemy`, not `Dead` | individual chevron | orange (the R14 guided-round identity channel) | nearest 16 |
| Health orbs | `HealthOrb, Position` | individual chevron | mint `(150,255,190)`, same as the orb | nearest 8 |
| Salvage capsules | `RerollToken, Position` | individual chevron | salvage grey (`Palette.SalvageCap`) | nearest 8 |
| Enemy ships | `EnemyShip, Position`, not `Dead` | **sector bucket** with count | red (`Palette.HealthLow`) | one marker per occupied sector |
| XP pickups | `XpPickup, Position` | **sector bucket** with count | xp blue `(50,150,255)` | one marker per occupied sector |

**Sector buckets:** the circle around the camera centre splits into 16 sectors (22.5° each). One
`int[16]` per bucketed class; off-screen entities add 1 to their sector. One chevron per non-zero
sector at the sector's centre ray, with the count as small text (12 px, same colour) when > 1.
This is what keeps a 300-ship rack and a 2000-orb field constant-cost: ≤ 16 + 16 bucket markers.

Why split individual vs bucketed:
- A missile must be individually locatable to be dodged — and normal play caps enemy missiles in the
  air at 7 (`EnemyShipStats.MissileCapFor`), so individuals are cheap there; the cap of 16 exists
  only to bound the inflated scenarios.
- Orbs/salvage are rare per screen and each one matters — individuals, capped at 8 as a guard.
- Ships and XP orbs are many-entity classes; direction-with-count is all the player can act on
  anyway ("the rack is coming from the northeast", "XP is that way").

**Nearest-N selection:** keep a fixed array of (distSq, position) per capped class with an insertion
check (arrays ≤ 16 long, O(cap) per accepted entity, O(1) reject). Deterministic regardless of
pop/swap storage order.

**Off-screen test:** reuse `RenderHelpers.ToScreen` + `IsOffScreen` semantics on the camera-centre
frame; skip entities inside it (they are already drawn as themselves).

**Not in v1 (deliberate):**
- Enemy *bullet* trails (`Ammo.IsEnemy`): fast and short-lived, they enter the frame on their own;
  bucketing them would repaint a sector every volley for sub-second warning.
- Mines: drift slowly toward the player; the wave-direction bucket already covers where they came from.
- Rack-vs-gun hull distinction (brighter chevron for a `MissileWeapon` hull): possible refinement,
  but ships are bucketed in v1, so there is no per-ship marker to recolour yet.
- Distance-based alpha fade: constant alpha first; add only if playtest says the cue is too quiet.
- A toggle key/env: no. This is a readability feature, always on. (The `hud.indicators` timing pass
  is the diagnostic handle, per TROUBLE_SHOOTING #61.)

## Code shape

- `src/Game/IndicatorRenderer.cs` (new, ~120 lines total, one responsibility):
  - `Collect(...)` — pure: walks the filters above, fills reused arrays/bucket counters. No Raylib
    calls, so the Tests project (which already references Game — see `DamageShadingTest`) can assert
    it headless.
  - `Draw(...)` — draws chevrons + counts from the collected result. Fixed max draws.
  - `EdgePoint(cameraCentre, viewport, inset, dir)` — pure ray-to-inset-rect clamp; also tested.
- `Renderer.DrawScene`: one new call + `RenderTimings.Pass("hud.indicators")`.
- `RenderHelpers`: expose or duplicate the small `Rotated` point rotation the chevron needs.
- `Palette`: threat-red reuses `HealthLow`; add an `IndicatorMissile` orange matching
  `WorldRenderer.AmmoColorOf(Orange)` so the cue and the round read as the same object (same rule as
  the salvage capsule sharing its HUD icon).

## Tests (headless, pure)

- `EdgePointTest`: axis directions hit the inset rectangle exactly; a 45° corner ray clamps on the
  nearer edge; entities exactly on the border are not marked.
- `IndicatorCollectTest` (small EntityManager worlds, like `EcsTests`):
  - one off-screen enemy missile → 1 individual orange marker; on-screen missile → none;
    player-owned missile (`IsEnemy=false`) → none.
  - dead ship (husk with `Dead`) → no ship bucket increment.
  - two off-screen ships in the same sector → one bucket count 2; in opposite sectors → two buckets.
  - 20 off-screen missiles → exactly the 16 nearest survive; same for orb/salvage cap 8.
  - XP bucket counts match pickup positions.
- Determinism: collecting twice from the same world yields identical arrays (reused buffers cleared).

## Verification (measure, don't assume)

- `dotnet test` for the new tests; project must keep compiling.
- Sim is untouched, but run the gate anyway: `.tools/scenario.sh missile-1024 --sim --iters 3` —
  compare median/p99/perShapeNs against the previous `.tools/run/index.tsv` row; verdict in the
  reply (should be a wash — no sim change).
- Render cost is the real measure: windowed `missile-1024` and `ammo-spray` runs with
  `SPACEVORS_DIAGNOSTIC=1`; read the `renderPasses` worst-first table for the new `hud.indicators`
  pass and compare total frame median to the prior baseline row. Expected: a few µs (bounded marker
  count, one extra component scan per class — the renderer already scans these same entities).
  If `hud.indicators` shows up as a frame percentage outlier, the bucket loops are the suspect first.
- Check start/mid/end scenario shots: markers should sit on the inset edge, never cover the XP bar,
  and the shot must not be a wall of chevrons (if it is, lower the caps or raise sector granularity
  — recorded here as the tuning knob, not guessed at now).

## Follow-up hooks (not this plan)

- **Scan pulse** (plans/REROLL_TOKENS.md menu): a loot/threat class reveal for N seconds is now a
  filter flag on `Collect`, no new renderer.
- **Elite marker** (plans/ENDLESS_DIFFICULTY.md): elites get an individual brighter chevron once
  they exist.
- If playtests show players ignoring bucketed ship markers, upgrade ships to "nearest 8 individuals"
  — the collection code already has that machinery for missiles.

## PLAN.md bookkeeping (at first implementation commit, not now)

- Point the roadmap bullet "Off-screen threat indicators for enemy racks and missiles" at this file
  and mention loot.
- On ship: `Status: shipped` header, `## Shipped` section with the perf verdict, one-line feature
  index entry under "Graphics and UI".

## Shipped (2026-11-01):

What landed:
- `src/Game/IndicatorRenderer.cs` — `Collect` (pure, headless-tested) + `Draw`. Edge chevrons point
  outward from the camera centre at off-screen enemies (red), enemy missiles (the guided round's
  orange, `WorldRenderer.AmmoColorOf(Orange)`), XP pickups, health orbs and salvage — each pickup in
  the exact colour it draws with in the world (`Palette.XpPickup`, `Palette.HealthOrb`,
  `Palette.SalvageCap`; `WorldRenderer` now draws the world pickups from the same two new constants).
- Triangle aperture tapers with distance: `WideHalfWidth` 11 px at or within `WideDistance` 900,
  linear to `ThinHalfWidth` 3 px at `FarDistance` 2600, constant beyond (the user-requested taper).
- Hooked in `Renderer.DrawScene` after `WorldRenderer.Draw`, before the HUD bars (timed pass
  `hud.indicators`), so HUD widgets paint over any marker that lands under them.
- `src/Tests/IndicatorRendererTest.cs` — 6 tests: on-screen/dead/player-owned filtering, loot
  collect, nearest-N cap whatever the storage order, buffer reset, edge clamp, aperture taper.

Deviations from the plan body (frozen intent above — this is what was actually built, per direct
instruction to taper triangles by distance rather than bucket):
- **Sector buckets replaced by individual distance-tapered chevrons with nearest-N caps**
  (enemies 24, missiles 24, XP 16, orbs 8, salvage). The taper itself declutters the far field;
  the caps keep the pass bounded per TROUBLE_SHOOTING #61. No count text.
- Indicators draw inside `DrawScene`, so they also exist behind the paused screens' dim wash
  (rather than a live-only flag); the dim covers them.
- XP markers are individual (capped 16), not bucketed — play-scale XP fields are small and the cap
  bounds the inflated scenarios.

Perf verdict (2026-11-01): sim flat — `missile-1024 --sim --iters 3` median 3.14 ms, budget pass
(Nothing in Collect runs on the sim side; sim never sees the renderer). Render on the sandbox's
llvmpipe (absolute numbers not comparable to the GPU baseline): windowed `missile-1024`,
`hud.indicators` median 0.12 ms ≈ 0.6 % of a ~20 ms llvmpipe frame with ~1000 missiles in the world
— the cost is the five component scans plus ≤80 triangles. Frame median unchanged from the same
host's prior shape (present/world.sparks dominate). Verdict: this change costs well under 1.5 % of
the 8.33 ms budget in the worst inflated scenario — keep. Screenshot check (frame120): red enemy
slivers on the side edges, wide orange cluster where the missile rack exits the top, blue XP markers
bottom — caps visibly holding.
