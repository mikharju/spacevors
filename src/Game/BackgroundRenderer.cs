using Raylib_cs;
using Spacevors.Domain;

namespace Spacevors.Game;

public static class BackgroundRenderer
{
    public static void Draw(
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        DrawStarfield(stars, camX, camY, windowWidth, windowHeight);
        DrawClutter(clutter, camX, camY, windowWidth, windowHeight);
    }

    private static void DrawStarfield(
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (pos, size, color, parallax) in stars)
        {
            float cx = pos.X - camX * parallax + windowWidth / 2f;
            float cy = pos.Y - camY * parallax + windowHeight / 2f;

            cx = ((cx % windowWidth) + windowWidth) % windowWidth;
            cy = ((cy % windowHeight) + windowHeight) % windowHeight;

            Raylib.DrawCircle((int)cx, (int)cy, (int)Math.Max(1f, size), color);
        }
    }

    private static void DrawClutter(
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (pos, width, height, color) in clutter)
        {
            float cx = pos.X - camX + windowWidth / 2f;
            float cy = pos.Y - camY + windowHeight / 2f;

            if (cx < -width || cx > windowWidth + width || cy < -height || cy > windowHeight + height) continue;

            Raylib.DrawRectangle((int)(cx - width / 2f), (int)(cy - height / 2f), (int)width, (int)height, color);
        }
    }
}
