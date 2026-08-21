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
- events
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

Example:

```
Transform
Velocity
Health
Weapon
Enemy
Player
Projectile
Lifetime
Experience
Loadout TurretOffset ArcOffset PendingChoice BlueSpark UpgradeExplosion
```

## Main loop

```
Loadout selection (before game starts)

↓

Input

↓

Simulation

↓

Rendering
```

Loadout selection is shown before the game loop begins.

Player chooses Forward or Broadside configuration.

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

## Events

Prefer event queues instead of systems directly calling each other.

Examples:
- EntityDied
- ProjectileHit
- LevelUp
- ExperienceCollected

## Project layout

```
src/

    Game/
        SpaceVorsApp.cs          -- game loop + input handling
        GameInitializer.cs       -- entity setup + world generation
        Renderer.cs              -- frame orchestration (scene composition)
        BackgroundRenderer.cs    -- starfield + clutter layers
        WorldRenderer.cs         -- world entities: asteroids, ammo, effects, pickups
        EnemyShipRenderer.cs     -- enemy ship sprites + fallbacks
        ShipSpriteRenderer.cs    -- player ship sprite (lit/flat)
        ThrusterFlameRenderer.cs -- thruster flames
        HudRenderer.cs           -- health bar + game over text
        UpgradeMenuRenderer.cs   -- upgrade choice cards
        ShipSelectScreen.cs      -- ship selection screen (scrollable list + input)
        RenderHelpers.cs         -- shared screen-space culling helpers

    Domain/
        Components/          -- Loadout, TurretOffset, ArcOffset, PendingChoice, etc.
        Systems/             -- BlueSparkHomeSystem, UpgradePickupSystem, etc.
        Events/
        Math/

    Infrastructure/
        Rendering/
        Input/
        Audio/
        Save/

    Tests/
```

## Principles

Dependencies point inward.

Domain never references infrastructure.

Keep systems small and deterministic.

Keep data organized with other related data. 
For example: All ship related data in record structs related to ship, no ship colors or such in far away switch statements.

## Performance

ComponentStorage<T> uses compact arrays with swap-pop deletion for O(1) removal.

Iteration is O(count) instead of O(N log N).

Entity IDs remain stable across compaction.

## Troubleshooting

Check TROUBLE_SHOOTING.md for problems encountered in the past. When encountering new problems, update TROUBLE_SHOOTING.md.