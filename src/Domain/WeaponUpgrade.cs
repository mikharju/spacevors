using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain;

// What an upgrade does to one gun, and what a gun may be upgraded with at all.
//
// Single home of that math, and in Domain rather than in the app because three paths have to agree on it
// number for number: a card the player buys, a new gun that arrives already carrying upgrades
// (plans/PREUPGRADED_NEW_WEAPONS.md), and the upgrade card that prints the numbers of that arriving gun.
// R10 — a card is never a lie — fails the moment those three read the rules separately.
public static class WeaponUpgrade
{
    // The stats a gun can carry: an arriving set is drawn from these, and `Damage` joins it only at the
    // milestone levels that may deal a damage card at all (R33).
    static readonly UpgradeOption[] ArrivingStats =
        [UpgradeOption.FireRate, UpgradeOption.ProjectileSpeed, UpgradeOption.Range, UpgradeOption.MissileHealth, UpgradeOption.ArmorPierce, UpgradeOption.Damage];

    /// The live copy of `turret` after one card. A stat this cannot serve — a hull-wide card, or a gun whose
    /// name is not in the table — leaves the turret alone, exactly as the buy paths always did.
    public static Turret ApplyStat(Turret turret, UpgradeOption stat) => stat switch
    {
        // A volley gun grows a shot per card; a guided volley stops at the round budget (R17/R18), and a gun
        // whose volley never grows takes the card as a faster rate.
        UpgradeOption.FireRate => turret.Weapon.CanGrowSalvo
            ? turret with { Weapon = turret.Weapon with { PelletCount = turret.Weapon.PelletCount + 1 } }
            : turret with { Weapon = turret.Weapon with { FireRate = turret.Weapon.FireRate * UpgradeDefinition.FireRate.Multiplier } },

        UpgradeOption.ProjectileSpeed => turret with
        {
            Weapon = turret.Weapon with { AmmoSpeed = turret.Weapon.AmmoSpeed * UpgradeDefinition.ProjectileSpeed.Multiplier }
        },

        // The card stretches the mount's reach and the shot's lifetime together, so a round never outlives the
        // range that was paid for it.
        UpgradeOption.Range => turret with
        {
            Range = turret.Range * UpgradeDefinition.Range.Multiplier,
            Weapon = turret.Weapon with { ShotLifetime = turret.Weapon.ShotLifetime * UpgradeDefinition.Range.Multiplier }
        },

        // Flat +4 on the live copy, cards adding on each other — the ceiling guard lives in the offer (R19).
        UpgradeOption.ArmorPierce => turret with { Weapon = turret.Weapon with { Pierce = turret.Weapon.Pierce + UpgradeDefinition.ArmorPierce.Additive } },

        // R33: a percent of the gun's **base** damage sized by its class, added rather than compounded.
        UpgradeOption.Damage when WeaponType.FromName(turret.WeaponName) is { } damageGun =>
            turret with { Weapon = turret.Weapon with { Damage = turret.Weapon.Damage + damageGun.DamageStep } },

        // R19: the same shape on the round's base hit points, so only a guided round is changed at all.
        UpgradeOption.MissileHealth when WeaponType.FromName(turret.WeaponName) is { } toughGun =>
            turret with { Weapon = turret.Weapon with { MissileHealth = turret.Weapon.MissileHealth + toughGun.MissileHealthStep } },

        _ => turret
    };

    /// The gun after every upgrade its card says it arrives with. One card of each stat: the set is a mask,
    /// so no stat can appear twice on one arriving gun.
    public static Turret Apply(Turret turret, WeaponPreUpgrade mask) =>
        StatsOf(mask).Aggregate(turret, (current, stat) => ApplyStat(current, stat));

    /// The turret a new-weapon card grants: the table's gun plus the upgrades the card says it arrives with. The
    /// grant writes it and the upgrade card prints its numbers, so both read this one call.
    public static Turret ArrivingTurret(WeaponType gun, WeaponPreUpgrade mask) => Apply(TurretMount.CarriedTurret(gun), mask);

