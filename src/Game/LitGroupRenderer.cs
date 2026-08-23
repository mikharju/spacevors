using Raylib_cs;

namespace Spacevors.Game;

// Groups lit draws by sprite variant so each variant renders under one shader-mode block.
public static class LitGroupRenderer
{
    // Reuses per-frame buffers instead of reallocating every tick (SpatialGrid pattern).
    public static void Clear(Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg)>> groups)
    {
        foreach (var list in groups.Values)
            list.Clear();
    }

    public static void Add(Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg)>> groups, LitSprite lit, Rectangle dest, float angleDeg)
    {
        if (!groups.TryGetValue(lit, out var list))
        {
            list = new();
            groups.Add(lit, list);
        }
        list.Add((dest, angleDeg));
    }

    // One block per variant keeps the normal/depth map uniforms constant inside a block (they are
    // global at batch-flush time). Overlapping sprites of different variants may change draw order
    // relative to entity order; there is no depth sorting either way.
    public static void Draw(Dictionary<LitSprite, List<(Rectangle Dest, float AngleDeg)>> groups)
    {
        foreach (var (lit, draws) in groups)
        {
            // Reused buffers keep variants with no draws this frame; skip their shader-mode toggle.
            if (draws.Count == 0) continue;

            if (!Lighting.BeginDraw(lit))
            {
                foreach (var (dest, angleDeg) in draws)
                    DrawFlat(lit.Base, dest, angleDeg);
                continue;
            }

            foreach (var (dest, angleDeg) in draws) Lighting.Draw(lit, dest, angleDeg);
            Lighting.EndDraw();
        }
    }

    static void DrawFlat(Texture2D baseTex, Rectangle dest, float angleDeg)
        => Raylib.DrawTexturePro(baseTex, RenderHelpers.FullSource(baseTex), dest, RenderHelpers.CenterOrigin(dest), angleDeg, Color.White);
}
