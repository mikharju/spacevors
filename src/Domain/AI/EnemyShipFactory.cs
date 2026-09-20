using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

public static class EnemyShipFactory
{
    // Weighted pick over all types (one RNG draw per call).
    public static EnemyShipType PickRandomType(Random rng)
    {
        float totalWeight = 0f;
        foreach (var type in EnemyShipType.All) totalWeight += type.SpawnWeight;

        float roll = (float)rng.NextDouble() * totalWeight;
        foreach (var type in EnemyShipType.All)
        {
            roll -= type.SpawnWeight;
            if (roll < 0f) return type;
        }
        return EnemyShipType.All[^1];
    }

    public static void AddComponents(EntityManager em, Entity entity, Vector2 position, Vector2 velocity, float rotation, float angularVelocity, EnemyShipType type, float elapsedTime = 0f)
    {
        int tier = EnemyShipStats.TierFor(elapsedTime);
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(velocity));
        em.AddComponent(entity, new Rotation(rotation));
        em.AddComponent(entity, new AngularVelocity(angularVelocity));
        em.AddComponent(entity, BuildEnemyShip(type));
        em.AddComponent(entity, BuildTurret(type, tier));
        int health = TieredHealth(type, tier);
        em.AddComponent(entity, new Health(health, health));
    }

    public static IInitialComponent[] CreateComponents(Vector2 position, Vector2 velocity, float rotation, float angularVelocity, EnemyShipType type, float elapsedTime = 0f)
    {
        int tier = EnemyShipStats.TierFor(elapsedTime);
        int health = TieredHealth(type, tier);
        return new IInitialComponent[]
        {
            new InitialComponent<Position>(new Position(position)),
            new InitialComponent<Velocity>(new Velocity(velocity)),
            new InitialComponent<Rotation>(new Rotation(rotation)),
            new InitialComponent<AngularVelocity>(new AngularVelocity(angularVelocity)),
            new InitialComponent<EnemyShip>(BuildEnemyShip(type)),
            new InitialComponent<Turret>(BuildTurret(type, tier)),
            new InitialComponent<Health>(new Health(health, health))
        };
    }

    private static int TieredHealth(EnemyShipType type, int tier) =>
        (int)MathF.Round(type.Health * EnemyShipStats.HpMultiplier(tier), MidpointRounding.AwayFromZero);

    private static EnemyShip BuildEnemyShip(EnemyShipType type) =>
        new(type.Radius, type.Speed, type.TurnRate, type.FiringRange, type.TurretFireRate, type.TurretAmmoSpeed, type.Acceleration, type.GraphicsId);

    private static Turret BuildTurret(EnemyShipType type, int tier) =>
        new(Weapon: new WeaponStats(type.TurretFireRate * EnemyShipStats.FireRateMultiplier(tier), type.TurretAmmoSpeed, KickbackForce: EnemyShipStats.WeaponKickbackForce, PelletCount: EnemyShipStats.WeaponPelletCount, Scatter: EnemyShipStats.WeaponScatter, Damage: EnemyShipStats.BaseDamage + EnemyShipStats.DamageAdd(tier)), WeaponName: EnemyShipStats.WeaponName, ArcAngle: EnemyShipStats.WeaponArcAngle, Range: type.FiringRange, IsEnemy: true);
}
