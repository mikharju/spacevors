using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems; // ShieldRules

namespace Spacevors.Game;

// What a shield looks like (plans/SHIELDS.md §7). The ring is an **event, not an aura**: it is drawn
// only while something is happening to the field. Full draws nothing but the heartbeat; empty draws
// nothing at all, because the shatter already announced it and the number lives in the HUD.
//
//   chipped, shock live   the charge arc, faint, no front — "spent, not refilling yet"
//   charging              the charge arc plus a bright front riding the charge, crawling as it refills
//   a hit the pool soaked a bright flash centred on the bearing the round came from
//   the pool broke        the ring pops out and fades, with the shards ShieldRenderer does not draw —
//                         those are entities of their own, ticked by EffectSystem
//   full                  a faint glint runs the ring once every few seconds
//
// The ring sits outside the hull's bounding circle and is a **draw-only** radius: no collision radius,
// no broad-phase index, no hurtbox moves with it (`SPACEVORS_DIAGNOSTIC=1`'s cyan overlay still draws
// the true shape inside it). The arc is centred on screen-up and symmetric — a nose-anchored arc would
// read as a directional shield, which this game does not have.
//
// Everything time-derived rides `em.ElapsedTime`, the world clock, with the phase mixed off the entity
// id the way EcmRenderer does: a seeded run draws the same frame twice, and shielded hulls to come do
// not pulse in lockstep.
public static class ShieldRenderer
{
    const float TopDeg = -90f;          // screen-up; the arc is drawn symmetric about it
    const float ThicknessFraction = 0.05f;
    const float MinThickness = 2f;

    const float FlashSpanDeg = 40f;
    const float FrontSpanDeg = 25f;
    const float BurstPop = 0.1f;        // how far out the ring pops when it breaks
    const float GlintPeriodSeconds = 5f;
    const float GlintSweepSeconds = 0.6f;
    const float GlintSpanDeg = 24f;

    // A shard's streak is its own trail, not a fixed length: this is the time it stands for, so a fast
    // piece leaves a longer streak than a slow one, the same read as a bullet's swept segment.
    const float ShardTrailSeconds = 0.06f;
    const float ShardThickness = 2f;

