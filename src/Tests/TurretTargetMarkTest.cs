using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class TurretTargetMarkTest
{
    // Runs one firing-system tick and applies its commands.
    private static void RunTick(EntityManager em)
    {
        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(new WorldView(em), 1f / 60f, commands);
        commands.Apply(em);
    }

    [Fact]
    public void ShipInRangeGetsMarkRefreshedEveryTick()
    {
        var (em, _, _) = TargetingTest.CreatePlayerWorld(range: 300f, ammoSpeed: 500f, shotLifetime: 3f);
        var enemy = TargetingTest.AddEnemyShip(em, new Vector2(0f, -200f));

        em.AddElapsedTime(1f);
        RunTick(em); // fires (cooldown was empty) and marks the ship
        Assert.True(em.TryGetComponent<AutoTargetMark>(enemy, out var mark), "Expected auto-target mark on in-range ship");
        Assert.Equal(1f, mark.LastTargetedAt);

        em.AddElapsedTime(1f);
        RunTick(em); // still on cooldown from the first shot: no fire, but the mark must refresh
        Assert.Equal(2f, em.GetComponent<AutoTargetMark>(enemy).LastTargetedAt);
    }

    [Fact]
    public void ShipOutsideArcOrRangeGetsNoMark()
    {
        var (em, _, _) = TargetingTest.CreatePlayerWorld(range: 300f, ammoSpeed: 500f, shotLifetime: 3f);
        var behind = TargetingTest.AddEnemyShip(em, new Vector2(0f, 200f)); // behind the turret (forward is -Y)
        var far = TargetingTest.AddEnemyShip(em, new Vector2(0f, -400f));   // beyond range 300

        RunTick(em);

        Assert.False(em.HasComponent<AutoTargetMark>(behind), "Behind-arc ship must not be marked");
        Assert.False(em.HasComponent<AutoTargetMark>(far), "Out-of-range ship must not be marked");
    }

    [Fact]
    public void MineInRangeGetsMark()
    {
        var (em, _, _) = TargetingTest.CreatePlayerWorld(range: 300f, ammoSpeed: 500f, shotLifetime: 3f);
        var mine = TargetingTest.AddMine(em, new Vector2(0f, -100f), MineSize.Small);

        RunTick(em);

        Assert.True(em.HasComponent<AutoTargetMark>(mine), "Expected auto-target mark on in-range mine");
    }

    [Fact]
    public void AsteroidSelectedAsLastResortIsNeverMarked()
    {
        var (em, _, _) = TargetingTest.CreatePlayerWorld(range: 300f, ammoSpeed: 500f, shotLifetime: 3f);
        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(0f, -150f)));
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 20f));

        RunTick(em);

        Assert.True(em.GetEntitiesWithComponents<Ammo>().Any(), "Turret should fire at the asteroid as last resort");
        Assert.False(em.HasComponent<AutoTargetMark>(asteroid), "Asteroids must never be marked");
    }

    [Fact]
    public void EnemyTurretFiringAtPlayerMarksNothing()
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 5f));

        // Enemy ship below the player, facing up (+Y) so its narrow arc covers the player.
        var enemy = em.CreateEntity();
        em.AddComponent(enemy, new Position(new Vector2(0f, -300f)));
        em.AddComponent(enemy, new Velocity(Vector2.Zero));
        em.AddComponent(enemy, new Rotation(MathF.PI));
        em.AddComponent(enemy, new EnemyShip(Radius: 20f, Speed: 65f, TurnRate: 1f, FiringRange: 700f, TurretFireRate: 1.5f, TurretAmmoSpeed: 200f, Acceleration: 45f, GraphicsId: 0));
        em.AddComponent(enemy, new Turret(Weapon: new WeaponStats(FireRate: 5f, AmmoSpeed: 200f, KickbackForce: 0f, PelletCount: 1, Scatter: 0f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: 700f, IsEnemy: true));

        RunTick(em);

        Assert.True(em.GetEntitiesWithComponents<Ammo>().Any(), "Expected enemy turret to fire at the player");
        Assert.Equal(0, em.GetEntitiesWithComponents<AutoTargetMark>().Count());
    }

    [Fact]
    public void DeadPlayerGetsNoMarks()
    {
        var (em, player, _) = TargetingTest.CreatePlayerWorld(range: 300f, ammoSpeed: 500f, shotLifetime: 3f);
        em.AddComponent(player, new Dead());
        var enemy = TargetingTest.AddEnemyShip(em, new Vector2(0f, -200f));

        RunTick(em);

        Assert.False(em.HasComponent<AutoTargetMark>(enemy), "No marks while the player is dead");
    }
}
