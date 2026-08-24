namespace Basin.Capabilities;

public sealed class ToplevelStackObservers
{
    private readonly ObserverList<IToplevelStackObserver> _observers = new();

    public void Add(IToplevelStackObserver observer) => _observers.Add(observer);

    public void Remove(IToplevelStackObserver observer) => _observers.Remove(observer);

    public void Changed()
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToplevelStackChanged();
        }

        _observers.EndDispatch();
    }
}
