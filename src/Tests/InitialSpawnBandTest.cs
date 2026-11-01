using Spacevors.Domain.Stats;
using Xunit;

namespace Tests;

// P4's staggered opening and P1's cull pull against each other. The cull is a distance from the player
// re-checked every tick, so an opening ship placed just inside it dies as soon as the player runs the
// other way — never fought, XP never earned — and the staggered arrival the band exists to create is
// silently undone. So the band needs slack at the far edge and honest range at the near one.
public class InitialSpawnBandTest
{
    // A few seconds of running away at a normal early escape speed, paid for by the cull.
    private const float MinCullSlack = 1500f;

    [Fact]
    public void FarEdgeOfTheBandStaysInsideTheCullWithSlack()
    {
        float slack = SpawningStats.Chase.CullDistance - SpawningStats.InitialWorld.EnemyShipMaxDistance;

        Assert.True(slack >= MinCullSlack,
            $"the farthest opening ship has {slack:F0} px of slack inside the {SpawningStats.Chase.CullDistance:F0} px cull, under {MinCullSlack:F0}");
    }

    [Fact]
    public void NearEdgeOfTheBandStaysBeyondTheWidestFiringRing()
    {
        // No opening ship may start the run already shooting: the band's near edge has to clear the widest
        // grown gun plus the widest player hull.
        float widestRing = EnemyShipType.All.Max(t => EnemyShipStats.FiringRangeFor(t, EnemyShipStats.MaxTier));
        widestRing += ShipType.All.Max(s => s.Radius);

        Assert.True(SpawningStats.InitialWorld.EnemyShipMinDistance > widestRing,
            $"the band's near edge is {SpawningStats.InitialWorld.EnemyShipMinDistance:F0} px, inside the {widestRing:F0} px firing ring");
    }
}
