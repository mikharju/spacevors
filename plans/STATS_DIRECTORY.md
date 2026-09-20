# STATS_DIRECTORY.md

Plan for a central `Domain/Stats/` directory holding all ship, weapon, mine and related stat data (2026-09-19).

**Status: implemented.** All three phases are done (commits "move … stats to Domain/Stats", "consolidate scattered stats into Domain/Stats", "add SpawningStats for difficulty tuning"). File names use the `…Stats` suffix per user request, and Phase 3 shipped as `SpawningStats.cs`. See "Implementation notes" at the bottom for deviations.

## Problem

Stat definitions are scattered across four places:

| What | Where today |
|---|---|
| Player ships, engines, weapons, loadouts, upgrade multipliers | `Components/GameplayComponents.cs` — mixed in with real ECS components (`Health`, `PendingChoice`, …) |
| Enemy ship types, mine types | `Components/EntityComponents.cs` — same mixing problem |
| Enemy tier scaling (HP/damage/fire-rate curves), enemy weapon constants, spawn weights | `AI/EnemyShipFactory.cs` |
| Mine health, XP curve, loot values, pickup behavior, player boost | inline in systems (`MineRespawnSystem`, `LevelUpSystem`, `CollisionSystem`, `PickupMagnetSystem`) and the app layer (`GameInitializer.PlayerBoost`) |

Consequences:

- Tuning one ship means opening 3–4 files; there is no single place that answers "what are X's stats".
- Mine health (2) is defined twice: `MineRespawnSystem.MineHealth` and inline in `GameInitializer`.
- Mine spawn speed range (`30 + rng*20`) is duplicated verbatim in the same two files.
- Enemy ammo radius 2.5 exists both as `WeaponType.AmmoRadius` default and as `TurretFiringSystem.DefaultAmmoRadius`.

## Proposal

New directory `src/Domain/Stats/`, namespace `Spacevors.Domain.Stats`. One file per entity kind, keeping the idiom already used in this codebase: `readonly record struct` + named static instances + `All` array + `FromX` lookup. No new abstractions, no config files — code-first per ARCHITECTURE.md (compile-checked, IDE-navigable, deterministic).

```
src/Domain/Stats/
    PlayerShipStats.cs  -- EngineLayout (3 presets), ShipType (4 ships) + All; shared player constants (Boost)
    WeaponStats.cs      -- WeaponStats, AddOnMount, WeaponType (7 weapons) + All/AddOnWeapons/FromName,
                           TurretDefinition, WeaponLoadout (7 loadouts)
    EnemyShipStats.cs   -- EnemyShipType (3 types) + All/FromGraphicsId/spawn weights; enemy weapon block; tier scaling curves
    MineStats.cs        -- MineSize, MineType (2 sizes) + FromSize (+ Health); spawn speed range; contact damage
    UpgradeStats.cs     -- UpgradeOption, UpgradeDefinition + All/For; XP curve XpForLevel(level)
    LootStats.cs        -- pickup/orb values: heal amount, magnet accel/speed, ship- and mine-death drop amounts/chances
    SpawningStats.cs    -- difficulty/spawn tuning in nested groups: InitialWorld, EnemyShips, Mines, Chase
```

Each file holds a `static class XxxStats` with shared constants/pure functions plus the per-instance record structs where applicable.

Rules:

- `Components/` keeps only per-entity **runtime** components. Static definitions move to `Stats/`. Components may reference stat types (e.g. `Turret.Weapon` is a `WeaponStats`) — that stays.
- A definition file holds data plus pure functions derived from it (tier curves, XP curve). Systems keep behavior.
- Extending = add one static block + an entry in the local `All` array (new ship → one place; new enemy type → one place incl. its spawn weight and texture key).

## Phase 1 — move existing definitions (pure relocation)

| From | To |
|---|---|
| `GameplayComponents.cs`: `EngineLayout`, `ShipType` | `Stats/PlayerShipStats.cs` |
| `GameplayComponents.cs`: `WeaponStats`, `AddOnMount`, `WeaponType`, `TurretDefinition`, `WeaponLoadout` | `Stats/WeaponStats.cs` |
| `GameplayComponents.cs`: `UpgradeOption`, `UpgradeDefinition` | `Stats/UpgradeStats.cs` |
| `EntityComponents.cs`: `EnemyShipType` | `Stats/EnemyShipStats.cs` |
| `EntityComponents.cs`: `MineSize`, `MineType` | `Stats/MineStats.cs` |

