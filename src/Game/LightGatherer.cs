using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

// Fills Lighting's per-frame point light list in priority order (highest first): the player's own
// thrusters, then explosions, then enemy thrusters and mines. Lights added later lose their slots when
// the list is full, so the player's hull keeps its glow even in a wall of fireballs - an explosion
// still reads bright on its own because the fireball sprite is drawn bright anyway.
public static class LightGatherer
{
    const float ExplosionLightRadiusScale = 2.5f; // light spills past the visible fireball
    const float ExplosionLightIntensity = 1.0f;
    const float ThrustLightRadiusRatio = 3.0f; // x ship radius
    const float ThrustLightIntensity = 0.8f; // dimmer than explosions (peak 1.0)
    const float MinThrustLightRatio = 0.1f; // ignore near-zero thrust

    // Engine flame burns blue-white, a fireball orange; the hull picks these up as coloured light
    // rather than one uniform wash (Lighting.uLightTints).
    static readonly System.Numerics.Vector3 ExplosionLightTint = new(1.0f, 0.55f, 0.30f);
    static readonly System.Numerics.Vector3 ThrustLightTint = new(0.55f, 0.78f, 1.0f);

    public static void Collect(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        CollectPlayerThrustLights(em, camX, camY, windowWidth, windowHeight);
        CollectExplosionLights(em, camX, camY, windowWidth, windowHeight);
        CollectEnemyThrustLights(em, camX, camY, windowWidth, windowHeight);
    }

    static void CollectExplosionLights(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, explosion, pos) in em.GetEntitiesWithComponents<Explosion, Position>())
        {
            if (Lighting.IsFull) return;

            Vector2 p = pos.Value;

            float lifeRatio = explosion.Lifetime / explosion.InitialLifetime;
            float radius = explosion.CurrentRadius * ExplosionLightRadiusScale;
            if (!IsInLightRange(p, radius, camX, camY, windowWidth, windowHeight)) continue;

            Lighting.AddLight(p, radius, lifeRatio * ExplosionLightIntensity, ExplosionLightTint);
        }
    }

    static void CollectPlayerThrustLights(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, player, pos, rot, accel) in em.GetEntitiesWithComponents<Player, Position, Rotation, Acceleration>())
        {
            if (Lighting.IsFull) return;
            if (em.HasComponent<Dead>(entity)) continue;

            // Main thrusters outrank side thrusters when the light list is full.
            float sin = (float)Math.Sin(rot.Angle);
            float cos = (float)Math.Cos(rot.Angle);
            Vector2 forwardDir = new Vector2(sin, -cos);

            float maxForce = player.MaxThrustForce;

            float mainAccel = Vector2.Dot(accel.Value, forwardDir);
            AddThrustLight(pos.Value, forwardDir * mainAccel, maxForce, player.Radius, camX, camY, windowWidth, windowHeight);
            AddThrustLight(pos.Value, accel.Value - forwardDir * mainAccel, maxForce, player.Radius, camX, camY, windowWidth, windowHeight);
        }
    }

    static void CollectEnemyThrustLights(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, ship, pos, accel) in em.GetEntitiesWithComponents<EnemyShip, Position, Acceleration>())
        {
            if (Lighting.IsFull) return;
            if (em.HasComponent<Dead>(entity)) continue;
            AddThrustLight(pos.Value, accel.Value, ship.Acceleration, ship.Radius, camX, camY, windowWidth, windowHeight);
        }

        foreach (var (entity, mine, pos, vel) in em.GetEntitiesWithComponents<EnemyMine, Position, Velocity>())
        {
            if (Lighting.IsFull) return;
            Vector2 v = vel.Value;
            float speed = v.Magnitude;
            if (speed < 1f || mine.Speed <= 0f) continue;
            AddThrustLight(pos.Value, v, mine.Speed, mine.Radius, camX, camY, windowWidth, windowHeight);
        }
    }

    // Light sits at the flame base: behind the hull, opposite the thrust direction.
    static void AddThrustLight(Vector2 pos, Vector2 accel, float maxAccel, float radius, float camX, float camY, int windowWidth, int windowHeight)
    {
        if (maxAccel <= 0f) return;
        float mag = accel.Magnitude;
        float ratio = mag / maxAccel;
        if (mag <= 0f || ratio < MinThrustLightRatio) return;

        var dir = new Vector2(-accel.Normalized.X, -accel.Normalized.Y);
        Vector2 lightPos = pos + dir * radius;
        float lightRadius = radius * ThrustLightRadiusRatio;
        if (!IsInLightRange(lightPos, lightRadius, camX, camY, windowWidth, windowHeight)) return;

        Lighting.AddLight(lightPos, lightRadius, Math.Clamp(ratio, 0f, 1f) * ThrustLightIntensity, ThrustLightTint);
    }

    static bool IsInLightRange(Vector2 worldPos, float radius, float camX, float camY, int windowWidth, int windowHeight)
    {
        var (cx, cy) = RenderHelpers.ToScreen(worldPos, camX, camY, windowWidth, windowHeight);
        return cx > -radius && cx < windowWidth + radius && cy > -radius && cy < windowHeight + radius;
    }
}
