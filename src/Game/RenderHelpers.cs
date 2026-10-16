using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Stats;

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

    // An enemy ship's turret: a plain dot on the hull, which has no mounts of its own.
    public const float TurretDotSize = 8f;

    public static void DrawTurretDot(float cx, float cy, Color color)
        => Raylib.DrawRectangle((int)(cx - TurretDotSize / 2f), (int)(cy - TurretDotSize / 2f), (int)TurretDotSize, (int)TurretDotSize, color);

    // Turret marker: how big a hull mount reads in world pixels.
    public static float TurretMarkerRadius(TurretSize size) => size == TurretSize.Large ? 9f : 6f;

    // How far a mounted marker reaches from its centre, for off-screen culling.
    public static float TurretMarkerExtent(TurretSize size) => TurretMarkerRadius(size) * MarkerApexReach;

    // A mounted turret: the mount circle with a point sticking out of it toward where the gun aims.
    // The triangle's base corners sit at 0.93 radius — inside the opaque circle, which is a disc, so
    // the whole base is hidden and only the tip at 1.9 radius shows.
    // Vertex order must be clockwise in screen space; counter-clockwise triangles are culled.
    public static void DrawTurretMarker(float cx, float cy, float radius, float angleDeg, Color color)
    {
        Raylib.DrawCircle((int)cx, (int)cy, radius, color);

        float angle = angleDeg * MathF.PI / 180f;
        Raylib.DrawTriangle(
            Rotated(cx, cy, 0f, -radius * MarkerApexReach, angle),
            Rotated(cx, cy, -radius * MarkerTailHalfWidth, radius * MarkerTailInset, angle),
            Rotated(cx, cy, radius * MarkerTailHalfWidth, radius * MarkerTailInset, angle),
            color);
    }

    const float MarkerApexReach = 1.9f;
    const float MarkerTailHalfWidth = 0.9f;
    const float MarkerTailInset = 0.25f;

    static System.Numerics.Vector2 Rotated(float cx, float cy, float x, float y, float angle)
        => new(cx + x * MathF.Cos(angle) - y * MathF.Sin(angle),
               cy + x * MathF.Sin(angle) + y * MathF.Cos(angle));
}
