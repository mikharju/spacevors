using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// What a jam does to a hull: it stops accelerating and shooting, turns slowly toward the heading it was
// given, and starts flying again the moment the jam runs out (plans/ECM_PULSE_AND_SALVAGE_FIELD.md).
public class EcmJamTest
{
    const float Dt = 1f / 60f;

    // The elapsed time at which racks are unlocked and the in-flight cap is above zero.
    const float RacksUnlockedAt = 200f;

    // Beyond the swarmer's 700 px gun range: a hull that is not jammed closes on the player from here.
    private static readonly Vector2 OutOfGunRange = new(0f, 1200f);

    // Inside it, and inside its firing arc: a hull that is not jammed shoots from here.
    private static readonly Vector2 InsideGunRange = new(0f, 300f);

    private static (EntityManager Em, Entity Player) World(float elapsed = 0f)
    {
        var em = new EntityManager();
        em.AddElapsedTime(elapsed);
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 100, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(100, 100));
        return (em, player);
    }

    // Facing up the screen (rotation 0), which is the direction of the player from both spawn points.
    private static Entity AddHull(EntityManager em, Vector2 at, EnemyArmament armament = EnemyArmament.Guns, EnemyShipType? type = null)
    {
        var hull = em.CreateEntity();
        EnemyShipFactory.AddComponents(em, hull, at, Vector2.Zero, 0f, 0f, type ?? EnemyShipType.Default, armament, em.ElapsedTime);
        if (armament != EnemyArmament.Guns && em.HasComponent<MissileCooldown>(hull))
            em.AddComponent(hull, new MissileCooldown(0f));   // the reload is set at spawn; a test wants it spent
        return hull;
    }

    private static void Jam(EntityManager em, Entity hull, float heading = MathF.PI / 2f) =>
        em.AddComponent(hull, new EcmDisabled(EcmStats.Duration, heading));

    private static void RunActionAndIntent(EntityManager em)
    {
        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(view, Dt, commands);
        new EnemyMissileFiringSystem().Update(view, Dt, commands);
        new EnemyShipSystem().Update(view, Dt, commands);
        commands.Apply(em);
    }

    private static float AccelerationOf(EntityManager em, Entity hull) =>
        em.TryGetComponent<Acceleration>(hull, out var accel) ? accel.Value.Magnitude : 0f;

    // The pair that prices the jam: the same hull, same tick, jammed and not. A hull outside its gun range
    // closes on the player, unless the jam has taken its throttles out.
    [Fact]
    public void JammedHullCoasts_WhenAnOrdinaryHullCloses()
    {
        var (freeEm, _) = World();
        Entity freeHull = AddHull(freeEm, OutOfGunRange);

        var (jammedEm, _) = World();
        Entity jammedHull = AddHull(jammedEm, OutOfGunRange);
        Jam(jammedEm, jammedHull);

        RunActionAndIntent(freeEm);
        RunActionAndIntent(jammedEm);

        Assert.True(AccelerationOf(freeEm, freeHull) > 0f, "an unhindered hull closes the distance");
        Assert.Equal(0f, AccelerationOf(jammedEm, jammedHull));
    }

    [Fact]
    public void JammedHullHoldsItsGuns_WhenAnOrdinaryHullFires()
    {
        var (freeEm, _) = World();
        AddHull(freeEm, InsideGunRange);

        var (jammedEm, _) = World();
        Entity jammedHull = AddHull(jammedEm, InsideGunRange);
        Jam(jammedEm, jammedHull);

        RunActionAndIntent(freeEm);
        RunActionAndIntent(jammedEm);

        Assert.Equal(1, GunRounds(freeEm));
        Assert.Equal(0, GunRounds(jammedEm));
    }

    // A rack is a second gun with its own reload: the jam holds it loaded rather than burning a reload the
    // player never had to dodge.
    [Fact]
    public void JammedRackLaunchesNothing_WhenAnOrdinaryRackDoes()
    {
        var (freeEm, _) = World(RacksUnlockedAt);
        AddHull(freeEm, InsideGunRange, EnemyArmament.Missiles, EnemyShipType.Interceptor);

        var (jammedEm, _) = World(RacksUnlockedAt);
        Entity jammedHull = AddHull(jammedEm, InsideGunRange, EnemyArmament.Missiles, EnemyShipType.Interceptor);
        Jam(jammedEm, jammedHull);

        RunActionAndIntent(freeEm);
        RunActionAndIntent(jammedEm);

        Assert.Equal(1, GuidedRounds(freeEm));
        Assert.Equal(0, GuidedRounds(jammedEm));
    }

    // The jammed hull's slow turn: it writes an angular velocity toward the heading it was given, at the
    // wander rate rather than at the hull's own turn rate.
    [Fact]
    public void JammedHullTurnsSlowlyTowardItsHeading()
    {
        var (em, _) = World();
        Entity hull = AddHull(em, InsideGunRange);
        Jam(em, hull, heading: MathF.PI / 2f);   // from facing up-screen to facing right

        var commands = new CommandBuffer();
        new EnemyShipSystem().Update(new WorldView(em), Dt, commands);
        commands.Apply(em);

        Assert.Equal(EcmStats.WanderTurnRate, em.GetComponent<AngularVelocity>(hull).Value, precision: 4);
        Assert.True(EcmStats.WanderTurnRate < em.GetComponent<EnemyShip>(hull).TurnRate,
            "the wander is slower than the hull's own turn, so it reads as helplessness");
    }

    // When the jam runs out the hull goes back to what it was doing: closing.
    [Fact]
    public void WhenTheJamExpiresTheHullFliesAgain()
    {
        var (em, _) = World();
        Entity hull = AddHull(em, OutOfGunRange);
        Jam(em, hull);

        var view = new WorldView(em);
        var ecm = new EcmSystem();
        for (int tick = 0; tick < (int)(EcmStats.Duration / Dt) + 2; tick++)
        {
            var commands = new CommandBuffer();
            ecm.Update(view, Dt, commands);
            commands.Apply(em);
        }

        Assert.False(em.HasComponent<EcmDisabled>(hull));

        RunActionAndIntent(em);

        Assert.True(AccelerationOf(em, hull) > 0f, "the hull is flying again");
    }

    private static int GunRounds(EntityManager em) => em.GetEntitiesWithComponents<Ammo>().ToList().Count;

    private static int GuidedRounds(EntityManager em) => em.GetEntitiesWithComponents<Missile, Ammo>().ToList().Count;
}
