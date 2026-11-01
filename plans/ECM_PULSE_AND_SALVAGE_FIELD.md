# Plan: ECM Pulse and Salvage Field Drops

Status: shipped (2026-11-01) — built as planned, with the deviations recorded under `## Shipped` below.
Supersedes the two sketches in plans/LOOT_ITEMS.md (that file's body describes a different, unshipped
design; this file is the record of what landed).

## What lands

Two new enemy-ship drops, both magnetised capsules like the reroll token:

- **Salvage field** — collected, it sweeps the battlefield once: **every XP orb anywhere** comes in,
  **every currency pickup** (today: reroll tokens) comes in, and the **nearest health orbs, only as many
  as it takes to top the hull off to full hp** come in. Everything else is left where it lies.
- **ECM pulse** — collected, it fires immediately and affects **every enemy in the world** for 5 s: ships
  stop accelerating and shooting, mines stop homing and bounce off the hull instead of detonating, enemy
  missiles lose their lock, lose their plume, and pop one after another. Disabled hulls get electric arcs.
  The capsule that lies on the field **never expires** and is **never swept** by a salvage field.

Both are pickups, not choices: the effect is visible the frame they touch the hull, with no follow-up UI.
That is the rule plans/LOOT_ITEMS.md set and both still obey it.

## What this changes from the LOOT_ITEMS.md sketches

| | old sketch | this plan |
|---|---|---|
| Salvage field | `PickupRadius × 2` for 6 s (a radius buff) | one-shot sweep of the whole field, no radius change |
| ECM pulse | deletes enemy rounds inside ~300 px | disables all enemy ships, mines and missiles for 5 s |
| Overlap with lull magnet (plans/WAVES_AND_BREATHERS.md W3) | had to be resolved | gone: the sweep is not a radius lever, so nothing multiplies with a lull bonus |
| ECM as the missile answer to R21 (plans/MISSILES.md) | deletes rounds | disables and detonates them — same answer, and it also answers guns and mines |

## Decisions

**D1 — Both drops join the existing one-roll ship drop table.**
`LootRoll.RollShipDrop` grows two bands; a ship death still spends **exactly one** RNG number, so seeded
runs and the perf baselines do not shift (plans/REROLL_TOKENS.md D3, `LootRollTest`). Mines keep their own
`5 %` health-orb roll — mines are the cheap kill, so the two powerful capsules stay priced against the
thing that shoots back.

Bands (`LootStats`):

| Drop | Chance | Note |
|---|---|---|
| HealthOrb | 5 % | unchanged |
| RerollToken | 6 % | unchanged |
| **SalvageField** | **3 %** | new |
| **EcmPulse** | **2.5 %** | new, the smallest band — it removes a threat instead of granting something |
| None | 83.5 % | |

Total rare-drop budget 16.5 %, up from 11 %. This is the number the open reward-side knob
(plans/DIFFICULTY_SCALING.md R2.6 item 7) will scale down late in a run; all four bands move together.

**D2 — The ECM capsule is a field resource: it never expires and nothing sweeps it.**
`EcmPulse` carries no `Lifetime` at all, so there is no expiry to age and nothing to forget. Because the
salvage field deliberately does **not** pull ECM capsules, banking one means flying into the kill zone
yourself — the tension the reroll token already has, but with the pile staying put instead of rotting.

**D3 — The salvage field decides once, at the moment of collection.**
- XP: mark every `XpPickup` in the world as `Chased` (the field does what the magnet radius does, without
  the radius).
- Currency: mark every currency pickup in the world. Today that is exactly `RerollToken`; the rule is
  "anything that lands in a player-held stock", so a future currency pickup joins by being added to this
  one pass. **ECM capsules are not currency and are not pulled.**
- Health: compute `needed = ceil(hpDeficit / LootStats.HealthOrbHealAmount)`, then pull the `needed`
  nearest health orbs and leave the rest alone. At full hp, `needed = 0` and no orb moves.
  If fewer orbs exist than `needed`, all of them are pulled ("if available").
- The salvage field and ECM capsules lying on the field are **not** swept — a burst does not hand you the
  next burst.

The `needed` count is fixed at the collection tick. Orbs already in flight are committed, so a hit taken
during the flight can overshoot full hp by a few points; the heal is clamped at `Max`, so the overshoot
costs nothing but the orbs.

