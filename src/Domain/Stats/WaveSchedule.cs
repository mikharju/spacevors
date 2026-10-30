namespace Spacevors.Domain.Stats;

// Where the wave clock says we are: which wave (1-based; during a lull it is the *next* wave),
// whether ships spawn now, and whether mine respawning is paused.
public readonly record struct WavePhase(int WaveIndex, bool InWave, bool MinesPaused);

// Pure schedule: elapsed time in, phase out. No entity state, no RNG draw for timing — the same
// clock every system and test can query, like EnemyShipStats.TierFor (plans/WAVES_AND_BREATHERS.md).
public static class WaveSchedule
{
    // The repeating two-wave pattern: wave, quiet gap, wave, full lull.
    public const float PairDuration =
        2 * WaveStats.WaveDuration + WaveStats.QuietGap + WaveStats.FullLull;

    public static WavePhase PhaseFor(float elapsedTime)
    {
        float t = elapsedTime - WaveStats.OpeningLull;
        if (t < 0f) return new WavePhase(1, InWave: false, MinesPaused: true);

        int pair = (int)(t / PairDuration);
        float p = t - pair * PairDuration;

        if (p < WaveStats.WaveDuration)
            return new WavePhase(2 * pair + 1, InWave: true, MinesPaused: false);
        p -= WaveStats.WaveDuration;

        if (p < WaveStats.QuietGap)
            return new WavePhase(2 * pair + 2, InWave: false, MinesPaused: false);
        p -= WaveStats.QuietGap;

        if (p < WaveStats.WaveDuration)
            return new WavePhase(2 * pair + 2, InWave: true, MinesPaused: false);
        return new WavePhase(2 * pair + 3, InWave: false, MinesPaused: true);
    }

    public static int WaveSize(int waveIndex) =>
        Math.Min(WaveStats.WaveStartSize + WaveStats.WaveGrowth * (waveIndex - 1), WaveStats.WaveMaxSize);
}
