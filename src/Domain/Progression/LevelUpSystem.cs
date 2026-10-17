using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

public class LevelUpSystem : GameSystem
{
    const int MilestoneLevelInterval = 5;
    const int MilestoneChoiceCount = 5;
    // A milestone hand carries up to three new weapons. Whether they fit a free hull mount is the
    // player's problem, not the hand's: a weapon that does not fit rides in the cargo until swapped in.
    const int MilestoneNewWeaponCards = 3;
    const string DiagnosticEnvVar = DiagnosticLogger.EnvVar;
    const string ScriptedUpgradesEnvVar = "SPACEVORS_DIAG_UPGRADES";

    // Diagnostics only: fixed upgrade sequence consumed one entry per level-up.
    private readonly UpgradableOption[]? _scriptedUpgrades;
    private int _scriptIndex;

    public LevelUpSystem(UpgradableOption[]? scriptedUpgrades = null)
    {
        _scriptedUpgrades = scriptedUpgrades ?? ParseScriptedUpgrades();
    }

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        if (!view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple)) return;
        Entity playerEntity = playerTuple.Entity;
        if (view.TryGetComponent<Dead>(playerEntity, out _)) return;

        var playerPos = playerTuple.Value2;

        var playerStats = view.GetComponent<Player>(playerEntity);
        int xpThreshold = UpgradeStats.XpForLevel(playerStats.Level);

        if (playerStats.Xp >= xpThreshold)
        {
            int newLevel = playerStats.Level + 1;
            bool isMilestoneLevel = newLevel % MilestoneLevelInterval == 0;

            // Do not consume the level-up when no choice can be offered (e.g. zero turrets).
            if (SpawnLevelUpChoice(view, playerEntity, playerPos.Value, newLevel, isMilestoneLevel, commands))
                commands.Add(new AddComponentCommand<Player>(playerEntity, playerStats with { Level = newLevel }));
        }
    }

    private bool SpawnLevelUpChoice(WorldView view, Entity playerEntity, Vector2 position, int newLevel, bool isMilestoneLevel, CommandBuffer commands)
    {
        if (_scriptedUpgrades != null && _scriptIndex < _scriptedUpgrades.Length)
        {
            var scripted = _scriptedUpgrades[_scriptIndex++];
            DiagnosticLogger.LogEvent("UPGRADE", $"level={newLevel} scripted choice: {FormatChoice(scripted)}");
            commands.AddEntity(new Position(position), new PendingChoice(), new PendingUpgradeOptions([scripted]));
            return true;
        }

        var turrets = new List<Turret>();
        foreach (var t in view.GetEntitiesWithComponents<Turret>())
        {
            if (!t.Value1.IsEnemy) turrets.Add(t.Value1);
        }

        if (turrets.Count == 0) return false;

        var weaponNames = turrets.Select(t => t.WeaponName).Distinct().ToList();

        // Cargo weapons are owned but inactive: they are not offered stat upgrades (those come from
        // the turrets on the hull above) and they are not offered back as new weapons.
        var ownedNames = new List<string>(weaponNames);
        if (view.TryGetComponent<WeaponCargo>(playerEntity, out var cargo))
            ownedNames.AddRange(cargo.Weapons.Select(w => w.WeaponName));

        var allOptions = new List<UpgradableOption>();

        if (isMilestoneLevel)
        {
            allOptions.Add(new UpgradableOption("", UpgradeOption.Hp));
            allOptions.AddRange(NewWeaponOptions(ownedNames, newLevel, view.Rng));

            foreach (var weapon in weaponNames)
            {
                allOptions.Add(new UpgradableOption(weapon, UpgradeOption.Damage));
            }

            Shuffle(allOptions, view.Rng);
            var shuffled = allOptions.ToArray();
            var choices = shuffled[..Math.Min(MilestoneChoiceCount, shuffled.Length)];

            DiagnosticLogger.LogEvent("UPGRADE", $"level={newLevel} choices: {FormatChoices(choices)}");
            commands.AddEntity(new Position(position), new PendingChoice(), new PendingUpgradeOptions(choices));
            return true;
        }
        else
        {
            allOptions.Add(new UpgradableOption("", UpgradeOption.Hp));

            foreach (var weapon in weaponNames)
            {
                var stats = turrets.First(t => t.WeaponName == weapon).Weapon;

                // R18: a guided volley that has reached its round budget (R17) would be offered a
                // "+1 shot" card it cannot honour, so the card is withdrawn and the gun keeps the
                // other two. Guns whose volleys never grow always have a working rate card.
                if (!SalvoCardIsDead(stats))
                    allOptions.Add(new UpgradableOption(weapon, UpgradeOption.FireRate));

                // A hitscan beam has no travelling projectile: the card would multiply AmmoSpeed 0
                // by 1.3 and be a dead choice.
                if (!stats.Hitscan)
                    allOptions.Add(new UpgradableOption(weapon, UpgradeOption.ProjectileSpeed));

                // R19: only a guided round has hit points to raise, and only up to the ceiling, so the
                // card exists only for the guns that fire them and only while they can still take it.
                if (stats.CanToughenRound)
                    allOptions.Add(new UpgradableOption(weapon, UpgradeOption.MissileHealth));

                allOptions.Add(new UpgradableOption(weapon, UpgradeOption.Range));
            }

            allOptions.Add(new UpgradableOption("", UpgradeOption.PickupRadius));

            allOptions.Add(new UpgradableOption("", UpgradeOption.ForwardAcceleration));
            allOptions.Add(new UpgradableOption("", UpgradeOption.TurnSpeed));
            allOptions.Add(new UpgradableOption("", UpgradeOption.SideThrust));
            allOptions.Add(new UpgradableOption("", UpgradeOption.BackThrust));

            Shuffle(allOptions, view.Rng);
            var shuffled = allOptions.ToArray();
            var choices = shuffled[..Math.Min(5, shuffled.Length)];

            DiagnosticLogger.LogEvent("UPGRADE", $"level={newLevel} choices: {FormatChoices(choices)}");
            commands.AddEntity(new Position(position), new PendingChoice(), new PendingUpgradeOptions(choices));
            return true;
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
        // A script names a gun directly and ignores its unlock level: the script is a test bench.
        if (entry != WeaponType.LoadTestWeapon.Name && WeaponType.FromName(entry) is not null)
            return new UpgradableOption(entry, UpgradeOption.Damage, IsNewWeapon: true);

        var parts = entry.Split(':');
        if (parts.Length != 2 || !Enum.TryParse<UpgradeOption>(parts[0], ignoreCase: true, out var stat))
            return null;

        string weaponName = parts[1].Trim();
        return new UpgradableOption(weaponName, stat);
    }

    // R18: a growing guided volley that has reached the guided-round budget (R17) cannot take another
    // shot, so the "+1 shot" card is withdrawn instead of being offered as a lie. Guns whose volleys
    // never grow (SalvoGrows off) keep the card as a plain rate increase.
    static bool SalvoCardIsDead(WeaponStats weapon) => weapon.SalvoGrows && weapon.PelletCount > 1 && !weapon.CanGrowSalvo;

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

    // New-weapon cards for a milestone hand: anything the ship does not already own, mounted or in
    // cargo, drawn from the pool that this level has unlocked.
    private static List<UpgradableOption> NewWeaponOptions(List<string> ownedNames, int newLevel, Random rng)
    {
        var pool = WeaponType.NewWeaponPoolFor(newLevel).Where(w => !ownedNames.Contains(w.Name)).ToList();
        Shuffle(pool, rng);

        var cards = new List<UpgradableOption>();
        foreach (var weapon in pool)
        {
            if (cards.Count >= MilestoneNewWeaponCards) break;
            cards.Add(new UpgradableOption(weapon.Name, UpgradeOption.Damage, IsNewWeapon: true));
        }

        return cards;
    }
}
