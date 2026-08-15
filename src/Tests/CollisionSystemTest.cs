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

    private void AddPlayer(EntityManager em)
    {
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(0f, 0f)));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));
        em.AddComponent(player, new Health(10));
    }

    [Fact]
    public void AmmoVsAsteroid_AsteroidPushedAwayFromAmmo()
    {
        var (em, view, system) = Setup();

        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(0f, 0f)));
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 20f));

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
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 20f));

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
        em.AddComponent(a1, new Asteroid(IsSmall: false, Radius: 20f));
        em.AddComponent(a1, new Velocity(new Vector2(100f, 0f)));

        var a2 = em.CreateEntity();
        em.AddComponent(a2, new Position(new Vector2(30f, 0f)));
        em.AddComponent(a2, new Asteroid(IsSmall: false, Radius: 20f));
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
        em.AddComponent(a1, new Asteroid(IsSmall: false, Radius: 10f));
        em.AddComponent(a1, new Velocity(new Vector2(3f, 0f)));

        var a2 = em.CreateEntity();
        em.AddComponent(a2, new Position(new Vector2(5f, 0f)));
        em.AddComponent(a2, new Asteroid(IsSmall: false, Radius: 10f));
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
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 20f));
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
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 20f));

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
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 20f));

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

    [Fact]
    public void MineVsAsteroid_PhysicsBounce()
    {
        var (em, view, system) = Setup();

        AddPlayer(em);

        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(-500f, 0f)));
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 20f));
        em.AddComponent(asteroid, new Velocity(Vector2.Zero));

        var mine = em.CreateEntity();
        em.AddComponent(mine, new Position(new Vector2(-470f, 0f)));
        em.AddComponent(mine, new EnemyMine(MineSize.Large, Speed: 10f, Angle: 0f));
        em.AddComponent(mine, new Velocity(new Vector2(-50f, 0f)));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.True(em.HasComponent<Velocity>(mine), "Mine should have Velocity after collision with asteroid");
        Assert.True(em.HasComponent<Velocity>(asteroid), "Asteroid should have Velocity after collision with mine");

        var mv = em.GetComponent<Velocity>(mine);
        var av = em.GetComponent<Velocity>(asteroid);

        // Mine moving left hits stationary asteroid → both slow down / reverse
        Assert.True(mv.Value.X > -50f, $"Mine should lose leftward speed, got {mv.Value.X}");
        Assert.True(av.Value.X < 0f, $"Asteroid should be pushed left (negative X), got {av.Value.X}");

        Assert.False(em.HasComponent<Dead>(mine), "Mine should not die from physics collision with asteroid");
    }

    [Fact]
    public void MineVsEnemyShip_PhysicsBounce()
    {
        var (em, view, system) = Setup();

        AddPlayer(em);

        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(new Vector2(-500f, 0f)));
        em.AddComponent(ship, new EnemyShip(Radius: 18f, Speed: 50f, TurnRate: 1f, DetectionRange: 500f, FiringRange: 300f, TurretFireRate: 2f, TurretAmmoSpeed: 150f, Acceleration: 30f, Damage: 1, GraphicsId: 0));
        em.AddComponent(ship, new Health(10));
        em.AddComponent(ship, new Velocity(Vector2.Zero));

        var mine = em.CreateEntity();
        em.AddComponent(mine, new Position(new Vector2(-475f, 0f)));
        em.AddComponent(mine, new EnemyMine(MineSize.Large, Speed: 10f, Angle: 0f));
        em.AddComponent(mine, new Velocity(new Vector2(-50f, 0f)));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.True(em.HasComponent<Velocity>(mine), "Mine should have Velocity after collision with ship");
        Assert.True(em.HasComponent<Velocity>(ship), "Ship should have Velocity after collision with mine");

        var mineVel = em.GetComponent<Velocity>(mine);
        var shipVel = em.GetComponent<Velocity>(ship);

        // Mine moving left hits stationary ship → both get affected by physics
        Assert.True(mineVel.Value.X > -50f, $"Mine should lose leftward speed, got {mineVel.Value.X}");
        Assert.True(shipVel.Value.X < 0f, $"Ship should be pushed left (negative X), got {shipVel.Value.X}");
    }

    [Fact]
    public void MineVsAsteroid_MultiCellMine_CorrectedOnce()
    {
        // A mine straddling a cell boundary occupies two cells. The grid must return it once,
        // otherwise the position correction is applied twice. Compare against an identical setup
        // shifted so the mine fits in one cell: results must match.
        var (velA, deltaA) = RunMineVsAsteroid(mineX: -256f); // straddles boundary x=-256 -> 2 cells
        var (velB, deltaB) = RunMineVsAsteroid(mineX: -192f); // single cell

        Assert.True(Math.Abs(velA.X - velB.X) < 0.001f, $"Multi-cell mine velocity differs: {velA.X} vs {velB.X}");
        Assert.True(Math.Abs(deltaA.X - deltaB.X) < 0.001f, $"Multi-cell mine correction differs: {deltaA.X} vs {deltaB.X}");
    }

    private (Vector2 MineVelocity, Vector2 PositionDelta) RunMineVsAsteroid(float mineX)
    {
        var (em, view, system) = Setup();
        AddPlayer(em);

        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(mineX - 30f, 0f)));
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 20f));
        em.AddComponent(asteroid, new Velocity(Vector2.Zero));

        var mine = em.CreateEntity();
        em.AddComponent(mine, new Position(new Vector2(mineX, 0f)));
        em.AddComponent(mine, new EnemyMine(MineSize.Large, Speed: 10f, Angle: 0f));
        em.AddComponent(mine, new Velocity(Vector2.Zero));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        return (em.GetComponent<Velocity>(mine).Value, em.GetComponent<Position>(mine).Value - new Vector2(mineX, 0f));
    }

    [Fact]
    public void AmmoVsMine_PhysicsImpulseAndDamage()
    {
        var (em, view, system) = Setup();

        AddPlayer(em);

        var mine = em.CreateEntity();
        em.AddComponent(mine, new Position(new Vector2(-508f, 0f)));
        em.AddComponent(mine, new EnemyMine(MineSize.Large, Speed: 10f, Angle: 0f));
        em.AddComponent(mine, new Health(2));

        var ammo = em.CreateEntity();
        em.AddComponent(ammo, new Position(new Vector2(-512f, 0f)));
        em.AddComponent(ammo, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f, Damage: 1));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);

        Assert.Contains(commands.Commands, c => c is DestroyEntityCommand dec && dec.Entity == ammo);

        commands.Apply(em);

        Assert.True(em.HasComponent<Velocity>(mine), "Mine should have Velocity after being hit by ammo");
        var mineVel = em.GetComponent<Velocity>(mine);
        Assert.True(mineVel.Value.X < -0.1f, $"Mine should be pushed left (negative X) when hit from left, got {mineVel.Value.X}");
    }

    [Fact]
    public void AmmoVsEnemyShip_SingleHit_DamageApplied()
    {
        var (em, view, system) = Setup();

        AddPlayer(em);

        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(new Vector2(-500f, 0f)));
        em.AddComponent(ship, new EnemyShip(Radius: 18f, Speed: 50f, TurnRate: 1f, DetectionRange: 500f, FiringRange: 300f, TurretFireRate: 2f, TurretAmmoSpeed: 150f, Acceleration: 30f, Damage: 1, GraphicsId: 0));
        em.AddComponent(ship, new Health(10));

        var ammo = em.CreateEntity();
        em.AddComponent(ammo, new Position(new Vector2(-496f, 0f)));
        em.AddComponent(ammo, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f, Damage: 6));

        var commands = new CommandBuffer();

        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.True(em.HasComponent<Health>(ship), "Ship should survive single hit");
        var health = em.GetComponent<Health>(ship);
        Assert.Equal(4, health.Current);
    }

    [Fact]
    public void TwoAmmoVsEnemyShip_BothDamageApplied_ShipDies()
    {
        var (em, view, system) = Setup();

        AddPlayer(em);

        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(new Vector2(-500f, 0f)));
        em.AddComponent(ship, new EnemyShip(Radius: 18f, Speed: 50f, TurnRate: 1f, DetectionRange: 500f, FiringRange: 300f, TurretFireRate: 2f, TurretAmmoSpeed: 150f, Acceleration: 30f, Damage: 1, GraphicsId: 0));
        em.AddComponent(ship, new Health(10));

        var ammo1 = em.CreateEntity();
        em.AddComponent(ammo1, new Position(new Vector2(-496f, 0f)));
        em.AddComponent(ammo1, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f, Damage: 6));

        var ammo2 = em.CreateEntity();
        em.AddComponent(ammo2, new Position(new Vector2(-497f, 0.5f)));
        em.AddComponent(ammo2, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f, Damage: 6));

        var commands = new CommandBuffer();

        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.False(em.HasComponent<Health>(ship), "Ship should be destroyed after taking 12 damage from two projectiles");
    }

    [Fact]
    public void TwoAmmoVsMine_BothDamageApplied_MineDies()
    {
        var (em, view, system) = Setup();

        AddPlayer(em);

        var mine = em.CreateEntity();
        em.AddComponent(mine, new Position(new Vector2(-508f, 0f)));
        em.AddComponent(mine, new EnemyMine(MineSize.Large, Speed: 10f, Angle: 0f));
        em.AddComponent(mine, new Health(10));

        var ammo1 = em.CreateEntity();
        em.AddComponent(ammo1, new Position(new Vector2(-504f, 0f)));
        em.AddComponent(ammo1, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f, Damage: 6));

        var ammo2 = em.CreateEntity();
        em.AddComponent(ammo2, new Position(new Vector2(-505f, 0.5f)));
        em.AddComponent(ammo2, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f, Damage: 6));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);

        commands.Apply(em);

        Assert.False(em.HasComponent<Health>(mine), "Mine should be destroyed after taking 12 damage from two projectiles");
    }

    [Fact]
    public void TwoMinesVsPlayer_BothHitInOneFrame_PlayerTakesDamage()
    {
        var (em, view, system) = Setup();

        AddPlayer(em);

        var mine1 = em.CreateEntity();
        em.AddComponent(mine1, new Position(new Vector2(-15f, 0f)));
        em.AddComponent(mine1, new EnemyMine(MineSize.Large, Speed: 10f, Angle: 0f));

        var mine2 = em.CreateEntity();
        em.AddComponent(mine2, new Position(new Vector2(0f, -15f)));
        em.AddComponent(mine2, new EnemyMine(MineSize.Large, Speed: 10f, Angle: 0f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        Assert.False(em.HasComponent<Health>(mine1), "Mine 1 should be destroyed");
        Assert.False(em.HasComponent<Health>(mine2), "Mine 2 should be destroyed");

        view.GetEntitiesWithComponents<Player, Health>().TryFirst(out var playerTuple);
        var playerHealth = playerTuple.Value2;
        Assert.Equal(4, playerHealth.Current);
    }

    [Fact]
    public void TwoAmmoVsPlayer_BothHitInOneFrame_TotalDamageLessThanHealth()
    {
        var (em, view, system) = Setup();

        AddPlayer(em);

        var ammo1 = em.CreateEntity();
        em.AddComponent(ammo1, new Position(new Vector2(-10f, 0f)));
        em.AddComponent(ammo1, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f, IsEnemy: true, Damage: 3));

        var ammo2 = em.CreateEntity();
        em.AddComponent(ammo2, new Position(new Vector2(0f, -10f)));
        em.AddComponent(ammo2, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f, IsEnemy: true, Damage: 3));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 120f, commands);
        commands.Apply(em);

        view.GetEntitiesWithComponents<Player, Health>().TryFirst(out var playerTuple);
        var playerHealth = playerTuple.Value2;
        Assert.Equal(4, playerHealth.Current);
    }
}
