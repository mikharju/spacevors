using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Spacevors.Game;
using Xunit;

namespace Tests;

// What the upgrade cards and the mount screen say, and the arithmetic that decides whether it fits
// (plans/CARD_AND_LOADOUT_READABILITY.md). The strings are checked here because the pixels are not:
// `UpgradeCardCopy` builds a card's text without touching a window, and every number in it must be the
// number the grant produces — which is what R10 ("a card is never a lie") comes down to once a card starts
// printing a before/after pair instead of a lone delta.
public class CardReadabilityTest
{
    // ---- one vocabulary (D1)

    [Fact]
    public void StatWords_NameEachStatOnce_NoMatterWhichScreenReadsThem()
    {
        UpgradeOption[] gunStats =
            [UpgradeOption.FireRate, UpgradeOption.ProjectileSpeed, UpgradeOption.Range, UpgradeOption.Damage, UpgradeOption.ArmorPierce, UpgradeOption.MissileHealth];

        var words = gunStats.Select(stat => MenuCopy.StatWord(stat, growsVolley: false)).ToList();
        Assert.Equal(words.Count, words.Distinct().Count());

        // The three spellings one stat used to carry are gone, not kept beside the word.
        Assert.DoesNotContain("rate", words);
        Assert.DoesNotContain("proj speed", words);
        Assert.DoesNotContain("round hp", words);
    }

    [Fact]
    public void FireRate_IsCalledVolleySizeOnlyWhereTheCardBuysShots()
    {
        Assert.Equal("attack speed", MenuCopy.StatWord(UpgradeOption.FireRate, growsVolley: false));
        Assert.Equal("volley size", MenuCopy.StatWord(UpgradeOption.FireRate, growsVolley: true));
    }

    [Fact]
    public void Abbreviations_OnlyEverShortenTheWordTheyComeFrom()
    {
        UpgradeOption[] gunStats =
            [UpgradeOption.FireRate, UpgradeOption.ProjectileSpeed, UpgradeOption.Range, UpgradeOption.Damage, UpgradeOption.ArmorPierce, UpgradeOption.MissileHealth];

        foreach (UpgradeOption stat in gunStats)
            foreach (bool volley in new[] { false, true })
            {
                string word = MenuCopy.StatWord(stat, volley);
                string abbrev = MenuCopy.StatAbbrev(stat, volley);
                Assert.False(string.IsNullOrWhiteSpace(abbrev), $"{stat} has no short word");
                Assert.True(abbrev.Length <= word.Length, $"{stat}: `{abbrev}` is not shorter than `{word}`");
            }
    }

    // ---- mounts have names, in every screen's mouth (D7/D8)

    [Fact]
    public void EveryMountOnEveryHull_HasAPositionAndASize()
    {
        ShipType[] hulls = [ShipType.Scout, ShipType.Fighter, ShipType.Heavy, ShipType.Eagle, ShipType.Manta];
        var seen = new List<string>();

        foreach (var hull in hulls)
            foreach (var slot in hull.Slots)
            {
                string label = MenuCopy.MountLabel(slot);
                Assert.False(string.IsNullOrWhiteSpace(label), $"{slot.Name} has no mount label");
                Assert.Contains(MenuCopy.SizeWord(slot.Size), label);
                seen.Add(label);
            }

        Assert.Contains("ahead · large", seen);
    }

    // Two mounts of one hull can share a position word, which is why the size rides in the label: without it
    // the legend would have two rows that look identical and mean different guns.
    [Fact]
    public void TheTwoFlankMounts_AreNotTheSameName()
    {
        string small = MenuCopy.MountLabel(new TurretSlotDefinition(TurretSlotName.SmallLeft, TurretSize.Small, Vector2.Zero, 0f, 1f));
        string heavy = MenuCopy.MountLabel(new TurretSlotDefinition(TurretSlotName.HeavyLeft, TurretSize.Large, Vector2.Zero, 0f, 1f));

        Assert.NotEqual(small, heavy);
        Assert.Equal("left flank · small", small);
        Assert.Equal("left flank · large", heavy);
    }

