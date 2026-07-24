using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class LevelUpSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        if (playerEntity.Value < 0) return;

        if (!em.HasComponent<Position>(playerEntity)) return;
        var playerPos = em.GetComponent<Position>(playerEntity);

        var playerStats = em.GetComponent<Player>(playerEntity);
        int xpThreshold = playerStats.Level * 10;

        if (playerStats.Xp >= xpThreshold)
        {
            SpawnLevelUpChoice(em, playerEntity, playerPos.Value);
            em.AddComponent(playerEntity, new Player(
                playerStats.Thrust,
                playerStats.SideThrust,
                playerStats.BackThrust,
                playerStats.Boost,
                Radius: playerStats.Radius,
                Xp: playerStats.Xp,
                Level: playerStats.Level + 1,
                PickupRadius: playerStats.PickupRadius,
                RotationSpeed: playerStats.RotationSpeed));
        }
    }

    private void SpawnLevelUpChoice(EntityManager em, Entity playerEntity, Vector2 position)
    {
        var turrets = em.GetEntitiesWithComponents<Turret>()
            .Where(t => !em.GetComponent<Turret>(t.Entity).IsEnemy)
            .ToList();

        if (turrets.Count == 0) return;

        var weaponNames = turrets.Select(t => em.GetComponent<Turret>(t.Entity).WeaponName).Distinct().ToList();
        var allOptions = new List<UpgradableOption>();

        foreach (var weapon in weaponNames)
        {
            allOptions.Add(new UpgradableOption(weapon, UpgradeOption.FireRate));
            allOptions.Add(new UpgradableOption(weapon, UpgradeOption.ProjectileSpeed));
        }

        var firstWeapon = weaponNames[0];
        allOptions.Add(new UpgradableOption(firstWeapon, UpgradeOption.PickupRadius));

        var shuffled = allOptions.OrderBy(_ => Random.Shared.Next()).ToArray();
        int count = Math.Min(2, shuffled.Length);

        var choiceEntity = em.CreateEntity();
        em.AddComponent(choiceEntity, new Position(position));
        em.AddComponent(choiceEntity, new PendingChoice());
        em.AddComponent(choiceEntity, new PendingUpgradeOptions(shuffled[0], shuffled[count - 1]));
    }
}
