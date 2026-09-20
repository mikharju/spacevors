namespace Spacevors.Domain.Stats;

// XP progression (LevelUpSystem).
public static class UpgradeStats
{
    // Cumulative XP threshold that must be reached while at `level` to advance.
    public static int XpForLevel(int level) => level * 10;
}

public enum UpgradeOption { FireRate, ProjectileSpeed, PickupRadius, Range, Damage, Hp, ForwardAcceleration, TurnSpeed, SideThrust, BackThrust }

public readonly record struct UpgradeDefinition(UpgradeOption Stat, float Multiplier = 1f, int Additive = 0)
{
    public string DisplayValue => Multiplier != 1f ? $"+{(int)MathF.Round((Multiplier - 1f) * 100f)}%" : $"+{Additive}";

    public static UpgradeDefinition FireRate { get; } = new(UpgradeOption.FireRate, Multiplier: 1.15f);
    public static UpgradeDefinition ProjectileSpeed { get; } = new(UpgradeOption.ProjectileSpeed, Multiplier: 1.3f);
    public static UpgradeDefinition PickupRadius { get; } = new(UpgradeOption.PickupRadius, Multiplier: 1.2f);
    // Increases both the firing range and the shot lifetime of one weapon type.
    public static UpgradeDefinition Range { get; } = new(UpgradeOption.Range, Multiplier: 1.15f);
    public static UpgradeDefinition Damage { get; } = new(UpgradeOption.Damage, Additive: 1);
    public static UpgradeDefinition Hp { get; } = new(UpgradeOption.Hp, Additive: 2);
    public static UpgradeDefinition ForwardAcceleration { get; } = new(UpgradeOption.ForwardAcceleration, Multiplier: 1.1f);
    public static UpgradeDefinition TurnSpeed { get; } = new(UpgradeOption.TurnSpeed, Multiplier: 1.1f);
    public static UpgradeDefinition SideThrust { get; } = new(UpgradeOption.SideThrust, Multiplier: 1.1f);
    public static UpgradeDefinition BackThrust { get; } = new(UpgradeOption.BackThrust, Multiplier: 1.1f);

    public static readonly UpgradeDefinition[] All = [FireRate, ProjectileSpeed, PickupRadius, Range, Damage, Hp, ForwardAcceleration, TurnSpeed, SideThrust, BackThrust];

    public static UpgradeDefinition For(UpgradeOption stat) => All.First(d => d.Stat == stat);
}
