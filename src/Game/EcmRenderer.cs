using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems; // Dead

namespace Spacevors.Game;

// What an ECM jam looks like (plans/ECM_PULSE_AND_SALVAGE_FIELD.md): arcs flicking over every jammed hull,
// and the ring that fires out of the capsule when the hull touches it.
//
// The flicker is hashed from the entity and the *simulated* tick, never from a wall clock and never from the
// world RNG: a seeded run draws the same frame twice, and the renderer does not disturb the random stream
// the sim is drawing from.
//
// Both passes are bounded — at most `ArcEntityCap` hulls, `ArcsPerHull` strips each, `WaveRingCount` rings
// per wave — because a scenario can put hundreds of jammed mines on screen and a per-unit renderer that
// scales with them is TROUBLE_SHOOTING #61.
public static class EcmRenderer
{
    const int ArcsPerHull = 3;
    const int ArcPoints = 4;
    const int ArcEntityCap = 24;
    const int FlickersPerSecond = 12;
    const float ArcRadiusFraction = 1.15f;
    const float ArcSpan = 0.9f;
    const float ArcAngleJitter = 0.15f;
    const float ArcRadialJitter = 0.22f;
    const float ArcThickness = 2f;
    const int WaveRingCount = 3;
    const float WaveRingInset = 0.12f;
    const float WaveRingThickness = 2.5f;
    const float MinRingSegments = 16f;
    const float RingSegmentPx = 12f;

    private static readonly System.Numerics.Vector2[] _arcPoints = new System.Numerics.Vector2[ArcPoints];

    public static void Draw(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        DrawArcs(em, camX, camY, windowWidth, windowHeight);
        DrawWaves(em, camX, camY, windowWidth, windowHeight);
    }

    private static void DrawArcs(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        int drawn = 0;
        int tick = (int)(em.ElapsedTime * FlickersPerSecond);

        foreach (var (entity, ship, pos) in em.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            if (drawn >= ArcEntityCap) return;
            if (em.HasComponent<Dead>(entity)) continue;
            if (!em.HasComponent<EcmDisabled>(entity)) continue;

            DrawArcsFor(entity, pos.Value, ship.Radius, tick, camX, camY, windowWidth, windowHeight);
            drawn++;
        }

        foreach (var (entity, mine, pos) in em.GetEntitiesWithComponents<EnemyMine, Position>())
        {
            if (drawn >= ArcEntityCap) return;
            if (!em.HasComponent<EcmDisabled>(entity)) continue;

            DrawArcsFor(entity, pos.Value, mine.Radius, tick, camX, camY, windowWidth, windowHeight);
            drawn++;
        }
    }

    // Three short jagged strips riding just outside the hull, each one drawn as a single line strip. The
    // starting bearing of each strip and the wobble of every point come from a hash of (entity, tick, strip),
    // so the arcs sit apart instead of stacking and the whole set redraws when the tick bucket rolls over.
    private static void DrawArcsFor(Entity entity, Vector2 worldPos, float hullRadius, int tick, float camX, float camY, int windowWidth, int windowHeight)
    {
        var (cx, cy) = RenderHelpers.ToScreen(worldPos, camX, camY, windowWidth, windowHeight);
        if (RenderHelpers.IsOffScreen(cx, cy, hullRadius * ArcRadiusFraction, windowWidth, windowHeight)) return;

        float reach = hullRadius * ArcRadiusFraction;
        float step = ArcSpan / (ArcPoints - 1);

        for (int arc = 0; arc < ArcsPerHull; arc++)
        {
            uint seed = Mix((uint)entity.Value * 2654435761u ^ (uint)tick * 97u ^ (uint)arc * 31u);
            float bearing = (seed & 0xFFFF) / 65535f * MathF.Tau;

            for (int point = 0; point < ArcPoints; point++)
            {
                uint wobble = Mix(seed + (uint)point * 97u);
                float angle = bearing + point * step + ((wobble & 0xFF) / 255f - 0.5f) * ArcAngleJitter;
                float radial = reach * (1f + ((wobble >> 8) & 0xFF) / 255f * ArcRadialJitter);
                _arcPoints[point] = new System.Numerics.Vector2(cx + MathF.Cos(angle) * radial, cy + MathF.Sin(angle) * radial);
            }

            Raylib.DrawLineStrip(_arcPoints, ArcPoints, Palette.EcmArc);
        }
    }

    private static void DrawWaves(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (_, wave, pos) in em.GetEntitiesWithComponents<EcmWave, Position>())
        {
            float radius = wave.CurrentRadius;
            if (radius <= WaveRingThickness) continue;

            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);
            if (RenderHelpers.IsOffScreen(cx, cy, radius, windowWidth, windowHeight)) continue;

            // Three fronts riding a little behind the leading one, so the wave reads as a front with a
            // thickness rather than as a single growing circle. `Palette.EcmWave` is already faint; the
            // wave's own life fades what is left of that as it reaches off-screen.
            float alpha = wave.Lifetime / wave.InitialLifetime;
            var center = new System.Numerics.Vector2(cx, cy);

            for (int ring = 0; ring < WaveRingCount; ring++)
            {
                float ringRadius = radius * (1f - ring * WaveRingInset);
                if (ringRadius <= WaveRingThickness) continue;

                Raylib.DrawRing(center, ringRadius - WaveRingThickness, ringRadius, 0f, 0f, RingSegments(ringRadius), Ui.WithAlpha(Palette.EcmWave, alpha));
            }
        }
    }

    // Enough segments that a ring 900 px across reads as a circle rather than a polygon, with a floor so the
    // first, smallest ring is still round.
    private static int RingSegments(float radius) => (int)MathF.Max(MinRingSegments, radius / RingSegmentPx);

    // xorshift: cheap, and enough of a scrambler that neighbouring entity ids and neighbouring ticks do not
    // draw the same arc.
    private static uint Mix(uint value)
    {
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        return value;
    }
}
