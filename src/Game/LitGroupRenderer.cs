using Raylib_cs;

namespace Spacevors.Game;

// One shader block's worth of work: one sprite variant drawn for one hull faction. The hull tint is a
// per-block uniform like the surface map, so a faction cannot share a block with another faction.
public readonly record struct LitBlock(LitSprite Lit, HullFaction Faction);

// Groups lit draws by sprite variant and faction so each pair renders under one shader-mode block.
public static class LitGroupRenderer
{
    // Reuses per-frame buffers instead of reallocating every tick (SpatialGrid pattern).
    public static void Clear(Dictionary<LitBlock, List<(Rectangle Dest, float AngleDeg, float Damage)>> groups)
    {
        foreach (var list in groups.Values)
            list.Clear();
    }

    // damage is the hull damage scalar [0,1]; 0 (default) renders an undamaged hull.
    public static void Add(Dictionary<LitBlock, List<(Rectangle Dest, float AngleDeg, float Damage)>> groups, LitSprite lit, HullFaction faction, Rectangle dest, float angleDeg, float damage = 0f)
    {
        var block = new LitBlock(lit, faction);
        if (!groups.TryGetValue(block, out var list))
        {
            list = new();
            groups.Add(block, list);
        }
        list.Add((dest, angleDeg, damage));
    }

    // One block per variant and faction keeps the normal/depth map and the hull tint constant inside a
    // block (they are global at batch-flush time). Overlapping sprites of different variants may change
    // draw order relative to entity order; there is no depth sorting either way.
    // Factions are two or three and every faction but Neutral uses sprites no other faction uses, so in
    // practice this is one block per sprite variant, as it was before factions existed.
    public static void Draw(Dictionary<LitBlock, List<(Rectangle Dest, float AngleDeg, float Damage)>> groups)
    {
        foreach (var (block, draws) in groups)
        {
            // Reused buffers keep variants with no draws this frame; skip their shader-mode toggle.
            if (draws.Count == 0) continue;

            var tint = Palette.HullTintOf(block.Faction);
            if (!Lighting.BeginDraw(block.Lit, tint))
            {
                var fallback = Palette.HullFallbackTint(block.Faction);
                foreach (var (dest, angleDeg, _) in draws)
                    DrawFlat(block.Lit.Base, dest, angleDeg, fallback);
                continue;
            }

            foreach (var (dest, angleDeg, damage) in draws) Lighting.Draw(block.Lit, dest, angleDeg, damage);
            Lighting.EndDraw();
        }
    }

    static void DrawFlat(Texture2D baseTex, Rectangle dest, float angleDeg, Color tint)
        => Raylib.DrawTexturePro(baseTex, RenderHelpers.FullSource(baseTex), dest, RenderHelpers.CenterOrigin(dest), angleDeg, tint);
}
