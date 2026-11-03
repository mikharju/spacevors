using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class LevelUpScriptTest
{
    // A bench entry is one card and a bundle is two, so entries are consumed in pairs — strong card first
    // (plans/UPGRADE_BUNDLE_PAIRS.md §6).
    [Fact]
    public void ScriptedPairIsDealtAsOneBundle()
    {
        var (em, player) = CreatePlayerWithTurret(xp: 15);
        var system = new LevelUpSystem([
            new UpgradableOption("RailGun", UpgradeOption.Damage, IsNewWeapon: true),
            new UpgradableOption("TestWeapon", UpgradeOption.FireRate)]);

        RunLevelUp(system, em);

        Assert.Equal(2, em.GetComponent<Player>(player).Level);
        var bundle = GetBundles(em)[0];
        Assert.Equal("RailGun", bundle.Powerful.WeaponName);
        Assert.True(bundle.Powerful.IsNewWeapon);
        Assert.Equal("TestWeapon", bundle.Weak.WeaponName);
        Assert.Equal(UpgradeOption.FireRate, bundle.Weak.Stat);
    }

    [Fact]
    public void ScriptedUpgradesAreConsumedInPairs()
    {
        var (em, player) = CreatePlayerWithTurret(xp: 15);
        var system = new LevelUpSystem([
            new UpgradableOption("", UpgradeOption.Hp),
            new UpgradableOption("TestWeapon", UpgradeOption.FireRate),
            new UpgradableOption("TestWeapon", UpgradeOption.Range),
            new UpgradableOption("TestWeapon", UpgradeOption.ProjectileSpeed)]);

        RunLevelUp(system, em);

        var first = GetBundles(em);
        Assert.Equal(UpgradeOption.Hp, first[0].Powerful.Stat);
        Assert.Equal(UpgradeOption.FireRate, first[0].Weak.Stat);

        DestroyChoices(em);
        SetXp(em, player, UpgradeStats.XpForLevel(2));

        RunLevelUp(system, em);

        var second = GetBundles(em);
        Assert.Equal(UpgradeOption.Range, second[0].Powerful.Stat);
        Assert.Equal(UpgradeOption.ProjectileSpeed, second[0].Weak.Stat);
    }

    // Fewer than two entries left is an exhausted bench: the level takes the dealt offer rather than a
    // half-filled bundle, and a dealt offer is always more than one bundle.
    [Fact]
    public void ScriptExhaustionFallsBackToDealtOffer()
    {
        var (em, player) = CreatePlayerWithTurret(xp: 15);
        var system = new LevelUpSystem([
            new UpgradableOption("", UpgradeOption.Hp),
            new UpgradableOption("TestWeapon", UpgradeOption.FireRate),
            new UpgradableOption("", UpgradeOption.TurnSpeed)]);

        RunLevelUp(system, em);
        DestroyChoices(em);
        SetXp(em, player, UpgradeStats.XpForLevel(2));

        RunLevelUp(system, em);

        Assert.True(GetBundles(em).Length > 1, "expected a dealt offer of several bundles");
    }

    [Fact]
    public void MilestoneOffersNewWeaponsWhenMountsAreFree()
    {
        // Heavy hull: three of its seven mounts filled, two distinct weapon types.
        var (em, player) = CreateHeavyLikePlayer(xp: UpgradeStats.XpForLevel(4), level: 4);

        RunLevelUp(new LevelUpSystem(), em);

        Assert.Equal(5, em.GetComponent<Player>(player).Level);
        var bundles = GetBundles(em);
        // A milestone hand is a full five bundles, and new guns are what it is built to offer.
        Assert.Equal(5, bundles.Length);
        Assert.Contains(Cards(bundles), card => card.IsNewWeapon);
    }

    [Fact]
    public void NoTurrets_LevelUpNotConsumedUntilTurretExists()
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, Xp: 15, Level: 1));

        var system = new LevelUpSystem();
        RunLevelUp(system, em);

        Assert.Empty(em.GetEntitiesWithComponents<PendingChoice>().ToList());
        var stats = em.GetComponent<Player>(player);
        Assert.Equal(1, stats.Level);
        Assert.Equal(15, stats.Xp);

        AddTurret(em, "TestWeapon");
        RunLevelUp(system, em);

        Assert.Equal(2, em.GetComponent<Player>(player).Level);
        Assert.InRange(GetBundles(em).Length, 1, 5);
    }

    private static void RunLevelUp(LevelUpSystem system, EntityManager em)
    {
        var view = new WorldView(em);
        var commands = new CommandBuffer();
        system.Update(view, 1f / 60f, commands);
        commands.Apply(em);
    }

    private static UpgradeBundle[] GetBundles(EntityManager em)
    {
        var choices = em.GetEntitiesWithComponents<PendingChoice>().ToList();
        Assert.Single(choices);
        return em.GetComponent<PendingUpgradeOptions>(choices[0].Entity).Bundles;
    }

    // Both halves of every bundle, in deal order: most of these tests ask about cards, not about pairs.
    private static UpgradableOption[] Cards(IEnumerable<UpgradeBundle> bundles) =>
        [.. bundles.SelectMany(bundle => new[] { bundle.Powerful, bundle.Weak })];

    private static void DestroyChoices(EntityManager em)
    {
        foreach (var (entity, _) in em.GetEntitiesWithComponents<PendingChoice>().ToList())
            em.DestroyEntity(entity);
    }

    private static void SetXp(EntityManager em, Entity player, int xp)
    {
        var stats = em.GetComponent<Player>(player);
        em.AddComponent(player, stats with { Xp = xp });
    }

    private static (EntityManager Em, Entity Player) CreatePlayerWithTurret(int xp, int level = 1)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, Xp: xp, Level: level, PickupRadius: 60f, RotationSpeed: 5f));

        AddTurret(em, "TestWeapon");

        return (em, player);
    }

    private static (EntityManager Em, Entity Player) CreateHeavyLikePlayer(int xp, int level)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 20, Radius: 84f, Xp: xp, Level: level, PickupRadius: 230f, RotationSpeed: 0.67f));

        foreach (var slot in TurretLayouts.Heavy)
            em.AddComponent(em.CreateEntity(), new TurretSlot(slot));

        MountOnHull(em, TurretSlotName.MainGun, WeaponType.MachineGun);
        MountOnHull(em, TurretSlotName.SmallLeft, WeaponType.Shotgun);
        MountOnHull(em, TurretSlotName.SmallRight, WeaponType.Shotgun);

        return (em, player);
    }

    // Mount a weapon where the hull says it goes, the way level-up does it.
    private static void MountOnHull(EntityManager em, string slotName, WeaponType weapon)
    {
        var view = new WorldView(em);
        Assert.True(TurretMount.TryFindSlot(view, slotName, out var slotEntity));
        TurretMount.Mount(em, slotEntity, view.GetComponent<TurretSlot>(slotEntity).Definition, weapon, Vector2.Zero, 0f);
    }

    private static void AddTurret(EntityManager em, string weaponName)
    {
        var turret = em.CreateEntity();
        em.AddComponent(turret, new Position(Vector2.Zero));
        em.AddComponent(turret, new Rotation(0f));
        em.AddComponent(turret, new Turret(
            Weapon: new WeaponStats(FireRate: 5f, AmmoSpeed: 500f, KickbackForce: 10f, PelletCount: 1, Scatter: 0.05f, ShotLifetime: 2f, Damage: 1),
            WeaponName: weaponName,
            ArcAngle: MathF.PI / 4f,
            Range: 300f));
    }
}
