using Spacevors.Domain;
using Xunit;

namespace Tests;

public class SpatialGridTest
{
    [Fact]
    public void Query_EmptyGrid_ReturnsNothing()
    {
        var grid = new SpatialGrid(128f);
        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results, out _);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Query_SingleEntity_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 10f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results, out _);
        Assert.True(count > 0);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(entity, results[i].Id);
        }
    }

    [Fact]
    public void Query_DistantEntity_ReturnsNothing()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(5000, 5000), 10f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results, out _);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Query_NearbyEntity_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(50, 50), 10f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(60, 60), 30f, results, out _);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Query_EntityAtCellBoundary_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(64, 64), 50f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 100f, results, out _);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Query_MultipleEntities_ReturnsEachOnce()
    {
        var grid = new SpatialGrid(128f);
        var e1 = new Entity(1);
        var e2 = new Entity(2);
        var e3 = new Entity(3);

        // e1 spans 4 cells, so without dedup it would appear 4 times.
        grid.Insert(e1, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 50f);
        grid.Insert(e2, SpatialGrid.CollisionKind.EnemyMine, new Vector2(64, 64), 50f);
        grid.Insert(e3, SpatialGrid.CollisionKind.EnemyShip, new Vector2(500, 500), 10f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 100f, results, out bool truncated);

        Assert.False(truncated);
        Assert.Equal(2, count);

        bool foundAsteroid = false;
        bool foundMine = false;
        for (int i = 0; i < count; i++)
        {
            if (results[i].Id == e1 && results[i].Kind == SpatialGrid.CollisionKind.Asteroid)
                foundAsteroid = true;
            if (results[i].Id == e2 && results[i].Kind == SpatialGrid.CollisionKind.EnemyMine)
                foundMine = true;
        }
        Assert.True(foundAsteroid);
        Assert.True(foundMine);
    }

    [Fact]
    public void Query_MultiCellEntity_ReturnsItExactlyOnce()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        // radius 200 spans cells x,y in [-2..1], i.e. 16 cells
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 200f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results, out bool truncated);

        Assert.False(truncated);
        Assert.Equal(1, count);
        Assert.Equal(entity, results[0].Id);
    }

    [Fact]
    public void Query_BufferTooSmall_SignalsTruncation()
    {
        var grid = new SpatialGrid(128f);
        for (int i = 0; i < 5; i++)
            grid.Insert(new Entity(i + 1), SpatialGrid.CollisionKind.Asteroid, new Vector2(i * 10f, 0f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[3];
        int count = grid.GetQueryItems(new Vector2(20f, 0f), 40f, results, out bool truncated);

        Assert.Equal(3, count);
        Assert.True(truncated);
    }

    [Fact]
    public void Query_BufferFitsAll_NoTruncation()
    {
        var grid = new SpatialGrid(128f);
        for (int i = 0; i < 5; i++)
            grid.Insert(new Entity(i + 1), SpatialGrid.CollisionKind.Asteroid, new Vector2(i * 10f, 0f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(20f, 0f), 40f, results, out bool truncated);

        Assert.Equal(5, count);
        Assert.False(truncated);
    }

    [Fact]
    public void Clear_RemovesAllEntities()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 10f);

        grid.Clear();

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results, out _);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Query_LargeRadius_CoversMultipleCells()
    {
        var grid = new SpatialGrid(128f);
        var e1 = new Entity(1);
        var e2 = new Entity(2);

        grid.Insert(e1, SpatialGrid.CollisionKind.Asteroid, new Vector2(-64, -64), 5f);
        grid.Insert(e2, SpatialGrid.CollisionKind.EnemyMine, new Vector2(192, 192), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 300f, results, out _);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Insert_EntityWithLargeRadius_CoversMultipleCells()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 200f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(256, 256), 10f, results, out _);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Query_ReturnsEntitiesInSameCellEvenIfDistant()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 0.5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(100, 100), 0.5f, results, out _);
        Assert.Equal(1, count);
    }
}
