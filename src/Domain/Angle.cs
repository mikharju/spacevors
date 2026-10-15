namespace Spacevors.Domain;

// Angle helpers for the rotation convention used everywhere: an angle a faces (sin a, -cos a),
// so an angle grows toward the ship's right. Radians in, radians out.
public static class Angle
{
    public const float FullTurn = MathF.PI * 2f;

    public static float FromDeg(float degrees) => degrees * MathF.PI / 180f;
    public static float ToDeg(float radians) => radians * 180f / MathF.PI;

    // The angle a direction vector points at — the inverse of (sin a, -cos a).
    public static float Of(Vector2 direction) => MathF.Atan2(direction.X, -direction.Y);

    // Wrap into (-π, π] so a stored angle cannot drift unbounded.
    public static float Normalize(float radians)
    {
        while (radians > MathF.PI) radians -= FullTurn;
        while (radians <= -MathF.PI) radians += FullTurn;
        return radians;
    }

    // Signed shortest turn from one angle to the other.
    public static float Delta(float from, float to) => Normalize(to - from);

    // Move from `current` toward `target` by at most maxStep radians.
    public static float RotateToward(float current, float target, float maxStep)
    {
        float delta = Delta(current, target);
        if (delta > maxStep) return current + maxStep;
        if (delta < -maxStep) return current - maxStep;
        return target;
    }

    // Pull an angle back to within span/2 of center. A full-turn arc has no edges, so nothing is clamped.
    public static float ClampToArc(float angle, float center, float span)
    {
        if (span >= FullTurn) return angle;
        float limit = span * 0.5f;
        return Normalize(center + Math.Clamp(Delta(center, angle), -limit, limit));
    }
}
