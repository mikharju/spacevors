# Turret layouts — implementation plan

Status: shipped (verified 2026-10-31)

Implements PLAN.md "Turret layouts". Plan only: no code changed here.

Confirmed decisions:
- Slot counts: **Scout 3 (1 filled, 2 empty), Fighter 4 (2 filled, 2 empty), Shadow 3 (1 filled, 2 empty), Heavy 7 (4 filled, 3 empty)**.
- Slot size: **Large slots accept any weapon; Small slots accept only Small weapons.**

## What changes conceptually

1. A **turret slot** becomes a first-class thing in the world: a named hull position with a size, a hull-relative direction and an arc of fire. Today that geometry is welded to the weapon (`TurretDefinition` / `AddOnMount`).
2. A weapon gets a **size** and a **turn speed**, so a turret physically slews inside its arc instead of snapping to the target. The slew is **relative to the hull**, so a ship that turns faster than a heavy gun's rating drags that gun off its target — flying badly has a cost. A deadband lets a turret that is already close enough stop correcting and just shoot. This is the only simulation change.
3. A new weapon from a level-5 card is **mounted into a slot the player picks** (large ship graphic + arcs), or **replaces** an existing weapon when no slot of that size is free.

## Data model

### New (Domain/Stats/TurretLayout.cs — new file)

```csharp
public enum TurretSize { Small, Large }

// Hull-local geometry. Offset in px, +x = ship's right, +y = behind the nose (nose is -y).
// Angles in radians, hull-relative: 0 = straight ahead, + = toward the ship's right.
public readonly record struct TurretSlotDefinition(
    string Name,            // "main_gun", "small_left", "small_right", "small_back"
    TurretSize Size,
    Vector2 Offset,
    float ArcCenter,        // direction: where the turret parks when centred
    float ArcAngle);        // total span the turret may traverse
```

Units: keep radians in code (as everywhere else — `EngineLayout.TurnRate`, `Turret.ArcAngle` are rad); degrees stay in PLAN.md and UI text (`StatsScreenRenderer.FormatDegPerSec` already exists).

### New component (Domain/Components/CombatComponents.cs)

```csharp
// One turret mount point on the player hull. Filled slot = this component + Turret.
public readonly record struct TurretSlot(TurretSlotDefinition Definition);

// A player turret's aim state: yaw relative to the hull, so the hull carries it for free.
// The mount's absolute facing is always hull + Yaw, written into Rotation on the same tick.
public readonly record struct TurretAim(float Yaw, bool Parked);
```

One entity per slot, created by `WorldFactory`. Empty slot = `TurretSlot` only; mounted slot = `TurretSlot` + `Turret` + `TurretAim` + `Rotation` + `FireCooldown`. Free/occupied slot logic is then a plain domain query (`TurretSlot` without `Turret`) — no counters to keep in sync.

`Rotation` on a player turret stays as **derived** state (absolute facing = hull + yaw), written by `TurretFiringSystem` whenever the yaw moves: the renderer's turret marker, the ammo spawn direction and the arc cull all keep reading it, and enemy turrets — which have no `TurretAim` and are aimed by their hull — are untouched.

### Deleted

- `AddOnMount`, `TurretDefinition` (Stats/WeaponStats.cs) — geometry moved to the slot, `Range`/`AutoTarget`/mount identity moved to the weapon.
- `TurretOffset`, `ArcOffset` components — both are now fields of `TurretSlot.Definition`.
- `WeaponSlots(Used, Max)` component — replaced by "slots of this ship minus slots holding a `Turret`". Touchpoints: `WorldFactory:32-33`, `LevelUpSystem:75,179-186`, `GameSession.AddNewWeaponTurret:689-714`, `StatsScreenRenderer:198-199`, `Tests/LevelUpScriptTest.cs:154`.
- **`WeaponType.TwinChainGun` and `WeaponLoadout.TwinChainGun`** — removed rather than re-slotted. Its whole identity was its own 22.5° forward arc and a fixed twin mount, and under the new rules the arc belongs to the slot, so it would degrade into "a fast small chain gun in some slot" with nothing distinguishing it. Touchpoints: `Stats/WeaponStats.cs:39,57,96-98`, `Tests/TurretFiringTest.cs:104-106` (three `InlineData` rows — replace them with a weapon that exists, e.g. AcidBubbleSpray, keeping the arc-cull coverage), `plans/SHIP_TYPES.md` (stale, note it there).

