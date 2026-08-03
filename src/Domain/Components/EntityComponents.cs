namespace Spacevors.Domain.Components;

public readonly record struct Player(
    float Thrust,
    float SideThrust,
    float BackThrust,
    float Boost,
    float Radius = 18f,
    int Xp = 0,
    int Level = 1,
    float PickupRadius = 60f,
    float RotationSpeed = 5f);

public readonly record struct EnemyShip(
    float Radius,
    float Speed,
    float TurnRate,
    float DetectionRange,
    float FiringRange,
    float TurretFireRate,
    float TurretAmmoSpeed,
    float Acceleration,
    int Damage,
    byte GraphicsId);

public enum MineSize { Small = 0, Large = 1 }

public readonly record struct EnemyMine(MineSize Size, float Speed, float Angle)
{
    public float Radius => Size == MineSize.Large ? 15f : 7.5f;
}

public readonly record struct Asteroid(bool IsSmall, float Radius, byte Variant = 0)
{
    public const int SmallVariantCount = 6;
    public const int LargeVariantCount = 6;
}

public readonly record struct Camera(Vector2 Target);
