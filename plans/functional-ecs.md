# PLAN: Functional Command-Based ECS

## Goal

Transform systems from direct mutators into read-only functions that receive a `CommandBuffer` parameter and add commands to it. Commands are batched and applied at end-of-phase, enabling parallel system execution within a phase while preserving correct ordering across phases.

## Current state

- Components are already immutable records (good foundation)
- Systems call `em.AddComponent()`, `em.DestroyEntity()` directly during update
- Systems run sequentially in the main loop
- No separation between reading data and mutating it

## Target architecture

A single frame may contain multiple simulation steps. Each step is a complete cycle:

```
Frame start
    │
    ▼
Step 1 — Physics phase
    ├── Create WorldView (read-only accessor over EntityManager storage)
    ├── Run systems sequentially against this View, each with its own CommandBuffer
    │   ├── PhysicsSystem → [UpdateVelocity, UpdatePosition]
    │   └── CameraSystem → [UpdateCameraTarget]
    ├── Collect all command buffers into one batch
    ├── Apply commands from buffer to ECS (single mutation pass)
    │
    ▼
Step 2 — Combat phase
    ├── Create fresh WorldView (sees Step 1 mutations: updated positions, camera targets)
    ├── Run systems sequentially against this View, each with its own CommandBuffer
    │   ├── TurretFiringSystem → [CreateEntityWithComponents for ammo]
    │   └── PickupMagnetSystem → [UpdatePosition, DestroyEntity, AddComponent]
    ├── Collect all command buffers into one batch
    ├── Apply commands from buffer to ECS (single mutation pass)
    │
    ▼
Step 3 — Resolution phase
    ├── Create fresh WorldView (sees Step 2 mutations: ammo entities exist, positions updated)
    ├── Run systems sequentially against this View, each with its own CommandBuffer
    │   └── CollisionSystem → [DestroyEntity, AddComponent, CreateEntityWithComponents for effects]
    ├── Collect all command buffers into one batch
    ├── Apply commands from buffer to ECS (single mutation pass)
    │
    ▼
Step 4 — Cleanup phase
    ├── Create fresh WorldView (sees Step 3 mutations: explosions, sparks spawned)
    ├── Run systems sequentially against this View, each with its own CommandBuffer
    │   ├── EffectSystem → [UpdateLifetime, DestroyEntity]
    │   ├── AmmoLifetimeSystem → [DestroyEntity]
    │   └── LevelUpSystem → [CreateEntityWithComponents for PendingChoice]
    ├── Collect all command buffers into one batch
    ├── Apply commands from buffer to ECS (single mutation pass)
    │
    ▼
Render — frame complete, next frame begins
```

The View is a thin accessor over the same underlying storage as `EntityManager`. It provides read-only query methods. No data copying occurs. The ECS can still be mutated by `CommandProcessor` after systems finish, but during system execution the View guarantees no mutations through its interface.

Each step commits all changes before the next step starts. This means:
- PhysicsSystem runs in Step 1, commands are applied, ECS is updated
- CollisionSystem gets a fresh View in Step 3 where physics-updated positions are already visible
- TurretFiringSystem spawns ammo in Step 2, CollisionSystem sees those entities in Step 3

## Command types

Commands are small record structs:

```
CreateEntityWithComponentsCommand – entity created with all components bundled together
DestroyEntityCommand              – entity to remove (by ID)
AddComponentCommand<T>            – add/update component on existing entity (T : notnull)
```

`CreateEntityWithComponentsCommand` bundles entity creation and initial components into a single command. This avoids the chicken-and-egg problem where `CreateEntityCommand` can't return an ID before execution — systems need to know the entity ID to reference it in subsequent `AddComponentCommand`s, but the ID is only assigned during processing. By bundling all initial components with creation, systems never need a pre-assigned ID for new entities.

`AddComponentCommand<T>` is generic — the component value is stored as its actual type T, not boxed into an object. Used to add/update components on existing (pre-existing) entities. All commands share a common non-generic base `Command`. Components are immutable records — adding the same component type again replaces it, handling both "add" and "update". The processor uses pattern matching on generic types to dispatch without runtime type checks or casting.

