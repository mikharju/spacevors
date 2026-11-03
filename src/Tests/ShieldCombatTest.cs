using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Shields (plans/SHIELDS.md): gunfire is taxed by armor and then banked by a pool of absorb points in
// front of hp. The pool spends whole points, arms a short shock whenever a hit spends any of it, and
// climbs back at a fixed rate once the shock is over — a small hole closes fast, a spent pool takes its
// time, and continuous fire therefore turns a shielded hull back into plain hp. No hull starts with one;
// impact and mine contact bypass the pool exactly as they bypass armor.
public class ShieldCombatTest
{
    private const float Dt = 1f / 120f;

    // ---- the rule itself ----

    [Fact]
    public void ThePoolSpendsWholePoints_RoundedUpSoAnAlmostEmptyPoolBreaks()
    {
        Assert.Equal(4, ShieldRules.Soak(16f, 4));
        Assert.Equal(2, ShieldRules.Soak(2f, 10));      // what the pool has, not what the hit asked for
        Assert.Equal(1, ShieldRules.Soak(0.5f, 4));     // a fraction still spends a whole point
        Assert.Equal(0, ShieldRules.Soak(0f, 4));       // and an empty pool spends nothing at all
    }

    [Fact]
    public void ThePaybackTimeIsProportionalToTheHole()
    {
        // Shock first, then 16 points a second: a 4-point hole is whole in a quarter of a second, a spent
        // 16 pool in one second, a spent 32 ceiling in two.
        Assert.Equal(2.25f, ShieldRules.SecondsToFull(12f, 16), 3);
        Assert.Equal(3f, ShieldRules.SecondsToFull(0f, 16), 3);
        Assert.Equal(4f, ShieldRules.SecondsToFull(0f, ShieldRules.ShieldCeiling), 3);
    }

    [Fact]
    public void BuyingACardBanksWhatItGrants_AndLeavesTheShockAlone()
    {
        var bought = ShieldRules.WithCard(new Shield(Points: 4f, Max: 16, Offline: 1.5f), 16);
        Assert.Equal(32, bought.Max);
        Assert.Equal(20f, bought.Points);        // banked at once, not after the wait
        Assert.Equal(1.5f, bought.Offline);      // a fight already in progress is not forgiven

        // And never past the ceiling, which is why the offer withdraws the card there rather than
        // dealing it as a no-op.
        var atCeiling = ShieldRules.WithCard(new Shield(Points: 30f, Max: ShieldRules.ShieldCeiling, Offline: 0f), 16);
        Assert.Equal(ShieldRules.ShieldCeiling, atCeiling.Max);
        Assert.Equal(32f, atCeiling.Points);
    }

    // ---- a hit against a shielded hull ----

