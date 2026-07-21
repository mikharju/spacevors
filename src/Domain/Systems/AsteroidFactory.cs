using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public static class AsteroidFactory
{
    public static void AddAsteroidComponents(EntityManager em, Entity entity, Vector2 position, float speed, float angle, Random rand)
    {
        float aw = 40f + (float)rand.NextDouble() * 60f;
        float ah = 30f + (float)rand.NextDouble() * 50f;
        float ar = Math.Max(aw, ah) / 2f;
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(new Vector2((float)Math.Cos(angle) * speed, (float)Math.Sin(angle) * speed)));
        em.AddComponent(entity, new Rotation((float)(rand.NextDouble() * Math.PI * 2)));
        em.AddComponent(entity, new AngularVelocity((float)(rand.NextDouble() - 0.5f) * 1.5f));
        em.AddComponent(entity, new Asteroid(aw, ah, ar));
    }
}
