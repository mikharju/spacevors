using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// A salvage capsule is a pickup: magnetised like a health orb, and worth one reroll charge when it is
// collected (plans/REROLL_TOKENS.md).
public class RerollTokenTest
{
    [Fact]
    public void Token_FarFromPlayer_AgesSpinsAndExpires()
    {
        var (em, player, token) = CreateWorld(tokenDistance: 1000f);
        em.AddComponent(token, new RerollToken(Lifetime: 0.1f, Radius: LootStats.RerollTokenRadius));
        var system = new PickupMagnetSystem();
        var view = new WorldView(em);
        float dt = 1f / 120f;

        var commands = new CommandBuffer();
        system.Update(view, dt, commands);
        commands.Apply(em);

        var aged = em.GetComponent<RerollToken>(token);
        Assert.Equal(0.1f - dt, aged.Lifetime, precision: 5);
        Assert.Equal(LootStats.RerollTokenSpinRate * dt, aged.Spin, precision: 5);

        bool destroyed = false;
        for (int frame = 0; frame < 20 && !destroyed; frame++)
        {
            commands = new CommandBuffer();
            system.Update(view, dt, commands);
            commands.Apply(em);
            destroyed = !em.HasComponent<RerollToken>(token);
        }

        Assert.True(destroyed, "a capsule nobody collects should expire at the end of its lifetime");
        Assert.Equal(0, StockCount(em, player));
    }

    [Fact]
    public void Token_InsidePickupRadius_IsPulledInAndStocksOneCharge()
    {
        var (em, player, token) = CreateWorld(tokenDistance: 60f);
        var system = new PickupMagnetSystem();
        var view = new WorldView(em);
        float dt = 1f / 120f;

        bool collected = false;
        for (int frame = 0; frame < 120 && !collected; frame++)
        {
            var commands = new CommandBuffer();
            system.Update(view, dt, commands);
            commands.Apply(em);
            collected = !em.HasComponent<RerollToken>(token);
        }

        Assert.True(collected, "a capsule inside the pickup radius should reach the hull");
        Assert.Equal(1, StockCount(em, player));
    }

    // Two capsules touching in the same tick must both count: the stock is written once per tick from the
    // number collected, not once per capsule through a stale component copy.
    [Fact]
    public void TwoTokens_CollectedInOneTick_StockBoth()
    {
        var (em, player, _) = CreateWorld(tokenDistance: 4f);
        var second = em.CreateEntity();
        em.AddComponent(second, new Position(new Vector2(4f, 2f)));
        em.AddComponent(second, new RerollToken(Radius: LootStats.RerollTokenRadius));

        var commands = new CommandBuffer();
        new PickupMagnetSystem().Update(new WorldView(em), 1f / 120f, commands);
        commands.Apply(em);

        Assert.Equal(2, StockCount(em, player));
    }

    // The stale-write trap of TROUBLE_SHOOTING #58: XP is ref-written by this same system, so a stock
    // write that went through the `Player` component would have dropped the XP this tick collected.
    [Fact]
    public void Token_And_XpOrb_CollectedInOneTick_KeepBothEffects()
    {
        var (em, player, _) = CreateWorld(tokenDistance: 4f);
        var orb = em.CreateEntity();
        em.AddComponent(orb, new Position(new Vector2(3f, 0f)));
        em.AddComponent(orb, new XpPickup(XpAmount: 3, Radius: 6f));

        var commands = new CommandBuffer();
        new PickupMagnetSystem().Update(new WorldView(em), 1f / 120f, commands);
        commands.Apply(em);

        Assert.Equal(3, em.GetComponent<Player>(player).Xp);
        Assert.Equal(1, StockCount(em, player));
    }

    static (EntityManager Em, Entity Player, Entity Token) CreateWorld(float tokenDistance)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, PickupRadius: 150f));

        var token = em.CreateEntity();
        em.AddComponent(token, new Position(new Vector2(tokenDistance, 0f)));
        em.AddComponent(token, new RerollToken(Radius: LootStats.RerollTokenRadius));

        return (em, player, token);
    }

    static int StockCount(EntityManager em, Entity player) =>
        em.TryGetComponent<RerollStock>(player, out var stock) ? stock.Count : 0;
}
