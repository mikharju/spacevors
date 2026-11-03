using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// What a level's offer is made of (plans/UPGRADE_BUNDLE_PAIRS.md). The legal cards are sorted into kinds and
// drained into two pools whose **order** carries the promises; five bundles are cut from those pools, each one
// powerful half and one weak half, and the player takes one bundle. So these are the shape rules: the pair is
// always one of each side, a promised card (a new large gun, a `Damage` card per class at a milestone) shows up
// while it is legal, neither half is disabled by the other, and a pool that runs dry deals fewer bundles rather
// than a broken pair. The per-card legality rules (R18/R19/R23/R29/R33) are guarded by `LevelUpSlotOfferTest`
// and `RerollOfferTest`, and what a taken bundle does by `BundleChoiceTest`.
public class HandCompositionTest
{
    // Every hull shape, over enough levels to cross the milestone beats at 5, 10, 15 and 20.
    static readonly ShipType[] Hulls = [ShipType.Scout, ShipType.Fighter, ShipType.Shadow, ShipType.Falcon, ShipType.Eagle, ShipType.Manta, ShipType.Raptor, ShipType.Heavy];

    // The invariant the whole design balances on: a bundle is one powerful card and one weak card, on every
    // hull, at every level, milestone or not.
    [Fact]
    public void EveryBundle_IsOnePowerfulHalfAndOneWeakHalf()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
                foreach (var bundle in Hand(hull, level))
                {
                    Assert.True(UpgradeOffer.IsPowerfulCard(bundle.Powerful),
                        $"{hull} L{level}: strong half {UpgradeOffer.FormatChoice(bundle.Powerful)} is not a powerful card");
                    Assert.False(UpgradeOffer.IsPowerfulCard(bundle.Weak),
                        $"{hull} L{level}: weak half {UpgradeOffer.FormatChoice(bundle.Weak)} is not a weak card");
                }
    }

    // A hand of one or two choices is the complaint this change answers, so the full five bundles are demanded
    // wherever the legal pools can fill them: every hull, from the level both defence cards are legal.
    [Fact]
    public void Hand_IsFiveBundles_OnceBothDefenceCardsAreLegal()
    {
        foreach (var hull in Hulls)
            for (int level = 10; level <= 21; level++)
            {
                var hand = Hand(hull, level);
                Assert.True(hand.Length == UpgradeOffer.BundlesPerLevel, $"{hull} L{level}: only {hand.Length} bundles: {Names(hand)}");
            }
    }

    // From the level armour joins the strong pool a hand is at most one bundle short — the strong side is still
    // limited to the kill gun's cards plus one defence card.
    [Fact]
    public void Hand_IsNearlyFull_FromTheLevelArmorUnlocks()
    {
        foreach (var hull in Hulls)
            for (int level = 5; level <= 9; level++)
                Assert.InRange(Hand(hull, level).Length, 4, UpgradeOffer.BundlesPerLevel);
    }

    // Never more bundles than there are cards to fill them with, and never a bundle that breaks a rule just to
    // reach the number.
    [Fact]
    public void Hand_IsNeverEmptyAndNeverLongerThanTheHand()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
                Assert.InRange(Hand(hull, level).Length, 1, UpgradeOffer.BundlesPerLevel);
    }

    // One new gun per bundle: the mount screen settles one weapon, so a pair of two would need two trips
    // (plans/UPGRADE_BUNDLE_PAIRS.md §3.7). Nothing else can go wrong inside a pair — the weak half is applied
    // before the strong half is mounted, so an upgrade spent on a gun the new gun displaces rides into cargo
    // with that gun rather than vanishing.
    [Fact]
    public void Bundle_NeverCarriesTwoNewGuns()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
                foreach (var bundle in Hand(hull, level))
                    Assert.False(bundle.Powerful.IsNewWeapon && bundle.Weak.IsNewWeapon,
                        $"{hull} L{level}: {Names(bundle)} — a bundle carries one new gun at most");
    }

    // A hull with nothing powerful to offer — its only gun is a chip and its large mount is taken — is still
    // dealt one bundle of two ordinary cards, so the level it earned resolves instead of hanging on an empty
    // choice forever.
    [Fact]
    public void ChipOnlyHull_WithNothingPowerfulToOffer_IsStillDealtOneBundle()
    {
        WeaponMount[] chipOnly = [new(TurretSlotName.MainGun, WeaponType.Stiletto)];

        for (int seed = 1; seed <= 5; seed++)
        {
            var bundles = Hand(ShipType.Fighter, 3, chipOnly, seed);
            Assert.Single(bundles);
            Assert.False(UpgradeOffer.IsPowerfulCard(bundles[0].Powerful), "there was nothing powerful to offer");
            Assert.False(bundles[0].Powerful.IsNewWeapon && bundles[0].Weak.IsNewWeapon);
        }
    }

    // One side of the hand never names one gun past its cap: three cards for the one kill gun is the ceiling on
    // the strong side, two for a chip gun on the weak side.
    [Fact]
    public void Hand_NeverNamesOneGunPastItsSideOfTheCap()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
            {
                var bundles = Hand(hull, level);
                Assert.True(HighestCountForOneGun(bundles.Select(b => b.Powerful)) <= 3,
                    $"{hull} L{level}: the strong half named one gun too often: {Names(bundles)}");
                Assert.True(HighestCountForOneGun(bundles.Select(b => b.Weak)) <= 2,
                    $"{hull} L{level}: the weak half named one chip gun three times: {Names(bundles)}");
            }
    }

    static int HighestCountForOneGun(IEnumerable<UpgradableOption> cards) =>
        cards.Where(card => card.WeaponName.Length > 0).GroupBy(card => card.WeaponName).Select(g => g.Count()).DefaultIfEmpty(0).Max();

    // The hull grows one way per hand: `Hp` at most once, armour and shield at most once each — and the
    // engine/vacuum filler at most twice, so a hand is never all thrust.
    [Fact]
    public void Hand_CapsHullAndUtilityCards()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
            {
                var cards = Cards(Hand(hull, level));
                Assert.True(cards.Count(card => card.Stat == UpgradeOption.Hp) <= 1, $"{hull} L{level}: two hull cards in one hand");
                Assert.True(cards.Count(card => card.Stat == UpgradeOption.Armor) <= 1, $"{hull} L{level}: two armor cards in one hand");
                Assert.True(cards.Count(card => card.Stat == UpgradeOption.Shield) <= 1, $"{hull} L{level}: two shield cards in one hand");
                Assert.True(cards.Count(IsUtilityCard) <= 2, $"{hull} L{level}: the hand is mostly filler: {Names(cards)}");
            }
    }

    // Both classes grow at every level: while the hull flies a kill gun and a chip gun, the strong half of some
    // bundle names a kill gun and the weak half of some bundle names a chip gun. This is R25's class line made
    // visible in the offer.
    [Fact]
    public void Hand_GrowsBothClassesTheHullFlies()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
            {
                var bundles = Hand(hull, level);
                if (!FliesBothClasses(hull)) continue;

                Assert.Contains(bundles, bundle => NamesAKillGun(bundle.Powerful));
                Assert.Contains(bundles, bundle => NamesAChipGun(bundle.Weak));
            }
    }

    // R33: the `Damage` card stays milestone-only. Ordinary hands grow the guns with stat cards.
    [Fact]
    public void OrdinaryHand_NeverCarriesADamageCard()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
            {
                if (UpgradeOffer.IsMilestoneLevel(level)) continue;
                Assert.DoesNotContain(Cards(Hand(hull, level)), card => card.Stat == UpgradeOption.Damage && !card.IsNewWeapon);
            }
    }

    // A milestone hand is the build beat: a new large gun to switch into even on a hull whose mounts are all
    // booked, and a `Damage` card for each class aboard — the kill card as a strong half, the chip card as the
    // weak half of another bundle.
    [Fact]
    public void MilestoneHand_OffersANewLargeGunAndADamageCardPerClassAboard()
    {
        foreach (var hull in Hulls)
            foreach (int level in new[] { 5, 10, 15, 20 })
            {
                var bundles = Hand(hull, level);
                Assert.Contains(bundles, bundle => bundle.Powerful.IsNewWeapon && WeaponType.FromName(bundle.Powerful.WeaponName) is { IsSupport: false });

                if (FliesAKillGun(hull))
                    Assert.True(bundles.Any(bundle => IsDamageCard(bundle.Powerful)), $"{hull} L{level}: no kill-gun damage card: {Names(bundles)}");
                if (FliesAChipGun(hull))
                    Assert.True(bundles.Any(bundle => IsDamageCard(bundle.Weak)), $"{hull} L{level}: no chip-gun damage card: {Names(bundles)}");
            }
    }

    // A milestone hand's strong half is always the build beat — a new gun, a `Damage` card, or the doubled
    // defence tier. It never falls back to an ordinary stat card: R33 keeps `Damage` milestone-only and the
    // strong pool has no stat fallback at all.
    [Fact]
    public void MilestoneHand_NeverDealsAnOrdinaryStatCardAsItsStrongHalf()
    {
        foreach (var hull in Hulls)
            foreach (int level in new[] { 5, 10, 15, 20 })
                foreach (var bundle in Hand(hull, level))
                    Assert.True(
                        bundle.Powerful.IsNewWeapon || IsDamageCard(bundle.Powerful)
                            || bundle.Powerful.Stat is UpgradeOption.Armor or UpgradeOption.Shield,
                        $"L{level} strong half {UpgradeOffer.FormatChoice(bundle.Powerful)} is not a milestone card");
    }

    // A milestone hand never grows a kill gun with an ordinary stat card: its strong half is the build beat —
    // a new gun, a `Damage` card, the doubled defence tier — and its weak half is the chip line (a new small
    // gun, the chip's `Damage` card, then the documented fallback). Kill-gun stat cards are what ordinary levels
    // are made of.
    [Fact]
    public void MilestoneHand_NeverDealsAKillGunStatCard()
    {
        foreach (var hull in Hulls)
            foreach (int level in new[] { 5, 10, 15, 20 })
                Assert.DoesNotContain(Cards(Hand(hull, level)), NamesAKillGunStatCard);
    }

    // When the milestone's own weak kinds have run dry — no chip gun aboard, so no `Damage` chip card, and a
    // new small gun that no kill gun's card may pair with — the weak halves fall back to the ordinary pool
    // rather than pairing two powerful halves.
    [Fact]
    public void MilestoneHand_WhenItsWeakPoolRunsDry_FallsBackToEngineCards()
    {
        // A hull flying only large kill guns, so no chip gun can hold a chip `Damage` card.
        WeaponMount[] killOnly =
        [
            new(TurretSlotName.MainGun, WeaponType.MachineGun),
            new(TurretSlotName.HeavyLeft, WeaponType.RailGun),
            new(TurretSlotName.HeavyRight, WeaponType.ScatterCannon),
        ];

        for (int seed = 1; seed <= 6; seed++)
        {
            var bundles = Hand(ShipType.Heavy, 20, killOnly, seed);

            foreach (var bundle in bundles)
            {
                Assert.True(UpgradeOffer.IsPowerfulCard(bundle.Powerful), $"strong half {UpgradeOffer.FormatChoice(bundle.Powerful)} is not powerful");
                Assert.False(UpgradeOffer.IsPowerfulCard(bundle.Weak), $"weak half {UpgradeOffer.FormatChoice(bundle.Weak)} is not weak");
            }

            Assert.Contains(Cards(bundles), IsUtilityCard);
        }
    }

    // The spill must never reach past a gate the rules already closed (R18/R19/R29), however short its pool is:
    // a chip at its class range roof is dealt no range card.
    [Fact]
    public void Hand_NeverDealsACardTheRulesWouldRefuse()
    {
        WeaponMount[] chipAtItsRoof = [new(TurretSlotName.SmallLeft, WeaponType.Stiletto)];

        for (int level = 2; level <= 21; level++)
        {
            var cards = Cards(Hand(ShipType.Fighter, level, chipAtItsRoof));

            Assert.DoesNotContain(cards, card => card.Stat == UpgradeOption.Range && card.WeaponName == WeaponType.Stiletto.Name);
            Assert.DoesNotContain(cards, card => card.Stat == UpgradeOption.ProjectileSpeed && card.WeaponName == WeaponType.Stiletto.Name);
            Assert.DoesNotContain(cards, card => card.Stat == UpgradeOption.MissileHealth && card.WeaponName == WeaponType.Stiletto.Name);
        }
    }

    // Armor and its counter unlock with tier 2 — no bundle below level 5 carries either card
    // (plans/ARMOR_AND_PIERCE.md).
    [Fact]
    public void NoArmorOrPierceCardBeforeLevelFive()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 4; level++)
                Assert.DoesNotContain(Cards(Hand(hull, level)), card => card.Stat is UpgradeOption.Armor or UpgradeOption.ArmorPierce);
    }

    // From level 5 the Armor card is in the strong pool and shows across a run — deterministic seeds, so this
    // proves the pool is wired in, not a probability claim.
    [Fact]
    public void ArmorCard_IsDealtSomewhereInAFullRunFromLevelFive()
    {
        for (int level = 5; level <= 21; level++)
            if (Cards(Hand(ShipType.Fighter, level)).Any(card => card.Stat == UpgradeOption.Armor))
                return;

        Assert.Fail("no hand from level 5 up offered Armor");
    }

    // Pierce is a per-gun stat card, dealt for a mounted gun from level 5 on ordinary hands. It shares the kill
    // pool with the rate/range/speed cards, so the sweep runs over seeds as well as levels: the same seed at the
    // same level deals the same hand.
    [Fact]
    public void PierceCard_IsDealtForAMountedGun_FromLevelFive()
    {
        for (int level = 6; level <= 9; level++)
            for (int seed = 1; seed <= 8; seed++)
                if (Cards(Hand(ShipType.Fighter, level, seed: seed))
                        .Any(card => card.Stat == UpgradeOption.ArmorPierce && card.WeaponName == WeaponType.MachineGun.Name))
                    return;

        Assert.Fail("no hand from level 6 up offered MachineGun armor pierce");
    }

    // A defence card is never a lie (R10/R19's rule): it is dealt only while its **tier's whole step** still
    // fits under the ceiling. At armour 11 the ordinary +1 card is still dealt and the milestone +2 card is not;
    // at armour 12 neither is, and the strong pool carries no defence card at all.
    [Fact]
    public void DefenceCard_WithdrawnWhenItsStepNoLongerFitsTheHeadroom()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: UpgradeStats.XpForLevel(6), level: 6);
        em.AddComponent(player, new Armor(ArmorRules.ArmorCeiling - 1));

        Assert.Contains(Cards(DealHand(em, 7)), card => card.Stat == UpgradeOption.Armor);
        Assert.DoesNotContain(Cards(DealHand(em, 10)), card => card.Stat == UpgradeOption.Armor);

        em.AddComponent(player, new Armor(ArmorRules.ArmorCeiling));
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Assert.DoesNotContain(Cards(DealHand(em, 7)), card => card.Stat == UpgradeOption.Armor);
            Assert.DoesNotContain(Cards(DealHand(em, 10)), card => card.Stat == UpgradeOption.Armor);
        }
    }

    // The tier is the level's, not the card's: every defence card in a milestone hand carries the doubled step,
    // every one in an ordinary hand the half step, and no hand mixes the two (plans/UPGRADE_BUNDLE_PAIRS.md §2).
    [Fact]
    public void DefenceCards_CarryTheTierOfTheLevelThatDealtThem()
    {
        foreach (var hull in Hulls)
            for (int level = 10; level <= 21; level++)
            {
                bool milestone = UpgradeOffer.IsMilestoneLevel(level);
                var expected = milestone ? DefenceTier.Milestone : DefenceTier.Ordinary;

                foreach (var card in Cards(Hand(hull, level)).Where(c => c.Stat is UpgradeOption.Armor or UpgradeOption.Shield))
                    Assert.Equal(expected, card.Tier);
            }
    }

    static bool IsUtilityCard(UpgradableOption option) =>
        option.Stat is UpgradeOption.PickupRadius or UpgradeOption.ForwardAcceleration
            or UpgradeOption.TurnSpeed or UpgradeOption.SideThrust or UpgradeOption.BackThrust;

    static bool NamesAKillGun(UpgradableOption card) =>
        card.WeaponName.Length > 0 && !card.IsNewWeapon && WeaponType.FromName(card.WeaponName) is { IsSupport: false };

    // A stat card for a kill gun — the ordinary level's strong half, which a milestone hand never deals. Its
    // `Damage` card is the milestone's own card and does not count.
    static bool NamesAKillGunStatCard(UpgradableOption card) => NamesAKillGun(card) && !IsDamageCard(card);

    static bool NamesAChipGun(UpgradableOption card) =>
        card.WeaponName.Length > 0 && !card.IsNewWeapon && WeaponType.FromName(card.WeaponName) is { IsSupport: true };

    static bool IsDamageCard(UpgradableOption card) => card.Stat == UpgradeOption.Damage && !card.IsNewWeapon;

    // What the fixture hull mounts, by class (R24/R25: keyed on the gun, not the mount).
    static List<string> Aboard(ShipType hull) => hull.Loadout.Mounts.Select(mount => mount.Weapon.Name).Distinct().ToList();

    static bool FliesBothClasses(ShipType hull) => FliesAKillGun(hull) && FliesAChipGun(hull);

    static bool FliesAKillGun(ShipType hull) => Aboard(hull).Any(name => WeaponType.FromName(name) is { IsSupport: false });

    static bool FliesAChipGun(ShipType hull) => Aboard(hull).Any(name => WeaponType.FromName(name) is { IsSupport: true });

    static string Names(UpgradeBundle[] bundles) => string.Join(" | ", Cards(bundles).Select(UpgradeOffer.FormatChoice));

    static string Names(IEnumerable<UpgradableOption> cards) => string.Join(", ", cards.Select(UpgradeOffer.FormatChoice));

    static string Names(UpgradeBundle bundle) => $"{UpgradeOffer.FormatChoice(bundle.Powerful)} + {UpgradeOffer.FormatChoice(bundle.Weak)}";

    // Both halves of every bundle, in deal order: most of these rules are about cards, not about pairs.
    static UpgradableOption[] Cards(UpgradeBundle[] bundles) =>
        [.. bundles.SelectMany(bundle => new[] { bundle.Powerful, bundle.Weak })];

    // The offer `hull` is dealt on reaching `level`: the hull starts holding the XP the previous level needs, so
    // one `Roll` is what the player would see. The default seed is the world default, which makes an offer
    // reproducible; a test that needs the draw to vary passes different seeds, because the same seed at the same
    // level deals the same bundles.
    static UpgradeBundle[] Hand(ShipType hull, int level, WeaponMount[]? mounts = null, int seed = EntityManager.DefaultRngSeed)
    {
        var (em, _) = CreateHull(hull, xp: UpgradeStats.XpForLevel(level - 1), level: level - 1, mounts, seed);
        return DealHand(em, level);
    }

    static UpgradeBundle[] DealHand(EntityManager em, int level)
    {
        var view = new WorldView(em);
        Assert.True(view.GetEntitiesWithComponents<Player>().TryFirst(out var playerTuple), "the fixture hull is missing");
        return UpgradeOffer.Roll(view, playerTuple.Entity, level, "test") ?? throw new InvalidOperationException("a hull with turrets must be dealt an offer");
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
