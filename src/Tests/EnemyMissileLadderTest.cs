using Spacevors.Domain;
using Spacevors.Domain.Stats;
using Xunit;

// The armament ladder (plans/MISSILES.md R20-R22): it only ever adds; a rack launches from farther than
// the guns it is mounted on, but from no farther than the player can answer; a round's fuse crosses its
// own launch ring; its damage stays under the bullet fired at the same tier; and the number of rounds
// allowed in the air climbs one step at a time.
public class EnemyMissileLadderTests
{
    // The longest gun in the player's hands. Nothing on the enemy side may fire from beyond it, or the
    // player is shot at from a distance no answer exists for.
    private static readonly float LongestPlayerGun = WeaponType.GravitonBurst.Range;

    [Fact]
    public void EveryRungOfTheLadderAddsToTheOneBelowIt()
    {
        foreach (var type in EnemyShipType.All)
        {
            float armed = 0f;
            for (int tier = 0; tier <= EnemyShipStats.MaxCompositionTier; tier++)
            {
                float now = ArmedWeights(type, tier);
                Assert.True(now >= armed, $"{type.TextureKey} loses an armament between composition tiers {tier - 1} and {tier}");
                armed = now;
            }
        }
    }

    [Fact]
    public void TheMiddleHullCarriesAFewRacksAndThenAnEvenMix()
    {
        Assert.Equal(0f, ArmedShareOfItsSpawns(EnemyShipType.Interceptor, 2));
        Assert.InRange(ArmedShareOfItsSpawns(EnemyShipType.Interceptor, 3), 0.28f, 0.32f);
        Assert.InRange(ArmedShareOfItsSpawns(EnemyShipType.Interceptor, 5), 0.49f, 0.51f);
    }

    [Fact]
    public void TheLargeHullCarriesItsRackLastAndThenAlmostAlways()
    {
        Assert.Equal(0f, ArmedShareOfItsSpawns(EnemyShipType.HeavyCannon, 7));
        Assert.InRange(ArmedShareOfItsSpawns(EnemyShipType.HeavyCannon, 8), 0.49f, 0.51f);
        Assert.True(ArmedShareOfItsSpawns(EnemyShipType.HeavyCannon, 10) >= 0.75f,
            "from composition tier 10 the long firing line is expected to carry its rack");
    }

    [Fact]
    public void TheSwarmerNeverCarriesARack()
    {
        for (int tier = 0; tier <= EnemyShipStats.MaxCompositionTier; tier++)
            Assert.Equal(0f, ArmedWeights(EnemyShipType.Default, tier));
    }

    [Fact]
    public void TheStartingFieldCarriesNoMissileHull()
    {
        // WorldFactory builds the first field at elapsed 0, so this is what the player meets first.
        var rng = new Random(1);
        for (int roll = 0; roll < 600; roll++)
        {
            var (_, armament) = EnemyShipStats.PickComposition(rng, EnemyShipStats.CompositionTierFor(0f));
            Assert.Equal(EnemyArmament.Guns, armament);
        }
    }

    [Fact]
    public void ARackOutreachesItsOwnGunsButNotThePlayersLongestGun()
    {
        foreach (var type in EnemyShipType.All)
        {
            if (type.Missiles is null) continue;

            for (int tier = 0; tier <= EnemyShipStats.MaxTier; tier++)
            {
                float ring = EnemyShipStats.MissileRangeFor(type, tier);
                Assert.True(ring > EnemyShipStats.FiringRangeFor(type, tier),
                    $"{type.TextureKey} at tier {tier}: a rack has to launch from outside the gun line it is mounted on");
                Assert.True(ring <= EnemyShipStats.MaxMissileFiringRange,
                    $"{type.TextureKey} at tier {tier}: {ring} px is past the rack ceiling");
                Assert.True(ring < LongestPlayerGun,
                    $"{type.TextureKey} at tier {tier}: {ring} px is past the range the player can answer");
            }
        }
    }

