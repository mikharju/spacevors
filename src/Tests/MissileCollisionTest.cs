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
        em.AddComponent(missile, new Missile(target, Speed: velocity.Magnitude, TurnRate: 3f, Steering: 0f));
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
}
