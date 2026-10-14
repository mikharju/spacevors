# PERF_SCENARIOS.md — scripted perf scenarios with fixed start states + trace capture

Status: **plan only — nothing in this file is implemented.** The `[SIM]` / `[APPLY]` / `[RENDER]`
lines this plan builds on *are* implemented (added 2026-10-14, still uncommitted in
`DiagnosticLogger.cs` + `GameSession.cs`); that is the only perf code that exists so far.
Companion to `plans/PARALLEL_SIM.md` (it supplies the measurement that plan's Stage 0/3/4 gates need).

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

Per run, under `.tools/run/<scenario>-<utc>/`:

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