**D4 — Swept pickups fly in fast, not instantly.**
Swept orbs keep the existing homing behaviour but use a higher cap, `LootStats.SalvageSweepSpeed = 1000f`
instead of `MaxMagnetSpeed = 350f`, so a 4000 px pile lands in ~4 s and the whole field visibly streams
into the hull. Collection stays a distance test and the magnet re-aims every tick, so a fast orb that
overshoots is inside the collection distance the next tick — no tunnelling.

**D5 — A swept pickup stops aging.**
While `Chased`, a pickup's `Lifetime` does not tick down. Without this, a swept orb whose remaining
lifetime is shorter than its flight time pays nothing for the sweep — the current 90 s clock is invisible
to the player and reads as stolen XP. It also makes `Chased` mean what it says: committed, and yours.

**D6 — The sweep is held in local state on the collection tick, never as a same-phase component write.**
This is TROUBLE_SHOOTING #58 in exactly the shape it bit before: `ProcessXpPickups`/`ProcessHealthOrbs`/
`ProcessRerollTokens` already write each pickup's own component back from a pre-phase copy, so a marker
queued earlier in the same flush loses to that stale copy. So: the field capsule is collected **first** in
`PickupMagnetSystem.Update`, and the sweep is carried into the three passes as locals —
`bool sweepXp`, `bool sweepCurrency`, `float pullRadius` (`-1` = no sweep). Each pass ORs the local with
the pickup's own `Chased` and writes the merged value, so there is exactly one writer per component per
tick.

The health pull uses a **scalar threshold radius** (the distance of the Nth nearest orb) instead of a
`HashSet` of chosen entities: no allocation on the sweep tick, and the pass just tests
`orb.Chased || dist < pullRadius`. Ties at exactly the threshold distance are harmless.

To make this work, `HealthOrb` and `RerollToken` gain a `Chased` field like `XpPickup` already has, and
`PullTowardsPlayer` gains a `pullRegardlessOfRadius` argument (the magnet-radius test is the only thing
the sweep bypasses). Sticky across ticks through the component, so the one-shot decision survives the
flight.

**D7 — Two state components, one timer system.**

| Component | On | Means |
|---|---|---|
| `EcmDisabled(float TimeRemaining, float WanderHeading)` | enemy ships, enemy mines | no thrust, no fire, no homing; mines also do not detonate on contact. `WanderHeading` is read by ships and ignored by mines. |
| `MissileStunned(float DetonateIn)` | enemy missiles | no guidance, no plume, detonates at expiry |
| `EcmWave(float MaxRadius, float Lifetime, float InitialLifetime)` | one fresh effect entity | the faint blue ring that fires out of the pickup |

A new `EcmSystem` (Domain/Combat) owns the two timers: age `EcmDisabled` and remove it at expiry; age
`MissileStunned` and, at expiry, destroy the round and spawn an `Explosion` at its position — the existing
shoot-down visual grammar, and the explosion carries no `Health`, so it hurts nobody. It is placed in
**Resolution, right after `PickupMagnetSystem`**, so the tick a capsule is collected the pulse is applied
and ages nothing (it reads pre-phase state) and the pulse runs the full 5 s. No new phase, no new
command pattern.

`EcmWave` ages in `EffectSystem` with the sparks and explosions — it is a pure effect, and putting it in
`EcmSystem` would make that system own a cosmetic timer for no reason.

**D8 — A disabled ship coasts, turns slowly to a random heading, and shoots nothing.**
- `EnemyShipSystem`: after the cull check, `if (EcmDisabled)` → write `Acceleration = Zero`,
  write `AngularVelocity` at `EcmStats.WanderTurnRate` toward `WanderHeading`, `continue`.
  `PhysicsSystem` integrates the turn; velocity is untouched, so the hull drifts on its existing momentum
  (Newtonian, and it reads as "dead in the water").
- `WanderHeading` is drawn from `view.Rng` **once per ship at pulse time** and stored in the component, so
  the AI system consumes no randomness and a seeded run replays the same wander. The pulse therefore
  consumes N numbers from the world stream (one per ship alive when it fires) — deterministic, and
  documented here because the baselines are seeded.
- `TurretFiringSystem`: skip enemy turrets carrying `EcmDisabled` (one guard beside the existing `Dead`
  guard). `EnemyMissileFiringSystem`: same guard. Cooldowns are not ticked while disabled, so a hull
  resumes firing on the tick the pulse ends — see the knob in Risks.
