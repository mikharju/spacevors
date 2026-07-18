namespace Spacevors.Domain;

public class EventQueue
{
    private readonly List<object> _events = new();

    public void Queue<T>(T eventObj) where T : notnull
    {
        _events.Add(eventObj);
    }

    public IEnumerable<object> Drain()
    {
        foreach (var e in _events)
        {
            yield return e;
        }
        _events.Clear();
    }

    public void Clear()
    {
        _events.Clear();
    }
}
