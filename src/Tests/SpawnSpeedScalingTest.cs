using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Player speed buys ships per wave, not a shorter gap between them: a fast player covers more ground per
// wave and has to meet more hulls for it (P3), while the in-wave rhythm stays speed-independent so a wave
// keeps reading as a wave instead of a burst followed by dead air (plans/WAVES_AND_BREATHERS.md).
public class SpawnSpeedScalingTest
{
    private const float Eps = 1e-3f;

    [Theory]
    [InlineData(0f, 1f)]     // stationary: the plain quota
    [InlineData(50f, 1f)]    // below the reference speed: the factor stays at 1
    [InlineData(200f, 2f)]   // twice reference speed: twice the ships
    [InlineData(300f, 3f)]   // capped at MaxSpeedFactor
    [InlineData(1000f, 3f)]  // far above the cap: still capped
    public void SpeedFactor_ScalesWithPlayerSpeed_Capped(float playerSpeed, float expected)
    {
        Assert.Equal(expected, EnemyShipSpawnSystem.SpeedFactor(playerSpeed), precision: 3);
    }

    [Theory]
    [InlineData(1, 0f, 6)]      // wave 1, stationary: exactly WaveSchedule.WaveSize
    [InlineData(1, 100f, 6)]    // at the reference speed: still the plain quota (boundary: factor 1)
    [InlineData(1, 200f, 12)]   // twice reference speed: twice the ships
    [InlineData(3, 150f, 15)]   // a half-step factor rounds (wave 3 is 10 ships)
    [InlineData(18, 300f, 120)] // a capped 40-ship wave at the factor cap; MaxEnemyShips bounds the field
    [InlineData(1000, 300f, 120)] // past the wave-size cap the quota still tracks the size, not the index
    public void WaveQuota_GrowsWithPlayerSpeed(int waveIndex, float playerSpeed, int expected)
    {
        Assert.Equal(expected, EnemyShipSpawnSystem.WaveQuota(waveIndex, playerSpeed));
    }

    [Fact]
    public void WaveQuota_NeverShrinksBelowTheWaveSize()
    {
        for (int wave = 1; wave <= 30; wave++)
        {
            Assert.True(EnemyShipSpawnSystem.WaveQuota(wave, 0f) >= WaveSchedule.WaveSize(wave),
                $"wave {wave} spawns fewer ships when standing still than the schedule promises");
        }
    }

    [Fact]
    public void InWaveInterval_StaysInsideTheWindow_AtAnySpeed()
    {
        // The interval has no speed term any more: every draw lands in [InWaveMinInterval, InWaveMaxInterval].
        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            float interval = EnemyShipSpawnSystem.NextInterval(rng);
            Assert.InRange(interval, WaveStats.InWaveMinInterval - Eps, WaveStats.InWaveMaxInterval + Eps);
        }
    }

    [Fact]
    public void NextInterval_SameSeed_IsDeterministic()
    {
        float a = EnemyShipSpawnSystem.NextInterval(new Random(99));
        float b = EnemyShipSpawnSystem.NextInterval(new Random(99));
        Assert.Equal(a, b);
    }

    [Fact]
    public void WaveSpendsItsQuota_MoreShipsArriveWhenThePlayerIsFast()
    {
        // Through the system, not just the pure function: a wave has to actually spend the quota its speed
        // factor bought, inside one wave window. The world is integrated here, because a wave that only
        // exists on paper (ships parked where they were placed while the player stands still) measures the
        // placement rule, not the quota.
        int stationary = CountWaveShips(0f);
        int fast = CountWaveShips(300f);

        Assert.True(fast > stationary, $"only {fast} ships at 300 px/s, {stationary} standing still");
        Assert.Equal(EnemyShipSpawnSystem.WaveQuota(1, 0f), stationary);
        Assert.Equal(EnemyShipSpawnSystem.WaveQuota(1, 300f), fast);
    }

    // How many ships wave 1 gets through its window at a fixed player speed. Deterministic: seeded
    // EntityManager, fixed 0.25 s step, the player's velocity component never changes, and positions
    // integrate so the spawn ring travels with the player the way it does in a real run.
    private static int CountWaveShips(float playerSpeed)
    {
        var em = new EntityManager();
        var view = new WorldView(em);
        AddPlayer(em, playerSpeed);
        em.AddElapsedTime(WaveStats.OpeningLull); // wave 1 opens once the opening lull ends

        var spawner = new EnemyShipSpawnSystem();
        var integrate = new PositionIntegrationSystem();
        const float deltaTime = 0.25f;
        int steps = (int)((WaveStats.WaveDuration + WaveStats.QuietGap) / deltaTime);

        for (int i = 0; i < steps; i++)
        {
            var commands = new CommandBuffer();
            spawner.Update(view, deltaTime, commands);
            integrate.Update(view, deltaTime, commands);
            commands.Apply(em);
            em.AddElapsedTime(deltaTime);
        }

        return em.GetEntitiesWithComponents<EnemyShip>().Count();
    }

    private static void AddPlayer(EntityManager em, float speed)
    {
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(new Vector2(speed, 0f)));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));
    }
}
