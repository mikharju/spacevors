using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// A new gun dealt to a hull that cannot fit it arrives carrying upgrades (plans/PREUPGRADED_NEW_WEAPONS.md).
// `HandCompositionTest` owns the shape of a hand and `LevelUpSlotOfferTest` the per-card legality gates; this file
// owns the arriving upgrades themselves: when they appear (D1), how many (D2), which (D3), that a build is part of
// the card's identity (D4/D8), and that the grant hands over exactly what the card promised.
public class PreUpgradedNewWeaponTest
{
    // A hull with a mount still free is dealt plain guns: an addition is not a replacement, so there is nothing to
    // make up for (D1).
    [Fact]
    public void NewGun_IsPlain_WhileAMountItFitsIsFree()
    {
        var (em, player) = CreateHull(ShipType.Fighter, 9);

        foreach (var card in NewGuns(em, 30))
            Assert.True(card.PreApplied == WeaponPreUpgrade.None,
                $"{UpgradeOffer.FormatChoice(card)} arrives upgraded while a mount is free");
    }

    // The mirror: every mount is booked, so a new gun would displace something already upgraded.
    [Fact]
    public void NewGun_ArrivesUpgraded_WhenNoMountOfThatSizeIsFree()
    {
        var (em, player) = CreateHull(ShipType.Heavy, 9, BookedHeavy);
        foreach (var gun in new[] { WeaponType.MachineGun, WeaponType.RailGun, WeaponType.GravitonBurst })
            for (int times = 0; times < 2; times++) Upgrade(em, player, UpgradeOption.Damage, gun.Name);

        var arriving = NewGuns(em, 30).Where(card => SizeOf(card) == TurretSize.Large).ToList();
        Assert.NotEmpty(arriving);
        Assert.Contains(arriving, card => card.PreApplied != WeaponPreUpgrade.None);
    }

    // D2: half, rounded up, of the upgrades on the **least** upgraded gun standing in a mount the new gun could
    // take. The three heavy mounts start at 0/2/0 upgrades, so the least is 0 and the boost is 0 — even though the
    // chip mounts hold six upgrades between them. Once every heavy mount has at least one, the least is 1 and half
    // rounded up is 1.
    [Fact]
    public void Boost_IsHalfRoundedUpOfTheLeastUpgradedGunInTheMountsItCouldTake()
    {
        var (em, player) = CreateHull(ShipType.Heavy, 9, BookedHeavy);
        Upgrade(em, player, UpgradeOption.Damage, WeaponType.RailGun.Name);
        Upgrade(em, player, UpgradeOption.Damage, WeaponType.RailGun.Name);
        foreach (var chip in new[] { WeaponType.Shotgun, WeaponType.AcidBubbleSpray, WeaponType.FlakCurtain })
            for (int times = 0; times < 2; times++) Upgrade(em, player, UpgradeOption.Range, chip.Name);

        AssertBoost(em, expected: 0);

        Upgrade(em, player, UpgradeOption.FireRate, WeaponType.MachineGun.Name);
        Upgrade(em, player, UpgradeOption.ArmorPierce, WeaponType.GravitonBurst.Name);
        AssertBoost(em, expected: 1);
    }

    // A cargo gun neither fires nor is offered upgrades, so it cannot set the boost either.
    [Fact]
    public void CargoGuns_NeverSetTheBoost()
    {
        var (em, player) = CreateHull(ShipType.Heavy, 9, BookedHeavy, cargo: [WeaponType.RailGun]);
        for (int times = 0; times < 6; times++) Upgrade(em, player, UpgradeOption.Damage, WeaponType.RailGun.Name);

        // The RailGun in cargo is already aboard, so the hull's own least-upgraded heavy mount is what counts.
        AssertBoost(em, expected: 0);
    }

