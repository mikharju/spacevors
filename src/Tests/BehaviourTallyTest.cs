using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// What the `[BEHAVIOUR]` rows say (plans/ENEMY_BEHAVIORS.md follow-up). The armament ladder is arithmetic —
// it says what share of spawns is dealt a manœuvre — so it is worth nothing unless the timing behind it is
// honest: these tests are that timing.
public class BehaviourTallyTest
{
    private static readonly int Spread = (int)EnemyBehavior.Spread;
    private static readonly int Chase = (int)EnemyBehavior.Chase;
    private static readonly int Rammer = (int)EnemyBehavior.Rammer;

    [Fact]
    public void HullSecondsAccumulatePerBehaviour()
    {
        var (em, view) = World();
        AddHull(em, new Vector2(0f, -300f), EnemyBehavior.Spread);
        AddHull(em, new Vector2(300f, 0f), EnemyBehavior.Spread);
        AddHull(em, new Vector2(-300f, 0f), EnemyBehavior.Chase);

        var tally = new BehaviourTally();
        tally.Sample(view);          // the first sample only establishes the clock
        em.AddElapsedTime(4f);
        tally.Sample(view);

        Assert.Equal(8f, tally.AliveSeconds[Spread], precision: 3);   // two hulls, four seconds each
        Assert.Equal(4f, tally.AliveSeconds[Chase], precision: 3);
        Assert.Equal(0f, tally.AliveSeconds[Rammer], precision: 3);   // a behaviour that never happened
        Assert.Equal(2f, tally.PeakLive[Spread], precision: 3);
        Assert.Equal(3, tally.Seen[Spread] + tally.Seen[Chase]);
    }

    // A behaviour the player never saw is not a behaviour they met: a flanker still crossing the field is
    // alive, but only the on-screen seconds say whether the fleet ever showed anything. The box is the
    // viewport centred on the player — the same one spawning happens outside of.
    [Fact]
    public void AFlankerStillCrossingTheFieldIsAliveButNotOnScreen()
    {
        var (em, view) = World();
        AddHull(em, new Vector2(0f, -3000f), EnemyBehavior.Spread);
        AddHull(em, new Vector2(0f, -400f), EnemyBehavior.Spread);

        var tally = new BehaviourTally();
        tally.Sample(view);
        em.AddElapsedTime(2f);
        tally.Sample(view);

        Assert.Equal(4f, tally.AliveSeconds[Spread], precision: 3);
        Assert.Equal(2f, tally.OnScreenSeconds[Spread], precision: 3);
    }

    [Fact]
    public void AWreckIsNotManoeuvring()
    {
        var (em, view) = World();
        Entity wreck = AddHull(em, new Vector2(0f, -300f), EnemyBehavior.Rammer);
        em.AddComponent(wreck, new Dead());

        var tally = new BehaviourTally();
        tally.Sample(view);
        em.AddElapsedTime(3f);
        tally.Sample(view);

        Assert.Equal(0f, tally.AliveSeconds[Rammer], precision: 3);
        Assert.Equal(0, tally.Seen[Rammer]);
    }

    // One hull, however long it lives, is one hull — otherwise a single long-lived heavy reads as a stream
    // of them and the "how many happened" column means nothing. A hull that arrives later is counted again.
    [Fact]
    public void EachHullCountsOnceHoweverLongItLives()
    {
        var (em, view) = World();
        Entity hull = AddHull(em, new Vector2(0f, -300f), EnemyBehavior.CutOff);

        var tally = new BehaviourTally();
        for (int minute = 0; minute < 5; minute++)
        {
            em.AddElapsedTime(60f);
            tally.Sample(view);
        }

        Assert.Equal(1, tally.Seen[(int)EnemyBehavior.CutOff]);
        Assert.Equal(240f, tally.AliveSeconds[(int)EnemyBehavior.CutOff], precision: 1);

        em.DestroyEntity(hull);
        AddHull(em, new Vector2(200f, -200f), EnemyBehavior.Strafe);
        em.AddElapsedTime(1f);
        tally.Sample(view);

        Assert.Equal(1, tally.Seen[(int)EnemyBehavior.Strafe]);
        Assert.Equal(1, tally.Seen[(int)EnemyBehavior.CutOff]);
    }

    static (EntityManager, WorldView) World()
    {
        var em = new EntityManager();
        Entity player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 400f, SideThrust: 80f, BackThrust: 80f, Boost: 2.5f,
            MaxHealth: 100, Radius: 46f, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 1.33f));
        em.AddComponent(player, new Health(100, 100));
        return (em, new WorldView(em));
    }

    static Entity AddHull(EntityManager em, Vector2 position, EnemyBehavior behavior)
    {
        Entity hull = em.CreateEntity();
        em.AddComponent(hull, new Position(position));
        em.AddComponent(hull, new Velocity(Vector2.Zero));
        em.AddComponent(hull, new Rotation(0f));
        em.AddComponent(hull, new EnemyShip(Radius: 20f, Speed: 90f, TurnRate: 1f, FiringRange: 700f,
            TurretFireRate: 1f, TurretAmmoSpeed: 300f, Acceleration: 45f, GraphicsId: 0, Behavior: behavior));
        return hull;
    }
}
