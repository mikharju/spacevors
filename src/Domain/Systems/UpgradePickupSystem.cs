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
                // Apply upgrade to player's weapon
                var currentWeapon = em.GetComponent<Weapon>(playerEntity);
                switch (upgrade.Type)
                {
                    case UpgradeType.FireRate:
                        currentWeapon = new Weapon(
                            currentWeapon.FireRate,
                            currentWeapon.AmmoSpeed,
                            currentWeapon.KickbackForce,
                            currentWeapon.UpgradeFireRateMultiplier * 1.5f,
                            currentWeapon.UpgradeProjectileSpeedMultiplier);
                        break;
                    case UpgradeType.ProjectileSpeed:
                        currentWeapon = new Weapon(
                            currentWeapon.FireRate,
                            currentWeapon.AmmoSpeed,
                            currentWeapon.KickbackForce,
                            currentWeapon.UpgradeFireRateMultiplier,
                            currentWeapon.UpgradeProjectileSpeedMultiplier * 1.5f);
                        break;
                }
                em.AddComponent(playerEntity, currentWeapon);

                // Remove upgrade pickup
                em.DestroyEntity(upgradeEntity);
            }
            else
            {
                // Update lifetime
                em.AddComponent(upgradeEntity, new Upgrade(upgrade.Type, newLifetime, upgrade.Radius));
            }
        }
    }
}
