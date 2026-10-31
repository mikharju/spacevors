# PARALLEL_SIM.md — parallel computing in the ECS hot path

Status: proposed (2026-10-31, not implemented)

Scope: the fixed-timestep simulation tick (Domain). Rendering/audio/input untouched.

## 1. Why this is hard here (the three constraints)

The design currently guarantees things that naive threading breaks:

1. **Determinism** — ARCHITECTURE.md "Determinism": one world `Random` (seed 42), same seed → same run,
   asserted by `Tests/WorldRngTest.cs`. Any parallel region that draws randomness, or whose *order*
   feeds later order, breaks this.
2. **Order-sensitive writes** — ARCHITECTURE.md "Write patterns": command order is last-write-wins, and
   `CommandProcessor` deliberately uses a `List` so storage layout (swap-pop) is *contractual*.
   Storage layout decides next tick's iteration order → floating-point results. Parallel producers
   appending to a shared list = nondeterministic world.
3. **Shared mutable accumulators** — `CollisionSystem` accumulates into `Dictionary<Entity,…>` and
   `HashSet<…>` fields; float accumulation is not associative, so accumulation order changes results
   bit-for-bit.

`Random` is also **not thread-safe** (`System.Random` docs): concurrent draws are a data race, not just
a determinism problem.

## 2. Where the time actually goes (documented measurements)

Budget: 8.33 ms/tick at 120 fps (`Tests/PerformanceBenchmark.cs`).

| Evidence | Number |
|---|---|
| `CollisionSystem` at ~15k bullets (PLAN.md, bullets-as-lines) | ≈ 7.3 ms/tick — ~88% of budget |
| `TurretFiringSystem` at 1k ships (PLAN.md, targeting) | ≈ 1.1 ms/tick |
| `DamageEffectSystem` at 1k ships (PLAN.md) | ≤ 0.09 ms/tick |
| Scenarios currently over budget (`mustMeetBudget: false`) | 1k ships + 100 asteroids; 1k asteroids + 100 ships; "1k of everything" |
| `SpatialGrid` micro-benchmark (10k entities, insert+clear + 1000 queries) | asserted < 15 ms/tick |

Conclusion: **CollisionSystem is the only thing worth parallelizing today.** Everything else is
either already inside budget or memory-bound and tiny.

Caveat: TROUBLE_SHOOTING.md #28 — benchmark numbers are noisy; any claim needs 3 runs of the same
scenario, with and without the change.

## 3. Prerequisites — do these before any thread is created

### P1. Determinism harness (blocking gate)

- Add a state-hash test: run N ticks with a fixed seed, hash every component of every entity
  (positions, velocities, health, entity ids, storage order). Assert equality across runs.
- Add a `SPACEVORS_PARALLEL=0|1` switch so the same test can compare serial vs parallel and require
  **bit-identical** output. Without this gate, parallel work is unverifiable.

### P2. Make ordering explicitly deterministic (already fragile serially)

These are latent determinism bugs that parallelism would turn into visible ones:

- `CollisionSystem._entitiesToDestroy` is a `HashSet<Entity>`; destroy order → swap-pop layout →
  next-tick iteration order. Replace with a deterministic list (sorted by entity id, or insertion
  order in a `List` + dedup by a bitset indexed by entity id).
- `CollisionSystem._effectsToSpawn` is a `HashSet<(Vector2, MineType)>`; its iteration order drives
  `SpawnSpark` → `view.Rng` draw order. Replace with a list + deterministic dedup key.
- `CollisionSystem._shipDeathData` / `_hitMines` iteration order drives loot + spark RNG draws.
  Same fix.

### P3. RNG: replace the single sequential stream

Any parallel region must not touch `view.Rng`. Two options:

- **a) Move all randomness out of parallel regions** (recommended first step): detection stages
  collect hit events; sparks/loot/scatter are spawned serially afterwards in a fixed order.
  Cheapest, keeps `Random` as-is, keeps `WorldRngTest` meaningful.
- **b) Counter-based per-entity/per-worker streams**: `seed' = hash(worldSeed, tick, entityOrWorkerId)`,
  splitmix64/xoshiro per draw site. Needed if we later want parallel loot/spark generation.
  Once chosen, the hash must be frozen — it changes every replay.

### P4. Replace the allocation-heavy shared structures (these are also big serial wins)

