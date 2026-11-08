using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// The volley cadence (plans/ENEMY_BEHAVIORS.md B7): a Burst heavy stops drizzling one shell every second
// and spends the same shells in a half-second wall once every three, with a tell before it. The pair that
// prices it is rounds per second over a long run — burst and ordinary must match, because a burst that
// also *raised* the gun's damage output would be a stat change wearing a behaviour's clothes
// (plans/DIFFICULTY_SCALING.md D5).
public class EnemyVolleyCadenceTest
{
    const float Dt = 1f / 60f;

    // The hull's gun, on the hull, pointed straight at the player: a fixed weapon so the counts below are
    // about cadence alone and not about which hull was rolled.
    private const float FireRate = 1f;
    private const int Seconds = 6;

    private static (EntityManager Em, Entity Hull) GunEm(EnemyBehavior behavior)
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 100, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(100, 100));

        var hull = em.CreateEntity();
        // 300 px "below" the player, facing up the screen: the player sits on the hull's own firing line,
        // so every case below has a target and the only variable left is the cadence.
        EnemyShipFactory.AddComponents(em, hull, new Vector2(0f, 300f), Vector2.Zero, 0f, 0f, EnemyShipType.Default, EnemyArmament.Guns);

        var ship = em.GetComponent<EnemyShip>(hull);
        // A recorded bearing straight up puts this hull's burst cycle 1.5 s into its period, so the
        // wind-up and the window are at fixed, assertable times rather than on the shared first beat.
        em.AddComponent(hull, ship with { Behavior = behavior, HoldBearing = new Vector2(0f, -1f) });
        var turret = em.GetComponent<Turret>(hull);
        em.AddComponent(hull, turret with { Weapon = turret.Weapon with { FireRate = FireRate, PelletCount = 1 } });
        em.AddComponent(hull, new FireCooldown(0f));
        return (em, hull);
    }

    // Rounds this hull has fired, counted by entity id so a round that has already flown off and died
    // still counts as fired.
    private static int RoundsFired(EntityManager em, float seconds)
    {
        var fired = new HashSet<Entity>();
        var system = new TurretFiringSystem();

        for (int tick = 0; tick < seconds / Dt; tick++)
        {
            em.AddElapsedTime(Dt);
            var view = new WorldView(em);
            var commands = new CommandBuffer();
            system.Update(view, Dt, commands);
            commands.Apply(em);

            foreach (var (entity, ammo) in em.GetEntitiesWithComponents<Ammo>())
                if (ammo.IsEnemy) fired.Add(entity);
        }

        return fired.Count;
    }

    [Fact]
    public void ABurstHullFiresNothingWhileItWindsUp()
    {
        var (em, _) = GunEm(EnemyBehavior.Burst);
        // The cycle for a hull with no recorded bearing opens 1.5 s in, so the first second and a bit is
        // all wind-up.
        Assert.Equal(0, RoundsFired(em, 1.2f));
    }

    [Fact]
    public void ABurstHullDeliversItsVolleyOnceTheWindowOpens()
    {
        var (em, _) = GunEm(EnemyBehavior.Burst);
        int inWindow = RoundsFired(em, 2.1f);
        Assert.True(inWindow >= 3, $"a one-round-per-second gun bursting at six times the rate should throw a volley, got {inWindow}");
    }

    [Fact]
    public void ABurstHullSpendsTheSameRoundsPerSecondAsAnOrdinaryOne()
    {
        var (burstEm, _) = GunEm(EnemyBehavior.Burst);
        var (chaseEm, _) = GunEm(EnemyBehavior.Chase);

        int burst = RoundsFired(burstEm, Seconds);
        int ordinary = RoundsFired(chaseEm, Seconds);

        Assert.True(Math.Abs(burst - ordinary) <= 1,
            $"burst fired {burst} rounds in {Seconds}s, an ordinary hull {ordinary}: the allotment must be redistributed, not raised");
    }

    // The tell is what makes the wall of slugs answerable: it lights up over the last stretch of the
    // cycle, before the guns open, and is dark through the idle part.
    [Fact]
    public void TheWindUpGlowChargesBeforeTheGunsOpen()
    {
        var bearing = new Vector2(0f, -1f);
        Assert.Equal(0f, EnemyShipStats.BurstTell(bearing, 0.5f));

        float charging = EnemyShipStats.BurstTell(bearing, 1.2f);
        Assert.True(charging > 0f && charging < 1f, $"the tell should be part-charged just before the window, got {charging}");
    }

    [Fact]
    public void EveryHullThatDoesNotBurstFiresOnItsOwnCooldown()
    {
        var (em, _) = GunEm(EnemyBehavior.CoverTuck);
        int fired = RoundsFired(em, Seconds);
        Assert.True(fired >= Seconds - 1 && fired <= Seconds + 1, $"a hiding hull still shoots normally, got {fired} rounds in {Seconds}s");
    }
}
