# IMPACT_DAMAGE.md

Proposal: impact damage rules for ship↔ship and ship↔asteroid collisions (2026-09-16).

## Status: done (2026-09-16)

Implemented in one commit (`ImpactDamage.cs` + wiring in `ResolveCollision` + 15 tests).
Deviations from the plan's calibration table: the table's "player" rows used the
Fighter hull (r=58, mass 10568 → ram costs 5); the smaller default test hull (r=18)
absorbs more of a hit and takes 3 — mass split working as designed. The pebble-ram
row reads 0 (not ~1) with the shipped constants. Feel tuning (constants at the top
of `ImpactDamage.cs`) still wants a playtest session.

## Requirements (from the request)

- Low-speed impacts: no damage at all.
- Medium speed: minor damage. High speed: rail-gun-class damage.
- Damage from **relative** (closing) speed — two ships flying side by side at 400 px/s
  with 10 px/s difference must feel like a gentle bump, not a crash.
- High-damage hits produce sparks.
- Feel realistic, not physically exact. Performance is a top priority.

## Current state (verified in code)

- `CollisionSystem.ResolveCollision` handles **all** body pairs (asteroid↔player,
  enemy↔player, enemy↔asteroid, enemy↔enemy, asteroid↔asteroid, mine↔ship/asteroid)
  with impulse resolution — and **already computes exactly what impact damage needs**:
  contact `normal`, masses (`EnemyShipMass = 3000` override for ships, `π·r²` otherwise),
  relative velocity, and `velAlongNormal` (the closing speed along the normal; negative
  = approaching). No new queries, components, or systems are required.
- Collisions currently deal **no** damage; only ammo, mines (`MineStats.ContactDamage = 3`)
  and turrets hurt anything.
- Health bookkeeping already has the right accumulator: `_frameRemainingHealth` merges
  multiple same-tick hits and feeds one death pass (player → `Dead`; enemy ships →
  loot/explosion/sparks via `_shipDeathData`).
- `SpawnSpark(commands, pos, view.Rng)` exists (used by mine hits and ship deaths).
- Scale reference: player hp 8–20, enemy hp 15–30, mine contact 3, RailGun = 15 damage
  at 900 px/s, player max speed 400 px/s, enemy speeds 50–90 px/s, asteroids drift slow.
  Asteroids have **no** `Health` (indestructible decoration).

## Proposed rule (absorbed-energy model)

Per collision, using values `ResolveCollision` already has:

```
v = |velAlongNormal|                       // closing speed, normal component only
if (v < MinClosingSpeed) → no damage       // covers side-by-side flight and swarm jostling

E   = 0.5 · μ · v²                          // kinetic energy available at the impact,
                                            // μ = mA·mB/(mA+mB) (reduced mass)
damage(body) = ImpactScale · E · invMass(body)/totalInvMass   // lighter body absorbs more
if damage < 0.5 → 0, else round up; clamp to MaxImpactDamage
```

- **Relative speed for free**: `velAlongNormal` is the closing velocity — same-direction
  ships with small Δv get v ≈ 0 and fall under the threshold automatically.
- **Normal component only**: grazing scrapes deal no damage (no grind-damage while
  sliding along an asteroid); the tangential part is already handled by friction.
- **Quadratic in v** = the "rail-gun" feel: twice the closing speed, four times the
  damage; mass split makes a pebble bouncing off a Heavy do nothing while the same
  pebble at ramming speed wrecks an Interceptor.
- **Damage only on approach**: the existing `velAlongNormal > 0 → return` guard means
  resting/pushed contact (velAlongNormal ≈ 0 after the bounce) never repeats damage —
  no per-pair cooldown state needed.

### Constants (starting values, tune by feel)

| Constant | Value | Meaning |
|---|---|---|
| `MinClosingSpeed` | 80 px/s | below: zero damage (enemy chase speeds are ≤ 90, closing speeds in jostling are far below) |
| `ImpactScale` | 4.6e-8 | damage per unit of absorbed energy — calibrated by the table below |
| `MaxImpactDamage` | 15 | per hit per body — a RailGun shot, the "nothing worse than a rail-gun hit" cap |
| `ImpactSparkDamage` | 3 | a body taking ≥ this gets a spark burst at the contact point |