### Extended

`WeaponStats` (runtime, upgradeable) gains:
```csharp
float TurnSpeed;   // rad/s relative to the hull: how fast the mount slews inside its arc
```

`WeaponType` (static) gains, and loses `AddOn`:
```csharp
TurretSize Size,
float Range,           // base mount range (was per-loadout / per-AddOn)
bool AutoTarget = true // was on AddOnMount / TurretDefinition
```
New offer pool: `WeaponType.NewWeaponPool` replaces `AddOnWeapons` (`AddOn != null` no longer marks "mountable"). Keep `LoadTestWeapon` out of it.

`WeaponLoadout` becomes slot-keyed:
```csharp
public readonly record struct WeaponMount(string SlotName, WeaponType Weapon);
public readonly record struct WeaponLoadout(string Name, IReadOnlyList<WeaponMount> Mounts);
```
`ShipType` gains `IReadOnlyList<TurretSlotDefinition> Slots` and drops `MaxWeaponSlots`.

`Turret` keeps `(Weapon, WeaponName, ArcAngle, Range, AutoTarget, IsEnemy)`; `ArcAngle`/`Range`/`AutoTarget` are copied from slot + weapon at mount time, exactly as today. `Rotation` on a turret entity now means **the turret's actual current aim**, not the arc centre.

## Proposed slot layouts (initial values)

Offsets in px (hull-local), arcs in degrees (radians in code). Arc spans start close to today's per-weapon arcs; the side slots are the one deliberate widening (45° → 120°) so small weapons are worth mounting.

### Scout (radius 46) — 3 slots, 2 empty
| name | size | offset | direction | arc | starting weapon |
|---|---|---|---|---|---|
| `main_gun` | Large | (0, −10) | 0° | 60° | Cyclotron |
| `small_left` | Small | (−14, 6) | −90° | 120° | — |
| `small_right` | Small | (14, 6) | 90° | 120° | — |

### Fighter (radius 58) — 4 slots, 2 empty
| name | size | offset | direction | arc | starting weapon |
|---|---|---|---|---|---|
| `main_gun` | Large | (0, −14) | 0° | 60° | LaserLance |
| `small_left` | Small | (−18, −2) | 0° | 45° | Cyclotron |
| `small_right` | Small | (18, −2) | 0° | 45° | — |
| `small_back` | Small | (0, 26) | 180° | 270° | — |

### Shadow (radius 58) — 3 slots, 2 empty (also the hull for `DiagnosticShips`)
| name | size | offset | direction | arc | starting weapon |
|---|---|---|---|---|---|
| `main_gun` | Large | (0, −14) | 0° | 60° | GravitonBurst |
| `small_left` | Small | (−18, 6) | −90° | 120° | — |
| `small_right` | Small | (18, 6) | 90° | 120° | — |

### Heavy (radius 84) — 7 slots, 3 empty
| name | size | offset | direction | arc | starting weapon |
|---|---|---|---|---|---|
| `main_gun` | Large | (0, −22) | 0° | 60° | MachineGun |
| `heavy_left` | Large | (−30, 8) | −60° | 120° | — |
| `heavy_right` | Large | (30, 8) | 60° | 120° | — |
| `small_left` | Small | (−18, −6) | −90° | 120° | Shotgun |
| `small_right` | Small | (18, −6) | 90° | 120° | Shotgun |
| `small_back` | Small | (0, 34) | 180° | 270° | FlakCurtain |
| `small_nose` | Small | (0, −38) | 0° | 60° | — |

`small_back` at 270° keeps the PointDefenceTurret identity (arc now belongs to the slot, so a wide rear slot must exist on every ship that should be able to mount one — Scout/Shadow lack it; add it if playtests want PD on small ships).

## Proposed weapon stats (initial values)

**Turn speed is hull-relative** — the rate the mount turns inside its arc, as on a real spec sheet. Its absolute rate is `hullRate + turnSpeed`, which is what makes "the hull out-turns the turret" fall out for free (see Simulation below). The design line is therefore the ship's own turn rate: Balanced 1.33 rad/s (76 °/s), Pursuit 1.20 (69 °/s), Slow/Heavy 0.67 (38 °/s).

- **Above the hull rate** → the turret holds its target through normal mouse-aimed flying; only a hard spin breaks it. This is the default for cheap/quick weapons.
- **Below the hull rate** → spinning drags the gun off its target at `hullRate − turnSpeed` °/s and it cannot catch up until you stop turning. This is the deliberate cost of the heavy guns.

