using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

// Draws targeting brackets and hp bars: bright red for the manually selected target, blue-grey for
// enemies currently targeted by a player turret (persisting briefly after selection stops).
public static class TargetingRenderer
{
    private static readonly Color ManualBracketColor = new(255, 80, 80, 230);
    private static readonly Color AutoBracketColor = new(150, 170, 200, 230);

    // Gap between the target radius and the bracket corners.
    private const float Margin = 10f;
    private const float ArmLength = 10f;
    private const float Thickness = 1.5f;

    private const float BarHeight = 3f;
    private const float BarGap = 6f; // gap between the top bracket corners and the bar

    public static void Draw(EntityManager em, Entity playerEntity, float camX, float camY, int windowWidth, int windowHeight)
    {
        bool hasManual = em.TryGetComponent<PrimaryTarget>(playerEntity, out var primary);
        if (hasManual && !em.HasComponent<Dead>(primary.Target))
        {
            // Gentle 1 Hz alpha pulse so the manual lock reads without any shape change.
            float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 2f * MathF.PI);
            var manualColor = new Color(ManualBracketColor.R, ManualBracketColor.G, ManualBracketColor.B, (byte)(229 + 26 * pulse));
            DrawTarget(em, primary.Target, manualColor, camX, camY, windowWidth, windowHeight);
        }

        foreach (var (entity, mark) in em.GetEntitiesWithComponents<AutoTargetMark>())
        {
            if (em.HasComponent<Dead>(entity)) continue; // dead ships keep their mark until destroyed
            if (em.ElapsedTime - mark.LastTargetedAt > AutoTargetMark.FreshWindow) continue;
            if (hasManual && entity == primary.Target) continue; // manual bracket takes priority

            DrawTarget(em, entity, AutoBracketColor, camX, camY, windowWidth, windowHeight);
        }
    }

    private static void DrawTarget(EntityManager em, Entity target, Color color, float camX, float camY, int windowWidth, int windowHeight)
    {
        float radius = GetRadius(em, target);
        if (radius <= 0f) return;

        var pos = em.GetComponent<Position>(target);
        var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);

        // Extend the cull extent for the bar above the bracket.
        if (RenderHelpers.IsOffScreen(cx, cy, radius + Margin + BarGap + BarHeight, windowWidth, windowHeight)) return;

        float halfSize = radius + Margin;
        DrawBrackets(cx, cy, halfSize, color);
        DrawHpBar(em, target, cx, cy - halfSize - BarGap - BarHeight / 2f, halfSize);
    }

    private static void DrawHpBar(EntityManager em, Entity target, float centerX, float centerY, float halfSize)
    {
        if (!em.TryGetComponent<Health>(target, out var health)) return; // dead ships have no Health and are skipped by the caller
        if (health.Current >= health.Max) return; // full-health targets need no bar — less clutter

        float ratio = Math.Clamp((float)health.Current / health.Max, 0f, 1f);
        int x = (int)(centerX - halfSize);
        int y = (int)(centerY - BarHeight / 2f);
        int width = (int)(halfSize * 2f);
        int barHeight = (int)BarHeight;

        // Two plain rects, not Ui.DrawRoundedRect: a rounded rect is four Raylib.DrawCircle corners,
        // and each one flushes the 2D batch. At 3 px tall the 1 px corner radius is invisible anyway.
        Raylib.DrawRectangle(x, y, width, barHeight, Palette.TrackDark);
        Raylib.DrawRectangle(x, y, Math.Max(barHeight, (int)(width * ratio)), barHeight, Palette.HealthColor(ratio));
    }

    private static float GetRadius(EntityManager em, Entity target)
    {
        if (em.TryGetComponent<EnemyShip>(target, out var ship)) return ship.Radius;
        if (em.TryGetComponent<EnemyMine>(target, out var mine)) return mine.Radius;
        return 0f;
    }

    // Four corners, two arms each. Batched quads, not DrawLineEx: every DrawLineEx flushes the 2D
    // batch and issues its own draw call, and this runs once per marked hull, which scales with the
    // number of turrets' targets. The arms are axis-aligned, so a plain centred rect is the same
    // segment at the same thickness.
    private static void DrawBrackets(float cx, float cy, float halfSize, Color color)
    {
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                float cornerX = cx + sx * halfSize;
                float cornerY = cy + sy * halfSize;
                DrawSegmentQuad(cornerX - sx * ArmLength / 2f, cornerY, ArmLength, color);
                DrawSegmentQuad(cornerX, cornerY - sy * ArmLength / 2f, Thickness, color);
            }
    }

    // A bracket arm as one quad: `width`/`height` swap between the horizontal and vertical arm, the
    // thicker dimension is the line's thickness. DrawRectanglePro places its centre at (cx, cy) when
    // the origin is the rect's half-size (the same anchor RenderHelpers.CenterOrigin enforces).
    static void DrawSegmentQuad(float cx, float cy, float extent, Color color)
    {
        bool horizontal = extent >= Thickness;
        float width = horizontal ? extent : Thickness;
        float height = horizontal ? Thickness : extent;
        var rect = new Rectangle(cx, cy, width, height);
        Raylib.DrawRectanglePro(rect, RenderHelpers.CenterOrigin(rect), 0f, color);
    }
}
