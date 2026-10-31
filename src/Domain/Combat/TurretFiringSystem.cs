using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// One turret's current target selection: the entity plus the aim data for firing at it.
// `Led` marks an aim that is an intercept solution rather than the target's current position: a round
// leaves at a rolled speed, so the shot is solved again for the speed it actually has.
public readonly record struct TargetSelection(Entity Target, Vector2 AimDirection, Vector2 PredictedPosition, float Radius, bool Led);

public class TurretFiringSystem : GameSystem
{
    // Distance (or squared distance) below which positions are treated as coincident.
    private const float Epsilon = 0.001f;

    // A bullet spawns this far in front of its turret, and its speed varies by this fraction.
    // Shared with the perf scenarios, which seed volleys that must look like fired ones.
    public const float AmmoSpawnOffset = 20f;
    public const float AmmoSpeedVariation = 0.3f;

    // An enemy round — gun or rack — leaves its hull clear of the body, not merely clear of the mount,
    // so it can never be born inside the hull that threw it. Both enemy passes use this one helper.
    private const float HullClearance = 5f;

    public static Vector2 EnemySpawnPosition(Vector2 hullPos, Vector2 aimDir, Vector2 hullForward, float hullRadius) =>
        hullPos + aimDir * AmmoSpawnOffset + hullForward * (hullRadius + HullClearance);

    // A manually selected target may be engaged at this multiple of normal range, capped by ammo reach.
    private const float TargetedRangeMultiplier = 3f;

    // Aim tolerances, in radians. A slewing mount keeps turning until it is within the acquire
    // tolerance, then parks; a parked mount only resumes turning once the aim has drifted past the
    // fire tolerance, which is also the firing gate — inside it the aim is good enough to shoot.
    static readonly float AimAcquireTolerance = Angle.FromDeg(3f);
    static readonly float FireAimTolerance = Angle.FromDeg(8f);

    // A dot test against -1 admits every bearing. A guided round needs no clear line and no facing mount:
    // it turns after launch, so only its range gates the shot.
    private const float AnyBearingCosine = -1f;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        bool playerDead = IsPlayerDead(view);
        ClearStalePrimaryTarget(view, commands);
        float hullRotation = PlayerRotation(view);

