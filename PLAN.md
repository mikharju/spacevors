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
- Tab: toggle ship stats screen (pauses the game; also works while picking an upgrade — cards are hidden behind it). Shows every upgradeable stat with its current value and how many times it was upgraded. Layout: ship/engine stats top-left, weapon stats top-right, upgrade cards in a bottom row. UI text scales with window width (1x at 1920px, capped at 2x) and shrinks to fit vertically on short windows
- L: force level-up (only when SPACEVORS_DIAGNOSTIC=1, for testing)
- M: spawn a test explosion at the fixed asteroid (0,-300) (only when SPACEVORS_DIAGNOSTIC=1, for testing)

### Diagnostics env vars (testing only)
- SPACEVORS_DIAGNOSTIC=1: enables [FRAME]/[UPGRADE]/[FIRE] logs, debug circles, fixed test asteroid at (0,-300), L key force level-up, M key test explosion
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
- ship stats screen (Tab): every upgradeable stat with current value + times upgraded (`UpgradeCounts` component, incremented in `ApplyUpgrade`); ship/engine stats top-left, weapon stats top-right, cards along the bottom; UI scales with window width (capped at 2x)

## Phase 4b

Difficulty scaling.

- shared elapsed time tracking across systems
- enemy ship spawn rate increases over 3 minutes (10s→4s intervals)
- mine respawn system with increasing frequency
- max 100 active enemy ships, ~23 mines cap

### Off-screen spawning

Enemies and mines never pop in on screen; they spawn just outside the current viewport (`SpawnPlacement.OutsideScreen`, margin 60px past the edge — viewport size flows from the window into `WorldView.ViewportSize` each tick, so resizing is handled).

- Enemy ships: forward quadrant (±45° of player velocity); initial velocity = half of player velocity + 40 px/s drift toward the player; face the player at spawn. Inheriting only half means fast players close on spawns quicker and slowing down doesn't fling enemies away. The existing drift-cancel AI brakes them into a chase
- No detection range: every enemy ship always turns toward the player and accelerates after it from any distance, capped at its own (slowish) Speed. Ships that spawn or drift out of view keep chasing and come back into view instead of coasting away forever
- Mines: forward quadrant while the player moves, any direction while stationary (no meaningful "front"); zero initial velocity, MineDriftSystem pulls them in as before
- Initial layout (GameInitializer) uses the same placement; takes viewport size as a parameter. Initial enemy ships additionally spawn at 1600–3200px from the player (never closer than just outside the screen), so they start beyond firing range and give the player a grace period before the first contact

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
- Asteroids (small + large) load as lit sprites through the same matcher; a set's base may be `<name>-texture.png` or a plain `<name>.png`. The loader scans only top-level files, so `not-in-use/` subfolders are ignored. Variant count = number of loaded bases (`Asteroid.Small/LargeVariantCount`)
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
- Turn flame: diagonal RCS pair fires while turning — front thruster on the side opposite the turn, rear thruster on the turn's side (turning right = left-front + right-rear). Front flame points forward from the nose corner; rear flame trails backward. Both are very small, sized like a weak lateral thrust flame (MinVisibleFlameIntensity scale) so they do not compete with the main booster. Turn rate comes from per-entity previous rotation tracked in a small dictionary inside the renderer (pruned each frame, cleared on new game); normalized by RotationSpeed/TurnRate
- Mines have no Acceleration or Rotation: flame behind motion, direction = −normalize(Velocity), intensity = |velocity| / mine.Speed
- Entities with Dead component are skipped
- Flame shape: one triangle per thruster, orange→yellow color by intensity

Stage 1 (thrust flames) — done:
- ThrusterFlameRenderer with thrust flames for player + enemy ships
- EnemyShipSystem: set Acceleration(Zero) on early-exit paths (no player, overlapping player, spin-damping branch, inside firing range) so stale acceleration does not produce phantom flames. Makes the component mean "thrust currently applied"
- Verified via screenshots (Shadow, Balanced engines): W/Shift forward flame behind hull visibly larger than A/D side flames and S retro-burner (min visible size); enemy chase flames

Stage 2 (turn flames) — done:
- Previous-angle tracking + side thruster flames for player and enemy ships
- Verified via screenshots + pixel analysis (Shadow, Fighter mid-turn): diagonal pair appears at the correct corners for the turn direction; flames small relative to main thrust flame

