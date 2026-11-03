using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// What a hand is made of (plans/UPGRADE_BUNDLES.md, on the bucket machinery shipped under
// plans/UPGRADE_HAND_COMPOSITION.md): the legal cards are dealt by kind and by priority, so a level-up can
// never be five engine cards or three upgrades for the one chip gun the player barely uses, and a slot
// whose kind has run dry leaks rather than inventing a card. These are the shape rules;
// `LevelUpSlotOfferTest` and `RerollOfferTest` keep guarding the per-card legality rules
// (R18/R19/R23/R29/R33) these slots are built on top of, and `TwoPickLevelUpTest` the two-pick bundle.
public class HandCompositionTest
{
    // Every hand shape, over enough levels to cross the milestone beats at 5, 10, 15 and 20.
    static readonly ShipType[] Hulls = [ShipType.Scout, ShipType.Fighter, ShipType.Shadow, ShipType.Falcon, ShipType.Eagle, ShipType.Manta, ShipType.Raptor, ShipType.Heavy];

    [Fact]
    public void Hand_AlwaysCarriesOneHullCard()
    {
        // The hull slot always carries exactly one hull card: `Hp` alone below level 5, `Hp` or `Armor`
        // from level 5 (plans/ARMOR_AND_PIERCE.md), and `Hp`, `Armor` or `Shield` from level 10
        // (plans/SHIELDS.md) — the fixture hulls start unarmored and unshielded, so both defence cards are
        // in the draw there. Exactly one is dealt either way: the slot sets the shape, not the bucket.
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
            {
                var hullCards = Hand(hull, level).Where(o => o.Stat is UpgradeOption.Hp or UpgradeOption.Armor or UpgradeOption.Shield).ToList();
                Assert.Single(hullCards);
                if (level < 5) Assert.Equal(UpgradeOption.Hp, hullCards[0].Stat);
            }
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
    // R33 as plans/UPGRADE_BUNDLES.md deals it: a milestone hand has one damage slot per class, so a hull
    // that flies both classes grows both — the chip's card is +25% of its base, the kill gun's +50%.
    [Fact]
    public void MilestoneHand_GrowsBothClasses_WithOneDamageCardEach()
    {
        WeaponMount[] mixed =
        [
            new(TurretSlotName.MainGun, WeaponType.MachineGun),
            new(TurretSlotName.SmallLeft, WeaponType.FlakCurtain),
        ];

        var offered = Hand(ShipType.Heavy, 5, mixed);
        var damageCards = offered.Where(o => o.Stat == UpgradeOption.Damage && !o.IsNewWeapon).ToList();

        Assert.Equal(2, damageCards.Count);
        Assert.Contains(damageCards, card => card.WeaponName == WeaponType.MachineGun.Name);
        Assert.Contains(damageCards, card => card.WeaponName == WeaponType.FlakCurtain.Name);
    }

    // With no chip aboard, the chip's damage slot has nothing honest to name and does not invent one. The
    // freed slot goes to a new gun of the class the hull does not fly — something it could actually mount —
    // rather than to filler, and the kill class is served first.
    [Fact]
    public void MilestoneHand_WithNoChipAboard_OffersAGunForTheMissingClass()
    {
        WeaponMount[] killOnly =
        [
            new(TurretSlotName.MainGun, WeaponType.MachineGun),
            new(TurretSlotName.HeavyLeft, WeaponType.ScatterCannon),
        ];

        var offered = Hand(ShipType.Heavy, 5, killOnly);
        var damageCards = offered.Where(o => o.Stat == UpgradeOption.Damage && !o.IsNewWeapon).ToList();

        Assert.Single(damageCards);
        Assert.Equal(WeaponType.MachineGun.Name, damageCards[0].WeaponName);
        Assert.Contains(offered, o => o.IsNewWeapon && IsSupportGun(o.WeaponName));
    }

    // R33's cadence: the damage card is a milestone card. The leak path may reach it, but only at a
    // milestone, because the buckets are built empty for every ordinary hand.
    [Fact]
    public void StandardHand_NeverCarriesADamageCard()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
            {
                if (UpgradeOffer.IsMilestoneLevel(level)) continue;
                Assert.DoesNotContain(Hand(hull, level), o => o.Stat == UpgradeOption.Damage && !o.IsNewWeapon);
            }
    }

    // A milestone hand owes the player a large gun to switch into — even a hull with every mount filled,
    // where the only way to fly it is to displace the gun already there (plans/UPGRADE_BUNDLES.md §2).
    [Fact]
    public void MilestoneHand_OffersANewLargeGun_WhileOneIsUnowned()
    {
        for (int seed = 1; seed <= 12; seed++)
        {
            var offered = Hand(ShipType.Scout, 5, seed: seed);
            var newGuns = offered.Where(o => o.IsNewWeapon).Select(o => o.WeaponName).ToList();

            Assert.Contains(newGuns, name => !IsSupportGun(name));
        }
    }

