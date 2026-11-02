using Spacevors.Domain;
using Spacevors.Domain.Stats;
using Spacevors.Game;
using Xunit;

// The support distance anchors flame and light bases onto the hull (plans/THRUSTER_FLAME_ANCHORS.md).
// A wrong number here moves every flame in the game, so the anchors are pinned against the painted
// extents measured from the shipped sprites (.tools/measure-hull.py), not against a screenshot.
public class HullShapeSupportTest
{
    [Fact]
    public void Support_IsTheFarEndpointProjectionPlusTheCap()
    {
        var limb = new Capsule(new Vector2(-0.2f, 0f), new Vector2(0.6f, 0f), 0.15f);
        Assert.Equal(0.75, limb.SupportAlong(new Vector2(1f, 0f)), 5);   // 0.6 + 0.15
        Assert.Equal(0.35, limb.SupportAlong(new Vector2(-1f, 0f)), 5);  // 0.2 + 0.15

        var pod = Capsule.Disc(new Vector2(0.5f, 0f), 0.2f);
        Assert.Equal(0.70, pod.SupportAlong(new Vector2(1f, 0f)), 5);
    }

    [Fact]
    public void Circle_SupportIsTheRadiusInEveryDirection()
    {
        for (int i = 0; i < 16; i++)
        {
            double a = Math.PI * 2 * i / 16;
            var d = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
            Assert.Equal(1.0, HullShape.Circle.SupportAlong(d), 5);
        }
    }

    // Aft = toward the tail (local +v), across = local +u. These are the directions the main and
    // side flames fire along; the numbers are the paint extents, and a shape fitted to the paint
    // must reach the same distance.
    [Theory]
    [InlineData((byte)0, 1.00, 1.00)] // swarmer: a disc
    [InlineData((byte)1, 0.96, 1.00)] // interceptor
    [InlineData((byte)2, 0.88, 0.90)] // heavy cannon
    [InlineData((byte)3, 0.80, 0.60)] // streak: the dart is half as wide as it is long
    [InlineData((byte)4, 0.71, 0.79)] // carmine
    public void EnemyHull_SupportMatchesThePaintedExtents(byte graphicsId, double aft, double across)
    {
        var shape = HullShapes.ForEnemy(graphicsId);
        Assert.Equal(aft, shape.SupportAlong(new Vector2(0f, 1f)), 2);
        Assert.Equal(across, shape.SupportAlong(new Vector2(1f, 0f)), 2);
    }

    public static IEnumerable<object[]> PlayerHullCases()
    {
        yield return new object[] { PlayerHullShapes.Scout, 1.00, 1.00 };
        yield return new object[] { PlayerHullShapes.Fighter, 1.00, 1.00 };
        yield return new object[] { PlayerHullShapes.Heavy, 1.00, 1.00 };
        yield return new object[] { PlayerHullShapes.Shadow, 1.00, 1.00 };
        yield return new object[] { PlayerHullShapes.Falcon, 0.87, 0.71 };
        yield return new object[] { PlayerHullShapes.Eagle, 0.80, 0.97 }; // the swept leading edge reaches past the wing bar
        yield return new object[] { PlayerHullShapes.Manta, 0.54, 0.79 }; // 0.49 R of the old base was air
        yield return new object[] { PlayerHullShapes.Raptor, 0.71, 0.68 };
    }

    [Theory]
    [MemberData(nameof(PlayerHullCases))]
    public void PlayerHull_SupportMatchesThePaintedExtents(HullShape shape, double aft, double across)
    {
        Assert.Equal(aft, shape.SupportAlong(new Vector2(0f, 1f)), 2);
        Assert.Equal(across, shape.SupportAlong(new Vector2(1f, 0f)), 2);
    }

    // Support is the worst reach along a direction, so the reach rule the shapes already test
    // (endpoints + cap inside the bounding disc) must hold here too, in every direction.
    [Fact]
    public void EveryShape_SupportStaysInsideTheBoundingRadiusItIsIndexedBy()
    {
        var shapes = new List<HullShape> { HullShape.Circle };
        for (byte id = 0; id < EnemyShipType.All.Length; id++)
            shapes.Add(HullShapes.ForEnemy(id));
        foreach (var caseArgs in PlayerHullCases())
            shapes.Add((HullShape)caseArgs[0]);

        foreach (var shape in shapes)
            for (int i = 0; i < 16; i++)
            {
                double a = Math.PI * 2 * i / 16;
                var d = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                Assert.True(shape.SupportAlong(d) <= 1.001f,
                    $"shape of {shape.Capsules.Length} capsules reaches {shape.SupportAlong(d):0.000} along {d}");
            }
    }

    // The renderer rotates the world flame direction into the shape's own frame; the manta's 0.54 R
    // tail is the case that used to draw the plume 0.46 radii out in empty space. The base sits one
    // inset (0.10 R) inside the surface, under the paint, so the plume shows and the base hides.
    [Fact]
    public void FlameBase_AnchorsOnTheShapedHullAndRotatesWithIt()
    {
        var pos = new Vector2(100f, 100f);

        var aftUp = ThrusterFlameRenderer.FlameBase(pos, PlayerHullShapes.Manta, 100f, new Vector2(0f, 1f), 0f);
        Assert.Equal(100f, aftUp.X, 1);
        Assert.Equal(144f, aftUp.Y, 1); // straight aft: 100 + (0.54 - 0.10) * 100

        var aftLeft = ThrusterFlameRenderer.FlameBase(pos, PlayerHullShapes.Manta, 100f, new Vector2(-1f, 0f), MathF.PI / 2f);
        Assert.Equal(56f, aftLeft.X, 1); // nose turned to +x: the same 0.44 R tail, now pointing left
        Assert.Equal(100f, aftLeft.Y, 1);

        var anywhere = ThrusterFlameRenderer.FlameBase(pos, HullShape.Circle, 100f, new Vector2(0f, 1f), 0.7f);
        Assert.Equal(100f, anywhere.X, 1); // a disc anchors at 0.9 R whatever way it turns
        Assert.Equal(190f, anywhere.Y, 1);
    }
}
