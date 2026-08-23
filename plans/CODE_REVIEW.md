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
| 1 | Core ECS infrastructure | done (2026-08-15), re-reviewed (2026-08-23) |
| 2 | Combat & physics systems | done (2026-08-15), re-reviewed (2026-08-23) |
| 3 | AI, progression & support | done (2026-08-15), re-reviewed (2026-08-23) |
| 4 | Presentation layer | done (2026-08-15), re-reviewed (2026-08-23) |
| 5 | Cross-cutting & docs | done (2026-08-23) |

## Priority list for open issues (2026-08-23)

Consolidated from the per-chunk re-reviews below; line numbers verified against code on 2026-08-23. Fixed-issue history stays in each chunk's "Re-review" section — this list tracks only what remains. Suggested order of attack: #1 (one-line fix, real bug), then the cheap dead-code cleanup (#6), then #2–#5 before any further scale work.

### P0 — Correctness (fix first)

1. **Health orbs never expire** — `PickupMagnetSystem.cs:88-93` computes `newLifetime` but never writes it back; the destroy branch is dead code, so orbs persist forever → unbounded entity growth + screen clutter in long runs. Fix: add `AddComponentCommand<HealthOrb>(orbEntity, new HealthOrb(newLifetime, orb.Radius))`, same pattern as XpPickup at :47/:53. (Chunks 2/3.)

### P1 — Performance (threatens the 10k@120fps goal)

2. **Per-frame allocations in lit-draw batching** — `WorldRenderer.cs:34-35` and `EnemyShipRenderer.cs:21-23` allocate a Dictionary + lists every frame; thousands of allocs/frame at scale. Reuse static buffers (SpatialGrid pattern). (Chunk 4.)
3. **Unbounded accumulator → spiral of death** — `SpaceVorsApp.cs:117`, no clamp on `accumulator += frameTime`. (Chunk 4.)
4. **Per-frame allocations in combat hot path** — CollisionSystem fresh dicts per frame (`mineDamageMap` :366, `shipDeathData` :389) + LINQ `.Distinct()` (:440,:448); TurretFiringSystem per-frame `.ToList()` (:11). (Chunk 2.)
5. **Redundant component lookups** — WorldRenderer.cs:214-215, :253/:270/:286 (HasComponent+GetComponent); GetPlayerLevel queries all Players via LINQ though `playerEntity` is in scope (SpaceVorsApp.cs:68-69). (Chunk 4.)

### P2 — Maintainability / robustness

6. **Dead code** (AGENTS.md says remove immediately; one cheap commit): `UpgradeExplosion` (EffectComponents.cs:8), `EngineLayout.Maneuverable` (GameplayComponents.cs:70), `WeaponLoadout.MachineGun`/`.Shotgun` (:126-131), unused `Weapon` struct (CombatComponents.cs:3-13), `MaxEntityId` (EntityManager.cs:21, WorldView.cs:15), dead branches (`protectedEntities`, `mineDamageMap`, `ResolveCircleVsCircle`).
7. **`SpaceVorsApp.Main` ~300 lines** (:15-307) — extract upgrade selection, game-over/restart, input. (Chunk 4.)
8. **ApplyUpgrade ~190 lines** + always-true guard (SpaceVorsApp.cs:396) + verbose record rebuilds copying ~8 unchanged fields. (Chunk 4.)
9. **Mixed write patterns** — app layer writes components directly (input :164/:176, turret sync :192-193); document or unify the contract. (Chunks 2/4.)
10. **Ammo-loop `continue` skips remaining checks if a target lacks Health** (CollisionSystem.cs:273,:286) — unreachable today (all mines spawn with `Health(2)` at MineRespawnSystem.cs:41, all ships via EnemyShipFactory.cs:27), but fragile; make each hit check independent. (Chunk 2.)
11. **Zero-turret level consumption** — LevelUpSystem.cs:59 returns without spawning a choice, but Update() still consumes XP and bumps the level (:34-40). (Chunk 3.)
12. **ECS core smells** — EntityManager 628 lines; CommandProcessor reflection dispatch intercepting `RemoveComponentCommand<T>` (CommandProcessor.cs:28-32); unbounded `_entityIdToSlot` + `MaxEntities=20_000` magic number (ComponentStorage.cs:7); DestroyEntity scans all storages. (Chunk 1.)
13. **Magic numbers** — GameInitializer starfield/clutter (:108-147) + `Boost: 2.5f` (:26); CollisionSystem mass/epsilon/mine-damage; GetMaxWeaponSlots fallback 3 (LevelUpSystem.cs:177).
14. **DiagnosticLogger Console I/O in Domain** — LogMouse :71-79, game-specific LogAllEnemyShips :83-97; move to Game or no-op interface. (Chunk 1.)
15. **Lighting mutable static state + BeginFrame/BeginDraw/EndDraw protocol** (Lighting.cs:116-121); UpgradeMenuRenderer hardcodes weapon-name strings (:146-149). (Chunk 4.)
16. **xunit 2.9.3 + runner.visualstudio 3.0.2 mismatch** (Tests.csproj:13-14) — latent test-infra breakage; align versions. (Chunk 4.)
17. Smaller: turrets sync to pre-step transform (SpaceVorsApp.cs:179-194); Vector2.Magnitude/SolveQuadratic double math; CoolDownHelper file/class mismatch + 2 lookups; ComponentQuery<T1> redundant lookup; sentinel coupling (~7 sites, `IsNull` unused in production); misleading "CleanupSystems" phase name (SimulationRunner.cs:37-43); copy-pasted difficulty ramp; confusing mine cap (TargetMineCount+15=23); dead `effectivePickupRadius` param; no-op XpPickup false→false writes; EnemyShipSystem stale-accel integration + spin-stop damping duplication; MineDrift per-mine lookup + blend duplication (k=3 vs k=6).

