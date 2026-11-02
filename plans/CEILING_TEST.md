# Ceiling test — how far toward 100k objects

Status: shipped

## Goal

One number that says how close the project is to the AGENTS.md goal (10k objects at 120 fps,
later 100k): the largest entity count whose measured frame/tick stays under the frame budget.
It must be quick, repeatable, comparable week to week, and it must **not** create or disturb
any perf gate — the existing budgets are for regressions, this is for headroom.

## The number

`ceiling` = the largest live entity count whose **median** tick/frame time stays under a budget.

- Budget default: **8.33 ms** (120 fps, `MaxFps` / the fixed 1/120 timestep), overridable with
  `--budget` (16.67 = 60 fps).
- Median, never mean: a single gen-2 collection must not decide the answer.
- The reported count is the **live count during the measured ticks** (the `[PERF]` `ents=` column),
  not the number requested. Effects and bullets expire; reporting the requested count would let the
  mix drift silently and the number would lie.
- Sim host additionally reports the count under a **4.17 ms** budget (50% of the frame) as a second
  column. Justification: in the window baselines `fraction.sim` runs 0.23–0.58, so a sim-only run
  that spends the whole 8.33 ms on the sim would not actually be playable. 50% is a stated
  convention, recorded in the row so a future change of the convention is visible.

## Entity mix v1 (pinned in code)

The count is meaningless without a pinned composition, so it is a reviewed constant, like a
scenario spec. v1 mirrors what a busy fight looks like today, measured from the `counts` section
of the three heaviest recorded scenarios (ammo-spray 11456, impact-storm 23882, missile-1024 12931
entities; effects are 58–80% of them):

| per 1000 entities | kind | why |
|---|---|---|
| 650 | sparks/smoke (effects, aged) | what a real fight is mostly made of; no collision, draw-heavy |
| 200 | bullets (in flight, spread along the aim cone) | projectile lifecycle + collision tests |
| 120 | explosions (aged) | short-lived, draw + effect cost |
| 25 | missiles | guidance, targeting, homing |
| 10 | asteroids | physics + asteroid-vs-asteroid collision |
| 3 | mines | |
| 2 | enemy hulls | AI, turrets, hull collision |

Effects and explosions are seeded already aged (the `SeededEffects` trick), so the measured ticks
see steady state instead of a pile-up. A probe measures 12 ticks (0.1 s), so decay during the
measurement is small; the live count is reported to make any drift visible.

The mix version (`v1`) is written into every record row. Changing the mix is allowed — it is the
right response to gameplay changing — but it invalidates the previous rows, so it must bump the
version rather than edit the numbers.

## Probe algorithm (probe, do not ramp)

Ramping one entity at a time costs the sum of every tick along the way (quadratic: ~7 minutes to
reach 100k). Probing is logarithmic:

1. Exponential phase: N = 2k, 4k, 8k, … until the median tick exceeds the budget.
2. Bisect between the last pass and the first fail until the interval is ≤ 5% of N.
3. Report the largest passing N and its median.

Per probe: build a fresh world of N (`WorldFactory.Create` + a new `ScenarioWorld` seeding helper,
radius ∝ √N so density matches play), discard 2 warm-up ticks, measure 12 ticks with the same
`Stopwatch`-per-tick loop `ScenarioBench` already uses, print a row. ~6 + ~5 probes cover 2k→128k.

## Hosts

**Sim (default, `.tools/ceiling.sh`):** `ScenarioBench` gains a `ceiling` mode. No window, no GPU,
`render=0`. Deterministic: ±1–2%, 2–3 s total.

**Window (`.tools/ceiling.sh --window`):** one window for the whole search — the process starts once
and swaps worlds between probes, so the ~1 s startup is paid once instead of once per iteration the
way `scenario.sh` pays it. Expected **10–15 s** total: ~11 probes × (0.1–0.3 s world build + 14
frames at ≤ 8.33 ms). Reliable to **±5%** (≈ ±800 objects at 16k) because a 12-frame median against
the observed ±20% frame-to-frame spread gives about ±5% on the median. Take the better of two runs
for the record (≈1.6× runtime) rather than more ticks per probe; ±2% would need 25+ ticks and
40–60 s, which the sim host already covers.

In the window variant the entities are placed within and around the viewport so the renderer
actually draws them. The v1 mix keeps colliding bodies at ~1.2% of the world, so the sim stays
close to linear while the **draw path becomes the limiter** — that is the point of the window
number, and a `[PERF]` row where `sim=` suddenly dominates is a finding to report, not a bug.
A later variant could zoom the camera to hold everything visible at constant density; that needs a
`Zoom` field on `Camera` (today `Camera(Target, Drift)`) and is deliberately out of scope.

## Record

`.tools/run/ceiling.tsv`, appended, one row per run (gitignored like the rest of `.tools/run/`):

```
date                 host    mix  budgetMs  passCount  failCount  medianMsAtPass  entsAtPass  gc2  seconds
2026-11-03T09:12Z    sim     v1   8.33      17408      18944      7.91            17102       3    2.4
```

