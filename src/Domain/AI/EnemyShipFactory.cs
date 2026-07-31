using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public static class EnemyShipFactory
{
    public const float Radius = 20f;
    public const float Speed = 35f;
    public const float TurnRate = 1.0f;
    public const int Health = 3;
    public const float DetectionRange = 1200f;
    public const float FiringRange = 300f;
    public const float TurretFireRate = 1.5f;
    public const float TurretAmmoSpeed = 200f;
    public const float Acceleration = 9.0f;

    public static void AddEnemyShipComponents(EntityManager em, Entity entity, Vector2 position, Vector2 velocity, float rotation, float angularVelocity)
    {
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(velocity));
        em.AddComponent(entity, new Rotation(rotation));
        em.AddComponent(entity, new AngularVelocity(angularVelocity));
        em.AddComponent(entity, new EnemyShip(Radius, Speed, TurnRate, DetectionRange, FiringRange, TurretFireRate, TurretAmmoSpeed, Acceleration, 1));
        em.AddComponent(entity, new Turret(Weapon: new WeaponStats(TurretFireRate, TurretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true));
        em.AddComponent(entity, new Health(Health));
    }

    public static void AddInterceptorComponents(EntityManager em, Entity entity, Vector2 position, Vector2 velocity, float rotation, float angularVelocity)
    {
        const float radius = 15f;
        const float speed = 40f;
        const int health = 2;
        const float acceleration = 15f;
        const float turretFireRate = 0.6f;

        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(velocity));
        em.AddComponent(entity, new Rotation(rotation));
        em.AddComponent(entity, new AngularVelocity(angularVelocity));
        em.AddComponent(entity, new EnemyShip(radius, speed, TurnRate, DetectionRange, FiringRange, turretFireRate, TurretAmmoSpeed, acceleration, 1));
        em.AddComponent(entity, new Turret(Weapon: new WeaponStats(turretFireRate, TurretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true));
        em.AddComponent(entity, new Health(health));
    }

    public static void AddHeavyCannonComponents(EntityManager em, Entity entity, Vector2 position, Vector2 velocity, float rotation, float angularVelocity)
    {
        const float radius = 28f;
        const float speed = 25f;
        const int health = 5;
        const float turretFireRate = 0.8f;
        const float turretAmmoSpeed = 160f;

        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(velocity));
        em.AddComponent(entity, new Rotation(rotation));
        em.AddComponent(entity, new AngularVelocity(angularVelocity));
        em.AddComponent(entity, new EnemyShip(radius, speed, TurnRate, DetectionRange, FiringRange, turretFireRate, turretAmmoSpeed, Acceleration, 2));
        em.AddComponent(entity, new Turret(Weapon: new WeaponStats(turretFireRate, turretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true));
        em.AddComponent(entity, new Health(health));
    }

    public static IInitialComponent[] CreateInterceptorComponents(Vector2 position, Vector2 velocity, float rotation, float angularVelocity)
    {
        const float radius = 15f;
        const float speed = 40f;
        const int health = 2;
        const float acceleration = 15f;
        const float turretFireRate = 0.6f;

        return new IInitialComponent[]
        {
            new InitialComponent<Position>(new Position(position)),
            new InitialComponent<Velocity>(new Velocity(velocity)),
            new InitialComponent<Rotation>(new Rotation(rotation)),
            new InitialComponent<AngularVelocity>(new AngularVelocity(angularVelocity)),
            new InitialComponent<EnemyShip>(new EnemyShip(radius, speed, TurnRate, DetectionRange, FiringRange, turretFireRate, TurretAmmoSpeed, acceleration, 1)),
            new InitialComponent<Turret>(new Turret(Weapon: new WeaponStats(turretFireRate, TurretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true)),
            new InitialComponent<Health>(new Health(health))
        };
    }

    public static IInitialComponent[] CreateHeavyCannonComponents(Vector2 position, Vector2 velocity, float rotation, float angularVelocity)
    {
        const float radius = 28f;
        const float speed = 25f;
        const int health = 5;
        const float turretFireRate = 0.8f;
        const float turretAmmoSpeed = 160f;

        return new IInitialComponent[]
        {
            new InitialComponent<Position>(new Position(position)),
            new InitialComponent<Velocity>(new Velocity(velocity)),
            new InitialComponent<Rotation>(new Rotation(rotation)),
            new InitialComponent<AngularVelocity>(new AngularVelocity(angularVelocity)),
            new InitialComponent<EnemyShip>(new EnemyShip(radius, speed, TurnRate, DetectionRange, FiringRange, turretFireRate, turretAmmoSpeed, Acceleration, 2)),
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
            new InitialComponent<EnemyShip>(new EnemyShip(Radius, Speed, TurnRate, DetectionRange, FiringRange, TurretFireRate, TurretAmmoSpeed, Acceleration, 1)),
            new InitialComponent<Turret>(new Turret(Weapon: new WeaponStats(TurretFireRate, TurretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f), WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true)),
            new InitialComponent<Health>(new Health(Health))
        };
    }
}