Stage 3 (mines) — done:
- Velocity-based mine flames
- Verified via pixel scan pre-cleanup; same DrawFlame path as verified thrust/turn flames

Stage 4 (tuning) — done:
- Size/color/width tuned via screenshots
- Performance check with LoadTestWeapon + max enemies passed (~125 flame entities, negligible)

Note: this raylib-cs build culls counter-clockwise triangles in screen space; DrawFlame emits vertices clockwise on purpose.

## Phase 8

Dynamic point lights (additional light sources).

Extends the Phase 5 lighting shader with a fixed set of per-frame point lights so explosions and thruster flames illuminate nearby lit sprites (ships + asteroids). Flat/fallback path unchanged.

Design decisions:
- Point lights live in the existing Lighting fragment shader as a uniform array `uLights[MAX_LIGHTS]` (world pos, radius, intensity) plus a shared warm tint; per-fragment cost is O(MAX_LIGHTS), independent of entity count
- MAX_LIGHTS fixed at 16. Emitter entities may exceed it; the list fills in priority order and overflow is dropped (no sorting). The cap is what keeps GPU + uniform cost bounded as the world grows toward 10k/100k objects
- Only lit sprites respond (player/enemy ships, asteroids). Mines/ammo/sparks/pickups stay flat — they are not drawn through the shader
- Light sources, by priority:
  - Ship death explosions (initial + secondary + final) and mine blasts — all use the existing Explosion component; rare and large, included first. Ammo hits do NOT spawn Explosion components (only sparks), so there is no per-shot light to limit
  - Thruster flames — numerous (up to ~125 in load test); fill remaining slots after explosions and are dropped first under pressure. Gives engine glow on nearby hulls/rocks without unbounded cost
- Intensity fades with effect life (lifeRatio); radius maps from Explosion.Radius / flame size; one shared warm tint for all lights keeps uniform count low (per-light color is an optional refinement)
- Point lights shade with sprite normals: per-light Lambertian term against `normalScreen` (light direction y-flipped from GL space, light elevated `PointLightHeight` px toward viewer so flat surfaces stay lit). No depth-map occlusion for point lights — would need 16 extra depth fetches per lit pixel for dubious benefit
- CPU gathers the list once per frame and uploads it once (K vec4s). No new draw calls: the per-sprite batch flush already exists in Lighting.TryDraw, so adding lights changes only fragment work + a few uniforms

Stage 1 (shader plumbing) — done: `uLights[16]` vec4 array (screen pos xy with GL bottom-left origin, radius z, intensity w); `Lighting.BeginFrame` + `AddLight`; one `SetShaderValueV` upload per lit-sprite draw; idle screenshot pixel-identical to pre-change baseline at zero lights
Stage 2 (explosion lights) — done: new `LightGatherer.Collect` gathers Explosion entities first (radius grows as the effect fades, intensity = lifeRatio); screenshots confirm ship deaths + mine blasts light nearby asteroid/player hull and fade out
Stage 3 (thruster flame lights, capped) — done: player/enemy/mines add a light at the flame base behind the hull after explosions; screen+radius culling on CPU; overflow drops thrusters first. No proximity-to-player bias in v1
Stage 4 (tuning) — done: `MaxPointLightContribution` 1.0, `ThrustLightIntensity` 0.8 via screenshots (boost glow clearly visible, unboosted subtle); A/B perf check with Scout LoadTestWeapon under software rendering (deterministic seed 42): steady-state avg fps 116.3 → 115.7 — no regression

Cheaper alternative if K-growth must be zero: make thruster flames self-emissive only (brighten the flame sprite / additive halo) instead of scene lights. Loses "flames light nearby rocks" but removes the largest emitter from the cap entirely.

## Camera follows mouse

Camera should be changed so it isn't simply mostly centered, but follows the mouse:
- If mouse is near center of window, then camera will be centered on player ship
- If mouse is moved further toward window edge, then camera will drift that way from player ship, but it will still keep player ship visible
- Camera center should be somewhere between player ship and mouse cursor

## Mouse clicks to set primary target

Allow player to set primary target for weapons.
- Add some targeting bracket on currently targeted enemy ship
- Initially all weapons will shoot targeted ship at much larger range than auto targeting range
- If targeted enemy is destroyed, no target is automatically selected
- If no target is selected, then weapons will shoot at closest target with current targeting priorities

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