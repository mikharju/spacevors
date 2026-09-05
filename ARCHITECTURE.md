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

Examples (all in Domain/Components/, except Dead which lives in Combat/CollisionSystem.cs):

```
Position Velocity Acceleration Rotation AngularVelocity
Player EnemyShip EnemyMine Asteroid Camera
Ammo FireCooldown Turret WeaponSlots TurretOffset ArcOffset
Explosion Spark BlueSpark GreenSpark HealthOrb XpPickup ShipDeathExplosion
Health PendingChoice PendingUpgradeOptions UpgradeCounts Dead
```

## Write patterns

Two ways to write components:

- Deferred commands (systems): a system adds writes to the phase CommandBuffer. The buffer is applied once, after every system in that phase finishes. Within a phase all systems read pre-phase values for command-written components; last write wins in system order.
- Direct writes (app layer only): GameSession writes straight to the EntityManager before the simulation step — Acceleration/AngularVelocity from input, turret Position/Rotation from SyncTurrets, thruster removal on pause. These are pre-phase: they form the tick's initial state, so systems read them normally.

Rules:
- Systems never write components directly; always via CommandBuffer. One documented exception: PickupMagnetSystem writes Player.Xp straight through WorldView.GetComponentRef so LevelUpSystem (same phase, runs later) reads the fresh value in the same tick.
- The app layer writes only before a step (pre-phase) or while paused, never mid-phase.

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
- PickupMagnetSystem raises Player.Xp; LevelUpSystem spawns PendingChoice + PendingUpgradeOptions
- GameSession reads PendingChoice to pause and show the upgrade menu

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
        AI/                    -- enemy ship + mine spawning, chase AI, drift
        Combat/                -- firing, collisions, effects, death explosions
        Components/            -- component records (entity, physics, combat, effect, gameplay)
        Physics/               -- force integration + position integration
        Progression/           -- XP/level-up, pickups, camera, blue spark homing
        Support/               -- SimulationRunner (phase ordering)
        EntityManager.cs       -- entities + ComponentStorage<T> (compact arrays, swap-pop)
        WorldView.cs           -- per-step read view over the EntityManager
        Commands.cs            -- command records + CommandBuffer
        CommandProcessor.cs    -- applies a CommandBuffer to the EntityManager
        SpatialGrid.cs         -- broad-phase spatial hash
        Vector2.cs             -- 2D math

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

ComponentStorage<T> uses compact arrays with swap-pop deletion for O(1) removal.

Iteration is O(count) instead of O(N log N).

Entity IDs remain stable across compaction.

## Troubleshooting

Check TROUBLE_SHOOTING.md for problems encountered in the past. When encountering new problems, update TROUBLE_SHOOTING.md.