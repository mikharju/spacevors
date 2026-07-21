namespace Spacevors.Domain.Components;

public readonly record struct EnemyShip(
    float Radius,
    float Speed,
    float TurnRate,
    int Health,
    float DetectionRange,
    float FiringRange,
    float TurretFireRate,
    float TurretAmmoSpeed);
