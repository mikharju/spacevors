using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public static class AsteroidFactory
{
    // A smashed small rock leaves a cloud of grey puffs instead of a fireball: it breaks apart, it
    // does not detonate. The puffs keep part of the rock's drift and jitter apart from each other.
    private const int BreakupPuffCount = 16;
    private const float BreakupPuffLifetime = 2.2f;
    private const float BreakupPuffRadiusFactor = 0.42f;
    private const float BreakupPuffInheritedFraction = 0.4f;
    private const float BreakupPuffJitterSpeed = 40f;

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

        // Health is what makes a rock shootable: the large scenery has none, so a hit on it stays an
        // obstacle hit and no bookkeeping follows.
        if (isSmall)
            em.AddComponent(entity, new Health(Asteroid.SmallHealth, Asteroid.SmallHealth));
    }

    public static void SpawnBreakupSmoke(CommandBuffer commands, Vector2 position, Vector2 velocity, float radius, Random rng)
    {
        float puffRadius = radius * BreakupPuffRadiusFactor;

        for (int i = 0; i < BreakupPuffCount; i++)
        {
            // Puffs start scattered across the rock's own disc, so the cloud is rock-sized.
            Vector2 offset = new Vector2(
                (float)(rng.NextDouble() * 2f - 1f),
                (float)(rng.NextDouble() * 2f - 1f)) * radius;

            Vector2 jitter = new Vector2(
                (float)(rng.NextDouble() * 2f - 1f),
                (float)(rng.NextDouble() * 2f - 1f)) * BreakupPuffJitterSpeed;

            commands.AddEntity(
                new Position(position + offset),
                new Velocity(velocity * BreakupPuffInheritedFraction + jitter),
                new SmokePuff(BreakupPuffLifetime, BreakupPuffLifetime, puffRadius));
        }
    }
}
