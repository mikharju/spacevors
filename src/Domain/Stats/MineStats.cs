namespace Spacevors.Domain.Stats;

public enum MineSize { Small = 0, Large = 1 }

// Shared mine tuning; per-size values live on MineType.
public static class MineStats
{
    // Spawn speed is random within [SpawnSpeedMin, SpawnSpeedMax].
    public const float SpawnSpeedMin = 30f;
    public const float SpawnSpeedMax = 50f;

    // Damage dealt to the player on contact (CollisionSystem).
    public const int ContactDamage = 12;

    // The field thins out as the run goes on (SpawningStats.Mines), so what is left of it grows rather
    // than gets more numerous: the large share drifts up with the difficulty tier, and since R32 so does the
    // hp of what spawns (`HealthAtTier`). A large mine is easier to see and to dodge, worth 2 xp instead of
    // 1 and shoves harder, but it is never harder to kill than a small one of its own tier
    // (plans/DIFFICULTY_SCALING.md R2.6 item 6: scarcer, not a chore to clear — R32 only gave the leftover
    // field some hp so a sniper round stops being pure overkill). The real threat step still comes later
    // from new mine kinds, not from bigger ones: plans/ENDLESS_DIFFICULTY.md P6.
    public const float LargeShareStart = 0.5f;
    public const float LargeShareAtTierCap = 0.7f;

    // Chance that a spawned mine is the large one. One rng draw per spawn, exactly as the flat coin flip
    // this replaces, so seeded runs stay comparable. Clamped, so a tier past the cap cannot inflate it.
    public static float LargeChanceFor(int tier)
    {
        float progress = Math.Clamp((float)tier / EnemyShipStats.MaxTier, 0f, 1f);
        return LargeShareStart + (LargeShareAtTierCap - LargeShareStart) * progress;
    }

    // R32: the hit points a spawned mine carries. 8 at the opening — two hits from a 4-damage support chip,
    // as a 2 hp mine was under a 1-damage one — and +2 per difficulty minute up to 28 at the tier cap, so the
    // field left late in a run is worth more than one RailGun slug (7.5x overkill before, 2.1x now) without
    // becoming a chore to clear. Both sizes share it, as they always have.
    public const int HealthBase = 8;
    public const int HealthPerTier = 2;
    public static int HealthAtTier(int tier) => HealthBase + HealthPerTier * tier;
}

public readonly record struct MineType(
    float Radius,
    float ExplosionRadius,
    int HitSparkCount,
    float PlayerContactForce,
    int PlayerContactSparkCount,
    int XpAmount,
    float XpPickupRadius,
    // Health shared by both sizes — the base that the tier scaling is added to at spawn (`HealthAtTier`).
    int Health)
{
    public static MineType Small { get; } = new(7.5f, 15f, 3, 120f, 5, 1, 6f, Health: MineStats.HealthBase);
    public static MineType Large { get; } = new(15f, 30f, 7, 240f, 10, 2, 9f, Health: MineStats.HealthBase);

    public static MineType FromSize(MineSize size) => size == MineSize.Large ? Large : Small;
}
