namespace Spacevors.Domain.Components;

public readonly record struct Position(Vector2 Value);

public readonly record struct Velocity(Vector2 Value);

public readonly record struct Acceleration(Vector2 Value);

public readonly record struct Rotation(float Angle);

public readonly record struct AngularVelocity(float Value);
