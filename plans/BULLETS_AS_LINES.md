# BULLETS_AS_LINES.md

Plan for turning bullets from points into swept lines, adding lasers and particle
accelerators, and giving projectiles a width that scores grazing hits (2026-10-11).

## Status: not started

## Requirements (from the request)

- Bullets travel much faster than today.
- A bullet that moves so fast it completely passes a target between ticks must still hit it
  (continuous / swept collision, no tunneling).
- New weapon class **laser**: fires a line from start to max range instantly; damage applies
  to the **first thing hit only**.
- New weapon class **particle accelerator**: much faster than current bullets, still has
  travel time, fast enough to draw a visible line on screen.
- Projectiles have a **width**: a near miss inside the width scores a grazing hit.
- Propose a few interesting weapons using these new properties.

## Current state (verified in code)

- `Ammo(Velocity, Radius, Lifetime, IsEnemy, Damage, Color)` — a moving **point** with a
  collision radius. Spawned by `TurretFiringSystem.FireAtTarget` with `Position + Velocity + Ammo`.
- Ammo velocity is **constant**: ammo entities never get `Acceleration`, and
  `PhysicsSystem` only integrates velocity for entities that have it. So the position at the
  start of the tick is exactly `Position - Velocity * dt`.
- `CollisionSystem` (Resolution phase) does point-vs-circle checks:
  `|candidate - ammoPos|² < (ammoRadius + candidate.Radius)²`, picking the **closest candidate
  to the current position** per kind. A bullet moving faster than ~2× target radius per tick
  can tunnel straight through.
- `SpatialGrid.GetQueryItems(position, radius)` is a point-radius cell query.
- `WorldRenderer.DrawAmmo` draws `DrawCircle(ammo.Radius)` — a point today.
- `TurretFiringSystem` lead-predicts moving targets by solving a quadratic with
  `WeaponStats.AmmoSpeed` — assumes a finite projectile speed.
- `GameSession.FixedDeltaTime = 1f / SpaceVorsApp.MaxFps` (private const).
- New weapons offered by upgrades come from `WeaponType.AddOnWeapons` — adding a `WeaponType`
  with an `AddOn` mount makes it appear in the pool automatically.
- Range upgrade multiplies **both** `turret.Range` and `Weapon.ShotLifetime`
  (`GameSession.ApplyUpgrade`).
- Fastest current weapon: RailGun at 900 px/s → 7.5 px/tick at 120 fps. Enemy ships ~15–30 px
  radius, so tunneling does not exist yet; it appears as soon as speeds go up an order of
  magnitude.

## Design decisions

### 1. All ammo becomes a swept capsule — no new component

Every ammo entity is treated as the **line segment** it swept during this tick:

```
segment = [Position - Velocity * dt, Position]        // exact, ammo velocity is constant
```

- `CollisionSystem.Update` already receives `deltaTime` (the fixed step) — compute the segment
  there. **No new component, no new system, no write-pattern change.**
- Hit test becomes **segment vs circle**: distance from the segment to the candidate center
  `< candidate.Radius + ammo.Radius`.
- `Ammo.Radius` is reinterpreted as **half-thickness** of the capsule. Existing values
  (2.5 / 3.75 / 5) keep working; a slow bullet's segment is short, so it behaves like today's
  circle. A wide, slow bullet is a fat capsule → grazing hits.
- **First-hit ordering**: among all candidates whose capsule distance passes, pick the one with
  the **smallest projection parameter `t` along the segment** (earliest along the direction of
  travel), tie-break by smaller distance, then by entity id for determinism. This replaces
  "closest to current position" and is what makes both no-tunneling and laser "first thing
  hit" fall out of the same code.
- Hit point for effects/impulses = the closest point on the segment to the target center.
- Enemy ammo benefits automatically (same loop, `IsEnemy` branch gets the same segment test
  against the player radius — fast enemy shots can no longer skip over the player).