- `ThrusterFlameRenderer`: skip enemy ships carrying `EcmDisabled` — both the thrust plume and the turn
  flame, so a drifting hull shows no fire at all.

**D9 — A disabled mine homes nothing, bounces off the hull, and costs the player nothing.**
- `MineDriftSystem`: skip mines carrying `EcmDisabled`, so a bounced mine coasts away instead of being
  steered straight back at the hull the next tick.
- `CollisionSystem.ResolveMineVsPlayer`: when the mine carries `EcmDisabled`, take a bounce branch —
  destroy nothing, take no `MineStats.ContactDamage`, spawn no contact sparks, no player impulse; set the
  mine's velocity to `-contact.Normal * mine.Speed` so it leaves along the contact normal. The existing
  normal already points mine → player (that is what pushes the player in the live path).
- Mines stay damageable and still drop their XP normally, so a pulse turns a mine field into a free clear.
- Mines keep their own hp/contact behaviour the moment the pulse ends.

**D10 — A disabled missile flies straight, shows no plume, then pops in a chain.**
- `MissileGuidanceSystem`: skip entities carrying `MissileStunned` (the first check in the loop). Velocity
  is left alone, so the round keeps its last heading — and because guidance is the only writer of
  `Ammo.Velocity`/`Velocity` for a round, the swept collision segment stays consistent (the invariant
  MISSILES.md §guidance is built on).
- `WorldRenderer.DrawMissiles`: when the round carries `MissileStunned`, draw it with `exhaustLength = 0`
  and no flame (pass a `flame` flag into `PlumeLook`/`DrawMissile` rather than special-casing the shape —
  the body must keep drawing, a stunned round is still a round).
- `EcmSystem` detonates it at `DetonateIn`. **`DetonateIn = EcmStats.MissileDetonateDelay + index *
  EcmStats.MissileDetonateStagger`** (0.25 s + 0.12 s per round): a chain of pops rather than one
  simultaneous flash, with no RNG draw per round.
- A stunned round still collides and still hurts if it crosses the hull before it pops. The pulse takes
  away its guidance, not its kinetic threat — that is what keeps it from being a get-out-of- missiles-free
  card.

**D11 — The wave is a domain effect entity, not a render timer.**
On collection, `PickupMagnetSystem` spawns one `EcmWave` at the pickup's position. It grows from 0 to
`MaxRadius` (900 px) over `Lifetime` (0.6 s) — `CurrentRadius = MaxRadius * (1 - Lifetime/Initial)` next
to `Explosion`'s existing formula — and is drawn as 3 concentric faint blue rings whose alpha fades with
life. Spawning it in the sim keeps game logic off the renderer, makes it testable without graphics, and
means a headless sim run can assert the pulse produced it.

**D12 — Arc flicker is deterministic and bounded.**
`EcmRenderer` (new, Game layer) draws arcs for every entity carrying `EcmDisabled` + `Position`, and the
wave rings. Flicker comes from a pure hash of `(entity id, floor(em.ElapsedTime * EcmStats.FlickersPerSecond))`
— **the renderer must not consume `em.Rng`** (the world stream belongs to the sim), and hashing simulated
elapsed time keeps a seeded run screenshotting the same frame twice, the same reason `RerollToken.Spin` is
simulated. Geometry: 3 short jagged strips per hull on a ring at `Radius * 1.15`, each drawn with one
`Raylib.DrawLineStrip` call, capped at `EcmStats.ArcEntityCap = 24` entities per frame — the bounded
per-unit renderer rule of TROUBLE_SHOOTING #61, since a scenario can put hundreds of mines on screen.

**D13 — The pulse marks what exists when it fires; picking another one refreshes it.**
Ships, mines and missiles that spawn during the 5 s are live and behave normally. Collecting a second
capsule while a pulse is running re-applies the components with full durations and re-stuns the missiles —
the same code path, no stacking timers.

**D14 — Names.** `SalvageField`, `EcmPulse` (pickups), `EcmDisabled`, `MissileStunned`, `EcmWave` (state
and effect), `EcmStats` (tuning), `EcmSystem` (timers), `EcmRenderer` (arcs + wave). The existing
`RerollToken` keeps its name: in prose it stays "salvage capsule", and only `SalvageField` is called a
salvage *field*, so the two never read as the same object.

