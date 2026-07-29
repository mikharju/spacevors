using System.Diagnostics;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class PositionIntegrationSystem : GameSystem
{
    public override void DirectMutationUpdate(WorldView view, float deltaTime)
    {
        var sw = Stopwatch.StartNew();

        foreach (var (entity, position, velocity) in view.GetEntitiesWithComponents<Position, Velocity>())
        {
            var newPos = position.Value + velocity.Value * deltaTime;
            view.SetComponent(entity, new Position(newPos));
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Position: integration", sw.ElapsedTicks);
    }
}
