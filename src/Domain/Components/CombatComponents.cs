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

public enum AmmoColor { Yellow, Green, Blue, Red }

public readonly record struct Ammo(Vector2 Velocity, float Radius, float Lifetime, bool IsEnemy = false, int Damage = 1, AmmoColor Color = AmmoColor.Yellow);

public readonly record struct FireCooldown(float Timer);

public readonly record struct Turret(
    WeaponStats Weapon,
    string WeaponName,
    float ArcAngle = MathF.PI / 2f,
    float Range = 360f,
    bool AutoTarget = true,
    bool IsEnemy = false);

public readonly record struct WeaponSlots(int Used, int Max);

public readonly record struct ArcOffset(float Angle);

public readonly record struct TurretOffset(Vector2 Value);
