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
    /// name is not in the table — leaves the turret alone, exactly as the buy paths always did. Every step
    /// comes off `card.Step`, the definition stamped with the card's own power, so the number a card prints,
    /// the step the gates allow and the step granted here cannot drift apart (R10).
    public static Turret ApplyStat(Turret turret, UpgradableOption card) => card.Stat switch
    {
        // A volley gun grows its volley per card — two shots for a super card — and a guided volley stops at
        // the round budget (R17/R18); a gun whose volley never grows takes the card as a faster rate.
        UpgradeOption.FireRate => turret.Weapon.CanGrowSalvoBy(card.SalvoShots)
            ? turret with { Weapon = turret.Weapon with { PelletCount = turret.Weapon.PelletCount + card.SalvoShots } }
            : turret with { Weapon = turret.Weapon with { FireRate = turret.Weapon.FireRate * card.Step.Multiplier } },

        UpgradeOption.ProjectileSpeed => turret with
        {
            Weapon = turret.Weapon with { AmmoSpeed = turret.Weapon.AmmoSpeed * card.Step.Multiplier }
        },

        // The card stretches the mount's reach and the shot's lifetime together, so a round never outlives the
        // range that was paid for it.
        UpgradeOption.Range => turret with
        {
            Range = turret.Range * card.Step.Multiplier,
            Weapon = turret.Weapon with { ShotLifetime = turret.Weapon.ShotLifetime * card.Step.Multiplier }
        },

        // +4 on the live copy, cards adding on each other (+8 for a super one) — the ceiling guard lives in
        // the offer (R19), which asks with this same step.
        UpgradeOption.ArmorPierce => turret with { Weapon = turret.Weapon with { Pierce = turret.Weapon.Pierce + card.Step.Additive } },

        // R33: a percent of the gun's **base** damage sized by its class, added rather than compounded. A
        // `Damage` card is never super (plans/SUPER_UPGRADES.md D3), so this one reads the table step.
        UpgradeOption.Damage when WeaponType.FromName(turret.WeaponName) is { } damageGun =>
            turret with { Weapon = turret.Weapon with { Damage = turret.Weapon.Damage + damageGun.DamageStep } },

        // R19: the same shape on the round's base hit points, so only a guided round is changed at all.
        UpgradeOption.MissileHealth when WeaponType.FromName(turret.WeaponName) is { } toughGun =>
            turret with { Weapon = turret.Weapon with { MissileHealth = turret.Weapon.MissileHealth + toughGun.MissileHealthStepFor(card.Power) } },

        _ => turret
    };

    /// The gun after every upgrade its card says it arrives with. One card of each stat: the set is a mask,
    /// so no stat can appear twice on one arriving gun. An arriving upgrade is always one **ordinary** card
    /// of its stat (plans/PREUPGRADED_NEW_WEAPONS.md D3) — grading it would let a swapped-in gun compound the
    /// arriving arithmetic, which is counted in cards for exactly that reason.
    public static Turret Apply(Turret turret, WeaponPreUpgrade mask) =>
        StatsOf(mask).Aggregate(turret, (current, stat) => ApplyStat(current, new UpgradableOption(turret.WeaponName, stat)));

    /// The turret a new-weapon card grants: the table's gun plus the upgrades the card says it arrives with. The
    /// grant writes it and the upgrade card prints its numbers, so both read this one call.
    public static Turret ArrivingTurret(WeaponType gun, WeaponPreUpgrade mask) => Apply(TurretMount.CarriedTurret(gun), mask);

    /// Count the upgrades an arriving gun carries, one per stat in its set: the stats screen shows them, and the
    /// next arriving gun is measured against them (plans/PREUPGRADED_NEW_WEAPONS.md D2).
    public static UpgradeCounts WithArrivingUpgrades(UpgradeCounts counts, WeaponPreUpgrade mask, string weaponName) =>
        StatsOf(mask).Aggregate(counts, (current, stat) => current.Increment(stat, weaponName));

    /// Which upgrades `live` may be given **at `power`**. `live` is the copy as it stands — a table copy for a
    /// gun that has not been mounted yet — and every gate is the shipped one, because an upgrade the rules
    /// would refuse to honour is a lie whether it is bought or arrives with the gun (R10, R18, R19, R29). A gun
    /// that is not in the table at all (a hand-built test turret) gets no round-health card: its step has no
    /// table base. A super card is gated against its **own doubled step**, so a volley that has room for one
    /// more shot is not offered a two-shot card and a chip at its range roof is not offered the step that
    /// would put it through it (plans/SUPER_UPGRADES.md D6).
    public static List<UpgradeOption> LegalStats(WeaponType? gun, Turret live, bool pierceUnlocked, CardPower power)
    {
        var stats = new List<UpgradeOption>();
        var weapon = live.Weapon;
        int shots = power == CardPower.Super ? 2 : 1;

        // R18: a growing guided volley that reached the round budget (R17) cannot take another shot — and a
        // super card cannot take two. Guns whose volleys never grow always have a working rate card.
        if (!weapon.GrowsVolley || weapon.CanGrowSalvoBy(shots))
            stats.Add(UpgradeOption.FireRate);

        // A hitscan beam has no travelling projectile: the card would multiply AmmoSpeed 0 by 1.3.
        if (!weapon.Hitscan)
            stats.Add(UpgradeOption.ProjectileSpeed);

        // R19: only a guided round has hit points to raise, and only while the next whole step still lands
        // at or under the ceiling.
        if (gun is { } tableGun && weapon.CanToughenRound(tableGun.MissileHealthStepFor(power)))
            stats.Add(UpgradeOption.MissileHealth);

        // R29: a support gun stops at its class roof. The card stretches the shot's lifetime too, so
        // withdrawing it at the roof caps the chip's live rounds — the same reason R18/R19 withdraw theirs.
        if (!live.IsSupport || live.Range * UpgradeDefinition.Range.ScaledBy(power).Multiplier <= WeaponType.SupportRangeCeiling)
            stats.Add(UpgradeOption.Range);

        // plans/ARMOR_AND_PIERCE.md: pierce is the level-5 answer to the armored hull, and at the ceiling it
        // is withdrawn rather than dealt as a lie.
        if (pierceUnlocked && weapon.CanPierceMore(UpgradeDefinition.ArmorPierce.ScaledBy(power).Additive))
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
