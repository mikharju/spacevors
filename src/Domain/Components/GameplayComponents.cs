using Spacevors.Domain.Stats; // UpgradeOption

namespace Spacevors.Domain.Components;

public readonly record struct Health(int Current, int Max);

public readonly record struct PendingChoice;

public readonly record struct UpgradableOption(string WeaponName, UpgradeOption Stat, bool IsNewWeapon = false);

public readonly record struct PendingUpgradeOptions(UpgradableOption[] Options);

// How many times each (stat, weapon) pair has been upgraded; ship-level stats use an empty weapon name.
public readonly record struct UpgradeCount(UpgradeOption Stat, string WeaponName, int Count);

public readonly record struct UpgradeCounts(IReadOnlyList<UpgradeCount> Counts)
{
    public static UpgradeCounts Empty { get; } = new(Array.Empty<UpgradeCount>());

    public int GetCount(UpgradeOption stat, string weaponName) =>
        Counts.FirstOrDefault(c => c.Stat == stat && c.WeaponName == weaponName).Count;

    public UpgradeCounts Increment(UpgradeOption stat, string weaponName)
    {
        var updated = new List<UpgradeCount>(Counts.Count + 1);
        bool found = false;
        foreach (var count in Counts)
        {
            if (count.Stat == stat && count.WeaponName == weaponName)
            {
                updated.Add(count with { Count = count.Count + 1 });
                found = true;
            }
            else updated.Add(count);
        }
        if (!found) updated.Add(new UpgradeCount(stat, weaponName, 1));
        return new(updated);
    }
}
