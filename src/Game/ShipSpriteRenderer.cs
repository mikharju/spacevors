using Raylib_cs;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

public static class ShipSpriteRenderer
{
    // Draws the ship sprite: lit (normal + depth maps) when available, otherwise flat texture.
    public static void DrawShipSprite(ShipType ship, float cx, float cy, float diameter, float angleDeg)
    {
        string key = ship.Name.ToLower();

        LitSprite? lit = null;
        Texture2D? tex = null;
        if (ImageLoader.PlayerShipLitSprites != null && ImageLoader.PlayerShipLitSprites.TryGetValue(key, out var litSprite))
            lit = litSprite;
        else if (ImageLoader.PlayerShipTextures != null && ImageLoader.PlayerShipTextures.TryGetValue(key, out var flat) && flat.Id != 0)
            tex = flat;

        Texture2D baseTex;
        if (lit != null) baseTex = lit.Base;
        else if (tex.HasValue) baseTex = tex.Value;
        else return;
        if (baseTex.Id == 0) return;

        float scale = diameter / baseTex.Width;
        var source = new Rectangle(0f, 0f, baseTex.Width, baseTex.Height);
        var dest = new Rectangle(cx, cy, baseTex.Width * scale, baseTex.Height * scale);
        var origin = new System.Numerics.Vector2(dest.Width / 2f, dest.Height / 2f);

        if (lit != null && Lighting.TryDraw(lit, source, dest, origin, angleDeg)) return;

        Raylib.DrawTexturePro(baseTex, source, dest, origin, angleDeg, Color.White);
    }
}
