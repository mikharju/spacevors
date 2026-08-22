using Spacevors.Domain.Components;
using Xunit;

public class UpgradeCountsTest
{
    [Fact]
    public void GetCount_ReturnsZero_WhenAbsent()
    {
        Assert.Equal(0, UpgradeCounts.Empty.GetCount(UpgradeOption.Hp, ""));
    }

    [Fact]
    public void Increment_AddsNewEntry()
    {
        var counts = UpgradeCounts.Empty.Increment(UpgradeOption.Damage, "MachineGun");

        Assert.Equal(1, counts.GetCount(UpgradeOption.Damage, "MachineGun"));
    }

    [Fact]
    public void Increment_ExistingStatIncrements_OthersUnaffected()
    {
        var counts = UpgradeCounts.Empty.Increment(UpgradeOption.Hp, "");
        for (int i = 0; i < 3; i++)
            counts = counts.Increment(UpgradeOption.FireRate, "MachineGun");

        Assert.Equal(1, counts.GetCount(UpgradeOption.Hp, ""));
        Assert.Equal(3, counts.GetCount(UpgradeOption.FireRate, "MachineGun"));
        Assert.Equal(0, counts.GetCount(UpgradeOption.FireRate, "Shotgun"));
    }

    [Fact]
    public void Increment_TracksSameStatPerWeaponSeparately()
    {
        var counts = UpgradeCounts.Empty.Increment(UpgradeOption.Damage, "MachineGun");
        counts = counts.Increment(UpgradeOption.Damage, "MachineGun");
        counts = counts.Increment(UpgradeOption.Damage, "Shotgun");

        Assert.Equal(2, counts.GetCount(UpgradeOption.Damage, "MachineGun"));
        Assert.Equal(1, counts.GetCount(UpgradeOption.Damage, "Shotgun"));
    }

    [Fact]
    public void Increment_DoesNotMutateOriginal()
    {
        var original = UpgradeCounts.Empty.Increment(UpgradeOption.Hp, "");
        var incremented = original.Increment(UpgradeOption.Hp, "");

        Assert.Equal(1, original.GetCount(UpgradeOption.Hp, ""));
        Assert.Equal(2, incremented.GetCount(UpgradeOption.Hp, ""));
    }
}
