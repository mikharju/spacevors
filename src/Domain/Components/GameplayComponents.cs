using Spacevors.Domain.Stats; // UpgradeOption

namespace Spacevors.Domain.Components;

public readonly record struct Health(int Current, int Max);

public readonly record struct PendingChoice;

// IsNewWeapon: the card offers the weapon itself, so the player then picks the hull slot to mount it in.
// Tier: only `Armor` and `Shield` read it — the step this card grants, stamped when it was dealt
// (plans/UPGRADE_BUNDLE_PAIRS.md §2). It rides on the card so the label and the grant read one number.
public readonly record struct UpgradableOption(
    string WeaponName, UpgradeOption Stat, bool IsNewWeapon = false, DefenceTier Tier = DefenceTier.Ordinary);

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
