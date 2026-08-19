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

### Ship selection (start)
- 1/2/3/4 or click: choose ship

### In-game
- W: forward thrust
- S: backward thrust
- A: left sideways thrust
- D: right sideways thrust
- Q/E: rotate
- Shift: boost (forward only, 2.5x)
- Mouse: aim (optional)
- Space: brake (optional)
- L: force level-up (only when SPACEVORS_DIAGNOSTIC=1, for testing)

### Diagnostics env vars (testing only)
- SPACEVORS_DIAGNOSTIC=1: enables [FRAME]/[UPGRADE]/[FIRE] logs, debug circles, fixed test asteroid at (0,-300), L key force level-up
- SPACEVORS_DIAG_UPGRADES="RailGun,Hp,FireRate:MachineGun": scripts upgrade choices, one entry per level-up. Entry is a new weapon name or `Stat:WeaponName`. When exhausted, falls back to normal random pool

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

## Phase 5

Dynamic lighting.

- per-sprite normal + depth maps: `<name>-normals.png`, `<name>-depth.png` next to `<name>-texture.png`
- GLSL shader (src/Game/Lighting.cs): directional light from top-right, self-shadowing via depth map
- flat texture fallback when maps or shader are unavailable
- Stage 1 (done): LitSprite matching in ImageLoader + tests
- Stage 2 (done): Shadow ship (key 4), Lighting.Init/Shutdown/TryDraw, select screen + gameplay rendering
- Stage 3: tuning — exposure/washout on bright textures, normal Y convention check, shadow strength
- In `Lighting.TryDraw`, `BeginShaderMode` must run before the map `Set*` calls: its batch flush clears raylib's texture-unit registry, so maps set earlier are lost and the sprite renders with the previous lit sprite's maps (verified against raylib 6.0)

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

## Phase 7

Thruster flames.

All ships (player, enemy) and mines show engine flames while accelerating or turning. Flame size is proportional to thrust force: `force = |acceleration| × mass`, with `mass ∝ radius²` (same convention as CollisionSystem).

Design decisions:
- Rendering-only feature: no new components or systems. Renderer already reads components directly everywhere; a domain ThrusterState component would be an unnecessary abstraction
- New file src/Game/ThrusterFlameRenderer.cs (keeps Renderer.cs small, one responsibility per type), called from DrawScene before ships/mines so flames draw behind them
- Thrust flame: one per active axis. All player axes are normalized by a single shared max (Thrust×Boost) so flame size reflects absolute thrust force across axes — main booster burns visibly larger than weak side/back thrusters; enemy flame is normalized by EnemyShip.Acceleration (drift-cancel stays below the gate). Flames passing the gate render at least MinVisibleFlameIntensity size so weak thrusters stay visible; flame base sits at the hull edge (radius × 1.0) so small flames are not hidden behind large ships. Direction = −normalize(axis acceleration). Flame length/width scale with intensity and ship radius
- Turn flame: side thruster on the side opposite the turn. Turn rate comes from per-entity previous rotation tracked in a small dictionary inside the renderer (pruned each frame, cleared on new game); normalized by RotationSpeed/TurnRate
- Mines have no Acceleration or Rotation: flame behind motion, direction = −normalize(Velocity), intensity = |velocity| / mine.Speed
- Entities with Dead component are skipped
- Flame shape: one triangle per thruster, orange→yellow color by intensity

Stage 1 (thrust flames) — done:
- ThrusterFlameRenderer with thrust flames for player + enemy ships
- EnemyShipSystem: set Acceleration(Zero) on early-exit paths (no player, out of detection range, spin-damping branch, inside firing range) so stale acceleration does not produce phantom flames. Makes the component mean "thrust currently applied"
- Verified via screenshots (Shadow, Balanced engines): W/Shift forward flame behind hull visibly larger than A/D side flames and S retro-burner (min visible size); enemy chase flames

Stage 2 (turn flames) — done:
- Previous-angle tracking + side thruster flames for player and enemy ships
- Verified via screenshot: mouse turn shows forward-pointing flank flame on the torque-producing side (physically self-consistent both directions)

Stage 3 (mines) — done:
- Velocity-based mine flames
- Verified via pixel scan pre-cleanup; same DrawFlame path as verified thrust/turn flames

Stage 4 (tuning) — done:
- Size/color/width tuned via screenshots
- Performance check with LoadTestWeapon + max enemies passed (~125 flame entities, negligible)

Note: this raylib-cs build culls counter-clockwise triangles in screen space; DrawFlame emits vertices clockwise on purpose.

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