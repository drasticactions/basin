using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// Holds a list of <see cref="ICaptureDamageObserver"/> instances for an implementation of <see cref="IScreenCapture"/>. Observers can add or remove observers during a dispatch.
/// </summary>
public sealed class CaptureDamageObservers
{
    private readonly ObserverList<ICaptureDamageObserver> _observers = new();

    /// <summary>
    /// Adds an observer.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    public void Add(ICaptureDamageObserver observer) => _observers.Add(observer);

    /// <summary>
    /// Removes an observer.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    public void Remove(ICaptureDamageObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Tells each observer that a part of an output changed.
    /// </summary>
    /// <param name="output">The output that changed.</param>
    /// <param name="damage">The changed area in output buffer pixels.</param>
    public void Damaged(IOutput output, in Box damage)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnSourceDamaged(output, damage);
        }

        _observers.EndDispatch();
    }

    /// <summary>
    /// Tells each observer that the cursor image or its position changed.
    /// </summary>
    public void CursorChanged()
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnCursorChanged();
        }

        _observers.EndDispatch();
    }
}
