namespace Spacevors.Domain;

public class WorldView
{
    private readonly EntityManager _em;

    public WorldView(EntityManager em)
    {
        _em = em;
    }

    public int MaxEntityId => _em.MaxEntityId;

    public T GetComponent<T>(Entity entity) where T : notnull
    {
        return _em.GetComponent<T>(entity);
    }

    public bool HasComponent<T>(Entity entity) where T : notnull
    {
        return _em.HasComponent<T>(entity);
    }

    public IEnumerable<(Entity Entity, T Value)> GetEntitiesWithComponents<T>() where T : notnull
    {
        return _em.GetEntitiesWithComponents<T>();
    }

    public IEnumerable<(Entity Entity, T1 Value1, T2 Value2)> GetEntitiesWithComponents<T1, T2>()
        where T1 : notnull where T2 : notnull
    {
        return _em.GetEntitiesWithComponents<T1, T2>();
    }

    public IEnumerable<(Entity Entity, T1 Value1, T2 Value2, T3 Value3)> GetEntitiesWithComponents<T1, T2, T3>()
        where T1 : notnull where T2 : notnull where T3 : notnull
    {
        return _em.GetEntitiesWithComponents<T1, T2, T3>();
    }

    public IEnumerable<(Entity Entity, T1 Value1, T2 Value2, T3 Value3, T4 Value4)> GetEntitiesWithComponents<T1, T2, T3, T4>()
        where T1 : notnull where T2 : notnull where T3 : notnull where T4 : notnull
    {
        return _em.GetEntitiesWithComponents<T1, T2, T3, T4>();
    }

    public IEnumerable<Entity> GetEntitiesWith<T>() where T : notnull
    {
        return _em.GetEntitiesWith<T>();
    }
}
