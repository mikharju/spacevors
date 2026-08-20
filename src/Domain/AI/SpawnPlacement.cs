namespace Spacevors.Domain.Systems;

public static class SpawnPlacement
{
    public const float ScreenMargin = 60f;
    public const float DriftSpeed = 40f;
    public const float MinDirectionalSpeed = 0.1f;
    private const float ForwardConeHalfAngle = MathF.PI / 4f;

    // Point just outside the screen rectangle (centered on `center`) along unit vector `direction`.
    public static Vector2 OutsideScreen(Vector2 center, Vector2 viewportSize, Vector2 direction)
    {
        var half = viewportSize * 0.5f + new Vector2(ScreenMargin, ScreenMargin);
        float tx = half.X / Math.Abs(direction.X);
        float ty = half.Y / Math.Abs(direction.Y);
        return center + direction * Math.Min(tx, ty);
    }

    // Random unit vector within +/-45 degrees of `velocityDir`.
    public static Vector2 ForwardDirection(Vector2 velocityDir, Random rng)
    {
        float angle = (float)(rng.NextDouble() * 2f * ForwardConeHalfAngle - ForwardConeHalfAngle);
        return Rotate(velocityDir, angle);
    }

    // Random unit vector in any direction.
    public static Vector2 AnyDirection(Random rng)
    {
        float angle = (float)(rng.NextDouble() * Math.PI * 2f);
        return new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
    }

    // Game angle convention: 0 = up, positive clockwise.
    public static float AngleFromTo(Vector2 from, Vector2 to) =>
        (float)Math.Atan2(to.X - from.X, -(to.Y - from.Y));

    private static Vector2 Rotate(Vector2 v, float angle)
    {
        float cosA = (float)Math.Cos(angle);
        float sinA = (float)Math.Sin(angle);
        return new Vector2(
            v.X * cosA - v.Y * sinA,
            v.X * sinA + v.Y * cosA);
    }
}
