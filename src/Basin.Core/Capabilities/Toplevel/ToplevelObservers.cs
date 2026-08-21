using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

public sealed class ToplevelObservers
{
    private readonly ObserverList<IToplevelObserver> _observers = new();

    public void Add(IToplevelObserver observer) => _observers.Add(observer);

    public void Remove(IToplevelObserver observer) => _observers.Remove(observer);

    public void Added(ulong toplevelId)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToplevelAdded(toplevelId);
        }

        _observers.EndDispatch();
    }

    public void Changed(ulong toplevelId)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToplevelChanged(toplevelId);
        }

        _observers.EndDispatch();
    }

    public void Removed(ulong toplevelId)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToplevelRemoved(toplevelId);
        }

        _observers.EndDispatch();
    }
}
