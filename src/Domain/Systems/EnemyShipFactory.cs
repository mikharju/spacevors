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
        em.AddComponent(entity, new EnemyShip(Radius, Speed, TurnRate, Health, DetectionRange, FiringRange, TurretFireRate, TurretAmmoSpeed, Acceleration, Damage: 1));
        em.AddComponent(entity, new Turret(TurretFireRate, TurretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true));
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
        em.AddComponent(entity, new EnemyShip(radius, speed, TurnRate, health, DetectionRange, FiringRange, turretFireRate, TurretAmmoSpeed, acceleration, Damage: 1));
        em.AddComponent(entity, new Turret(turretFireRate, TurretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true));
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
        em.AddComponent(entity, new EnemyShip(radius, speed, TurnRate, health, DetectionRange, FiringRange, turretFireRate, turretAmmoSpeed, Acceleration, Damage: 2));
        em.AddComponent(entity, new Turret(turretFireRate, turretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true));
        em.AddComponent(entity, new Health(health));
    }
}
