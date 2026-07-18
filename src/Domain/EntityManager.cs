namespace Spacevors.Domain;

public class EntityManager
{
    private readonly Dictionary<Type, object> _storages = new();
    private int _nextId = 0;

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

    public void Clear()
    {
        _nextId = 0;
        _storages.Clear();
    }
}

public abstract class ComponentStorageBase
{
    public abstract void Remove(Entity entity);
}
