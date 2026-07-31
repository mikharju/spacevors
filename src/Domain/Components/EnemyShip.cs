namespace Spacevors.Domain.Components;

public readonly record struct EnemyShip(
    float Radius,
    float Speed,
    float TurnRate,
    float DetectionRange,
    float FiringRange,
    float TurretFireRate,
    float TurretAmmoSpeed,
    float Acceleration,
    int Damage);
