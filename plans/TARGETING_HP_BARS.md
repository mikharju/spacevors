# TARGETING_HP_BARS.md

Plan for the PLAN.md section "Enemy hp bar visible on targeting graphic" (2026-09-14).

## Status: not started

## Requirements (verbatim from PLAN.md)

- Any ship or mine targeted by player turret automatically gets blue grey targeting bracket
- Auto targeting brackets persist for 1 second after turret has stopped targeting that ship or mine
- Dead ships have no targeting brackets
- Manual targeting bracket is bright red
- All targeting brackets show enemy hp bar above bracket
- Enemy hp bar is green when full, transitions to red when nearing dead

Out of scope: the separate "Graphical damage indicators" section (smoke/sparks) — different feature, plan separately.

## Current state (verified in code)

- `TargetingRenderer` draws corner brackets **only** for the manual `PrimaryTarget`, color `(255, 80, 80, 230)`; skips dead and off-screen targets; no hp bar anywhere.
- `TurretFiringSystem.FindTarget` runs **only when a turret's fire cooldown expires**. Its result is aim data (`AimDirection`, `PredictedPosition`, `Radius`) — the selected entity id is not carried, so nothing downstream knows *which* enemy a turret currently targets.
  - Both `AutoTarget = true/false` branches search enemy ships/mines (the flag toggles lead prediction only); manual primary takes priority at extended range; asteroids are also selectable as last resort.
- `Health(int Current)` has **no max** — an hp ratio cannot be computed. Enemy max HP exists only implicitly: `EnemyShipFactory.TieredHealth(type, tier)` at spawn time; mines are always 2 (`MineRespawnSystem`, `GameInitializer`).
- Death flow: `CollisionSystem` removes `Health`, adds `Dead` + `ShipDeathExplosion`; the ship entity is destroyed ~1 s later by `ShipDeathExplosionSystem`. Mines are destroyed immediately. So a dead ship lingers with `Position`/`EnemyShip`/`Dead` but no `Health`.
- `EntityManager.ElapsedTime` is public and frozen while paused (only advanced in `GameSession.StepSimulation`) — usable for time-based expiry without any new system.
- `WorldRenderer.Draw` calls `TargetingRenderer.Draw` after ships and mines, so brackets/bars already overlay both.

## Design decisions

1. **Auto-target state is a component on the target entity**: `AutoTargetMark(float LastTargetedAt)` (world seconds from `WorldView.ElapsedTime`). Written by `TurretFiringSystem` through the CommandBuffer — the default write pattern, deterministic, no new RNG sources (`WorldRngTest` stays green).
2. **FindTarget runs every tick for every turret**, not only at fire time. It is a pure function of current state, so firing behavior is bit-identical; the per-tick result is used both to refresh `AutoTargetMark` (player turrets only) and to fire when the cooldown allows. This keeps "targeted by turret" continuous for slow weapons (railgun fires every 1.5 s — mark-at-fire-time alone would flicker the bracket off between shots).
   - Cost: player turrets (≤4) each re-run their candidate search per tick (~200 candidates worst case); enemy-turret branch only looks up the single player entity, so it stays trivial. Measure before optimizing (see Stage 4 fallbacks).
3. **Expiry is renderer-side**: draw an auto bracket while `em.ElapsedTime - LastTargetedAt <= 1 s`. No cleanup system — stale marks are ignored and vanish with entity destruction (`DestroyEntity` removes all components). While paused, time is frozen so brackets persist across the upgrade menu (desirable).
4. **Manual takes priority**: while a live `PrimaryTarget` exists on the player, the auto bracket for that same entity is suppressed; the bright red manual bracket + bar draws instead. After clearing the lock, the blue-grey bracket appears immediately from the still-fresh mark — no visual gap.
5. **HP ratio needs a max → `Health(int Current, int Max)`** on all entities (uniform data model: health knows its own ceiling). Mechanical call-site update; player HUD keeps reading `Player.MaxHealth` as today, and the two sources stay in sync at spawn / Hp upgrade / diagnostic H key.
6. **Bar geometry**: width = bracket outer size (`2 × (radius + Margin)`), height 4 px, gap 6 px above the top corners of the bracket; dark background rect + filled portion. Fill color lerps green `(80,255,80)` at full → red `(255,60,60)` at empty — same palette as `HudRenderer`.
7. **Colors**: manual keeps its existing bright red (bump to `(255, 60, 60, 255)` only if screenshots show it not reading against the new blue-grey); auto bracket = blue-grey, proposed `(150, 170, 200, 230)`, tune via screenshot.

## Stages (one commit each)

### Stage 1 — `Health` carries max HP

- `GameplayComponents.cs`: `readonly record struct Health(int Current)` → `Health(int Current, int Max)`.
- Call sites:
  - `GameInitializer`: player `new Health(shipType.MaxHealth, shipType.MaxHealth)`; initial mines `(2, 2)`.
  - `EnemyShipFactory` (`AddComponents` + `CreateComponents`): `(TieredHealth(type, tier), TieredHealth(type, tier))`.
  - `MineRespawnSystem`: `(2, 2)`.
  - `CollisionSystem` (3 damage writes for mine/ship/player): preserve the existing max — read current `Health.Max` before writing `new Health(remaining, h.Max)`. The ship-death path removes `Health` entirely and is unchanged.
  - `PickupMagnetSystem` heal write: preserve `Max`.
  - `GameSession` diagnostic H key: `new Health(10000, 10000)` **and** set `Player.MaxHealth = 10000` so the HUD text stays consistent (currently shows "10000/8").