Each system receives its own `CommandBuffer` instance during execution. Systems add commands directly to their buffer. After all systems in a phase complete, the buffers are collected into one batch and applied. This eliminates thread-safety concerns since each system writes to its own buffer with no contention.

## New files

### `src/Domain/Commands.cs`
- `Command` abstract base class
- `CreateEntityWithComponentsCommand : Command` – holds a collection of component values to add on the newly created entity. Processor assigns ID and adds all components atomically.
- `AddComponentCommand<T> : Command where T : notnull` – holds entity ID and strongly typed component value (T) for existing entities
- `DestroyEntityCommand : Command` – holds entity ID to remove
- `CommandBuffer` – simple mutable list per system. No thread-safety needed since each system has its own buffer. Provides `Add(Command)` and `Commands` (read-only list).

### `src/Domain/WorldView.cs`
- Thin read-only accessor over `EntityManager`
- Provides query methods that delegate to `EntityManager` but expose no mutation methods:
  - `GetComponent<T>(Entity)`
  - `HasComponent<T>(Entity)`
  - `GetEntitiesWithComponents<T>()` → `IEnumerable<(Entity, T)>`
  - `GetEntitiesWithComponents<T1, T2>()`, `<T1, T2, T3>()`, etc.
- No `AddComponent`, no `DestroyEntity` — these are compile-time errors

### `src/Domain/CommandProcessor.cs`
- Takes `EntityManager` + `IEnumerable<Command>`
- Applies all commands in order using pattern matching on generic types: creates entities with bundled components, adds components to existing entities, destroys entities
- No separate ID assignment — `CreateEntityWithComponentsCommand` handles creation and component addition atomically
- Handles deduplication of destroy operations
- No runtime type checks or casting — uses C# pattern matching with generic arms like `case AddComponentCommand<Velocity> cmd`

## Changes to existing files

### `src/Domain/EntityManager.cs`
- Keep as-is — it remains the mutation target
- Existing methods (`CreateEntity`, `DestroyEntity`, `AddComponent`) unchanged for GameInitializer and tests
- Add `ProcessCommands(IEnumerable<Command>)` method

### `src/Domain/System.cs`
- Change signature: `public abstract void Update(WorldView view, float deltaTime, CommandBuffer commands)`
- Each system receives its own CommandBuffer instance (created by RunPhase) and adds commands to it
- Remove direct EntityManager dependency from systems

## System phases (step grouping)

Systems are grouped into phases based on data dependencies. Systems within a phase have no mutual dependencies and can run in parallel. Phases execute sequentially, each committing before the next starts.

**Phase 1 — Movement:** PhysicsSystem, CameraSystem
- Both read positions/velocities, write updated values
- No cross-dependencies between them

**Phase 2 — Actions:** TurretFiringSystem, PickupMagnetSystem, EnemyShipSystem, MineDriftSystem, MineRespawnSystem, EnemyShipSpawnSystem
- Systems that spawn entities or modify state based on current world
- See physics-updated positions from Phase 1

**Phase 3 — Resolution:** CollisionSystem
- Needs ammo entities to exist (spawned in Phase 2)
- Needs updated positions (from Phase 1)
- Spawns effects (explosions, sparks, loot)

**Phase 4 — Cleanup & Progression:** EffectSystem, AmmoLifetimeSystem, LevelUpSystem
- Process lifetime-based entity expiration
- Check level-up conditions after all combat resolved

## Refactoring order (simplest to most complex)

1. **AmmoLifetimeSystem** – single query, lifetime check → adds destroy/update commands to buffer
2. **EffectSystem** – same pattern repeated for spark/explosion/debug types
3. **PhysicsSystem** – reads components, writes velocity/position/rotation via commands in buffer
4. **CameraSystem** – read player pos, write camera target command in buffer
5. **CooldownHelper** – becomes a pure function: `GetCooldown(view, entity)`, command-based set via buffer
6. **PickupMagnetSystem** – reads + writes position/velocity, destroys collected items, applies XP/health via commands in buffer
7. **MineDriftSystem**, **MineRespawnSystem**, **EnemyShipSpawnSystem** – spawning systems use CreateEntityWithComponentsCommand with bundled components
8. **EnemyShipSystem** – AI logic adds commands to buffer
9. **TurretFiringSystem** – target finding (read-only) + firing (adds CreateEntityWithComponentsCommand for ammo with bundled components)
10. **LevelUpSystem** – reads player XP, creates PendingChoice entity via CreateEntityWithComponentsCommand in buffer
11. **CollisionSystem** – most complex: many interactions, spawns effects via CreateEntityWithComponentsCommand, adds commands to buffer

