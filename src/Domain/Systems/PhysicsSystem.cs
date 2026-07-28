using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class PhysicsSystem : GameSystem
{
    private const float AngularDamping = 0.95f;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        foreach (var (entity, accel) in view.GetEntitiesWithComponents<Acceleration>())
        {
            Vector2 currentVel = view.HasComponent<Velocity>(entity)
                ? view.GetComponent<Velocity>(entity).Value
                : Vector2.Zero;

            var newVel = currentVel + accel.Value * deltaTime;
            commands.Add(new AddComponentCommand<Velocity>(entity, new Velocity(newVel)));
        }

        foreach (var (entity, position, velocity) in view.GetEntitiesWithComponents<Position, Velocity>())
        {
            var newPos = position.Value + velocity.Value * deltaTime;
            commands.Add(new AddComponentCommand<Position>(entity, new Position(newPos)));
        }

        foreach (var (entity, rotation, angVel) in view.GetEntitiesWithComponents<Rotation, AngularVelocity>())
        {
            var dampedAngVel = angVel.Value * AngularDamping;
            commands.Add(new AddComponentCommand<AngularVelocity>(entity, new AngularVelocity(dampedAngVel)));

            var newAngle = rotation.Angle + angVel.Value * deltaTime;
            commands.Add(new AddComponentCommand<Rotation>(entity, new Rotation(newAngle)));
        }
    }
}
