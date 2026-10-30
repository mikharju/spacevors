using Spacevors.Domain;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

// The capsule pair math is the whole basis of the shape work, so it is pinned case by case here
// rather than leaned on through the game (plans/NON_CIRCLE_COLLISION.md).
public class CapsuleGeometryTest
{
    const float Tolerance = 1e-4f;

    static void AssertContact(Contact contact, Vector2 normal, float depth, Vector2 point)
    {
        Assert.True(Vector2.Dot(contact.Normal - normal, contact.Normal - normal) < Tolerance,
            $"normal {contact.Normal}, expected {normal}");
        Assert.Equal(depth, contact.Depth, 4);
        Assert.True(Vector2.Dot(contact.Point - point, contact.Point - point) < Tolerance,
            $"contact point {contact.Point}, expected {point}");
    }

    [Fact]
    public void SideBySide_ReportsDepthAcrossAndNormalFromAToB()
    {
        var a = new Capsule(new Vector2(-1f, 0f), new Vector2(1f, 0f), 0.5f);
        var b = new Capsule(new Vector2(-1f, 0.8f), new Vector2(1f, 0.8f), 0.5f);

        Assert.True(CapsuleGeometry.TryCollide(a, b, out Contact contact));
        AssertContact(contact, new Vector2(0f, 1f), 0.2f, new Vector2(-1f, 0.5f));
    }

    // Parallel segments have a band of equally close pairs; reading the parameters naively reports the
    // distance between mismatched ends, which is far enough to turn a real touch into a miss. Half a
    // pixel of thickness on each side is what makes the old shortcut detectable.
    [Fact]
    public void ParallelButStaggeredAlongTheirLength_StillMeasuresTheTrueGap()
    {
        var a = new Capsule(new Vector2(0f, 0f), new Vector2(1f, 0f), 0.51f);
        var b = new Capsule(new Vector2(0.2f, 1f), new Vector2(1.2f, 1f), 0.51f);

        Assert.True(CapsuleGeometry.TryCollide(a, b, out Contact contact),
            "the two axes are one unit apart where they overlap, so they touch");
        Assert.Equal(0.02f, contact.Depth, 3);
        AssertContact(contact, new Vector2(0f, 1f), 0.02f, new Vector2(1f, 0.51f));
    }

    [Fact]
    public void RoundEndHitsTheSideOfAnother_ReportsTheCapNormal()
    {
        var a = new Capsule(new Vector2(-1f, 0f), new Vector2(0f, 0f), 0.5f);
        var b = new Capsule(new Vector2(0.8f, -1f), new Vector2(0.8f, 1f), 0.5f);

        Assert.True(CapsuleGeometry.TryCollide(a, b, out Contact contact));
        AssertContact(contact, new Vector2(1f, 0f), 0.2f, new Vector2(0.5f, 0f));
    }

    // A disc is a capsule with no axis, so the circle cases must agree with what NarrowPhase already
    // reported for two circles.
    [Fact]
    public void DiscAgainstSegment_AndDiscAgainstDisc_MatchTheCircleMath()
    {
        var disc = new Capsule(new Vector2(0f, 0.8f), new Vector2(0f, 0.8f), 0.5f);
        var bar = new Capsule(new Vector2(-1f, 0f), new Vector2(1f, 0f), 0.5f);

        Assert.True(CapsuleGeometry.TryCollide(disc, bar, out Contact againstBar));
        AssertContact(againstBar, new Vector2(0f, -1f), 0.2f, new Vector2(0f, 0.3f));

        var first = new Capsule(Vector2.Zero, Vector2.Zero, 0.5f);
        var second = new Capsule(new Vector2(0.6f, 0f), new Vector2(0.6f, 0f), 0.5f);
        Assert.True(CapsuleGeometry.TryCollide(first, second, out Contact betweenDiscs));
        Assert.Equal(0.4f, betweenDiscs.Depth, 4);
        Assert.Equal(1f, betweenDiscs.Normal.X, 4);
    }

    // Axes crossing means a deep overlap with no direction to read off the closest points, so the
    // normal is chosen from the primitive that has an axis at all — deterministically, because the
    // same frame must resolve the same way every replay.
    [Fact]
    public void CrossingAxes_PushApartAlongTheSideOfTheAxisThatHasOne()
    {
        var a = new Capsule(new Vector2(-1f, 0f), new Vector2(1f, 0f), 0.3f);
        var b = new Capsule(new Vector2(0f, -1f), new Vector2(0f, 1f), 0.3f);

        Assert.True(CapsuleGeometry.TryCollide(a, b, out Contact contact));
        Assert.Equal(0.6f, contact.Depth, 4);
        Assert.Equal(0f, contact.Normal.X, 4);
        Assert.Equal(1f, MathF.Abs(contact.Normal.Y), 4);
    }

