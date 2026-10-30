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
    public void DamageGrowsQuadraticallyWithClosingSpeed()
    {
        // Equal masses: raw damage is 9e-6·v², so doubling the speed quadruples it
        // (0.81 at 300 → 3.24 at 600). The 0.5 floor and the 3-point cap clip both ends.
        int DamageAt(float speed) => ImpactDamage.ForMasses(10000f, 10000f, speed).DamageA;

        Assert.Equal(0, DamageAt(200f));
        Assert.Equal(1, DamageAt(300f));
        Assert.Equal(2, DamageAt(400f));
        Assert.Equal(3, DamageAt(500f));
        Assert.Equal(3, DamageAt(600f)); // capped
    }

    [Fact]
    public void LighterBodyAbsorbsMoreDamage()
    {
        var (light, heavy) = ImpactDamage.ForMasses(3000f, 30000f, 800f);
        Assert.Equal(3, light);
        Assert.Equal(1, heavy);
    }

    [Fact]
    public void DamageIsCappedSoOneHitNeverKillsTheInitialHull()
    {
        Assert.Equal(3, ImpactDamage.MaxImpactDamage); // 8 hp Scout keeps 5
        var (a, b) = ImpactDamage.ForMasses(PlayerMass, BigAsteroidMass, 5000f);
        Assert.True(a <= ImpactDamage.MaxImpactDamage);
        Assert.True(b <= ImpactDamage.MaxImpactDamage);
    }

    [Fact]
    public void StrongCollision_IsThreePointsOfDamage()
    {
        // Scout hull (r=46 -> mass 6648) at 800 px/s closing into a large asteroid:
        // the big impact the tuning targets — 3 of its 8 hp.
        var (asteroidDamage, playerDamage) = ImpactDamage.ForMasses(BigAsteroidMass, 6648f, 800f);
        Assert.Equal(3, playerDamage);
        Assert.Equal(5, 8 - playerDamage);
        Assert.True(asteroidDamage >= playerDamage); // the (indestructible) rock absorbs at least as much
    }

    [Fact]
    public void FullSpeedRam_IsOnePointOfDamage()
    {
        // Player top speed is 400 px/s: the ram a pilot does on purpose is a small hit.
        var (_, playerDamage) = ImpactDamage.ForMasses(BigAsteroidMass, 6648f, 400f);
        Assert.Equal(1, playerDamage);
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
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10, Radius: 46f)); // Scout hull: impact damage depends on mass
        em.AddComponent(player, new Health(hp, 10));
        em.AddComponent(player, new Velocity(vel));
        return player;
    }

    private static Entity AddEnemyShip(EntityManager em, Vector2 pos, Vector2 vel, int hp)
    {
        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(pos));
        em.AddComponent(ship, new EnemyShip(Radius: 18f, Speed: 50f, TurnRate: 1f, FiringRange: 300f, TurretFireRate: 2f, TurretAmmoSpeed: 150f, Acceleration: 30f, GraphicsId: 0));
        em.AddComponent(ship, new Rotation(0f));
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

        // 800 px/s closing, ship mass 3000 vs asteroid 5027: the big impact, 3 damage.
        Assert.Equal(27, em.GetComponent<Health>(ship).Current);
        Assert.Equal(3, CountSparks(view)); // damage 3 >= SparkDamageThreshold -> 2 + 3/3 sparks
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

        // 400 px/s closing is the ram a pilot does on purpose: one point of damage.
        Assert.Equal(9, em.GetComponent<Health>(player).Current);
    }

    [Fact]
    public void PlayerRamsAsteroid_LethalImpact_PlayerDies()
    {
        var (em, view, system) = Setup();
        var player = AddPlayer(em, Vector2.Zero, new Vector2(800f, 0f), 2);
        AddAsteroid(em, new Vector2(30f, 0f), 40f);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.True(em.HasComponent<Dead>(player));
        // The fatal hit must zero the health: the hull damage shading reads Health, and a
        // stale high value would render a pristine hull while dying.
        Assert.Equal(0, em.GetComponent<Health>(player).Current);

        // The run ends with the player blowing up like an enemy hull, only bigger: the blast
        // sequence is keyed to 1.5x the hull radius.
        Assert.Equal(69f, em.GetComponent<ShipDeathExplosion>(player).ShipRadius);

        // Like every enemy hull, the player stops existing as a ship under the final blast: the
        // sequence component, Position and Rotation come off (no draw, motion, collision or
        // camera), and the turret mounts are destroyed. Player/Health/Dead remain as a husk for
        // the HUD and the game-over screen - and with the component gone, ticking the sequence
        // system again can never re-fire the final stage.
        em.AddComponent(player, new Rotation(0f));
        var mount = em.CreateEntity();
        em.AddComponent(mount, new TurretSlot(TurretLayouts.Scout[0]));
        var sequence = new CommandBuffer();
        new ShipDeathExplosionSystem().Update(view, 1.01f, sequence);
        sequence.Apply(em);
        Assert.False(em.HasComponent<ShipDeathExplosion>(player), "the sequence must end, not re-fire");
        Assert.False(em.HasComponent<Position>(player), "the husk draws and collides as nothing");
        Assert.False(em.HasComponent<Rotation>(player));
        Assert.True(em.HasComponent<Health>(player), "the husk keeps Health for the hud");
        Assert.True(em.HasComponent<Dead>(player), "the husk keeps Dead for game-over detection");
        Assert.False(em.HasComponent<TurretSlot>(mount), "the turret mounts go with the wreck");

        int explosionsBefore = em.GetEntitiesWithComponents<Explosion>().Count();
        var retick = new CommandBuffer();
        new ShipDeathExplosionSystem().Update(view, 1f, retick);
        retick.Apply(em);
        Assert.Equal(explosionsBefore, em.GetEntitiesWithComponents<Explosion>().Count());
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
        Assert.Equal(28, em.GetComponent<Health>(ship).Current);
        Assert.Equal(50, em.GetComponent<Health>(mine).Current);
    }
}
