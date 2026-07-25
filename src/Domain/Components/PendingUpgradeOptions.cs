namespace Spacevors.Domain.Components;

public enum UpgradeOption { FireRate, ProjectileSpeed, PickupRadius, AutoTargetRange, ShotLifetime, Damage }

public readonly record struct UpgradableOption(string WeaponName, UpgradeOption Stat, bool IsNewWeapon = false);

public readonly record struct PendingUpgradeOptions(UpgradableOption OptionA, UpgradableOption OptionB);
