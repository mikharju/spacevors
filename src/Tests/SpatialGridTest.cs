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

    [Fact]
    public void BoxQuery_EntityAlongLongBox_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(500, 0), 10f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItemsInBox(new Vector2(-100, -10), new Vector2(600, 10), results, out bool truncated);

        Assert.False(truncated);
        Assert.Equal(1, count);
        Assert.Equal(entity, results[0].Id);
    }

    [Fact]
    public void BoxQuery_EntityOutsideBox_ReturnsNothing()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(500, 400), 10f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItemsInBox(new Vector2(-100, -100), new Vector2(600, 100), results, out _);
        Assert.Equal(0, count);
    }

    [Fact]
    public void BoxQuery_MultiCellEntity_ReturnsItExactlyOnce()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(300, 0), 200f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItemsInBox(new Vector2(-100, -50), new Vector2(900, 50), results, out bool truncated);

        Assert.False(truncated);
        Assert.Equal(1, count);
    }

    [Fact]
    public void BoxQuery_BufferTooSmall_SignalsTruncation()
    {
        var grid = new SpatialGrid(128f);
        for (int i = 0; i < 5; i++)
            grid.Insert(new Entity(i + 1), SpatialGrid.CollisionKind.Asteroid, new Vector2(i * 200f, 0f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[3];
        int count = grid.GetQueryItemsInBox(new Vector2(-10, -10), new Vector2(1000, 10), results, out bool truncated);

        Assert.Equal(3, count);
        Assert.True(truncated);
    }

    // --- Buffer contract: empty, exact-fit, and dedup-before-overflow ---

    [Fact]
    public void Query_ZeroLengthBuffer_SignalsTruncationAndWritesNothing()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 5f);

        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, Span<SpatialGrid.SpatialItem>.Empty, out bool truncated);

        Assert.Equal(0, count);
        Assert.True(truncated);
    }

    [Fact]
    public void Query_ZeroLengthBuffer_EmptyGrid_DoesNotSignalTruncation()
    {
        var grid = new SpatialGrid(128f);

        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, Span<SpatialGrid.SpatialItem>.Empty, out bool truncated);

        Assert.Equal(0, count);
        Assert.False(truncated);
    }

    [Fact]
    public void Query_BufferExactlyFitsAll_NoTruncation()
    {
        var grid = new SpatialGrid(128f);
        for (int i = 0; i < 5; i++)
            grid.Insert(new Entity(i + 1), SpatialGrid.CollisionKind.Asteroid, new Vector2(i * 10f, 0f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[5];
        int count = grid.GetQueryItems(new Vector2(20f, 0f), 40f, results, out bool truncated);

        Assert.Equal(5, count);
        Assert.False(truncated);
    }

    // A full buffer must not report truncation when every remaining cell entry is a duplicate
    // of an entity already returned: dedup runs before the capacity check.
    [Fact]
    public void Query_BufferFullButRemainingEntriesAreDuplicates_NoTruncation()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 200f);
        grid.Insert(new Entity(2), SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[2];
        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results, out bool truncated);

        Assert.Equal(2, count);
        Assert.False(truncated);
    }

    [Fact]
    public void Query_TruncatedCountNeverExceedsBuffer()
    {
        var grid = new SpatialGrid(128f);
        for (int i = 0; i < 1000; i++)
            grid.Insert(new Entity(i + 1), SpatialGrid.CollisionKind.EnemyMine, new Vector2(i % 100, (i / 100) % 10), 2f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[256];
        int count = grid.GetQueryItems(new Vector2(50, 5), 10f, results, out bool truncated);

        Assert.True(truncated);
        Assert.Equal(256, count);
    }

    // --- Positive and negative cell boundaries (CellSize 128 → boundaries at multiples of 128) ---

    [Fact]
    public void Query_EntityExactlyOnNegativeCellBoundary_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(-128f, -128f), 0f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(-128f, -128f), 0f, results, out _);
        Assert.Equal(1, count);
    }

    // Entity AABB ends exactly at x = -128, query AABB starts exactly at x = -128.
    [Fact]
    public void Query_AabbsTouchAcrossNegativeCellBoundary_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(-138f, 0f), 10f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(-118f, 0f), 10f, results, out _);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Query_RegionStraddlesOriginCellBoundary_ReturnsNegativeSideEntity()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(-1f, -1f), 0f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(0, 0), 1f, results, out _);
        Assert.Equal(1, count);
    }

    [Fact]
    public void BoxQuery_NegativeCoordinates_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(-256f, -256f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItemsInBox(new Vector2(-300f, -300f), new Vector2(-200f, -200f), results, out _);
        Assert.Equal(1, count);
    }

    [Fact]
    public void BoxQuery_DegenerateBox_MinEqualsMax_ReturnsEntityInThatCell()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(-100f, -100f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItemsInBox(new Vector2(-100f, -100f), new Vector2(-100f, -100f), results, out _);
        Assert.Equal(1, count);
    }

    [Fact]
    public void BoxQuery_InvertedBox_ReturnsNothing()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItemsInBox(new Vector2(100f, 100f), new Vector2(-100f, -100f), results, out bool truncated);

        Assert.Equal(0, count);
        Assert.False(truncated);
    }

    // --- Large radii / queries crossing many cells ---

    [Fact]
    public void Query_RadiusSpanningManyCells_ReturnsEachEntityOnce()
    {
        var grid = new SpatialGrid(128f);
        for (int i = 0; i < 20; i++)
            grid.Insert(new Entity(i + 1), SpatialGrid.CollisionKind.Asteroid, new Vector2(i * 500f - 5000f, i * 300f - 3000f), 10f);

        // 6000f radius spans 94x94 cells.
        var results = new SpatialGrid.SpatialItem[64];
        int count = grid.GetQueryItems(new Vector2(0, 0), 6000f, results, out bool truncated);

        Assert.False(truncated);
        Assert.Equal(20, count);
        Assert.Equal(20, results.Take(count).Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void Insert_LargeRadiusOnNegativeSide_QueryAtFarEdge_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(-1000f, -1000f), 500f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(-1500f, -1000f), 1f, results, out _);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Query_PartialOverlapOfMultiCellEntity_ReturnsItOnce()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 1000f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(995f, 0f), 1f, results, out bool truncated);

        Assert.Equal(1, count);
        Assert.False(truncated);
    }

    // --- Shared _seen / bucket reuse across queries and frames ---

    [Fact]
    public void Query_SequentialQueries_AreStable()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 200f);
        grid.Insert(new Entity(2), SpatialGrid.CollisionKind.Asteroid, new Vector2(10f, 0f), 5f);

        for (int round = 0; round < 3; round++)
        {
            Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
            int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results, out bool truncated);
            Assert.False(truncated);
            Assert.Equal(2, count);
        }
    }

    [Fact]
    public void Clear_ThenReinsertSameEntity_ReturnsIt()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 200f);
        grid.Clear();
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0, 0), 200f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(0, 0), 10f, results, out _);
        Assert.Equal(1, count);
    }

    // --- Differential guard: the grid must never miss a true overlap and never duplicate ---

    [Fact]
    public void Query_MatchesBruteForce_NoMissedOverlapsNoDuplicates()
    {
        var rng = new Random(12345);
        var results = new SpatialGrid.SpatialItem[512];
        int checkedPairs = 0;

        for (int trial = 0; trial < 200; trial++)
        {
            var grid = new SpatialGrid(128f);
            var entities = new List<(Entity Id, Vector2 Position, float Radius)>();
            int n = 1 + rng.Next(30);
            for (int i = 0; i < n; i++)
            {
                // Half the placements land within +-1.5 units of a cell boundary.
                var position = new Vector2(SnapNearBoundary(rng, -2000f, 2000f), SnapNearBoundary(rng, -2000f, 2000f));
                float radius = 0.5f + (float)rng.NextDouble() * 300f;
                var id = new Entity(i + 1);
                grid.Insert(id, SpatialGrid.CollisionKind.Asteroid, position, radius);
                entities.Add((id, position, radius));
            }

            int queryCount = 1 + rng.Next(8);
            for (int q = 0; q < queryCount; q++)
            {
                var center = new Vector2(SnapNearBoundary(rng, -2500f, 2500f), SnapNearBoundary(rng, -2500f, 2500f));
                float queryRadius = (float)rng.NextDouble() * 800f;

                int count = grid.GetQueryItems(center, queryRadius, results, out bool truncated);
                Assert.False(truncated);

                var returned = new HashSet<Entity>();
                for (int i = 0; i < count; i++)
                    Assert.True(returned.Add(results[i].Id), "entity returned more than once");

                foreach (var (id, position, radius) in entities)
                {
                    checkedPairs++;
                    float dx = center.X - position.X, dy = center.Y - position.Y;
                    bool overlaps = dx * dx + dy * dy < (queryRadius + radius) * (queryRadius + radius);
                    Assert.True(!overlaps || returned.Contains(id),
                        $"missed overlap: query=({center.X},{center.Y}) r={queryRadius} entity=({position.X},{position.Y}) r={radius}");
                }
            }
        }

        Assert.True(checkedPairs > 10_000, $"differential test checked only {checkedPairs} pairs");
    }

    [Fact]
    public void BoxQuery_MatchesBruteForce_NoMissedAabbOverlaps()
    {
        var rng = new Random(999);
        var results = new SpatialGrid.SpatialItem[512];

        for (int trial = 0; trial < 200; trial++)
        {
            var grid = new SpatialGrid(128f);
            var entities = new List<(Entity Id, Vector2 Position, float Radius)>();
            int n = 1 + rng.Next(30);
            for (int i = 0; i < n; i++)
            {
                var position = new Vector2(SnapNearBoundary(rng, -2000f, 2000f), SnapNearBoundary(rng, -2000f, 2000f));
                float radius = 0.5f + (float)rng.NextDouble() * 300f;
                var id = new Entity(i + 1);
                grid.Insert(id, SpatialGrid.CollisionKind.Asteroid, position, radius);
                entities.Add((id, position, radius));
            }

            var min = new Vector2(SnapNearBoundary(rng, -2500f, 2500f), SnapNearBoundary(rng, -2500f, 2500f));
            var max = new Vector2(min.X + (float)rng.NextDouble() * 1500f, min.Y + (float)rng.NextDouble() * 1500f);

            int count = grid.GetQueryItemsInBox(min, max, results, out bool truncated);
            Assert.False(truncated);

            var returned = new HashSet<Entity>();
            for (int i = 0; i < count; i++)
                Assert.True(returned.Add(results[i].Id), "entity returned more than once");

            foreach (var (id, position, radius) in entities)
            {
                bool overlaps = position.X - radius <= max.X && position.X + radius >= min.X
                             && position.Y - radius <= max.Y && position.Y + radius >= min.Y;
                Assert.True(!overlaps || returned.Contains(id),
                    $"missed box overlap: box=({min.X},{min.Y})-({max.X},{max.Y}) entity=({position.X},{position.Y}) r={radius}");
            }
        }
    }

    // Half the values land within +-1.5 units of a cell boundary, the rest are uniform.
    private static float SnapNearBoundary(Random rng, float min, float max)
    {
        float value = min + (float)rng.NextDouble() * (max - min);
        if (rng.Next(2) == 0)
            value = (float)Math.Round(value / 128f) * 128f + (float)(rng.NextDouble() * 3f - 1.5f);
        return value;
    }
}
