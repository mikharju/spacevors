using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class CameraSystem : GameSystem
{
    private const float FollowSpeed = 5f;

    public override void GenerateUpdateCommands(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var playerTuple = view.GetEntitiesWithComponents<Position, Player>().FirstOrDefault();
        if (playerTuple.Entity.Value < 0) return;

        var targetPos = playerTuple.Value1.Value;

        foreach (var (entity, camera) in view.GetEntitiesWithComponents<Camera>())
        {
            var diff = targetPos - camera.Target;
            var newTarget = camera.Target + diff * Math.Min(FollowSpeed * deltaTime, 1f);
            commands.Add(new AddComponentCommand<Camera>(entity, new Camera(newTarget)));
        }
    }
}
