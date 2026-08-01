using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public static class EnemyShipFactory
{
    public const byte DefaultGraphicsId = 0;
    public const byte InterceptorGraphicsId = 1;
    public const byte HeavyCannonGraphicsId = 2;

    // Shared constants for default enemy ship
    public const float Radius = 20f;
    public const float Speed = 35f;
    public const float TurnRate = 1.0f;
    public const int Health = 3;
    public const float DetectionRange = 1200f;
    public const float FiringRange = 300f;
    public const float TurretFireRate = 1.5f;
    public const float TurretAmmoSpeed = 200f;
    public const float Acceleration = 9.0f;

    private static (float radius, float speed, int health, float acceleration, float turretFireRate, float turretAmmoSpeed) GetInterceptorParams()
    {
        return (45f, 40f, 2, 15f, 0.6f, TurretAmmoSpeed);
    }

    private static (float radius, float speed, int health, float acceleration, float turretFireRate, float turretAmmoSpeed) GetHeavyCannonParams()
    {
        return (78f, 25f, 5, Acceleration, 0.8f, 160f);
    }

    public static void AddEnemyShipComponents(EntityManager em, Entity entity, Vector2 position, Vector2 velocity, float rotation, float angularVelocity)
    {
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(velocity));
        em.AddComponent(entity, new Rotation(rotation));
        em.AddComponent(entity, new AngularVelocity(angularVelocity));
        em.AddComponent(entity, new EnemyShip(Radius, Speed, TurnRate, DetectionRange, FiringRange, TurretFireRate, TurretAmmoSpeed, Acceleration, Health, DefaultGraphicsId));
        em.AddComponent(entity, new Turret(Weapon: new WeaponStats(TurretFireRate, TurretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true));
        em.AddComponent(entity, new Health(Health));
    }

    public static void AddInterceptorComponents(EntityManager em, Entity entity, Vector2 position, Vector2 velocity, float rotation, float angularVelocity)
    {
        var (radius, speed, health, acceleration, turretFireRate, turretAmmoSpeed) = GetInterceptorParams();

        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(velocity));
        em.AddComponent(entity, new Rotation(rotation));
        em.AddComponent(entity, new AngularVelocity(angularVelocity));
        em.AddComponent(entity, new EnemyShip(radius, speed, TurnRate, DetectionRange, FiringRange, turretFireRate, turretAmmoSpeed, acceleration, health, InterceptorGraphicsId));
        em.AddComponent(entity, new Turret(Weapon: new WeaponStats(turretFireRate, turretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true));
        em.AddComponent(entity, new Health(health));
    }

    public static void AddHeavyCannonComponents(EntityManager em, Entity entity, Vector2 position, Vector2 velocity, float rotation, float angularVelocity)
    {
        var (radius, speed, health, acceleration, turretFireRate, turretAmmoSpeed) = GetHeavyCannonParams();

        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(velocity));
        em.AddComponent(entity, new Rotation(rotation));
        em.AddComponent(entity, new AngularVelocity(angularVelocity));
        em.AddComponent(entity, new EnemyShip(radius, speed, TurnRate, DetectionRange, FiringRange, turretFireRate, turretAmmoSpeed, acceleration, health, HeavyCannonGraphicsId));
        em.AddComponent(entity, new Turret(Weapon: new WeaponStats(turretFireRate, turretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true));
        em.AddComponent(entity, new Health(health));
    }

    public static IInitialComponent[] CreateInterceptorComponents(Vector2 position, Vector2 velocity, float rotation, float angularVelocity)
    {
        var (radius, speed, health, acceleration, turretFireRate, turretAmmoSpeed) = GetInterceptorParams();

        return new IInitialComponent[]
        {
            new InitialComponent<Position>(new Position(position)),
            new InitialComponent<Velocity>(new Velocity(velocity)),
            new InitialComponent<Rotation>(new Rotation(rotation)),
            new InitialComponent<AngularVelocity>(new AngularVelocity(angularVelocity)),
            new InitialComponent<EnemyShip>(new EnemyShip(radius, speed, TurnRate, DetectionRange, FiringRange, turretFireRate, turretAmmoSpeed, acceleration, health, InterceptorGraphicsId)),
            new InitialComponent<Turret>(new Turret(Weapon: new WeaponStats(turretFireRate, turretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true)),
            new InitialComponent<Health>(new Health(health))
        };
    }

    public static IInitialComponent[] CreateHeavyCannonComponents(Vector2 position, Vector2 velocity, float rotation, float angularVelocity)
    {
        var (radius, speed, health, acceleration, turretFireRate, turretAmmoSpeed) = GetHeavyCannonParams();

        return new IInitialComponent[]
        {
            new InitialComponent<Position>(new Position(position)),
            new InitialComponent<Velocity>(new Velocity(velocity)),
            new InitialComponent<Rotation>(new Rotation(rotation)),
            new InitialComponent<AngularVelocity>(new AngularVelocity(angularVelocity)),
            new InitialComponent<EnemyShip>(new EnemyShip(radius, speed, TurnRate, DetectionRange, FiringRange, turretFireRate, turretAmmoSpeed, acceleration, health, HeavyCannonGraphicsId)),
            new InitialComponent<Turret>(new Turret(Weapon: new WeaponStats(turretFireRate, turretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true)),
            new InitialComponent<Health>(new Health(health))
        };
    }

    public static IInitialComponent[] CreateEnemyShipComponents(Vector2 position, Vector2 velocity, float rotation, float angularVelocity)
    {
        return new IInitialComponent[]
        {
            new InitialComponent<Position>(new Position(position)),
            new InitialComponent<Velocity>(new Velocity(velocity)),
            new InitialComponent<Rotation>(new Rotation(rotation)),
            new InitialComponent<AngularVelocity>(new AngularVelocity(angularVelocity)),
            new InitialComponent<EnemyShip>(new EnemyShip(Radius, Speed, TurnRate, DetectionRange, FiringRange, TurretFireRate, TurretAmmoSpeed, Acceleration, Health, DefaultGraphicsId)),
            new InitialComponent<Turret>(new Turret(Weapon: new WeaponStats(TurretFireRate, TurretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true)),
            new InitialComponent<Health>(new Health(Health))
        };
    }
}
