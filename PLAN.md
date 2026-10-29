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
- Arrows/WASD or mouse wheel: browse the scrollable ship list
- Click, Enter, or 1/2/3/4: choose ship

### In-game
- W: forward thrust
- S: backward thrust
- A: left sideways thrust
- D: right sideways thrust
- Shift: boost (forward only, 2.5x)
- Mouse: aim — the ship rotates toward the cursor (primary rotation)
- Left click: lock the clicked enemy ship, mine or asteroid as primary target (red corner brackets); all weapons then prioritize it at extended range. Clicking empty space clears the target. Rocks are targets only this way — the guns never shoot scenery on their own
- Tab: toggle ship stats screen (pauses the game; also works while picking an upgrade — cards are hidden behind it). Shows every upgradeable stat with its current value and how many times it was upgraded, how many hull mounts of each size are filled, and per weapon its hull mount with firing arc and turret turn speed. Layout: ship/engine stats top-left, weapon stats top-right, upgrade cards in a bottom row. UI text scales with window width (1x at 1920px, capped at 2x) and shrinks to fit vertically on short windows
- Upgrade choice: 1-5 or click a card. A new-weapon card marks the weapon's size in its prominent line (large / small) and lists its base numbers: range, damage (with pellet count when it fires several), attack speed and projectile speed — "beam instant" for hitscan guns. Choosing one opens the turret-mount screen — the hull drawn large, every mount shown as a circle with its firing arc and a number that never changes, mounts that can take the weapon ringed in green, and the cargo of inactive weapons listed at the left. With a weapon in hand: click a mount or press its number to fit it (a filled mount swaps, the displaced gun going into cargo and into hand), C stores the weapon in cargo, right-click puts it down, Esc leaves the screen (back to the same cards if the weapon was never placed)
- Turret mounts and cargo from the stats screen: Tab, then click "Manage mounts and cargo". With nothing in hand, click a mounted turret or press its number to take it down into cargo (it stops firing and stops being offered upgrades); click a cargo weapon to pick it up, then click a mount to fit it. Esc returns to the stats screen. Everything is free and unlimited — rearranging is not a choice the game charges for
- R: restart from game over (back to ship selection)
- L: force level-up (only when SPACEVORS_DIAGNOSTIC=1, for testing)
- M: spawn a test explosion at the fixed asteroid (0,-300) (only when SPACEVORS_DIAGNOSTIC=1, for testing)
- N: spawn a test enemy ship 250px up-right of the player with huge health (only when SPACEVORS_DIAGNOSTIC=1, for click-targeting tests)
- H: set player health to 10000 so test enemies cannot end the run (only when SPACEVORS_DIAGNOSTIC=1, for testing)
- T: toggle a position pin that freezes the player in place — kickback and collisions cannot move it (only when SPACEVORS_DIAGNOSTIC=1, for stable visual tests)
- K: halve hp of the locked target or nearest enemy ship to reach damage smoke tiers quickly (only when SPACEVORS_DIAGNOSTIC=1, for testing)

### Diagnostics env vars (testing only)
- SPACEVORS_DIAGNOSTIC=1: enables [FRAME]/[UPGRADE]/[FIRE] logs, debug circles, fixed test asteroid at (0,-300), L key force level-up, M key test explosion, N key test enemy spawn, H key invincible player, T key position pin, K key halve target hp, J key halve player hp, U key rack-armed test hull (enemy missiles), B key guided-round bench (8 headings x 2 plume sizes, drawn in screen space), V key plume anchor overlay (marks the computed plume centre and heading line on every guided round)
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
- enemy ship spawn interval ramps from random 5–10s to random 2–4s over 3 minutes
- mine respawn system with increasing frequency (random 10–20s → 4–8s)
- max 100 active enemy ships, 23 mines cap

### Off-screen spawning

Enemies and mines never pop in on screen; they spawn just outside the current viewport (`SpawnPlacement.OutsideScreen`, margin 60px past the edge — viewport size flows from the window into `WorldView.ViewportSize` each tick, so resizing is handled).

- Enemy ships: forward quadrant (±45° of player velocity); initial velocity = half of player velocity + 40 px/s drift toward the player; face the player at spawn. Inheriting only half means fast players close on spawns quicker and slowing down doesn't fling enemies away. The existing drift-cancel AI brakes them into a chase
- No detection range: every enemy ship always turns toward the player and accelerates after it from any distance, capped at its own (slowish) Speed. Ships that spawn or drift out of view keep chasing and come back into view instead of coasting away forever
- Mines: forward quadrant while the player moves, any direction while stationary (no meaningful "front"); zero initial velocity, MineDriftSystem pulls them in as before
- Initial layout (GameInitializer) uses the same placement; takes viewport size as a parameter. Initial enemy ships additionally spawn at 2400–5000px from the player (never closer than just outside the screen), so they start beyond firing range and arrive one at a time over ~90 s instead of as a pack (Phase 4d tuning)

## Phase 4c

Enemy variants (stats in `EnemyShipType`, all ammo does 1 damage):

- Interceptor: radius 45px, speed 90, high acceleration (85), low fire rate (0.6/s), 2 hp
- Heavy Cannon: radius 78px, slow (speed 50), slow ammo (160 px/s), fire rate 0.8/s, 5 hp
- Standard: radius 20px, speed 65, acceleration 45, baseline fire rate (1.5/s), 3 hp
- All three spawn equally (~33% each); enemy turret ammo does 1 damage at base (time-based tiers in Phase 4d add on top)

## Phase 4d

Difficulty scaling fix (plans/DIFFICULTY_SCALING.md P1–P5, implemented). Fixes the inverted curve — hard start, empty late game. All deterministic: derived from `ElapsedTime` and current player velocity, no new RNG sources (`WorldRngTest` still passes).

- **Cull stale ships** (`EnemyShipSystem`): enemy ships beyond 5500px from the player are destroyed in the existing per-ship loop; frees the 100-ship cap for fresh threats. Must stay above the initial spawn band (5000px)
- **Speed scales with player speed**: effective top-speed cap = `max(base, 0.75 × playerSpeed)` with a hard ceiling at 2× base — applied to both the velocity clamp and the drift-cancel threshold; fast movement no longer guarantees escape, slow play is unchanged
- **Spawn rate scales with player speed** (`EnemyShipSpawnSystem`): interval divided by `clamp(playerSpeed / 100 px/s, 1, 3)` — threat density per unit distance stays roughly constant at any speed (pure `NextInterval`/`SpeedFactor`, same RNG call count)
- **Survivable start**: initial mines 15 → 9; grace periods: mine respawn delay 10 → 20 s, ship spawn delay 5 → 9 s
- **Time-based stat tiers** (`EnemyShipFactory`, applied at spawn time only — no mid-run mutation): `tier = clamp(floor(elapsed / 60 s), 0, 10)`; HP ×(1 + 0.25·tier) rounded away from zero, turret damage base 1 + tier/2, fire rate ×(1 + 0.1·tier); stats stop growing after 10 minutes (MaxTier cap)

## Phase 5

Dynamic lighting.

