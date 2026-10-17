using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// The ceilings on the difficulty ladder (plans/DIFFICULTY_SCALING.md R2.7), in the WeaponBalanceTest
// style: the fleet may get deadlier with time, but not past the point where the player's own weapon
// ladder and the dodge-based readability of the fight stop making sense.
public class EnemyDifficultyTest
{
    // The player's mid band: MachineGun-class guns reach 1350 px. An enemy engages at
    // FiringRange + the player's hull radius, so the ring the tier ladder produces has to stay inside it.
    const float PlayerMidBandRange = 1350f;

    // A starter Scout's sustained fire has to drop the toughest hull in single-digit seconds.
    const float ToughestHullTtkBudgetSeconds = 7f;

    [Fact]
    public void EnemiesNeverOutReachThePlayersMidBand()
    {
        float widestRing = EnemyShipType.All.Max(t => EnemyShipStats.FiringRangeFor(t, EnemyShipStats.MaxTier));
        widestRing += ShipType.All.Max(s => s.Radius); // the hull being shot at, taking the widest one

        Assert.True(widestRing < PlayerMidBandRange,
            $"the tier-{EnemyShipStats.MaxTier} firing ring is {widestRing:F0} px, at or past the player's {PlayerMidBandRange:F0} px mid band");
    }

    [Fact]
    public void EnemyAmmoStaysSlowerThanThePlayersSlowestRound()
    {
        float fastestEnemyAmmo = EnemyShipType.All.Max(t => EnemyShipStats.AmmoSpeedFor(t, EnemyShipStats.MaxTier));

        // Guided rounds are not in this comparison: a missile is not something the player out-flies,
        // and it is deliberately slower than anything else. What keeps *it* fair is its turn radius.
        float slowestPlayerRound = WeaponType.All
            .Where(w => w.Name != WeaponType.LoadTestWeapon.Name && !w.Stats.Hitscan && !w.Stats.Homing)
            .Min(w => w.Stats.AmmoSpeed);

        Assert.True(fastestEnemyAmmo <= EnemyShipStats.MaxAmmoSpeed,
            $"enemy ammo reaches {fastestEnemyAmmo:F0} px/s, over the {EnemyShipStats.MaxAmmoSpeed:F0} px/s readability ceiling");
        Assert.True(fastestEnemyAmmo < slowestPlayerRound,
            $"enemy ammo reaches {fastestEnemyAmmo:F0} px/s while the player's slowest round is {slowestPlayerRound:F0} px/s");
    }

    [Fact]
    public void ToughestTierHullDiesInsideTheTtkBudget()
    {
        float scoutDps = ShipType.Scout.Loadout.Mounts
            .Sum(m => m.Weapon.Stats.FireRate * m.Weapon.Stats.PelletCount * m.Weapon.Stats.Damage);
        int toughestHp = (int)MathF.Round(
            EnemyShipType.HeavyCannon.Health * EnemyShipStats.HpMultiplier(EnemyShipStats.MaxTier),
            MidpointRounding.AwayFromZero);

        float ttk = toughestHp / scoutDps;
        Assert.True(ttk <= ToughestHullTtkBudgetSeconds,
            $"a tier-{EnemyShipStats.MaxTier} HeavyCannon survives {ttk:F1} s of a starter Scout's fire");
    }

    [Fact]
    public void TheLadderDriftsEachRoleInsteadOfTheWholeFleet()
    {
        int tier = EnemyShipStats.MaxTier;

        // The Interceptor trades reach for closing speed; the HeavyCannon is the long firing line and
        // does not get faster; the tracking drift applies to everyone.
        Assert.True(EnemyShipStats.FiringRangeFor(EnemyShipType.Interceptor, tier) < EnemyShipType.Interceptor.FiringRange);
        Assert.True(EnemyShipStats.FiringRangeFor(EnemyShipType.HeavyCannon, tier) > EnemyShipType.HeavyCannon.FiringRange);
        Assert.Equal(EnemyShipType.HeavyCannon.Speed, EnemyShipStats.SpeedFor(EnemyShipType.HeavyCannon, tier), precision: 3);
        Assert.True(EnemyShipStats.SpeedFor(EnemyShipType.Interceptor, tier) > EnemyShipType.Interceptor.Speed);
        Assert.True(EnemyShipStats.TurnRateFor(EnemyShipType.Default, tier) > EnemyShipType.Default.TurnRate);

        // The knobs above are reach, closing speed and tracking only. The gun itself stays on the old
        // ladder, so a hit at the cap is still 6 damage instead of a compound of everything.
        var (_, gun) = SpawnAt(EnemyShipType.Default, 600f);
        Assert.Equal(EnemyShipStats.BaseDamage + EnemyShipStats.DamageAdd(EnemyShipStats.MaxTier), gun.Weapon.Damage);
        Assert.Equal(EnemyShipStats.WeaponPelletCount, gun.Weapon.PelletCount);
    }

    [Fact]
    public void HullAndGunKeepTheSameGrownRange()
    {
        // The chase system brakes the hull at FiringRange and the turret fires at its Range. If the two
        // drift apart, ships stop somewhere their own guns cannot reach.
        foreach (var type in EnemyShipType.All)
        {
            var (hull, turret) = SpawnAt(type, 600f); // at the tier cap
            float grown = EnemyShipStats.FiringRangeFor(type, EnemyShipStats.MaxTier);

            Assert.Equal(grown, hull.FiringRange, precision: 3);
            Assert.Equal(hull.FiringRange, turret.Range, precision: 3);
            Assert.Equal(EnemyShipStats.AmmoSpeedFor(type, EnemyShipStats.MaxTier), turret.Weapon.AmmoSpeed, precision: 3);
            Assert.Equal(EnemyShipStats.AmmoSpeedFor(type, EnemyShipStats.MaxTier), hull.TurretAmmoSpeed, precision: 3);
        }
    }

    private static (EnemyShip Hull, Turret Turret) SpawnAt(EnemyShipType type, float elapsed)
    {
        var em = new EntityManager();
        var ship = em.CreateEntity();
        foreach (var component in EnemyShipFactory.CreateComponents(Vector2.Zero, Vector2.Zero, 0f, 0f, type, elapsed))
            component.Apply(em, ship);

        return (em.GetComponent<EnemyShip>(ship), em.GetComponent<Turret>(ship));
    }
}
