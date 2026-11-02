using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
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
    // The player's glow is as bright as an explosion's; a hostile's is dimmer, because every hostile in a
    // wave thrusts at once and the 16 light slots fill with engine glow: at full strength a crowd turned
    // itself into one orange field and the hulls lost their shape.
    const float PlayerThrustLightIntensity = 0.8f;
    const float EnemyThrustLightIntensity = 0.5f;
    const float MinThrustLightRatio = 0.1f; // ignore near-zero thrust

    // A fireball is orange. An engine's light is its hull's faction colour, because that light lands on
    // the hull it comes out of: the player's glow is cool and the enemy's is warm, matching the flame the
    // ThrusterFlameRenderer draws. One blue-white glow for both used to wash every hostile hull cyan -
    // radius 3x the hull at up to 0.8 intensity, on top of the ambient - which is what buried the enemy
    // art's own red and green (plans/SHIP_TINT_PALETTE.md).
    static readonly System.Numerics.Vector3 ExplosionLightTint = new(1.0f, 0.55f, 0.30f);
    static readonly System.Numerics.Vector3 PlayerThrustLightTint = new(0.55f, 0.78f, 1.0f);
    static readonly System.Numerics.Vector3 EnemyThrustLightTint = new(1.0f, 0.66f, 0.42f);

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
            var shape = player.Shape ?? HullShape.Circle;

            float mainAccel = Vector2.Dot(accel.Value, forwardDir);
            AddThrustLight(pos.Value, forwardDir * mainAccel, maxForce, shape, player.Radius, rot.Angle, PlayerThrustLightTint, PlayerThrustLightIntensity, camX, camY, windowWidth, windowHeight);
            AddThrustLight(pos.Value, accel.Value - forwardDir * mainAccel, maxForce, shape, player.Radius, rot.Angle, PlayerThrustLightTint, PlayerThrustLightIntensity, camX, camY, windowWidth, windowHeight);
        }
    }

    static void CollectEnemyThrustLights(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, ship, pos, rot, accel) in em.GetEntitiesWithComponents<EnemyShip, Position, Rotation, Acceleration>())
        {
            if (Lighting.IsFull) return;
            if (em.HasComponent<Dead>(entity)) continue;
            AddThrustLight(pos.Value, accel.Value, ship.Acceleration, HullShapes.ForEnemy(ship.GraphicsId), ship.Radius, rot.Angle, EnemyThrustLightTint, EnemyThrustLightIntensity, camX, camY, windowWidth, windowHeight);
        }

        foreach (var (entity, mine, pos, vel) in em.GetEntitiesWithComponents<EnemyMine, Position, Velocity>())
        {
            if (Lighting.IsFull) return;
            Vector2 v = vel.Value;
            float speed = v.Magnitude;
            if (speed < 1f || mine.Speed <= 0f) continue;
            AddThrustLight(pos.Value, v, mine.Speed, HullShape.Circle, mine.Radius, 0f, EnemyThrustLightTint, EnemyThrustLightIntensity, camX, camY, windowWidth, windowHeight);
        }
    }

    // Light sits at the flame base: on the hull's surface opposite the thrust, exactly where
    // ThrusterFlameRenderer starts the plume, so a hull is lit where its fire actually is.
    static void AddThrustLight(Vector2 pos, Vector2 accel, float maxAccel, HullShape shape, float radius, float angle, System.Numerics.Vector3 tint, float intensity, float camX, float camY, int windowWidth, int windowHeight)
    {
        if (maxAccel <= 0f) return;
        float mag = accel.Magnitude;
        float ratio = mag / maxAccel;
        if (mag <= 0f || ratio < MinThrustLightRatio) return;

        var dir = new Vector2(-accel.Normalized.X, -accel.Normalized.Y);
        Vector2 lightPos = ThrusterFlameRenderer.FlameBase(pos, shape, radius, dir, angle);
        float lightRadius = radius * ThrustLightRadiusRatio;
        if (!IsInLightRange(lightPos, lightRadius, camX, camY, windowWidth, windowHeight)) return;

        Lighting.AddLight(lightPos, lightRadius, Math.Clamp(ratio, 0f, 1f) * intensity, tint);
    }

    static bool IsInLightRange(Vector2 worldPos, float radius, float camX, float camY, int windowWidth, int windowHeight)
    {
        var (cx, cy) = RenderHelpers.ToScreen(worldPos, camX, camY, windowWidth, windowHeight);
        return cx > -radius && cx < windowWidth + radius && cy > -radius && cy < windowHeight + radius;
    }
}
