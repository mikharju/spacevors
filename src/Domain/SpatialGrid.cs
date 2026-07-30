using Spacevors.Domain;

namespace Spacevors.Domain;

public class SpatialGrid
{
    private readonly Dictionary<(int CellX, int CellY), List<QueryEntry>> _cells = new();
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

    public readonly record struct QueryEntry(Entity Id, CollisionKind Kind, Vector2 Position, float Radius);

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
                    entries = new List<QueryEntry>();
                    _cells[(x, y)] = entries;
                }
                entries.Add(new QueryEntry(id, kind, position, radius));
            }
        }
    }

    public IEnumerable<QueryEntry> Query(Vector2 position, float radius)
    {
        int minX = (int)MathF.Floor((position.X - radius) / CellSize);
        int maxX = (int)MathF.Floor((position.X + radius) / CellSize);
        int minY = (int)MathF.Floor((position.Y - radius) / CellSize);
        int maxY = (int)MathF.Floor((position.Y + radius) / CellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (_cells.TryGetValue((x, y), out var entries))
                {
                    foreach (var entry in entries)
                    {
                        yield return entry;
                    }
                }
            }
        }
    }
}
