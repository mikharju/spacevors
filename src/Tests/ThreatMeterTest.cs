using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// What the `[THREAT]` line says once a second (plans/DIFFICULTY_SCALING.md R2.8). The difficulty knobs
// are tuned against this row, so it has to be honest about who can hit the hull right now.
public class ThreatMeterTest
{
    [Fact]
    public void Measure_CountsOnlyShipsStandingInsideTheirOwnGunRange()
    {
        var em = new EntityManager();
        AddPlayer(em, radius: 50f, current: 6, max: 10, level: 4);

        // The ring is gun range + hull radius (700 + 50): the ship at 700 px stands in it, the one at
        // 900 px does not, even though both would be hit by a clicked target.
        AddEnemyShip(em, new Vector2(700f, 0f), range: 700f, fireRate: 2f, damage: 3);
        AddEnemyShip(em, new Vector2(-900f, 0f), range: 700f, fireRate: 5f, damage: 6);

        var s = ThreatMeter.Measure(new WorldView(em));

        Assert.Equal(2, s.Ships);
        Assert.Equal(1, s.ShipsInRing);
        Assert.Equal(6f, s.IncomingDps, precision: 3); // 2/s × 1 pellet × 3 damage, far gun excluded
        Assert.Equal(6, s.Health);
        Assert.Equal(10, s.MaxHealth);
        Assert.Equal(4, s.Level);
    }

    [Fact]
    public void Measure_AWreckWithItsGunStillOnItIsNotThreat()
    {
        var em = new EntityManager();
        AddPlayer(em, radius: 50f, current: 10, max: 10, level: 1);
        var wreck = AddEnemyShip(em, new Vector2(300f, 0f), range: 700f, fireRate: 2f, damage: 3);
        em.AddComponent(wreck, new Dead());

        var s = ThreatMeter.Measure(new WorldView(em));

        Assert.Equal(0, s.Ships);
        Assert.Equal(0f, s.IncomingDps, precision: 3);
    }

    [Fact]
    public void Measure_ReadsRoundsMountsMinesAndSmallRocks()
    {
        var em = new EntityManager();
        AddPlayer(em, radius: 50f, current: 8, max: 8, level: 2);

        AddAmmo(em, isEnemy: true);
        AddAmmo(em, isEnemy: true);
        AddAmmo(em, isEnemy: false); // the player's own shots are not incoming fire

        AddMountedGun(em, fireRate: 4f, pellets: 3, damage: 1); // 4/s × 3 × 1
        AddEnemyShip(em, new Vector2(100f, 0f), range: 700f, fireRate: 2f, damage: 3);

        AddMine(em);
        AddMine(em);
        em.AddComponent(AddMine(em), new Dead());

        AddRock(em, isSmall: true);
        AddRock(em, isSmall: true);
        AddRock(em, isSmall: false); // the large ones are scenery, not a resource being chewed through

        var s = ThreatMeter.Measure(new WorldView(em));

        Assert.Equal(2, s.EnemyRounds);
        Assert.Equal(12f, s.PlayerDps, precision: 3);
        Assert.Equal(2, s.Mines);
        Assert.Equal(2, s.SmallRocks);
        Assert.Equal(0, s.Tier); // a fresh world has not reached the first tier
    }

    [Fact]
    public void Measure_WithoutAPlayerSaysNothing()
    {
        var em = new EntityManager();
        AddEnemyShip(em, new Vector2(100f, 0f), range: 700f, fireRate: 2f, damage: 3);

        var s = ThreatMeter.Measure(new WorldView(em));

        Assert.Equal(0, s.Ships);
        Assert.Equal(0f, s.IncomingDps, precision: 3);
    }

    private static void AddPlayer(EntityManager em, float radius, int current, int max, int level)
    {
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 400f, SideThrust: 80f, BackThrust: 80f, Boost: 2.5f,
            MaxHealth: max, Radius: radius, Xp: 0, Level: level, PickupRadius: 60f, RotationSpeed: 1.33f));
        em.AddComponent(player, new Health(current, max));
    }

    private static Entity AddEnemyShip(EntityManager em, Vector2 pos, float range, float fireRate, int damage)
    {
        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(pos));
        em.AddComponent(ship, new Velocity(Vector2.Zero));
        em.AddComponent(ship, new Rotation(0f));
        em.AddComponent(ship, new EnemyShip(Radius: 20f, Speed: 65f, TurnRate: 1f, FiringRange: range,
            TurretFireRate: fireRate, TurretAmmoSpeed: 200f, Acceleration: 45f, GraphicsId: 0));
        em.AddComponent(ship, new Turret(
            Weapon: new WeaponStats(FireRate: fireRate, AmmoSpeed: 200f, KickbackForce: 0f, PelletCount: 1, Scatter: 0f, Damage: damage),
            WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: range, IsEnemy: true));
        return ship;
    }

    private static void AddMountedGun(EntityManager em, float fireRate, int pellets, int damage)
    {
        var mount = em.CreateEntity();
        em.AddComponent(mount, new TurretSlot(new TurretSlotDefinition("test_gun", TurretSize.Small,
            Vector2.Zero, ArcCenter: 0f, ArcAngle: MathF.PI)));
        em.AddComponent(mount, new Turret(
            Weapon: new WeaponStats(FireRate: fireRate, AmmoSpeed: 600f, KickbackForce: 0f, PelletCount: pellets, Scatter: 0f, Damage: damage),
            WeaponName: "TestGun", ArcAngle: MathF.PI / 2f, Range: 1000f));
    }

    private static void AddAmmo(EntityManager em, bool isEnemy)
    {
        var bullet = em.CreateEntity();
        em.AddComponent(bullet, new Position(new Vector2(40f, 0f)));
        em.AddComponent(bullet, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 2.5f, Lifetime: 3f, IsEnemy: isEnemy));
    }

    private static Entity AddMine(EntityManager em)
    {
        var mine = em.CreateEntity();
        em.AddComponent(mine, new Position(new Vector2(0f, 300f)));
        em.AddComponent(mine, new EnemyMine(MineSize.Small, Speed: 10f, Angle: 0f));
        return mine;
    }

    private static void AddRock(EntityManager em, bool isSmall)
    {
        var rock = em.CreateEntity();
        em.AddComponent(rock, new Position(new Vector2(400f, 0f)));
        em.AddComponent(rock, new Asteroid(isSmall, Radius: isSmall ? 40f : 200f));
    }
}
