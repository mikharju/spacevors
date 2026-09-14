using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public static class EnemyShipFactory
{
    private const float InterceptorThreshold = 0.333f;
    private const float HeavyCannonThreshold = 0.666f;

    // Time-based stat tiers (plans/DIFFICULTY_SCALING.md P5): applied at spawn time only, so existing
    // entities are never mutated mid-run. Multipliers are capped by MaxTier — stats stop growing after
    // 10 minutes instead of compounding forever against the player's upgrades.
    public const float TierDuration = 60f;
    private const int MaxTier = 10;

    // Base damage of enemy turret ammo (all types); tiers add on top via DamageAdd.
    private const int BaseDamage = 1;

    public static EnemyShipType PickRandomType(Random rng)
    {
        float roll = (float)rng.NextDouble();
        if (roll < InterceptorThreshold) return EnemyShipType.Interceptor;
        if (roll < HeavyCannonThreshold) return EnemyShipType.HeavyCannon;
        return EnemyShipType.Default;
    }

    public static int TierFor(float elapsedTime) => Math.Clamp((int)(elapsedTime / TierDuration), 0, MaxTier);

    public static float HpMultiplier(int tier) => 1f + 0.25f * tier;

    public static int DamageAdd(int tier) => tier / 2;

    public static float FireRateMultiplier(int tier) => 1f + 0.1f * tier;

    public static void AddComponents(EntityManager em, Entity entity, Vector2 position, Vector2 velocity, float rotation, float angularVelocity, EnemyShipType type, float elapsedTime = 0f)
    {
        int tier = TierFor(elapsedTime);
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(velocity));
        em.AddComponent(entity, new Rotation(rotation));
        em.AddComponent(entity, new AngularVelocity(angularVelocity));
        em.AddComponent(entity, BuildEnemyShip(type));
        em.AddComponent(entity, BuildTurret(type, tier));
        em.AddComponent(entity, new Health(TieredHealth(type, tier)));
    }

    public static IInitialComponent[] CreateComponents(Vector2 position, Vector2 velocity, float rotation, float angularVelocity, EnemyShipType type, float elapsedTime = 0f)
    {
        int tier = TierFor(elapsedTime);
        return new IInitialComponent[]
        {
            new InitialComponent<Position>(new Position(position)),
            new InitialComponent<Velocity>(new Velocity(velocity)),
            new InitialComponent<Rotation>(new Rotation(rotation)),
            new InitialComponent<AngularVelocity>(new AngularVelocity(angularVelocity)),
            new InitialComponent<EnemyShip>(BuildEnemyShip(type)),
            new InitialComponent<Turret>(BuildTurret(type, tier)),
            new InitialComponent<Health>(new Health(TieredHealth(type, tier)))
        };
    }

    private static int TieredHealth(EnemyShipType type, int tier) =>
        (int)MathF.Round(type.Health * HpMultiplier(tier), MidpointRounding.AwayFromZero);

    private static EnemyShip BuildEnemyShip(EnemyShipType type) =>
        new(type.Radius, type.Speed, type.TurnRate, type.FiringRange, type.TurretFireRate, type.TurretAmmoSpeed, type.Acceleration, type.GraphicsId);

    private static Turret BuildTurret(EnemyShipType type, int tier) =>
        new(Weapon: new WeaponStats(type.TurretFireRate * FireRateMultiplier(tier), type.TurretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f, Damage: BaseDamage + DamageAdd(tier)), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: type.FiringRange, IsEnemy: true);
}
