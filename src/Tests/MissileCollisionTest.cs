using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

// R19: a guided round is a body, not a bolt of luck. The other side's fire shoots it down, its own
// side's fire walks straight through it, and what is left of it is smoke rather than a detonation.
public class MissileCollisionTests
{
    private static (EntityManager em, WorldView view, CollisionSystem system) Setup()
    {
        var em = new EntityManager();
        return (em, new WorldView(em), new CollisionSystem());
    }

    // A launched missile: the same components TurretFiringSystem puts on a guided round, including the
    // hit points that make it shootable.
    private static Entity AddMissile(EntityManager em, Vector2 position, Vector2 velocity, bool isEnemy, int health)
    {
        var target = em.CreateEntity();
        var missile = em.CreateEntity();
        em.AddComponent(missile, new Position(position));
        em.AddComponent(missile, new Velocity(velocity));
        em.AddComponent(missile, new Ammo(velocity, Radius: 4f, Lifetime: 10f, IsEnemy: isEnemy, Damage: 3));
        em.AddComponent(missile, new Missile(target, Speed: velocity.Magnitude, MaxSpeed: velocity.Magnitude, Acceleration: WeaponStats.MissileLaunchAcceleration, TurnRate: 3f, Steering: 0f));
        em.AddComponent(missile, new Health(health, health));
        return missile;
    }

    private static Entity AddBullet(EntityManager em, Vector2 position, Vector2 velocity, bool isEnemy, int damage)
    {
        var bullet = em.CreateEntity();
        em.AddComponent(bullet, new Position(position));
        em.AddComponent(bullet, new Ammo(velocity, Radius: 3f, Lifetime: 10f, IsEnemy: isEnemy, Damage: damage));
        return bullet;
    }

    private static bool WasDestroyed(CommandBuffer commands, Entity entity) =>
        commands.Commands.Any(c => c is DestroyEntityCommand destroy && destroy.Entity == entity);

    [Fact]
    public void EnemyFire_ShootsDownAPlayersMissile()
    {
        var (em, view, system) = Setup();
        var missile = AddMissile(em, new Vector2(0f, 0f), new Vector2(0f, -100f), isEnemy: false, health: 1);
        var bullet = AddBullet(em, new Vector2(2f, 0f), new Vector2(400f, 0f), isEnemy: true, damage: 1);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 60f, commands);

