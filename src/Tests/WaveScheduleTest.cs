using Spacevors.Domain.Stats;
using Xunit;

namespace Tests;

// The wave clock is pure arithmetic, so the pacing contract is testable without a world.
public class WaveScheduleTest
{
    [Fact]
    public void Opening_IsAFullLull_WaveOneFollows()
    {
        Assert.Equal(new WavePhase(1, InWave: false, MinesPaused: true), WaveSchedule.PhaseFor(0f));
        Assert.Equal(new WavePhase(1, InWave: false, MinesPaused: true), WaveSchedule.PhaseFor(WaveStats.OpeningLull - 0.01f));
        Assert.Equal(new WavePhase(1, InWave: true, MinesPaused: false), WaveSchedule.PhaseFor(WaveStats.OpeningLull));
    }

    [Fact]
    public void OnePair_WaveQuietGapWaveFullLullInOrder()
    {
        float t0 = WaveStats.OpeningLull;

        // Wave 1: [20, 65). Quiet gap: [65, 74) — ships off, mines still on.
        Assert.Equal(new WavePhase(1, true, false), WaveSchedule.PhaseFor(t0 + 20f));
        Assert.Equal(new WavePhase(2, false, false), WaveSchedule.PhaseFor(t0 + WaveStats.WaveDuration));
        Assert.Equal(new WavePhase(2, false, false), WaveSchedule.PhaseFor(t0 + WaveStats.WaveDuration + WaveStats.QuietGap - 0.01f));

        // Wave 2: [74, 119). Full lull: [119, 134) — everything off.
        Assert.Equal(new WavePhase(2, true, false), WaveSchedule.PhaseFor(t0 + WaveStats.WaveDuration + WaveStats.QuietGap));
        Assert.Equal(new WavePhase(3, false, true), WaveSchedule.PhaseFor(t0 + 2 * WaveStats.WaveDuration + WaveStats.QuietGap));
        Assert.Equal(new WavePhase(3, false, true), WaveSchedule.PhaseFor(t0 + WaveSchedule.PairDuration - 0.01f));

        // The pattern repeats: wave 3 opens exactly one pair after wave 1.
        Assert.Equal(new WavePhase(3, true, false), WaveSchedule.PhaseFor(t0 + WaveSchedule.PairDuration));
    }

    [Fact]
    public void WaveNumberIncreasesMonotonically_AndNeverSkips()
    {
        int previous = WaveSchedule.PhaseFor(0f).WaveIndex;
        for (float t = 0f; t <= 600f; t += 0.5f)
        {
            int current = WaveSchedule.PhaseFor(t).WaveIndex;
            Assert.True(current == previous || current == previous + 1,
                $"wave index jumped {previous} -> {current} at t={t}");
            previous = current;
        }
    }

    [Fact]
    public void WaveSize_GrowsPerWave_AndCaps()
    {
        Assert.Equal(6, WaveSchedule.WaveSize(1));
        Assert.Equal(8, WaveSchedule.WaveSize(2));
        Assert.Equal(WaveStats.WaveMaxSize, WaveSchedule.WaveSize(18));
        Assert.Equal(WaveStats.WaveMaxSize, WaveSchedule.WaveSize(1000));
    }

    [Fact]
    public void PhaseFor_IsPure_SameInputSameOutput()
    {
        for (float t = 0f; t < 300f; t += 1.7f)
            Assert.Equal(WaveSchedule.PhaseFor(t), WaveSchedule.PhaseFor(t));
    }
}
