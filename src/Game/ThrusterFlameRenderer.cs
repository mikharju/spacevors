using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

public static class ThrusterFlameRenderer
{
    // The base sits this much inside the hull's surface along the flame direction. Flames draw before
    // the hull, so an inset base hides under the paint and only the plume shows; without it the base
    // floats wherever the hurtbox reaches past the paint (plans/THRUSTER_FLAME_ANCHORS.md).
    const float FlameBaseInsetRatio = 0.10f;
    const float MaxThrustFlameLengthRatio = 1.0f;
    const float ThrustFlameHalfWidthRatio = 0.15f;
    const float TurnSideOffsetRatio = 0.6f;
    const float TurnFrontOffsetRatio = 0.8f;
    const float TurnRearOffsetRatio = 0.7f;
    const float MaxTurnFlameLengthRatio = 0.25f;
    const float TurnFlameHalfWidthRatio = 0.07f;

    // Absolute ceilings on the RCS pair. Their authored ratios look proportionate on a fighter and
    // shout from a dreadnought: an RCS puff is a puff at any hull size, so it stops growing at a
    // fixed screen size instead of at a fraction of the hull.
    const float TurnFlameMaxLength = 10f;
    const float TurnFlameMaxHalfWidth = 2.5f;

    const float MinFlameIntensity = 0.05f;
    const float MinVisibleFlameIntensity = 0.2f;
    const int FlameAlpha = 230;

