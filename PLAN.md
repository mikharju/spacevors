# PLAN.md

## Vision

A minimalist Vampire Survivors-style game in space.

The player pilots a spacecraft using Newtonian flight while surviving increasingly dangerous enemy waves.

Runs entirely from code with no editor-generated assets.

## Core gameplay

Player:
- thrust
- rotate
- inertia
- drift
- boost

Enemies:
- pursue player
- swarm
- collide
- attack

Weapons:
- automatic
- upgrade over time

Progression:
- gain XP
- level up
- choose upgrades

Goal:
- survive as long as possible.

## Controls

- W: thrust
- A/D: rotate
- Shift: boost
- Mouse: aim (optional)
- Space: brake (optional)

## MVP

- player ship
- Newtonian movement
- camera
- enemies
- automatic weapon
- projectiles
- collisions
- health
- XP
- level-ups
- game over

## Phase 1

Foundation.

- project setup
- ECS
- game loop
- rendering
- input
- fixed timestep

## Phase 2

Movement.

- Newtonian physics
- camera
- player controls

## Phase 3

Combat.

- enemies
- projectiles
- collision
- health
- death

## Phase 4

Progression.

- XP
- level-ups
- upgrades
- enemy spawning

## Phase 4b

Difficulty scaling.

- shared elapsed time tracking across systems
- enemy ship spawn rate increases over 3 minutes (10s→4s intervals)
- mine respawn system with increasing frequency
- max 100 active enemy ships, ~23 mines cap

## Phase 4c

Enemy variants.

- Interceptor: smaller (15px), purple, faster acceleration (15), low fire rate (0.6/s)
- Heavy Cannon: larger (28px), dark red-gray, 2 damage ammo, slower fire rate (0.8/s)
- Standard: unchanged (20px), red, baseline stats
- All three spawn equally (~33% each)

## Phase 6

Content.

Possible upgrades:
- faster fire
- extra projectiles
- piercing
- homing
- drones
- shields
- engine upgrades
- stronger boost

Enemy types:
- swarm
- interceptor
- tank
- sniper
- kamikaze

## Future

- bosses
- asteroid fields
- procedural sectors
- elite enemies
- save/load
- replays
- mod-friendly data definitions

## Done criteria

The game is considered complete when:
- gameplay is fun
- architecture remains simple
- code is easy for local LLMs to navigate
- new weapons and enemies require minimal changes
```