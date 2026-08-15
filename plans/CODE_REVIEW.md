# CODE_REVIEW.md

Full codebase review, split into chunks to fit a 100k context window.

## Size estimate

- C# source (tracked, excl. assets/obj): ~7,030 lines ≈ 75k tokens
- Docs/plans: ~1,200 lines of markdown
- Whole repo in one pass leaves no room for analysis → split into chunks below.
- Budget per chunk: ≤ ~35k content tokens, leaving headroom for findings + conversation.

## Review process (per chunk)

1. Fresh session per chunk; read all listed files fully.
2. Check against AGENTS.md rules and ARCHITECTURE.md principles.
3. Append findings to the Findings section below: `[severity] file:line — issue` with one-line fix suggestion.
4. Severities: `blocker`, `major`, `minor`, `nit`.

## Chunks (review in this order)

### Chunk 1 — Core ECS infrastructure (~2,000 lines)

Files:
- src/Domain/EntityManager.cs, ComponentStorage.cs, Entity.cs, System.cs, WorldView.cs
- src/Domain/SpatialGrid.cs, Vector2.cs, Commands.cs, CommandProcessor.cs
- src/Domain/DiagnosticLogger.cs, CoolDownHelper.cs
- Tests: EcsTests.cs, SpatialGridTest.cs, Vector2Tests.cs, CommandProcessorTest.cs, CommandApplyTest.cs, ReflectionTest.cs

Focus: swap-pop correctness and entity ID stability (see plans/POP_AND_SWAP.md), memory layout, determinism, API surface. Everything else depends on this chunk — review first.

### Chunk 2 — Combat & physics systems (~2,300 lines)

Files:
- src/Domain/Combat/: CollisionSystem.cs, TurretFiringSystem.cs, EffectSystem.cs, ShipDeathExplosionSystem.cs, AmmoLifetimeSystem.cs, AsteroidFactory.cs
- src/Domain/Physics/: PhysicsSystem.cs, PositionIntegrationSystem.cs
- src/Domain/Components/: all 5 files (Entity, Physics, Combat, Effect, Gameplay)
- Tests: CollisionSystemTest.cs, TurretFiringTest.cs

Focus: hot-path performance for 10k → 100k objects, no new `yield return` in hot paths, determinism of collision resolution and damage order.

### Chunk 3 — AI, progression & simulation support (~1,050 lines)

Files:
- src/Domain/AI/: EnemyShipSystem.cs, EnemyShipSpawnSystem.cs, EnemyShipFactory.cs, MineDriftSystem.cs, MineRespawnSystem.cs
- src/Domain/Progression/: PickupMagnetSystem.cs, BlueSparkHomeSystem.cs, CameraSystem.cs, LevelUpSystem.cs
- src/Domain/Support/SimulationRunner.cs
- Tests: PerformanceBenchmark.cs

Focus: determinism of spawning and randomness (seeded RNG?), system ordering in SimulationRunner, whether the benchmark actually measures the 10k@120fps goal.

### Chunk 4 — Presentation layer & project files (~1,750 lines)

Files:
- src/Game/: SpaceVorsApp.cs, Renderer.cs, GameInitializer.cs, ImageLoader.cs
- src/Game/Game.csproj, src/Domain/Domain.csproj, src/Tests/Tests.csproj, spacevors.slnx, .gitignore

Focus: dependency direction (Domain must not reference Raylib or Game), resource lifecycle/disposal, fixed timestep + interpolation per ARCHITECTURE.md.

### Chunk 5 — Cross-cutting & docs consistency (~1,200 lines md + greps)