## Numbers (new stats)

`Domain/Stats/EcmStats.cs`:

| Constant | Start | Note |
|---|---|---|
| `Duration` | 5.0 s | as specced |
| `WanderTurnRate` | 0.8 rad/s | "slowly"; a ship's own turn rate is 2–4 rad/s |
| `MissileDetonateDelay` | 0.25 s | first pop |
| `MissileDetonateStagger` | 0.12 s | per round in flight |
| `WaveMaxRadius` | 900 px | past the on-screen half-diagonal at 1080p, so it sweeps off the edge |
| `WaveLifetime` | 0.6 s | "quickly" |
| `WaveRingCount` | 3 | faint blue, fading outward |
| `ArcsPerHull`, `ArcSegmentCount` | 3, 4 | small enough to read as arcs, cheap enough to bound |
| `ArcEntityCap` | 24 | #61 bound |
| `FlickersPerSecond` | 12 | arc redraw rate |
| `CapsuleFlashRate` | 6 rad/s | the blue-grey-white flash |

`LootStats` additions: `SalvageFieldChance = 0.03`, `EcmPulseChance = 0.025`, `SalvageSweepSpeed = 1000`,
`FieldCapsuleRadius = 16` (collection radius, like `RerollTokenRadius`), and `RerollTokenSpinRate`
**renamed** to `CapsuleSpinRate` — three call sites (`LootStats`, `PickupMagnetSystem`, `RerollTokenTest`)
and all three capsules tumble at the same rate, so one name is honest.

## Look

- **ECM capsule**: the existing salvage capsule silhouette, flashing between `EcmGrey (118,128,140)` and
  `EcmWhite (235,242,255)` through a blue mid-tone — `RenderHelpers.DrawSalvageCapsule` hard-codes the
  `Palette.Salvage*` colours today, so it gains a colour-set parameter that the reroll pass keeps passing
  as-is. The flash phase is advanced by `PickupMagnetSystem` each tick and stored on the component (like
  `Spin`), not read off a wall clock.
- **Salvage field capsule**: the same grey capsule as the reroll token with a slow-pulsing thin cyan ring
  around it (`Palette.Accent`). Shape-first, motion-second: a tumbling grey capsule with a ring is
  "collect everything", a flashing capsule is "jam everything", and neither can be mistaken for the
  steady grey reroll capsule.
- **Wave**: 3 faint blue rings, alpha ~70 down to 0.
- **Arcs**: pale blue-white strips over the hull, flickering.
- `IndicatorRenderer` gains an ECM-capsule marker set (cap 8, `Palette.EcmWhite`-ish) so a capsule banked
  off-screen can be found again; the salvage field capsule joins the existing salvage marker set.

## Files touched

| File | Change |
|---|---|
| `Domain/Components/EffectComponents.cs` | `SalvageField`, `EcmPulse`, `EcmWave`; `HealthOrb`/`RerollToken` gain `Chased` |
| `Domain/Components/CombatComponents.cs` | `EcmDisabled`, `MissileStunned` |
| `Domain/Stats/LootStats.cs` | 2 bands, sweep speed, capsule radius, spin-rate rename |
| `Domain/Stats/LootRoll.cs` | 4-band `ShipDrop`, one roll as before |
| `Domain/Stats/EcmStats.cs` | new tuning table |
| `Domain/Progression/PickupMagnetSystem.cs` | collect field + ECM capsules first; sweep locals into the three passes; `EcmDisabled`/`MissileStunned`/`EcmWave` spawn; chased pickups stop aging |
| `Domain/Combat/EcmSystem.cs` | new: age `EcmDisabled`, detonate stunned missiles |
| `Domain/Combat/EffectSystem.cs` | age `EcmWave` |
| `Domain/Support/SimulationRunner.cs` | `EcmSystem` after `PickupMagnetSystem` |
| `Domain/Combat/TurretFiringSystem.cs` | 1 guard |
| `Domain/Combat/EnemyMissileFiringSystem.cs` | 1 guard |
| `Domain/Combat/MissileGuidanceSystem.cs` | 1 guard |
| `Domain/Combat/CollisionSystem.cs` | ship drop bands; mine bounce branch; drop the two new capsules |
| `Domain/AI/EnemyShipSystem.cs` | disabled branch (coast + slow wander) |
| `Domain/AI/MineDriftSystem.cs` | 1 guard |
| `Game/WorldRenderer.cs` | draw the two capsules; plume off for stunned rounds; wave + arc passes |
| `Game/EcmRenderer.cs` | new: arcs + wave rings |
| `Game/RenderHelpers.cs` | `DrawSalvageCapsule` takes colours; ring helper |
| `Game/ThrusterFlameRenderer.cs` | skip disabled enemy ships |
| `Game/Palette.cs` | ECM colours |
| `Game/IndicatorRenderer.cs` | ECM marker set |
| `Game/GameSession.cs` | diag keys `E` / `F` |

