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
    public void NextInterval_EarlyGame_IsWithinRampRangeDividedBySpeedFactor()
    {
        // At t=0 the ramp interval is random in [5, 10]; a player at 300 px/s (factor 3) divides it.
        var rng = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            float slow = EnemyShipSpawnSystem.NextInterval(0f, 0f, rng);
            Assert.InRange(slow, 5f - Eps, 10f + Eps);

            float fast = EnemyShipSpawnSystem.NextInterval(0f, 300f, rng);
            Assert.InRange(fast, 5f / 3f - Eps, 10f / 3f + Eps);
        }
    }

    [Fact]
    public void NextInterval_LateGame_IsWithinPlateauRangeDividedBySpeedFactor()
    {
        // Past the 180 s ramp the interval is random in [2, 4].
        var rng = new Random(7);
        for (int i = 0; i < 100; i++)
        {
            float slow = EnemyShipSpawnSystem.NextInterval(300f, 0f, rng);
            Assert.InRange(slow, 2f - Eps, 4f + Eps);

            float fast = EnemyShipSpawnSystem.NextInterval(300f, 600f, rng);
            Assert.InRange(fast, 2f / 3f - Eps, 4f / 3f + Eps);
        }
    }

    [Fact]
    public void NextInterval_SameSeed_IsDeterministic()
    {
        float a = EnemyShipSpawnSystem.NextInterval(120f, 250f, new Random(99));
        float b = EnemyShipSpawnSystem.NextInterval(120f, 250f, new Random(99));
        Assert.Equal(a, b);
    }

    private const float Eps = 1e-3f;
}
