using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class UpgradePickupSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        if (!hasPlayer) return;

        var playerPos = em.GetComponent<Position>(playerEntity);
        var playerStats = em.GetComponent<Player>(playerEntity);
        float playerRadius = playerStats.Radius;

        var upgrades = em.GetEntitiesWithComponents<Upgrade, Position>().ToList();

        foreach (var (upgradeEntity, upgrade, _) in upgrades)
        {
            if (!em.HasComponent<Upgrade>(upgradeEntity)) continue;

            // Handle lifetime expiry
            float newLifetime = upgrade.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                em.DestroyEntity(upgradeEntity);
                continue;
            }

            var upgradePos = em.GetComponent<Position>(upgradeEntity);
            var diff = upgradePos.Value - playerPos.Value;
            float dist = (float)Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);

            if (dist < playerRadius + upgrade.Radius)
            {
                SpawnUpgradeExplosion(em, upgradePos.Value);
                SpawnUpgradeSparks(em, upgradePos.Value, playerPos.Value);

                var choiceEntity = em.CreateEntity();
                em.AddComponent(choiceEntity, new Position(upgradePos.Value));
                em.AddComponent(choiceEntity, new PendingChoice());

                em.DestroyEntity(upgradeEntity);
            }
            else
            {
                // Update lifetime
                em.AddComponent(upgradeEntity, new Upgrade(upgrade.Type, newLifetime, upgrade.Radius));
            }
        }
    }

    private void SpawnUpgradeExplosion(EntityManager em, Vector2 position)
    {
        var explosionEntity = em.CreateEntity();
        em.AddComponent(explosionEntity, new Position(position));
        em.AddComponent(explosionEntity, new UpgradeExplosion(30f, 0.5f));
    }

    private void SpawnUpgradeSparks(EntityManager em, Vector2 sparkPos, Vector2 targetPos)
    {
        var dir = targetPos - sparkPos;
        float dist = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (dist < 0.01f) return;

        var normalizedDir = dir / dist;

        for (int i = 0; i < 4; i++)
        {
            float angleOffset = ((float)i - 1.5f) * 0.3f;
            float cos = (float)Math.Cos(angleOffset);
            float sin = (float)Math.Sin(angleOffset);
            var sparkDir = new Vector2(
                normalizedDir.X * cos - normalizedDir.Y * sin,
                normalizedDir.X * sin + normalizedDir.Y * cos
            );

            float speed = 100f + i * 30f;
            var velocity = sparkDir * speed;

            var sparkEntity = em.CreateEntity();
            em.AddComponent(sparkEntity, new Position(sparkPos));
            em.AddComponent(sparkEntity, new Velocity(velocity));
            em.AddComponent(sparkEntity, new BlueSpark(0.6f));
        }
    }
}
