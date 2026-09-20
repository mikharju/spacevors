namespace Spacevors.Domain.Stats;

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
    public static EnemyShipType Default { get; } = new(0, "enemy-1", 20f, 65f, 1.0f, 700f, 1.5f, 200f, 45.0f, 15);
    public static EnemyShipType Interceptor { get; } = new(1, "interceptor", 45f, 90f, 1.0f, 700f, 0.6f, 200f, 85f, 7);
    public static EnemyShipType HeavyCannon { get; } = new(2, "heavy-cannon", 78f, 50f, 1.0f, 700f, 0.8f, 160f, 45.0f, 30);

    public static readonly EnemyShipType[] All = [Default, Interceptor, HeavyCannon];

    public static EnemyShipType? FromGraphicsId(byte graphicsId) => graphicsId < All.Length ? All[graphicsId] : null;
}
