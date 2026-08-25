using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class EnemyShipSystem : GameSystem
{
    private const float DriftCancelForwardThrust = 18f;
    private const float DriftCancelSideThrust = 14f;
    private const float DriftCancelBackThrust = 16f;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        bool hasPlayer = view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;

        foreach (var (shipEntity, ship, shipPos, currentRot) in view.GetEntitiesWithComponents<EnemyShip, Position, Rotation>())
        {
            if (view.TryGetComponent<Dead>(shipEntity, out _)) continue;
            if (!hasPlayer)
            {
                commands.Add(new AddComponentCommand<Acceleration>(shipEntity, new Acceleration(Vector2.Zero)));
                continue;
            }

            var playerPos = playerTuple.Value2;

            var toPlayer = playerPos.Value - shipPos.Value;
            float distSq = toPlayer.X * toPlayer.X + toPlayer.Y * toPlayer.Y;
            if (distSq < 0.01f)
            {
                commands.Add(new AddComponentCommand<Acceleration>(shipEntity, new Acceleration(Vector2.Zero)));
                continue;
            }

            view.TryGetComponent<Velocity>(shipEntity, out var vel);
            var currentVel = vel.Value;
            float speed = currentVel.Magnitude;

            bool inDriftCancel = speed > ship.Speed;

            if (inDriftCancel)
            {
                ApplySpinStop(view, shipEntity, ship, deltaTime, commands);
                ApplyRotationTowardPlayer(view, shipEntity, ship, playerPos.Value, currentRot.Angle, deltaTime, commands);
                ApplyDriftCancel(view, shipEntity, ship, currentRot.Angle, currentVel, deltaTime, commands);
                continue;
            }

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
                commands.Add(new AddComponentCommand<Acceleration>(shipEntity, new Acceleration(Vector2.Zero)));
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

            if (dist <= ship.FiringRange)
            {
                commands.Add(new AddComponentCommand<Acceleration>(shipEntity, new Acceleration(Vector2.Zero)));
                continue;
            }

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

    private static void ApplySpinStop(
        WorldView view, Entity shipEntity, EnemyShip ship, float deltaTime, CommandBuffer commands)
    {
        if (view.TryGetComponent<AngularVelocity>(shipEntity, out var angVel))
        {
            float currentAngVel = angVel.Value;
            if (Math.Abs(currentAngVel) > ship.TurnRate)
            {
                float damping = Math.Sign(currentAngVel) * ship.TurnRate * deltaTime;
                float newAngVel = currentAngVel - damping;
                if (Math.Sign(newAngVel) != Math.Sign(currentAngVel))
                    newAngVel = 0f;
                commands.Add(new AddComponentCommand<AngularVelocity>(shipEntity, new AngularVelocity(newAngVel)));
            }
        }
    }

    private static void ApplyRotationTowardPlayer(
        WorldView view, Entity shipEntity, EnemyShip ship, Vector2 playerPos,
        float currentAngle, float deltaTime, CommandBuffer commands)
    {
        var toPlayer = playerPos - view.GetComponent<Position>(shipEntity).Value;
        float distSq = toPlayer.X * toPlayer.X + toPlayer.Y * toPlayer.Y;
        if (distSq < 0.01f) return;

        float dist = (float)Math.Sqrt(distSq);
        var toPlayerDir = toPlayer / dist;
        float targetAngle = (float)Math.Atan2(toPlayerDir.X, -toPlayerDir.Y);
        float angleDiff = NormalizeAngle(targetAngle - currentAngle);

        if (!float.IsNaN(angleDiff))
        {
            float maxTurn = ship.TurnRate * deltaTime;
            float newAngle = Math.Abs(angleDiff) < maxTurn
                ? targetAngle
                : currentAngle + MathF.Sign(angleDiff) * maxTurn;
            commands.Add(new AddComponentCommand<Rotation>(shipEntity, new Rotation(newAngle)));
        }
    }

    private static void ApplyDriftCancel(
        WorldView view, Entity shipEntity, EnemyShip ship, float facingAngle,
        Vector2 currentVel, float deltaTime, CommandBuffer commands)
    {
        float forwardX = (float)Math.Sin(facingAngle);
        float forwardY = -(float)Math.Cos(facingAngle);
        var forwardDir = new Vector2(forwardX, forwardY);

        float sideX = (float)Math.Cos(facingAngle);
        float sideY = (float)Math.Sin(facingAngle);
        var sideDir = new Vector2(sideX, sideY);

        float forwardSpeed = Vector2.Dot(currentVel, forwardDir);
        float sideSpeed = Vector2.Dot(currentVel, sideDir);

        float forwardAccelMag;
        if (forwardSpeed > 0f)
            forwardAccelMag = -DriftCancelBackThrust;
        else
            forwardAccelMag = DriftCancelForwardThrust;

        float sideAccelMag = -DriftCancelSideThrust * MathF.Sign(sideSpeed);

        Vector2 cancelAccel = forwardDir * forwardAccelMag + sideDir * sideAccelMag;
        commands.Add(new AddComponentCommand<Acceleration>(shipEntity, new Acceleration(cancelAccel)));
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= 2f * MathF.PI;
        while (angle < -MathF.PI) angle += 2f * MathF.PI;
        return angle;
    }
}
