using System.Diagnostics;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

// Steers every live missile toward its lock by rotating its velocity, and lets it build to the speed the
// gun gave it as a ceiling — a round leaves its rack slow and gets there, which is what the growing
// exhaust is drawn from.
//
// It runs first in the Intent phase on purpose: the phase writes next-tick motion, so
// PositionIntegrationSystem advances the round by the heading decided here, and the next tick's
// collision sweep reads exactly the velocity that produced that movement. Writing guidance any
// earlier would turn the heading before the round has moved and desynchronise the swept segment.
//
// A guided round still never gets an `Acceleration` component, so the collision pass's assumption that a
// round's velocity is constant between ticks stays true — the speed is rewritten at the tick boundary
// instead, in both places that hold it (see the three commands below).
public class MissileGuidanceSystem : GameSystem
{
    // Below this the round is treated as standing still: nothing to rotate.
    private const float Epsilon = 0.001f;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var sw = Stopwatch.StartNew();
        int steered = 0;

        foreach (var (entity, missile, pos, vel, ammo) in view.GetEntitiesWithComponents<Missile, Position, Velocity, Ammo>())
        {
            if (vel.Value.Magnitude < Epsilon) continue;

            // The round's own flight speed is the authority, not the velocity magnitude: the hull that
            // threw it adds its own motion at launch, and from here on the missile flies at its own speed.
            float speed = MathF.Min(missile.Speed + missile.Acceleration * deltaTime, missile.MaxSpeed);

            // A lock that died is simply gone: its components are, and entity ids are never reused.
            // The round then flies on and lets its fuse end it — no re-acquisition.
            Vector2 desired = view.TryGetComponent<Position>(missile.LockedTarget, out var targetPos)
                ? targetPos.Value - pos.Value
                : vel.Value;
            if (desired.Magnitude < Epsilon) continue;

            float heading = Angle.Of(vel.Value);
            float maxTurn = missile.TurnRate * deltaTime;
            float turned = Angle.Normalize(Angle.RotateToward(heading, Angle.Of(desired), maxTurn));

            // 1 = swinging as hard as it can, 0 = already dead on the lock. The exhaust is drawn from
            // this, so it tells the player which way the round is committed to going.
            float steering = maxTurn > Epsilon ? MathF.Abs(Angle.Delta(heading, turned)) / maxTurn : 0f;
            if (turned == heading && steering == missile.Steering && speed == missile.Speed) continue;

            var newVel = new Vector2(MathF.Sin(turned), -MathF.Cos(turned)) * speed;
            commands.Add(new AddComponentCommand<Velocity>(entity, new Velocity(newVel)));
            commands.Add(new AddComponentCommand<Ammo>(entity, ammo with { Velocity = newVel }));
            commands.Add(new AddComponentCommand<Missile>(entity, missile with { Speed = speed, Steering = steering }));
            steered++;
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Missiles: guidance", sw.ElapsedTicks, steered);
    }
}
