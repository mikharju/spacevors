namespace Spacevors.Domain.Components;

public readonly record struct Turret(float FireRate, float AmmoSpeed, float KickbackForce, float ArcAngle, float Range);
