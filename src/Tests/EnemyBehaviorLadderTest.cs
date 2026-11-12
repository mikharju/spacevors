using Spacevors.Domain.Stats;
using Xunit;

namespace Tests;

// The manoeuvre ladder read as data (plans/ENEMY_BEHAVIORS.md). Asserted against the weight table rather
// than against dice rolls, so a table edit that breaks a rule fails at once instead of showing up as a
// feel problem twenty minutes into a run.
public class EnemyBehaviorLadderTest
{
    private const float Epsilon = 1e-4f;

    // Share of every spawn draw at this composition tier whose behaviour satisfies the predicate.
    private static float FleetShare(int compositionTier, EnemyBehavior behavior)
    {
        float total = 0f, matching = 0f;
        foreach (var type in EnemyShipType.All)
            foreach (var entry in EnemyShipStats.UnlockedArmaments(type, compositionTier))
            {
                total += type.SpawnWeight * entry.Weight;
                if (entry.Behavior == behavior) matching += type.SpawnWeight * entry.Weight;
            }

        return matching / total;
    }

    private static float HullWeight(EnemyShipType type, int compositionTier)
    {
        float total = 0f;
        foreach (var entry in EnemyShipStats.UnlockedArmaments(type, compositionTier)) total += entry.Weight;
        return total;
    }

    private static float BehaviorWeight(EnemyShipType type, int compositionTier, EnemyBehavior behavior)
    {
        float total = 0f;
        foreach (var entry in EnemyShipStats.UnlockedArmaments(type, compositionTier))
            if (entry.Behavior == behavior) total += entry.Weight;
        return total;
    }

    // Every hull's *gun* rows sum to 1 at every tier: a behaviour is a way of holding a gun, so it takes
    // its share out of the gun budget instead of inflating the hull. That is what keeps the missile shares
    // of plans/MISSILES.md R20 meaning what they said before any behaviour existed.
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(20)]
    public void BehavioursSplitTheGunBudgetInsteadOfAddingToIt(int compositionTier)
    {
        foreach (var type in EnemyShipType.All)
        {
            float guns = 0f;
            foreach (var entry in EnemyShipStats.UnlockedArmaments(type, compositionTier))
                if (entry.Armament == EnemyArmament.Guns) guns += entry.Weight;

            Assert.Equal(1f, guns, 3);
        }
    }

    // The opening fleet is untouched: nothing manoeuvres until the second composition tier, so the first
    // two minutes stay the game the existing difficulty tests describe. The first flanker arrives at 2:00
    // rather than 3:00 (plans/ENEMY_BEHAVIORS.md follow-up), because at 3:00 and 4 % of spawns the whole
    // behaviour was one hull per wave — a thing you notice once in a run, not a thing the fleet does.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void NothingManoeuvresInTheOpening(int compositionTier)
    {
        foreach (var type in EnemyShipType.All)
            foreach (var entry in EnemyShipStats.UnlockedArmaments(type, compositionTier))
                Assert.Equal(EnemyBehavior.Chase, entry.Behavior);
    }

    [Fact]
    public void TheOpeningFieldChases()
    {
        var rng = new Random(7);
        for (int roll = 0; roll < 600; roll++)
        {
            var (_, _, behavior) = EnemyShipStats.PickComposition(rng, 0);
            Assert.Equal(EnemyBehavior.Chase, behavior);
        }
    }

    // The tight ball is what the player's area guns are paid for, so the manœuvres stay a clear minority of
    // every spawn at every tier — the user's rule: never all, never even a majority. Measured peak is 13.8 %
    // of spawns at tier 8 (plans/ENEMY_BEHAVIORS.md follow-up), so a fifth is real slack, not a rounding.
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(20)]
    public void TheFleetNeverLosesItsMajorityToSpread(int compositionTier)
        => Assert.True(FleetShare(compositionTier, EnemyBehavior.Spread) <= 0.2f + Epsilon,
            $"spread holds {FleetShare(compositionTier, EnemyBehavior.Spread):P0} of every spawn at tier {compositionTier}");

    // And the swarmer, the hull the ball is made of, keeps three quarters of itself converging even at the
    // widest rung of its own ladder.
    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(20)]
    public void TheSwarmerNeverHandsAwayItsMajority(int compositionTier)
        => Assert.True(BehaviorWeight(EnemyShipType.Default, compositionTier, EnemyBehavior.Spread) / HullWeight(EnemyShipType.Default, compositionTier) <= 0.25f + Epsilon,
            $"the swarmer hands away {BehaviorWeight(EnemyShipType.Default, compositionTier, EnemyBehavior.Spread) / HullWeight(EnemyShipType.Default, compositionTier):P0} of itself at tier {compositionTier}");

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(20)]
    public void AHidingHullIsAlwaysAMinorityOfTheFleet(int compositionTier)
        => Assert.True(FleetShare(compositionTier, EnemyBehavior.CoverTuck) <= 0.25f + Epsilon,
            $"crippled hulls tuck {FleetShare(compositionTier, EnemyBehavior.CoverTuck):P0} of every spawn at tier {compositionTier}");

    // A ram is a role, not the fleet's behaviour: it stays a minority of its own hull at every tier, so
    // there is always a shooting version of the same hull left to read.
    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(20)]
    public void ARamIsAlwaysAMinorityOfItsOwnHull(int compositionTier)
    {
        foreach (var type in EnemyShipType.All)
        {
            float share = BehaviorWeight(type, compositionTier, EnemyBehavior.Rammer) / HullWeight(type, compositionTier);
            Assert.True(share <= 0.15f + Epsilon, $"{type.TextureKey} rams {share:P0} of its own spawns at tier {compositionTier}");
        }
    }

    // Hiding is only worth doing when there is hp left to protect, so tuck rides on the two hulls with
    // bulk (HeavyCannon 120, Carmine 88) and never on the 20-60 hp hulls that die in one volley anyway.
    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(20)]
    public void OnlyHullsWithHpToSpareHideBehindFriends(int compositionTier)
    {
        foreach (var type in EnemyShipType.All)
        {
            if (BehaviorWeight(type, compositionTier, EnemyBehavior.CoverTuck) <= 0f) continue;
            Assert.True(type.Health >= 80, $"{type.TextureKey} hides behind friends with only {type.Health} hp");
        }
    }

    // A hull that is charging in and a hull that is hiding are opposite intentions: no hull and tier
    // offers both, so the fleet never shows the player a hull that cannot decide what it is doing.
    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(20)]
    public void NoHullIsGivenContradictoryManoeuvres(int compositionTier)
    {
        foreach (var type in EnemyShipType.All)
        {
            bool rams = BehaviorWeight(type, compositionTier, EnemyBehavior.Rammer) > 0f;
            bool hides = BehaviorWeight(type, compositionTier, EnemyBehavior.CoverTuck) > 0f;
            Assert.False(rams && hides, $"{type.TextureKey} is told to charge and to hide at tier {compositionTier}");
        }
    }
}
