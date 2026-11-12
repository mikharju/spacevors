# ENEMY_BEHAVIORS.md

Status: shipped (2026-11-08) — B1-B7 all landed; `## Shipped` below says what deviated and why.
`## Follow-up (2026-11-08)` re-tunes the spread after play: it holds a lane inside its ring, it starts at
2:00 instead of 8:00, and it is born from a wider arc.

Two parts: **Part 1 is the plan** — decisions and why, for a human to read in two minutes. **Part 2 is the
implementation memo** — mechanics, invariants, tests, measurement. A decision lives only in Part 1, a mechanic
only in Part 2, so neither goes stale against the other. Part 2 is not a scratch pad: it is what the
implementer (usually an LLM, per AGENTS.md) must not rediscover the hard way — the ceilings to preserve, the
tests to write, the things already tried and rejected.

Late-game **behavior** escalation for enemy hulls: where a hull holds itself, how it approaches, when it gives
up a firing solution. No new hull species, no stat creep, no new rng source. Supersedes the "Behaviour" bullet
in plans/DIFFICULTY_SCALING.md R2.6 item 10 and the P1 item in plans/ENDLESS_DIFFICULTY.md, which stay as the
problem statement.

# Part 1 — The plan

## The problem

Every hull behaves the same way forever: brake at `FiringRange` pointing at the player. The fleet arrives as
one blob on one bearing, and the player's fix is to kite, turn, and shoot parked ships.

## The insight that sets the direction

Player damage is delivered by **auto-lead** (`TurretFiringSystem.EvaluateMovingTarget`) with rounds at
400–4200 px/s and four hitscan beams. The player's *aim* cannot be fooled. Their **facing, mount arcs and area
guns can**:

- Hull mount arcs are partial — Scout covers 60° ahead and 120° at each flank, and ~60° of astern is covered
  by nothing. A hull holding a bearing the player is not facing must be flown at. That is the game's core verb.
- The area guns are priced on formations: PrismLance is documented as "the gun that hits a formation instead of
  one hull" (70 px beam spacing), FlakCurtain's disc is 9 px wide, RippleCutter's blade 16 px. A clumped swarm
  is free multi-hull DPS for exactly those guns.
- **A hull already blocks a round.** A bullet or a beam is consumed by the first body on its path, and `Pierce` is
  armour-pierce, not pass-through — so one hull standing between the player and another is real cover, for free
  (mechanic detail in Part 2). Guided rounds are no exception here: a locked round flies at whatever its priority
  or the player's click points it at and spends itself on the first body in between.

So the behavior budget goes into **where hulls are**, not into avoiding what's coming.

## Decisions

- **Spread, don't converge.** Mid and late hulls hold the bearing they arrived on instead of joining one chase
  line, keep a short repulsion from their neighbours, and strafe along the ring instead of parking (B1–B3). The
  swarm is deliberately the exception — see below.
- **Pursue where the player is going**, not where they are (B4). This closes the ~290 px/s escape valve without
  a speed cap and without touching the difficulty clock — the cheap half of plans/ENDLESS_DIFFICULTY.md P2.
- **Two behaviors that change what kind of enemy you're fighting.** A crippled hull does not run: it tucks
  **behind a healthier, bigger hull** and keeps shooting from there (B5). And a rammer brakes *into* the player
  instead of at the ring (B6) — `ImpactDamage` already exists and is quadratic in closing speed. The rammer is in,
  not a maybe: if it reads as a resource drain, tune its share and its contact damage.
- **One cadence change**: heavies burst behind a visible wind-up tell instead of dribbling (B7). Denser *and*
  more readable at the same time, at unchanged rounds/s. A telegraphed wall is fair; an even drizzle is noise.
- **No new ships.** Behaviors spend the existing wave quota only — `MaxEnemyShips = 100`, wave size, the in-wave
  interval and the ±45° spawn cone all stay as they are. Current density is playtested good; the difficulty here
  comes from where hulls stand, never from how many arrive (plans/ENDLESS_DIFFICULTY.md P7 concurrency stays
  unspent).
