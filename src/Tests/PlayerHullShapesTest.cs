using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Xunit;

// The player hull shapes are data too (plans/NON_CIRCLE_COLLISION.md), and the way they fail quietly is a
// layout that reaches past the hull's bounding radius: everything that has to *find* a collision — the
// circle queued in front of each shape test, spawn clearance, explosion radii — reads that radius, so a
// shape growing outside it stops being tested at all.
public class PlayerHullShapesTest
{
    // Reach ≤ 1 is what makes a hurtbox safe to shrink: a capsule is the convex hull of its two end discs,
    // and a disc of radius `HalfThickness` centred |end| from the origin lies inside the unit disc exactly
    // when |end| + HalfThickness ≤ 1. So this invariant is the proof that a player's shape can never be hit
    // where the disc it replaced could not have been.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PlayerHullShape_FitsInsideTheBoundingRadiusEverythingElseReads(int shipIndex)
    {
        ShipType ship = ShipType.All[shipIndex];

        foreach (var capsule in ship.Shape.Capsules)
        {
            Assert.True(capsule.HalfThickness > 0f, $"{ship.Name} has a capsule with no thickness");
            AssertInside(ship.Name, capsule.From, capsule);
            AssertInside(ship.Name, capsule.To, capsule);
        }
    }

    static void AssertInside(string shipName, Vector2 end, Capsule capsule)
    {
        float reach = end.Magnitude + capsule.HalfThickness;
        Assert.True(reach <= 1.001f,
            $"{shipName} capsule {capsule} reaches {reach:0.000} radii from the centre, but the player's " +
            "collision circle only reaches one");
    }

    // The game must never fall back to the disc: that is the state of a world with no authored shapes, and a
    // player colliding as a disc is invisible in play.
    [Fact]
    public void EveryPlayerHullType_CarriesAnAuthoredShape()
    {
        foreach (var ship in ShipType.All)
            Assert.NotNull(ship.Shape);

        Assert.Equal(5, ShipType.All.Length); // a sixth hull means a sixth shape to author
    }

    // The shape reaches the entity as well as the hull type: `Player.Shape` is what the collision system
    // reads, and a wire-up that forgets it leaves the real game colliding as a circle.
    [Fact]
    public void StartingWorld_PlayerCarriesItsHullsShape()
    {
        var (em, playerEntity, _) = WorldFactory.Create(ShipType.Heavy, new Vector2(1920f, 1080f));

        Assert.Equal(ShipType.Heavy.Shape, em.GetComponent<Player>(playerEntity).Shape);
    }
}
