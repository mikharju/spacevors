using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

public class CollisionSystemTests
{
    private (EntityManager em, WorldView view, CollisionSystem system) Setup()
    {
        var em = new EntityManager();
        var view = new WorldView(em);
        var system = new CollisionSystem();
        return (em, view, system);
    }

    [Fact]
    public void AmmoVsAsteroid_AsteroidPushedAwayFromAmmo()
    {
        var (em, view, system) = Setup();

        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(0f, 0f)));
        em.AddComponent(asteroid, new Asteroid(Radius: 20f));

        var ammo = em.CreateEntity();
        em.AddComponent(ammo, new Position(new Vector2(-5f, 0f)));
        em.AddComponent(ammo, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);

        var cmdList = commands.Commands.ToList();
        Assert.NotEmpty(cmdList);

        commands.Apply(em);

        Assert.True(em.HasComponent<Velocity>(asteroid), "Asteroid should have Velocity component after collision");
        var asteroidVel = em.GetComponent<Velocity>(asteroid);
        Assert.True(asteroidVel.Value.X < -0.1f, $"Expected negative X velocity (hit from left), got {asteroidVel.Value.X}");
    }

    [Fact]
    public void AmmoVsAsteroid_AmmoDestroyed()
    {
        var (em, view, system) = Setup();

        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(0f, 0f)));
        em.AddComponent(asteroid, new Asteroid(Radius: 20f));

        var ammo = em.CreateEntity();
        em.AddComponent(ammo, new Position(new Vector2(-5f, 0f)));
        em.AddComponent(ammo, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);

        Assert.Contains(commands.Commands, c => c is DestroyEntityCommand dec && dec.Entity == ammo);
    }

    [Fact]
    public void AsteroidVsAsteroid_ElasticCollision_ReversesVelocities()
    {
        var (em, view, system) = Setup();

        var a1 = em.CreateEntity();
        em.AddComponent(a1, new Position(new Vector2(-30f, 0f)));
        em.AddComponent(a1, new Asteroid(Radius: 20f));
        em.AddComponent(a1, new Velocity(new Vector2(100f, 0f)));

        var a2 = em.CreateEntity();
        em.AddComponent(a2, new Position(new Vector2(30f, 0f)));
        em.AddComponent(a2, new Asteroid(Radius: 20f));
        em.AddComponent(a2, new Velocity(new Vector2(-100f, 0f)));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        var v1 = em.GetComponent<Velocity>(a1);
        var v2 = em.GetComponent<Velocity>(a2);

        Assert.True(v1.Value.X > 50f, $"Expected positive X velocity after bounce, got {v1.Value.X}");
        Assert.True(v2.Value.X < -50f, $"Expected negative X velocity after bounce, got {v2.Value.X}");
    }

    [Fact]
    public void AsteroidVsAsteroid_LowSpeed_NoRestitution_SticksTogether()
    {
        var (em, view, system) = Setup();

        var a1 = em.CreateEntity();
        em.AddComponent(a1, new Position(new Vector2(-5f, 0f)));
        em.AddComponent(a1, new Asteroid(Radius: 10f));
        em.AddComponent(a1, new Velocity(new Vector2(3f, 0f)));

        var a2 = em.CreateEntity();
        em.AddComponent(a2, new Position(new Vector2(5f, 0f)));
        em.AddComponent(a2, new Asteroid(Radius: 10f));
        em.AddComponent(a2, new Velocity(new Vector2(-3f, 0f)));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        var v1 = em.GetComponent<Velocity>(a1);
        var v2 = em.GetComponent<Velocity>(a2);

        Assert.True(v1.Value.X < 3f, $"Expected reduced velocity (no bounce), got {v1.Value.X}");
        Assert.True(v2.Value.X > -3f, $"Expected reduced velocity (no bounce), got {v2.Value.X}");
    }

    [Fact]
    public void AmmoVsAsteroid_GrazingImpact_InducesAngularVelocity()
    {
        var (em, view, system) = Setup();

        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(0f, 0f)));
        em.AddComponent(asteroid, new Asteroid(Radius: 20f));
        em.AddComponent(asteroid, new AngularVelocity(0f));

        var ammo = em.CreateEntity();
        em.AddComponent(ammo, new Position(new Vector2(-5f, -14f)));
        em.AddComponent(ammo, new Ammo(Velocity: new Vector2(200f, 200f), Radius: 3f, Lifetime: 10f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        var angVel = em.GetComponent<AngularVelocity>(asteroid);
        Assert.True(angVel.Value < -0.003f, $"Expected negative angular velocity (clockwise), got {angVel.Value}");
    }

    [Fact]
    public void AmmoVsAsteroid_ConsistentImpulseDirection_WithResolveCollision()
    {
        var (em, view, system) = Setup();

        // Asteroid at origin, ammo approaching from left
        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(0f, 0f)));
        em.AddComponent(asteroid, new Asteroid(Radius: 20f));

        var ammo = em.CreateEntity();
        em.AddComponent(ammo, new Position(new Vector2(-5f, 0f)));
        em.AddComponent(ammo, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        var asteroidVel = em.GetComponent<Velocity>(asteroid);

        // Impulse should push asteroid to the LEFT (normal = ammoPos - asteroidPos points toward ammo)
        Assert.True(asteroidVel.Value.X < -0.1f,
            $"Asteroid impulse direction inconsistent: expected negative X velocity, got {asteroidVel.Value.X}");
    }

    [Fact]
    public void AmmoVsAsteroid_ProjectileFromRight_AsteroidPushedLeft()
    {
        var (em, view, system) = Setup();

        // Stationary asteroid at origin, projectile approaching from right moving leftward
        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(0f, 0f)));
        em.AddComponent(asteroid, new Asteroid(Radius: 20f));

        var ammo = em.CreateEntity();
        em.AddComponent(ammo, new Position(new Vector2(10f, 0f)));
        em.AddComponent(ammo, new Ammo(Velocity: new Vector2(-100f, 0f), Radius: 3f, Lifetime: 10f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.True(em.HasComponent<Velocity>(asteroid), "Asteroid should have Velocity component after collision");
        var asteroidVel = em.GetComponent<Velocity>(asteroid);

        // Asteroid hit from the right must be pushed RIGHT (positive X) — away from incoming projectile
        Assert.True(asteroidVel.Value.X > 0.05f,
            $"Expected positive X velocity when hit from right, got {asteroidVel.Value.X}");
    }
}
