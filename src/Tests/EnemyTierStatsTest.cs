using Spacevors.Domain;
using Spacevors.Domain.Components;
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
        var (health, turret) = Spawn(EnemyShipType.Default, 0f);

        Assert.Equal(3, health);
        Assert.Equal(1, turret.Weapon.Damage);
        Assert.Equal(1.5f, turret.Weapon.FireRate, precision: 3);
    }

    [Fact]
    public void SpawnAtTierBoundaries_ScalesHpDamageAndFireRate()
    {
        // Default ship (HP 3, fire rate 1.5): tier 1 at t=60 s, tier 2 at t=120 s.
        var (hp1, turret1) = Spawn(EnemyShipType.Default, 60f);
        Assert.Equal(4, hp1);                       // round(3 x 1.25)
        Assert.Equal(1, turret1.Weapon.Damage);     // +1 every 2 tiers: none yet
        Assert.Equal(1.65f, turret1.Weapon.FireRate, precision: 3);

        var (hp2, turret2) = Spawn(EnemyShipType.Default, 120f);
        Assert.Equal(5, hp2);                       // round(3 x 1.5), away from zero
        Assert.Equal(2, turret2.Weapon.Damage);     // +1 at tier 2
        Assert.Equal(1.8f, turret2.Weapon.FireRate, precision: 3);

        // Interceptor (HP 2) and HeavyCannon (HP 5) scale from their own base health.
        var (hpInt, _) = Spawn(EnemyShipType.Interceptor, 60f);
        Assert.Equal(3, hpInt);                     // round(2 x 1.25), away from zero

        var (hpHeavy, turretHeavy) = Spawn(EnemyShipType.HeavyCannon, 120f);
        Assert.Equal(8, hpHeavy);                   // round(5 x 1.5), away from zero
        Assert.Equal(2, turretHeavy.Weapon.Damage);
    }

    [Fact]
    public void SpawnPastMaxTier_StatsStayCapped()
    {
        var (hpEarly, turretEarly) = Spawn(EnemyShipType.Default, 600f);   // tier 10
        var (hpLate, turretLate) = Spawn(EnemyShipType.Default, 7200f);    // still tier 10

        Assert.Equal(hpEarly, hpLate);
        Assert.Equal(turretEarly.Weapon.Damage, turretLate.Weapon.Damage);
        Assert.Equal(turretEarly.Weapon.FireRate, turretLate.Weapon.FireRate);

        Assert.Equal(11, hpEarly);                  // round(3 x 3.5), away from zero
        Assert.Equal(6, turretEarly.Weapon.Damage); // base 1 + 10/2
        Assert.Equal(3f, turretEarly.Weapon.FireRate, precision: 3);       // 1.5 x 2.0
    }

    private static (int Health, Turret Turret) Spawn(EnemyShipType type, float elapsed)
    {
        var em = new EntityManager();
        var entity = em.CreateEntity();
        foreach (var component in EnemyShipFactory.CreateComponents(Vector2.Zero, Vector2.Zero, 0f, 0f, type, elapsed))
            component.Apply(em, entity);

        return (em.GetComponent<Health>(entity).Current, em.GetComponent<Turret>(entity));
    }
}
