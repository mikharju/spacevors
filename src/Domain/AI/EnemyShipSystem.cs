using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

public class EnemyShipSystem : GameSystem
{
    private const float DriftCancelForwardThrust = 18f;
    private const float DriftCancelSideThrust = 14f;
    private const float DriftCancelBackThrust = 16f;

    // Positions the manoeuvres read from each other: who to keep a gap from, and who is fit enough to
    // hide behind. Rebuilt every tick from this tick's state and reused between ticks so the hot path
    // allocates nothing. Plain-chase hulls are deliberately absent from the first list.
    private readonly List<(Vector2 Position, float Radius)> _maneuveringHulls = new();
    private readonly List<(Vector2 Position, float Radius, float HpFraction)> _coverCandidates = new();

    public static float EffectiveSpeed(float baseSpeed, float playerSpeed) =>
        MathF.Min(MathF.Max(baseSpeed, SpawningStats.Chase.PlayerSpeedFactor * playerSpeed), SpawningStats.Chase.MaxSpeedMultiplier * baseSpeed);

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        bool hasPlayer = view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;

        Vector2 playerVel = Vector2.Zero;
        if (hasPlayer && view.TryGetComponent<Velocity>(playerEntity, out var playerVelComp))
            playerVel = playerVelComp.Value;
        float playerSpeed = playerVel.Magnitude;

        if (hasPlayer) CollectManoeuvres(view);

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
            float distSq = toPlayer.LengthSquared;

            if (distSq > SpawningStats.Chase.CullDistance * SpawningStats.Chase.CullDistance)
            {
                commands.Add(new DestroyEntityCommand(shipEntity));
                continue;
            }

            // An ECM-jammed hull: no thrust, no turn toward the player, a slow turn to a heading picked
            // when the pulse fired. Velocity is left alone, so the hull coasts on the momentum it already
            // had — that is what reads as a ship that has lost control rather than one that has stopped.
            // A jam outranks every manoeuvre, which is why this branch comes first.
            if (view.TryGetComponent<EcmDisabled>(shipEntity, out var jam))
            {
                ApplyJamWander(shipEntity, jam.WanderHeading, currentRot.Angle, deltaTime, commands);
                continue;
            }

            if (distSq < 0.01f)
            {
                commands.Add(new AddComponentCommand<Acceleration>(shipEntity, new Acceleration(Vector2.Zero)));
                continue;
            }

            view.TryGetComponent<Velocity>(shipEntity, out var vel);
            var currentVel = vel.Value;
            float speed = currentVel.Magnitude;

            float speedCap = EffectiveSpeed(ship.Speed, playerSpeed);
            bool inDriftCancel = speed > speedCap;

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
            TurnToward(shipEntity, ship, Angle.Of(toPlayer), currentRot.Angle, deltaTime, commands);

            var plan = PlanFor(view, ship, shipEntity, shipPos.Value, playerPos.Value, playerVel, dist, speedCap);
            Vector2 thrust = plan.ShouldHold(shipPos.Value)
                ? Vector2.Zero
                : (plan.TargetPoint - shipPos.Value).Normalized * ship.Acceleration;

            if (ship.Behavior != EnemyBehavior.Chase)
                thrust = ClampToAcceleration(thrust + EnemySteering.SeparationThrust(shipPos.Value, ship.Radius, _maneuveringHulls, ship.Acceleration), ship.Acceleration);

            if (thrust.LengthSquared < 0.0001f)
            {
                commands.Add(new AddComponentCommand<Acceleration>(shipEntity, new Acceleration(Vector2.Zero)));
                continue;
            }

            commands.Add(new AddComponentCommand<Acceleration>(shipEntity, new Acceleration(thrust)));

