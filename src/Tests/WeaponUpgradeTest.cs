using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Xunit;

namespace Tests;

// One card, one gun: the arithmetic every path shares — a card the player buys, the upgrades a new gun
// arrives with (plans/PREUPGRADED_NEW_WEAPONS.md), and the numbers the card prints for them.
public class WeaponUpgradeTest
{
    [Fact]
    public void FireRate_GrowsTheVolleyOfAGunWhoseSalvoGrows()
    {
        var carried = TurretMount.CarriedTurret(WeaponType.ScatterCannon);

        var upgraded = WeaponUpgrade.ApplyStat(carried, Card(UpgradeOption.FireRate));

        Assert.Equal(carried.Weapon.PelletCount + 1, upgraded.Weapon.PelletCount);
        Assert.Equal(carried.Weapon.FireRate, upgraded.Weapon.FireRate);
    }

    // A single-shot or guided gun has no volley to grow, so its rate card is the plain faster trigger.
    [Fact]
    public void FireRate_SpeedsUpAGunWhoseVolleyNeverGrows()
    {
        var carried = TurretMount.CarriedTurret(WeaponType.LaserLance);
        Assert.False(carried.Weapon.CanGrowSalvo);

        var upgraded = WeaponUpgrade.ApplyStat(carried, Card(UpgradeOption.FireRate));

        Assert.Equal(carried.Weapon.FireRate * UpgradeDefinition.FireRate.Multiplier, upgraded.Weapon.FireRate, 3);
        Assert.Equal(carried.Weapon.PelletCount, upgraded.Weapon.PelletCount);
    }

    // Reach and bullet life move together, so a round never outlives the range that was paid for it.
    [Fact]
    public void Range_StretchesReachAndBulletLifeByTheSameFactor()
    {
        var carried = TurretMount.CarriedTurret(WeaponType.RailGun);
        float factor = UpgradeDefinition.Range.Multiplier;

        var upgraded = WeaponUpgrade.ApplyStat(carried, Card(UpgradeOption.Range));

        Assert.Equal(carried.Range * factor, upgraded.Range, 3);
        Assert.Equal(carried.Weapon.ShotLifetime * factor, upgraded.Weapon.ShotLifetime, 3);
    }

    // R33: a damage card is a percent of the gun's base damage, added rather than compounded.
    [Fact]
    public void Damage_AddsAPercentOfTheBaseGunTwiceOverRatherThanCompounding()
    {
        var carried = TurretMount.CarriedTurret(WeaponType.RailGun);
        int step = WeaponType.RailGun.DamageStep;

        var once = WeaponUpgrade.ApplyStat(carried, Card(UpgradeOption.Damage));
        var twice = WeaponUpgrade.ApplyStat(once, Card(UpgradeOption.Damage));

        Assert.Equal(WeaponType.RailGun.Stats.Damage + step, once.Weapon.Damage);
        Assert.Equal(WeaponType.RailGun.Stats.Damage + 2 * step, twice.Weapon.Damage);
    }

    // R19: a plain round is never *sold* the card — `LegalStats` is the gate, `ApplyStat` is only the
    // arithmetic — and what a guided round is sold buys the step sized on its base, not on the live copy.
    [Fact]
    public void MissileHealth_IsOfferedOnlyToAGuidedRound_AndBuysTheStepOfItsBase()
    {
        var seeker = TurretMount.CarriedTurret(WeaponType.SeekerPod);
        var shotgun = TurretMount.CarriedTurret(WeaponType.Shotgun);

        Assert.Contains(UpgradeOption.MissileHealth, Legal(WeaponType.SeekerPod, seeker));
        Assert.DoesNotContain(UpgradeOption.MissileHealth, Legal(WeaponType.Shotgun, shotgun));

        Assert.Equal(seeker.Weapon.MissileHealth + WeaponType.SeekerPod.MissileHealthStep,
            WeaponUpgrade.ApplyStat(seeker, Card(UpgradeOption.MissileHealth)).Weapon.MissileHealth);
    }

    // The gates an arriving set must respect, since a build that carried one of these would be a card the
    // rules refuse to honour: a beam has no projectile to speed up, a chip already at its class roof (R29)
    // has no range to buy, and pierce waits for level 5.
    [Fact]
    public void LegalStats_WithdrawsTheCardsThatGunCannotTake()
    {
        var lance = TurretMount.CarriedTurret(WeaponType.LaserLance);
        var stiletto = TurretMount.CarriedTurret(WeaponType.Stiletto);
        var rail = TurretMount.CarriedTurret(WeaponType.RailGun);

        Assert.DoesNotContain(UpgradeOption.ProjectileSpeed, Legal(WeaponType.LaserLance, lance));
        Assert.DoesNotContain(UpgradeOption.Range, Legal(WeaponType.Stiletto, stiletto));
        Assert.DoesNotContain(UpgradeOption.ArmorPierce, Legal(WeaponType.RailGun, rail, pierceUnlocked: false));
        Assert.Contains(UpgradeOption.ArmorPierce, Legal(WeaponType.RailGun, rail));
    }

