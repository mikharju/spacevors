using Spacevors.Domain.Stats; // UpgradeOption

namespace Spacevors.Domain.Components;

public readonly record struct Health(int Current, int Max);

public readonly record struct PendingChoice;

// IsNewWeapon: the card offers the weapon itself, so the player then fits it on the hull or leaves it in cargo.
// Tier: only `Armor` and `Shield` read it — the step this card grants, stamped when it was dealt
// (plans/UPGRADE_BUNDLE_PAIRS.md §2). It rides on the card so the label and the grant read one number.
// PreApplied: what a new gun already carries when the card is dealt to a hull that cannot fit it
// (plans/PREUPGRADED_NEW_WEAPONS.md). Because the set is part of the card, one gun dealt as two different sets
// is two different cards — which is what lets a hand offer several builds of the same unowned gun.
// Power: whether this card is a **super** one, the same upgrade at twice the step, stamped on two halves of
// every hand (plans/SUPER_UPGRADES.md). Grading a card makes it a different card, so every "same card / same
// pair / same hand" rule in the offer sees the grade without knowing about it.
public readonly record struct UpgradableOption(
    string WeaponName,
    UpgradeOption Stat,
    bool IsNewWeapon = false,
    DefenceTier Tier = DefenceTier.Ordinary,
    WeaponPreUpgrade PreApplied = WeaponPreUpgrade.None,
    CardPower Power = CardPower.Base)
{
    public bool IsSuper => Power == CardPower.Super;

    /// The one step this card grants: the stat's definition at the defence tier and the power stamped on the
    /// card. The card's label, the offer's ceiling gates and the grant all read this, so a card can never
    /// print one number and hand out another (R10). A percent-of-base card still takes its base from the gun —
    /// `Step.StepForBase(gun.Stats.MissileHealth)` — and a `Damage` card's percent comes from the gun as well.
    public UpgradeDefinition Step
    {
        get
        {
            var def = Stat is UpgradeOption.Armor or UpgradeOption.Shield
                ? UpgradeDefinition.For(Stat) with { Additive = UpgradeDefinition.DefenceStep(Stat, Tier) }
                : UpgradeDefinition.For(Stat);
            return def.ScaledBy(Power);
        }
    }

    /// A `FireRate` card on a gun whose volley grows buys shots instead of a faster rate, and a super card buys
    /// two (plans/SUPER_UPGRADES.md D2). Whether the gun can take them is its own budget check; when it cannot,
    /// the card falls back to the rate multiplier, exactly as an ordinary card does at the budget.
    public int SalvoShots => IsSuper ? 2 : 1;
}

// What a level offers: a pair of cards taken together — one powerful (a kill gun's upgrade, a new large gun,
// a defence card) and one weak (a chip's upgrade, a new small gun, an engine or utility card, `Hp`). The
// classes are R25's kill/support axis made visible (plans/UPGRADE_BUNDLE_PAIRS.md §2), and a bundle is chosen
// atomically, so neither half can be disabled by the other.
public readonly record struct UpgradeBundle(UpgradableOption Powerful, UpgradableOption Weak);

// The bundles on screen. A level takes exactly one of them and is done, so there is no pick count left to
// carry and nothing for a reroll to sell back.
public readonly record struct PendingUpgradeOptions(UpgradeBundle[] Bundles);

// Charges spent to re-deal the upgrade hand, collected from ship salvage (`RerollToken`). It is a
// component on the player rather than a `Player` field: `PickupMagnetSystem` ref-writes `Player.Xp`
// mid-phase, so a queued `Player` write built from a pre-phase copy would drop this tick's XP.
public readonly record struct RerollStock(int Count)
{
    public static RerollStock Empty { get; } = new(0);

    public RerollStock With(int count) => new(Math.Max(0, count));
}

// Turrets the ship carries but does not mount: inactive, and never offered upgrades. A gun taken off
// the hull comes back here with its upgraded stats intact, ready to be mounted into a fitting mount.
public readonly record struct WeaponCargo(IReadOnlyList<Turret> Weapons)
{
    public static WeaponCargo Empty { get; } = new(Array.Empty<Turret>());

    public bool Holds(string weaponName) => TryFind(weaponName, out _);

    public bool TryFind(string weaponName, out Turret turret)
    {
        foreach (var weapon in Weapons)
        {
            if (weapon.WeaponName == weaponName)
            {
                turret = weapon;
                return true;
            }
        }

        turret = default;
        return false;
    }

    public WeaponCargo With(Turret turret) => this with { Weapons = [.. Weapons, turret] };

    public WeaponCargo Without(string weaponName) => this with { Weapons = [.. Weapons.Where(w => w.WeaponName != weaponName)] };
}

// How many times each (stat, weapon) pair has been upgraded; ship-level stats use an empty weapon name.
public readonly record struct UpgradeCount(UpgradeOption Stat, string WeaponName, int Count);

public readonly record struct UpgradeCounts(IReadOnlyList<UpgradeCount> Counts)
{
    public static UpgradeCounts Empty { get; } = new(Array.Empty<UpgradeCount>());

    public int GetCount(UpgradeOption stat, string weaponName) =>
        Counts.FirstOrDefault(c => c.Stat == stat && c.WeaponName == weaponName).Count;

    public UpgradeCounts Increment(UpgradeOption stat, string weaponName)
    {
        var updated = new List<UpgradeCount>(Counts.Count + 1);
        bool found = false;
        foreach (var count in Counts)
        {
            if (count.Stat == stat && count.WeaponName == weaponName)
            {
                updated.Add(count with { Count = count.Count + 1 });
                found = true;
            }
            else updated.Add(count);
        }
        if (!found) updated.Add(new UpgradeCount(stat, weaponName, 1));
        return new(updated);
    }
}