    // D3: every arriving upgrade is one the rules would let the player buy for that gun, and `Damage` appears only
    // at a milestone level (R33). `WeaponUpgrade.LegalStats` is the same gate a bought card passes.
    [Fact]
    public void ArrivingSet_OnlyContainsUpgradesThatGunCouldHaveBeenOffered()
    {
        foreach (var hull in new[] { ShipType.Scout, ShipType.Fighter, ShipType.Shadow, ShipType.Heavy })
            for (int level = 2; level <= 20; level++)
            {
                var (em, player) = CreateHull(hull, level, BookedFor(hull));
                foreach (var gun in MountedNames(em))
                    for (int times = 0; times < 3; times++) Upgrade(em, player, UpgradeOption.Damage, gun);

                // One hand, at exactly this level: the legality of an arriving set is the legality of the
                // level that dealt it, so a sweep over levels is what covers the gates.
                foreach (var card in NewGuns(em, 1))
                {
                    if (card.PreApplied == WeaponPreUpgrade.None) continue;
                    var offered = Gun(card.WeaponName);

                    var arriving = WeaponUpgrade.StatsOf(card.PreApplied);
                    var legal = WeaponUpgrade.LegalStats(offered, TurretMount.CarriedTurret(offered), UpgradeOffer.PierceUnlockedAt(level), CardPower.Base);
                    if (UpgradeOffer.IsMilestoneLevel(level)) legal.Add(UpgradeOption.Damage);

                    foreach (var stat in arriving) Assert.Contains(stat, legal);

                    if (offered.Stats.Hitscan) Assert.DoesNotContain(UpgradeOption.ProjectileSpeed, arriving);
                    if (!UpgradeOffer.PierceUnlockedAt(level)) Assert.DoesNotContain(UpgradeOption.ArmorPierce, arriving);
                    if (!UpgradeOffer.IsMilestoneLevel(level)) Assert.DoesNotContain(UpgradeOption.Damage, arriving);
                }
            }
    }

    // The grant hands over what the card printed: the turret that reaches cargo *is* the one the card describes,
    // and its upgrades are counted on the ship — so the stats screen shows them and the next arriving gun is
    // measured against them.
    [Fact]
    public void TheGrant_GivesTheGunTheCardPromisedAndCountsItsUpgrades()
    {
        var (em, player) = CreateHull(ShipType.Heavy, 9, BookedHeavy);
        foreach (var heavy in new[] { WeaponType.MachineGun, WeaponType.RailGun, WeaponType.GravitonBurst })
            for (int times = 0; times < 4; times++) Upgrade(em, player, UpgradeOption.Damage, heavy.Name);

        var card = FirstUpgradedNewGun(em);
        var gun = Gun(card.WeaponName);

        Assert.True(NewWeaponGrant.TryGrant(em, player, card, out var carried));

        Assert.Equal(WeaponUpgrade.ArrivingTurret(gun, card.PreApplied), carried);
        Assert.True(em.GetComponent<WeaponCargo>(player).Holds(gun.Name), "the arriving gun never reached cargo");

        var counts = em.GetComponent<UpgradeCounts>(player);
        foreach (var stat in WeaponUpgrade.StatsOf(card.PreApplied))
            Assert.Equal(1, counts.GetCount(stat, gun.Name));
    }

    // R23, and D8: a build is only ever dealt for a gun the ship does not own, and once the grant puts one aboard
    // no later hand offers it again — in any build.
    [Fact]
    public void AnOwnedGun_IsNeverOfferedAgain_AsAnyBuild()
    {
        var (em, player) = CreateHull(ShipType.Heavy, 9, BookedHeavy);
        foreach (var heavy in new[] { WeaponType.MachineGun, WeaponType.RailGun, WeaponType.GravitonBurst })
            for (int times = 0; times < 4; times++) Upgrade(em, player, UpgradeOption.Damage, heavy.Name);

        var card = FirstUpgradedNewGun(em);
        Assert.True(NewWeaponGrant.TryGrant(em, player, card, out _));
        Assert.False(NewWeaponGrant.TryGrant(em, player, card, out _), "the ship was handed a second copy of one gun type");

        for (int levelUp = 0; levelUp < 10; levelUp++)
        {
            RunLevelUp(em);
            Assert.DoesNotContain(Hand(em), option => option.IsNewWeapon && option.WeaponName == card.WeaponName);
            ClearHand(em);
        }
    }

    // D4: on a booked hull the same unowned gun may be on offer as two different builds — never as the same build
    // twice. The fixture shrinks the small-gun pool to two unowned chips so the third card has to be a second
    // build of one of them.
    [Fact]
    public void ABookedHull_SeesTheSameGunAsDifferentBuilds_ButNeverTheSameBuildTwice()
    {
        WeaponType[] chipsAboard = [WeaponType.Shotgun, WeaponType.AcidBubbleSpray, WeaponType.FlakCurtain, WeaponType.InterceptorTurret];
        var (em, player) = CreateHull(ShipType.Heavy, 10, BookedHeavy,
            cargo: [WeaponType.FlechetteFan, WeaponType.RippleCutter, WeaponType.Stiletto, WeaponType.Cyclotron]);
        foreach (var mounted in MountedNames(em))
            for (int times = 0; times < 4; times++) Upgrade(em, player, UpgradeOption.Damage, mounted);

        bool sawTwoBuildsOfOneGun = false;
        for (int levelUp = 0; levelUp < 40 && !sawTwoBuildsOfOneGun; levelUp++)
        {
            RunLevelUp(em);
            var newGuns = Hand(em).Where(option => option.IsNewWeapon).ToList();

            Assert.Equal(newGuns.Count, newGuns.Distinct().Count());
            sawTwoBuildsOfOneGun = newGuns.GroupBy(card => card.WeaponName).Any(group => group.Count() > 1);
            ClearHand(em);
        }

        Assert.True(sawTwoBuildsOfOneGun, "a booked hull was never offered two builds of one gun");
    }

