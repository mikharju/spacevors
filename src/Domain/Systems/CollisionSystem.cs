using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class CollisionSystem : GameSystem
{
    private const float PlayerRestitution = 0.2f;
    private const float AsteroidAsteroidRestitution = 0.4f;
    private const float RotationFactor = 0.03f;
    private const float CorrectionPercent = 0.8f;
    private const float Slop = 0.01f;

    public override void Update(EntityManager em, float deltaTime)
    {
        var asteroids = em.GetEntitiesWithComponents<Asteroid>().ToList();
        var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        if (hasPlayer)
        {
            foreach (var (entity, asteroid) in asteroids)
            {
                ResolveCircleVsCircle(em, entity, asteroid, playerEntity);
            }
        }

        for (int i = 0; i < asteroids.Count; i++)
        {
            for (int j = i + 1; j < asteroids.Count; j++)
            {
                var (aEntity, aAsteroid) = asteroids[i];
                var (bEntity, bAsteroid) = asteroids[j];
                ResolveCircleVsCircle(em, aEntity, aAsteroid, bEntity, bAsteroid);
            }
        }
    }

    private void ResolveCircleVsCircle(
        EntityManager em,
        Entity aEntity,
        Asteroid aAst,
        Entity bEntity,
        Asteroid? bAst = null)
    {
        var aPos = em.GetComponent<Position>(aEntity);
        var bPos = em.GetComponent<Position>(bEntity);

        float aRadius;
        float bRadius;
        bool isAsteroidVsAsteroid;

        if (bAst != null)
        {
            // Asteroid vs asteroid
            aRadius = aAst.Radius;
            bRadius = bAst.Value.Radius;
            isAsteroidVsAsteroid = true;
        }
        else
        {
            // Player vs asteroid (bEntity is player)
            aRadius = aAst.Radius;
            var playerComp = em.GetComponent<Player>(bEntity);
            bRadius = playerComp.Radius;
            isAsteroidVsAsteroid = false;
        }

        var diff = bPos.Value - aPos.Value;
        float distSq = diff.X * diff.X + diff.Y * diff.Y;
        float radiusSum = aRadius + bRadius;

        if (distSq >= radiusSum * radiusSum || distSq < 0.001f) return;

        float dist = (float)Math.Sqrt(distSq);
        var normal = diff / dist;

        float penetration = radiusSum - dist;

        float aMass = MathF.PI * aRadius * aRadius;
        float bMass = isAsteroidVsAsteroid ? MathF.PI * bRadius * bRadius : MathF.PI * bRadius * bRadius;
        float invMassA = 1f / aMass;
        float invMassB = 1f / bMass;
        float totalInvMass = invMassA + invMassB;

        Vector2 aVel = em.HasComponent<Velocity>(aEntity)
            ? em.GetComponent<Velocity>(aEntity).Value
            : Vector2.Zero;
        Vector2 bVel = em.HasComponent<Velocity>(bEntity)
            ? em.GetComponent<Velocity>(bEntity).Value
            : Vector2.Zero;

        var relVel = bVel - aVel;
        float velAlongNormal = Vector2.Dot(relVel, normal);

        // Positional correction
        float correctionMagnitude = Math.Max(penetration - Slop, 0f) * CorrectionPercent;
        if (correctionMagnitude > 0f && totalInvMass > 0f)
        {
            var correctionPerInvMass = normal * (correctionMagnitude / totalInvMass);
            em.AddComponent(aEntity, new Position(aPos.Value - correctionPerInvMass * invMassA));
            em.AddComponent(bEntity, new Position(bPos.Value + correctionPerInvMass * invMassB));
        }

        if (velAlongNormal > 0f) return;

        float restitution = isAsteroidVsAsteroid ? AsteroidAsteroidRestitution : PlayerRestitution;
        float j = -(1 + restitution) * velAlongNormal / totalInvMass;
        var impulse = normal * j;

        aVel -= impulse * invMassA;
        bVel += impulse * invMassB;

        em.AddComponent(aEntity, new Velocity(aVel));
        em.AddComponent(bEntity, new Velocity(bVel));

        // Spin from tangential component
        var correctedRelVel = bVel - aVel;
        var velNormalComponent = Vector2.Dot(correctedRelVel, normal);
        var tangentVel = correctedRelVel - normal * velNormalComponent;
        float tangentSpeed = tangentVel.Magnitude;

        if (em.HasComponent<AngularVelocity>(aEntity))
        {
            var aAngVel = em.GetComponent<AngularVelocity>(aEntity);
            em.AddComponent(aEntity, new AngularVelocity(aAngVel.Value + tangentSpeed * RotationFactor));
        }

        if (em.HasComponent<AngularVelocity>(bEntity))
        {
            var bAngVel = em.GetComponent<AngularVelocity>(bEntity);
            em.AddComponent(bEntity, new AngularVelocity(bAngVel.Value - tangentSpeed * RotationFactor));
        }
    }
}
