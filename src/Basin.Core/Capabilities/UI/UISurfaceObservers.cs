using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// A list of <see cref="IUISurfaceObserver"/> instances for an implementation of <see cref="IUISurface"/>.
/// An observer can remove itself during dispatch.
/// </summary>
public sealed class UISurfaceObservers
{
    private readonly ObserverList<IUISurfaceObserver> _observers = new();

    /// <summary>
    /// Adds an observer at the end of the list.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    public void Add(IUISurfaceObserver observer) => _observers.Add(observer);

    /// <summary>
    /// Removes an observer. The call has no effect when the observer is not in the list.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    public void Remove(IUISurfaceObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Calls <see cref="IUISurfaceObserver.OnSurfaceDamaged"/> on each observer.
    /// </summary>
    /// <param name="surface">The surface that changed.</param>
    /// <param name="damage">The changed region. An empty region means the whole surface.</param>
    public void Damaged(IUISurface surface, PixmanRegion32 damage)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnSurfaceDamaged(surface, damage);
        }

        _observers.EndDispatch();
    }

    /// <summary>
    /// Calls <see cref="IUISurfaceObserver.OnSurfaceDestroyed"/> on each observer.
    /// </summary>
    /// <param name="surface">The disposed surface.</param>
    public void Destroyed(IUISurface surface)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnSurfaceDestroyed(surface);
        }

        _observers.EndDispatch();
    }
}
