using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class LaserTest
{
    private const float Dt = 1f / 120f;

    private static Entity AddBeam(
        EntityManager em, Vector2 start, Vector2 direction, float length, int damage, bool isEnemy = false, float width = 3f)
    {
        var beam = em.CreateEntity();
        em.AddComponent(beam, new Position(start));
        em.AddComponent(beam, new LaserBeam(start, direction, length, width, damage, isEnemy, AmmoColor.Blue,
            LaserBeam.FlashLifetime, LaserBeam.FlashLifetime));
        return beam;
    }

    private static Entity AddShip(EntityManager em, Vector2 position, int health = 10)
    {
        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(position));
        em.AddComponent(ship, new EnemyShip(Radius: 18f, Speed: 50f, TurnRate: 1f, FiringRange: 300f, TurretFireRate: 2f, TurretAmmoSpeed: 150f, Acceleration: 30f, GraphicsId: 0));
        em.AddComponent(ship, new Health(health, health));
        return ship;
    }

    [Fact]
    public void HitscanTurret_FiresBeamInsteadOfAmmo()
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(0f, 0f)));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10));

        var turret = em.CreateEntity();
        em.AddComponent(turret, new Position(new Vector2(0f, 0f)));
        em.AddComponent(turret, new Rotation(0f));
        em.AddComponent(turret, new Turret(
            Weapon: new WeaponStats(FireRate: 1f, AmmoSpeed: 0f, KickbackForce: 0f, PelletCount: 1, Scatter: 0f, Damage: 7, Hitscan: true),
            WeaponName: "LaserLance",
            ArcAngle: MathF.PI / 4f,
            Range: 1200f));

        // Asteroid straight ahead (turret rotation 0 points down, -Y), clicked as the primary target.
        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(0f, -500f)));
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 30f));
        em.AddComponent(player, new PrimaryTarget(asteroid));

        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(view, Dt, commands);
        commands.Apply(em);

        Assert.Equal(0, em.GetEntitiesWithComponents<Ammo>().Count());

        Assert.True(em.GetEntitiesWithComponents<LaserBeam>().TryFirst(out var beamTuple));
        var beam = beamTuple.Value1;
        Assert.Equal(0f, beam.Start.X, 1);
        Assert.Equal(-20f, beam.Start.Y, 1);
        Assert.True(Vector2.Dot(beam.Direction, new Vector2(0f, -1f)) > 0.99f);
        Assert.Equal(1200f, beam.Length, 1);
        Assert.Equal(7, beam.Damage);
        Assert.True(beam.DamagePending);
    }

    [Fact]
    public void Beam_DamagesOnlyFirstShipAlongPath()
    {
        var em = new EntityManager();
        var view = new WorldView(em);

        var near = AddShip(em, new Vector2(300f, 0f));
        var far = AddShip(em, new Vector2(600f, 0f));
        AddBeam(em, new Vector2(0f, 0f), new Vector2(1f, 0f), 1000f, damage: 5);

        var commands = new CommandBuffer();
        new CollisionSystem().Update(view, Dt, commands);
        commands.Apply(em);

        Assert.Equal(5, em.GetComponent<Health>(near).Current);
        Assert.Equal(10, em.GetComponent<Health>(far).Current);
    }

    [Fact]
    public void Beam_AppliesDamageExactlyOnce()
    {
        var em = new EntityManager();
        var view = new WorldView(em);

        var ship = AddShip(em, new Vector2(300f, 0f));
        var beam = AddBeam(em, new Vector2(0f, 0f), new Vector2(1f, 0f), 1000f, damage: 5);

        var commands = new CommandBuffer();
        new CollisionSystem().Update(view, Dt, commands);
        commands.Apply(em);

        var commands2 = new CommandBuffer();
        new CollisionSystem().Update(view, Dt, commands2);
        commands2.Apply(em);

        Assert.Equal(5, em.GetComponent<Health>(ship).Current);
        Assert.False(em.GetComponent<LaserBeam>(beam).DamagePending);
    }

    [Fact]
    public void Beam_BlockedByAsteroid_BehindShipUntouched()
    {
        var em = new EntityManager();
        var view = new WorldView(em);

        var blocker = em.CreateEntity();
        em.AddComponent(blocker, new Position(new Vector2(200f, 0f)));
        em.AddComponent(blocker, new Asteroid(IsSmall: false, Radius: 20f));

        var ship = AddShip(em, new Vector2(600f, 0f));
        AddBeam(em, new Vector2(0f, 0f), new Vector2(1f, 0f), 1000f, damage: 5);

        var commands = new CommandBuffer();
        new CollisionSystem().Update(view, Dt, commands);
        commands.Apply(em);

        Assert.Equal(10, em.GetComponent<Health>(ship).Current);
    }

    [Fact]
    public void Beam_GrazesShipWithinWidth()
    {
        var em = new EntityManager();
        var view = new WorldView(em);

        // Beam along y=0, ship center 18 px off-axis: 18 > 18 is false at width 3 (18 < 18+3) → hit.
        var ship = AddShip(em, new Vector2(300f, 18f));
        AddBeam(em, new Vector2(0f, 0f), new Vector2(1f, 0f), 1000f, damage: 5, width: 3f);

        var commands = new CommandBuffer();
        new CollisionSystem().Update(view, Dt, commands);
        commands.Apply(em);

        Assert.Equal(5, em.GetComponent<Health>(ship).Current);
    }

    [Fact]
    public void EnemyBeam_HitsPlayer()
    {
        var em = new EntityManager();
        var view = new WorldView(em);

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(400f, 0f)));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));
        em.AddComponent(player, new Health(10, 10));

        AddBeam(em, new Vector2(0f, 0f), new Vector2(1f, 0f), 1000f, damage: 3, isEnemy: true);

        var commands = new CommandBuffer();
        new CollisionSystem().Update(view, Dt, commands);
        commands.Apply(em);

        Assert.Equal(7, em.GetComponent<Health>(player).Current);
    }

    [Fact]
    public void BeamLifetime_ExpireDestroysEntity()
    {
        var em = new EntityManager();
        var view = new WorldView(em);

        var beam = AddBeam(em, new Vector2(0f, 0f), new Vector2(1f, 0f), 100f, damage: 1);

        var commands = new CommandBuffer();
        new EffectSystem().Update(view, LaserBeam.FlashLifetime + 0.01f, commands);

        Assert.Contains(commands.Commands, c => c is DestroyEntityCommand dec && dec.Entity == beam);
    }
}
