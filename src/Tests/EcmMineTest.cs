using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// A jammed mine stops hunting the hull and stops detonating on contact: it rebounds off the ship and costs
// the player nothing, which is what turns a mine field into something the player can shoot clear while it
// cannot answer (plans/ECM_PULSE_AND_SALVAGE_FIELD.md D9).
public class EcmMineTest
{
    const float Dt = 1f / 60f;
    const int PlayerHp = 20;

    // A small mine (radius 7.5) against an 18 px hull: 20 px apart is inside the 25.5 px contact distance,
    // with the contact normal running mine → player, i.e. along -x.
    private static (EntityManager Em, Entity Player, Entity Mine) OverlappingWorld(bool jammed)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: PlayerHp, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(PlayerHp, PlayerHp));

        var mine = em.CreateEntity();
        em.AddComponent(mine, new Position(new Vector2(20f, 0f)));
        em.AddComponent(mine, new Velocity(new Vector2(0f, -40f)));   // drifting down at the player
        em.AddComponent(mine, new EnemyMine(MineSize.Small, 40f, 0f));
        em.AddComponent(mine, new Health(MineType.Small.Health, MineType.Small.Health));

        if (jammed) em.AddComponent(mine, new EcmDisabled(EcmStats.Duration, 0f));

        return (em, player, mine);
    }

    private static void Resolve(EntityManager em)
    {
        var commands = new CommandBuffer();
        new CollisionSystem().Update(new WorldView(em), Dt, commands);
        commands.Apply(em);
    }

    [Fact]
    public void JammedMineBouncesOffTheHullAndCostsNothing()
    {
        var (em, player, mine) = OverlappingWorld(jammed: true);

        Resolve(em);

        Assert.True(em.HasComponent<EnemyMine>(mine), "a jammed mine does not detonate");
        Assert.Equal(PlayerHp, em.GetComponent<Health>(player).Current);

        var velocity = em.GetComponent<Velocity>(mine).Value;
        Assert.True(velocity.X > 0f, "the mine leaves along the contact normal, away from the hull");
        Assert.True(em.GetComponent<Position>(mine).Value.X > 20f, "and is pushed clear of the overlap");
    }

    // The control that makes the test above mean something: an unhindered mine on the same contact kills
    // itself and takes 3 hp with it.
    [Fact]
    public void AnOrdinaryMineStillDetonatesOnContact()
    {
        var (em, player, mine) = OverlappingWorld(jammed: false);

        Resolve(em);

        Assert.False(em.HasComponent<EnemyMine>(mine));
        Assert.Equal(PlayerHp - MineStats.ContactDamage, em.GetComponent<Health>(player).Current);
    }

    // The jam takes the mine's hunting out too, and that is what lets a bounce actually separate the two
    // instead of steering the mine straight back into the hull on the next tick.
    [Fact]
    public void JammedMineStopsHuntingTheHull()
    {
        var (jammedEm, _, jammedMine) = OverlappingWorld(jammed: true);
        var (freeEm, _, freeMine) = OverlappingWorld(jammed: false);
        var velocityBefore = jammedEm.GetComponent<Velocity>(jammedMine).Value;

        var jammedCommands = new CommandBuffer();
        new MineDriftSystem().Update(new WorldView(jammedEm), Dt, jammedCommands);
        jammedCommands.Apply(jammedEm);

        var freeCommands = new CommandBuffer();
        new MineDriftSystem().Update(new WorldView(freeEm), Dt, freeCommands);
        freeCommands.Apply(freeEm);

        Assert.Equal(velocityBefore, jammedEm.GetComponent<Velocity>(jammedMine).Value);
        Assert.NotEqual(velocityBefore, freeEm.GetComponent<Velocity>(freeMine).Value);
    }
}
