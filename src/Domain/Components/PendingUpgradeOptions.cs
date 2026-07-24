namespace Spacevors.Domain.Components;

public enum UpgradeOption { FireRate, ProjectileSpeed, PickupRadius }

public readonly record struct UpgradableOption(string WeaponName, UpgradeOption Stat);

public readonly record struct PendingUpgradeOptions(UpgradableOption OptionA, UpgradableOption OptionB);