No value changes, no behavior change. After the move:

- `GameplayComponents.cs` shrinks to runtime components only (`Health`, `PendingChoice`, `UpgradableOption`, `PendingUpgradeOptions`, `UpgradeCount(s)`).
- `EntityComponents.cs` keeps `Player`, `EnemyShip`, `EnemyMine`, `Asteroid`, `Camera`.
- Using-directive updates in ~15 files: Domain (factories, systems), Game (`GameSession`, `GameInitializer`, `Renderer`, `WorldRenderer`, `UpgradeMenuRenderer`, `EnemyShipRenderer`, `ShipSpriteRenderer`, `ShipSelectScreen`), Tests (`EnemyTierStatsTest`, `OffScreenSpawnTest`, `TurretFiringTest`). RenderBench is unaffected.

Two commits: (1a) player ship/weapon definitions, (1b) upgrade/enemy/mine definitions. Build + full test run after each.

## Phase 2 — consolidate scattered stats

Each item below moves a value to its stat home; values are unchanged so behavior is identical. One small commit per group.

1. **Enemy tier scaling** (`AI/EnemyShipFactory.cs` → `Stats/EnemyShipStats.cs`):
   `TierDuration = 60`, `MaxTier = 10`, `BaseDamage = 1`, `HpMultiplier(t) = 1 + 0.25t`, `DamageAdd(t) = t / 2`, `FireRateMultiplier(t) = 1 + 0.1t`. The factory keeps component construction and calls the moved functions; `EnemyTierStatsTest` follows them to the new location.
2. **Enemy spawn weights** (`EnemyShipFactory.PickRandomType`): thresholds 0.333 / 0.666 → a `SpawnWeight` field on each `EnemyShipType` (≈1/3 each); `PickRandomType` derives from `All`. Adding a fourth enemy type then needs no threshold surgery.
3. **Enemy weapon block** (`EnemyShipFactory.BuildTurret`): name `"EnemyWeapon"`, arc π/8, scatter 0.05, pellet count 1, kickback 0 → named constants in `Stats/EnemyShipStats.cs`. Also the enemy ammo radius (currently `TurretFiringSystem.DefaultAmmoRadius = 2.5f`) moves here; the same-named local fallback for unknown player weapons stays in the firing system.
4. **Mine health**: `MineRespawnSystem.MineHealth = 2` + inline `new Health(2, 2)` in `GameInitializer` → a `Health` field on each `MineType` (both currently 2; per-size so they can diverge later). Both spawn sites read it.
5. **Mine spawn speed range**: duplicated `30f + rng*20f` in `GameInitializer` and `MineRespawnSystem` → `SpawnSpeedMin/Max` in `Stats/MineStats.cs`.
6. **Mine contact damage**: `CollisionSystem.MineContactDamage = 3` → `MineStats.cs` (it is literally "damage a mine does").
7. **XP curve** (`LevelUpSystem`): inline `level * 10` threshold → `XpForLevel(level)` in `Stats/UpgradeStats.cs`.
8. **Loot values** (`CollisionSystem.SpawnLootOnDeath` / `SpawnShipLootOnDeath`) → `Stats/LootStats.cs`: ship death XP 3, pickup radius 18; mine death orb chance 5% (shared with ship death — currently the same literal twice), orb radius = `XpPickupRadius + 2`; ship-death health orb radius 20.
9. **Pickup behavior** (`PickupMagnetSystem`): `HealthOrbHealAmount = 3`, `MagnetAcceleration = 800`, `MaxMagnetSpeed = 350` → `Stats/LootStats.cs`.
10. **Player boost**: `GameInitializer.PlayerBoost = 2.5f` (app layer) → shared player constant in `Stats/PlayerShipStats.cs`; also fixes the layering smell of gameplay tuning living in the app layer.

## Phase 3 — difficulty/spawn tuning file (done)

