using Spacevors.Domain;
using Spacevors.Domain.Systems;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

// What one upgrade card says, as plain text. It lives here rather than in the renderer for two reasons:
// the strings are testable without a window, and every number on a card then comes out of the same Domain
// call the grant uses — `WeaponUpgrade.ApplyStat(live, card)` for a gun, `card.Step` for the hull — so a
// printed number cannot disagree with what the player receives (R10).
//
// A card answers three questions in this order: **which thing changes** (`Name`), **how big the step is**
// (`Value`), **what the number is before and after** (`Numbers`). A new gun adds a fourth answer, which
// upgrades it already carries (`Caps` and the pip count) — the fact that makes two builds of one gun two
// different cards (plans/PREUPGRADED_NEW_WEAPONS.md).
//
// Wrapping, colour, and what gets dropped when a card is too narrow stay with the renderer: this file
// measures nothing and never truncates.
public static class UpgradeCardCopy
{
    // The hull-wide numbers a hull card changes, read once per frame by whoever draws the hand.
    public readonly record struct Hull(
        int MaxHp, int Armor, float ShieldMax, float Thrust, float SideThrust, float BackThrust,
        float RotationSpeed, float PickupRadius);

    // One card's text, unwrapped. `CapsShort` is the same set of caps in the short vocabulary, so the
    // renderer can degrade a tight row without inventing words of its own.
    public readonly record struct Text(
        string Name,
        string Value,
        string[] Caps,
        string[] CapsShort,
        string[] Numbers,
        int Pips,
        int PipsFilled);

    /// `liveGun` is the turret this card would change, or null when the ship does not have that gun — which
    /// is the case for a new-weapon card, and for no other. A card with no live gun prints no before/after
    /// pair for it rather than a guess out of the table.
    public static Text Build(UpgradableOption card, Turret? liveGun, Hull hull, bool pierceUnlocked)
    {
        bool volley = GrowsVolley(card, liveGun);
        (int pips, int pipsFilled) = Pips(card, pierceUnlocked);
        return new Text(
            Name(card, volley),
            Value(card, liveGun, volley),
            Caps(card, shortWords: false),
            Caps(card, shortWords: true),
            Numbers(card, liveGun, hull, volley),
            Pips: pips,
            PipsFilled: pipsFilled);
    }

    /// Whether this card buys shots rather than a faster trigger — the same test `ApplyStat` applies, read
    /// off the live turret when there is one, because a guided volley that has spent its round budget (R17)
    /// takes the card as a rate and the card must say so.
    static bool GrowsVolley(UpgradableOption card, Turret? liveGun) => liveGun is { } live
        ? live.Weapon.CanGrowSalvoBy(card.SalvoShots)
        : WeaponType.FromName(card.WeaponName)?.Stats.CanGrowSalvoBy(card.SalvoShots) ?? false;

    static string Name(UpgradableOption card, bool volley)
    {
        if (card.IsNewWeapon)
            return $"new weapon · {MenuCopy.FriendlyWeaponName(card.WeaponName)}";

        // A card that names no gun changes the hull itself, so it carries no gun name to lead with.
        if (string.IsNullOrEmpty(card.WeaponName)) return MenuCopy.StatWord(card.Stat);

        return $"{MenuCopy.FriendlyWeaponName(card.WeaponName)} {MenuCopy.StatWord(card.Stat, volley)}";
    }

    static string Value(UpgradableOption card, Turret? liveGun, bool volley)
    {
        if (WeaponType.FromName(card.WeaponName) is not { } gun) return DisplayStep(card.Step);

        // The prominent line of a new-weapon card is the mount size: the thing the player has to know
        // before they can put the gun anywhere (PLAN.md, Controls).
        if (card.IsNewWeapon) return MenuCopy.SizeWord(gun.Size);

        // R33: the damage card is a percent of the gun's base sized by its class, so the percent is read
        // off the gun. A `Damage` card is never super (plans/SUPER_UPGRADES.md D3).
        if (card.Stat == UpgradeOption.Damage) return $"+{UpgradeDefinition.DamagePercentFor(gun.IsSupport)}%";

        // A card that grows a volley buys shots — two of them on a super card — not the +30 % a gun with a
        // fixed volley gets (R17/R18).
        if (card.Stat == UpgradeOption.FireRate && volley)
            return $"+{card.SalvoShots} shot{(card.SalvoShots > 1 ? "s" : "")}";

        return DisplayStep(card.Step);
    }

