using Spacevors.Domain.Stats;
using Xunit;

namespace Tests;

// Player.Xp is the run's total and is never reset, so the curve that matters is what each level costs
// on top of the one before it: 10 XP to leave level 1, then 20 more, then 30 more.
public class XpCurveTest
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(4, 100)]
    [InlineData(10, 550)]
    public void CumulativeThresholdGrowsTriangularly(int level, int expectedTotalXp)
        => Assert.Equal(expectedTotalXp, UpgradeStats.XpForLevel(level));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void EachLevelCostsTenXpMoreThanTheOneBefore(int level)
        => Assert.Equal(level * 10, UpgradeStats.XpForLevel(level) - UpgradeStats.XpForLevel(level - 1));

    [Fact]
    public void ProgressMeasuresTheCurrentLevelNotTheRunTotal()
    {
        // Right after reaching level 2 the bar is empty again, though the total XP it took to get
        // there is still on the player.
        Assert.Equal(0f, UpgradeStats.LevelProgress(level: 2, xp: UpgradeStats.XpForLevel(1)));
        Assert.Equal(0.5f, UpgradeStats.LevelProgress(level: 2, xp: 20));
        Assert.Equal(1f, UpgradeStats.LevelProgress(level: 2, xp: UpgradeStats.XpForLevel(2)));

        // XP collected in the frame that levels the player up does not fill the next level's bar.
        Assert.Equal(0.05f, UpgradeStats.LevelProgress(level: 2, xp: UpgradeStats.XpForLevel(1) + 1));
        Assert.Equal(1f, UpgradeStats.LevelProgress(level: 1, xp: 999));
    }
}
