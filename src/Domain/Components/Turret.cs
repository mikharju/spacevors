namespace Spacevors.Domain.Components;

public readonly record struct Turret(
    WeaponStats Weapon,
    float ArcAngle = MathF.PI / 2f,
    float Range = 360f,
    bool IsEnemy = false);
