# MISSILES.md — plan for guided missile weapons

Plan only: no code changed here. This is the first item under PLAN.md "Near future".

## Status: implemented (2026-10-17), 8 commits, 22 new tests

Everything below shipped as planned, in the commit order of §13. Where the code ended up different:

- **The rule numbers moved.** This plan drafted its rules as R9-R16, which collided with the slew-rate,
  hull-size, identity and colour rules already numbered R9-R14 in `plans/WEAPON_BALANCE.md`. They are now
  **R15-R22** (15 certainty, 16 turn radius, 17 round budget, 18 volley privilege, 19 damageable round,
  20 monotone ladder, 21 honest reach, 22 cap ladder) and this file has been renumbered to match. The
  tests live in `WeaponBalanceTest`, `MissileCollisionTest`, `EnemyMissileFiringTest` and
  `EnemyMissileLadderTest`.
- **`+1 missile` growth uses a named budget**, `WeaponStats.HomingRoundBudget = 4`, rather than restating
  R17's number at three call sites, and the card is withdrawn at the ceiling by the same mechanism that
  killed the `ProjectileSpeed`-on-a-beam card.
- **The large hull's top rung is ~80 %, not "~100 %"** of its spawns (4:1 over gun-only heavies at
  composition tier 10), so a bare heavy stays a rare sight instead of disappearing from the roster.
- **Enemy rack launches skip the gun pass's ±15 % speed jitter.** On a guided round the speed *is* the
  turn radius and the fuse reach, so a jittered round would be a different missile than the one the player
  just read. `AmmoSpawnOffset`, hull clearance, inherited velocity and the arc gate (`FiringArc`) are
  shared with the gun pass instead of copied.
- **Not done from §12/§13:** a `missile-barrage` perf scenario. The pinned scenarios run unarmed, so they
  prove no regression (ammo-spray sim median 1.31 ms, impact-storm 2.11 ms) and nothing about the cost of
  live missiles. That is the open measurement.
- **Accepted edge from §11 still open:** `AmmoSpawnOffset = 20` means a missile launched beside an adjacent
  hull detonates on its first sweep. An arming distance is the fix if it ever reads as unfair.

Decisions already fixed (by the request):
- **Same mounts as turrets.** A missile gun is just a `WeaponType` with a `Size`, mounted into an existing hull slot through `TurretMount`. No new mount concept, no new slot kind.
- **Enemy missiles arrive as a difficulty ladder on the existing hulls**: gun-only first, then a few missile-only variants, then an even mix, and later the large hull carries guns *and* missiles. Slow enemy missiles only; fast missiles stay player-only, enforced by ceilings.
- **Rounds collide across factions.** Enemy bullets *and* missiles shoot down player missiles; player bullets *and* missiles shoot down enemy missiles. Same faction never collides — the player's own missiles still never touch the player, its mounts, or each other.
- **Missiles have health** (1–3 hp, big missiles more) and **missile hp is an upgrade card**.
- **`FireRate` cards do not all mean "+1 missile".** Growth is per weapon; only a genuine swarm gun grows its salvo.
- **Enemy missiles launch from farther than enemy guns fire.**
- **The in-flight enemy missile cap escalates slowly: 3 → 5 at tier 10 → 7 at tier 20**, because other weapons are doing point defence against them.
- **Missile graphics: tip triangle + shaft square + exhaust triangle**, the exhaust growing bigger and brighter while the missile works hard, never fully invisible. A missile mount draws a **square rack that does not turn** to face its target.

## 1. What a missile is here, in one paragraph

A missile is a bullet that carries a **lock** (an entity id), **steers toward it at a limited turn rate**, flies **slowly**, detonates on the **first hull it touches** and dies when its **fuse** (the existing ammo lifetime) runs out.

Everything else is already the bullet path: launch arc, mount slew, target priority, kickback, damage, lifetime, streak rendering. The plan therefore adds **one component and one guidance system**, one branch in the collision pass (so the other side can shoot a missile down), and two small components plus one firing pass on the enemy side.

## 2. Reuse map — what already does the job

| Concern | Today | Missile |
|---|---|---|
| Choosing what to shoot, arc, range, 3× clicked-target reach | `TurretFiringSystem.FindTarget` | unchanged; a homing gun uses the **no-prediction** scan (a missile steers, it does not need a lead solution) |
| Mount slew, arcs, cooldown, salvo spread, speed roll | `TurretFiringSystem` / `TurretMount` | unchanged |
| Spawning the round | `commands.AddEntity(Position, Velocity, Ammo)` | same, plus `Missile` |
| Damage on contact (ships, mines, asteroids), per-frame health accumulation, consume-on-hit | `CollisionSystem` ammo pass | unchanged, plus a cross-faction round-vs-missile branch (§5) |
| Fuse / expiry | `AmmoLifetimeSystem` | unchanged |
| Being damaged itself | `Health` + `_frameRemainingHealth` in `CollisionSystem` | reused: a missile carries `Health` and is destroyed by opposing-faction rounds (§5) |
| Rendering | `WorldRenderer.DrawAmmo` streak, `ShapeSprites` generated-disc pattern | a new pass drawing two generated images per missile: body and exhaust (§10) |
| Upgrade cards | `GameSession.ApplyUpgrade` | `Range`/`Damage`/`ProjectileSpeed` unchanged; **`FireRate` becomes per-weapon for missiles**, and a new `MissileHealth` card arrives (§7) |
| Fitting into a hull slot, cargo, mount screen | `TurretMount`, `WeaponCargo` | unchanged; only the drawn marker differs for a missile rack (§10) |
| Enemy hull stats, chase AI, tier drift, spawn weights | `EnemyShipStats`, `EnemyShipSystem`, `EnemyShipSpawnSystem` | unchanged — the ladder rides a *composition* tier of its own, so no hull stat moves (§9) |

New machinery: `Missile` component + `MissileGuidanceSystem`, a cross-faction round-vs-missile branch in `CollisionSystem`, and on the enemy side two small components + `EnemyMissileFiringSystem` (§9).

## 3. Data model

### New component (`Domain/Components/CombatComponents.cs`)

```csharp
// A locked round: the entity it is chasing, the speed it was launched at, how hard it can turn (rad/s),
// and how hard it turned last tick (0..1) — the renderer draws the exhaust from that.
// Self-contained — the round carries its own numbers, so nothing upstream has to be looked up per tick.
public readonly record struct Missile(Entity LockedTarget, float Speed, float TurnRate, float Steering);
```

