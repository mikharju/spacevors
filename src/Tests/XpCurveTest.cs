using Spacevors.Domain.Stats;
using Xunit;

namespace Tests;

// Player.Xp is the run's total and is never reset, so the curve that matters is what each level costs
// on top of the one before it. The step itself is a tuning knob (it has been halved to speed leveling
// up), so this test pins the **shape** — every level costs more than the one before, triangularly — and
// reads the scale off the curve instead of hard-coding a number that goes stale with the knob.
public class XpCurveTest
{
    // What leaving `level` costs: its threshold minus the one before it.
    static int Cost(int level) => UpgradeStats.XpForLevel(level) - UpgradeStats.XpForLevel(level - 1);

    [Fact]
    public void ALevelCostsSomething()
        => Assert.True(Cost(1) > 0, "a level that costs nothing hands out upgrades for free");

    [Fact]
    public void ThresholdsOnlyEverRise()
    {
        for (int level = 1; level <= 30; level++)
            Assert.True(UpgradeStats.XpForLevel(level) > UpgradeStats.XpForLevel(level - 1),
                $"level {level}'s threshold is not past level {level - 1}'s");
    }

    // The whole point of the curve, whatever the step is: a later level is never cheaper than an
    // earlier one. A flat step would make levels free by comparison and stop leveling slowing down as
    // the run gets stronger.
    [Fact]
    public void EachLevelCostsMoreXpThanTheOneBefore()
    {
        for (int level = 2; level <= 30; level++)
            Assert.True(Cost(level) > Cost(level - 1),
                $"leaving level {level} costs {Cost(level)} XP, not more than level {level - 1}'s {Cost(level - 1)}");
    }

    // Triangular in whatever the step is: level 1's cost *is* the step, and level n's total is that
    // step times the triangle sum n(n+1)/2. True for any step, so re-tuning the knob needs no edit here.
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void CumulativeThresholdIsTriangularInWhateverTheStepIs(int level)
        => Assert.Equal(Cost(1) * level * (level + 1) / 2, UpgradeStats.XpForLevel(level));

    [Fact]
    public void ProgressMeasuresTheCurrentLevelNotTheRunTotal()
    {
        // Right after reaching level 2 the bar is empty again, though the total XP it took to get
        // there is still on the player.
        Assert.Equal(0f, UpgradeStats.LevelProgress(level: 2, xp: UpgradeStats.XpForLevel(1)));
        Assert.Equal(1f, UpgradeStats.LevelProgress(level: 2, xp: UpgradeStats.XpForLevel(2)));

        // Half of the level's own cost is half the bar, whatever that cost is.
        Assert.Equal(0.5f, UpgradeStats.LevelProgress(level: 2, xp: UpgradeStats.XpForLevel(1) + Cost(2) / 2));

        // XP collected in the frame that levels the player up does not fill the next level's bar.
        Assert.Equal(1f / Cost(2), UpgradeStats.LevelProgress(level: 2, xp: UpgradeStats.XpForLevel(1) + 1));
        Assert.Equal(1f, UpgradeStats.LevelProgress(level: 1, xp: 999));
    }
}