| weapon | size | turn speed (°/s) | turn speed (rad/s) | holds target while flying? | base range px | auto target |
|---|---|---|---|---|---|---|
| MachineGun | Large | 180 | 3.14 | yes | 1260 | yes |
| Shotgun | Small | 140 | 2.44 | yes | 860 | yes |
| Cyclotron | Small | 300 | 5.24 | yes | 1360 | yes |
| AcidBubbleSpray | Small | 140 | 2.44 | yes | 650 | no |
| PointDefenceTurret | Small | 220 | 3.84 | yes | 680 | yes |
| RippleCutter | Small | 200 | 3.49 | yes | 260 | yes |
| FlakCurtain | Small | 300 | 5.24 | yes | 220 | yes |
| LaserLance | Large | 90 | 1.57 | barely (14 °/s spare on a Balanced hull) | 1400 | yes |
| GravitonBurst | Large | 80 | 1.40 | no (loses the target at ~7 °/s of drift) | 1600 | yes |
| PrismLance | Large | 70 | 1.22 | no | 1200 | yes |
| RailGun | Large | 55 | 0.96 | no — a full-rate 1 s turn loses it by 21° | 1200 | no |
| LoadTestWeapon | Large | effectively instant (100000) | — | yes | 580 | yes |

Notes:
- RailGun/PrismLance/GravitonBurst are the "don't spin while you shoot" guns; they read best on Heavy (38 °/s hull, so 55 °/s still holds). On a Pursuit hull the RailGun becomes a burst weapon you fire in straight flight.
- RailGun range: today the starting loadout says 2000 px and the level-up mount says 500 px. With one per-weapon value it becomes **1200 px**; a deliberate compromise, retune by playtest.
- `LoadTestWeapon` must slew instantly so perf-scenario seeded volleys keep looking like fired ones (scenarios strip the `Turret` component anyway — `ScenarioWorld.SilencePlayerTurrets` — but keep it cheap).
- Marker radii are render data (Game layer, `RenderHelpers`): small slot 6 px, large slot 9 px.

### Aim tolerances (named constants in `TurretFiringSystem`)

A turret does not need to point exactly at the target, and once it is "close enough" it should stop turning instead of micro-correcting every tick:

| constant | value | meaning |
|---|---|---|
| `AimAcquireTolerance` | 3° (0.052 rad) | while **slewing**, keep turning until the aim error is under this → the mount parks nearly on the solution |
| `FireAimTolerance` | 8° (0.14 rad) | while **parked**, this is both the release threshold (resume slewing past it) and the firing gate (fire at or under it) |

Two thresholds, one hysteresis band (3° in / 8° out) so a parked turret neither limit-cycles around the boundary nor re-fires a correction every tick. Consequences worth having:
- **A parked turret issues zero commands** — the deadband removes the per-tick `Rotation`/`TurretAim` allocation for an aimed gun, which is the common state.
- 8° is far below every slot arc (45–270°) and below the scatter of the wide weapons, so it never gates anything that fires today except while genuinely mid-slew.
- The band is a deliberate first knob to make per-weapon later (one field on `WeaponStats`) if the flak-type weapons want a wider "point roughly" allowance; not in v1.

## Simulation: turret slew (`TurretFiringSystem`)

Single place, no new system, one target search per turret per tick as today.

**Frame of reference.** The mount is bolted to the hull, so the stored state is yaw *relative to the hull*; the hull carries it for free. That is what makes a fast ship out-turn a slow gun: to hold a fixed world bearing while the hull turns at H, the yaw must change at H — a mount rated below H simply cannot, and its aim error grows at `H − turnSpeed` until the fire gate closes.

