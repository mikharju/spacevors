# MISSILE_PERF.md — missile-carrying perf scenarios

Status: proposed (2026-10-31). Plan only; no code changed.

## Why

The known measurement gap: every pinned scenario runs **unarmed by construction**
(`ScenarioWorld.AddUnarmedHull` gives no `Turret` and no `MissileWeapon`), so no scenario
has ever measured the missile systems. plans/MISSILES.md ends with exactly this note, and
PLAN.md's roadmap prices missiles **before any parallel-sim decision** (plans/PARALLEL_SIM.md
gates need a loaded world to A/B against).

What a missile adds to a tick, to be priced:

1. `MissileGuidanceSystem` per round: heading steer, speed build, rewrite of both `Velocity`
   and `Ammo.Velocity` (Intent, first system).
2. Collision grid: missiles are bodies with 1-3 hp; the cross-fire rule makes queries
   asymmetric (only opposing-side rounds/beams hit them).
3. Detonation: missile-vs-hull contact → warhead + effects (vs shot-down → smoke breakup).
4. `EnemyMissileFiringSystem` / `TurretFiringSystem` racks: launch gating, fuse bookkeeping.
5. Render: the `world.missiles` pass (dart + plume sprites) — visible only on a real-window
   run.

Reality check first: live missiles in real play are few (enemy guided cap 3-7 at high tiers;
player racks are limited by mounts). Expectation to verify, not assume: realistic worlds pay
almost nothing; the value is proving there is no super-linear surprise and putting a
per-missile-ns number on the record before missile-heavy content (bosses, elites, armament
ladders) arrives.

## Scenarios

Names final at implementation; roles fixed:

- `missile-barrage` — **realistic steady state**: Shadow with 2-3 armed racks firing at a
  ring of enemy hulls that carry tier-10 racks of their own; missiles live in both
  directions; launch/detonation cycles inside the measured window (measure longer than the
  default 120 ticks — a rack volley is cooldown-paced, see open question 2).
- `missile-ladder` — **headroom curve**: N silent guided rounds already in flight aimed at
  high-hp hulls, N = 16 / 64 / 256 / 1024, one spec parameterized like `scale-ladder`.
  `perEntity` gives per-missile guidance + collision ns; circle-reject and grid behaviour
  included.
- `missile-vs-ammo` — **point-defence interaction**: `ammo-spray`'s ~7k-round stream with
  enemy missiles crossing it. Prices the cross-fire pass, missile hp absorption and breakup
  smoke — the answer to "is shooting down missiles free?" in numbers.

Keep the existing unarmed scenarios untouched; these are additions, not edits.

## Prerequisites in Domain/Scenarios (each a small commit)

1. **`SeedMissilesInFlight(count, spread, aimAt)`** — builds exactly what the firing systems
   build: `Position` + `Velocity` + `Ammo` + `Missile(LockedTarget, Speed, MaxSpeed,
   Acceleration, TurnRate, Steering)` + `Health`. Speeds from the real `WeaponStats` rules:
   a seeded round starts mid-build toward card speed; guided speed is never rolled (ARCHITECTURE
   invariant), so no rng draw there. Locks point at seeded hulls. Reuse the bullet guard
   pattern: resample until clear of every hull/asteroid, and randomize fuse ages so detonations
   do not stack on one tick.
2. **Armed-hull variant** — extend `AddUnarmedHull` with an opt-in `MissileWeapon` +
   `MissileCooldown` on a hull (default stays unarmed so `hull-stream` / `hull-crowding` stay
   byte-identical).
3. **Scenario specs + budgets + count ranges** (`missiles=N/cap` range in `frames.jsonl` is
   already reported — reuse it), baselines, `.tools/scenario.sh` names.

## Determinism contract

- Same seed → same missile states per tick; lock choice and fuse ages draw from `em.Rng`.
- Never assert timing columns (TROUBLE_SHOOTING #28); ladder numbers are records, budgets loose.
- Pinned run recipe: `.tools/scenario.sh <name> --sim --iters 3`, then one real-window run
  per scenario for the render pass.

## Acceptance when shipped

- Table in the Shipped section: median/p99 sim ms and **per-missile ns** for each ladder rung,
  plus `missile-barrage` and `missile-vs-ammo` deltas against their unarmed twins
  (`baseline-idle`, `ammo-spray`) — that delta *is* the missile price.
- Verdict line for PLAN.md: keep/optimize/revert posture for missiles before parallel sim.
- Add dated `## Follow-up:` pointer lines to plans/MISSILES.md (gap closed) and
  plans/PARALLEL_SIM.md (gate world exists).
- Update the PLAN.md roadmap line to shipped and this file's header.

## Open questions for the user

1. Ladder ceiling: 1024 or 512 guided rounds? (1024 is far beyond play; it prices future
   boss/elite content and the grid's behaviour under many moving hp-bodies.)
2. `missile-barrage` measured window: 1200 ticks (one full rack volley cycle) at the cost of
   longer runs, or 120 ticks with racks seeded mid-cooldown?
3. Should `missile-vs-ammo` include a player-missile variant too, or is the enemy-missile
   PD case enough for now?
