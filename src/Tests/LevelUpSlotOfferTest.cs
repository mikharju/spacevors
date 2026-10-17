using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// What a level-up hand may contain once cargo exists: new weapons the ship does not already own,
// whether or not a mount is free for them, and stat upgrades only for the guns actually on the hull.
public class LevelUpSlotOfferTest
{
    [Fact]
    public void MilestoneHand_OffersAtMostThreeNewWeapons()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: 45, level: 4);

        RunLevelUp(em);

        Assert.Equal(5, em.GetComponent<Player>(player).Level);

        var offered = NewWeaponCards(em);
        Assert.NotEmpty(offered);
        Assert.True(offered.Count <= 3, $"a milestone hand offers at most three new weapons, got {offered.Count}");
    }

    [Fact]
    public void FullyBookedHull_StillOfferedNewWeapons()
    {
        WeaponMount[] everyMount =
        [
            new(TurretSlotName.MainGun, WeaponType.MachineGun),
            new(TurretSlotName.HeavyLeft, WeaponType.RailGun),
            new(TurretSlotName.HeavyRight, WeaponType.GravitonBurst),
            new(TurretSlotName.SmallLeft, WeaponType.Shotgun),
            new(TurretSlotName.SmallRight, WeaponType.AcidBubbleSpray),
            new(TurretSlotName.SmallBack, WeaponType.FlakCurtain),
            new(TurretSlotName.SmallNose, WeaponType.InterceptorTurret),
        ];

        var (em, _) = CreateHull(ShipType.Heavy, xp: 999_999, level: 4, everyMount);

        Assert.Empty(TurretMount.FreeSlots(new WorldView(em)));

        // An unowned weapon is always worth offering: it can wait in cargo until the player makes
        // room. A hand may shuffle its new-weapon cards out entirely, so look across several.
        var offered = new List<string>();
        for (int levelUp = 0; levelUp < 12; levelUp++)
        {
            RunLevelUp(em);
            offered.AddRange(TakeNewWeaponCards(em));
        }

        Assert.NotEmpty(offered);
    }

    [Fact]
    public void MountedWeaponsAreNeverOfferedAgain()
    {
        var (em, _) = CreateHull(ShipType.Fighter, xp: 45, level: 4);
        var mounted = TurretMount.MountedSlots(new WorldView(em)).Select(m => m.WeaponName).ToHashSet();

        RunLevelUp(em);

        Assert.DoesNotContain(NewWeaponCards(em), name => mounted.Contains(name));
    }

    [Fact]
    public void CargoWeaponsAreNeverOfferedAgain()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: 45, level: 4);
        em.AddComponent(player, WeaponCargo.Empty.With(TurretMount.CarriedTurret(WeaponType.RailGun)));

        RunLevelUp(em);

        Assert.DoesNotContain(NewWeaponCards(em), name => name == WeaponType.RailGun.Name);
    }

    // Cargo weapons are inactive: they are not upgraded either, so no card may name one.
    [Fact]
    public void CargoWeaponsGetNoStatUpgradeCards()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: 999_999, level: 1);
        em.AddComponent(player, WeaponCargo.Empty.With(TurretMount.CarriedTurret(WeaponType.RailGun)));

        RunLevelUp(em);

        var choices = em.GetEntitiesWithComponents<PendingChoice>().ToList();
        Assert.Single(choices);
        Assert.DoesNotContain(em.GetComponent<PendingUpgradeOptions>(choices[0].Entity).Options,
            o => o.WeaponName == WeaponType.RailGun.Name);
    }

    // A hitscan gun fires instant beams: a projectile-speed card would multiply AmmoSpeed 0 by 1.3,
    // so it must never be dealt.
    [Fact]
    public void HitscanGun_IsNeverOfferedAProjectileSpeedCard()
    {
        WeaponMount[] hitscanOnly = [new(TurretSlotName.MainGun, WeaponType.LaserLance)];
        var (em, _) = CreateHull(ShipType.Fighter, xp: 999_999, level: 1, hitscanOnly);

        for (int levelUp = 0; levelUp < 10; levelUp++)
        {
            RunLevelUp(em);

            foreach (var (entity, _, options) in em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().ToList())
            {
                Assert.DoesNotContain(options.Options,
                    o => o.Stat == UpgradeOption.ProjectileSpeed && o.WeaponName == WeaponType.LaserLance.Name);
                em.DestroyEntity(entity);
            }
        }
    }

    // R18: a guided volley that has reached its round budget (R17) is not dealt another "+1 shot"
    // card. Below the ceiling the same gun still is, so the card is withdrawn, not banned.
    [Fact]
    public void GuidedVolleyAtItsCeiling_IsNotOfferedAnotherShot()
    {
        WeaponMount[] swarmOnly = [new(TurretSlotName.MainGun, WeaponType.Swarmlet)];

        foreach (int pellets in new[] { WeaponType.Swarmlet.Stats.PelletCount, 5 })
        {
            var (em, _) = CreateHull(ShipType.Fighter, xp: 999_999, level: 1, swarmOnly);
            foreach (var (turretEntity, turret) in em.GetEntitiesWithComponents<Turret>().ToList())
                em.AddComponent(turretEntity, turret with { Weapon = turret.Weapon with { PelletCount = pellets } });

            bool offered = false;
            for (int levelUp = 0; levelUp < 12; levelUp++)
            {
                RunLevelUp(em);

                foreach (var (entity, _, options) in em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().ToList())
                {
                    if (options.Options.Any(o => o.Stat == UpgradeOption.FireRate && o.WeaponName == WeaponType.Swarmlet.Name))
                        offered = true;
                    em.DestroyEntity(entity);
                }
            }

            var grown = WeaponType.Swarmlet.Stats with { PelletCount = pellets };
            Assert.Equal(grown.CanGrowSalvo, offered);
        }
    }

    // Tier gating: the pool is what new-weapon cards are drawn from, so unlocking a gun is a matter
    // of which level its UnlockLevel reaches. Tier 2 opens with the first milestone hand (level 5).
    [Fact]
    public void TierTwoGuns_UnlockWithTheFirstMilestone()
    {
        string[] advanced = ["RailGun", "LaserLance", "PrismLance", "GravitonBurst", "AcidBubbleSpray", "SeekerPod", "Swarmlet"];

        foreach (string name in advanced)
        {
            Assert.DoesNotContain(WeaponType.NewWeaponPoolFor(1), w => w.Name == name);
            Assert.Contains(WeaponType.NewWeaponPoolFor(5), w => w.Name == name);
        }

        // Guided guns unlock in two steps: the small pods with the first milestone hand, the big
        // torpedo only for a run that has reached level 10.
        Assert.DoesNotContain(WeaponType.NewWeaponPoolFor(5), w => w.Name == "HavocTorpedo");
        Assert.Contains(WeaponType.NewWeaponPoolFor(10), w => w.Name == "HavocTorpedo");

        Assert.DoesNotContain(WeaponType.NewWeaponPoolFor(5), w => w.Name == WeaponType.LoadTestWeapon.Name);
    }

    static (EntityManager Em, Entity Player) CreateHull(ShipType ship, int xp, int level, WeaponMount[]? mounts = null)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Rotation(0f));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 20, Radius: 40f, Xp: xp, Level: level, PickupRadius: 150f, RotationSpeed: 1f));

        foreach (var slot in ship.Slots)
            em.AddComponent(em.CreateEntity(), new TurretSlot(slot));

        foreach (var mount in mounts ?? [.. ship.Loadout.Mounts])
        {
            var view = new WorldView(em);
            Assert.True(TurretMount.TryFindSlot(view, mount.SlotName, out var slotEntity));
            TurretMount.Mount(em, slotEntity, view.GetComponent<TurretSlot>(slotEntity).Definition, mount.Weapon, Vector2.Zero, hullRotation: 0f);
        }

        return (em, player);
    }

    static void RunLevelUp(EntityManager em)
    {
        var commands = new CommandBuffer();
        new LevelUpSystem().Update(new WorldView(em), 1f / 120f, commands);
        commands.Apply(em);
    }

    static List<string> NewWeaponCards(EntityManager em)
    {
        var choices = em.GetEntitiesWithComponents<PendingChoice>().ToList();
        Assert.Single(choices);

        return [.. em.GetComponent<PendingUpgradeOptions>(choices[0].Entity).Options
            .Where(o => o.IsNewWeapon)
            .Select(o => o.WeaponName)];
    }

    // Read the hand and clear it, so the next level-up can offer another one.
    static List<string> TakeNewWeaponCards(EntityManager em)
    {
        var choices = em.GetEntitiesWithComponents<PendingChoice>().ToList();
        if (choices.Count == 0) return new List<string>();

        List<string> names = [.. em.GetComponent<PendingUpgradeOptions>(choices[0].Entity).Options
            .Where(o => o.IsNewWeapon)
            .Select(o => o.WeaponName)];

        foreach (var (entity, _) in choices) em.DestroyEntity(entity);
        return names;
    }
}