Per player (non-`IsEnemy`) turret each tick:
1. Read the turret's `TurretAim` (hull-relative yaw, arc-window-relative) and the player hull `Rotation` (hoisted out of the loop, one read per tick). Absolute aim = `hull + yaw`. Enemy turrets keep their current instant hull-aimed behaviour (`IsEnemy` branch untouched).
2. Eligibility search: unchanged — the existing arc cull uses the arc **centre** (`cosHalfArc`), so target-finding semantics stay identical. The target must be inside the slot's arc window to be selectable at all, exactly as today.
3. Desired yaw = `Normalize(aimAngle(solution) − hull)`, then clamped into `[ArcCenter − ArcAngle/2, ArcCenter + ArcAngle/2]`. If the turret is already outside its window (the hull turned and dragged it out), the clamp wins over target tracking: slew to the nearest window edge first.
4. Deadband (see tolerances above): if parked and error ≤ `FireAimTolerance`, do nothing at all; if parked and error > `FireAimTolerance`, or slewing and error > `AimAcquireTolerance`, slew by at most `TurnSpeed · dt` (`RotateToward`, shortest path); when the error drops to `AimAcquireTolerance`, park.
5. Fire: unchanged cooldown gate **plus** `AngleDiff(absoluteAim, solution.AimDirection) <= FireAimTolerance` — **and nothing else**. The gate is purely an aim-error test: no hull angular velocity, no "must be settled" condition, no parked-only rule. A turret that is mid-slew fires the instant its sweep crosses within 8° of the solution, which is what makes a slow gun still contribute during a spin — it just cannot *hold* the orientation, so it fires in bursts as the arc sweeps through a target instead of steadily.
   - Sampling is not a risk: the fastest possible crossing of the ±8° band is `hullRate + turnSpeed` ≈ 130 °/s, i.e. ~0.12 s inside the band, ~14 ticks at the 120 Hz fixed step. No continuous-time or sub-step handling needed.
6. Write `TurretAim` and `Rotation` (absolute, derived) only when the yaw changed — a parked turret emits no commands.
7. **No valid target: slew back to the arc centre** at `TurnSpeed`, park when within `AimAcquireTolerance` of it. So an idle gun walks to the middle of its green sector and holds there, carried by the hull — it re-reads as "ready", and it is where a new target is most likely to be acquired from. This also self-heals a mount the hull dragged outside its window.
8. `ArcAngle >= 2π` slots (Flak/Ripple): the window is unbounded, normalize yaw to ±π and skip the clamp — those mounts are free to point anywhere, but still obey their turn speed, so a target 180° behind takes real time to swing onto. Rest-to-centre is a no-op there (every direction is the centre).

Helpers stay local and tiny: `RotateToward`, `AngleDiff`, `NormalizeAngle`, `ClampToArc` — four pure static methods in `TurretFiringSystem` (or a small `AngleMath.cs` if they read better separate).

Target choice keeps today's nearest-in-arc rule with **no hysteresis** (accepted): the deadband plus rest-to-centre means a wobbling mount mostly sits still, and the fix if playtest shows visible thrash is to store the current target `Entity` in `TurretAim` and prefer it while it stays in the arc and in range.

`GameSession.SyncTurrets:267-283` keeps writing **Position** from the slot offset (pre-phase, still app layer) and **stops writing Rotation** — the aim is now simulated. `WorldFactory:99-107` creates slot entities and initial `Rotation = arc centre`, `TurretAim = ArcCenter` (parked, ready to fire, so a fresh ship behaves exactly as today).

## Mounting: domain API (`Domain/Progression/WeaponMount.cs`, new)

Keeps the rules testable without graphics (GameSession only drives the UI):
```csharp
static bool Fits(TurretSlotDefinition slot, WeaponType weapon);      // Small fits both, Large only Large
static Entity? FirstFreeSlot(EntityManager em, WeaponType weapon);
static int CountFreeSlots(EntityManager em, TurretSize size);
static bool Mount(EntityManager em, Entity slotEntity, WeaponType weapon);   // adds Turret/Rotation/FireCooldown
static void Unmount(EntityManager em, Entity slotEntity);                   // removes Turret/Rotation/FireCooldown
```
`GameSession.AddNewWeaponTurret` becomes a call to `Mount`/`Unmount` on a chosen slot entity. Replacing = `Unmount` + `Mount` in the same slot; the removed weapon's upgrade levels (`UpgradeCounts`, keyed by weapon name) survive — a replacement back to the same type restores them, a different type keeps a stale count that shows in the stats screen. Accept that in v1, note it in PLAN.md.

## Level-up offers (`LevelUpSystem`)

"Most weapons offered must fit a free slot; a minority may be of a size whose slots are all full."