- **Broadphase**: `SpatialGrid` is `Dictionary<(int,int), List<SpatialItem>>` + a shared
  `HashSet<Entity> _seen` cleared per query. Per-query HashSet clear/add is the dominant query cost,
  and it is inherently single-threaded. Replace with a **flat counting-sort grid**: per-cell counts →
  prefix sums → one `int[]` of item indices. Read-only after build → safe for N concurrent readers,
  no per-query dedup set (dedup by "only test pairs with `a.Id < b.Id`", and insert each entity into
  exactly one cell + pad the query radius).
- **Per-entity accumulators**: replace `Dictionary<Entity, Vector2/int/float>` in `CollisionSystem`
  with arrays indexed by entity id (or slot) + a touched-list. Parallel workers then write
  **disjoint cells** — no locks, no contention, deterministic merge.
- **CommandBuffer**: `List<ICommand>` boxes every `AddComponentCommand<T>` record → thousands of heap
  allocations per tick. Replace with per-component-type typed arrays (or direct ref writes for the
  hot ones). This is a prerequisite for parallel producers (they need pre-sized, allocation-free
  output) and a serial win on its own.
- **Inner-loop data layout**: the narrowphase does `view.GetComponent<Position>(candidate.Id)` per
  candidate — a `Dictionary<Type, object>` lookup + slot probe per candidate. Gather
  `Position`/`Radius`/`Kind` into contiguous SoA arrays indexed by broadphase item index.

### P5. Hot-path hygiene (cheap, do first, improves measurement trust)

- `CollisionSystem.Update` starts **5 `Stopwatch` objects unconditionally per tick** (lines 38, 71,
  119, 154, 189) — `Stopwatch.StartNew()` allocates. `SimulationRunner` already gates timing behind
  a null callback; do the same here.
- `Vector2.Magnitude` uses `(float)Math.Sqrt` (double); `MathF.Sqrt`/`MathF.Sqrt` + `LengthSquared()`
  (CODE_REVIEW.md chunk 1 minor).
- The asteroid loop and the ship loop each run **two** grid queries per entity
  (`r` and `r + 15f`); one padded query + kind filter halves broadphase work.

### P6. Freeze the read-only contract

During a parallel region: no `AddComponent`, no `DestroyEntity`, no `RemoveComponent`, no
`GetComponentRef` held across structural change (the documented invalidation contract), no
`DiagnosticLogger` Console writes. Encode this as: parallel regions take a read-only snapshot
(the SoA arrays from P4), never `WorldView`.

## 4. Good places to convert (ranked)

### Tier 1 — worth it

1. **Broadphase build** (`CollisionSystem` grid insert). Two-pass counting sort: parallel count pass,
   serial prefix sum, parallel scatter pass. Scales linearly, no shared mutation.
2. **Ammo swept-capsule detection** (the loop over `Ammo, Position` + `QueryBox`). Read-only against
   the grid; each bullet is independent; per-worker hit lists. This is the loop that costs 7.3 ms at
   15k bullets. Highest payoff.
3. **Asteroid↔asteroid / ship↔ship / ship↔asteroid / *↔mine narrowphase**. Independent pair tests;
   `Id.Value <= aEntity.Value` already halves the pairs. Per-worker accumulator arrays.

### Tier 2 — only if measurement says so

4. **`TurretFiringSystem` target selection** — O(turrets × targets), the only other quadratic loop
   (1.1 ms at 1k ships; it grows badly at 10k). Split: **selection** is read-only and per-turret
   independent → parallel; **firing** consumes `view.Rng` (speed variation) and emits commands →
   keep serial in turret-id order.
5. **`EnemyShipSystem`** per-ship AI — each ship writes only its own components, no RNG → clean
   data-parallel candidate, but not a measured bottleneck.

### Tier 3 — do **not** parallelize

- `PositionIntegrationSystem`, `AmmoLifetimeSystem`, `PhysicsSystem`: pure loops but tiny and
  memory-bandwidth-bound; `Parallel.For` dispatch (~1–10 µs) is the same order as the loop itself.
  Revisit only at 100k entities.
- `CommandProcessor.Apply` / `CommandBuffer.Apply`: structural mutation (create/destroy, swap-pop) —
  must stay single-threaded.
- `PickupMagnetSystem` (direct `Player.Xp` ref write read later in the same phase), `LevelUpSystem`,
  spawn systems, death/loot passes: RNG + structural changes + same-phase read chains.
