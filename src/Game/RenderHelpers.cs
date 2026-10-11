using Raylib_cs;
using Spacevors.Domain;

namespace Spacevors.Game;

internal static class RenderHelpers
{
    // World → screen position. The camera target is the centre of the window; 1 world unit = 1 px.
    public static (float X, float Y) ToScreen(Vector2 world, float camX, float camY, int windowWidth, int windowHeight)
    {
        return (world.X - camX + windowWidth / 2f, world.Y - camY + windowHeight / 2f);
    }

    public static bool IsOffScreen(float cx, float cy, float extent, int windowWidth, int windowHeight)
    {
        return cx < -extent || cx > windowWidth + extent || cy < -extent || cy > windowHeight + extent;
    }

    // Farthest corner distance of a box drawn centered on its middle.
    public static float HalfDiagonal(float width, float height)
    {
        return 0.5f * MathF.Sqrt(width * width + height * height);
    }

    public static Rectangle FullSource(Texture2D tex) => new(0f, 0f, tex.Width, tex.Height);

    public static System.Numerics.Vector2 CenterOrigin(Rectangle rect) => new(rect.Width / 2f, rect.Height / 2f);

    // Turret marker: the small square drawn on the ship that owns a turret.
    public const float TurretMarkerSize = 8f;

    public static void DrawTurretMarker(float cx, float cy, Color color)
    {
        Raylib.DrawRectangle(
            (int)(cx - TurretMarkerSize / 2f),
            (int)(cy - TurretMarkerSize / 2f),
            (int)TurretMarkerSize,
            (int)TurretMarkerSize,
            color);
    }
}
