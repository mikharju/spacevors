using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Spending a salvage capsule re-deals the hand on screen. The point of these tests is that a re-deal goes
// through the *same* offer code as a level-up, so every rule that keeps a card honest still holds after the
// re-deal (plans/REROLL_TOKENS.md D5, balance rules R10/R18/R19).
public class RerollOfferTest
{
    [Fact]
    public void NoCharges_RerollRefusesAndTheHandStays()
    {
        var (em, player) = CreateHullWithHand();

        var before = Hand(em);
        Assert.False(UpgradeOffer.TryReroll(em, player));
        Assert.Equal(before, Hand(em));
    }

    [Fact]
    public void OneCharge_SpendsExactlyOneAndReplacesTheHand()
    {
        var (em, player) = CreateHullWithHand(charges: 3);
        var original = Hand(em);

        Assert.True(UpgradeOffer.TryReroll(em, player));
        Assert.Equal(2, em.GetComponent<RerollStock>(player).Count);
        Assert.NotEmpty(Hand(em));

        // A re-deal of the same five is allowed (the pool can be that small), but with this many cards in
        // the pool it must not happen every single time — otherwise nothing was re-dealt.
        bool sawADifferentHand = !Hand(em).SequenceEqual(original);
        for (int attempt = 0; attempt < 12 && !sawADifferentHand; attempt++)
        {
            UpgradeOffer.TryReroll(em, player);
            sawADifferentHand = !Hand(em).SequenceEqual(original);
        }

        Assert.True(sawADifferentHand, "twelve re-deals all identical means the hand is not being re-dealt");
    }

    [Fact]
    public void Redeal_NeverNamesACargoWeapon()
    {
        var (em, player) = CreateHullWithHand(xp: 999_999, level: 1, charges: 40);
        em.AddComponent(player, WeaponCargo.Empty.With(TurretMount.CarriedTurret(WeaponType.RailGun)));

        for (int attempt = 0; attempt < 20; attempt++)
        {
            UpgradeOffer.TryReroll(em, player);

            Assert.DoesNotContain(Hand(em), o => o.WeaponName == WeaponType.RailGun.Name);
        }
    }

    // A hitscan beam has no travelling projectile, so the speed card is withdrawn — from a re-deal too.
    [Fact]
    public void Redeal_NeverOffersProjectileSpeedToAHitscanGun()
    {
        WeaponMount[] hitscanOnly = [new(TurretSlotName.MainGun, WeaponType.LaserLance)];
        var (em, player) = CreateHullWithHand(xp: 999_999, level: 1, charges: 40, mounts: hitscanOnly);

        for (int attempt = 0; attempt < 20; attempt++)
        {
            UpgradeOffer.TryReroll(em, player);

            Assert.DoesNotContain(Hand(em),
                o => o.Stat == UpgradeOption.ProjectileSpeed && o.WeaponName == WeaponType.LaserLance.Name);
        }
    }

    // R18: a guided volley at its round budget keeps the card withdrawn across a re-deal.
    [Fact]
    public void Redeal_AtTheVolleyCeiling_WithdrawsTheShotCard()
    {
        WeaponMount[] swarmOnly = [new(TurretSlotName.MainGun, WeaponType.Swarmlet)];
        var (em, player) = CreateHullWithHand(xp: 999_999, level: 1, charges: 60, mounts: swarmOnly);

        foreach (var (turretEntity, turret) in em.GetEntitiesWithComponents<Turret>().ToList())
            em.AddComponent(turretEntity, turret with { Weapon = turret.Weapon with { PelletCount = 5 } });

        Assert.False((WeaponType.Swarmlet.Stats with { PelletCount = 5 }).CanGrowSalvo,
            "five pellets is meant to be this gun's volley ceiling");

        for (int attempt = 0; attempt < 30; attempt++)
        {
            UpgradeOffer.TryReroll(em, player);

            Assert.DoesNotContain(Hand(em),
                o => o.Stat == UpgradeOption.FireRate && o.WeaponName == WeaponType.Swarmlet.Name);
        }
    }

    // A milestone re-deal keeps the milestone shape: at most three new weapons, and none of them a gun
    // the ship already owns on the hull or in the cargo.
    [Fact]
    public void Redeal_MilestoneHand_StillCarriesAtMostThreeNewWeapons()
    {
        var (em, player) = CreateHullWithHand(xp: 999_999, level: 5, charges: 60);
        var mounted = TurretMount.MountedSlots(new WorldView(em)).Select(m => m.WeaponName).ToHashSet();

        for (int attempt = 0; attempt < 20; attempt++)
        {
            UpgradeOffer.TryReroll(em, player);
            var newWeapons = Hand(em).Where(o => o.IsNewWeapon).Select(o => o.WeaponName).ToList();
            int mostOfOneClass = newWeapons.GroupBy(name => WeaponType.FromName(name) is { } gun ? gun.Size : TurretSize.Small)
                .Select(group => group.Count()).DefaultIfEmpty(0).Max();

            Assert.True(mostOfOneClass <= 3, $"a milestone re-deal offered {mostOfOneClass} new weapons of one class");
            Assert.DoesNotContain(newWeapons, name => mounted.Contains(name));
        }
    }

    // The re-deal draws from the world RNG and nothing else, so the same seed re-deals the same hands —
    // the determinism every other seeded run depends on.
    [Fact]
    public void Redeal_IsDeterministicForOneSeed()
    {
        var (emA, playerA) = CreateHullWithHand(xp: 999_999, level: 4, charges: 5);
        var (emB, playerB) = CreateHullWithHand(xp: 999_999, level: 4, charges: 5);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(UpgradeOffer.TryReroll(emA, playerA), UpgradeOffer.TryReroll(emB, playerB));
            Assert.Equal(Hand(emA), Hand(emB));
        }
    }

    static (EntityManager Em, Entity Player) CreateHullWithHand(int xp = 15, int level = 1, int charges = 0, WeaponMount[]? mounts = null)
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp, level, mounts);

        var commands = new CommandBuffer();
        new LevelUpSystem().Update(new WorldView(em), 1f / 120f, commands);
        commands.Apply(em);

        if (charges > 0) em.AddComponent(player, new RerollStock(charges));
        return (em, player);
    }

    // Flattened to cards: a reroll is judged on what it offers, not on how the pairs are cut.
    static UpgradableOption[] Hand(EntityManager em)
    {
        Assert.True(em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pending));
        return [.. em.GetComponent<PendingUpgradeOptions>(pending.Entity).Bundles
            .SelectMany(bundle => new[] { bundle.Powerful, bundle.Weak })];
    }

    static (EntityManager Em, Entity Player) CreateHull(ShipType ship, int xp, int level, WeaponMount[]? mounts)
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
