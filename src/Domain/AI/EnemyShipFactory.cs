using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public static class EnemyShipFactory
{
    private const float InterceptorThreshold = 0.333f;
    private const float HeavyCannonThreshold = 0.666f;

    public static EnemyShipType PickRandomType(Random rng)
    {
        float roll = (float)rng.NextDouble();
        if (roll < InterceptorThreshold) return EnemyShipType.Interceptor;
        if (roll < HeavyCannonThreshold) return EnemyShipType.HeavyCannon;
        return EnemyShipType.Default;
    }

    public static void AddComponents(EntityManager em, Entity entity, Vector2 position, Vector2 velocity, float rotation, float angularVelocity, EnemyShipType type)
    {
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(velocity));
        em.AddComponent(entity, new Rotation(rotation));
        em.AddComponent(entity, new AngularVelocity(angularVelocity));
        em.AddComponent(entity, BuildEnemyShip(type));
        em.AddComponent(entity, BuildTurret(type));
        em.AddComponent(entity, new Health(type.Health));
    }

    public static IInitialComponent[] CreateComponents(Vector2 position, Vector2 velocity, float rotation, float angularVelocity, EnemyShipType type)
    {
        return new IInitialComponent[]
        {
            new InitialComponent<Position>(new Position(position)),
            new InitialComponent<Velocity>(new Velocity(velocity)),
            new InitialComponent<Rotation>(new Rotation(rotation)),
            new InitialComponent<AngularVelocity>(new AngularVelocity(angularVelocity)),
            new InitialComponent<EnemyShip>(BuildEnemyShip(type)),
            new InitialComponent<Turret>(BuildTurret(type)),
            new InitialComponent<Health>(new Health(type.Health))
        };
    }

    private static EnemyShip BuildEnemyShip(EnemyShipType type) =>
        new(type.Radius, type.Speed, type.TurnRate, type.FiringRange, type.TurretFireRate, type.TurretAmmoSpeed, type.Acceleration, type.Health, type.GraphicsId);

    private static Turret BuildTurret(EnemyShipType type) =>
        new(Weapon: new WeaponStats(type.TurretFireRate, type.TurretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: type.FiringRange, IsEnemy: true);
}
