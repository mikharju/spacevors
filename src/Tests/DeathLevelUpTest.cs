using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class DeathLevelUpTest
{
    private const int XpThresholdForLevel1 = 10;

    [Fact]
    public void LevelUpSkipsDeadPlayer()
    {
        var (em, player) = CreatePlayerWithTurret(xp: XpThresholdForLevel1 + 5, dead: true);

        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new LevelUpSystem().Update(view, 1f / 60f, commands);
        commands.Apply(em);

        Assert.Equal(0, em.GetEntitiesWithComponents<PendingChoice>().Count());
        Assert.Equal(1, em.GetComponent<Player>(player).Level);
    }

    [Fact]
    public void LevelUpTriggersForAlivePlayer()
    {
        var (em, player) = CreatePlayerWithTurret(xp: XpThresholdForLevel1 + 5, dead: false);

        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new LevelUpSystem().Update(view, 1f / 60f, commands);
        commands.Apply(em);

        Assert.Equal(1, em.GetEntitiesWithComponents<PendingChoice>().Count());
        Assert.Equal(2, em.GetComponent<Player>(player).Level);
    }

    [Fact]
    public void PlayerTurretDoesNotFireWhenPlayerDead()
    {
        var (em, player) = CreatePlayerWithTurret(xp: 0, dead: true);

        // Asteroid directly in front of the turret (rotation 0 points -Y): would be targeted if not skipped.
        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(0f, -100f)));
        em.AddComponent(asteroid, new Velocity(Vector2.Zero));
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 30f));

        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(view, 1f / 60f, commands);
        commands.Apply(em);

        Assert.Equal(0, em.GetEntitiesWithComponents<Ammo>().Count());
    }

    private static (EntityManager Em, Entity Player) CreatePlayerWithTurret(int xp, bool dead)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(0f, 0f)));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, Xp: xp, Level: 1, PickupRadius: 60f, RotationSpeed: 5f));
        if (dead) em.AddComponent(player, new Dead());

        var turret = em.CreateEntity();
        em.AddComponent(turret, new Position(new Vector2(0f, 0f)));
        em.AddComponent(turret, new Rotation(0f));
        em.AddComponent(turret, new Turret(
            Weapon: new WeaponStats(FireRate: 5f, AmmoSpeed: 500f, KickbackForce: 10f, PelletCount: 1, Scatter: 0.05f, ShotLifetime: 2f, Damage: 1),
            WeaponName: "TestWeapon",
            ArcAngle: MathF.PI / 4f,
            Range: 300f));

        return (em, player);
    }
}
