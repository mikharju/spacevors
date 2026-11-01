using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// The mine respawn ramp (plans/DIFFICULTY_SCALING.md R2.1, plans/WAVES_AND_BREATHERS.md): mines are the
// one content the difficulty ladder makes *weaker* — the respawn window widens from the start interval at
// the opening to the late interval once RampDuration has passed, and never narrows on the way. The
// direction is the whole rule: interpolated backwards, the field refills slowest through the opening (an
// unearned second grace period on top of the wave clock's) and hits its fastest respawn from
// RampDuration on — more XP and more heal orbs per second exactly where every other knob has stopped
// raising pressure.
public class MineRespawnIntervalTest
{
    private const float Eps = 1e-3f;

    // The window the system-level check below counts respawns in. Mines are gated by the wave clock, so
    // the two windows are exactly one WaveSchedule.PairDuration apart: same wave/gap/lull shape, and the
    // ramp is the only thing that differs.
    private const int WindowSeconds = 60;

    [Theory]
    [InlineData(-1f, SpawningStats.Mines.StartMinInterval, SpawningStats.Mines.StartMaxInterval)]
    [InlineData(0f, SpawningStats.Mines.StartMinInterval, SpawningStats.Mines.StartMaxInterval)]
    [InlineData(SpawningStats.Mines.RampDuration / 2f,
        (SpawningStats.Mines.StartMinInterval + SpawningStats.Mines.LateMinInterval) / 2f,
        (SpawningStats.Mines.StartMaxInterval + SpawningStats.Mines.LateMaxInterval) / 2f)]
    [InlineData(SpawningStats.Mines.RampDuration, SpawningStats.Mines.LateMinInterval, SpawningStats.Mines.LateMaxInterval)]
    [InlineData(SpawningStats.Mines.RampDuration * 3f, SpawningStats.Mines.LateMinInterval, SpawningStats.Mines.LateMaxInterval)]
    public void IntervalRange_RampsFromStartToLate_AndClampsAtBothEnds(float elapsed, float expectedMin, float expectedMax)
    {
        var (min, max) = MineRespawnSystem.IntervalRange(elapsed);

        Assert.Equal(expectedMin, min, precision: 3);
        Assert.Equal(expectedMax, max, precision: 3);
    }

    [Fact]
    public void IntervalRange_NeverNarrowsAsTheRunGoesOn()
    {
        // The rule as one property: a longer run always waits at least as long between respawns, so mine
        // density can only fall. A flipped interpolation fails here at once.
        var (previousMin, previousMax) = MineRespawnSystem.IntervalRange(0f);

        for (float elapsed = 0f; elapsed <= 4f * SpawningStats.Mines.RampDuration; elapsed += 1f)
        {
            var (min, max) = MineRespawnSystem.IntervalRange(elapsed);

            Assert.True(min >= previousMin - Eps && max >= previousMax - Eps,
                $"the respawn window narrowed at t={elapsed:F0} s: [{min:F2}, {max:F2}] after [{previousMin:F2}, {previousMax:F2}]");
            Assert.True(min <= max + Eps, $"the respawn window crossed over at t={elapsed:F0} s: [{min:F2}, {max:F2}]");

            (previousMin, previousMax) = (min, max);
        }
    }

    [Fact]
    public void LateWindowIsSlowerThanTheOpeningWindow()
    {
        // The direction, asserted as data rather than as a formula: even the *fastest* late respawn waits
        // longer than the *slowest* opening one, so the ramp cannot be honoured with the constants swapped.
        var (openingMin, openingMax) = MineRespawnSystem.IntervalRange(0f);
        var (lateMin, _) = MineRespawnSystem.IntervalRange(SpawningStats.Mines.RampDuration);

        Assert.True(lateMin > openingMax,
            $"the late interval starts at {lateMin:F1} s, not slower than the opening interval's {openingMax:F1} s");
    }

    [Theory]
    [InlineData(0f, SpawningStats.Mines.StartMinInterval, SpawningStats.Mines.StartMaxInterval)]
    [InlineData(SpawningStats.Mines.RampDuration, SpawningStats.Mines.LateMinInterval, SpawningStats.Mines.LateMaxInterval)]
    public void NextInterval_FallsInsideTheWindowForThatTime(float elapsed, float min, float max)
    {
        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
            Assert.InRange(MineRespawnSystem.NextInterval(elapsed, rng), min - Eps, max + Eps);
    }

    [Fact]
    public void NextInterval_SameSeed_SameInterval()
    {
        float a = MineRespawnSystem.NextInterval(75f, new Random(99));
        float b = MineRespawnSystem.NextInterval(75f, new Random(99));

        Assert.Equal(a, b);
    }

    [Fact]
    public void RespawnRate_FallsFromTheOpeningToTheSameWindowOnePairLater()
    {
        // The observable consequence, through the system rather than the pure function: the same 60 s
        // window one wave pair later respawns fewer mines. The windows are exactly `PairDuration` apart so
        // the wave clock is in the same shape in both — this measures the ramp, not the lull pattern.
        int opening = CountRespawns(WaveStats.OpeningLull);
        int onePairLater = CountRespawns(WaveStats.OpeningLull + WaveSchedule.PairDuration);

        Assert.True(opening > onePairLater,
            $"mine respawns did not slow down: {opening} in the opening window, {onePairLater} one pair later");
    }

    // How many mines respawn in a WindowSeconds window starting at `windowStart`, from an empty field.
    // Deterministic: EntityManager's seeded rng (DefaultRngSeed) and a fixed 1 s step.
    private static int CountRespawns(float windowStart)
    {
        var em = new EntityManager();
        var view = new WorldView(em);
        AddStationaryPlayer(em);
        em.AddElapsedTime(windowStart);

        var spawner = new MineRespawnSystem();
        const float deltaTime = 1f;

        for (int i = 0; i < WindowSeconds; i++)
        {
            var commands = new CommandBuffer();
            spawner.Update(view, deltaTime, commands);
            commands.Apply(em);
            em.AddElapsedTime(deltaTime);
        }

        return em.GetEntitiesWithComponents<EnemyMine>().Count();
    }

    // Stationary on purpose: a moving player would make the spawn direction draw depend on the velocity the
    // two windows happen to be in. Mines spawn in any direction, which is the same draw either way.
    private static void AddStationaryPlayer(EntityManager em)
    {
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));
    }
}
