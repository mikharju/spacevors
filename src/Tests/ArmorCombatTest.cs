using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Armor and armor piercing (plans/ARMOR_AND_PIERCE.md): gunfire is reduced by the target's Armor
// with the round's Pierce cancelling it first, floored at 1. Only hulls are armored; impact and
// mine contact bypass it; enemy rounds never carry pierce; HeavyCannon is the armored hull and the
// fleet's heavy slug — the one enemy round meant to walk through player armor.
public class ArmorCombatTest
{
    private const float Dt = 1f / 120f;

    // ---- the rule itself ----

    [Fact]
    public void ArmorSubtractsPerHit_FlooredAtOne()
    {
        Assert.Equal(1, ArmorRules.EffectiveDamage(damage: 4, armor: 4, pierce: 0));
        Assert.Equal(8, ArmorRules.EffectiveDamage(damage: 12, armor: 4, pierce: 0));
        Assert.Equal(56, ArmorRules.EffectiveDamage(damage: 60, armor: 4, pierce: 0));
        Assert.Equal(1, ArmorRules.EffectiveDamage(damage: 4, armor: ArmorRules.ArmorCeiling, pierce: 0));
    }

    [Fact]
    public void PierceCancelsArmorBeforeItSubtracts_NeverAddingDamage()
    {
        Assert.Equal(4, ArmorRules.EffectiveDamage(damage: 4, armor: 4, pierce: 4));
        Assert.Equal(12, ArmorRules.EffectiveDamage(damage: 12, armor: 4, pierce: 4));
        // Pierce against an unarmored hull is worth nothing — it never inflates the hit.
        Assert.Equal(4, ArmorRules.EffectiveDamage(damage: 4, armor: 0, pierce: 8));
    }

    // ---- hits through the collision system ----

    [Fact]
    public void AChipAgainstAnArmoredHullFloorsToOne()
    {
        var (em, player) = SetupWorld();
        var ship = AddShip(em, armor: 4);
        AddEnemyAmmoAt(em, damage: 4);

        Step(em, player);

        Assert.Equal(119, em.GetComponent<Health>(ship).Current);
    }

    [Fact]
    public void APiercedRoundCancelsTheArmorItHits()
    {
        var (em, player) = SetupWorld();
        var ship = AddShip(em, armor: 4);
        AddEnemyAmmoAt(em, damage: 4, pierce: 4);

        Step(em, player);

        Assert.Equal(116, em.GetComponent<Health>(ship).Current);
    }

    [Fact]
    public void AnUnarmoredHullTakesFullDamage()
    {
        var (em, player) = SetupWorld();
        var ship = AddShip(em, armor: 0);
        AddEnemyAmmoAt(em, damage: 4);

        Step(em, player);

        Assert.Equal(116, em.GetComponent<Health>(ship).Current);
    }

    [Fact]
    public void AnArmoredPlayerFloorsEnemyChips_AndPiercedEnemyFireStillWalksThrough()
    {
        var (em, player) = SetupWorld();
        em.AddComponent(player, new Armor(4));
        AddPlayerAmmoAt(em, damage: 4);

        Step(em, player);

        Assert.Equal(79, em.GetComponent<Health>(player).Current);
    }

    [Fact]
    public void ALaserBeamIsMitigatedTheSameWayAsAMovingRound()
    {
        var (em, player) = SetupWorld();
        var ship = AddShip(em, armor: 4, position: new Vector2(50f, 0f));

        var beam = em.CreateEntity();
        em.AddComponent(beam, new Position(new Vector2(-100f, 0f)));
        em.AddComponent(beam, new LaserBeam(
            new Vector2(-100f, 0f), new Vector2(1f, 0f), Length: 200f, Width: 3f,
            Damage: 16, IsEnemy: false, AmmoColor.Blue, LaserBeam.FlashLifetime, LaserBeam.FlashLifetime));

        Step(em, player);

        Assert.Equal(108, em.GetComponent<Health>(ship).Current);
    }

    // ---- rounds carry the gun's pierce ----

