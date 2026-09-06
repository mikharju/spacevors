using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

// Picks the enemy ship or mine under a world-space click point.
// Click zones are forgiving: small targets get larger multipliers so they stay clickable.
public static class PrimaryTargetPicker
{
    public const float SmallShipClickMultiplier = 1.5f;
    public const float LargeShipClickMultiplier = 1.1f;
    public const float SmallMineClickMultiplier = 4f;
    public const float LargeMineClickMultiplier = 2f;

    // Ships at or below this radius (Standard, Interceptor) count as small.
    private const float SmallShipRadiusThreshold = 45f;

    public static Entity? Pick(EntityManager em, Vector2 point)
    {
        Entity? best = null;
        float bestDistSq = float.MaxValue;

        foreach (var (entity, ship) in em.GetEntitiesWithComponents<EnemyShip>())
        {
            if (em.TryGetComponent<Dead>(entity, out _)) continue;

            var pos = em.GetComponent<Position>(entity);
            float zone = ship.Radius * (ship.Radius <= SmallShipRadiusThreshold ? SmallShipClickMultiplier : LargeShipClickMultiplier);
            Consider(entity, pos.Value, point, zone, ref best, ref bestDistSq);
        }

        foreach (var (entity, mine) in em.GetEntitiesWithComponents<EnemyMine>())
        {
            var pos = em.GetComponent<Position>(entity);
            float zone = mine.Radius * (mine.Size == MineSize.Small ? SmallMineClickMultiplier : LargeMineClickMultiplier);
            Consider(entity, pos.Value, point, zone, ref best, ref bestDistSq);
        }

        return best;
    }

    // Keeps the candidate closest to the click point; exact ties keep the first found.
    private static void Consider(Entity entity, Vector2 center, Vector2 point, float zone, ref Entity? best, ref float bestDistSq)
    {
        var diff = center - point;
        float distSq = diff.X * diff.X + diff.Y * diff.Y;

        if (distSq <= zone * zone && distSq < bestDistSq)
        {
            best = entity;
            bestDistSq = distSq;
        }
    }
}