- System-level parallelism across a phase: tempting (Movement's three systems are independent), but
  the shared `CommandBuffer` and same-phase read chains (`PickupMagnet` → `LevelUp`) make it fragile
  for a small win.

## 5. Expected speedup (estimates — must be measured)

Amdahl with parallelizable fraction `p` of the tick. In collision-heavy scenarios the three Tier-1
loops are roughly `p ≈ 0.5–0.65` of tick time (the resolution/death/loot/command flush and the
serial grid prefix-sum + merge stay serial).

| Configuration | Estimated tick speedup |
|---|---|
| 4 workers, ~80% parallel efficiency | **1.5–1.8×** |
| 8 workers, ~60–70% efficiency (contention + memory bandwidth) | **1.7–2.0×** |
| Infinite workers (Amdahl ceiling) | 2.2–2.9× |

So: **expect ~1.5–2× on the simulation tick, not 4× or 20×.** End-to-end fps gain is smaller whenever
rendering dominates (in this sandbox llvmpipe renders at ~4 fps, so in-game fps will barely move —
verify with the headless benchmark, not with in-game fps). And the user's observed **70–90 fps on a
powerful machine** is already short of the 120 fps the sim benchmark hits, which is direct evidence
that the in-game frame is not sim-bound at today's object counts — see §11 before writing any threads.

Prerequisite work (P4/P5) is not just an enabler — it is expected to give **1.5–3× serially on its
own** (no boxing, no per-query HashSet, flat grid, one query instead of two). Combined realistic
outcome: **2.5–5× total**, with threads contributing the smaller half. That reframing matters:
**the algorithmic/structural fixes are the bigger lever; threads are the last 1.5–2×.**

At 100k objects threads are not sufficient: the broadphase must be O(N) and allocation-free first.

## 6. Parameters that must be decided (nothing here is automatic)

| Parameter | Recommendation | Why it is a decision |
|---|---|---|
| Worker count | `clamp(physicalCores/2, 1, 4)` conservative default; `SPACEVORS_WORKERS` override; final value from the §13 sweep | This box reports 20 vCPUs (KVM, no SMT), and `ProcessorCount` counts logical CPUs while ignoring hybrid P/E cores. The game will rarely be alone, so oversubscribing is a real user-facing cost. |
| Serial threshold per loop | start at ~2048 items; measure the crossover | Below it, dispatch cost exceeds the win. |
| Partitioning | static equal ranges over entity-index space, chunk 512–1024 items | Dynamic partitioning costs atomics + breaks deterministic merge order. |
| Parallel regions per tick | 3 (broadphase build, pair narrowphase, ammo detection) | One region per entity/system = dispatch overhead dominates. |
| Per-worker accumulator count | = worker count, merged in fixed worker-index order | Merge order **is** the determinism contract. |
| Main thread | participate as worker 0 | Avoids one idle core and one extra hand-off. |
| Worker lifetime | start with `Parallel.For`; move to a persistent spin-wait pool only if measured dispatch > ~50 µs/tick | Spin-wait burns CPU and hurts render; only take it if the data demands it. |
| RNG streams (if P3b) | one per worker, seeded `hash(seed, tick, worker)` | Frozen forever once shipped; changes all replays. |
| GC mode | benchmark Server GC vs Workstation; allocation budget per tick | Parallel regions multiply allocation pressure. |
| Determinism switch | `SPACEVORS_PARALLEL=0/1`, default off until the hash test passes | Needed for A/B and for rollback. |

## 7. What .NET does and does not do automatically

Does **not**:
- **No automatic loop parallelization.** The JIT never turns a `for` into threads. `Parallel.For`
  always parallelizes — it does not decide *whether* it is worth it, so the threshold is ours.
- **No automatic vectorization of this code.** The JIT auto-vectorizes only simple scalar loops;
  `Vector2` struct loops and method-call bodies are not vectorized. SIMD needs explicit
  `System.Numerics.Vector2` / `Vector<T>` / `Vector256`.
- No automatic determinism, no automatic task-granularity tuning, no automatic contention handling.

Does:
- **Thread-pool sizing** (hill-climbing) is automatic — but that governs blocked/I/O tasks, not the
  fan-out width of a compute loop. It will not pick your worker count.
- `Parallel.For` **default range partitioning** picks chunk sizes heuristically and preserves order;
  override with `ParallelOptions` / an explicit `RangePartitioner` when we want control.
- **Server GC** parallelizes collections automatically; tiered compilation + PGO warm up the first
  ticks (a cold-JIT first run is not a valid measurement — see TROUBLE_SHOOTING #28).

## 8. Staged plan (small commits, each independently verifiable)

**Stage 0 — measure (no behaviour change).**
0a. **Find the real limiter first** — *done 2026-10-14*: `[SIM] tick` / `[APPLY] <phase>` /
`[RENDER] frame` lines added to `DiagnosticLogger` + `GameSession`. Run the game with
`SPACEVORS_DIAGNOSTIC=1` and compare `[SIM]` against `[FRAME] dt=`. If sim is a small share of the
frame, stop: the work belongs in the render path, not in sim threads.
0b. Remove the unconditional `Stopwatch` allocations in `CollisionSystem` (P5). Capture per-scenario,
per-sub-stage timings as the frozen baseline: `dotnet test src/Tests/Tests.csproj --filter
"FullyQualifiedName~PerformanceBenchmark"`, 3 runs, record the same scenario lines.
0c. Add the worker-count sweep from §13 to pick the worker count and the serial threshold by data.

**Stage 1 — determinism gate.** State-hash test + `SPACEVORS_PARALLEL` switch. Must pass on the
unmodified tree first.

**Stage 2 — deterministic ordering (P2).** Ordered destroy list, ordered effect/death/loot lists.
Pure refactor; hash test must be unchanged.

**Stage 3 — serial structural wins (P4/P5).** Flat counting-sort grid, array-indexed accumulators,
typed command arrays, SoA candidate arrays, single padded query. Measure after each.
Expected: 1.5–3×. **Stop and re-evaluate here** — the goal (10k @ 120 fps) may already be met.

**Stage 4 — first parallel region.** Ammo detection only, 4 workers, per-worker hit lists, serial
merge in worker order. Acceptance: hash test bit-identical to serial; benchmark ≥1.2× on ammo-heavy
scenarios; no regression on small scenarios.

**Stage 5 — parallel broadphase build + pair narrowphase.** Same acceptance gates.

**Stage 6 — optional:** turret selection split (Tier 2 #4), worker-count tuning, persistent pool.

Rollback: `SPACEVORS_PARALLEL=0` keeps the serial path; the serial path stays maintained (it is the
test oracle), so parallel code is additive, not a replacement.

## 9. Risks

- **Determinism drift** — the main risk; mitigated only by the Stage 1 hash test.
- **False speedups** — sandbox timing noise (TROUBLE_SHOOTING #28) and in-game fps being render-bound.
- **Contention/false sharing** on accumulator arrays — pad per-worker arrays, keep writes disjoint.
- **Complexity vs AGENTS.md** ("prefer deleting code", "keep functions ~20 lines", "never optimize
  prematurely"). Parallel code is a permanent maintenance tax; Stage 3 may make Stages 4–6
  unnecessary. Decide with numbers at the Stage 3 gate.
- **Alternatives that may beat threads**: SIMD (`Vector256` over SoA positions), sort-and-sweep or a
  hierarchical grid instead of the uniform grid, and capping live ammo. Cheaper to keep
  deterministic; worth a measurement pass if Stage 3 under-delivers.

## 10. Decisions (answered by the user 2026-10-14)

1. **Target:** no fixed 100k commitment — "any speedup would be nice". Observed in-game:
   **70–90 fps on a powerful machine**, which is *below* the 120 fps the sim benchmark targets,
   so the in-game limiter is not proven to be the sim. See §11.
2. **Determinism:** "same seed + same worker count" is acceptable. The plan still recommends the
   stronger guarantee because it is nearly free — see §12.
3. **Worker count:** decide by measurement, and be conservative (the game will rarely be alone).
   Add a worker-count sweep benchmark — see §13.

## 11. Is 100k @ 120 fps realistic? (cost model, not a measurement)

Budget is 8.33 ms for **sim + render together**. Extrapolating the one measured point
(`CollisionSystem` ≈ 7.3 ms at 15k ammo ≈ **0.49 µs/entity**):

- **Sim:** 100k entities at the same local density ≈ **49 ms/tick serial**. Only true if density
  stays constant (a much bigger map). The benchmark clusters everything in r=150, where cost is
  quadratic — at 100k clustered around the player there is no fix.
- **Render:** the bigger wall. Every renderer pass iterates **all** entities and culls per entity
  (`WorldRenderer.Draw` runs ~15 passes; `RenderHelpers.IsOffScreen` is applied after a component
  lookup). At 100k that is ~15 × 100k lookups ≈ tens of ms of CPU before any GPU work. GPU cost is
  separate: `RenderBench` has never measured past **800 lit sprites**, and the lighting shader runs
  up to 16 point lights per fragment.
- **Memory:** `_entityIdToSlot` is sized to the highest entity id ever created and ids are never
  recycled (CODE_REVIEW.md P2 #12). A 10-minute run firing continuously creates millions of ids →
  several MB per component type, per storage, never freed.

Verdict: **100k @ 120 fps is not reachable without changing things beyond the sim** — SoA/contiguous
data layout, zero per-tick allocation, a spatial index feeding the render path (draw the visible set,
don't scan the world), and distance/size-based graphics LOD. None of that breaks the stated
structural requirements (ECS, command buffer, determinism, hexagonal layering); the binding
constraint is "don't simplify graphics" — 100k lit, 16-light sprites at 1080p is not a 120 fps
workload on consumer GPUs.

Realistic ladder on this architecture: **10k @ 120 fps = achievable** (the current goal);
**20–30k @ 120 fps = plausible** after §3 + §4 Tier 1; **100k = a different engine design.**

First action, before any of it: prove which half of the frame is missing. **Now instrumented**
(added 2026-10-14): with `SPACEVORS_DIAGNOSTIC=1` each tick logs `[SIM] tick: Xms`, each command
flush logs `[APPLY] <phase>: Xms`, and each frame logs `[RENDER] frame: Xms`. If `[SIM]` is a small
share of `[FRAME] dt=`, **sim parallelism buys ~0 fps** and the work belongs in the render path.

Caveat when reading it: `[RENDER]` includes raylib's `SetTargetFPS(120)` pacing wait (raylib waits
inside `EndDraw`), so it floors at ~8.0 ms whenever the game is fast enough to hit the cap. A
sandbox run (llvmpipe, 116 fps, ~85 entities) showed `[SIM] tick: 0.15–0.33ms` against
`[RENDER] frame: 7.95–8.17ms` — i.e. sim is ~2–4% of the frame there, and the render number is
almost entirely the pacing wait. Only a run where fps is *below* the cap (like the reported 70–90 fps)
gives a real render cost.

## 12. Determinism: worker-count dependence is cheaper to avoid than it looks

Accepting "same seed + same worker count" is workable, and bit-exact cross-architecture determinism
is already not guaranteed (`MathF.Sqrt`, JIT differences). But the costs are real:

- If worker count is derived from `ProcessorCount`, it can change **within one session** — other
  load, affinity changes, hybrid P/E core scheduling, SMT toggling, laptop throttling. Then one user
  gets a nondeterministic run and a hard-to-repro bug.
- Tests and benchmarks become configuration-dependent: the hash test must run per worker count, and
  A/B benchmark comparisons get confounded by the machine's state.
- Replays / save-and-replay / "watch a friend's run" would need the same worker count as the
  recording machine.

The strong version costs little: **bucket results by chunk index, not by worker.** Partition the
entity index space into fixed contiguous chunks (e.g. 1024), each chunk writes its own result
buffer, merge in ascending chunk index. Then output is identical to the serial order *and*
independent of worker count — work-stealing stays legal because a stolen chunk still writes to its
own buffer. Cost: `N/1024` buffers (≈98 at 100k), slightly more bookkeeping, and possible load
imbalance if chunks are static. Recommendation: take it for the ammo/pair-narrowphase loops; accept
worker-count dependence only where the strong version is genuinely hard.

## 13. Worker-count sweep (how to decide #3 empirically)

Add `src/Tests/WorkerCountSweepBenchmark.cs` (or a small console runner — Release build only, Debug
numbers are meaningless):

- Grid: each `PerformanceBenchmark` scenario × worker counts `{1, 2, 4, 6, 8, 12, 20}`,
  3 iterations × 60 frames, same seed.
- Report per scenario: ms/tick, speedup vs 1 worker, **parallel efficiency %**, and where the curve
  bends over (the knee). Small scenarios should show parallelism *losing* — that sets the serial
  threshold from §6.
- Contention variant: rerun with N background busy-threads to emulate "the game is not alone" and
  watch the knee move left.
- In-game variant: sweep `SPACEVORS_WORKERS` with `SPACEVORS_DIAGNOSTIC=1`, capture `[FPS]` and the
  sum of `[SYSTEM]` lines, so the decision is made against real frame time, not sim-only time.

Conservative default, given the game shares the machine: `workers = clamp(physicalCores / 2, 1, 4)`,
never more than half the machine, exposed as a settings value + `SPACEVORS_WORKERS` override.
Prefer blocking/yield waits over spin-wait barriers (spin-wait steals cycles from other apps and
heats laptops). Note `Environment.ProcessorCount` counts *logical* CPUs and ignores hybrid P/E
topology, so the sweep — not the core count — should set the default.
