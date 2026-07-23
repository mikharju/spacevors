using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class EffectSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        var sparks = em.GetEntitiesWithComponents<Spark>().ToList();
        foreach (var (entity, spark) in sparks)
        {
            if (!em.HasComponent<Spark>(entity)) continue;

            var newLifetime = spark.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                em.DestroyEntity(entity);
            }
            else
            {
                em.AddComponent(entity, new Spark(newLifetime));
            }
        }

        var explosions = em.GetEntitiesWithComponents<Explosion>().ToList();
        foreach (var (entity, explosion) in explosions)
        {
            if (!em.HasComponent<Explosion>(entity)) continue;

            var newLifetime = explosion.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                em.DestroyEntity(entity);
            }
            else
            {
                em.AddComponent(entity, new Explosion(explosion.Radius, newLifetime));
            }
        }

        var greenSparks = em.GetEntitiesWithComponents<GreenSpark>().ToList();
        foreach (var (entity, spark) in greenSparks)
        {
            if (!em.HasComponent<GreenSpark>(entity)) continue;

            var newLifetime = spark.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                em.DestroyEntity(entity);
            }
            else
            {
                em.AddComponent(entity, new GreenSpark(newLifetime));
            }
        }

        var blueSparks = em.GetEntitiesWithComponents<BlueSpark>().ToList();
        foreach (var (entity, spark) in blueSparks)
        {
            if (!em.HasComponent<BlueSpark>(entity)) continue;

            var newLifetime = spark.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                em.DestroyEntity(entity);
            }
            else
            {
                em.AddComponent(entity, new BlueSpark(newLifetime));
            }
        }

        var debugMarkers = em.GetEntitiesWithComponents<DebugMarker>().ToList();
        foreach (var (entity, marker) in debugMarkers)
        {
            if (!em.HasComponent<DebugMarker>(entity)) continue;

            var newLifetime = marker.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                em.DestroyEntity(entity);
            }
            else
            {
                em.AddComponent(entity, new DebugMarker(newLifetime));
            }
        }
    }
}
