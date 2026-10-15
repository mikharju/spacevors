using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// A milestone hand offers new weapons the hull can actually take: mostly weapons that fit a free
// mount, and at most one that means replacing something already mounted.
public class LevelUpSlotOfferTest
{
    [Fact]
    public void MilestoneHand_OffersNewWeaponsThatFitFreeMounts()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: 45, level: 4);

        RunLevelUp(em);

        Assert.Equal(5, em.GetComponent<Player>(player).Level);

        var offered = NewWeaponCards(em);
        Assert.NotEmpty(offered);
        Assert.True(offered.Count <= 3, $"a milestone hand offers at most three new weapons, got {offered.Count}");

        var freeSlots = TurretMount.FreeSlots(new WorldView(em));
        int needsReplacement = offered.Count(name => !freeSlots.Any(slot => TurretMount.Fits(slot, WeaponType.FromName(name)!.Value)));
        Assert.True(needsReplacement <= 1, $"only one offered weapon may need a replacement, got {needsReplacement}");
    }

    [Fact]
    public void FullyBookedHull_OfferAtMostOneReplacement()
    {
        WeaponMount[] everyMount =
        [
            new(TurretSlotName.MainGun, WeaponType.MachineGun),
            new(TurretSlotName.HeavyLeft, WeaponType.RailGun),
            new(TurretSlotName.HeavyRight, WeaponType.GravitonBurst),
            new(TurretSlotName.SmallLeft, WeaponType.Shotgun),
            new(TurretSlotName.SmallRight, WeaponType.AcidBubbleSpray),
            new(TurretSlotName.SmallBack, WeaponType.FlakCurtain),
            new(TurretSlotName.SmallNose, WeaponType.PointDefenceTurret),
        ];

        var (em, _) = CreateHull(ShipType.Heavy, xp: 45, level: 4, everyMount);

        Assert.Empty(TurretMount.FreeSlots(new WorldView(em)));

        RunLevelUp(em);

        // With every mount booked, a hull may be offered at most one weapon — and that one asks the
        // player to pull something out.
        Assert.True(NewWeaponCards(em).Count <= 1, "a full hull should not be offered a shelf of weapons it cannot fit");
    }

    [Fact]
    public void MountedWeaponsAreNeverOfferedAgain()
    {
        var (em, _) = CreateHull(ShipType.Fighter, xp: 45, level: 4);
        var mounted = TurretMount.MountedSlots(new WorldView(em)).Select(m => m.WeaponName).ToHashSet();

        RunLevelUp(em);

        Assert.DoesNotContain(NewWeaponCards(em), name => mounted.Contains(name));
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
}