- **Behaviors are per-hull, never fleet-wide.** Three gates: which hull role may carry one, which composition
  tier unlocks it, and what share of that hull's spawns does it — a flag riding the existing weighted draw, so
  hull × armament × behavior is still one rng draw. The stat ladder (`MaxTier = 10`) stays frozen, and a fleet
  at the top of the ladder still contains plain-chase hulls (per-hull table in Part 2).
- **The swarm stays clumped for the whole run.** A tight ball of small hulls is exactly what PrismLance (70 px
  beam spacing), FlakCurtain's 9 px disc and RippleCutter's 16 px blade are *paid for*; spread them all and those
  guns become dead weight in a loadout the player already spent levels on. At most a stated **minority** of the
  swarm's spawns (≤ 20 %) ever holds a spread bearing — never the majority, never the whole ball. Spacing (B2)
  goes on the mid hulls instead, where the knot is the thing the player cannot pick apart.
- **Cripple behavior is cover, not flight**, and only for hulls with hp to spare (Carmine, HeavyCannon — a 20 hp
  dart has no cripple state worth saving), ≤ 25 % of live hulls at a time. Cover **redistributes** the player's
  damage onto the biggest hull rather than reducing it, so it buys tempo and positioning and not a DPS tax; a
  cripple with no cover in range holds the far edge of its own ring instead of running.
- **A fleet the player cannot bring to bear is a wall, not difficulty.** At most 25 % of live hulls may hold a
  bearing outside the player's arc coverage at once.
- **One axis per commit, measured** — see Part 2 for the scenarios and the `[THREAT]` columns to read.

## Decided against — do not re-litigate without new evidence

- **Reactive dodging of incoming fire, in any form (including "weave to break a missile lock").** Lead
  prediction absorbs constant-velocity motion, so only the accelerated part of a dodge shows up as a miss:
  `½·a·t²` at a hull's own 40–110 px/s² over the flight time from a 700 px ring. That is 1.5–5.6 px for Streak
  against 2200–4200 px/s ammo (0.17–0.32 s), and 0 px against beams. The only rounds slow enough to be dodged
  (400–500 px/s) are cone guns whose own scatter cone — ±84 to ±350 px — swallows a 108–168 px step. HeavyCannon
  (a 45, r 78) cannot move out of its own hull radius at all. The cost is worse than the gain: a dodge needs a
  1.4–1.75 s threat scan across live player rounds (~11k live entities in `ammo-spray`, 100-hull fleet cap),
  and a dodge that *works* is a whiff with no feedback — the player just watches damage numbers not move.
- **Beams as the answer to evasion.** Correct but not load-bearing: 2200–4200 px/s covers the same 0.17–0.28 s,
  so the player would never feel a ballistic-vs-beam choice.
- **A player top speed or drag**, and **raising `MaxTier` past ~11** — both already rejected in
  plans/ENDLESS_DIFFICULTY.md.
- **Missile cover-evasion — out of scope for this change.** No curving around a shield hull, no re-lock when a
  body blocks the path, no missile guidance changes at all. Cover works against guided rounds too. If cover turns
  out too strong, the knobs are the tuck trigger and the cover cone (B5), not missile behaviour.
- **Enemy shields** — a refillable absorb pool is a sustained-fire economy, not a behavior. Its own plan file,
  priced against plans/SHIELDS.md, if it is ever wanted.
- **Herding the player toward mines, rock-cover holding, escort formations** — all need intent that outlives
  one tick. Not worth a state machine at this size.

## Open questions (playtest calls)

- Does cover-tucking read as tactics or as a stall? If a stall, shorten the cover window (B5) — do not delete the
  behavior, and do not "fix" it by giving the cripple a bigger gun.
- Rammer tuning: what share of Interceptor spawns, and what contact damage, before it reads as a threat rather
  than a drain? The behavior itself is decided; only its numbers are open.
