using System.Collections;

namespace Spacevors.Domain;

public class ComponentStorage<T> : ComponentStorageBase, IEnumerable<(Entity Entity, T Value)> where T : notnull
{
    public const int MaxEntities = 20_000;

    private T[] _data = Array.Empty<T>();
    private Entity[] _entityIds = Array.Empty<Entity>();
    private int[]? _entityIdToSlot; // null until first AddComponent, then -1 means "no slot"
    private int _count = 0;

    private void EnsureSlotsArray(int entityId)
    {
        if (_entityIdToSlot == null)
        {
            var size = Math.Max(entityId + 1, MaxEntities);
            _entityIdToSlot = new int[size];
            Array.Fill(_entityIdToSlot, -1);
            return;
        }

        if (entityId >= _entityIdToSlot.Length)
        {
            int oldLength = _entityIdToSlot.Length;
            int newLength = Math.Max(entityId + 1, oldLength * 2);

            Array.Resize(ref _entityIdToSlot, newLength);
            Array.Fill(_entityIdToSlot, -1, oldLength, newLength - oldLength);
        }
    }

    public void Add(Entity entity, T value)
    {
        int id = entity.Value;
        EnsureSlotsArray(id);

        if (_entityIdToSlot![id] != -1)
        {
            _data[_entityIdToSlot[id]] = value;
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
        _entityIdToSlot[id] = slot;
        _count++;
    }

    public T Get(Entity entity)
    {
        int id = entity.Value;
        if (_entityIdToSlot != null && id < _entityIdToSlot.Length && _entityIdToSlot[id] != -1)
        {
            return _data[_entityIdToSlot[id]];
        }
        throw new KeyNotFoundException($"Component of type {typeof(T).Name} not found for entity {entity.Value}");
    }

    public bool Has(Entity entity)
    {
        int id = entity.Value;
        return _entityIdToSlot != null && id < _entityIdToSlot.Length && _entityIdToSlot[id] != -1;
    }

    public bool TryGetSlot(Entity entity, out int slot)
    {
        int id = entity.Value;
        if (_entityIdToSlot != null && id < _entityIdToSlot.Length && _entityIdToSlot[id] != -1)
        {
            slot = _entityIdToSlot[id];
            return true;
        }
        slot = 0;
        return false;
    }

    public ref T GetComponentRef(Entity entity)
    {
        if (!TryGetSlot(entity, out int slot))
            throw new KeyNotFoundException($"Component of type {typeof(T).Name} not found for entity {entity.Value}");
        return ref GetBySlot(slot);
    }

    public ref T GetBySlot(int slot)
    {
        return ref _data[slot];
    }

    public override void Remove(Entity entity)
    {
        int id = entity.Value;
        if (_entityIdToSlot == null || id >= _entityIdToSlot.Length || _entityIdToSlot[id] == -1)
            return;

        int slot = _entityIdToSlot[id];
        _entityIdToSlot[id] = -1;

        _count--;
        if (slot < _count)
        {
            _data[slot] = _data[_count];
            _entityIds[slot] = _entityIds[_count];
            _entityIdToSlot[_entityIds[slot].Value] = slot;
        }
    }

    public override int Count => _count;

    public override IEnumerable<int> GetEntityIds()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return _entityIds[i].Value;
        }
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
