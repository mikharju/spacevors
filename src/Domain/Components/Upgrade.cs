namespace Spacevors.Domain.Components;

public readonly record struct Upgrade(UpgradeType Type, float Lifetime = 30f, float Radius = 15f);

public enum UpgradeType { FireRate, ProjectileSpeed }
