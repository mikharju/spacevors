using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Impact damage rule (plans/IMPACT_DAMAGE.md): pure helper tests + wiring through CollisionSystem.
public class ImpactDamageTest
{
    private const float PlayerMass = 1018f;   // π · 18² (default player radius)
    private const float BigAsteroidMass = 5027f; // π · 40²

    // ---- pure rule: ImpactDamage.ForMasses ----

    [Fact]
    public void BelowMinClosingSpeed_NoDamage()
    {
        Assert.Equal((0, 0), ImpactDamage.ForMasses(3000f, 5000f, 159.9f));
    }

    [Fact]
    public void SideBySideLowRelativeSpeed_NoDamage()
    {
        // Two fast ships with only 10 px/s between them feel like a gentle bump.
        Assert.Equal((0, 0), ImpactDamage.ForMasses(PlayerMass, 3000f, 10f));
    }

    [Fact]
    public void DamageScalesQuadraticallyWithClosingSpeed()
    {
        var (a200, _) = ImpactDamage.ForMasses(10000f, 10000f, 200f);
        var (a400, _) = ImpactDamage.ForMasses(10000f, 10000f, 400f);
        Assert.Equal(1, a200);
        Assert.Equal(4, a400); // double speed = four times the energy
    }

    [Fact]
    public void LighterBodyAbsorbsMoreDamage()
    {
        var (light, heavy) = ImpactDamage.ForMasses(3000f, 30000f, 500f);
        Assert.Equal(6, light);
        Assert.Equal(1, heavy);
    }

    [Fact]
    public void DamageIsCappedSoOneHitNeverKillsTheInitialHull()
    {
        Assert.Equal(7, ImpactDamage.MaxImpactDamage); // 8 hp Scout keeps 1 hp
        var (a, b) = ImpactDamage.ForMasses(PlayerMass, BigAsteroidMass, 5000f);
        Assert.True(a <= ImpactDamage.MaxImpactDamage);
        Assert.True(b <= ImpactDamage.MaxImpactDamage);
    }

    [Fact]
    public void StrongCollision_BarelySurvivesInitialHull()
    {
        // Scout hull (r=46 -> mass 6648) at 800 px/s closing into a large asteroid:
        // the strong collision the tuning targets — 8 hp minus damage leaves exactly 1.
        var (asteroidDamage, playerDamage) = ImpactDamage.ForMasses(BigAsteroidMass, 6648f, 800f);
        Assert.Equal(7, playerDamage);
        Assert.Equal(1, 8 - playerDamage);
        Assert.True(asteroidDamage >= playerDamage); // the (indestructible) rock absorbs at least as much
    }

    [Fact]
    public void FullSpeedRam_IsOnlyMinorDamage()
    {
        // Player top speed is 400 px/s: a normal ram is a medium hit since the
        // speed doubling moved "strong" to 800 px/s closing.
        var (_, playerDamage) = ImpactDamage.ForMasses(BigAsteroidMass, 6648f, 400f);
        Assert.Equal(2, playerDamage);
    }

    [Fact]
    public void TinyAbsorbedEnergy_RoundsToZero()
    {
        // Pebble (r=15) vs player just above the speed threshold: nothing happens.
        Assert.Equal((0, 0), ImpactDamage.ForMasses(707f, PlayerMass, 200f));
    }

    [Fact]
    public void SparkCount_GrowsWithDamageAndCaps()
    {
        Assert.Equal(2, ImpactDamage.SparkCount(2));
        Assert.Equal(3, ImpactDamage.SparkCount(3));
        Assert.Equal(6, ImpactDamage.SparkCount(15));
    }

    // ---- wiring through CollisionSystem.ResolveCollision ----

    private static (EntityManager em, WorldView view, CollisionSystem system) Setup()
    {
        var em = new EntityManager();
        return (em, new WorldView(em), new CollisionSystem());
    }