## Main loop changes (`SpaceVorsApp.cs`)

### Sequential (initial integration)

Helper method:
```csharp
void RunPhase(GameSystem[] systems)
{
    var view = new WorldView(em);
    foreach (var system in systems)
    {
        system.Update(view, FixedDeltaTime, system._commandBuffer);
        system._commandBuffer.Clear();
    }
    commandProcessor.Process(systems.SelectMany(s => s._commandBuffer.Commands));
}
```

Phase system collections (explicit arrays, instantiated once at class level):
- **Movement:** `_movementSystems = new[] { _physicsSystem, _cameraSystem }`
- **Actions:** `_actionSystems = new[] { _turretFiringSystem, _pickupMagnetSystem, _enemyShipSystem, _mineDriftSystem, _mineRespawnSystem, _enemyShipSpawnSystem }`
- **Resolution:** `_resolutionSystems = new[] { _collisionSystem }`
- **Cleanup & Progression:** `_cleanupSystems = new[] { _effectSystem, _ammoLifetimeSystem, _levelUpSystem }`

Main loop:
```csharp
while (accumulator >= FixedDeltaTime)
{
    RunPhase(_movementSystems);
    RunPhase(_actionSystems);
    RunPhase(_resolutionSystems);
    RunPhase(_cleanupSystems);

    accumulator -= FixedDeltaTime;
}
```

Each system is instantiated once and reused across frames (systems hold mutable state like timers). The arrays are immutable references to the same system instances. Each system holds its own `CommandBuffer` field, allocated once at construction time. At the start of each step, buffers are cleared before use — no per-frame allocations. Commands from all buffers are collected via `SelectMany` and applied in a single pass after all systems complete.

### Parallel (optional future optimization)

When profiling shows parallel execution is beneficial:
```csharp
async Task RunPhaseParallel(GameSystem[] systems)
{
    var view = new WorldView(em);
    foreach (var system in systems)
        system._commandBuffer.Clear();
    
    var tasks = systems.Select(s => Task.Run(() => s.Update(view, FixedDeltaTime, s._commandBuffer)));
    await Task.WhenAll(tasks);
    commandProcessor.Process(systems.SelectMany(s => s._commandBuffer.Commands));
}
```

## Parallelism model (future)

Within each phase, systems run in parallel:

- **Reads:** all come from `WorldView` — no locking needed, same underlying storage
- **Writes:** each system writes to its own `CommandBuffer` — no contention, no locking required
- **Mutation:** happens once after all systems finish, via `CommandProcessor.Process()` on collected commands — sequential but fast (just array operations)

Between phases, there is a full commit. Each new phase sees fresh data from the previous phase's mutations. This gives us both parallelism within phases and correct ordering across phases. CommandBuffers are reused across frames — cleared at the start of each step to avoid allocations.

## Tradeoffs

### Pros
- Systems are read-only about ECS state → easier to test, reason about, parallelize
- Clear separation: what the world looks like vs what we want to change it to
- Deterministic: commands applied in known order at end of each phase
- No accidental mutations during iteration (View has no mutation methods)
- Correct inter-phase ordering — physics updates visible before collision checks

### Cons
- CommandBuffer is mutable — systems have a side effect (adding to buffer), slightly less pure than returning a collection
- More indirection (system → commands buffer → processor)
- CollisionSystem's complex multi-pass logic needs careful handling as a single system
- More phases = more View creations and command processing passes, but each pass is fast

## Testing strategy

1. Unit tests for CommandProcessor (verify correct application order, deduplication)
2. Unit tests for WorldView (verify read-only access, query correctness)
3. Unit tests for each refactored system (pass View + empty CommandBuffer, assert commands added to buffer — no ECS mutations through View)
4. Integration: run full game loop, verify behavior unchanged
5. Existing ECS tests in `EcsTests.cs` remain valid (EntityManager API unchanged for GameInitializer/tests)