    // Two discs on the same spot are coincident: no direction, and the circle path has always refused
    // to push them. Same answer here.
    [Fact]
    public void CoincidentDiscs_AreNotAContact()
    {
        var a = new Capsule(Vector2.Zero, Vector2.Zero, 0.5f);
        var b = new Capsule(Vector2.Zero, Vector2.Zero, 0.5f);

        Assert.False(CapsuleGeometry.TryCollide(a, b, out _));
    }

    [Fact]
    public void ShortOfTouching_IsNotAContact()
    {
        var a = new Capsule(new Vector2(-1f, 0f), new Vector2(1f, 0f), 0.5f);
        var b = new Capsule(new Vector2(-1f, 2f), new Vector2(1f, 2f), 0.5f);

        Assert.False(CapsuleGeometry.TryCollide(a, b, out _));
    }

    // A swept round body: the path parameter is what orders hits by travel, and the normal points from
    // the target toward the path — the convention the ammo impulse already reads.
    [Fact]
    public void SweepReportsTheTouchAlongThePathAndPushesTheTargetAwayFromIt()
    {
        var limb = new Capsule(new Vector2(0f, 0.8f), new Vector2(0f, 3f), 0.65f);

        Assert.True(CapsuleGeometry.TrySweep(new Vector2(-5f, 0f), new Vector2(5f, 0f), 0.25f, limb, out Contact contact, out float t));
        Assert.Equal(0.5f, t, 4);
        AssertContact(contact, new Vector2(0f, -1f), 0.1f, new Vector2(0f, 0f));
    }

    [Fact]
    public void SweepPastALimb_IsNotAContact()
    {
        var limb = new Capsule(new Vector2(0f, 2f), new Vector2(0f, 3f), 0.65f);

        Assert.False(CapsuleGeometry.TrySweep(new Vector2(-5f, 0f), new Vector2(5f, 0f), 0.25f, limb, out _, out _));
    }

    // A round body that has not moved this tick is a disc, and must still collide.
    [Fact]
    public void SweptRoundWithNoTravel_IsStillTested()
    {
        var limb = new Capsule(new Vector2(-1f, 0.9f), new Vector2(1f, 0.9f), 0.5f);

        Assert.True(CapsuleGeometry.TrySweep(Vector2.Zero, Vector2.Zero, 0.5f, limb, out Contact contact, out float t));
        Assert.Equal(0f, t, 4);
        Assert.Equal(0.1f, contact.Depth, 4);
    }

    // Where a beam stops on a target is the entry of its ray: these pin that math.
    [Fact]
    public void EntryAlongRay_DeadOnDisc_IsTheNearSide()
    {
        var disc = Capsule.Disc(new Vector2(100f, 0f), 10f);

        Assert.True(CapsuleGeometry.TryEntryAlongRay(Vector2.Zero, new Vector2(1f, 0f), disc, out float entry));
        Assert.Equal(90f, entry, 3);
    }

    // A grazing ray enters at the tangent along its axis, not at the disc's nominal near side:
    // x = 100 - sqrt(10^2 - 6^2) = 92, while the naive 100 - 10 = 90 would stop 2 units short.
    [Fact]
    public void EntryAlongRay_GrazingDisc_IsTheTangentEntryPoint()
    {
        var disc = Capsule.Disc(new Vector2(100f, 0f), 10f);

        Assert.True(CapsuleGeometry.TryEntryAlongRay(new Vector2(0f, 6f), new Vector2(1f, 0f), disc, out float entry));
        Assert.Equal(92f, entry, 3);
    }

    [Fact]
    public void EntryAlongRay_SideOnALimb_IsTheBandEntry()
    {
        var limb = new Capsule(new Vector2(100f, -40f), new Vector2(100f, 40f), 10f);

        Assert.True(CapsuleGeometry.TryEntryAlongRay(Vector2.Zero, new Vector2(1f, 0f), limb, out float entry));
        Assert.Equal(90f, entry, 3);
    }

    [Fact]
    public void EntryAlongRay_PastTheTipOfALimb_EntersThroughTheCapDisc()
    {
        var limb = new Capsule(new Vector2(100f, -40f), new Vector2(100f, 40f), 10f);

        Assert.True(CapsuleGeometry.TryEntryAlongRay(new Vector2(0f, 45f), new Vector2(1f, 0f), limb, out float entry));
        Assert.Equal(91.34f, entry, 2);
    }

    [Fact]
    public void EntryAlongRay_BesideAndPastTheEnd_IsNoEntry()
    {
        var limb = new Capsule(new Vector2(100f, -40f), new Vector2(100f, 40f), 10f);

        Assert.False(CapsuleGeometry.TryEntryAlongRay(new Vector2(0f, 60f), new Vector2(1f, 0f), limb, out _));
    }

    [Fact]
    public void EntryAlongRay_StartingInside_EntersAtZero()
    {
        var disc = Capsule.Disc(new Vector2(10f, 0f), 10f);

        Assert.True(CapsuleGeometry.TryEntryAlongRay(new Vector2(5f, 0f), new Vector2(1f, 0f), disc, out float entry));
        Assert.Equal(0f, entry, 3);
    }
}
