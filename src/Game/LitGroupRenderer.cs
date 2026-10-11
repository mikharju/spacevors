using Raylib_cs;

namespace Spacevors.Game;

// Groups lit draws by sprite variant so each variant renders under one shader-mode block.
public static class LitGroupRenderer
{
    // Reuses per-frame buffers instead of reallocating every tick (SpatialGrid pattern).
    public static void Clear(Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg, float Damage)>> groups)
    {
        foreach (var list in groups.Values)
            list.Clear();
    }

    // damage is the hull damage scalar [0,1]; 0 (default) renders an undamaged hull.
    public static void Add(Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg, float Damage)>> groups, LitSprite lit, Rectangle dest, float angleDeg, float damage = 0f)
    {
        if (!groups.TryGetValue(lit, out var list))
        {
            list = new();
            groups.Add(lit, list);
        }
        list.Add((dest, angleDeg, damage));
    }

    // One block per variant keeps the normal/depth map uniforms constant inside a block (they are
    // global at batch-flush time). Overlapping sprites of different variants may change draw order
    // relative to entity order; there is no depth sorting either way.
    public static void Draw(Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg, float Damage)>> groups)
    {
        foreach (var (lit, draws) in groups)
        {
            // Reused buffers keep variants with no draws this frame; skip their shader-mode toggle.
            if (draws.Count == 0) continue;

            if (!Lighting.BeginDraw(lit))
            {
                foreach (var (dest, angleDeg, _) in draws)
                    DrawFlat(lit.Base, dest, angleDeg);
                continue;
            }

            foreach (var (dest, angleDeg, damage) in draws) Lighting.Draw(lit, dest, angleDeg, damage);
            Lighting.EndDraw();
        }
    }

    static void DrawFlat(Texture2D baseTex, Rectangle dest, float angleDeg)
        => Raylib.DrawTexturePro(baseTex, RenderHelpers.FullSource(baseTex), dest, RenderHelpers.CenterOrigin(dest), angleDeg, Color.White);
}
