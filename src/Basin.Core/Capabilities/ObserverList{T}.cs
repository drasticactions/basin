using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

public sealed class ObserverList<T>
    where T : class
{
    private readonly List<T?> _observers = [];
    private readonly ThreadAffinity _affinity = ThreadAffinity.Capture();
    private int _dispatching;
    private bool _compact;

    public int Count => _observers.Count;

    public T? this[int index] => _observers[index];

    public void Add(T observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _affinity.Assert();
        _observers.Add(observer);
    }

    public void Remove(T observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _affinity.Assert();
        var index = _observers.IndexOf(observer);
        if (index < 0)
        {
            return;
        }

        if (_dispatching > 0)
        {
            _observers[index] = null;
            _compact = true;
            return;
        }

        _observers.RemoveAt(index);
    }

    public int BeginDispatch()
    {
        _dispatching++;
        return _observers.Count;
    }

    public void EndDispatch()
    {
        if (--_dispatching > 0 || !_compact)
        {
            return;
        }

        _compact = false;
        for (var i = _observers.Count - 1; i >= 0; i--)
        {
            if (_observers[i] is null)
            {
                _observers.RemoveAt(i);
            }
        }
    }
}
