using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// A list of <see cref="IToplevelObserver"/> instances for an implementation of a toplevel model or source.
/// An observer can remove itself during dispatch.
/// </summary>
public sealed class ToplevelObservers
{
    private readonly ObserverList<IToplevelObserver> _observers = new();

    /// <summary>
    /// Adds an observer at the end of the list.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    public void Add(IToplevelObserver observer) => _observers.Add(observer);

    /// <summary>
    /// Removes an observer. The call has no effect when the observer is not in the list.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    public void Remove(IToplevelObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Calls <see cref="IToplevelObserver.OnToplevelAdded"/> on each observer.
    /// </summary>
    /// <param name="toplevelId">The id of the new toplevel.</param>
    public void Added(ulong toplevelId)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToplevelAdded(toplevelId);
        }

        _observers.EndDispatch();
    }

    /// <summary>
    /// Calls <see cref="IToplevelObserver.OnToplevelChanged"/> on each observer.
    /// </summary>
    /// <param name="toplevelId">The id of the changed toplevel.</param>
    public void Changed(ulong toplevelId)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToplevelChanged(toplevelId);
        }

        _observers.EndDispatch();
    }

    /// <summary>
    /// Calls <see cref="IToplevelObserver.OnToplevelRemoved"/> on each observer.
    /// </summary>
    /// <param name="toplevelId">The id of the removed toplevel.</param>
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