    [Fact]
    public void ARoundsFuseCrossesItsOwnLaunchRing()
    {
        foreach (var type in EnemyShipType.All)
        {
            if (type.Missiles is not { } spec) continue;

            for (int tier = 0; tier <= EnemyShipStats.MaxTier; tier++)
            {
                float reach = EnemyShipStats.MissileReachFor(spec, tier);
                float ring = EnemyShipStats.MissileRangeFor(type, tier);
                Assert.True(reach >= ring,
                    $"{type.TextureKey} at tier {tier}: its round crosses {reach:0} px on a {spec.Fuse} s fuse counting the launch ramp, short of its own {ring:0} px ring");
            }
        }
    }

    [Fact]
    public void ARackNeverOutDamagesTheBulletItIsFiredBeside()
    {
        // Only over the composition tiers at which the armament can actually roll: that is what keeps the
        // large hull's rack premium honest, since guns+missiles arrive four minutes after the rack's own
        // damage ladder has pulled clear of the bullet's.
        foreach (var type in EnemyShipType.All)
        {
            if (type.Missiles is not { } spec) continue;

            for (int compositionTier = 0; compositionTier <= EnemyShipStats.MaxCompositionTier; compositionTier++)
            {
                if (!EnemyShipStats.UnlockedArmaments(type, compositionTier).Any(a => a.Armament != EnemyArmament.Guns)) continue;

                int statTier = EnemyShipStats.TierFor(compositionTier * EnemyShipStats.TierDuration);
                int round = EnemyShipStats.BaseDamage + EnemyShipStats.MissileDamageAdd(statTier) + spec.DamageBonus;
                int bullet = EnemyShipStats.BaseDamage + EnemyShipStats.DamageAdd(statTier);
                Assert.True(round <= bullet,
                    $"{type.TextureKey} at composition tier {compositionTier}: a steering round lands {round} against the bullet's {bullet}");
            }
        }
    }

    [Fact]
    public void TheRoundsAllowedInTheAirClimbOneStepAtATime()
    {
        int previous = EnemyShipStats.MissileCapFor(0);
        Assert.Equal(0, previous);

        int lastRungTier = 0;
        for (int tier = 1; tier <= EnemyShipStats.MaxCompositionTier; tier++)
        {
            int cap = EnemyShipStats.MissileCapFor(tier);
            if (cap == previous) continue;

            Assert.True(cap > previous, $"the in-flight cap went backwards at composition tier {tier}");
            // One more round at a time, except where the ladder first opens from nothing.
            Assert.True(cap - previous == 1 || previous == 0,
                $"the in-flight cap jumped from {previous} to {cap} at composition tier {tier}");
            // Two minutes apart at the tightest: the pressure has to arrive as a change the player can
            // notice, not as a wall.
            Assert.True(tier - lastRungTier >= 2, $"the cap rose again one minute after the rung at tier {lastRungTier}");

            previous = cap;
            lastRungTier = tier;
        }

        Assert.Equal(5, EnemyShipStats.MissileCapFor(10));
        Assert.Equal(7, EnemyShipStats.MissileCapFor(20));
    }

    [Fact]
    public void EnemyRacksStayInsideTheEnemyCeilings()
    {
        foreach (var type in EnemyShipType.All)
        {
            if (type.Missiles is not { } spec) continue;

            Assert.True(spec.TurnRate <= EnemyShipStats.MaxMissileTurnRate,
                $"{type.TextureKey}'s rounds turn tighter than any enemy round is allowed to");
            Assert.True(spec.Fuse <= EnemyShipStats.MaxMissileFuse);

            for (int tier = 0; tier <= EnemyShipStats.MaxTier; tier++)
                Assert.True(EnemyShipStats.MissileSpeedFor(spec, tier) <= EnemyShipStats.MaxAmmoSpeed,
                    $"{type.TextureKey} at tier {tier} throws a round faster than the readability ceiling");
        }
    }

    private static float ArmedWeights(EnemyShipType type, int compositionTier) =>
        EnemyShipStats.UnlockedArmaments(type, compositionTier)
            .Where(a => a.Armament != EnemyArmament.Guns)
            .Sum(a => a.Weight);

    // The share of this hull's own spawns that carries a rack, straight off the weights the draw reads.
    private static float ArmedShareOfItsSpawns(EnemyShipType type, int compositionTier)
    {
        var unlocked = EnemyShipStats.UnlockedArmaments(type, compositionTier).ToList();
        return ArmedWeights(type, compositionTier) / unlocked.Sum(a => a.Weight);
    }
}