    // A percent-of-base card carries no multiplier and no additive: its step is a share of the table value
    // (R33 for damage, R19 for a round's hit points), so the definition's own `DisplayValue` — which reads
    // those two fields — would print `+0`. The share is what the card means.
    static string DisplayStep(UpgradeDefinition step) =>
        step.PercentOfBase != 0 ? $"+{step.PercentOfBase}%" : step.DisplayValue;

    /// What a new gun arrives carrying, one cap per stat, each at the step that upgrade really grants.
    /// An arriving set is always one **ordinary** card per stat, so on a volley gun the arriving `FireRate`
    /// buys a shot and its cap says `volley +1 shot`, never `attack speed +15%` (R10).
    static string[] Caps(UpgradableOption card, bool shortWords)
    {
        if (!card.IsNewWeapon || WeaponType.FromName(card.WeaponName) is not { } gun) return [];

        var carried = TurretMount.CarriedTurret(gun).Weapon;
        bool volley = carried.CanGrowSalvoBy(1);

        return [.. WeaponUpgrade.StatsOf(card.PreApplied).Select(stat =>
        {
            string word = shortWords ? MenuCopy.StatAbbrev(stat, volley) : MenuCopy.StatWord(stat, volley);
            return $"{word} {ArrivingStep(gun, stat, volley)}";
        })];
    }

    static string ArrivingStep(WeaponType gun, UpgradeOption stat, bool volley)
    {
        if (stat == UpgradeOption.FireRate && volley) return "+1 shot";
        if (stat == UpgradeOption.Damage) return $"+{UpgradeDefinition.DamagePercentFor(gun.IsSupport)}%";
        return DisplayStep(UpgradeDefinition.For(stat));
    }

    /// The number the card moves, before and after, as one short row. For a gun the pair is the live turret
    /// and that turret after this card; for the hull it is the component field the grant multiplies or adds
    /// to. A card naming a gun the ship does not have prints no pair rather than a guess out of the table.
    static string[] Numbers(UpgradableOption card, Turret? liveGun, Hull hull, bool volley)
    {
        if (card.IsNewWeapon) return ArrivingNumbers(card);
        if (string.IsNullOrEmpty(card.WeaponName)) return HullNumbers(card, hull);
        if (liveGun is not { } live) return [];

        var after = WeaponUpgrade.ApplyStat(live, card);
        return card.Stat switch
        {
            UpgradeOption.FireRate when volley =>
                [$"volley {live.Weapon.PelletCount} -> {after.Weapon.PelletCount}"],
            UpgradeOption.FireRate =>
                [$"fires {Rate(live.Weapon.FireRate)} -> {Rate(after.Weapon.FireRate)} /s"],
            UpgradeOption.ProjectileSpeed =>
                [$"projectile {Whole(live.Weapon.AmmoSpeed)} -> {Whole(after.Weapon.AmmoSpeed)} px/s"],
            UpgradeOption.Range =>
                [$"range {Whole(live.Range)} -> {Whole(after.Range)} px"],
            UpgradeOption.ArmorPierce =>
                [$"pierce {Whole(live.Weapon.Pierce)} -> {Whole(after.Weapon.Pierce)}"],
            UpgradeOption.Damage =>
                [$"damage {Whole(live.Weapon.Damage)} -> {Whole(after.Weapon.Damage)}"],
            UpgradeOption.MissileHealth =>
                [$"round hp {Whole(live.Weapon.MissileHealth)} -> {Whole(after.Weapon.MissileHealth)}"],
            _ => [],
        };
    }