    // ---- the card's text (D2/D3)

    [Fact]
    public void StatCard_PrintsTheGunBeforeAndAfter_CalledTheWayTheCardNamesIt()
    {
        var (em, player) = CreateHull(ShipType.Fighter, 5);
        var live = TurretOf(em, WeaponType.MachineGun.Name);
        var card = new UpgradableOption(WeaponType.MachineGun.Name, UpgradeOption.Range);

        var text = UpgradeCardCopy.Build(card, live, SnapshotOf(em, player), pierceUnlocked: false);

        Assert.Equal("Machine Gun range", text.Name);
        Assert.Equal("+15%", text.Value);
        Assert.Empty(text.Caps);
        Assert.Equal(0, text.Pips);
        Assert.Single(text.Numbers);
        Assert.Matches("^range \\d+ -> \\d+ px$", text.Numbers[0]);
        Assert.StartsWith($"range {Whole(live.Range)} -> ", text.Numbers[0]);
    }

    // The point of the pair: an ordinary card and a super card for one gun start from the same number, and the
    // player can see the gap without comparing two percentages across five bundles.
    [Fact]
    public void SuperCard_KeepsTheSameBeforeNumberAndMovesItFurther()
    {
        var (em, player) = CreateHull(ShipType.Fighter, 5);
        var live = TurretOf(em, WeaponType.MachineGun.Name);
        var hull = SnapshotOf(em, player);

        var plain = UpgradeCardCopy.Build(new UpgradableOption(WeaponType.MachineGun.Name, UpgradeOption.Range), live, hull, false);
        var spiked = UpgradeCardCopy.Build(new UpgradableOption(WeaponType.MachineGun.Name, UpgradeOption.Range, Power: CardPower.Super), live, hull, false);

        Assert.Equal(FirstNumber(plain.Numbers[0]), FirstNumber(spiked.Numbers[0]));
        Assert.NotEqual(LastNumber(plain.Numbers[0]), LastNumber(spiked.Numbers[0]));
        Assert.Equal("+30%", spiked.Value);
    }

    [Fact]
    public void NewWeaponCard_WithoutAnArrivingSet_ShowsNoCapsNoPipsAndNoArrow()
    {
        var (em, player) = CreateHull(ShipType.Fighter, 5);
        var card = new UpgradableOption(WeaponType.RailGun.Name, UpgradeOption.Damage, IsNewWeapon: true);

        var text = UpgradeCardCopy.Build(card, null, SnapshotOf(em, player), pierceUnlocked: true);

        Assert.Equal("new weapon · Rail Gun", text.Name);
        Assert.Equal("large", text.Value);
        Assert.Empty(text.Caps);
        Assert.Equal(0, text.Pips);
        Assert.All(text.Numbers, line => Assert.DoesNotContain("->", line));
    }

    // R10, the sharp edge of the arriving set: an arriving upgrade is one **ordinary** card of its stat, and on
    // a gun whose volley grows the FireRate upgrade buys a shot. A cap promising "attack speed +15 %" there
    // would be a number the gun never gets.
    [Fact]
    public void ArrivingFireRate_OnAVolleyGun_IsCappedAsAShot_NotAsAFasterTrigger()
    {
        var (em, player) = CreateHull(ShipType.Fighter, 5);
        var card = new UpgradableOption(WeaponType.Shotgun.Name, UpgradeOption.Damage, IsNewWeapon: true,
            PreApplied: WeaponPreUpgrade.FireRate | WeaponPreUpgrade.Damage);

        var text = UpgradeCardCopy.Build(card, null, SnapshotOf(em, player), pierceUnlocked: true);

        Assert.Contains("volley size +1 shot", text.Caps);
        Assert.DoesNotContain("attack speed", string.Join(" ", text.Caps));
        Assert.Equal(2, text.Caps.Length);
        Assert.Contains(text.Numbers, line => line.StartsWith("damage ") && line.Contains(" -> "));
        Assert.Equal(2, text.PipsFilled);
        Assert.True(text.Pips >= 2);
    }

