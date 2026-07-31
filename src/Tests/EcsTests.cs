using Spacevors.Domain;
using Xunit;

public class EntityTests
{
    [Fact]
    public void NullEntity_IsNull()
    {
        Assert.True(Entity.Null.IsNull);
    }

    [Fact]
    public void ValidEntity_NotNull()
    {
        var e = new Entity(5);
        Assert.False(e.IsNull);
    }

    [Fact]
    public void NullEntity_EqualsNull()
    {
        Assert.Equal(Entity.Null, Entity.Null);
    }

    [Fact]
    public void SameValueEntities_Equal()
    {
        var a = new Entity(3);
        var b = new Entity(3);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentValueEntities_NotEqual()
    {
        var a = new Entity(3);
        var b = new Entity(4);
        Assert.NotEqual(a, b);
    }
}

public class EntityManagerTests
{
    private readonly EntityManager _em;

    public record struct Position(float X, float Y);
    public record struct Velocity(float Dx, float Dy);

    public EntityManagerTests()
    {
        _em = new();
    }

    [Fact]
    public void CreateEntity_ReturnsValidId()
    {
        var e1 = _em.CreateEntity();
        var e2 = _em.CreateEntity();
        Assert.NotEqual(e1, e2);
    }

    [Fact]
    public void AddAndGetComponent_Works()
    {
        var entity = _em.CreateEntity();
        var pos = new Position(1f, 2f);
        _em.AddComponent(entity, pos);

        var retrieved = _em.GetComponent<Position>(entity);
        Assert.Equal(pos.X, retrieved.X);
        Assert.Equal(pos.Y, retrieved.Y);
    }

    [Fact]
    public void HasComponent_ReturnsTrue_WhenAdded()
    {
        var entity = _em.CreateEntity();
        _em.AddComponent(entity, new Position(0f, 0f));

        Assert.True(_em.HasComponent<Position>(entity));
    }

    [Fact]
    public void HasComponent_ReturnsFalse_WhenNotAdded()
    {
        var entity = _em.CreateEntity();
        Assert.False(_em.HasComponent<Position>(entity));
    }

    [Fact]
    public void GetComponent_Throws_WhenNotFound()
    {
        var entity = _em.CreateEntity();
        Assert.Throws<KeyNotFoundException>(() => _em.GetComponent<Position>(entity));
    }

    [Fact]
    public void DestroyEntity_RemovesAllComponents()
    {
        var entity = _em.CreateEntity();
        _em.AddComponent(entity, new Position(1f, 2f));
        _em.AddComponent(entity, new Velocity(3f, 4f));

        _em.DestroyEntity(entity);

        Assert.False(_em.HasComponent<Position>(entity));
        Assert.False(_em.HasComponent<Velocity>(entity));
    }

    [Fact]
    public void GetEntitiesWith_ReturnsMatching()
    {
        var e1 = _em.CreateEntity();
        var e2 = _em.CreateEntity();
        var e3 = _em.CreateEntity();

        _em.AddComponent(e1, new Position(0f, 0f));
        _em.AddComponent(e2, new Position(1f, 1f));
        // e3 has no position

        var entities = _em.GetEntitiesWith<Position>().ToList();
        Assert.Equal(2, entities.Count);
        Assert.Contains(e1, entities);
        Assert.Contains(e2, entities);
    }

    [Fact]
    public void GetEntitiesWithComponents_ReturnsEntityAndValue()
    {
        var entity = _em.CreateEntity();
        var pos = new Position(5f, 10f);
        _em.AddComponent(entity, pos);

        var results = _em.GetEntitiesWithComponents<Position>().ToList();
        Assert.Single(results);
        Assert.Equal(entity, results[0].Entity);
        Assert.Equal(pos.X, results[0].Value1.X);
    }

    [Fact]
    public void Clear_RemovesAllData()
    {
        var entity = _em.CreateEntity();
        _em.AddComponent(entity, new Position(1f, 2f));
        _em.Clear();

        Assert.False(_em.HasComponent<Position>(entity));
    }

    [Fact]
    public void DestroyMiddleEntity_CompactsCorrectly()
    {
        var e1 = _em.CreateEntity();
        var e2 = _em.CreateEntity();
        var e3 = _em.CreateEntity();

        _em.AddComponent(e1, new Position(1f, 1f));
        _em.AddComponent(e2, new Position(2f, 2f));
        _em.AddComponent(e3, new Position(3f, 3f));

        _em.DestroyEntity(e2);

        Assert.Equal(2, _em.GetEntitiesWith<Position>().Count());
        Assert.True(_em.HasComponent<Position>(e1));
        Assert.False(_em.HasComponent<Position>(e2));
        Assert.True(_em.HasComponent<Position>(e3));

        var pos1 = _em.GetComponent<Position>(e1);
        Assert.Equal(1f, pos1.X);
        Assert.Equal(1f, pos1.Y);

        var pos3 = _em.GetComponent<Position>(e3);
        Assert.Equal(3f, pos3.X);
        Assert.Equal(3f, pos3.Y);
    }

    [Fact]
    public void IterateAfterMixedAddsAndDeletes_ReturnsCorrectPairs()
    {
        var e1 = _em.CreateEntity();
        var e2 = _em.CreateEntity();
        var e3 = _em.CreateEntity();
        var e4 = _em.CreateEntity();

        _em.AddComponent(e1, new Position(1f, 1f));
        _em.AddComponent(e2, new Position(2f, 2f));
        _em.AddComponent(e3, new Position(3f, 3f));
        _em.AddComponent(e4, new Position(4f, 4f));

        _em.DestroyEntity(e2);
        _em.DestroyEntity(e3);

        var results = _em.GetEntitiesWithComponents<Position>().ToList();
        Assert.Equal(2, results.Count);

        var entities = results.Select(r => r.Entity).ToList();
        Assert.Contains(e1, entities);
        Assert.Contains(e4, entities);
    }

