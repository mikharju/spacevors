# PERF_SCENARIOS.md — scripted perf scenarios with fixed start states + trace capture

Status: shipped (verified 2026-10-31)

Written while unimplemented; everything in this file has since shipped — see the Shipped
sections below. Companion to `plans/PARALLEL_SIM.md` (it supplies the measurement that plan's
Stage 0/3/4 gates need).

## 1. Verdict: feasible, and closer than it looks

What already exists:

- **The sim already runs with no window.** `Tests/PerformanceBenchmark.cs` constructs an
  `EntityManager`, adds entities, and drives `SimulationRunner` + `CommandBuffer` + `WorldView` for
  60 frames. A sim-only scenario host is nearly free.
- **Determinism already holds.** One RNG on `EntityManager` (seed 42), **no `Random.Shared` anywhere
  in `src/`**, and `GameInitializer` world gen *and* the starfield/clutter all draw from `em.Rng`.
  `WorldRngTest` asserts same-seed equality. `GameInitializer` already has a diagnostics branch that
  pins the first asteroid to a fixed position/speed "so weapon firing is testable deterministically".
- **Scripted-behaviour precedent exists.** `SPACEVORS_DIAG_UPGRADES="RailGun,Hp,FireRate:MachineGun"`
  (`LevelUpSystem`) already drives game decisions from an env var, one entry per level-up, warning on
  unknown entries. `SPACEVORS_SCENARIO=<name>` is the same seam pattern.
- **Per-weapon rosters exist.** `ShipType.DiagnosticShips` = Shadow copies carrying one weapon each,
  including `LoadTestWeapon`, listed only under `SPACEVORS_DIAGNOSTIC=1`.
- **Headless Raylib precedent exists.** `src/RenderBench/` + `.tools/` (Xvfb, llvmpipe, `shot.sh`,
  `diff.sh`, `input.sh`).
- **Timing seams exist.** `[SIM] tick` / `[APPLY] <phase>` / `[RENDER] frame` / `[SYSTEM]` lines.

What is missing: a named starting state, auto-run-N-ticks-then-exit, machine-readable per-tick output
with entity counts, stored baselines, a trace wrapper, and a way to bypass ship selection.

## 2. Design: one scenario spec, two hosts

**Scenario spec** — plain data + a builder in **Domain** (no Raylib reference):

```
Name, ShipType/Loadout, Seed, MeasureTicks, ViewportSize,
StartState   : spawn primitives — asteroids / ships / mines / **ammo already in flight** /
               effect entities with randomized ages / player health ratio,
               built from AsteroidFactory, EnemyShipFactory, WeaponStats, SpawnPlacement
WeaponOverride (e.g. LoadTestWeapon) + whether the weapon keeps firing while measured
ScriptedEvents: level-up choices (reuse the DIAG_UPGRADES format), timed triggers
                ("kill ship S at tick T", "spawn N mines at tick T")
Budgets      : max median frame ms, max median sim ms
CountRanges  : expected live ammo / sparks / explosions / entities (ranges, not exact)
```

Two hosts consume the same spec:

1. **Sim host** — a small console project `src/ScenarioBench/` (mirrors `RenderBench/`, no Raylib).
   Runs exactly N ticks, prints perf lines. Deterministic, needs no X, CI-friendly, fast.
2. **Game host** — `SPACEVORS_SCENARIO=<name>` makes the real game build from the spec instead of
   normal world gen, skips ship selection, and exits after N ticks.

**Why two hosts:** the delta between them *is* the render cost, measured against an identical world
state. The log analysis showed render is ~77% of the frame, and nothing today measures it against a
real world — `RenderBench` uses synthetic sprite counts, `PerformanceBenchmark` is sim-only.

## 3. Determinism contract ("mostly deterministic", made precise)

Deterministic and assertable: world state per tick, entity counts by kind, RNG draw sequence, spawn
positions, damage, effect counts. Assert with a state hash every N ticks (same gate as
`PARALLEL_SIM.md` §P1 — build it once, use it for both).