    public static void Draw(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, shield, pos) in em.GetEntitiesWithComponents<Shield, Position>())
        {
            if (em.HasComponent<Dead>(entity)) continue;
            float hullRadius = HullRadiusOf(em, entity);
            if (hullRadius <= 0f) continue;

            float ring = ShieldRules.RingRadius(hullRadius);
            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);
            if (RenderHelpers.IsOffScreen(cx, cy, ring * (1f + BurstPop), windowWidth, windowHeight)) continue;

            var center = new System.Numerics.Vector2(cx, cy);
            float thickness = MathF.Max(MinThickness, hullRadius * ThicknessFraction);
            float span = ArcFor(shield.Points, shield.Max);

            if (span > 0f)
            {
                Raylib.DrawRing(center, ring - thickness, ring, TopDeg - span * 0.5f, span, RingSegments(ring), Palette.ShieldArc);
                if (shield.Offline <= 0f) DrawRefillFront(center, ring, thickness, span);
            }
            else if (shield.Points >= shield.Max)
            {
                DrawHeartbeat(center, ring, thickness, entity, em.ElapsedTime);
            }

            if (em.TryGetComponent<ShieldFlash>(entity, out var flash))
            {
                float alpha = Math.Clamp(flash.Lifetime / ShieldRules.FlashSeconds, 0f, 1f);
                Raylib.DrawRing(center, ring - thickness * 2f, ring * 1.04f,
                    flash.BearingDeg - FlashSpanDeg * 0.5f, FlashSpanDeg, RingSegments(ring),
                    Ui.WithAlpha(Palette.ShieldLit, alpha));
            }

            if (em.TryGetComponent<ShieldBurst>(entity, out var burst))
            {
                float progress = 1f - Math.Clamp(burst.Lifetime / ShieldRules.BurstSeconds, 0f, 1f);
                float radius = ring * (1f + BurstPop * progress);
                Raylib.DrawRing(center, radius - thickness, radius, 0f, 0f, RingSegments(radius),
                    Ui.WithAlpha(Palette.ShieldLit, 1f - progress));
            }
        }

        DrawShards(em, camX, camY, windowWidth, windowHeight);
    }

    // The arc's span in degrees — the whole "invisible when full" rule in one line, and the reason it is
    // testable without a window: a whole pool says nothing, so the helper answers 0°.
    public static float ArcFor(float points, int max) => points >= max ? 0f : points / max * 360f;

    // The heartbeat: where the glint's leading edge is, in degrees, or null when this hull is between
    // glints. One sweep per period, its phase mixed off the entity id so two shielded hulls never pulse
    // together. Pure, so the determinism rule is tested rather than asserted.
    public static float? GlintAngleDeg(uint entityId, float elapsedSeconds)
    {
        float phase = (elapsedSeconds + GlintPhaseOffsetSeconds(entityId)) % GlintPeriodSeconds;
        return phase < GlintSweepSeconds ? TopDeg + phase / GlintSweepSeconds * 360f : null;
    }

    static float GlintPhaseOffsetSeconds(uint entityId)
        => (Mix(entityId) % 10000u) / 10000f * GlintPeriodSeconds;

    static void DrawRefillFront(System.Numerics.Vector2 center, float ring, float thickness, float span)
    {
        // The front rides the leading edge of the charge, so it crawls while the pool fills and is gone
        // the moment it is whole. It costs no state: it is a function of `Points`, hence deterministic.
        float leading = TopDeg + span * 0.5f;
        Raylib.DrawRing(center, ring - thickness * 1.6f, ring, leading - FrontSpanDeg * 0.5f, FrontSpanDeg,
            RingSegments(ring), Palette.ShieldLit);
    }

    static void DrawHeartbeat(System.Numerics.Vector2 center, float ring, float thickness, Entity entity, float elapsed)
    {
        float? angle = GlintAngleDeg(unchecked((uint)entity.Value), elapsed);
        if (angle is null) return;

        Raylib.DrawRing(center, ring - thickness, ring, angle.Value - GlintSpanDeg, GlintSpanDeg,
            RingSegments(ring), Ui.WithAlpha(Palette.ShieldLit, 0.55f));
    }

    static void DrawShards(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (_, shard, pos, velocity) in em.GetEntitiesWithComponents<ShieldShard, Position, Velocity>())
        {
            float speed = velocity.Value.Magnitude;
            if (speed <= 0.001f) continue;

            var (cx, cy) = RenderHelpers.ToScreen(pos.Value, camX, camY, windowWidth, windowHeight);
            if (RenderHelpers.IsOffScreen(cx, cy, ShardTrailSeconds * speed, windowWidth, windowHeight)) continue;

            var direction = velocity.Value.Normalized;
            float trail = ShardTrailSeconds * speed;
            float life = Math.Clamp(shard.Lifetime / shard.InitialLifetime, 0f, 1f);

            // White-hot while it is flying, the arc's colour as it goes: the shard is the field coming
            // apart, so it ends as the field it came from rather than as an ember.
            var color = MixColors(Palette.ShieldArc, Palette.ShieldLit, life);
            ShapeSprites.DrawStreak(
                new System.Numerics.Vector2(cx - direction.X * trail, cy - direction.Y * trail),
                new System.Numerics.Vector2(cx, cy),
                ShardThickness,
                Ui.WithAlpha(color, life));
        }
    }

    static float HullRadiusOf(EntityManager em, Entity entity) =>
        em.TryGetComponent<Player>(entity, out var player) ? player.Radius
        : em.TryGetComponent<EnemyShip>(entity, out var ship) ? ship.Radius
        : 0f;

    // Enough segments that a hull-sized ring reads as a circle and not a polygon, with a floor for the
    // smallest hulls. Same rule as EcmRenderer's wave.
    static int RingSegments(float radius) => (int)MathF.Max(16f, radius / 12f);

    static Color MixColors(Color from, Color to, float t) => new(
        (int)(from.R + (to.R - from.R) * t),
        (int)(from.G + (to.G - from.G) * t),
        (int)(from.B + (to.B - from.B) * t),
        (int)(from.A + (to.A - from.A) * t));

    // xorshift, so neighbouring entity ids do not share a phase. Copied from EcmRenderer rather than
    // pulled into a shared helper, because the two are separate renderers by design.
    static uint Mix(uint value)
    {
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        return value;
    }
}
