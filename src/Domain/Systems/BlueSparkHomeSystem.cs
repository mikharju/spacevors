using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class BlueSparkHomeSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        if (!hasPlayer) return;

        var playerPos = em.GetComponent<Position>(playerEntity);

        var blueSparks = em.GetEntitiesWithComponents<BlueSpark, Position>().ToList();

        foreach (var (sparkEntity, _, sparkPos) in blueSparks)
        {
            if (!em.HasComponent<BlueSpark>(sparkEntity)) continue;

            var dir = playerPos.Value - sparkPos.Value;
            float distSq = dir.X * dir.X + dir.Y * dir.Y;
            if (distSq < 0.01f) continue;

            float dist = (float)Math.Sqrt(distSq);
            var normalizedDir = dir / dist;

            var currentVel = em.HasComponent<Velocity>(sparkEntity)
                ? em.GetComponent<Velocity>(sparkEntity).Value
                : Vector2.Zero;

            float targetSpeed = 180f;
            var targetVel = normalizedDir * targetSpeed;

            float blendFactor = 1f - MathF.Exp(-6f * deltaTime);
            var newVel = currentVel + (targetVel - currentVel) * blendFactor;

            em.AddComponent(sparkEntity, new Velocity(newVel));
        }
    }
}