    private static Entity AddPlayer(EntityManager em, Vector2 pos, Vector2 vel, int hp)
    {
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(pos));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));
        em.AddComponent(player, new Health(hp, 10));
        em.AddComponent(player, new Velocity(vel));
        return player;
    }

    private static Entity AddEnemyShip(EntityManager em, Vector2 pos, Vector2 vel, int hp)
    {
        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(pos));
        em.AddComponent(ship, new EnemyShip(Radius: 18f, Speed: 50f, TurnRate: 1f, FiringRange: 300f, TurretFireRate: 2f, TurretAmmoSpeed: 150f, Acceleration: 30f, GraphicsId: 0));
        em.AddComponent(ship, new Health(hp, 30));
        em.AddComponent(ship, new Velocity(vel));
        return ship;
    }

    private static Entity AddAsteroid(EntityManager em, Vector2 pos, float radius)
    {
        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(pos));
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: radius));
        em.AddComponent(asteroid, new Velocity(Vector2.Zero));
        return asteroid;
    }

    private static int CountSparks(WorldView view)
    {
        int count = 0;
        foreach (var _ in view.GetEntitiesWithComponents<DamageSpark>()) count++;
        return count;
    }

    [Fact]
    public void EnemyShipRamsAsteroid_ShipDamaged_AndSparksThrown()
    {
        var (em, view, system) = Setup();
        AddPlayer(em, Vector2.Zero, Vector2.Zero, 10);
        var ship = AddEnemyShip(em, new Vector2(-500f, 0f), new Vector2(800f, 0f), 30);
        AddAsteroid(em, new Vector2(-465f, 0f), 40f);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        // 800 px/s closing, ship mass 3000 vs asteroid 5027: absorbs the capped 7 damage.
        Assert.Equal(23, em.GetComponent<Health>(ship).Current);
        Assert.Equal(4, CountSparks(view)); // damage 7 >= SparkDamageThreshold -> 2 + 7/3 sparks
    }

    [Fact]
    public void EnemyShipDriftsIntoAsteroid_NoDamageNoSparks()
    {
        var (em, view, system) = Setup();
        AddPlayer(em, Vector2.Zero, Vector2.Zero, 10);
        var ship = AddEnemyShip(em, new Vector2(-500f, 0f), new Vector2(50f, 0f), 30);
        AddAsteroid(em, new Vector2(-465f, 0f), 40f);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(30, em.GetComponent<Health>(ship).Current);
        Assert.Equal(0, CountSparks(view));
    }

    [Fact]
    public void EnemyShipRamsAsteroid_LethalImpact_FullDeathFlowRuns()
    {
        var (em, view, system) = Setup();
        AddPlayer(em, Vector2.Zero, Vector2.Zero, 10);
        var ship = AddEnemyShip(em, new Vector2(-500f, 0f), new Vector2(500f, 0f), 2);
        AddAsteroid(em, new Vector2(-465f, 0f), 40f);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        Assert.Contains(commands.Commands, c => c is AddComponentCommand<ShipDeathExplosion>);
        commands.Apply(em);

        Assert.True(em.HasComponent<Dead>(ship), "impact kill must run the same death flow as guns");
        Assert.False(em.HasComponent<Health>(ship));
    }

    [Fact]
    public void PlayerRamsAsteroid_PlayerDamaged()
    {
        var (em, view, system) = Setup();
        var player = AddPlayer(em, Vector2.Zero, new Vector2(400f, 0f), 10);
        AddAsteroid(em, new Vector2(30f, 0f), 40f);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        // 400 px/s closing is a medium hit since the speed doubling: 2 damage.
        Assert.Equal(8, em.GetComponent<Health>(player).Current);
    }

    [Fact]
    public void PlayerRamsAsteroid_LethalImpact_PlayerDies()
    {
        var (em, view, system) = Setup();
        var player = AddPlayer(em, Vector2.Zero, new Vector2(400f, 0f), 2);
        AddAsteroid(em, new Vector2(30f, 0f), 40f);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.True(em.HasComponent<Dead>(player));
        // The fatal hit must zero the health: the hull damage shading reads Health, and a
        // stale high value would render a pristine hull while dying.
        Assert.Equal(0, em.GetComponent<Health>(player).Current);
    }

    [Fact]
    public void TwoShipsFlyingTogether_LowRelativeSpeed_NoDamage()
    {
        var (em, view, system) = Setup();
        AddPlayer(em, new Vector2(5000f, 5000f), Vector2.Zero, 10);
        var ahead = AddEnemyShip(em, new Vector2(-500f, 0f), new Vector2(300f, 0f), 20);
        var behind = AddEnemyShip(em, new Vector2(-530f, 0f), new Vector2(310f, 0f), 20);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.Equal(20, em.GetComponent<Health>(ahead).Current);
        Assert.Equal(20, em.GetComponent<Health>(behind).Current);
    }

    [Fact]
    public void MineSlamsIntoShip_MineTakesNoImpactDamage()
    {
        var (em, view, system) = Setup();
        AddPlayer(em, new Vector2(5000f, 5000f), Vector2.Zero, 10);
        var ship = AddEnemyShip(em, new Vector2(-500f, 0f), Vector2.Zero, 30);

        var mine = em.CreateEntity();
        em.AddComponent(mine, new Position(new Vector2(-475f, 0f)));
        em.AddComponent(mine, new EnemyMine(MineSize.Large, Speed: 10f, Angle: 0f));
        em.AddComponent(mine, new Health(50, 50));
        em.AddComponent(mine, new Velocity(new Vector2(-2000f, 0f)));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        // Mines keep their own contact rule: the ship absorbs the hit, the mine does not.
        Assert.Equal(26, em.GetComponent<Health>(ship).Current);
        Assert.Equal(50, em.GetComponent<Health>(mine).Current);
    }
}
