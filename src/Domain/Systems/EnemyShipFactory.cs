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
        em.AddComponent(entity, new EnemyShip(Radius, Speed, TurnRate, Health, DetectionRange, FiringRange, TurretFireRate, TurretAmmoSpeed, Acceleration));
        em.AddComponent(entity, new Turret(TurretFireRate, TurretAmmoSpeed, KickbackForce: 0f, PelletCount: 1, ArcAngle: MathF.PI / 8f, Range: DetectionRange, IsEnemy: true));
        em.AddComponent(entity, new Health(Health));
    }
}
