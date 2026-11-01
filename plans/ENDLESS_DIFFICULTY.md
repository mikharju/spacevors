# ENDLESS_DIFFICULTY.md

Status: proposed (2026-11-01) — planning only, nothing implemented here.

Plan for keeping the difficulty curve climbing past the current wall, and for closing the escape valve
that lets speed undo everything the ladder does. Ordered by cost, cheapest first; each item states the
problem it solves, what it can break, and the assertion that has to land with it.

## Why now

The curve stops escalating on three clocks, all of them deliberate:

| Axis | Where it stops | When |
|---|---|---|
| `EnemyShipStats.MaxTier = 10` | hp, damage, fire rate, range, speed, ammo, turn rate | 10 min |
| `WaveStats.WaveMaxSize = 40` | wave quota (6 + 2/wave) | wave 18 ≈ 16.4 min |
| `EnemyShipStats.MaxCompositionTier = 20` | who appears and the missile in-flight cap (7) | 20 min |

Lull lengths are fixed forever (`QuietGap = 9 s`, `FullLull = 15 s`), `MaxEnemyShips = 100` and
`MaxMines = 23` are static, and past 20 minutes nothing in the world changes.

The bigger hole is speed. Player velocity is unbounded — `PhysicsSystem` integrates `v += a·dt` with no
drag and no clamp, forward thrust is 400 px/s² (`EngineLayout.Balanced`) times `Boost = 2.5`, and
`ForwardAcceleration` compounds ×1.1 per card. Above **~290 px/s** nothing can follow you (the chase
ceiling is `2 × base`; the fastest tier-10 hull is Streak at 145 → 290), above **300 px/s** the spawn
factor is capped, and `Chase.CullDistance = 5500` then deletes the fleet behind you in a couple of
seconds. Everything this file proposes is priced against that.

**Decided against** (do not re-litigate without new evidence):

- **A player top speed or drag.** It changes core feel and punishes progression
  (plans/DIFFICULTY_SCALING.md "What NOT to do"), and it cannot work as balance anyway: any cap high
  enough to be unreachable is far above the ~290 px/s escape threshold, so the escape survives.
- **Raising `MaxTier` past ~11.** D6 — a tier-*t* HeavyCannon must die inside 7 s of a starter Scout's
  17 DPS — allows `30 × (1 + 0.25t) / 17 ≤ 7`, i.e. **t ≤ 11.9**. Raising the cap breaks
  `EnemyDifficultyTest.ToughestTierHullDiesInsideTheTtkBudget` on the first commit.
- **Pellets/scatter/concurrency as the first move.** Those are the entity-costing axes; they get spent
  last, after measurement.
- **Difficulty selection (0.75 / 1 / 1.3).** Three copies of every balance table, asserted three times.

## Rules every item has to keep

1. **A stated ceiling per axis, asserted in the `EnemyDifficultyTest` style.** The mine respawn ramp ran
   backwards for exactly this reason (plans/DIFFICULTY_SCALING.md follow-up 2026-11-01): it was the one
   live ramp with no assertion on its direction.
2. **Determinism.** Derive everything from world time or a world accumulator; no new rng source, and keep
   the rng draw *count* per spawn unchanged so seeded traces stay comparable (`WorldRngTest`).
3. **No mutation of spawned entities.** Tier is applied at spawn; nothing rewrites an entity later
   (plans/DIFFICULTY_SCALING.md R2.11).
4. **Measure per item**: `.tools/scenario.sh ammo-spray --sim --iters 3` for the cheap cases,
   `impact-storm` for anything touching hulls/entities, `missile-384`/`missile-1024` for the missile axes,
   and the `[THREAT]` row (`ships`, `in`, `out`, `tier=T/C`, `missiles=/cap`, `hp`) for the survivability
   band. One axis per commit.

## P1 — Behaviour instead of numbers (cheapest, do first)

