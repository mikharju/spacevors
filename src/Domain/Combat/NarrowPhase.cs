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
}