- per-sprite normal + depth maps: `<name>-normals.png`, `<name>-depth.png` next to `<name>-texture.png`
- GLSL shader (src/Game/Lighting.cs): directional light from top-right, self-shadowing via depth map
- flat texture fallback when maps or shader are unavailable
- Asteroids (small + large) load as lit sprites through the same matcher; a set's base may be `<name>-texture.png` or a plain `<name>.png`. The loader scans only top-level files, so `not-in-use/` subfolders are ignored. Variant count = number of loaded bases (`Asteroid.Small/LargeVariantCount`)
- Stage 1 (done): LitSprite matching in ImageLoader + tests
- Stage 2 (done): Shadow ship (key 4), Lighting.Init/Shutdown + BeginDraw/Draw/EndDraw, lit draws batched per sprite variant in LitGroupRenderer, select screen + gameplay rendering
- Stage 3: tuning — exposure/washout on bright textures, normal Y convention check, shadow strength
- In `Lighting.BeginDraw`, `BeginShaderMode` must run before the map `Set*` calls: its batch flush clears raylib's texture-unit registry, so maps set earlier are lost and the sprite renders with the previous lit sprite's maps (verified against raylib 6.0). `LitGroupRenderer` keeps one BeginDraw/EndDraw block per sprite variant so the map uniforms stay constant inside a block

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
- New file src/Game/ThrusterFlameRenderer.cs (keeps Renderer.cs small, one responsibility per type), called from WorldRenderer.Draw before player/enemy ships so flames draw behind them
- Thrust flame: one per active axis. All player axes are normalized by a single shared max (Thrust×Boost) so flame size reflects absolute thrust force across axes — main booster burns visibly larger than weak side/back thrusters; enemy flame is normalized by EnemyShip.Acceleration (drift-cancel stays below the gate). Flames passing the gate render at least MinVisibleFlameIntensity size so weak thrusters stay visible; flame base sits at the hull edge (radius × 1.0) so small flames are not hidden behind large ships. Direction = −normalize(axis acceleration). Flame length/width scale with intensity and ship radius
- Turn flame: diagonal RCS pair fires while turning — front thruster on the side opposite the turn, rear thruster on the turn's side (turning right = left-front + right-rear). Front flame points forward from the nose corner; rear flame trails backward. Both are very small, sized like a weak lateral thrust flame (MinVisibleFlameIntensity scale) so they do not compete with the main booster. Turn rate comes from the AngularVelocity component (written for the player in GameSession, for enemies by EnemyShipSystem); normalized by RotationSpeed/TurnRate
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
- Point lights live in the existing Lighting fragment shader as a uniform array `uLights[MaxPointLights]` (screen pos, radius, intensity) plus a shared warm tint; per-fragment cost is O(MaxPointLights), independent of entity count
- MaxPointLights fixed at 16 in Lighting.cs; the shader source is generated from that constant so the two cannot drift. Emitter entities may exceed it; the list fills in priority order and overflow is dropped (no sorting). The cap is what keeps GPU + uniform cost bounded as the world grows toward 10k/100k objects
- Only lit sprites respond (player/enemy ships, asteroids). Mines/ammo/sparks/pickups stay flat — they are not drawn through the shader
- Light sources, by priority:
  - Ship death explosions (initial + secondary + final) and mine blasts — all use the existing Explosion component; rare and large, included first. Ammo hits do NOT spawn Explosion components (only sparks), so there is no per-shot light to limit
  - Thruster flames — numerous (up to ~125 in load test); fill remaining slots after explosions and are dropped first under pressure. Gives engine glow on nearby hulls/rocks without unbounded cost
- Intensity fades with effect life (lifeRatio); radius maps from Explosion.Radius / flame size; one shared warm tint for all lights keeps uniform count low (per-light color is an optional refinement)
- Point lights shade with sprite normals: per-light Lambertian term against `normalScreen` (light direction y-flipped from GL space, light elevated `PointLightHeight` px toward viewer so flat surfaces stay lit). No depth-map occlusion for point lights — would need 16 extra depth fetches per lit pixel for dubious benefit
- CPU gathers the list once per frame and uploads it once (K vec4s). No new draw calls: the per-sprite batch flush already exists in Lighting.EndDraw, so adding lights changes only fragment work + a few uniforms

Stage 1 (shader plumbing) — done: `uLights[16]` vec4 array (screen pos xy with GL bottom-left origin, radius z, intensity w); `Lighting.BeginFrame` + `AddLight`; one `SetShaderValueV` upload per lit-sprite draw; idle screenshot pixel-identical to pre-change baseline at zero lights
Stage 2 (explosion lights) — done: new `LightGatherer.Collect` gathers Explosion entities first (radius grows as the effect fades, intensity = lifeRatio); screenshots confirm ship deaths + mine blasts light nearby asteroid/player hull and fade out
Stage 3 (thruster flame lights, capped) — done: player/enemy/mines add a light at the flame base behind the hull after explosions; screen+radius culling on CPU; overflow drops thrusters first. No proximity-to-player bias in v1
Stage 4 (tuning) — done: `MaxPointLightContribution` 1.0, `ThrustLightIntensity` 0.8 via screenshots (boost glow clearly visible, unboosted subtle); A/B perf check with Scout LoadTestWeapon under software rendering (deterministic seed 42): steady-state avg fps 116.3 → 115.7 — no regression

Cheaper alternative if K-growth must be zero: make thruster flames self-emissive only (brighten the flame sprite / additive halo) instead of scene lights. Loses "flames light nearby rocks" but removes the largest emitter from the cap entirely.

## Stats directory (Domain/Stats) — done

All tuning data lives in `src/Domain/Stats/` as code-first record structs + named constants: player ships/engines (`PlayerShipStats`), weapons/loadouts (`WeaponStats`), enemy types + spawn weights + tier curves + shared enemy weapon (`EnemyShipStats`), mines (`MineStats`), upgrades + XP curve (`UpgradeStats`), loot/magnet values (`LootStats`), and the whole difficulty/spawn curve in `SpawningStats` (initial world, enemy-ship spawning, mine respawns, chase/cull). `Components/` holds runtime per-entity components only. See plans/STATS_DIRECTORY.md.

## Camera follows mouse — done

Camera drifts based on the mouse's screen offset from window center (`CameraSystem`):
- Mouse inside a dead zone (5% of half-viewport) → camera stays centered on player ship
- Drift ramps linearly to `MaxDriftFraction` (0.5) of the half-viewport at the window edge, so at full deflection the player sits a quarter-screen from the edge — always visible
- Corner offsets are normalized before mapping, so diagonal drift never exceeds edge drift
- The camera tracks the player directly (per-tick motion is already smooth); only the mouse-driven drift eases in at `DriftFollowSpeed` (1.5/s), so fast mouse moves don't yank the screen — the eased offset persists on the `Camera.Drift` component
- Mouse screen position flows into the simulation via `WorldView.MouseScreenPosition` (same seam as `ViewportSize`, set per tick in GameSession); aiming math is unchanged because it converts mouse→world through the camera target, so there is no feedback loop

## Mouse clicks to set primary target — done

Left click locks the clicked enemy ship or mine as primary target; clicking empty space clears it.

Implementation:
- `PrimaryTarget(Entity)` component on the player entity (CombatComponents.cs); set/cleared by GameSession from the left-click input via `PrimaryTargetPicker.Pick` (Domain/Combat) — the picker is pure domain logic, unit-tested without graphics
- Forgiving click zones scale with collision radius so small targets stay clickable: ships ×1.5 (radius ≤ 45) / ×1.1 (larger); mines ×4 (small) / ×2 (large). Multiple candidates → closest center wins; dead ships are ignored
- `TurretFiringSystem` checks the locked target before the normal auto search: it fires at it when in arc and within targeted range = min(3× turret.Range, AmmoSpeed × ShotLifetime) — extended reach without extending shot lifetime. Out of range/arc or destroyed → falls back to normal closest-target behavior (no auto re-selection); a dead target's component is cleared each tick
- `TargetingRenderer` (Game layer) draws red corner brackets around the live target, after ships and mines so they overlay both; skips dead/off-screen targets (later extended with auto-target brackets + HP bars — see next section)
- Entity IDs are never reused, so a stale `PrimaryTarget` can only ever point at a dead entity — validity check = has Position + EnemyShip/EnemyMine + no Dead

## Enemy hp bar visible on targeting graphic — done

Player turrets mark their current target every tick; auto-marked enemies get blue-grey brackets, the manually locked target gets bright red, and every bracket shows an HP bar (green→red) above it.

Implementation:
- `Health` now carries `(Current, Max)` so any damaged entity can report its own max — damage writes preserve Max, heals/upgrades write both
- `AutoTargetMark(LastTargetedAt)` component (CombatComponents.cs): player turrets add/refresh it on their selected enemy ship or mine every tick. Selection is a pure function of state, so per-tick runs keep firing behavior bit-identical; enemy turrets and asteroid last-resort picks never mark
- `TurretFiringSystem.FindTarget` returns the target entity alongside aim data (`TargetSelection`) so the caller can stamp the mark without re-searching
- `TargetingRenderer` (Game layer) draws both bracket kinds after ships/mines: manual red takes priority over auto on the same entity; auto brackets expire 1 s after last selection using world time (frozen while paused); dead and off-screen targets are skipped
- HP bar above each bracket: dark background, fill lerps green→red by current/max ratio; entities without Health draw no bar
- Verified in-game via manual playtest (brackets + bars on auto and manual targets); 6 new mark-lifecycle tests in TurretTargetMarkTest; PerformanceBenchmark worst case (1k ships) keeps TurretFiringSystem at ~1.1 ms/frame — within budget, no optimization needed