        // Commands are deferred to phase end, so the query can be iterated directly.
        foreach (var (turretEntity, turret, turretPos, turretRot) in view.GetEntitiesWithComponents<Turret, Position, Rotation>())
        {
            if (turret.IsEnemy && view.TryGetComponent<Dead>(turretEntity, out _)) continue;
            if (!turret.IsEnemy && playerDead) continue;

            // A player turret is bolted to a hull slot and slewed inside its arc; an enemy turret is
            // aimed by its hull, which is what its Rotation already holds. A player rack slews nothing at
            // all: a rack is a square that does not turn, and its round leaves along the hull's nose.
            MountState? mount = turret.IsEnemy || turret.Weapon.Homing ? null : ReadMount(view, turretEntity);

            float arcCenterAngle = mount is { } m ? hullRotation + m.Slot.ArcCenter : turretRot.Angle;
            var selection = FindTarget(view, turretEntity, turret, turretPos.Value, arcCenterAngle);

            float aimAngle = turretRot.Angle;
            if (mount is { } ms)
                aimAngle = SlewMount(turretEntity, ms.Slot, ms.Aim, hullRotation, selection, turret.Weapon.TurnSpeed, deltaTime, commands);

            if (!turret.IsEnemy && selection.HasValue && IsMarkableTarget(view, selection.Value.Target))
                commands.Add(new AddComponentCommand<AutoTargetMark>(selection.Value.Target, new AutoTargetMark(view.ElapsedTime)));

            var cooldown = CooldownHelper.GetCooldown(view, turretEntity);

            if (cooldown > 0f)
            {
                commands.Add(new AddComponentCommand<FireCooldown>(turretEntity, new FireCooldown(Math.Max(cooldown - deltaTime, 0f))));
                continue;
            }

            if (!selection.HasValue) continue;
            // Being inside the arc is not enough: a mount still travelling to a solution holds its fire.
            if (mount is not null && !AimOnTarget(aimAngle, selection.Value.AimDirection)) continue;

            // A rack does not aim, so its round leaves along the hull's nose and guidance turns it onto the
            // lock from there. That is what lets a stern rack cover what is ahead — and a bow rack what is
            // behind — while the round still has to fly the whole way around.
            var shot = turret.Weapon.Homing
                ? selection.Value with { AimDirection = Angle.Direction(hullRotation) }
                : selection.Value;

            FireAtTarget(view, turretEntity, turret, turretPos.Value, aimAngle, shot, commands);
            commands.Add(new AddComponentCommand<FireCooldown>(turretEntity, new FireCooldown(1f / turret.Weapon.FireRate)));

            if (Environment.GetEnvironmentVariable(DiagnosticLogger.EnvVar) == "1")
            {
                commands.AddEntity(new Position(selection.Value.PredictedPosition), new DebugMarker(0.5f));
            }
        }
    }

    // Slew the mount toward the firing solution — or back to the middle of its arc when it has no
    // target — at the weapon's turn speed. A mount that is already good enough holds still and
    // issues no commands, which is the common state. Returns the mount's absolute aim.
    // Only TurretAim (the hull-relative yaw) is written here: the mount's world Rotation is derived
    // from it every frame by the app layer, so a turning hull carries the aim with it for free.
    private static float SlewMount(
        Entity turretEntity,
        TurretSlotDefinition slot,
        TurretAim mount,
        float hullRotation,
        TargetSelection? selection,
        float turnSpeed,
        float deltaTime,
        CommandBuffer commands)
    {
        float yaw = mount.Yaw;
        float desiredYaw = selection.HasValue
            ? Angle.ClampToArc(Angle.Normalize(Angle.Of(selection.Value.AimDirection) - hullRotation), slot.ArcCenter, slot.ArcAngle)
            : slot.ArcCenter;

        float error = Angle.Delta(yaw, desiredYaw);
        float holdTolerance = mount.Parked ? FireAimTolerance : AimAcquireTolerance;

        if (MathF.Abs(error) <= holdTolerance)
        {
            if (!mount.Parked)
                commands.Add(new AddComponentCommand<TurretAim>(turretEntity, new TurretAim(yaw, Parked: true)));
            return hullRotation + yaw;
        }

        float newYaw = Angle.Normalize(Angle.RotateToward(yaw, desiredYaw, turnSpeed * deltaTime));
        bool arrived = MathF.Abs(Angle.Delta(newYaw, desiredYaw)) <= AimAcquireTolerance;
        commands.Add(new AddComponentCommand<TurretAim>(turretEntity, new TurretAim(newYaw, arrived)));
        return hullRotation + newYaw;
    }

    // A player turret's mount state: the slot it sits in and how far it has slewed inside its arc.
    private readonly record struct MountState(TurretSlotDefinition Slot, TurretAim Aim);

    private static MountState? ReadMount(WorldView view, Entity turretEntity)
    {
        if (!view.TryGetComponent<TurretSlot>(turretEntity, out var slot)) return null;
        if (!view.TryGetComponent<TurretAim>(turretEntity, out var aim)) return null;
        return new MountState(slot.Definition, aim);
    }

    static bool AimOnTarget(float aimAngle, Vector2 aimDirection) =>
        MathF.Abs(Angle.Delta(aimAngle, Angle.Of(aimDirection))) <= FireAimTolerance;

    static float PlayerRotation(WorldView view) =>
        view.GetEntitiesWithComponents<Player, Rotation>().TryFirst(out var player) ? player.Value2.Angle : 0f;

    private static bool IsPlayerDead(WorldView view)
    {
        return view.GetEntitiesWithComponents<Player>().TryFirst(out var player) && view.TryGetComponent<Dead>(player.Entity, out _);
    }

    // A dead or destroyed target is dropped so weapons fall back to auto-targeting.
    private static void ClearStalePrimaryTarget(WorldView view, CommandBuffer commands)
    {
        if (!view.GetEntitiesWithComponents<Player>().TryFirst(out var playerTuple)) return;
        if (!view.TryGetComponent<PrimaryTarget>(playerTuple.Entity, out _)) return;
        if (TryGetLivePrimaryTarget(view, out _)) return;

        commands.Add(new RemoveComponentCommand<PrimaryTarget>(playerTuple.Entity));
    }

    private static bool TryGetLivePrimaryTarget(WorldView view, out Entity target)
    {
        if (!view.GetEntitiesWithComponents<Player>().TryFirst(out var playerTuple))
        {
            target = default;
            return false;
        }

        if (!view.TryGetComponent<PrimaryTarget>(playerTuple.Entity, out var primary))
        {
            target = default;
            return false;
        }

        target = primary.Target;
        bool targetable = view.TryGetComponent<EnemyShip>(target, out _)
            || view.TryGetComponent<EnemyMine>(target, out _)
            || view.TryGetComponent<Asteroid>(target, out _);
        if (!targetable) return false;
        return !view.TryGetComponent<Dead>(target, out _);
    }

    // Lead-predicts a single moving target (enemy ship or mine); shared by the priority-target check and the auto-targeting search.
    private static TargetSelection? EvaluateMovingTarget(
        WorldView view, Entity targetEntity, Vector2 turretPos, Vector2 forwardDir, float cosHalfArc, float rangeSq, Vector2 playerVelocity, float ammoSpeed)
    {
        if (!view.TryGetComponent<Position>(targetEntity, out var pos)) return null;
        if (!view.TryGetComponent<Velocity>(targetEntity, out var vel)) return null;

        float radius = GetTargetRadius(view, targetEntity);

        Vector2 relPos = pos.Value - turretPos;
        float distSq = relPos.X * relPos.X + relPos.Y * relPos.Y;

        if (distSq > rangeSq || distSq < Epsilon) return null;

        Vector2 relVel = vel.Value - playerVelocity;
        float travelTime = InterceptTime(relPos, relVel, ammoSpeed);
        if (travelTime <= 0f) return null;

        Vector2 predictedPos = pos.Value + vel.Value * travelTime;
        var toPredicted = predictedPos - turretPos;
        float distToPredictedSq = toPredicted.X * toPredicted.X + toPredicted.Y * toPredicted.Y;

        if (distToPredictedSq > rangeSq) return null;

        Vector2 aimDir = (toPredicted - playerVelocity * travelTime) / (ammoSpeed * travelTime);
        if (Vector2.Dot(forwardDir, aimDir) < cosHalfArc) return null;

        return new TargetSelection(targetEntity, aimDir, predictedPos, radius, Led: true);
    }

    // Hitscan (instant) aim: target's current position must be in arc and within range — no lead.
    private static TargetSelection? EvaluateStaticTarget(
        WorldView view, Entity targetEntity, Vector2 turretPos, Vector2 forwardDir, float cosHalfArc, float rangeSq)
    {
        if (!view.TryGetComponent<Position>(targetEntity, out var pos)) return null;

        Vector2 relPos = pos.Value - turretPos;
        float distSq = relPos.X * relPos.X + relPos.Y * relPos.Y;
        if (distSq > rangeSq || distSq < Epsilon) return null;

        Vector2 aimDir = relPos / (float)Math.Sqrt(distSq);
        if (Vector2.Dot(forwardDir, aimDir) < cosHalfArc) return null;

        return new TargetSelection(targetEntity, aimDir, pos.Value, GetTargetRadius(view, targetEntity), Led: false);
    }

    private static float GetTargetRadius(WorldView view, Entity targetEntity) =>
        view.TryGetComponent<EnemyShip>(targetEntity, out var ship) ? ship.Radius
        : view.TryGetComponent<EnemyMine>(targetEntity, out var mine) ? mine.Radius
        : view.TryGetComponent<Asteroid>(targetEntity, out var rock) ? rock.Radius
        : 0f;

    // Only enemy ships and mines get auto brackets; asteroid selections are never marked.
    private static bool IsMarkableTarget(WorldView view, Entity target) =>
        view.TryGetComponent<EnemyShip>(target, out _) || view.TryGetComponent<EnemyMine>(target, out _);

    // Targets are chosen by the slot's arc (arcCenterAngle), not by where the mount currently points:
    // a turret tracks what it cannot yet reach and only holds fire until its aim catches up.
    private TargetSelection? FindTarget(WorldView view, Entity turretEntity, Turret turret, Vector2 turretPos, float arcCenterAngle)
    {
        Vector2 forwardDir = Angle.Direction(arcCenterAngle);

        // A guided round is gated by its range alone: it turns onto its lock after launch, so a mount that
        // cannot point at the target — and a rack that points at nothing — still engages.
        float cosHalfArc = turret.Weapon.Homing ? AnyBearingCosine : (float)Math.Cos(turret.ArcAngle / 2f);
        float rangeSq = turret.Range * turret.Range;

        TargetSelection? nearestTarget = null;
        float nearestDistSq = float.MaxValue;

        if (!turret.IsEnemy)
        {
            Vector2 playerVelocity = Vector2.Zero;
            foreach (var (_, _, vel) in view.GetEntitiesWithComponents<Player, Velocity>())
            {
                playerVelocity = vel.Value;
                break;
            }

            float ammoSpeed = turret.Weapon.AmmoSpeed;

            // A clicked target — a ship, a mine or a rock — takes priority at an extended range (capped
            // by what the ammo can physically reach).
            if (TryGetLivePrimaryTarget(view, out var primaryTarget))
            {
                TargetSelection? aimed;
                if (turret.Weapon.Hitscan)
                {
                    // The beam reaches exactly turret.Range and lands instantly: aim at the current position.
                    aimed = EvaluateStaticTarget(view, primaryTarget, turretPos, forwardDir, cosHalfArc, turret.Range * turret.Range);
                }
                else
                {
                    float targetedRange = Math.Min(turret.Range * TargetedRangeMultiplier, turret.Weapon.AmmoSpeed * turret.Weapon.ShotLifetime);

                    // A guided round steers instead of being led, so its lock is taken where the target
                    // is; only the range cap is shared with the lead-predicted path.
                    aimed = turret.Weapon.Homing
                        ? EvaluateStaticTarget(view, primaryTarget, turretPos, forwardDir, cosHalfArc, targetedRange * targetedRange)
                        : EvaluateMovingTarget(view, primaryTarget, turretPos, forwardDir, cosHalfArc, targetedRange * targetedRange, playerVelocity, ammoSpeed);
                }
                if (aimed.HasValue) return aimed;
            }

            // Auto-targeting sees only what shoots back: enemy ships and mines. A rock is scenery
            // until the player clicks one, which is the path above. A homing round needs no lead
            // solution either — it will turn onto whatever it locked — so it scans like a beam does.
            if (turret.AutoTarget && !turret.Weapon.Hitscan && !turret.Weapon.Homing)
            {
                FindTargetWithPrediction(view, turretPos, forwardDir, cosHalfArc, rangeSq, playerVelocity, ammoSpeed, ref nearestTarget, ref nearestDistSq);
            }
            else
            {
                FindTargetWithoutPrediction(view, turretPos, forwardDir, cosHalfArc, rangeSq, ref nearestTarget, ref nearestDistSq);
            }
        }
        else
        {
            bool hasPlayer = view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
            Entity playerEntity = playerTuple.Entity;

            if (hasPlayer && view.TryGetComponent<EnemyShip>(turretEntity, out var es)
                && view.TryGetComponent<Player>(playerEntity, out var player))
            {
                Vector2 enemyVelocity = Vector2.Zero;
                if (view.TryGetComponent<Velocity>(turretEntity, out var vel))
                {
                    enemyVelocity = vel.Value;
                }

                float reach = es.FiringRange + player.Radius;
                float reachSq = reach * reach;
                float halfArc = turret.ArcAngle / 2f;

                foreach (var (_, _, playerVel) in view.GetEntitiesWithComponents<Player, Velocity>())
                {
                    var playerPos = view.GetComponent<Position>(playerEntity);
                    Vector2 relPos = playerPos.Value - turretPos;
                    float distSq = relPos.X * relPos.X + relPos.Y * relPos.Y;

                    if (distSq < Epsilon || distSq > reachSq) continue;

                    // Engage when any part of the hull is inside the firing cone.
                    float dist = MathF.Sqrt(distSq);
                    Vector2 toCenterDir = relPos / dist;
                    if (!FiringArc.Engulfs(forwardDir, toCenterDir, halfArc, player.Radius, dist)) continue;

                    Vector2 relVel = playerVel.Value - enemyVelocity;
                    float ammoSpeed = es.TurretAmmoSpeed;
                    float travelTime = InterceptTime(relPos, relVel, ammoSpeed);
                    if (travelTime <= 0f) continue;

                    Vector2 predictedPos = playerPos.Value + playerVel.Value * travelTime;
                    var toPredicted = predictedPos - turretPos;
                    float distToPredictedSq = toPredicted.X * toPredicted.X + toPredicted.Y * toPredicted.Y;

                    if (distToPredictedSq > reachSq) continue;

                    Vector2 aimDir = (toPredicted - enemyVelocity * travelTime) / (ammoSpeed * travelTime);
                    nearestTarget = new TargetSelection(playerEntity, aimDir, predictedPos, player.Radius, Led: true);
                    nearestDistSq = distSq;
                }
            }
        }

        return nearestTarget;
    }

    private static void FindTargetWithPrediction(WorldView view, Vector2 turretPos, Vector2 forwardDir, float cosHalfArc, float rangeSq, Vector2 playerVelocity, float ammoSpeed, ref TargetSelection? nearestTarget, ref float nearestDistSq)
    {
        foreach (var (mineEntity, _, _, minePos) in view.GetEntitiesWithComponents<EnemyMine, Velocity, Position>())
        {
            var aimed = EvaluateMovingTarget(view, mineEntity, turretPos, forwardDir, cosHalfArc, rangeSq, playerVelocity, ammoSpeed);
            if (!aimed.HasValue) continue;

            Vector2 relPos = minePos.Value - turretPos;
            float distSq = relPos.X * relPos.X + relPos.Y * relPos.Y;

            if (distSq < nearestDistSq)
            {
                nearestTarget = aimed;
                nearestDistSq = distSq;
            }
        }

        foreach (var (enemyShipEntity, _, _, shipPos) in view.GetEntitiesWithComponents<EnemyShip, Velocity, Position>())
        {
            // A dead ship is already spent: its hull rides out the death sequence as a wreck, and
            // weapons retarget instead of emptying into the fireball.
            if (view.HasComponent<Dead>(enemyShipEntity)) continue;

            var aimed = EvaluateMovingTarget(view, enemyShipEntity, turretPos, forwardDir, cosHalfArc, rangeSq, playerVelocity, ammoSpeed);
            if (!aimed.HasValue) continue;

            Vector2 relPos = shipPos.Value - turretPos;
            float distSq = relPos.X * relPos.X + relPos.Y * relPos.Y;

            if (distSq < nearestDistSq)
            {
                nearestTarget = aimed;
                nearestDistSq = distSq;
            }
        }

        // Deliberately no second pass that aims at the ship's current position: a shot fired without
        // the lead solution is off by exactly (ship velocity x flight time), so a mount whose lead
        // falls outside its arc holds fire instead of throwing shots that cannot land.
    }

    private static void FindTargetWithoutPrediction(WorldView view, Vector2 turretPos, Vector2 forwardDir, float cosHalfArc, float rangeSq, ref TargetSelection? nearestTarget, ref float nearestDistSq)
    {
        foreach (var (mineEntity, mine, velocity, minePos) in view.GetEntitiesWithComponents<EnemyMine, Velocity, Position>())
        {
            Vector2 toTarget = minePos.Value - turretPos;
            float distSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y;

            if (distSq > rangeSq || distSq < Epsilon) continue;

            float dist = (float)Math.Sqrt(distSq);
            var aimDir = toTarget / dist;
            float dot = Vector2.Dot(forwardDir, aimDir);
            if (dot < cosHalfArc) continue;

            if (distSq < nearestDistSq)
            {
                nearestTarget = new TargetSelection(mineEntity, aimDir, minePos.Value, mine.Radius, Led: false);
                nearestDistSq = distSq;
            }
        }

        foreach (var (enemyShipEntity, enemyShip, velocity, shipPos) in view.GetEntitiesWithComponents<EnemyShip, Velocity, Position>())
        {
            // Same rule as the lead pass: wrecks riding out their death sequence are not targets.
            if (view.HasComponent<Dead>(enemyShipEntity)) continue;

            Vector2 toTarget = shipPos.Value - turretPos;
            float distSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y;

            if (distSq > rangeSq || distSq < Epsilon) continue;

            float dist = (float)Math.Sqrt(distSq);
            var aimDir = toTarget / dist;
            float dot = Vector2.Dot(forwardDir, aimDir);
            if (dot < cosHalfArc) continue;

            if (distSq < nearestDistSq)
            {
                nearestTarget = new TargetSelection(enemyShipEntity, aimDir, shipPos.Value, enemyShip.Radius, Led: false);
                nearestDistSq = distSq;
            }
        }
    }

    private void FireAtTarget(WorldView view, Entity turretEntity, Turret turret, Vector2 turretPos, float turretAngle, TargetSelection target, CommandBuffer commands)
    {
        Vector2 ammoDir = target.AimDirection;
        bool guided = turret.Weapon.Homing;

        int pelletCount = turret.Weapon.PelletCount;
        float scatterAngle = turret.Weapon.Scatter;

        // A round is fired at a rolled speed and leaves from the muzzle, not from the turret, so a led
        // target is solved again per round at the speed that round actually has and from where the round
        // starts. Solving once at the nominal speed leaves the roll itself tilting a slow round several
        // degrees off the solution the mount slewed to.
        Vector2 carriedVelocity = InheritedVelocity(view, turretEntity, turret);
        bool leads = false;
        Vector2 relPos = Vector2.Zero, relVel = Vector2.Zero;
        if (target.Led && view.TryGetComponent<Position>(target.Target, out var targetPos) && view.TryGetComponent<Velocity>(target.Target, out var targetVel))
        {
            leads = true;
            relPos = targetPos.Value - (turretPos + ammoDir * AmmoSpawnOffset);
            relVel = targetVel.Value - carriedVelocity;
        }

        for (int i = 0; i < pelletCount; i++)
        {
            float angleOffset = ScatterOffset(view.Rng, scatterAngle, i, pelletCount);
            // A guided round's speed is the number its turn radius and fuse reach are computed from, so it
            // is not rolled: a jittered round would be a different missile than the one the player read off
            // the card. Dumb ammo keeps its roll, which is what makes a shotgun spray a spray.
            float ammoSpeed = guided
                ? turret.Weapon.AmmoSpeed
                : turret.Weapon.AmmoSpeed * (1f + (view.Rng.NextSingle() - 0.5f) * AmmoSpeedVariation);
            // The speed it actually leaves at: a guided round is launched slow and guidance builds it to
            // `ammoSpeed`, which is its card speed and its ceiling.
            float flightSpeed = guided ? ammoSpeed * WeaponStats.MissileLaunchFraction : ammoSpeed;

            Vector2 aimDir = ammoDir;
            if (leads)
            {
                float travelTime = InterceptTime(relPos, relVel, ammoSpeed);
                if (travelTime > 0f) aimDir = (relPos + relVel * travelTime) / (ammoSpeed * travelTime);
            }

            float cosOff = (float)Math.Cos(angleOffset);
            float sinOff = (float)Math.Sin(angleOffset);

            Vector2 pelletDir = new Vector2(
                aimDir.X * cosOff - aimDir.Y * sinOff,
                aimDir.X * sinOff + aimDir.Y * cosOff
            );

            Vector2 spawnPos = turretPos + pelletDir * AmmoSpawnOffset;
            if (turret.IsEnemy && view.TryGetComponent<EnemyShip>(turretEntity, out var ship))
                spawnPos = EnemySpawnPosition(turretPos, pelletDir, Angle.Direction(turretAngle), ship.Radius);

            Vector2 ammoVel = pelletDir * flightSpeed + carriedVelocity;

            float ammoRadius = GetAmmoRadius(turret);
            int damage = turret.Weapon.Damage;
            var ammoColor = GetAmmoColor(turret, damage);

            if (turret.Weapon.Hitscan)
            {
                // Instant beam to max range; CollisionSystem damages the first thing hit.
                // With BeamSpacing, pellets form parallel beams instead of overlapping ones, and the
                // same fan applies: one beam stays exactly on the aim line at every pellet count.
                Vector2 beamStart = spawnPos;
                if (turret.Weapon.BeamSpacing > 0f && pelletCount > 1)
                {
                    var perp = new Vector2(-pelletDir.Y, pelletDir.X);
                    beamStart += perp * (FanStep(i) * turret.Weapon.BeamSpacing);
                }
                commands.AddEntity(
                    new Position(beamStart),
                    new LaserBeam(beamStart, pelletDir, turret.Range, ammoRadius, damage, turret.IsEnemy, ammoColor,
                        LaserBeam.FlashLifetime, LaserBeam.FlashLifetime));
                continue;
            }

            var ammo = new Ammo(ammoVel, ammoRadius, turret.Weapon.ShotLifetime, turret.IsEnemy, damage, ammoColor);

            // A guided round carries its lock and its own flight numbers, so nothing downstream has to
            // find the gun back. The locked entity may be gone when the round arrives; guidance treats a
            // missing lock as "fly on until the fuse dies".
            // It also carries hit points: a round that steers is worth shooting down, and only the
            // other side's fire can do it.
            if (turret.Weapon.Homing)
            {
                int missileHealth = turret.Weapon.MissileHealth;
                commands.AddEntity(
                    new Position(spawnPos),
                    new Velocity(ammoVel),
                    ammo,
                    new Missile(target.Target, flightSpeed, ammoSpeed, WeaponStats.MissileLaunchAcceleration, turret.Weapon.HomingTurnRate, Steering: 0f),
                    new Health(missileHealth, missileHealth));
            }
            else
            {
                commands.AddEntity(new Position(spawnPos), new Velocity(ammoVel), ammo);
            }
        }

        if (turret.Weapon.KickbackForce > 0)
        {
            Vector2 kickbackDir = new Vector2(-ammoDir.X, -ammoDir.Y);
            var playerTuples = view.GetEntitiesWithComponents<Player>().ToList();
            foreach (var (playerEntity, player) in playerTuples)
            {
                if (view.TryGetComponent<Velocity>(playerEntity, out var currentVel))
                {
                    float kickback = turret.Weapon.KickbackForce * KickbackScale(player.Radius);
                    commands.Add(new AddComponentCommand<Velocity>(playerEntity, new Velocity(currentVel.Value + kickbackDir * kickback)));
                }
            }
        }
    }

    // Scatter is the half-angle of the cone a shot pattern covers. The volley always has one round on
    // the aim line and the rest fan out from it; a volley that straddled the line — every even pellet
    // count does — put its whole pattern a little off a small target and missed with all of it. A lone
    // pellet has nothing to fan against, so it rolls a random angle inside the cone instead — without
    // that roll Scatter would do nothing at all for a spray weapon.
    private static float ScatterOffset(Random rng, float scatter, int pellet, int pelletCount)
    {
        if (pelletCount == 1) return scatter == 0f ? 0f : (rng.NextSingle() * 2f - 1f) * scatter;
        return FanStep(pellet) * scatter;
    }

    // Steps of a fan around its centre round: 0, -1, +1, -2, +2 ... The first round flies straight at
    // the target, the rest alternate to each side of it. Even counts cannot be symmetric around a
    // centre round, so their last step falls on the far side.
    private static float FanStep(int pellet) => pellet switch
    {
        0 => 0f,
        _ => (pellet % 2 == 1 ? -1 : 1) * (float)((pellet + 1) / 2)
    };

    // Fallback ammo radius when the weapon name is not a known player WeaponType.
    private const float DefaultAmmoRadius = 2.5f;

    // Ships are treated as spheres, so mass scales with r^3: bigger ships recoil much less. The lightest ship keeps 2/3 of base kickback.
    private static readonly float LightestShipRadius = ShipType.All.Min(t => t.Radius);

    private static float KickbackScale(float radius)
    {
        float ratio = LightestShipRadius / radius;
        return (2f / 3f) * ratio * ratio * ratio;
    }

    private static float GetAmmoRadius(Turret turret)
    {
        if (turret.IsEnemy) return EnemyShipStats.AmmoRadius;

        var type = WeaponType.FromName(turret.WeaponName);
        return type?.AmmoRadius ?? DefaultAmmoRadius;
    }

    private static AmmoColor GetAmmoColor(Turret turret, int damage)
    {
        if (turret.IsEnemy) return damage > 1 ? AmmoColor.Red : AmmoColor.Yellow;

        var type = WeaponType.FromName(turret.WeaponName);
        return type?.Color ?? AmmoColor.Yellow;
    }

    // Flight time at which a round of this speed, fired at the target from here, meets it;
    // <= 0 when it never can — the target outpaces the round, or is receding.
    private static float InterceptTime(Vector2 relPos, Vector2 relVel, float ammoSpeed) =>
        SolveQuadratic(
            ammoSpeed * ammoSpeed - relVel.X * relVel.X - relVel.Y * relVel.Y,
            -2f * (relPos.X * relVel.X + relPos.Y * relVel.Y),
            -(relPos.X * relPos.X + relPos.Y * relPos.Y));

    // The velocity a round carries from whoever fired it: the player's hull for a player turret, the
    // firing ship for an enemy one.
    private static Vector2 InheritedVelocity(WorldView view, Entity turretEntity, Turret turret)
    {
        if (turret.IsEnemy)
            return view.TryGetComponent<Velocity>(turretEntity, out var shipVel) ? shipVel.Value : Vector2.Zero;

        return view.GetEntitiesWithComponents<Player, Velocity>().TryFirst(out var player) ? player.Value2.Value : Vector2.Zero;
    }

    private static float SolveQuadratic(float a, float b, float c)
    {
        if (Math.Abs(a) < 1e-6f)
        {
            if (Math.Abs(b) < 1e-6f) return -1f;
            return -c / b;
        }

        float discriminant = b * b - 4f * a * c;
        if (discriminant < 0f) return -1f;

        float sqrtD = (float)Math.Sqrt(discriminant);
        float t1 = (-b + sqrtD) / (2f * a);
        float t2 = (-b - sqrtD) / (2f * a);

        if (t1 > 0f && t2 > 0f) return Math.Min(t1, t2);
        if (t1 > 0f) return t1;
        if (t2 > 0f) return t2;
        return -1f;
    }
}
