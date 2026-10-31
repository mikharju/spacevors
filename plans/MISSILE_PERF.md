# MISSILE_PERF.md — missile-carrying perf scenarios

Status: shipped (2026-10-31)

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

Two scenarios, one per missile count (decisions below). Both measure 120 ticks.

- `missile-384` — 384 guided rounds live, mixed on purpose: **half enemy-guided** rounds
  flying through a seeded `ammo-spray`-scale player round stream (player guns shoot them
  down), and **half player-guided** rounds locked on seeded enemy hulls that carry their
  normal guns and racks (enemy fire shoots those down). So both PD directions run, and
  guidance runs for the full count. Racks stay silent: everything is seeded already in
  flight, like `ammo-spray` does with bullets.
- `missile-1024` — same composition, 1024 rounds.

How the numbers are read:

- `missile-X` vs the same-seed `ammo-spray` twin → **the missile price** at that load.
- `missile-1024` minus `missile-384`, divided by 640 → **marginal per-missile ns**
  (guidance + grid + PD), the ladder point without a ladder of runs.

Detonations and shot-downs happen inside the window and are reported as count ranges
(`missiles=n/cap`, effects), not held constant.

Keep the existing unarmed scenarios untouched; these are additions, not edits.

## Prerequisites in Domain/Scenarios (each a small commit)

1. **`SeedMissilesInFlight(count, spread, aimAt)`** — builds exactly what the firing systems
   build: `Position` + `Velocity` + `Ammo` + `Missile(LockedTarget, Speed, MaxSpeed,
   Acceleration, TurnRate, Steering)` + `Health`. Speeds from the real `WeaponStats` rules:
   a seeded round starts mid-build toward card speed; guided speed is never rolled (ARCHITECTURE
   invariant), so no rng draw there. Locks point at seeded hulls. Reuse the bullet guard
   pattern: resample until clear of every hull/asteroid, and randomize fuse ages so detonations
   do not stack on one tick.
2. **Armed-hull variant** — extend `AddUnarmedHull` with an opt-in hull that carries its
   normal guns (so enemy fire can shoot down player-guided rounds) and, for enemy racks,
   `MissileWeapon` + `MissileCooldown` (default stays unarmed so `hull-stream` /
   `hull-crowding` stay byte-identical).
3. **Scenario specs + budgets + count ranges** (`missiles=N/cap` range in `frames.jsonl` is
   already reported — reuse it), baselines, `.tools/scenario.sh` names.

## Determinism contract

