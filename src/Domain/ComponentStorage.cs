using System.Collections;

namespace Spacevors.Domain;

public class ComponentStorage<T> : ComponentStorageBase, IEnumerable<(Entity Entity, T Value)> where T : notnull
{
    private readonly List<T> _data = new();
    private readonly HashSet<int> _activeIds = new();

    public void Add(Entity entity, T value)
    {
        int id = entity.Value;
        if (id >= _data.Count)
        {
            var needed = id - _data.Count + 1;
            for (int i = 0; i < needed; i++)
            {
                _data.Add(default!);
            }
        }
        _data[id] = value;
        _activeIds.Add(id);
    }

    public T Get(Entity entity)
    {
        int id = entity.Value;
        if (_activeIds.Contains(id))
        {
            return _data[id];
        }
        throw new KeyNotFoundException($"Component of type {typeof(T).Name} not found for entity {entity.Value}");
    }

    public bool Has(Entity entity)
    {
        return _activeIds.Contains(entity.Value);
    }

    public override void Remove(Entity entity)
    {
        _activeIds.Remove(entity.Value);
    }

    public override int Count => _activeIds.Count;

    public override IEnumerable<int> GetEntityIds() => _activeIds;

    public IEnumerator<(Entity Entity, T Value)> GetEnumerator()
    {
        foreach (var id in _activeIds.OrderBy(x => x))
        {
            yield return (new Entity(id), _data[id]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