Escalation with no stat creep at all, aimed at the actual failure mode ("they park at the ring edge, I
kite, shoot, they die").

- **Orbit / strafe at the firing ring** instead of parking: a moving firing line has to be chased and
  prediction-shot.
- **Weapons-hot delay after spawn** (fairness purchase the plan already asks for when reach grows).
- **Rear and flanking spawns at high composition tier**, and band tightening with tier — never inside
  2× the ring, so nothing spawns able to shoot you the frame it appears.

Cost: chase-AI logic, no entities, no ceilings to re-price (the ring itself is unchanged, so D1 holds).
Assertion to add: an orbiting hull's distance from the player stays inside `[FiringRange, ringCeiling]`,
so orbit cannot smuggle a ship past the D1 reach ceiling. Determinism: steering from current state only.

## P2 — Escalate on distance travelled, not only on elapsed time

The one change that closes the speed escape: advance the difficulty clock on
`max(elapsedTime, distanceTravelled ÷ ReferenceSpeed)`. A player who covers 3× the ground earns 3× the
progression of a player who idles, so speed stops being an escape and the "20 minute wall" becomes
"20 minutes of *play*".

- Deterministic: an accumulator over `Velocity × dt`, seeded like everything else.
- **This breaks a documented invariant** — ARCHITECTURE.md and plans/DIFFICULTY_SCALING.md both say the
  difficulty is derived from world elapsed time. ARCHITECTURE.md must be updated in the same commit, and
  `WaveSchedule`/`TierFor`/`CompositionTierFor` need the new clock passed in rather than reaching for
  `view.ElapsedTime`.
- Risk: two clocks drifting apart (a run that idles and a run that flies get different worlds). That is
  the point, but the wave clock and the stat clock must use the *same* escalated value or the lull
  pattern desyncs from the tier.

## P3 — Duty cycle instead of peak size

Keep `WaveMaxSize = 40` and make the run busier by tightening the rhythm: `FullLull` 15 → 10 s and
`QuietGap` 9 → 6 s over the wave index, or a third wave inside the pair past a given index. Peak entity
count never rises, so `MaxEnemyShips` and the 10k-object goal stay untouched, while the share of the run
under pressure climbs.

Mitigation: always keep at least one full lull per pair — the loot window is the other half of the design
(plans/WAVES_AND_BREATHERS.md W3, orbs last 90 s) and a run with no lull is not this game.
Assertion: extend `WaveScheduleTest` — the lull floor, the pair shape, and that wave size stays capped.

## P4 — Elites (R2.6 item 8)

A share of hulls at high composition tier with +hp/+radius and a visible ring. Difficulty you can *read*,
and it spends the hp budget on a few ships instead of the whole fleet, so D6 stays assertable per elite
class (elite hp ceiling chosen so an elite still dies inside the TTK budget).

Cost: one marker component + a ring in the renderer + one more row in the existing weighted composition
draw (no new rng source). Cap the share (e.g. ≤ 20 % at composition tier 20) so it cannot become a
de-facto `MaxTier` increase.

## P5 — Reward-side decay (free, no sim cost)

`LootStats.HealthOrbChance` 0.05 → 0.03 late, and slower `Hp` card cadence. This is the honest answer to
"enemy damage is already 6 against a base-8 Scout" (plans/DIFFICULTY_SCALING.md R2.1) — pressure without
one more enemy stat. Pair it with the `[THREAT]` `hp` column so the survivability floor is measured, not
assumed, and never stack it in the same commit as a mine-supply change (both move the same healing
economy).

## P6 — New mine kinds, not bigger mines

Today's change drifts the mine *size* mix (0.5 → 0.7 large by the tier cap) while hp stays fixed at 2 for
both sizes: scarcer, bigger, worth more, never a clearing chore. The real threat step is new kinds, which
PLAN.md already lists: a mine that turns into a missile on approach, and a mine that fires one delayed
beam after lock-on. Both add behaviour, not hp, and both stay a two-shot pop.

## P7 — Entity-costing axes, spent last, measured first

- **Pellet volleys** (`WeaponPelletCount` 1 → 2 on HeavyCannon at composition tier ≥5): cheap to write,
  paid in entities, and the enemy round budget is already asserted in `EnemyDifficultyTest`
  (Σ fire rate × pellets × `MaxEnemyShips`). Price it against `impact-storm` before landing.
- **Concurrency**: `MaxEnemyShips` 100 → 120–140. R2.6 item 4. Only after the `[THREAT]` row shows
  `in` staying under the survivable band with the cap raised.
- **Missile cap past 7**: the world-wide in-flight cap is the safety valve and gunfire shoots rounds
  down, but `MaxMissileFiringRange = 1450` must stay under the player's longest gun (1700).

## Known limit found while moving P3 onto the quota (2026-11-01)

Wave quota now scales with player speed (`EnemyShipSpawnSystem.WaveQuota`, 1× → 3×), but quota is not the
only thing that bounds how many ships arrive: spawns go into the ±45° cone ahead of the player — which is
one screen edge — and `SpawningStats.EnemyShips.MinSpawnDistance = 300` keeps ships apart. A world frozen
in place (the unit-test case) caps a wave at ~5 arrivals on a 1920×1024 screen regardless of quota. In a
real run the ring travels with the player so this is a rate limit rather than a wall, but **measure before
touching it**: the knobs are `MinSpawnDistance`, the cone half-angle, and spawning on a ring further out
than the screen edge. Do not raise the quota again until a `[THREAT]` run shows the quota actually going
unspent.

## Verification, in order

1. Add the axis's ceiling assertion first (it fails), then the change.
2. `.tools/scenario.sh <scenario> --sim --iters 3`; compare median/p99 against `.tools/run/index.tsv`.
3. One headless play-through per change with the `[THREAT]` row read at the same elapsed times (5, 10,
   15, 20, 30 min) — the `hp` walk is the only survivability instrument that exists.
4. Append a dated `## Shipped` / `## Follow-up` note here; never rewrite the sections above.
