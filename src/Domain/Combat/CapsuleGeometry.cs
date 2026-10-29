using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// Closest-point math for capsules (plans/NON_CIRCLE_COLLISION.md).
//
// A capsule is a segment inflated by a disc, so *every* pair of round primitives collapses to one
// of three questions: point to point, point to segment, segment to segment. That collapse is the
// reason hulls are built out of these rather than out of polygons: the contact normal, the
// penetration depth and the touch point all fall out of the closest points, uniquely, and because
// the ends are round there is no "which feature touched which" case to specialise — the case that
// makes polygon pairs long and jittery.
public static class CapsuleGeometry
{
    // Below this a segment has no direction of its own (it is a disc).
    const float DegenerateLengthSq = 1e-6f;

    // Closest points of segments [aFrom,aTo] and [bFrom,bTo], and where they sit along each segment
    // (s on the first, t on the second, both clamped onto it). A zero-length segment — a disc — falls
    // out of the same math.
    public static void ClosestPoints(
        Vector2 aFrom, Vector2 aTo, Vector2 bFrom, Vector2 bTo,
        out Vector2 onA, out Vector2 onB, out float s, out float t)
    {
        var first = aTo - aFrom;
        var second = bTo - bFrom;
        var apart = aFrom - bFrom;
        float firstSq = Vector2.Dot(first, first);
        float secondSq = Vector2.Dot(second, second);

        if (firstSq <= DegenerateLengthSq && secondSq <= DegenerateLengthSq)
        {
            s = 0f;
            t = 0f;
            onA = aFrom;
            onB = bFrom;
            return;
        }

        float ontoSecond = Vector2.Dot(second, apart);
        if (firstSq <= DegenerateLengthSq)
        {
            s = 0f;
            t = Math.Clamp(ontoSecond / secondSq, 0f, 1f);
            onA = aFrom;
            onB = bFrom + second * t;
            return;
        }

        float ontoFirst = Vector2.Dot(first, apart);
        if (secondSq <= DegenerateLengthSq)
        {
            t = 0f;
            s = Math.Clamp(-ontoFirst / firstSq, 0f, 1f);
            onA = aFrom + first * s;
            onB = bFrom;
            return;
        }

        float across = Vector2.Dot(first, second);
        float denominator = firstSq * secondSq - across * across;

        if (denominator > DegenerateLengthSq)
        {
            s = Math.Clamp((across * ontoSecond - ontoFirst * secondSq) / denominator, 0f, 1f);
            t = (across * s + ontoSecond) / secondSq;

            // The unconstrained answer can leave either segment; pin it to an end and re-solve the other.
            if (t < 0f)
            {
                t = 0f;
                s = Math.Clamp(-ontoFirst / firstSq, 0f, 1f);
            }
            else if (t > 1f)
            {
                t = 1f;
                s = Math.Clamp((across - ontoFirst) / firstSq, 0f, 1f);
            }
        }
        else
        {
            // Parallel: a whole band of pairs is equally close, and reading one off the parameters
            // would report the distance between mismatched ends — too far, and a real overlap would
            // read as a miss. The two cross-sections at the ends of the first segment are the
            // candidates, and the nearer is the true one.
            var crossSection = NearerCrossSection(apart, first, second, ontoSecond, across, secondSq);
            s = crossSection.S;
            t = crossSection.T;
        }

        onA = aFrom + first * s;
        onB = bFrom + second * t;
    }

    private readonly record struct CrossSection(float S, float T);

    static CrossSection NearerCrossSection(
        Vector2 apart, Vector2 first, Vector2 second, float ontoSecond, float across, float secondSq)
    {
        float tAtStart = Math.Clamp(ontoSecond / secondSq, 0f, 1f);
        float tAtEnd = Math.Clamp((ontoSecond + across) / secondSq, 0f, 1f);

        var startPair = apart - second * tAtStart;
        var endPair = apart + first - second * tAtEnd;

        return Vector2.Dot(startPair, startPair) <= Vector2.Dot(endPair, endPair)
            ? new CrossSection(0f, tAtStart)
            : new CrossSection(1f, tAtEnd);
    }

    // Two capsules sharing a frame. They overlap when their axes come within the sum of their
    // thicknesses, and `Normal` points from A toward B.
    public static bool TryCollide(Capsule a, Capsule b, out Contact contact)
    {
        contact = default;

        ClosestPoints(a.From, a.To, b.From, b.To, out Vector2 onA, out Vector2 onB, out _, out _);

        var apart = onB - onA;
        float distanceSq = Vector2.Dot(apart, apart);
        float radiusSum = a.HalfThickness + b.HalfThickness;
        if (distanceSq >= radiusSum * radiusSum) return false;

        float distance = MathF.Sqrt(distanceSq);
        Vector2 normal;
        if (distance >= NarrowPhase.CoincidentDistance) normal = apart / distance;
        // The axes crossed: there is a deep overlap and no direction to read off it, so take one
        // deterministically — the sides of whichever primitive has a direction at all. Two discs on
        // the same spot are the one case with nothing to choose, and those stay coincident, exactly
        // as two coincident circles always have been.
        else if (!TrySideOf(a, b, out normal)) return false;

        contact = new Contact(normal, radiusSum - distance, onA + normal * a.HalfThickness);
        return true;
    }

    // A round body swept along [from,to], against a capsule. `t` is where along the path the touch
    // happened, which is how hits are ordered by travel. `Normal` points from the target toward the
    // mover's path, matching NarrowPhase.TrySweep.
    public static bool TrySweep(
        Vector2 from, Vector2 to, float movingRadius, Capsule capsule, out Contact contact, out float t)
    {
        contact = default;

        ClosestPoints(from, to, capsule.From, capsule.To, out Vector2 onPath, out Vector2 onAxis, out t, out _);

        var apart = onPath - onAxis;
        float distanceSq = Vector2.Dot(apart, apart);
        float radiusSum = movingRadius + capsule.HalfThickness;
        if (distanceSq >= radiusSum * radiusSum) return false;

        float distance = MathF.Sqrt(distanceSq);
        var normal = distance < NarrowPhase.CoincidentDistance ? Vector2.Zero : apart / distance;
        contact = new Contact(normal, radiusSum - distance, onPath);
        return true;
    }

    // A direction to separate along when the axes themselves crossed.
    static bool TrySideOf(Capsule a, Capsule b, out Vector2 side)
    {
        var axis = Vector2.Dot(a.To - a.From, a.To - a.From) >= DegenerateLengthSq ? a.To - a.From : b.To - b.From;
        float length = axis.Magnitude;
        if (length < MathF.Sqrt(DegenerateLengthSq))
        {
            side = Vector2.Zero;
            return false;
        }

        side = new Vector2(-axis.Y / length, axis.X / length);
        return true;
    }
}