Not deterministic — measure statistically instead: per-tick wall time, GC timing, raylib frame
pacing, GPU/llvmpipe behaviour. Mitigations: median + p95 over N ticks, 3 iterations, discard the
first ~30 ticks (JIT warm-up), and never assert on timing columns (TROUBLE_SHOOTING #28).

Pins a scenario **must** set, or runs are not comparable:

- `WorldView.ViewportSize` — world gen places mines/ships via `SpawnPlacement.OutsideScreen(viewport)`
  and the renderer culls by screen box. Fix it at 1920×1024.
- `WorldView.MouseScreenPosition` — `CameraSystem` drifts the camera from the mouse, which changes
  culling and therefore render cost. Pin to window centre.
- **Seed** — `GameInitializer.Initialize` currently hardcodes `new EntityManager()` (seed 42); it
  needs a seed parameter.
- **One sim tick per rendered frame** in scenario mode. Today `dt ≈ 15.8 ms` produces ~1.9 ticks per
  frame, which multiplies sim cost by a wall-clock-dependent factor and makes runs non-comparable.
- **Upgrade pauses** — a `PendingChoice` pauses the sim. Scripted upgrades keep the *choices*
  deterministic, but the pause still burns frames: scenario mode should either apply without pausing
  or count paused frames separately.

## 4. Run structure and output

### 4.1 Start state: seeded — no warm-up phase

A measured window of ~1 s (120 ticks at the fixed 1/120 step) is the right size for something you can
watch, but it is not enough to *reach* an interesting state by playing — `LoadTestWeapon` needs
seconds of firing to build the ~7k live bullets seen in the pasted log. So the scenario builds the
state it wants to measure. There is deliberately **no warm-up phase**: it would add a knob per
scenario, slow every run down, and hide which state is actually being measured. Cold JIT is handled
outside the scenario instead — `--iters N` pools N runs and discards the first whole run (§4.4).

**A. Seed the state directly (default).** The scenario builds the world it wants to measure: bullets
already in flight, an asteroid in the right place, ships in a cluster, player at 10 % hp.

- Instant — no 900-tick ramp (900 rendered warm-up frames ≈ 13 s of wall time at 60 fps).
- The scenario *is* the start state: readable, reviewable, diffable in git.
- Stronger determinism — the measured state does not depend on 900 ticks of every system's behaviour,
  so tuning a fire rate cannot silently change what a scenario measures.
- **Reaches states play cannot produce** — `scale-ladder` at 20k–100k entities is impossible to reach
  by playing, and deliberate worst cases (all bullets inside one grid cell) are only constructible.

**Snapshot files — deliberately not planned.** Dumping and reloading the world would give real
states with instant restarts, but it needs a serializer for ~25 component types and produces an opaque
fixture. Skip it unless seeding proves insufficient.

Rules that keep seeded states honest:

- **Build with the game's own factories and stats**, not hand-typed numbers: `AsteroidFactory`,
  `EnemyShipFactory`, `WeaponStats` for bullet speed and scatter, `SpawnPlacement` for placement, all
  drawing from `em.Rng`. A hand-placed world can be accidentally cheap (bullets in lockstep land in the
  same grid cells and run cache-friendly) or accidentally expensive.
- **Randomize effect-entity ages.** Sparks created at tick 0 all expire on the same tick, producing a
  sawtooth in entity counts and cost. Give each a random remaining lifetime.
- **Check for a settling transient.** Bullets placed touching an asteroid fire a burst of impacts in
  the first ticks. Implemented guard: a seeded bullet is resampled until it is clear of every asteroid.
  Compare the first 20 measured ticks with the last 20; if they still differ materially, move the
  entities apart.
- **Density decays.** 7,400 seeded bullets converging on one asteroid are destroyed as they hit, so the
   measured second covers a falling density. Acceptable because `frames.jsonl` reports counts per frame
   and `perEntity` normalizes by them. Both `ammo-spray` and `ammo-spray-miss` therefore keep the
   turrets **silenced**: `LoadTestWeapon` fires 8,000 pellets per volley, and a volley landing inside
   the measured second doubles the bullet count in one frame and swamps everything else. A sustained
   (still-firing) variant needs a ~2 s window to cover one volley cycle — deferred until the numbers
   from the draining pair are known.

Measured phase: `measure=120` ticks by default, delimited by `[PERF-BEGIN]` / `[PERF-END]` in the log.

### 4.2 Real window, uncapped frame rate, one tick per frame

- Scenario runs open the **real window by default**; `--sim` is the opt-in headless mode.
- **Disable the fps cap in scenario mode** (`SetTargetFPS(0)`). With the 120 cap active, `dt` floors
  at 8.33 ms and both the fps percentiles and the sim/render fractions are meaningless.
- **`--cap <fps>` opts back into pacing** (`SPACEVORS_SCENARIO_CAP`, 0 = uncapped) for the question the
  uncapped run cannot answer: what a player actually feels at the shipped 120 fps target. Expect
  `[RENDER]` to absorb raylib's pacing sleep there — that variant measures pacing, not headroom.
- Force **exactly one sim tick per rendered frame** — the accumulator's ~1.9 ticks/frame is
  wall-clock noise. A slow scenario advances the sim slower than real time; irrelevant, since
  scenarios are defined in ticks.
- Upgrades apply **without pausing** (decision, §10) so `PendingChoice` never inserts frame gaps.
- `--hold <s>` keeps the window open after the measured phase (unmeasured) so a 1 s scenario is
  actually watchable. Default 3 s; `--hold key` waits for a keypress.

### 4.3 Output files

Per run, under `.tools/run/scenario-<scenario>-<utc>/`. `.tools/run/latest` symlinks to the newest
run, `.tools/run/index.tsv` lists every run kept (scenario, host, frames, median/p99 ms, sim share,
budget verdict, directory), and anything older than a day is cleared by the next run. The logs are
gitignored; the scripts that write them are not.

Source of truth is one diagnostics-gated log line per measured frame (reused stopwatch, no
allocation), which the script parses into `frames.jsonl`:

```
[PERF] tick=123 dt=15.78 sim=1.83 apply=0.11 render=12.07 other=0.17 ents=9310 ammo=7369 sparks=412 explosions=37 beams=0 mines=24 ships=18 asteroids=35
```

Entity counts by kind are not optional — cost without a denominator is uninterpretable (the pasted
log only became useful once it was "148 ns per bullet"). Counts come free from `TryGetStorage`.

| File | Contents |
|---|---|
| `frames.jsonl` | one row per measured frame: `tick, dtMs, simMs, applyMs, renderMs, otherMs, ents, ammo, sparks, explosions, beams, mines, ships, asteroids` — the artifact to hand to an LLM |
| `summary.json` | bottom-line stats, below |
| `log.txt` | the existing human diagnostics log (`[SYSTEM]` sub-stages) for drill-down |
| `<name>.nettrace` | only with `--trace` |

`summary.json`:

```json
{ "scenario": "ammo-spray", "iterations": 5, "measuredFrames": 600,
  "frameMs": {"mean":15.4,"median":15.1,"p90":16.8,"p99":19.2,"max":21.0},
  "fps":     {"mean":64.9,"median":66.2,"low1":52.1,"low10":59.5},
  "fraction":{"sim":0.22,"render":0.76,"other":0.02},
  "sim":     {"median":1.85,"p99":2.40},
  "render":  {"median":12.10,"p99":14.00},
  "entities":{"start":9212,"end":9398,"growthPerTick":0.6},
  "perEntity":{"positionIntegrationNs":27,"collisionDetectionNs":148},
  "budget":  {"frameMsMedian":16.0,"pass":true} }
```

Definitions, pinned so they are never ambiguous:

- `low1` / `low10` = the 1st / 10th percentile **frame time**, reported as fps (`1000/p99frame`,
  `1000/p90frame`) — the usual game-industry convention.
- `fraction.*` = Σ stage time ÷ Σ `dt` over measured frames; `other` = input, camera, UI, GC, present
  wait. **Fractions must sum to ~1** — if they don't, the instrumentation is wrong. Useful self-check.
- Percentiles are computed over **all iterations pooled**.
- `perEntity.*` divides a stage's median by the entity count it operates on, so runs at different
  scales stay comparable.

### 4.4 The 1%-low caveat for a 1 s scenario

1% of 120 frames is 1.2 frames — a "1% low" from one measured second is just *the worst frame*, not a
statistic. Rule: **≥1000 pooled measured frames before quoting 1% lows.** With `measure=120` that
means `--iters 8`, or a longer measured phase. Default `--iters 5` (600 frames) is enough for median,
p90 and the fractions; `summary.json` should emit `"low1": null` when the sample is too small rather
than a meaningless number.

`--iters N` means **N measured iterations plus one discarded warm-up iteration** — cold JIT skews the
first run after a build (TROUBLE_SHOOTING #28).

## 5. Proposed scenario catalogue

| Scenario | Isolates |
|---|---|
| `baseline-idle` | the floor to subtract from every other number |
| `ammo-spray` | seeded ~7k bullets converging on one asteroid **plus sustained `LoadTestWeapon` fire** to hold density steady — reproduces the pasted log at t=0 instead of ramping into it |
| `ammo-spray-miss` | same seeded load aimed into empty space, **no firing** → broadphase + detection cost without resolution, loot or effects, and a decaying density that `perEntity` normalizes |
| `impact-storm` | forced N simultaneous impacts → `EffectSystem` + spark/explosion counts + their render cost |
| `hull-damage` | player at 100 / 50 / 10 % hp with the lighting shader → `DamageShading`, procedural hull, `LightGatherer` (16 point lights) |
| `cluster-collision` | 1k asteroids + 100 ships clustered — the scenario that already fails the benchmark budget |
| `mine-field` | 3k mines |
| `death-salvo` | 50 ships killed in one tick → loot, `ShipDeathExplosion` staging, sparks |
| `scale-ladder` | one spec parameterized at 1k / 5k / 10k / 20k entities → the scaling curve for the 10k/100k goal. **Only constructible by seeding** — this is the scenario that justifies §4.1A |

`ammo-spray` vs `ammo-spray-miss` is the highest-value pair: their difference prices collision
resolution + effects directly.

**First batch** (decision, §10): `baseline-idle`, `ammo-spray`, `ammo-spray-miss`, `hull-damage`.

## 6. Shell interface

```
.tools/scenario.sh <name> [--sim] [--measure N] [--iters N] [--hold S] [--cap FPS] [--seed N]
                          [--trace] [--apitrace] [--shot] [--compare] [--out DIR]
```

The runner maps its flags onto the game's scenario seams — `SPACEVORS_SCENARIO`,
`SPACEVORS_SCENARIO_CAP`, `SPACEVORS_SCENARIO_TICKS`, `SPACEVORS_SCENARIO_HOLD` — the same variables a
manual run can set. There is no `--warmup`: a scenario's seeded state *is* the start state (§4.1).

Default is the **real window** (`--sim` opts out of rendering). Steps: build Release once → if running
in the sandbox (`.tools/root` exists) source `.tools/env.sh` and start Xvfb as a harness **background
job**, otherwise use the real display → run `dotnet Game.dll` with `SPACEVORS_SCENARIO=<name>
SPACEVORS_DIAGNOSTIC=1` → parse `[PERF]` → write `frames.jsonl` + `summary.json` → print the summary
table → compare with `.tools/baselines/<name>.json` when `--compare` → exit non-zero on budget breach
→ close the window with `.tools/stop.sh`.

- `--trace` wraps the run: `dotnet-trace collect --profile cpu-sampling,alloc-heavy --sample-rate 1
  -o .tools/run/<name>.nettrace -- dotnet Game.dll`
- `--apitrace` wraps it: `apitrace trace --api gl dotnet Game.dll` → per-frame draw-call counts.
- `--shot` captures a frame at the end of the measured phase via `.tools/shot.sh`, so a scenario can
  assert visual stability numerically with `.tools/diff.sh` (e.g. the `hull-damage` scenarios).
- Sandbox constraints already documented in TROUBLE_SHOOTING #26: long-lived processes must be harness
  background jobs, `DISPLAY=localhost:99`, `HOME=/tmp/dotnet-home`, run `dotnet Game.dll` rather than
  the apphost.

## 7. Prerequisites, in order (each a small commit)

1. **Seed parameter** on `GameInitializer.Initialize(shipType, viewportSize, seed)`.
2. **Scenario spec + start-state builders in Domain**, exposing a small primitive vocabulary
   (`SpawnAmmoInFlight(count, ringRadius, aimAt, weaponStats)`, `SpawnAsteroidCluster`,
   `SpawnShipCluster`, `SpawnEffectBurst(count, randomAges)`, `SetPlayerHealthRatio`) that reuses
   `AsteroidFactory` / `EnemyShipFactory` / `WeaponStats` / `SpawnPlacement`. Then *move*
   `PerformanceBenchmark`'s six setups into it — consolidation, not new code; that benchmark becomes a
   thin consumer.
3. **`[PERF]` per-tick line** + entity counts by kind (diagnostics-gated, reused stopwatch).
4. **Scenario mode in the game**: `SPACEVORS_SCENARIO` → build from spec, skip ship select, pin
   viewport/mouse, one tick per frame, auto-exit after N ticks (stay alive if `--shot`).
   Put this in a new small type (`ScenarioHost`), **not** inline in `GameSession` — it is already
   574 lines and CODE_REVIEW P2 #7 is exactly about this kind of growth.
5. **`.tools/scenario.sh`** + baseline files.
6. Optional: a CI job running the sim host over all scenarios with loose budgets. CI
   (`.github/workflows/build-game.yml`) is currently **build-only** — no test or perf job at all, so
   this would be the first performance guardrail.

## 8. What this buys the parallel-sim plan

Every gate in `PARALLEL_SIM.md` becomes a repeatable A/B on identical worlds: same seed, same entity
counts, sim and render reported separately, plus `dotnet-trace diff` for method-level deltas. It also
answers the question that plan §11 raises — "is the frame sim-bound at all?" — per scenario, instead
of one hand-pasted log.

## 9. Risks and honest limits

- **llvmpipe render numbers in this sandbox are not representative of your GPU.** Use sandbox
  `--render` runs for *relative* comparisons and draw-call counts; run them on your own machine for
  absolute fps.
- **Never assert on timing columns** — flaky by construction (TROUBLE_SHOOTING #28).
- **Scenario drift**: count ranges will break when gameplay tuning changes on purpose. Keep budgets
  loose, counts as ranges, and make a deliberate retune a one-line baseline refresh.
- **Effect entity counts are unbounded by design** — every impact spawns sparks
  (`ImpactDamage.SparkCount`, `MineType.HitSparkCount`); only `DamageEffectSystem` has a per-tick
  budget. Scenarios should *report* these, not cap them.
- No `yield return` in scenario paths; Domain must not reference Raylib.
- A scenario host is test infrastructure: it needs the same readability discipline as the game
  (short builders, one scenario per type, no clever DSL).

## 10. Decisions (answered by the user 2026-10-14)

1. **Sim host = a new console project** `src/ScenarioBench/` (not tests). Keep one cheap test that
   runs the smallest scenario so the spec cannot rot silently.
2. **Upgrades apply without pausing** in scenario mode.
3. **First batch of scenarios**: `baseline-idle`, `ammo-spray`, `ammo-spray-miss`, `hull-damage`.
4. **Scenarios must run in a real window** (visible), and the measured phase should be short — ~1 s.
   That is what §4.1–4.2 are built around: a long unmeasured warm-up builds the state, 120 measured
   ticks are timed, and `--hold` keeps the window up so you can actually watch it.

Still open:

- **Seeded-state fidelity**: how do we know a seeded world is representative rather than accidentally
  cheap? Proposed check — run each seeded scenario once and compare its `perEntity` numbers against
  the same stage measured during a real playthrough (`SPACEVORS_DIAGNOSTIC=1` log). A large gap means
  the seed is unrealistic and the primitive needs fixing.

Resolved since the first draft: warm-up is out (§4.1); the capped variant is a startup parameter
rather than a separate scenario (§4.2).

## Follow-up (shipped, verified 2026-10-31) — sim vs render cost logging


The diagnostic log now shows which half of the frame costs what, so performance work can be aimed at the real limiter (plans/PARALLEL_SIM.md §11).

- `[SIM] tick: Xms` — one line per fixed-timestep tick: all four phases plus their command flushes
- `[APPLY] <phase>: Xms` — the previously invisible `CommandBuffer.Apply` cost, per phase (Movement/Action/Resolution/Intent)
- `[RENDER] frame: Xms` — the frame's draw call (`Renderer.Render` / `Renderer.RenderPaused`), covering the in-game, stats-screen and upgrade-menu paths
- Stopwatches are reused instance fields: nothing allocated per tick/frame, and `DiagnosticLogger` no-ops when `SPACEVORS_DIAGNOSTIC` is off
- `[RENDER]` includes raylib's `SetTargetFPS(120)` pacing wait (raylib waits inside `EndDraw`), so it floors at ~8 ms while the game is at the cap — read it against `[FRAME] dt=`
- Verified headless (Xvfb + llvmpipe, 116 fps, ~85 entities): `[SIM] 0.15–0.33ms` vs `[RENDER] 7.95–8.17ms`; full suite green (248 passed)


## Shipped — perf scenario harness (verified 2026-10-31)


Implements plans/PERF_SCENARIOS.md: scripted scenarios that start from a seeded state, run ~1 s in a real window and record a machine-readable trace.

- `Domain/WorldFactory.cs` — the entity world (player, camera, turrets, 35 asteroids, mines, enemy ships) moved out of `GameInitializer`, so a world can be built without Raylib. `GameInitializer` keeps the starfield and clutter, which still draw from `em.Rng` in the same order.
- `Domain/Scenarios/` — `ScenarioSpec` (ship, seed, bullet count, aim + lateral aim offset, stream length, health ratio, seeded particles, measured ticks, frame budget) plus five scenarios: `baseline-idle`, `ammo-spray`, `ammo-spray-miss`, `impact-storm`, `hull-damage`. `ScenarioWorld` seeds a `LoadTestWeapon` volley already in flight (uniform ages, resampled clear of every asteroid), pre-ages impact particles **at the ray-solved entry points of the seeded bullets** — a cloud spread over the rock reads as impacts that never happen — silences player turrets and sets hull damage.
- What measuring the scenarios taught (visible in `ScenarioBench`, not in the playthrough log): one asteroid impact spawns ~7 particles, and a seeded volley loses ~24 bullets/tick to lifetime expiry alone. So the first `ammo-spray` (stream driven dead centre) ran at ~30 impacts/tick and its 13-19k sparks were a storm, not a mix. `ammo-spray` now grazes the rim (`AimOffsetR: 1.05`, ~7.8 impacts/tick, ~6.8k sparks steady); `impact-storm` (0.3, ~12.4 impacts/tick, ~18k entities) is the explicit worst case.
- `[RENDER-PASS] <pass>: Xms` — one line per draw pass (`clear`, `lighting.begin`, `lights.collect`, `background`, `world.*` for all 15 world passes, `hud.*`, `present`), emitted by `RenderTimings` when diagnostics are on. `summary.json` ranks them by mean ms with their share of the render total. First sandbox run: `world.ammo` 66% of render, `world.player` 12%, `present` 10% (llvmpipe — GPU split still to be measured).
- `[PERF]` rows now carry `gc0/gc1/gc2` collection counts and `allocKb` for the frame; `summary.json` reports collections per run, allocated KB/frame, and the median frame time with and without a collection — the direct test of whether GC explains the sim p99 spikes.
- `SimulationRunner.RunTick` — the four phases with a command flush after each now live in one method, used by `GameSession` and `ScenarioBench` alike.
- Scenario mode in the game: `SPACEVORS_SCENARIO`, `_CAP` (0 = uncapped), `_TICKS`, `_HOLD`; pinned 1920×1024 viewport and centred mouse, one tick per frame, upgrades applied without pausing, auto-exit after the measured ticks.
- `[PERF]` row per measured frame: `dt sim apply render other` ms + counts (ents/ammo/sparks/explosions/beams/mines/ships/asteroids/pickups), delimited by `[PERF-BEGIN]`/`[PERF-END]`.
- `.tools/scenario.sh <name> [--sim] [--measure N] [--iters N] [--hold S] [--cap FPS] [--out DIR] [--compare] [--update-baseline]` and `.tools/scenario.py` produce `frames.jsonl` + `summary.json`: frameMs mean/median/p90/p99/max, fps mean/median/low1/low10, sim/render/other fractions, perEntity ns, entity counts, budget pass. Baselines live in `.tools/baselines/`. It uses the caller's display; in the sandbox prefix `. .tools/env.sh &&` (see TROUBLE_SHOOTING #31).
- `src/ScenarioBench` — sim-only host, no Raylib. Verified in the sandbox: `ammo-spray-miss` with 7,000 bullets, 1.23 ms median per tick, 236 ns/entity.
- No warm-up phase by decision: cold JIT is handled by discarding the first whole run (`--iters N` runs N+1) and the first frame of each pooled run.


## Follow-up (shipped, verified 2026-10-31) — perf gates


- Every scenario now carries the real target, 1000/120 ms. Budgets used to be per-scenario and loose (10-32 ms), which is how a render regression that ate 89% of the frame sat green. `impact-storm` is marked `ExpectedOverBudget` instead: 23,876 entities are knowingly over 120 fps, and clearing that flag is how the shortfall gets closed.
- An unmarked breach exits non-zero, prints `budget: regression median X against Y`, and records the verdict in `index.tsv`. `--check-baseline` gates a run against its committed baseline (median x1.15, p99 x1.25, per-shape cost x2.0) and is host-aware: a sim-host run against a window baseline reports the deltas as information instead of comparing incomparable milliseconds.
- `perShapeNs` — mean pass ms divided by the entities that pass drew — is the render metric that transfers between machines: ~1.1 µs per shape before batching, ~0.1 µs after. A jump back up is the signature of per-shape draw calls returning, which is the regression most likely to be silent.
- `Tests/ScenarioPerformanceTest.cs` drives the scenario specs headless (median of measured ticks, warm-up discarded) against characterized ceilings, replacing `Tests/PerformanceBenchmark.cs` — a second, weaker source of truth with three `mustMeetBudget: false` phases and a stale CollisionSystem claim. The Linux CI job now runs `dotnet test`.
- The habit is written into AGENTS.md: after a sim or render change, run `.tools/scenario.sh <name> --sim --iters 3` and report the delta as a verdict - keep, optimize, or revert.
- The sim test asserts two things per scenario, because they answer differently. **Median tick ms** is machine-bound, so it has a ceiling set per host: desktop (0.5 / 1.0 / 5.0 / 2.5 / 5.0 ms) and CI (1.5 / 3.0 / 15.0 / 7.5 / 15.0), selected by `SPACEVORS_PERF_HOST=ci` which the workflow sets. **Allocated KB per tick** is nearly host-independent, so one budget serves every machine: 15 / 16 / 400 / 16 / 1000 KB, measured at 9.5 / 10.9 / 289.9 / 10.4 / 746.2 - and the real-GPU harness independently measured 296 and 753 KB per frame for the same two scenarios, which is what makes the metric worth gating on. ammo-spray at 290 KB against ammo-spray-miss at 10 KB localizes the allocation to impacts spawning effects, not bullets existing.
- Tests run serially (`Tests/AssemblySettings.cs`): xunit's parallel classes put every other class's work inside the timed ticks, which on a small CI runner was enough to fail a scenario by 5%.
- `[SYSTEM]` lines are now aggregated (`systemMs`, `spikeFrames`, `phaseMs`) instead of sitting unread in 3,600 lines per iteration. Attribution named `CollisionSystem` at 76.4% of `ammo-spray` sim and `Collision: detection` as the frame-9 spike — **but that cost was mostly the JIT, not the broadphase** (see the next bullet). Frame 2's cost is real and deterministic: `Effects: damage spark` processing the seeded particle cloud for the first time.
- The frame tail in a 120-tick scenario window is tiered-JIT promotion, not game code. `DOTNET_TieredCompilation=0` on the same machine: tick median 1.83 → 1.02 ms, p99 12.56 → 2.50 ms, max 22.99 → 3.03 ms, `Collision: detection` 1.10 → 0.36 ms mean. Consequences: the committed baselines are JIT-warm-up-inclusive; the collision broadphase is far cheaper than the attribution suggested; and `plans/PARALLEL_SIM.md` is aimed at a 0.36 ms cost until something more expensive replaces it. What remains after tiering is off is the Effects tail (~2.5–3 ms at frames 33/76) — spark and explosion generation, the deferred design change.
- Implemented: `TieredCompilation=false` in `Game.csproj` and `ScenarioBench.csproj` (the harness must run the same JIT tiers as the product, or it measures a build the game never ships). `Tests.csproj` deliberately omits it — its ms ceilings were characterized tiered, and adding the switch there would silently retune those gates. Result in the harness, no env var needed: `ammo-spray` sim median 1.04 ms, p99 2.73 ms, **0 of 357 frames over the 8.33 ms budget**, `Collision: detection` down to 0.374 ms mean. Tradeoff paid knowingly: eager JIT lands on the first tick (frame-1 dt 39 → 80 ms) while a whole 120-tick run finishes faster (wall 345 → 261 ms). The committed `host=window` baselines predate this and must be retaken on the real GPU host.
- ReadyToRun completes that pair, and it is the part that changed what the player feels. Measured on one host, `ammo-spray`, 3 fresh processes × 120 ticks: first tick **81 → 21 ms** with R2R, at the cost of a slower steady tick (median 0.78 → 1.09 ms). R2R *with* tiering starts cheaper still (13 ms) but ticks slower (1.32 ms), so the shipped combination is R2R + tiering off. No configuration measured had a mid-run hitch above 3.6 ms.
- `scenario.sh` now **publishes** (R2R, `linux-x64`, framework-dependent) and runs that artifact instead of the `bin/Release` build output, for both modes, and prints `artifact: … (ReadyToRun)`. A property that only exists in the game's csproj is invisible to the harness, so `PublishReadyToRun` is set in `ScenarioBench.csproj` too; the publish lands in `bin/Release/net10.0/linux-x64/publish/`. This also means `.tools/game.sh` — which runs `bin/Debug` — is now three steps away from what ships.
- The in-program screenshots were the "frozen for 20-40 frames" the user felt, not the game: each PNG encode stalls the window ~0.1-0.2 s *after* the frame row is written, so `dt` cannot see it. Now only the final run takes shots (3 encodes instead of 12), and the run count is printed up front (`N measured + 1 discarded cold run`). `scenario.py` has one `measured_blocks` exclusion shared by the frame, pass and system tables: frame 1, a shot frame, and its successor are out of every statistic. Pass means corrected accordingly (asteroids −28%, sparks −1%), so a retaken baseline is slightly stricter on `perShapeNs`.
- The remaining tail after all of this is particle generation, not broadphase: `Effects: explosion` 2.09 ms at tick 33 and `Effects: damage spark` 2.06 ms at tick 76, against 0.37 ms of detection. That is the deferred spark-generation design change (≈7 particles per impact), and it is now the biggest game-side lever.
- `impact-storm` met the 120 fps budget after shape batching (4.76 ms), so its `ExpectedOverBudget` mark was removed. `hull-damage`'s per-shape canary was a false alarm (fixed pass overhead divided by a handful of sparks), so `perShapeNs` now needs ≥100 drawn shapes.

