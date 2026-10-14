using System.Numerics;
using Raylib_cs;

namespace Spacevors.Game;

// Circles and streaks for the many-small-entities passes. Raylib's shape draws (DrawCircle,
// DrawLineEx) flush the 2D batch and issue a GL draw call per shape - ~1.1us each on a real GPU,
// which measured as 58-78% of the render total in the ammo scenarios. Textured quads go through
// raylib's normal batched 2D path, the one the sprites already use, so a few thousand particles
// become a handful of draws.
//
// The disc is generated once: a white circle on a transparent square, tinted per draw. Radius 32
// keeps explosions (the largest thing drawn this way) close to 1:1, and the hard edge matches the
// filled circles these passes drew before.
static class ShapeSprites
{
    const int TextureRadius = 32;
    const int TextureSize = TextureRadius * 2;

    static Texture2D _disc;

    static Texture2D Disc
    {
        get
        {
            if (_disc.Id != 0) return _disc;

            // Drawn into an image rather than generated: Raylib-cs has no circle-image generator, and
            // ImageDrawCircleV gives the same hard-edged disc DrawCircle used to produce. Bilinear
            // filtering then smooths it down to the few-pixel sizes these particles use.
            Image image = Raylib.GenImageColor(TextureSize, TextureSize, Transparent);
            Raylib.ImageDrawCircleV(ref image, new Vector2(TextureRadius, TextureRadius), TextureRadius, Opaque);

            _disc = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            return _disc;
        }
    }

    static readonly Color Opaque = new(255, 255, 255, 255);
    static readonly Color Transparent = new(255, 255, 255, 0);

    static Rectangle FullSource => new(0, 0, TextureSize, TextureSize);

    public static void DrawCircle(float cx, float cy, float radius, Color color)
    {
        float size = MathF.Max(radius, 0.5f) * 2f;

        Raylib.DrawTexturePro(Disc, FullSource,
            new Rectangle(cx - size * 0.5f, cy - size * 0.5f, size, size),
            new Vector2(size * 0.5f, size * 0.5f), 0f, color);
    }

    // A bullet's swept segment as a disc stretched along its travel: round ends instead of the flat
    // caps DrawLineEx produced, at the same width.
    public static void DrawStreak(Vector2 from, Vector2 to, float thickness, Color color)
    {
        Vector2 axis = to - from;
        float length = axis.Length();
        if (length <= 0.001f)
        {
            DrawCircle(to.X, to.Y, thickness * 0.5f, color);
            return;
        }

        Rectangle dest = new(from.X, from.Y - thickness * 0.5f, length + thickness, thickness);

        Raylib.DrawTexturePro(Disc, FullSource, dest,
            new Vector2(0f, thickness * 0.5f), MathF.Atan2(axis.Y, axis.X) * 180f / MathF.PI, color);
    }
}
