using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;

namespace Spacevors.Domain.Scenarios;

// Applies a scenario's start state on top of the world WorldFactory produced.
//
// Seeded bullets are placed as a volley that has already been flying for a while: ages are uniform
// along the stream, so distance travelled, remaining lifetime and speed variation match what
// TurretFiringSystem produces. That matters because collision cost depends on where the bullets are,
// not merely on how many exist.
public static class ScenarioWorld
{
    public static void Apply(EntityManager em, Entity playerEntity, ScenarioSpec spec)
    {
        var playerPos = em.GetComponent<Position>(playerEntity).Value;

        if (spec.SeededWeapon is { } weapon)
            SeedVolleyInFlight(em, playerPos, weapon, spec.BulletCount, spec.AimDirection, spec.StreamLength, em.Rng);

        if (!spec.WeaponFires)
            SilencePlayerTurrets(em);

        if (spec.PlayerHealthRatio < 1f)
            SetPlayerHealth(em, playerEntity, spec.PlayerHealthRatio);
    }

    private static void SeedVolleyInFlight(
        EntityManager em, Vector2 origin, WeaponType weapon, int count, Vector2 aim, float streamLength, Random rng)
    {
        float fullStream = weapon.Stats.AmmoSpeed * weapon.Stats.ShotLifetime;
        float stream = streamLength > 0f ? MathF.Min(streamLength, fullStream) : fullStream;

        for (int i = 0; i < count; i++)
        {
            Vector2 dir = SpawnPlacement.Rotate(aim, (float)(rng.NextDouble() - 0.5) * 2f * weapon.Stats.Scatter);
            float speed = weapon.Stats.AmmoSpeed * (1f + (float)(rng.NextDouble() - 0.5) * TurretFiringSystem.AmmoSpeedVariation);

            // Resample the distance until the bullet is clear of every asteroid: one placed inside a
            // rock is destroyed on the first tick, which is a transient and not steady-state cost.
            float travelled = 0f;
            for (int attempt = 0; attempt < ResampleAttempts; attempt++)
            {
                travelled = (float)rng.NextDouble() * stream;
                if (!OverlapsAsteroid(em, origin + dir * travelled)) break;
            }

            var spawn = origin + dir * TurretFiringSystem.AmmoSpawnOffset;
            var entity = em.CreateEntity();
            em.AddComponent(entity, new Position(spawn + dir * travelled));
            em.AddComponent(entity, new Velocity(dir * speed));
            em.AddComponent(entity, new Ammo(dir * speed, weapon.AmmoRadius, (fullStream - travelled) / speed,
                IsEnemy: false, weapon.Stats.Damage, weapon.Color));
        }
    }

    private const int ResampleAttempts = 4;
    private const float AsteroidClearance = 5f;

    private static bool OverlapsAsteroid(EntityManager em, Vector2 position)
    {
        foreach (var (_, asteroidPosition, asteroid) in em.GetEntitiesWithComponents<Position, Asteroid>())
        {
            float clearance = asteroid.Radius + AsteroidClearance;
            Vector2 offset = asteroidPosition.Value - position;
            if (offset.X * offset.X + offset.Y * offset.Y < clearance * clearance) return true;
        }
        return false;
    }

    // Player turrets are the ones carrying a TurretOffset; enemy ship turrets are not.
    private static void SilencePlayerTurrets(EntityManager em)
    {
        foreach (var (entity, _, _) in em.GetEntitiesWithComponents<Turret, TurretOffset>().ToList())
            em.RemoveComponent<Turret>(entity);
    }

    private static void SetPlayerHealth(EntityManager em, Entity playerEntity, float ratio)
    {
        var health = em.GetComponent<Health>(playerEntity);
        int current = Math.Max(1, (int)MathF.Round(health.Max * ratio, MidpointRounding.AwayFromZero));
        em.AddComponent(playerEntity, new Health(current, health.Max));
    }
}
