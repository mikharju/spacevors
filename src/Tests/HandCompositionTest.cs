using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// What a hand is made of (plans/UPGRADE_HAND_COMPOSITION.md): the legal cards are dealt one per slot by
// kind, so a level-up can never be five engine cards or three upgrades for the one chip gun the player
// barely uses. These are the shape rules; `LevelUpSlotOfferTest` and `RerollOfferTest` keep guarding the
// per-card legality rules (R18/R19/R23/R29/R33) these slots are built on top of.
public class HandCompositionTest
{
    // Every hand shape, over enough levels to cross the milestone beats at 5, 10, 15 and 20.
    static readonly ShipType[] Hulls = [ShipType.Scout, ShipType.Fighter, ShipType.Shadow, ShipType.Falcon, ShipType.Eagle, ShipType.Manta, ShipType.Raptor, ShipType.Heavy];

    [Fact]
    public void Hand_AlwaysCarriesAnHpCard()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
                Assert.Contains(Hand(hull, level), o => o.Stat == UpgradeOption.Hp);
    }

    // The complaint this change answers: a hand used to be able to carry almost nothing for the guns the
    // player is actually shooting with — 40% of a one-gun hull's hands held at most one.
    [Fact]
    public void Hand_AlwaysCarriesThreeCardsForGuns()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
            {
                var offered = Hand(hull, level);
                int gunCards = offered.Count(NamesAGun);
                Assert.True(gunCards >= 3, $"{hull.Name} at level {level} got {gunCards} cards for guns: {Names(offered)}");
            }
    }

    [Fact]
    public void Hand_IsAlwaysFiveCards_WhileTheHullHasLegalCardsToFillIt()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
                Assert.Equal(5, Hand(hull, level).Length);
    }

    // One engine/utility card per non-milestone hand: flying stays a choice, it is just no longer the
    // default answer to a level-up.
    [Fact]
    public void NonMilestoneHand_CarriesOneEngineOrUtilityCard()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
            {
                if (UpgradeOffer.IsMilestoneLevel(level)) continue;
                Assert.Equal(1, Hand(hull, level).Count(IsUtilityCard));
            }
    }

    // A four-gun hull has enough distinct guns that no hand needs to double up on one of them. A two-gun
    // hull does — three weapon slots against two guns — and doubling up there is the honest choice: it
    // upgrades what the player is carrying instead of padding the hand with filler.
    [Fact]
    public void HeavyHand_NeverNamesTheSameGunTwice()
    {
        for (int level = 2; level <= 21; level++)
        {
            var offered = Hand(ShipType.Heavy, level);
            var guns = offered.Where(NamesAGun).Select(o => o.WeaponName).ToList();
            Assert.True(guns.Count == guns.Distinct().Count(), $"level {level} doubled up on a gun: {Names(offered)}");
        }
    }

    // The one-gun hull with two free mounts was the worst case of the old shuffle. The spilled slots buy
    // it guns to mount rather than a third upgrade for the gun it already has.
    [Fact]
    public void OneGunHull_WithFreeMounts_IsOfferedNewGuns()
    {
        for (int level = 2; level <= 4; level++)
        {
            var offered = Hand(ShipType.Falcon, level);
            Assert.True(offered.Count(o => o.IsNewWeapon) >= 2, $"Falcon at level {level} got no guns to mount: {Names(offered)}");
        }
    }

    // A hull holding only one class still gets a full hand out of the spill rules.
    [Fact]
    public void SupportOnlyHull_StillGetsAFullHand()
    {
        WeaponMount[] supportOnly = [new(TurretSlotName.SmallLeft, WeaponType.Stiletto), new(TurretSlotName.SmallRight, WeaponType.FlakCurtain)];

        for (int level = 2; level <= 21; level++)
            Assert.Equal(5, Hand(ShipType.Fighter, level, supportOnly).Length);
    }

    [Fact]
    public void MilestoneHand_OfferANewGun_WhileGunsRemainToUnlock()
    {
        for (int level = 5; level <= 20; level += 5)
            Assert.Contains(Hand(ShipType.Fighter, level), o => o.IsNewWeapon);
    }

    // A milestone hand has one weapon slot left after the hull card and the three new guns, so it deals
    // one mounted gun's damage card — kill class first — instead of one per gun and a shuffle deciding
    // which one the player gets to see. R33 still sizes the card; this only picks the gun.
    [Fact]
    public void MilestoneHand_DealsOneDamageCard_KillClassFirst()
    {
        WeaponMount[] mixed =
        [
            new(TurretSlotName.MainGun, WeaponType.MachineGun),
            new(TurretSlotName.SmallLeft, WeaponType.FlakCurtain),
        ];

        var offered = Hand(ShipType.Heavy, 5, mixed);
        var damageCards = offered.Where(o => o.Stat == UpgradeOption.Damage && !o.IsNewWeapon).ToList();

        Assert.Single(damageCards);
        Assert.Equal(WeaponType.MachineGun.Name, damageCards[0].WeaponName);
    }

    // Late game, once the ship owns the whole roster, the freed milestone slots buy damage cards for the
    // guns it flies instead of leaving the hand short.
    [Fact]
    public void MilestoneHand_WithNothingLeftToUnlock_FillsWithDamageAndUtilityCards()
    {
        var carried = WeaponType.All.Where(w => w.Name != WeaponType.LoadTestWeapon.Name).Select(TurretMount.CarriedTurret).ToList();
        var (em, player) = CreateHull(ShipType.Fighter, xp: 999_999, level: 4, mounts: [new(TurretSlotName.MainGun, WeaponType.MachineGun)]);
        em.AddComponent(player, new WeaponCargo(carried));

        var offered = DealHand(em, 5);

        Assert.Equal(5, offered.Length);
        Assert.Contains(offered, o => o.Stat == UpgradeOption.Hp);
        Assert.DoesNotContain(offered, o => o.IsNewWeapon);
        Assert.Contains(offered, o => o.Stat == UpgradeOption.Damage && !o.IsNewWeapon);
    }

    // The re-deal goes through the same dealer, so it cannot break the shape a level-up hand has.
    [Fact]
    public void Reroll_HandsOutTheSameShapeAsALevelUpHand()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: UpgradeStats.XpForLevel(1), level: 1);
        var commands = new CommandBuffer();
        new LevelUpSystem().Update(new WorldView(em), 1f / 120f, commands);
        commands.Apply(em);
        em.AddComponent(player, new RerollStock(10));

        for (int attempt = 0; attempt < 10; attempt++)
        {
            Assert.True(UpgradeOffer.TryReroll(em, player));
            var offered = PendingHand(em);

            Assert.Contains(offered, o => o.Stat == UpgradeOption.Hp);
            Assert.Equal(1, offered.Count(IsUtilityCard));
            Assert.True(offered.Count(NamesAGun) >= 3, $"a re-deal went back to filler: {Names(offered)}");
        }
    }

    // The spill must never reach past a gate the rules already closed (R18/R19/R29), however empty its
    // slot is: a chip at its class range roof is dealt no range card.
    [Fact]
    public void Spill_NeverDealsACardTheRulesWouldRefuse()
    {
        WeaponMount[] chipAtItsRoof = [new(TurretSlotName.SmallLeft, WeaponType.Stiletto)];

        for (int level = 2; level <= 21; level++)
        {
            var offered = Hand(ShipType.Fighter, level, chipAtItsRoof);

            Assert.DoesNotContain(offered, o => o.Stat == UpgradeOption.Range && o.WeaponName == WeaponType.Stiletto.Name);
            Assert.DoesNotContain(offered, o => o.Stat == UpgradeOption.ProjectileSpeed && o.WeaponName == WeaponType.Stiletto.Name);
            Assert.DoesNotContain(offered, o => o.Stat == UpgradeOption.MissileHealth && o.WeaponName == WeaponType.Stiletto.Name);
        }
    }

    static bool NamesAGun(UpgradableOption option) => option.WeaponName.Length > 0;

    static bool IsUtilityCard(UpgradableOption option) =>
        option.Stat is UpgradeOption.PickupRadius or UpgradeOption.ForwardAcceleration
            or UpgradeOption.TurnSpeed or UpgradeOption.SideThrust or UpgradeOption.BackThrust;

    static string Names(UpgradableOption[] hand) => string.Join(", ", hand.Select(UpgradeOffer.FormatChoice));

    // The hand `hull` is dealt on reaching `level`: the hull starts holding the XP the previous level
    // needs, so one `Roll` is the hand the player would see.
    static UpgradableOption[] Hand(ShipType hull, int level, WeaponMount[]? mounts = null)
    {
        var (em, _) = CreateHull(hull, xp: UpgradeStats.XpForLevel(level - 1), level: level - 1, mounts);
        return DealHand(em, level);
    }

    static UpgradableOption[] DealHand(EntityManager em, int level)
    {
        var view = new WorldView(em);
        Assert.True(view.GetEntitiesWithComponents<Player>().TryFirst(out var playerTuple), "the fixture hull is missing");
        return UpgradeOffer.Roll(view, playerTuple.Entity, level, "test") ?? throw new InvalidOperationException("a hull with turrets must be dealt a hand");
    }

    static UpgradableOption[] PendingHand(EntityManager em)
    {
        Assert.True(em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pending));
        return em.GetComponent<PendingUpgradeOptions>(pending.Entity).Options;
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
}
