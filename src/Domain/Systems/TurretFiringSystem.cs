using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class TurretFiringSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        var turrets = em.GetEntitiesWithComponents<Turret>().ToList();

        foreach (var (turretEntity, turret) in turrets)
        {
            var cooldown = CooldownHelper.GetCooldown(em, turretEntity);

            if (cooldown <= 0f)
            {
                var target = FindTarget(em, turret, turretEntity);

                if (target.HasValue)
                {
                    FireAtTarget(em, turretEntity, turret, target.Value);
                    CooldownHelper.SetCooldown(em, turretEntity, 1f / turret.Weapon.FireRate);

                    if (Environment.GetEnvironmentVariable("SPACEVORS_DIAGNOSTIC") == "1")
                    {
                        var markerEntity = em.CreateEntity();
                        em.AddComponent(markerEntity, new Position(target.Value.PredictedPosition));
                        em.AddComponent(markerEntity, new DebugMarker(0.5f));
                    }
                }
            }
            else if (cooldown > 0f)
            {
                var newCooldown = cooldown - deltaTime;
                CooldownHelper.SetCooldown(em, turretEntity, Math.Max(newCooldown, 0f));
            }
        }
    }

    private (Vector2 AimDirection, Vector2 PredictedPosition, float Radius)? FindTarget(EntityManager em, Turret turret, Entity turretEntity)
    {
        var turretPos = em.GetComponent<Position>(turretEntity);
        var turretRot = em.GetComponent<Rotation>(turretEntity);

        Vector2 forwardDir = new Vector2((float)Math.Sin(turretRot.Angle), -(float)Math.Cos(turretRot.Angle));

        float cosHalfArc = (float)Math.Cos(turret.ArcAngle / 2f);
        float rangeSq = turret.Range * turret.Range;

        (Vector2 AimDirection, Vector2 PredictedPosition, float Radius)? nearestTarget = null;
        float nearestDistSq = float.MaxValue;

        if (!turret.IsEnemy)
        {
            Vector2 playerVelocity = Vector2.Zero;
            foreach (var (_, _, vel) in em.GetEntitiesWithComponents<Player, Velocity>())
            {
                playerVelocity = vel.Value;
                break;
            }

            float ammoSpeed = turret.Weapon.AmmoSpeed;

            if (turret.AutoTarget)
            {
                FindTargetWithPrediction(em, turretPos.Value, forwardDir, cosHalfArc, rangeSq, playerVelocity, ammoSpeed, ref nearestTarget, ref nearestDistSq);
            }
            else
            {
                FindTargetWithoutPrediction(em, turretPos.Value, forwardDir, cosHalfArc, rangeSq, ref nearestTarget, ref nearestDistSq);
            }

            CheckTargets(em.GetEntitiesWithComponents<Asteroid>(), a => a.Radius, rangeSq);
        }
        else
        {
            var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
            Entity playerEntity = playerTuple.Entity;

            EnemyShip enemyShip = em.HasComponent<EnemyShip>(turretEntity) ? em.GetComponent<EnemyShip>(turretEntity) : new EnemyShip(0, 0, 0, 0, 0, 300f, 0, 0, 0, 0);
            float firingRangeSq = enemyShip.FiringRange * enemyShip.FiringRange;

            if (playerEntity.Value >= 0 && em.HasComponent<EnemyShip>(turretEntity))
            {
                Vector2 enemyVelocity = Vector2.Zero;
                if (em.HasComponent<Velocity>(turretEntity))
                {
                    enemyVelocity = em.GetComponent<Velocity>(turretEntity).Value;
                }

                foreach (var (_, _, playerVel) in em.GetEntitiesWithComponents<Player, Velocity>())
                {
                    var playerPos = em.GetComponent<Position>(playerEntity);
                    Vector2 relPos = playerPos.Value - turretPos.Value;
                    float distSq = relPos.X * relPos.X + relPos.Y * relPos.Y;

                    if (distSq > firingRangeSq || distSq < 0.001f) continue;

                    Vector2 relVel = playerVel.Value - enemyVelocity;
                    float ammoSpeed = enemyShip.TurretAmmoSpeed;
                    float a = ammoSpeed * ammoSpeed - relVel.X * relVel.X - relVel.Y * relVel.Y;
                    float b = -2f * (relPos.X * relVel.X + relPos.Y * relVel.Y);
                    float c = -distSq;

                    float travelTime = SolveQuadratic(a, b, c);
                    if (travelTime <= 0f) continue;

                    Vector2 predictedPos = playerPos.Value + playerVel.Value * travelTime;
                    var toPredicted = predictedPos - turretPos.Value;
                    float distToPredictedSq = toPredicted.X * toPredicted.X + toPredicted.Y * toPredicted.Y;

                    if (distToPredictedSq > firingRangeSq) continue;

                    Vector2 aimDir = (toPredicted - enemyVelocity * travelTime) / (enemyShip.TurretAmmoSpeed * travelTime);
                    float dot = Vector2.Dot(forwardDir, aimDir);
                    if (dot < cosHalfArc) continue;

                    nearestTarget = (aimDir, predictedPos, 10f);
                    nearestDistSq = distSq;
                }
            }

            CheckTargets(em.GetEntitiesWithComponents<EnemyMine>(), m => m.Radius, firingRangeSq);
        }

        return nearestTarget;

        void CheckTargets<T>(IEnumerable<(Entity Entity, T Value)> candidates, Func<T, float> getRadius, float checkRangeSq)
        {
            foreach (var (candidateEntity, value) in candidates)
            {
                var pos = em.GetComponent<Position>(candidateEntity);
                var diff = pos.Value - turretPos.Value;
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

    private static void FindTargetWithPrediction(EntityManager em, Vector2 turretPos, Vector2 forwardDir, float cosHalfArc, float rangeSq, Vector2 playerVelocity, float ammoSpeed, ref (Vector2 AimDirection, Vector2 PredictedPosition, float Radius)? nearestTarget, ref float nearestDistSq)
    {
        foreach (var (mineEntity, mine, velocity) in em.GetEntitiesWithComponents<EnemyMine, Velocity>())
        {
            var minePos = em.GetComponent<Position>(mineEntity);
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

        foreach (var (enemyShipEntity, enemyShip, velocity) in em.GetEntitiesWithComponents<EnemyShip, Velocity>())
        {
            var shipPos = em.GetComponent<Position>(enemyShipEntity);
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

        foreach (var (enemyShipEntity, enemyShip) in em.GetEntitiesWithComponents<EnemyShip>())
        {
            if (!em.HasComponent<Velocity>(enemyShipEntity))
            {
                var shipPos = em.GetComponent<Position>(enemyShipEntity);
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
    }

    private static void FindTargetWithoutPrediction(EntityManager em, Vector2 turretPos, Vector2 forwardDir, float cosHalfArc, float rangeSq, ref (Vector2 AimDirection, Vector2 PredictedPosition, float Radius)? nearestTarget, ref float nearestDistSq)
    {
        foreach (var (mineEntity, mine, velocity) in em.GetEntitiesWithComponents<EnemyMine, Velocity>())
        {
            var minePos = em.GetComponent<Position>(mineEntity);
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

        foreach (var (enemyShipEntity, enemyShip, velocity) in em.GetEntitiesWithComponents<EnemyShip, Velocity>())
        {
            var shipPos = em.GetComponent<Position>(enemyShipEntity);
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

        foreach (var (enemyShipEntity, enemyShip) in em.GetEntitiesWithComponents<EnemyShip>())
        {
            if (!em.HasComponent<Velocity>(enemyShipEntity))
            {
                var shipPos = em.GetComponent<Position>(enemyShipEntity);
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
    }

    private void FireAtTarget(EntityManager em, Entity turretEntity, Turret turret, (Vector2 AimDirection, Vector2 PredictedPosition, float Radius) target)
    {
        var turretPos = em.GetComponent<Position>(turretEntity);
        var turretRot = em.GetComponent<Rotation>(turretEntity);

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

            float speedVariation = 1f + (Random.Shared.NextSingle() - 0.5f) * 0.3f;

            Vector2 spawnOffset = pelletDir * 20f;
            if (turret.IsEnemy && em.HasComponent<EnemyShip>(turretEntity))
            {
                var ship = em.GetComponent<EnemyShip>(turretEntity);
                float forwardOffset = ship.Radius + 5f;
                Vector2 forwardDir = new Vector2((float)Math.Sin(turretRot.Angle), -(float)Math.Cos(turretRot.Angle));
                spawnOffset += forwardDir * forwardOffset;
            }

            var spawnPos = turretPos.Value + spawnOffset;
            Vector2 ammoVel = pelletDir * turret.Weapon.AmmoSpeed * speedVariation;

            if (!turret.IsEnemy)
            {
                foreach (var (_, _, vel) in em.GetEntitiesWithComponents<Player, Velocity>())
                {
                    ammoVel += vel.Value;
                    break;
                }
            }

            float ammoRadius = GetAmmoRadius(turret);
            int damage = turret.Weapon.Damage;

            var ammoEntity = em.CreateEntity();
            em.AddComponent(ammoEntity, new Position(spawnPos));
            em.AddComponent(ammoEntity, new Velocity(ammoVel));
            em.AddComponent(ammoEntity, new Ammo(ammoVel, ammoRadius, turret.Weapon.ShotLifetime, turret.IsEnemy, damage));
        }

        if (turret.Weapon.KickbackForce > 0)
        {
            Vector2 kickbackDir = new Vector2(-ammoDir.X, -ammoDir.Y);
            var playerTuples = em.GetEntitiesWithComponents<Player>().ToList();
            foreach (var (playerEntity, _) in playerTuples)
            {
                if (em.HasComponent<Velocity>(playerEntity))
                {
                    var currentVel = em.GetComponent<Velocity>(playerEntity).Value;
                    em.AddComponent(playerEntity, new Velocity(currentVel + kickbackDir * turret.Weapon.KickbackForce));
                }
            }
        }
    }

    private static float GetAmmoRadius(Turret turret)
    {
        if (turret.IsEnemy)
        {
            return 2.5f;
        }

        return turret.WeaponName switch
        {
            "RailGun" => 3.75f,
            "AcidBubbleSpray" => 5f,
            _ => 2.5f
        };
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