`Stats/SpawningStats.cs` consolidates spawn/difficulty constants currently owned by systems, so one file tunes the whole threat curve (the P1–P5 values from `plans/DIFFICULTY_SCALING.md`). Constants are grouped in nested static classes:

- `InitialWorld` (`GameInitializer`): mine count 9, ship count 6, initial ship band 2400–5000.
- `EnemyShips` (`EnemyShipSpawnSystem`): initial delay 9 s, interval ramp 5–10 → 2–4 s over 180 s, cap 100, min spawn distance 300, follow factor 0.5, reference speed 100, max speed factor 3.
- `Mines` (`MineRespawnSystem`): initial delay 20 s, interval ramp 4–8 → 10–20 s over 180 s, cap 23.
- `Chase` (`EnemyShipSystem`): cull distance 5500, chase player-speed factor 0.75, max speed multiplier 2.

Pure functions (`NextInterval`, `SpeedFactor`, `EffectiveSpeed`) stay in their systems; only the numbers move.

Optional add-on: asteroid stats (`AsteroidFactory` radius ranges 35–50 / 125–250, angular velocity ±0.75) → `Stats/Asteroids.cs`, for full entity coverage. Low priority (background obstacles).

## What stays where it is

- Physics/collision mechanics in `CollisionSystem` (restitutions, masses): system mechanics, not stats.
- Targeting mechanics: `TurretFiringSystem.TargetedRangeMultiplier = 3`, `PrimaryTargetPicker` click multipliers.
- Visual/effect tuning: `DamageEffectSystem` smoke/spark rates, `ShipDeathExplosionSystem` timing — presentation, leave with the systems.
- Camera drift parameters (`CameraSystem`).
- Component default parameter values (e.g. `HealthOrb(Lifetime = 30f)`): part of component shape; every production spawn site passes explicit values anyway. Removing vestigial defaults is an optional cleanup, not required here.

## Verification

- `dotnet build` + `dotnet test` after each commit (existing tests already cover tier stats, speed scaling, spawn intervals, firing, pickups).
- Tests that follow moved code: `EnemyTierStatsTest`, `SpawnIntervalScalingTest`, `EnemySpeedScalingTest`, `TurretFiringTest`, `OffScreenSpawnTest`.
- Headless run per the recipe at the bottom of TROUBLE_SHOOTING.md; screenshot ship-select screen and Tab stats screen to confirm displayed values are unchanged.

## Docs updates (with the last commit)

- ARCHITECTURE.md: add `Stats/` to the project layout; note that `Components/` holds runtime components only.
- PLAN.md: one-line entry under the relevant section pointing at this plan.

## Alternatives considered

- **JSON/data files + loader**: easier for non-programmers, but adds a dependency and a loading path against an explicit code-first architecture choice; loses compile-time safety and IDE navigation. Rejected.
- **Single `Stats.cs` file**: ~300 lines mixing all entity kinds — harder to find the ship you're editing than one-file-per-kind. Rejected.
- **Keep definitions in `Components/`, only add a folder for new stats**: leaves the current mixing of runtime components and static data, which is exactly what this plan fixes. Rejected.

## Implementation notes (deviations from this plan)

- File names use the `…Stats` suffix (`PlayerShipStats.cs`, …) per user request; Phase 3 shipped as requested rather than being skipped.
- Mine respawn interval constants were renamed while moving: old `MinInterval/MaxInterval` (4/8 s) → `SpawningStats.Mines.StartMinInterval/StartMaxInterval`; the previously inline ramp targets 10/20 s are now named `LateMinInterval/LateMaxInterval`. Values unchanged.
- `PickRandomType` was rewritten from threshold checks (0.333 / 0.666) to a weighted cumulative walk over `EnemyShipType.All` using the new `SpawnWeight` field (all 1f). Result: exactly ⅓ each instead of 0.333/0.333/0.334, still one RNG draw per call — determinism preserved (`WorldRngTest` passes).
- Enemy ammo radius now lives on `EnemyShipStats.AmmoRadius`; `TurretFiringSystem.DefaultAmmoRadius` stays as the fallback for unknown player weapon names (as planned in item 3).
- Asteroid stats were not added (optional add-on, out of scope).
