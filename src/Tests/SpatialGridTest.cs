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
        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Query_SingleEntity_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 10f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results);
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
        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Query_NearbyEntity_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(50, 50), 10f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(60, 60), 30f, results);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Query_EntityAtCellBoundary_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(64, 64), 50f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 100f, results);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Query_MultipleEntities_ReturnsAllInOverlappingCells()
    {
        var grid = new SpatialGrid(128f);
        var e1 = new Entity(1);
        var e2 = new Entity(2);
        var e3 = new Entity(3);

        grid.Insert(e1, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 50f);
        grid.Insert(e2, SpatialGrid.CollisionKind.EnemyMine, new Vector2(64, 64), 50f);
        grid.Insert(e3, SpatialGrid.CollisionKind.EnemyShip, new Vector2(500, 500), 10f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 100f, results);
        
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
    public void Clear_RemovesAllEntities()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 10f);

        grid.Clear();

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results);
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
        int count = grid.GetQueryItems(new Vector2(0, 0), 300f, results);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Insert_EntityWithLargeRadius_CoversMultipleCells()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 200f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(256, 256), 10f, results);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Query_ReturnsEntitiesInSameCellEvenIfDistant()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 0.5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(100, 100), 0.5f, results);
        Assert.Equal(1, count);
    }
}
