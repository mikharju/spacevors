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
        Assert.Equal(pos.X, results[0].Value.X);
    }

    [Fact]
    public void Clear_RemovesAllData()
    {
        var entity = _em.CreateEntity();
        _em.AddComponent(entity, new Position(1f, 2f));
        _em.Clear();

        Assert.False(_em.HasComponent<Position>(entity));
    }
}
