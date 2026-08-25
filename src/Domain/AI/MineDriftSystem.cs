using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class MineDriftSystem : GameSystem
{
    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        bool hasPlayer = view.GetEntitiesWithComponents<Player>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;

        foreach (var (mineEntity, mine, minePos) in view.GetEntitiesWithComponents<EnemyMine, Position>())
        {
            if (!hasPlayer) continue;
            var playerPos = view.GetComponent<Position>(playerEntity);

            var dir = playerPos.Value - minePos.Value;
            float distSq = dir.X * dir.X + dir.Y * dir.Y;
            if (distSq < 0.01f) continue;

            float dist = (float)Math.Sqrt(distSq);
            var normalizedDir = dir / dist;

            Vector2 currentVel = view.TryGetComponent<Velocity>(mineEntity, out var vel) ? vel.Value : Vector2.Zero;

            float targetSpeed = mine.Speed;
            var targetVel = normalizedDir * targetSpeed;

            float blendFactor = 1f - MathF.Exp(-3f * deltaTime);
            var newVel = currentVel + (targetVel - currentVel) * blendFactor;

            commands.Add(new AddComponentCommand<Velocity>(mineEntity, new Velocity(newVel)));
        }
    }
}
