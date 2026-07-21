namespace Spacevors.Domain.Components;

public readonly record struct Ammo(Vector2 Velocity, float Radius, float Lifetime, bool IsEnemy = false);
