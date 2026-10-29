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

    // Diagnostics: a hull's collider drawn over its art, so the gap between the collision circle and
    // the painted hull is something you look at instead of something you argue about. Each capsule is
    // its axis at twice its thickness; the round caps are not drawn, which is near enough to read the
    // fit, and a lone disc is left to the bounding circle the diagnostics pass already draws.
    public static void DrawHullShape(HullShape shape, float radius, float cx, float cy, float angleDeg, Color color)
    {
        float angle = angleDeg * MathF.PI / 180f;
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);

        foreach (var capsule in shape.Capsules)
        {
            if (capsule.From == capsule.To) continue;
            Raylib.DrawLineEx(
                ScreenOf(cx, cy, capsule.From, cos, sin, radius),
                ScreenOf(cx, cy, capsule.To, cos, sin, radius),
                capsule.HalfThickness * 2f * radius,
                color);
        }
    }

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
