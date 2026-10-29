using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

// What a shaped hull actually collides with, end to end through the collision system
// (plans/NON_CIRCLE_COLLISION.md). A hull's shape sits inside the bounding circle the broad phase
// indexes it by, so a shape can only ever take hits away. Each test therefore comes in a pair: the
// same geometry with circle art, which still hits. Without the pair, a pass-through proves only that
// the setup was wrong.
//
// Fixtures use the interceptor at its real radius, facing north: a spine 0.33R thick down the middle
// and a wing bar 0.2R thick across it, inside a 45R bounding circle.
public class HullShapeCollisionTest
{
    // Every pair below is the same hull at the same radius, told apart only by its art id: 1 is the
    // interceptor's cross, 0 is the blob that collides as one disc.
    const float HullRadius = 45f;
    const byte CrossArt = 1;
    const byte DiscArt = 0;

    // The wide hull: its shape is a slab with a pod at each wing end, so its disc is the furthest from
    // its paint of the three.
    const float HeavyRadius = 78f;
    const byte HeavyArt = 2;

    [Fact]
    public void RoundThroughTheGapBetweenSpineAndWing_PassesThrough()
    {
        var (em, view, system) = Setup();

        // (30, 20) is 36 from the hull centre — well inside its bounding circle — but 20 clear of the
        // wing bar and 30 clear of the spine, which is the gap between them.
        var ship = AddHull(em, Vector2.Zero, HullRadius, CrossArt);
        var ammo = AddRound(em, new Vector2(30f, 20f), new Vector2(0f, -400f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(10, em.GetComponent<Health>(ship).Current);
        Assert.True(em.HasComponent<Ammo>(ammo), "the round should fly on through the gap");
    }

    [Fact]
    public void SameRoundAgainstCircleArt_Hits()
    {
        var (em, view, system) = Setup();

        var ship = AddHull(em, Vector2.Zero, HullRadius, DiscArt);
        AddRound(em, new Vector2(30f, 20f), new Vector2(0f, -400f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        // A 45R circle has no gap to miss.
        Assert.Equal(4, em.GetComponent<Health>(ship).Current);
    }

    // The wing bar ends at 0.8R and its round cap reaches 1.0R, so a wing tip is as hittable as the
    // middle of the hull. A shape that only protects the middle is a smaller circle, not a shape.
    [Fact]
    public void RoundAgainstAWingTip_Hits()
    {
        var (em, view, system) = Setup();

        var ship = AddHull(em, Vector2.Zero, HullRadius, CrossArt);
        var ammo = AddRound(em, new Vector2(40f, 0f), new Vector2(0f, -400f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(4, em.GetComponent<Health>(ship).Current);
        Assert.False(em.HasComponent<Ammo>(ammo), "the round should be spent on the wing");
    }

    // Two hulls whose bounding circles overlap by 5 px while their shapes are 15 px apart: the grid
    // still finds the pair, and the narrow phase still declines to shove them apart.
    [Fact]
    public void ShapedHullsOverlappingOnlyByTheirBoundingCircles_AreNotPushedApart()
    {
        var (em, view, system) = Setup();

        var first = AddHull(em, Vector2.Zero, HullRadius, CrossArt);
        var second = AddHull(em, new Vector2(60f, 60f), HullRadius, CrossArt);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(Vector2.Zero, em.GetComponent<Position>(first).Value);
        Assert.Equal(new Vector2(60f, 60f), em.GetComponent<Position>(second).Value);
    }

    [Fact]
    public void SamePairWithCircleArt_IsPushedApart()
    {
        var (em, view, system) = Setup();

        var first = AddHull(em, Vector2.Zero, HullRadius, DiscArt);
        var second = AddHull(em, new Vector2(60f, 60f), HullRadius, DiscArt);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.NotEqual(Vector2.Zero, em.GetComponent<Position>(first).Value);
    }

    // In front of the heavy cannon's nose is air: inside the bounding circle, outside every primitive.
    [Fact]
    public void RoundPastTheHeavyCannonsNose_TouchesNothing()
    {
        var (em, view, system) = Setup();

        var ship = AddHull(em, Vector2.Zero, HeavyRadius, HeavyArt);
        var ammo = AddRound(em, new Vector2(0f, 74f), new Vector2(0f, -400f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(10, em.GetComponent<Health>(ship).Current);
        Assert.True(em.HasComponent<Ammo>(ammo), "the round should fly past the nose");
    }

    [Fact]
    public void SameRoundAgainstTheHeavyCannonsCircle_Hits()
    {
        var (em, view, system) = Setup();

        var ship = AddHull(em, Vector2.Zero, HeavyRadius, DiscArt);
        AddRound(em, new Vector2(0f, 74f), new Vector2(0f, -400f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(4, em.GetComponent<Health>(ship).Current);
    }

    private static (EntityManager em, WorldView view, CollisionSystem system) Setup()
    {
        var em = new EntityManager();
        return (em, new WorldView(em), new CollisionSystem());
    }

    // A hull the collision system can see: where it is, which way it faces, what it is made of, and
    // enough health to survive one round.
    private static Entity AddHull(EntityManager em, Vector2 position, float radius, byte graphicsId)
    {
        var entity = em.CreateEntity();
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(Vector2.Zero));
        em.AddComponent(entity, new Rotation(0f));
        em.AddComponent(entity, new EnemyShip(
            radius, Speed: 50f, TurnRate: 1f, FiringRange: 300f,
            TurretFireRate: 0f, TurretAmmoSpeed: 150f, Acceleration: 30f, GraphicsId: graphicsId));
        em.AddComponent(entity, new Health(10, 10));
        return entity;
    }

    private static Entity AddRound(EntityManager em, Vector2 position, Vector2 velocity)
    {
        var entity = em.CreateEntity();
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Ammo(Velocity: velocity, Radius: 3f, Lifetime: 10f, Damage: 6));
        return entity;
    }
}
