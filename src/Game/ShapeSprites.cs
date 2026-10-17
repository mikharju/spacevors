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

    // Missile: a nose-up body (triangle tip over a square shaft) and a separate aft triangle for the
    // exhaust, authored white and tinted per draw like the disc. The exhaust is its own image so it
    // can stretch and brighten without moving the body, and it always has a floor: a guided round that
    // goes invisible mid-flight is a round the player cannot dodge.
    const int BodyWidth = 8, BodyHeight = 14;
    const int ExhaustWidth = 8, ExhaustHeight = 12;

    // How far the plume's nozzle edge reaches into the body's tail, as a fraction of the plume's own
    // length: 0 butts the two shapes together, 0.5 buries half of the plume inside the hull.
    const float ExhaustBurialInsideTail = 0.5f;

    static Texture2D _missileBody, _missileExhaust;

    static Texture2D MissileBody
    {
        get
        {
            if (_missileBody.Id != 0) return _missileBody;

            Image image = Raylib.GenImageColor(BodyWidth, BodyHeight, Transparent);
            Raylib.ImageDrawTriangle(ref image,
                new Vector2(BodyWidth / 2f, 0f),
                new Vector2(0f, BodyHeight * 0.4f),
                new Vector2(BodyWidth, BodyHeight * 0.4f), Opaque);
            Raylib.ImageDrawRectangleRec(ref image, new Rectangle(0f, BodyHeight * 0.4f, BodyWidth, BodyHeight * 0.6f), Opaque);

            _missileBody = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            return _missileBody;
        }
    }

    static Texture2D MissileExhaust
    {
        get
        {
            if (_missileExhaust.Id != 0) return _missileExhaust;

            // Full width at the nozzle, apex aft: the plume leaves the mount as wide as the hull that
            // threw it and tapers away, so where it meets the body the two shapes are continuous.
            Image image = Raylib.GenImageColor(ExhaustWidth, ExhaustHeight, Transparent);
            Raylib.ImageDrawTriangle(ref image,
                new Vector2(0f, 0f),
                new Vector2(ExhaustWidth / 2f, ExhaustHeight),
                new Vector2(ExhaustWidth, 0f), Opaque);

            _missileExhaust = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            return _missileExhaust;
        }
    }

    // `angleRad` follows the world's angle convention (0 faces up on screen), the same one the hull
    // sprites are drawn with. The flame hangs off the aft end along that heading.
    public static void DrawMissile(float cx, float cy, float angleRad, float bodyLength, float bodyWidth,
        float exhaustLength, float exhaustWidth, Color bodyColor, Color exhaustColor)
    {
        float degrees = angleRad * 180f / MathF.PI;

        if (exhaustLength > 0f)
        {
            // The plume's nozzle edge sits a little *inside* the body's tail: two shapes that only
            // touch edge to edge read as two objects flying in formation, however carefully the
            // arithmetic looks on paper.
            Vector2 aft = new Vector2(cx, cy) - new Vector2(MathF.Sin(angleRad), -MathF.Cos(angleRad))
                * (bodyLength * 0.5f - exhaustLength * ExhaustBurialInsideTail);
            Raylib.DrawTexturePro(MissileExhaust, new Rectangle(0, 0, ExhaustWidth, ExhaustHeight),
                new Rectangle(aft.X - exhaustWidth * 0.5f, aft.Y - exhaustLength * 0.5f, exhaustWidth, exhaustLength),
                new Vector2(exhaustWidth * 0.5f, exhaustLength * 0.5f), degrees, exhaustColor);
        }

        Raylib.DrawTexturePro(MissileBody, new Rectangle(0, 0, BodyWidth, BodyHeight),
            new Rectangle(cx - bodyWidth * 0.5f, cy - bodyLength * 0.5f, bodyWidth, bodyLength),
            new Vector2(bodyWidth * 0.5f, bodyLength * 0.5f), degrees, bodyColor);
    }
}
