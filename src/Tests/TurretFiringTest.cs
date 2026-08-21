using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class TurretFiringTest
{
    [Fact]
    public void TestTurretCreatesAmmo()
    {
        var em = new EntityManager();

        // Create player with velocity
        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(new Vector2(100f, 100f)));
        em.AddComponent(playerEntity, new Velocity(new Vector2(10f, 5f)));
        em.AddComponent(playerEntity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 5f));

        // Create turret
        var turretEntity = em.CreateEntity();
        em.AddComponent(turretEntity, new Position(new Vector2(0f, 0f)));
        em.AddComponent(turretEntity, new Rotation(0f));
        em.AddComponent(turretEntity, new Turret(
            Weapon: new WeaponStats(FireRate: 5f, AmmoSpeed: 500f, KickbackForce: 10f, PelletCount: 1, Scatter: 0.05f, ShotLifetime: 2f, Damage: 1),
            WeaponName: "TestWeapon",
            ArcAngle: MathF.PI / 4f,
            Range: 300f,
            AutoTarget: true));

        // Create an asteroid target (in front of turret - turret points DOWN with rotation 0)
        var asteroidEntity = em.CreateEntity();
        em.AddComponent(asteroidEntity, new Position(new Vector2(0f, -100f)));
        em.AddComponent(asteroidEntity, new Velocity(Vector2.Zero));
        em.AddComponent(asteroidEntity, new Asteroid(IsSmall: false, Radius: 30f));

        // Create view and run system
        var view = new WorldView(em);
        
        // Check cooldown before running
        var cooldown = CooldownHelper.GetCooldown(view, turretEntity);
        Assert.True(cooldown <= 0f, $"Turret has cooldown {cooldown}, expected <= 0");

        // Run TurretFiringSystem
        var commands = new CommandBuffer();
        var system = new TurretFiringSystem();
        system.Update(view, 1f / 60f, commands);

        Assert.True(commands.Commands.Count() > 0, $"No commands added! Cooldown={cooldown}");

        // Apply commands
        commands.Apply(em);

        // Check if ammo was created
        var ammoCount = em.GetEntitiesWithComponents<Ammo>().Count();
        Assert.True(ammoCount > 0, $"Expected ammo to be created, but got {ammoCount}");
    }

    [Fact]
    public void TestKickbackScalesWithShipRadius()
    {
        // Lightest ship (radius 46) gets 2/3 of base kickback; double the radius means 8x the mass.
        float light = KickbackAfterShot(radius: 46f);
        float heavy = KickbackAfterShot(radius: 92f);

        Assert.Equal(20f, light, precision: 5); // 30 * 2/3
        Assert.Equal(2.5f, heavy, precision: 5); // 30 * 2/3 / 8
    }

    private static float KickbackAfterShot(float radius)
    {
        var em = new EntityManager();

        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(new Vector2(100f, 100f)));
        em.AddComponent(playerEntity, new Velocity(Vector2.Zero));
        em.AddComponent(playerEntity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: radius, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 5f));

        var turretEntity = em.CreateEntity();
        em.AddComponent(turretEntity, new Position(Vector2.Zero));
        em.AddComponent(turretEntity, new Rotation(0f));
        em.AddComponent(turretEntity, new Turret(
            Weapon: new WeaponStats(FireRate: 5f, AmmoSpeed: 500f, KickbackForce: 30f, PelletCount: 1, Scatter: 0.05f),
            WeaponName: "TestWeapon",
            ArcAngle: MathF.PI / 4f,
            Range: 300f));

        var asteroidEntity = em.CreateEntity();
        em.AddComponent(asteroidEntity, new Position(new Vector2(0f, -100f)));
        em.AddComponent(asteroidEntity, new Velocity(Vector2.Zero));
        em.AddComponent(asteroidEntity, new Asteroid(IsSmall: false, Radius: 30f));

        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(new WorldView(em), 1f / 60f, commands);
        commands.Apply(em);

        // Shot goes down (-Y), so kickback pushes the player up (+Y).
        return em.GetComponent<Velocity>(playerEntity).Value.Y;
    }

    [Theory]
    [InlineData("TwinChainGun", 0f, -100f, true)]     // forward: fires
    [InlineData("TwinChainGun", 0f, 100f, false)]     // backward: no fire
    [InlineData("TwinChainGun", -100f, 0f, false)]    // left side: no fire
    [InlineData("PointDefenceTurret", 0f, -100f, false)] // forward: excluded
    [InlineData("PointDefenceTurret", 0f, 100f, true)]   // backward: fires
    [InlineData("PointDefenceTurret", -100f, 0f, true)]  // left side: fires
    public void TestAddOnWeaponCoversIntendedArc(string weaponName, float targetX, float targetY, bool expectedToFire)
    {
        var weapon = WeaponType.FromName(weaponName);
        Assert.NotNull(weapon);
        var mount = weapon!.Value.AddOn!.Value;

        var em = new EntityManager();

        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(Vector2.Zero));
        em.AddComponent(playerEntity, new Velocity(Vector2.Zero));
        em.AddComponent(playerEntity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 5f));

        var turretEntity = em.CreateEntity();
        em.AddComponent(turretEntity, new Position(Vector2.Zero));
        em.AddComponent(turretEntity, new Rotation(mount.ArcOffset)); // player angle 0 + arc offset
        em.AddComponent(turretEntity, new Turret(Weapon: weapon.Value.Stats, WeaponName: weapon.Value.Name, ArcAngle: mount.ArcAngle, Range: mount.Range, AutoTarget: mount.AutoTarget));

        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(targetX, targetY)));
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 30f));

        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(new WorldView(em), 1f / 60f, commands);
        commands.Apply(em);

        bool fired = em.GetEntitiesWithComponents<Ammo>().Count() > 0;
        Assert.Equal(expectedToFire, fired);
    }

    private static EntityManager CreateEnemyFiringWorld(Vector2 shipPos, Vector2 shipVel, float shipAngle, Vector2 playerPos, Vector2 playerVel)
    {
        var em = new EntityManager();

        var shipEntity = em.CreateEntity();
        em.AddComponent(shipEntity, new Position(shipPos));
        em.AddComponent(shipEntity, new Velocity(shipVel));
        em.AddComponent(shipEntity, new Rotation(shipAngle));
        em.AddComponent(shipEntity, new EnemyShip(Radius: 20f, Speed: 65f, TurnRate: 1f, FiringRange: 700f, TurretFireRate: 1.5f, TurretAmmoSpeed: 200f, Acceleration: 45f, Damage: 3, GraphicsId: 0));
        em.AddComponent(shipEntity, new Turret(Weapon: new WeaponStats(1.5f, 200f, KickbackForce: 0f, PelletCount: 1, Scatter: 0f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: 1200f, IsEnemy: true));

        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(playerPos));
        em.AddComponent(playerEntity, new Velocity(playerVel));
        em.AddComponent(playerEntity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 5f));

        return em;
    }

    private static int RunFiringPass(EntityManager em)
    {
        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(new WorldView(em), 1f / 60f, commands);
        commands.Apply(em);
        return em.GetEntitiesWithComponents<Ammo>().Count();
    }

    [Fact]
    public void TestEnemyFiresWhenHullEdgeIsInArc()
    {
        // Player center is 13 deg off axis (outside the 11.25 deg half-arc),
        // but the hull edge is inside: asin(18/300) = 3.4 deg margin.
        float angle = MathF.PI * 13f / 180f;
        var em = CreateEnemyFiringWorld(Vector2.Zero, Vector2.Zero, MathF.PI / 2f, new Vector2(300f * (float)Math.Cos(angle), 300f * (float)Math.Sin(angle)), Vector2.Zero);

        Assert.True(RunFiringPass(em) > 0, "Expected enemy to fire when hull edge is inside the arc");
    }

    [Fact]
    public void TestEnemyFiresWhenHullEdgeIsInRange()
    {
        // Player center at 710 px is beyond FiringRange (700), but hull edge (710 - 18) is in range.
        var em = CreateEnemyFiringWorld(Vector2.Zero, Vector2.Zero, MathF.PI / 2f, new Vector2(710f, 0f), Vector2.Zero);

        Assert.True(RunFiringPass(em) > 0, "Expected enemy to fire when hull edge is in range");
    }

    [Fact]
    public void TestEnemyDoesNotFireAtOwnMines()
    {
        var em = CreateEnemyFiringWorld(Vector2.Zero, Vector2.Zero, MathF.PI / 2f, new Vector2(5000f, 0f), Vector2.Zero);

        var mineEntity = em.CreateEntity();
        em.AddComponent(mineEntity, new Position(new Vector2(200f, 0f)));
        em.AddComponent(mineEntity, new Velocity(Vector2.Zero));
        em.AddComponent(mineEntity, new EnemyMine(MineSize.Large, Speed: 10f, Angle: MathF.PI / 2f));

        int ammoCount = RunFiringPass(em);
        Assert.True(ammoCount == 0, $"Enemy fired {ammoCount} shots at its own mine");
    }

    [Fact]
    public void TestEnemyAmmoInheritsShipVelocity()
    {
        // Ship drifts sideways at 80 px/s while player sits dead ahead.
        // Lead aim must cancel the drift: world velocity y stays near zero.
        var em = CreateEnemyFiringWorld(Vector2.Zero, new Vector2(0f, 80f), MathF.PI / 2f, new Vector2(500f, 0f), Vector2.Zero);

        Assert.True(RunFiringPass(em) > 0, "Expected enemy to fire at player dead ahead");

        var (ammoEntity, ammo) = em.GetEntitiesWithComponents<Ammo>().FirstOrDefault();
        Assert.True(ammoEntity.Value >= 0, "Expected enemy ammo to be spawned");
        var vel = ammo.Velocity;

        Assert.InRange(vel.X, 150f, 215f);
        Assert.InRange(vel.Y, -25f, 25f);

        var relativeVel = vel - new Vector2(0f, 80f); // strip inherited ship velocity
        float speed = relativeVel.Magnitude;
        Assert.InRange(speed, 170f, 230f); // 200 px/s +/- 15% spawn variation

        var expectedDir = new Vector2(0.9165f, -0.4f); // lead direction for this geometry
        Assert.True(Vector2.Dot(relativeVel / speed, expectedDir) > 0.999f, $"Aim {relativeVel} not along expected lead direction");
    }
}
