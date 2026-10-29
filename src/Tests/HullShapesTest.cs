using Spacevors.Domain;
using Spacevors.Domain.Stats;
using Xunit;

// The authored hull shapes (plans/NON_CIRCLE_COLLISION.md) are data, so the things that would break
// silently are pinned here rather than left to a screenshot.
public class HullShapesTest
{
    // A capsule that reaches past the hull's collision radius is a real bug, not a cosmetic one: the
    // spatial grid indexes and queries by that radius, so a shape growing past it stops being found
    // by the queries that are supposed to hit it.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HullShape_FitsInsideTheBoundingRadiusThatIndexesIt(byte graphicsId)
    {
        foreach (var capsule in HullShapes.ForEnemy(graphicsId).Capsules)
        {
            Assert.True(capsule.HalfThickness > 0f, $"hull {graphicsId} has a capsule with no thickness");
            AssertInside(capsule.From, capsule.HalfThickness, graphicsId, capsule);
            AssertInside(capsule.To, capsule.HalfThickness, graphicsId, capsule);
        }
    }

    static void AssertInside(Vector2 end, float halfThickness, byte graphicsId, Capsule capsule)
    {
        // The round cap reaches halfThickness beyond the axis end, in the worst direction.
        float reach = end.Magnitude + halfThickness;
        Assert.True(reach <= 1.001f,
            $"hull {graphicsId} capsule {capsule} reaches {reach:0.000} radii from the centre, but the " +
            "broad phase only looks one radius out");
    }

    // Hulls that share art share a shape, and an art id the table does not know must collide exactly
    // as it did before shapes existed rather than as nothing at all.
    [Fact]
    public void UnknownArtId_FallsBackToTheCircleItAlwaysHad()
    {
        Assert.True(HullShapes.ForEnemy(0).IsCircle, "the swarmer's paint is a blob; its shape stays a disc");
        Assert.True(HullShapes.ForEnemy(99).IsCircle);

        Assert.False(HullShapes.ForEnemy(1).IsCircle, "the interceptor's disc is 60% air — it needs the cross");
        Assert.False(HullShapes.ForEnemy(2).IsCircle);
    }

    // Every enemy art id in play must have a deliberate entry, or a new hull silently collides as a
    // disc while looking like a hull.
    [Fact]
    public void EveryEnemyHullArt_HasAnAuthoredEntry()
    {
        for (byte id = 0; id < EnemyShipType.All.Length; id++)
            Assert.NotNull(HullShapes.ForEnemy(id));

        Assert.Equal(3, EnemyShipType.All.Length); // a fourth hull means a fourth shape to author
    }
}
