using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

public class PickupMagnetSystem : GameSystem
{
    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        if (!view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple)) return;
        Entity playerEntity = playerTuple.Entity;

        var playerPos = view.GetComponent<Position>(playerEntity);

        ProcessXpPickups(view, playerEntity, playerPos.Value, deltaTime, commands);
        ProcessHealthOrbs(view, playerEntity, playerPos.Value, deltaTime, commands);
        ProcessRerollTokens(view, playerEntity, playerPos.Value, deltaTime, commands);
    }

    private void ProcessXpPickups(WorldView view, Entity playerEntity, Vector2 playerPos, float deltaTime, CommandBuffer commands)
    {
        var playerStats = view.GetComponent<Player>(playerEntity);
        int totalXpGained = 0;

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

            bool insideRadius = dist < playerStats.PickupRadius;
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

            float collectionDist = playerStats.Radius + pickup.Radius;
            if (dist < collectionDist)
            {
                totalXpGained += pickup.XpAmount;
                commands.Add(new DestroyEntityCommand(pickupEntity));
                continue;
            }

            var normalizedDir = diff / dist;
            var newVel = normalizedDir * LootStats.MaxMagnetSpeed;

            var newPos = pos.Value + newVel * deltaTime;
            commands.Add(new AddComponentCommand<Position>(pickupEntity, new Position(newPos)));
            commands.Add(new AddComponentCommand<Velocity>(pickupEntity, new Velocity(newVel)));
            commands.Add(new AddComponentCommand<XpPickup>(pickupEntity, new XpPickup(pickup.XpAmount, newLifetime, pickup.Radius, true)));
        }

        if (totalXpGained > 0)
        {
            // Mutate directly so LevelUpSystem (same phase, runs later) reads the fresh value.
            ref Player player = ref view.GetComponentRef<Player>(playerEntity);
            player = player with { Xp = player.Xp + totalXpGained };
        }
    }

    private void ProcessHealthOrbs(WorldView view, Entity playerEntity, Vector2 playerPos, float deltaTime, CommandBuffer commands)
    {
        var playerStats = view.GetComponent<Player>(playerEntity);
        int totalHeal = 0;

        foreach (var (orbEntity, orb, pos) in view.GetEntitiesWithComponents<HealthOrb, Position>())
        {
            float newLifetime = orb.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(orbEntity));
                continue;
            }

            float dist = PullTowardsPlayer(view, commands, orbEntity, playerPos, playerStats.PickupRadius, deltaTime);

            if (dist < playerStats.Radius + orb.Radius)
            {
                totalHeal += LootStats.HealthOrbHealAmount;
                SpawnHealSparks(position: pos.Value, commands);
                commands.Add(new DestroyEntityCommand(orbEntity));
                continue;
            }

            commands.Add(new AddComponentCommand<HealthOrb>(orbEntity, new HealthOrb(newLifetime, orb.Radius)));
        }

        if (totalHeal > 0)
        {
            var health = view.GetComponent<Health>(playerEntity);
            int healed = Math.Min(health.Current + totalHeal, health.Max);
            commands.Add(new AddComponentCommand<Health>(playerEntity, new Health(healed, health.Max)));
        }
    }

    // Ship salvage: the same accelerating pull as a health orb, and one charge per capsule collected.
    // The stock is a component of its own rather than a `Player` field — `ProcessXpPickups` ref-writes
    // `Player.Xp` in this phase, so a queued `Player` write built from a pre-phase copy would drop it.
    private void ProcessRerollTokens(WorldView view, Entity playerEntity, Vector2 playerPos, float deltaTime, CommandBuffer commands)
    {
        var playerStats = view.GetComponent<Player>(playerEntity);
        int collected = 0;

        foreach (var (tokenEntity, token, _) in view.GetEntitiesWithComponents<RerollToken, Position>())
        {
            float newLifetime = token.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(tokenEntity));
                continue;
            }

            float dist = PullTowardsPlayer(view, commands, tokenEntity, playerPos, playerStats.PickupRadius, deltaTime);

            if (dist < playerStats.Radius + token.Radius)
            {
                collected++;
                commands.Add(new DestroyEntityCommand(tokenEntity));
                continue;
            }

            // The capsule tumbles where it lies. The angle is simulated, not read off a clock, so a
            // seeded run draws the same pose in a screenshot twice; a capsule that fell on a later tick
            // starts turning later, so capsules never sit parallel.
            commands.Add(new AddComponentCommand<RerollToken>(
                tokenEntity, new RerollToken(newLifetime, token.Radius, token.Spin + LootStats.RerollTokenSpinRate * deltaTime)));
        }

        if (collected > 0)
        {
            var stock = view.TryGetComponent<RerollStock>(playerEntity, out var held) ? held : RerollStock.Empty;
            commands.Add(new AddComponentCommand<RerollStock>(playerEntity, stock.With(stock.Count + collected)));
        }
    }

    // Accelerates one pickup toward the hull while it is inside the magnet radius, and reports the
    // distance so the caller can decide whether it has been collected. The health orb and the salvage
    // capsule share this pull; XP orbs do not — they are the many-entity loop and move at a constant
    // magnet speed instead, which is what makes them grabbable at any ship speed.
    private static float PullTowardsPlayer(WorldView view, CommandBuffer commands, Entity pickup, Vector2 playerPos, float magnetRadius, float deltaTime)
    {
        var pos = view.GetComponent<Position>(pickup);
        var diff = playerPos - pos.Value;
        float dist = (float)Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);

        if (dist <= 0f || dist >= magnetRadius) return dist;

        Vector2 currentVel = view.TryGetComponent<Velocity>(pickup, out var vel) ? vel.Value : Vector2.Zero;
        var newVel = currentVel + (diff / dist) * LootStats.MagnetAcceleration * deltaTime;

        float speed = (float)Math.Sqrt(newVel.X * newVel.X + newVel.Y * newVel.Y);
        if (speed > LootStats.MaxMagnetSpeed) newVel = newVel / speed * LootStats.MaxMagnetSpeed;

        commands.Add(new AddComponentCommand<Position>(pickup, new Position(pos.Value + newVel * deltaTime)));
        commands.Add(new AddComponentCommand<Velocity>(pickup, new Velocity(newVel)));
        return dist;
    }

    private void SpawnHealSparks(Vector2 position, CommandBuffer commands)
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

            commands.AddEntity(new Position(position), new Velocity(velocity), new HealSpark(0.6f));
        }
    }
}