A missile also carries **`Health(Current, Max)`** — the existing component, because "carrying it is what makes an entity damageable" is already the rule that decides what can be shot at (§5).

### Extended (`Domain/Stats/WeaponStats.cs`)

```csharp
bool Homing = false,          // fires guided rounds instead of dumb ammo
float HomingTurnRate = 0f,    // rad/s steering limit; the round's turn radius is AmmoSpeed / HomingTurnRate
int MissileHealth = 1,        // hp the launched round carries; upgraded by the MissileHealth card
bool SalvoGrows = true;       // a FireRate card adds a round (true) or speeds the gun up (false)
```

This follows the existing `Hitscan` / `BeamSpacing` pattern: the weapon table decides what kind of round leaves the muzzle, the round carries the flight data. `SalvoGrows` defaults to **true**, which is exactly today's behaviour (`ApplyUpgrade` adds a pellet whenever `PelletCount > 1`), so no existing gun changes and only the deliberate swarm weapons opt out.

### `TurretFiringSystem` changes (three small ones)

1. A `Homing` weapon selects with the **static / no-prediction** path (already exists — `EvaluateStaticTarget` and `FindTargetWithoutPrediction`), so `TargetSelection.Led` is false and the per-round intercept re-solve is skipped: steering replaces lead. This covers the player's guns only — the enemy missile is fired by its own small pass, not by this loop (§9).
2. When `turret.Weapon.Homing`, the fired entity also gets `Missile(selection.Target, ammoSpeed, turret.Weapon.HomingTurnRate, Steering: 0f)` and `Health(weapon.MissileHealth, weapon.MissileHealth)`.
3. Multi-missile salvos reuse `PelletCount` + `Scatter` (so a 3-missile volley fans out and does not spawn inside itself). All missiles of one volley share the lock in v1 (open question Q3).

Entity ids are never recycled (`EntityManager.CreateEntity` is `_nextId++`), so a lock can never alias a freshly spawned entity: a dead target is simply a component lookup that fails.

## 4. New system: `MissileGuidanceSystem` (Intent phase, first)

```csharp
public class MissileGuidanceSystem : GameSystem
{
    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        foreach (var (entity, missile, pos, vel, ammo) in view.GetEntitiesWithComponents<Missile, Position, Velocity, Ammo>())
        {
            float speed = vel.Value.Magnitude;
            if (speed < Epsilon) continue;

            Vector2 toTarget = view.TryGetComponent<Position>(missile.LockedTarget, out var targetPos)
                ? targetPos.Value - pos.Value
                : vel.Value;                                   // lock gone: fly straight until the fuse ends it

            float heading = Angle.Of(vel.Value);
            float maxTurn = missile.TurnRate * deltaTime;
            float steered = Angle.Normalize(Angle.RotateToward(heading, Angle.Of(toTarget), maxTurn));
            // 1 = swinging as hard as it can, 0 = already dead on the lock. The exhaust is drawn from this.
            float steering = maxTurn > 0f ? MathF.Abs(Angle.Delta(heading, steered)) / maxTurn : 0f;
            if (steered == heading && missile.Steering == steering) continue;   // the common case: nothing to write

            var newVel = new Vector2(MathF.Sin(steered), -(float)MathF.Cos(steered)) * speed;
            commands.Add(new AddComponentCommand<Velocity>(entity, new Velocity(newVel)));
            commands.Add(new AddComponentCommand<Ammo>(entity, ammo with { Velocity = newVel }));
            commands.Add(new AddComponentCommand<Missile>(entity, missile with { Steering = steering }));
        }
    }
}
```

Notes that matter:

- **Phase: Intent, listed first.** Intent is where systems write next-tick motion, and `PositionIntegrationSystem` moves the round with that velocity on the next tick. The swept-capsule pass in `CollisionSystem` then reads exactly the velocity that produced the movement it is sweeping — writing guidance any earlier (Action) would turn the heading before the round has moved and desynchronise the sweep.
- **Dual write invariant.** The `Velocity` component drives movement; `Ammo.Velocity` drives the collision sweep *and* the render streak. Guidance writes both. Missiles still never get `Acceleration`, so the "ammo velocity is constant between ticks" assumption the sweep relies on stays true. (The alternative — dropping `Ammo.Velocity` and reading the `Velocity` component in the sweep — is one extra lookup per round in the hottest loop: not worth it.)
- **Constant speed.** Guidance rotates the heading and keeps `|v|`; the launch speed (weapon speed × the ±15 % roll × any `ProjectileSpeed` upgrades) is the only speed source. This is what keeps `AmmoSpeed` upgrades and the R6 reach rule honest.
- **Cost.** No spatial query, no target search: the lock is an entity id, so it is three O(1) component probes per live missile, and the common case writes nothing. Realistic worst case is a couple of dozen live missiles (§11).
- **Determinism.** No RNG. `Tests/WorldRngTest.cs` stays green.

## 5. Round vs round: same faction never, opposing faction always

Two rules the request asks for, with one mechanism between them:

| Rule | Where it comes from |
|---|---|
| The player's own missiles never touch the player, its mounts, or each other | true today; pinned by tests, no code |
| Enemy **bullets and missiles** destroy player missiles; player **bullets and missiles** destroy enemy missiles | a new cross-faction round-vs-missile branch in `CollisionSystem` |

### Why the first half is already true

| Case | Why |
|---|---|
| Player missile hits the player hull | `CollisionSystem` damages the player only inside the `if (!ammo.IsEnemy) continue;` branch — friendly ammo has no player-hit path at all |
| Player missile hits its own turret mount or the hull entity | the spatial grid holds only `Asteroid` / `EnemyMine` / `EnemyShip`; the player and its mounts are never inserted |
| Player missile hits another player missile | the new pass is cross-faction only |
| Enemy missile hits another enemy | enemy rounds only ever engage the player (the same rule enemy beams already follow) |

One accepted edge: the muzzle sits 20 px out (`AmmoSpawnOffset`), so a missile fired while an enemy hull is already hugging the mount detonates on that hull immediately. Correct enough to keep; if it reads unfair, the fix belongs in guidance (an arming distance), not in collision (Q4).

### The cross-fire pass