- Does the swarm ever want more than its ≤ 20 % spread minority? Only if the ball stops feeling good to sweep —
  the sweep is PrismLance, FlakCurtain and RippleCutter's payoff.

# Part 2 — Implementation memo

## Behaviors

**B1 — Bearing spread.** Steer to hold the spawn bearing relative to the player while closing to the ring,
instead of `atan2(toPlayer)` straight in. Bearing derived from spawn position — no rng draw. One atan2 plus a
heading per hull. Assertion: after a wave, live hulls occupy ≥ 4 of 8 bearing sectors; all inside
`[FiringRange, CullDistance]`.

**B2 — Shell separation.** Repulsion inside ~2 summed radii, added to the thrust vector. Pure steering: enemy
hulls do not collide with each other today (`CollisionSystem` resolves hull-vs-player, ammo-vs-hull,
missile-vs-hull only), so nothing physical changes. Soft — one hull-radius of gap, so it reads as a formation
with spacing, not a standoff. O(n²) bounded by `MaxEnemyShips = 100` → ≤ 4,950 pair tests/tick (~50 µs of an
8.33 ms frame); **do not** thread the spatial grid into the AI for this. Held off the swarm (Part 1).
Assertion: pairs inside 2 summed radii separate monotonically (no oscillation), and separation never pushes a
hull inside its own `FiringRange` brake band — the brake test runs on the *combined* vector.

**B3 — Tangential hold.** At the ring, hold a tangential heading rather than braking to a park (the
ENDLESS_DIFFICULTY P1 item). Not evasion — lead prediction absorbs constant velocity — it removes the parked
target a kiting player comes back to. Shares B1's heading function. Tangential motion *lowers* closing speed,
which is an `ImpactDamage` (D4) mitigation and should be measured as one. Assertion: orbit distance stays
inside `[FiringRange, ringCeiling]` — orbit cannot smuggle a ship past D1.

**B4 — Cut-off pursuit.** Thrust at the intercept point rather than the current position, reusing the existing
`InterceptTime` quadratic. One solve per pursuing hull. Breaks into overshoot → the swing must be caught by the
existing drift-cancel pass. Assertion: against a player holding 250 px/s, distance to the player's *future*
position falls over 10 s where today's chase's rises; `EnemySpeedScalingTest` stays green.

**B5 — Cripple cover-tuck.** Below ~25 % hp a Carmine or HeavyCannon picks a **cover hull** — the largest live
hull at ≥ 60 % hp whose bearing from the player falls within a stated cone of its own — and steers to a point just
behind that hull: `coverPos + normalize(coverPos − playerPos) × (coverRadius + ownRadius + gap)`. It keeps firing
from there. When the cover hull dies, or after a ~8 s window, it re-engages at its own ring. No cover in range →
hold the **far edge of its own firing ring**, which is a brake-point change and never a range change: D1 and
`FiringRange` stay untouched, and standing past its own ring would just mean holding fire, so the fallback must
stay inside it. Cap ≤ 25 % of live hulls tucked at a time; `CullDistance` still applies; `EcmDisabled` cancels the
tuck. Reading `Health` is a read, not a mutation of spawn-time tier (R2.11 holds).

Why it works for free: `CollisionSystem` takes the **earliest** hit along a round's path, spends the round there
and destroys it, and beams stop at the first body too — a hull in the line already eats the shot. So there is no
line-of-sight test to add and no cover bookkeeping: the shield hull simply absorbs. `Pierce` is armour-pierce, not
pass-through. **Guided rounds are not a cover bypass in this change** (see the scope decision in Part 1): a locked
round flies at its lock and hits whatever body is in between, so tucking behind a shield is not a missile free
kill. The player's answers are the shield dying faster, and flying to get a clear angle.

