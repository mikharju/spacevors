# ARCHITECTURE.md

## Stack

Language:
- C# (.NET)

Framework:
- Raylib-cs

Reason:
- editor-independent
- small API
- code-first
- easy for LLMs to understand
- minimal engine magic

## Overall architecture

Hexagonal architecture with ECS.

```
                Input
                  │
                  ▼
        Input Adapter
                  │
                  ▼
        Simulation (Core ECS)
        ├── Physics
        ├── Combat
        ├── AI
        ├── Progression
        └── Spawning
                  │
      ┌───────────┼───────────┐
      ▼           ▼           ▼
 Rendering     Audio      Save/Load
 Adapters     Adapters     Adapters
```

## Layers

### Domain

Pure game rules.

Contains:
- components
- systems
- math
- game state

No graphics.

No input.

No Raylib.

### Adapters

Translate between the outside world and the domain.

Examples:
- keyboard
- mouse
- rendering
- audio
- persistence

## ECS

Entities:
- integer IDs

Components:
- plain data only

Systems:
- pure logic operating on components

Examples (all in Domain/Components/, except Dead which lives in Combat/CollisionSystem.cs; DebugMarker is diagnostics-only):

```
Position Velocity Acceleration Rotation AngularVelocity
Player EnemyShip EnemyMine Asteroid Camera
Ammo FireCooldown Turret WeaponSlots TurretOffset ArcOffset PrimaryTarget
Explosion Spark BlueSpark GreenSpark HealthOrb XpPickup ShipDeathExplosion DebugMarker
Health PendingChoice PendingUpgradeOptions UpgradeCounts Dead
```

## Write patterns

Three ways systems write components, plus direct app-layer writes:

