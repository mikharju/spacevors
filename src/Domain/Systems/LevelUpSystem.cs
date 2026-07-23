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
                PickupRadius: playerStats.PickupRadius));
        }
    }

    private void SpawnLevelUpChoice(EntityManager em, Entity playerEntity, Vector2 position)
    {
        var allOptions = new[] { UpgradeOption.FireRate, UpgradeOption.ProjectileSpeed, UpgradeOption.PickupRadius };
        var shuffled = allOptions.OrderBy(_ => Random.Shared.Next()).ToArray();

        var choiceEntity = em.CreateEntity();
        em.AddComponent(choiceEntity, new Position(position));
        em.AddComponent(choiceEntity, new PendingChoice());
        em.AddComponent(choiceEntity, new PendingUpgradeOptions(shuffled[0], shuffled[1]));
    }
}