Files:
- AGENTS.md, ARCHITECTURE.md, PLAN.md, plans/*.md (POP_AND_SWAP, LOOT_DROPS, SHIP_TYPES, functional-ecs)

Focus: doc-vs-code drift (e.g. ARCHITECTURE.md lists Events/, Math/, Infrastructure/ dirs and a Systems/ folder that don't match the actual layout), dead code scan across repo, verify "game logic must not depend on rendering/input/audio" holds in practice.

## Status

| Chunk | Topic | Status |
|-------|-------|--------|
| 1 | Core ECS infrastructure | done (2026-08-15) |
| 2 | Combat & physics systems | done (2026-08-15) |
| 3 | AI, progression & support | done (2026-08-15) |
| 4 | Presentation layer | done (2026-08-15) |
| 5 | Cross-cutting & docs | pending |

## Findings

### Chunk 1 — Core ECS infrastructure (2026-08-15)

#### Major

- **[perf/correctness] `CommandProcessor.cs:28-32` — reflection dispatch is redundant and slow.**
  `RemoveComponentCommand<T>` already implements `IApplyCommand` (`Commands.cs:56`), so the `cmd is IApplyCommand` branch at line 33 handles it via normal interface dispatch. The preceding reflection branch (`GetType().IsGenericType && GetGenericTypeDefinition() == typeof(RemoveComponentCommand<>)`) intercepts it first and calls `GetMethod("Apply").Invoke(...)` — a per-command reflection call that allocates and is slow, on the per-frame destroy hot path.
  Fix: delete lines 28-32; let it fall through to `IApplyCommand`. Note `ReflectionTest.cs` exercises this exact unused-in-production pattern — update or drop it.

- **[correctness] `SpatialGrid.GetQueryItems` (`SpatialGrid.cs:56-78`) silently truncates.**
  Filling stops at `count >= result.Length` with no signal to the caller. `CollisionSystem` uses a fixed `stackalloc [256]` buffer (`CollisionSystem.cs:117`); in a dense region >256 candidates, collisions are dropped silently. At 10k objects this is a real hazard.
  Fix: return whether truncation occurred (or take a growable sink) and at least `DiagnosticLogger.LogWarning` when the buffer fills.

- **[correctness] `SpatialGrid` returns one item per (entity, cell), i.e. duplicates.**
  An entity spanning N cells appears up to N times in query results. Callers must dedupe by `Entity`. `CollisionSystem` dedupes asteroid/ship pairs via `candidate.Id.Value <= aEntity.Value`, but the mine-query loops (`CollisionSystem.cs:135-144, 167-176`) do not — safe today only because mines are small (≤1 cell). Fragile if any radius grows past `CellSize`.
  Fix: dedupe by entity id in the grid or document + enforce the caller contract; add a SpatialGrid test for multi-cell duplicates.

- **[perf] `SpatialGrid` allocates per frame.**
  `Insert` creates a new `List<SpatialItem>` per touched cell (`SpatialGrid.cs:48-49`) and `Clear()` discards all of them every tick (`CollisionSystem.cs:33`). Thousands of List + dict allocations/frame → GC pressure at 10k/120fps, worse toward 100k.
  Fix: reuse cell buckets (clear lists instead of recreating) or a flat array-of-arrays keyed by hashed cell.

- **[perf/design] `ComponentStorage._entityIdToSlot` grows unbounded; entity ids never recycled.**
  `EntityManager._nextId` only increases (`EntityManager.cs:8,14`), so each storage's index array is sized to the max id ever created and only doubles upward (`ComponentStorage.cs:14-32`). Memory is O(total entities ever created) per component type and never shrinks, even after `Clear()`. Also `MaxEntities = 20_000` (`ComponentStorage.cs:7`) is a magic number.
  Fix: recycle dead ids (free list) or periodically compact the index; make the initial capacity a named constant/param.

- **[design] `GameSystem.ElapsedTime` is static mutable global state.**
  `_accumulatedTime` lives on the abstract base (`System.cs:5-8`) and is read directly by `EnemyShipSpawnSystem` / `MineRespawnSystem`. It is shared across every system instance and any world in the process, breaking determinism/isolation when two simulations coexist (tests + benchmark) and hiding a dependency that should flow through `WorldView`.
  Fix: move elapsed-time state onto `EntityManager`/`WorldView` and pass it into systems.

#### Minor

- **`WorldView` is a pure pass-through that enforces nothing.** Every method forwards to `_em`; it still exposes mutating `GetStorage<T>()` / `GetComponentRef<T>`, so it adds indirection without read-only safety. Either make it genuinely read-only or delete it and pass `EntityManager`. (AGENTS.md: prefer deleting code, avoid unnecessary abstractions.)

- **`EntityManager.DestroyEntity` scans all storages** (`EntityManager.cs:17-26`) — O(component types) per destroy, calling `Remove` even for components the entity lacks. Track an entity's component set to skip misses at scale.

- **`Vector2.Magnitude` uses double math** (`Vector2.cs:8`, `(float)Math.Sqrt`). Use `MathF.Sqrt`. Also there is no `LengthSquared()`, yet callers hand-write `x*x + y*y` ~30 times (CollisionSystem, TurretFiringSystem, PickupMagnetSystem). Add `LengthSquared()` and replace the repetition — it's the idiomatic fast path.

- **`DiagnosticLogger` sits in Domain but does I/O** (`Console.*`) and takes input state (`LogMouse`, `DiagnosticLogger.cs:61-69`). ARCHITECTURE.md keeps graphics/input out of Domain; move diagnostics to an adapter or at least gate behind an interface.

- **`ComponentQuery<T1>` single-component enumerator does a redundant lookup** — `_smallest == _storage1`, then `MoveNext` calls `TryGetSlot` on that same storage (always true). Minor wasted work per entity.

#### Nit / test gaps

- **File/class name mismatch:** file `CoolDownHelper.cs` vs class `CooldownHelper`.
- **`EntityManager.cs` is 613 lines** of mostly duplicated `ComponentQuery<T1..T4>` + enumerator boilerplate. Defensible to avoid boxing, but it breaches "keep files small"; consider splitting queries into their own file.
- **Sentinel coupling:** `Entity.Null == -1` leaks into systems (`CollisionSystem.cs:48` checks `playerEntity.Value >= 0`). Prefer an explicit `IsNull`.
- **Test gaps:** no SpatialGrid test for multi-cell duplicate results or buffer truncation; no test that entity-id growth/compaction keeps `_entityIdToSlot` consistent after many creates.

### Chunk 2 — Combat & physics systems (2026-08-15)

#### Major

- **[gameplay/perf] `GameplayComponents.cs:112` + `SpaceVorsApp.cs:26` — default player ship (Scout) is equipped with the load-test weapon.**
  `ShipType.Scout` uses `WeaponLoadout.LoadTestWeapon`: 8000 pellets/shot, fire rate 0.5/s, kickback 100f (`GameplayComponents.cs:47`). Scout is the default selection in the ship picker, so every player shot spawns ~8000 Ammo entities at once — instantly blows the 10k-object budget and breaks gameplay (kickback).
  Fix: give Scout a real loadout (its description says "side shotguns"); keep `LoadTestWeapon` behind an explicit debug flag only.

- **[perf] `CollisionSystem.cs:98-110` — ship×asteroid loop is O(n²) and bypasses the spatial grid.**
  For every enemy ship it iterates all `_asteroidPositions` with a distance check (500 ships × 2000 asteroids ≈ 1M checks/frame), defeating the purpose of the grid. It also re-fetches `GetComponent<Position>(entity)` per asteroid inside the inner loop (`:107`) although `pos` is already in hand.
  Fix: `_grid.GetQueryItems(pos.Value, ship.Radius, queryBuffer)` + `Kind == Asteroid` filter + real rSum check (querying with the entity's own radius is provably sufficient for detection because `Insert` covers every cell of the body) + dedupe candidates by Entity id.

- **[correctness] `CollisionSystem.cs:135-144, 167-176` — duplicate grid candidates double-apply position correction for mine pairs.**
  The mine-query loops don't dedupe by entity id; a mine near cell boundaries appears up to N times in the buffer. `ResolveCollision` accumulates position correction (`:591-597`) BEFORE its separating-velocity early-out (`:599`), so each duplicate re-pushes both bodies — entities get pushed apart up to N× (and can double-impulse if `velAlongNormal` stays ≤ 0). Asteroid/ship pairs are protected by id-order dedupe (`:129, :161`); mine pairs aren't.
  Fix: track seen candidate ids per query; add a regression test with a multi-cell entity (pairs with Chunk 1's duplicate finding).

- **[design/determinism] `Random.Shared` in gameplay-affecting paths.**
  `CollisionSystem.cs:402-405, 774, 784` (loot drops) and `ShipDeathExplosionSystem.cs:41, 80, 92-96`. AGENTS.md lists deterministic behavior as a goal; loot rolls can't be reproduced or unit-tested.
  Fix: one seeded RNG owned by the world (`EntityManager`/`WorldView`) passed into systems — same fix will cover Chunk 3 spawn systems.

#### Minor

- **Mixed write patterns across systems.** `EffectSystem.cs` issues one `AddComponentCommand` per effect entity per frame (lifetime ticks for Spark/Explosion/GreenSpark/BlueSpark/DebugMarker) → at 10k live effects that's 10k commands/frame through CommandBuffer+Apply, while `AmmoLifetimeSystem.cs:27` and `PositionIntegrationSystem.cs:27` mutate components directly via ref. Pick one rule (direct mutation is faster; deferred commands give last-write-wins in system order) and document the ordering contract.

- **`TurretFiringSystem.cs:218-236, 279-296` — redundant third target loop.** `GetEntitiesWithComponents<EnemyShip, Position>()` re-processes every ship already handled by the `<EnemyShip, Velocity, Position>` loop above it — all enemy ships have a Velocity (`EnemyShipFactory.cs:36/49/62`). Delete both third loops (~40 lines).

- **`TurretFiringSystem.cs:26` — `Environment.GetEnvironmentVariable("SPACEVORS_DIAGNOSTIC")` called per turret, per shot.** Read once into a static readonly (the `DiagnosticLogger._enabled` pattern at `DiagnosticLogger.cs:7`). Domain reading env vars is also an environment coupling.

- **Dead/confused logic in CollisionSystem:**
  - `protectedEntities` (`:415-436`) never filters anything — `_entitiesToDestroy` only ever contains ammo entities, the protected set only mines/ships → the check at `:433` is always true. Delete or clarify intent.
  - `mineDamageMap` (`:358-363`) recomputes damage totals already tracked in `_frameRemainingHealth`. Redundant — delete the map.
  - `ResolveCircleVsCircle` (`:521-553`) is only ever called with bEntity = player (callers at `:72, :81, :91`); the `TryGetComponent<Asteroid>/<EnemyShip>` branches are dead → inline a player-specific resolve.

- **`PhysicsSystem.cs:17-18` — ignored `TryGetComponent<Velocity>` result.** An entity with Acceleration but no Velocity is silently treated as zero velocity and gets a new component added; handle the missing case explicitly or assert the invariant. Also `:44` integrates angle with pre-damping angVel while storing the damped value — verify intent (damping lags one frame).

- **Redundant lookups in TurretFiringSystem:** EnemyShip TryGetComponent twice for the same entity (`:78, :83`); player Position fetched inside a single-iteration loop (`:93`) — hoist out; `foreach…break` "get first" pattern repeated 4× (`:53-57, :91, :332-336`).

- **Magic numbers/strings:** ship mass override `3000f` (`CollisionSystem.cs:95, 108, 175`); mine radius logic duplicated at `:332` instead of using `EnemyMine.Radius`; epsilon `0.001f` repeated ~10× across both files → named constant; player damage from mines hardcoded to 3 (`:752`); `GetAmmoRadius` switches on WeaponName strings ("RailGun"/"AcidBubbleSpray") — reference weapon data instead (Turret also carries a redundant `string WeaponName` alongside `WeaponStats`).

#### Nit / dead code / test gaps

- **`Weapon` record struct (`CombatComponents.cs:3-13`) is unused** (grep: no references) → delete per AGENTS.md dead-code rule.
- **Stopwatch + LogSystem in every system Update.** Gated by the `_enabled` early-out so cost is just a bool check; Stopwatches still start/stop unconditionally (~dozens/frame, ~µs total). Optional: gate stopwatch creation behind an `IsEnabled` property.
- **Test gaps:** no regression test for multi-cell duplicate candidates (Major #3) or buffer truncation; `TurretFiringTest.cs` is a single smoke test — no coverage of arc culling, prediction, cooldown decay, or enemy turrets; no test that EffectSystem/AmmoLifetime destroy expired entities.

**Cross-chunk notes:** Chunk 3 must verify SimulationRunner applies the CommandBuffer after all systems (validates safety of the mixed direct-mutation + deferred-command patterns) and check `EnemyShipSpawnSystem`/`MineRespawnSystem` for more `Random.Shared`. Chunk 4: `SpaceVorsApp.cs:212, 295` read env vars per frame — same fix as Minor #3.

### Chunk 3 — AI, progression & support (2026-08-15)

Resolved cross-chunk question from Chunks 1–2: commands are applied **per phase** (`PerformanceBenchmark.cs:167/174/181/188`), so within a phase all systems read pre-phase state for command-written components. That makes the stale-read bugs below real, not theoretical.

#### Major

- **[correctness] `PickupMagnetSystem.ApplyXp`/`ApplyHealth` lose updates via deferred read-modify-write.**
  Each call re-reads Player/Health from view (`:118, :133`) — stale within the phase since commands apply at phase end — and issues an `AddComponentCommand`. Two pickups collected in one frame → both commands computed from the same base → last write wins → XP lost. Same for two health orbs (+3 applied once instead of +6).
  Fix: accumulate deltas locally per frame, emit one command after the loop (or mutate via ref like `AmmoLifetimeSystem` does).

- **[correctness] `LevelUpSystem` clobbers same-frame XP gains.**
  Runs in the same Resolution phase after PickupMagnetSystem; its Player command (`:27-36`) writes `Xp: playerStats.Xp` from a stale read → on level-up frames, XP gained that frame is lost. The milestone check (`:53`) also uses the pre-increment Level (off-by-one vs the new level).
  Fix: same as above; compute milestone from the new level.

- **[perf/test] Benchmark does not measure the 10k@120fps goal and its "frame time" is wrong.**
  Only active scenario is ~5.3k objects (`PerformanceBenchmark.cs:14-18` — all larger scenarios commented out); there is no `Assert` on a frame budget, so it can never fail; and `timings` accumulates across frames within an iteration (`:165`) while `allFrameTimes` sums those running totals (`:190-191`) → "Total frame time" is cumulative, inflated ~30×.
  Fix: track per-frame deltas (or reset the dict per frame), re-enable scenarios up to and past 10k objects, assert average < 8.3ms (or at minimum `DiagnosticLogger.LogWarning`).

- **[design] Shared static system instances across all simulations.**
  `SimulationRunner.MovementSystems` etc. are `static readonly` arrays of singletons (`:7-36`); systems hold mutable state (`EnemyShipSpawnSystem._timer :7`, `MineRespawnSystem._timer :7`) and read the static `GameSystem.ElapsedTime` → benchmark iterations, test runs, and game restarts in one process share timer/elapsed state.
  Fix: construct systems per world/simulation (or add an explicit Reset).

#### Minor

- **`EnemyShipSpawnSystem.cs:32-33` — spawning is gated on player speed.** `if (velMagnitude < 0.1f) return;` means a stationary player spawns no enemies at all (timer keeps expiring, retries every frame). Verify intent; if deliberate, name it as such.

- **Copy-pasted difficulty ramp.** `EnemyShipSpawnSystem.cs:72-78` and `MineRespawnSystem.cs:37-43` duplicate the same ElapsedTime/180s ramp with different inline magic numbers (5/10 vs 10/20). Extract a shared ramp helper; name the constants.

- **`MineRespawnSystem.cs:19` — confusing cap logic.** `activeMines >= TargetMineCount + 15`: "Target"=8 but up to 23 mines are allowed, and nothing actually targets 8. Rename/clarify (e.g., `MaxMines = 23`).

- **`PickupMagnetSystem` — dead parameter + redundant XpPickup writes.** The `effectivePickupRadius` param (`:19-20, :23, :72`) is never used; methods re-read `playerStats.PickupRadius`. The Chased-flag dance issues no-op `AddComponentCommand<XpPickup>` every frame for every pickup (sets false when already false at `:45`; sets true again at `:68` while already true) → thousands of redundant commands/frame at hundreds of pickups. Fix: drop the param; only write XpPickup when Chased actually changes.

- **Health orbs have no max-health clamp** (`PickupMagnetSystem.cs:134`, `health.Current + 3`) — player health grows unbounded; heal amount is a magic number. Verify intent / add a named cap.

- **`LevelUpSystem`: level-up with zero turrets still consumes the level** (`:48` returns before spawning choices, but `:27-36` still increments Level) → wasted level + threshold jump. Also `OrderBy(_ => Random.Shared.Next())` is not a shuffle (correlated, O(n log n) RNG calls) — use Fisher-Yates with the world RNG; unseeded anyway (Chunk 2 determinism finding).

- **`EnemyShipSystem.cs:80-90` — velocity integrated with stale acceleration.** After issuing `AddComponentCommand<Acceleration>(targetAccel)` at `:78`, it re-reads the OLD Acceleration and writes a Velocity command from that; next frame's PhysicsSystem integrates with the new accel regardless. The speed clamp (`:84-87`) is the only useful part, but it operates on stale-accel math. Fix: integrate with `targetAccel` or delete the block and let physics handle it.

- **Homing pattern duplicated + player lookup in loop.** MineDriftSystem re-fetches player Position per mine (`:16`) and iterates all mines even without a player (early-return instead of `continue` at `:15`). The exponential-blend "home toward player" is copy-pasted in MineDriftSystem (k=3) and BlueSparkHomeSystem (k=6, speed 180f) → shared helper with named constants.

- **Unseeded `Random.Shared` extends to all spawn/choice paths** — EnemyShipSpawnSystem `:7, 37, 46, 54, 59-67, 78`; MineRespawnSystem `:7, 27-35, 43`; LevelUpSystem `:80, 103`. Same fix as Chunk 2 (world-owned seeded RNG).

- **Phase naming is misleading.** "CleanupSystems" contains core AI (EnemyShipSystem), both spawners, and the camera (`SimulationRunner.cs:30-36`). Rename to reflect actual role. Also document the per-phase Apply contract — verify Game.cs/SpaceVorsApp applies after each phase exactly like the benchmark, otherwise benchmark ≠ game behavior (Chunk 4 check).

#### Nit / dead code

- **Double math instead of MathF:** `Math.Abs/Math.Sign/Sqrt` in EnemyShipSystem `:45-53, 57, 100-105, 119`, MineDriftSystem `:22`, BlueSparkHomeSystem `:23`, PickupMagnetSystem `:38, 87, 94, 143-144`.
- **Inconsistent player lookup:** FirstOrDefault + sentinel dance (EnemyShipSpawnSystem `:15-17`, MineDriftSystem `:9-11`, BlueSparkHomeSystem `:9-11`) vs TryFirst elsewhere — standardize on TryFirst.
- **EnemyShipFactory has two parallel APIs for identical data:** `Add*Components(EntityManager…)` (used by GameInitializer) vs `Create*Components() → IInitialComponent[]` (used by spawn systems). Consolidate to one. Turret WeaponStats also duplicates EnemyShip's fire-rate/ammo-speed fields ×3 variants, plus the `"EnemyWeapon"` string.
- **Magic numbers:** MineRespawn mine speed 30+rand×20 and Health(2); PickupMagnet spark count 6 / speeds 80+i×25 / spread 0.3f — a third copy of the spark-spawn pattern (CollisionSystem + ShipDeathExplosionSystem already have their own).
- **Benchmark is not reproducible end-to-end:** it seeds `new Random(42)` for layout, but the systems it runs use `Random.Shared` for spawning/loot.

**Cross-chunk notes for Chunk 4:** verify Game.cs/SpaceVorsApp applies commands per phase like the benchmark; check whether GameInitializer's local `rand` (used at lines 85–116) is seeded; find where PendingChoice/PendingUpgradeOptions are consumed and destroyed (upgrade application, player-death edge case); env-var reads at SpaceVorsApp.cs:212/295.

### Chunk 4 — Presentation layer & project files (2026-08-15)

Dependency direction verified clean: Domain has zero Raylib references (grep), Game→Domain and Tests→Domain only, csproj/slnx minimal and correct. The one blemish is a namespace, not an assembly reference (Minor #7).

#### Major

- **[gameplay] Mouse aim is mapped to player position, but rendering uses the lagging camera.**
  `SpaceVorsApp.cs:112-113` computes mouse world coords from `playerPos`, while `Renderer.Render` draws with `renderCam.Target` (`SpaceVorsApp.cs:213`). `CameraSystem` exponentially follows the player at rate 5/s (`CameraSystem.cs:7,19`), so in steady state the camera trails by ~playerSpeed/5 (60–120 units at typical thrust) — while moving, the cursor points at a different world location than what is on screen.
  Fix: compute mouse world coords from `renderCam.Target` (the same value rendering uses).

- **[gameplay] "Hit points" upgrade heals current HP instead of raising max.**
  `SpaceVorsApp.cs:419-424` writes `Health.Current + 2`, but `Health` stores only Current (`GameplayComponents.cs:3`) and the bar denominator is immutable `shipType.MaxHealth` (`Renderer.cs:37,543`). Repeated purchases push current past max; `DrawHealthBar` then computes `healthPercent > 1` with no clamp (`Renderer.cs:546`) → bar overflows its border.
  Fix: store Max on `Health` (or Player) and raise it; clamp the bar fill to [0,1].

- **[UI] Upgrade-card hit rects don't match drawn cards.**
  `GetUpgradeCardRect` (`Renderer.cs:601-612`) always centers **5** cards, but `DrawUpgradeCards` (`Renderer.cs:578-594`) centers by actual option count — milestone levels offer only 3 options (`LevelUpSystem.cs:81`), so hit rects are offset ~206px from the drawn cards and mouse selection hits the wrong card or nothing (keyboard still works). Additionally `DrawCard` draws a fixed **220**×140 rect (`Renderer.cs:642-643`) while the hit-test width is 196 with spacing 10 → adjacent cards visually overlap by 14px.
  Fix: one shared layout helper used by both draw and hit-testing, based on actual option count.

- **[perf] No viewport culling in any entity draw path.**
  Every `Draw*` method (e.g. `Renderer.cs:164` DrawAmmo, `:83` DrawAsteroids, `:435` DrawMines) iterates all entities and issues Raylib calls with no screen-bounds check — only `DrawClutter` (`:77`) culls. At the 10k-object goal (e.g. an 8000-pellet shot) most draws are off-screen yet still pay GetComponent + draw-call cost, eating the 8.3ms frame budget.
  Fix: after computing cx/cy in each method, `if (cx < -r || cx > W+r || cy < -r || cy > H+r) continue;`.

- **[perf] Coarse Thread.Sleep pacing undermines the 120fps goal.**
  Frame pacing is `Thread.Sleep((int)((MaxFrameTime - elapsed) * 1000))` (`SpaceVorsApp.cs:73, 218, 302`) — integer-ms granularity plus OS timer slack means achieved FPS settles well below 120 even at near-zero load (ship-select screen included).
  Fix: hybrid wait (sleep most of the budget, spin-wait the final ~1–2ms) or a high-resolution timer; measure achieved FPS to confirm.

#### Minor

- **[correctness/edge] Player death on a level-up frame softlocks the game.**
  `LevelUpSystem` never checks `Dead` (`LevelUpSystem.cs:13-37`) and runs in the same Resolution phase after `CollisionSystem`, which can add `Dead` to the player (`CollisionSystem.cs:423`); commands apply at phase end, so a PendingChoice spawns for an already-dead player. The pause branch never checks gameOver (only checked inside `!hasPendingChoice`, `SpaceVorsApp.cs:205`) → simulation halts forever on the upgrade screen and GAME OVER is never shown.
  Fix: skip level-up when the player has `Dead`; also check death in the pause branch.

- **[correctness/visual] Turrets sync to pre-step player transform.**
  Player Position/Rotation are read once per render frame before the fixed-step loop (`SpaceVorsApp.cs:107-119`) and turret Position/Rotation are written from them (`:164-179`); physics then advances the ship by N steps while turrets stay put → visible lag behind the hull on multi-step frames.
  Fix: sync turrets inside the step loop after physics, or compute turret transforms at render time.

- **[design] No interpolation despite ARCHITECTURE.md.**
  "Rendering interpolates if necessary" (`ARCHITECTURE.md:124`) — no previous-state storage exists anywhere; rendering uses current state only. Invisible at a true 120Hz step + 120fps display, but on slower machines motion stutters with no smoothing (compounds Major #5).
  Fix: store prev Position/Rotation and lerp by `accumulator / FixedDeltaTime` in Renderer, or document the deviation.

- **[robustness] Unbounded accumulator → spiral of death.**
  `SpaceVorsApp.cs:104` accumulates raw frameTime with no clamp; after a hitch (window drag, GC pause) the loop at `:182` runs dozens of catch-up steps in one frame → visible freeze.
  Fix: clamp the accumulator (e.g. to 0.25s) or cap steps per frame.

- **[gameplay] Post-death simulation keeps running; no restart.**
  After gameOver, input still writes Acceleration/AngularVelocity to the dead player every frame (`SpaceVorsApp.cs:149-161`) and player turrets keep firing — `TurretFiringSystem.cs:13` only skips Dead *enemy* turrets. The only exit is closing the window; the outer loop (`:29`) never re-enters ship select.
  Fix: gate input and player-turret firing on !Dead; add a restart key that breaks to the outer loop.

- **[design] Renderer.cs lives in namespace Spacevors.Domain.Systems** (`Renderer.cs:6`) while sitting in the Game project and referencing Raylib_cs — a presentation class inside Domain's namespace muddies layering for readers/LLMs (it even needs `using Spacevors.Game;` at `:4` to reach ImageLoader).
  Fix: move it to namespace Spacevors.Game.

- **[dead code] ~100 lines of unused card UI in Renderer.**
  `DrawEngineCards`, `GetEngineCardRect`, `DrawLoadoutCards`, `GetLoadoutCardRect` + private helpers (`Renderer.cs:614, 627, 702, 728, 747, 780`) have zero callers (grep-verified); they describe the old engine/loadout selection flow that ARCHITECTURE.md still documents.
  Fix: delete; Chunk 5 should reconcile ARCHITECTURE.md's main-loop section with the actual ship-select screen.

- **[perf] DrawAsteroids iterates all asteroids twice** (`Renderer.cs:85-161`): one pass for `<Asteroid, Rotation>`, a second for the rest with per-entity HasComponent checks.
  Fix: single pass + `TryGetComponent<Rotation>`.

- **[correctness] ImageLoader.LoadAssets crashes if asset dirs are missing.**
  `Directory.GetFiles("assets/asteroids/small", ...)` (`ImageLoader.cs:24,32`) throws when run from a different CWD (relative paths), while `LoadDirectoryTextures` guards with `Directory.Exists` (`:51`) — inconsistent.
  Fix: same Exists guard everywhere, or resolve assets relative to the app base directory and fail with a clear message.

- **[design] Mixed write patterns extend into the app layer.**
  Input writes Acceleration/AngularVelocity/turret Position+Rotation directly via `em.AddComponent` (`SpaceVorsApp.cs:149-178`) while systems use CommandBuffer — same inconsistency as Chunk 2's finding. Ordering is safe today only because input runs before all phases; the contract is undocumented.
  Fix: document "input writes are pre-phase and direct" or route through commands like everything else.

#### Nit / dead code

- **`GameInitializer` returns `turretEntities`** (`GameInitializer.cs:10, 121-133`) which SpaceVorsApp never uses → drop it from the tuple (AGENTS.md: prefer deleting code).
- **Magic numbers in GameInitializer:** Boost `2.5f` (`:21`), asteroid counts/distances/speeds (`:34-53`), mine count 15 + speed range (`:57-67`), ship counts (`:72, :98`) — name them or group into a config struct.
- **ApplyUpgrade:** always-true guard `if (newDamage > turret.Weapon.Damage)` (`SpaceVorsApp.cs:406`); the 10 switch cases rebuild Player/Turret records copying 7–9 unchanged fields each — a small helper (or ref mutation) would shrink it substantially.
- **One-frame flash of empty pause overlay** after upgrade selection: entities are destroyed at `SpaceVorsApp.cs:272-273` but `DrawUpgradeCards` still runs this frame with null options (`:296-297`).
- **`DrawPlayerShip` has no fallback when the player texture is missing** (unlike asteroids/enemies) → invisible ship.
- **F12 always overwrites "screenshot.png"** and .gitignore doesn't cover it; `xunit.runner.visualstudio 3.x` paired with xunit v2 (`Tests.csproj:13-14`) — verified working (66/66 pass) but a cross-major pairing; align versions.

**Cross-chunk resolutions & notes for Chunk 5:**
- Verified: SpaceVorsApp applies commands **per phase**, exactly like the benchmark (`SpaceVorsApp.cs:189-199`) → benchmark == game behavior on this point (closes Chunk 3's open question).
- Verified: GameInitializer's `rand` is seeded — `new Random(42)` at `GameInitializer.cs:29`; initial layout deterministic, but runtime spawning still uses `Random.Shared` (Chunk 3), so full runs are not reproducible.
- PendingChoice/PendingUpgradeOptions lifecycle: created together by LevelUpSystem (`AddEntity`, `LevelUpSystem.cs:83,106`); consumed and destroyed only in SpaceVorsApp's pause branch (`:258-274`). Edge case: death race (Minor #1). No other consumers.
- Env-var reads confirmed at `SpaceVorsApp.cs:212, 295` — per-frame, same fix as Chunk 2 Minor #3 (static readonly).
- ARCHITECTURE.md drift to verify in Chunk 5: main-loop section describes "Loadout selection … Forward or Broadside" but the actual flow is ship select (Scout/Fighter/Heavy) with engine stats baked into ShipType; dead Renderer card functions corroborate the old design. Also "Rendering interpolates if necessary" is unimplemented (Minor #3).

