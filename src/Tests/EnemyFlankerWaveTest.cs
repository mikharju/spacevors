using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Flanker waves (plans/ENEMY_BEHAVIORS.md follow-up): occasional waves that arrive holding lanes instead of
// joining the chase line — a formation the player can see coming, rather than a steady trickle of lone
// flankers. Only once the fleet has lanes to hold, and never the whole wave.
public class EnemyFlankerWaveTest
{
    // Nothing before 6:00. A wave of flankers in the opening is the whole encounter, not a variation on it.
    [Fact]
    public void NoFlankerWaveBeforeSixMinutes()
    {
        for (int tier = 0; tier < WaveSchedule.FlankerWaveTier; tier++)
            for (int wave = 1; wave <= 24; wave++)
                Assert.False(WaveSchedule.IsFlankerWave(wave, tier),
                    $"wave {wave} at composition tier {tier} ({tier * 60}s) is already a flank wave");
    }

    // "A small chance after 6:00, a bigger one after 8:00", answered as a clock rather than as dice: a quarter
    // of waves, then half of them — and never all of them, because a game made only of flank waves has no
    // swarm left for them to be a departure from.
    [Fact]
    public void FlankerWavesGetMoreFrequentFromEightMinutes()
    {
        int Count(int tier) => Enumerable.Range(1, 12).Count(wave => WaveSchedule.IsFlankerWave(wave, tier));

        Assert.Equal(3, Count(WaveSchedule.FlankerWaveTier));
        Assert.Equal(6, Count(WaveSchedule.FlankerWaveFrequentTier));
        Assert.True(Count(WaveSchedule.FlankerWaveFrequentTier) < 12, "every wave cannot be a flank wave");
    }

    // What the spawner makes of it. The pair is two waves of the same late fleet: wave 10 at 9:20 is a flank
    // wave and hands every other hull to a lane, wave 9 at 8:20 is an ordinary one where only the ladder
    // decides — about a sixth of its hulls.
    [Fact]
    public void AFlankerWaveHandsEveryOtherHullToALane()
    {
        Assert.True(WaveSchedule.IsFlankerWave(10, 9));
        Assert.False(WaveSchedule.IsFlankerWave(9, 8));

        (int spread, int total) flank = SpawnWave(560f);
        Assert.True(flank.total >= 10, $"the flank wave should have spent most of its quota, got {flank.total} hulls");
        Assert.True(flank.spread * 2 >= flank.total, $"a flank wave should be at least half lanes: {flank.spread} of {flank.total}");

        (int spread, int total) ordinary = SpawnWave(500f);
        Assert.True(ordinary.total >= 10, $"the ordinary wave should have spent most of its quota, got {ordinary.total} hulls");
        Assert.True(ordinary.spread * 2 < ordinary.total, $"an ordinary wave must not read as a flank wave: {ordinary.spread} of {ordinary.total}");
    }

    // Runs one wave's spawner without moving the clock: each call spends at most one hull of the quota, and
    // the wave the elapsed time opened stays open the whole time.
    static (int Spread, int Total) SpawnWave(float elapsed)
    {
        var em = new EntityManager();
        var view = new WorldView(em);
        AddPlayer(em);
        em.AddElapsedTime(elapsed);

        var spawner = new EnemyShipSpawnSystem();
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var commands = new CommandBuffer();
            spawner.Update(view, 5f, commands);
            commands.Apply(em);
        }

        int spread = 0, total = 0;
        foreach (var (_, ship, _) in em.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            total++;
            if (ship.Behavior == EnemyBehavior.Spread) spread++;
        }

        return (spread, total);
    }

    static void AddPlayer(EntityManager em)
    {
        Entity player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 400f, SideThrust: 80f, BackThrust: 80f, Boost: 2.5f,
            MaxHealth: 100, Radius: 46f, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 1.33f));
        em.AddComponent(player, new Health(100, 100));
    }
}
