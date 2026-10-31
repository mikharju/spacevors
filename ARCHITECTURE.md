# ARCHITECTURE.md

Stable structure and invariants. Feature rationale, history and measurements live in the
`plans/` file for that feature (trust its `Status:` header first).

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
- components (runtime per-entity state only)
- stats (tuning data, code-first — see Stats/ below)
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
Ammo LaserBeam FireCooldown Turret TurretSlot TurretAim WeaponCargo PrimaryTarget AutoTargetMark
Explosion BeamImpact DamageSpark HealSpark HealthOrb XpPickup ShipDeathExplosion SmokePuff DebugMarker
Health (Current, Max) PendingChoice PendingUpgradeOptions UpgradeCounts Dead
```

Health rides the player, enemy ships, mines and small asteroids: carrying it is what makes an
entity damageable, so the large asteroids that are pure scenery have none and a hit on them
stays an obstacle hit.

## Write patterns

Three ways systems write components, plus direct app-layer writes:

1. Deferred commands (default): a system adds writes to the phase CommandBuffer. One buffer per tick is applied once after every system in that phase finishes; later phases then read those values within the same tick. Within a phase all systems read pre-phase values for command-written components; last write wins in command order. Destructions are deferred until after all other commands of the flush, so an entity destroyed this phase can still receive writes in the same flush (they are discarded with it).
2. Direct ref mutation (speed exceptions): three systems mutate a single component in place via slot refs instead of queuing a command per entity — this avoids thousands of command allocations per tick on hot paths:
   - PositionIntegrationSystem integrates Position in place; it reads the pre-phase Velocity (PhysicsSystem's new-velocity commands are not applied yet), so position advances with last tick's velocity.
   - AmmoLifetimeSystem decrements Ammo.Lifetime in place; only destructions go through commands.
   - PickupMagnetSystem writes Player.Xp straight through WorldView.GetComponentRef so LevelUpSystem (same phase, runs later) reads the fresh value in the same tick.
3. Local accumulation + single flush: CollisionSystem accumulates per-entity velocity/position-correction/angular-velocity/health deltas in per-frame dictionaries and emits one command per entity at the end of Update — otherwise last-write-wins would drop all but the final hit on an entity struck several times in one frame.

Direct writes (app layer): GameSession writes straight to the EntityManager before the simulation step or while paused, never mid-phase:
- Pre-phase (form the tick's initial state; systems read them normally): Acceleration/AngularVelocity from input, turret mount Position and Rotation from `TurretMount.SyncWorldTransforms` — both are derived from the hull plus the slot offset and the `TurretAim` yaw, so a turning hull carries every mount with it; the aim itself (`TurretAim`) is simulation state, command-written by TurretFiringSystem, diagnostic-key writes.
- While paused: upgrade application (Player/Turret/Health, and mounting a new weapon into a hull slot), pending-choice cleanup, thruster removal on pause entry.

Rules:
- Systems default to CommandBuffer; the direct ref mutations above are the only exceptions and must stay single-component in-place edits with no structural changes during iteration. Any new direct-write path requires updating this section first.
- Direct mutation is visible immediately to later systems in the same phase; command writes are not. Choose deliberately: use a ref when a same-phase reader needs the fresh value, otherwise queue a command.

## Phases

One tick runs four phases in order (SimulationRunner). The per-tick CommandBuffer is applied after each phase, so later phases read earlier phases' writes within the same tick:

1. Movement: PhysicsSystem → PositionIntegrationSystem → AmmoLifetimeSystem
2. Action: TurretFiringSystem → EnemyMissileFiringSystem → EnemyShipSpawnSystem
3. Resolution: CollisionSystem → PickupMagnetSystem → LevelUpSystem → ShipDeathExplosionSystem → EffectSystem
4. Intent: MissileGuidanceSystem → MineDriftSystem → MineRespawnSystem → EnemyShipSystem → CameraSystem → DamageEffectSystem

Systems in this phase write next-tick motion intent (Acceleration/Velocity) and view state; Movement integrates them on the following tick. Order within a phase matters for command-written components (see Write patterns).

`MissileGuidanceSystem` is deliberately **first** in Intent: it rewrites both `Velocity` and the round's own `Ammo.Velocity` so the position next tick, the collision segment and the drawn direction all agree on the heading it just chose. A round keeps one constant velocity between ticks, so its turn radius stays `Speed / TurnRate`.

## Main loop

```
Ship selection (before game starts) → Input → Simulation (fixed timestep) → Rendering
```

Ship selection is shown before the game loop begins.

Player chooses one of the defined ship types (`ShipType.All`: Scout, Fighter, Heavy, Shadow, Falcon, Eagle, Manta, Raptor). In diagnostics mode the list also carries `ShipType.DiagnosticShips`: Shadow copies with one weapon each.

Game pauses during upgrade choice; resumes after selection.

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

## Guided rounds

A missile is a round, not a hull: spawned and destroyed exactly like a bullet (`Position` +
`Velocity` + `Ammo`) plus `Missile(LockedTarget, Speed, MaxSpeed, Acceleration, TurnRate,
Steering)` and `Health` (1-3 hp). On the launcher only: `MissileWeapon(Weapon, Range)` and
`MissileCooldown(Timer)`; a hull carries guns, a rack, or both.

- `MissileGuidanceSystem` is the only writer of `Speed`: it climbs by `Acceleration` toward
  `MaxSpeed` from a launch fraction of the card speed. The turn radius `Speed / TurnRate` is
  tightest just after launch and widest at cruise; balance rules are measured at the launch end.
- A guided round's speed is never rolled (it *is* the turn radius and fuse reach); the ±15 % ammo
  speed variation applies to dumb ammo only.
- A player rack is gated by **range only** and does not aim or slew — the price is flying all the
  way around from a slow launch. The enemy rack keeps its cone, because its round must cross its
  own launch ring inside its fuse (R21, plans/WEAPON_BALANCE.md). Enemy missiles are an armament
  ladder over existing hulls, not new enemy kinds (plans/DIFFICULTY_SCALING.md R2.12).
- Faction rule: only the **opposing** side's fire hits a missile (`CollisionKind.Missile`);
  same-side rounds and beams pass through. A shot-down missile breaks up in smoke; its warhead
  never detonates.
- Rendering (dart proportions, flame-as-colour-channel, plume anchor, bench keys):
  plans/MISSILES.md.

## Collision

Two passes and one seam. `SpatialGrid` (broad phase) indexes and culls every collider by its
**bounding radius**. `NarrowPhase` answers "did they touch, how deep, which way" and always
returns one `Contact(Normal, Depth, Point)`.

- Every overlap in the game goes through that seam; no call site in `CollisionSystem` holds circle
  math of its own.
- A body enters as `CollisionBody(Position, Angle, Shape, Scale)`: circles (one-test fast path)
  for every round, mine and pickup; a hull is 1-4 capsules, a rock is a disc or two capsules by
  art variant — authored in `Stats/HullShapes.cs`, `Stats/PlayerHullShapes.cs`,
  `Stats/AsteroidShapes.cs`, in the sprite's frame and in units of `Radius`, never derived from
  pixels at run time.
- The bounding disc stays the **conservative reject** in front of every shape test, and a shape
  never reaches past it (pinned by `HullShapesTest`, `PlayerHullShapesTest`,
  `AsteroidShapesTest`). A player's hurtbox can only be smaller than the disc it replaced.
- A capsule is a segment inflated by a disc; a disc is a capsule with no axis. A swept round
  takes the **earliest** contact along its path, a resting pair the **deepest**; exactly one
  contact per body pair per tick.
- Beams never penetrate: the first-hit pass shortens the beam to the exact first contact and puts
  one glow on the shortened end.
- Mass and inertia still come from the bounding radius, not from the shape: weight following
  silhouette is a balance change and has to be measured as one.
- Diagnostics (`SPACEVORS_DIAGNOSTIC=1`) draws colliders in cyan as the exact capsule
  silhouette, never as the bounding circle.
- Why capsules and not polygons or pixels, silhouette fitting, and measured cost:
  plans/NON_CIRCLE_COLLISION.md.

## Cross-system signaling

No event queues. Systems signal each other through components, written via the CommandBuffer (see Write patterns). A later system in a same or next phase reacts to the component it finds.

Examples:
- CollisionSystem adds Dead; ShipDeathExplosionSystem reacts with staged explosions; Dead entities are not auto-targeted, so weapons retarget instead of firing into a wreck. An enemy entity is destroyed under the final blast; the player becomes a husk instead - Position, Rotation and the sequence component come off and the turret mounts are destroyed, while Player/Health/Dead stay for the HUD bars, stats and game-over detection (the sequence component must never outlive its sequence: a leftover re-fires the final stage every frame).
- TurretFiringSystem / EnemyMissileFiringSystem put `Missile` + `Health` on a round at launch; MissileGuidanceSystem steers it each tick; CollisionSystem reads the same round's `Ammo` to shoot it down
- PickupMagnetSystem raises Player.Xp (direct ref write, see Write patterns); LevelUpSystem spawns PendingChoice + PendingUpgradeOptions
- GameSession reads PendingChoice to pause and show the upgrade menu

## Determinism

- One world RNG owned by EntityManager (default seed 42), exposed as WorldView.Rng; all gameplay randomness (spawning, loot, scatter, upgrade shuffles) goes through it. No Random.Shared anywhere in src/.
- Elapsed time lives on EntityManager (WorldView.ElapsedTime); difficulty ramps, spawn intervals, and enemy stat tiers (EnemyShipStats.TierFor) derive from it.
- Same seed → same run; covered by Tests/WorldRngTest.cs (same-seed spawn equality).

## Project layout

```
src/

    Game/                    -- app layer: window, input, rendering (Raylib-cs)
        SpaceVorsApp.cs          -- entry point: window + top-level loop (ship select <-> session; SPACEVORS_SCENARIO* runs a perf scenario instead)
        GameSession.cs           -- one playthrough: player input, fixed-timestep stepping, pause/menu, upgrades, scenario [PERF] rows
        GameInitializer.cs       -- starfield + world clutter (the entities themselves come from Domain WorldFactory)
        Renderer.cs              -- frame orchestration (scene composition)
        RenderTimings.cs         -- per-pass render timing, diagnostics only (one reused stopwatch)
        BackgroundRenderer.cs    -- starfield + clutter layers
        WorldRenderer.cs         -- world entities: asteroids, ammo, effects, pickups
        EnemyShipRenderer.cs     -- enemy ship sprites + fallbacks
        ShipSpriteRenderer.cs    -- player ship sprite (lit/flat) + shared ship placeholder shape
        ThrusterFlameRenderer.cs -- thruster flames
        TargetingRenderer.cs     -- targeting brackets + hp bars
        HudRenderer.cs           -- health bar + game over text
        UpgradeMenuRenderer.cs   -- upgrade choice cards
        MountMenuRenderer.cs     -- turret-mount / cargo screen (hull, arcs, take down / fit)
        StatsScreenRenderer.cs   -- ship stats screen (Tab)
        ShipSelectScreen.cs      -- ship selection screen (scrollable list + input)
        RenderHelpers.cs         -- shared world→screen transform + screen-space culling helpers
        Ui.cs                    -- shared UI primitives: rounded rects, soft-depth panels, easing/fade
        Palette.cs               -- colours shared by more than one renderer
        ImageLoader.cs           -- texture loading (flat + lit sprite sets)
        Lighting.cs              -- lighting shader: directional + point lights + procedural hull damage
        DamageShading.cs         -- hp ratio -> hull damage scalar [0,1] for the shader
        LightGatherer.cs         -- per-frame point-light collection for the shader
        LitSprite.cs             -- lit sprite data (base/normal/depth textures)
        LitSpriteMatcher.cs      -- matches texture files into lit sprites
        LitGroupRenderer.cs      -- batches lit draws per variant under one shader-mode block
        ShapeSprites.cs          -- circles and streaks as tinted quads of one generated disc
        AsteroidSprite.cs        -- asteroid graphics variants

    Domain/                  -- pure game logic, no Raylib
        AI/                    -- enemy ship + mine spawning (incl. SpawnPlacement), factories, chase AI, drift
        Combat/                -- firing, click-target priority, auto-aim lead, collisions (the seam: swept capsules, beam first-hit), impact damage, effects, death explosions, threat readout (ThreatMeter, off the hot path)
        Components/            -- runtime per-entity components only (entity, physics, combat, effect, gameplay); static definitions live in Stats/
        Stats/                 -- all tuning data, code-first: PlayerShipStats, WeaponStats (weapons with tier unlock, loadouts), EnemyShipStats (per-tier drifts, spawn weights, tier curves, ceilings), HullShapes / PlayerHullShapes / AsteroidShapes (collision primitives per art id), TurretLayout (per-hull mount slots and sizes), MineStats, UpgradeStats (+ XP curve), LootStats, SpawningStats, WaveStats + WaveSchedule (wave/lull timing). Balance rules asserted by Tests/WeaponBalanceTest.cs and Tests/EnemyDifficultyTest.cs; plans/WEAPON_BALANCE.md and plans/DIFFICULTY_SCALING.md state why.
        Physics/               -- force integration + position integration
        Progression/           -- XP/level-up, pickups, camera
        Support/               -- SimulationRunner (phase ordering + RunTick: the four phases with a command flush after each)
        EntityManager.cs       -- EntityManager + ComponentStorageBase + ComponentQuery<T1..T4>
        ComponentStorage.cs    -- ComponentStorage<T>: compact arrays, swap-pop
        Entity.cs              -- entity id type
        System.cs              -- GameSystem base class (Update contract)
        WorldView.cs           -- per-step view: reads, world RNG/elapsed time, viewport/mouse seams
        Commands.cs            -- command records + CommandBuffer
        CommandProcessor.cs    -- applies a CommandBuffer to the EntityManager
        SpatialGrid.cs         -- broad-phase spatial hash
        Vector2.cs             -- 2D math
        Angle.cs               -- rotation convention helpers (a faces (sin a, -cos a))
        TurretMount.cs         -- mount slots as entities: fit/mount rules + SyncWorldTransforms
        CoolDownHelper.cs      -- FireCooldown read helper
        DiagnosticLogger.cs    -- SPACEVORS_DIAGNOSTIC=1 logging; EnvVar is the single gate source (the other SPACEVORS_* seams are listed in DIAGNOSTICS.md)
        WorldFactory.cs        -- starting world: player, camera, hull turret mounts, asteroids, mines, enemy ships (seeded, no Raylib)
        Scenarios/             -- ScenarioSpec + the named perf scenarios + ScenarioWorld (seeded start states)

    RenderBench/             -- headless render benchmark (lit-sprite draw cost)
    ScenarioBench/           -- headless sim-only scenario runner: same [PERF] rows, no renderer

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
- Frame-cost split: with `SPACEVORS_DIAGNOSTIC=1` each tick logs `[SIM] tick: Xms`, each flush `[APPLY] <phase>: Xms`, each frame `[RENDER] frame: Xms`. `[RENDER]` includes raylib's `SetTargetFPS` pacing wait — read it against `[FRAME] dt=` to tell sim-bound from render-bound. Never sum `[SYSTEM]` totals together with their sub-stage lines.
- Threat readout: `[THREAT]` prints once a second in every run (it is what the difficulty knobs are tuned against); `ThreatMeter.Measure` is a read-only aggregate, never called from the hot path.
- Perf scenarios: `SPACEVORS_SCENARIO=<name>` runs a seeded scenario (pinned viewport/mouse, one tick per frame, uncapped); one `[PERF]` row per measured frame; `.tools/scenario.sh` pools runs, `.tools/run/index.tsv` is the perf record; budgets and `--check-baseline` exit non-zero. Design, decisions and current numbers: plans/PERF_SCENARIOS.md.
- Shape batching: the many-small passes (ammo, sparks, explosions, smoke) draw through `ShapeSprites` — a generated disc quad inside raylib's batched 2D path — not `DrawCircle`/`DrawLineEx`, so thousands of particles become a handful of draws.

## Troubleshooting

Check TROUBLE_SHOOTING.md for problems encountered in the past. When encountering new problems, update TROUBLE_SHOOTING.md.
