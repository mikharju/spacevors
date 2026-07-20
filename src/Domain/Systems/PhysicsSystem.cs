using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class PhysicsSystem : GameSystem
{
    private const float AngularDamping = 0.95f;

    public override void Update(EntityManager em, float deltaTime)
    {
        foreach (var (entity, accel) in em.GetEntitiesWithComponents<Acceleration>())
        {
            Vector2 currentVel = em.HasComponent<Velocity>(entity)
                ? em.GetComponent<Velocity>(entity).Value
                : Vector2.Zero;

            var newVel = currentVel + accel.Value * deltaTime;
            em.AddComponent(entity, new Velocity(newVel));
        }

        foreach (var (entity, position, velocity) in em.GetEntitiesWithComponents<Position, Velocity>())
        {
            var newPos = position.Value + velocity.Value * deltaTime;
            em.AddComponent(entity, new Position(newPos));
        }

        foreach (var (entity, rotation, angVel) in em.GetEntitiesWithComponents<Rotation, AngularVelocity>())
        {
            var dampedAngVel = angVel.Value * AngularDamping;
            em.AddComponent(entity, new AngularVelocity(dampedAngVel));

            var newAngle = rotation.Angle + angVel.Value * deltaTime;
            em.AddComponent(entity, new Rotation(newAngle));
        }
    }
}
