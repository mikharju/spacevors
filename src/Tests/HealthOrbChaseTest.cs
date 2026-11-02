using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// A health orb used to accelerate toward the hull and top out at 350 px/s — under a boosting hull's cruise
// speed — so a player who touched one and kept flying often never got it. A committed orb now closes at a
// speed that matches the hull it is following, which is what this file pins (plans/
// ECM_PULSE_AND_SALVAGE_FIELD.md).
public class HealthOrbChaseTest
{
    const float Dt = 1f / 120f;
    const float HullSpeed = 1500f;

    // An orb 100 px behind a hull already doing 1500 px/s, inside the magnet radius, so it is committed.
    private static (EntityManager Em, Entity Player, Entity Orb) World(int hp)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(100f, 0f)));
        em.AddComponent(player, new Velocity(new Vector2(HullSpeed, 0f)));
        em.AddComponent(player, new Player(Thrust: 400f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 100, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(hp, 100));

        var orb = em.CreateEntity();
        em.AddComponent(orb, new Position(Vector2.Zero));
        // Named defaults: `new HealthOrb()` would bind to the parameterless struct constructor and give
        // Lifetime 0 / Radius 0, which is a destroyed orb rather than a test subject (#63).
        em.AddComponent(orb, new HealthOrb(Lifetime: 90f, Radius: 8f));

        return (em, player, orb);
    }

    // One tick of the app's order for this test: the magnet resolves, then the hull moves by its own
    // velocity the way PositionIntegrationSystem would.
    private static void Tick(EntityManager em, Entity player)
    {
        var commands = new CommandBuffer();
        new PickupMagnetSystem().Update(new WorldView(em), Dt, commands);
        commands.Apply(em);

        var pos = em.GetComponent<Position>(player).Value;
        var vel = em.GetComponent<Velocity>(player).Value;
        em.AddComponent(player, new Position(pos + vel * Dt));
    }

    // The hull is faster than the orb's flat committed speed, so the orb has to match it with margin to win.
    // It does: the gap closes and the heal lands.
    [Fact]
    public void HealthOrb_CatchesAHullBoostingAwayFromIt()
    {
        var (em, player, orb) = World(hp: 50);

        bool collected = false;
        for (int tick = 0; tick < 200 && !collected; tick++)
        {
            Tick(em, player);
            collected = !em.HasComponent<HealthOrb>(orb);
        }

        Assert.True(collected, "a committed health orb must not be outrun by a boosting hull");
        Assert.Equal(62, em.GetComponent<Health>(player).Current);
    }

    // A committed orb stops aging. At 149 px out it needs ~0.12 s to arrive, so a 0.05 s fuse would have
    // destroyed it on the third tick under the old rule — and the player would have watched the orb they had
    // already triggered simply rot away.
    [Fact]
    public void CommittedHealthOrb_OutlivesItsOwnFuse()
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(100f, 0f)));
        em.AddComponent(player, new Velocity(new Vector2(HullSpeed, 0f)));
        em.AddComponent(player, new Player(Thrust: 400f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 100, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(50, 100));

        var orb = em.CreateEntity();
        em.AddComponent(orb, new Position(Vector2.Zero));   // 100 px behind the hull, inside the magnet
        em.AddComponent(orb, new HealthOrb(Lifetime: 0.05f, Radius: 8f));

        bool collected = false;
        for (int tick = 0; tick < 60 && !collected; tick++)
        {
            Tick(em, player);
            collected = !em.HasComponent<HealthOrb>(orb);
        }

        Assert.True(collected, "an orb already hunting the hull should not expire mid-flight");
        Assert.Equal(62, em.GetComponent<Health>(player).Current);
    }

    // An orb outside the magnet radius and uncommitted still ages and still expires — the sweep and the
    // commit are what lift the fuse, not the mere existence of a health orb.
    [Fact]
    public void UncommittedHealthOrb_StillAgesAndExpires()
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(0f, 5000f)));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 100, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(50, 100));

        var orb = em.CreateEntity();
        em.AddComponent(orb, new Position(Vector2.Zero));
        em.AddComponent(orb, new HealthOrb(Lifetime: 0.1f));

        var view = new WorldView(em);
        for (int tick = 0; tick < 40; tick++)
        {
            var commands = new CommandBuffer();
            new PickupMagnetSystem().Update(view, Dt, commands);
            commands.Apply(em);
        }

        Assert.False(em.HasComponent<HealthOrb>(orb), "an orb nobody called in should still rot on its own clock");
    }

    // The chase speed is the hull's speed with margin, floored at the committed speed and capped so a
    // runaway hull is not chased by a hypersonic orb.
    [Fact]
    public void ChaseSpeed_MatchesTheHullBetweenFloorAndCap()
    {
        Assert.Equal(LootStats.CommittedChaseSpeed, LootStats.HealthOrbChaseSpeedFor(0f), precision: 3);
        Assert.Equal(LootStats.CommittedChaseSpeed, LootStats.HealthOrbChaseSpeedFor(900f), precision: 3);
        Assert.Equal(HullSpeed * LootStats.HealthOrbChasePlayerSpeedFactor, LootStats.HealthOrbChaseSpeedFor(HullSpeed), precision: 3);
        Assert.Equal(LootStats.HealthOrbChaseSpeedCap, LootStats.HealthOrbChaseSpeedFor(100_000f), precision: 3);
    }
}
