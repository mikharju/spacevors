using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class BlueSparkHomeSystem : GameSystem
{
    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        bool hasPlayer = view.GetEntitiesWithComponents<Player>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;

        if (!hasPlayer) return;

        var playerPos = view.GetComponent<Position>(playerEntity);

        foreach (var (sparkEntity, _, sparkPos) in view.GetEntitiesWithComponents<BlueSpark, Position>())
        {
            var dir = playerPos.Value - sparkPos.Value;
            float distSq = dir.X * dir.X + dir.Y * dir.Y;
            if (distSq < 0.01f) continue;

            float dist = (float)Math.Sqrt(distSq);
            var normalizedDir = dir / dist;

            Vector2 currentVel = view.TryGetComponent<Velocity>(sparkEntity, out var vel) ? vel.Value : Vector2.Zero;

            float targetSpeed = 180f;
            var targetVel = normalizedDir * targetSpeed;

            float blendFactor = 1f - MathF.Exp(-6f * deltaTime);
            var newVel = currentVel + (targetVel - currentVel) * blendFactor;

            commands.Add(new AddComponentCommand<Velocity>(sparkEntity, new Velocity(newVel)));
        }
    }
}
