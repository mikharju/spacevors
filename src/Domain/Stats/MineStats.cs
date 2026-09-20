namespace Spacevors.Domain.Stats;

public enum MineSize { Small = 0, Large = 1 }

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