    // The set the offer can actually put on a gun, at its widest: every cap present, the name intact. Nothing
    // here may be dropped or truncated — that is how a milestone hand used to lose a stat off the card.
    [Fact]
    public void WidestArrivingSet_KeepsEveryCapAndTheGunName()
    {
        var (em, player) = CreateHull(ShipType.Fighter, 20);
        var all = WeaponPreUpgrade.FireRate | WeaponPreUpgrade.ProjectileSpeed | WeaponPreUpgrade.Range |
                  WeaponPreUpgrade.MissileHealth | WeaponPreUpgrade.ArmorPierce | WeaponPreUpgrade.Damage;
        var card = new UpgradableOption(WeaponType.FarlockSalvo.Name, UpgradeOption.Damage, IsNewWeapon: true, PreApplied: all);

        var text = UpgradeCardCopy.Build(card, null, SnapshotOf(em, player), pierceUnlocked: true);

        Assert.Equal(6, text.Caps.Length);
        Assert.Equal(6, text.CapsShort.Length);
        Assert.Equal("new weapon · Farlock Salvo", text.Name);
        Assert.Contains(text.Caps, cap => cap.StartsWith("round hit points "));
        Assert.Contains(text.CapsShort, cap => cap.StartsWith("round hp "));
        Assert.Equal(6, text.PipsFilled);
    }

    // ---- the printed pair is the grant (R10)

    // For the hull-wide cards there is no `ApplyStat` to lean on: the grant multiplies or adds a component
    // field. So the test does the grant for real and checks the number the card printed against the component
    // afterwards — the only way this invariant can be held without a window.
    [Fact]
    public void HullCard_AfterNumberIsTheNumberTheGrantLeavesBehind()
    {
        (UpgradeOption stat, Func<EntityManager, Entity, float> read)[] cards =
        [
            (UpgradeOption.Hp, (em, p) => em.GetComponent<Health>(p).Max),
            (UpgradeOption.Armor, (em, p) => em.GetComponent<Armor>(p).Value),
            (UpgradeOption.PickupRadius, (em, p) => em.GetComponent<Player>(p).PickupRadius),
            (UpgradeOption.ForwardAcceleration, (em, p) => em.GetComponent<Player>(p).Thrust),
            (UpgradeOption.SideThrust, (em, p) => em.GetComponent<Player>(p).SideThrust),
            (UpgradeOption.BackThrust, (em, p) => em.GetComponent<Player>(p).BackThrust),
            (UpgradeOption.TurnSpeed, (em, p) => Angle.ToDeg(em.GetComponent<Player>(p).RotationSpeed)),
        ];

        foreach (var (stat, read) in cards)
        {
            var (em, player) = CreateHull(ShipType.Fighter, 5);
            em.AddComponent(player, new Health(20, 20));
            var card = new UpgradableOption("", stat, Power: CardPower.Super);

            var text = UpgradeCardCopy.Build(card, null, SnapshotOf(em, player), pierceUnlocked: false);
            Assert.Single(text.Numbers);

            GameSession.ApplyUpgrade(em, player, card);
            // The card prints whole numbers, so the comparison rounds the same way: the grant leaves 68.75 and
            // the card says 69, which is the same number as far as a player is concerned.
            Assert.Equal(read(em, player), LastNumber(text.Numbers[0]), 0);
        }
    }

