using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems; // Dead

namespace Spacevors.Game;

// Edge-of-screen indicators: a triangle parked on the viewport edge pointing outward at an
// off-screen threat or pickup. The aperture is wide when the object is near and narrows with
// distance to a sliver past FarDistance, so the closest objects read as the sharpest pointers.
// Enemies are red; each pickup marker carries the colour its pickup is drawn with in the world,
// and an enemy missile carries the guided round's orange flame colour. Render-only: it reads the
// ECS read-only, exactly like TargetingRenderer, and the sim never sees it.
public static class IndicatorRenderer
{
    // Hard caps. Normal play keeps off-screen populations tiny (enemy missiles are capped at 7 by
    // EnemyShipStats.MissileCapFor), but scenario worlds inflate every count, and a per-unit HUD
    // loop that scales with them is TROUBLE_SHOOTING #61. The nearest markers survive the cap —
    // the dropped ones are the far objects the taper was flattening to slivers anyway.
    public const int EnemyCap = 24, MissileCap = 24, XpCap = 16, OrbCap = 8, SalvageCap = 8, EcmCap = 8;

    // How far inside the true edge the markers sit, and the taper's distance window: an object at
    // or within WideDistance gets the full aperture, one at or beyond FarDistance gets ThinHalfWidth.
    public const float EdgeInset = 28f;
    public const float WideDistance = 900f, FarDistance = 2600f;
    public const float WideHalfWidth = 11f, ThinHalfWidth = 3f;
    const float ChevronHalfLength = 9f;

    // Reused buffers: Collect fills them each frame and Draw reads them, so the frame loop
    // allocates nothing. Public because the headless tests assert on them.
    public static readonly Vector2[] EnemyMarkers = new Vector2[EnemyCap];
    public static readonly Vector2[] MissileMarkers = new Vector2[MissileCap];
    public static readonly Vector2[] XpMarkers = new Vector2[XpCap];
    public static readonly Vector2[] OrbMarkers = new Vector2[OrbCap];
    public static readonly Vector2[] SalvageMarkers = new Vector2[SalvageCap];
    public static readonly Vector2[] EcmMarkers = new Vector2[EcmCap];

    // Fields, not properties: AddNearest ref-writes them, and the tests read them.
    public static int EnemyCount, MissileCount, XpCount, OrbCount, SalvageCount, EcmCount;

    // One cheap pass per class. Origin is the camera centre, not the player: the edge is the
    // viewport's, and the camera carries the mouse drift.
    public static void Collect(EntityManager em, Vector2 cameraCenter, Vector2 viewport)
    {
        EnemyCount = 0;
        MissileCount = 0;
        XpCount = 0;
        OrbCount = 0;
        SalvageCount = 0;
        EcmCount = 0;

        foreach (var (entity, _, pos) in em.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            // A wreck draws nothing, so it points at nothing either.
            if (em.HasComponent<Dead>(entity)) continue;
            AddNearest(EnemyMarkers, ref EnemyCount, EnemyCap, pos.Value, cameraCenter, viewport);
        }

        foreach (var (_, _, ammo, pos) in em.GetEntitiesWithComponents<Missile, Ammo, Position>())
        {
            if (ammo.IsEnemy) AddNearest(MissileMarkers, ref MissileCount, MissileCap, pos.Value, cameraCenter, viewport);
        }

        foreach (var (_, _, pos) in em.GetEntitiesWithComponents<XpPickup, Position>())
            AddNearest(XpMarkers, ref XpCount, XpCap, pos.Value, cameraCenter, viewport);

        foreach (var (_, _, pos) in em.GetEntitiesWithComponents<HealthOrb, Position>())
            AddNearest(OrbMarkers, ref OrbCount, OrbCap, pos.Value, cameraCenter, viewport);

        // Both grey capsules point the same way, in the same grey: a salvage capsule and a salvage-field
        // capsule are both salvage to fly back out to, and the world already tells them apart by the ring.
        foreach (var (_, _, pos) in em.GetEntitiesWithComponents<RerollToken, Position>())
            AddNearest(SalvageMarkers, ref SalvageCount, SalvageCap, pos.Value, cameraCenter, viewport);

        foreach (var (_, _, pos) in em.GetEntitiesWithComponents<SalvageField, Position>())
            AddNearest(SalvageMarkers, ref SalvageCount, SalvageCap, pos.Value, cameraCenter, viewport);

        // An ECM capsule never expires and is never swept in, so it can be waiting on the far side of the
        // field when the player wants it. Pointing at it is what makes saving one for later possible.
        foreach (var (_, _, pos) in em.GetEntitiesWithComponents<EcmPulse, Position>())
            AddNearest(EcmMarkers, ref EcmCount, EcmCap, pos.Value, cameraCenter, viewport);
    }