    // Where a flame's base sits: on the hull's own surface along that direction, not at the bounding
    // radius. Shapes are authored in the sprite frame (nose at -v), so the unit world direction rotates
    // by -angle into it. Also the anchor for the flame's point light (LightGatherer).
    public static Vector2 FlameBase(Vector2 pos, HullShape shape, float radius, Vector2 dir, float angle)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        var local = new Vector2(dir.X * cos + dir.Y * sin, -dir.X * sin + dir.Y * cos);
        float reach = MathF.Max(0f, shape.SupportAlong(local) - FlameBaseInsetRatio) * radius;
        return pos + dir * reach;
    }

    public static void Draw(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, player, pos, rot) in em.GetEntitiesWithComponents<Player, Position, Rotation>())
        {
            if (em.HasComponent<Dead>(entity)) continue;
            DrawPlayerThrustFlames(em, entity, pos.Value, rot.Angle, player, camX, camY, windowWidth, windowHeight);
            DrawTurnFlame(em, entity, pos.Value, rot.Angle, player.Shape ?? HullShape.Circle, player.Radius, player.RotationSpeed, HullFaction.Player, camX, camY, windowWidth, windowHeight);
        }

        foreach (var (entity, ship, pos, rot) in em.GetEntitiesWithComponents<EnemyShip, Position, Rotation>())
        {
            if (em.HasComponent<Dead>(entity)) continue;
            // An ECM-jammed hull shows no fire at all — not the thrust plume, not the turn pair. A ship
            // drifting without light is the read that it has lost control, and the arcs say why.
            if (em.HasComponent<EcmDisabled>(entity)) continue;
            DrawEnemyThrustFlame(em, entity, pos.Value, rot.Angle, HullShapes.ForEnemy(ship.GraphicsId), ship.Radius, ship.Acceleration, camX, camY, windowWidth, windowHeight);
            DrawTurnFlame(em, entity, pos.Value, rot.Angle, HullShapes.ForEnemy(ship.GraphicsId), ship.Radius, ship.TurnRate, HullFaction.Enemy, camX, camY, windowWidth, windowHeight);
        }

        foreach (var (entity, mine, pos, vel) in em.GetEntitiesWithComponents<EnemyMine, Position, Velocity>())
        {
            // A jammed mine coasts instead of thrusting, so it shows no flame either.
            if (em.HasComponent<EcmDisabled>(entity)) continue;

            Vector2 v = vel.Value;
            float speed = v.Magnitude;
            if (speed < 1f || mine.Speed <= 0f) continue;

            var n = v.Normalized;
            var dir = new Vector2(-n.X, -n.Y);
            // A mine is a disc: support is its radius in every direction, so the base lands where it always did.
            DrawThrustFlame(FlameBase(pos.Value, HullShape.Circle, mine.Radius, dir, 0f), dir, mine.Radius, speed, mine.Speed, HullFaction.Neutral, camX, camY, windowWidth, windowHeight);
        }
    }

    private static void DrawPlayerThrustFlames(
        EntityManager em, Entity entity, Vector2 pos, float angle, Player player,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        if (!em.TryGetComponent<Acceleration>(entity, out var accel)) return;
        Vector2 a = accel.Value;
        if (a.Magnitude <= 0f) return;

        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        var forward = new Vector2(sin, -cos);
        var right = new Vector2(cos, sin);

        float maxForce = player.MaxThrustForce;
        var shape = player.Shape ?? HullShape.Circle;

        float fwd = Vector2.Dot(a, forward);
        if (fwd > 0f)
        {
            var dir = new Vector2(-forward.X, -forward.Y);
            DrawThrustFlame(FlameBase(pos, shape, player.Radius, dir, angle), dir, player.Radius, fwd, maxForce, HullFaction.Player, camX, camY, windowWidth, windowHeight);
        }
        else if (fwd < 0f)
            DrawThrustFlame(FlameBase(pos, shape, player.Radius, forward, angle), forward, player.Radius, -fwd, maxForce, HullFaction.Player, camX, camY, windowWidth, windowHeight);

        float side = Vector2.Dot(a, right);
        if (side > 0f)
        {
            var dir = new Vector2(-right.X, -right.Y);
            DrawThrustFlame(FlameBase(pos, shape, player.Radius, dir, angle), dir, player.Radius, side, maxForce, HullFaction.Player, camX, camY, windowWidth, windowHeight);
        }
        else if (side < 0f)
            DrawThrustFlame(FlameBase(pos, shape, player.Radius, right, angle), right, player.Radius, -side, maxForce, HullFaction.Player, camX, camY, windowWidth, windowHeight);
    }

    private static void DrawEnemyThrustFlame(
        EntityManager em, Entity entity, Vector2 pos, float angle, HullShape shape, float radius, float maxAccel,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        if (!em.TryGetComponent<Acceleration>(entity, out var accel)) return;
        Vector2 a = accel.Value;
        float mag = a.Magnitude;
        if (mag <= 0f || maxAccel <= 0f) return;

        var n = a.Normalized;
        var dir = new Vector2(-n.X, -n.Y);
        DrawThrustFlame(FlameBase(pos, shape, radius, dir, angle), dir, radius, mag, maxAccel, HullFaction.Enemy, camX, camY, windowWidth, windowHeight);
    }

    private static void DrawTurnFlame(
        EntityManager em, Entity entity, Vector2 pos, float angle, HullShape shape, float radius, float maxTurnRate,
        HullFaction faction, float camX, float camY, int windowWidth, int windowHeight)
    {
        if (maxTurnRate <= 0f || !em.TryGetComponent<AngularVelocity>(entity, out var angVel)) return;

        float turnIntensity = Math.Clamp(MathF.Abs(angVel.Value) / maxTurnRate, 0f, 1f);
        if (turnIntensity < MinFlameIntensity) return;
        int sideSign = angVel.Value > 0f ? 1 : -1;

        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        var forward = new Vector2(sin, -cos);
        var right = new Vector2(cos, sin);

        // Diagonal RCS pair: front thruster on the side opposite the turn, rear thruster on the turn's
        // side. Capped absolutely (see TurnFlameMaxLength): a big hull's turn read comes from the pair's
        // placement at the hull corners, never from the puffs' size.
        float length = MathF.Min(radius * MaxTurnFlameLengthRatio, TurnFlameMaxLength) * turnIntensity;
        float halfWidth = MathF.Min(radius * TurnFlameHalfWidthRatio, TurnFlameMaxHalfWidth) * turnIntensity;
        var color = FlameColor(faction, turnIntensity);

        // The authored mount directions survive; only their length becomes the hull's surface distance,
        // so the pair sits on a shaped hull's corners instead of floating off the bounding disc.
        var frontMount = (forward * TurnFrontOffsetRatio - right * TurnSideOffsetRatio * sideSign).Normalized;
        DrawFlame(FlameBase(pos, shape, radius, frontMount, angle), forward, length, halfWidth, color, camX, camY, windowWidth, windowHeight);

        var rearMount = (right * TurnSideOffsetRatio * sideSign - forward * TurnRearOffsetRatio).Normalized;
        DrawFlame(FlameBase(pos, shape, radius, rearMount, angle), new Vector2(-forward.X, -forward.Y), length, halfWidth, color, camX, camY, windowWidth, windowHeight);
    }

    private static void DrawThrustFlame(
        Vector2 basePos, Vector2 dir, float radius, float force, float maxForce, HullFaction faction,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        if (force <= 0f || maxForce <= 0f) return;
        float ratio = force / maxForce;
        if (ratio < MinFlameIntensity) return;
        float intensity = Math.Clamp(ratio, MinVisibleFlameIntensity, 1f);

        // The plume's **length** tracks the engine's actual output, unclamped at the bottom: leaning
        // on the throttle shows a stub and only a full burn reaches the full length, which is what
        // makes acceleration strength legible from the plume alone. **Width** and colour stay on the
        // clamped intensity: even the weakest flame has to read as fire, and a plume that narrowed
        // with output too would leave nothing to see at part throttle. The sim has no per-hull mass
        // (every enemy hull collides as one mass), so the hull's radius stays the size scale —
        // mass-proportionate means "the bigger engine, the longer the flame", never "the fatter".
        DrawFlame(
            basePos, dir,
            radius * MaxThrustFlameLengthRatio * MathF.Min(ratio, 1f),
            radius * ThrustFlameHalfWidthRatio * intensity,
            FlameColor(faction, intensity), camX, camY, windowWidth, windowHeight);
    }

    private static void DrawFlame(
        Vector2 worldBase, Vector2 dir, float length, float halfWidth, Color color,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        if (length <= 0f || halfWidth <= 0f) return;

        var tip = worldBase + dir * length;
        var (baseCx, baseCy) = RenderHelpers.ToScreen(worldBase, camX, camY, windowWidth, windowHeight);
        if (RenderHelpers.IsOffScreen(baseCx, baseCy, length + halfWidth, windowWidth, windowHeight)) return;

        var perp = new Vector2(-dir.Y, dir.X);
        // Vertex order must be clockwise in screen space; counter-clockwise triangles are culled.
        var (v0x, v0y) = RenderHelpers.ToScreen(worldBase - perp * halfWidth, camX, camY, windowWidth, windowHeight);
        var (v1x, v1y) = RenderHelpers.ToScreen(worldBase + perp * halfWidth, camX, camY, windowWidth, windowHeight);
        var (v2x, v2y) = RenderHelpers.ToScreen(tip, camX, camY, windowWidth, windowHeight);
        Raylib.DrawTriangle(
            new System.Numerics.Vector2(v0x, v0y),
            new System.Numerics.Vector2(v1x, v1y),
            new System.Numerics.Vector2(v2x, v2y),
            color);
    }

    // Engine fire carries the faction: a cool ion plume on the player, a warm chemical plume on every
    // hostile. Same shape, same alpha, only the hue moves — and it is the hue of the point light that the
    // same flame casts (LightGatherer), so a hull is lit by the colour of the fire coming out of it.
    private static Color FlameColor(HullFaction faction, float intensity) => faction == HullFaction.Player
        ? new Color((int)(70 + 80 * intensity), (int)(150 + 80 * intensity), 255, FlameAlpha)
        : new Color(255, (int)(140 + 90 * intensity), (int)(30 + 90 * intensity), FlameAlpha);

}
