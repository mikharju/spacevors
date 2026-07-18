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
```

## Main loop

```
Input

↓

Simulation

↓

Rendering
```

Simulation uses a fixed timestep.

Rendering interpolates if necessary.

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
        Program.cs

    Domain/
        Components/
        Systems/
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