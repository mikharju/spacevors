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
    }
}