    [Fact]
    public void ReAddAfterDestroy_Works()
    {
        var entity = _em.CreateEntity();
        _em.AddComponent(entity, new Position(1f, 1f));
        _em.DestroyEntity(entity);

        Assert.False(_em.HasComponent<Position>(entity));

        _em.AddComponent(entity, new Position(5f, 6f));
        Assert.True(_em.HasComponent<Position>(entity));

        var pos = _em.GetComponent<Position>(entity);
        Assert.Equal(5f, pos.X);
        Assert.Equal(6f, pos.Y);
    }

    [Fact]
    public void DestroyLastEntity_CompactsCorrectly()
    {
        var e1 = _em.CreateEntity();
        var e2 = _em.CreateEntity();
        var e3 = _em.CreateEntity();

        _em.AddComponent(e1, new Position(1f, 1f));
        _em.AddComponent(e2, new Position(2f, 2f));
        _em.AddComponent(e3, new Position(3f, 3f));

        _em.DestroyEntity(e3);

        Assert.Equal(2, _em.GetEntitiesWith<Position>().Count());
        Assert.True(_em.HasComponent<Position>(e1));
        Assert.True(_em.HasComponent<Position>(e2));
    }

    [Fact]
    public void DestroyAllEntities_EmptyStorage()
    {
        var e1 = _em.CreateEntity();
        var e2 = _em.CreateEntity();

        _em.AddComponent(e1, new Position(1f, 1f));
        _em.AddComponent(e2, new Position(2f, 2f));

        _em.DestroyEntity(e1);
        _em.DestroyEntity(e2);

        Assert.Empty(_em.GetEntitiesWith<Position>());
    }

    [Fact]
    public void MultiComponentQuery_AfterDestroy_WorksCorrectly()
    {
        var e1 = _em.CreateEntity();
        var e2 = _em.CreateEntity();
        var e3 = _em.CreateEntity();

        _em.AddComponent(e1, new Position(1f, 1f));
        _em.AddComponent(e1, new Velocity(1f, 1f));

        _em.AddComponent(e2, new Position(2f, 2f));
        _em.AddComponent(e2, new Velocity(2f, 2f));

        _em.AddComponent(e3, new Position(3f, 3f));
        // e3 has no velocity

        _em.DestroyEntity(e1);

        var results = _em.GetEntitiesWithComponents<Position, Velocity>().ToList();
        Assert.Single(results);
        Assert.Equal(e2, results[0].Entity);
    }

    [Fact]
    public void GetStorage_ReturnsCorrectStorage()
    {
        var entity = _em.CreateEntity();
        _em.AddComponent(entity, new Position(5f, 10f));
        _em.AddComponent(entity, new Velocity(3f, 4f));

        var posStorage = _em.GetStorage<Position>();
        var velStorage = _em.GetStorage<Velocity>();

        Assert.Equal(1, posStorage.Count);
        Assert.Equal(1, velStorage.Count);
    }

    [Fact]
    public void GetStorage_IndexedAccess_MatchesIterator()
    {
        var e1 = _em.CreateEntity();
        var e2 = _em.CreateEntity();
        var e3 = _em.CreateEntity();

        _em.AddComponent(e1, new Position(1f, 2f));
        _em.AddComponent(e1, new Velocity(0.5f, 0.5f));

        _em.AddComponent(e2, new Position(3f, 4f));
        _em.AddComponent(e2, new Velocity(1f, 1f));

        _em.AddComponent(e3, new Position(5f, 6f));

        var velStorage = _em.GetStorage<Velocity>();
        var posStorage = _em.GetStorage<Position>();

        var indexedResults = new List<(Entity Entity, Position Pos, Velocity Vel)>();

        for (int i = 0; i < velStorage.Count; i++)
        {
            Entity entity = velStorage.GetEntity(i);
            if (!posStorage.TryGetSlot(entity, out int posSlot))
                continue;

            ref Position position = ref posStorage.GetComponent(posSlot);
            ref Velocity velocity = ref velStorage.GetComponent(i);

            indexedResults.Add((entity, position, velocity));
        }

        var iteratorResults = _em.GetEntitiesWithComponents<Position, Velocity>().ToList();

        Assert.Equal(iteratorResults.Count, indexedResults.Count);
        foreach (var (indexedEntity, indexedPos, _) in indexedResults)
        {
            var matching = iteratorResults.FirstOrDefault(r => r.Entity == indexedEntity);
            Assert.Equal(indexedPos.X, matching.Value1.X);
            Assert.Equal(indexedPos.Y, matching.Value1.Y);
        }
    }

    [Fact]
    public void GetStorage_IndexedMutation_AffectsGetComponent()
    {
        var entity = _em.CreateEntity();
        _em.AddComponent(entity, new Position(1f, 2f));
        _em.AddComponent(entity, new Velocity(0.5f, 0.5f));

        var velStorage = _em.GetStorage<Velocity>();
        var posStorage = _em.GetStorage<Position>();

        for (int i = 0; i < velStorage.Count; i++)
        {
            Entity entity2 = velStorage.GetEntity(i);
            if (!posStorage.TryGetSlot(entity2, out int posSlot))
                continue;

            ref Position position = ref posStorage.GetComponent(posSlot);
            ref Velocity velocity = ref velStorage.GetComponent(i);

            position = new Position(position.X + velocity.Dx, position.Y + velocity.Dy);
        }

        var retrieved = _em.GetComponent<Position>(entity);
        Assert.Equal(1.5f, retrieved.X);
        Assert.Equal(2.5f, retrieved.Y);
    }
}
