using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class TurretFiringSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        var turrets = em.GetEntitiesWithComponents<Turret>().ToList();

        foreach (var (turretEntity, turret) in turrets)
        {
            var cooldown = GetCooldown(em, turretEntity);

            if (cooldown <= 0f)
            {
                var target = FindTarget(em, turret, turretEntity);

                if (target.HasValue)
                {
                    FireAtTarget(em, turretEntity, turret, target.Value);
                    SetCooldown(em, turretEntity, 1f / turret.FireRate);
                }
            }
            else if (cooldown > 0f)
            {
                var newCooldown = cooldown - deltaTime;
                SetCooldown(em, turretEntity, Math.Max(newCooldown, 0f));
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
            foreach (var (mineEntity, mine) in em.GetEntitiesWithComponents<EnemyMine>())
            {
                var minePos = em.GetComponent<Position>(mineEntity);
                var toEnemy = minePos.Value - turretPos.Value;
                float distSq = toEnemy.X * toEnemy.X + toEnemy.Y * toEnemy.Y;

                if (distSq > rangeSq || distSq < 0.001f) continue;

                float dist = (float)Math.Sqrt(distSq);
                var toEnemyDir = toEnemy / dist;

                float dot = Vector2.Dot(forwardDir, toEnemyDir);
                if (dot < cosHalfArc) continue;

                if (distSq < nearestDistSq)
                {
                    nearestTarget = (minePos.Value, mine.Radius);
                    nearestDistSq = distSq;
                }
            }

            foreach (var (asteroidEntity, asteroid) in em.GetEntitiesWithComponents<Asteroid>())
            {
                var asteroidPos = em.GetComponent<Position>(asteroidEntity);
                var toEnemy = asteroidPos.Value - turretPos.Value;
                float distSq = toEnemy.X * toEnemy.X + toEnemy.Y * toEnemy.Y;

                if (distSq > rangeSq || distSq < 0.001f) continue;

                float dist = (float)Math.Sqrt(distSq);
                var toEnemyDir = toEnemy / dist;

                float dot = Vector2.Dot(forwardDir, toEnemyDir);
                if (dot < cosHalfArc) continue;

                if (distSq < nearestDistSq)
                {
                    nearestTarget = (asteroidPos.Value, asteroid.Radius);
                    nearestDistSq = distSq;
                }
            }
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

            if (playerEntity.Value >= 0 && em.HasComponent<EnemyShip>(turretEntity))
            {
                var ship = em.GetComponent<EnemyShip>(turretEntity);
                var playerPos = em.GetComponent<Position>(playerEntity);
                var toPlayer = playerPos.Value - turretPos.Value;
                float distSq = toPlayer.X * toPlayer.X + toPlayer.Y * toPlayer.Y;

                if (distSq <= ship.FiringRange * ship.FiringRange && distSq > 0.001f)
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

            foreach (var (mineEntity, mine) in em.GetEntitiesWithComponents<EnemyMine>())
            {
                var minePos = em.GetComponent<Position>(mineEntity);
                var toTarget = minePos.Value - turretPos.Value;
                float distSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y;

                if (distSq > rangeSq || distSq < 0.001f) continue;

                float dist = (float)Math.Sqrt(distSq);
                var toTargetDir = toTarget / dist;

                float dot = Vector2.Dot(forwardDir, toTargetDir);
                if (dot < cosHalfArc) continue;

                if (distSq < nearestDistSq)
                {
                    nearestTarget = (minePos.Value, mine.Radius);
                    nearestDistSq = distSq;
                }
            }
        }

        return nearestTarget;
    }

    private void FireAtTarget(EntityManager em, Entity turretEntity, Turret turret, (Vector2 Position, float Radius) target)
    {
        var turretPos = em.GetComponent<Position>(turretEntity);
        var turretRot = em.GetComponent<Rotation>(turretEntity);

        Vector2 dirToTarget = target.Position - turretPos.Value;
        float dist = (float)Math.Sqrt(dirToTarget.X * dirToTarget.X + dirToTarget.Y * dirToTarget.Y);
        Vector2 ammoDir = dirToTarget / dist;

        var spawnPos = turretPos.Value + ammoDir * 20f;
        Vector2 ammoVel = ammoDir * turret.AmmoSpeed;

        var ammoEntity = em.CreateEntity();
        em.AddComponent(ammoEntity, new Position(spawnPos));
        em.AddComponent(ammoEntity, new Velocity(ammoVel));
        em.AddComponent(ammoEntity, new Ammo(ammoVel, 2.5f, 3f, turret.IsEnemy));

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

    private static float GetCooldown(EntityManager em, Entity entity)
    {
        if (em.HasComponent<FireCooldown>(entity))
        {
            return em.GetComponent<FireCooldown>(entity).Timer;
        }
        return 0f;
    }

    private static void SetCooldown(EntityManager em, Entity entity, float value)
    {
        em.AddComponent(entity, new FireCooldown(value));
    }
}
