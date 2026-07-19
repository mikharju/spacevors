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
                ResolveAABBvsCircle(em, entity, asteroid, playerEntity);
            }
        }

        for (int i = 0; i < asteroids.Count; i++)
        {
            for (int j = i + 1; j < asteroids.Count; j++)
            {
                var (aEntity, aAsteroid) = asteroids[i];
                var (bEntity, bAsteroid) = asteroids[j];
                ResolveAABBvsAABB(em, aEntity, aAsteroid, bEntity, bAsteroid);
            }
        }
    }

    private void ResolveAABBvsAABB(
        EntityManager em,
        Entity aEntity,
        Asteroid aAst,
        Entity bEntity,
        Asteroid bAst)
    {
        var aPos = em.GetComponent<Position>(aEntity);
        var bPos = em.GetComponent<Position>(bEntity);

        float aHalfW = aAst.Width / 2f;
        float aHalfH = aAst.Height / 2f;
        float bHalfW = bAst.Width / 2f;
        float bHalfH = bAst.Height / 2f;

        // AABB bounds (center-based)
        float aMinX = (float)aPos.Value.X - aHalfW;
        float aMaxX = (float)aPos.Value.X + aHalfW;
        float aMinY = (float)aPos.Value.Y - aHalfH;
        float aMaxY = (float)aPos.Value.Y + aHalfH;

        float bMinX = (float)bPos.Value.X - bHalfW;
        float bMaxX = (float)bPos.Value.X + bHalfW;
        float bMinY = (float)bPos.Value.Y - bHalfH;
        float bMaxY = (float)bPos.Value.Y + bHalfH;

        // Check AABB overlap on both axes
        float overlapX = Math.Min(aMaxX, bMaxX) - Math.Max(aMinX, bMinX);
        float overlapY = Math.Min(aMaxY, bMaxY) - Math.Max(aMinY, bMinY);

        if (overlapX <= 0f || overlapY <= 0f) return;

        // Minimum translation direction: axis with smallest overlap
        Vector2 normal;
        float penetration;
        if (overlapX < overlapY)
        {
            penetration = overlapX;
            var diff = bPos.Value - aPos.Value;
            normal = Math.Abs(diff.X) > 0.001f ? new Vector2(Math.Sign(diff.X), 0f) : new Vector2(1, 0);
        }
        else
        {
            penetration = overlapY;
            var diff = bPos.Value - aPos.Value;
            normal = Math.Abs(diff.Y) > 0.001f ? new Vector2(0f, Math.Sign(diff.Y)) : new Vector2(0, 1);
        }

        float aMass = aAst.Width * aAst.Height;
        float bMass = bAst.Width * bAst.Height;
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

        float j = -(1 + AsteroidAsteroidRestitution) * velAlongNormal / totalInvMass;
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

    private void ResolveAABBvsCircle(
        EntityManager em,
        Entity aEntity,
        Asteroid aAst,
        Entity playerEntity)
    {
        var aPos = em.GetComponent<Position>(aEntity);
        var bPos = em.GetComponent<Position>(playerEntity);

        float aHalfW = aAst.Width / 2f;
        float aHalfH = aAst.Height / 2f;

        // AABB bounds (center-based)
        float aMinX = (float)aPos.Value.X - aHalfW;
        float aMaxX = (float)aPos.Value.X + aHalfW;
        float aMinY = (float)aPos.Value.Y - aHalfH;
        float aMaxY = (float)aPos.Value.Y + aHalfH;

        // Find closest point on AABB to circle center
        float closestX = Math.Max(aMinX, Math.Min((float)bPos.Value.X, aMaxX));
        float closestY = Math.Max(aMinY, Math.Min((float)bPos.Value.Y, aMaxY));

        var diff = bPos.Value - new Vector2(closestX, closestY);
        float distSq = diff.X * diff.X + diff.Y * diff.Y;
        float radius = 18f;

        if (distSq >= radius * radius || distSq < 0.001f) return;

        float dist = (float)Math.Sqrt(distSq);
        var normal = diff / dist;

        // Penetration: how far the circle overlaps the AABB edge
        float penetration = radius - dist;

        float aMass = aAst.Width * aAst.Height;
        float bMass = 18f * 18f;
        float invMassA = 1f / aMass;
        float invMassB = 1f / bMass;
        float totalInvMass = invMassA + invMassB;

        Vector2 aVel = em.HasComponent<Velocity>(aEntity)
            ? em.GetComponent<Velocity>(aEntity).Value
            : Vector2.Zero;
        Vector2 bVel = em.HasComponent<Velocity>(playerEntity)
            ? em.GetComponent<Velocity>(playerEntity).Value
            : Vector2.Zero;

        var relVel = bVel - aVel;
        float velAlongNormal = Vector2.Dot(relVel, normal);

        // Positional correction
        float correctionMagnitude = Math.Max(penetration - Slop, 0f) * CorrectionPercent;
        if (correctionMagnitude > 0f && totalInvMass > 0f)
        {
            var correctionPerInvMass = normal * (correctionMagnitude / totalInvMass);
            em.AddComponent(aEntity, new Position(aPos.Value - correctionPerInvMass * invMassA));
            em.AddComponent(playerEntity, new Position(bPos.Value + correctionPerInvMass * invMassB));
        }

        if (velAlongNormal > 0f) return;

        float j = -(1 + PlayerRestitution) * velAlongNormal / totalInvMass;
        var impulse = normal * j;

        aVel -= impulse * invMassA;
        bVel += impulse * invMassB;

        em.AddComponent(aEntity, new Velocity(aVel));
        em.AddComponent(playerEntity, new Velocity(bVel));

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

        if (em.HasComponent<AngularVelocity>(playerEntity))
        {
            var bAngVel = em.GetComponent<AngularVelocity>(playerEntity);
            em.AddComponent(playerEntity, new AngularVelocity(bAngVel.Value - tangentSpeed * RotationFactor));
        }
    }
}