    // The hull's own cards. Each row is the number `ApplyUpgrade` writes: a multiplier or an additive off
    // `card.Step`, with the two ceilings applied the way the grant applies them. `CardReadabilityTest` grants
    // these cards for real and compares, because there is no per-hull Domain call to lean on here.
    static string[] HullNumbers(UpgradableOption card, Hull hull)
    {
        var step = card.Step;
        return card.Stat switch
        {
            UpgradeOption.Hp => [$"hp {hull.MaxHp} -> {hull.MaxHp + step.Additive}"],
            UpgradeOption.Armor => [$"armor {hull.Armor} -> {Math.Min(hull.Armor + step.Additive, ArmorRules.ArmorCeiling)}"],
            UpgradeOption.Shield => [$"shield {Whole(hull.ShieldMax)} -> {Math.Min(hull.ShieldMax + step.Additive, ShieldRules.ShieldCeiling)}"],
            UpgradeOption.PickupRadius => [$"pickup {Whole(hull.PickupRadius)} -> {Whole(Applied(hull.PickupRadius, step))} px"],
            UpgradeOption.ForwardAcceleration => [$"thrust {Whole(hull.Thrust)} -> {Whole(Applied(hull.Thrust, step))}"],
            UpgradeOption.SideThrust => [$"side thrust {Whole(hull.SideThrust)} -> {Whole(Applied(hull.SideThrust, step))}"],
            UpgradeOption.BackThrust => [$"back thrust {Whole(hull.BackThrust)} -> {Whole(Applied(hull.BackThrust, step))}"],
            UpgradeOption.TurnSpeed =>
                [$"turn {Whole(Angle.ToDeg(hull.RotationSpeed))} -> {Whole(Angle.ToDeg(Applied(hull.RotationSpeed, step)))} °/s"],
            _ => [],
        };
    }

    // The two numbers a new-weapon card has always led with, now printed as a pair when the arriving set
    // moves them: `range 1000 -> 1500 px` says both what the gun is and what the pre-installed upgrade is
    // worth, which a lone boosted number did not.
    static string[] ArrivingNumbers(UpgradableOption card)
    {
        if (WeaponType.FromName(card.WeaponName) is not { } gun) return [];

        var plain = TurretMount.CarriedTurret(gun);
        var arriving = WeaponUpgrade.ArrivingTurret(gun, card.PreApplied);
        string pellets = arriving.Weapon.PelletCount > 1 ? $" x{arriving.Weapon.PelletCount}" : "";
        bool movedRange = card.PreApplied.HasFlag(WeaponPreUpgrade.Range);
        bool movedDamage = card.PreApplied.HasFlag(WeaponPreUpgrade.Damage);

        return
        [
            movedRange
                ? $"range {Whole(plain.Range)} -> {Whole(arriving.Range)} px"
                : $"range {Whole(arriving.Range)} px",
            movedDamage
                ? $"damage {Whole(plain.Weapon.Damage)} -> {Whole(arriving.Weapon.Damage)}{pellets}"
                : $"damage {Whole(arriving.Weapon.Damage)}{pellets}",
        ];
    }

    /// One pip per stat this gun could still be upgraded with, and how many the arriving set already fills.
    /// Legality is read at the ordinary step on the gun **as it arrives**, so a chip that has reached its
    /// range roof shows a shorter row rather than a pip that no card would ever fill (R19, R29).
    static (int Total, int Filled) Pips(UpgradableOption card, bool pierceUnlocked)
    {
        if (!card.IsNewWeapon || WeaponType.FromName(card.WeaponName) is not { } gun) return (0, 0);

        int filled = WeaponUpgrade.StatsOf(card.PreApplied).Length;
        if (filled == 0) return (0, 0);

        int total = WeaponUpgrade.LegalStats(gun, WeaponUpgrade.ArrivingTurret(gun, card.PreApplied), pierceUnlocked, CardPower.Base).Count;
        return (Math.Max(total, filled), filled);
    }

    /// How the card row splits the space it was given between the two halves of a bundle. The busy half — a
    /// new-weapon card, which carries a name, a mount size, its arriving caps, its pips and its numbers — used
    /// to be handed exactly half the card and then overflow it; now the split follows the two halves' content,
    /// floored so neither side can be squeezed out of existence.
    public static int SplitHeight(int desiredTop, int desiredBottom, int total, int minHalf) =>
        desiredTop + desiredBottom <= total
            ? Math.Max(total / 2, minHalf)
            : Math.Clamp((int)MathF.Round((float)total * desiredTop / (desiredTop + desiredBottom)), minHalf, total - minHalf);

    static float Applied(float value, UpgradeDefinition step) =>
        step.Multiplier != 1f ? value * step.Multiplier : value + step.Additive;

    static string Whole(float value) => $"{(int)MathF.Round(value)}";

    static string Rate(float value) => value < 10f ? $"{value:0.0}" : $"{(int)MathF.Round(value)}";
}