### Calibration table (what a human should feel)

| Scenario | v (px/s) | Damage |
|---|---|---|
| Two enemies jostling while chasing | ~30 | 0 |
| Enemy drifts into an asteroid while patrolling | ~60 | 0 |
| Player gently backs into an asteroid | ~85 | 0–1 |
| Interceptor (3000) rams player head-on (490) | 490 | player ~3, interceptor ~10 |
| Player full-ram into a large asteroid (400) | 400 | player ~4 |
| Player full-ram into a small pebble | 400 | player ~1 (pebble absorbs the hit) |
| Player flung at 800 px/s by an explosion into an asteroid | 800 | ~15 → lethal |

### Sparks

One burst per collision (not per body) at the contact point
(`aPos + normal · aRadius`), when either body's damage ≥ `ImpactSparkDamage`:
`2 + damage/3` sparks (cap 6) through the existing `SpawnSpark`. Heavy hits also read
through the already-shipped hull damage shading and smoke as hp drops.

### Who takes damage

- **Player and enemy ships** (anything with `Health`), both sides of a ship↔ship hit.
- **Asteroids: none** — they have no `Health` and stay indestructible decoration.
- **Mines: excluded** — they already have their own contact rule (fixed damage + self
  destruct in `ResolveMineVsPlayer`); stacking impact rules on top would double-charge.

### Death integration

Impact damage writes into `_frameRemainingHealth`, so the existing single death pass
just works — with one addition: an impact that kills an enemy ship must register
`_shipDeathData` (hit point, pos, radius, graphicsId — all available at the call site)
so impact kills loot/explode identically to gun kills.

## Performance (top priority — why this is near-free)

- **Hot path cost**: one compare against `MinClosingSpeed` per resolved collision
  (already inside code that computes `velAlongNormal`); the damage branch (≈10 flops +
  two dictionary writes) runs only for rare fast hits. No new grid queries, components,
  systems, or allocations.
- **Particles**: sparks spawn only above the damage threshold — same budget pattern as
  existing mine-hit sparks; the 1 s lifetime + existing renderer cap them.
- Verify with the existing per-system `DiagnosticLogger` timings and
  `PerformanceBenchmark` (swarm scenarios) — expect no measurable change.

## Determinism

- Damage is a pure function of state (velocities, radii, masses) — no RNG.
- Spark RNG uses `view.Rng` in collision iteration order, the same established pattern
  as existing mine/ship-death sparks in this system; `WorldRngTest` must stay green.

## Rejected alternatives

- **Absolute speed instead of closing speed**: violates the side-by-side requirement
  outright.
- **Include tangential speed in damage**: sliding along a surface would grind damage;
  physically wrong (normal impulse causes the dent).
- **Per-pair impact cooldown**: unnecessary state — the approach-only guard already
  prevents repeat damage during sustained contact.
- **Damage asteroids too**: needs a new `Health` + destruction path on decoration
  objects; out of scope, can be revisited later.
- **Linear damage ramp**: cheaper but loses the quadratic "rail-gun" escalation the
  request explicitly asks for; the quadratic form costs nothing extra here.

## Stages (when approved)

1. **Pure rule + tests**: static `ImpactDamage` helper (closing speed + masses →
   per-body damage) with the constants above; unit tests: below-threshold → 0,
   same-direction low-Δv → 0, quadratic scaling, mass split (light body absorbs more),
   cap, rounding floor.
2. **Wire into `ResolveCollision`**: damage accumulation into `_frameRemainingHealth`,
   `_shipDeathData` on impact kills, spark burst at contact point; tests: ram kills an
   enemy (loot + explosion path), jostling swarm takes zero damage, player death by ram,
   spark commands spawned only above threshold.
3. **Tune + docs**: playtest feel (ram a few asteroids), adjust the table constants,
   PLAN.md/ARCHITECTURE.md updates.
