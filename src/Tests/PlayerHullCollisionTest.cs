using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

// What the player's own hull shape collides with, end to end through the collision system
// (plans/NON_CIRCLE_COLLISION.md). A shape sits inside the hull's bounding radius, so it can only ever
// take hits away from the player — never add any. Each pair below is therefore the same geometry with a
// hull that has no shape, which still behaves exactly as it did before shapes existed. Without the pair,
// a pass-through proves only that the setup was wrong.
//
// Fixtures use the Heavy at its real radius (84), facing north: a spine, a bar across the waist, a bar
// across the aft, and a thin bar down each fore pod, with a gap you can see the stars through between the
// spine and each pod.
public class PlayerHullCollisionTest
{
    const float PlayerRadius = 84f;

    // The fore slot: 55 from the player's centre, inside the 84 bounding circle, and at least 14 clear of
    // every primitive — the spine, the waist bar and the fore pod bar are all further away than that.
    static readonly Vector2 ForeSlot = new(38f, -40f);

    // The aft slot, inboard of the bounding circle and outside the aft bar and the spine's tail cap.
    static readonly Vector2 AftOfTheAftBar = new(40f, 70f);

    [Fact]
    public void EnemyRoundThroughTheForeSlot_PassesThrough()
    {
        var (em, view, system) = Setup();
        var player = AddPlayer(em, PlayerHullShapes.Heavy);
        var round = AddEnemyRound(em, ForeSlot, new Vector2(0f, -400f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(20, em.GetComponent<Health>(player).Current);
        Assert.True(em.HasComponent<Ammo>(round), "the round should fly on through the gap in the hull");
    }

    [Fact]
    public void SameRoundAgainstAPlayerWithoutAShape_Hits()
    {
        var (em, view, system) = Setup();
        var player = AddPlayer(em, shape: null);
        AddEnemyRound(em, ForeSlot, new Vector2(0f, -400f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        // An 84 circle has no slot to miss.
        Assert.Equal(14, em.GetComponent<Health>(player).Current);
    }

    // A shape is not a smaller circle: the thin bar down a fore pod is as hittable as the middle of the
    // hull, so a round that lands on the art lands.
    [Fact]
    public void EnemyRoundOnThePlayersForePod_Hits()
    {
        var (em, view, system) = Setup();
        var player = AddPlayer(em, PlayerHullShapes.Heavy);
        var round = AddEnemyRound(em, new Vector2(63f, -25f), new Vector2(0f, -400f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(14, em.GetComponent<Health>(player).Current);
        Assert.False(em.HasComponent<Ammo>(round), "the round should be spent on the pod");
    }

    // Behind the aft bar is air out to the bounding circle: a beam that ends there is spent on nothing,
    // while the same beam against a circle-player is spent on the player.
    [Fact]
    public void EnemyBeamThatStopsBehindTheAftBar_DoesNotHurt()
    {
        var (em, view, system) = Setup();
        var player = AddPlayer(em, PlayerHullShapes.Heavy);
        AddEnemyBeam(em, AftOfTheAftBar + new Vector2(0f, 50f), new Vector2(0f, -1f), 50f);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(20, em.GetComponent<Health>(player).Current);
    }

    [Fact]
    public void SameBeamAgainstAPlayerWithoutAShape_Hurts()
    {
        var (em, view, system) = Setup();
        var player = AddPlayer(em, shape: null);
        AddEnemyBeam(em, AftOfTheAftBar + new Vector2(0f, 50f), new Vector2(0f, -1f), 50f);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(17, em.GetComponent<Health>(player).Current);
    }

    // Grinding is the other half: a rock whose circle overlaps the player's must not shove the player
    // along a normal the hull's shape never met.
    [Fact]
    public void AsteroidInTheForeSlot_DoesNotPushThePlayer()
    {
        var (em, view, system) = Setup();
        var player = AddPlayer(em, PlayerHullShapes.Heavy);
        var rock = AddRock(em, ForeSlot, 10f);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(ForeSlot, em.GetComponent<Position>(rock).Value);
    }

    [Fact]
    public void SameRockAgainstAPlayerWithoutAShape_PushesIt()
    {
        var (em, view, system) = Setup();
        AddPlayer(em, shape: null);
        var rock = AddRock(em, ForeSlot, 10f);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.NotEqual(ForeSlot, em.GetComponent<Position>(rock).Value);
    }

    private static (EntityManager em, WorldView view, CollisionSystem system) Setup()
    {
        var em = new EntityManager();
        return (em, new WorldView(em), new CollisionSystem());
    }

    // The player as the collision system sees it, at the Heavy's real radius and facing north. A null shape
    // is what a world without authored hull shapes gives: the disc the radius has always described.
    private static Entity AddPlayer(EntityManager em, HullShape? shape)
    {
        var entity = em.CreateEntity();
        em.AddComponent(entity, new Position(Vector2.Zero));
        em.AddComponent(entity, new Rotation(0f));
        em.AddComponent(entity, new Player(
            Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 20,
            Radius: PlayerRadius, Shape: shape));
        em.AddComponent(entity, new Health(20, 20));
        return entity;
    }

    private static Entity AddEnemyRound(EntityManager em, Vector2 position, Vector2 velocity)
    {
        var entity = em.CreateEntity();
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Ammo(
            Velocity: velocity, Radius: 3f, Lifetime: 10f, IsEnemy: true, Damage: 6));
        return entity;
    }

    // A beam whose far end is `start` and whose near end is `length` along `direction`.
    private static Entity AddEnemyBeam(EntityManager em, Vector2 start, Vector2 direction, float length)
    {
        var entity = em.CreateEntity();
        em.AddComponent(entity, new LaserBeam(
            start, direction, length, Width: 6f, Damage: 3, IsEnemy: true,
            Color: AmmoColor.Red, Lifetime: 0.3f, InitialLifetime: 0.3f));
        return entity;
    }

    private static Entity AddRock(EntityManager em, Vector2 position, float radius)
    {
        var entity = em.CreateEntity();
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Asteroid(IsSmall: true, Radius: radius));
        return entity;
    }
}
