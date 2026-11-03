using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// What a level's bundle is worth, and the two defence tiers it is built from
// (plans/UPGRADE_BUNDLE_PAIRS.md §2 and §6). The grant itself lives in the app layer, so these tests pin the
// one function both the card's label and the grant read — that shared read is what keeps a defence card from
// being a lie (R10) — plus the ceiling arithmetic the tiers were chosen against, and the scripted bench's tier.
public class BundleChoiceTest
{
    // An ordinary level grants half the step a milestone does: +1 armour against +2, +8 shield against +16
    // (one multiplier over the two steps, `UpgradeDefinition.MilestoneDefenceMultiplier`).
    [Fact]
    public void OrdinaryDefenceStepIsHalfTheMilestoneStep()
    {
        Assert.Equal(1, UpgradeDefinition.DefenceStep(UpgradeOption.Armor, DefenceTier.Ordinary));
        Assert.Equal(2, UpgradeDefinition.DefenceStep(UpgradeOption.Armor, DefenceTier.Milestone));
        Assert.Equal(8, UpgradeDefinition.DefenceStep(UpgradeOption.Shield, DefenceTier.Ordinary));
        Assert.Equal(16, UpgradeDefinition.DefenceStep(UpgradeOption.Shield, DefenceTier.Milestone));
    }

    // The number printed on the card is the number granted, because both read `DefenceStep` off the card's own
    // tier. A milestone armour card says +2 and an ordinary one +1.
    [Fact]
    public void DefenceCardPrintsTheStepItGrants()
    {
        foreach (var tier in new[] { DefenceTier.Ordinary, DefenceTier.Milestone })
        {
            var armor = new UpgradableOption("", UpgradeOption.Armor, Tier: tier);
            Assert.Contains($"+{UpgradeDefinition.DefenceStep(UpgradeOption.Armor, tier)}", UpgradeOffer.FormatChoice(armor));

            var shield = new UpgradableOption("", UpgradeOption.Shield, Tier: tier);
            Assert.Contains($"+{UpgradeDefinition.DefenceStep(UpgradeOption.Shield, tier)}", UpgradeOffer.FormatChoice(shield));
        }
    }

    // The tiers are what make the defence axis finite: twelve ordinary armour buys or six milestone ones reach
    // ArmorCeiling, and a milestone shield card is two pools' worth — four ordinary buys or two milestone ones
    // fill the 32-point pool. Nothing walks past its ceiling.
    [Fact]
    public void DefenceStepsWalkTheirCeilingsWithoutPassingThem()
    {
        int armor = 0;
        for (int buys = 1; buys <= 12; buys++)
            armor = Math.Min(armor + UpgradeDefinition.DefenceStep(UpgradeOption.Armor, DefenceTier.Ordinary), ArmorRules.ArmorCeiling);
        Assert.Equal(ArmorRules.ArmorCeiling, armor);

        int milestoneArmor = 0;
        for (int buys = 1; buys <= 6; buys++)
            milestoneArmor = Math.Min(milestoneArmor + UpgradeDefinition.DefenceStep(UpgradeOption.Armor, DefenceTier.Milestone), ArmorRules.ArmorCeiling);
        Assert.Equal(ArmorRules.ArmorCeiling, milestoneArmor);

        var pool = new Shield(Points: 0f, Max: 0, Offline: 0f);
        for (int buys = 1; buys <= 8; buys++)
            pool = ShieldRules.WithCard(pool, UpgradeDefinition.DefenceStep(UpgradeOption.Shield, DefenceTier.Ordinary));
        Assert.Equal(ShieldRules.ShieldCeiling, pool.Max);

        var milestonePool = new Shield(Points: 0f, Max: 0, Offline: 0f);
        for (int buys = 1; buys <= 4; buys++)
            milestonePool = ShieldRules.WithCard(milestonePool, UpgradeDefinition.DefenceStep(UpgradeOption.Shield, DefenceTier.Milestone));
        Assert.Equal(ShieldRules.ShieldCeiling, milestonePool.Max);
    }

    // The tier is stamped from the level the card was dealt at, and only defence cards carry one — so a benched
    // `Armor:` entry at a milestone grants the milestone step like a dealt card, and an engine card is left
    // alone.
    [Fact]
    public void BenchEntriesTakeTheTierOfTheLevelTheyAreDealtAt()
    {
        Assert.Equal(DefenceTier.Milestone, UpgradeOffer.StampDefenceTier(new UpgradableOption("", UpgradeOption.Shield), 10).Tier);
        Assert.Equal(DefenceTier.Ordinary, UpgradeOffer.StampDefenceTier(new UpgradableOption("", UpgradeOption.Shield), 11).Tier);
        Assert.Equal(DefenceTier.Ordinary, UpgradeOffer.StampDefenceTier(new UpgradableOption("", UpgradeOption.SideThrust), 10).Tier);
    }

    // A level-up is dealt bundles, every one of them one powerful and one weak card — there is no pick count
    // left to carry, because a bundle is taken whole.
    [Fact]
    public void LevelUpDealsBundlesThatAreTakenWhole()
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 20, Radius: 40f, Xp: UpgradeStats.XpForLevel(1), Level: 1, PickupRadius: 150f, RotationSpeed: 1f));
        foreach (var slot in ShipType.Fighter.Slots)
            em.AddComponent(em.CreateEntity(), new TurretSlot(slot));

        var view = new WorldView(em);
        Assert.True(TurretMount.TryFindSlot(view, TurretSlotName.MainGun, out var mainGun));
        TurretMount.Mount(em, mainGun, em.GetComponent<TurretSlot>(mainGun).Definition, WeaponType.MachineGun, Vector2.Zero, hullRotation: 0f);

        var commands = new CommandBuffer();
        new LevelUpSystem(scriptedUpgrades: []).Update(new WorldView(em), 1f / 60f, commands);
        commands.Apply(em);

        Assert.True(em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pending));
        var bundles = em.GetComponent<PendingUpgradeOptions>(pending.Entity).Bundles;
        Assert.True(bundles.Length >= 1, "a hull with a turret must be dealt at least one bundle");

        foreach (var bundle in bundles)
        {
            Assert.True(UpgradeOffer.IsPowerfulCard(bundle.Powerful), $"strong half {UpgradeOffer.FormatChoice(bundle.Powerful)} is not powerful");
            Assert.False(UpgradeOffer.IsPowerfulCard(bundle.Weak), $"weak half {UpgradeOffer.FormatChoice(bundle.Weak)} is not weak");
        }
    }
}
