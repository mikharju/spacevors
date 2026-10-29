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
2. Action: TurretFiringSystem → EnemyMissileFiringSystem → EnemyShipSpawnSystem
3. Resolution: CollisionSystem → PickupMagnetSystem → LevelUpSystem → ShipDeathExplosionSystem → EffectSystem
4. Intent: MissileGuidanceSystem → MineDriftSystem → MineRespawnSystem → EnemyShipSystem → CameraSystem → DamageEffectSystem

Systems in this phase write next-tick motion intent (Acceleration/Velocity) and view state; Movement integrates them on the following tick. Order within a phase matters for command-written components (see Write patterns).

`MissileGuidanceSystem` is deliberately **first** in Intent: it turns a guided round's heading, builds its speed toward `MaxSpeed`, and rewrites both `Velocity` (what Movement integrates) and the round's own `Ammo.Velocity` (what the swept collision reads and the renderer draws the streak from), so the round's position next tick, its collision segment and its drawn direction all agree on the heading guidance just chose. It never gives a round an `Acceleration` component: the speed is rewritten at the tick boundary as it climbs, so the collision pass still reads one constant velocity between ticks and the turn radius stays `Speed / TurnRate`.

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

## Guided rounds

A missile is a round, not a hull: it is spawned exactly like a bullet (`Position` + `Velocity` + `Ammo`)
with three components added, and it is destroyed exactly like one.

Components:
- `Missile(LockedTarget, Speed, MaxSpeed, Acceleration, TurnRate, Steering)` — the lock taken at launch, the
  flight numbers, and how hard the round turned last tick (0..1), which is one of the two numbers the exhaust
  is drawn from. Self-contained: nothing downstream looks back up at the gun that fired it.
  `Speed` is the round's own flight speed and `MissileGuidanceSystem` is its only writer: it climbs by
  `Acceleration` toward `MaxSpeed`, so a round leaves its rack at `WeaponStats.MissileLaunchFraction` of its
  card speed and builds to that speed. The turn radius is `Speed / TurnRate`, which makes it tightest just
  after launch and widest at cruise — the balance rules are measured at the launch end for that reason.
