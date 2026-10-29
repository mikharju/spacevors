using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// What a collision has to report before anything can be resolved: which way to push, how far the
// bodies are already inside each other, and where the touch happened. Circles hand all three over
// from their centres; anything else has to work them out, so they all cross one seam here
// (plans/NON_CIRCLE_COLLISION.md).
//
// `Normal` points from body A toward body B, so an impulse along it separates them. `Point` is
// where the touch happened: A's surface for a static pair, the mover's path position for a swept
// one. `HasNormal` is false when the geometry is degenerate — a swept path ran through A's centre —
// which means there is a hit but nothing to push along.
public readonly record struct Contact(Vector2 Normal, float Depth, Vector2 Point)
{
    public bool HasNormal => Normal.X != 0f || Normal.Y != 0f;
}

// The narrow phase: given two bodies, is there an overlap, and what shape is it?
//
// Only circle↔circle is implemented. The point of routing every overlap through here is that
// resolution then never derives its geometry from centres, so primitives that are not circles can
// be added behind it without touching impulse, friction or impact damage again.
public static class NarrowPhase
{
    // Static pairs closer than this are coincident: no reliable normal, and pushing them would
    // fling them apart. Compared against squared distance, as the callers always did.
    public const float CoincidentDistanceSq = 0.001f;

    // The same threshold read as a plain distance, for the swept case below.
    public const float CoincidentDistance = 0.001f;

    // Two static circles. Overlap must be real (not merely coincident) to count.
    public static bool TryCollide(
        Vector2 aPosition, float aRadius, Vector2 bPosition, float bRadius, out Contact contact)
    {
        contact = default;

        var diff = bPosition - aPosition;
        float distanceSq = Vector2.Dot(diff, diff);
        float radiusSum = aRadius + bRadius;
        if (distanceSq >= radiusSum * radiusSum || distanceSq < CoincidentDistanceSq) return false;

        float distance = MathF.Sqrt(distanceSq);
        var normal = diff / distance;
        contact = new Contact(normal, radiusSum - distance, aPosition + normal * aRadius);
        return true;
    }

    // A round body swept between two ticks: the segment [from, to] inflated by `movingRadius`,
    // against a static circle. `t` is where along the path the touch happened, which is how hits
    // are ordered by travel.
    //
    // Unlike TryCollide this still reports a hit when the path runs through the target's centre:
    // the round is spent either way, there is just no direction to push in.
    public static bool TrySweep(
        Vector2 from, Vector2 to, float movingRadius,
        Vector2 targetPosition, float targetRadius, out Contact contact, out float t)
    {
        contact = default;

        Vector2 touch = Vector2.ClosestPointOnSegment(from, to, targetPosition, out t);
        var diff = touch - targetPosition;
        float distanceSq = Vector2.Dot(diff, diff);
        float radiusSum = movingRadius + targetRadius;
        if (distanceSq >= radiusSum * radiusSum) return false;

        float distance = MathF.Sqrt(distanceSq);
        var normal = distance < CoincidentDistance ? Vector2.Zero : diff / distance;
        contact = new Contact(normal, radiusSum - distance, touch);
        return true;
    }

    // A body as the narrow phase sees it: where it is, which way it faces, what it is made of, and how
    // big it is. `Scale` is the hull's collision radius — the same bounding radius the broad phase
    // indexed it by, which is why a shape must never reach past it.
    public readonly record struct CollisionBody(Vector2 Position, float Angle, HullShape Shape, float Scale)
    {
        public static CollisionBody Circle(Vector2 position, float radius) => new(position, 0f, HullShape.Circle, radius);
    }

    // Two bodies of any shape. Circles keep the single-test fast path: rocks, mines, pickups and every
    // round in flight are circles, and they are the bodies there are thousands of.
    //
    // For the rest the *deepest* primitive contact wins, so a body pair still gets one contact per tick
    // — the same shape the impulse, the position correction and the impact-damage accumulator assume.
    public static bool TryCollide(CollisionBody a, CollisionBody b, out Contact contact)
    {
        contact = default;
        if (a.Shape.IsCircle && b.Shape.IsCircle) return TryCollide(a.Position, a.Scale, b.Position, b.Scale, out contact);

        bool found = false;
        foreach (var first in a.Shape.Capsules)
        {
            Capsule firstInWorld = InWorld(first, a);
            foreach (var second in b.Shape.Capsules)
            {
                if (!CapsuleGeometry.TryCollide(firstInWorld, InWorld(second, b), out Contact hit)) continue;
                if (found && hit.Depth <= contact.Depth) continue;
                contact = hit;
                found = true;
            }
        }

        return found;
    }

    // A swept round body against a body of any shape. The *earliest* touch along the path is what the
    // ammo pass needs, since it picks the first thing a round met rather than the deepest.
    public static bool TrySweep(
        Vector2 from, Vector2 to, float movingRadius, CollisionBody target, out Contact contact, out float t)
    {
        if (target.Shape.IsCircle) return TrySweep(from, to, movingRadius, target.Position, target.Scale, out contact, out t);

        contact = default;
        t = 0f;
        bool found = false;
        foreach (var primitive in target.Shape.Capsules)
        {
            if (!CapsuleGeometry.TrySweep(from, to, movingRadius, InWorld(primitive, target), out Contact hit, out float along)) continue;
            if (found && along >= t) continue;
            contact = hit;
            t = along;
            found = true;
        }

        return found;
    }

    // Shape units to world: turn by the body's heading, scale by its radius, move to its position.
    static Capsule InWorld(Capsule local, CollisionBody body)
    {
        float cos = MathF.Cos(body.Angle);
        float sin = MathF.Sin(body.Angle);
        return new Capsule(
            InWorld(local.From, cos, sin, body),
            InWorld(local.To, cos, sin, body),
            local.HalfThickness * body.Scale);
    }

    static Vector2 InWorld(Vector2 local, float cos, float sin, CollisionBody body) =>
        body.Position + new Vector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos) * body.Scale;
}
