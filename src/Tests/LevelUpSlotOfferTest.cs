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
        var (em, player) = CreateHull(ShipType.Fighter, xp: UpgradeStats.XpForLevel(4), level: 4);

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
        var (em, _) = CreateHull(ShipType.Fighter, xp: UpgradeStats.XpForLevel(4), level: 4);
        var mounted = TurretMount.MountedSlots(new WorldView(em)).Select(m => m.WeaponName).ToHashSet();

        RunLevelUp(em);

        Assert.DoesNotContain(NewWeaponCards(em), name => mounted.Contains(name));
    }

    [Fact]
    public void CargoWeaponsAreNeverOfferedAgain()
    {
        var (em, player) = CreateHull(ShipType.Fighter, xp: UpgradeStats.XpForLevel(4), level: 4);
        em.AddComponent(player, WeaponCargo.Empty.With(TurretMount.CarriedTurret(WeaponType.RailGun)));

        RunLevelUp(em);

        Assert.DoesNotContain(NewWeaponCards(em), name => name == WeaponType.RailGun.Name);
    }

    // R23 — the ship holds one gun of each type, mounted or in cargo — is the two tests above: a card may
    // never name a gun already on board. This is the other half of the same rule: a hand may hold up to
    // three new-weapon cards, but never two cards naming the same weapon.
    [Fact]
    public void AHand_NeverDealsTheSameWeaponTwice()
    {
        var (em, _) = CreateHull(ShipType.Heavy, xp: 999_999, level: 4);

        var hands = new List<List<string>>();
        for (int levelUp = 0; levelUp < 20; levelUp++)
        {
            RunLevelUp(em);
            var offered = TakeNewWeaponCards(em);
            if (offered.Count > 0) hands.Add(offered);
        }

        Assert.NotEmpty(hands);
        foreach (var hand in hands)
            Assert.Equal(hand.Count, hand.Distinct().Count());
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

    // R33 (it supersedes R29's ban): the damage card is a percent of the gun's **base** damage, sized by its
    // mount class, so a support chip is no longer doubled by a flat +1 and a milestone hand may deal one to
    // a support gun as well as a kill gun. The five slots show one gun's card per milestone — that shape is
    // `HandCompositionTest.MilestoneHand_DealsOneDamageCard_KillClassFirst` — so the support half is checked
    // here on a hull that flies nothing but support.
    [Fact]
    public void MilestoneHand_DealsADamageCardToASupportGunToo()
    {
        WeaponMount[] supportOnly = [new(TurretSlotName.SmallLeft, WeaponType.FlakCurtain)];
        var (em, _) = CreateHull(ShipType.Heavy, xp: 999_999, level: 4, supportOnly);

        RunLevelUp(em); // level 5: the milestone hand, which is the only hand that carries Damage cards

        // A new-weapon card reuses the Damage stat with `IsNewWeapon` set: it hands over a gun, it does not
        // upgrade one, so only the upgrade cards count here.
        var offered = new List<UpgradableOption>();
        foreach (var (_, _, options) in em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>())
            offered.AddRange(options.Options);

        Assert.Contains(offered, o => o.Stat == UpgradeOption.Damage && !o.IsNewWeapon && o.WeaponName == WeaponType.FlakCurtain.Name);
    }

    // R33: the card is sized by mount class — a support chip grows a quarter at a time, a kill gun a half —
    // which is what lets the card be dealt to both classes without the chip overtaking the gun (R24/R25).
    [Fact]
    public void DamageCardIsSizedByItsMountClass()
    {
        Assert.Equal(6, WeaponType.MachineGun.DamageStep);   // +50% of 12
        Assert.Equal(30, WeaponType.RailGun.DamageStep);     // +50% of 60
        Assert.Equal(20, WeaponType.HavocTorpedo.DamageStep); // +50% of 40
        Assert.Equal(1, WeaponType.FlakCurtain.DamageStep);  // +25% of 4
        Assert.Equal(2, WeaponType.Stiletto.DamageStep);     // +25% of 8
        Assert.Equal(2, WeaponType.SeekerPod.DamageStep);    // +25% of 8, a guided chip
    }

    // R33: the step is read off the table, so cards add on the base instead of compounding on each other —
    // two cards on the MachineGun are 12 + 6 + 6 = 24, exactly double the base, and not 12 × 1.5 × 1.5 = 27.
    [Fact]
    public void DamageCardsAddOnTheBaseAndNotOnEachOther()
    {
        var gun = WeaponType.MachineGun;
        int once = gun.Stats.Damage + gun.DamageStep;
        int twice = once + gun.DamageStep;

        Assert.Equal(18, once);
        Assert.Equal(2 * gun.Stats.Damage, twice);
        Assert.NotEqual((int)MathF.Round(gun.Stats.Damage * 1.5f * 1.5f), twice);
    }

    // R33's two percents divide every gun's base damage exactly at R32's ×4 scale, so a card is always a
    // whole number of damage: no rounding drift to compound across a long run.
    [Fact]
    public void DamageCardPercentsDivideEveryGunBaseExactly()
    {
        foreach (var gun in WeaponType.All)
        {
            int percent = UpgradeDefinition.DamagePercentFor(gun.IsSupport);
            Assert.Equal(0, gun.Stats.Damage * percent % 100);
            Assert.Equal(gun.Stats.Damage * percent / 100, gun.DamageStep);
        }
    }

    // R29: the Range card stops at the support class's roof (R24/R26). A chip at 1350 px has nothing left
    // to buy, and the card stretches the shot's lifetime as well as its range — so withdrawing it at the
    // roof caps the support mount's live rounds too. A chip below the roof still takes the card, which is
    // what makes the withdrawal a ceiling rather than a ban.
    [Fact]
    public void SupportGunAtItsRangeRoof_IsNotOfferedARangeCard()
    {
        WeaponMount[] longChip = [new(TurretSlotName.SmallLeft, WeaponType.Stiletto)];
        var (em, _) = CreateHull(ShipType.Fighter, xp: 999_999, level: 1, longChip);

        for (int levelUp = 0; levelUp < 12; levelUp++)
        {
            RunLevelUp(em);

            foreach (var (entity, _, options) in em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().ToList())
            {
                Assert.DoesNotContain(options.Options,
                    o => o.Stat == UpgradeOption.Range && o.WeaponName == WeaponType.Stiletto.Name);
                em.DestroyEntity(entity);
            }
        }

        WeaponMount[] shortChip = [new(TurretSlotName.SmallLeft, WeaponType.FlakCurtain)];
        var (shortEm, _) = CreateHull(ShipType.Fighter, xp: 999_999, level: 1, shortChip);

        bool offered = false;
        for (int levelUp = 0; levelUp < 12; levelUp++)
        {
            RunLevelUp(shortEm);

            foreach (var (entity, _, options) in shortEm.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().ToList())
            {
                if (options.Options.Any(o => o.Stat == UpgradeOption.Range && o.WeaponName == WeaponType.FlakCurtain.Name))
                    offered = true;
                shortEm.DestroyEntity(entity);
            }
        }

        Assert.True(offered, "a chip below the class roof should still be dealt its Range card");
    }

    // R19, percent shape (plans/WEAPON_BALANCE.md R33): a `MissileHealth` card buys a percent of the gun's
    // **base** round health. A gun whose rounds start tougher therefore gains more from the card, the step
    // is the same every time it is bought (+50% +50% = +100% of the base, never +125%), and the floor of one
    // hit point keeps the card honest on a 1 hp round, where half a hit point would round away to nothing.
    [Fact]
    public void MissileHealthCardIsAPercentOfTheBaseRoundHealth()
    {
        Assert.Equal(1, UpgradeDefinition.MissileHealth.StepForBase(1)); // floored, never a dead card
        Assert.Equal(1, UpgradeDefinition.MissileHealth.StepForBase(2));
        Assert.Equal(2, UpgradeDefinition.MissileHealth.StepForBase(4));
        Assert.Equal(4, UpgradeDefinition.MissileHealth.StepForBase(8));
        Assert.Equal("+50%", UpgradeDefinition.MissileHealth.DisplayValue);

        foreach (var gun in WeaponType.All)
            Assert.Equal(UpgradeDefinition.MissileHealth.StepForBase(gun.Stats.MissileHealth), gun.MissileHealthStep);
    }

    // Stacking the card: the same base-derived step every time, and the ceiling landed on exactly — never
    // oversold, never a card the rules would refuse to honour. A gun that starts with a tougher round needs
    // fewer cards to get there, because it starts closer with the same step per card.
    [Fact]
    public void MissileHealthCardsStackOnTheBaseAndLandOnTheCeiling()
    {
        foreach (var gun in WeaponType.All.Where(g => g.Stats.Homing))
        {
            int step = gun.MissileHealthStep;
            var live = gun.Stats;
            int cards = 0;
            while (live.CanToughenRound(step))
            {
                live = live with { MissileHealth = live.MissileHealth + step };
                Assert.True(++cards <= 6, $"{gun.Name} never stopped taking missile-health cards");
            }

            Assert.Equal(WeaponStats.MissileHealthCeiling, live.MissileHealth);
            Assert.Equal(gun.Stats.MissileHealth + cards * step, live.MissileHealth);
        }

        Assert.Equal(4, CardsToTheRoundCeiling(WeaponType.SeekerPod));
        Assert.Equal(1, CardsToTheRoundCeiling(WeaponType.FarlockSalvo));
    }

    // R19: a mount whose rounds are already at the ceiling is dealt no more toughness cards, while the same
    // gun one step below it still is — the card is withdrawn at the ceiling, not banned.
    [Fact]
    public void GuidedRoundsAtTheirHealthCeiling_AreNotOfferedMoreToughness()
    {
        int atCeiling = WeaponStats.MissileHealthCeiling;
        foreach (int health in new[] { atCeiling - WeaponType.SeekerPod.MissileHealthStep, atCeiling })
        {
            WeaponMount[] podOnly = [new(TurretSlotName.MainGun, WeaponType.SeekerPod)];
            var (em, _) = CreateHull(ShipType.Fighter, xp: 999_999, level: 1, podOnly);
            foreach (var (turretEntity, turret) in em.GetEntitiesWithComponents<Turret>().ToList())
                em.AddComponent(turretEntity, turret with { Weapon = turret.Weapon with { MissileHealth = health } });

            bool offered = false;
            for (int levelUp = 0; levelUp < 12; levelUp++)
            {
                RunLevelUp(em);

                foreach (var (entity, _, options) in em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().ToList())
                {
                    if (health == atCeiling)
                        Assert.DoesNotContain(options.Options,
                            o => o.Stat == UpgradeOption.MissileHealth && o.WeaponName == WeaponType.SeekerPod.Name);
                    else if (options.Options.Any(o => o.Stat == UpgradeOption.MissileHealth && o.WeaponName == WeaponType.SeekerPod.Name))
                        offered = true;
                    em.DestroyEntity(entity);
                }
            }

            if (health != atCeiling)
                Assert.True(offered, "a pod below the round-health ceiling should still be dealt its toughness card");
        }
    }

    static int CardsToTheRoundCeiling(WeaponType gun)
    {
        var live = gun.Stats;
        int cards = 0;
        while (live.CanToughenRound(gun.MissileHealthStep))
        {
            live = live with { MissileHealth = live.MissileHealth + gun.MissileHealthStep };
            cards++;
        }
        return cards;
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