    public static void Draw(Vector2 cameraCenter, Vector2 viewport)
    {
        DrawMarkers(EnemyMarkers, EnemyCount, cameraCenter, viewport, Palette.EnemyIndicator);
        DrawMarkers(MissileMarkers, MissileCount, cameraCenter, viewport, WorldRenderer.AmmoColorOf(AmmoColor.Orange));
        DrawMarkers(XpMarkers, XpCount, cameraCenter, viewport, Palette.XpPickup);
        DrawMarkers(OrbMarkers, OrbCount, cameraCenter, viewport, Palette.HealthOrb);
        DrawMarkers(SalvageMarkers, SalvageCount, cameraCenter, viewport, Palette.SalvageCap);
        DrawMarkers(EcmMarkers, EcmCount, cameraCenter, viewport, Palette.EcmIndicator);
    }

    // Keeps the cap nearest off-screen positions. Selection is purely by distance, so it is
    // deterministic whatever order the pop/swap storage enumerates in.
    private static void AddNearest(Vector2[] buffer, ref int count, int cap, Vector2 world, Vector2 center, Vector2 viewport)
    {
        var (sx, sy) = RenderHelpers.ToScreen(world, center.X, center.Y, (int)viewport.X, (int)viewport.Y);
        if (!RenderHelpers.IsOffScreen(sx, sy, 0f, (int)viewport.X, (int)viewport.Y)) return;

        if (count < cap)
        {
            buffer[count++] = world;
            return;
        }

        float distSq = DistSq(world, center);
        int worst = 0;
        float worstDistSq = DistSq(buffer[0], center);
        for (int i = 1; i < count; i++)
        {
            float d = DistSq(buffer[i], center);
            if (d > worstDistSq)
            {
                worstDistSq = d;
                worst = i;
            }
        }
        if (distSq < worstDistSq) buffer[worst] = world;
    }

    static float DistSq(Vector2 a, Vector2 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static void DrawMarkers(Vector2[] markers, int count, Vector2 center, Vector2 viewport, Color color)
    {
        for (int i = 0; i < count; i++)
        {
            var dir = markers[i] - center;
            float dist = dir.Magnitude;
            if (dist < 1f) continue;
            var unit = dir / dist;
            DrawChevron(EdgePoint(viewport, unit), unit, HalfWidthFor(dist), color);
        }
    }

    // Where the ray from the screen centre toward the object crosses the inset edge rectangle.
    public static Vector2 EdgePoint(Vector2 viewport, Vector2 unitDir)
    {
        var half = viewport * 0.5f - new Vector2(EdgeInset, EdgeInset);
        float tx = half.X / MathF.Max(MathF.Abs(unitDir.X), 1e-6f);
        float ty = half.Y / MathF.Max(MathF.Abs(unitDir.Y), 1e-6f);
        float t = MathF.Min(tx, ty);
        return viewport * 0.5f + unitDir * t;
    }

    public static float HalfWidthFor(float distance)
    {
        float t = Math.Clamp((distance - WideDistance) / (FarDistance - WideDistance), 0f, 1f);
        return WideHalfWidth + (ThinHalfWidth - WideHalfWidth) * t;
    }

    // Tip pointing outward at the object, base inset from the edge point. Vertex order is
    // clockwise in screen space — the convention RenderHelpers sets out: counter-clockwise
    // triangles are culled.
    private static void DrawChevron(Vector2 edge, Vector2 unitDir, float halfWidth, Color color)
    {
        var tip = edge + unitDir * ChevronHalfLength;
        var back = edge - unitDir * ChevronHalfLength;
        var perp = new Vector2(unitDir.Y, -unitDir.X);
        Raylib.DrawTriangle(ToRay(tip), ToRay(back + perp * halfWidth), ToRay(back - perp * halfWidth), color);
    }

    static System.Numerics.Vector2 ToRay(Vector2 v) => new(v.X, v.Y);
}