    [Fact]
    public void APiercedGunLandsItsPierceOnEveryRoundItFires()
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 40));

        var turret = em.CreateEntity();
        em.AddComponent(turret, new Position(Vector2.Zero));
        em.AddComponent(turret, new Rotation(0f));
        em.AddComponent(turret, new Turret(
            Weapon: new WeaponStats(1f, 600f, 0f, 1, Scatter: 0f, Damage: 12, Pierce: 4),
            WeaponName: "MachineGun",
            ArcAngle: MathF.PI / 4f,
            Range: 1200f));

        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(0f, -500f)));
        em.AddComponent(asteroid, new Velocity(Vector2.Zero));
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 30f));
        em.AddComponent(player, new PrimaryTarget(asteroid));

        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(new WorldView(em), Dt, commands);
        commands.Apply(em);

        int rounds = 0, pierce = -1;
        foreach (var (_, ammo) in em.GetEntitiesWithComponents<Ammo>()) { rounds++; pierce = ammo.Pierce; }
        Assert.Equal(1, rounds);
        Assert.Equal(4, pierce);
    }

    [Fact]
    public void APiercedHitscanGunCarriesItsPierceOnTheBeam()
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 40));

        var turret = em.CreateEntity();
        em.AddComponent(turret, new Position(Vector2.Zero));
        em.AddComponent(turret, new Rotation(0f));
        em.AddComponent(turret, new Turret(
            Weapon: new WeaponStats(1f, 0f, 0f, 1, Scatter: 0f, Damage: 16, Pierce: 4, Hitscan: true),
            WeaponName: "LaserLance",
            ArcAngle: MathF.PI / 4f,
            Range: 1200f));

        var asteroid = em.CreateEntity();
        em.AddComponent(asteroid, new Position(new Vector2(0f, -500f)));
        em.AddComponent(asteroid, new Velocity(Vector2.Zero));
        em.AddComponent(asteroid, new Asteroid(IsSmall: false, Radius: 30f));
        em.AddComponent(player, new PrimaryTarget(asteroid));

        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(new WorldView(em), Dt, commands);
        commands.Apply(em);

        int beams = 0, pierce = -1;
        foreach (var (_, beam) in em.GetEntitiesWithComponents<LaserBeam>()) { beams++; pierce = beam.Pierce; }
        Assert.Equal(1, beams);
        Assert.Equal(4, pierce);
    }

    // ---- the hull tables ----

    [Fact]
    public void OnlyTheSlowHullsStartArmored()
    {
        foreach (var hull in ShipType.All)
        {
            bool slowEngine = hull.Engine == EngineLayout.Slow;
            Assert.True(hull.Armor == (slowEngine ? 4 : 0),
                $"{hull.Name} ({hull.Engine.Name} engines) starts with armor {hull.Armor}; only the Slow hulls may");
        }

        Assert.Equal(4, ShipType.Heavy.Armor);
        Assert.Equal(4, ShipType.Eagle.Armor);
    }

    [Fact]
    public void WorldFactoryArmsTheHullTable_HeavyYesScoutNo()
    {
        var (heavyEm, heavyPlayer, _) = WorldFactory.Create(ShipType.Heavy, new Vector2(1920f, 1080f));
        Assert.Equal(4, heavyEm.GetComponent<Armor>(heavyPlayer).Value);

        var (scoutEm, scoutPlayer, _) = WorldFactory.Create(ShipType.Scout, new Vector2(1920f, 1080f));
        Assert.False(scoutEm.HasComponent<Armor>(scoutPlayer));
    }

    [Fact]
    public void HeavyCannonSpawnsArmored_AndNoOtherHullDoes()
    {
        foreach (var type in EnemyShipType.All)
        {
            var em = new EntityManager();
            var ship = em.CreateEntity();
            foreach (var component in EnemyShipFactory.CreateComponents(Vector2.Zero, Vector2.Zero, 0f, 0f, type, EnemyArmament.Guns, 0f))
                component.Apply(em, ship);

            Assert.Equal(type.Armor > 0, em.HasComponent<Armor>(ship));
            if (type.Armor > 0) Assert.Equal(4, em.GetComponent<Armor>(ship).Value);
        }

        Assert.Equal(4, EnemyShipType.HeavyCannon.Armor);
    }

    // ---- the enemy heavy shot, and the no-pierce rule ----

    [Fact]
    public void HeavyCannonSlugOutdamagesEveryOtherEnemyShotAtEveryTier()
    {
        for (int tier = 0; tier <= EnemyShipStats.MaxTier; tier++)
        {
            int heavyDamage = EnemyShipFactory.BuildTurret(EnemyShipType.HeavyCannon, tier).Weapon.Damage;
            foreach (var type in EnemyShipType.All)
            {
                if (type == EnemyShipType.HeavyCannon) continue;
                int other = EnemyShipFactory.BuildTurret(type, tier).Weapon.Damage;
                Assert.True(heavyDamage > other, $"tier {tier}: {type.TextureKey} lands {other}, HeavyCannon only {heavyDamage}");
            }
        }
    }

    [Fact]
    public void HeavyCannonSlugIsBiggerAndSlowerThanEveryOtherEnemyRound()
    {
        Assert.Equal(4f, EnemyShipType.HeavyCannon.TurretAmmoRadius);
        Assert.Equal(EnemyShipStats.AmmoRadius, EnemyShipType.Default.TurretAmmoRadius);

        foreach (var type in EnemyShipType.All.Where(t => t != EnemyShipType.HeavyCannon))
        {
            Assert.True(EnemyShipType.HeavyCannon.TurretAmmoRadius > type.TurretAmmoRadius);
            Assert.True(
                EnemyShipStats.AmmoSpeedFor(EnemyShipType.HeavyCannon, EnemyShipStats.MaxTier)
                    < EnemyShipStats.AmmoSpeedFor(type, EnemyShipStats.MaxTier),
                $"{type.TextureKey} out-flies the HeavyCannon's slug");
        }
    }

    [Fact]
    public void TheSlugIsTheOnlyEnemyRoundThatWalksThroughPlayerArmor()
    {
        int playerArmor = ShipType.Heavy.Armor;

        foreach (var type in EnemyShipType.All)
        {
            int damage = EnemyShipFactory.BuildTurret(type, 0).Weapon.Damage;
            int expected = type == EnemyShipType.HeavyCannon ? 4 : 1;
            Assert.Equal(expected, ArmorRules.EffectiveDamage(damage, playerArmor, pierce: 0));
        }
    }

    [Fact]
    public void EnemyRoundsNeverCarryPierce_Ever()
    {
        foreach (var type in EnemyShipType.All)
        {
            for (int tier = 0; tier <= EnemyShipStats.MaxTier; tier++)
                Assert.Equal(0, EnemyShipFactory.BuildTurret(type, tier).Weapon.Pierce);

            if (type.Missiles is null) continue;

            var em = new EntityManager();
            var ship = em.CreateEntity();
            EnemyShipFactory.AddComponents(em, ship, Vector2.Zero, Vector2.Zero, 0f, 0f, type, EnemyArmament.GunsAndMissiles, 0f);
            Assert.Equal(0, em.GetComponent<MissileWeapon>(ship).Weapon.Pierce);
        }
    }

    // ---- what armor does NOT touch ----

    [Fact]
    public void MineContactIgnoresArmor()
    {
        var (em, player) = SetupWorld();
        em.AddComponent(player, new Armor(4));

        var mine = em.CreateEntity();
        em.AddComponent(mine, new Position(new Vector2(1f, 0f)));
        em.AddComponent(mine, new EnemyMine(MineSize.Large, Speed: 0f, Angle: 0f));
        em.AddComponent(mine, new Health(2, 2));

        Step(em, player);

        Assert.Equal(80 - MineStats.ContactDamage, em.GetComponent<Health>(player).Current);
    }

    [Fact]
    public void ImpactDamageIgnoresArmor()
    {
        int unarmored = RamLoss(armored: false);
        int armored = RamLoss(armored: true);

        Assert.True(unarmored >= 1, "the ram should land a real impact — the fixture stopped meaning anything");
        Assert.Equal(unarmored, armored);
    }

    // ---- the table invariants §6 asked for on shipping day ----

    [Fact]
    public void KillClassStillLeadsTheTableAgainstHeavyCannonArmor()
    {
        int armor = EnemyShipType.HeavyCannon.Armor;

        float worstKill = Guns(kill: true).Min(g => MitigatedDps(g, armor));
        float bestSupport = Guns(kill: false).Max(g => MitigatedDps(g, armor));

        Assert.True(worstKill > bestSupport,
            $"armor {armor} let the best support chip ({bestSupport:F1}) catch the worst kill gun ({worstKill:F1})");
    }

    [Fact]
    public void EveryGunStillLandsAtLeastOneAgainstTheArmorCeiling()
    {
        foreach (var gun in Guns(kill: true).Concat(Guns(kill: false)))
            Assert.True(ArmorRules.EffectiveDamage(gun.Stats.Damage, ArmorRules.ArmorCeiling, pierce: 0) >= 1,
                $"{gun.Name} would land nothing at the armor ceiling");
    }

    [Fact]
    public void PierceCardsCannotMoveTheClassLineAgainstUnarmoredHulls()
    {
        // Pierce is worth 0 against armor 0, so the R25 class line is the shipped one again.
        foreach (var gun in WeaponType.All.Where(g => g.Name != WeaponType.LoadTestWeapon.Name))
            Assert.Equal(gun.Stats.Damage, ArmorRules.EffectiveDamage(gun.Stats.Damage, armor: 0, pierce: WeaponStats.PierceCeiling));
    }

    // ---- helpers ----

    static IEnumerable<WeaponType> Guns(bool kill) =>
        WeaponType.All.Where(w =>
            w.Name != WeaponType.LoadTestWeapon.Name && !w.Stats.Homing &&
            (kill ? !w.IsSupport : w.IsSupport));

    static float MitigatedDps(WeaponType gun, int armor) =>
        gun.Stats.FireRate * gun.Stats.PelletCount * ArmorRules.EffectiveDamage(gun.Stats.Damage, armor, pierce: 0);

    static (EntityManager Em, Entity Player) SetupWorld()
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 80, Radius: 18f));
        em.AddComponent(player, new Health(80, 80));
        return (em, player);
    }

    static Entity AddShip(EntityManager em, int armor, Vector2 position = default)
    {
        if (position == Vector2.Zero) position = new Vector2(-500f, 0f);
        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(position));
        em.AddComponent(ship, new EnemyShip(Radius: 18f, Speed: 50f, TurnRate: 1f, FiringRange: 300f, TurretFireRate: 2f, TurretAmmoSpeed: 150f, Acceleration: 30f, GraphicsId: 0));
        em.AddComponent(ship, new Rotation(0f));
        em.AddComponent(ship, new Health(120, 120));
        if (armor > 0) em.AddComponent(ship, new Armor(armor));
        return ship;
    }

    static void AddEnemyAmmoAt(EntityManager em, int damage, int pierce = 0)
    {
        var ammo = em.CreateEntity();
        em.AddComponent(ammo, new Position(new Vector2(-496f, 0f)));
        em.AddComponent(ammo, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 3f, Lifetime: 10f, IsEnemy: false, Damage: damage, Pierce: pierce));
    }

    static void AddPlayerAmmoAt(EntityManager em, int damage)
    {
        var ammo = em.CreateEntity();
        em.AddComponent(ammo, new Position(new Vector2(-16f, 0f)));
        em.AddComponent(ammo, new Ammo(Velocity: new Vector2(200f, 0f), Radius: 2.5f, Lifetime: 10f, IsEnemy: true, Damage: damage));
    }

    static void Step(EntityManager em, Entity player)
    {
        var commands = new CommandBuffer();
        new CollisionSystem().Update(new WorldView(em), Dt, commands);
        commands.Apply(em);
    }

    // One player hull already touching a small rock, closing at 800 px/s; returns the hp the impact took.
    static int RamLoss(bool armored)
    {
        var (em, player) = SetupWorld();
        em.AddComponent(player, new Velocity(new Vector2(800f, 0f)));
        if (armored) em.AddComponent(player, new Armor(4));

        var rock = em.CreateEntity();
        em.AddComponent(rock, new Position(new Vector2(24f, 0f)));
        em.AddComponent(rock, new Asteroid(IsSmall: true, Radius: 10f));
        em.AddComponent(rock, new Health(Asteroid.SmallHealth, Asteroid.SmallHealth));

        Step(em, player);

        return 80 - em.GetComponent<Health>(player).Current;
    }
}