## Graphical damage indicators — done

Damaged ships emit grey smoke puffs below 2/3 hp, more smoke plus white sparks below 1/3 hp; both fade after 1 second and trail behind moving ships.

Implementation:
- `SmokePuff(Lifetime, InitialLifetime, Radius)` component (EffectComponents.cs), aged by EffectSystem like the other effects; rendered as a grey circle that grows to ~2x its spawn radius while fading (`WorldRenderer.DrawSmokePuffs`)
- `DamageEffectSystem` (Domain/Combat, Intent phase): probabilistic per-tick emission — roll `rng < rate x dt`, light tier 2 puffs/s, heavy tier 6 puffs/s + 3 sparks/s; spawns inherit half the ship's velocity plus jitter, so puffs trail behind moving ships without per-tick source tracking
- Per-tick spawn budget (1 puff + 1 spark) allocated in priority order: player > live manual target > fresh auto-mark (< `AutoTargetMark.FreshWindow` = 1 s old, shared with the bracket renderer) > others; ties broken by distance to camera then entity id. Particles live exactly 1 second, so steady-state live count is bounded by the budget — no separate culling pass
- Ships outside the viewport (plus a 100 px margin) and dead ships never emit; all randomness goes through `view.Rng` after candidate sorting, so determinism is preserved
- K diagnostic key (SPACEVORS_DIAGNOSTIC=1): halves hp of the locked target or nearest enemy ship to reach smoke tiers quickly
- 7 new tests in DamageEffectTest (tiers, culling, ordering, budget, expiry); PerformanceBenchmark: DamageEffectSystem <= 0.09 ms/frame at 1k ships — no optimization needed

## Hull damage shading — done

Damaged ship hulls show procedural scorch marks, impact craters, perforation holes and pulsing ember glow — no new bitmaps; undamaged hulls render bit-identically to before.

Implementation (plan: plans/DAMAGE_SHADING.md):
- Damage scalar [0,1] rides the previously unused vertex-color B channel (RG already held the rotation angle); `DamageShading.FromHealth` maps hp ratio: 0 until the 2/3 smoke threshold, continuous to 1 at destruction; wired for player and enemy ships only (asteroids/mines pass 0)
- Lighting shader damage block (skipped entirely at damage 0): hash Voronoi field over sprite-local UVs → impacts appear and craters widen with damage; soot darkens albedo, crater dents perturb the normal map and sink the depth map so the existing diffuse + self-shadow passes shade them; patterns rotate with the hull while lighting stays screen-space (turning damaged ships keep lit/shadow sides)
- Above ~0.55 damage: interior impacts punch alpha holes (space shows through) and craters/holes glow with a pulsing additive ember term (frame-global `uTime`); glow stays small so directional shading keeps reading
- Diagnostic keys (SPACEVORS_DIAGNOSTIC=1): K halves target hp (existing, now also walks hull shading), new J halves player hp
- Shader validated offline with glslang (no display in the build environment — in-game visual tuning pending); 4 new tests in DamageShadingTest (mapping, continuity, clamping)

## Impact damage — done

Fast collisions between ships and asteroids (and ships vs ships) deal closing-speed damage; gentle bumps deal none, and one hit can never destroy the initial hull.

Implementation (plan: plans/IMPACT_DAMAGE.md):
- `ImpactDamage.ForMasses` (Domain/Combat): absorbed energy ½·μ·v² at the contact normal split by inverse-mass share (lighter body absorbs more); zero below 160 px/s closing speed — side-by-side flight and swarm jostling never hurt; quadratic ramp capped at 3, so one hit costs at most 3 of the initial 8 hp Scout hull
- Tuning targets: normal 400 px/s ram = 1 (small impact); big collision at 800 px/s closing (explosion-flung, head-on) = 3; retuned down again (was 2/7) — tune constants at the top of `ImpactDamage.cs`
- Wired into `CollisionSystem.ResolveCollision` using the closing speed and masses the impulse solve already computes (hot-path cost: one compare); damage only on approach, so resting contact never chips hp — no cooldown state
- Player and enemy ships take damage through the existing per-frame health accumulator, so the existing death pass runs the full kill flow (loot, explosion, sparks) for impact kills; asteroids (no Health) and mines (own contact rule) take none
- Hits where either body absorbs ≥ 2 damage throw a spark burst (2 + damage/3, cap 6) at the contact point
- Death graphics render fully: the fatal hit writes `Health(0)` (without it the killing blow from high hp left the hull shading scalar stale and the wreck looked pristine), and game over waits 1.2 s after the player dies — simulation keeps running so holes, ember glow, smoke and sparks play out before the overlay; input stops at death
- 16 new tests in ImpactDamageTest (threshold, quadratic scaling, mass split, cap, 1-point ram and 3-point big-impact calibration, death flow, zero-health on fatal hit, side-by-side zero damage, mine exclusion); PerformanceBenchmark collision stress unchanged

## UI polish — done

Plain soft-depth UI language across HUD and menus: rounded translucent panels, soft shadows, one accent color, eased values — no new bitmaps or fonts.

