using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class EnemyShipSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        var ships = em.GetEntitiesWithComponents<EnemyShip>().ToList();

        foreach (var (shipEntity, ship) in ships)
        {
            if (!hasPlayer) continue;

            var shipPos = em.GetComponent<Position>(shipEntity);
            var playerPos = em.GetComponent<Position>(playerEntity);

            var toPlayer = playerPos.Value - shipPos.Value;
            float distSq = toPlayer.X * toPlayer.X + toPlayer.Y * toPlayer.Y;

            if (distSq > ship.DetectionRange * ship.DetectionRange || distSq < 0.01f) continue;

            float dist = (float)Math.Sqrt(distSq);
            var toPlayerDir = toPlayer / dist;

            // Turn toward player
            float targetAngle = (float)Math.Atan2(toPlayerDir.X, -toPlayerDir.Y);
            var currentRot = em.GetComponent<Rotation>(shipEntity);
            float angleDiff = NormalizeAngle(targetAngle - currentRot.Angle);

            float maxTurn = ship.TurnRate * deltaTime;
            if (Math.Abs(angleDiff) < maxTurn)
            {
                em.AddComponent(shipEntity, new Rotation(targetAngle));
            }
            else
            {
                em.AddComponent(shipEntity, new Rotation(currentRot.Angle + Math.Sign(angleDiff) * maxTurn));
            }

            // Blend velocity toward player direction
            var currentVel = em.HasComponent<Velocity>(shipEntity)
                ? em.GetComponent<Velocity>(shipEntity).Value
                : Vector2.Zero;

            float targetSpeed = ship.Speed;
            var targetVel = toPlayerDir * targetSpeed;

            float blendFactor = 1f - MathF.Exp(-1.5f * deltaTime);
            var newVel = currentVel + (targetVel - currentVel) * blendFactor;

            em.AddComponent(shipEntity, new Velocity(newVel));
        }
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= 2f * MathF.PI;
        while (angle < -MathF.PI) angle += 2f * MathF.PI;
        return angle;
    }
}