The per-system cost is one `TryGetComponent` per enemy ship per tick (AI), per enemy turret per tick
(firing), per enemy mine per tick (drift), per enemy missile per tick (guidance) — the shape every other
guard in those systems already has.

## Tests

Deterministic, no graphics, one system or one phase per test.

- `LootRollTest`: four bands land in their ranges, still never two drops at once, still **one RNG number
  per death** (the existing test must pass untouched).
- `SalvageFieldTest`
  - an XP orb 3000 px out is `Chased` after collection and reaches the hull within a few seconds;
  - two XP orbs, one collected normally and one swept, credit their XP **exactly once** — run the whole
    Resolution phase (both `CollisionSystem`-free passes plus the field capsule) and apply the commands
    once, the #58 test shape;
  - at 40/100 hp with orbs at 100/300/600/900 px and `needed = 3`, exactly the three nearest are `Chased`
    and the 900 px orb does not move;
  - at full hp no health orb is pulled; with fewer orbs than needed, all are pulled;
  - every reroll token is pulled; an `EcmPulse` capsule is **not** moved by a sweep;
  - a swept orb stops aging (`Lifetime` unchanged while `Chased`).
- `EcmPulseTest`
  - collecting adds `EcmDisabled` to every enemy ship and mine, `MissileStunned` to every enemy missile,
    spawns exactly one `EcmWave`, destroys the capsule, and leaves **player** rounds and turrets alone;
  - a mine, a ship and a missile present in the world are all covered by one collection;
  - the ECM capsule does not expire over 100 s and its flash phase advances;
  - a second capsule collected mid-pulse refreshes the timers.
