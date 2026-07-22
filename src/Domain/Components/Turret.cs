namespace Spacevors.Domain.Components;

public readonly record struct Turret(
    float FireRate,
    float AmmoSpeed,
    float KickbackForce = 0f,
    int PelletCount = 1,
    float ArcAngle = MathF.PI / 2f,
    float Range = 360f,
    bool IsEnemy = false);
