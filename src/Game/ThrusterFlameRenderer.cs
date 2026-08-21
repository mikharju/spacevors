using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

public static class ThrusterFlameRenderer
{
    const float ThrustBaseOffsetRatio = 1.0f;
    const float MaxThrustFlameLengthRatio = 1.2f;
    const float ThrustFlameHalfWidthRatio = 0.35f;
    const float TurnSideOffsetRatio = 0.6f;
    const float TurnFrontOffsetRatio = 0.8f;
    const float TurnRearOffsetRatio = 0.7f;
    const float MaxTurnFlameLengthRatio = 0.25f;
    const float TurnFlameHalfWidthRatio = 0.07f;
    const float MinFlameIntensity = 0.05f;
    const float MinVisibleFlameIntensity = 0.2f;
    const int FlameAlpha = 230;

    public static void Draw(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, player, pos, rot) in em.GetEntitiesWithComponents<Player, Position, Rotation>())
        {
            if (em.HasComponent<Dead>(entity)) continue;
            DrawPlayerThrustFlames(em, entity, pos.Value, rot.Angle, player, camX, camY, windowWidth, windowHeight);
            DrawTurnFlame(em, entity, pos.Value, rot.Angle, player.Radius, player.RotationSpeed, camX, camY, windowWidth, windowHeight);
        }

        foreach (var (entity, ship, pos, rot) in em.GetEntitiesWithComponents<EnemyShip, Position, Rotation>())
        {
            if (em.HasComponent<Dead>(entity)) continue;
            DrawEnemyThrustFlame(em, entity, pos.Value, ship.Radius, ship.Acceleration, camX, camY, windowWidth, windowHeight);
            DrawTurnFlame(em, entity, pos.Value, rot.Angle, ship.Radius, ship.TurnRate, camX, camY, windowWidth, windowHeight);
        }

        foreach (var (entity, mine, pos) in em.GetEntitiesWithComponents<EnemyMine, Position>())
        {
            if (!em.TryGetComponent<Velocity>(entity, out var vel)) continue;
            Vector2 v = vel.Value;
            float speed = v.Magnitude;
            if (speed < 1f || mine.Speed <= 0f) continue;

            var n = v.Normalized;
            var dir = new Vector2(-n.X, -n.Y);
            DrawThrustFlame(pos.Value + dir * mine.Radius * ThrustBaseOffsetRatio, dir, mine.Radius, speed, mine.Speed, camX, camY, windowWidth, windowHeight);
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

        float fwd = Vector2.Dot(a, forward);
        if (fwd > 0f)
        {
            var dir = new Vector2(-forward.X, -forward.Y);
            DrawThrustFlame(pos + dir * player.Radius * ThrustBaseOffsetRatio, dir, player.Radius, fwd, maxForce, camX, camY, windowWidth, windowHeight);
        }
        else if (fwd < 0f)
            DrawThrustFlame(pos + forward * player.Radius * ThrustBaseOffsetRatio, forward, player.Radius, -fwd, maxForce, camX, camY, windowWidth, windowHeight);

        float side = Vector2.Dot(a, right);
        if (side > 0f)
        {
            var dir = new Vector2(-right.X, -right.Y);
            DrawThrustFlame(pos + dir * player.Radius * ThrustBaseOffsetRatio, dir, player.Radius, side, maxForce, camX, camY, windowWidth, windowHeight);
        }
        else if (side < 0f)
            DrawThrustFlame(pos + right * player.Radius * ThrustBaseOffsetRatio, right, player.Radius, -side, maxForce, camX, camY, windowWidth, windowHeight);
    }

    private static void DrawEnemyThrustFlame(
        EntityManager em, Entity entity, Vector2 pos, float radius, float maxAccel,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        if (!em.TryGetComponent<Acceleration>(entity, out var accel)) return;
        Vector2 a = accel.Value;
        float mag = a.Magnitude;
        if (mag <= 0f || maxAccel <= 0f) return;

        var n = a.Normalized;
        var dir = new Vector2(-n.X, -n.Y);
        DrawThrustFlame(pos + dir * radius * ThrustBaseOffsetRatio, dir, radius, mag, maxAccel, camX, camY, windowWidth, windowHeight);
    }

    private static void DrawTurnFlame(
        EntityManager em, Entity entity, Vector2 pos, float angle, float radius, float maxTurnRate,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        if (maxTurnRate <= 0f || !em.TryGetComponent<AngularVelocity>(entity, out var angVel)) return;

        float turnIntensity = Math.Clamp(MathF.Abs(angVel.Value) / maxTurnRate, 0f, 1f);
        if (turnIntensity < MinFlameIntensity) return;
        int sideSign = angVel.Value > 0f ? 1 : -1;

        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        var forward = new Vector2(sin, -cos);
        var right = new Vector2(cos, sin);

        // Diagonal RCS pair: front thruster on the side opposite the turn, rear thruster on the turn's side.
        float length = radius * MaxTurnFlameLengthRatio * turnIntensity;
        float halfWidth = radius * TurnFlameHalfWidthRatio * turnIntensity;
        var color = FlameColor(turnIntensity);

        var frontPos = pos + forward * radius * TurnFrontOffsetRatio - right * radius * TurnSideOffsetRatio * sideSign;
        DrawFlame(frontPos, forward, length, halfWidth, color, camX, camY, windowWidth, windowHeight);

        var rearPos = pos - forward * radius * TurnRearOffsetRatio + right * radius * TurnSideOffsetRatio * sideSign;
        DrawFlame(rearPos, new Vector2(-forward.X, -forward.Y), length, halfWidth, color, camX, camY, windowWidth, windowHeight);
    }

    private static void DrawThrustFlame(
        Vector2 basePos, Vector2 dir, float radius, float force, float maxForce,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        if (force <= 0f || maxForce <= 0f) return;
        float ratio = force / maxForce;
        if (ratio < MinFlameIntensity) return;
        float intensity = Math.Clamp(ratio, MinVisibleFlameIntensity, 1f);

        DrawFlame(
            basePos, dir,
            radius * MaxThrustFlameLengthRatio * intensity,
            radius * ThrustFlameHalfWidthRatio * intensity,
            FlameColor(intensity), camX, camY, windowWidth, windowHeight);
    }

    private static void DrawFlame(
        Vector2 worldBase, Vector2 dir, float length, float halfWidth, Color color,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        if (length <= 0f || halfWidth <= 0f) return;

        var tip = worldBase + dir * length;
        float extent = length + halfWidth;
        float baseCx = worldBase.X - camX + windowWidth / 2f;
        float baseCy = worldBase.Y - camY + windowHeight / 2f;
        if (baseCx < -extent || baseCx > windowWidth + extent || baseCy < -extent || baseCy > windowHeight + extent) return;

        var perp = new Vector2(-dir.Y, dir.X);
        // Vertex order must be clockwise in screen space; counter-clockwise triangles are culled.
        var v0 = ToScreen(worldBase - perp * halfWidth, camX, camY, windowWidth, windowHeight);
        var v1 = ToScreen(worldBase + perp * halfWidth, camX, camY, windowWidth, windowHeight);
        var v2 = ToScreen(tip, camX, camY, windowWidth, windowHeight);
        Raylib.DrawTriangle(v0, v1, v2, color);
    }

    private static System.Numerics.Vector2 ToScreen(Vector2 world, float camX, float camY, int windowWidth, int windowHeight)
    {
        return new System.Numerics.Vector2(world.X - camX + windowWidth / 2f, world.Y - camY + windowHeight / 2f);
    }

    private static Color FlameColor(float intensity)
    {
        int green = (int)(140f + 90f * intensity);
        int blue = (int)(30f + 90f * intensity);
        return new Color(255, green, blue, FlameAlpha);
    }

}
