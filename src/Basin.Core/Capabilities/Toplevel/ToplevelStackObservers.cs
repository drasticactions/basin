namespace Basin.Capabilities;

/// <summary>
/// A list of <see cref="IToplevelStackObserver"/> instances for an implementation of <see cref="IToplevelStack"/>.
/// An observer can remove itself during dispatch.
/// </summary>
public sealed class ToplevelStackObservers
{
    private readonly ObserverList<IToplevelStackObserver> _observers = new();

    /// <summary>
    /// Adds an observer at the end of the list.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    public void Add(IToplevelStackObserver observer) => _observers.Add(observer);

    /// <summary>
    /// Removes an observer. The call has no effect when the observer is not in the list.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    public void Remove(IToplevelStackObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Calls <see cref="IToplevelStackObserver.OnToplevelStackChanged"/> on each observer.
    /// </summary>
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
