using System.Collections;

namespace Spacevors.Domain;

public class ComponentStorage<T> : ComponentStorageBase, IEnumerable<(Entity Entity, T Value)> where T : notnull
{
    private T[] _data = Array.Empty<T>();
    private Entity[] _entityIds = Array.Empty<Entity>();
    private readonly Dictionary<int, int> _slotMap = new();
    private int _count = 0;

    public void Add(Entity entity, T value)
    {
        int id = entity.Value;
        if (_slotMap.TryGetValue(id, out int existingSlot))
        {
            _data[existingSlot] = value;
            return;
        }

        int slot = _count;
        if (slot >= _data.Length)
        {
            Array.Resize(ref _data, slot * 2 + 1);
            Array.Resize(ref _entityIds, slot * 2 + 1);
        }

        _data[slot] = value;
        _entityIds[slot] = entity;
        _slotMap[id] = slot;
        _count++;
    }

    public T Get(Entity entity)
    {
        int id = entity.Value;
        if (_slotMap.TryGetValue(id, out int slot))
        {
            return _data[slot];
        }
        throw new KeyNotFoundException($"Component of type {typeof(T).Name} not found for entity {entity.Value}");
    }

    public bool Has(Entity entity)
    {
        return _slotMap.ContainsKey(entity.Value);
    }

    public override void Remove(Entity entity)
    {
        int id = entity.Value;
        if (!_slotMap.Remove(id, out int slot))
            return;

        _count--;
        if (slot < _count)
        {
            _data[slot] = _data[_count];
            _entityIds[slot] = _entityIds[_count];
            _slotMap[_entityIds[slot].Value] = slot;
        }
    }

    public override int Count => _count;

    public override IEnumerable<int> GetEntityIds()
    {
        int[] ids = new int[_count];
        _slotMap.Keys.CopyTo(ids, 0);
        return ids;
    }

    public IEnumerator<(Entity Entity, T Value)> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return (_entityIds[i], _data[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
