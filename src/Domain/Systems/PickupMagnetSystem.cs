using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class PickupMagnetSystem : GameSystem
{
    const float MagnetAcceleration = 800f;
    const float MaxMagnetSpeed = 350f;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var playerTuple = view.GetEntitiesWithComponents<Player, Position>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        if (playerEntity.Value < 0) return;

        var playerPos = view.GetComponent<Position>(playerEntity);
        var playerStats = view.GetComponent<Player>(playerEntity);
        float pickupRadius = playerStats.PickupRadius;

        ProcessXpPickups(view, playerEntity, playerPos.Value, pickupRadius, playerStats.Radius, deltaTime, commands);
        ProcessHealthOrbs(view, playerEntity, playerPos.Value, pickupRadius, playerStats.Radius, deltaTime, commands);
    }

    private void ProcessXpPickups(WorldView view, Entity playerEntity, Vector2 playerPos, float pickupRadius, float playerRadius, float deltaTime, CommandBuffer commands)
    {
        foreach (var (pickupEntity, pickup, pos) in view.GetEntitiesWithComponents<XpPickup, Position>())
        {
            float newLifetime = pickup.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(pickupEntity));
                continue;
            }

            var diff = playerPos - pos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float dist = (float)Math.Sqrt(distSq);

            bool insideRadius = dist < pickupRadius + pickup.Radius;
            bool isChased = pickup.Chased;

            if (!insideRadius && !isChased)
            {
                commands.Add(new AddComponentCommand<XpPickup>(pickupEntity, new XpPickup(pickup.XpAmount, newLifetime, pickup.Radius, false)));
                continue;
            }

            if (insideRadius && !isChased)
            {
                commands.Add(new AddComponentCommand<XpPickup>(pickupEntity, new XpPickup(pickup.XpAmount, newLifetime, pickup.Radius, true)));
            }

            float collectionDist = playerRadius + pickup.Radius;
            if (dist < collectionDist)
            {
                ApplyXp(view, playerEntity, pickup.XpAmount, commands);
                commands.Add(new DestroyEntityCommand(pickupEntity));
                continue;
            }

            var normalizedDir = diff / dist;
            var newVel = normalizedDir * MaxMagnetSpeed;

            var newPos = pos.Value + newVel * deltaTime;
            commands.Add(new AddComponentCommand<Position>(pickupEntity, new Position(newPos)));
            commands.Add(new AddComponentCommand<Velocity>(pickupEntity, new Velocity(newVel)));
            commands.Add(new AddComponentCommand<XpPickup>(pickupEntity, new XpPickup(pickup.XpAmount, newLifetime, pickup.Radius, true)));
        }
    }

    private void ProcessHealthOrbs(WorldView view, Entity playerEntity, Vector2 playerPos, float pickupRadius, float playerRadius, float deltaTime, CommandBuffer commands)
    {
        foreach (var (orbEntity, orb, pos) in view.GetEntitiesWithComponents<HealthOrb, Position>())
        {
            float newLifetime = orb.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(orbEntity));
                continue;
            }

            var diff = playerPos - pos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float dist = (float)Math.Sqrt(distSq);

            if (dist < pickupRadius + orb.Radius)
            {
                Vector2 currentVel = view.TryGetComponent<Velocity>(orbEntity, out var vel) ? vel.Value : Vector2.Zero;
                var accel = (diff / dist) * MagnetAcceleration;
                var newVel = currentVel + accel * deltaTime;
                float speed = (float)Math.Sqrt(newVel.X * newVel.X + newVel.Y * newVel.Y);

                if (speed > MaxMagnetSpeed)
                {
                    newVel = newVel / speed * MaxMagnetSpeed;
                }

                var newPos = pos.Value + newVel * deltaTime;
                commands.Add(new AddComponentCommand<Position>(orbEntity, new Position(newPos)));
                commands.Add(new AddComponentCommand<Velocity>(orbEntity, new Velocity(newVel)));
            }

            float collectionDist = playerRadius + orb.Radius;
            if (dist < collectionDist)
            {
                ApplyHealth(view, playerEntity, commands);
                SpawnGreenExplosion(view, position: pos.Value, commands);
                commands.Add(new DestroyEntityCommand(orbEntity));
            }
        }
    }

    private void ApplyXp(WorldView view, Entity playerEntity, int xpAmount, CommandBuffer commands)
    {
        var playerStats = view.GetComponent<Player>(playerEntity);
        commands.Add(new AddComponentCommand<Player>(playerEntity, new Player(
            playerStats.Thrust,
            playerStats.SideThrust,
            playerStats.BackThrust,
            playerStats.Boost,
            Radius: playerStats.Radius,
            Xp: playerStats.Xp + xpAmount,
            Level: playerStats.Level,
            PickupRadius: playerStats.PickupRadius,
            RotationSpeed: playerStats.RotationSpeed)));
    }

    private void ApplyHealth(WorldView view, Entity playerEntity, CommandBuffer commands)
    {
        var health = view.GetComponent<Health>(playerEntity);
        commands.Add(new AddComponentCommand<Health>(playerEntity, new Health(health.Current + 3)));
    }

    private void SpawnGreenExplosion(WorldView view, Vector2 position, CommandBuffer commands)
    {
        for (int i = 0; i < 6; i++)
        {
            float angleOffset = ((float)i - 2.5f) * 0.3f;
            var dir = new Vector2(0f, -1f);
            float cos = (float)Math.Cos(angleOffset);
            float sin = (float)Math.Sin(angleOffset);
            var sparkDir = new Vector2(dir.X * cos - dir.Y * sin, dir.X * sin + dir.Y * cos);

            float speed = 80f + i * 25f;
            var velocity = sparkDir * speed;

            commands.AddEntity(new Position(position), new Velocity(velocity), new GreenSpark(0.6f));
        }
    }
}
