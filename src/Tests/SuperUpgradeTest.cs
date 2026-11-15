using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Spacevors.Game;
using Xunit;

namespace Tests;

// Two halves of every hand are the same upgrade at **twice** the step (plans/SUPER_UPGRADES.md): one bundle's
// powerful half and a different bundle's weak half. So the grade has to hold the line on three things: the shape
// of the spike in a hand, the legality gates read at the doubled step rather than the ordinary one (R10/R17/R18/
// R19/R29), and the one number a card prints, gates and grants with. `HandCompositionTest` owns the rest of the
// hand shape and `LevelUpSlotOfferTest` the ordinary per-card gates.
public class SuperUpgradeTest
{
    static readonly ShipType[] Hulls = [ShipType.Scout, ShipType.Fighter, ShipType.Shadow, ShipType.Falcon, ShipType.Eagle, ShipType.Manta, ShipType.Raptor, ShipType.Heavy];

    // The promise: one spiked half on each side of the hand, and never both in one bundle — which is what makes
    // the spike a trade rather than a bigger bundle.
    [Fact]
    public void Hand_SpikesOnePowerfulHalfAndOneWeakHalf_InDifferentBundles()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
            {
                var hand = Hand(hull, level);
                int strong = hand.Count(bundle => bundle.Powerful.IsSuper);
                int weak = hand.Count(bundle => bundle.Weak.IsSuper);

                Assert.True(strong <= 1 && weak <= 1, $"{hull} L{level}: more than one spike on a side: {Names(hand)}");
                Assert.True(hand.All(bundle => !(bundle.Powerful.IsSuper && bundle.Weak.IsSuper)),
                    $"{hull} L{level}: one bundle carries both spikes: {Names(hand)}");
                Assert.True(strong == 0 || weak == 0 || IndexOfSpike(hand, true) != IndexOfSpike(hand, false),
                    $"{hull} L{level}: both spikes in one bundle: {Names(hand)}");
                Assert.True(UpgradeOffer.IsMilestoneLevel(level) || (strong == 1 && weak == 1),
                    $"{hull} L{level}: an ordinary hand must spike both sides: {Names(hand)}");

                // The one legitimate spikeless hand: a milestone whose every card is one the rules refuse to
                // double — a `Damage` card, whose step is the milestone's own (D3), or a new gun arriving plain
                // because a mount it fits is still free (D5 and pre-upgrade D1).
                Assert.True(strong + weak > 0 || Cards(hand).All(SpikelessBecause),
                    $"{hull} L{level}: a hand with something to spike dealt none: {Names(hand)}");
            }
    }

    static bool SpikelessBecause(UpgradableOption card) =>
        card.Stat == UpgradeOption.Damage || (card.IsNewWeapon && card.PreApplied == WeaponPreUpgrade.None);

    // Grading a card makes it a **different card**, which is what keeps the shipped distinctness rules honest:
    // a hand may hold the ordinary and the super version of one stat as two real offers.
    [Fact]
    public void GradeIsPartOfCardIdentity_SoASuperCardIsNeverTheSameCardAsItsTwin()
    {
        var ordinary = new UpgradableOption(WeaponType.MachineGun.Name, UpgradeOption.Range);
        var super = ordinary with { Power = CardPower.Super };

        Assert.NotEqual(ordinary, super);
        Assert.False(ordinary.IsSuper);
        Assert.True(super.IsSuper);
    }

    // The gate is read at the step the card actually grants, never at the constant (D6). Each of these is legal
    // for an ordinary card and refused for a super one.
    [Fact]
    public void SuperCard_IsRefusedWhereTheDoubledStepWouldBreakAGate()
    {
        var chipAtRoof = TurretMount.CarriedTurret(WeaponType.Stiletto) with { Range = 1150f };
        Assert.DoesNotContain(UpgradeOption.Range, Legal(WeaponType.Stiletto, chipAtRoof, CardPower.Super));
        Assert.Contains(UpgradeOption.Range, Legal(WeaponType.Stiletto, chipAtRoof, CardPower.Base));

        var roundNearCeiling = TurretMount.CarriedTurret(WeaponType.SeekerPod) with { Weapon = WeaponType.SeekerPod.Stats with { MissileHealth = 9 } };
        Assert.DoesNotContain(UpgradeOption.MissileHealth, Legal(WeaponType.SeekerPod, roundNearCeiling, CardPower.Super));
        Assert.Contains(UpgradeOption.MissileHealth, Legal(WeaponType.SeekerPod, roundNearCeiling, CardPower.Base));

        var gunWithFourPierce = TurretMount.CarriedTurret(WeaponType.RailGun) with { Weapon = WeaponType.RailGun.Stats with { Pierce = 4 } };
        Assert.DoesNotContain(UpgradeOption.ArmorPierce, Legal(WeaponType.RailGun, gunWithFourPierce, CardPower.Super));
        Assert.Contains(UpgradeOption.ArmorPierce, Legal(WeaponType.RailGun, gunWithFourPierce, CardPower.Base));

        // R17/R18: a guided volley with room for exactly one more shot is not offered a two-shot card.
        var podAtBudget = TurretMount.CarriedTurret(WeaponType.Swarmlet) with { Weapon = WeaponType.Swarmlet.Stats with { PelletCount = 5 } };
        Assert.DoesNotContain(UpgradeOption.FireRate, Legal(WeaponType.Swarmlet, podAtBudget, CardPower.Super));
    }

    // A dealt hand never spikes a card its own gun could not take at the doubled step — the same rule checked
    // against the live hull rather than against a hand-built turret.
    [Fact]
    public void Hand_NeverSpikesACardTheRulesWouldRefuseToHonour()
    {
        var (em, _) = CreateHull(ShipType.Heavy, 10, BookedHeavy);
        var hand = DealHand(em, 10);

        foreach (var card in Cards(hand).Where(card => card.IsSuper && card.WeaponName.Length > 0 && !card.IsNewWeapon))
        {
            var live = MountedTurret(em, card.WeaponName);
            Assert.Contains(card.Stat, WeaponUpgrade.LegalStats(WeaponType.FromName(card.WeaponName), live, UpgradeOffer.PierceUnlockedAt(10), CardPower.Super));
        }
    }

    // A super card is the same upgrade at twice the step: the multiplier cards double their **delta**, so the
    // ×1.15 card becomes exactly ×1.30 — the top of R10's band, not a third card size — and the shot cards double
    // their shots. The grant reads the definition the card printed (R10).
    [Fact]
    public void SuperCard_GrantsTwiceTheOrdinaryStep_AndPrintsThatNumber()
    {
        var machineGun = TurretMount.CarriedTurret(WeaponType.MachineGun);

        var range = Stat(WeaponType.MachineGun, UpgradeOption.Range);
        Assert.Equal("+15%", range.Step.DisplayValue);
        Assert.Equal("+30%", (range with { Power = CardPower.Super }).Step.DisplayValue);
        Assert.Equal(machineGun.Range * 1.30f, WeaponUpgrade.ApplyStat(machineGun, range with { Power = CardPower.Super }).Range, 3);

        var pierce = Stat(WeaponType.RailGun, UpgradeOption.ArmorPierce);
        Assert.Equal(UpgradeDefinition.ArmorPierce.Additive * 2, (pierce with { Power = CardPower.Super }).Step.Additive);
        Assert.Equal(8, WeaponUpgrade.ApplyStat(TurretMount.CarriedTurret(WeaponType.RailGun), pierce with { Power = CardPower.Super }).Weapon.Pierce);

        // A gun whose volley grows buys two shots, and the card says so in shots rather than percent.
        var volley = Stat(WeaponType.ScatterCannon, UpgradeOption.FireRate);
        Assert.Equal(2, (volley with { Power = CardPower.Super }).SalvoShots);
        Assert.Equal(TurretMount.CarriedTurret(WeaponType.ScatterCannon).Weapon.PelletCount + 2,
            WeaponUpgrade.ApplyStat(TurretMount.CarriedTurret(WeaponType.ScatterCannon), volley with { Power = CardPower.Super }).Weapon.PelletCount);
    }

    // The percent cards are doubled as a percent of the table base, not as a doubled integer: a SeekerPod's 4 hp
    // round takes +4 from a super card and +2 from an ordinary one, and the ceiling still bounds both.
    [Fact]
    public void SuperRoundHealthCard_IsTheDoubledPercentOfTheBaseRound_NotTheDoubledStep()
    {
        Assert.Equal(2, WeaponType.SeekerPod.MissileHealthStepFor(CardPower.Base));
        Assert.Equal(4, WeaponType.SeekerPod.MissileHealthStepFor(CardPower.Super));

        var seeker = TurretMount.CarriedTurret(WeaponType.SeekerPod);
        var card = new UpgradableOption(WeaponType.SeekerPod.Name, UpgradeOption.MissileHealth, Power: CardPower.Super);

        Assert.Equal(seeker.Weapon.MissileHealth + 4, WeaponUpgrade.ApplyStat(seeker, card).Weapon.MissileHealth);
    }

    // D3: the super grade and the milestone tier are the same number, so they never stack. An ordinary level's
    // super defence card is the milestone step, and a milestone hand never spikes a defence card at all.
    [Fact]
    public void DefenceCard_IsSuperOnlyOnAnOrdinaryLevel_WhereItsStepIsTheMilestoneStep()
    {
        var ordinary = new UpgradableOption("", UpgradeOption.Armor, Tier: DefenceTier.Ordinary);
        var superOrdinary = ordinary with { Power = CardPower.Super };
        Assert.Equal(1, ordinary.Step.Additive);
        Assert.Equal(2, superOrdinary.Step.Additive);
        Assert.Equal(UpgradeDefinition.DefenceStep(UpgradeOption.Armor, DefenceTier.Milestone), superOrdinary.Step.Additive);

        foreach (int level in new[] { 6, 8, 9, 11, 12 })
            AssertSpikes(Hand(ShipType.Fighter, level), level);

        foreach (int level in new[] { 5, 10, 15, 20 })
            foreach (var card in Cards(Hand(ShipType.Fighter, level)))
                Assert.False(card.IsSuper && card.Stat is UpgradeOption.Armor or UpgradeOption.Shield,
                    $"L{level}: a milestone hand spiked a defence card: {Names(Hand(ShipType.Fighter, level))}");
    }

    // The ceiling is read at the doubled step: a hull at armor 11 may still be sold the ordinary +1 card, and must
    // not be sold a +2 card that would round off to +1.
    [Fact]
    public void SuperDefenceCard_IsWithdrawnWhenTheDoubledStepNoLongerFitsTheHeadroom()
    {
        var (em, player) = CreateHull(ShipType.Fighter, 6);
        em.AddComponent(player, new Armor(11));

        var hand = DealHand(em, 6);
        Assert.Contains(hand, bundle => bundle.Powerful.Stat == UpgradeOption.Armor && !bundle.Powerful.IsSuper);
        Assert.DoesNotContain(Cards(hand), card => card.Stat == UpgradeOption.Armor && card.IsSuper);
    }

    // D5: a spiked new gun arrives with as many upgrades as the **least** upgraded gun it would displace — three
    // here — where an ordinary one arrives with half of that rounded up, and both are still capped by what the gun
    // may legally carry.
    [Fact]
    public void SuperNewGunArrivesWithAsManyUpgradesAsTheLeastUpgradedGunItDisplaces()
    {
        var (em, player) = CreateHull(ShipType.Heavy, 10, BookedHeavy);
        foreach (var gun in new[] { WeaponType.MachineGun, WeaponType.RailGun, WeaponType.GravitonBurst })
            for (int times = 0; times < 3; times++) Upgrade(em, player, UpgradeOption.Damage, gun.Name);

        var largeGuns = Cards(DealHand(em, 10))
            .Where(card => card.IsNewWeapon && Gun(card.WeaponName).Size == TurretSize.Large).ToList();
        Assert.NotEmpty(largeGuns);
        Assert.Contains(largeGuns, card => card.IsSuper);

        foreach (var card in largeGuns)
        {
            // `+1` because a milestone arriving set may also carry the `Damage` upgrade (R33).
            int mayCarry = WeaponUpgrade.LegalStats(Gun(card.WeaponName), TurretMount.CarriedTurret(Gun(card.WeaponName)),
                UpgradeOffer.PierceUnlockedAt(10), CardPower.Base).Count + 1;
            int expected = Math.Min(card.IsSuper ? 3 : 2, mayCarry);

            Assert.Equal(expected, WeaponUpgrade.StatsOf(card.PreApplied).Length);
        }
    }

    // D9: a bench entry opts into the grade with a trailing `*`, and the two rules the deal refuses are refused
    // in the bench too — so a benched card can never print one step and grant another.
    [Fact]
    public void BenchEntry_NamesTheGradeWithAStar_AndRefusesTheCardsTheDealWouldRefuse()
    {
        Assert.Equal(CardPower.Super, UpgradeOffer.ParseEntry("FireRate:MachineGun*")?.Power);
        Assert.Equal(CardPower.Base, UpgradeOffer.ParseEntry("FireRate:MachineGun")?.Power);
        Assert.Null(UpgradeOffer.ParseEntry("Damage:MachineGun*"));

        var milestoneArmor = UpgradeOffer.StampDefenceTier(new UpgradableOption("", UpgradeOption.Armor, Power: CardPower.Super), 10);
        Assert.Equal(CardPower.Base, milestoneArmor.Power);
        Assert.Equal(DefenceTier.Milestone, milestoneArmor.Tier);

        var ordinaryArmor = UpgradeOffer.StampDefenceTier(new UpgradableOption("", UpgradeOption.Armor, Power: CardPower.Super), 11);
        Assert.Equal(CardPower.Super, ordinaryArmor.Power);
        Assert.Equal(2, ordinaryArmor.Step.Additive);
    }

    // A super card is still one **card**: it counts once on the ship, and the doubling is in the step. The numbers
    // are written out rather than recomputed from the definition, so the card cannot drift from the grant.
    [Fact]
    public void SuperCard_GrantsTheDoubledHullStepAndCountsAsOneCard()
    {
        var (em, player) = CreateHull(ShipType.Fighter, 6);

        GameSession.ApplyUpgrade(em, player, new UpgradableOption("", UpgradeOption.PickupRadius, Power: CardPower.Super));

        Assert.Equal(210f, em.GetComponent<Player>(player).PickupRadius, 3);   // 150 × 1.4
        Assert.Equal(1, em.GetComponent<UpgradeCounts>(player).GetCount(UpgradeOption.PickupRadius, ""));
    }

    // The card that was dealt and counted but never applied — the `PickupRadius` case went missing in the
    // pre-upgrade refactor and nothing noticed. Pinned here so it cannot go missing again.
    [Fact]
    public void PickupRadiusCard_GrowsTheMagnetField()
    {
        var (em, player) = CreateHull(ShipType.Fighter, 6);
        float before = em.GetComponent<Player>(player).PickupRadius;

        GameSession.ApplyUpgrade(em, player, new UpgradableOption("", UpgradeOption.PickupRadius));

        Assert.Equal(180f, em.GetComponent<Player>(player).PickupRadius, 3);   // 150 × 1.2
        Assert.NotEqual(before, em.GetComponent<Player>(player).PickupRadius);
    }

    static void AssertSpikes(UpgradeBundle[] hand, int level)
    {
        Assert.Contains(hand, bundle => bundle.Powerful.IsSuper);
        Assert.Contains(hand, bundle => bundle.Weak.IsSuper);
    }

    static int IndexOfSpike(UpgradeBundle[] hand, bool powerful)
    {
        for (int index = 0; index < hand.Length; index++)
            if ((powerful ? hand[index].Powerful : hand[index].Weak).IsSuper) return index;

        return -1;
    }

    static List<UpgradeOption> Legal(WeaponType gun, Turret live, CardPower power) =>
        WeaponUpgrade.LegalStats(gun, live, pierceUnlocked: true, power);

    static UpgradableOption Stat(WeaponType gun, UpgradeOption stat) => new(gun.Name, stat);

    static Turret MountedTurret(EntityManager em, string weaponName)
    {
        var mounted = TurretMount.MountedSlots(new WorldView(em)).First(slot => slot.WeaponName == weaponName);
        return em.GetComponent<Turret>(mounted.SlotEntity);
    }

    // Both halves of the dealt hand.
    static UpgradableOption[] Cards(UpgradeBundle[] bundles) =>
        [.. bundles.SelectMany(bundle => new[] { bundle.Powerful, bundle.Weak })];

    static string Names(UpgradeBundle[] bundles) =>
        string.Join(" | ", bundles.Select(bundle => $"{UpgradeOffer.FormatChoice(bundle.Powerful)} + {UpgradeOffer.FormatChoice(bundle.Weak)}"));

    static UpgradeBundle[] Hand(ShipType hull, int level, WeaponMount[]? mounts = null)
    {
        var (em, _) = CreateHull(hull, level, mounts);
        return DealHand(em, level);
    }

    static UpgradeBundle[] DealHand(EntityManager em, int level)
    {
        var view = new WorldView(em);
        Assert.True(view.GetEntitiesWithComponents<Player>().TryFirst(out var playerTuple));
        return UpgradeOffer.Roll(view, playerTuple.Entity, level, "test") ?? throw new InvalidOperationException("a hull with turrets must be dealt an offer");
    }

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

    static (EntityManager Em, Entity Player) CreateHull(ShipType hull, int level, WeaponMount[]? mounts = null)
    {
        var em = new EntityManager(EntityManager.DefaultRngSeed);

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Rotation(0f));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f,
            MaxHealth: 20, Radius: 40f, Xp: UpgradeStats.XpForLevel(level - 1), Level: level - 1, PickupRadius: 150f, RotationSpeed: 1f));
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

        return (em, player);
    }

    static void Upgrade(EntityManager em, Entity player, UpgradeOption stat, string weaponName)
    {
        var counts = em.TryGetComponent<UpgradeCounts>(player, out var held) ? held : UpgradeCounts.Empty;
        em.AddComponent(player, counts.Increment(stat, weaponName));
    }

    static WeaponType Gun(string weaponName) => WeaponType.FromName(weaponName)!.Value;
}
