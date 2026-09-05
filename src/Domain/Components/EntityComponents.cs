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

public readonly record struct EnemyShip(
    float Radius,
    float Speed,
    float TurnRate,
    float FiringRange,
    float TurretFireRate,
    float TurretAmmoSpeed,
    float Acceleration,
    int Damage,
    byte GraphicsId);

public readonly record struct EnemyShipType(
    byte GraphicsId,
    string TextureKey,
    float Radius,
    float Speed,
    float TurnRate,
    float FiringRange,
    float TurretFireRate,
    float TurretAmmoSpeed,
    float Acceleration,
    int Health)
{
    public static EnemyShipType Default { get; } = new(0, "enemy-1", 20f, 65f, 1.0f, 700f, 1.5f, 200f, 45.0f, 3);
    public static EnemyShipType Interceptor { get; } = new(1, "interceptor", 45f, 90f, 1.0f, 700f, 0.6f, 200f, 85f, 2);
    public static EnemyShipType HeavyCannon { get; } = new(2, "heavy-cannon", 78f, 50f, 1.0f, 700f, 0.8f, 160f, 45.0f, 5);

    public static readonly EnemyShipType[] All = [Default, Interceptor, HeavyCannon];

    public static EnemyShipType? FromGraphicsId(byte graphicsId) => graphicsId < All.Length ? All[graphicsId] : null;
}

public enum MineSize { Small = 0, Large = 1 }

public readonly record struct EnemyMine(MineSize Size, float Speed, float Angle)
{
    public float Radius => MineType.FromSize(Size).Radius;
}

public readonly record struct MineType(
    float Radius,
    float ExplosionRadius,
    int HitSparkCount,
    float PlayerContactForce,
    int PlayerContactSparkCount,
    int XpAmount,
    float XpPickupRadius)
{
    public static MineType Small { get; } = new(7.5f, 15f, 3, 120f, 5, 1, 6f);
    public static MineType Large { get; } = new(15f, 30f, 7, 240f, 10, 2, 9f);

    public static MineType FromSize(MineSize size) => size == MineSize.Large ? Large : Small;
}

public readonly record struct Asteroid(bool IsSmall, float Radius, byte Variant = 0)
{
    public const int SmallVariantCount = 3;
    public const int LargeVariantCount = 3;
}

// Target is the camera center; Drift is the eased mouse-driven offset from the player.
public readonly record struct Camera(Vector2 Target, Vector2 Drift);
