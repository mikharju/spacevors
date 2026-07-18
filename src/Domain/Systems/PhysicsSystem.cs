using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class PhysicsSystem : GameSystem
{
    private const float AngularDamping = 0.95f;

    public override void Update(EntityManager em, float deltaTime)
    {
        foreach (var (entity, position) in em.GetEntitiesWithComponents<Position>())
        {
            // Linear physics: acceleration -> velocity -> position
            if (em.HasComponent<Acceleration>(entity))
            {
                var accel = em.GetComponent<Acceleration>(entity);
                Vector2 currentVel;

                if (em.HasComponent<Velocity>(entity))
                {
                    currentVel = em.GetComponent<Velocity>(entity).Value;
                }
                else
                {
                    currentVel = Vector2.Zero;
                }

                var newVel = currentVel + accel.Value * deltaTime;
                em.AddComponent(entity, new Velocity(newVel));
            }

            // Update position from velocity
            if (!em.HasComponent<Velocity>(entity)) continue;

            var v = em.GetComponent<Velocity>(entity);
            var newPos = position.Value + v.Value * deltaTime;
            em.AddComponent(entity, new Position(newPos));
        }

        // Rotation physics: angular velocity -> angle
        foreach (var (entity, rotation) in em.GetEntitiesWithComponents<Rotation>())
        {
            if (!em.HasComponent<AngularVelocity>(entity)) continue;

            var angVel = em.GetComponent<AngularVelocity>(entity);
            var dampedAngVel = angVel.Value * AngularDamping;
            em.AddComponent(entity, new AngularVelocity(dampedAngVel));

            var newAngle = rotation.Angle + angVel.Value * deltaTime;
            em.AddComponent(entity, new Rotation(newAngle));
        }
    }
}