- `EcmDisableTest` (runs `EnemyShipSystem` + `TurretFiringSystem` + `EnemyMissileFiringSystem` +
  `EcmSystem` against one view, the #58 test shape)
  - a disabled ship writes zero `Acceleration`, spawns no `Ammo`, and its `Rotation` moves toward
    `WanderHeading` at the slow rate; an undisabled ship in the same world still chases and fires;
  - after `Duration` ticks the ship accelerates and fires again;
  - a disabled rack launches nothing.
- `EcmMineTest`
  - a disabled mine overlapping the hull is **not** destroyed and the player's hp does not drop;
  - its velocity points away from the hull after the contact;
  - once the pulse ends, a mine contact detonates and costs `ContactDamage` as before;
  - `MineDriftSystem` leaves a disabled mine's velocity alone.
- `EcmMissileTest`
  - a stunned round's `Velocity` and `Ammo.Velocity` are unchanged tick to tick (no steering);
  - at `DetonateIn` the round is gone and one `Explosion` exists at its position, and the player's hp is
    unchanged;
  - staggered detonation: two rounds stun on the same tick and pop on different ticks.

## Measure, do not assume

Per AGENTS.md, after the sim and render work:

    .tools/scenario.sh impact-storm --sim --iters 3
    .tools/scenario.sh missile-384 --sim --iters 3

against the previous rows of `.tools/run/index.tsv`, with `--check-baseline`, and a window run of
`impact-storm` for the `renderPasses` table (arcs + wave + two new capsule passes are the render delta).
Verdict in the reply: cost of the guards and the extra passes in %, keep / optimize / revert.

Two costs the scenarios cannot see, and how to price them honestly:

- **The sweep is a one-time command burst**: N XP orbs × 4 commands, then N orbs writing position/velocity
  every tick until they arrive. No scenario seeds XP orbs, so measure it in a window run with the diag key
  and a big pile, or add a `loot-field` scenario as a follow-up rather than guessing a number.
- **Arcs on a crowded field**: bounded at 24 entities × 3 strip calls, which is the #61 rule. Confirm with
  the window run's `renderPasses` on `hull-crowding`, where a pulse covers everything on screen.

## Risks and knobs

- **The pulse is an escape valve, so it caps pressure** — the LOOT_ITEMS argument still holds, only
  stronger now that it silences guns, mines and missiles at once. The knobs are the drop band (2.5 %) and
  `Duration`; put it in after the missile difficulty curve is measured, not before.
- **Ships fire the tick the pulse ends** (frozen cooldowns). If that reads as a cheap shot, the honest fix
  is to restart a reload on expiry, one line in `EcmSystem`.
- **A pulse over a mine field is a free clear** (mines bounce, stay killable, pay XP). That is a real
  reward for pulling it near a field; keep the band small and see how it plays.
- **Sweeping every XP orb is a lot of moving dots**: a swept orb is one `DrawCircle` and four commands per
  tick. In play the pile is tens of orbs; if a late run makes it hundreds, the fix is `SalvageSweepSpeed`
  upward (shorter flight) rather than a new system.
- **ECM capsules accumulate forever** — a long run leaves tens of capsules on the field. Cheap to draw, and
  it makes the map read as a place with leftovers; note it if it ever starts to clutter.
- **Salvage field value is situational**: dead in a sparse run, strongest when the pile is behind you.
  That is the LOOT_ITEMS risk and it is not designed away here; it is what makes the capsule worth flying
  for.

## Order of work (small commits)

1. `LootRoll`/`LootStats` bands + `LootRollTest` (no behaviour yet, capsules drop and do nothing).
2. `EcmDisabled` / `MissileStunned` / `EcmWave` components + `EcmSystem` + phase slot + `EffectSystem`,
   with a diag key that applies the pulse directly. Test the disable behaviour (D8–D10) before the pickup
   exists.
3. The guards: `EnemyShipSystem`, `TurretFiringSystem`, `EnemyMissileFiringSystem`,
   `MineDriftSystem`, `MissileGuidanceSystem`, `CollisionSystem` mine bounce.
4. `SalvageField` sweep in `PickupMagnetSystem` (D3–D6), with `Chased` on `HealthOrb`/`RerollToken`.
5. Collection: `EcmPulse` capsule (never expires) + `SalvageField` capsule, and the wave spawn.
6. Render: two capsules, arcs, wave, plume-off, flame-off, indicators, `Palette`.
7. Measure both scenarios + the window `renderPasses`; record the verdict here.
8. Docs: this file's `## Shipped`, its PLAN.md index line, ARCHITECTURE.md (components + Resolution
   phase list), DIAGNOSTICS.md (`E`/`F` keys).

## Docs to update alongside

- `ARCHITECTURE.md`: five new components in the component list, `EcmSystem` in the Resolution phase list.
- `PLAN.md`: replace the "Salvage field and ECM pulse: two deferred pickups" index line with this file;
  drop the roadmap line's "next candidates" wording once shipped. A salvage *currency* still has nothing
  else to buy (plans/TURRET_LAYOUTS.md), so the currency rule in D3 stays a rule, not a counter.
- `plans/LOOT_ITEMS.md`: header becomes `superseded by plans/ECM_PULSE_AND_SALVAGE_FIELD.md`; its body
  stays as the frozen survey (its "still deferred, ranked after these two" list is still the menu).
- `DIAGNOSTICS.md`: `E` spawns an ECM capsule ahead of the hull, `F` spawns a salvage-field capsule.
- `TROUBLE_SHOOTING.md`: nothing new expected — #58 (same-phase stale write) and #61 (bounded per-unit
  renderers) already cover the two traps this feature walks into.

## Shipped (2026-11-01)

Everything in "What lands" is in the code, with 26 new tests (suite 526 green) across `LootRollTest`,
`SalvageFieldTest`, `EcmPulseTest`, `EcmJamTest`, `EcmMineTest`, `EcmMissileTest`, `HealthOrbChaseTest`.

Landed as: `SalvageField` / `EcmPulse` / `EcmWave` / `EcmDisabled` / `MissileStunned` components,
`EcmStats`, `EcmSystem` (Resolution, after `PickupMagnetSystem`), the five system guards plus the mine
bounce in `CollisionSystem.ResolveMineVsPlayer`, the sweep in `PickupMagnetSystem`, `EcmRenderer`
(arcs + wave), the two capsule passes in `WorldRenderer`, `MissileStunned` plume-off, flame-off in
`ThrusterFlameRenderer`, ECM colours in `Palette`, an ECM marker set in `IndicatorRenderer`, and diag keys
`E` / `F` (`DIAGNOSTICS.md`).

