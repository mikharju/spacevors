using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class TargetingTest
{
    // Turret sits at origin, rotation 0 = facing down (-Y). Targets with negative Y are in front.
    private static (EntityManager em, Entity player, Entity turret) CreatePlayerWorld(float range, float ammoSpeed, float shotLifetime, float arcAngle = MathF.PI / 2f)
    {
        var em = new EntityManager();

        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(Vector2.Zero));
        em.AddComponent(playerEntity, new Velocity(Vector2.Zero));
        em.AddComponent(playerEntity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 5f));

        var turretEntity = em.CreateEntity();
        em.AddComponent(turretEntity, new Position(Vector2.Zero));
        em.AddComponent(turretEntity, new Rotation(0f));
        em.AddComponent(turretEntity, new Turret(
            Weapon: new WeaponStats(FireRate: 5f, AmmoSpeed: ammoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0f, ShotLifetime: shotLifetime),
            WeaponName: "TestWeapon",
            ArcAngle: arcAngle,
            Range: range,
            AutoTarget: true));

        return (em, playerEntity, turretEntity);
    }

    private static Entity AddEnemyShip(EntityManager em, Vector2 pos, Vector2 vel = default)
    {
        var entity = em.CreateEntity();
        em.AddComponent(entity, new Position(pos));
        em.AddComponent(entity, new Velocity(vel));
        em.AddComponent(entity, new Rotation(0f));
        em.AddComponent(entity, new EnemyShip(Radius: 20f, Speed: 65f, TurnRate: 1f, FiringRange: 700f, TurretFireRate: 1.5f, TurretAmmoSpeed: 200f, Acceleration: 45f, Damage: 3, GraphicsId: 0));
        return entity;
    }

    private static Entity AddMine(EntityManager em, Vector2 pos, MineSize size)
    {
        var entity = em.CreateEntity();
        em.AddComponent(entity, new Position(pos));
        em.AddComponent(entity, new Velocity(Vector2.Zero));
        em.AddComponent(entity, new EnemyMine(size, Speed: 10f, Angle: 0f));
        return entity;
    }

    private static int RunFiringPass(EntityManager em)
    {
        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(new WorldView(em), 1f / 60f, commands);
        commands.Apply(em);
        return em.GetEntitiesWithComponents<Ammo>().Count();
    }

    private static Vector2 GetSingleAmmoPosition(EntityManager em)
    {
        var list = em.GetEntitiesWithComponents<Ammo, Position>().ToList();
        Assert.Single(list);
        return list[0].Value2.Value;
    }

    [Fact]
    public void TestTargetedShipBeyondNormalRangeFires()
    {
        // Range 300, reach 500*3=1500 -> targeted gate min(900, 1500) = 900. Enemy at 800 px.
        var (em, player, _) = CreatePlayerWorld(range: 300f, ammoSpeed: 500f, shotLifetime: 3f);
        var enemy = AddEnemyShip(em, new Vector2(0f, -800f));

        Assert.True(RunFiringPass(em) == 0, "Beyond normal range the turret must not fire without a target");

        em.AddComponent(player, new PrimaryTarget(enemy));
        Assert.True(RunFiringPass(em) > 0, "Expected turret to fire at targeted ship beyond normal range");
    }

    [Fact]
    public void TestTargetedRangeCappedByAmmoReach()
    {
        // Range 1000 -> 3x = 3000, but reach is only 200*3=600. Enemy at 1500 px: beyond normal range (so auto-targeting won't fire) and unreachable by ammo.
        var (em, player, _) = CreatePlayerWorld(range: 1000f, ammoSpeed: 200f, shotLifetime: 3f);
        var enemy = AddEnemyShip(em, new Vector2(0f, -1500f));

        em.AddComponent(player, new PrimaryTarget(enemy));
        Assert.True(RunFiringPass(em) == 0, "Turret must not fire at a target beyond physical ammo reach");
    }

    [Fact]
    public void TestTargetedShipTakesPriorityOverCloserEnemy()
    {
        // Locked ship in targeted range (806 px < 900) beats a closer auto-target (224 px < 300).
        var (em, player, _) = CreatePlayerWorld(range: 300f, ammoSpeed: 500f, shotLifetime: 3f);
        var locked = AddEnemyShip(em, new Vector2(-100f, -800f));
        AddEnemyShip(em, new Vector2(100f, -200f));

        em.AddComponent(player, new PrimaryTarget(locked));
        Assert.True(RunFiringPass(em) > 0);

        // Ammo spawns 20 px along the aim direction: locked ship is on the -X side.
        var ammoPos = GetSingleAmmoPosition(em);
        Assert.True(ammoPos.X < 0f, $"Ammo aimed at {ammoPos}, expected toward locked ship (-X side)");
    }

    [Fact]
    public void TestFallsBackToAutoWhenTargetOutOfRange()
    {
        // Locked ship beyond targeted gate (1965 px > 900); closer enemy in normal range.
        var (em, player, _) = CreatePlayerWorld(range: 300f, ammoSpeed: 500f, shotLifetime: 3f);
        var locked = AddEnemyShip(em, new Vector2(500f, -1900f));
        AddEnemyShip(em, new Vector2(-100f, -200f));

        em.AddComponent(player, new PrimaryTarget(locked));
        Assert.True(RunFiringPass(em) > 0, "Expected fallback fire at the closer enemy");

        var ammoPos = GetSingleAmmoPosition(em);
        Assert.True(ammoPos.X < 0f, $"Ammo aimed at {ammoPos}, expected toward fallback enemy (-X side)");
    }

    [Fact]
    public void TestFallsBackToAutoWhenTargetOutsideArc()
    {
        // Narrow 45 deg arc. Locked ship in range (894 px < 900) but 26.6 deg off axis (> 22.5 half-arc).
        var (em, player, _) = CreatePlayerWorld(range: 300f, ammoSpeed: 500f, shotLifetime: 3f, arcAngle: MathF.PI / 4f);
        var locked = AddEnemyShip(em, new Vector2(400f, -800f));
        AddEnemyShip(em, new Vector2(-50f, -200f));

        em.AddComponent(player, new PrimaryTarget(locked));
        Assert.True(RunFiringPass(em) > 0, "Expected fallback fire at the in-arc enemy");

        var ammoPos = GetSingleAmmoPosition(em);
        Assert.True(ammoPos.X < 0f, $"Ammo aimed at {ammoPos}, expected toward fallback enemy (-X side)");
    }

    [Fact]
    public void TestDeadTargetIsClearedAndFallsBack()
    {
        var (em, player, _) = CreatePlayerWorld(range: 300f, ammoSpeed: 500f, shotLifetime: 3f);
        var enemy = AddEnemyShip(em, new Vector2(0f, -800f));
        em.AddComponent(player, new PrimaryTarget(enemy));
        em.AddComponent(enemy, new Dead());

        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(new WorldView(em), 1f / 60f, commands);

        Assert.True(commands.Commands.OfType<RemoveComponentCommand<PrimaryTarget>>().Any(), "Expected PrimaryTarget removal command for dead target");
        Assert.True(em.GetEntitiesWithComponents<Ammo>().Count() == 0, "Must not fire at a dead target with no fallback enemy");

        commands.Apply(em);
        Assert.False(em.HasComponent<PrimaryTarget>(player), "PrimaryTarget must be cleared after apply");
    }

    [Fact]
    public void TestTargetedMineBeyondNormalRangeFires()
    {
        var (em, player, _) = CreatePlayerWorld(range: 300f, ammoSpeed: 500f, shotLifetime: 3f);
        var mine = AddMine(em, new Vector2(0f, -800f), MineSize.Small);

        Assert.Equal(0, RunFiringPass(em));

        em.AddComponent(player, new PrimaryTarget(mine));
        Assert.True(RunFiringPass(em) > 0, "Expected turret to fire at targeted mine beyond normal range");
    }

    [Fact]
    public void TestPickerSmallShipForgivingZone()
    {
        var em = new EntityManager();
        AddEnemyShip(em, Vector2.Zero); // radius 20 -> zone 30

        Assert.NotNull(PrimaryTargetPicker.Pick(em, new Vector2(28f, 0f)));
        Assert.Null(PrimaryTargetPicker.Pick(em, new Vector2(40f, 0f)));
    }

    [Fact]
    public void TestPickerLargeShipTightZone()
    {
        var em = new EntityManager();
        var big = em.CreateEntity();
        em.AddComponent(big, new Position(Vector2.Zero));
        em.AddComponent(big, new EnemyShip(Radius: 78f, Speed: 50f, TurnRate: 1f, FiringRange: 700f, TurretFireRate: 0.8f, TurretAmmoSpeed: 160f, Acceleration: 45f, Damage: 3, GraphicsId: 2));

        Assert.NotNull(PrimaryTargetPicker.Pick(em, new Vector2(84f, 0f))); // <= 78*1.1 = 85.8
        Assert.Null(PrimaryTargetPicker.Pick(em, new Vector2(90f, 0f)));
    }

    [Fact]
    public void TestPickerMinesForgivingZones()
    {
        var emSmall = new EntityManager();
        AddMine(emSmall, Vector2.Zero, MineSize.Small); // radius 7.5 -> zone 30
        Assert.NotNull(PrimaryTargetPicker.Pick(emSmall, new Vector2(28f, 0f)));

        var emLarge = new EntityManager();
        AddMine(emLarge, Vector2.Zero, MineSize.Large); // radius 15 -> zone 30
        Assert.NotNull(PrimaryTargetPicker.Pick(emLarge, new Vector2(28f, 0f)));
        Assert.Null(PrimaryTargetPicker.Pick(emLarge, new Vector2(40f, 0f)));
    }

    [Fact]
    public void TestPickerChoosesClosestOfMultiple()
    {
        var em = new EntityManager();
        var ship = AddEnemyShip(em, Vector2.Zero);          // zone 30
        var mine = AddMine(em, new Vector2(20f, 0f), MineSize.Small); // zone 30

        Assert.Equal(ship, PrimaryTargetPicker.Pick(em, new Vector2(8f, 0f)));   // 8 px vs 12 px
        Assert.Equal(mine, PrimaryTargetPicker.Pick(em, new Vector2(14f, 0f)));  // 14 px vs 6 px
    }

    [Fact]
    public void TestPickerIgnoresDeadShips()
    {
        var em = new EntityManager();
        var ship = AddEnemyShip(em, Vector2.Zero);
        em.AddComponent(ship, new Dead());

        Assert.Null(PrimaryTargetPicker.Pick(em, new Vector2(5f, 0f)));
    }
}