- `Health` — a guided round is a body with 1-3 hp, so the other side can shoot it down. Dumb ammo has none.
- On the launcher only: `MissileWeapon(Weapon, Range)` (a rack's own stats and its own, wider launch ring)
  and `MissileCooldown(Timer)`. A hull may carry guns, a rack, or both, each on its own cooldown; a
  missile-only hull has no `Turret` component at all.

Who may launch: **range only**. A guided mount is not gated by its slot's arc and does not slew — a rack is a
square that does not turn — so its round leaves along the hull's nose and guidance picks the lock up from
there, which lets a forward rack cover what is behind the hull. The price is geometry, not a rule: the round
has to fly all the way around, from a slow launch, at the target's speed. The enemy rack keeps its firing
cone, because an enemy round that launched backwards could not cross its own launch ring inside its fuse
(R21 reach honesty in plans/WEAPON_BALANCE.md), while the player chose the weapon and pays for the long way.

Two passes produce them — `TurretFiringSystem` for the player's homing guns, which lock instead of
computing lead, and `EnemyMissileFiringSystem` for enemy racks — and one pass steers them. The enemy rack
is a second gun on the same hull rather than a new enemy kind: the escalation is an armament ladder over
the existing hulls (plans/DIFFICULTY_SCALING.md R2.12). A guided round's speed is never rolled: the gun's
±15 % speed variation applies to dumb ammo only, because on a guided round the speed *is* the turn radius and
the fuse reach.

Faction rule: a missile sits in the collision grid (`CollisionKind.Missile`) and the cross-fire pass
counts only hits from the **opposing** side. Same-side rounds and beams pass through it, which is what
keeps a player's missiles from eating their own DPS and what makes a round never collide with itself. A
missile shot down breaks up in smoke; its warhead never detonates.

Rendering: `world.missiles` is its own pass (`WorldRenderer.DrawMissiles`), drawing tip + shaft + plume
from two generated sprites. The body is drawn `MissileBodyStretch` (7.5) times the collision radius long with
a fixed-size nose: at 2.5 a round drew a 10 px dart and read as a bright dot at the ranges missiles are
fired across. The plume answers two numbers — the rudder (`Steering`) and the throttle (how far the round is
still from its top speed) — and both have floors, so a guided round is never invisible mid-flight. The plume
is anchored on the body's **tail**, not part-way between the centre and the tail, so it stays aft as it grows
with boost and turn. Both draws use the renderer-wide sprite convention: the dest rect's **position is the
point the sprite is centred and rotated about** (`RenderHelpers.CenterOrigin` supplies the half-size origin),
so building the dest rect as `centre - size/2` instead shifts a sprite up-left by half its own extent — a
45 px body slides 22 px while its plume slides 10, and the two visibly come apart. `ShapeSprites.PlumeAnchor`
computes that anchor, and the diagnostic overlay (V key) marks the same point it returns, so a screenshot
shows the intended anchor under the drawn flame. The B key adds a static bench: the same `ShapeSprites.DrawMissile`
call repeated over eight headings and both plume sizes at fixed screen positions, so one screenshot measures
every orientation at once against numbers the renderer itself publishes. The
player's guided rounds are Orange (the guided colour) and the enemy's stay
Red, because colour is the threat channel and the shape is what says "guided". Both bodies are painted the
same light grey (`MissileBodyColor`) — a missile is a piece of hardware, not a faction badge — so the
**flame** is the colour channel: the round's own ammo colour, Orange for the player's racks and Red for an
enemy's, washing toward white only while the round is turning hard. A rack's mount marker is a
square that does not turn, against the round empty mounts and the aiming triangles of gun mounts.

## Collision

Two passes and one seam between them. `SpatialGrid` is the broad phase: it indexes every collider by its
**bounding radius** and answers "what could touch this box or circle". `NarrowPhase` answers "did they
touch, how deep, and which way", and always answers it with one `Contact(Normal, Depth, Point)`.

- Every overlap in the game goes through that seam; the eight call sites in `CollisionSystem` hold no
  circle math of their own.
- A body enters the narrow phase as a `CollisionBody(Position, Angle, Shape, Scale)`. Rocks, mines,
  pickups and every round are `CollisionBody.Circle`; a hull brings the capsules its art was authored
  with — an enemy by art id (`Stats/HullShapes.cs`), a player per hull type on `ShipType.Shape`, carried into
  the `Player` component (`Stats/PlayerHullShapes.cs`) — both in the sprite's own frame and in units of
  `Radius`. Shapes are committed data reviewed as a diff, never derived from pixels at run time — the sim
  must not depend on the art. A `Player` built without one collides as the disc its `Radius` describes:
  that is what test worlds do, and the game always sets it (`PlayerHullShapesTest` pins the wire-up).
- Circles keep a one-test fast path, and the bounding circle is kept as a **conservative reject** in
  front of the shape test: a shape never reaches past the radius the grid indexed it by (pinned by
  `HullShapesTest`), so anything that misses the circle cannot touch the shape either. A volley through
  empty space pays nothing new.
- The player is one more body, built once a tick and queued behind the same circle reject for everything it
  meets: a grinding hull, a rock, a mine, an enemy round, an enemy beam. Because its shape lies inside the
  bounding radius, a player's hurtbox can only be smaller than the disc it replaced — never hit where the disc
  could not have been. `PlayerHullShapesTest` states that as the reach invariant;
  `PlayerHullCollisionTest` pairs every pass-through with the same geometry against a shape-less player,
  because a pass-through on its own proves only that the setup was wrong.
- `CapsuleGeometry` is the whole shape math. A capsule is a segment inflated by a disc, so a disc is a
  capsule with no axis and every pair of round primitives is one of point/point, point/segment,
  segment/segment — the normal, the depth and the touch point all fall out of the closest points,
  uniquely, with no "which feature touched which" case to specialise.
- Ordering differs by caller and is not interchangeable: a swept round takes the **earliest** contact
  along its path, a resting pair takes the **deepest** (which way to push). Either way a body pair yields
  exactly one contact per tick, which is what the impulse, the position correction and the
  impact-damage accumulator assume.
- Mass and inertia still come from the bounding radius, not from the shape: weight following silhouette
  is a balance change and has to be measured as one.
- Diagnostics (`SPACEVORS_DIAGNOSTIC=1`) draws a hull's collider in cyan over its art — enemy hulls and the
  player in play, and every ship preview on the select screen — as the exact capsule silhouette, round caps
  included, because an overlay that ends flat blames the fit on a corner nothing can hit. A collider is
  never drawn as its bounding circle: that is the broad phase's business, and drawing it over the art hides
  the mismatch the overlay exists to show.

## Cross-system signaling

No event queues. Systems signal each other through components, written via the CommandBuffer (see Write patterns). A later system in a same or next phase reacts to the component it finds.

Examples:
- CollisionSystem adds Dead; ShipDeathExplosionSystem reacts with staged explosions
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
        Combat/                -- firing (incl. click-target priority — a rock is a target only when clicked, never auto-targeted; auto-aim solves the intercept in the hull's frame and re-solves it per round at the speed and from the muzzle that round really has; a lead the mount cannot point at inside its arc makes the target unselectable rather than being fired unled; hitscan laser beams), collisions (swept-capsule ammo, beam first-hit pass, small-asteroid health; narrow phase behind one seam — circles for rounds, rocks and mines, capsule composites for hulls), impact damage rule (never asteroids), effects, damage emission, death explosions, asteroid factory (spawn, and break-up smoke where a small rock's end is puffs rather than a fireball), threat readout (`ThreatMeter.Measure`: one read a second of who can hit the hull right now, never called from the hot path)
        Components/            -- runtime per-entity components only (entity, physics, combat, effect, gameplay); static definitions live in Stats/
        Stats/                 -- tuning data, code-first: PlayerShipStats (engines, ships), WeaponStats (weapons with the level their tier unlocks at, loadouts), EnemyShipStats (types with their per-tier drifts — reach, closing speed, ammo speed — plus spawn weights, tier curves, enemy weapon and the ceilings those drifts obey), HullShapes (each hull art id's collision primitives: capsules in the sprite's frame, in units of the hull's Radius), MineStats, UpgradeStats (+ XP curve), LootStats, SpawningStats (initial world, spawn ramps, chase/cull). The balance rules the weapon table obeys (range ladder, hitscan priced below slower ammo, tier ceiling, mount-size floor, round-rate budget) are asserted by Tests/WeaponBalanceTest.cs; the difficulty ceilings (the firing ring stays inside the player's mid band, enemy ammo stays dodgeable, the toughest hull dies inside a TTK budget) by Tests/EnemyDifficultyTest.cs. plans/WEAPON_BALANCE.md and plans/DIFFICULTY_SCALING.md state why.
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