    // A hull-wide card is not a gun card: `ApplyStat` is also what the buy path calls, and it has always
    // left the turret untouched for those.
    [Fact]
    public void AHullWideCard_LeavesTheGunAlone()
        => Assert.Equal(TurretMount.CarriedTurret(WeaponType.RailGun), WeaponUpgrade.ApplyStat(TurretMount.CarriedTurret(WeaponType.RailGun), Card(UpgradeOption.Hp)));

    // The arriving set is a mask, so it holds each stat once and survives the trip through the card.
    [Fact]
    public void TheArrivingSet_HoldsEachStatOnceAndRoundTripsThroughTheMask()
    {
        UpgradeOption[] stats =
        [
            UpgradeOption.FireRate, UpgradeOption.ProjectileSpeed, UpgradeOption.Range,
            UpgradeOption.MissileHealth, UpgradeOption.ArmorPierce, UpgradeOption.Damage
        ];

        var mask = stats.Aggregate(WeaponPreUpgrade.None, (current, stat) => current | WeaponUpgrade.BitOf(stat));

        Assert.Equal(stats.Length, WeaponUpgrade.StatsOf(mask).Length);
        foreach (var stat in stats)
        {
            Assert.NotEqual(WeaponPreUpgrade.None, WeaponUpgrade.BitOf(stat));
            Assert.Contains(stat, WeaponUpgrade.StatsOf(WeaponUpgrade.BitOf(stat)));
        }

        Assert.Empty(WeaponUpgrade.StatsOf(WeaponPreUpgrade.None));
    }

    // The card and the grant read this one call, so the numbers on the card are the numbers on the gun.
    [Fact]
    public void ArrivingTurret_IsTheTableGunWithExactlyTheStatsOfItsMask()
    {
        const WeaponPreUpgrade mask = WeaponPreUpgrade.FireRate | WeaponPreUpgrade.Damage;

        var expected = WeaponUpgrade.ApplyStat(
            WeaponUpgrade.ApplyStat(TurretMount.CarriedTurret(WeaponType.LaserLance), Card(UpgradeOption.FireRate)),
            Card(UpgradeOption.Damage));

        Assert.Equal(expected, WeaponUpgrade.ArrivingTurret(WeaponType.LaserLance, mask));
    }

    // Each arriving upgrade is counted once on the ship, which is what the stats screen reads and what the
    // next arriving gun is measured against (D2).
    [Fact]
    public void WithArrivingUpgrades_CountsOneCardPerStatOfTheSet()
    {
        const WeaponPreUpgrade mask = WeaponPreUpgrade.Range | WeaponPreUpgrade.Damage;

        var counts = WeaponUpgrade.WithArrivingUpgrades(UpgradeCounts.Empty, mask, WeaponType.RailGun.Name);

        Assert.Equal(1, counts.GetCount(UpgradeOption.Range, WeaponType.RailGun.Name));
        Assert.Equal(1, counts.GetCount(UpgradeOption.Damage, WeaponType.RailGun.Name));
        Assert.Equal(0, counts.GetCount(UpgradeOption.FireRate, WeaponType.RailGun.Name));
    }

    // An arriving upgrade is one **ordinary** card of its stat: the mask carries no power, so a swapped-in
    // build can never double its own steps (plans/SUPER_UPGRADES.md invariant 5).
    [Fact]
    public void ArrivingUpgrades_AreAlwaysOneOrdinaryCardOfTheirStat()
    {
        var plain = TurretMount.CarriedTurret(WeaponType.RailGun);

        var arriving = WeaponUpgrade.ArrivingTurret(WeaponType.RailGun, WeaponPreUpgrade.Range | WeaponPreUpgrade.ArmorPierce);

        Assert.Equal(plain.Range * UpgradeDefinition.Range.Multiplier, arriving.Range, 3);
        Assert.Equal(UpgradeDefinition.ArmorPierce.Additive, arriving.Weapon.Pierce);
    }

    // One ordinary card of this stat, wherever a test names a card rather than a stat.
    static UpgradableOption Card(UpgradeOption stat) => new("", stat);

    static List<UpgradeOption> Legal(WeaponType gun, Turret live, bool pierceUnlocked = true, CardPower power = CardPower.Base) =>
        WeaponUpgrade.LegalStats(gun, live, pierceUnlocked, power);
}