`.tools/run/index.tsv` is left alone: its columns are scenario-shaped and a ceiling row would
pollute the table a perf gate reads.

## Non-goals

- **No baseline, no gate.** No `.tools/baselines/ceiling.json` is ever written, `--check-baseline`
  never sees it, and the exit status is 0 unless the bench itself crashed. The number is a record to
  compare by eye, so it cannot make anything nervous.
- No changes to existing scenarios, budgets or `ExpectedOverBudget` flags.
- Not a test of the GPU's absolute throughput: at play-like density most objects are off-screen and
  culled, so the window number is bounded by the draw path, not by raw fill rate.

## Risks and mitigations

- **GC.** A 100k-entity world allocates continuously; gen-2 cost is part of the answer, so the row
  records `gc2` like `[PERF]` does, and the median (not the mean) decides the pass.
- **Superlinear collisions** if density grows. The √N spawn radius holds density constant; if a
  probe's `sim=` jumps far out of line with N, report it as a scaling finding.
- **World swaps in one process** could leak entities or textures between probes. The probe must
  fully unload the previous world (the same teardown the sim bench already runs per process) and the
  run must print entity/texture counts at the end so a leak shows up.
- **Host noise.** Recorded numbers are only comparable on the same host and with nothing else
  loaded; the row carries the host tag and the date, and the window number takes min-of-two.

## Order of work

1. `ScenarioWorld.SeedMixedWorld(em, player, count, seed)` — the v1 mix, radius ∝ √N, effects aged.
   Small helpers, ~20 lines each; reuses the existing volley/effect/missile seeding.
2. `src/ScenarioBench/Ceiling.cs` — probe (build, warm up, median of 12) and the exponential + bisect
   search; `Program.cs` gains the `ceiling` argument and the budget flag. Nothing existing changes.
3. `.tools/ceiling.sh` — run it, append the row, print the headline line, no gate.
4. Window mode: same probe, driven inside one open window; HUD line with N and ms/tick.
5. Record the first numbers (sim + window) in this file under a dated `## Shipped` section and add
   the PLAN.md index line.

## Files this touches

`src/Domain/Scenarios/ScenarioWorld.cs`, `src/ScenarioBench/Program.cs`,
`src/ScenarioBench/Ceiling.cs` (new), `.tools/ceiling.sh` (new), `ARCHITECTURE.md` (one tooling
line), `PLAN.md` (index line). No change to `Game`, the render layer, or existing scenarios.

## Shipped (2026-11-02)

What landed:

- `src/Domain/Scenarios/CeilingMix.cs` — the pinned v1 mix (per 1000: 650 sparks, 200 bullets, 120
  explosions, 25 missiles, 10 asteroids, 3 mines, 2 hulls) and `SpawnRadius = 700 px · sqrt(N/1000)`.
  `ScenarioWorld.SeedMixedWorld` builds it; `ScenarioSpec.CeilingCount` routes it through `Apply`, so
  both hosts build the same world. `src/Tests/CeilingWorldTest.cs` pins the mix, the disc sampling and
  the spec field (562 tests pass).
- `src/Domain/CeilingProbe.cs` — the search (double until the first miss, then bisect to 5%), the
  shared budget / bracket / warm-up constants, and the two output lines. It moved out of ScenarioBench
  so the window and sim hosts cannot drift apart; the sample and bracket records are namespace-level.
- `src/ScenarioBench/Ceiling.cs` plus a `ceiling` argument in `Program.cs` — the sim host.
- `SpaceVorsApp` + `GameSession` — `SPACEVORS_CEILING=<budgetMs>` (with `_START`, `_MAX`) runs the same
  search inside one open window, one `GameSession` per probe, the frame times read in process through
  the new `GameSession.ScenarioFrameMs` / `ScenarioSimMs` / `ScenarioRenderMs` / `ScenarioLiveEntities`.
- `.tools/ceiling.sh` — publishes ReadyToRun the way `scenario.sh` does, runs either host, tees the log
  to `.tools/run/ceiling-<utc>/`, appends one row per bracket to `.tools/run/ceiling.tsv`. No baseline
  file, no gate, exit 0. raylib's INFO banner is filtered from the screen but kept in the log.

Deviations from the plan:

- Two brackets per run instead of one: `bracket=frame` (the 8.33 ms budget) and `bracket=playable`
  (4.17 ms, i.e. half the frame left for rendering). The playable bracket re-probes rather than
  interpolating, because interpolating would pretend to know the cost curve better than the probe does.
- `ents` reports what was alive during the measured frames, not what was asked for: expiring explosions
  give birth to smoke, so the live count lands ~18% above the requested count. `Game` did change, and
  `SeedMixedWorld` now also silences the player's turrets and pins its health at 1e6 — otherwise the
  homing rounds kill the ship inside the measured window and the turrets add bullets on top of the
  pinned mix, so the probe would not be measuring one variable.
