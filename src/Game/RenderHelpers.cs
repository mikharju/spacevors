namespace Spacevors.Game;

internal static class RenderHelpers
{
    public static bool IsOffScreen(float cx, float cy, float extent, int windowWidth, int windowHeight)
    {
        return cx < -extent || cx > windowWidth + extent || cy < -extent || cy > windowHeight + extent;
    }

    // Farthest corner distance of a box drawn centered on its middle.
    public static float HalfDiagonal(float width, float height)
    {
        return 0.5f * MathF.Sqrt(width * width + height * height);
    }
}
