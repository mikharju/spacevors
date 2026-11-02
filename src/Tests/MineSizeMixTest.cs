using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Mines get scarcer over a run (SpawningStats.Mines) and what is left of them gets bigger and, since R32,
// carries more hits: the large share and the spawned hp both drift with the tier, and neither of them ever
// makes a large mine harder to kill than a small one (plans/DIFFICULTY_SCALING.md R2.6 item 6).
public class MineSizeMixTest
{
    [Theory]
    [InlineData(0, MineStats.LargeShareStart)]
    [InlineData(5, 0.6f)]
    [InlineData(EnemyShipStats.MaxTier, MineStats.LargeShareAtTierCap)]
    [InlineData(EnemyShipStats.MaxTier + 7, MineStats.LargeShareAtTierCap)] // past the cap: stays at it
    public void LargeChance_RampsFromTheEvenOpeningToTheTierCap(int tier, float expected)
    {
        Assert.Equal(expected, MineStats.LargeChanceFor(tier), precision: 3);
    }

    [Fact]
    public void LargeChance_OnlyGrowsAndNeverLeavesTheBand()
    {
        float previous = MineStats.LargeChanceFor(0);
        for (int tier = 0; tier <= EnemyShipStats.MaxTier; tier++)
        {
            float chance = MineStats.LargeChanceFor(tier);
            Assert.InRange(chance, MineStats.LargeShareStart, MineStats.LargeShareAtTierCap);
            Assert.True(chance >= previous, $"the large share shrank at tier {tier}");
            previous = chance;
        }
    }

    [Fact]
    public void BothMineSizesShareOneFixedHealth()
    {
        // The mix may drift and the tier may add hp; the two sizes never differ. A large mine is bigger and
        // worth more xp, never harder to kill than a small one of its own tier.
        Assert.Equal(MineType.Small.Health, MineType.Large.Health);
    }

    [Fact]
    public void LateFieldHasAtLeastAsManyLargeMinesAsTheOpeningField()
    {
        // Through the system, not just the pure function: the respawner has to consult the tier. Both runs
        // draw from the same seeded sequence and consume the same number of draws per spawn, so a call
        // site that ignored the tier would produce equal counts and fail here.
        int openingLarge = CountLargeMines(WaveStats.OpeningLull);
        int lateLarge = CountLargeMines(SpawningStats.Mines.RampDuration + WaveStats.WaveDuration);

        Assert.True(lateLarge > openingLarge,
            $"the large share did not rise: {openingLarge} large mines at the opening, {lateLarge} late");
    }

    // R32: a mine's hp follows the difficulty tier it spawns at — 8 at the opening, two support-chip hits as
    // before, and 28 at the tier cap. Both sizes carry it, so this is the leftover field getting weightier,
    // not the large mine getting an unfair deal.
    [Theory]
    [InlineData(0, MineStats.HealthBase)]
    [InlineData(5, MineStats.HealthBase + 10)]
    [InlineData(EnemyShipStats.MaxTier, 28)]
    public void MineHp_RampsWithTheDifficultyTier(int tier, int expectedHp)
    {
        Assert.Equal(expectedHp, MineStats.HealthAtTier(tier));
    }

    [Fact]
    public void LateMinesSpawnTougherThanOpeningMines()
    {
        // Through the respawner, not just the pure function: a call site that ignored the tier would spawn
        // both fields at the base hp and fail here.
        Assert.True(SpawnedMineHp(SpawningStats.Mines.RampDuration + WaveStats.WaveDuration)
            > SpawnedMineHp(WaveStats.OpeningLull), "the respawner never consulted the tier for hp");
    }

    static int SpawnedMineHp(float elapsed)
    {
        var em = new EntityManager();
        var view = new WorldView(em);
        AddStationaryPlayer(em);
        em.AddElapsedTime(elapsed);

        var commands = new CommandBuffer();
        new MineRespawnSystem().Update(view, 100f, commands);
        commands.Apply(em);

        Assert.True(view.GetEntitiesWithComponents<Health>().TryFirst(out var mine));
        return view.GetComponent<Health>(mine.Entity).Max;
    }

    // How many of 200 respawns come out large at a fixed elapsed time. One spawn per call: a long tick
    // always expires the respawn timer, and the field is emptied after each count so the 23 cap never
    // blocks. Deterministic: seeded EntityManager, fixed dt, the same draw count per spawn.
    private static int CountLargeMines(float elapsed)
    {
        var em = new EntityManager();
        var view = new WorldView(em);
        AddStationaryPlayer(em);
        em.AddElapsedTime(elapsed);

        var spawner = new MineRespawnSystem();
        int large = 0;

        for (int i = 0; i < 200; i++)
        {
            var commands = new CommandBuffer();
            spawner.Update(view, 100f, commands);
            commands.Apply(em);

            foreach (var (mineEntity, mine, _) in view.GetEntitiesWithComponents<EnemyMine, Position>().ToList())
            {
                if (mine.Size == MineSize.Large) large++;
                em.DestroyEntity(mineEntity);
            }
        }

        return large;
    }

    // Stationary on purpose: the spawn direction then comes from one AnyDirection draw in both runs, so the
    // two comparisons see the same seeded draws and differ only by the tier's threshold.
    private static void AddStationaryPlayer(EntityManager em)
    {
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));
    }
}
