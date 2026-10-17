using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

// The rack pass: an armed hull throws a guided round at the player, on its own reload, and never past
// the number of rounds the world is allowed to have in the air. A hull with no rack never launches.
public class EnemyMissileFiringTests
{
    // A rack's tier-0 ring is 980 px, so a hull standing 900 px away is inside it. Rotation 0 faces
    // (0,-1), which is the direction of the player from here.
    private static readonly Vector2 HullPos = new(0f, 900f);

    // Past the first rung of the ladder: at composition tier 3 the world may hold three rounds.
    private const float ElapsedWithRacksUnlocked = 200f;

    private static Entity AddPlayer(EntityManager em)
    {
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(0f, 0f)));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));
        em.AddComponent(player, new Health(10, 10));
        return player;
    }

    // The reload is set at spawn, so a test that wants a launch has to arrive with it already run out.
    private static Entity AddHull(EntityManager em, EnemyShipType type, EnemyArmament armament)
    {
        var hull = em.CreateEntity();
        EnemyShipFactory.AddComponents(em, hull, HullPos, Vector2.Zero, 0f, 0f, type, armament, ElapsedWithRacksUnlocked);
        if (em.HasComponent<MissileCooldown>(hull)) em.AddComponent(hull, new MissileCooldown(0f));
        return hull;
    }

    private static void Tick(EntityManager em, WorldView view)
    {
        var commands = new CommandBuffer();
        new EnemyMissileFiringSystem().Update(view, 1f, commands);
        commands.Apply(em);
    }

    private static int RoundsInAir(EntityManager em) => em.GetEntitiesWithComponents<Missile, Ammo>().ToList().Count;

    [Fact]
    public void AnArmedHullLaunchesOneRoundLockedOnThePlayer()
    {
        var em = new EntityManager();
        em.AddElapsedTime(ElapsedWithRacksUnlocked);
        var view = new WorldView(em);
        var player = AddPlayer(em);
        AddHull(em, EnemyShipType.Interceptor, EnemyArmament.Missiles);

        var commands = new CommandBuffer();
        new EnemyMissileFiringSystem().Update(view, 1f, commands);
        commands.Apply(em);

        var rounds = em.GetEntitiesWithComponents<Missile, Ammo>().ToList();
        Assert.Single(rounds);
        var (round, missile, ammo) = rounds[0];
        Assert.True(ammo.IsEnemy, "a rack's round belongs to the enemy, so the player's fire can answer it");
        Assert.Equal(player, missile.LockedTarget);
        Assert.True(em.HasComponent<Health>(round), "and it arrives shootable");
    }

    [Fact]
    public void AGunOnlyHullLaunchesNothing()
    {
        var em = new EntityManager();
        em.AddElapsedTime(ElapsedWithRacksUnlocked);
        var view = new WorldView(em);
        AddPlayer(em);
        AddHull(em, EnemyShipType.HeavyCannon, EnemyArmament.Guns);

        Tick(em, view);
        Assert.Equal(0, RoundsInAir(em));
    }

    [Fact]
    public void ARackOnlyHullCarriesNoGunAtAll()
    {
        var em = new EntityManager();
        var hull = em.CreateEntity();
        EnemyShipFactory.AddComponents(em, hull, HullPos, Vector2.Zero, 0f, 0f, EnemyShipType.Interceptor, EnemyArmament.Missiles, ElapsedWithRacksUnlocked);

        Assert.False(em.HasComponent<Turret>(hull), "a missile hull has no gun to watch, which is what makes it readable");
        Assert.True(em.HasComponent<MissileWeapon>(hull));
    }

    [Fact]
    public void NothingLaunchesWhileTheAirIsAlreadyFull()
    {
        var em = new EntityManager();
        em.AddElapsedTime(ElapsedWithRacksUnlocked);
        var view = new WorldView(em);
        AddPlayer(em);
        AddHull(em, EnemyShipType.Interceptor, EnemyArmament.Missiles);

        // Fill the world's allowance (three at composition tier 3) with rounds that are not this hull's.
        var otherHull = AddHull(em, EnemyShipType.HeavyCannon, EnemyArmament.GunsAndMissiles);
        for (int i = 0; i < EnemyShipStats.MissileCapFor(EnemyShipStats.CompositionTierFor(ElapsedWithRacksUnlocked)); i++)
        {
            var round = em.CreateEntity();
            em.AddComponent(round, new Position(new Vector2(100f * i, 400f)));
            em.AddComponent(round, new Ammo(new Vector2(0f, -100f), Radius: 4f, Lifetime: 6f, IsEnemy: true, Damage: 2));
            em.AddComponent(round, new Missile(otherHull, Speed: 100f, TurnRate: 1f, Steering: 0f));
        }

        Tick(em, view);
        Assert.Equal(EnemyShipStats.MissileCapFor(3), RoundsInAir(em));
    }

    [Fact]
    public void ARackKeepsItsReloadBetweenLaunches()
    {
        var em = new EntityManager();
        em.AddElapsedTime(ElapsedWithRacksUnlocked);
        var view = new WorldView(em);
        AddPlayer(em);
        AddHull(em, EnemyShipType.Interceptor, EnemyArmament.Missiles);

        Tick(em, view);
        Tick(em, view);

        Assert.Equal(1, RoundsInAir(em));
    }

    [Fact]
    public void ARackStaysReadyRatherThanBurningItsReloadOutOfRange()
    {
        var em = new EntityManager();
        em.AddElapsedTime(ElapsedWithRacksUnlocked);
        var view = new WorldView(em);
        AddPlayer(em);
        var hull = AddHull(em, EnemyShipType.Interceptor, EnemyArmament.Missiles);
        em.AddComponent(hull, new Position(new Vector2(0f, 4000f)));

        Tick(em, view);
        Assert.Equal(0, RoundsInAir(em));

        // Ready, not mid-reload: move it back inside the ring and the next tick launches.
        em.AddComponent(hull, new Position(HullPos));
        Tick(em, view);
        Assert.Equal(1, RoundsInAir(em));
    }
}
