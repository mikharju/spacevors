using System.Diagnostics;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class PositionIntegrationSystem : GameSystem
{
    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var sw = Stopwatch.StartNew();

        var velStorage = view.GetStorage<Velocity>();
        var posStorage = view.GetStorage<Position>();

        int updated = 0;

        for (int i = 0; i < posStorage.Count; i++)
        {
            Entity entity = posStorage.GetEntity(i);

            if (!velStorage.TryGetSlot(entity, out int velSlot))
                continue;

            ref Position position = ref posStorage.GetComponent(i);
            ref Velocity velocity = ref velStorage.GetComponent(velSlot);

            position = new Position(position.Value + velocity.Value * deltaTime);
            updated++;
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Position: integration", sw.ElapsedTicks, updated);
    }
}
