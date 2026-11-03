namespace Spacevors.Domain.Stats;

// XP progression (LevelUpSystem).
public static class UpgradeStats
{
    // What the next level costs, on top of what the previous one cost.
    private const int XpStepPerLevel = 5;

    // Cumulative XP that must be reached while at `level` to advance. A level costs `XpStepPerLevel` more
    // than the one before it (10 to leave level 1, then 20 more, then 30 more), so the total is the
    // triangular sum. `Player.Xp` is the run's total and is never spent or reset.
    public static int XpForLevel(int level) => XpStepPerLevel * level * (level + 1) / 2;

    // How far through `level` the player is, 0..1 — the slice between where this level started and the
    // next threshold, not the run total, so the bar empties on every level-up while Xp keeps counting up.
    public static float LevelProgress(int level, int xp)
    {
        int levelStart = XpForLevel(level - 1);
        int levelSpan = XpForLevel(level) - levelStart;
        return Math.Clamp((xp - levelStart) / (float)levelSpan, 0f, 1f);
    }
}

public enum UpgradeOption { FireRate, ProjectileSpeed, PickupRadius, Range, Damage, Hp, ForwardAcceleration, TurnSpeed, SideThrust, BackThrust, MissileHealth, Armor, ArmorPierce }

// A card's effect on one stat. `Multiplier` scales the live value; `Additive` shifts it; `PercentOfBase`
// adds a share of the stat's table value, so the step never grows with the cards already bought.
public readonly record struct UpgradeDefinition(UpgradeOption Stat, float Multiplier = 1f, int Additive = 0, int PercentOfBase = 0)
{
    public string DisplayValue => PercentOfBase != 0 ? $"+{PercentOfBase}%"
        : Multiplier != 1f ? $"+{(int)MathF.Round((Multiplier - 1f) * 100f)}%" : $"+{Additive}";

    /// How many units this card adds to `baseValue`. A percent-of-base card always buys the same step,
    /// whatever the stat has already grown to; the step is never smaller than one unit, so a card that
    /// would round down to nothing is never dealt (R10).
    public int StepForBase(int baseValue) => PercentOfBase == 0 ? Additive : PercentStep(baseValue, PercentOfBase);

    /// A percent of a table value, rounded away from zero and never below one unit. Shared by the two
    /// percent cards, both of which take their base from the weapon table rather than a live stat.
    public static int PercentStep(int baseValue, int percent)
        => Math.Max(1, (int)MathF.Round(baseValue * percent / 100f, MidpointRounding.AwayFromZero));

    public static UpgradeDefinition FireRate { get; } = new(UpgradeOption.FireRate, Multiplier: 1.15f);
    public static UpgradeDefinition ProjectileSpeed { get; } = new(UpgradeOption.ProjectileSpeed, Multiplier: 1.3f);
    public static UpgradeDefinition PickupRadius { get; } = new(UpgradeOption.PickupRadius, Multiplier: 1.2f);
    // Increases both the firing range and the shot lifetime of one weapon type.
    public static UpgradeDefinition Range { get; } = new(UpgradeOption.Range, Multiplier: 1.15f);
    // R33: the damage card buys a percent of the gun's **base** per-shot damage, sized by mount class —
    // a support chip +25%, a kill gun +50%. The class gap therefore widens across a run instead of being
    // frozen by a flat step, and the card is dealt only by a milestone hand (every fifth level), which is
    // what makes a step that big affordable. The step itself comes from the gun (`WeaponType.DamageStep`),
    // so cards add on the base — +50% twice is +100%, never +125% — and the menu reads the percent off the
    // gun too (`UpgradeMenuRenderer`), so `DisplayValue` is never the label for this card.
    public static UpgradeDefinition Damage { get; } = new(UpgradeOption.Damage);

    // R33: the two damage-card sizes. 25 and 50 divide every gun's base damage exactly at R32's ×4 scale,
    // so a card is a whole number of damage on every gun in the table.
    public const int SupportDamagePercent = 25;
    public const int KillDamagePercent = 50;
    public static int DamagePercentFor(bool isSupport) => isSupport ? SupportDamagePercent : KillDamagePercent;

    // R32: a hull card is a quarter of the smallest hull's 32 hp. It stays additive because a percent of
    // the hull's base would need the same table anchor as the damage card and buy the same 8 hp on every
    // hull anyway.
    public static UpgradeDefinition Hp { get; } = new(UpgradeOption.Hp, Additive: 8);
    public static UpgradeDefinition ForwardAcceleration { get; } = new(UpgradeOption.ForwardAcceleration, Multiplier: 1.1f);
    public static UpgradeDefinition TurnSpeed { get; } = new(UpgradeOption.TurnSpeed, Multiplier: 1.1f);
    public static UpgradeDefinition SideThrust { get; } = new(UpgradeOption.SideThrust, Multiplier: 1.1f);
    public static UpgradeDefinition BackThrust { get; } = new(UpgradeOption.BackThrust, Multiplier: 1.1f);
    // R19: more hit points on every round this mount fires, for the guns whose rounds can be shot down.
    // The step is 50% of the gun's **base** round health, so it follows the missile — never less for a
    // bigger round, and more from 3 hp up — and it is the same step every time the card is bought: two
    // cards are +100% of the base, never +125%. Rounds that cannot be shot down ignore the stat.
    public static UpgradeDefinition MissileHealth { get; } = new(UpgradeOption.MissileHealth, PercentOfBase: 50);

    // plans/ARMOR_AND_PIERCE.md: one hull, so the armor card is small (+2) and walks the ceiling (12)
    // slowly; it competes with `Hp` in the guaranteed hull slot and is offered only from level 5.
    public static UpgradeDefinition Armor { get; } = new(UpgradeOption.Armor, Additive: 2);

    // Many weapons each buy their own line against the one armored hull, and pierce is worthless
    // against unarmored targets, so the step is a flat +4 (§6: against armor, +1 pierce ≈ +1 damage —
    // the right shape for a situational card, no multiplier to compound).
    public static UpgradeDefinition ArmorPierce { get; } = new(UpgradeOption.ArmorPierce, Additive: 4);

    public static readonly UpgradeDefinition[] All = [FireRate, ProjectileSpeed, PickupRadius, Range, Damage, Hp, ForwardAcceleration, TurnSpeed, SideThrust, BackThrust, MissileHealth, Armor, ArmorPierce];

    public static UpgradeDefinition For(UpgradeOption stat) => All.First(d => d.Stat == stat);
}