    // D6: a milestone hand on a booked hull leads with new guns — and still deals the `Damage` card that grows the
    // guns already aboard.
    [Fact]
    public void MilestoneHand_OnABookedHull_LeadsWithNewGunsAndStillGrowsTheGunsAboard()
    {
        foreach (int level in new[] { 10, 15, 20 })
        {
            var bundles = Hand(ShipType.Heavy, level, BookedHeavy);
            var cards = Cards(bundles);
            var newGuns = cards.Where(card => card.IsNewWeapon).ToList();

            Assert.True(newGuns.Count >= 4, $"L{level}: only {newGuns.Count} new guns in a milestone hand: {Format(bundles)}");
            Assert.Contains(newGuns, card => SizeOf(card) == TurretSize.Large);
            Assert.Contains(newGuns, card => SizeOf(card) == TurretSize.Small);
            Assert.Contains(cards, card => !card.IsNewWeapon && card.Stat == UpgradeOption.Damage);
        }
    }

    // D5: a bundle may carry a large and a small new gun, and both arrive, each with its own build.
    [Fact]
    public void BothNewGunsOfABundle_AreGranted_EachWithItsOwnBuild()
    {
        var (largeCard, smallCard) = FirstTwoNewGunPair();
        var (em, player) = CreateHull(ShipType.Heavy, 10, BookedHeavy);

        Assert.True(NewWeaponGrant.TryGrant(em, player, largeCard, out var large));
        Assert.True(NewWeaponGrant.TryGrant(em, player, smallCard, out var small));

        Assert.Equal(largeCard.WeaponName, large.WeaponName);
        Assert.Equal(smallCard.WeaponName, small.WeaponName);
        Assert.Equal(TurretSize.Large, Gun(large.WeaponName).Size);
        Assert.Equal(TurretSize.Small, Gun(small.WeaponName).Size);
        Assert.Equal(2, em.GetComponent<WeaponCargo>(player).Weapons.Count);
    }

    // The first bundle of a milestone hand that carries a new gun on **both** halves.
    static (UpgradableOption Powerful, UpgradableOption Weak) FirstTwoNewGunPair()
    {
        foreach (int level in new[] { 10, 15, 20 })
            foreach (var bundle in Hand(ShipType.Heavy, level, BookedHeavy))
                if (bundle.Powerful.IsNewWeapon && bundle.Weak.IsNewWeapon)
                    return (bundle.Powerful, bundle.Weak);

        throw new InvalidOperationException($"no milestone hand paired two new guns: expected one at L10/15/20");
    }

    // ---- fixture and helpers (the same shape the other offer tests use) ----

    // Heavy's seven mounts filled: three heavy guns and four chips, so nothing of either size is free.
    static WeaponMount[] BookedHeavy =>
    [
        new(TurretSlotName.MainGun, WeaponType.MachineGun),
        new(TurretSlotName.HeavyLeft, WeaponType.RailGun),
        new(TurretSlotName.HeavyRight, WeaponType.GravitonBurst),
        new(TurretSlotName.SmallLeft, WeaponType.Shotgun),
        new(TurretSlotName.SmallRight, WeaponType.AcidBubbleSpray),
        new(TurretSlotName.SmallBack, WeaponType.FlakCurtain),
        new(TurretSlotName.SmallNose, WeaponType.InterceptorTurret)
    ];

    // A hull dealt its own loadout in full: enough to fill the mounts the hull actually has.
    static WeaponMount[] BookedFor(ShipType hull) => [.. hull.Loadout.Mounts];

    static void AssertBoost(EntityManager em, int expected)
    {
        var large = NewGuns(em, 12).Where(card => SizeOf(card) == TurretSize.Large).ToList();
        Assert.NotEmpty(large);
        Assert.All(large, card => Assert.Equal(expected, WeaponUpgrade.StatsOf(card.PreApplied).Length));
    }

