using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class LevelUpSystem : GameSystem
{
    const int MilestoneLevelInterval = 5;
    const string DiagnosticEnvVar = "SPACEVORS_DIAGNOSTIC";
    const string ScriptedUpgradesEnvVar = "SPACEVORS_DIAG_UPGRADES";

    private static readonly WeaponType[] AllNewWeapons = WeaponType.AddOnWeapons;

    // Diagnostics only: fixed upgrade sequence consumed one entry per level-up.
    private readonly UpgradableOption[]? _scriptedUpgrades;
    private int _scriptIndex;

    public LevelUpSystem(UpgradableOption[]? scriptedUpgrades = null)
    {
        _scriptedUpgrades = scriptedUpgrades ?? ParseScriptedUpgrades();
    }

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;
        if (playerEntity.Value < 0) return;
        if (view.TryGetComponent<Dead>(playerEntity, out _)) return;

        var playerPos = playerTuple.Value2;

        var playerStats = view.GetComponent<Player>(playerEntity);
        int xpThreshold = playerStats.Level * 10;

        if (playerStats.Xp >= xpThreshold)
        {
            int newLevel = playerStats.Level + 1;
            bool isMilestoneLevel = newLevel % MilestoneLevelInterval == 0;
            SpawnLevelUpChoice(view, playerEntity, playerPos.Value, newLevel, isMilestoneLevel, commands);
            commands.Add(new AddComponentCommand<Player>(playerEntity, playerStats with { Level = newLevel }));
        }
    }

    private void SpawnLevelUpChoice(WorldView view, Entity playerEntity, Vector2 position, int newLevel, bool isMilestoneLevel, CommandBuffer commands)
    {
        if (_scriptedUpgrades != null && _scriptIndex < _scriptedUpgrades.Length)
        {
            var scripted = _scriptedUpgrades[_scriptIndex++];
            DiagnosticLogger.LogEvent("UPGRADE", $"level={newLevel} scripted choice: {FormatChoice(scripted)}");
            commands.AddEntity(new Position(position), new PendingChoice(), new PendingUpgradeOptions([scripted]));
            return;
        }

        var turrets = new List<Turret>();
        foreach (var t in view.GetEntitiesWithComponents<Turret>())
        {
            if (!t.Value1.IsEnemy) turrets.Add(t.Value1);
        }

        if (turrets.Count == 0) return;

        var weaponNames = turrets.Select(t => t.WeaponName).Distinct().ToList();
        var allOptions = new List<UpgradableOption>();

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

            Shuffle(allOptions, view.Rng);
            var shuffled = allOptions.ToArray();
            var choices = shuffled[..Math.Min(3, shuffled.Length)];

            DiagnosticLogger.LogEvent("UPGRADE", $"level={newLevel} choices: {FormatChoices(choices)}");
            commands.AddEntity(new Position(position), new PendingChoice(), new PendingUpgradeOptions(choices));
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

            Shuffle(allOptions, view.Rng);
            var shuffled = allOptions.ToArray();
            var choices = shuffled[..Math.Min(5, shuffled.Length)];

            DiagnosticLogger.LogEvent("UPGRADE", $"level={newLevel} choices: {FormatChoices(choices)}");
            commands.AddEntity(new Position(position), new PendingChoice(), new PendingUpgradeOptions(choices));
        }
    }

    // Diagnostics only: SPACEVORS_DIAG_UPGRADES="RailGun,Hp,FireRate:MachineGun" — one entry per level-up.
    private static UpgradableOption[]? ParseScriptedUpgrades()
    {
        if (Environment.GetEnvironmentVariable(DiagnosticEnvVar) != "1") return null;

        var raw = Environment.GetEnvironmentVariable(ScriptedUpgradesEnvVar);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var options = new List<UpgradableOption>();
        foreach (var entry in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (ParseEntry(entry) is { } option) options.Add(option);
            else DiagnosticLogger.LogWarning($"SPACEVORS_DIAG_UPGRADES: ignoring unknown entry '{entry}'");
        }

        return options.Count > 0 ? [.. options] : null;
    }

    private static UpgradableOption? ParseEntry(string entry)
    {
        if (AllNewWeapons.Any(w => w.Name == entry))
            return new UpgradableOption(entry, UpgradeOption.Damage, IsNewWeapon: true);

        var parts = entry.Split(':');
        if (parts.Length != 2 || !Enum.TryParse<UpgradeOption>(parts[0], ignoreCase: true, out var stat))
            return null;

        string weaponName = parts[1].Trim();
        return new UpgradableOption(weaponName, stat);
    }

    private static string FormatChoice(UpgradableOption option) =>
        option.IsNewWeapon ? $"new:{option.WeaponName}" : FormatStat(option);

    private static string FormatChoices(UpgradableOption[] options) =>
        string.Join(", ", options.Select(FormatChoice));

    private static string FormatStat(UpgradableOption option) =>
        string.IsNullOrEmpty(option.WeaponName) ? option.Stat.ToString() : $"{option.Stat}:{option.WeaponName}";

    private static void Shuffle<T>(List<T> items, Random rng)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
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
