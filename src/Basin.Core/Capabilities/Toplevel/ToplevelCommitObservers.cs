namespace Basin.Capabilities;

public sealed class ToplevelCommitObservers
{
    private readonly ObserverList<IToplevelCommitObserver> _observers = new();
    private readonly HashSet<IToplevelCommitObserver> _members = new(ReferenceEqualityComparer.Instance);

    public int Count => _members.Count;

    public void Add(IToplevelCommitObserver observer)
    {
        if (_members.Add(observer))
        {
            _observers.Add(observer);
        }
    }

    public void Remove(IToplevelCommitObserver observer)
    {
        if (_members.Remove(observer))
        {
            _observers.Remove(observer);
        }
    }

    public void Committed(ulong toplevelId, in Box damage)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToplevelCommitted(toplevelId, damage);
        }

        _observers.EndDispatch();
    }
}
