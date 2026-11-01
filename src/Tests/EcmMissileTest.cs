using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// An ECM-stunned enemy round keeps the heading it had — it is still a body flying past, just no longer a
// hunting one — shows no plume, and pops on its own staggered fuse (plans/ECM_PULSE_AND_SALVAGE_FIELD.md D10).
public class EcmMissileTest
{
    const float Dt = 1f / 60f;
    private static readonly Vector2 LaunchVelocity = new(300f, 0f);   // broadside to the player: it must turn

    private static (EntityManager Em, Entity Player, Entity Round) World(bool stunned, float detonateIn = 5f)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(0f, -400f)));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 20, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(20, 20));

        var round = em.CreateEntity();
        em.AddComponent(round, new Position(Vector2.Zero));
        em.AddComponent(round, new Velocity(LaunchVelocity));
        em.AddComponent(round, new Ammo(LaunchVelocity, Radius: 4f, Lifetime: 10f, IsEnemy: true, Damage: 2));
        em.AddComponent(round, new Missile(player, Speed: 300f, MaxSpeed: 300f, Acceleration: 0f, TurnRate: 2f, Steering: 0f));
        em.AddComponent(round, new Health(4, 4));

        if (stunned) em.AddComponent(round, new MissileStunned(detonateIn));

        return (em, player, round);
    }

    private static void Steer(EntityManager em)
    {
        var commands = new CommandBuffer();
        new MissileGuidanceSystem().Update(new WorldView(em), Dt, commands);
        commands.Apply(em);
    }

    [Fact]
    public void StunnedRoundKeepsTheHeadingItHad()
    {
        var (stunnedEm, _, stunnedRound) = World(stunned: true);
        var (freeEm, _, freeRound) = World(stunned: false);

        Steer(stunnedEm);
        Steer(freeEm);

        Assert.Equal(LaunchVelocity, stunnedEm.GetComponent<Velocity>(stunnedRound).Value);
        Assert.Equal(LaunchVelocity, stunnedEm.GetComponent<Ammo>(stunnedRound).Velocity);
        Assert.NotEqual(LaunchVelocity, freeEm.GetComponent<Velocity>(freeRound).Value);
    }

    // The pop is the shoot-down visual a player round already gets, and it carries no Health: light only, so
    // the round that goes off beside the hull cannot hurt it.
    [Fact]
    public void StunnedRoundPopsOnItsFuseAndHurtsNobody()
    {
        var (em, player, round) = World(stunned: true, detonateIn: 0.05f);
        var view = new WorldView(em);
        var ecm = new EcmSystem();

        bool detonated = false;
        for (int tick = 0; tick < 30 && !detonated; tick++)
        {
            var commands = new CommandBuffer();
            ecm.Update(view, Dt, commands);
            commands.Apply(em);
            detonated = !em.HasComponent<Missile>(round);
        }

        Assert.True(detonated, "a stunned round detonates when its fuse runs out");
        Assert.Single(em.GetEntitiesWithComponents<Explosion>().ToList());
        Assert.Equal(20, em.GetComponent<Health>(player).Current);
    }

    // Two rounds caught by the same pulse pop on different ticks — the chain the player can count, rather
    // than one flash that hides how many rounds the pulse actually took.
    [Fact]
    public void TwoRoundsFromOnePulsePopOnDifferentTicks()
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 20, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(20, 20));

        var capsule = em.CreateEntity();
        em.AddComponent(capsule, new Position(new Vector2(30f, 0f)));
        em.AddComponent(capsule, new EcmPulse(Radius: LootStats.EcmCapsuleRadius));

        Entity[] rounds = [AddEnemyRound(em, player, new Vector2(200f, -100f)), AddEnemyRound(em, player, new Vector2(-200f, -100f))];

        var collect = new CommandBuffer();
        new PickupMagnetSystem().Update(new WorldView(em), Dt, collect);
        collect.Apply(em);

        var view = new WorldView(em);
        var ecm = new EcmSystem();
        int[] poppedOn = [-1, -1];

        for (int tick = 0; tick < 200; tick++)
        {
            var commands = new CommandBuffer();
            ecm.Update(view, Dt, commands);
            commands.Apply(em);

            for (int i = 0; i < rounds.Length; i++)
                if (poppedOn[i] < 0 && !em.HasComponent<Missile>(rounds[i])) poppedOn[i] = tick;
        }

        Assert.NotEqual(-1, poppedOn[0]);
        Assert.NotEqual(-1, poppedOn[1]);
        Assert.NotEqual(poppedOn[0], poppedOn[1]);
        Assert.Equal(2, em.GetEntitiesWithComponents<Explosion>().ToList().Count);
    }

    private static Entity AddEnemyRound(EntityManager em, Entity player, Vector2 at)
    {
        var round = em.CreateEntity();
        em.AddComponent(round, new Position(at));
        em.AddComponent(round, new Velocity(new Vector2(0f, 100f)));
        em.AddComponent(round, new Ammo(new Vector2(0f, 100f), Radius: 4f, Lifetime: 10f, IsEnemy: true, Damage: 2));
        em.AddComponent(round, new Missile(player, Speed: 100f, MaxSpeed: 100f, Acceleration: 0f, TurnRate: 1f, Steering: 0f));
        em.AddComponent(round, new Health(4, 4));
        return round;
    }
}
