namespace Spacevors.Domain.Systems;

// The arc gate the chase AI is built around: a hull engages when any part of the target's hull is
// inside its firing cone, so a wide target at the cone's edge is not exempt. The gun pass and the
// missile pass both read it — two hand-rolled gates would drift away from the one the AI brakes on.
public static class FiringArc
{
    public static bool Engulfs(Vector2 forwardDir, Vector2 toCenterDir, float halfArc, float targetRadius, float distance)
    {
        float angularMargin = MathF.Asin(Math.Clamp(targetRadius / distance, -1f, 1f));
        return Vector2.Dot(forwardDir, toCenterDir) >= MathF.Cos(halfArc + angularMargin);
    }
}