    /// Count the upgrades an arriving gun carries, one per stat in its set: the stats screen shows them, and the
    /// next arriving gun is measured against them (plans/PREUPGRADED_NEW_WEAPONS.md D2).
    public static UpgradeCounts WithArrivingUpgrades(UpgradeCounts counts, WeaponPreUpgrade mask, string weaponName) =>
        StatsOf(mask).Aggregate(counts, (current, stat) => current.Increment(stat, weaponName));

    /// Which upgrades `live` may be given. `live` is the copy as it stands — a table copy for a gun that has
    /// not been mounted yet — and every gate is the shipped one, because an upgrade the rules would refuse to
    /// honour is a lie whether it is bought or arrives with the gun (R10, R18, R19, R29). A gun that is not in
    /// the table at all (a hand-built test turret) gets no round-health card: its step has no table base.
    public static List<UpgradeOption> LegalStats(WeaponType? gun, Turret live, bool pierceUnlocked)
    {
        var stats = new List<UpgradeOption>();
        var weapon = live.Weapon;

        // R18: a growing guided volley that reached the round budget (R17) cannot take another shot. Guns
        // whose volleys never grow always have a working rate card.
        if (!(weapon.SalvoGrows && weapon.PelletCount > 1 && !weapon.CanGrowSalvo))
            stats.Add(UpgradeOption.FireRate);

        // A hitscan beam has no travelling projectile: the card would multiply AmmoSpeed 0 by 1.3.
        if (!weapon.Hitscan)
            stats.Add(UpgradeOption.ProjectileSpeed);

        // R19: only a guided round has hit points to raise, and only while the next whole step still lands
        // at or under the ceiling.
        if (gun is { } tableGun && weapon.CanToughenRound(tableGun.MissileHealthStep))
            stats.Add(UpgradeOption.MissileHealth);

        // R29: a support gun stops at its class roof. The card stretches the shot's lifetime too, so
        // withdrawing it at the roof caps the chip's live rounds — the same reason R18/R19 withdraw theirs.
        if (!live.IsSupport || live.Range * UpgradeDefinition.Range.Multiplier <= WeaponType.SupportRangeCeiling)
            stats.Add(UpgradeOption.Range);

        // plans/ARMOR_AND_PIERCE.md: pierce is the level-5 answer to the armored hull, and at the ceiling it
        // is withdrawn rather than dealt as a lie.
        if (pierceUnlocked && weapon.CanPierceMore(UpgradeDefinition.ArmorPierce.Additive))
            stats.Add(UpgradeOption.ArmorPierce);

        return stats;
    }

    /// How many upgrades one gun has — every card ever granted for its name, arriving ones included. This is
    /// the number the next arriving gun is measured against (plans/PREUPGRADED_NEW_WEAPONS.md D2).
    public static int CountOn(UpgradeCounts counts, string weaponName) =>
        counts.Counts.Where(count => count.WeaponName == weaponName).Sum(count => count.Count);

    public static WeaponPreUpgrade BitOf(UpgradeOption stat) => stat switch
    {
        UpgradeOption.FireRate => WeaponPreUpgrade.FireRate,
        UpgradeOption.ProjectileSpeed => WeaponPreUpgrade.ProjectileSpeed,
        UpgradeOption.Range => WeaponPreUpgrade.Range,
        UpgradeOption.MissileHealth => WeaponPreUpgrade.MissileHealth,
        UpgradeOption.ArmorPierce => WeaponPreUpgrade.ArmorPierce,
        UpgradeOption.Damage => WeaponPreUpgrade.Damage,
        _ => WeaponPreUpgrade.None
    };

    /// The stats inside an arriving set, in one fixed order — the order they are applied, stamped and printed.
    public static UpgradeOption[] StatsOf(WeaponPreUpgrade mask) =>
        [.. ArrivingStats.Where(stat => mask.HasFlag(BitOf(stat)))];
}
