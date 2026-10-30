using System.Numerics;
using Raylib_cs;

namespace Spacevors.Game;

// Circles and streaks for the many-small-entities passes. Raylib's shape draws (DrawCircle,
// DrawLineEx) flush the 2D batch and issue a GL draw call per shape - ~1.1us each on a real GPU,
// which measured as 58-78% of the render total in the ammo scenarios. Textured quads go through
// raylib's normal batched 2D path, the one the sprites already use, so a few thousand particles
// become a handful of draws.
//
// The disc is generated once: a white circle on a transparent square, tinted per draw. The texture
// radius (384) exceeds the largest thing ever drawn from it - the player's scaled death blast on a
// 84 px hull reaches ~330 px - so every draw downscales it: the circle's own pixels stay at or below
// screen resolution instead of appearing as scaled-up blocks. The per-draw cost is unchanged: one
// textured quad, and bilinear reads four texels whatever the texture size.
static class ShapeSprites
{
    const int TextureRadius = 384;
    const int TextureSize = TextureRadius * 2;

    static Texture2D _disc;

    static Texture2D Disc
    {
        get
        {
            if (_disc.Id != 0) return _disc;

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

    // The glow's opaque core, as a fraction of its radius. 0.65 keeps the core (13 px on the 20 px
    // glow of a 8 px-half-thickness beam) comfortably wider than the beam itself and pushes the
    // fade-out close to the rim, leaving a broad beam-coloured centre.
    const float GlowCoreFraction = 0.65f;

    static Texture2D _glowDisc;

    // The same disc with alpha ramped from nothing at the rim to opaque at the core: a bright
    // centre fading to a dark perimeter. Rings are painted rim-inward with rising alpha.
    static Texture2D GlowDisc
    {
        get
        {
            if (_glowDisc.Id != 0) return _glowDisc;

            var center = new Vector2(TextureRadius, TextureRadius);
            Image image = Raylib.GenImageColor(TextureSize, TextureSize, Transparent);
            int core = (int)(TextureRadius * GlowCoreFraction);
            for (int r = TextureRadius; r > core; r--)
            {
                int alpha = 255 * (TextureRadius - r) / (TextureRadius - core);
                Raylib.ImageDrawCircleV(ref image, center, r, new Color(255, 255, 255, alpha));
            }
            Raylib.ImageDrawCircleV(ref image, center, core, Opaque);

            _glowDisc = Raylib.LoadTextureFromImage(image);
            Raylib.UnloadImage(image);
            return _glowDisc;
        }
    }

    // A radial glow: brightest at the centre, transparent at the rim, tinted per draw like everything else.
    public static void DrawGlow(float cx, float cy, float radius, Color color)
    {
        float size = MathF.Max(radius, 0.5f) * 2f;

        Raylib.DrawTexturePro(GlowDisc, FullSource,
            new Rectangle(cx, cy, size, size),
            new Vector2(size * 0.5f, size * 0.5f), 0f, color);
    }

    public static void DrawCircle(float cx, float cy, float radius, Color color)
    {
        float size = MathF.Max(radius, 0.5f) * 2f;

        // The dest rect's position is the point the disc is centred on — the renderer-wide sprite
        // convention (ARCHITECTURE.md, the DrawMissile centre fix): a rect built as centre - size/2
        // draws the disc up-left by its own radius instead.
        Raylib.DrawTexturePro(Disc, FullSource,
            new Rectangle(cx, cy, size, size),
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

        // Same convention as DrawCircle: the rect's position lands under the origin — here the streak's
        // left-end centre — so the capsule sits on the path [from,to], as collision sees it.
        Rectangle dest = new(from.X, from.Y, length + thickness, thickness);

        Raylib.DrawTexturePro(Disc, FullSource, dest,
            new Vector2(0f, thickness * 0.5f), MathF.Atan2(axis.Y, axis.X) * 180f / MathF.PI, color);
    }

    // Missile: a nose-up body (triangle tip over a long shaft) and a separate aft triangle for the
    // exhaust, authored white and tinted per draw like the disc. The exhaust is its own image so it
    // can stretch and brighten without moving the body, and it always has a floor: a guided round that
    // goes invisible mid-flight is a round the player cannot dodge.
    // Both sprites are an **odd** number of pixels wide on purpose. Each is rotated about the centre of its
    // texture, and an even width has no centre column: the triangle's apex lands on a column to one side of
    // that point and the slopes rasterise unevenly, so the shape leans half a pixel. On the exhaust that
    // lean is all the player sees at cruise — only the apex end is outside the tail — and it reads as the
    // flame sitting off the missile's axis when the round flies sideways.
    const int BodyWidth = 9;

    // The nose is a fixed triangle — it is the point of the thing, it does not stretch with the airframe —
    // and the shaft under it is what carries the read. The shaft used to be 8 px tall, and at the length a
    // round is actually drawn on screen the whole missile read as a bright dot.
    const int TipHeight = 5, ShaftHeight = 25;
    const int BodyHeight = TipHeight + ShaftHeight;
    const int ExhaustWidth = 9, ExhaustHeight = 12;

    // How much of the plume's own length stays inside the body's tail: 0 butts the two shapes together,
    // 1 hides the whole plume inside the hull. The plume is anchored on the **tail**, not part-way between
    // the body's centre and its tail: anchored part-way, a lengthening plume slid forward along the body
    // and out past the nose, which is what made the flame look detached from a turning missile.
    const float ExhaustBurialInsideTail = 0.25f;

    static Texture2D _missileBody, _missileExhaust;

    static Texture2D MissileBody
    {
        get
        {
            if (_missileBody.Id != 0) return _missileBody;

            Image image = Raylib.GenImageColor(BodyWidth, BodyHeight, Transparent);
            Raylib.ImageDrawTriangle(ref image,
                new Vector2(BodyWidth / 2f, 0f),
                new Vector2(0f, TipHeight),
                new Vector2(BodyWidth, TipHeight), Opaque);
            Raylib.ImageDrawRectangleRec(ref image, new Rectangle(0f, TipHeight, BodyWidth, ShaftHeight), Opaque);

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

    // Where the plume's centre goes for a body/plume pair. One place computes it, so a diagnostic overlay
    // that marks the anchor cannot drift away from what actually gets drawn.
    public static Vector2 PlumeAnchor(float cx, float cy, float angleRad, float bodyLength, float exhaustLength) =>
        new Vector2(cx, cy) - new Vector2(MathF.Sin(angleRad), -MathF.Cos(angleRad))
            * (bodyLength * 0.5f + exhaustLength * (0.5f - ExhaustBurialInsideTail));

    // `angleRad` follows the world's angle convention (0 faces up on screen), the same one the hull
    // sprites are drawn with. The flame hangs off the aft end along that heading.
    public static void DrawMissile(float cx, float cy, float angleRad, float bodyLength, float bodyWidth,
        float exhaustLength, float exhaustWidth, Color bodyColor, Color exhaustColor)
    {
        float degrees = angleRad * 180f / MathF.PI;

        // The dest rect's position is the sprite's centre and rotation origin — the convention every sprite
        // draw in the renderer uses (RenderHelpers.CenterOrigin). Subtracting half the size here instead
        // shifts each sprite up-left by half its own extent, and because the flame and the body have
        // different extents they come apart: a 45 px body drifts 22.5 px while its plume drifts 10.
        if (exhaustLength > 0f)
        {
            // The plume hangs off the tail with a quarter of its own length buried inside it: two shapes
            // that only touch edge to edge read as two objects flying in formation, however carefully the
            // arithmetic looks on paper. Anchoring on the tail rather than on a fixed distance from the
            // centre is what keeps the plume aft as it grows with boost and turn.
            Vector2 aft = PlumeAnchor(cx, cy, angleRad, bodyLength, exhaustLength);
            Raylib.DrawTexturePro(MissileExhaust, new Rectangle(0, 0, ExhaustWidth, ExhaustHeight),
                new Rectangle(aft.X, aft.Y, exhaustWidth, exhaustLength),
                new Vector2(exhaustWidth * 0.5f, exhaustLength * 0.5f), degrees, exhaustColor);
        }

        Raylib.DrawTexturePro(MissileBody, new Rectangle(0, 0, BodyWidth, BodyHeight),
            new Rectangle(cx, cy, bodyWidth, bodyLength),
            new Vector2(bodyWidth * 0.5f, bodyLength * 0.5f), degrees, bodyColor);
    }
}