Implementation (plan: plans/UI_POLISH.md):
- `Ui.cs` helper: rounded rects (2 rects + 4 circles), panels (shadow + translucent fill + 1px border), alpha/easing/fade helpers; `Palette` gains the design language (accent cyan, off-white/muted text, translucent panel colors, green→amber→red `HealthColor` gradient)
- HUD: hp bar is one rounded pip per hp point with eased fade-out on loss + muted "8/8" readout; new thin top-edge xp bar (accent fill, "LV n" readout — the HUD showed no xp before); game over fades a dark overlay + title + accent rule + "Press R to restart" hint (the hint never existed)
- Upgrade cards: rounded panels with shadow, hover lifts 4px + accent border, value in accent / label muted, rounded key badge, 150 ms fade-in; stats screen: rounded panels, accent headers with hairline rule, alternating row tint, accent LV readout; ship select: rounded cards, accent selection stripe, trackless rounded scrollbar
- Targeting: 1.5px arms, manual lock pulses at 1 Hz (alpha 229–255), hp bars rounded 3px and only when damaged
- Renderer-only; visual verification pending a display (TROUBLE_SHOOTING.md #25)

## Bullets as lines, lasers, particle accelerators — done

Bullets are swept capsules instead of points: a bullet that jumps over a target between ticks still hits it, wide shots score grazing hits, and instant lasers plus streak-fast particle guns joined the arsenal.

Implementation (plan: plans/BULLETS_AS_LINES.md):
- Swept collision: every ammo entity is treated as the segment `[Position − Velocity·dt, Position]` (exact — ammo velocity is constant) inflated by `Ammo.Radius` (now the capsule half-thickness); hit test is segment-vs-circle and the **earliest hit along travel** wins, so no tunneling and grazing width fall out of the same code. Enemy shots can no longer skip over the player either
- `SpatialGrid.GetQueryItemsInBox` serves the swept segment's AABB; `Vector2.ClosestPointOnSegment` (with projection parameter t) is the shared hit math
- Lasers: `LaserBeam` component (static segment Start→Start+Dir·Length, `DamagePending` flag) — `TurretFiringSystem` fires it instantly at `turret.Range` when `WeaponStats.Hitscan`, `CollisionSystem` damages the **first thing hit only, exactly once**, `EffectSystem` fades the 0.12 s flash, renderer draws the fading line
- Rendering: ammo draws as the swept line (`DrawLineEx`, thickness = 2·radius) — fast bullets as streaks; when the swept length is no longer than the bullet is wide the line would read as a bar across it, so those draw as a round dot instead
- New weapons: **Laser Lance** (hitscan sniper), **Prism Lance** (3 parallel beams via `BeamSpacing`), **Ripple Cutter** (short 32-px-wide panic beam), **Cyclotron** (4200 px/s particle stream), **Graviton Burst** (5-pellet accelerator volley), **Flak Curtain** (slow wide flak disc — grazing does the aiming); RailGun sped up 900→2500 px/s
- Ship loadouts: Scout = Cyclotron, Fighter = Laser Lance + Cyclotron, Heavy = machinegun + side shotguns + Flak Curtain, Shadow = Graviton Burst; old weapons remain in the milestone upgrade pool
- Milestone (every 5th) level-ups now offer 5 random choices instead of 3
- 24 new tests (box query, closest-point math, tunneling, grazing, earliest-hit order, laser once-only/first-hit/blocking, hitscan firing, milestone choice count); ammo-heavy benchmark stays inside the 8.3 ms budget (CollisionSystem ≈ 7.3 ms at ~15k bullets)

## Shadow weapon test ships — done

Shadow carries a real weapon in normal play; the load-test spray moved to a diagnostics-only Shadow copy, joining one copy per new weapon.

- Shadow = Graviton Burst (Balanced engines, 2 slots); `WeaponLoadout.MountedAsUpgrade(weapon)` builds a one-turret loadout from the weapon's own level-up mount
- `ShipType.DiagnosticShips`: Shadow hull, engines and graphics carrying Laser Lance, Prism Lance, Ripple Cutter, Cyclotron, Graviton Burst, Flak Curtain and LoadTestWeapon — one weapon each — appended to the select list only under `SPACEVORS_DIAGNOSTIC=1`
- 4 new tests (Shadow's weapon, clone roster and hull identity, distinct names, every clone fires in a started world)

## Sim vs render cost logging — done

The diagnostic log now shows which half of the frame costs what, so performance work can be aimed at the real limiter (plans/PARALLEL_SIM.md §11).

- `[SIM] tick: Xms` — one line per fixed-timestep tick: all four phases plus their command flushes
- `[APPLY] <phase>: Xms` — the previously invisible `CommandBuffer.Apply` cost, per phase (Movement/Action/Resolution/Intent)
- `[RENDER] frame: Xms` — the frame's draw call (`Renderer.Render` / `Renderer.RenderPaused`), covering the in-game, stats-screen and upgrade-menu paths
- Stopwatches are reused instance fields: nothing allocated per tick/frame, and `DiagnosticLogger` no-ops when `SPACEVORS_DIAGNOSTIC` is off
- `[RENDER]` includes raylib's `SetTargetFPS(120)` pacing wait (raylib waits inside `EndDraw`), so it floors at ~8 ms while the game is at the cap — read it against `[FRAME] dt=`
- Verified headless (Xvfb + llvmpipe, 116 fps, ~85 entities): `[SIM] 0.15–0.33ms` vs `[RENDER] 7.95–8.17ms`; full suite green (248 passed)

## Perf scenario harness — done

Implements plans/PERF_SCENARIOS.md: scripted scenarios that start from a seeded state, run ~1 s in a real window and record a machine-readable trace.

- `Domain/WorldFactory.cs` — the entity world (player, camera, turrets, 35 asteroids, mines, enemy ships) moved out of `GameInitializer`, so a world can be built without Raylib. `GameInitializer` keeps the starfield and clutter, which still draw from `em.Rng` in the same order.
- `Domain/Scenarios/` — `ScenarioSpec` (ship, seed, bullet count, aim + lateral aim offset, stream length, health ratio, seeded particles, measured ticks, frame budget) plus five scenarios: `baseline-idle`, `ammo-spray`, `ammo-spray-miss`, `impact-storm`, `hull-damage`. `ScenarioWorld` seeds a `LoadTestWeapon` volley already in flight (uniform ages, resampled clear of every asteroid), pre-ages impact particles **at the ray-solved entry points of the seeded bullets** — a cloud spread over the rock reads as impacts that never happen — silences player turrets and sets hull damage.
- What measuring the scenarios taught (visible in `ScenarioBench`, not in the playthrough log): one asteroid impact spawns ~7 particles, and a seeded volley loses ~24 bullets/tick to lifetime expiry alone. So the first `ammo-spray` (stream driven dead centre) ran at ~30 impacts/tick and its 13-19k sparks were a storm, not a mix. `ammo-spray` now grazes the rim (`AimOffsetR: 1.05`, ~7.8 impacts/tick, ~6.8k sparks steady); `impact-storm` (0.3, ~12.4 impacts/tick, ~18k entities) is the explicit worst case.
- `[RENDER-PASS] <pass>: Xms` — one line per draw pass (`clear`, `lighting.begin`, `lights.collect`, `background`, `world.*` for all 15 world passes, `hud.*`, `present`), emitted by `RenderTimings` when diagnostics are on. `summary.json` ranks them by mean ms with their share of the render total. First sandbox run: `world.ammo` 66% of render, `world.player` 12%, `present` 10% (llvmpipe — GPU split still to be measured).
- `[PERF]` rows now carry `gc0/gc1/gc2` collection counts and `allocKb` for the frame; `summary.json` reports collections per run, allocated KB/frame, and the median frame time with and without a collection — the direct test of whether GC explains the sim p99 spikes.
- `SimulationRunner.RunTick` — the four phases with a command flush after each now live in one method, used by `GameSession` and `ScenarioBench` alike.
- Scenario mode in the game: `SPACEVORS_SCENARIO`, `_CAP` (0 = uncapped), `_TICKS`, `_HOLD`; pinned 1920×1024 viewport and centred mouse, one tick per frame, upgrades applied without pausing, auto-exit after the measured ticks.
- `[PERF]` row per measured frame: `dt sim apply render other` ms + counts (ents/ammo/sparks/explosions/beams/mines/ships/asteroids/pickups), delimited by `[PERF-BEGIN]`/`[PERF-END]`.
- `.tools/scenario.sh <name> [--sim] [--measure N] [--iters N] [--hold S] [--cap FPS] [--out DIR] [--compare] [--update-baseline]` and `.tools/scenario.py` produce `frames.jsonl` + `summary.json`: frameMs mean/median/p90/p99/max, fps mean/median/low1/low10, sim/render/other fractions, perEntity ns, entity counts, budget pass. Baselines live in `.tools/baselines/`. It uses the caller's display; in the sandbox prefix `. .tools/env.sh &&` (see TROUBLE_SHOOTING #31).
- `src/ScenarioBench` — sim-only host, no Raylib. Verified in the sandbox: `ammo-spray-miss` with 7,000 bullets, 1.23 ms median per tick, 236 ns/entity.
- No warm-up phase by decision: cold JIT is handled by discarding the first whole run (`--iters N` runs N+1) and the first frame of each pooled run.

## Render cost: shape batching — done

- The baselines settled where the frame goes: ~**1.1 µs per drawn shape** (sparks, bullets, explosions — measured six times across three scenarios), i.e. the cost is per GL draw call, not per entity or pixel. `world.sparks` is 58% of `ammo-spray` render and 78% of `impact-storm`; sim is 6-11%.
- `Game/ShapeSprites.cs` draws those shapes as tinted quads of one generated disc via `DrawTexturePro`, staying in raylib's batched 2D path. An `Rlgl` vertex block (first attempt) submitted geometry that never appeared on screen, so the check for any render change is now the automatic start/mid/end frames plus a pixel diff against a known-good frame: the batched frame differs from the immediate one in 0.8% of pixels (streak caps and edge filtering).
- Sandbox pass effect: `world.sparks` 18.6 → 1.53 ms, `world.ammo` 13.4 → 0.60 ms, `world.explosions` 5.0 → 0.24 ms (llvmpipe). Re-take the real-GPU baselines to get the meaningful numbers.
- Scenario shots: `SPACEVORS_SCENARIO_SHOTS=start,mid,end` writes PNGs of those frames into the run dir, so a render change can be verified with one command instead of pressing F12 into an unfocused window.

## Perf gates — done

- Every scenario now carries the real target, 1000/120 ms. Budgets used to be per-scenario and loose (10-32 ms), which is how a render regression that ate 89% of the frame sat green. `impact-storm` is marked `ExpectedOverBudget` instead: 23,876 entities are knowingly over 120 fps, and clearing that flag is how the shortfall gets closed.
- An unmarked breach exits non-zero, prints `budget: regression median X against Y`, and records the verdict in `index.tsv`. `--check-baseline` gates a run against its committed baseline (median x1.15, p99 x1.25, per-shape cost x2.0) and is host-aware: a sim-host run against a window baseline reports the deltas as information instead of comparing incomparable milliseconds.
- `perShapeNs` — mean pass ms divided by the entities that pass drew — is the render metric that transfers between machines: ~1.1 µs per shape before batching, ~0.1 µs after. A jump back up is the signature of per-shape draw calls returning, which is the regression most likely to be silent.
- `Tests/ScenarioPerformanceTest.cs` drives the scenario specs headless (median of measured ticks, warm-up discarded) against characterized ceilings, replacing `Tests/PerformanceBenchmark.cs` — a second, weaker source of truth with three `mustMeetBudget: false` phases and a stale CollisionSystem claim. The Linux CI job now runs `dotnet test`.
- The habit is written into AGENTS.md: after a sim or render change, run `.tools/scenario.sh <name> --sim --iters 3` and report the delta as a verdict - keep, optimize, or revert.
- The sim test asserts two things per scenario, because they answer differently. **Median tick ms** is machine-bound, so it has a ceiling set per host: desktop (0.5 / 1.0 / 5.0 / 2.5 / 5.0 ms) and CI (1.5 / 3.0 / 15.0 / 7.5 / 15.0), selected by `SPACEVORS_PERF_HOST=ci` which the workflow sets. **Allocated KB per tick** is nearly host-independent, so one budget serves every machine: 15 / 16 / 400 / 16 / 1000 KB, measured at 9.5 / 10.9 / 289.9 / 10.4 / 746.2 - and the real-GPU harness independently measured 296 and 753 KB per frame for the same two scenarios, which is what makes the metric worth gating on. ammo-spray at 290 KB against ammo-spray-miss at 10 KB localizes the allocation to impacts spawning effects, not bullets existing.
- Tests run serially (`Tests/AssemblySettings.cs`): xunit's parallel classes put every other class's work inside the timed ticks, which on a small CI runner was enough to fail a scenario by 5%.
- `[SYSTEM]` lines are now aggregated (`systemMs`, `spikeFrames`, `phaseMs`) instead of sitting unread in 3,600 lines per iteration. Attribution named `CollisionSystem` at 76.4% of `ammo-spray` sim and `Collision: detection` as the frame-9 spike — **but that cost was mostly the JIT, not the broadphase** (see the next bullet). Frame 2's cost is real and deterministic: `Effects: damage spark` processing the seeded particle cloud for the first time.
- The frame tail in a 120-tick scenario window is tiered-JIT promotion, not game code. `DOTNET_TieredCompilation=0` on the same machine: tick median 1.83 → 1.02 ms, p99 12.56 → 2.50 ms, max 22.99 → 3.03 ms, `Collision: detection` 1.10 → 0.36 ms mean. Consequences: the committed baselines are JIT-warm-up-inclusive; the collision broadphase is far cheaper than the attribution suggested; and `plans/PARALLEL_SIM.md` is aimed at a 0.36 ms cost until something more expensive replaces it. What remains after tiering is off is the Effects tail (~2.5–3 ms at frames 33/76) — spark and explosion generation, the deferred design change.
- Implemented: `TieredCompilation=false` in `Game.csproj` and `ScenarioBench.csproj` (the harness must run the same JIT tiers as the product, or it measures a build the game never ships). `Tests.csproj` deliberately omits it — its ms ceilings were characterized tiered, and adding the switch there would silently retune those gates. Result in the harness, no env var needed: `ammo-spray` sim median 1.04 ms, p99 2.73 ms, **0 of 357 frames over the 8.33 ms budget**, `Collision: detection` down to 0.374 ms mean. Tradeoff paid knowingly: eager JIT lands on the first tick (frame-1 dt 39 → 80 ms) while a whole 120-tick run finishes faster (wall 345 → 261 ms). The committed `host=window` baselines predate this and must be retaken on the real GPU host.
- ReadyToRun completes that pair, and it is the part that changed what the player feels. Measured on one host, `ammo-spray`, 3 fresh processes × 120 ticks: first tick **81 → 21 ms** with R2R, at the cost of a slower steady tick (median 0.78 → 1.09 ms). R2R *with* tiering starts cheaper still (13 ms) but ticks slower (1.32 ms), so the shipped combination is R2R + tiering off. No configuration measured had a mid-run hitch above 3.6 ms.
- `scenario.sh` now **publishes** (R2R, `linux-x64`, framework-dependent) and runs that artifact instead of the `bin/Release` build output, for both modes, and prints `artifact: … (ReadyToRun)`. A property that only exists in the game's csproj is invisible to the harness, so `PublishReadyToRun` is set in `ScenarioBench.csproj` too; the publish lands in `bin/Release/net10.0/linux-x64/publish/`. This also means `.tools/game.sh` — which runs `bin/Debug` — is now three steps away from what ships.
- The in-program screenshots were the "frozen for 20-40 frames" the user felt, not the game: each PNG encode stalls the window ~0.1-0.2 s *after* the frame row is written, so `dt` cannot see it. Now only the final run takes shots (3 encodes instead of 12), and the run count is printed up front (`N measured + 1 discarded cold run`). `scenario.py` has one `measured_blocks` exclusion shared by the frame, pass and system tables: frame 1, a shot frame, and its successor are out of every statistic. Pass means corrected accordingly (asteroids −28%, sparks −1%), so a retaken baseline is slightly stricter on `perShapeNs`.
- The remaining tail after all of this is particle generation, not broadphase: `Effects: explosion` 2.09 ms at tick 33 and `Effects: damage spark` 2.06 ms at tick 76, against 0.37 ms of detection. That is the deferred spark-generation design change (≈7 particles per impact), and it is now the biggest game-side lever.
- `impact-storm` met the 120 fps budget after shape batching (4.76 ms), so its `ExpectedOverBudget` mark was removed. `hull-damage`'s per-shape canary was a false alarm (fixed pass overhead divided by a handful of sparks), so `perShapeNs` now needs ≥100 drawn shapes.

## Difficulty scaling

- Add difficulty rating to enemy ships. New int stat starting at 1. Initially low hp enemies have lowest rating, while high hp have highest, say 10 for HeavyCannon enemy. Mines have ratings too.
- At start of game only spawn easiest enemies and mines
- As game progresses mix in harder enemies
- As difficulty increases allow clusters of enemies to spawn

## Damage affects ship performance

- If ship has less than 2/3 hp left, it takes randomly selected negative stat modifier
- Damage modifiers behave like upgrades, but reduce ship performance instead of increasing it
- Between 2/3 and 1/3 hp left, ship can have small negative modifiers like 20% reduced performance
- Less than 1/3 hp left, ship can have large negative modifieres like 60% reduced performance
- Show red text bottom center of scree listing all damage effects currently on player ship
- Ship can have one or two small or large damage effects at any one time
- If ship restores hp above threshold, damage effect associated with that tier gets fixed
- Show damage effects on ship status screen with red text

## Turret layouts — done

Ships have a turret layout: Multiple turret slots with following properties:
- size (large, small)
- arc of fire (degrees) how far can the turret turn
- direction (degrees) which way the weapon points if centered in the middle of it's firing arc
- Location on hull (Vector from ship center)
- name (eg. main_gun, small_left,, small_right, small_back)

Weapons get:
- Size (large, small)
- Turning speed (degrees / s)

Ships may get new weapons to put into their turret slots on the 5 level new weapon cards.
- User chooses which slot the weapon goes into.
- Large weapon may not be mounted on small slot

During upgrade when user is choosing where to new weapon: 
- render large ship graphic with empty turrets shown as greyish blue circles
- render the arc of fire as green section of circle originating from center of turret showing which directions the turret may fire in

Graphics: 
- Render a greyish blue circle where the turret location is, different size for large and small
- Render triangle greyish red triangle in turret circle when turret is mounted
- Triangle turns to point where the turret is pointing

Ships get the same starting weapons as currently, but they are now in their turret slots
- Add one more turret slot to each ship (small ships end up with two empty slots and large ship with three)
- If ship has free turret slots, most weapons offered must be of type fitting the free slot. 
- Minority of offered new weapons may be of size whose slots are all full; instead of forcing a replacement, such a weapon can wait in cargo and be swapped in later.
- Implemented: one entity per hull mount (`TurretSlot`), created by `WorldFactory` from `TurretLayouts` (Scout 3, Fighter 4, Shadow 3, Heavy 7 — two free on the small ships, three on Heavy). A filled mount also carries `Turret`/`TurretAim`/`Position`/`Rotation`; the arc, offset and size belong to the slot, the size/range/turn speed to the weapon (`WeaponType.Size`, `WeaponStats.TurnSpeed`). Slew is hull-relative, so absolute rate = hull rate + turn rating and a hull turning faster than a gun drags its aim off target — intended. `AimAcquireTolerance` 3° stops the correction, `FireAimTolerance` 8° restarts it and is also the firing gate, so a sweeping mount fires as it crosses a target and a spinning hull silences guns rated below its turn rate. With no valid target a mount walks back to the middle of its arc and parks. `TwinChainGun` is gone (it needed a slot of its own); every other weapon fits a slot. Mount screen: `MountMenuRenderer` (hull unrotated, empty mounts as greyish-blue circles sized by slot, arcs as green sectors, mounted guns as greyish-red circles with a point sticking out, number badges + right-click back); in-game markers use the same shapes. Perf: `ammo-spray` sim median 1.25 ms vs 1.26 ms on `HEAD`, `impact-storm` 2.06 vs 2.09 ms — no measurable cost (scenarios silence player turrets, so the slew path is not in these numbers).


- Cargo follow-up: replacing a mounted weapon destroyed it, so weapons now have a home instead. `WeaponCargo` on the player holds inactive turrets — with their upgraded stats, because it stores the `Turret` component, not a weapon name — and the mount screen is one screen for both entries: placing a card's weapon, or rearranging from the stats screen. With a weapon in hand, clicking or pressing a mount fits it there and any gun in the way is displaced into cargo and becomes what is held next; with nothing in hand, clicking a mounted gun takes it down into cargo. A weapon from a card can be stored with C and re-offered never happens: cargo weapons count as owned, and being inactive they are not offered stat upgrades either (those come from the turrets on the hull). The old `ReplaceSlot` step and the "at most one weapon that needs a replacement" hand rule are gone. Cargo never appears in perf scenarios (their player has no turrets, so no level-up). Sim cost unchanged: `ammo-spray` 1.26/1.27 ms against 1.30/1.32 on the previous commit, `impact-storm` 2.09/2.11 against 2.17/2.19 — measured in one session, this change is free.

- Fitting-screen and card follow-up: every hull mount carries its number all the time (a mount's key no longer depends on what else is on screen), the mounts that accept the weapon in hand are drawn with a green ring (`Palette.TurretFits`, doubled when hovered), and a new-weapon card names its mount size in the accent line and lists the gun's base range / damage / attack speed / projectile speed below it — honest numbers because a new weapon has no upgrades yet. The stat block is skipped rather than squeezed on cards narrower than 150 px.
- Marker shape follow-up: a bare triangle read as ambiguous about which way the gun points, so a mounted mount is drawn as the same circle an empty mount has, plus a triangle whose base corners sit inside the circle (0.93 radius, hidden by the opaque disc) and whose tip reaches 1.9 radius. One helper, `RenderHelpers.DrawTurretMarker`, draws both shapes for the world and the mount screen. Measured on the sandbox's software-GL host (`ammo-spray`, window mode, iters 1): median 15.62 ms against 15.55 ms before — the extra draw call per mount is noise; the `world.turrets` pass stays at 0.014 ms mean over 240 frames. Bug found while drawing them: the marker heading (`Rotation`) was only written on ticks where a mount slewed, so a mount holding still kept an old heading while the ship turned and then snapped to the right one the instant it started firing — which read as turrets teleporting onto a target. A mount's `Rotation` is now derived every frame from the hull plus `TurretAim` (`TurretMount.SyncWorldTransforms`), the same way its position already was; the simulation writes only `TurretAim`.

## Weapon balance pass — done

Plan: plans/WEAPON_BALANCE.md. Every gun now owns one cell of (range band, delivery class, fire rate, pellets per shot, mount size), and the DPS ladder prices delivery: a hitscan beam can never miss on the way, so it buys the least damage.

Range ladder, base auto-target range in px: **aura 600 → close 900 → mid 1350 → long 1600–1700**, with RailGun the one sniper whose click reach (3× base, capped by its own ammo at 5000 px) really flies 4500 px. The 600 px floor is not taste: enemy guns open fire at `700 + your hull radius`, so a shorter gun cannot answer what is shooting it — the old 220 and 260 px guns sat inside that ring.

Rules the data obeys, each asserted in `WeaponBalanceTest`: the range on a card is crossable by the slowest round that gun fires (`AmmoSpeed × 0.85 × ShotLifetime ≥ Range`); ≤ 12 rounds a second from one mount; a beam stays under 0.8× the slowest projectile gun of its band; a large gun out-DPSes the best small gun of its tier and class by ≥ 25%; tier 2 is ≤ 1.4× tier 1 of its size and must beat it in DPS **or** in reach; at most two guns slew slower than a Balanced hull turns; one gun per identity cell; starter firepower follows hull size (Scout 17 < Fighter 22.5 ≈ Shadow 24 < Heavy 45 raw DPS — a 168 px hull dodges worse than a 92 px one and gets engaged from 784 px instead of 746, so the big ship is allowed to start ahead).

Raw DPS (fire rate × pellets × damage). Tier 1: Cyclotron 8 (1600 px, 4200 px/s), RippleCutter 8, InterceptorTurret 9, Shotgun 10.5, MachineGun 12, FlakCurtain 12. Tier 2 (`UnlockLevel: 5`): PrismLance 8.4, LaserLance 9.6 at 1650 px instant, RailGun 14 (1500 / 4500 px), GravitonBurst 16.5, AcidBubbleSpray 12 (900 px cone).

- `Scatter` is now the half-angle of the cone a shot pattern covers: several pellets spread evenly across it, a lone pellet rolls randomly inside it (`view.Rng`, seeded). Before that the offset for `PelletCount == 1` was always 0, so Flak Curtain's 0.5 and Acid Bubble's 0.4 did nothing at all.
- New-weapon cards come from `WeaponType.NewWeaponPoolFor(level)`: tier-1 guns only until the first milestone hand, then the five tier-2 guns as well. `SPACEVORS_DIAG_UPGRADES` ignores tiers — a script is a bench — and so does `ShipType.DiagnosticShips`, which now carries one Shadow copy per gun.
- Dead and misleading cards fixed: `ProjectileSpeed` is never offered to a hitscan gun (it multiplied `AmmoSpeed 0` by 1.3), and a `FireRate` card on a multi-pellet gun says "+1 shot", which is what `ApplyUpgrade` does, instead of the "+15%" it promised.
- `PointDefenceTurret` became **InterceptorTurret**: a twin 2-pellet snap at 900 px on the fastest mount in the game (360°/s). "Point defence" promised anti-projectile fire the sim does not have, and the gun was dominated by two others.
- Starter loadouts: Scout = Cyclotron + InterceptorTurret, Fighter = MachineGun + Shotgun, Shadow = a Ripple Cutter in each of its three mounts, Heavy unchanged (machine gun, two side shotguns, flak disc). Shadow is the only hull that starts fully booked — cargo makes that a free swap, not a dead end — guarded by `AtMostOneHullStartsFullyBooked`.
- Naming trap found while writing the rules: `AutoTarget: false` (RailGun) turns off **lead prediction**, it does not make a gun manual-only. AcidBubbleSpray's flag is gone, so its bubbles lead their target again.
- Perf: the scenarios pin `LoadTestWeapon` and silence player turrets, so a balance pass cannot move them — measured anyway: `ammo-spray` sim median 1.33 ms against 1.31/1.33/1.36 on the previous commit, `impact-storm` 2.14 ms against 2.14–2.19. Free. What the scenarios do not see is live round count in real play: the Heavy's starter set goes from ~52 to ~93 live rounds (rate × lifetime) — still two orders under the 10k-object goal, and the number to watch when the next gun is added.

## Rocks are cover, and the small ones break — done

- **Guns ignore rocks unless one is clicked.** Auto-targeting now sees only what shoots back — enemy ships and mines. A rock becomes a target through a click, and then it is treated like any other primary target: 3× base range capped by what the ammo can physically reach, lead-predicted, reticled, and it keeps priority over closer enemies. `TurretFiringSystem` lost its asteroid last-resort target scan (and with it the generic `CheckTargets` helper); `PrimaryTargetPicker` gained an asteroid pass, where the click zone is the rock's own radius — no forgiveness multiplier, because the hull is already ship-sized and a wider zone would steal clicks meant for the space beside it. A destroyed rock is noticed by the same rule that drops a killed ship: its components are gone, so `PrimaryTarget` is cleared.
- **Small rocks are destructible, big ones are scenery.** `Asteroid.SmallHealth = 50`, given by `AsteroidFactory` to the 35–50 px rocks only. Having `Health` *is* the difference: a hit on a rock without it is just an obstacle hit, and no bookkeeping follows. Gunfire — bullets and beams — chips a small rock.
- **A breaking rock leaves smoke, not a fireball.** While it survives, each hit flashes the usual impact effect; the hit that ends it flashes nothing (`DamageAsteroid` reports whether the rock is still there, and the flash is skipped), and the resolution pass replaces it with `AsteroidFactory.SpawnBreakupSmoke`: 16 grey `SmokePuff`s scattered over the rock's own disc, inheriting 0.4 of its drift and jittering apart, 2.2 s lifetime, drawn by the existing smoke pass which grows them to about twice their radius as they fade. No `Explosion`, no staged `ShipDeathExplosion`, no loot, no XP.
- **Rocks are still broken by gunfire only.** Impact damage keeps skipping them, so the field cannot grind itself away as ships and rocks collide. Rocks are still never auto-bracketed: `AutoTargetMark` stays ships-and-mines, and a clicked rock shows the manual reticle instead.
- Tests: 6 in `AsteroidHealthTest` (damage tracked, break-up cloud, no fireball on the killing hit, puffs dissipate, big rock immune, ramming harmless, factory arms only small rocks) plus 4 targeting tests (a rock is ignored until clicked, a clicked rock is engaged beyond base range, a smashed rock loses priority, the picker clicks a rock and prefers a ship clicked over the rock behind it). `TurretFiringTest`, `TurretSlewTest`, `TurretTargetMarkTest`, `LaserTest`, `ScatterConeTest` and `ShipRosterTest` now click the rock they were firing at.
- Measured: `ammo-spray` sim median **1.29 ms** / p99 2.08 against 1.33 / 2.37, `impact-storm` **2.09 ms** / 4.35 against 2.14 / 4.96 — this costs ~0%, keep. The added work is one component lookup per rock hit; the per-tick health bookkeeping only runs for small rocks, and the killing hit now spawns fewer effects than the hits before it.
- Open knob: clearing a rock pays nothing — no XP, no loot — so it is pure DPS spent on cover. Whether rocks should drop something, or whether the reward is simply the shot lane you opened, is a playtest call.

## Difficulty: deadlier individuals, and a threat readout — done

- **A one-second threat row, logged in every run.** `ThreatMeter.Measure` is a read-only aggregate over the world: ships alive, ships standing inside their own gun range plus the player's hull radius (the ones that can hit the hull right now), live enemy rounds, their summed fire rate × pellets × damage (`in`), the same sum for the guns on the player's own hull (`out`), hp, level, mines, small rocks. `GameSession` takes it once a second of game time. The `[THREAT]` line is the one log line not behind the diagnostics gate — it is the tuning record for the difficulty knobs — and the same row is drawn along the bottom edge when `SPACEVORS_DIAGNOSTIC=1` (`hud.threat` render pass). First read of an unattended Scout run: `t=18s tier=0 ships=8 ring=2 rounds=2 in=1.4/s out=17.0/s hp=1/8 lvl=1 mines=2 rocks=13`. The `out` column lands exactly on the 17 DPS the weapon pass priced for the Scout, and the hp walk says the early threat is drifting mines, not guns (`mines` 9→2 while the hull sits in a field).
- **The ladder now drifts reach, closing speed and tracking, not only hp / damage / fire rate.** Applied at spawn, tier = minute, capped at 10. Reach: Interceptor 700→550 (it trades reach for the close), Default 700→800, HeavyCannon 700→950 — the slow 160 px/s gun becomes the long firing line. Closing speed: Interceptor 90→120, Default 65→85, HeavyCannon unchanged. Ammo speed +1%/tier only (200→220, 160→176). Tracking +0.03 rad/s per tier (1.0→1.30): they hold a guns-on-you arc through a strafe without becoming faster or harder-hitting. The hull and its turret are built with the same grown range, because the chase AI brakes the hull at `FiringRange` and the gun fires at it.
- **Ceilings, asserted by `EnemyDifficultyTest` in the `WeaponBalanceTest` style.** The firing ring — widest grown `FiringRange` plus the widest hull radius (84) — stays inside the player's 1350 px mid band, so mid and long guns still trade favourably at minute 10. Enemy ammo stays under 250 px/s and under the player's slowest round (400), so the fight stays readable and the growth in `in` comes from range and tracking rather than faster bullets. A tier-10 HeavyCannon still dies inside ~7 s of a starter Scout's sustained fire. Per-shot damage is deliberately *not* one of these knobs: it stays on `DamageAdd`, 6 against a base-8 Scout.
- Direction chosen: **deadlier individuals rather than a busier world** — no new entities, so no sim cost, but harsher on the low-hp hulls, which is exactly what the ring ceiling and the flat damage pay for.
- Tests: 4 in `ThreatMeterTest` (the ring gate, a wreck not counting, rounds/mounts/mines/rocks, no player) and 5 in `EnemyDifficultyTest` (the three ceilings, drift by role not by fleet, hull and gun agreeing on range). 318 passing.
- Measured: `ammo-spray` sim median **1.28 ms** / p99 2.58 against 1.29 / 2.08, `impact-storm` **2.07** / 4.08 against 2.09 / 4.35 — this costs ~0%, keep. The scenarios run at tier 0, where the new drifts are zero, so they prove no regression rather than the tier-10 cost; the readout itself is 1 Hz in the window path only.
- Still open (plans/DIFFICULTY_SCALING.md R2): composition unlocks and weight drift (P6), pellet volleys and concurrency, mines getting scarcer rather than weaker, the reward side (heal-orb chance), elites, spawn surges, and the fairness purchase of a weapons-hot delay on spawn.

## Auto-aim keeps its lead, and aims at the speed the round actually has — done

- **The symptom, measured**: a Fighter moving at 150 px/s or more hit *nothing*. In a 5 s fight against four closing ships with the starter loadout, 0 of 71 rounds crossed a hull; parked, 16 did.
- **Answer to "does auto-aim account for the hull's movement?"** Yes, and correctly. `EvaluateMovingTarget` solves the intercept in the hull's frame (`relVel = target − hull`, `aimDir = (toPredicted − hull·t) / (ammoSpeed·t)`) and `FireAtTarget` adds the hull's velocity to the round, so shot and solution agree. Measured: a Cyclotron's shots pass 2-6 px from a stationary or closing target at every hull speed from 0 to 300 px/s, and so does any other gun's lead that its arc allows.
- **The bug**: `FindTargetWithPrediction` carried a second ship scan over `<EnemyShip, Position>`. A multi-component query means *at least* these components, not *exactly* these, so that scan re-saw every ship the lead pass had already looked at — and re-selected it with the lead discarded. It fired whenever the solution was rejected for any reason (barrel direction outside the mount's arc, the round unable to catch a fleeing target, intercept outside range), putting the shot exactly `hull velocity × flight time` off: 313 px at 200 px/s across a 700 px gap, 416 px at 300. That is the "slow shots like shotgun consistently miss while moving" report. `FindTargetWithoutPrediction` had the same duplicate scan — pure re-work for the guns that never lead.
- **The fix — two of them.** (1) Both duplicate scans are gone (−38 lines): a mount whose lead falls outside its arc now holds fire instead of spending the volley. (2) `TargetSelection` carries a `Led` flag and `FireAtTarget` re-solves the intercept **per round, at the speed that round actually leaves at and from the muzzle it leaves from**. The ±15% speed roll used to be drawn *after* the solution, so the round flew at a speed the aim was never solved for — up to ~4° of tilt on a slow led round (≈50 px at 700 px) — and the 20 px muzzle offset added `hullSpeed × 20 / ammoSpeed` on top (10 px for a shotgun at 200 px/s). One shared `InterceptTime` helper now feeds both the target search and the shot, replacing two copies of the same quadratic.
- **Measured, same fight (hits per 5 s)**: parked 16 → 15 → **18**; 150 px/s 0 → 16 → **18**; 250 px/s 0 → 17 → **19**; 400 px/s 0 → 17 → **20**. Rounds thrown drop from 70 to ~20 because the shotgun's mounts go quiet — see the open knob. Enemy turrets obey the same rule, so incoming fire is now as accurate as it was aimed: difficulty-visible, worth a playtest against the `[THREAT]` readout. Sim cost unchanged: `ammo-spray` sim median **1.30 ms** against 1.28/1.29, `impact-storm` **2.12** / 4.52 against 2.07–2.14 / 4.08–4.35 — the per-round solve runs only for rounds actually fired, and the scenarios seed their volleys instead.
- **Tests** (`AimLeadTest`, 6): a scatter-free 400 px/s gun in the Fighter's 45° left mount, a ship 700 px dead ahead, hull strafing at 0/100/200/300 px/s — no shot may pass more than 150 px from it (the discarded-lead shot was 328 px off at 200 and 431 px at 300), and where the lead fits inside the arc (100 px/s, 14.5°) the shot has to pass within **5 px** of a 20 px hull. Plus the arc's real meaning: a ship crossing 45° off the nose — outside the ±22.5° arc, and it does not reach the arc's edge for 0.6 s — **is** engaged, because the arc gates the firing solution and not the target's bearing. `TestEnemyAmmoInheritsShipVelocity` no longer hard-codes a lead direction (that was the nominal-speed solution, which the rolled speed invalidates); it now asserts the round's world line runs onto the player within 1 px whatever speed it rolled.
- **Open knob — the lead has to fit inside the arc, because the barrel is what carries the round.** The largest cross-line drift a mount can lead is `ammoSpeed × sin(halfArc)`: for a 400 px/s round (`Shotgun`, `AcidBubbleSpray`) in a 45° arc that is 153 px/s, against a hull with 400 px/s² of thrust and no drag. `MachineGun` 230, `InterceptorTurret` 459, `Cyclotron` 1608. The Fighter's side mounts are the only 45° small mounts in the game — Scout, Shadow and Heavy give small mounts 120°, which lifts a shotgun to 346 px/s. Options for a playtest call: widen the Fighter's side arcs to match the other hulls, lift the slow guns' ammo speed (the DPS ladder prices delivery, so faster rounds buy less damage), or accept that a mount goes quiet when it cannot point at the solution.


## Missiles — done

Guided rounds on both sides (plans/MISSILES.md; the rules are R15-R22 in plans/WEAPON_BALANCE.md and
plans/DIFFICULTY_SCALING.md R2.12).

- A missile is a round with a lock, not a hull: `Ammo` plus `Missile` plus `Health`, spawned like a
  bullet and steered by `MissileGuidanceSystem` (first in the Intent phase). Its turn radius is
  `Speed / TurnRate`, which is what keeps a lock dodgeable.
- Three player guns, one per size and tier rung: SeekerPod (small, quick, unlock 5), Swarmlet (small,
  three-round volley, unlock 5), HavocTorpedo (large, slow, unlock 10). Orange is the guided colour, and a
  rack on the hull is drawn as a square that does not turn.
- A guided round carries 1-3 hp and only the **other side's** fire can hit it: point defence is free,
  because every gun and beam already on the hull is the answer. `MissileHealth` is the one card that buys
  tougher rounds, and it is withdrawn at the ceiling rather than offered as a lie.
- Enemy missiles are an **armament ladder over the three existing hulls**, not a fourth species: the
  Interceptor starts carrying a few at 3 minutes and an even mix at 5, the HeavyCannon adds guns+missiles
  at 8 and mostly carries them from 10. Guided rounds allowed in the air: 0 → 3 → 4 → 5 → 6 → 7 at
  composition tiers 3 / 8 / 10 / 14 / 20, printed in the `[THREAT]` row as `missiles=n/cap`.
- A rack launches from 1.4× the hull's own gun ring (capped at 1450 px) while the hull still brakes at its
  old range, and its rounds stay at 165-250 px/s: far, slow and shootable is the whole design.
- **A guided round builds to its speed.** It leaves at 0.7 × its card speed (`MissileLaunchFraction`) and
  accelerates at 200 px/s² to that speed, so the card speed is a ceiling: SeekerPod 294 → 420, Swarmlet
  343 → 490, HavocTorpedo 196 → 280 px/s, and an enemy rack's round 116 → 165 at tier 0 up to 175 → 250 at
  tier 10 (still capped by the enemy ammo-speed ceiling). A guided round's speed is never rolled, so its turn
  radius is a fact rather than a range: tightest just after launch (112 / 116 / 250 px for the player's three
  guns) and widest at cruise, which is why R16 is measured at the launch end.
- **A player rack is gated by range alone and does not aim:** its round leaves along the hull's nose and
  steers onto the lock, so a forward mount covers what is behind the hull, and the price is the whole way
  round at a slow launch. The enemy rack keeps its cone and keeps aiming, because a round launched backwards
  from a 980-1330 px ring could not cross that ring inside its fuse.
- **The dart is drawn 7.5 × the collision radius** — a fixed 5 px nose over a 3× longer shaft, ~45 px for the
  torpedo and ~30 px for the pod rounds. At 2.5 it drew ~15 px and read as a bright dot. The body is painted
  light grey for both sides, so the **flame** is the colour channel: Orange for the player's racks, Red for an
  enemy's. It answers both the turn and the throttle still running, keeps its full colour at cruise, washes
  toward white only in a hard turn, and has floors on length and brightness either way.
- Sim cost: the pinned scenarios run unarmed, so they prove no regression only — `ammo-spray` sim median
  **1.30-1.34 ms** and `impact-storm` **2.16-2.19 ms**, against 1.33 / 2.12 before any missile code (this
  host's p99 band is ±0.3 ms, so all of it sits inside noise). A missile-carrying scenario is the missing
  measurement.

## Non-circle hull collision — done

Hulls collide as the shape their art was drawn for (plans/NON_CIRCLE_COLLISION.md). Circles stay
everywhere they were always right: rocks, mines, pickups, and every round in flight.

- The primitive is a **capsule** — a segment inflated by a disc — and a hull is one to four of them.
  Every pair of round primitives is then point/point, point/segment or segment/segment: normal, depth
  and touch point all fall out of the closest points, uniquely, and round ends remove the
  feature-pairing case that makes polygon pairs long and jittery. Polygon+SAT was priced at 10-50× this
  for no gameplay gain; pixel-to-pixel was rejected outright (no normal, no depth, no contact point to
  resolve, and it makes art load-bearing gameplay).
- Shapes are **authored data** (`Domain/Stats/HullShapes.cs`), one per art id, in the sprite's frame and
  in units of the hull's `Radius` — never derived from pixels at run time, because the sim must not
  depend on the art. The numbers came off the silhouette with `.tools/measure-hull.py` (painted area in
  R²: swarmer 3.97 against a disc's 3.14, so it stays a disc and it is also the most numerous hull;
  interceptor 1.96, its disc 60% air → spine plus wing bar; heavy cannon 2.37 with paint out to
  |u| = 0.89 → slab plus a pod at each wing end).
- **The bounding radius did not move.** It is still what the grid indexes and culls by, so the broad
  phase, explosion radii, spawn placement and the targeting ring are untouched, and a shape can never
  reach past the query that found it (`HullShapesTest` pins it). The circle is kept as a conservative
  reject in front of the shape test.
- One contact per body pair per tick — deepest for resolution, earliest-along-path for a swept round —
  so the impulse, the position correction and the impact-damage accumulator kept their shape.
- Mass and inertia still come from the bounding radius, deliberately: making weight follow the silhouette
  is a balance change and has to be measured as one.
- Cost, same worlds with shapes on and off: `hull-stream` **0.75 vs 0.72 ms**, `hull-crowding` **0.37 vs
  0.31 ms**. Against the pre-change baselines the stream got *cheaper* (1.20 → 0.75): a 5500-round volley
  used to be eaten by a 45-radius disc its rounds could not see.
- Diagnostics draws each hull's collider in cyan over its art instead of the old orange bounding circle —
  enemy hulls and the player in play, and every preview on the ship select screen. The player's collider
  is a cyan disc because that is what it still is.
- Player hulls are still circles on purpose — their shapes are a balance decision (an 84-radius Heavy's
  silhouette is far thinner than its disc), not a plumbing one.

## Near future

- New ships and engines (crab with better side ways acceleration, broadside with heavy slot pointing to one side, what else?)
- New mines (Turns into missile, fires a delayed beam)
- Difficulty scaling: pellet volleys and concurrency; mines and the reward side (the type/composition
  drift landed as the armament ladder)

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