Cost: this is the **same neighbour pass B2 needs** — ≤ 4,950 pair tests/tick at the 100-hull cap — and only the
crippled minority runs the pick (≤ 25 hulls × 100 candidates ≈ 2,500 comparisons plus a `TryGetComponent<Health>`
slot probe each). No grid, no pathfinding, no LOS sweep. Not heavy.

Assertion: the chosen cover is the largest healthy hull inside the bearing cone; a tucked hull never leaves its
own firing ring nor crosses the cull line; the tucked share stays ≤ 25 %; with no cover available it holds the far
ring edge rather than fleeing; a jammed hull does none of it.

**B6 — Rammer.** Brake into the player instead of at the ring. A behavior row on the Interceptor — the hull
whose numbers already say "hit something" — at ≤ 15 % of its spawns; never a fourth hull, and never on Streak,
whose whole identity is surviving by not being hit. Needs a visible tell
(magenta is reserved for elites, plans/SHIP_TINT_PALETTE.md) and a death-on-contact rule so it is not a free
trade. Assertion: closing speed and resulting damage stay inside D5 (≤ 1/3 of a late hull); it dies on contact.

**B7 — Volley cadence.** Heavies fire a 3-round burst behind a wind-up tell, then a dead zone. Period derived
from `view.ElapsedTime` modulo a cycle with a per-hull phase offset taken from spawn position — no per-hull
state. Rate unchanged, only redistributed. Assertion: rounds/s spawned over a window is unchanged — this is
redistribution, not escalation, so the `EnemyDifficultyTest` round budget is untouched.

## Unlock ladder (data, all knobs open)

Gate on composition tier next to `EnemyShipStats.UnlockedArmaments`, so the whole late-game mix stays in one
place. Extend `EnemyArmamentRow` to carry a behavior flag rather than adding a second draw; `EnemyShip` gains a
`Behavior` byte written at spawn and never mutated after.

Per hull × composition tier — this table is the only place the ladder is written. Every entry is a **share** of
that hull's spawns (a weight on the row), not a switch: at tier 8 only part of the Interceptors fan out, so a
fleet at the top of the ladder still mixes plain-chase hulls with behaving ones. That mix is what keeps the
read legible — the ball of mites, the heavies spreading, one heavy backing off.