Tradeoff: a bullet can now hit a target it passed *during* the tick (the hit resolves at the
crossing point, not at the bullet's drawn position). That is the intended, correct feel.

### 2. SpatialGrid gains a box query

Add `GetQueryItemsInBox(min, max, Span<SpatialItem>, out truncated)` — same buckets, same
`_seen` dedup, iterating the cell range of the segment's AABB expanded by the capsule radius.

- Normal bullets: segment ≤ ~50 px → 1–4 cells, same cost as today.
- Lasers (Stage 4) reuse it for a ~2000 px beam: worst case ~16×16 = 256 cell lookups per
  beam; lasers are slow-firing and few, so this is fine. If profiling ever says otherwise,
  replace with DDA cell walking (not worth the cleverness now).

### 3. Laser = new `LaserBeam` component, not a super-fast bullet

```csharp
public readonly record struct LaserBeam(
    Vector2 Start, Vector2 Direction, float Length, float Width,
    int Damage, bool IsEnemy, AmmoColor Color,
    float Lifetime, float InitialLifetime,
    bool DamagePending = true);
```

Considered and rejected: *laser as ammo with `speed = range / dt`*. It reuses everything, but
(a) hard-couples weapon data to the fixed timestep, (b) the Range upgrade multiplies
`ShotLifetime`, which would give the beam extra damage ticks, and (c) `speedVariation` in
`FireAtTarget` would randomly change beam length. Explicit is better.

- **Firing**: `TurretFiringSystem` spawns one `LaserBeam` entity at the muzzle
  (`Start = spawnPos`, `Direction = pelletDir`, `Length = turret.Range`, `Lifetime ≈ 0.12 s`
  for the visual flash). No velocity, no movement — the beam is static and instant.
- **Damage**: a small **beam pass inside `CollisionSystem`** (after the ammo pass, same tick,
  Resolution phase): for each beam with `DamagePending`, box-query along the beam, find the
  first hit (smallest `t`), apply damage through the existing per-frame health accumulator +
  death flow, spawn the hit effect at the hit point, then write the beam back with
  `DamagePending = false`. One beam = at most one damage application, ever.
  - Asteroids and mines block the beam like they block bullets (asteroids have no Health —
    the beam just stops with an impact effect; mines take damage via the existing mine path).
  - Putting this in `CollisionSystem` (not a new system) is deliberate: the grid, the health
    accumulator, the death/loot/spark flow all live there; duplicating them in a second
    system is worse than ~60 lines in an already-large file. If the file grows uncomfortably,
    split it as `CollisionSystem.cs` + `CollisionSystem.Beams.cs` (partial class) — same type,
    smaller files.
- **Lifetime/fade**: `EffectSystem` ticks `Lifetime` like it already does for sparks/explosions.
- **Renderer**: `WorldRenderer` draws the beam as a thick line (`Width`), alpha scaled by
  `Lifetime / InitialLifetime`.

### 4. Hitscan targeting

`WeaponStats` gains `bool Hitscan = false`. When true, `TurretFiringSystem`:

- aims at the target's **current position** (skip lead prediction — `travelTime ≈ 0` makes the
  quadratic pointless),
- uses `turret.Range` as both engagement range and beam length.

ProjectileSpeed upgrade on a hitscan weapon is a harmless no-op (multiplies an unused
`AmmoSpeed`); Range upgrade naturally lengthens the beam.

### 5. Renderer draws ammo as lines

`DrawAmmo` draws `DrawLineEx(prev → pos, thickness = 2 * ammo.Radius, color)` where
`prev = pos - ammo.Velocity * dt`.

- `GameSession.FixedDeltaTime` becomes `public const` (or is passed into `Renderer.Draw`) so
  the renderer computes the same segment the collision used — one source of truth, no new
  component data.
- Slow bullets: a 2–4 px line with round-ish visual weight ≈ today's dot. Fast bullets and
  particle shots read as streaks for free.
- Culling: skip only if **both** endpoints are off-screen (bounding box of the segment).

### 6. Particle accelerator = pure stats, no new code

A particle accelerator is just a `WeaponType` with high `AmmoSpeed` (e.g. 3500–5000 px/s →
30–40 px/tick streak), small `AmmoRadius` (1.5), low damage, high fire rate. Stages 1–2 give
it streaks, no-tunneling and grazing for free.

## New weapons

Spec'd ones first; the rest are proposals that exploit sweep + width + hitscan. All are
`WeaponType` entries with an `AddOn` mount so they enter the upgrade pool automatically.

