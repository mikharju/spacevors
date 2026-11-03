using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// The bundle itself (plans/UPGRADE_BUNDLES.md): a level takes two cards out of the five it is dealt, the
// pair it is built around is a small-gun and a large-gun upgrade, and the bookkeeping of a spent pick lives
// in the domain so it is testable without a window. The shape of the hands is `HandCompositionTest`'s job;
// the per-card legality is `LevelUpSlotOfferTest`'s and `RerollOfferTest`'s.
public class TwoPickLevelUpTest
{
    static readonly ShipType[] Hulls = [ShipType.Scout, ShipType.Fighter, ShipType.Shadow, ShipType.Falcon, ShipType.Eagle, ShipType.Manta, ShipType.Raptor, ShipType.Heavy];

    [Fact]
    public void Bundle_ALevelUpIsDealtTwoPicks()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: UpgradeStats.XpForLevel(1), level: 1);

        LevelUp(em);

        var hand = Hand(em);
        Assert.Equal(5, hand.Options.Length);
        Assert.Equal(2, hand.PicksLeft);
        Assert.Empty(hand.Taken);
    }

    // A short hand — a bench script with one card left, a pool with nothing else legal — cannot owe more
    // picks than it has cards.
    [Fact]
    public void Bundle_DealClampsPicksToTheHand()
    {
        var hand = PendingUpgradeOptions.Deal([new UpgradableOption("", UpgradeOption.Hp)], UpgradeOffer.CardsPerLevel);

        Assert.Single(hand.Options);
        Assert.Equal(1, hand.PicksLeft);
    }

    [Fact]
    public void Bundle_SpendingAPickShortensTheHandAndCountsDown()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: UpgradeStats.XpForLevel(1), level: 1);
        LevelUp(em);

        var taken = Hand(em).Options[0];
        Assert.True(UpgradeOffer.SpendPick(em, player, taken));

        var hand = Hand(em);
        Assert.Equal(4, hand.Options.Length);
        Assert.Equal(1, hand.PicksLeft);
        Assert.DoesNotContain(hand.Options, card => card == taken);
        Assert.Contains(hand.Taken, card => card == taken);
    }

    // The second pick is the level's last: the hand is left alone for the app to clear, and the pick is not
    // written down as taken twice.
    [Fact]
    public void Bundle_SpendingTheLastPickEndsTheLevel()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: UpgradeStats.XpForLevel(1), level: 1);
        LevelUp(em);

        Assert.True(UpgradeOffer.SpendPick(em, player, Hand(em).Options[0]));
        Assert.False(UpgradeOffer.SpendPick(em, player, Hand(em).Options[0]));

        Assert.Equal(1, Hand(em).PicksLeft);
        Assert.Single(Hand(em).Taken);
    }

    // A salvage capsule re-deals the rest of the hand; it must not sell back the card the player has already
    // taken this level, which with two picks a level it plainly can.
    [Fact]
    public void Bundle_RerollNeverResellsATakenCard()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: UpgradeStats.XpForLevel(1), level: 1, charges: 20);
        LevelUp(em);

        var taken = Hand(em).Options[0];
        UpgradeOffer.SpendPick(em, player, taken);

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Assert.True(UpgradeOffer.TryReroll(em, player));
            Assert.DoesNotContain(Hand(em).Options, card => card == taken);
        }
    }

    // The FOMO fix, stated as a rule: an ordinary hand always carries an upgrade for a small-class gun and
    // one for a kill-class gun, so a level is never a choice between "the big gun" and everything else.
    [Fact]
    public void StandardHand_CarriesAnUpgradeForEachClassTheHullFlies()
    {
        foreach (var hull in Hulls)
            for (int level = 2; level <= 21; level++)
            {
                if (UpgradeOffer.IsMilestoneLevel(level)) continue;

                var gunCards = HandFor(hull, level).Where(o => o.WeaponName.Length > 0 && !o.IsNewWeapon).ToList();
                var classes = hull.Loadout.Mounts.Select(m => m.Weapon.IsSupport).ToList();

                Assert.True(!classes.Contains(true) || gunCards.Any(card => ClassOf(card)),
                    $"{hull.Name} at level {level} got no small-gun upgrade: {Names(gunCards)}");
                Assert.True(!classes.Contains(false) || gunCards.Any(card => !ClassOf(card)),
                    $"{hull.Name} at level {level} got no kill-gun upgrade: {Names(gunCards)}");
            }
    }

    // A milestone hand always has a damage card to take, whether or not a new gun fits anywhere: this is the
    // option the player falls back to when every mount is already spoken for.
    [Fact]
    public void MilestoneHand_AlwaysCarriesADamageCard()
    {
        foreach (var hull in Hulls)
            for (int level = 5; level <= 20; level += 5)
                Assert.Contains(HandFor(hull, level), o => o.Stat == UpgradeOption.Damage && !o.IsNewWeapon);
    }

    // A new large gun goes in by displacing the gun already in that mount, and that gun's cards go into
    // cargo with it: a stat card for a gun that is no longer firing would be a card that does nothing, so it
    // leaves the hand and the picks owed shrink with it.
    [Fact]
    public void Bundle_CardsForADisplacedGunLeaveTheHand()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: UpgradeStats.XpForLevel(1), level: 1);
        LevelUp(em);
        UpgradeOffer.SpendPick(em, player, Hand(em).Options[0]);

        var displaced = Hand(em).Options.FirstOrDefault(card => card.WeaponName.Length > 0 && !card.IsNewWeapon).WeaponName;
        Assert.NotNull(displaced);

        foreach (var (entity, turret) in em.GetEntitiesWithComponents<Turret>().ToList())
            if (turret.WeaponName == displaced) em.DestroyEntity(entity);

        Assert.True(UpgradeOffer.PruneCardsThatNoLongerApply(em, player));

        var left = Hand(em);
        Assert.DoesNotContain(left.Options, card => card.WeaponName == displaced);
        Assert.True(left.PicksLeft <= left.Options.Length, $"{left.PicksLeft} picks owed with {left.Options.Length} cards left");
    }

    static bool ClassOf(UpgradableOption card) =>
        WeaponType.FromName(card.WeaponName) is { } gun && gun.IsSupport;

    static string Names(IEnumerable<UpgradableOption> cards) => string.Join(", ", cards.Select(UpgradeOffer.FormatChoice));

    static UpgradableOption[] HandFor(ShipType hull, int level)
    {
        var (em, _) = CreateHull(hull, xp: UpgradeStats.XpForLevel(level - 1), level: level - 1);
        var view = new WorldView(em);
        Assert.True(view.GetEntitiesWithComponents<Player>().TryFirst(out var player));

        return UpgradeOffer.Roll(view, player.Entity, level, "test") ?? throw new InvalidOperationException("a hull with turrets must be dealt a hand");
    }

    static void LevelUp(EntityManager em)
    {
        var commands = new CommandBuffer();
        new LevelUpSystem().Update(new WorldView(em), 1f / 60f, commands);
        commands.Apply(em);
    }

    static PendingUpgradeOptions Hand(EntityManager em)
    {
        Assert.True(em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pending));
        return em.GetComponent<PendingUpgradeOptions>(pending.Entity);
    }

    static (EntityManager Em, Entity Player) CreateHull(ShipType ship, int xp, int level, int charges = 0)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Rotation(0f));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 20, Radius: 40f, Xp: xp, Level: level, PickupRadius: 150f, RotationSpeed: 1f));

        foreach (var slot in ship.Slots)
            em.AddComponent(em.CreateEntity(), new TurretSlot(slot));

        foreach (var mount in ship.Loadout.Mounts)
        {
            var view = new WorldView(em);
            Assert.True(TurretMount.TryFindSlot(view, mount.SlotName, out var slotEntity));
            TurretMount.Mount(em, slotEntity, view.GetComponent<TurretSlot>(slotEntity).Definition, mount.Weapon, Vector2.Zero, hullRotation: 0f);
        }

        if (charges > 0) em.AddComponent(player, new RerollStock(charges));
        return (em, player);
    }
}