- Same seed → same missile states per tick; lock choice and fuse ages draw from `em.Rng`.
- Never assert timing columns (TROUBLE_SHOOTING #28); ladder numbers are records, budgets loose.
- Pinned run recipe: `.tools/scenario.sh <name> --sim --iters 3`, then one real-window run
  per scenario for the render pass.

## Acceptance when shipped

- Table in the Shipped section: median/p99 sim ms and **per-missile ns** for both counts,
  plus each scenario's delta against its same-seed `ammo-spray` twin — that delta *is* the
  missile price.
- Verdict line for PLAN.md: keep/optimize/revert posture for missiles before parallel sim.
- Add dated `## Follow-up:` pointer lines to plans/MISSILES.md (gap closed) and
  plans/PARALLEL_SIM.md (gate world exists).
- Update the PLAN.md roadmap line to shipped and this file's header.

## Decisions (user, 2026-10-31)

1. Two scenarios, fixed loads of **384** and **1024** missiles (no ladder of 16/64/256;
   the 384→1024 delta gives the marginal per-missile number).
2. Measured window: the default **120 ticks** to start; a longer volley-cycle window is a
   later follow-up only if the 120-tick numbers say so.
3. **Include the player-missile PD variant**: each scenario seeds guided rounds on both
   sides, so player guns PD enemy missiles and enemy guns PD player missiles.

## Shipped (2026-10-31)

Landed as planned: `missile-384` and `missile-1024` scenarios, 120 ticks, both PD directions,
silent racks. New seams: `ScenarioSpec.MissileCount`, `ScenarioWorld.SeedMissileWorld` (armed
tier-10 hulls via the now-public `EnemyShipFactory.BuildTurret`, SeekerPod rounds on the player
side, HeavyCannon-rack rounds on the enemy side), and a `missiles=` column on every `[PERF]`
row. `ScenarioWorldTest` pins exact counts, both factions, live locks, armed hulls, no racks.

Numbers (`--sim --iters 3`, host=sim, this host; medians):

| world | median | p99 | alloc KB/frame | missiles mean→end |
|---|---|---|---|---|
| ammo-spray (twin) | 1.33-1.35 ms | 2.24-2.26 | ~296 | — |
| missile-384 | 1.79 ms | 4.59 | 312 | 374 → 165 |
| missile-1024 | 3.02 ms | 6.72 | 343.5 | 984 → 459 |

- Missile price at play-relevant load: **+0.44 ms for 384 missiles (+34% of ammo-spray)**
  ≈ 1.2 µs per missile per tick. Real play carries single-digit missiles, so its sim cost is
  microseconds — the answer to plans/MISSILES.md's open question is "free at real counts".
- Marginal cost 384→1024: (3.02−1.79)/640 ≈ **1.9 µs per missile** — mildly super-linear
  (more PD hits and effect spawns per tick as density rises), still 45% inside the 8.33 ms
  sim budget at 1024, which is ~100× the live missile count play can produce.
- Counts decay hard (PD works), so the means sit well below the seeded counts — perEntity
  style numbers should divide by the mean, not the seed.
- Render (one llvmpipe window run, relative only per TROUBLE_SHOOTING #25): `world.missiles`
  is 1.7 ms mean of a llvmpipe render dominated by the `hud.health` software-rasteriser
  artifact — the missiles pass is small against `world.sparks`. Absolute render numbers need
  a real-GPU run: `update-baseline-perf-stats.sh`-style window runs will create
  `.tools/baselines/missile-*.json` on user hardware; no baselines were written from the
  sandbox.
- Ceilings added to `ScenarioPerformanceTest`: 5.0/15.0 and 8.0/24.0 ms (desktop/CI),
  450/500 KB per tick; all 18 perf assertions pass.
- Verdict for PLAN.md: **keep** — guidance, cross-fire PD and detonation are linear-ish and
  cheap at play counts; the pre-condition for parallel-sim gate decisions now exists.

## Follow-up (2026-10-31) — a real-GPU missile-384 run looked slow: it was the HUD, not the missiles

User's window baseline came back at **183.8 / 184.8 ms** median. The render-pass split localised
it in one line: `hud.health` was **181 ms — 96-99% of the whole frame** (its real-GPU baseline is
0.043 ms), while `world.missiles` was under 0.3 ms and sim sat at 1.71 ms.

Cause: `HudRenderer.DrawHealthBar` drew **one rounded pip per hp point** — six raylib primitives
each — and `SeedMissileWorld` sets the player's hp to 40,000 so detonations can't end the window.
40,000 × 6 ≈ 240,000 draw primitives per frame in the HUD. The same 40,000-hp worlds
(`hull-crowding` included) would have shown it the moment anyone ran them in a window.

Fix: the bar is now a fixed-length track with a proportional fill, eased and still green→amber→red,
plus the exact numbers beside it — constant cost for any hp, and ~10 lines shorter than the pip
loop. Verified by a re-run: **frame median 183.8 → 20.3 ms**, and `hud.health` is gone from the
render passes entirely.

Decision recorded: the missile counts stay at 384 / 1024. There is nothing to optimise — at
play-relevant counts missiles cost microseconds, and the only scaling note is that the 384→1024
superlinearity lives in the *effects spawned by point-defence kills*, not in guidance. If that ever
matters the lever is an effects-per-tick budget, not the missile code.

Baselines for the missile scenarios still need writing on real hardware
(`.tools/scenario.sh missile-384 --iters 5 --check-baseline --update-baseline`); the sandbox runs
are llvmpipe and must not become baselines (TROUBLE_SHOOTING #25/#31).

## Follow-up (2026-10-31) — real-GPU baselines recorded

Baselines now exist for both scenarios on user hardware (`--iters 5`): missile-384 frame median
**3.59 ms** (ammo-spray twin 3.01, so the missile price is **+0.57 ms ≈ 2.2 µs per live missile**,
of which `world.missiles` render is only 0.055 ms); missile-1024 **5.11 ms**, marginal
384→1024 ≈ 3.2 µs/missile, superlinear again from PD-kill effects, not guidance. `hud.health`
0.01 ms everywhere confirms the HUD fix. Only watch item: at 1024 (≈100× play counts) the p99
tail (10.3 ms) crosses the 8.33 ms budget on 22/592 frames; irrelevant at play counts, lever is
an effects budget if missile-heavy content ever ships.
