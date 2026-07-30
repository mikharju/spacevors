using System.Diagnostics;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class PositionIntegrationSystem : GameSystem
{
    public override void DirectMutationUpdate(WorldView view, float deltaTime)
    {
        var sw = Stopwatch.StartNew();

        foreach (var (entity, velocity) in view.GetEntitiesWithComponents<Velocity>())
        {
            ref var pos = ref view.GetComponentRef<Position>(entity);
            pos = new Position(pos.Value + velocity.Value * deltaTime);
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Position: integration", sw.ElapsedTicks);
    }
}
