using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public static class AsteroidFactory
{
    public static void AddAsteroidComponents(EntityManager em, Entity entity, Vector2 position, float speed, float angle, Random rand)
    {
        bool isSmall = rand.NextDouble() < 0.5;
        float radius = isSmall ? (35f + (float)rand.NextDouble() * 15f) : (125f + (float)rand.NextDouble() * 125f);
        byte variant = (byte)(isSmall ? rand.Next(Asteroid.SmallVariantCount) : rand.Next(Asteroid.LargeVariantCount));
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(new Vector2((float)Math.Cos(angle) * speed, (float)Math.Sin(angle) * speed)));
        em.AddComponent(entity, new Rotation((float)(rand.NextDouble() * Math.PI * 2)));
        em.AddComponent(entity, new AngularVelocity((float)(rand.NextDouble() - 0.5f) * 1.5f));
        em.AddComponent(entity, new Asteroid(isSmall, radius, variant));
    }
}
