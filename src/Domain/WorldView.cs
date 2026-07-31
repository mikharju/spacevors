namespace Spacevors.Domain;

public class WorldView
{
    private readonly EntityManager _em;

    public WorldView(EntityManager em)
    {
        _em = em;
    }

    public int MaxEntityId => _em.MaxEntityId;

    public ComponentStorage<T> GetStorage<T>() where T : notnull
        => _em.GetStorage<T>();

    public bool TryGetStorage<T>(out ComponentStorage<T> storage) where T : notnull
        => _em.TryGetStorage(out storage);

    public ref T GetComponentRef<T>(Entity entity) where T : notnull
        => ref _em.GetComponentRef<T>(entity);

    public T GetComponent<T>(Entity entity) where T : notnull
    {
        return _em.GetComponent<T>(entity);
    }

    public bool HasComponent<T>(Entity entity) where T : notnull
    {
        return _em.HasComponent<T>(entity);
    }

    public bool TryGetComponent<T>(Entity entity, out T component) where T : notnull
    {
        return _em.TryGetComponent(entity, out component);
    }

    public ComponentQuery<T> GetEntitiesWithComponents<T>() where T : notnull
    {
        return _em.GetEntitiesWithComponents<T>();
    }

    public ComponentQuery<T1, T2> GetEntitiesWithComponents<T1, T2>()
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
