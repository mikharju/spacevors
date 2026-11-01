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

    // The field thins out as the run goes on (SpawningStats.Mines), so what is left of it grows rather
    // than gets tougher: the large share drifts up with the difficulty tier — the same enemy stat clock
    // that mines deliberately stay outside for hp. A large mine is easier to see and to dodge, worth 2 xp
    // instead of 1 and shoves harder, but it still dies in the same two hits
    // (plans/DIFFICULTY_SCALING.md R2.6 item 6: scarcer, not a chore to clear). The real threat step comes
    // later from new mine kinds, not from bigger ones: plans/ENDLESS_DIFFICULTY.md P6.
    public const float LargeShareStart = 0.5f;
    public const float LargeShareAtTierCap = 0.7f;

    // Chance that a spawned mine is the large one. One rng draw per spawn, exactly as the flat coin flip
    // this replaces, so seeded runs stay comparable. Clamped, so a tier past the cap cannot inflate it.
    public static float LargeChanceFor(int tier)
    {
        float progress = Math.Clamp((float)tier / EnemyShipStats.MaxTier, 0f, 1f);
        return LargeShareStart + (LargeShareAtTierCap - LargeShareStart) * progress;
    }
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
