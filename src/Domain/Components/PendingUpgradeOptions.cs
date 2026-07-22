namespace Spacevors.Domain.Components;

public enum UpgradeOption { FireRate, ProjectileSpeed, PickupRadius }

public readonly record struct PendingUpgradeOptions(UpgradeOption OptionA, UpgradeOption OptionB);