    [Fact]
    public void ThePoolDrinksAHitWhole_TheHullLosesNothing()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 16f);
        AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 4);

        Step(em);

        Assert.Equal(80, em.GetComponent<Health>(player).Current);
        var shield = em.GetComponent<Shield>(player);
        Assert.Equal(12f, shield.Points);
        Assert.Equal(ShieldRules.ShockSeconds, shield.Offline);   // a hit that spent points armed the shock
        Assert.True(em.HasComponent<ShieldFlash>(player), "a soaked hit must flash the ring");
    }

    [Fact]
    public void ArmorTaxesTheHitBeforeThePoolEverSeesIt()
    {
        var (em, player) = SetupWorld();
        em.AddComponent(player, new Armor(4));
        GiveShield(em, player, points: 16f);
        AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 4);

        Step(em);

        // Armor takes the 4 down to the floor of 1, and the pool pays that 1.
        Assert.Equal(80, em.GetComponent<Health>(player).Current);
        Assert.Equal(15f, em.GetComponent<Shield>(player).Points);
    }

    [Fact]
    public void AHitBiggerThanThePoolSpillsOntoTheHull()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 2f, max: 16);
        AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 10);

        Step(em);

        Assert.Equal(72, em.GetComponent<Health>(player).Current);
        var shield = em.GetComponent<Shield>(player);
        Assert.Equal(0f, shield.Points);
        Assert.True(em.HasComponent<ShieldBurst>(player), "the pool breaking must fire the shatter");
    }

    [Fact]
    public void AHalfPointPoolSpendsItsLastPointAndBreaks()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 0.5f, max: 16);
        AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 4);

        Step(em);

        // Rounding up is what stops a pool from leaving an unspendable fraction behind, forever invisible,
        // never breaking.
        Assert.Equal(0f, em.GetComponent<Shield>(player).Points);
        Assert.Equal(77, em.GetComponent<Health>(player).Current);
        Assert.True(em.HasComponent<ShieldBurst>(player));
    }

    [Fact]
    public void AnEmptyPoolSpendsNothing_ArmsNoShock_FlashsWithNothing()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 0f, max: 16);
        AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 4);

        Step(em);

        var shield = em.GetComponent<Shield>(player);
        Assert.Equal(76, em.GetComponent<Health>(player).Current);
        Assert.Equal(0f, shield.Points);
        Assert.Equal(0f, shield.Offline);   // a hit on an already dry pool must not re-arm the shock
        Assert.False(em.HasComponent<ShieldFlash>(player));
        Assert.Equal(0, Count<ShieldShard>(em));   // the shatter fires once, not every hit on a dry pool
    }

    [Fact]
    public void TwoRoundsInOneTickBothDrinkThePool()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 16f);
        AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 4);
        AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 4);

        Step(em);

        // The pool is written back the way Health is: several rounds soak off one hull in a tick, and a
        // per-command write would leave only the last of them standing.
        Assert.Equal(8f, em.GetComponent<Shield>(player).Points);
        Assert.Equal(80, em.GetComponent<Health>(player).Current);
    }

    [Fact]
    public void AFullySoakedHitSpawnsNoAmberSpark_AHitThatReachesTheHullDoes()
    {
        var (soaked, soakedPlayer) = SetupWorld();
        GiveShield(soaked, soakedPlayer, points: 16f);
        AddEnemyRoundAt(soaked, new Vector2(-16f, 0f), damage: 4);
        Step(soaked);

        var (spent, spentPlayer) = SetupWorld();
        GiveShield(spent, spentPlayer, points: 2f, max: 16);
        AddEnemyRoundAt(spent, new Vector2(-16f, 0f), damage: 4);
        Step(spent);

        // Amber is the game's "this hull is being hit" colour; the ring's flash carries a soaked hit on its
        // own, and a partial soak shows both reads at once (plans/SHIELDS.md §7).
        Assert.Equal(0, Count<DamageSpark>(soaked));
        Assert.True(Count<DamageSpark>(spent) > 0);
        Assert.True(soaked.HasComponent<ShieldFlash>(soakedPlayer));
        Assert.True(spent.HasComponent<ShieldFlash>(spentPlayer));
    }

    [Fact]
    public void TheFlashBearsTheDirectionTheRoundCameFrom()
    {
        var (left, leftPlayer) = SetupWorld();
        GiveShield(left, leftPlayer, points: 16f);
        AddEnemyRoundAt(left, new Vector2(-16f, 0f), damage: 4, velocity: new Vector2(200f, 0f));
        Step(left);
        AssertNearBearing(left.GetComponent<ShieldFlash>(leftPlayer).BearingDeg, 180f);

        var (above, abovePlayer) = SetupWorld();
        GiveShield(above, abovePlayer, points: 16f);
        AddEnemyRoundAt(above, new Vector2(0f, -16f), damage: 4, velocity: new Vector2(0f, 200f));
        Step(above);
        AssertNearBearing(above.GetComponent<ShieldFlash>(abovePlayer).BearingDeg, -90f);
    }

    [Fact]
    public void ABreakSpawnsItsShardsOnceAndTheyFlyOutsideTheRing()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 2f, max: 16);
        AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 10);

        Step(em);

        Assert.Equal(ShieldRules.ShardCount, Count<ShieldShard>(em));
        float ring = ShieldRules.RingRadius(18f);
        foreach (var (_, shard, pos) in em.GetEntitiesWithComponents<ShieldShard, Position>())
        {
            Assert.True(shard.Lifetime > 0f);
            Assert.True(pos.Value.Magnitude >= ring,   // the hull sits at the origin in this fixture
                "a shard starts on the field it broke off, not inside the hull");
        }
    }

    [Fact]
    public void TheFlashAndTheShatterClearWhenTheirLivesEnd()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 2f, max: 16);
        AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 10);
        Step(em);
        Assert.True(em.HasComponent<ShieldFlash>(player));
        Assert.True(em.HasComponent<ShieldBurst>(player));

        StepShield(em, 0.3f);   // longer than either lifetime

        Assert.False(em.HasComponent<ShieldFlash>(player), "a leftover flash welds a bright arc to the hull");
        Assert.False(em.HasComponent<ShieldBurst>(player), "a leftover burst leaves a ring hanging on the hull");
    }

    // ---- what the pool does NOT touch ----

    [Fact]
    public void MineContactIgnoresThePool()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 16f);

        var mine = em.CreateEntity();
        em.AddComponent(mine, new Position(new Vector2(1f, 0f)));
        em.AddComponent(mine, new EnemyMine(MineSize.Large, Speed: 0f, Angle: 0f));
        em.AddComponent(mine, new Health(2, 2));

        Step(em);

        Assert.Equal(80 - MineStats.ContactDamage, em.GetComponent<Health>(player).Current);
        Assert.Equal(16f, em.GetComponent<Shield>(player).Points);
    }

    [Fact]
    public void ImpactDamageIgnoresThePool()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 16f);
        em.AddComponent(player, new Velocity(new Vector2(800f, 0f)));

        var rock = em.CreateEntity();
        em.AddComponent(rock, new Position(new Vector2(24f, 0f)));
        em.AddComponent(rock, new Asteroid(IsSmall: true, Radius: 10f));
        em.AddComponent(rock, new Health(Asteroid.SmallHealth, Asteroid.SmallHealth));

        Step(em);

        int lost = 80 - em.GetComponent<Health>(player).Current;
        Assert.True(lost >= 1, "the ram should land a real impact — the fixture stopped meaning anything");
        Assert.Equal(16f, em.GetComponent<Shield>(player).Points);
    }

    // ---- the balance shape the whole design rests on ----

    [Fact]
    public void ContinuousFireReArmsTheShock_ThePoolPaysForFourHitsAndNoMore()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 16f);

        // One 4-damage round every half second for three seconds — six hits, each re-arming the 2 s
        // shock, so the pool never gets a chance to refill: it drinks four of them and the hull eats the
        // other two. This is the guard against the pool quietly becoming a damage-per-second tank.
        for (int tick = 0; tick < 360; tick++)
        {
            if (tick % 60 == 0) AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 4);
            Step(em);
            StepShield(em, Dt);
        }

        Assert.Equal(0f, em.GetComponent<Shield>(player).Points);
        Assert.Equal(72, em.GetComponent<Health>(player).Current);
    }

    [Fact]
    public void AShieldLeftAloneComesBackInProportionToTheHole()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 16f);
        AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 4);
        Step(em);
        Assert.Equal(12f, em.GetComponent<Shield>(player).Points);

        // A quarter of the pool gone — and the shock says nothing comes back for two full seconds.
        StepShield(em, 1f);
        Assert.Equal(12f, em.GetComponent<Shield>(player).Points);
        Assert.True(em.GetComponent<Shield>(player).Offline > 0f);

        // Then 16 points a second, so the 4-point hole is whole just after the shock ends.
        StepShield(em, 2.5f);
        Assert.Equal(16f, em.GetComponent<Shield>(player).Points);
    }

    [Fact]
    public void ARegenStepNeverDropsThisTicksSpend()
    {
        var (em, player) = SetupWorld();
        GiveShield(em, player, points: 16f);
        AddEnemyRoundAt(em, new Vector2(-16f, 0f), damage: 4);

        // The shipped order: combat in Resolution, refill in Intent, each behind its own flush.
        var view = new WorldView(em);
        var combat = new CommandBuffer();
        new CollisionSystem().Update(view, Dt, combat);
        combat.Apply(em);
        Assert.Equal(12f, em.GetComponent<Shield>(player).Points);

        var regen = new CommandBuffer();
        new ShieldSystem().Update(view, Dt, regen);
        regen.Apply(em);

        // Run the other way round — the ShieldSystem reading the pool as it was before the flush and
        // writing it back afterwards — and the hit would be undone. TROUBLE_SHOOTING #58.
        Assert.Equal(12f, em.GetComponent<Shield>(player).Points);
        Assert.True(em.GetComponent<Shield>(player).Offline > 0f);
    }

    [Fact]
    public void ShieldSystemRunsLastInTheIntentPhase()
    {
        // The placement is load-bearing, so it is guarded rather than commented: anywhere earlier, the
        // refill writes a pre-hit copy of the pool back over the spend (TROUBLE_SHOOTING #58).
        var runner = new SimulationRunner();
        Assert.IsType<ShieldSystem>(runner.IntentSystems[^1]);
        Assert.DoesNotContain(runner.ResolutionSystems, system => system is ShieldSystem);
    }

    // ---- the table rules ----

    [Fact]
    public void NoHullStartsWithAShield_PlayerOrEnemy()
    {
        foreach (var hull in ShipType.All)
        {
            var (em, player, _) = WorldFactory.Create(hull, new Vector2(1920f, 1080f));
            Assert.False(em.HasComponent<Shield>(player), $"{hull.Name} starts with a shield");
        }

        foreach (var type in EnemyShipType.All)
        {
            var em = new EntityManager();
            var ship = em.CreateEntity();
            EnemyShipFactory.AddComponents(em, ship, Vector2.Zero, Vector2.Zero, 0f, 0f, type, EnemyArmament.Guns, 0f);
            Assert.False(em.HasComponent<Shield>(ship), $"{type.TextureKey} starts with a shield");
        }
    }

    [Fact]
    public void ShieldCard_IsOfferedFromLevelTen_AndWithdrawnAtTheCeiling()
    {
        var (em, player) = CreateHull(ShipType.Fighter, UpgradeStats.XpForLevel(10), level: 9);

        Assert.False(OffersShieldCard(em, player, level: 9), "the pool unlocks with tier 4, not before");
        Assert.True(OffersShieldCard(em, player, level: 10));

        // At the ceiling the card is withdrawn rather than dealt as a lie (R19's rule).
        em.AddComponent(player, new Shield(ShieldRules.ShieldCeiling, ShieldRules.ShieldCeiling, 0f));
        Assert.False(OffersShieldCard(em, player, level: 10));
    }

    // A shield card buys 16 points of pool at a milestone and half that (+8) on an ordinary level
    // (plans/UPGRADE_BUNDLE_PAIRS.md §2), so the 32-point ceiling is two milestone buys or four ordinary ones.
    [Fact]
    public void TheShieldCardIsAWholePoolStepAtAMilestone_AndHalfOneOrdinarily()
    {
        Assert.Equal(16, UpgradeDefinition.Shield.Additive);
        Assert.Equal("+16", UpgradeDefinition.Shield.DisplayValue);
        Assert.Equal(16, UpgradeDefinition.DefenceStep(UpgradeOption.Shield, DefenceTier.Milestone));
        Assert.Equal(8, UpgradeDefinition.DefenceStep(UpgradeOption.Shield, DefenceTier.Ordinary));
        Assert.Equal(32, ShieldRules.ShieldCeiling);   // exactly two milestone cards
    }

    // ---- helpers ----

    static (EntityManager Em, Entity Player) SetupWorld()
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 80, Radius: 18f));
        em.AddComponent(player, new Health(80, 80));
        return (em, player);
    }

    static void GiveShield(EntityManager em, Entity player, float points, int max = 16) =>
        em.AddComponent(player, new Shield(points, max, Offline: 0f));

    // A round already inside the hull, travelling onward: it is spent on the player this tick.
    static void AddEnemyRoundAt(EntityManager em, Vector2 position, int damage, Vector2? velocity = null)
    {
        var round = em.CreateEntity();
        em.AddComponent(round, new Position(position));
        em.AddComponent(round, new Ammo(
            Velocity: velocity ?? new Vector2(200f, 0f), Radius: 2.5f, Lifetime: 10f,
            IsEnemy: true, Damage: damage));
    }

    static void Step(EntityManager em)
    {
        var commands = new CommandBuffer();
        new CollisionSystem().Update(new WorldView(em), Dt, commands);
        commands.Apply(em);
    }

    static void StepShield(EntityManager em, float seconds)
    {
        var view = new WorldView(em);
        for (int tick = 0; tick < (int)MathF.Round(seconds / Dt); tick++)
        {
            var commands = new CommandBuffer();
            new ShieldSystem().Update(view, Dt, commands);
            commands.Apply(em);
        }
    }

    static int Count<T>(EntityManager em) where T : notnull
    {
        int found = 0;
        foreach (var _ in em.GetEntitiesWithComponents<T>()) found++;
        return found;
    }

    static void AssertNearBearing(float actual, float expected)
        => Assert.True(MathF.Abs(MathF.Abs(actual) - MathF.Abs(expected)) < 5f,
            $"bearing {actual}° should be ±{expected}°");

    // The strong pool draws armour and shield beside its gun cards, so one hand that lacks the shield card
    // proves nothing — roll enough hands that "never" is a real answer.
    static bool OffersShieldCard(EntityManager em, Entity player, int level)
    {
        var view = new WorldView(em);
        for (int roll = 0; roll < 60; roll++)
        {
            var hand = UpgradeOffer.Roll(view, player, level, "test");
            if (hand is not null && hand.SelectMany(bundle => new[] { bundle.Powerful, bundle.Weak })
                    .Any(card => card.Stat == UpgradeOption.Shield)) return true;
        }
        return false;
    }

    static (EntityManager Em, Entity Player) CreateHull(ShipType ship, int xp, int level)
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Rotation(0f));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 40, Radius: 18f, Xp: xp, Level: level, PickupRadius: 150f, RotationSpeed: 1f));

        foreach (var slot in ship.Slots)
            em.AddComponent(em.CreateEntity(), new TurretSlot(slot));

        foreach (var mount in ship.Loadout.Mounts)
        {
            var view = new WorldView(em);
            Assert.True(TurretMount.TryFindSlot(view, mount.SlotName, out var slotEntity));
            TurretMount.Mount(em, slotEntity, view.GetComponent<TurretSlot>(slotEntity).Definition, mount.Weapon, Vector2.Zero, hullRotation: 0f);
        }

        return (em, player);
    }
}
