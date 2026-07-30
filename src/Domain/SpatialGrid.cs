using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Domain;

public class SpatialGrid
{
    private readonly Dictionary<(int CellX, int CellY), List<SpatialItem>> _cells = new();
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
        int? Health = null,
        MineSize? Size = null,
        int Damage = 0);

    public void Clear()
    {
        _cells.Clear();
    }

    public void Insert(Entity id, CollisionKind kind, Vector2 position, float radius)
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
                entries.Add(new SpatialItem(id, kind, position, radius));
            }
        }
    }

    public void Insert(Entity id, CollisionKind kind, Vector2 position, float radius, int? health, MineSize? size, int damage)
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
                entries.Add(new SpatialItem(id, kind, position, radius, health, size, damage));
            }
        }
    }

    public int GetQueryItems(Vector2 position, float radius, Span<SpatialItem> result)
    {
        int minX = (int)MathF.Floor((position.X - radius) / CellSize);
        int maxX = (int)MathF.Floor((position.X + radius) / CellSize);
        int minY = (int)MathF.Floor((position.Y - radius) / CellSize);
        int maxY = (int)MathF.Floor((position.Y + radius) / CellSize);

        int count = 0;
        for (int x = minX; x <= maxX && count < result.Length; x++)
        {
            for (int y = minY; y <= maxY && count < result.Length; y++)
            {
                if (_cells.TryGetValue((x, y), out var entries))
                {
                    for (int i = 0; i < entries.Count && count < result.Length; i++)
                    {
                        result[count++] = entries[i];
                    }
                }
            }
        }
        return count;
    }
}
