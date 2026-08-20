using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class TurretFiringSystem : GameSystem
{
    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        bool playerDead = IsPlayerDead(view);

        var turrets = view.GetEntitiesWithComponents<Turret, Position, Rotation>().ToList();

        foreach (var (turretEntity, turret, turretPos, turretRot) in turrets)
        {
            if (turret.IsEnemy && view.TryGetComponent<Dead>(turretEntity, out _)) continue;
            if (!turret.IsEnemy && playerDead) continue;

            var cooldown = CooldownHelper.GetCooldown(view, turretEntity);

            if (cooldown <= 0f)
            {
                var target = FindTarget(view, turretEntity, turret, turretPos.Value, turretRot.Angle);

                if (target.HasValue)
                {
                    FireAtTarget(view, turretEntity, turret, turretPos.Value, turretRot.Angle, target.Value, commands);
                    commands.Add(new AddComponentCommand<FireCooldown>(turretEntity, new FireCooldown(1f / turret.Weapon.FireRate)));

                    if (Environment.GetEnvironmentVariable("SPACEVORS_DIAGNOSTIC") == "1")
                    {
                        commands.AddEntity(new Position(target.Value.PredictedPosition), new DebugMarker(0.5f));
                    }
                }
            }
            else if (cooldown > 0f)
            {
                var newCooldown = cooldown - deltaTime;
                commands.Add(new AddComponentCommand<FireCooldown>(turretEntity, new FireCooldown(Math.Max(newCooldown, 0f))));
            }
        }
    }

    private static bool IsPlayerDead(WorldView view)
    {
        view.GetEntitiesWithComponents<Player>().TryFirst(out var player);
        return player.Entity.Value >= 0 && view.TryGetComponent<Dead>(player.Entity, out _);
    }

    private (Vector2 AimDirection, Vector2 PredictedPosition, float Radius)? FindTarget(WorldView view, Entity turretEntity, Turret turret, Vector2 turretPos, float turretAngle)
    {
        Vector2 forwardDir = new Vector2((float)Math.Sin(turretAngle), -(float)Math.Cos(turretAngle));

        float cosHalfArc = (float)Math.Cos(turret.ArcAngle / 2f);
        float rangeSq = turret.Range * turret.Range;

        (Vector2 AimDirection, Vector2 PredictedPosition, float Radius)? nearestTarget = null;
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

            if (turret.AutoTarget)
            {
                FindTargetWithPrediction(view, turretPos, forwardDir, cosHalfArc, rangeSq, playerVelocity, ammoSpeed, ref nearestTarget, ref nearestDistSq);
            }
            else
            {
                FindTargetWithoutPrediction(view, turretPos, forwardDir, cosHalfArc, rangeSq, ref nearestTarget, ref nearestDistSq);
            }

            CheckTargets(view.GetEntitiesWithComponents<Asteroid>(), a => a.Radius, rangeSq);
        }
        else
        {
            view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
            Entity playerEntity = playerTuple.Entity;

            if (playerEntity.Value >= 0 && view.TryGetComponent<EnemyShip>(turretEntity, out var es)
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

                    if (distSq < 0.001f || distSq > reachSq) continue;

                    // Engage when any part of the hull is inside the firing cone.
                    float dist = MathF.Sqrt(distSq);
                    float angularMargin = MathF.Asin(Math.Clamp(player.Radius / dist, -1f, 1f));
                    Vector2 toCenterDir = relPos / dist;
                    if (Vector2.Dot(forwardDir, toCenterDir) < MathF.Cos(halfArc + angularMargin)) continue;

                    Vector2 relVel = playerVel.Value - enemyVelocity;
                    float ammoSpeed = es.TurretAmmoSpeed;
                    float a = ammoSpeed * ammoSpeed - relVel.X * relVel.X - relVel.Y * relVel.Y;
                    float b = -2f * (relPos.X * relVel.X + relPos.Y * relVel.Y);
                    float c = -distSq;

                    float travelTime = SolveQuadratic(a, b, c);
                    if (travelTime <= 0f) continue;

                    Vector2 predictedPos = playerPos.Value + playerVel.Value * travelTime;
                    var toPredicted = predictedPos - turretPos;
                    float distToPredictedSq = toPredicted.X * toPredicted.X + toPredicted.Y * toPredicted.Y;

                    if (distToPredictedSq > reachSq) continue;

                    Vector2 aimDir = (toPredicted - enemyVelocity * travelTime) / (ammoSpeed * travelTime);
                    nearestTarget = (aimDir, predictedPos, player.Radius);
                    nearestDistSq = distSq;
                }
            }
        }

        return nearestTarget;

        void CheckTargets<T>(ComponentQuery<T> candidates, Func<T, float> getRadius, float checkRangeSq)
            where T : notnull
        {
            foreach (var (candidateEntity, value) in candidates)
            {
                var pos = view.GetComponent<Position>(candidateEntity);
                var diff = pos.Value - turretPos;
                float distSq = diff.X * diff.X + diff.Y * diff.Y;

                if (distSq > checkRangeSq || distSq < 0.001f) continue;

                float dist = (float)Math.Sqrt(distSq);
                var dir = diff / dist;
                if (Vector2.Dot(forwardDir, dir) < cosHalfArc) continue;

                if (distSq < nearestDistSq)
                {
                    nearestTarget = (dir, pos.Value, getRadius(value));
                    nearestDistSq = distSq;
                }
            }
        }
    }

    private static void FindTargetWithPrediction(WorldView view, Vector2 turretPos, Vector2 forwardDir, float cosHalfArc, float rangeSq, Vector2 playerVelocity, float ammoSpeed, ref (Vector2 AimDirection, Vector2 PredictedPosition, float Radius)? nearestTarget, ref float nearestDistSq)
    {
        foreach (var (mineEntity, mine, velocity, minePos) in view.GetEntitiesWithComponents<EnemyMine, Velocity, Position>())
        {
            Vector2 relPos = minePos.Value - turretPos;
            float distSq = relPos.X * relPos.X + relPos.Y * relPos.Y;

            if (distSq > rangeSq || distSq < 0.001f) continue;

            Vector2 relVel = velocity.Value - playerVelocity;
            float a = ammoSpeed * ammoSpeed - relVel.X * relVel.X - relVel.Y * relVel.Y;
            float b = -2f * (relPos.X * relVel.X + relPos.Y * relVel.Y);
            float c = -distSq;

            float travelTime = SolveQuadratic(a, b, c);
            if (travelTime <= 0f) continue;

            Vector2 predictedPos = minePos.Value + velocity.Value * travelTime;
            var toPredicted = predictedPos - turretPos;
            float distToPredictedSq = toPredicted.X * toPredicted.X + toPredicted.Y * toPredicted.Y;

            if (distToPredictedSq > rangeSq) continue;

            Vector2 aimDir = (toPredicted - playerVelocity * travelTime) / (ammoSpeed * travelTime);
            float dot = Vector2.Dot(forwardDir, aimDir);
            if (dot < cosHalfArc) continue;

            if (distSq < nearestDistSq)
            {
                nearestTarget = (aimDir, predictedPos, mine.Radius);
                nearestDistSq = distSq;
            }
        }

        foreach (var (enemyShipEntity, enemyShip, velocity, shipPos) in view.GetEntitiesWithComponents<EnemyShip, Velocity, Position>())
        {
            Vector2 relPos = shipPos.Value - turretPos;
            float distSq = relPos.X * relPos.X + relPos.Y * relPos.Y;

            if (distSq > rangeSq || distSq < 0.001f) continue;

            Vector2 relVel = velocity.Value - playerVelocity;
            float a = ammoSpeed * ammoSpeed - relVel.X * relVel.X - relVel.Y * relVel.Y;
            float b = -2f * (relPos.X * relVel.X + relPos.Y * relVel.Y);
            float c = -distSq;

            float travelTime = SolveQuadratic(a, b, c);
            if (travelTime <= 0f) continue;

            Vector2 predictedPos = shipPos.Value + velocity.Value * travelTime;
            var toPredicted = predictedPos - turretPos;
            float distToPredictedSq = toPredicted.X * toPredicted.X + toPredicted.Y * toPredicted.Y;

            if (distToPredictedSq > rangeSq) continue;

            Vector2 aimDir = (toPredicted - playerVelocity * travelTime) / (ammoSpeed * travelTime);
            float dot = Vector2.Dot(forwardDir, aimDir);
            if (dot < cosHalfArc) continue;

            if (distSq < nearestDistSq)
            {
                nearestTarget = (aimDir, predictedPos, enemyShip.Radius);
                nearestDistSq = distSq;
            }
        }

        foreach (var (enemyShipEntity, enemyShip, shipPos) in view.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            var toTarget = shipPos.Value - turretPos;
            float distSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y;

            if (distSq > rangeSq || distSq < 0.001f) continue;

            float dist = (float)Math.Sqrt(distSq);
            var toTargetDir = toTarget / dist;

            float dot = Vector2.Dot(forwardDir, toTargetDir);
            if (dot < cosHalfArc) continue;

            if (distSq < nearestDistSq)
            {
                nearestTarget = (toTargetDir, shipPos.Value, enemyShip.Radius);
                nearestDistSq = distSq;
            }
        }
    }

    private static void FindTargetWithoutPrediction(WorldView view, Vector2 turretPos, Vector2 forwardDir, float cosHalfArc, float rangeSq, ref (Vector2 AimDirection, Vector2 PredictedPosition, float Radius)? nearestTarget, ref float nearestDistSq)
    {
        foreach (var (mineEntity, mine, velocity, minePos) in view.GetEntitiesWithComponents<EnemyMine, Velocity, Position>())
        {
            Vector2 toTarget = minePos.Value - turretPos;
            float distSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y;

            if (distSq > rangeSq || distSq < 0.001f) continue;

            float dist = (float)Math.Sqrt(distSq);
            var aimDir = toTarget / dist;
            float dot = Vector2.Dot(forwardDir, aimDir);
            if (dot < cosHalfArc) continue;

            if (distSq < nearestDistSq)
            {
                nearestTarget = (aimDir, minePos.Value, mine.Radius);
                nearestDistSq = distSq;
            }
        }

        foreach (var (enemyShipEntity, enemyShip, velocity, shipPos) in view.GetEntitiesWithComponents<EnemyShip, Velocity, Position>())
        {
            Vector2 toTarget = shipPos.Value - turretPos;
            float distSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y;

            if (distSq > rangeSq || distSq < 0.001f) continue;

            float dist = (float)Math.Sqrt(distSq);
            var aimDir = toTarget / dist;
            float dot = Vector2.Dot(forwardDir, aimDir);
            if (dot < cosHalfArc) continue;

            if (distSq < nearestDistSq)
            {
                nearestTarget = (aimDir, shipPos.Value, enemyShip.Radius);
                nearestDistSq = distSq;
            }
        }

        foreach (var (enemyShipEntity, enemyShip, shipPos) in view.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            Vector2 toTarget = shipPos.Value - turretPos;
            float distSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y;

            if (distSq > rangeSq || distSq < 0.001f) continue;

            float dist = (float)Math.Sqrt(distSq);
            var aimDir = toTarget / dist;
            float dot = Vector2.Dot(forwardDir, aimDir);
            if (dot < cosHalfArc) continue;

            if (distSq < nearestDistSq)
            {
                nearestTarget = (aimDir, shipPos.Value, enemyShip.Radius);
                nearestDistSq = distSq;
            }
        }
    }

    private void FireAtTarget(WorldView view, Entity turretEntity, Turret turret, Vector2 turretPos, float turretAngle, (Vector2 AimDirection, Vector2 PredictedPosition, float Radius) target, CommandBuffer commands)
    {
        Vector2 ammoDir = target.AimDirection;

        int pelletCount = turret.Weapon.PelletCount;
        float scatterAngle = turret.Weapon.Scatter;

        for (int i = 0; i < pelletCount; i++)
        {
            float angleOffset = (i - (pelletCount - 1) / 2f) * scatterAngle;
            float cosOff = (float)Math.Cos(angleOffset);
            float sinOff = (float)Math.Sin(angleOffset);

            Vector2 pelletDir = new Vector2(
                ammoDir.X * cosOff - ammoDir.Y * sinOff,
                ammoDir.X * sinOff + ammoDir.Y * cosOff
            );

            float speedVariation = 1f + (view.Rng.NextSingle() - 0.5f) * 0.3f;

            Vector2 spawnOffset = pelletDir * 20f;
            if (turret.IsEnemy && view.TryGetComponent<EnemyShip>(turretEntity, out var ship))
            {
                float forwardOffset = ship.Radius + 5f;
                Vector2 forwardDir = new Vector2((float)Math.Sin(turretAngle), -(float)Math.Cos(turretAngle));
                spawnOffset += forwardDir * forwardOffset;
            }

            var spawnPos = turretPos + spawnOffset;
            Vector2 ammoVel = pelletDir * turret.Weapon.AmmoSpeed * speedVariation;

            if (!turret.IsEnemy)
            {
                foreach (var (_, _, vel) in view.GetEntitiesWithComponents<Player, Velocity>())
                {
                    ammoVel += vel.Value;
                    break;
                }
            }
            else if (view.TryGetComponent<Velocity>(turretEntity, out var shipVel))
            {
                ammoVel += shipVel.Value;
            }

            float ammoRadius = GetAmmoRadius(turret);
            int damage = turret.Weapon.Damage;
            var ammoColor = GetAmmoColor(turret, damage);

            commands.AddEntity(new Position(spawnPos), new Velocity(ammoVel), new Ammo(ammoVel, ammoRadius, turret.Weapon.ShotLifetime, turret.IsEnemy, damage, ammoColor));
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
        if (turret.IsEnemy) return DefaultAmmoRadius;

        var type = WeaponType.FromName(turret.WeaponName);
        return type?.AmmoRadius ?? DefaultAmmoRadius;
    }

    private static AmmoColor GetAmmoColor(Turret turret, int damage)
    {
        if (turret.IsEnemy) return damage > 1 ? AmmoColor.Red : AmmoColor.Yellow;

        var type = WeaponType.FromName(turret.WeaponName);
        return type?.Color ?? AmmoColor.Yellow;
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
