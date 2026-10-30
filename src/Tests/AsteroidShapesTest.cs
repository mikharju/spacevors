using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Xunit;

// The authored asteroid shapes (plans/NON_CIRCLE_COLLISION.md) are data, so what would break silently
// is pinned here rather than left to a screenshot.
public class AsteroidShapesTest
{
    // Same rule as for hulls: the spatial grid indexes a rock by its collision radius, so a shape that
    // reaches past that radius stops being found by the queries that are supposed to hit it.
    [Theory]
    [InlineData(false, (byte)0)]
    [InlineData(false, (byte)3)]
    [InlineData(false, (byte)6)]
    [InlineData(true, (byte)0)]
    [InlineData(true, (byte)3)]
    [InlineData(true, (byte)4)]
    public void AsteroidShape_FitsInsideTheBoundingRadiusThatIndexesIt(bool isSmall, byte variant)
    {
        string size = isSmall ? "small" : "large";
        foreach (var capsule in AsteroidShapes.For(isSmall, variant).Capsules)
        {
            Assert.True(capsule.HalfThickness > 0f, $"{size} rock variant {variant} has a capsule with no thickness");
            AssertInside(size, variant, capsule, capsule.From);
            AssertInside(size, variant, capsule, capsule.To);
        }
    }

    static void AssertInside(string size, byte variant, Capsule capsule, Vector2 end)
    {
        float reach = end.Magnitude + capsule.HalfThickness;
        Assert.True(reach <= 1.001f,
            $"{size} rock variant {variant} capsule {capsule} reaches {reach:0.000} radii from the centre, " +
            "but the broad phase only looks one radius out");
    }

    // The lumpy art is the whole reason rocks have shapes, and the round art is the reason the rest stay
    // discs. A variant index the table does not know collides as it always did.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundArt_StaysADisc_LumpyArtDoesNot(bool isSmall)
    {
        for (byte variant = 0; variant < 3; variant++)
            Assert.True(AsteroidShapes.For(isSmall, variant).IsCircle,
                $"the {Third(variant)} round rock's art fills its circle, so its shape stays a disc");

        Assert.False(AsteroidShapes.For(isSmall, 3).IsCircle, "the first lumpy rock needs capsules");
        Assert.True(AsteroidShapes.For(isSmall, (byte)99).IsCircle);
    }

    static string Third(byte variant) => variant switch { 0 => "first", 1 => "second", _ => "third" };

    // The factory rolls a variant up to each count, so every index it can roll has to be a deliberate
    // entry: raising a count without authoring the new art's shape leaves that rock colliding as a disc.
    [Fact]
    public void EveryVariantTheFactoryCanRoll_HasAnAuthoredEntry()
    {
        Assert.False(AsteroidShapes.For(true, (byte)(Asteroid.SmallVariantCount - 1)).IsCircle);
        Assert.False(AsteroidShapes.For(false, (byte)(Asteroid.LargeVariantCount - 1)).IsCircle);
    }
}
