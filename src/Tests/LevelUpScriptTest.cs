using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class LevelUpScriptTest
{
    [Fact]
    public void ScriptedUpgradeShowsSingleCardAtAnyLevel()
    {
        var (em, player) = CreatePlayerWithTurret(xp: 15);
        var system = new LevelUpSystem([new UpgradableOption("RailGun", UpgradeOption.Damage, IsNewWeapon: true)]);

        RunLevelUp(system, em);

        Assert.Equal(2, em.GetComponent<Player>(player).Level);
        var options = GetChoiceOptions(em);
        Assert.Single(options);
        Assert.Equal("RailGun", options[0].WeaponName);
        Assert.True(options[0].IsNewWeapon);
    }

    [Fact]
    public void ScriptedUpgradesAreConsumedInOrder()
    {
        var (em, player) = CreatePlayerWithTurret(xp: 15);
        var system = new LevelUpSystem([
            new UpgradableOption("", UpgradeOption.Hp),
            new UpgradableOption("TestWeapon", UpgradeOption.FireRate)]);

        RunLevelUp(system, em);
        Assert.Equal(UpgradeOption.Hp, GetChoiceOptions(em)[0].Stat);

        DestroyChoices(em);
        SetXp(em, player, 25);

        RunLevelUp(system, em);

        var options = GetChoiceOptions(em);
        Assert.Single(options);
        Assert.Equal("TestWeapon", options[0].WeaponName);
        Assert.Equal(UpgradeOption.FireRate, options[0].Stat);
    }

    [Fact]
    public void ScriptExhaustionFallsBackToRandomPool()
    {
        var (em, player) = CreatePlayerWithTurret(xp: 15);
        var system = new LevelUpSystem([new UpgradableOption("", UpgradeOption.Hp)]);

        RunLevelUp(system, em);
        DestroyChoices(em);
        SetXp(em, player, 25);

        RunLevelUp(system, em);

        var options = GetChoiceOptions(em);
        Assert.True(options.Length > 1 && options.Length <= 5);
    }

    [Fact]
    public void MilestoneOffersNewWeaponWhenDistinctWeaponsBelowMaxSlots()
    {
        // Heavy-like loadout: three turrets but only two distinct weapon types, max three slots.
        var (em, player) = CreateHeavyLikePlayer(xp: 45, level: 4);

        RunLevelUp(new LevelUpSystem(), em);

        Assert.Equal(5, em.GetComponent<Player>(player).Level);
        var options = GetChoiceOptions(em);
        Assert.Equal(3, options.Length);
        Assert.Contains(options, o => o.IsNewWeapon);
    }

    private static void RunLevelUp(LevelUpSystem system, EntityManager em)
    {
        var view = new WorldView(em);
        var commands = new CommandBuffer();
        system.Update(view, 1f / 60f, commands);
        commands.Apply(em);
    }

    private static UpgradableOption[] GetChoiceOptions(EntityManager em)
    {
        var choices = em.GetEntitiesWithComponents<PendingChoice>().ToList();
        Assert.Single(choices);
        return em.GetComponent<PendingUpgradeOptions>(choices[0].Entity).Options;
    }

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
        em.AddComponent(player, new WeaponSlots(2, 3));

        AddTurret(em, "MachineGun");
        AddTurret(em, "Shotgun");
        AddTurret(em, "Shotgun");

        return (em, player);
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
