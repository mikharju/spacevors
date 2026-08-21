using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

// Fills Lighting's per-frame point light list in priority order (highest first): explosions,
// player main thrusters (forward/backward), player side thrusters, enemy thrusters.
// Lights added later lose their slots when the list is full.
public static class LightGatherer
{
    const float ExplosionLightRadiusScale = 2.5f; // light spills past the visible fireball
    const float ExplosionLightIntensity = 1.0f;
    const float ThrustLightRadiusRatio = 3.0f; // x ship radius
    const float ThrustLightIntensity = 0.8f; // dimmer than explosions (peak 1.0)
    const float MinThrustLightRatio = 0.1f; // ignore near-zero thrust

    public static void Collect(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        CollectExplosionLights(em, camX, camY, windowWidth, windowHeight);
        CollectThrustLights(em);
    }

    static void CollectExplosionLights(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, explosion) in em.GetEntitiesWithComponents<Explosion>())
        {
            var pos = em.GetComponent<Position>(entity);
            Vector2 p = pos.Value;

            float lifeRatio = explosion.Lifetime / explosion.InitialLifetime;
            float radius = explosion.CurrentRadius * ExplosionLightRadiusScale;
            if (!IsInLightRange(p, radius, camX, camY, windowWidth, windowHeight)) continue;

            Lighting.AddLight(p, radius, lifeRatio * ExplosionLightIntensity);
        }
    }

    static void CollectThrustLights(EntityManager em)
    {
        foreach (var (entity, player, pos, rot) in em.GetEntitiesWithComponents<Player, Position, Rotation>())
        {
            if (em.HasComponent<Dead>(entity)) continue;
            if (!em.TryGetComponent<Acceleration>(entity, out var accel)) continue;

            // Main thrusters outrank side thrusters when the light list is full.
            float sin = (float)Math.Sin(rot.Angle);
            float cos = (float)Math.Cos(rot.Angle);
            Vector2 forwardDir = new Vector2(sin, -cos);

            float maxForce = player.MaxThrustForce;

            float mainAccel = Vector2.Dot(accel.Value, forwardDir);
            AddThrustLight(pos.Value, forwardDir * mainAccel, maxForce, player.Radius);
            AddThrustLight(pos.Value, accel.Value - forwardDir * mainAccel, maxForce, player.Radius);
        }

        foreach (var (entity, ship, pos) in em.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            if (em.HasComponent<Dead>(entity)) continue;
            if (!em.TryGetComponent<Acceleration>(entity, out var accel)) continue;
            AddThrustLight(pos.Value, accel.Value, ship.Acceleration, ship.Radius);
        }

        foreach (var (entity, mine, pos) in em.GetEntitiesWithComponents<EnemyMine, Position>())
        {
            if (!em.TryGetComponent<Velocity>(entity, out var vel)) continue;
            Vector2 v = vel.Value;
            float speed = v.Magnitude;
            if (speed < 1f || mine.Speed <= 0f) continue;
            AddThrustLight(pos.Value, v, mine.Speed, mine.Radius);
        }
    }

    // Light sits at the flame base: behind the hull, opposite the thrust direction.
    static void AddThrustLight(Vector2 pos, Vector2 accel, float maxAccel, float radius)
    {
        if (maxAccel <= 0f) return;
        float mag = accel.Magnitude;
        float ratio = mag / maxAccel;
        if (mag <= 0f || ratio < MinThrustLightRatio) return;

        var dir = new Vector2(-accel.Normalized.X, -accel.Normalized.Y);
        Lighting.AddLight(pos + dir * radius, radius * ThrustLightRadiusRatio, Math.Clamp(ratio, 0f, 1f) * ThrustLightIntensity);
    }

    static bool IsInLightRange(Vector2 worldPos, float radius, float camX, float camY, int windowWidth, int windowHeight)
    {
        float cx = worldPos.X - camX + windowWidth / 2f;
        float cy = worldPos.Y - camY + windowHeight / 2f;
        return cx > -radius && cx < windowWidth + radius && cy > -radius && cy < windowHeight + radius;
    }
}
