using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class CameraSystem : GameSystem
{
    private const float FollowSpeed = 5f;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        if (!view.GetEntitiesWithComponents<Position, Player>().TryFirst(out var playerTuple)) return;

        var targetPos = playerTuple.Value1.Value;

        foreach (var (entity, camera) in view.GetEntitiesWithComponents<Camera>())
        {
            var diff = targetPos - camera.Target;
            var newTarget = camera.Target + diff * Math.Min(FollowSpeed * deltaTime, 1f);
            commands.Add(new AddComponentCommand<Camera>(entity, new Camera(newTarget)));
        }
    }
}
