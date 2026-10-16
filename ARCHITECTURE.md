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
Explosion DamageSpark HealSpark HealthOrb XpPickup ShipDeathExplosion SmokePuff DebugMarker
Health (Current, Max) PendingChoice PendingUpgradeOptions UpgradeCounts Dead
```

Health rides the player, enemy ships, mines and small asteroids: carrying it is what makes an entity damageable, so the large asteroids that are pure scenery have none and a hit on them stays an obstacle hit.

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
2. Action: TurretFiringSystem → EnemyShipSpawnSystem
3. Resolution: CollisionSystem → PickupMagnetSystem → LevelUpSystem → ShipDeathExplosionSystem → EffectSystem
4. Intent: MineDriftSystem → MineRespawnSystem → EnemyShipSystem → CameraSystem → DamageEffectSystem

Systems in this phase write next-tick motion intent (Acceleration/Velocity) and view state; Movement integrates them on the following tick. Order within a phase matters for command-written components (see Write patterns).

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

Player chooses one of the defined ship types (`ShipType.All`: Scout, Fighter, Heavy, Shadow). In diagnostics mode the list also carries `ShipType.DiagnosticShips`: Shadow copies with one weapon each.

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
        TargetingRenderer.cs     -- targeting brackets + hp bars: red for the locked target, blue-grey for auto-targeted enemies
        HudRenderer.cs           -- health bar + game over text
        UpgradeMenuRenderer.cs   -- upgrade choice cards
        StatsScreenRenderer.cs   -- ship stats screen (Tab)
        ShipSelectScreen.cs      -- ship selection screen (scrollable list + input)
        RenderHelpers.cs         -- shared world→screen transform + screen-space culling helpers
        Ui.cs                    -- shared UI primitives: rounded rects, soft-depth panels, easing/fade
        Palette.cs               -- colours shared by more than one renderer
        ImageLoader.cs           -- texture loading (flat + lit sprite sets)
        Lighting.cs              -- lighting shader: directional light + point lights + procedural hull damage (vertex color B)
        DamageShading.cs         -- hp ratio -> hull damage scalar [0,1] for the shader
        LightGatherer.cs         -- per-frame point-light collection for the shader
        LitSprite.cs             -- lit sprite data (base/normal/depth textures)
        LitSpriteMatcher.cs      -- matches texture files into lit sprites
        LitGroupRenderer.cs      -- batches lit draws per variant under one shader-mode block
        ShapeSprites.cs          -- circles and streaks as tinted quads of one generated disc
        AsteroidSprite.cs        -- asteroid graphics variants

    Domain/                  -- pure game logic, no Raylib
        AI/                    -- enemy ship + mine spawning (incl. SpawnPlacement), factories, chase AI (player-speed-scaled cap, stale-ship cull), drift
        Combat/                -- firing (incl. click-target priority — a rock is a target only when clicked, never auto-targeted; hitscan laser beams), collisions (swept-capsule ammo, beam first-hit pass, small-asteroid health), impact damage rule (never asteroids), effects, damage emission, death explosions, asteroid factory (spawn, and break-up smoke where a small rock's end is puffs rather than a fireball), threat readout (`ThreatMeter.Measure`: one read a second of who can hit the hull right now, never called from the hot path)
        Components/            -- runtime per-entity components only (entity, physics, combat, effect, gameplay); static definitions live in Stats/
        Stats/                 -- tuning data, code-first: PlayerShipStats (engines, ships), WeaponStats (weapons with the level their tier unlocks at, loadouts), EnemyShipStats (types with their per-tier drifts — reach, closing speed, ammo speed — plus spawn weights, tier curves, enemy weapon and the ceilings those drifts obey), MineStats, UpgradeStats (+ XP curve), LootStats, SpawningStats (initial world, spawn ramps, chase/cull). The balance rules the weapon table obeys (range ladder, hitscan priced below slower ammo, tier ceiling, mount-size floor, round-rate budget) are asserted by Tests/WeaponBalanceTest.cs; the difficulty ceilings (the firing ring stays inside the player's mid band, enemy ammo stays dodgeable, the toughest hull dies inside a TTK budget) by Tests/EnemyDifficultyTest.cs. plans/WEAPON_BALANCE.md and plans/DIFFICULTY_SCALING.md state why.
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
        CoolDownHelper.cs      -- FireCooldown read helper
        DiagnosticLogger.cs    -- SPACEVORS_DIAGNOSTIC=1 logging (fps, per-system timings, per-tick sim total, render total, per-phase command-flush total, events); EnvVar is the single source for the gate. `LogThreat` is the one exception to the gate: the `[THREAT]` row is a tuning record and prints in every run.
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
- Frame-cost split: with `SPACEVORS_DIAGNOSTIC=1` each tick logs `[SIM] tick: Xms` (all four phases plus their command flushes), each flush logs `[APPLY] <phase>: Xms`, and each frame logs `[RENDER] frame: Xms` around the draw call. `[RENDER]` includes raylib's `SetTargetFPS` pacing wait, which happens inside `EndDraw`, so it floors at ~1000/MaxFps ms whenever the game is fast enough to hit the cap — read it against `[FRAME] dt=` to tell whether a frame is sim-bound or render-bound. Never sum `[SYSTEM]` system totals together with their sub-stage lines (`CollisionSystem` and `Collision: *` overlap).
- Threat readout: `GameSession` calls `ThreatMeter.Measure` once a second of game time and prints `[THREAT] t=… tier=… ships=… ring=… rounds=… in=…/s out=…/s hp=… lvl=… mines=… rocks=…`. `ring` counts the ships standing inside their own gun range plus the player's hull radius — the ones that can hit the hull this instant — `in` is their summed fire rate × pellets × damage, `out` the same sum for the guns mounted on the player's hull. It prints in every run, not only under the diagnostics gate, because it is what the difficulty knobs are tuned against; the same row is drawn along the bottom edge when `SPACEVORS_DIAGNOSTIC=1` (`hud.threat` render pass). It is a read-only aggregate, called once a second, never from a system.
- Perf scenarios: `SPACEVORS_SCENARIO=<name>` opens a seeded scenario in a real window instead of ship select — pinned 1920×1024 viewport, centred mouse, exactly one tick per frame, upgrades applied without pausing, uncapped frame rate (`SPACEVORS_SCENARIO_CAP=<fps>` restores pacing), then `SPACEVORS_SCENARIO_HOLD` seconds of unmeasured aftermath. Each measured frame prints one `[PERF]` row (dt/sim/apply/render/other ms + entity counts by kind + GC collections and allocated bytes for the frame); `.tools/scenario.sh` pools runs into `frames.jsonl` + `summary.json`. Runs stay on disk under `.tools/run/` for a day (older runs are cleared by the next one): `.tools/run/latest` symlinks the newest run and `.tools/run/index.tsv` lists every run kept with its headline numbers, so a log is findable by someone who was not watching the terminal. `--sim` uses `ScenarioBench`, which drives the same `WorldFactory` + `ScenarioWorld` + `SimulationRunner.RunTick` with no renderer at all. The scenario start state is built in code (`Domain/Scenarios`), never reached by playing, and there is no warm-up phase.
- Shape batching: the five many-small-entities passes (`ammo`, `sparks`, `healSparks`, `explosions`, `smoke`) draw through `ShapeSprites`, not `Raylib.DrawCircle`/`DrawLineEx`. Shape draws flush raylib's 2D batch and issue one GL draw per shape — ~1.1 µs each on a real GPU, 58-78% of the render in the ammo scenarios. `ShapeSprites` draws a generated white disc with `DrawTexturePro`, which stays inside raylib's batched 2D path (the sprites' path), so thousands of particles become a handful of draws. A moving bullet's streak is that disc stretched along its travel.
- Scenario frames: `SPACEVORS_SCENARIO_SHOTS=start,mid,end` (or 1-based frame numbers) makes the game write PNGs of those measured frames into the run dir; `scenario.sh` sets it by default. Shot rows carry `shot=1` and are excluded from the statistics along with the frame that pays for the encode.
- Sim attribution: the `[SYSTEM]` lines `DiagnosticLogger` already emits per tick are aggregated by `scenario.py` into `systemMs` (worst first, share of sim) and `spikeFrames` (the systems behind the slowest distinct frame indices). System totals such as `CollisionSystem` are kept apart from the phases nested inside them such as `Collision: detection`, which is where the answer usually is. In `ammo-spray`, `CollisionSystem` is 76.4% of sim mean and the frame-9 tail is `Collision: detection` at 10.6 ms.
- Render-pass split: with diagnostics on, `RenderTimings` brackets each draw pass with one reused stopwatch and logs `[RENDER-PASS] <pass>: Xms` — the frame passes (`clear`, `lighting.begin`, `lights.collect`, `background`, `hud.*`, `present`) and every `WorldRenderer` pass (`world.ammo`, `world.sparks`, …). There is no aggregate `world` line: the `world.*` passes sum to it. `present` covers `EndDrawing`, which is also where raylib's `SetTargetFPS` wait lands. `summary.json` lists the passes worst-first with their share of the render total.
- Current status: `Tests/ScenarioPerformanceTest.cs` drives the scenario specs headless (simulation only, no GL) against characterized ceilings, and CI runs the suite, so a sim regression fails the build instead of waiting for a manual run. Render cost is measured on real hardware by `.tools/scenario.sh`: every scenario carries a 1000/120 ms budget, an unmarked breach exits non-zero, and `perShapeNs` tracks the cost of one drawn shape (~1.1 µs before shape batching, ~0.1 µs after). `impact-storm` is knowingly over budget (23,876 entities, ~15k sparks) and marked `ExpectedOverBudget`. The open question is the sim tail: repeatable spikes at fixed tick indices, with no GC involved.

## Troubleshooting

Check TROUBLE_SHOOTING.md for problems encountered in the past. When encountering new problems, update TROUBLE_SHOOTING.md.