using System.ComponentModel.DataAnnotations;

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

    public ComponentStorage<T> GetStorage<T>() where T : notnull
    {
        var type = typeof(T);
        if (_storages.TryGetValue(type, out var storage))
            return (ComponentStorage<T>)storage;
        throw new KeyNotFoundException($"No storage for {typeof(T).Name}");
    }

    public bool TryGetStorage<T>(out ComponentStorage<T> storage) where T : notnull
    {
        var type = typeof(T);
        if (_storages.TryGetValue(type, out var s))
        {
            storage = (ComponentStorage<T>)s;
            return true;
        }

        storage = null!;
        return false;
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

    public ComponentQuery<T1> GetEntitiesWithComponents<T1>() where T1 : notnull
    {
        if (!_storages.TryGetValue(typeof(T1), out var s1))
            return default;

        return new ComponentQuery<T1>(
            (ComponentStorage<T1>)s1);
    }

    public static ComponentStorageBase FindSmallest(ComponentStorageBase first, params ComponentStorageBase[] rest)
    {
        var smallest = first;
        foreach (var s in rest)
            if (s.Count < smallest.Count)
                smallest = s;
        return smallest;
    }

    public ComponentQuery<T1, T2> GetEntitiesWithComponents<T1, T2>()
        where T1 : notnull
        where T2 : notnull
    {
        if (!_storages.TryGetValue(typeof(T1), out var s1) ||
            !_storages.TryGetValue(typeof(T2), out var s2))
            return default;

        return new ComponentQuery<T1, T2>(
            (ComponentStorage<T1>)s1,
            (ComponentStorage<T2>)s2);
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

        for (int i = 0; i < smallest.Count; i++)
        {
            var entity = smallest.GetEntity(i);

            if (!storage1.TryGetSlot(entity, out int slot1))
                continue;
            if (!storage2.TryGetSlot(entity, out int slot2))
                continue;
            if (!storage3.TryGetSlot(entity, out int slot3))
                continue;

            yield return (
                entity,
                storage1.GetBySlot(slot1),
                storage2.GetBySlot(slot2),
                storage3.GetBySlot(slot3));
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

        for (int i = 0; i < smallest.Count; i++)
        {
            var entity = smallest.GetEntity(i);

            if (!storage1.TryGetSlot(entity, out int slot1))
                continue;
            if (!storage2.TryGetSlot(entity, out int slot2))
                continue;
            if (!storage3.TryGetSlot(entity, out int slot3))
                continue;
            if (!storage4.TryGetSlot(entity, out int slot4))
                continue;

            yield return (
                entity,
                storage1.GetBySlot(slot1),
                storage2.GetBySlot(slot2),
                storage3.GetBySlot(slot3),
                storage4.GetBySlot(slot4));
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

    public abstract Entity GetEntity(int slot);
}
public readonly struct ComponentQuery<T1>
    where T1 : notnull
{
    private readonly ComponentStorage<T1> _s1;
    
    internal ComponentQuery(ComponentStorage<T1> s1)
    {
        _s1 = s1;
    }

    public Enumerator GetEnumerator()
    {
        if (_s1 == null) return new Enumerator(false);
        return new Enumerator(_s1);
    }
    public struct Enumerator
    {
        private readonly ComponentStorage<T1> _storage1;
        private readonly ComponentStorageBase _smallest;
        private int _index;
        private readonly bool _valid;

        internal Enumerator(ComponentStorage<T1> storage1)
        {
            _storage1 = storage1;
            _smallest = EntityManager.FindSmallest(_storage1);
            _index = -1;
            _valid = true;
        }

        internal Enumerator(bool valid)
        {
            _storage1 = null!;
            _smallest = null!;
            _index = 0;
            _valid = valid;
        }

        public (Entity Entity, T1 Value1) Current { get; private set; }
        
        public bool MoveNext()
        {
            if (!_valid) return false;

            while (++_index < _smallest.Count)
            {
                var entity = _smallest.GetEntity(_index);

                if (!_storage1.TryGetSlot(entity, out var s1))
                    continue;

                Current = (
                    entity,
                    _storage1.GetBySlot(s1));

                return true;
            }

            return false;
        }
    }

    public bool TryFirst(out (Entity Entity, T1 Value1) result)
    {
        var e = GetEnumerator();
        if (e.MoveNext())
        {
            result = e.Current;
            return true;
        }

        result = default;
        return false;
    }

    public List<(Entity Entity, T1 Value1)> ToList()
    {
        var list = new List<(Entity Entity, T1 Value1)>();

        foreach (var item in this)
            list.Add(item);

        return list;
    }

    public int Count() => _s1.Count;

    public (Entity Entity, T1 Value1) FirstOrDefault() => TryFirst(out var result) ? result : default;

    public bool Any() => _s1 is not null && _s1.Count > 0;
}


public readonly struct ComponentQuery<T1,T2>
    where T1 : notnull
    where T2 : notnull
{
    private readonly ComponentStorage<T1> _s1;
    private readonly ComponentStorage<T2> _s2;

    internal ComponentQuery(ComponentStorage<T1> s1,
                            ComponentStorage<T2> s2)
    {
        _s1 = s1;
        _s2 = s2;
    }

    public Enumerator GetEnumerator()
    {
        if (_s1 == null || _s2 == null) return new Enumerator(false);
        return new Enumerator(_s1, _s2);
    }

    public struct Enumerator
    {
        private readonly ComponentStorage<T1> _storage1;
        private readonly ComponentStorage<T2> _storage2;
        private readonly ComponentStorageBase _smallest;
        private int _index;
        private readonly bool _valid;

        internal Enumerator(ComponentStorage<T1> storage1, ComponentStorage<T2> storage2)
        {
            _storage1 = storage1;
            _storage2 = storage2;
            _smallest = EntityManager.FindSmallest(_storage1, _storage2);
            _index = -1;
            _valid = true;
        }

        internal Enumerator(bool valid)
        {
            _storage1 = null!;
            _storage2 = null!;
            _smallest = null!;
            _index = 0;
            _valid = valid;
        }

        public (Entity Entity, T1 Value1, T2 Value2) Current { get; private set; }
        
        public bool MoveNext()
        {
            if (!_valid) return false;

            while (++_index < _smallest.Count)
            {
                var entity = _smallest.GetEntity(_index);

                if (!_storage1.TryGetSlot(entity, out var s1))
                    continue;
                if (!_storage2.TryGetSlot(entity, out var s2))
                    continue;

                Current = (
                    entity,
                    _storage1.GetBySlot(s1),
                    _storage2.GetBySlot(s2));

                return true;
            }

            return false;
        }

    }

    public bool TryFirst(out (Entity Entity, T1 Value1, T2 Value2) result)
    {
        var e = GetEnumerator();
        if (e.MoveNext())
        {
            result = e.Current;
            return true;
        }

        result = default;
        return false;
    }

    public List<(Entity Entity, T1 Value1, T2 Value2)> ToList()
    {
        var list = new List<(Entity Entity, T1 Value1, T2 Value2)>();

        foreach (var item in this)
            list.Add(item);

        return list;
    }
}