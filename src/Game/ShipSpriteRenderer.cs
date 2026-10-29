using Raylib_cs;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

public static class ShipSpriteRenderer
{
    static readonly Color FallbackShipColor = new(180, 60, 60, 255);

    // Draws the ship sprite: lit (normal + depth maps) when available, otherwise flat texture.
    // damage is the hull damage scalar [0,1] (see DamageShading); 0 renders an undamaged hull.
    public static void DrawShipSprite(ShipType ship, float cx, float cy, float diameter, float angleDeg, float damage = 0f)
    {
        string key = ship.TextureKey;

        LitSprite? lit = null;
        Texture2D? tex = null;
        if (ImageLoader.PlayerShipLitSprites != null && ImageLoader.PlayerShipLitSprites.TryGetValue(key, out var litSprite))
            lit = litSprite;
        else if (ImageLoader.PlayerShipTextures != null && ImageLoader.PlayerShipTextures.TryGetValue(key, out var flat) && flat.Id != 0)
            tex = flat;

        Texture2D? baseTex = lit != null ? lit.Base : tex;
        if (baseTex == null || baseTex.Value.Id == 0)
        {
            DrawFallbackShip(cx, cy, diameter / 2f, angleDeg);
            return;
        }

        var texture = baseTex.Value;
        float scale = diameter / texture.Width;
        var dest = new Rectangle(cx, cy, texture.Width * scale, texture.Height * scale);

        if (lit != null && Lighting.BeginDraw(lit))
        {
            Lighting.Draw(lit, dest, angleDeg, damage);
            Lighting.EndDraw();
            return;
        }

        DrawFlat(texture, dest, angleDeg);
    }

    // Placeholder for any ship with no usable sprite (player or enemy), so a missing or failed
    // texture never hides a ship. angleDeg matches DrawTexturePro's clockwise rotation, so the
    // placeholder faces the same way the sprite would.
    public static void DrawFallbackShip(float cx, float cy, float radius, float angleDeg)
    {
        float angle = angleDeg * MathF.PI / 180f;
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);

        var tip = Rotated(0f, -radius, cos, sin);
        var left = Rotated(-radius * 0.4f, radius * 0.6f, cos, sin);
        var right = Rotated(radius * 0.4f, radius * 0.6f, cos, sin);

        // Vertex order must be clockwise in screen space; counter-clockwise triangles are culled.
        Raylib.DrawTriangle(
            new System.Numerics.Vector2(cx + tip.X, cy + tip.Y),
            new System.Numerics.Vector2(cx + left.X, cy + left.Y),
            new System.Numerics.Vector2(cx + right.X, cy + right.Y),
            FallbackShipColor);
    }

    static System.Numerics.Vector2 Rotated(float x, float y, float cos, float sin)
        => new(x * cos - y * sin, x * sin + y * cos);

    // Diagnostics: a hull's collider drawn over its art, so the fit between the painted silhouette and
    // what actually gets hit is something you look at instead of something you argue about. One colour
    // wherever a collider is drawn — on the select screen as much as in a fight — so "cyan is what you
    // can hit" is a rule rather than three conventions.
    public static void DrawHullShape(HullShape shape, float radius, float cx, float cy, float angleDeg)
    {
        float angle = angleDeg * MathF.PI / 180f;
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);

        foreach (var capsule in shape.Capsules)
            DrawCapsule(
                ScreenOf(cx, cy, capsule.From, cos, sin, radius),
                ScreenOf(cx, cy, capsule.To, cos, sin, radius),
                capsule.HalfThickness * radius);
    }

    const int CapSegments = 12; // per half-circle: round enough to read at hull size, cheap in a diagnostic

    // A capsule is a segment inflated by a disc, and that is exactly what the narrow phase tests, so the
    // overlay shows the round caps. DrawLineEx ends flat: it reads as a rectangle and blames a missed or
    // caught shot on a corner that nothing in the game can collide with.
    static void DrawCapsule(System.Numerics.Vector2 from, System.Numerics.Vector2 to, float capRadius)
    {
        if (capRadius <= 0f) return;

        var axis = to - from;
        if (axis.X == 0f && axis.Y == 0f)
        {
            Raylib.DrawCircle((int)from.X, (int)from.Y, capRadius, ColliderColor);
            return;
        }

        // Two half-circles joined by the segment's sides — the convex outline of the pair of discs a
        // capsule is. This raylib binding's DrawPoly only draws regular polygons, so the silhouette is
        // fanned by hand: ~24 hairline-overlapping triangles per capsule, in a pass that draws nothing
        // when diagnostics are off.
        float axisAngle = MathF.Atan2(axis.Y, axis.X);
        Span<System.Numerics.Vector2> outline = stackalloc System.Numerics.Vector2[2 * (CapSegments + 1)];

        int count = 0;
        for (int i = 0; i <= CapSegments; i++)
        {
            float a = axisAngle - MathF.PI / 2f + MathF.PI * i / CapSegments;
            outline[count++] = to + new System.Numerics.Vector2(MathF.Cos(a), MathF.Sin(a)) * capRadius;
        }
        for (int i = 0; i <= CapSegments; i++)
        {
            float a = axisAngle + MathF.PI / 2f + MathF.PI * i / CapSegments;
            outline[count++] = from + new System.Numerics.Vector2(MathF.Cos(a), MathF.Sin(a)) * capRadius;
        }

        // Wound the other way round from the outline walk: this pass culls counter-clockwise triangles,
        // and an incorrectly wound fan draws nothing at all.
        for (int i = 1; i + 1 < count; i++)
            Raylib.DrawTriangle(outline[0], outline[i + 1], outline[i], ColliderColor);
    }

    static readonly Color ColliderColor = new(0, 255, 255, 110);

    // Shape units to screen: scale by the hull radius, then rotate by the hull's heading — the same
    // frame DrawTexturePro draws the sprite in, so an authored capsule lines up with the art.
    static System.Numerics.Vector2 ScreenOf(float cx, float cy, Spacevors.Domain.Vector2 local, float cos, float sin, float radius)
    {
        var rotated = Rotated(local.X * radius, local.Y * radius, cos, sin);
        return new System.Numerics.Vector2(cx + rotated.X, cy + rotated.Y);
    }

    static void DrawFlat(Texture2D baseTex, Rectangle dest, float angleDeg)
        => Raylib.DrawTexturePro(baseTex, RenderHelpers.FullSource(baseTex), dest, RenderHelpers.CenterOrigin(dest), angleDeg, Color.White);
}
