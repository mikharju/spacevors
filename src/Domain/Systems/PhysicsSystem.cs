using System.Diagnostics;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class PhysicsSystem : GameSystem
{
    private const float AngularDamping = 0.95f;

    public override void GenerateUpdateCommands(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var sw = Stopwatch.StartNew();

        foreach (var (entity, accel) in view.GetEntitiesWithComponents<Acceleration>())
        {
            view.TryGetComponent<Velocity>(entity, out var vel);
            var currentVel = vel.Value;

            var newVel = currentVel + accel.Value * deltaTime;
            commands.Add(new AddComponentCommand<Velocity>(entity, new Velocity(newVel)));
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Physics: velocity integration", sw.ElapsedTicks);

        sw.Restart();

        foreach (var (entity, rotation, angVel) in view.GetEntitiesWithComponents<Rotation, AngularVelocity>())
        {
            var dampedAngVel = angVel.Value * AngularDamping;
            commands.Add(new AddComponentCommand<AngularVelocity>(entity, new AngularVelocity(dampedAngVel)));

            var newAngle = rotation.Angle + angVel.Value * deltaTime;
            commands.Add(new AddComponentCommand<Rotation>(entity, new Rotation(newAngle)));
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Physics: rotation", sw.ElapsedTicks);
    }
}
