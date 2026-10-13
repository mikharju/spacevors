using Spacevors.Domain;
using Spacevors.Domain.Stats;
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

    // --- Cell boundaries on both signs ---

    [Fact]
    public void Query_EntityExactlyOnNegativeCellBoundary_ReturnsItOnce()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        // Center exactly on the -128 boundary, radius 0 → exactly one cell, (-1, -1).
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(-128f, -128f), 0f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(-128f, -128f), 0f, results, out bool truncated);

        Assert.Equal(1, count);
        Assert.False(truncated);
        Assert.Equal(entity, results[0].Id);
    }

    [Fact]
    public void Query_EntityExactlyOnPositiveCellBoundary_ReturnsItOnce()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(128f, 128f), 0f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(128f, 128f), 0f, results, out bool truncated);

        Assert.Equal(1, count);
        Assert.False(truncated);
        Assert.Equal(entity, results[0].Id);
    }

    [Fact]
    public void Query_AcrossNegativeBoundary_FindsEntityInAdjacentNegativeCell()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        // Spans cells x=-2 only ([-145, -135] / 128 → -2..-2).
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(-140f, 0f), 5f);

        // Query reaches x=-130 → cell range -2..0, overlapping the entity's cell.
        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(0f, 0f), 130f, results, out bool truncated);

        Assert.Equal(1, count);
        Assert.False(truncated);
    }

    [Fact]
    public void Query_AcrossNegativeBoundary_ExcludesEntityInNonOverlappingCell()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(-140f, 0f), 5f);

        // Query reaches x=-120 → cell range -1..0, entity lives in cell -2.
        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(0f, 0f), 120f, results, out _);
        Assert.Equal(0, count);
    }

    // --- Buffer capacity and truncation consistency ---

    [Fact]
    public void Query_EmptyBuffer_SignalsTruncationWhenItemsExist()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(0f, 0f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[0];
        int count = grid.GetQueryItems(new Vector2(0f, 0f), 10f, results, out bool truncated);

        Assert.Equal(0, count);
        Assert.True(truncated);
    }

    [Fact]
    public void Query_EmptyBuffer_EmptyGrid_NoTruncation()
    {
        var grid = new SpatialGrid(128f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[0];
        int count = grid.GetQueryItems(new Vector2(0f, 0f), 10f, results, out bool truncated);

        Assert.Equal(0, count);
        Assert.False(truncated);
    }

    [Fact]
    public void Query_BufferFitsExactly_NoTruncation()
    {
        var grid = new SpatialGrid(128f);
        for (int i = 0; i < 5; i++)
            grid.Insert(new Entity(i + 1), SpatialGrid.CollisionKind.Asteroid, new Vector2(i * 10f, 0f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[5];
        int count = grid.GetQueryItems(new Vector2(20f, 0f), 40f, results, out bool truncated);

        Assert.Equal(5, count);
        Assert.False(truncated);
    }

    [Fact]
    public void Query_BufferFullWithOnlyDuplicatesLeft_NoTruncation()
    {
        var grid = new SpatialGrid(128f);
        var spanning = new Entity(1);
        // Spans cells (-1,-1)..(0,0); the second entity shares cell (-1,-1) with it.
        grid.Insert(spanning, SpatialGrid.CollisionKind.Asteroid, new Vector2(0f, 0f), 50f);
        grid.Insert(new Entity(2), SpatialGrid.CollisionKind.Asteroid, new Vector2(-10f, -10f), 5f);

        // Buffer holds both uniques; the remaining three cell hits are duplicates only.
        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[2];
        int count = grid.GetQueryItems(new Vector2(0f, 0f), 10f, results, out bool truncated);

        Assert.Equal(2, count);
        Assert.False(truncated);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 1, true)]
    [InlineData(4, 4, true)]
    [InlineData(5, 5, false)]
    [InlineData(6, 5, false)]
    public void Query_TruncationMatchesBufferCapacity(int capacity, int expectedCount, bool expectedTruncated)
    {
        var grid = new SpatialGrid(128f);
        for (int i = 0; i < 5; i++)
            grid.Insert(new Entity(i + 1), SpatialGrid.CollisionKind.Asteroid, new Vector2(i * 10f, 0f), 5f);

        var results = new SpatialGrid.SpatialItem[capacity];
        int count = grid.GetQueryItems(new Vector2(20f, 0f), 40f, results, out bool truncated);

        Assert.Equal(expectedCount, count);
        Assert.Equal(expectedTruncated, truncated);
    }

    // --- Large radii crossing many cells ---

    [Fact]
    public void Query_LargeRadiusAcrossManyCells_ReturnsEachEntityOnce()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(-1000f, -1000f), 10f);
        grid.Insert(new Entity(2), SpatialGrid.CollisionKind.EnemyMine, new Vector2(1000f, 1000f), 10f);
        grid.Insert(new Entity(3), SpatialGrid.CollisionKind.EnemyShip, new Vector2(0f, 1500f), 10f);

        // Radius 2000 spans 32x32 cells.
        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[16];
        int count = grid.GetQueryItems(new Vector2(0f, 0f), 2000f, results, out bool truncated);

        Assert.Equal(3, count);
        Assert.False(truncated);
    }

    [Fact]
    public void Insert_LargeRadiusEntity_FoundFromFarCornerOfItsCells()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        // Radius 300 spans cells -3..2 on both axes.
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0f, 0f), 300f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(380f, 380f), 1f, results, out bool truncated);

        Assert.Equal(1, count);
        Assert.False(truncated);
    }

    // --- Box variant boundaries ---

    [Fact]
    public void BoxQuery_NegativeCoordinates_ReturnsEntity()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(-500f, -1f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItemsInBox(new Vector2(-600f, -10f), new Vector2(-400f, 10f), results, out bool truncated);

        Assert.Equal(1, count);
        Assert.False(truncated);
        Assert.Equal(entity, results[0].Id);
    }

    [Fact]
    public void BoxQuery_DegenerateBox_SingleCellQuery()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(-128f, -128f), 0f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItemsInBox(new Vector2(-128f, -128f), new Vector2(-128f, -128f), results, out bool truncated);

        Assert.Equal(1, count);
        Assert.False(truncated);
    }

    [Fact]
    public void BoxQuery_InvertedBox_ReturnsNothing()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(0f, 0f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItemsInBox(new Vector2(100f, 100f), new Vector2(-100f, -100f), results, out bool truncated);

        Assert.Equal(0, count);
        Assert.False(truncated);
    }

    // --- Dedup and lifecycle details ---

    [Fact]
    public void Query_SameEntityInsertedTwice_ReturnsFirstInsertionOnce()
    {
        var grid = new SpatialGrid(128f);
        var entity = new Entity(1);
        grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, new Vector2(0f, 0f), 5f);
        grid.Insert(entity, SpatialGrid.CollisionKind.EnemyMine, new Vector2(0f, 0f), 5f, MineSize.Large);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(0f, 0f), 10f, results, out bool truncated);

        Assert.Equal(1, count);
        Assert.Equal(SpatialGrid.CollisionKind.Asteroid, results[0].Kind);
        Assert.False(truncated);
    }

    [Fact]
    public void Clear_ThenReinsert_ReusesBucketsWithoutStaleEntries()
    {
        var grid = new SpatialGrid(128f);
        grid.Insert(new Entity(1), SpatialGrid.CollisionKind.Asteroid, new Vector2(0f, 0f), 5f);

        grid.Clear();
        var fresh = new Entity(2);
        grid.Insert(fresh, SpatialGrid.CollisionKind.EnemyShip, new Vector2(0f, 0f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        int count = grid.GetQueryItems(new Vector2(0f, 0f), 10f, results, out bool truncated);

        Assert.Equal(1, count);
        Assert.Equal(fresh, results[0].Id);
        Assert.False(truncated);
    }

    [Fact]
    public void Query_SequentialQueries_AreIndependent()
    {
        var grid = new SpatialGrid(128f);
        var e1 = new Entity(1);
        var e2 = new Entity(2);
        grid.Insert(e1, SpatialGrid.CollisionKind.Asteroid, new Vector2(0f, 0f), 5f);
        grid.Insert(e2, SpatialGrid.CollisionKind.Asteroid, new Vector2(1000f, 1000f), 5f);

        Span<SpatialGrid.SpatialItem> results = stackalloc SpatialGrid.SpatialItem[8];
        Assert.Equal(1, grid.GetQueryItems(new Vector2(0f, 0f), 10f, results, out _));
        Assert.Equal(1, grid.GetQueryItems(new Vector2(1000f, 1000f), 10f, results, out _));
        Assert.Equal(e2, results[0].Id);
    }

    // --- Randomized property checks (fixed seed → deterministic) ---

    [Fact]
    public void Query_NeverMissesOverlappingEntity_Randomized()
    {
        const int entityCount = 200;
        const int queryCount = 50;
        var grid = new SpatialGrid(128f);
        var rng = new Random(20260815);

        var entities = new (Entity Id, Vector2 Pos, float Radius)[entityCount];
        for (int i = 0; i < entityCount; i++)
        {
            var pos = new Vector2(RandomInRange(rng, -1024f, 1024f), RandomInRange(rng, -1024f, 1024f));
            float radius = RandomInRange(rng, 0.5f, 150f);
            entities[i] = (new Entity(i + 1), pos, radius);
            grid.Insert(entities[i].Id, SpatialGrid.CollisionKind.Asteroid, pos, radius);
        }

        var results = new SpatialGrid.SpatialItem[entityCount + 56];
        for (int q = 0; q < queryCount; q++)
        {
            var qp = new Vector2(RandomInRange(rng, -1024f, 1024f), RandomInRange(rng, -1024f, 1024f));
            float qr = RandomInRange(rng, 0f, 200f);
            int count = grid.GetQueryItems(qp, qr, results, out bool truncated);

            Assert.False(truncated);

            var returned = new HashSet<Entity>();
            for (int i = 0; i < count; i++)
            {
                Assert.True(returned.Add(results[i].Id), "duplicate entity in query results");
                var (id, pos, radius) = entities[results[i].Id.Value - 1];
                Assert.Equal(id, results[i].Id);
                Assert.Equal(pos, results[i].Position);
                Assert.Equal(radius, results[i].Radius);
            }

            foreach (var (id, pos, radius) in entities)
            {
                float dx = pos.X - qp.X;
                float dy = pos.Y - qp.Y;
                float rSum = qr + radius;
                bool overlaps = dx * dx + dy * dy < rSum * rSum;
                if (overlaps)
                    Assert.True(returned.Contains(id), $"entity {id.Value} overlaps the query but was not returned");
            }
        }
    }

    [Fact]
    public void BoxQuery_NeverMissesOverlappingEntity_Randomized()
    {
        const int entityCount = 200;
        const int queryCount = 50;
        var grid = new SpatialGrid(128f);
        var rng = new Random(777);

        var entities = new (Entity Id, Vector2 Pos, float Radius)[entityCount];
        for (int i = 0; i < entityCount; i++)
        {
            var pos = new Vector2(RandomInRange(rng, -1024f, 1024f), RandomInRange(rng, -1024f, 1024f));
            float radius = RandomInRange(rng, 0.5f, 150f);
            entities[i] = (new Entity(i + 1), pos, radius);
            grid.Insert(entities[i].Id, SpatialGrid.CollisionKind.Asteroid, pos, radius);
        }

        var results = new SpatialGrid.SpatialItem[entityCount + 56];
        for (int q = 0; q < queryCount; q++)
        {
            var min = new Vector2(RandomInRange(rng, -1024f, 512f), RandomInRange(rng, -1024f, 512f));
            var max = new Vector2(min.X + RandomInRange(rng, 0f, 1024f), min.Y + RandomInRange(rng, 0f, 1024f));
            int count = grid.GetQueryItemsInBox(min, max, results, out bool truncated);

            Assert.False(truncated);

            var returned = new HashSet<Entity>();
            for (int i = 0; i < count; i++)
                Assert.True(returned.Add(results[i].Id), "duplicate entity in query results");

            foreach (var (id, pos, radius) in entities)
            {
                bool overlaps = pos.X - radius <= max.X && pos.X + radius >= min.X
                             && pos.Y - radius <= max.Y && pos.Y + radius >= min.Y;
                if (overlaps)
                    Assert.True(returned.Contains(id), $"entity {id.Value} overlaps the box but was not returned");
            }
        }
    }

    private static float RandomInRange(Random rng, float min, float max) =>
        min + (float)rng.NextDouble() * (max - min);
}
