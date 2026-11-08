using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// One tracking rule for every pickup, orbs and capsules alike: it lies where it fell until the hull comes
// inside its range, then it commits and closes at a speed that matches the hull, so nothing the hull has
// briefly been near can be shaken off (plans/LOOT_DROPS.md, plans/ECM_PULSE_AND_SALVAGE_FIELD.md).
//
// A health orb used to accelerate toward the hull and top out at 350 px/s — under a boosting hull's cruise
// speed — so a player who touched one and kept flying often never got it. The capsules followed that same
// debris pull and are why the rule now covers all of them: a salvage capsule could be grazed, commit to
// nothing, and then trail the hull at 350 px/s until its 90 s clock ran out — loot the player had already
// earned, lost.
public class PickupChaseTest
{
    const float Dt = 1f / 120f;
    const float HullSpeed = 1500f;
    const int ChaseTicks = 200;

    // A swept orb starts ~960 px behind a hull doing 1500 px/s and closes on it at the chase margin
    // (375 px/s), so it needs ~2.6 s of chasing to arrive.
    const int SweepTicks = 400;
    const float MagnetRadius = 150f;

    // A hull already doing 1500 px/s with one pickup 100 px behind it — inside the magnet radius, so the
    // first tick is the one that commits it — and flying straight away from it. Every case below is the
    // worst one: the hull never slows down and never turns back.
    private static (EntityManager Em, Entity Player, Entity Pickup) World(int hp, Action<EntityManager, Entity> addPickup)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(100f, 0f)));
        em.AddComponent(player, new Velocity(new Vector2(HullSpeed, 0f)));
        em.AddComponent(player, new Player(Thrust: 400f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 100, Radius: 18f, PickupRadius: MagnetRadius));
        em.AddComponent(player, new Health(hp, 100));

        var pickup = em.CreateEntity();
        em.AddComponent(pickup, new Position(Vector2.Zero));
        addPickup(em, pickup);

        return (em, player, pickup);
    }

    // One tick of the app's order for this test: the magnet resolves, then the hull moves by its own
    // velocity the way PositionIntegrationSystem would.
    private static void Tick(EntityManager em, Entity player)
    {
        var commands = new CommandBuffer();
        new PickupMagnetSystem().Update(new WorldView(em), Dt, commands);
        commands.Apply(em);

        var pos = em.GetComponent<Position>(player).Value;
        if (em.TryGetComponent<Velocity>(player, out var vel))
            em.AddComponent(player, new Position(pos + vel.Value * Dt));
    }

    private static bool RunUntilCollected<T>(EntityManager em, Entity player, Entity pickup, int ticks = ChaseTicks) where T : notnull
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            Tick(em, player);
            if (!em.HasComponent<T>(pickup)) return true;
        }

        return false;
    }

    // The hull is faster than the pickup's floor speed, so the pickup has to match it with margin to win.
    // It does: the gap closes and the heal lands.
    [Fact]
    public void HealthOrb_CatchesAHullBoostingAwayFromIt()
    {
        var (em, player, orb) = World(50, (store, pickup) =>
            // Named defaults: `new HealthOrb()` would bind to the parameterless struct constructor and give
            // Lifetime 0 / Radius 0, which is a destroyed orb rather than a test subject (#63).
            store.AddComponent(pickup, new HealthOrb(Lifetime: 90f, Radius: 8f)));

        Assert.True(RunUntilCollected<HealthOrb>(em, player, orb), "a committed health orb must not be outrun by a boosting hull");
        Assert.Equal(62, em.GetComponent<Health>(player).Current);
    }

    // A committed orb stops aging. At 100 px behind a 1500 px/s hull it needs ~0.18 s to arrive, so a 0.05 s
    // fuse would have destroyed it on the third tick under the old rule — and the player would have watched
    // the orb they had already triggered simply rot away.
    [Fact]
    public void CommittedHealthOrb_OutlivesItsOwnFuse()
    {
        var (em, player, orb) = World(50, (store, pickup) =>
            store.AddComponent(pickup, new HealthOrb(Lifetime: 0.05f, Radius: 8f)));

        Assert.True(RunUntilCollected<HealthOrb>(em, player, orb), "an orb already hunting the hull should not expire mid-flight");
        Assert.Equal(62, em.GetComponent<Health>(player).Current);
    }

    // An orb outside the magnet radius and uncommitted still ages and still expires — the commit is what
    // lifts the fuse, not the mere existence of a health orb.
    [Fact]
    public void UncommittedHealthOrb_StillAgesAndExpires()
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(0f, 5000f)));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 100, Radius: 18f, PickupRadius: MagnetRadius));
        em.AddComponent(player, new Health(50, 100));

        var orb = em.CreateEntity();
        em.AddComponent(orb, new Position(Vector2.Zero));
        em.AddComponent(orb, new HealthOrb(Lifetime: 0.1f));

        for (int tick = 0; tick < 40; tick++) Tick(em, player);

        Assert.False(em.HasComponent<HealthOrb>(orb), "an orb nobody called in should still rot on its own clock");
    }

    // The same guarantee for the capsules, each with a fuse far shorter than the flight time: the hull
    // grazes it on the way past at 1500 px/s and the capsule still arrives. Under the debris pull it topped
    // out at 350 px/s, was never committed by the graze, and expired behind the hull.
    [Fact]
    public void RerollToken_GrazedByABoostingHull_StillArrivesAndStocksACharge()
    {
        var (em, player, capsule) = World(100, (store, pickup) =>
            store.AddComponent(pickup, new RerollToken(Lifetime: 0.05f, Radius: LootStats.RerollTokenRadius)));

        Assert.True(RunUntilCollected<RerollToken>(em, player, capsule), "a capsule the hull has touched must not fall behind it");
        Assert.Equal(1, StockCount(em, player));
    }

    // The capsule the report named: a salvage field grazed at speed still reaches the hull and still spends
    // itself, which means the XP orb behind the hull — out of the plain magnet range the whole time — is
    // committed from anywhere and follows.
    [Fact]
    public void SalvageField_GrazedByABoostingHull_StillArrivesAndSweepsTheField()
    {
        var (em, player, capsule) = World(100, (store, pickup) =>
            store.AddComponent(pickup, new SalvageField(Lifetime: 0.05f, Radius: LootStats.FieldCapsuleRadius)));
        var orb = em.CreateEntity();
        em.AddComponent(orb, new Position(new Vector2(-600f, 0f)));  // 700 px behind the hull, past its range
        em.AddComponent(orb, new XpPickup(XpAmount: 3, Lifetime: 90f, Radius: 6f));

        Assert.True(RunUntilCollected<SalvageField>(em, player, capsule), "a field capsule the hull has touched must not be outflown");
        Assert.True(em.GetComponent<XpPickup>(orb).Chased, "the field committed an orb that was never inside the hull's range");

        for (int tick = 0; tick < SweepTicks && em.HasComponent<XpPickup>(orb); tick++) Tick(em, player);

        Assert.False(em.HasComponent<XpPickup>(orb), "the swept orb follows a boosting hull all the way in");
        Assert.Equal(3, em.GetComponent<Player>(player).Xp);
    }

    // The ECM capsule has no fuse, so what the old rule cost it was not expiry but the chase: a hull that
    // brushed it and ran left a capsule following at 350 px/s and never closing. Now the graze commits it,
    // and the jam lands on a hull the player is long clear of.
    [Fact]
    public void EcmCapsule_GrazedByABoostingHull_StillArrivesAndJams()
    {
        var (em, player, capsule) = World(100, (store, pickup) =>
            store.AddComponent(pickup, new EcmPulse(Radius: LootStats.EcmCapsuleRadius)));
        var ship = em.CreateEntity();
        EnemyShipFactory.AddComponents(em, ship, new Vector2(0f, 900f), new Vector2(0f, -60f), 0f, 0f, EnemyShipType.Default, EnemyArmament.Guns);

        Assert.True(RunUntilCollected<EcmPulse>(em, player, capsule), "a capsule the hull has touched follows it instead of trailing it");
        Assert.True(em.HasComponent<EcmDisabled>(ship), "the capsule fired the jam when it finally arrived");
    }

    // The other half of the rule: until the hull comes inside its range a capsule lies exactly where it fell.
    // The ECM capsule in particular has to stay a thing the player goes and takes.
    [Fact]
    public void Capsule_OutsideTheHullsRange_LiesWhereItFell()
    {
        var (em, player, capsule) = World(100, (store, pickup) =>
            store.AddComponent(pickup, new EcmPulse(Radius: LootStats.EcmCapsuleRadius)));
        em.AddComponent(capsule, new Position(new Vector2(0f, 600f)));   // 608 px out, well past the 150 px range

        for (int tick = 0; tick < 30; tick++) Tick(em, player);

        Assert.Equal(0f, em.GetComponent<Position>(capsule).Value.X, precision: 3);
        Assert.Equal(600f, em.GetComponent<Position>(capsule).Value.Y, precision: 3);
    }

    // The chase speed is the hull's speed with margin, floored at the committed speed and capped so a
    // runaway hull is not chased by a hypersonic pickup. The floor is the speed XP orbs have always used, so
    // up to 960 px/s of hull speed every pickup tracks the hull exactly as an XP orb does — and above it,
    // which is where capsules used to lose, every pickup matches the hull instead.
    [Fact]
    public void ChaseSpeed_MatchesTheHullBetweenFloorAndCap()
    {
        Assert.Equal(LootStats.CommittedChaseSpeed, LootStats.ChaseSpeedFor(0f), precision: 3);
        Assert.Equal(LootStats.CommittedChaseSpeed, LootStats.ChaseSpeedFor(900f), precision: 3);
        Assert.Equal(HullSpeed * LootStats.ChasePlayerSpeedFactor, LootStats.ChaseSpeedFor(HullSpeed), precision: 3);
        Assert.Equal(LootStats.ChaseSpeedCap, LootStats.ChaseSpeedFor(100_000f), precision: 3);
    }

    static int StockCount(EntityManager em, Entity player) =>
        em.TryGetComponent<RerollStock>(player, out var stock) ? stock.Count : 0;
}
