using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Touching an ECM capsule jams the whole world at once — hulls, mines and enemy rounds alike — and fires the
// ring out from where it was picked up (plans/ECM_PULSE_AND_SALVAGE_FIELD.md). The capsule itself is the one
// pickup that never runs out.
public class EcmPulseTest
{
    const float Dt = 1f / 60f;

    private static (EntityManager Em, Entity Player) World()
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 100, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(100, 100));
        return (em, player);
    }

    // 30 px out: inside the capsule's collection distance (18 + 16), so the first tick is the one that fires.
    private static void AddCapsuleAtTheHull(EntityManager em)
    {
        var capsule = em.CreateEntity();
        em.AddComponent(capsule, new Position(new Vector2(30f, 0f)));
        em.AddComponent(capsule, new EcmPulse(Radius: LootStats.EcmCapsuleRadius));
    }

    private static Entity AddCapsuleAway(EntityManager em)
    {
        var capsule = em.CreateEntity();
        em.AddComponent(capsule, new Position(new Vector2(600f, 0f)));
        em.AddComponent(capsule, new EcmPulse(Radius: LootStats.EcmCapsuleRadius));
        return capsule;
    }

    private static Entity AddShip(EntityManager em, Vector2 at)
    {
        var ship = em.CreateEntity();
        EnemyShipFactory.AddComponents(em, ship, at, new Vector2(0f, -60f), 0f, 0f, EnemyShipType.Default, EnemyArmament.Guns);
        return ship;
    }

    private static Entity AddMine(EntityManager em, Vector2 at)
    {
        var mine = em.CreateEntity();
        em.AddComponent(mine, new Position(at));
        em.AddComponent(mine, new Velocity(Vector2.Zero));
        em.AddComponent(mine, new EnemyMine(MineSize.Small, 40f, 0f));
        em.AddComponent(mine, new Health(MineType.Small.Health, MineType.Small.Health));
        return mine;
    }

    private static Entity AddRound(EntityManager em, Entity target, bool isEnemy, Vector2 velocity)
    {
        var round = em.CreateEntity();
        em.AddComponent(round, new Position(new Vector2(0f, -200f)));
        em.AddComponent(round, new Velocity(velocity));
        em.AddComponent(round, new Ammo(velocity, Radius: 4f, Lifetime: 10f, IsEnemy: isEnemy, Damage: 2));
        em.AddComponent(round, new Missile(target, Speed: velocity.Magnitude, MaxSpeed: velocity.Magnitude, Acceleration: 0f, TurnRate: 1f, Steering: 0f));
        em.AddComponent(round, new Health(4, 4));
        return round;
    }

    private static void Tick(EntityManager em)
    {
        var commands = new CommandBuffer();
        new PickupMagnetSystem().Update(new WorldView(em), Dt, commands);
        commands.Apply(em);
    }

    private static int Count<T>(EntityManager em) where T : notnull => em.GetEntitiesWithComponents<T>().ToList().Count;

    [Fact]
    public void Capsule_JamsEveryEnemyAndFiresOneWave()
    {
        var (em, player) = World();
        AddCapsuleAtTheHull(em);
        Entity ship = AddShip(em, new Vector2(500f, 0f));
        Entity mine = AddMine(em, new Vector2(0f, 700f));
        Entity enemyRound = AddRound(em, player, isEnemy: true, new Vector2(0f, 300f));
        Entity playerRound = AddRound(em, ship, isEnemy: false, new Vector2(0f, -300f));

        Tick(em);

        Assert.True(Count<EcmPulse>(em) == 0, "the capsule is spent");
        Assert.Equal(EcmStats.Duration, em.GetComponent<EcmDisabled>(ship).TimeRemaining, precision: 5);
        Assert.Equal(EcmStats.Duration, em.GetComponent<EcmDisabled>(mine).TimeRemaining, precision: 5);
        Assert.True(em.HasComponent<MissileStunned>(enemyRound), "an enemy round loses its seeker");
        Assert.False(em.HasComponent<MissileStunned>(playerRound), "the player's own rounds are untouched");
        Assert.True(Count<EcmWave>(em) == 1, "one ring fires out of where the capsule was");
    }

    // The pulse marks what exists when it fires: the heading each hull wanders to is drawn from the world RNG
    // then and stored on the component, so nothing downstream has to roll a number.
    [Fact]
    public void Capsule_GivesEachHullAHeadingOfItsOwn()
    {
        var (em, _) = World();
        AddCapsuleAtTheHull(em);
        Entity first = AddShip(em, new Vector2(500f, 0f));
        Entity second = AddShip(em, new Vector2(-500f, 0f));

        Tick(em);

        float firstHeading = em.GetComponent<EcmDisabled>(first).WanderHeading;
        float secondHeading = em.GetComponent<EcmDisabled>(second).WanderHeading;
        Assert.InRange(firstHeading, 0f, MathF.Tau);
        Assert.InRange(secondHeading, 0f, MathF.Tau);
        Assert.NotEqual(firstHeading, secondHeading);
    }

    // The capsule has no fuse at all: 91 s of an untouched field is past the 90 s every other pickup rots on,
    // and it is still lying there, still tumbling and still flashing.
    [Fact]
    public void Capsule_OutlastsEveryOtherPickupsFuse()
    {
        var (em, _) = World();
        Entity capsule = AddCapsuleAway(em);

        for (int tick = 0; tick < 91 * 60; tick++) Tick(em);

        var rest = em.GetComponent<EcmPulse>(capsule);
        Assert.True(em.HasComponent<EcmPulse>(capsule), "an ECM capsule waits on the field for the whole run");
        Assert.True(rest.Spin > 0f);
        Assert.True(rest.Flash > 0f);
        Assert.Equal(600f, em.GetComponent<Position>(capsule).Value.X, precision: 3);
    }

    // A second capsule while a jam is still up refreshes it rather than stacking a second clock: the same
    // application path, so the player's 5 s restarts and the world does not get a two-pulse state.
    [Fact]
    public void Capsule_SecondPickupRefreshesTheJam()
    {
        var (em, _) = World();
        Entity ship = AddShip(em, new Vector2(500f, 0f));
        AddCapsuleAtTheHull(em);

        Tick(em);

        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new EcmSystem().Update(view, 4f, commands);
        commands.Apply(em);
        Assert.True(em.TryGetComponent<EcmDisabled>(ship, out var worn) && worn.TimeRemaining < EcmStats.Duration);

        AddCapsuleAtTheHull(em);
        Tick(em);

        Assert.Equal(EcmStats.Duration, em.GetComponent<EcmDisabled>(ship).TimeRemaining, precision: 5);
    }

    // The jam's own clock runs it out and takes the component off again.
    [Fact]
    public void JamCountdownExpiresOnItsOwn()
    {
        var (em, _) = World();
        Entity ship = AddShip(em, new Vector2(500f, 0f));
        AddCapsuleAtTheHull(em);
        Tick(em);

        var view = new WorldView(em);
        var ecm = new EcmSystem();
        for (int tick = 0; tick < (int)(EcmStats.Duration / Dt) + 2; tick++)
        {
            var commands = new CommandBuffer();
            ecm.Update(view, Dt, commands);
            commands.Apply(em);
        }

        Assert.False(em.HasComponent<EcmDisabled>(ship), "five seconds of jam, then the hull is back");
    }
}
