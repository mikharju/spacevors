using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class PickupMagnetSystem : GameSystem
{
    const float MagnetAcceleration = 800f;
    const float MaxMagnetSpeed = 350f;

    public override void Update(EntityManager em, float deltaTime)
    {
        var playerTuple = em.GetEntitiesWithComponents<Player, Position>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        if (playerEntity.Value < 0) return;

        var playerPos = em.GetComponent<Position>(playerEntity);
        var playerStats = em.GetComponent<Player>(playerEntity);
        float pickupRadius = playerStats.PickupRadius;

        ProcessXpPickups(em, playerEntity, playerPos.Value, pickupRadius, playerStats.Radius, deltaTime);
        ProcessHealthOrbs(em, playerEntity, playerPos.Value, pickupRadius, playerStats.Radius, deltaTime);
    }

    private void ProcessXpPickups(EntityManager em, Entity playerEntity, Vector2 playerPos, float pickupRadius, float playerRadius, float deltaTime)
    {
        var pickups = em.GetEntitiesWithComponents<XpPickup, Position>().ToList();

        foreach (var (pickupEntity, pickup, pos) in pickups)
        {
            if (!em.HasComponent<XpPickup>(pickupEntity)) continue;

            float newLifetime = pickup.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                em.DestroyEntity(pickupEntity);
                continue;
            }

            var diff = playerPos - pos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float dist = (float)Math.Sqrt(distSq);

            bool insideRadius = dist < pickupRadius + pickup.Radius;
            bool isChased = pickup.Chased;

            if (!insideRadius && !isChased)
            {
                em.AddComponent(pickupEntity, new XpPickup(pickup.XpAmount, newLifetime, pickup.Radius, false));
                continue;
            }

            if (insideRadius && !isChased)
            {
                em.AddComponent(pickupEntity, new XpPickup(pickup.XpAmount, newLifetime, pickup.Radius, true));
            }

            float collectionDist = playerRadius + pickup.Radius;
            if (dist < collectionDist)
            {
                ApplyXp(em, playerEntity, pickup.XpAmount);
                em.DestroyEntity(pickupEntity);
                continue;
            }

            var normalizedDir = diff / dist;
            var newVel = normalizedDir * MaxMagnetSpeed;

            var newPos = pos.Value + newVel * deltaTime;
            em.AddComponent(pickupEntity, new Position(newPos));
            em.AddComponent(pickupEntity, new Velocity(newVel));
            em.AddComponent(pickupEntity, new XpPickup(pickup.XpAmount, newLifetime, pickup.Radius, true));
        }
    }

    private void ProcessHealthOrbs(EntityManager em, Entity playerEntity, Vector2 playerPos, float pickupRadius, float playerRadius, float deltaTime)
    {
        var orbs = em.GetEntitiesWithComponents<HealthOrb, Position>().ToList();

        foreach (var (orbEntity, orb, pos) in orbs)
        {
            if (!em.HasComponent<HealthOrb>(orbEntity)) continue;

            float newLifetime = orb.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                em.DestroyEntity(orbEntity);
                continue;
            }

            var diff = playerPos - pos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float dist = (float)Math.Sqrt(distSq);

            if (dist < pickupRadius + orb.Radius)
            {
                var currentVel = em.HasComponent<Velocity>(orbEntity) ? em.GetComponent<Velocity>(orbEntity).Value : Vector2.Zero;
                var accel = (diff / dist) * MagnetAcceleration;
                var newVel = currentVel + accel * deltaTime;
                float speed = (float)Math.Sqrt(newVel.X * newVel.X + newVel.Y * newVel.Y);

                if (speed > MaxMagnetSpeed)
                {
                    newVel = newVel / speed * MaxMagnetSpeed;
                }

                var newPos = pos.Value + newVel * deltaTime;
                em.AddComponent(orbEntity, new Position(newPos));
                em.AddComponent(orbEntity, new Velocity(newVel));
            }

            float collectionDist = playerRadius + orb.Radius;
            if (dist < collectionDist)
            {
                ApplyHealth(em, playerEntity);
                SpawnGreenExplosion(em, pos.Value);
                em.DestroyEntity(orbEntity);
            }
        }
    }

    private void ApplyXp(EntityManager em, Entity playerEntity, int xpAmount)
    {
        var playerStats = em.GetComponent<Player>(playerEntity);
        em.AddComponent(playerEntity, new Player(
            playerStats.Thrust,
            playerStats.SideThrust,
            playerStats.BackThrust,
            playerStats.Boost,
            Radius: playerStats.Radius,
            Xp: playerStats.Xp + xpAmount,
            Level: playerStats.Level,
            PickupRadius: playerStats.PickupRadius,
            RotationSpeed: playerStats.RotationSpeed));
    }

    private void ApplyHealth(EntityManager em, Entity playerEntity)
    {
        if (!em.HasComponent<Health>(playerEntity)) return;
        var health = em.GetComponent<Health>(playerEntity);
        em.AddComponent(playerEntity, new Health(health.Current + 3));
    }

    private void SpawnGreenExplosion(EntityManager em, Vector2 position)
    {
        var explosionEntity = em.CreateEntity();
        em.AddComponent(explosionEntity, new Position(position));
        em.AddComponent(explosionEntity, new Explosion(25f, 0.5f));

        for (int i = 0; i < 6; i++)
        {
            float angleOffset = ((float)i - 2.5f) * 0.3f;
            var dir = new Vector2(0f, -1f);
            float cos = (float)Math.Cos(angleOffset);
            float sin = (float)Math.Sin(angleOffset);
            var sparkDir = new Vector2(dir.X * cos - dir.Y * sin, dir.X * sin + dir.Y * cos);

            float speed = 80f + i * 25f;
            var velocity = sparkDir * speed;

            var sparkEntity = em.CreateEntity();
            em.AddComponent(sparkEntity, new Position(position));
            em.AddComponent(sparkEntity, new Velocity(velocity));
            em.AddComponent(sparkEntity, new GreenSpark(0.6f));
        }
    }
}