## Implementation phases

### Phase 1: Foundation
- Create `Commands.cs` with command types (`CreateEntityWithComponentsCommand` holding object[], `AddComponentCommand<T>` generic, `DestroyEntityCommand`) and simple mutable `CommandBuffer` class (one per system, no thread-safety needed)
- Create `WorldView.cs` with read-only query methods delegating to EntityManager
- Create `CommandProcessor.cs` — uses pattern matching on generic command types for dispatch; handles CreateEntityWithComponentsCommand by iterating object[] and adding each component via reflection
- Add tests for CommandProcessor and WorldView

### Phase 2: Simple systems (Phase 4 cleanup)
- Refactor AmmoLifetimeSystem — takes View + CommandBuffer, adds destroy/update commands to buffer
- Refactor EffectSystem — same pattern

### Phase 3: Movement phase
- Refactor PhysicsSystem — takes View + CommandBuffer, adds velocity/position update commands
- Refactor CameraSystem — takes View + CommandBuffer, adds camera target command

### Phase 4: Action systems (Phase 2)
- Refactor TurretFiringSystem, PickupMagnetSystem, EnemyShipSystem
- Systems take View + CommandBuffer, add commands to buffer during execution

### Phase 5: Spawning & drift systems (Phase 2)
- Refactor MineDriftSystem, MineRespawnSystem, EnemyShipSpawnSystem
- Refactor CooldownHelper — pure function for get, command-based set via buffer

### Phase 6: Resolution phase
- Refactor CollisionSystem (largest refactor) — takes View + CommandBuffer, uses CreateEntityWithComponentsCommand for spawning effects

### Phase 7: Progression phase
- Refactor LevelUpSystem — takes View + CommandBuffer, adds level-up choice entity creation via CreateEntityWithComponentsCommand

### Phase 8: Sequential integration
- Instantiate all systems once as class-level fields in SpaceVorsApp.cs
- Add `CommandBuffer` field to each system (allocated at construction)
- Create explicit immutable arrays for each phase (movement, actions, resolution, cleanup)
- Add `RunPhase(GameSystem[] systems)` helper that creates WorldView, runs systems sequentially, clears each buffer after use, and processes collected commands
- Update main loop to call RunPhase with the four phase collections
- Full game test
- Verify behavior matches pre-refactoring output

### Phase 9: Parallel execution (optional)
- Only if profiling shows parallelism is beneficial
- Add `RunPhaseParallel(GameSystem[] systems)` helper that clears all buffers, runs systems via Task.Run in parallel, collects commands, and processes them
- Update main loop to use parallel version
- Verify behavior matches sequential output

## Inter-phase data flow

The phased approach ensures correct ordering without sacrificing parallelism:

| Dependency | Phase A | Phase B | Result |
|---|---|---|---|
| Physics → Collision | Updates positions/velocities | Reads updated positions | Collision sees physics results |
| TurretFiring → Collision | Spawns ammo entities | Detects ammo collisions | Ammo exists before collision check |
| PickupMagnet → LevelUp | Applies XP to player | Checks level-up threshold | XP is current when checked |

Systems within the same phase have no data dependencies and can safely run in parallel. Systems across phases see each other's results through committed ECS state.

## Decision: phased command batches

**Decision: one batch per phase, applied before next phase starts.**

Each phase runs all its systems sequentially (initially) against a shared View, each system writing to its own CommandBuffer. After all systems complete, buffers are collected into one batch and applied to ECS. The next phase creates a fresh View and repeats. This gives us correct ordering across phases with no thread-safety concerns since each system has its own buffer. Parallelism within phases is deferred until profiling shows it's needed. The phased approach matches the current sequential system execution order (Physics → Actions → Collision → Cleanup) but enables parallelism where possible in the future.

## Decision: CommandBuffer lifecycle

**Decision: allocate once per system, clear at start of each step.**

Each system holds its own `CommandBuffer` as a field, allocated at construction time. Buffers are cleared before use within each phase (after systems write to them), not recreated each frame. This avoids per-frame allocations while keeping the isolation benefit of per-system buffers.
