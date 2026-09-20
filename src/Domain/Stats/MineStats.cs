namespace Spacevors.Domain.Stats;

public enum MineSize { Small = 0, Large = 1 }

// Shared mine tuning; per-size values live on MineType.
public static class MineStats
{
    // Spawn speed is random within [SpawnSpeedMin, SpawnSpeedMax].
    public const float SpawnSpeedMin = 30f;
    public const float SpawnSpeedMax = 50f;

    // Damage dealt to the player on contact (CollisionSystem).
    public const int ContactDamage = 3;
}

public readonly record struct MineType(
    float Radius,
    float ExplosionRadius,
    int HitSparkCount,
    float PlayerContactForce,
    int PlayerContactSparkCount,
    int XpAmount,
    float XpPickupRadius,
    // Fixed health shared by all mines; difficulty tiers do not apply to mines.
    int Health)
{
    public static MineType Small { get; } = new(7.5f, 15f, 3, 120f, 5, 1, 6f, Health: 2);
    public static MineType Large { get; } = new(15f, 30f, 7, 240f, 10, 2, 9f, Health: 2);

    public static MineType FromSize(MineSize size) => size == MineSize.Large ? Large : Small;
}
