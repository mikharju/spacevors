using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class LevelUpSystem : GameSystem
{
    private static readonly WeaponType[] AllNewWeapons = [
        WeaponType.RailGun,
        WeaponType.TwinChainGun,
        WeaponType.AcidBubbleSpray,
        WeaponType.PointDefenceTurret];

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;
        if (playerEntity.Value < 0) return;

        var playerPos = playerTuple.Value2;

        var playerStats = view.GetComponent<Player>(playerEntity);
        int xpThreshold = playerStats.Level * 10;

        if (playerStats.Xp >= xpThreshold)
        {
            SpawnLevelUpChoice(view, playerEntity, playerPos.Value, commands);
            commands.Add(new AddComponentCommand<Player>(playerEntity, new Player(
                playerStats.Thrust,
                playerStats.SideThrust,
                playerStats.BackThrust,
                playerStats.Boost,
                Radius: playerStats.Radius,
                Xp: playerStats.Xp,
                Level: playerStats.Level + 1,
                PickupRadius: playerStats.PickupRadius,
                RotationSpeed: playerStats.RotationSpeed,
                MaxHealth: playerStats.MaxHealth)));
        }
    }

    private void SpawnLevelUpChoice(WorldView view, Entity playerEntity, Vector2 position, CommandBuffer commands)
    {
        var turrets = new List<Turret>();
        foreach (var t in view.GetEntitiesWithComponents<Turret>())
        {
            if (!t.Value1.IsEnemy) turrets.Add(t.Value1);
        }

        if (turrets.Count == 0) return;

        var weaponNames = turrets.Select(t => t.WeaponName).Distinct().ToList();
        var allOptions = new List<UpgradableOption>();

        int playerLevel = view.GetComponent<Player>(playerEntity).Level;
        bool isMilestoneLevel = playerLevel % 5 == 0;

        if (isMilestoneLevel)
        {
            allOptions.Add(new UpgradableOption("", UpgradeOption.Hp));

            var usedSlots = weaponNames.Count;
            int maxSlots = GetMaxWeaponSlots(view, playerEntity);

            if (usedSlots < maxSlots)
            {
                var availableWeapons = AllNewWeapons
                    .Where(w => !weaponNames.Contains(w.Name))
                    .ToList();

                foreach (var weapon in availableWeapons)
                {
                    allOptions.Add(new UpgradableOption(weapon.Name, UpgradeOption.Damage, IsNewWeapon: true));
                }
            }

            foreach (var weapon in weaponNames)
            {
                allOptions.Add(new UpgradableOption(weapon, UpgradeOption.Damage));
            }

            var shuffled = allOptions.OrderBy(_ => Random.Shared.Next()).ToArray();
            int count = Math.Min(3, shuffled.Length);

            commands.AddEntity(new Position(position), new PendingChoice(), new PendingUpgradeOptions(shuffled[..count]));
        }
        else
        {
            allOptions.Add(new UpgradableOption("", UpgradeOption.Hp));

            foreach (var weapon in weaponNames)
            {
                allOptions.Add(new UpgradableOption(weapon, UpgradeOption.FireRate));
                allOptions.Add(new UpgradableOption(weapon, UpgradeOption.ProjectileSpeed));
            }

            var firstWeapon = weaponNames[0];
            allOptions.Add(new UpgradableOption(firstWeapon, UpgradeOption.PickupRadius));

            allOptions.Add(new UpgradableOption("", UpgradeOption.ForwardAcceleration));
            allOptions.Add(new UpgradableOption("", UpgradeOption.TurnSpeed));
            allOptions.Add(new UpgradableOption("", UpgradeOption.SideThrust));
            allOptions.Add(new UpgradableOption("", UpgradeOption.BackThrust));

            var shuffled = allOptions.OrderBy(_ => Random.Shared.Next()).ToArray();
            int count = Math.Min(5, shuffled.Length);

            commands.AddEntity(new Position(position), new PendingChoice(), new PendingUpgradeOptions(shuffled[..count]));
        }
    }

    private static int GetMaxWeaponSlots(WorldView view, Entity playerEntity)
    {
        if (view.TryGetComponent<WeaponSlots>(playerEntity, out var ws))
        {
            return ws.Max;
        }
        return 3;
    }
}
