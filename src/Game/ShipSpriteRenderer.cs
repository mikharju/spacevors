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
        var dest = new Rectangle(cx, cy, baseTex.Width * scale, baseTex.Height * scale);

        if (lit != null && Lighting.BeginDraw(lit))
        {
            Lighting.Draw(lit, dest, angleDeg);
            Lighting.EndDraw();
            return;
        }

        DrawFlat(baseTex, dest, angleDeg);
    }

    static void DrawFlat(Texture2D baseTex, Rectangle dest, float angleDeg)
        => Raylib.DrawTexturePro(baseTex, RenderHelpers.FullSource(baseTex), dest, RenderHelpers.CenterOrigin(dest), angleDeg, Color.White);
}
