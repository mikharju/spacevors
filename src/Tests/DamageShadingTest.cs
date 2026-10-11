using Spacevors.Domain.Components;
using Spacevors.Game;
using Xunit;

namespace Tests;

public class DamageShadingTest
{
    [Fact]
    public void FullAndLightHp_HaveNoHullDamage()
    {
        Assert.Equal(0f, DamageShading.FromHpRatio(1f));
        Assert.Equal(0f, DamageShading.FromHpRatio(DamageShading.LightHpThreshold));
    }

    [Fact]
    public void DamageRisesContinuouslyBelowSmokeThreshold()
    {
        float justBelow = DamageShading.LightHpThreshold - 0.01f;
        Assert.True(DamageShading.FromHpRatio(justBelow) > 0f);
        Assert.Equal(0.5f, DamageShading.FromHpRatio(1f / 3f), 3);
        Assert.Equal(1f, DamageShading.FromHpRatio(0f));

        Assert.True(DamageShading.FromHpRatio(0.5f) < DamageShading.FromHpRatio(0.25f));
    }

    [Fact]
    public void OutOfRangeRatiosAndHealth_AreClamped()
    {
        Assert.Equal(1f, DamageShading.FromHpRatio(-0.5f));
        Assert.Equal(0f, DamageShading.FromHpRatio(1.5f));
        Assert.Equal(0f, DamageShading.FromHealth(new Health(5, 0)));
    }

    [Fact]
    public void FromHealth_MatchesRatioMapping()
    {
        Assert.Equal(DamageShading.FromHpRatio(0.25f), DamageShading.FromHealth(new Health(1, 4)), 5);
    }
}
