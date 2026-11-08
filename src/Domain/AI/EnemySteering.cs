using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// Where one hull is trying to get this tick, and how close it has to get before it stops thrusting
// (plans/ENEMY_BEHAVIORS.md).
//
// Facing is deliberately not part of a plan: every manoeuvre faces the player, because an enemy's gun is
// its hull and a hull that turns away stops shooting. That is what makes a manoeuvre cost something
// without needing a rule to say so — a hull busy circling or tucking still holds its firing line, so it
// buys nothing on the cheap.
public readonly record struct SteerPlan(Vector2 TargetPoint, float HoldDistance)
{
    // A negative hold distance never holds: a charge has no point it is willing to stop at.
    public bool ShouldHold(Vector2 position) =>
        HoldDistance >= 0f && (TargetPoint - position).LengthSquared <= HoldDistance * HoldDistance;
}

// The manoeuvres, each one a pure function from state to a point to head for. Every point lies on or
// inside the hull's own firing ring: no manoeuvre changes a range, so the reach ceiling the difficulty
// tests assert (D1) keeps describing the game the AI actually plays.
public static class EnemySteering
{
    public static SteerPlan Plan(EnemyShip ship, Vector2 position, Vector2 playerPos, Vector2 playerVelocity, float distance, float speedCap)
    {
        return ship.Behavior switch
        {
            EnemyBehavior.Spread => SpreadPlan(ship, playerPos),
            EnemyBehavior.Strafe => new SteerPlan(RingPoint(position, playerPos, SpawningStats.Manoeuvre.StrafeLeadAngle, ship.FiringRange), -1f),
            EnemyBehavior.CutOff => CutOffPlan(ship, distance, playerPos, playerVelocity, speedCap),
            EnemyBehavior.Rammer => new SteerPlan(playerPos, -1f),
            _ => ChasePlan(ship, playerPos)
        };
    }

    // Today's chase, kept as one row of this switch rather than as the shape of the whole system: brake
    // inside the firing ring, close on the player outside it.
    private static SteerPlan ChasePlan(EnemyShip ship, Vector2 playerPos) => new(playerPos, ship.FiringRange);

    // Hold the bearing the hull spawned on, at its own ring, instead of joining the chase line
    // (plans/ENEMY_BEHAVIORS.md B1). Without a recorded bearing — test worlds, seeded scenarios — a
    // spread hull is simply a chasing hull.
    private static SteerPlan SpreadPlan(EnemyShip ship, Vector2 playerPos)
    {
        if (ship.HoldBearing.LengthSquared < 0.01f) return ChasePlan(ship, playerPos);
        return new SteerPlan(playerPos + ship.HoldBearing * ship.FiringRange, SpawningStats.Manoeuvre.HoldTolerance);
    }

    // Aim where the player is **going**, not where they are (B4). This is the cheap half of closing the
    // ~290 px/s escape valve: a hull 700 px back solves the meeting point instead of trailing the
    // player's exhaust, and it needs no new clock, no rng and no state.
    private static SteerPlan CutOffPlan(EnemyShip ship, float distance, Vector2 playerPos, Vector2 playerVelocity, float speedCap)
    {
        float leadTime = MathF.Min(distance / MathF.Max(speedCap, 1f), SpawningStats.Manoeuvre.MaxCutOffLeadTime);
        return new SteerPlan(playerPos + playerVelocity * leadTime, ship.FiringRange);
    }

    // Where a crippled hull wants to be: just behind the cover hull, on the far side from the player, so
    // the shield's own body is the thing the player's rounds spend themselves on (B5).
    public static SteerPlan TuckPlan(EnemyShip ship, Vector2 coverPosition, float coverRadius, Vector2 playerPos)
    {
        var awayFromPlayer = coverPosition - playerPos;
        float distance = awayFromPlayer.Magnitude;
        if (distance < 0.01f) return new SteerPlan(playerPos, ship.FiringRange);

        var tuckPoint = coverPosition + awayFromPlayer / distance * (coverRadius + ship.Radius + SpawningStats.Manoeuvre.CoverGap);
        return new SteerPlan(tuckPoint, SpawningStats.Manoeuvre.HoldTolerance);
    }

    // The biggest hull that still has most of its hp and sits within the cover cone of this hull's own
    // bearing, as seen from the player. The cone is what keeps a tuck from routing halfway across the
    // field to reach a friend on the far side; picking the biggest is what makes the shield worth hiding
    // behind. Tied bearings are resolved by the list's order, which is stable for a given world.
    public static bool TryFindCover(
        Vector2 position, Vector2 playerPos,
        List<(Vector2 Position, float Radius, float HpFraction)> candidates,
        out Vector2 coverPosition, out float coverRadius)
    {
        coverPosition = Vector2.Zero;
        coverRadius = 0f;

        Vector2 ownBearing = position - playerPos;
        if (ownBearing.LengthSquared < 0.01f) return false;
        float ownAngle = Angle.Of(ownBearing);

        foreach (var (candidatePos, candidateRadius, hpFraction) in candidates)
        {
            if (hpFraction < SpawningStats.Manoeuvre.HealthyHpFraction) continue;

            Vector2 relative = candidatePos - playerPos;
            if (relative.LengthSquared < 0.01f) continue;
            if (MathF.Abs(Angle.Delta(Angle.Of(relative), ownAngle)) > SpawningStats.Manoeuvre.CoverCone) continue;

            if (candidateRadius > coverRadius)
            {
                coverRadius = candidateRadius;
                coverPosition = candidatePos;
            }
        }

        return coverRadius > 0f;
    }

    // Behaving hulls keep a gap from each other so the fleet reads as a formation instead of a knot (B2).
    // Plain-chase hulls are not in the list and never push: the swarm's tight ball is what the player's
    // area guns are paid for, so it is left alone on purpose.
    public static Vector2 SeparationThrust(
        Vector2 position, float radius, List<(Vector2 Position, float Radius)> others, float acceleration)
    {
        Vector2 push = Vector2.Zero;
        int crowded = 0;

        foreach (var (otherPos, otherRadius) in others)
        {
            var away = position - otherPos;
            float gap = radius + otherRadius + SpawningStats.Manoeuvre.SeparationGap;
            float distanceSq = away.LengthSquared;
            if (distanceSq < 0.01f || distanceSq > gap * gap) continue;

            push += away / (float)Math.Sqrt(distanceSq);
            crowded++;
        }

        if (crowded == 0) return Vector2.Zero;
        return push / crowded * (acceleration * SpawningStats.Manoeuvre.SeparationThrustFraction);
    }

    private static Vector2 RingPoint(Vector2 position, Vector2 playerPos, float leadAngle, float ringRadius)
    {
        var radial = position - playerPos;
        if (radial.LengthSquared < 0.01f) return playerPos + radial.Normalized * ringRadius;
        return playerPos + SpawnPlacement.Rotate(radial.Normalized, leadAngle) * ringRadius;
    }
}
