namespace Spacevors.Domain.Stats;

// Wave pacing (plans/WAVES_AND_BREATHERS.md): enemy ships arrive in discrete waves separated by
// two alternating kinds of lull. Pure tuning; the arithmetic lives in WaveSchedule.
public static class WaveStats
{
    // Opening grace period — the first wave starts at this time. Replaces the old ship and mine
    // InitialDelay constants.
    public const float OpeningLull = 20f;

    // The window during which one wave's quota spawns. Leftover quota is lost: the cap and the
    // within-wave interval bound how fast it can spend itself anyway.
    public const float WaveDuration = 45f;

    // Quiet gap: no new ships, mine respawns continue — less pressure.
    // Full lull: nothing spawns — no pressure, the loot-collection window.
    // The opening lull is a full lull; even-numbered waves end in one.
    public const float QuietGap = 9f;
    public const float FullLull = 15f;

    // Ships a wave brings: WaveStartSize + WaveGrowth per wave past the first, capped.
    public const int WaveStartSize = 6;
    public const int WaveGrowth = 2;
    public const int WaveMaxSize = 40;

    // Random interval between spawns within a wave, divided by SpeedFactor(playerSpeed) (P3).
    public const float InWaveMinInterval = 0.6f;
    public const float InWaveMaxInterval = 1.5f;
}