Deviations from the plan, all simplifications or additions the player asked for:

- **D4 is not a speed cap, it is a second pull.** `SalvageSweepSpeed = 1000` became
  `CommittedChaseSpeed = 1200`, and swept pickups use a new `ChaseTowardsPlayer` (constant speed, re-aimed
  every tick) instead of `PullTowardsPlayer` with a `pullRegardlessOfRadius` argument. `PullTowardsPlayer`
  keeps its old shape and stays the wreckage pull for uncalled salvage; the sweep passes
  `magnetRadius = float.PositiveInfinity` instead of growing a parameter. XP orbs use the same committed
  chase, so "XP orbs don't really miss the player" and the sweep are one mechanism, not two.
- **Health orbs now match the hull's speed** (added on request, beyond the original spec):
  `LootStats.HealthOrbChaseSpeedFor(playerSpeed) = clamp(playerSpeed × 1.25, 1200, 2400)`. A committed orb
  used to accelerate under a boosting hull's cruise speed and often never landed; a hull doing 1500 px/s
  is now caught in ~0.2 s. `HealthOrbChaseTest` pins the catch, the fuse behaviour and the floor/cap.
- **Sweep state is three radii, not three locals + a bool**: `SalvageSweep(XpRadius, HealthRadius,
  PullTokens)`, with `-1`/pickup-radius meaning "no sweep". Same single-writer property D6 required, one
  value to thread instead of four.
- **Renderer-only knobs stayed in the renderer.** `ArcsPerHull`, `ArcPoints`, `ArcEntityCap`,
  `FlickersPerSecond`, `WaveRingCount` and the ring widths are `EcmRenderer` consts, not `EcmStats`
  fields — the sim has no use for them, and `EcmStats` stayed a sim tuning table. `CapsuleFlashRate` went
  to `LootStats` with the other pickup looks.
- **Test names**: `EcmDisableTest` is `EcmJamTest`; `HealthOrbChaseTest` is new. The "XP credited exactly
  once" test asserts the sweep write survives the collection tick (the #58 shape: one writer per component
  per tick) and that the credit lands once, on arrival, rather than driving two orbs through one hand-built
  Resolution flush.
- **A new TROUBLE_SHOOTING entry after all** (#63): `new HealthOrb()` — a record struct whose parameters
  are *all* optional — binds to the implicit parameterless struct constructor and yields zeros, so a
  hand-built test world's orb expired on the first tick. Production code was never exposed (it always
  passes a radius). Tests now name the values they reason about.

### Perf verdict (2026-11-01, host differs from the recorded baselines so ms are informational; the gates
are not)

    .tools/scenario.sh impact-storm --sim --iters 3 --check-baseline
      median 3.69 ms (budget 8.33) 3/357 frames over; gate ok — median 0.69×, p99 0.92×; entities −0.2 %
    .tools/scenario.sh missile-384    --sim --iters 3 --check-baseline
      median 3.05 ms (budget 8.33) 5/357 frames over; gate ok — median 0.85×, p99 1.12×; entities −0.6 %
    .tools/scenario.sh impact-storm --iters 1        (window, software render in this sandbox)
      world.ecm 0.00 ms, world.salvageFields 0.00 ms, world.ecmPulses 0.00 ms; EcmSystem 0.047 ms = 2.2 % of sim

**Keep.** The guards and the new system are inside the noise of both scenarios — nothing in either seeds an
ECM capsule, so the measured cost is the five `TryGetComponent` guards plus an `EcmSystem` pass over an
empty set, and it is free. The entity delta is the drop table, not a leak: ship kills now spend 5.5 % of
their roll on capsules that used to be nothing.

What the scenarios cannot see, and the honest state of it: the sweep's command burst and the arcs were
**not exercised at runtime** — a scripted run never flies into an ECM capsule, so `world.ecm` reported 0.00
ms because nothing was jammed rather than because the pass is cheap. Their cost is bounded by construction
(24 entities × 3 `DrawLineStrip` calls, 3 rings per wave) and their behaviour is covered by the sim tests.
To see them: `SPACEVORS_DIAGNOSTIC=1`, press `E` or `F`, and read the `renderPasses` table.
