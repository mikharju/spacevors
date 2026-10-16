using Spacevors.Domain.Stats; // MineSize, MineType

namespace Spacevors.Domain.Components;

public readonly record struct Player(
    float Thrust,
    float SideThrust,
    float BackThrust,
    float Boost,
    int MaxHealth,
    float Radius = 18f,
    int Xp = 0,
    int Level = 1,
    float PickupRadius = 60f,
    float RotationSpeed = 5f)
{
    public float MaxThrustForce => MathF.Max(Thrust * Boost, MathF.Max(SideThrust, BackThrust));
}

// Enemy damage lives on the ship's Turret.Weapon (see EnemyShipFactory.BuildTurret).
public readonly record struct EnemyShip(
    float Radius,
    float Speed,
    float TurnRate,
    float FiringRange,
    float TurretFireRate,
    float TurretAmmoSpeed,
    float Acceleration,
    byte GraphicsId);

public readonly record struct EnemyMine(MineSize Size, float Speed, float Angle)
{
    public float Radius => MineType.FromSize(Size).Radius;
}

public readonly record struct Asteroid(bool IsSmall, float Radius, byte Variant = 0)
{
    public const int SmallVariantCount = 3;
    public const int LargeVariantCount = 3;

    // Only the small rocks can be shot to pieces: enough hits that clearing one costs real ammo,
    // few enough that holding a gun on it for a couple of seconds does it. The large ones are scenery.
    public const int SmallHealth = 50;
}

// Target is the camera center; Drift is the eased mouse-driven offset from the player.
public readonly record struct Camera(Vector2 Target, Vector2 Drift);
