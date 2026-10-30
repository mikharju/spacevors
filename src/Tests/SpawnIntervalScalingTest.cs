using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class SpawnIntervalScalingTest
{
    [Theory]
    [InlineData(0f, 1f)]     // stationary: no change to the base interval
    [InlineData(50f, 1f)]    // below reference speed: factor stays at 1
    [InlineData(200f, 2f)]   // twice reference speed: half the interval
    [InlineData(300f, 3f)]   // capped at MaxSpeedFactor
    [InlineData(1000f, 3f)]  // far above the cap: still capped
    public void SpeedFactor_ScalesWithPlayerSpeed_Capped(float playerSpeed, float expected)
    {
        Assert.Equal(expected, EnemyShipSpawnSystem.SpeedFactor(playerSpeed), precision: 3);
    }

    [Fact]
    public void NextInterval_InWave_IsWithinRangeDividedBySpeedFactor()
    {
        // Within a wave the interval is random in [InWaveMinInterval, InWaveMaxInterval];
        // a player at 300 px/s (factor 3) divides it (plans/WAVES_AND_BREATHERS.md, P3 kept).
        var rng = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            float slow = EnemyShipSpawnSystem.NextInterval(0f, rng);
            Assert.InRange(slow, WaveStats.InWaveMinInterval - Eps, WaveStats.InWaveMaxInterval + Eps);

            float fast = EnemyShipSpawnSystem.NextInterval(300f, rng);
            Assert.InRange(fast, WaveStats.InWaveMinInterval / 3f - Eps, WaveStats.InWaveMaxInterval / 3f + Eps);
        }
    }

    [Fact]
    public void NextInterval_SameSeed_IsDeterministic()
    {
        float a = EnemyShipSpawnSystem.NextInterval(250f, new Random(99));
        float b = EnemyShipSpawnSystem.NextInterval(250f, new Random(99));
        Assert.Equal(a, b);
    }

    private const float Eps = 1e-3f;
}
