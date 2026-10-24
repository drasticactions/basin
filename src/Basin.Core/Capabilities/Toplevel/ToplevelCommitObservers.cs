namespace Basin.Capabilities;

/// <summary>
/// A set of <see cref="IToplevelCommitObserver"/> instances for an implementation of a toplevel model or source.
/// It holds each observer once, and an observer can remove itself during dispatch.
/// </summary>
public sealed class ToplevelCommitObservers
{
    private readonly ObserverList<IToplevelCommitObserver> _observers = new();
    private readonly HashSet<IToplevelCommitObserver> _members = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets the number of observers in the set.
    /// </summary>
    public int Count => _members.Count;

    /// <summary>
    /// Adds an observer. A second add of the same instance has no effect.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    public void Add(IToplevelCommitObserver observer)
    {
        if (_members.Add(observer))
        {
            _observers.Add(observer);
        }
    }

    /// <summary>
    /// Removes an observer. The call has no effect when the observer is not in the set.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    public void Remove(IToplevelCommitObserver observer)
    {
        if (_members.Remove(observer))
        {
            _observers.Remove(observer);
        }
    }

    /// <summary>
    /// Sends a content commit to each observer, in the order of addition.
    /// </summary>
    /// <param name="toplevelId">The id of the toplevel that committed.</param>
    /// <param name="damage">The damaged area in the surface coordinates of the root surface.</param>
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
