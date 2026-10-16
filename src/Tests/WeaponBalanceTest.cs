using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// The weapon balance rules from plans/WEAPON_BALANCE.md, read straight off the weapon table so the
// next gun added cannot quietly break them. LoadTestWeapon is not a gun — it is the perf scenarios'
// 8000-pellet spray — so it is outside every rule here.
public class WeaponBalanceTest
{
    static WeaponType[] Guns => [.. WeaponType.All.Where(w => w.Name != WeaponType.LoadTestWeapon.Name)];

    static float Dps(WeaponType w) => w.Stats.FireRate * w.Stats.PelletCount * w.Stats.Damage;

    // Delivery class: how much a shot can lose on the way to the target.
    static string Delivery(WeaponType w) =>
        w.Stats.Hitscan ? "hitscan"
        : w.Stats.AmmoSpeed >= 2000f ? "fast"
        : w.Stats.AmmoSpeed >= 600f ? "medium"
        : "slow";

    // R6: the range on a card must be what the slowest round that gun fires can actually cross.
    [Fact]
    public void CardRangeIsReachableByTheSlowestAmmoItFires()
    {
        foreach (var w in Guns.Where(w => !w.Stats.Hitscan))
        {
            float worstReach = w.Stats.AmmoSpeed * (1f - TurretFiringSystem.AmmoSpeedVariation / 2f) * w.Stats.ShotLifetime;
            Assert.True(worstReach >= w.Range, $"{w.Name} promises {w.Range:0} px but its slowest round dies at {worstReach:0} px");
        }
    }

    // The reach floor: a gun shorter than the enemy's own firing ring cannot answer what shoots it.
    [Fact]
    public void NoGunReachesLessThanTheEnemyFiringRing()
    {
        foreach (var w in Guns)
            Assert.True(w.Range >= 600f, $"{w.Name} reaches only {w.Range:0} px");
    }

    // R7: rounds spawned per second from one mount stay bounded, so a loadout cannot blow up the
    // entity count the 10k-object goal is priced against.
    [Fact]
    public void EveryGunStaysInsideTheRoundRateBudget()
    {
        foreach (var w in Guns)
        {
            float rounds = w.Stats.FireRate * w.Stats.PelletCount;
            Assert.True(rounds <= 12f, $"{w.Name} fires {rounds:0.#} rounds a second from one mount");
        }
    }

    // R1: instant delivery is the cheapest delivery, so a beam buys less damage than the slowest
    // travelling round that covers the same band.
    [Fact]
    public void BeamGunDpsStaysUnderItsOwnBand()
    {
        foreach (var beam in Guns.Where(w => w.Stats.Hitscan))
        {
            var peers = Guns.Where(w => !w.Stats.Hitscan && MathF.Abs(w.Range - beam.Range) <= 0.15f * beam.Range).ToList();
            Assert.NotEmpty(peers);

            var slowest = peers[0];
            foreach (var peer in peers)
                if (peer.Stats.AmmoSpeed < slowest.Stats.AmmoSpeed) slowest = peer;

            Assert.True(Dps(beam) <= 0.8f * Dps(slowest),
                $"{beam.Name} at {Dps(beam):0.#} dps is too strong beside {slowest.Name} ({Dps(slowest):0.#} dps with {slowest.Stats.AmmoSpeed:0} px/s ammo)");
        }
    }

    // R3: a large mount is scarce, so a large gun of the same tier and delivery class must out-DPS
    // the best small gun of that class. Beams are exempt — R1 caps them from the other side.
    [Fact]
    public void LargeGunsEarnTheirMountWithDamage()
    {
        foreach (var group in Guns.Where(w => Delivery(w) != "hitscan").GroupBy(w => (w.UnlockLevel, Delivery(w))))
        {
            var small = group.Where(w => w.Size == TurretSize.Small).ToList();
            var large = group.Where(w => w.Size == TurretSize.Large).ToList();
            if (small.Count == 0 || large.Count == 0) continue;

            float weakestLarge = large.Min(Dps);
            float bestSmall = small.Max(Dps);
            Assert.True(weakestLarge >= 1.25f * bestSmall,
                $"{group.Key}: the weakest large gun is at {weakestLarge:0.#} dps, the best small one at {bestSmall:0.#}");
        }
    }

    // R4: tier 2 is a step up, never a leap. The floor exempts beams for the reason R3 does: their
    // tier-2 edge is reach and extra beams, not damage.
    [Fact]
    public void TierTwoIsSlightlyStrongerNotTwiceAsStrong()
    {
        foreach (var w in Guns.Where(w => w.UnlockLevel > 1))
        {
            var tierOne = Guns.Where(g => g.UnlockLevel == 1 && g.Size == w.Size).ToList();

            Assert.True(Dps(w) <= 1.4f * tierOne.Max(Dps), $"{w.Name} is more than 1.4x every tier-1 {w.Size} gun");
            if (w.Stats.Hitscan) continue;

            bool aStepUp = tierOne.Any(g => Dps(w) >= 1.15f * Dps(g) || (Dps(w) >= Dps(g) && w.Range >= 1.3f * g.Range));
            Assert.True(aStepUp, $"{w.Name} neither out-damages nor clearly outranges a tier-1 {w.Size} gun");
        }
    }

    // R9: only the deliberate heavy snipers may slew slower than a Balanced hull turns, which is
    // what drags their aim off target while the ship spins.
    [Fact]
    public void OnlyTheHeavyGunsSlewSlowerThanTheHullTurns()
    {
        int slowerThanHull = Guns.Count(w => w.Stats.TurnSpeed < EngineLayout.Balanced.TurnRate);

        Assert.True(slowerThanHull <= 2, $"{slowerThanHull} guns lose their target while a Balanced hull spins");
    }

    // R13: one gun per cell of (range band, delivery, fire rate, whether one shot carries several
    // pellets, mount size).
    [Fact]
    public void NoTwoGunsShareAnIdentityCell()
    {
        var cells = Guns.Select(w => (
            Band: w.Range < 750f ? "aura" : w.Range < 1100f ? "close" : w.Range < 1500f ? "mid" : "long",
            Delivery: Delivery(w),
            Rate: w.Stats.FireRate >= 4f ? "stream" : w.Stats.FireRate >= 1.5f ? "burst" : "volley",
            Shot: w.Stats.PelletCount > 1 ? $"{w.Stats.PelletCount} pellets" : "single",
            w.Size)).ToList();

        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    // R12: a bigger hull is a worse dodge, so the big ship may carry more firepower from the start
    // and the light one starts slightly behind the mediums.
    [Fact]
    public void StarterFirepowerFollowsHullSize()
    {
        float Firepower(ShipType ship) => ship.Loadout.Mounts.Sum(m => Dps(m.Weapon));

        float scout = Firepower(ShipType.Scout);
        float fighter = Firepower(ShipType.Fighter);
        float shadow = Firepower(ShipType.Shadow);
        float heavy = Firepower(ShipType.Heavy);

        Assert.True(scout < fighter && scout < shadow, $"the 46 px Scout ({scout:0.#}) should start behind the 58 px hulls ({fighter:0.#}, {shadow:0.#})");
        Assert.True(heavy > fighter && heavy > shadow, $"the 84 px Heavy ({heavy:0.#}) should start ahead of the mediums ({fighter:0.#}, {shadow:0.#})");
    }
}
