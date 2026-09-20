using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class EnemyTierStatsTest
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(59.9f, 0)]
    [InlineData(60f, 1)]
    [InlineData(119.9f, 1)]
    [InlineData(120f, 2)]
    [InlineData(3000f, 10)]   // capped at MaxTier (10 minutes)
    [InlineData(7200f, 10)]   // stays capped after the cap
    public void TierFor_DerivesFromElapsedTime_Capped(float elapsed, int expected)
    {
        Assert.Equal(expected, EnemyShipFactory.TierFor(elapsed));
    }

    [Fact]
    public void SpawnAtTimeZero_UsesBaseStats()
    {
        var type = EnemyShipType.Default;
        var (health, turret) = Spawn(type, 0f);

        Assert.Equal(type.Health, health);
        Assert.Equal(1, turret.Weapon.Damage); // base enemy damage before tier scaling
        Assert.Equal(type.TurretFireRate, turret.Weapon.FireRate, precision: 3);
    }

    [Fact]
    public void SpawnAtTierBoundaries_ScalesHpDamageAndFireRate()
    {
        var type = EnemyShipType.Default;
        int baseDamage = Spawn(type, 0f).Turret.Weapon.Damage;

        // Tier 1 at t=60 s.
        var (hp1, turret1) = Spawn(type, 60f);
        Assert.Equal(ExpectedHealth(type, 1), hp1);
        Assert.Equal(baseDamage + EnemyShipFactory.DamageAdd(1), turret1.Weapon.Damage); // no add yet at tier 1
        Assert.Equal(ExpectedFireRate(type, 1), turret1.Weapon.FireRate, precision: 3);

        // Tier 2 at t=120 s.
        var (hp2, turret2) = Spawn(type, 120f);
        Assert.Equal(ExpectedHealth(type, 2), hp2);
        Assert.Equal(baseDamage + EnemyShipFactory.DamageAdd(2), turret2.Weapon.Damage); // +1 at tier 2
        Assert.Equal(ExpectedFireRate(type, 2), turret2.Weapon.FireRate, precision: 3);

        // Other ship types scale from their own recorded base stats.
        var (hpInt, _) = Spawn(EnemyShipType.Interceptor, 60f);
        Assert.Equal(ExpectedHealth(EnemyShipType.Interceptor, 1), hpInt);

        var (hpHeavy, turretHeavy) = Spawn(EnemyShipType.HeavyCannon, 120f);
        Assert.Equal(ExpectedHealth(EnemyShipType.HeavyCannon, 2), hpHeavy);
        Assert.Equal(baseDamage + EnemyShipFactory.DamageAdd(2), turretHeavy.Weapon.Damage);
    }

    [Fact]
    public void SpawnPastMaxTier_StatsStayCapped()
    {
        var type = EnemyShipType.Default;
        int baseDamage = Spawn(type, 0f).Turret.Weapon.Damage;
        int cappedTier = EnemyShipFactory.TierFor(7200f);

        var (hpEarly, turretEarly) = Spawn(type, 600f);   // at the cap
        var (hpLate, turretLate) = Spawn(type, 7200f);    // still capped

        Assert.Equal(hpEarly, hpLate);
        Assert.Equal(turretEarly.Weapon.Damage, turretLate.Weapon.Damage);
        Assert.Equal(turretEarly.Weapon.FireRate, turretLate.Weapon.FireRate);

        Assert.Equal(ExpectedHealth(type, cappedTier), hpEarly);
        Assert.Equal(baseDamage + EnemyShipFactory.DamageAdd(cappedTier), turretEarly.Weapon.Damage);
        Assert.Equal(ExpectedFireRate(type, cappedTier), turretEarly.Weapon.FireRate, precision: 3);
    }

    // Expected stats derived from the base values recorded on EnemyShipType and the factory's public
    // tier formulas, so tuning base health or fire rate does not break these tests.
    private static int ExpectedHealth(EnemyShipType type, int tier) =>
        (int)MathF.Round(type.Health * EnemyShipFactory.HpMultiplier(tier), MidpointRounding.AwayFromZero);

    private static float ExpectedFireRate(EnemyShipType type, int tier) =>
        type.TurretFireRate * EnemyShipFactory.FireRateMultiplier(tier);

    private static (int Health, Turret Turret) Spawn(EnemyShipType type, float elapsed)
    {
        var em = new EntityManager();
        var entity = em.CreateEntity();
        foreach (var component in EnemyShipFactory.CreateComponents(Vector2.Zero, Vector2.Zero, 0f, 0f, type, elapsed))
            component.Apply(em, entity);

        return (em.GetComponent<Health>(entity).Current, em.GetComponent<Turret>(entity));
    }
}