1. Deferred commands (default): a system adds writes to the phase CommandBuffer. One buffer per tick is applied once after every system in that phase finishes; later phases then read those values within the same tick. Within a phase all systems read pre-phase values for command-written components; last write wins in command order. Destructions are deferred until after all other commands of the flush, so an entity destroyed this phase can still receive writes in the same flush (they are discarded with it).
2. Direct ref mutation (speed exceptions): three systems mutate a single component in place via slot refs instead of queuing a command per entity — this avoids thousands of command allocations per tick on hot paths:
   - PositionIntegrationSystem integrates Position in place; it reads the pre-phase Velocity (PhysicsSystem's new-velocity commands are not applied yet), so position advances with last tick's velocity.
   - AmmoLifetimeSystem decrements Ammo.Lifetime in place; only destructions go through commands.
   - PickupMagnetSystem writes Player.Xp straight through WorldView.GetComponentRef so LevelUpSystem (same phase, runs later) reads the fresh value in the same tick.
3. Local accumulation + single flush: CollisionSystem accumulates per-entity velocity/position-correction/angular-velocity/health deltas in per-frame dictionaries and emits one command per entity at the end of Update — otherwise last-write-wins would drop all but the final hit on an entity struck several times in one frame.

Direct writes (app layer): GameSession writes straight to the EntityManager before the simulation step or while paused, never mid-phase:
- Pre-phase (form the tick's initial state; systems read them normally): Acceleration/AngularVelocity from input, turret Position/Rotation from SyncTurrets, diagnostic-key writes.
- While paused: upgrade application (Player/Turret/Health/WeaponSlots + new weapon turrets), pending-choice cleanup, thruster removal on pause entry.

Rules:
- Systems default to CommandBuffer; the direct ref mutations above are the only exceptions and must stay single-component in-place edits with no structural changes during iteration. Any new direct-write path requires updating this section first.
- Direct mutation is visible immediately to later systems in the same phase; command writes are not. Choose deliberately: use a ref when a same-phase reader needs the fresh value, otherwise queue a command.

## Phases

One tick runs four phases in order (SimulationRunner). The per-tick CommandBuffer is applied after each phase, so later phases read earlier phases' writes within the same tick:

1. Movement: PhysicsSystem → BlueSparkHomeSystem → PositionIntegrationSystem → AmmoLifetimeSystem
2. Action: TurretFiringSystem → EnemyShipSpawnSystem
3. Resolution: CollisionSystem → PickupMagnetSystem → LevelUpSystem → ShipDeathExplosionSystem → EffectSystem
4. Cleanup: MineDriftSystem → MineRespawnSystem → EnemyShipSystem → CameraSystem

Order within a phase matters (see Write patterns). The "Cleanup" name is historical — it holds AI, spawners and camera.

## Main loop

```
Ship selection (before game starts)

↓

Input

↓

Simulation

↓

Rendering
```

Ship selection is shown before the game loop begins.

Player chooses one of the defined ship types (`ShipType.All`: Scout, Fighter, Heavy, Shadow).

Game pauses during upgrade choice; resumes after selection.

Simulation uses a fixed timestep.

Rendering draws the latest simulated state; no interpolation between ticks.

## Physics

2D Newtonian.

Components:
- position
- velocity
- acceleration
- rotation
- angular velocity

Movement uses forces instead of directly setting velocity.

## Cross-system signaling

No event queues. Systems signal each other through components, written via the CommandBuffer (see Write patterns). A later system in a same or next phase reacts to the component it finds.

Examples:
- CollisionSystem adds Dead; ShipDeathExplosionSystem reacts with staged explosions
- PickupMagnetSystem raises Player.Xp (direct ref write, see Write patterns); LevelUpSystem spawns PendingChoice + PendingUpgradeOptions
- GameSession reads PendingChoice to pause and show the upgrade menu

## Determinism

- One world RNG owned by EntityManager (default seed 42), exposed as WorldView.Rng; all gameplay randomness (spawning, loot, scatter, upgrade shuffles) goes through it. No Random.Shared anywhere in src/.
- Elapsed time lives on EntityManager (WorldView.ElapsedTime); difficulty ramps and spawn intervals derive from it.
- Same seed → same run; covered by Tests/WorldRngTest.cs (same-seed spawn equality).

## Project layout

```
src/

    Game/                    -- app layer: window, input, rendering (Raylib-cs)
        SpaceVorsApp.cs          -- entry point: window + top-level loop (ship select <-> session)
        GameSession.cs           -- one playthrough: player input, fixed-timestep stepping, pause/menu, upgrades
        GameInitializer.cs       -- entity setup + world generation
        Renderer.cs              -- frame orchestration (scene composition)
        BackgroundRenderer.cs    -- starfield + clutter layers
        WorldRenderer.cs         -- world entities: asteroids, ammo, effects, pickups
        EnemyShipRenderer.cs     -- enemy ship sprites + fallbacks
        ShipSpriteRenderer.cs    -- player ship sprite (lit/flat)
        ThrusterFlameRenderer.cs -- thruster flames
        TargetingRenderer.cs     -- red corner brackets on the player's locked target
        HudRenderer.cs           -- health bar + game over text
        UpgradeMenuRenderer.cs   -- upgrade choice cards
        StatsScreenRenderer.cs   -- ship stats screen (Tab)
        ShipSelectScreen.cs      -- ship selection screen (scrollable list + input)
        RenderHelpers.cs         -- shared screen-space culling helpers
        ImageLoader.cs           -- texture loading (flat + lit sprite sets)
        Lighting.cs              -- lighting shader: directional light + point lights
        LightGatherer.cs         -- per-frame point-light collection for the shader
        LitSprite.cs             -- lit sprite data (base/normal/depth textures)
        LitSpriteMatcher.cs      -- matches texture files into lit sprites
        LitGroupRenderer.cs      -- batches lit draws per variant under one shader-mode block
        AsteroidSprite.cs        -- asteroid graphics variants

    Domain/                  -- pure game logic, no Raylib
        AI/                    -- enemy ship + mine spawning (incl. SpawnPlacement), factories, chase AI, drift
        Combat/                -- firing (incl. click-target priority), collisions, effects, death explosions, asteroid factory
        Components/            -- component records (entity, physics, combat, effect, gameplay)
        Physics/               -- force integration + position integration
        Progression/           -- XP/level-up, pickups, camera, blue spark homing
        Support/               -- SimulationRunner (phase ordering)
        EntityManager.cs       -- EntityManager + ComponentStorageBase + ComponentQuery<T1..T4>
        ComponentStorage.cs    -- ComponentStorage<T>: compact arrays, swap-pop
        Entity.cs              -- entity id type
        System.cs              -- GameSystem base class (Update contract)
        WorldView.cs           -- per-step view: reads, world RNG/elapsed time, viewport/mouse seams
        Commands.cs            -- command records + CommandBuffer
        CommandProcessor.cs    -- applies a CommandBuffer to the EntityManager
        SpatialGrid.cs         -- broad-phase spatial hash
        Vector2.cs             -- 2D math
        CoolDownHelper.cs      -- FireCooldown read helper
        DiagnosticLogger.cs    -- SPACEVORS_DIAGNOSTIC=1 logging (fps, per-system timings, events)

    RenderBench/             -- headless render benchmark (lit-sprite draw cost)

    Tests/                   -- xunit tests for domain logic + matchers
```

## Principles

Dependencies point inward.

Domain never references rendering, input, or audio (no Raylib).

Keep systems small and deterministic.

Keep data organized with other related data. 
For example: All ship related data in record structs related to ship, no ship colors or such in far away switch statements.

## Performance

- ComponentStorage<T>: compact arrays + entity→slot direct-index map (O(1) lookup), swap-pop deletion for O(1) removal; entity IDs stay stable across compaction. Iteration order is not insertion order — do not rely on it.
- Multi-component queries iterate the smallest participating storage and probe the rest with O(1) slot lookups, so iteration cost scales with the smallest set instead of N log N.
- Current status: Tests/PerformanceBenchmark.cs runs the full four-phase simulation at a fixed 120 fps timestep and checks an 8.3 ms frame budget per scenario. Ammo-heavy scenarios (up to ~15k live bullets) meet the budget; ship/asteroid-heavy scenarios currently exceed it — CollisionSystem is the known bottleneck (see plans/CODE_REVIEW.md, chunk 2).

## Troubleshooting

Check TROUBLE_SHOOTING.md for problems encountered in the past. When encountering new problems, update TROUBLE_SHOOTING.md.