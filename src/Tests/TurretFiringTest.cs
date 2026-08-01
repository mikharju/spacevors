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
        em.AddComponent(playerEntity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, Radius: 18f, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 5f));

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
        em.AddComponent(asteroidEntity, new Asteroid(Radius: 30f));

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
}
