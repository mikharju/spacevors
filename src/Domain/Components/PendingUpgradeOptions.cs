namespace Spacevors.Domain.Components;

public enum UpgradeOption { FireRate, ProjectileSpeed, PickupRadius, AutoTargetRange, ShotLifetime, Damage, Hp, ForwardAcceleration, TurnSpeed }

public readonly record struct UpgradableOption(string WeaponName, UpgradeOption Stat, bool IsNewWeapon = false);

public readonly record struct PendingUpgradeOptions(UpgradableOption[] Options);
