using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class EnemyShipSystem : GameSystem
{
    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        foreach (var (shipEntity, ship, shipPos, currentRot) in view.GetEntitiesWithComponents<EnemyShip, Position, Rotation>())
        {
            if (!hasPlayer) continue;

            var playerPos = playerTuple.Value2;

            var toPlayer = playerPos.Value - shipPos.Value;
            float distSq = toPlayer.X * toPlayer.X + toPlayer.Y * toPlayer.Y;

            if (distSq > ship.DetectionRange * ship.DetectionRange || distSq < 0.01f) continue;

            view.TryGetComponent<AngularVelocity>(shipEntity, out var angVel);
            float currentAngVel = angVel.Value;

            if (Math.Abs(currentAngVel) > ship.TurnRate)
            {
                float damping = Math.Sign(currentAngVel) * ship.TurnRate * deltaTime;
                float newAngVel = currentAngVel - damping;
                if (Math.Sign(newAngVel) != Math.Sign(currentAngVel))
                {
                    newAngVel = 0f;
                }
                commands.Add(new AddComponentCommand<AngularVelocity>(shipEntity, new AngularVelocity(newAngVel)));
                continue;
            }

            float dist = (float)Math.Sqrt(distSq);
            var toPlayerDir = toPlayer / dist;

            float targetAngle = (float)Math.Atan2(toPlayerDir.X, -toPlayerDir.Y);
            float angleDiff = NormalizeAngle(targetAngle - currentRot.Angle);

            if (float.IsNaN(angleDiff)) continue;

            float maxTurn = ship.TurnRate * deltaTime;
            if (Math.Abs(angleDiff) < maxTurn)
            {
                commands.Add(new AddComponentCommand<Rotation>(shipEntity, new Rotation(targetAngle)));
            }
            else
            {
                commands.Add(new AddComponentCommand<Rotation>(shipEntity, new Rotation(currentRot.Angle + Math.Sign(angleDiff) * maxTurn)));
            }

            if (dist <= ship.FiringRange) continue;

            view.TryGetComponent<Velocity>(shipEntity, out var vel);
            var currentVel = vel.Value;

            var targetAccel = toPlayerDir * ship.Acceleration;
            commands.Add(new AddComponentCommand<Acceleration>(shipEntity, new Acceleration(targetAccel)));

            if (view.TryGetComponent<Acceleration>(shipEntity, out var accel))
            {
                var newVel = currentVel + accel.Value * deltaTime;

                if (newVel.Magnitude > ship.Speed)
                {
                    newVel = newVel / newVel.Magnitude * ship.Speed;
                }

                commands.Add(new AddComponentCommand<Velocity>(shipEntity, new Velocity(newVel)));
            }
        }
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= 2f * MathF.PI;
        while (angle < -MathF.PI) angle += 2f * MathF.PI;
        return angle;
    }
}
