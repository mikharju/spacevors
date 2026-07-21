using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class MineDriftSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        var mines = em.GetEntitiesWithComponents<EnemyMine>().ToList();

        foreach (var (mineEntity, mine) in mines)
        {
            if (!hasPlayer) continue;

            var minePos = em.GetComponent<Position>(mineEntity);
            var playerPos = em.GetComponent<Position>(playerEntity);

            var dir = playerPos.Value - minePos.Value;
            float distSq = dir.X * dir.X + dir.Y * dir.Y;
            if (distSq < 0.01f) continue;

            float dist = (float)Math.Sqrt(distSq);
            var normalizedDir = dir / dist;

            var currentVel = em.HasComponent<Velocity>(mineEntity)
                ? em.GetComponent<Velocity>(mineEntity).Value
                : Vector2.Zero;

            // Blend toward player direction at mine speed
            float targetSpeed = mine.Speed;
            var targetVel = normalizedDir * targetSpeed;

            // Smooth blend (lerp factor based on deltaTime)
            float blendFactor = 1f - MathF.Exp(-3f * deltaTime);
            var newVel = currentVel + (targetVel - currentVel) * blendFactor;

            em.AddComponent(mineEntity, new Velocity(newVel));
        }
    }
}