- **A new grid kind: `SpatialGrid.CollisionKind.Missile`.** Every missile inserts itself with its own radius — small missile 4 px, large 6 px. A bullet has to be able to hit it, and a 1.5 px Cyclotron slug needs a target worth aiming at.
- **One branch added to the existing ammo sweep**: a round may damage a missile **only when `round.IsEnemy != missile.IsEnemy`**. The probing round is consumed exactly as a hull hit consumes it; the missile loses `Health` exactly as a ship does, through the same `_frameRemainingHealth` accumulator. Missile-vs-missile is the same branch seen from both sides, so a head-on pass is a mutual kill.
- **Beams get the same branch** in the hitscan first-hit pass, so a `LaserLance` or a `RippleCutter` blade cuts a missile out of the air with no travel-time business at all.
- **No bullet-vs-bullet pass, and no same-faction pass — deliberately twice over.** The request asks for missiles to be contestable, not for a shrapnel screen; and bullet-vs-bullet would add per-round pair work to the hottest loop in the game (`ammo-spray`: 5k rounds) for no readable benefit.
- **The missile target is its current disc, not a swept capsule.** At `FixedDeltaTime = 1/120` a 200 px/s missile advances 1.7 px per tick while the *probing* bullet's own sweep is already exact, so the error is under two px and every swept-capsule calculation stays where it is.

### Missiles have health

- **Reuse `Health(Current, Max)`.** "Carrying `Health` is what makes an entity damageable" is already the project's rule, and the per-frame arithmetic already exists. A missile that runs out is destroyed with a small `Explosion` plus smoke; its warhead does not land (Q1).
- **Start values: 1 hp small, 3 hp large.** Enemy side: Interceptor missile 2, HeavyCannon missile 3.
- **A hit costs the probing round's own `Ammo.Damage`.** A `RailGun` slug (14) ends any missile in one shot while a `Cyclotron` stream (1) needs three on a torpedo — point defence is a real DPS tax, not a free kill, and that tax is what lets the enemy missile cap climb later (§9).
- A missile is damaged **only** by opposing-faction rounds: never impact, never a mine, never a rock it is not detonating on.

### The missile-health upgrade card

- New `UpgradeOption.MissileHealth`, `Additive: 1`, per weapon like `Damage`; stored as `WeaponStats.MissileHealth` and stamped on every launched round as `Health(max, max)`, so a card paid for mid-flight never has to touch a round already in the air.
- Offered only for homing weapons — the same exclusion that keeps `ProjectileSpeed` off hitscan guns, so no dead cards enter the hand.

This is the half of the design that makes the pricing honest: a guided round nobody can shoot down is a *guaranteed* hit, and R15 would have to price it at its ceiling. With shoot-down, the player gets a second thing to do when a lock lands — spend gun DPS on the round instead of the hull — and `FlakCurtain`'s 9 px disc at 600 px finally has a job it is uniquely good at (Q12).

## 6. What variants are possible — the catalogue

Axes, with the code each one costs:

| Axis | Variants | Cost in this codebase |
|---|---|---|
| **Seeker** | lock-on at fire (v1) · retarget to nearest when the lock dies · "seek nearest" with no lock at all · dumb-fire ballistic (no seeker) | retarget ≈ 3 lines in guidance; seek-nearest = a grid query per round (adds hot-path cost); dumb-fire = zero new code, it is just a slow bullet |
| **Steering law** | pure pursuit (v1) · proportional navigation · dive-from-the-side (fly a bearing, then turn in) | PN ≈ 10 lines and buys real intercepts on fast cross-courses; the dive pattern is a two-phase heading in guidance |
| **Warhead** | single hull hit (v1) · proximity fuse (detonate within N px) · splash/AoE on impact · cluster (splits into bullets at the fuse or on hit) | proximity ≈ 5 lines in guidance + a damage hand-off; splash/AoE = a new grid-circle damage pass in `CollisionSystem` (the real new machinery); cluster = spawn-on-expire hook in `AmmoLifetimeSystem` |
| **Salvo** | 1 · 2 · 3–4 per volley (=`PelletCount`) · and whether a `FireRate` card grows it | the growth choice is one `SalvoGrows` flag per weapon; the ceiling is the existing R17 budget (§7) |
| **Counterplay** | shoot it down (v1: bullets and beams, cross-faction only) · outrun it · out-turn it · kill the launcher · break the lock | shoot-down is one grid kind + one branch (§5); the other four are already free |
| **Speed profile** | constant (v1) · boost then cruise | a 2-line `speed(t)` in guidance; the exhaust already visualises it (§10) |
| **Supply** | unlimited (v1) · magazine + reload | new component + new UI, and it is a *resource* mechanic the rest of the game has none of — out of scope |
| **Target class** | hunts ships only · also mines and clicked rocks (v1: free, follows the existing target paths) · a *hunter* missile that seeks enemy rounds | the v1 round-vs-round pass makes enemy rounds killable, but a homing anti-round seeker is still a second target search per round — the reason `PointDefenceTurret` was renamed away in the balance pass stays true for a *seeker*, not for a gun |
| **Carry** | turret gun (v1) · mine that turns into a missile (PLAN.md's "New mines") | later, reuses `Missile` verbatim |
| **Enemy shape** | armament variants of the existing hulls, unlocked by tier (v1) · a dedicated missile species · salvo of two slow missiles · a hull that launches and runs | the armament ladder is one stats table + one firing pass (§9); a new species costs a hull, art, spawn weights and a second set of difficulty tests for no new behaviour |

**Recommended v1 = lock-on pure pursuit, single warhead, constant speed, unlimited supply, enemy missiles as armament variants of the existing hulls.** Everything else in the table is a follow-up card, not a v1 dependency.

## 7. Recommended v1 weapons and the numbers

Range bands are the ones fixed in `plans/WEAPON_BALANCE.md` §2 (aura 600 / close 900 / mid 1350).

| Gun | Size | Unlocks | Fire rate | Missiles/volley | Damage | Raw DPS | Launch speed | Fuse | Range | Turn rate | Round hp |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **SeekerPod** | Small | 5 | 1.2/s | 1 | 3 | 3.6 | 300 px/s | 4.0 s | 900 (close) | 150°/s | 1 |
| **HavocTorpedo** | Large | 10 | 0.7/s | 1 | 10 | 7.0 | 200 px/s | 8.0 s | 1350 (mid) | 45°/s | 3 |
| **Swarmlet** | Small | 5 | 0.8/s | 3 | 2 | 4.8 | 350 px/s | 2.5 s | 600 (aura) | 170°/s | 1 |

Why these three identities:

- **SeekerPod** is the answer to what shoots back at close range: a missile that steers needs no lead, so it works from a mount whose arc cannot hold a firing solution — the exact weakness the aim-lead pass documented for the Fighter's 45° side mounts. Priced low because it is the most certain delivery in the game.
- **HavocTorpedo** is the anti-heavy gun: a big round (10 damage, **3 hp** so a swarm of interceptor fire does not brush it aside) that punishes the slow, straight-flying `HeavyCannon`, and that a fast `Interceptor` simply outruns or out-turns (255 px turn radius vs the Interceptor's ~92 px). Poor `TurnSpeed` on the mount (slow slew) so the big slot costs twice.
- **Swarmlet** is the one that swarms: three cheap 1 hp rounds at aura range, and the **only** missile whose volley grows with cards. That is where the request's warning bites — see the growth rule below.

All three pass the existing table rules: R6 reach (`300 × 0.85 × 4.0 = 1020 ≥ 900`, `200 × 0.85 × 8.0 = 1360 ≥ 1350`, `350 × 0.85 × 2.5 = 744 ≥ 600`), R7 round budget (1.2 / 0.7 / 2.4 rounds/s per mount, against today's 12), R5 per-shot damage (3, 2, and the torpedo as the second "big round" exception alongside `RailGun`), and the new R15–R17 below.

### How each missile grows (the request's warning)

Today `ApplyUpgrade` treats a `FireRate` card as "+1 shot" for **any** gun that already fires several rounds, and multiplies the rate otherwise. Applied to missiles unchanged, four cards turn every missile gun into a swarm gun: the 1-missile SeekerPod becomes a 5-missile spray, which is a different weapon and a much bigger entity count. So:

- `WeaponStats.SalvoGrows` makes the choice **per weapon**, defaulting to today's behaviour. **SeekerPod and HavocTorpedo set it false** — their `FireRate` card is a real rate increase (×1.15), which is also what makes them feel like a gun being tuned rather than a spell being stacked.
- **Swarmlet keeps `SalvoGrows: true`**: +1 missile per card is its whole upgrade fantasy, and the card text already says "+1 shot".
- The growth **ceiling is an existing rule, not a new number**: a salvo may not grow past what R17 allows (`FireRate × PelletCount ≤ 4`), so Swarmlet stops at 5 per volley. `LevelUpSystem` then stops offering the card at the ceiling — the same shape as the hitscan exclusion that killed the `ProjectileSpeed`-on-a-beam dead card, so a full hand never fills with cards that do nothing.
- `Range` still multiplies the fuse (which is what makes a long-range missile physically reach), and `Damage`/`ProjectileSpeed` do what they say. None of them touch the salvo.

## 8. Balance rules to add (extend `plans/WEAPON_BALANCE.md` + `WeaponBalanceTest`)

- **R15 — certainty is the most expensive delivery.** A homing gun's raw DPS must be **≤ the weakest non-homing gun in its reach band (±15 %)**. Today: 600 band → RippleCutter 8 (Swarmlet 4.8 ✓); 900 band → InterceptorTurret 9 (SeekerPod 3.6 ✓); 1350 band → PrismLance 8.4 (HavocTorpedo 7.0 ✓). Homing is the *other* side of R1: a beam cannot miss on travel, a missile cannot miss on aim. Cross-fire (R19) is what keeps that ceiling from having to be lower still.
- **R16 — a missile must not be a guaranteed hit.** Its turn radius `AmmoSpeed / HomingTurnRate` must be **larger** than the turn radius of the fastest thing it hunts (a tier-10 Interceptor turns at ~92 px), so a target that flies well can still break the lock geometry. 114 px, 255 px and 118 px clear it.
- **R17 — missile salvo budget.** `FireRate × PelletCount ≤ 4` rounds/s for a homing gun (stricter than R7's 12), because a missile's fuse is 2.5–8 s and live rounds = rate × fuse. This is also the ceiling the salvo-growth rule borrows (§7).
- **R18 — salvo growth is a per-weapon privilege.** A `FireRate` card grows a volley only where `SalvoGrows` is true, and never past R17. The failure this guards against is silent: four cards on a 1-missile gun would otherwise make it the swarm gun, changing its identity and multiplying live entities.
- **R19 — a missile is damageable, and only by the other side.** It carries `Health` (1 hp small … 3 hp large at base), it is hit only by opposing-faction rounds and beams, a hit consumes the probing round and costs its own damage, and no bullet-vs-bullet or same-faction pass exists. Its hp may grow only through the `MissileHealth` card.

Recorded with the difficulty rules in `plans/DIFFICULTY_SCALING.md` and asserted in `EnemyDifficultyTest` (§9):

- **R20 — the escalation ladder is monotone.** Gun-only precedes missile-only, which precedes guns+missiles, on any one hull; a hull's missile share may only rise with tier; and an enemy missile never exceeds the player's slowest round in speed nor `MaxMissileTurnRate` in steering. These are the lines that keep the request's rule — *"a few missile-only variants … later an even mix … later both"* — from drifting into a missile storm during a later tuning pass.
- **R21 — an enemy missile can physically cross the ring it launches from.** `launch speed × fuse ≥ launch ring` at every tier (the enemy twin of the player's R6), with `MaxMissileFiringRange = 1450` as the outer bound.
- **R22 — the in-flight enemy missile cap is a slow, monotone ladder** tied to the composition tier, not to the stat ladder: 0 until tier 3, 3 through tier 7, **5 at tier 10**, **7 from tier 20**. It is a ceiling, not a floor: point defence and killing launchers are what keep the real number below it.

## 9. Enemy missiles: an armament ladder, not a new monster

**Shape: the same three hulls, re-armed over time.** A hull keeps its hull — radius, speed, hp, chase AI, firing ring — and gains an **armament**: `Guns`, `Missiles`, or `GunsAndMissiles`. The escalation the request asks for is then one readable table of *which armament each hull can roll from which tier, at what share of that hull's spawns*. No fourth species to spawn, art, test and threat-count.

### The ladder (recommended numbers)

Two tier axes, deliberately separate. **`TierFor` (stats) stays capped at 10** — that cap exists because hp/damage/fire-rate must stop compounding against the player's upgrades. A new **`CompositionTierFor` (capped at 20)** drives *what appears* and *how many missiles may be in the air*, which are the two axes the request wants to keep escalating past the ten-minute mark without re-opening the stat ceilings.

| Comp. tier | Clock | Small `enemy-1` (r 20) | Middle `interceptor` (r 45) | Large `heavy-cannon` (r 78) | Missiles in flight |
|---|---|---|---|---|---|
| 0–2 | 0:00–3:00 | guns | guns | guns | 0 |
| 3–4 | 3:00–5:00 | guns | guns · **missiles at 30 %** — "a few" | guns | 3 |
| 5–7 | 5:00–8:00 | guns | guns / missiles **even mix** | guns | 3 |
| 8–9 | 8:00–10:00 | guns | even mix | guns · **guns+missiles at 50 %** | 4 |
| 10–13 | 10:00–14:00 | guns | even mix | guns+missiles ~100 % | **5** |
| 14–19 | 14:00–20:00 | guns | even mix | guns+missiles | 6 |
| 20+ | capped | guns | even mix | guns+missiles | **7** |

Why that shape:

- The **middle** hull is the first missile carrier: it already stands off at 550–700 px, and 7 hp makes it a thing you can actually kill before it launches, so a fuse-armed body reads as a duellist rather than a lottery.
- The **large** hull gets the *second* weapon and gets it last: it is the slow long firing line (700→950 px), so a missile on it reads as the execution of a role the player already knows, and it arrives when the player has a grown hull, upgraded guns and several minutes of missile recognition behind them.
- The **small** swarmer stays un-armed in v1: up to 100 live hulls makes it the one armament no weight table tames (Q6).
- The in-flight cap climbs **one step at a time, four minutes apart**, because the pressure the request wants at 10 and 20 minutes comes from *mixing* — a HeavyCannon that also throws a fuse — not from volume. And the cap is safe to lift at all only because the player now has an answer that spends DPS rather than position: point defence (§5).

### The data

```csharp
public enum EnemyArmament { Guns, Missiles, GunsAndMissiles }

// One row of the escalation: an armament, the tier it unlocks at, and its share of that hull's spawns.
// A row only ever adds to the pool, so Weight is what turns "a few" into "an even mix".
public readonly record struct EnemyArmamentRow(EnemyArmament Armament, int MinTier, float Weight);
```

`EnemyShipStats` holds one row list per hull, next to the other tier drifts. `EnemyShipFactory.PickRandomType` gains the tier and becomes a weighted draw over hull × unlocked armament — which is also the composition unlock and the weight drift `plans/DIFFICULTY_SCALING.md` P6 has been holding back, so this closes that item rather than adding a new one. `WorldFactory` builds the initial world at elapsed 0 → tier 0, so the starting field can never contain a missile hull (assert it).

### The firing plumbing

- A missile-carrying hull also carries `MissileWeapon(WeaponStats Weapon, float Range)` and `MissileCooldown(float Timer)`. `Missiles` armament = **no `Turret` component at all**, only the missile; `GunsAndMissiles` = both, each on its own cooldown.
- A new **`EnemyMissileFiringSystem`**, Action phase right after `TurretFiringSystem`: iterate `MissileWeapon, EnemyShip, Position, Rotation, Velocity`, gate on the missile's own reach and the same hull-radius arc margin the gun pass uses, then launch `Position + Velocity + Ammo + Missile + Health` locked on the player. No lead: a missile steers. It ticks its own cooldown, so `TurretFiringSystem`'s enemy branch stays as it is.
- Single-sourced with the gun pass, deliberately: `AmmoSpawnOffset`, `AmmoSpeedVariation`, the inherited-velocity rule and the arc gate — extracted as a helper rather than copied, because a second hand-rolled gate would drift away from the one the chase AI is built around (TROUBLE_SHOOTING entry 48).
- **Missiles launch from farther than guns fire — and the hull never notices.** `MissileWeapon.Range = FiringRange × 1.4`, capped by `MaxMissileFiringRange = 1450`. The chase AI keeps braking at the hull's own `FiringRange` whatever it carries, so arming a hull changes nothing about how it flies, and the 1350 px ceiling on the *bullet* ring (the fairness rule from the difficulty pass) stays exactly where it is. Missiles buy reach instead of a new hull behaviour, which is also why they must be slower than the player: a fuse launched from 1330 px gives the player a long, visible window to react.

### Per-hull missile numbers (all inside the ceilings)

| Hull | Launch speed (tier drift) | Turn rate | Fuse | Launch ring | Round hp | Damage | Salvo |
|---|---|---|---|---|---|---|---|
| Interceptor, `Missiles` | 165 px/s (+4/tier, cap 250) | 70°/s | 6 s | 770–980 px | 2 | `BaseDamage + tier/3` | 1 |
| HeavyCannon, `GunsAndMissiles` | 200 px/s (+5/tier, cap 250) | 50°/s | 6 s | 980–1330 px | 3 | `BaseDamage + tier/3 + 1` | 1 |

R21 holds at both ends: at tier 0 the Interceptor crosses `165 × 6 = 990 ≥ 770`, at tier 10 the HeavyCannon crosses `250 × 6 = 1500 ≥ 1330`. The 1.4 multiplier is what makes the launch ring reachable at all, and it is the number to touch first if the fuse ever needs shortening.

### Ceilings, asserted in `EnemyDifficultyTest`

- **speed ≤ `MaxAmmoSpeed` (250)** — the existing readability ceiling now covers missiles too, and 165–250 sits inside it.
- **turn rate ≤ a new `MaxMissileTurnRate = 90°/s`**, fuse **≤ 6 s**, and **launch ring ≤ `MaxMissileFiringRange = 1450`** — under the player's longest auto-target band (1700), so nothing shoots at the player from a distance no player gun can answer. Fast player missiles stay the player's because these lines make "enemy missile" and "fast missile" mutually exclusive by construction.
- **missile damage ≤ bullet damage at the same stat tier.** Missiles get their own, slower ladder — `MissileDamageAdd = tier / 3` against the bullet's `tier / 2` — because a steering round is the most certain delivery the enemy ever gets, and certainty is paid for in damage, exactly as R1/R15 price it for the player. The HeavyCannon's `+1` hull-role premium keeps that inequality true only because the guns+missiles armament cannot appear before composition tier 8: at tier 8 the missile lands 4 against the bullet's 5. The test asserts the rule over the tiers where each armament can actually roll, which is the honest version of the claim.
- **the in-flight cap ladder above** — counted live (`Ammo` + `Missile` + `IsEnemy`, a handful of entities to walk). Per-hull reload (~1 missile / 3 s) is the soft knob; the world-wide cap is the hard one.

What the player does about it — five answers, which is what all these ceilings are buying: **outrun** it (165–250 px/s can never catch a hull flying faster), **out-turn** it (at ~200 px/s the player's turn radius is ~150 px against a 135 px Interceptor missile and a 233 px torpedo), **shoot it down** (1–3 hp, and every gun and beam on the hull is a point-defence answer: `FlakCurtain`'s 9 px disc is the cheapest), **kill the launcher**, or **let the fuse run out**.

## 10. Rendering and UI

### The missile itself: tip, shaft, exhaust

- **Two generated images, drawn as two batched quads.** Same pattern as `ShapeSprites.Disc` (`GenImageColor` + `ImageDrawTriangle` / `ImageDrawRec`, white on transparent, tinted per draw): a **body** — a tip triangle over a square shaft — and an **exhaust** — a triangle opening aft. Draw order along the heading: tip, shaft, exhaust. Two `DrawTexturePro` calls per missile keeps a few dozen missiles inside raylib's batched 2D path, which is the whole reason `ShapeSprites` exists (TROUBLE_SHOOTING #33: raw shape draws cost ~1.1 µs each).
- **A `world.missiles` pass next to `world.ammo`**, so its cost is visible in `RenderTimings` and bullet streaks are untouched.
- **The exhaust is the steering tell.** `Missile.Steering` (§4, 0–1 from the turn the round actually applied last tick) drives the exhaust's length and brightness: dead on the lock → short and dim; swinging as hard as it can → long and bright. That is honest telemetry, because the turn is exactly the quantity the player dodges against. If a boost phase is added later, the same knob carries it — no new state.
- **Never invisible.** The exhaust has a floor length and a floor alpha, and the body stays fully opaque for the whole life of the round. A missile the player lost track of is a fairness bug, not a subtle visual.
- **Sizes match the hitbox:** shaft about 10 × 4 px for a small missile and 16 × 6 px for a large one, against collision radii of 4 and 6 px, so a graze reads as a graze.
- **Colour:** player `Orange`, enemy `Red` (one new `AmmoColor` member plus one line in `AmmoColorOf`) — instantly readable against the yellow/cyan bullet soup. Optional cheap damage tell: flicker the exhaust while `Health < Max`.
- Smoke trails stay out of v1: entity cost against a scenario that is already knowingly over budget.

### The rack: a mount that does not aim

- A homing mount draws a **square** in the hull's frame instead of `RenderHelpers.DrawTurretMarker`'s aim-following triangle (circle + triangle today). **Not turning is the message**: the rack only chooses the arc, the round steers itself. Same square on the mount screen, so the mount menu says what kind of weapon fits where without reading text.
- The **arc sector still draws** — a launch still has to be inside the slot's arc, and the missile inherits nothing from the mount's facing after that.
- Raw shape draws stay fine here: a hull has at most seven mounts, so the batching rule that saves thousands of particles does not apply (the current marker is already two raw draws).
- **A missile-carrying enemy hull must be recognisable before it fires**, or the ladder is unfair rather than hard. v1 buys that with the launch itself: a bright, slow, unmistakable exhaust plus one launch smoke puff. The cleaner tell — the same square rack glyph on the enemy hull — needs an `Armament` byte on `EnemyShip` for the renderer to read, so it is a recommended follow-up rather than a v1 dependency (Q7).

### Text

- **Cards**: `NewWeaponSpecs` adds a `homing 150°/s` line next to the projectile-speed line, and a `salvo 3` line where the gun fires more than one — the two numbers that define the class.
- **Stats screen**: `Homing turn` and `Round hp` rows for a homing weapon (the existing `Turret turn speed` row still applies — the mount still slews to pick its launch bearing).
- **Mount screen, cargo, ship select, diagnostics bench**: unchanged apart from the square marker; add the new guns to `ShipType.DiagnosticShips` so each can be play-tested from a fresh start.

## 11. Performance

The guidance pass is three O(1) probes per live missile, writing nothing when the heading is already straight. Live missile count in real play: a full Heavy (3 large mounts) with torpedoes at 0.7/s × 8 s fuse ≈ 17 in the world; a SeekerPod-heavy loadout ≈ 10–15; enemy missiles are hard-capped world-wide, 3 at tier 3 and 7 at tier 20 (§9). Worst case is therefore about thirty guidance iterations per tick — two orders under the 10k-object goal. The cap is the whole reason that number stays small, so it is a perf knob as much as a fairness one.

The cross-fire pass is the one piece with real hot-path exposure: it adds a grid kind, so **every** round's box query now returns missile candidates too, and each candidate costs one faction comparison plus (only when they differ) a distance test. Missiles are ≤ ~30 entities, so they occupy few cells; the added work per bullet is a couple of register comparisons. That is exactly the kind of claim AGENTS.md says to measure rather than assert.

Measure both scenarios, because they stress opposite things: `.tools/scenario.sh ammo-spray --sim --iters 3` (5k rounds querying a grid that now carries a new kind) and `.tools/scenario.sh impact-storm --sim --iters 3`, against the previous row of `.tools/run/index.tsv`, verdict in the reply. The scenarios silence player turrets (`ScenarioWorld` unmounts them) and run at composition tier 0 — where the ladder arms nobody — so they prove *no regression*, not the missile cost: same caveat already recorded for the difficulty and balance passes. The enemy firing pass iterates the `MissileWeapon` storage only, so it costs nothing in a world with no missile hulls. If a playtest shows missiles mattering, add a `missile-barrage` scenario that seeds `Missile` components plus opposing fire — that is the state the cross-fire pass is expensive in, and it is the one the existing scenarios cannot reach.

Render cost: two batched quads per missile at ~0.1 µs per shape after batching, against ~30 missiles — noise. The exhaust animation is a scale and a tint on the same texture, so no new texture state changes per frame.

## 12. Tests

- `MissileGuidanceTest`: a locked missile curves onto a stationary hull and kills it; the heading moves by at most `TurnRate × dt` per tick (no U-turn); speed stays constant across ticks; a destroyed lock makes it fly straight and die at the fuse; a missile never gains `Acceleration`; `Steering` reads 1 mid-turn and 0 once the round is dead on the lock (the exhaust depends on it).
- `MissileCollisionTest` — one file for both directions of the request's rule:
  - *friendly*: a player missile overlapping the player hull deals nothing; two player missiles pass each other; an enemy missile passes an enemy hull; a bullet passes a same-faction missile.
  - *cross-fire*: an enemy bullet destroys a 1 hp player missile and is itself consumed; a player bullet does the same to an enemy missile; a 3 hp torpedo survives two 1-damage hits and dies to one 14-damage slug; a beam cuts a missile; two opposing missiles on a head-on course kill each other; a bullet never damages a bullet; **a missile destroyed mid-flight never damages the player** (Q1).
- `MissileHealthUpgradeTest`: the card raises the hp of that gun's *next* rounds only; it is offered for homing weapons only; no card appears for a non-homing gun.
- `SalvoGrowthTest` (R18): a `FireRate` card on SeekerPod multiplies the rate and leaves the volley at 1; on Swarmlet it adds a missile; the card stops being offered once the volley would break R17.
- `TurretFiringTest` addition: a homing gun locks the nearest in-arc target, its round carries that lock plus `Health`, and the round is not lead-solved.
- `WeaponBalanceTest`: R15 (certainty pricing, all three bands), R16 (turn radius), R17 (salvo budget), R18 (growth privilege), R19 (base hp within 1–3) for the new rows; existing rules keep covering them automatically.
- `EnemyArmamentLadderTest` (R20): no hull in the initial world or at composition tier 0 carries a missile; the Interceptor's missile row unlocks at tier 3 and the HeavyCannon's guns+missiles row at tier 8; a hull's missile weight never falls as the tier rises; a tier-2 draw returns zero missile hulls over many draws; **raising the composition cap to 20 does not move the stat cap at 10**.
- `EnemyMissileFiringTest`: launches at a player inside its reach and not outside it; honours the arc gate and the hull-radius margin; the round inherits the hull's velocity and the same ±30 % speed roll; a `Missiles` hull has no `Turret` component and fires once per reload; a `GunsAndMissiles` hull fires both weapons on independent cooldowns; **the hull still brakes at its own `FiringRange`** whatever it carries.
- `EnemyMissileCapTest` (R22): the cap follows the ladder — 3 at tier 3, 4 at tier 8, **5 at tier 10**, 6 at tier 14, **7 at tier 20** — and a crowded world never exceeds it, checked after 200 ticks with 40 hulls alive.
- `EnemyMissileReachTest` (R21): `launch speed × fuse ≥ launch ring` for every hull at every tier, and the ring stays under `MaxMissileFiringRange`.
- `EnemyDifficultyTest`: the ceilings (speed, turn rate, fuse, ring, missile damage ≤ bullet damage at the same tier); the TTK check generalises from "HeavyCannon" to "every hull", which a missile variant does not change since armament never moves hull hp.
- `ThreatMeterTest`: the new `missiles=` column counts live enemy missiles and ignores live player ones.
- `WorldRngTest` unchanged (the armament draw goes through `view.Rng`, one extra draw per spawn once tier 3 arrives); `SPACEVORS_DIAG_UPGRADES` scripts keep working (they name guns directly and ignore the pool).

## 13. Commit split (small, each leaves the tree green)

1. `missiles: Missile component + guidance system` — component, system, Intent-phase wiring in `SimulationRunner`, the firing hook, `MissileGuidanceTest`. Ships nothing playable yet.
2. `missiles: SeekerPod, HavocTorpedo, Swarmlet + R15–R17, R18` — weapon table rows, the `SalvoGrows` split in `ApplyUpgrade`, the card-withdrawal rule in `LevelUpSystem`, balance-test rules, card lines, bench ships.
3. `missiles: missile graphics` — generated body/exhaust images, `world.missiles` pass, `Steering`-driven exhaust with its floors, `AmmoColor.Orange`, the square rack marker in the mount screen and on the hull, stats-screen rows.
4. `missiles: missiles are damageable` — `SpatialGrid.CollisionKind.Missile`, the cross-faction branch in the ammo sweep and the beam pass, `Health` on launch, the `MissileHealth` card, `MissileCollisionTest` + `MissileHealthUpgradeTest` (R19).
5. `missiles: enemy missile firing` — `MissileWeapon` + `MissileCooldown`, `EnemyMissileFiringSystem`, the shared arc/launch helper, the cap ladder, `CompositionTierFor`, ceilings in `EnemyDifficultyTest`. Nothing is armed yet, so the commit is behaviour-free by construction.
6. `missiles: the armament ladder` — `EnemyArmament` rows per hull (tier gates + weights), the weighted hull×armament pick, the ladder / firing / reach / cap tests.
7. `missiles: threat readout` — the `missiles=` column in `ThreatMeter` and the hud row.
8. Docs (below).

Order matters twice: the damageable-missile commit (4) lands **before** the enemy side (5–6), so the player never faces a fuse that cannot be shot down; and the graphics commit (3) lands before either, so every later playtest is readable.

## 14. Docs to update on implementation

- `ARCHITECTURE.md`: `Missile`, `MissileWeapon`, `MissileCooldown` in the component list, and `Health` noted as also riding a missile; `MissileGuidanceSystem` first in the Intent phase and `EnemyMissileFiringSystem` after `TurretFiringSystem` in the Action phase; the new `SpatialGrid.CollisionKind.Missile` in the broad-phase description; a line in Write patterns about the dual `Velocity`/`Ammo` command pair.
- `PLAN.md`: move "Missiles" out of *Near future* into a done section with the numbers; add the `MissileHealth` card to the upgrade list and the armament ladder to the difficulty paragraph.
- `plans/WEAPON_BALANCE.md`: R15–R17, R18 and R19, with the three missile rows.
- `plans/DIFFICULTY_SCALING.md`: **P6 (which enemies appear when, and how their weights drift) becomes real** as the armament ladder; record the new **composition tier (cap 20) beside the stat tier (cap 10)** and why they are split; add R20, R21 and R22 to the ceiling list. Record what is still open there (weight drift beyond missiles, elites, surges).
- `TROUBLE_SHOOTING.md`: only if implementation hits a *workflow* problem worth recording (e.g. a headless-verification trap), not for game-logic findings.

## 15. Open questions (all with a recommendation)

1. **Does a missile shot down mid-flight detonate on the spot, or just break up?** → **Break up** (small `Explosion` + smoke, no warhead). Detonating means deciding whose damage a friendly kill deals next to the hull — a 3-damage popup from a shot the player took deliberately is the wrong lesson.
2. **Retarget when the locked target dies?** → **No.** Fly straight, let the fuse end it. Cheap, readable, and it makes "kill the lock" a real counter.
3. **Does a multi-missile volley lock one target or one each?** → **One target in v1** (the volley fans with `Scatter`). One-each is ~5 lines in `FireAtTarget`; decide after play-testing Swarmlet — it is the difference between a shotgun of missiles and a swarm.
4. **Arming distance** (a missile spawned inside an adjacent hull detonates at once)? → **Accept it for now**; if it reads unfair, add it in guidance, not in collision.
5. **Are the ladder's gates right — Interceptor missiles at 3, even mix at 5, HeavyCannon guns+missiles at 8?** → **Yes as a first pass.** The `[THREAT]` row is the instrument: it already sums incoming damage per second, and the new `missiles=` column makes a ladder step show up as a number rather than a feeling.
6. **Should the small `enemy-1` ever carry a missile?** → **No in v1.** Up to 100 live hulls makes it the one armament no weight table tames. If the late fleet reads one-note, a tier-14 missile variant with the cap doing the bounding is the honest way in.
7. **Is the launch tell enough for missile hulls, or do they need a visible marker now?** → **Launch tell in v1**; the square rack glyph on enemy hulls (which needs an `Armament` byte on `EnemyShip`, plus sprite variants later) is the follow-up if playtesting shows players eating first hits from hulls they never distinguished.
8. **Does a guns+missiles HeavyCannon need an offset — less hp, a slower gun — or are the cap and the slow missile enough?** → **No offset in v1.** The hull's numbers stay exactly as the difficulty tests price them; the cap is the offset. Revisit if first contact after 8 min becomes a coin flip.
9. **Do missile hulls pay more XP or loot?** → **No.** The difficulty pass chose "deadlier individuals", and keeping the reward side untouched is what makes `LootStats` independently tunable.
10. **Is the cap schedule right — 3 through tier 7, 4 at 8, 5 at 10, 6 at 14, 7 at 20 — and should the *stat* tier extend to 20 as well?** → **Caps as scheduled; the stat tier stays at 10.** Composition and concurrency may keep escalating without hp/damage/fire-rate compounding forever against the player's upgrades, which is the reason the original cap exists.
11. **`MissileHealth`: one global card or one per weapon, and should it stop somewhere?** → **Per weapon** (fits the existing `Damage`/`FireRate` shape and keeps the hand readable), **uncapped in v1** like `Damage`. Point-defence math is the natural brake: a 6 hp torpedo is six Cyclotron hits, so the card stops paying for itself long before it becomes silly.
12. **Does point defence need a mechanical bonus, or is `FlakCurtain`'s role enough?** → **Role only, no multiplier.** Its 9 px radius and 0.5 rad cone already make it the cheapest missile killer; a "vs missile" damage multiplier would be a new damage path and a new balance axis for a job the numbers already do.
13. **Should auto-target ever prefer an incoming missile over the hull that fired it?** → **Not in v1.** Nearest-wins stays; a priority rule silently changes what the player's guns do without being asked. If missiles feel unkillable in playtest, this is the second lever, after the card in Q11.
14. **Should enemy missile hp drift with tier (2→3, 3→4)?** → **Stay flat.** The ladder escalates through *mixing* and the cap; hp drift would make a late missile need four point-defence hits, which reads as a wall rather than a step.
15. **Exhaust brightness: steering effort (v1) or a launch boost phase?** → **Steering effort**, because it shows the quantity the player dodges against. A boost phase can drive the same knob later without new state.
16. **Should a damaged missile look damaged?** → **Yes, cheaply** — flicker the exhaust while `Health < Max`. A missile that shrugs off three hits and looks untouched teaches the wrong thing about the player's own aim.
17. **Should the enemy missile's longer launch ring (up to 1450 px, past the 1350 px bullet ring) be shown to the player?** → **No hint.** The launch itself is the tell, and the fuse plus the speed are the answer; a diagnostics-only ring circle is enough for tuning.
18. **Should `ProjectileSpeed` cards apply to missiles?** → **Yes, no cap** — it works today (guidance keeps the launch speed) and it makes the card range honest. Watch `[THREAT]`: a heavily upgraded missile is both harder to dodge *and* harder to shoot down, which is the one combination these ceilings exist to catch.
19. **A "homing turn rate" upgrade card?** → **No for v1.** Rate and salvo growth already cover the class; a turn-rate card is a new `UpgradeOption` plus screen plumbing for the stat that R16's guard rail is built on.
20. **Magazine / reload / missile supply?** → **No.** It is a resource mechanic nothing else in the game has.
21. **A hunter-killer missile that seeks enemy rounds?** → **Not now.** The cross-fire pass gives point defence without it; a seeker would need a per-round target search — the same cost that made the balance pass rename `PointDefenceTurret` away.
22. **Do player missiles lock mines and clicked rocks?** → **Yes** (free via the existing target paths). A torpedo wasting 10 damage on a 2 hp mine is a real tradeoff for bad target priority.
23. **How many player missile guns in v1?** → **Three** (SeekerPod, HavocTorpedo, Swarmlet). Swarmlet has to be there: without it no weapon in the game has the growing-salvo fantasy the growth rule is written around — and it is the only one allowed to have it.
24. **Should the Interceptor's missile variant change behaviour (stand-off kiter) or keep the hull's chase AI?** → **Keep the AI.** Same hull, same flight, different weapon is what makes the ladder legible in one sentence — and the reach difference comes from the missile's own range, not from the hull flying differently.
25. **Model the variants as armament rows on the hull, or as extra `EnemyShipType` entries (one per hull+armament pair)?** → **Armament rows.** Extra types would triple the type list, duplicate every hull stat, and force graphics ids and difficulty tests to cover combinations the table already expresses.