    // The leak path's guard on `Hp`: the hull card already spent it, so a leaked hull card is health only
    // when the hull slot drew a defence card instead. A hand therefore never carries two `Hp` cards.
    [Fact]
    public void Hand_NeverCarriesTwoHpCards()
    {
        foreach (var hull in Hulls)
            for (int level = 5; level <= 21; level++)
                Assert.True(Hand(hull, level).Count(o => o.Stat == UpgradeOption.Hp) <= 1,
                    $"{hull.Name} at level {level} was dealt two health cards");
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
        Assert.Contains(offered, o => o.Stat is UpgradeOption.Hp or UpgradeOption.Armor);
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

    // Armor and its counter unlock with tier 2 — no hand below level 5 carries either card
    // (plans/ARMOR_AND_PIERCE.md).
    [Fact]
    public void NoArmorOrPierceCardBeforeLevelFive()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 4; level++)
                Assert.DoesNotContain(Hand(hull, level), o => o.Stat is UpgradeOption.Armor or UpgradeOption.ArmorPierce);
    }

    // From level 5 the Armor card is in the hull slot's draw and shows across a run — deterministic
    // seeds, so this proves the bucket is wired into the slot, not a probability claim.
    [Fact]
    public void ArmorCard_IsDealtSomewhereInAFullRunFromLevelFive()
    {
        for (int level = 5; level <= 21; level++)
            if (Hand(ShipType.Fighter, level).Any(o => o.Stat == UpgradeOption.Armor))
                return;

        Assert.Fail("no hand from level 5 up offered Armor");
    }

    // Pierce is a per-gun stat card, dealt for a mounted gun from level 5 in ordinary hands. It shares the
    // kill bucket with the rate/range/speed cards and an ordinary hand takes one card from it, so the draw
    // has to be seen several times to say the card reaches play at all — the same seed at the same level
    // deals the same hand, so the sweep runs over seeds as well as levels.
    [Fact]
    public void PierceCard_IsDealtForAMountedGun_FromLevelFive()
    {
        for (int level = 6; level <= 9; level++)
            for (int seed = 1; seed <= 8; seed++)
            {
                var offered = Hand(ShipType.Fighter, level, seed: seed);
                if (offered.Any(o => o.Stat == UpgradeOption.ArmorPierce && o.WeaponName == WeaponType.MachineGun.Name))
                    return;
            }

        Assert.Fail("no hand from level 6 up offered MachineGun armor pierce");
    }

    // The ceiling withdraws its card instead of dealing a lie (R19's rule): a hull at armor 12 is
    // dealt `Hp` alone again, and a gun at the pierce ceiling is never named for another one.
    // The ceiling is per-gun — the Shotgun beside the maxed MachineGun keeps its own draw.
    [Fact]
    public void ArmorCardsAtTheirCeilings_AreWithdrawn()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: UpgradeStats.XpForLevel(6), level: 6);
        em.AddComponent(player, new Armor(ArmorRules.ArmorCeiling));

        var maxedMachineGuns = new List<Entity>();
        foreach (var (turretEntity, turret) in em.GetEntitiesWithComponents<Turret>())
            if (turret.WeaponName == WeaponType.MachineGun.Name) maxedMachineGuns.Add(turretEntity);
        foreach (var turretEntity in maxedMachineGuns)
        {
            var turret = em.GetComponent<Turret>(turretEntity);
            em.AddComponent(turretEntity, turret with { Weapon = turret.Weapon with { Pierce = WeaponStats.PierceCeiling } });
        }

        for (int attempt = 0; attempt < 12; attempt++)
        {
            var offered = DealHand(em, 7);
            Assert.DoesNotContain(offered, o => o.Stat == UpgradeOption.Armor);
            Assert.Contains(offered, o => o.Stat == UpgradeOption.Hp);
            Assert.DoesNotContain(offered, o => o.Stat == UpgradeOption.ArmorPierce && o.WeaponName == WeaponType.MachineGun.Name);
        }
    }

    static bool NamesAGun(UpgradableOption option) => option.WeaponName.Length > 0;

    // Which class a named card's gun belongs to (R24/R25): support chips and kill guns, keyed on the gun,
    // never on the mount it happens to sit in.
    static bool IsSupportGun(string weaponName) => WeaponType.FromName(weaponName) is { } gun && gun.IsSupport;    static bool IsUtilityCard(UpgradableOption option) =>
        option.Stat is UpgradeOption.PickupRadius or UpgradeOption.ForwardAcceleration
            or UpgradeOption.TurnSpeed or UpgradeOption.SideThrust or UpgradeOption.BackThrust;

    static string Names(UpgradableOption[] hand) => string.Join(", ", hand.Select(UpgradeOffer.FormatChoice));

    // The hand `hull` is dealt on reaching `level`: the hull starts holding the XP the previous level
    // needs, so one `Roll` is the hand the player would see. The default seed is the world default, which
    // makes a hand reproducible; a test that needs to see the draw vary passes different seeds, because the
    // same seed at the same level deals the same hand.
    static UpgradableOption[] Hand(ShipType hull, int level, WeaponMount[]? mounts = null, int seed = EntityManager.DefaultRngSeed)
    {
        var (em, _) = CreateHull(hull, xp: UpgradeStats.XpForLevel(level - 1), level: level - 1, mounts, seed);
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

    static (EntityManager Em, Entity Player) CreateHull(ShipType ship, int xp, int level, WeaponMount[]? mounts = null, int seed = EntityManager.DefaultRngSeed)
    {
        var em = new EntityManager(seed);

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