| Weapon | Kind | Stats sketch | Why it's interesting |
|---|---|---|---|
| **Laser Lance** | laser | Hitscan, Range 1400, Width 3, Damage 12, FireRate 0.8, blue | The spec'd laser: instant long-range sniper beam, first hit only — pick targets out of a swarm, cannot poke through a tank to the fragile thing behind it. |
| **Cyclotron** | particle accelerator | AmmoSpeed 4200, Radius 1.5, Damage 1, FireRate 12, cyan-ish (Blue) | The spec'd accelerator: a machine-gun of streaks; visible tracers make fast, leading shots satisfying. |
| **Prism Lance** | laser ×3 | Hitscan, 3 parallel beams offset ±14 px perpendicular, Damage 5 each, FireRate 0.6 | Three-lane hitscan: a column of enemies gets one beam each per volley; strafing to line enemies up becomes a skill. |
| **Flak Curtain** | wide bullet | AmmoSpeed 160, Radius 9, Damage 1, FireRate 10, Scatter 0.5, Lifetime 0.9 | A slow wall of fat discs around the ship. Grazing width does the aiming — shreds swarms that get close, punishes parking far away. |
| **Graviton Burst** | particle accelerator | AmmoSpeed 3000, Radius 4, Damage 3, PelletCount 5, Scatter 0, FireRate 1.2 | Five fat fast particles down one line with speed jitter: the streaks visually merge into one thick beam that "draws in" across the screen; each pellet stops on its own first hit, so it punches a short tunnel through a formation. |
| **Ripple Cutter** | wide laser | Hitscan, Length 260, Width 16, Damage 6, FireRate 1.5 | A short, very wide slab — anti-swarm panic button; first-hit-only means it never clears what's behind the front rank. |

Also free: bump **RailGun** `AmmoSpeed` 900 → ~2500 so it reads as a proper railgun streak
(same bullet, no code).

## Stages (one commit each)

1. **SpatialGrid box query** + unit tests (bounds, dedup, truncation flag).
2. **Swept capsule ammo collision**: segment-vs-circle test, earliest-hit-along-segment
   selection, hit point from segment; keep impulse/mine/player branches behavior-identical
   with the new hit point. Tests: no-tunneling (6000 px/s bullet through a 12 px target hits),
   grazing (offset miss within width hits), earliest-of-two targets, slow bullet behaves as
   before.
3. **Renderer**: ammo as thick lines + segment culling; expose `FixedDeltaTime`. Verify with
   headless screenshots (recipe in TROUBLE_SHOOTING.md) that slow bullets still look like dots.
4. **Laser**: `LaserBeam` component, `WeaponStats.Hitscan`, firing in `TurretFiringSystem`,
   beam pass in `CollisionSystem`, fade in `EffectSystem`, renderer. Tests: first-hit-only,
   no second-tick damage, asteroid blocks beam, hitscan aim (no lead).
5. **Weapons**: Laser Lance + Cyclotron `WeaponType`s (+ loadouts), RailGun speed bump.
   Screenshot pass for streak look.
6. **(Optional, separate commits)**: Prism Lance, Flak Curtain, Graviton Burst, Ripple Cutter.

## Testing & performance

- Pure-logic tests in `src/Tests` (no graphics): segment-circle helper, tunneling, grazing,
  first-hit ordering, laser once-only. Determinism: `WorldRngTest` must stay green (no new
  RNG sources; beam pass consumes none).
- Re-run `Tests/PerformanceBenchmark.cs`: the ammo scenarios (~15k bullets) now run box
  queries instead of point queries — expect near-identical cost for current speeds; record
  numbers. `CollisionSystem` is the known bottleneck; do not pre-optimize, measure.
- No new `yield return` in hot paths; segment math stays allocation-free (locals + stackalloc
  buffer as today).

## Risks / tradeoffs

- **Feel change**: shots now connect with where a target *was crossed* this tick; in dense
  fast fights a few shots that "looked like misses" will now hit. Intended, but visible.
- **CollisionSystem size**: adding the beam pass grows an already-large file; partial-class
  split is the sanctioned escape hatch.
- **Query width cost**: box queries are ≤ point queries + a few cells today; only long beams
  pay more, and they are rare by design (slow fire rate).
- **Line rendering**: `DrawLineEx` has flat caps; very thick short lines (Flak Curtain) may
  look square-ish — acceptable, tune radius/width by screenshot.