### P3 — Docs & nits (one batch commit)

18. **Doc drift fixes** (all verified 2026-08-23): ARCHITECTURE.md project layout + loadout section; PLAN.md controls table (Q/E/Space unimplemented), Phase 4c enemy stats, Phase 7 flame note, Phase 5/8 `TryDraw` refs; SHIP_TYPES.md ships list (Shadow missing, Fighter is RailGun) + milestone HP text (+2 not +5); LOOT_DROPS.md UI description (5 cards, keys 1–5) + "additive" → multiplicative ×1.2; functional-ecs.md historical notes; GRAPHICS_REVIEW.md status header (Open: 2 → 1).
19. **F12 screenshot.png not gitignored** (+ TROUBLE_SHOOTING #1's numbered-file claim unverified — confirm, then fix doc or add .gitignore entry).
20. One-frame flash after upgrade selection (SpaceVorsApp.cs:252-254/:289-290); DrawPlayerShip no fallback on missing texture (ShipSpriteRenderer.cs:23-24); AGENTS.md typo "and and" (:57).

**Excluded:** Scout's `LoadTestWeapon` — documented intentional for load testing (GameplayComponents.cs:174); keep tracking until a real Scout loadout lands.

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

#### Re-review (2026-08-23)

**Fixed since 2026-08-15:**
- **[Major] SpatialGrid silent truncation** — `GetQueryItems` now has an `out bool truncated` param (`SpatialGrid.cs:61`); `CollisionSystem` aggregates it and warns once per frame (`CollisionSystem.cs:325`). Tests added.
- **[Major] Multi-cell duplicates** — deduped inside the grid via a reused `_seen` HashSet (`SpatialGrid.cs:9,80`); callers no longer need to dedupe. Tests `Query_MultiCellEntity_ReturnsItExactlyOnce`, `Query_MultipleEntities_ReturnsEachOnce`.
- **[Major/perf] SpatialGrid per-frame allocations** — `Clear()` now reuses cell buckets instead of recreating them (`SpatialGrid.cs:32-36`).
- **[Major/design] Static `GameSystem.ElapsedTime`** — gone. Elapsed time lives on `EntityManager` (`EntityManager.cs:12,25`) and flows through `WorldView` (`WorldView.cs:19`); `System.cs` is now a 6-line abstract class.
- **[Determinism infra] World-owned seeded RNG** — `EntityManager.Rng` (seed param, default 42) exposed via `WorldView.Rng`; `Random.Shared` no longer appears anywhere in src/ (grep). Closes the determinism findings of Chunks 1–3 at the infrastructure level.
- **[Nit/test gap] SpatialGrid tests** for multi-cell duplicates and buffer truncation now exist (`SpatialGridTest.cs:70-143`).

**Still open:**
- **[Major/perf/correctness] `CommandProcessor.cs:28-32` reflection dispatch** — unchanged; still intercepts `RemoveComponentCommand<T>` before the `IApplyCommand` branch at :33. `ReflectionTest.cs` remains but now tests `EntityManager.AddComponent` via reflection, not the processor path (the production reflection branch is untested).
- **[Major/perf/design] Unbounded `_entityIdToSlot` / no id recycling** — partially mitigated: `Clear()` now resets `_nextId = 0` (`EntityManager.cs:200`), so memory is bounded per simulation lifetime instead of across simulations. Within one long session ids still grow monotonically (ammo churn) and the index array never shrinks. `MaxEntities = 20_000` magic number remains (`ComponentStorage.cs:7`).
- **[Minor] `DestroyEntity` scans all storages** — unchanged (`EntityManager.cs:32-41`).
- **[Minor] `Vector2.Magnitude` double math, no `LengthSquared()`** — unchanged (`Vector2.cs:10`).
- **[Minor] `DiagnosticLogger` in Domain doing I/O** — unchanged; `LogMouse` still present (`:71-79`) and a new game-specific `LogAllEnemyShips` was added (`:83-97`), deepening the coupling.
- **[Minor] `ComponentQuery<T1>` redundant lookup** — unchanged (`EntityManager.cs:257-263`).
- **[Minor→mitigated] `WorldView` pass-through** — no longer pure pass-through (carries `ViewportSize`, exposes world services `Rng`/`ElapsedTime`), but still exposes mutating `GetStorage<T>` / `GetComponentRef<T>`.
- **[Nit] `CoolDownHelper.cs` file/class name mismatch** — unchanged; helper also does Has+Get = 2 lookups where one `TryGetComponent` would do (`CoolDownHelper.cs:7-10`).
- **[Nit] `EntityManager.cs` size** — now 628 lines (was 613); still one file of duplicated query boilerplate.
- **[Nit] Sentinel coupling** — `Entity.IsNull` was added but production code still uses `.Value >= 0` in 7 places (`CollisionSystem.cs:50`, `EnemyShipSystem.cs:15`, `MineDriftSystem.cs:11`, `BlueSparkHomeSystem.cs:11`, `EnemyShipSpawnSystem.cs:21`, `TurretFiringSystem.cs:46,86`); `IsNull` is only used in tests.
- **[Nit/test gap] No test** that `_entityIdToSlot` stays consistent after many creates (id growth).

**New issues:**
- **[nit/dead code] `MaxEntityId` has zero consumers.** Defined at `EntityManager.cs:21`, forwarded by `WorldView.cs:15`, used nowhere (grep incl. tests) → delete both.
- **[nit/perf] `CommandBuffer.Apply` allocates a new `CommandProcessor` per apply** (`Commands.cs:134`). The processor is stateless — reuse one instance or inline `Process`.
- **[nit] `SpatialGrid._cells` never shrinks.** Empty lists for cells no longer touched accumulate for the session's lifetime (bounded by world extent; acceptable, but note alongside the bucket-reuse fix).

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

#### Re-review (2026-08-23)

**Fixed since 2026-08-15:**
- **[Major] O(n²) ship×asteroid loop** — now a spatial-grid query with `Kind == Asteroid` filter and the real rSum check inside `ResolveCollision` (`CollisionSystem.cs:100-109`); candidate positions come from the grid item, no per-candidate `GetComponent`.
- **[Major] Duplicate mine candidates double-applying correction** — fixed at the root by grid-level dedup (Chunk 1) plus a regression test `MineVsAsteroid_MultiCellMine_CorrectedOnce` (`CollisionSystemTest.cs:295`). Collision resolution also now accumulates velocities/position-corrections in per-frame dictionaries and flushes once via commands (`FlushCollisions`, `CollisionSystem.cs:497-510`), fixing last-write-wins overwrites when one entity collides multiple times in a frame; covered by `TwoMinesVsPlayer_BothHitInOneFrame`, `TwoAmmoVsEnemyShip_*`, etc.
- **[Major/determinism] `Random.Shared` in loot/explosions** — gone; all paths use the world RNG (`CollisionSystem.cs:380,401,408,453,770`, `ShipDeathExplosionSystem.cs:19,25,80`).
- **[Minor/test gap] TurretFiringTest was a single smoke test** — now 8 tests covering add-on weapon arc culling, enemy hull-edge engagement and range, lead prediction with velocity inheritance, kickback scaling (`TurretFiringTest.cs:60-222`). CollisionSystemTest gained multi-hit-per-frame and mine/ship physics-bounce coverage.
- **[Minor] Player turrets firing after death** (old Chunk 4 Minor #5) — `TurretFiringSystem.cs:16` now skips non-enemy turrets when the player is dead.

**Still open:**
- **[Major/gameplay] Scout ships with LoadTestWeapon** (`GameplayComponents.cs:174`) — unchanged, but now carries an explicit comment "Intentionally kept while development is ongoing so manual load testing is easy". Still 8000 pellets/shot + kickback 100 for the default ship; keep tracking until a real Scout loadout lands.
- **[Minor] Mixed write patterns** — `EffectSystem` still issues one `AddComponentCommand` per effect entity per frame (`EffectSystem.cs:21,39,57,75,93`) while `AmmoLifetimeSystem.cs:27` and `PositionIntegrationSystem.cs:27` mutate via ref. The "pick one rule" question is still unanswered.
- **[Minor] Redundant third target loop** — now in *both* `FindTargetWithPrediction` (`TurretFiringSystem.cs:227-245`) and `FindTargetWithoutPrediction` (`:288-305`); every enemy ship has a Velocity (`EnemyShipFactory.cs:22,35`). ~76 lines to delete.
- **[Minor] Env var read per turret per shot** — unchanged (`TurretFiringSystem.cs:29`).
- **[Minor] Dead/confused logic in CollisionSystem:** `protectedEntities` still filters nothing (`:424-426, :440-446` — `_entitiesToDestroy` only ever contains ammo); `mineDamageMap` still redundant and its `totalDamage` value is never even read (`:366-371`, a HashSet would do); `ResolveCircleVsCircle` still only called with bEntity = player (`:75`) so the Asteroid/EnemyShip branches at `:542-551` are dead.
- **[Minor] PhysicsSystem** — `TryGetComponent<Velocity>` result still ignored (`PhysicsSystem.cs:17`); angle still integrated with pre-damping angVel while storing the damped value (`:44`, one-frame lag).
- **[Minor] Redundant lookups in TurretFiringSystem:** EnemyShip TryGetComponent twice per shot (`:86, :329`); "get first player" `foreach…break` repeated at `:62-66, :99-105` (with a `GetComponent<Position>` inside the loop), `:341-345`; kickback does `GetEntitiesWithComponents<Player>().ToList()` per shot (`:362`).
- **[Minor] Magic numbers:** ship mass override `3000f` (`CollisionSystem.cs:97,108,178`); epsilon `0.001f` ~7× in CollisionSystem + ~5× in TurretFiring; mine damage to player hardcoded to 3 (`:759`); mine proximity band `aRadius + 15f` (`:133, :168`). Ammo radius/color now come from `WeaponType` data (improved) but are still keyed by the redundant `Turret.WeaponName` string alongside `WeaponStats` (`CombatComponents.cs:21-27`).
- **[Nit] `Weapon` record struct still unused** (`CombatComponents.cs:3-13`) → delete.

**New issues:**
- **[minor/correctness] Ammo loop `continue`s past remaining checks when a hit target lacks Health.** `CollisionSystem.cs:273, :286` — if the mine/ship has no Health component, the whole rest of that ammo's iteration is skipped, including enemy-ammo-vs-player damage (`:299+`). One missing component silently disables player damage from that projectile. Scope the handling instead of `continue`.
- **[minor/perf] Per-frame allocations in CollisionSystem.** `mineDamageMap` (`:366`) and `shipDeathData` (`:389`) are fresh dictionaries every frame instead of cleared fields like the other accumulators; `_entitiesToDestroy.Distinct()` (`:440`) and `_effectsToSpawn.Distinct()` (`:448`) use LINQ (allocation + hashing) on the hot path.
- **[minor/gameplay] Health orbs never expire.** `PickupMagnetSystem.ProcessHealthOrbs` computes `newLifetime` and destroys on expiry (`:88-93`) but never writes the aged `HealthOrb` back for surviving orbs (unlike XpPickup, which is rewritten with `newLifetime` at `:47/:53/:70`) — Lifetime stays at its initial value forever. Write the aged component or delete the field.
- **[nit/dead code] Unused types:** `UpgradeExplosion` (`EffectComponents.cs:8`), `EngineLayout.Maneuverable` (`GameplayComponents.cs:70`), loadouts `WeaponLoadout.MachineGun` / `WeaponLoadout.Shotgun` (`GameplayComponents.cs:126-131`) — no references (grep).
- **[nit/perf] `TurretFiringSystem.Update` does `.ToList()` over all turrets per frame** (`:11`) — allocation proportional to enemy ship count; iterate the query directly.
- **[nit] `SolveQuadratic` uses double math** (`Math.Abs`, `(float)Math.Sqrt`, `TurretFiringSystem.cs:403-412`).

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

#### Re-review (2026-08-23)

**Fixed since 2026-08-15:**
- **[Major] PickupMagnetSystem lost XP/heal updates** — fixed. Deltas are accumulated locally per frame and Player.Xp is mutated directly via ref (`PickupMagnetSystem.cs:73-78`), with a comment documenting that LevelUpSystem (same phase, later) reads the fresh value.
- **[Major] LevelUpSystem clobbered same-frame XP / off-by-one milestone** — fixed as a consequence of the above; milestone now computed from `newLevel` (`LevelUpSystem.cs:37`).
- **[Major/design] Shared static system instances** — fixed. `SimulationRunner` is now an instance class constructing fresh systems per simulation (`SimulationRunner.cs:12-44`); benchmark creates one per iteration. Timer/elapsed state no longer leaks across runs.
- **[Major/perf/test] Benchmark did not measure the goal / wrong frame time** — fixed. Six scenarios up to 15k objects re-enabled (`PerformanceBenchmark.cs:12-20`), per-frame `frameTime` reset each frame (`:162, :189`), and a real budget check: `mustMeetBudget` → `Assert.Fail`, else warning (`:209-215`). Three scenarios currently exceed the 8.3ms budget and are honestly marked `mustMeetBudget: false` with a pointer to this review (`:11, :15, :16, :18`).
- **[Minor] Health orbs had no max-health clamp** — fixed: `Math.Min(health.Current + totalHeal, playerStats.MaxHealth)` (`PickupMagnetSystem.cs:128`); heal amount is a named constant (`:9`).
- **[Minor] `OrderBy(Random.Shared.Next())` fake shuffle** — fixed with proper Fisher-Yates using the world RNG (`LevelUpSystem.cs:162-169`).
- **[Minor] Unseeded `Random.Shared` in spawn/choice paths** — gone; all spawners/factories take `view.Rng` (also closes Chunk 3's "benchmark not reproducible end-to-end" nit — layout and runtime spawning now share the seeded world RNG).
- **[Minor, part] Homing pattern / player lookup** — BlueSparkHomeSystem fixed: early return without a player + single Position fetch before the loop (`BlueSparkHomeSystem.cs:13-15`). Spawn placement extracted into shared `SpawnPlacement` helper (new file) used by both spawners.

**Still open:**
- **[Minor] Spawning gated on player speed** — unchanged (`EnemyShipSpawnSystem.cs:37`, via `SpawnPlacement.MinDirectionalSpeed`); a stationary player spawns no ships. MineRespawnSystem handles the stationary case explicitly (`MineRespawnSystem.cs:31-34`) — consider matching that behavior or documenting the difference.
- **[Minor] Copy-pasted difficulty ramp** — still duplicated between `EnemyShipSpawnSystem.cs:52-56` and `MineRespawnSystem.cs:43-47` (improved: named Min/MaxInterval constants, but the 180s ramp shape + inline end-values are copy-pasted).
- **[Minor] Confusing mine cap** — unchanged: `activeMines >= TargetMineCount + 15` (`MineRespawnSystem.cs:20`).
- **[Minor] PickupMagnetSystem dead parameter** — `effectivePickupRadius` still unused in both methods (`:24, :81`); they re-read `playerStats.PickupRadius`.
- **[Minor, part] Redundant XpPickup writes** — improved (Chased only written on real transitions at `:53`, lifetime-carrying write at `:70`), but the no-op false→false write for every out-of-radius unchased pickup remains (`:45-49`) → still one command/frame per idle pickup.
- **[Minor] Level-up with zero turrets still consumes the level** — unchanged: the Level-increment command is added at `LevelUpSystem.cs:39` regardless of the early return in `SpawnLevelUpChoice` (`:59`).
- **[Minor] EnemyShipSystem integrates velocity with stale acceleration** — unchanged (`EnemyShipSystem.cs:93-102`: re-reads pre-command Acceleration after issuing a new one).
- **[Minor, part] Homing blend duplicated** — MineDriftSystem still fetches player Position per mine and uses `continue` instead of early return (`MineDriftSystem.cs:15-16`); the exponential-blend home-toward-player is still copy-pasted (k=3 there vs k=6 in BlueSparkHomeSystem) with no shared helper.
- **[Minor] Phase naming** — unchanged: "CleanupSystems" still holds core AI + both spawners + camera (`SimulationRunner.cs:37-43`).

**New issues:**
- **[minor/correctness] Health orbs never expire (aged value discarded).** `ProcessHealthOrbs` computes `newLifetime` and destroys on expiry (`PickupMagnetSystem.cs:88-93`) but never rewrites the orb with it — Lifetime stays at its initial 30f forever, so the expiry branch is dead. (See also Chunk 2 new issues.)
- **[nit] EnemyShipSystem duplicates spin-stop damping** between `:53-64` and `ApplySpinStop` (`:107-122`); `ApplyRotationTowardPlayer` recomputes toPlayer/dist the caller already has and re-fetches Position via `GetComponent` (`:128`).
- **[nit] `LevelUpSystem.GetMaxWeaponSlots` fallback magic number 3** (`:177`) — should come from ShipType/WeaponSlots.
- **[nit] MineRespawnSystem interval constants are `int`** used in float math (`:9-10, :46-47`) while EnemyShipSpawnSystem uses `float`.
- **[note] LevelUpSystem now carries diagnostic state** (`_scriptedUpgrades`, `_scriptIndex`, env-var parsing at construction, `:13-20, :123-138`). Safe today because SimulationRunner builds fresh instances per simulation; if the app ever reuses a runner across restarts, `_scriptIndex` would persist.

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

#### Re-review (2026-08-23)

**Fixed since 2026-08-15:**
- **[Major] Mouse aim vs lagging camera** — mouse world coords now computed from `aimCam.Target`, the same value rendering uses (`SpaceVorsApp.cs:127-128` vs `:240-241`).
- **[Major] "Hit points" upgrade healed current instead of max** — `Player.MaxHealth` added; the Hp case raises both `Health.Current + 2` and `MaxHealth + 2` (`SpaceVorsApp.cs:409-423`) and the bar clamps to [0,1] against MaxHealth (`HudRenderer.cs:17`).
- **[Major] Upgrade-card hit rects mismatched drawn cards** — draw (`UpgradeMenuRenderer.cs:31`) and hover test (`StatsScreenRenderer.cs:66`) both use one shared `GetUpgradeCardRect` based on the actual option count (`UpgradeMenuRenderer.cs:40-59`); card size is derived from window width, no fixed 220×140.
- **[Major] No viewport culling** — every entity draw path now culls via `RenderHelpers.IsOffScreen` after computing screen coords (WorldRenderer.cs:51,100,125,163,185,212,259,276,295,315; EnemyShipRenderer.cs:50,94; ThrusterFlameRenderer.cs:152; BackgroundRenderer.cs:42).
- **[Major] Coarse Thread.Sleep pacing** — gone (grep); `Raylib.SetTargetFPS(MaxFps)` handles pacing (`SpaceVorsApp.cs:19`).
- **[Minor] Death on level-up frame softlock** — LevelUpSystem skips dead players (`LevelUpSystem.cs:27`, Chunk 3) and the app drops stale PendingChoice + sets gameOver (`SpaceVorsApp.cs:90-97`).
- **[Minor] Post-death simulation / no restart** — R returns to ship select (`SpaceVorsApp.cs:79-83`); input gated on `!gameOver` (`:119`); player turrets skip when dead (`TurretFiringSystem.cs:16`, Chunk 2). Simulation keeps running during game over (background animates behind GAME OVER) — acceptable.
- **[Minor] Renderer in Domain namespace** — now `namespace Spacevors.Game` (`Renderer.cs:5`), and the file is a thin dispatcher split into WorldRenderer/HudRenderer/StatsScreenRenderer/UpgradeMenuRenderer/ShipSelectScreen/etc.
- **[Minor] ~100 lines of unused card UI** — gone; replaced by ShipSelectScreen + StatsScreenRenderer + UpgradeMenuRenderer, all used (grep).
- **[Minor] DrawAsteroids iterated twice** — single pass over `<Asteroid, Rotation>` (`WorldRenderer.cs:37`); every asteroid has a Rotation (added by AsteroidFactory).
- **[Minor] ImageLoader crashed on missing asset dirs** — `LoadSpriteSets` returns empty sets when the dir is absent (`ImageLoader.cs:74`) and paths resolve via `AppContext.BaseDirectory` (`:49`) so CWD no longer matters; map files without a complete lit set are skipped with a warning (`:94-98`).
- **[Nit] GameInitializer unused turretEntities return** — signature is now a 5-tuple (`GameInitializer.cs:15`); `rand` comes from the world RNG `em.Rng` (`:39`), so initial layout and runtime spawning share one seeded stream → full runs are reproducible end-to-end (closes Chunk 3's "benchmark not reproducible" note).
- **[Nit] Per-frame env-var reads in app layer** — gone; SPACEVORS_DIAGNOSTIC read once per run (`SpaceVorsApp.cs:24`) and once per game start (`GameInitializer.cs:33`). (TurretFiringSystem's per-shot read remains — Chunk 2.)

**Still open:**
- **[Minor] Turrets sync to pre-step player transform** — unchanged in shape: turret Position/Rotation written once per render frame before the fixed-step loop (`SpaceVorsApp.cs:179-194`), so turrets lag the hull on multi-step frames.
- ~~**[Minor] No interpolation despite ARCHITECTURE.md**~~ — **resolved by doc change**: ARCHITECTURE.md:124 now states "Rendering draws the latest simulated state; no interpolation between ticks", matching the code (no previous-state storage anywhere). Motion stutter on slow machines remains as a documented tradeoff, not drift.
- **[Minor] Unbounded accumulator → spiral of death** — `accumulator += frameTime` with no clamp (`SpaceVorsApp.cs:117`).
- **[Minor] Mixed write patterns extend into the app layer** — input still writes Acceleration/AngularVelocity directly via AddComponent (`:164, :176`) and turret sync writes Position/Rotation (`:192-193`); contract undocumented (same as Chunk 2's finding).
- **[Nit] Magic numbers in GameInitializer** — partially fixed: spawn counts/distances now named constants (`:10-13`), but the starfield/clutter block still has inline magic numbers (`:108-147`) and `Boost: 2.5f` (`:26`).
- **[Nit] ApplyUpgrade always-true guard** — `if (newDamage > turret.Weapon.Damage)` still present (`SpaceVorsApp.cs:396`); switch cases still rebuild Player/Turret records copying ~8 unchanged fields each (ApplyUpgrade is now ~190 lines).
- **[Nit] One-frame flash after upgrade selection** — same issue, different shape: `upgradeOptions` captured before the choice entity is destroyed (`:252-254`, `:289-290`) and passed to RenderPaused at `:299` → cards draw one more frame after selection.
- **[Nit] DrawPlayerShip has no fallback** — ShipSpriteRenderer.DrawShipSprite silently returns when the texture is missing (`ShipSpriteRenderer.cs:23-24`); enemies have a triangle fallback, the player ship just vanishes without warning.
- **[Nit] F12 screenshot.png not gitignored** — TakeScreenshot writes to CWD (`SpaceVorsApp.cs:35`); no .gitignore entry.
- **[Nit] xunit version mismatch** — still xunit 2.9.3 + xunit.runner.visualstudio 3.0.2 (`Tests.csproj:13-14`).

**New issues:**
- **[minor/perf] Per-frame allocations in the lit-draw batching path.** `DrawAsteroids` allocates a fresh `Dictionary<LitSprite, List<...>>` + diagnostic list every frame (`WorldRenderer.cs:34-35`); `EnemyShipRenderer.Draw` does the same (`:21-23`). Key count is bounded by variant count, but each list holds one tuple per visible sprite → thousands of allocations/frame at 10k objects. Fix: reuse static buffers cleared per frame (same pattern as SpatialGrid's bucket reuse).
- **[minor] `SpaceVorsApp.Main` is ~300 lines** (`SpaceVorsApp.cs:15-307`) — breaches "keep functions short"; upgrade-selection handling (`:248-300`), game-over/restart, and input could each be extracted.
- **[minor] Redundant component lookups in render paths.** DrawMines does HasComponent<Health> + GetComponent<Health> (`WorldRenderer.cs:214-215`); DrawXpPickups/DrawHealthOrbs/DrawGreenSparks do a redundant HasComponent<Position> before GetComponent (`:253, :270, :286`) — one TryGetComponent each would do.
- **[nit] `Lighting` holds mutable static state** (PointLights array + camera/window fields, `Lighting.cs:116-121`) with a BeginFrame/BeginDraw/EndDraw protocol that is easy to break; it also uploads all 16 light slots per variant block (`:184`). Acceptable for a single-threaded renderer, but note alongside the static-state findings in Chunks 1–3.
- **[nit] UpgradeMenuRenderer hardcodes weapon-name strings** "MachineGun"/"Shotgun" in `GetUpgradeLabel` (`UpgradeMenuRenderer.cs:146-149`) — fragile coupling to WeaponType names; derive labels from weapon data instead.
- **[nit] GetPlayerLevel queries all Players via LINQ FirstOrDefault + sentinel check** (`SpaceVorsApp.cs:68-69`) although `playerEntity` is in scope at both call sites — just use `em.GetComponent<Player>(playerEntity).Level`.
- **[note] New headless render benchmark** `RenderBench/Program.cs` (357 lines) with a pixel oracle that keeps the legacy per-sprite shader as a frozen copy (`:174-259`) — good practice; delete the oracle + legacy shaders once the per-sprite path is gone for good (the comment says so).

**Cross-chunk resolutions & notes for Chunk 5:**
- Verified: SpaceVorsApp applies commands **per phase**, exactly like the benchmark (`SpaceVorsApp.cs:219-229`) → benchmark == game behavior on this point.
- PendingChoice/PendingUpgradeOptions lifecycle: created by LevelUpSystem; consumed/destroyed only in SpaceVorsApp (death race `:93-94`, selection `:289-290`); no other consumers.
- ARCHITECTURE.md drift to verify in Chunk 5 (updated): main-loop section describes "Loadout selection … Forward or Broadside" but the actual flow is ship select with engine stats baked into ShipType; the interpolation item was resolved by a doc change (ARCHITECTURE.md:124, see Still open above). The old dead card functions are gone, so that corroboration no longer applies.

### Chunk 5 — Cross-cutting & docs consistency (2026-08-23)

Scope: AGENTS.md, ARCHITECTURE.md, PLAN.md, TROUBLE_SHOOTING.md, plans/{POP_AND_SWAP, SHIP_TYPES, LOOT_DROPS, functional-ecs, GRAPHICS_REVIEW}.md. Method: read every doc in full and verify each concrete claim (controls, stats, file layout, API names) against code via grep/read. First review of this chunk (the 2026-08-15 pass covered Chunks 1–4 only).

**Verified correct:**
- Dependency direction holds: Domain has zero references to Raylib/Game; Game → Domain only; Tests → both (csproj + grep). AGENTS.md's "game logic must not depend on rendering/input/audio" is satisfied in practice.
- TROUBLE_SHOOTING.md entries match current code: #5 Raylib-cs 8.0.0 ✓, #11 asset copy in Game.csproj ✓, #14 shader uniform leak (matches Lighting design + RenderBench probe), #15 Escape exit key (SpaceVorsApp comment), #16 ship-level upgrade empty weapon name (UpgradeCounts/StatsScreenRenderer).
- LOOT_DROPS.md core numbers: mine XP 1/2 with radii 6/9 (`MineType`, EntityComponents.cs:66-67), ship XP 3 radius 18 (CollisionSystem.cs:786), health orb 5% roll (:778,:788), heal 3 (PickupMagnetSystem.cs:9), level threshold `Level * 10` (LevelUpSystem.cs:32).
- SHIP_TYPES.md Phase 2 weapon slots heavy=3 / scout=1 / fighter=2 (GameplayComponents.cs:180,:193,:206); engine upgrades +10% (`UpgradeDefinition` ×1.1, :20-23); FireRate +15%, ProjectileSpeed +30% (:13-14).
- GRAPHICS_REVIEW.md fix log matches code: AngularVelocity turn flame (ThrusterFlameRenderer.cs:103), LitGroupRenderer batching with measured numbers, PrevAngles deleted.

**Drift found:**

ARCHITECTURE.md:
- **[major] Project layout stale** — lists non-existent `Domain/Systems/`, `Domain/Events/`, `Domain/Math/`, `Infrastructure/`; Game/ file list missing 8 files (ImageLoader, Lighting, LightGatherer, LitGroupRenderer, LitSprite, LitSpriteMatcher, StatsScreenRenderer, AsteroidSprite); RenderBench project absent. Fix: regenerate the layout from the actual tree.
- **[major] Main-loop section "Loadout selection … Forward or Broadside"** — outdated; actual flow is ship select (ShipSelectScreen, 4 ships).
- **[minor] ECS example component list stale** — `Transform`/`Weapon`/`Enemy`/`Projectile`/`Lifetime`/`Experience` don't exist; the example's `UpgradeExplosion` is dead code. Fix: use real components (Position, Velocity, Turret, HealthOrb…).
- **[minor] Events section aspirational** — no event system exists; systems communicate via components + CommandBuffer.

PLAN.md:
- **[major] Controls table lists Q/E rotate and Space brake** — unimplemented; input is W/S/A/D + Shift boost + mouse aim only (SpaceVorsApp.cs:119-176). Fix: remove the rows or implement them.
- **[minor] Phase 4c enemy stats stale** — Interceptor radius 45 / accel 85 vs doc "smaller (15px), faster acceleration (15)"; HeavyCannon radius 78 vs "(28px)" (EntityComponents.cs:41-43); fire rates match.
- **[minor] Phase 7 turn-flame note says "dictionary inside the renderer"** — actual implementation reads the AngularVelocity component (ThrusterFlameRenderer.cs:103; PrevAngles deleted per GRAPHICS_REVIEW).
- **[minor] Phases 5/8 reference deleted API `Lighting.TryDraw`** — now BeginDraw/EndDraw + LitGroupRenderer (commit 89e2d45); "upload once per frame" vs actual one upload of all 16 light slots per variant block (Lighting.cs:184).
- **[nit] Ship select listed as "1/2/3/4 or click"** — actually also arrows/WASD + wheel + Enter.

SHIP_TYPES.md:
- **[major] Starting ships stale** — Scout's "side shotguns" vs LoadTestWeapon loadout (GameplayComponents.cs:174, documented intentional); Fighter listed as "machinegun" but actual loadout is RailGun (:187); Shadow ship missing from the list entirely (:209-222).
- **[minor] Phase 3 milestone "Hp upgrade 5 points / 2 random weapons"** — code gives Hp +2 (`UpgradeDefinition.Hp` Additive=2) and up to 3 options drawn from a pool of Hp + new weapons + existing-weapon damage (LevelUpSystem.cs:64-90).

LOOT_DROPS.md:
- **[minor] UI description stale** — "Digit1/Digit2 keys showing 2 of 3 options at a time, cycling" vs actual up to 5 cards with keys 1–5 + click (LevelUpSystem.cs:115; SpaceVorsApp.cs:262-270).
- **[minor] PickupRadius upgrade described as "additive stacking"** — code is multiplicative ×1.2 (GameplayComponents.cs:15).
- **[nit] "Files to Change" paths stale** (no `Systems/` dir); "UpgradePickupSystem delete entirely" — done ✓.

functional-ecs.md (historical plan):
- **[minor] Per-system CommandBuffer vs actual shared per-phase buffer** — SimulationRunner.RunPhase passes one buffer to all systems in a phase.
- **[minor] WorldView "no mutation methods"** — still exposes GetStorage/GetComponentRef (Chunk 1 finding, still open).
- **[nit] Phase composition differs from plan** — CameraSystem is Cleanup not Movement; PickupMagnet/LevelUp are Resolution; systems instantiated once → SimulationRunner builds fresh per simulation (Chunk 3 fix).

GRAPHICS_REVIEW.md:
- **[minor] Internal inconsistency in its own status header** — "Status (checked 2026-08-21): Fixed: 28 · Open: 2" still lists "Perf: Lighting.TryDraw per-sprite shader-mode overhead (deferred)" (:15), but the doc's later section records it as Measured + fixed by batching with numbers (:173+). Fix: update header to Open: 1 and drop the TryDraw item.
- **[nit] "Not issues (verified): PrevAngles is safe from entity-ID reuse" (:147)** references deleted code (PrevAngles removed per the doc's own fix entry at :128).

POP_AND_SWAP.md: historical; implementation deviated as already noted in Chunk 2 (array `_entityIdToSlot` indexed by entity ID instead of the plan's `Dictionary<int,int>` slot map) — acceptable, mark implemented.

**New issues:**
- **[nit] TROUBLE_SHOOTING #1 claims each F12 shot produces both screenshot0NN.png and screenshot.png**, but the app only calls TakeScreenshot("screenshot.png") (SpaceVorsApp.cs:35); the doc hedges that the mechanism lives in the stripped native raylib lib — unverified. Consider a note or removal once confirmed.
- **[nit] AGENTS.md typo** — "problems encountered before and and avoid" (:57).

