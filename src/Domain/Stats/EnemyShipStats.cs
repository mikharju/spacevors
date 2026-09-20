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
    int Health,
    // Relative share of enemy spawns (EnemyShipFactory.PickRandomType); equal weights give an even split.
    float SpawnWeight = 1f)
{
    public static EnemyShipType Default { get; } = new(0, "enemy-1", 20f, 65f, 1.0f, 700f, 1.5f, 200f, 45.0f, 15);
    public static EnemyShipType Interceptor { get; } = new(1, "interceptor", 45f, 90f, 1.0f, 700f, 0.6f, 200f, 85f, 7);
    public static EnemyShipType HeavyCannon { get; } = new(2, "heavy-cannon", 78f, 50f, 1.0f, 700f, 0.8f, 160f, 45.0f, 30);

    public static readonly EnemyShipType[] All = [Default, Interceptor, HeavyCannon];

    public static EnemyShipType? FromGraphicsId(byte graphicsId) => graphicsId < All.Length ? All[graphicsId] : null;
}

// Shared enemy weapon and time-based stat tiers (plans/DIFFICULTY_SCALING.md P5).
public static class EnemyShipStats
{
    // Single enemy weapon shared by all ship types.
    public const string WeaponName = "EnemyWeapon";
    public const float WeaponArcAngle = MathF.PI / 8f;
    public const float WeaponScatter = 0.05f;
    public const int WeaponPelletCount = 1;
    public const float WeaponKickbackForce = 0f;
    public const float AmmoRadius = 2.5f;

    // Base damage of enemy turret ammo (all types); tiers add on top via DamageAdd.
    public const int BaseDamage = 1;

    // Tiers are applied at spawn time only, so existing entities are never mutated mid-run.
    // Multipliers are capped by MaxTier — stats stop growing after 10 minutes instead of compounding forever against the player's upgrades.
    public const float TierDuration = 60f;
    private const int MaxTier = 10;

    public static int TierFor(float elapsedTime) => Math.Clamp((int)(elapsedTime / TierDuration), 0, MaxTier);

    public static float HpMultiplier(int tier) => 1f + 0.25f * tier;

    public static int DamageAdd(int tier) => tier / 2;

    public static float FireRateMultiplier(int tier) => 1f + 0.1f * tier;
}