    // Deal `rolls` hands from one seeded world, reading each and clearing it, so a rule that shows up in some hands
    // rather than every one can still be pinned.
    static List<UpgradableOption> NewGuns(EntityManager em, int rolls)
    {
        var cards = new List<UpgradableOption>();
        for (int roll = 0; roll < rolls; roll++)
        {
            RunLevelUp(em);
            cards.AddRange(Hand(em).Where(option => option.IsNewWeapon));
            ClearHand(em);
        }

        return cards;
    }

    static UpgradableOption FirstUpgradedNewGun(EntityManager em) =>
        NewGuns(em, 30).First(card => card.PreApplied != WeaponPreUpgrade.None);

    static UpgradeBundle[] Hand(ShipType hull, int level, WeaponMount[] mounts)
    {
        var (em, _) = CreateHull(hull, level, mounts);
        var view = new WorldView(em);
        Assert.True(view.GetEntitiesWithComponents<Player>().TryFirst(out var playerTuple));
        return UpgradeOffer.Roll(view, playerTuple.Entity, level, "test") ?? throw new InvalidOperationException("a hull with turrets must be dealt an offer");
    }

    // Both halves of the hand on screen; a level that dealt nothing leaves no hand.
    static UpgradableOption[] Hand(EntityManager em)
    {
        var pending = em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().ToList();
        Assert.True(pending.Count <= 1, "more than one pending hand");
        return pending.Count == 0 ? [] : Cards(pending[0].Value2);
    }

    static void RunLevelUp(EntityManager em)
    {
        var commands = new CommandBuffer();
        new LevelUpSystem().Update(new WorldView(em), 1f / 120f, commands);
        commands.Apply(em);
    }

    static void ClearHand(EntityManager em)
    {
        foreach (var (entity, _) in em.GetEntitiesWithComponents<PendingChoice>().ToList())
            em.DestroyEntity(entity);
    }

    static UpgradableOption[] Cards(PendingUpgradeOptions options) => Cards(options.Bundles);

    static UpgradableOption[] Cards(UpgradeBundle[] bundles) =>
        [.. bundles.SelectMany(bundle => new[] { bundle.Powerful, bundle.Weak })];

    static void Upgrade(EntityManager em, Entity player, UpgradeOption stat, string weaponName)
    {
        var counts = em.TryGetComponent<UpgradeCounts>(player, out var held) ? held : UpgradeCounts.Empty;
        em.AddComponent(player, counts.Increment(stat, weaponName));
    }

    static List<string> MountedNames(EntityManager em) =>
        [.. TurretMount.MountedSlots(new WorldView(em)).Select(slot => slot.WeaponName)];

    static TurretSize SizeOf(UpgradableOption card) => Gun(card.WeaponName).Size;

    // The table entry a card names. `FromName` answers nullable because a card may name a hand-built test gun.
    static WeaponType Gun(string weaponName) => WeaponType.FromName(weaponName)!.Value;

    static string Format(IEnumerable<UpgradeBundle> bundles) =>
        string.Join(" | ", bundles.Select(bundle => $"{UpgradeOffer.FormatChoice(bundle.Powerful)} + {UpgradeOffer.FormatChoice(bundle.Weak)}"));

    // (em, player). `Level: level - 1` makes the first `RunLevelUp` the hand the player is dealt on reaching
    // `level`, and the XP is far past any later level so a test may deal hand after hand from one world; a test
    // that wants exactly one hand calls `UpgradeOffer.Roll` with the level it means.
    static (EntityManager Em, Entity Player) CreateHull(ShipType hull, int level, WeaponMount[]? mounts = null, WeaponType[]? cargo = null)
    {
        var em = new EntityManager(EntityManager.DefaultRngSeed);

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Rotation(0f));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f,
            MaxHealth: 20, Radius: 40f, Xp: 999_999, Level: level - 1, PickupRadius: 150f, RotationSpeed: 1f));
        em.AddComponent(player, WeaponCargo.Empty);
        em.AddComponent(player, UpgradeCounts.Empty);

        foreach (var slot in hull.Slots)
            em.AddComponent(em.CreateEntity(), new TurretSlot(slot));

        foreach (var mount in mounts ?? [.. hull.Loadout.Mounts])
        {
            var view = new WorldView(em);
            Assert.True(TurretMount.TryFindSlot(view, mount.SlotName, out var slotEntity));
            TurretMount.Mount(em, slotEntity, view.GetComponent<TurretSlot>(slotEntity).Definition, mount.Weapon, Vector2.Zero, hullRotation: 0f);
        }

        var carried = WeaponCargo.Empty;
        foreach (var gun in cargo ?? [])
            carried = carried.With(TurretMount.CarriedTurret(gun));
        em.AddComponent(player, carried);

        return (em, player);
    }
}
