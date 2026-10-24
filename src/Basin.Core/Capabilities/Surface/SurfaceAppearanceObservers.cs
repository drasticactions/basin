namespace Basin.Capabilities;

/// <summary>
/// Holds a list of <see cref="ISurfaceAppearanceObserver"/> instances for an implementation of <see cref="ISurfaceAppearance"/>. Observers can add or remove observers during a dispatch.
/// </summary>
public sealed class SurfaceAppearanceObservers
{
    private readonly ObserverList<ISurfaceAppearanceObserver> _observers = new();

    /// <summary>
    /// Adds an observer.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    public void Add(ISurfaceAppearanceObserver observer) => _observers.Add(observer);

    /// <summary>
    /// Removes an observer.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    public void Remove(ISurfaceAppearanceObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Tells each observer that the appearance of a surface changed.
    /// </summary>
    /// <param name="surface">The surface that changed.</param>
    public void Changed(Surface surface)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.AppearanceChanged(surface);
        }

        _observers.EndDispatch();
    }
}
