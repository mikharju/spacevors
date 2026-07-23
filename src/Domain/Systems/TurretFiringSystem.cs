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
                    CooldownHelper.SetCooldown(em, turretEntity, 1f / turret.FireRate);
                }
            }
            else if (cooldown > 0f)
            {
                var newCooldown = cooldown - deltaTime;
                CooldownHelper.SetCooldown(em, turretEntity, Math.Max(newCooldown, 0f));
            }
        }
    }

    private (Vector2 Position, float Radius)? FindTarget(EntityManager em, Turret turret, Entity turretEntity)
    {
        var turretPos = em.GetComponent<Position>(turretEntity);
        var turretRot = em.GetComponent<Rotation>(turretEntity);

        Vector2 forwardDir = new Vector2((float)Math.Sin(turretRot.Angle), -(float)Math.Cos(turretRot.Angle));

        float cosHalfArc = (float)Math.Cos(turret.ArcAngle / 2f);
        float rangeSq = turret.Range * turret.Range;

        (Vector2 Position, float Radius)? nearestTarget = null;
        float nearestDistSq = float.MaxValue;

        if (!turret.IsEnemy)
        {
            CheckTargets(em.GetEntitiesWithComponents<EnemyMine>(), m => m.Radius, rangeSq);
            CheckTargets(em.GetEntitiesWithComponents<Asteroid>(), a => a.Radius, rangeSq);
        }

        if (!turret.IsEnemy)
        {
            foreach (var (enemyShipEntity, enemyShip) in em.GetEntitiesWithComponents<EnemyShip>())
            {
                var shipPos = em.GetComponent<Position>(enemyShipEntity);
                var toTarget = shipPos.Value - turretPos.Value;
                float distSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y;

                if (distSq > rangeSq || distSq < 0.001f) continue;

                float dist = (float)Math.Sqrt(distSq);
                var toTargetDir = toTarget / dist;

                float dot = Vector2.Dot(forwardDir, toTargetDir);
                if (dot < cosHalfArc) continue;

                if (distSq < nearestDistSq)
                {
                    nearestTarget = (shipPos.Value, enemyShip.Radius);
                    nearestDistSq = distSq;
                }
            }
        }
        else
        {
            var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
            Entity playerEntity = playerTuple.Entity;

            EnemyShip enemyShip = em.HasComponent<EnemyShip>(turretEntity) ? em.GetComponent<EnemyShip>(turretEntity) : new EnemyShip(0, 0, 0, 0, 0, 300f, 0, 0, 0, 0);
            float firingRangeSq = enemyShip.FiringRange * enemyShip.FiringRange;

            if (playerEntity.Value >= 0 && em.HasComponent<EnemyShip>(turretEntity))
            {
                var playerPos = em.GetComponent<Position>(playerEntity);
                var toPlayer = playerPos.Value - turretPos.Value;
                float distSq = toPlayer.X * toPlayer.X + toPlayer.Y * toPlayer.Y;

                if (distSq <= firingRangeSq && distSq > 0.001f)
                {
                    float dist = (float)Math.Sqrt(distSq);
                    var toPlayerDir = toPlayer / dist;

                    float dot = Vector2.Dot(forwardDir, toPlayerDir);
                    if (dot >= cosHalfArc)
                    {
                        nearestTarget = (playerPos.Value, em.GetComponent<Player>(playerEntity).Radius);
                        nearestDistSq = distSq;
                    }
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
                    nearestTarget = (pos.Value, getRadius(value));
                    nearestDistSq = distSq;
                }
            }
        }
    }

    private void FireAtTarget(EntityManager em, Entity turretEntity, Turret turret, (Vector2 Position, float Radius) target)
    {
        var turretPos = em.GetComponent<Position>(turretEntity);
        var turretRot = em.GetComponent<Rotation>(turretEntity);

        Vector2 dirToTarget = target.Position - turretPos.Value;
        float dist = (float)Math.Sqrt(dirToTarget.X * dirToTarget.X + dirToTarget.Y * dirToTarget.Y);
        Vector2 ammoDir = dirToTarget / dist;

        int pelletCount = turret.PelletCount;
        float scatterAngle = pelletCount > 1 ? 0.1f : 0.033f;

        for (int i = 0; i < pelletCount; i++)
        {
            float angleOffset = (i - (pelletCount - 1) / 2f) * scatterAngle;
            float cosOff = (float)Math.Cos(angleOffset);
            float sinOff = (float)Math.Sin(angleOffset);

            Vector2 pelletDir = new Vector2(
                ammoDir.X * cosOff - ammoDir.Y * sinOff,
                ammoDir.X * sinOff + ammoDir.Y * cosOff
            );

            float speedVariation = 1f + (Random.Shared.NextSingle() - 0.5f) * 0.15f;

            Vector2 spawnOffset = pelletDir * 20f;
            if (turret.IsEnemy && em.HasComponent<EnemyShip>(turretEntity))
            {
                var ship = em.GetComponent<EnemyShip>(turretEntity);
                float forwardOffset = ship.Radius + 5f;
                Vector2 forwardDir = new Vector2((float)Math.Sin(turretRot.Angle), -(float)Math.Cos(turretRot.Angle));
                spawnOffset += forwardDir * forwardOffset;
            }

            var spawnPos = turretPos.Value + spawnOffset;
            Vector2 ammoVel = pelletDir * turret.AmmoSpeed * speedVariation;

            if (em.HasComponent<Velocity>(turretEntity))
            {
                var turretVel = em.GetComponent<Velocity>(turretEntity).Value;
                ammoVel += turretVel;
            }

            float ammoRadius = turret.IsEnemy ? (em.HasComponent<EnemyShip>(turretEntity) ? em.GetComponent<EnemyShip>(turretEntity).Damage > 1 ? 4f : 2.5f : 2.5f) : 2.5f;
            int damage = turret.IsEnemy && em.HasComponent<EnemyShip>(turretEntity) ? em.GetComponent<EnemyShip>(turretEntity).Damage : 1;

            var ammoEntity = em.CreateEntity();
            em.AddComponent(ammoEntity, new Position(spawnPos));
            em.AddComponent(ammoEntity, new Velocity(ammoVel));
            em.AddComponent(ammoEntity, new Ammo(ammoVel, ammoRadius, 3f, turret.IsEnemy, damage));
        }

        if (turret.KickbackForce > 0)
        {
            Vector2 kickbackDir = new Vector2(-ammoDir.X, -ammoDir.Y);
            var playerTuples = em.GetEntitiesWithComponents<Player>().ToList();
            foreach (var (playerEntity, _) in playerTuples)
            {
                if (em.HasComponent<Velocity>(playerEntity))
                {
                    var currentVel = em.GetComponent<Velocity>(playerEntity).Value;
                    em.AddComponent(playerEntity, new Velocity(currentVel + kickbackDir * turret.KickbackForce));
                }
            }
        }
    }

}
