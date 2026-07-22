namespace Spacevors.Domain.Components;

public readonly record struct Weapon(
    float FireRate,
    float AmmoSpeed,
    float KickbackForce,
    int PelletCount = 1,
    float UpgradeFireRateMultiplier = 1f,
    float UpgradeProjectileSpeedMultiplier = 1f)
{
    public float EffectiveFireRate => FireRate * UpgradeFireRateMultiplier;
    public float EffectiveAmmoSpeed => AmmoSpeed * UpgradeProjectileSpeedMultiplier;
}