            if (view.TryGetComponent<Acceleration>(shipEntity, out var accel))
            {
                var newVel = currentVel + accel.Value * deltaTime;

                if (newVel.Magnitude > speedCap)
                {
                    newVel = newVel / newVel.Magnitude * speedCap;
                }

                commands.Add(new AddComponentCommand<Velocity>(shipEntity, new Velocity(newVel)));
            }
        }
    }

    // One pass to collect the state the manoeuvres share. Read-only: nothing here mutates a hull, and no
    // rng is spent, so the fleet a seed produces is the same fleet every run (plans/DIFFICULTY_SCALING.md
    // R2.11, plans/ENEMY_BEHAVIORS.md). The cover list is only built when a hull could actually use it.
    private void CollectManoeuvres(WorldView view)
    {
        _maneuveringHulls.Clear();
        _coverCandidates.Clear();
        bool anyCoverTuck = false;

        foreach (var (entity, ship, pos) in view.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            if (view.HasComponent<Dead>(entity)) continue;
            if (ship.Behavior == EnemyBehavior.Chase) continue;

            _maneuveringHulls.Add((pos.Value, ship.Radius));
            if (ship.Behavior == EnemyBehavior.CoverTuck) anyCoverTuck = true;
        }

        if (!anyCoverTuck) return;

        foreach (var (_, ship, pos, health) in view.GetEntitiesWithComponents<EnemyShip, Position, Health>())
        {
            if (health.Max <= 0) continue;
            float fraction = (float)health.Current / health.Max;
            if (fraction < SpawningStats.Manoeuvre.HealthyHpFraction) continue;
            _coverCandidates.Add((pos.Value, ship.Radius, fraction));
        }
    }

    // The point this hull is heading for. A crippled tucker wants the far side of the biggest fit hull
    // it can reach; with no such hull in range it falls back to the plain chase, which already parks it
    // at the outer edge of its own ring. That is the whole "run away" question answered: a crippled hull
    // with nowhere to hide keeps fighting instead of fleeing the field.
    private SteerPlan PlanFor(WorldView view, EnemyShip ship, Entity shipEntity, Vector2 position, Vector2 playerPos, Vector2 playerVelocity, float distance, float speedCap)
    {
        if (ship.Behavior == EnemyBehavior.CoverTuck
            && IsCrippled(view, shipEntity)
            && EnemySteering.TryFindCover(position, playerPos, _coverCandidates, out var coverPosition, out float coverRadius))
            return EnemySteering.TuckPlan(ship, coverPosition, coverRadius, playerPos);

        return EnemySteering.Plan(ship, position, playerPos, playerVelocity, distance, speedCap);
    }

    private static bool IsCrippled(WorldView view, Entity entity) =>
        view.TryGetComponent<Health>(entity, out var health)
        && health.Max > 0
        && (float)health.Current / health.Max <= SpawningStats.Manoeuvre.CrippleHpFraction;

    // Separation is added to the heading thrust, so the pair has to stay inside the hull's own
    // acceleration: a hull cannot push harder than its engines because two friends are close.
    private static Vector2 ClampToAcceleration(Vector2 thrust, float limit)
    {
        float magnitude = thrust.Magnitude;
        if (magnitude <= limit || magnitude < 0.0001f) return thrust;
        return thrust / magnitude * limit;
    }

    // The one turn: rotate toward the player at the hull's turn rate. Both the normal path and the
    // over-speed path need it; they used to carry two copies that had already started to drift apart.
    private static void TurnToward(Entity shipEntity, EnemyShip ship, float targetAngle, float currentAngle, float deltaTime, CommandBuffer commands)
    {
        float angleDiff = NormalizeAngle(targetAngle - currentAngle);
        if (float.IsNaN(angleDiff)) return;

        float maxTurn = ship.TurnRate * deltaTime;
        float newAngle = MathF.Abs(angleDiff) < maxTurn ? targetAngle : currentAngle + MathF.Sign(angleDiff) * maxTurn;
        commands.Add(new AddComponentCommand<Rotation>(shipEntity, new Rotation(newAngle)));
    }

    // The jammed hull's slow turn. It writes AngularVelocity so `PhysicsSystem` integrates the heading the
    // way it does for every other hull, and lands on the heading instead of drifting past it: the turn is
    // re-aimed every tick, so arrival stops the spin with nothing left to bleed off.
    private static void ApplyJamWander(
        Entity shipEntity, float targetHeading, float currentAngle, float deltaTime, CommandBuffer commands)
    {
        commands.Add(new AddComponentCommand<Acceleration>(shipEntity, new Acceleration(Vector2.Zero)));

        float angleDiff = NormalizeAngle(targetHeading - currentAngle);
        if (float.IsNaN(angleDiff)) return;

        float maxTurn = EcmStats.WanderTurnRate * deltaTime;
        if (Math.Abs(angleDiff) <= maxTurn)
        {
            commands.Add(new AddComponentCommand<Rotation>(shipEntity, new Rotation(targetHeading)));
            commands.Add(new AddComponentCommand<AngularVelocity>(shipEntity, new AngularVelocity(0f)));
            return;
        }

        commands.Add(new AddComponentCommand<AngularVelocity>(shipEntity, new AngularVelocity(MathF.Sign(angleDiff) * EcmStats.WanderTurnRate)));
    }

    // Wraps an angle difference into [-π, π]. Not Angle.Delta, whose (-π, π] wrap flips the sign of an
    // exactly-opposite heading: at π the hull turns toward the decreasing angle, which is what
    // EnemyShipChaseTest pins and what the old inline turn code did.
    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= 2f * MathF.PI;
        while (angle < -MathF.PI) angle += 2f * MathF.PI;
        return angle;
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
        if (toPlayer.LengthSquared < 0.01f) return;

        TurnToward(shipEntity, ship, Angle.Of(toPlayer), currentAngle, deltaTime, commands);
    }

    private static void ApplyDriftCancel(
        WorldView view, Entity shipEntity, EnemyShip ship, float facingAngle,
        Vector2 currentVel, float deltaTime, CommandBuffer commands)
    {
        var forwardDir = Angle.Direction(facingAngle);
        var sideDir = new Vector2((float)Math.Cos(facingAngle), (float)Math.Sin(facingAngle));

        float forwardSpeed = Vector2.Dot(currentVel, forwardDir);
        float sideSpeed = Vector2.Dot(currentVel, sideDir);

        float forwardAccelMag = forwardSpeed > 0f ? -DriftCancelBackThrust : DriftCancelForwardThrust;
        float sideAccelMag = -DriftCancelSideThrust * MathF.Sign(sideSpeed);

        Vector2 cancelAccel = forwardDir * forwardAccelMag + sideDir * sideAccelMag;
        commands.Add(new AddComponentCommand<Acceleration>(shipEntity, new Acceleration(cancelAccel)));
    }
}