At a milestone level (every 5th):
1. `free = slots without Turret`, split by size (a Small weapon counts as fitting any free slot, Large only free Large slots).
2. New-weapon cards: draw from `NewWeaponPool`, excluding weapon types already mounted (unchanged). Prefer fitting sizes; **at most 1** of the 3 new-weapon cards may be of a size with no free slot (that card is a *replace* offer).
3. Hand = 1 Hp + up to 3 new-weapon cards + existing-weapon damage cards, filled to 5.
4. Order: single Fisher-Yates `Shuffle(allOptions, view.Rng)` as today — deterministic, same RNG stream, `WorldRngTest` unaffected.
5. `UpgradableOption` gains nothing: `IsNewWeapon` already marks the new-weapon card; the card label adds "replaces a weapon" when the offered size has no free slot.

If zero free slots and no replace card was drawn, behaviour stays as today (no new-weapon cards).

## UI: the two-step upgrade (Game layer)

`GameSession` grows a paused-UI step machine — world is paused, so this is app state, not simulation state:
```
enum UpgradeStep { Cards, PickSlot, ReplaceSlot }
```
- `Cards`: today's five cards. On a new-weapon card: if a fitting free slot exists → `PickSlot`; otherwise → `ReplaceSlot`.
- `PickSlot`: click a free slot circle, or press its number key. Right-click = back to the cards.
- `ReplaceSlot`: click an occupied turret, or press its number key.
- Scenario mode never pauses: it takes card 0 and `FirstFreeSlot` (so `ScenarioBench` and the perf baselines keep working).

New `src/Game/MountMenuRenderer.cs` (one responsibility, keeps `UpgradeMenuRenderer` untouched):
- Ship graphic large and unrotated, reuse the `ShipSelectScreen:190` pattern (`ShipSpriteRenderer.DrawShipSprite(ship, cx, cy, radius*2*zoom, 0f)`), placed above the card row.
- **Empty slots**: filled circle, greyish blue, radius per size.
- **Arc of fire**: green circle sector from the slot centre (`Raylib.DrawCircleSector` + faint edge), spanning the slot arc, hull-aligned.
- **Mounted slots**: greyish red triangle pointing along the turret's current `Rotation`.
- Hover: ring in `Palette.Accent` + number badge (reuse `UpgradeMenuRenderer` badge style); hint line under the graphic.
- Hit-testing in screen space: slot screen position = rotate offset by 0° (graphic is unrotated) + scale; forgiving radius = marker radius + 8 px, mirroring `PrimaryTargetPicker`'s forgiving click zones.

`Palette` additions: `TurretEmpty (90,120,150,200)`, `TurretMounted (170,90,90,230)`, `TurretArcFill (60,190,110,45)`, `TurretArcEdge (60,190,110,110)`.

In-game graphics (`WorldRenderer.DrawTurrets:253-264`): replace the 8 px `RenderHelpers.DrawTurretMarker` square with the same two shapes — triangle for a filled slot (points along `Rotation`, so the swing is visible), greyish-blue circle for an empty slot (position computed from the slot offset + player transform, no entity state needed). ≤7 markers per frame, so the shape-batching rule is not at stake, and the `perShapeNs` canary needs ≥100 shapes, so no baseline noise.

Stats screen (`StatsScreenRenderer.BuildStats`): "Weapons x/y" becomes `Turret slots  small 2/3 · large 1/1`; each weapon section adds `Firing arc  60° @ 0°` and `Turn speed  180°/s`.

## Stages (one commit each, project compiling at every step)

