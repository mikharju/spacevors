using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

public static class ThrusterFlameRenderer
{
    const float ThrustBaseOffsetRatio = 0.75f;
    const float MaxThrustFlameLengthRatio = 1.2f;
    const float ThrustFlameHalfWidthRatio = 0.35f;
    const float TurnSideOffsetRatio = 0.6f;
    const float MaxTurnFlameLengthRatio = 0.8f;
    const float TurnFlameHalfWidthRatio = 0.25f;
    const float MinFlameIntensity = 0.1f;
    const int FlameAlpha = 230;

    static readonly Dictionary<Entity, float> PrevAngles = new();
    static readonly List<Entity> StaleKeys = new();
    static float LastDrawTime = -1f;

    public static void Reset()
    {
        PrevAngles.Clear();
        LastDrawTime = -1f;
    }

    public static void Draw(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        float now = (float)Raylib.GetTime();
        float dt = now - LastDrawTime;
        LastDrawTime = now;

        foreach (var (entity, player, pos, rot) in em.GetEntitiesWithComponents<Player, Position, Rotation>())
        {
            if (em.HasComponent<Dead>(entity)) continue;
            DrawShipFlames(em, entity, pos.Value, rot.Angle, player.Radius, player.Thrust * player.Boost, player.RotationSpeed, dt, camX, camY, windowWidth, windowHeight);
        }

        foreach (var (entity, ship, pos, rot) in em.GetEntitiesWithComponents<EnemyShip, Position, Rotation>())
        {
            if (em.HasComponent<Dead>(entity)) continue;
            DrawShipFlames(em, entity, pos.Value, rot.Angle, ship.Radius, ship.Acceleration, ship.TurnRate, dt, camX, camY, windowWidth, windowHeight);
        }

        foreach (var (entity, mine, pos) in em.GetEntitiesWithComponents<EnemyMine, Position>())
        {
            if (!em.TryGetComponent<Velocity>(entity, out var vel)) continue;
            Vector2 v = vel.Value;
            float speed = v.Magnitude;
            if (speed < 1f || mine.Speed <= 0f) continue;

            float intensity = Math.Clamp(speed / mine.Speed, 0f, 1f);
            if (intensity < MinFlameIntensity) continue;

            var n = v.Normalized;
            var dir = new Vector2(-n.X, -n.Y);
            DrawFlame(
                pos.Value + dir * mine.Radius * ThrustBaseOffsetRatio,
                dir,
                mine.Radius * MaxThrustFlameLengthRatio * intensity,
                mine.Radius * ThrustFlameHalfWidthRatio * intensity,
                FlameColor(intensity), camX, camY, windowWidth, windowHeight);
        }

        PruneStaleKeys(em);
    }

    private static void DrawShipFlames(
        EntityManager em, Entity entity, Vector2 pos, float angle,
        float radius, float maxAccel, float maxTurnRate, float dt,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        if (em.TryGetComponent<Acceleration>(entity, out var accel))
        {
            Vector2 a = accel.Value;
            float mag = a.Magnitude;
            if (mag > 0f && maxAccel > 0f)
            {
                float intensity = Math.Clamp(mag / maxAccel, 0f, 1f);
                if (intensity >= MinFlameIntensity)
                {
                    var n = a.Normalized;
                    var dir = new Vector2(-n.X, -n.Y);
                    DrawFlame(
                        pos + dir * radius * ThrustBaseOffsetRatio,
                        dir,
                        radius * MaxThrustFlameLengthRatio * intensity,
                        radius * ThrustFlameHalfWidthRatio * intensity,
                        FlameColor(intensity), camX, camY, windowWidth, windowHeight);
                }
            }
        }

        if (dt > 0f && maxTurnRate > 0f)
        {
            float turnIntensity = 0f;
            int sideSign = 0;

            if (PrevAngles.TryGetValue(entity, out float prevAngle))
            {
                float delta = NormalizeAngle(angle - prevAngle);
                float turnRate = delta / dt;
                turnIntensity = Math.Clamp(Math.Abs(turnRate) / maxTurnRate, 0f, 1f);
                sideSign = turnRate > 0f ? 1 : -1;
            }

            if (turnIntensity >= MinFlameIntensity && sideSign != 0)
            {
                float cos = MathF.Cos(angle);
                float sin = MathF.Sin(angle);
                var forward = new Vector2(sin, -cos);
                var right = new Vector2(cos, sin);

                DrawFlame(
                    pos + right * (radius * TurnSideOffsetRatio) * sideSign,
                    forward,
                    radius * MaxTurnFlameLengthRatio * turnIntensity,
                    radius * TurnFlameHalfWidthRatio * turnIntensity,
                    FlameColor(turnIntensity), camX, camY, windowWidth, windowHeight);
            }
        }

        PrevAngles[entity] = angle;
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

    private static void PruneStaleKeys(EntityManager em)
    {
        foreach (var key in PrevAngles.Keys)
        {
            if (!em.HasComponent<Rotation>(key)) StaleKeys.Add(key);
        }

        for (int i = 0; i < StaleKeys.Count; i++)
            PrevAngles.Remove(StaleKeys[i]);

        StaleKeys.Clear();
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= 2f * MathF.PI;
        while (angle < -MathF.PI) angle += 2f * MathF.PI;
        return angle;
    }
}