| hull (role) | 0–2 | 3–4 | 5–7 | 8+ | never |
|---|---|---|---|---|---|
| Default — swarm (mass, r 20, 65 px/s) | chase | chase | chase | ≤ 20 % B1 spread | B2 (the ball *is* the sweep's payoff), B4, B5 (no hp worth saving) |
| Interceptor — stand-off + rack (28 hp) | chase | B1 spread | B1 · B6 rammer ≤ 15 % | B1 · B4 · B6 ≤ 15 % | B7 (its gun is a chip, not a volley) |
| Streak — dart (20 hp, 115 px/s) | chase | chase | B3 strafe | B3 · B4 | B6 (its identity is not getting hit, not hitting) |
| Carmine — line-breaker (88 hp) | chase | chase | B5 cover-tuck | B1 · B5 · B7 | B4 (55 px/s) |
| HeavyCannon — armored long line (120 hp, r 78) | chase | chase | B5 cover-tuck | B1 · B7 | B2 (it *is* the wall), B4 (50 px/s) |

B2 spacing enters at 5+ on Interceptor and Streak — the hulls whose knot a player cannot pick apart — and is
**held off the swarm** deliberately: see the clumpers-stay decision in Part 1 and the playtest question below.
Nothing before composition tier 3 (~3 min) behaves differently from today, so the opening fleet and the whole
first wave are unchanged.

## Invariants every behavior must keep

1. **One `Acceleration` command per hull per tick.** Compute one combined vector, issue one command. Last-write-
   wins in a phase flush would silently drop a behavior that queued a second one.
2. **`EcmDisabled` wins.** The jam branch stays the outermost early-return (`ApplyJamWander`); no behavior runs
   under a jam.
3. **`EffectiveSpeed` clamps the result** of every new vector — pursuit, orbit and cover-tuck alike. No behavior
   exceeds the rubber-band ceiling (plans/DIFFICULTY_SCALING.md P2).
4. **The ring is unchanged.** Behaviors choose headings, not ranges, so D1 (`max(FiringRange) + heaviest radius
   < 1350`) stays assertable exactly as written.
5. **No mutation of spawn-time stats** (R2.11). Behavior state — cover window, cadence offset — is its own
   small component, following `FireCooldown` / `MissileCooldown`.
6. **Determinism.** No new `Random` draws and no new rng source; derive from positions, bearings and
   `view.ElapsedTime`. Same-seed traces stay comparable (`WorldRngTest`).
7. **No dead-angle lock** — the 25 % arc-coverage cap from Part 1, asserted, because B1 is what opens it.
8. **No new phase and no new system.** All of this is `EnemyShipSystem` writing next-tick intent. If a future
   behavior ever needs a scan over live rounds, it goes in `CollisionSystem` (which owns the grid and already
   iterates every round), not in a new system.
9. **A hull instance carries one behavior.** Several entries on one table row are alternative weighted rows, not
   a stack — the draw picks one. Contradictory pairs never co-occur on the same hull (a rammer does not break
   off; a hull does not both hold a bearing and chase an intercept).

## Code shape

- `EnemyShipSystem.cs` is 227 lines and already holds two near-duplicate turn-toward-player paths
  (`ApplyRotationTowardPlayer` and the inline turn in `Update`). Extract `Domain/AI/EnemySteering.cs` first:
  small pure functions — `HeadingToBearing`, `HeadingToIntercept`, `SeparationThrust`, `CoverPoint` — each `(state in, vector
  out)`, ≤ 20 lines, unit-testable without graphics. `EnemyShipSystem` becomes the dispatcher that picks the
  behavior and issues the one command. Delete the duplicate turn code in that same commit.
- No per-frame allocation in the steering path, no new `yield return` in a hot path.

## Tests

Small deterministic sim tests, no graphics; `EnemyShipChaseTest` is the model.

`EnemyBearingSpreadTest` (sector spread + ring band) · `EnemySeparationTest` (monotone separation, no band
violation) · `EnemyTangentialHoldTest` (orbit radius band — the D1-smuggling assertion ENDLESS_DIFFICULTY P1
asks for) · `EnemyCutOffPursuitTest` (closing on a constant-velocity player; cap respected) ·
`EnemyCoverTuckTest` (cover = largest healthy hull in the bearing cone; no ring or cull violation; ≤ 25 % tucked;
far-ring fallback when there is no cover; jam cancels it) · `EnemyVolleyCadenceTest` (rounds/s
unchanged) · a rammer contact-damage test inside the `ImpactDamageTest` style.

Extend `EnemyDifficultyTest`: D1/D3/D5/D6 unchanged (no behavior may move a stat), and the unlock ladder
asserted as data the way `UnlockedArmaments` is (plans/MISSILES.md R20 style). `WorldRngTest` must stay green —
zero new draws.

## Measurement

One axis per commit (AGENTS.md):

- `.tools/scenario.sh impact-storm --sim --iters 3` — the hull-and-grid world; the right price for B1–B6.
- `.tools/scenario.sh ammo-spray --sim --iters 3` — must stay flat. Anything that moves it means per-round work
  leaked into a steering path.
- New scenario `encircle-64` — 64 hulls holding spread bearings around one hull: prices separation's O(n²) plus
  the extra steering together, and gives `encircle-64` its own row in `.tools/run/index.tsv`.
- One headless play-through per behavior, reading `[THREAT]` (`hp`, `in`, `out`) at 5/10/15/20/30 min.
- Add one read-only `sectors` column to `ThreatMeter.Measure`: how many of 8 bearing sectors around the player
  hold ≥ 1 hull. Without it B1's effect is assumed, not measured. `Measure` is documented as never in the hot
  path, so it is free.

## Order of work

1. Extract `EnemySteering`; behavior identical; verify `EnemyShipChaseTest` green and `impact-storm` flat.
2. B2 → B1 → B3. This is the "spread out around the player" ask, and the cheapest real difficulty in the file:
   no stat moves, no entity spawns, and the player's answer is flying and turning.
3. B4 cut-off pursuit.
4. B5 cover-tuck, then B6 rammer — each gated behind composition tier, each read against the `[THREAT]` walk.
5. B7 cadence last: it changes the rhythm of incoming fire, so it should land after the positional work has
   been played, not with it.

## Shipped (2026-11-08): what landed

- `EnemyBehavior { Chase, Spread, Strafe, CutOff, CoverTuck, Rammer, Burst }` is a fourth column of
  `EnemyArmamentRow`, so a manoeuvre is dealt on the **same roll** as an armament and no hull needs state
  written after spawn. `PickComposition` returns `(Type, Armament, Behavior)`. Behaviour rows split a hull's
  **gun** budget, never add to it — which is why every missile share in `EnemyMissileLadderTest` still reads
  as plans/MISSILES.md R20 said.
- `EnemyShip.HoldBearing` (the unit player→spawn offset) is the only new state. It is B1's bearing and B7's
  phase in one number: no timer, no rng draw, nothing to write later.
- `src/Domain/AI/EnemySteering.cs`: `SteerPlan(TargetPoint, HoldDistance)` plus one plan per behaviour,
  `TryFindCover`, `SeparationThrust`. `EnemyShipSystem` dispatches on behaviour, turns every manoeuvre toward
  the player (the gun *is* the hull, so a manoeuvre costs a firing solution rather than needing a rule), adds
  separation into the same vector, clamps the pair to the hull's own acceleration, and still writes exactly one
  `Acceleration` per hull per tick. The two copies of the turn code are now one `TurnToward`.
- Jam stays outermost: a jammed hull does nothing whatever its behaviour (asserted for all five).
- Burst: `TurretFiringSystem` holds a Burst hull's fire outside `EnemyShipStats.InBurstWindow` and divides its
  cooldown by `BurstRateMultiplier` inside it, so the same rounds/s arrive as a wall. `EnemyShipRenderer`
  charges a warm glow over the hull from `BurstTell`.
- The ram spends itself: `CollisionSystem.SpendTheRam` ends a Rammer hull on the contact it lands on the player.
- `ThreatMeter.Measure` gained `BearingSectors`, printed as `sectors=` on the `[THREAT]` row.
- Diagnostics: `Q` spawns a Carmine carrying the next manoeuvre in the ladder (DIAGNOSTICS.md).
- 56 new cases in `EnemyBehaviorLadderTest`, `EnemyManoeuvreTest`, `EnemyCoverTuckTest`,
  `EnemyVolleyCadenceTest`; 706 passing.

### Deviations

- **No `encircle-64` scenario.** `hull-crowding` already puts many hulls around one player, so a second
  many-hull scenario would price the same lines for the cost of another scenario to maintain. Separation is
  bounded by the 100-hull cap instead.
- **No per-tick cap on the tuck share or on the arc a tucker may cross.** Both are asserted on the ladder data
  (≤25 % of the fleet, ≤20 % of the swarmer, ≤15 % of a ramming hull) and the cone is asserted in
  `AFriendOnTheFarSideIsNotCover`. A runtime counter would be work to keep a promise the data already makes.
- **No 8 s "give up" timer on a failed tuck.** A crippled hull with nothing to hide behind chases at its own
  ring, which is the fallback the memo described; a timer is state with nothing to show for it.
- **The ram now dies on contact.** The memo assumed impact damage would make the trade two-way; against the real
  rule a ram's own share of that damage is 1-2 hp, so it would bounce off and charge again for free. The spend
  rule replaces the assumption rather than the number.
- **Missiles are out of scope**, per the decision above: no go-around cover, no evasion. A round flies where it
  was pointed and whatever it meets first takes it.

Perf verdict (2026-11-08, `.tools/scenario.sh <name> --sim --iters 2`, three runs each side, the change stashed
between them): `hull-crowding` 0.44/0.44/0.56 ms with the change against 0.45/0.50/0.56 ms without;
`impact-storm` 2.27/2.25/2.32 against 2.26/2.31/2.33; `ammo-spray` 1.54 against 1.50. **The change costs ~0 % —
keep.** The full `dotnet test` suite (706 cases) is green apart from the known `ammo-spray-miss` ceiling flake
(TROUBLE_SHOOTING.md §28): one run of three read 2.69 ms against its 2.50 ms ceiling and the other two passed,
and that scenario run directly measured 0.79-0.85 ms with the change against 0.84-0.87 ms without it. Honest
limit on that number: the scenarios seed plain-chase hulls, so what was measured is the new
per-tick pre-pass and the dispatch, not spread/separation/tuck at 100 behaving hulls. The first real look at
those paths is a `Q`-key walk, or a scenario that seeds behaving hulls — the gap the dropped `encircle-64`
left behind.

## Follow-up (2026-11-08): the spread read as nothing in play

Playtest: over a several-minute run hardly any hull spread out, and the one that did sat far away and never
moved fast enough to matter. That is exactly what the shipped ladder does. Spread share of every spawn, with
the wave quota a ~250 px/s player actually gets:

| clock | comp. tier | spread | wave quota |
|---|---|---|---|
| 0:00–2:00 | 0–2 | 0 % | 15–25 |
| 3:00–4:00 | 3–4 | 4.1 % | 30–35 |
| 5:00–7:00 | 5–7 | 3.7 % | 40–50 |
| 8:00–9:00 | 8–9 | 12.2 % | 55–60 |
| 10:00+ | 10+ | 8.3 % | 65–80 |

Before 8:00 the only spreading hull in the game is the Interceptor's, one or two per wave. From 10:00 the
HeavyCannon's `GunsAndMissiles 4 : 1` rows dilute every behaviour row and the share falls again.

Four causes, and only two of them are numbers:

- **The manoeuvre had almost no motion by construction.** `HoldBearing` is the spawn bearing and the hold point
  was `player + bearing × FiringRange` — the same ray the hull spawned along. So it could only shuffle
  radially, from its spawn distance (572–1020 px, screen edge + margin) to its ring (655 px for an Interceptor
  at tier 3): a few hundred px, about a second, then nothing. No tangential force exists beyond separation.
- **"Far away" is the ring, not the behaviour.** Every hull parks at its own ring, 655–900 px, so a spreader is
  at the same distance as the ball the player is already shooting.
- **"Not fast enough" is `EffectiveSpeed`.** 188 px/s for an Interceptor against a ~290 px/s player: chasing a
  point that travels with the player loses the bearing, so the flank trails instead of holding it.
- **It dies first.** Player guns take the nearest enemy in range, and an isolated hull on a lone bearing at
  655 px is nearer than the swarm — about 2 s of visibility on 49 hp.
- Spawns come from a ±45° cone of the player's heading, so every spreader in a wave freezes a bearing inside
  the same forward arc: fan-out in front of the player, never around them.

### Landed: a lane, not the ring (geometry only)

`SpreadPlan` now aims at `player + playerVelocity × SpreadLeadSeconds + bearing × (ring ×
SpreadHoldFraction)`, with `SpreadHoldFraction = 0.55` and `SpreadLeadSeconds = 1.2`:

- Holding at ~0.55 of the ring (360–430 px at these tiers) puts the flank **inside gun range**, so its bearing
  is something the player answers rather than background scenery. Reaching a point *inside* the ring cannot
  extend anyone's reach, so D1 holds and no stat moves.
- Leading the lane by 1.2 s fixes the speed problem without touching speed: a hull capped below the player
  cannot hold a frozen bearing by chasing it, only by cutting toward where it will be. The lead is bounded by
  the player's own speed, so nobody aims a minute ahead.

`ASpreadHullClosesIntoGunRangeWhereAChaserParks` is the pair that prices the fraction — the chaser in the same
place does not move at all; `ASpreadHullLeadsThePlayerIntoItsLane` is the same hull with a still player and a
player running crosswise. 708 green. Perf unchanged (`hull-crowding` 0.43/0.41/0.43 ms against the 0.45/0.50/0.56
recorded before; `impact-storm` 2.26/2.25/2.20 against 2.26/2.31/2.33) — the scenarios seed chase hulls, and the
change is one vector add per spreading hull.

### Landed the same day: how many, how soon, and from where

Ladder, and the new spread share of **every** spawn (fleet-wide, not of one hull):

| clock | comp. tier | spread | per wave @250 px/s |
|---|---|---|---|
| 0:00–2:00 | 0–1 | 0 % | 0 |
| 2:00–4:00 | 2–4 | 6.2–6.8 % | 1.7 → 2.4 |
| 5:00–7:00 | 5–7 | 10.2 % | 4.1 → 5.1 |
| 8:00–9:00 | 8–9 | 13.8 % | 7.6 |
| 10:00+ | 10+ | 9.4 % | 6.1 |

- Interceptor spread from **tier 2** at 0.3 of its guns, 0.25 from tier 5 (it held 0.2 from tier 3).
- The swarmer spreads from **tier 5** (0.15) and a quarter of itself from tier 8 — it had nothing before 8:00.
- Carmine spreads from tier 5: a flanker is the nearest thing to the player's guns, so the flank worth
  *seeing* needs hp behind it — 154 there against the Interceptor's 49, which is why the old ladder showed
  one 2-second hull per wave.
- Gun rows still sum to 1 per hull per tier, so the missile shares of plans/MISSILES.md R20 are untouched,
  and the fleet's spread share peaks at 13.8 % — a fifth is the asserted ceiling, not a rounding.
- Spreading hulls are **born from a wider arc**: ±60° of the player's heading against the wave's ±45°
  (`Manoeuvre.SpreadSpawnCone`, passed as `SpawnPlacement.ForwardDirection`'s half-angle). Not any bearing:
  a hull born behind a fast player falls behind and never arrives, and past a right angle it is not a flank,
  it is an ambush from the rear. The composition is now drawn **before** the placement, since where a hull
  may be born is part of what it is; either order consumes one rng draw, so a seed still replays the same
  run. `SpreadingHullsSpawnFromAWiderArcThanTheWaveDoes` asserts both the bound and that the arc is real.

Measured on the headless run, same procedure both sides (a `Q`-spawned Carmine, player idle, bracket centre
from screen centre): **642 px** with `SpreadHoldFraction = 1`, `SpreadLeadSeconds = 0` — at the top edge of
the screen, which is the complaint — against **418 px** with the lane. `hull-crowding` 0.43/0.43/0.46 ms and
`impact-storm` 2.27/2.28/2.24 ms against the recorded 0.45/0.50/0.56 and 2.26/2.31/2.33: **~0 %, keep**.
713 green.

### Still open

- **The tier-10 dilution is untouched**: the HeavyCannon's `GunsAndMissiles 4 : 1` rows still swamp every
  behaviour row, so spread falls from 13.8 % to 9.4 % at 10:00. Either the rack ratio comes down or a rack
  hull is allowed a lane too (the original memo said a rack is a rack, which forbids a firing pattern, not a
  bearing). Deliberately left for a read run, not guessed at.
- **The metric to watch in a real session is `sectors=` on `[THREAT]`** (DIAGNOSTICS.md). It should climb
  from the 1–2 a trailing ball gives toward 4–5 once the flanks hold lanes. Nobody has read it yet; the
  numbers above are all arithmetic and screenshots, not a played session.
