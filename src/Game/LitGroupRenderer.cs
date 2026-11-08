using Raylib_cs;
using Spacevors.Domain;
using System.Diagnostics;

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

    private static readonly Dictionary<uint, int> _lastDrawnFrame = new();
    private static readonly List<uint> _newTextures = [];
    private static readonly Stopwatch _blockSw = new();
    private static int _frame;

    // The most expensive lit block of the frame, kept as numbers so nothing is formatted unless a
    // spike row actually asks for it.
    private static double _worstBlockMs, _worstBeginMs, _worstDrawMs, _worstFlushMs;
    private static uint _worstBlockId;
    private static int _worstBlockDraws, _worstBlockGap;
    private static float _worstBlockPx;

    // Called once per drawn frame, before any sprite is gathered.
    public static void BeginFrame()
    {
        _frame++;
        _newTextures.Clear();
        _worstBlockMs = 0;
    }

    // Comma-separated texture ids drawn for the first time this frame, "-" when every sprite drawn was
    // already known. Ids only: they identify the texture, and `subworst=` says which pass painted it.
    public static string NewTextures => _newTextures.Count == 0 ? "-" : string.Join(',', _newTextures);

    // The block that cost the most this frame, as one spike-log column: the variant's base texture id,
    // how many sprites it drew and how many pixels it painted (fill rate), how many frames since that
    // variant was last drawn (-1 = never, 3000 = a week at 120 fps, i.e. a texture that went cold), and
    // where the time went: `begin` = shader + uniform + texture bind, `draw` = queueing the sprites,
    // `flush` = the batch flush EndDraw triggers, which is where the GPU submission and any driver wait land.
    public static string WorstLitBlock => _worstBlockMs == 0
        ? "none"
        : $"id={_worstBlockId} draws={_worstBlockDraws} gap={_worstBlockGap} px={_worstBlockPx / 1_000_000:F2}M " +
          $"begin={_worstBeginMs:F1} draw={_worstDrawMs:F1} flush={_worstFlushMs:F1}ms";

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
        bool probe = DiagnosticLogger.IsEnabled;

        foreach (var (block, draws) in groups)
        {
            // Reused buffers keep variants with no draws this frame; skip their shader-mode toggle.
            if (draws.Count == 0) continue;

            if (probe) _blockSw.Restart();

            var tint = Palette.HullTintOf(block.Faction);
            if (!Lighting.BeginDraw(block.Lit, tint))
            {
                var fallback = Palette.HullFallbackTint(block.Faction);
                foreach (var (dest, angleDeg, _) in draws)
                    DrawFlat(block.Lit.Base, dest, angleDeg, fallback);
                continue;
            }

            long beginTicks = probe ? ElapsedAndRestart() : 0;

            foreach (var (dest, angleDeg, damage) in draws) Lighting.Draw(block.Lit, dest, angleDeg, damage);

            long drawTicks = probe ? ElapsedAndRestart() : 0;

            Lighting.EndDraw();

            if (probe) RecordBlock(block, draws, beginTicks, drawTicks, _blockSw.ElapsedTicks);
        }
    }

    // Which of this frame's lit blocks cost the most, and what it cost. Blocks are per variant and
    // faction, so two or three dozen of these run per frame and only the worst is reported.
    static void RecordBlock(LitBlock block, List<(Rectangle Dest, float AngleDeg, float Damage)> draws, long beginTicks, long drawTicks, long flushTicks)
    {
        int gap = NoteTextures(block.Lit);
        double ms = Ms(beginTicks) + Ms(drawTicks) + Ms(flushTicks);
        if (ms <= _worstBlockMs) return;

        _worstBlockMs = ms;
        _worstBlockId = block.Lit.Base.Id;
        _worstBlockDraws = draws.Count;
        _worstBlockGap = gap;
        _worstBlockPx = TotalPixels(draws);
        _worstBeginMs = Ms(beginTicks);
        _worstDrawMs = Ms(drawTicks);
        _worstFlushMs = Ms(flushTicks);
    }

    // Frames since this variant was last drawn (-1 = never), and the same for its normal/depth map,
    // which is what `newLit=` lists. Tracked here rather than in Add: a block is recorded once per
    // frame, while Add runs once per sprite.
    static int NoteTextures(LitSprite lit)
    {
        int gap = NoteTexture(lit.Base.Id);
        NoteTexture(lit.NormalDepth.Id);
        return gap;
    }

    static int NoteTexture(uint id)
    {
        int gap = _lastDrawnFrame.TryGetValue(id, out int frame) ? _frame - frame : -1;
        _lastDrawnFrame[id] = _frame;
        if (gap < 0) _newTextures.Add(id);
        return gap;
    }

    static float TotalPixels(List<(Rectangle Dest, float AngleDeg, float Damage)> draws)
    {
        float pixels = 0;
        foreach (var (dest, _, _) in draws) pixels += dest.Width * dest.Height;
        return pixels;
    }

    static long ElapsedAndRestart()
    {
        long ticks = _blockSw.ElapsedTicks;
        _blockSw.Restart();
        return ticks;
    }

    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    static void DrawFlat(Texture2D baseTex, Rectangle dest, float angleDeg, Color tint)
        => Raylib.DrawTexturePro(baseTex, RenderHelpers.FullSource(baseTex), dest, RenderHelpers.CenterOrigin(dest), angleDeg, tint);
}
