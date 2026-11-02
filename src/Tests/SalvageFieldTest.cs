using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// A salvage field is one decision, made on the tick the hull touches the capsule (plans/
// ECM_PULSE_AND_SALVAGE_FIELD.md): every XP orb and every currency capsule on the field comes in, plus only
// as many of the nearest health orbs as the hull needs to come back to full. Everything else stays put.
public class SalvageFieldTest
{
    const float Dt = 1f / 120f;

    // A hull at 76/100 needs two 12 hp orbs; at 100/100 it needs none. Both are the interesting cases.
    private static (EntityManager Em, Entity Player) World(int hp)
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 100, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(hp, 100));
        return (em, player);
    }

    // 30 px out: inside the capsule's collection distance (18 + 16), so the field is spent on the first tick.
    private static void AddField(EntityManager em)
    {
        var capsule = em.CreateEntity();
        em.AddComponent(capsule, new Position(new Vector2(30f, 0f)));
        em.AddComponent(capsule, new SalvageField(Radius: LootStats.FieldCapsuleRadius));
    }

    private static Entity AddOrb(EntityManager em, float x)
    {
        var orb = em.CreateEntity();
        em.AddComponent(orb, new Position(new Vector2(x, 0f)));
        // The defaults are named here on purpose: `new HealthOrb()` on a record struct whose parameters are
        // all optional binds to the parameterless struct constructor and yields zeros (TROUBLE_SHOOTING #63).
        em.AddComponent(orb, new HealthOrb(Lifetime: 90f, Radius: 8f));
        return orb;
    }

    private static Entity AddXp(EntityManager em, float x, float lifetime = 90f)
    {
        var orb = em.CreateEntity();
        em.AddComponent(orb, new Position(new Vector2(x, 0f)));
        em.AddComponent(orb, new XpPickup(3, lifetime, Radius: 6f));
        return orb;
    }

    private static void Tick(EntityManager em)
    {
        var commands = new CommandBuffer();
        new PickupMagnetSystem().Update(new WorldView(em), Dt, commands);
        commands.Apply(em);
    }

    // The sweep is a radius rather than a set of marked entities, so the XP pass still writes the orb's own
    // component exactly once this tick. Had the field queued a marker instead, the pass's pre-phase copy
    // would have overwritten it — the stale-write trap of TROUBLE_SHOOTING #58.
    [Fact]
    public void Field_CommitsEveryXpOrbOnTheFieldAndCreditsItExactlyOnce()
    {
        var (em, player) = World(hp: 100);
        AddField(em);
        var far = AddXp(em, 3000f);

        Tick(em);

        Assert.True(em.GetComponent<XpPickup>(far).Chased, "an orb 3000 px out should be committed by the field");
        Assert.True(em.GetComponent<Position>(far).Value.X < 3000f, "a committed orb is on its way");
        Assert.Equal(0, em.GetComponent<Player>(player).Xp);

        for (int tick = 0; tick < 600 && em.HasComponent<XpPickup>(far); tick++) Tick(em);

        Assert.Equal(3, em.GetComponent<Player>(player).Xp);
    }

    // A swept orb whose fuse is shorter than its flight time still pays: a committed pickup stops aging, or
    // the sweep would quietly steal the loot it promised.
    [Fact]
    public void Field_SweptOrbDoesNotExpireMidFlight()
    {
        var (em, player) = World(hp: 100);
        AddField(em);
        var shortLived = AddXp(em, 2000f, lifetime: 0.5f);

        for (int tick = 0; tick < 600 && em.HasComponent<XpPickup>(shortLived); tick++) Tick(em);

        Assert.Equal(3, em.GetComponent<Player>(player).Xp);
    }

    // 76/100 needs two orbs, so the two nearest are called and the two furthest are left exactly where they
    // lie — the field is a top-up, not a vacuum.
    [Fact]
    public void Field_PullsOnlyTheNearestHealthOrbsTheHullNeeds()
    {
        var (em, _) = World(hp: 76);
        AddField(em);
        Entity near = AddOrb(em, 100f);
        Entity second = AddOrb(em, 300f);
        Entity third = AddOrb(em, 600f);
        Entity fourth = AddOrb(em, 900f);

        Tick(em);

        Assert.True(em.GetComponent<HealthOrb>(near).Chased);
        Assert.True(em.GetComponent<HealthOrb>(second).Chased, "the second orb is still needed to fill the hull");
        Assert.False(em.GetComponent<HealthOrb>(third).Chased, "two orbs already fill the hull");
        Assert.Equal(600f, em.GetComponent<Position>(third).Value.X, precision: 3);
        Assert.Equal(900f, em.GetComponent<Position>(fourth).Value.X, precision: 3);
    }

    [Fact]
    public void Field_AtFullHp_TakesNoHealthOrbAtAll()
    {
        var (em, _) = World(hp: 100);
        AddField(em);
        var orb = AddOrb(em, 600f);

        Tick(em);

        Assert.False(em.GetComponent<HealthOrb>(orb).Chased);
        Assert.Equal(600f, em.GetComponent<Position>(orb).Value.X, precision: 3);
    }

    // Fewer orbs on the field than the hull needs: it takes every one of them — "if available", and no more.
    [Fact]
    public void Field_WhenFewerOrbsExistThanNeeded_PullsThemAll()
    {
        var (em, _) = World(hp: 40);
        AddField(em);
        Entity first = AddOrb(em, 300f);
        Entity second = AddOrb(em, 900f);

        Tick(em);

        Assert.True(em.GetComponent<HealthOrb>(first).Chased);
        Assert.True(em.GetComponent<HealthOrb>(second).Chased);
    }

    // Currency is anything the player banks, so the reroll capsules come in from anywhere. An ECM capsule is
    // not currency: it is a field resource, and it waits where it fell for as long as the run does.
    [Fact]
    public void Field_PullsCurrencyButNeverAnEcmCapsule()
    {
        var (em, _) = World(hp: 100);
        AddField(em);

        var token = em.CreateEntity();
        em.AddComponent(token, new Position(new Vector2(2000f, 0f)));
        em.AddComponent(token, new RerollToken(Radius: LootStats.RerollTokenRadius));

        var capsule = em.CreateEntity();
        em.AddComponent(capsule, new Position(new Vector2(2000f, 60f)));
        em.AddComponent(capsule, new EcmPulse(Radius: LootStats.EcmCapsuleRadius));

        Tick(em);

        Assert.True(em.GetComponent<RerollToken>(token).Chased, "currency is called in from anywhere");
        Assert.False(em.TryGetComponent<Velocity>(capsule, out var velocity) && velocity.Value.Magnitude > 0f,
            "an ECM capsule is never swept");
        Assert.Equal(2000f, em.GetComponent<Position>(capsule).Value.X, precision: 3);
    }
}