    // A shield card grows the pool the same step; the pair is printed against the pool the ship has, clamped
    // by the ceiling like the grant clamps it.
    [Fact]
    public void ShieldCard_PairRespectsTheCeilingTheGrantRespects()
    {
        var (em, player) = CreateHull(ShipType.Fighter, 12);
        em.AddComponent(player, new Shield(Points: 30f, Max: 24, Offline: 0f));
        var card = new UpgradableOption("", UpgradeOption.Shield, Tier: DefenceTier.Milestone);

        var text = UpgradeCardCopy.Build(card, null, SnapshotOf(em, player), pierceUnlocked: false);

        Assert.Contains("shield 24 -> 32", text.Numbers[0]);
        GameSession.ApplyUpgrade(em, player, card);
        Assert.Equal(32, em.GetComponent<Shield>(player).Max);
    }

    // ---- the fit arithmetic that used to spill over the divider

    // A new-weapon half needs more than half a 200 px card once it carries its caps and its numbers; the old
    // fixed 50/50 handed it 100 px and the block ran across the divider into its partner.
    [Fact]
    public void TheBusyHalfGetsMoreThanHalfTheCard_WithoutSqueezingOutTheOther()
    {
        int busy = 134, lean = 76, cardHeight = 200, floor = 72;

        int top = UpgradeCardCopy.SplitHeight(busy, lean, cardHeight, floor);

        Assert.True(top > cardHeight / 2, $"the busy half was still handed {top} px of {cardHeight}");
        Assert.True(top >= floor && cardHeight - top >= floor, "a half was squeezed out of the card");
    }

    [Fact]
    public void TwoEqualHalves_StillSplitTheCardEvenly()
    {
        Assert.Equal(100, UpgradeCardCopy.SplitHeight(80, 80, 200, 72));
    }

    // ---- fixtures

    static float FirstNumber(string line) => Number(line.Split(" -> ")[0]);

    static float LastNumber(string line) => Number(line.Split(" -> ")[^1]);

    // The digits out of a printed row ("armor 4 -> 6"), so the tests compare numbers, not formatting.
    static float Number(string part) =>
        float.Parse(new string(part.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray()));

    static string Whole(float value) => $"{(int)MathF.Round(value)}";

    static Turret TurretOf(EntityManager em, string weaponName)
    {
        foreach (var (entity, turret) in em.GetEntitiesWithComponents<Turret>())
            if (turret.WeaponName == weaponName) return turret;
        throw new InvalidOperationException($"no {weaponName} mounted");
    }

    static UpgradeCardCopy.Hull SnapshotOf(EntityManager em, Entity player)
    {
        var p = em.GetComponent<Player>(player);
        return new UpgradeCardCopy.Hull(
            MaxHp: em.TryGetComponent<Health>(player, out var health) ? health.Max : p.MaxHealth,
            Armor: em.TryGetComponent<Armor>(player, out var armor) ? armor.Value : 0,
            ShieldMax: em.TryGetComponent<Shield>(player, out var shield) ? shield.Max : 0f,
            Thrust: p.Thrust, SideThrust: p.SideThrust, BackThrust: p.BackThrust,
            RotationSpeed: p.RotationSpeed, PickupRadius: p.PickupRadius);
    }

    static (EntityManager Em, Entity Player) CreateHull(ShipType hull, int level)
    {
        var em = new EntityManager(EntityManager.DefaultRngSeed);

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Rotation(0f));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f,
            MaxHealth: 20, Radius: 40f, Xp: 0, Level: level, PickupRadius: 150f, RotationSpeed: 1f));
        em.AddComponent(player, WeaponCargo.Empty);
        em.AddComponent(player, UpgradeCounts.Empty);

        foreach (var slot in hull.Slots)
            em.AddComponent(em.CreateEntity(), new TurretSlot(slot));

        foreach (var mount in hull.Loadout.Mounts)
        {
            var view = new WorldView(em);
            Assert.True(TurretMount.TryFindSlot(view, mount.SlotName, out var slotEntity));
            TurretMount.Mount(em, slotEntity, view.GetComponent<TurretSlot>(slotEntity).Definition, mount.Weapon, Vector2.Zero, hullRotation: 0f);
        }

        return (em, player);
    }
}
