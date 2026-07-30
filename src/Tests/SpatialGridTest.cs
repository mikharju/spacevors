using Spacevors.Domain;
using Xunit;

namespace Tests;

public class SpatialGridTest
{
    [Fact]
    public void Query_EmptyGrid_ReturnsNothing()
    {
        var grid = new SpatialGrid(128f);
        var results = grid.Query(new Vector2(0, 0), 10f).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Query_SingleEntity_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 10f);

        var results = grid.Query(new Vector2(0, 0), 10f).ToList();
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(entity, r.Id));
    }

    [Fact]
    public void Query_DistantEntity_ReturnsNothing()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(5000, 5000), 10f);

        var results = grid.Query(new Vector2(0, 0), 10f).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Query_NearbyEntity_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(50, 50), 10f);

        var results = grid.Query(new Vector2(60, 60), 30f).ToList();
        Assert.Single(results);
    }

    [Fact]
    public void Query_EntityAtCellBoundary_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(64, 64), 50f);

        var results = grid.Query(new Vector2(0, 0), 100f).ToList();
        Assert.Single(results);
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

        var results = grid.Query(new Vector2(0, 0), 100f).ToList();
        Assert.Contains(results, r => r.Id == e1 && r.Kind == SpatialGrid.CollisionKind.Asteroid);
        Assert.Contains(results, r => r.Id == e2 && r.Kind == SpatialGrid.CollisionKind.EnemyMine);
    }

    [Fact]
    public void Clear_RemovesAllEntities()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 10f);

        grid.Clear();

        var results = grid.Query(new Vector2(0, 0), 10f).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Query_LargeRadius_CoversMultipleCells()
    {
        var grid = new SpatialGrid(128f);
        var e1 = new Entity(1);
        var e2 = new Entity(2);

        grid.Insert(e1, SpatialGrid.CollisionKind.Asteroid, new Vector2(-64, -64), 5f);
        grid.Insert(e2, SpatialGrid.CollisionKind.EnemyMine, new Vector2(192, 192), 5f);

        var results = grid.Query(new Vector2(0, 0), 300f).ToList();
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Insert_EntityWithLargeRadius_CoversMultipleCells()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 200f);

        var results = grid.Query(new Vector2(256, 256), 10f).ToList();
        Assert.Single(results);
    }

    [Fact]
    public void Query_ReturnsEntitiesInSameCellEvenIfDistant()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 0.5f);

        var results = grid.Query(new Vector2(100, 100), 0.5f).ToList();
        Assert.Single(results);
    }
}
