namespace Spacevors.Domain;

public class EntityManager
{
    private readonly Dictionary<Type, object> _storages = new();
    private int _nextId = 0;

    public int MaxEntityId => _nextId - 1;

    public Entity CreateEntity()
    {
        return new Entity(_nextId++);
    }

    public void DestroyEntity(Entity entity)
    {
        foreach (var storage in _storages.Values)
        {
            if (storage is ComponentStorageBase baseStorage)
            {
                baseStorage.Remove(entity);
            }
        }
    }

    public ref T GetComponentRef<T>(Entity entity) where T : notnull
    {
        var type = typeof(T);
        if (_storages.TryGetValue(type, out var storage))
        {
            return ref ((ComponentStorage<T>)storage).GetComponentRef(entity);
        }
        throw new KeyNotFoundException($"Component of type {typeof(T).Name} not found for entity {entity.Value}");
    }

    public void AddComponent<T>(Entity entity, T component) where T : notnull
    {
        var type = typeof(T);
        if (!_storages.TryGetValue(type, out var storage))
        {
            storage = new ComponentStorage<T>();
            _storages[type] = storage;
        }

        ((ComponentStorage<T>)storage).Add(entity, component);
    }

    public T GetComponent<T>(Entity entity) where T : notnull
    {
        var type = typeof(T);
        if (_storages.TryGetValue(type, out var storage))
        {
            return ((ComponentStorage<T>)storage).Get(entity);
        }
        throw new KeyNotFoundException($"Component of type {typeof(T).Name} not found for entity {entity.Value}");
    }

    public bool HasComponent<T>(Entity entity) where T : notnull
    {
        var type = typeof(T);
        if (_storages.TryGetValue(type, out var storage))
        {
            return ((ComponentStorage<T>)storage).Has(entity);
        }
        return false;
    }

    public bool TryGetComponent<T>(Entity entity, out T component) where T : notnull
    {
        var type = typeof(T);
        if (_storages.TryGetValue(type, out var storage))
        {
            var typedStorage = (ComponentStorage<T>)storage;
            if (typedStorage.TryGetSlot(entity, out int slot))
            {
                component = typedStorage.GetBySlot(slot);
                return true;
            }
        }

        component = default!;
        return false;
    }

    public IEnumerable<Entity> GetEntitiesWith<T>() where T : notnull
    {
        var type = typeof(T);
        if (_storages.TryGetValue(type, out var storage))
        {
            foreach (var (entity, _) in (ComponentStorage<T>)storage)
            {
                yield return entity;
            }
        }
    }

    public IEnumerable<(Entity Entity, T Value)> GetEntitiesWithComponents<T>() where T : notnull
    {
        var type = typeof(T);
        if (_storages.TryGetValue(type, out var storage))
        {
            foreach (var pair in (ComponentStorage<T>)storage)
            {
                yield return pair;
            }
        }
    }

    private static ComponentStorageBase FindSmallest(ComponentStorageBase first, params ComponentStorageBase[] rest)
    {
        var smallest = first;
        foreach (var s in rest)
            if (s.Count < smallest.Count)
                smallest = s;
        return smallest;
    }

    public IEnumerable<(Entity Entity, T1 Value1, T2 Value2)> GetEntitiesWithComponents<T1, T2>()
        where T1 : notnull where T2 : notnull
    {
        if (!_storages.TryGetValue(typeof(T1), out var s1) || !_storages.TryGetValue(typeof(T2), out var s2))
            yield break;

        var storage1 = (ComponentStorage<T1>)s1;
        var storage2 = (ComponentStorage<T2>)s2;
        var smallest = FindSmallest(storage1, storage2);

        foreach (var id in smallest.GetEntityIds())
        {
            var entity = new Entity(id);
            if (!storage1.TryGetSlot(entity, out int slot1)) continue;
            if (!storage2.TryGetSlot(entity, out int slot2)) continue;
            yield return (entity, storage1.GetBySlot(slot1), storage2.GetBySlot(slot2));
        }
    }

    public IEnumerable<(Entity Entity, T1 Value1, T2 Value2, T3 Value3)> GetEntitiesWithComponents<T1, T2, T3>()
        where T1 : notnull where T2 : notnull where T3 : notnull
    {
        if (!_storages.TryGetValue(typeof(T1), out var s1) || !_storages.TryGetValue(typeof(T2), out var s2) || !_storages.TryGetValue(typeof(T3), out var s3))
            yield break;

        var storage1 = (ComponentStorage<T1>)s1;
        var storage2 = (ComponentStorage<T2>)s2;
        var storage3 = (ComponentStorage<T3>)s3;
        var smallest = FindSmallest(storage1, storage2, storage3);

        foreach (var id in smallest.GetEntityIds())
        {
            var entity = new Entity(id);
            if (!storage1.TryGetSlot(entity, out int slot1)) continue;
            if (!storage2.TryGetSlot(entity, out int slot2)) continue;
            if (!storage3.TryGetSlot(entity, out int slot3)) continue;
            yield return (entity, storage1.GetBySlot(slot1), storage2.GetBySlot(slot2), storage3.GetBySlot(slot3));
        }
    }

    public IEnumerable<(Entity Entity, T1 Value1, T2 Value2, T3 Value3, T4 Value4)> GetEntitiesWithComponents<T1, T2, T3, T4>()
        where T1 : notnull where T2 : notnull where T3 : notnull where T4 : notnull
    {
        if (!_storages.TryGetValue(typeof(T1), out var s1) || !_storages.TryGetValue(typeof(T2), out var s2) || !_storages.TryGetValue(typeof(T3), out var s3) || !_storages.TryGetValue(typeof(T4), out var s4))
            yield break;

        var storage1 = (ComponentStorage<T1>)s1;
        var storage2 = (ComponentStorage<T2>)s2;
        var storage3 = (ComponentStorage<T3>)s3;
        var storage4 = (ComponentStorage<T4>)s4;
        var smallest = FindSmallest(storage1, storage2, storage3, storage4);

        foreach (var id in smallest.GetEntityIds())
        {
            var entity = new Entity(id);
            if (!storage1.TryGetSlot(entity, out int slot1)) continue;
            if (!storage2.TryGetSlot(entity, out int slot2)) continue;
            if (!storage3.TryGetSlot(entity, out int slot3)) continue;
            if (!storage4.TryGetSlot(entity, out int slot4)) continue;
            yield return (entity, storage1.GetBySlot(slot1), storage2.GetBySlot(slot2), storage3.GetBySlot(slot3), storage4.GetBySlot(slot4));
        }
    }

    public void Clear()
    {
        _nextId = 0;
        _storages.Clear();
    }
}

public abstract class ComponentStorageBase
{
    public abstract void Remove(Entity entity);
    public abstract int Count { get; }
    public abstract IEnumerable<int> GetEntityIds();
}
