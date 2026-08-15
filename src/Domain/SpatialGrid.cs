using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Domain;

public class SpatialGrid
{
    private readonly Dictionary<(int CellX, int CellY), List<SpatialItem>> _cells = new();
    private readonly HashSet<Entity> _seen = new();
    public float CellSize { get; }

    public enum CollisionKind
    {
        Asteroid,
        EnemyMine,
        EnemyShip
    }

    public SpatialGrid(float cellSize)
    {
        CellSize = cellSize;
    }

    public readonly record struct SpatialItem(
        Entity Id,
        CollisionKind Kind,
        Vector2 Position,
        float Radius,
        MineSize? Size = null);

    // Reuses cell buckets across frames instead of reallocating them every tick.
    public void Clear()
    {
        foreach (var entries in _cells.Values)
            entries.Clear();
    }

    public void Insert(Entity id, CollisionKind kind, Vector2 position, float radius, MineSize? size = null)
    {
        int minX = (int)MathF.Floor((position.X - radius) / CellSize);
        int maxX = (int)MathF.Floor((position.X + radius) / CellSize);
        int minY = (int)MathF.Floor((position.Y - radius) / CellSize);
        int maxY = (int)MathF.Floor((position.Y + radius) / CellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (!_cells.TryGetValue((x, y), out var entries))
                {
                    entries = new List<SpatialItem>();
                    _cells[(x, y)] = entries;
                }
                entries.Add(new SpatialItem(id, kind, position, radius, size));
            }
        }
    }

    // Returns each entity at most once, even if it spans multiple cells.
    // Sets truncated to true when a unique item did not fit in result.
    public int GetQueryItems(Vector2 position, float radius, Span<SpatialItem> result, out bool truncated)
    {
        int minX = (int)MathF.Floor((position.X - radius) / CellSize);
        int maxX = (int)MathF.Floor((position.X + radius) / CellSize);
        int minY = (int)MathF.Floor((position.Y - radius) / CellSize);
        int maxY = (int)MathF.Floor((position.Y + radius) / CellSize);

        _seen.Clear();
        truncated = false;
        int count = 0;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (!_cells.TryGetValue((x, y), out var entries)) continue;

                for (int i = 0; i < entries.Count; i++)
                {
                    SpatialItem item = entries[i];
                    if (!_seen.Add(item.Id)) continue;
                    if (count >= result.Length)
                    {
                        truncated = true;
                        return count;
                    }
                    result[count++] = item;
                }
            }
        }
        return count;
    }
}
