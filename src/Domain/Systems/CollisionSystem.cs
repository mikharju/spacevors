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
                ResolveCollision(em, entity, asteroid, isPlayer: true, playerEntity);
            }
        }

        for (int i = 0; i < asteroids.Count; i++)
        {
            for (int j = i + 1; j < asteroids.Count; j++)
            {
                var (aEntity, aAsteroid) = asteroids[i];
                var (bEntity, bAsteroid) = asteroids[j];
                ResolveCollision(em, aEntity, aAsteroid, isPlayer: false, Entity.Null, bEntity, bAsteroid);
            }
        }
    }

    private void ResolveCollision(
        EntityManager em,
        Entity aEntity,
        Asteroid aAst,
        bool isPlayer,
        Entity playerEntity,
        Entity? otherEntity = null,
        Asteroid? otherAsteroid = null)
    {
        var aPos = em.GetComponent<Position>(aEntity);

        Entity b;
        Asteroid bAst;
        if (isPlayer)
        {
            b = playerEntity;
            if (!em.HasComponent<Position>(b)) return;
            bAst = new Asteroid(0, 0, 18f);
        }
        else
        {
            b = otherEntity!.Value;
            bAst = otherAsteroid!.Value;
        }

        var bPos = em.GetComponent<Position>(b);
        float combinedRadius = aAst.Radius + bAst.Radius;

        var diff = bPos.Value - aPos.Value;
        float dist = diff.Magnitude;

        if (dist >= combinedRadius || dist < 0.001f) return;

        // Collision normal: from A toward B
        var normal = diff / dist;

        float aMass = aAst.Radius * aAst.Radius;
        float bMass = bAst.Radius * bAst.Radius;
        float invMassA = 1f / aMass;
        float invMassB = 1f / bMass;
        float totalInvMass = invMassA + invMassB;

        Vector2 aVel = em.HasComponent<Velocity>(aEntity)
            ? em.GetComponent<Velocity>(aEntity).Value
            : Vector2.Zero;
        Vector2 bVel = em.HasComponent<Velocity>(b)
            ? em.GetComponent<Velocity>(b).Value
            : Vector2.Zero;

        // Relative velocity: B relative to A (standard formulation)
        var relVel = bVel - aVel;
        float velAlongNormal = Vector2.Dot(relVel, normal);

        // Positional correction with slop — prevents deep penetration from persisting.
        // Only correct when overlap exceeds the small tolerance (slop).
        float overlap = combinedRadius - dist;
        float correctionMagnitude = Math.Max(overlap - Slop, 0f) * CorrectionPercent;
        if (correctionMagnitude > 0f && totalInvMass > 0f)
        {
            var correctionPerInvMass = normal * (correctionMagnitude / totalInvMass);
            em.AddComponent(aEntity, new Position(aPos.Value - correctionPerInvMass * invMassA));
            em.AddComponent(b, new Position(bPos.Value + correctionPerInvMass * invMassB));
        }

        // If already separating after positional correction, skip impulse.
        if (velAlongNormal > 0f) return;

        float restitution = isPlayer ? PlayerRestitution : AsteroidAsteroidRestitution;

        // Standard scalar impulse: j = -(1+e)*(v_rel·n)/(invMassA + invMassB)
        float j = -(1 + restitution) * velAlongNormal / totalInvMass;

        var impulse = normal * j;

        aVel -= impulse * invMassA;
        bVel += impulse * invMassB;

        em.AddComponent(aEntity, new Velocity(aVel));
        if (!em.HasComponent<Velocity>(b))
            em.AddComponent(b, new Velocity(bVel));
        else
            em.AddComponent(b, new Velocity(bVel));

        // Spin from tangential component of the corrected relative velocity.
        // Recompute after impulse so spin reflects post-collision motion.
        var correctedRelVel = bVel - aVel;
        var velNormalComponent = Vector2.Dot(correctedRelVel, normal);
        var tangentVel = correctedRelVel - normal * velNormalComponent;
        float tangentSpeed = tangentVel.Magnitude;

        if (em.HasComponent<AngularVelocity>(aEntity))
        {
            var aAngVel = em.GetComponent<AngularVelocity>(aEntity);
            em.AddComponent(aEntity, new AngularVelocity(aAngVel.Value + tangentSpeed * RotationFactor));
        }

        if (em.HasComponent<AngularVelocity>(b))
        {
            var bAngVel = em.GetComponent<AngularVelocity>(b);
            em.AddComponent(b, new AngularVelocity(bAngVel.Value - tangentSpeed * RotationFactor));
        }
    }
}