        Assert.True(WasDestroyed(commands, missile), "the missile should die to the enemy's bullet");
        Assert.True(WasDestroyed(commands, bullet), "the bullet that shot it down should be spent");
    }

    [Fact]
    public void PlayersFire_ShootsDownAnEnemyMissile()
    {
        var (em, view, system) = Setup();
        var missile = AddMissile(em, new Vector2(0f, 0f), new Vector2(0f, -100f), isEnemy: true, health: 1);
        var bullet = AddBullet(em, new Vector2(2f, 0f), new Vector2(400f, 0f), isEnemy: false, damage: 1);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 60f, commands);

        Assert.True(WasDestroyed(commands, missile), "the enemy's missile should die to the player's bullet");
        Assert.True(WasDestroyed(commands, bullet), "the bullet that shot it down should be spent");
    }

    [Fact]
    public void AMissileNeverEatsItsOwnSidesFire()
    {
        var (em, view, system) = Setup();
        var missile = AddMissile(em, new Vector2(0f, 0f), new Vector2(0f, -100f), isEnemy: false, health: 1);
        var bullet = AddBullet(em, new Vector2(2f, 0f), new Vector2(400f, 0f), isEnemy: false, damage: 1);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 60f, commands);

        Assert.False(WasDestroyed(commands, missile), "the player's own bullet must not shoot the player's missile down");
        Assert.False(WasDestroyed(commands, bullet), "and the bullet carries on to whatever is behind it");
        Assert.DoesNotContain(commands.Commands, c => c is AddComponentCommand<Health>);
    }

    [Fact]
    public void ARoundWithHealthLeft_KeepsFlyingWithTheHealthItEarned()
    {
        var (em, view, system) = Setup();
        var missile = AddMissile(em, new Vector2(0f, 0f), new Vector2(0f, -100f), isEnemy: true, health: 3);
        var bullet = AddBullet(em, new Vector2(2f, 0f), new Vector2(400f, 0f), isEnemy: false, damage: 1);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 60f, commands);
        // Applying the buffer empties it, so the commands have to be read before the state is.
        bool bulletSpent = WasDestroyed(commands, bullet);
        bool missileDestroyed = WasDestroyed(commands, missile);
        commands.Apply(em);

        Assert.False(missileDestroyed, "a three hit round survives a single bullet");
        Assert.True(bulletSpent, "but the bullet is still spent on it");
        var health = em.GetComponent<Health>(missile);
        Assert.Equal(2, health.Current);
        Assert.Equal(3, health.Max);
    }

    [Fact]
    public void APlayersBeam_StopsAtAnEnemyMissile()
    {
        var (em, view, system) = Setup();
        var missile = AddMissile(em, new Vector2(0f, 0f), new Vector2(0f, -100f), isEnemy: true, health: 3);

        var beam = em.CreateEntity();
        em.AddComponent(beam, new LaserBeam(new Vector2(-100f, 0f), new Vector2(1f, 0f), Length: 200f, Width: 3f,
            Damage: 2, IsEnemy: false, AmmoColor.Blue, Lifetime: 0.2f, InitialLifetime: 0.2f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 60f, commands);
        commands.Apply(em);

        var health = em.GetComponent<Health>(missile);
        Assert.Equal(1, health.Current);
    }

    [Fact]
    public void APlayersBeam_PassesThroughItsOwnMissile()
    {
        var (em, view, system) = Setup();
        var missile = AddMissile(em, new Vector2(0f, 0f), new Vector2(0f, -100f), isEnemy: false, health: 3);

        var beam = em.CreateEntity();
        em.AddComponent(beam, new LaserBeam(new Vector2(-100f, 0f), new Vector2(1f, 0f), Length: 200f, Width: 3f,
            Damage: 2, IsEnemy: false, AmmoColor.Blue, Lifetime: 0.2f, InitialLifetime: 0.2f));

        var commands = new CommandBuffer();
        system.Update(view, 1 / 60f, commands);
        commands.Apply(em);

        Assert.Equal(3, em.GetComponent<Health>(missile).Current);
    }

    // A hull as the thing a missile is covering: the same components the spawner puts on one, with the
    // swarmer's disc shape so its collision radius is the radius the grid indexed it by.
    private static Entity AddShip(EntityManager em, Vector2 position, int health)
    {
        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(position));
        em.AddComponent(ship, new Rotation(0f));
        em.AddComponent(ship, new EnemyShip(Radius: 20f, Speed: 0f, TurnRate: 0f, FiringRange: 0f,
            TurretFireRate: 0f, TurretAmmoSpeed: 0f, Acceleration: 0f, GraphicsId: 0));
        em.AddComponent(ship, new Health(health, health));
        return ship;
    }

    // A round is spent on the earliest thing its swept capsule met (ARCHITECTURE.md: "a swept round
    // takes the earliest contact along its path"). A missile hanging in front of the hull it was
    // shielding is exactly that first thing, so the hull behind it must not also take the blow.
    [Fact]
    public void ARoundSpentOnAMissile_DoesNotPunchThroughToTheHullBehindIt()
    {
        var (em, view, system) = Setup();
        var ship = AddShip(em, new Vector2(0f, -300f), health: 10);
        var missile = AddMissile(em, new Vector2(0f, -280f), new Vector2(0f, -100f), isEnemy: true, health: 5);

        // 1800 px/s over this dt is a 30 px capsule: it meets the missile a third of the way along its
        // path and the hull only at the very end of it.
        var bullet = AddBullet(em, new Vector2(6f, -300f), new Vector2(0f, -1800f), isEnemy: false, damage: 3);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 60f, commands);
        bool bulletSpent = WasDestroyed(commands, bullet);
        commands.Apply(em);

        Assert.Equal(2, em.GetComponent<Health>(missile).Current);
        Assert.Equal(10, em.GetComponent<Health>(ship).Current);
        Assert.True(bulletSpent, "the round is spent on the missile, not shared with the hull behind it");
    }

    // The same rule on the player's side of the seam: an enemy round that a rock stopped this tick never
    // reaches the hull behind that rock, because the round's path ended at the rock.
    [Fact]
    public void ARoundStoppedByARock_DoesNotReachThePlayerBehindIt()
    {
        var (em, view, system) = Setup();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(0f, -320f)));
        em.AddComponent(player, new Rotation(0f));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f,
            MaxHealth: 100, Radius: 20f, Xp: 0, Level: 1, PickupRadius: 120f, RotationSpeed: 1.33f));
        em.AddComponent(player, new Health(100, 100));

        var rock = em.CreateEntity();
        em.AddComponent(rock, new Position(new Vector2(0f, -290f)));
        em.AddComponent(rock, new Asteroid(IsSmall: true, Radius: 12f));
        em.AddComponent(rock, new Health(50, 50));

        var round = AddMissile(em, new Vector2(8f, -300f), new Vector2(0f, -1800f), isEnemy: true, health: 3);

        var commands = new CommandBuffer();
        system.Update(view, 1 / 60f, commands);
        bool roundSpent = WasDestroyed(commands, round);
        commands.Apply(em);

        Assert.Equal(47, em.GetComponent<Health>(rock).Current);
        Assert.Equal(100, em.GetComponent<Health>(player).Current);
        Assert.True(roundSpent, "the round is spent on the rock, not carried on to the player");
    }
}