- Tests: update existing `new Health(x)` sites in `CollisionSystemTest.cs` / `PerformanceBenchmark.cs`; add one assertion that damage preserves `Max`.
- Commit: `health carries max hp`

### Stage 2 — `AutoTargetMark` + per-tick target selection (Domain)

- New component in `CombatComponents.cs`:
  ```csharp
  // Last world time a player turret selected this entity as its current target.
  public readonly record struct AutoTargetMark(float LastTargetedAt);
  ```
- `TurretFiringSystem.cs`: introduce a small result type so the selection carries its entity:
  ```csharp
  public readonly record struct TargetSelection(Entity Target, Vector2 AimDirection, Vector2 PredictedPosition, float Radius);
  ```
  `FindTarget` and its helpers return `TargetSelection?` (mechanical tuple→struct change inside this file; `FireAtTarget` takes the selection).
- Update loop: compute the selection once per turret per tick (all turrets — enemy branch is a single player lookup). Then, for non-enemy turrets with a live player: if the selected entity has `EnemyShip` or `EnemyMine`, queue `AddComponentCommand<AutoTargetMark>(target.Target, new AutoTargetMark(view.ElapsedTime))`. Firing stays exactly as today (only when cooldown ≤ 0).
- Asteroid selections are never marked; enemy turrets never mark. Player dead → no selection at all (existing early-out), so marks go stale and fade after game over.
- Tests (`TurretFiringTest.cs` or new `TurretTargetMarkTest.cs`, reusing the world-builder helpers from `TargetingTest.cs`):
  - ship in range/arc gets a mark with `LastTargetedAt == ElapsedTime`; refreshed on later ticks;
  - out of arc / out of range → no mark;
  - mine gets marked; asteroid does not;
  - enemy turret firing at player marks nothing;
  - dead target is never selected (existing behavior, regression guard).
- Commit: `auto-target marks`

### Stage 3 — TargetingRenderer: auto brackets + hp bars (Game)

- New draw pass over `<AutoTargetMark, Position>` entities: skip if `Dead`; skip if `em.ElapsedTime - LastTargetedAt > AutoBracketPersistence` (`const float = 1f`); skip the current manual target entity; off-screen cull with extent extended for the bar.
- Refactor shared helpers in `TargetingRenderer`:
  - `DrawBrackets(cx, cy, halfSize, color)` — existing geometry, parameterized color (manual red / auto blue-grey).
  - `DrawHpBar(em, target, cx, cy, halfSize)` — reads `Health` (skip if missing), ratio = clamp(Current/Max); background rect `(40, 40, 40, 180)`, fill width × ratio with manual RGB lerp green→red by `1 - ratio`; positioned above the top bracket corners (`cy - halfSize - BarGap - BarHeight`).
- Manual path: existing logic unchanged except it now also draws the bar; color as decided in Design #7.
- No changes to `WorldRenderer` draw order (brackets already overlay ships/mines).
- Commit: `targeting brackets + hp bars`

### Stage 4 — Verification, performance check, docs

- Headless screenshots per TROUBLE_SHOOTING.md "General workflow that works":
  - Ship: Fighter (key 2 — troubleshooting #21: Scout's LoadTestWeapon ring pollutes targeting tests); press H then T for a stable scene; N to spawn test enemies.
  - Verify: blue-grey brackets + green bars on auto-targeted ships/mines; bar shrinks and shifts toward red as the test enemy takes damage (use a lower-HP spawn or several seconds of fire); left-click → bright red bracket + bar on the clicked ship; click empty space → manual clears, blue-grey persists from the fresh mark then fades ~1 s after turrets stop selecting it.
  - Dead ships: no brackets during/after the death explosion (mark ignored via `Dead` check).
  - The exact 1 s fade is timing-sensitive in screenshots — assert it with a domain test instead (mark timestamp vs `em.ElapsedTime`).
- Performance: run `PerformanceBenchmark` and read `[FRAME]` per-system timings under max-enemy load; confirm the per-tick `FindTarget` cost for player turrets stays small. Fallbacks only if measured over budget: (a) mark at fire time + short persistence bump, or (b) cache last selection per turret with cheap re-validation — both change behavior subtly, so measure first (AGENTS.md: never optimize prematurely).
- Docs: rewrite the PLAN.md section as a done summary (style of "Mouse clicks to set primary target — done"); ARCHITECTURE.md component list gains `AutoTargetMark`, and `Health` is noted as `(Current, Max)`.

## Determinism & invariants

- No new RNG sources; all mark writes are deterministic functions of state + `ElapsedTime`; `WorldRngTest` must pass unchanged.
- Firing behavior is intentionally untouched — same `FindTarget` result at fire time as before, just computed more often.
- Entity ids are never reused (existing invariant), so a stale `AutoTargetMark` can only ever sit on a dead/destroyed entity — the `Dead` check plus destruction cleanup covers it.

## Open tuning items (decide via screenshots in Stage 4)

- Exact blue-grey RGB for auto brackets; whether manual red needs an alpha/brightness bump to stay distinct.
- Bar background opacity over bright sprites; optional 1 px outline if fill reads poorly on dark hulls.
- Small mines (radius 7.5 → ~35 px bar): keep proportional first; add a minimum bar width only if unreadable.