- The planned HUD line was dropped: a probe finishes in about a second and the `[CEIL]` rows already say
  it all. Zoom-out was left out too — at constant density the on-screen set stays near what one 1920x1024
  screen holds (~1280 objects) at every count, so the window number tracks the sim number plus a
  constant; a render-limited ceiling would need a `Zoom` on `Camera` and is a separate decision.
- A throwaway probe runs before each search (unreported). Without it the first probe pays JIT warm-up on
  the sim host and shader compilation plus the first texture uploads on the window host - the black
  screen before frame 1 - and the smallest count reads a third slower than it is.

Perf record (sandbox: shared CPU, software Mesa. Direction only, never an absolute):

| host | bracket | count asked | ents live | median | notes |
|---|---|---|---|---|---|
| sim | frame 8.33 ms | 58k-76k | 69k-90k | 8.08-8.20 ms | 19-23 probes, 0.6-2.2 s total |
| sim | playable 4.17 ms | 30k-39k | 35k-46k | 4.05-4.09 ms | |
| window | frame 8.33 ms | ~1000 | ~1150 | ~7 ms | llvmpipe rasterises the frame, not the sim |

Two repeats of the same sim search differed by ~30% (58k vs 76k), which is the shared sandbox CPU and
not the probe; under llvmpipe the window medians are not even monotonic, so its bracket is only
trustworthy on real hardware. The numbers for the AMD RX 7900 XTX are still to be recorded: run
`.tools/ceiling.sh` and then `.tools/ceiling.sh --window` on the desktop with nothing else loaded.

Reference point, so the first number is not a shock: the recorded `impact-storm` baseline already holds
23,882 live entities at a 5.20 ms median window frame, so the 10k-at-120-fps goal is met with room to
spare, and the sim half of 100k in this mix is within reach even on this sandbox.

## Follow-up (2026-11-02): real-hardware numbers

AMD RX 7900 XTX, clean desktop. `.tools/run/ceiling-20261102T131343Z` (sim) and `…T131411Z` (window).

| host | 8.33 ms bracket | 4.17 ms bracket | 100k live would cost |
|---|---|---|---|
| sim | 58,000 asked / 69,173 live @ 7.99 ms | 30,000 / 35,017 @ 4.06 ms | 11.75 ms |
| window | 40,000 asked / 47,094 live @ 7.90 ms | 18,500 / 22,166 @ 4.06 ms | 16.14 ms |

Cost model, least squares over the 19 / 20 probes of those runs (worst fit error 0.24 ms sim, 0.14 ms
window) — the numbers are worth keeping because they let any future change be predicted, not just timed:

- sim ≈ 0.03 ms + **117 ns per live entity per tick**; window ≈ 0.70 ms + **154 ns per live entity per
  frame**, so the renderer adds 0.70 ms fixed plus ~37 ns per entity.
- Linear all the way from 2k to 77k: no broadphase or collision knee, which is what the √N spawn radius
  was for. A knee would show up as a bend in this line, and there is none.
- The 0.70 ms intercept agrees with `baseline-idle` (51 entities, 0.73 ms median frame): an empty world's
  whole frame is overhead, and the probe's fixed cost is that same overhead.
- At the ceiling 70% of the frame is sim and ~23% is the renderer's marginal share: **the 120 fps ceiling
  is sim-bound, not render-bound.** Lit-sprite work moves the 0.70 ms and the 37 ns, the sim moves the 117.
- Repeatability: the two searches inside one run agree to 0.3-2% above 8k entities, 4-8% at 2-4k. The
  ±30% sandbox spread in the Shipped section was harness contention, not the probe.
- Spikes: max/median is 1.03-1.10 up to 16k and 1.35-1.63 above 32k. At the 40k pass point the worst
  measured frame was 11.43 ms, so "median under budget" is not "no dropped frames"; keeping *every* frame
  under 8.33 ms puts the window ceiling nearer 26k asked / 32k live.

Mix honesty: v1 is 65% sparks and 20% bullets, the cheapest-per-entity mix of anything recorded. Reading
the same model off the existing baselines (frame median minus 0.70 ms overhead, over live entities) gives
ceiling mix 154 ns per entity, impact-storm 188, ammo-spray 201, missile-1024 **343**. A missile-heavy
100k would cost ~35 ms a frame, so "100k" only ever means 100k of *this* mix - which is exactly why every
row carries `mix=v1`.

Against the goal: 10k at 120 fps is met 4.7x over (47k live inside one frame). 100k at 120 fps is ~2x
away (16.1 vs 8.33 ms) and that 2x has to come out of the sim's 117 ns. 100k at 60 fps is already inside
measurement distance - the fit says 103k live fits 16.67 ms - so the next run worth doing is
`.tools/ceiling.sh --window --budget 16.67 --start 64000 --max 200000`.

Still not measured by the probe: RSS and the p99 shape, by design (it takes the median). The scenario
baselines own those numbers.