1. **Stats reshape**: `TurretSize`, `TurretSlotDefinition`, per-ship `Slots`, `WeaponType.Size/Range/AutoTarget`/`NewWeaponPool`, `WeaponStats.TurnSpeed`, slot-keyed `WeaponLoadout`; delete `AddOnMount`/`TurretDefinition`/`MaxWeaponSlots`/`TwinChainGun`. Compile + `ShipRosterTest`, `TurretFiringTest` fixes.
2. **Slot entities in the world**: `TurretSlot` component, `WorldFactory` creates one entity per slot, delete `TurretOffset`/`ArcOffset`/`WeaponSlots`, update `SyncTurrets`, `ScenarioWorld.SilencePlayerTurrets`, `StatsScreenRenderer`, tests. No behaviour change yet.
3. **Slew**: `TurretAim` component + hull-relative slew, arc-window clamp, the 3°/8° deadband and rest-to-centre in `TurretFiringSystem`; drop the `Rotation` write from `SyncTurrets`. New `TurretSlewTest`: slew rate, park-and-hold (no commands while parked), fire gate, **out-turn** (hull rotating faster than the mount's rating keeps the target selectable but throttles the shots), **sweep-fire** (the same spinning turret fires when its arc crosses a target within tolerance), **rest-to-centre** (target removed → yaw returns to `ArcCenter` and parks). Measure scenarios (see below).
4. **Mount API + offers**: `WeaponMount`, `LevelUpSystem` hand composition, `GameSession.ApplyUpgrade` mount path. New `SlotMountTest`, `LevelUpSlotOfferTest`.
5. **Mount UI + turret graphics**: `UpgradeStep` machine, `MountMenuRenderer`, `WorldRenderer.DrawTurrets`, `Palette`/`RenderHelpers` additions. Verify with screenshots.
6. **Stats screen + docs**: new rows, PLAN.md (controls: mount-screen keys, mark items done) and ARCHITECTURE.md (components list `+TurretSlot +TurretAim −TurretOffset −ArcOffset −WeaponSlots`; write patterns: turret `Rotation`/`TurretAim` are command-written by `TurretFiringSystem`, only `Position` stays a pre-phase app write; one entity per player turret slot).

## Verification

- `dotnet test` (serial suite, `Tests/AssemblySettings.cs`).
- Perf per AGENTS.md, after stages 2–3 (they touch sim) and stage 5 (render): `.tools/scenario.sh ammo-spray --sim --iters 3` and `.tools/scenario.sh impact-storm --sim --iters 3`, compare median/p99/`perShapeNs` against the previous row of `.tools/run/index.tsv`, state the verdict. Expect ~0 change (player turrets are stripped in scenarios); a change means something leaked into the hot path. For stage 5 also run `hull-damage` and inspect start/mid/end shots — the ship + markers must still be drawn.
- Visual check of the mount screen headless: `SPACEVORS_DIAGNOSTIC=1 SPACEVORS_DIAG_UPGRADES="RailGun,Shotgun" ` + forced level-up (`L`), and the F12/screenshot recipe in TROUBLE_SHOOTING.md "General workflow that works". Expect: RailGun (large) selectable only in Large slots, Shotgun (small) in both.
- Manual playtest: watch the triangle swing on a slow Large weapon (RailGun 55 °/s) while spinning the hull — it should fire in bursts as its sector sweeps across a target, not go silent, and settle back to steady fire within ~0.25 s of flying straight. Confirm a fast weapon (Cyclotron 300 °/s) never stops during the same manoeuvre, that an untargeted gun walks back to the middle of its green sector, and that no turret ever fires outside its sector.

## Decisions locked in

All previously open questions are settled — implement as written, tune by playtest:

- **All weapons follow the new rules.** The arc belongs to the slot; there is no per-weapon arc cap. **TwinChainGun is deleted** rather than re-slotted, because its only identity was its own 22.5° arc.
- **Buff/detrainer accepted as proposed**: side slots 45°→120°, RailGun range 2000→1200 px. Both are single-line knobs in one file.
- **No target-selection hysteresis** in v1; nearest-in-arc stays.
- **Firing gate stays a pure aim-error test**, with no hull-rate or settled condition — a slow gun that the spin sweeps onto a valid target fires at that moment; it just cannot hold the orientation.
- **Slow guns losing tracking under sustained spin is intended**, and it is a burst-fire pattern rather than a silencing.
- **Slot counts as tabled**, including Heavy's 7 slots out-reaching normal run lengths; the empty-slot graphics make that visible on purpose.
- **Idle turrets slew back to their arc centre** and park there.

## Remaining tuning knobs (not decisions)

- Turn speed per weapon is the one dial that changes how the mechanic *feels*: raise the three sub-hull-rate guns (RailGun 55, PrismLance 70, GravitonBurst 80 °/s) toward 100+ °/s to make the effect invisible, keep them low to make "don't spin while you shoot" a real play pattern.
- `AimAcquireTolerance` / `FireAimTolerance` (3°/8°): widen `FireAimTolerance` only if guns read as too eager to miss; per-weapon tolerances are the first fields to add to `WeaponStats` if flak-type weapons want a "point roughly" allowance.
- Slot arc spans per ship (`TurretLayout.cs`) — the whole point of putting arcs on slots is that these are retunable without touching weapons.
- If the pattern proves fun, an obvious later upgrade is per-weapon **turn speed** as an upgradeable stat, so the player can buy out of a sluggish heavy gun.
