using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// A list of observers that stays safe to change while it is walked. A removal during a dispatch leaves a
/// <see langword="null"/> slot, and the list compacts when the last dispatch ends.
/// </summary>
/// <typeparam name="T">The observer type.</typeparam>
public sealed class ObserverList<T>
    where T : class
{
    private readonly List<T?> _observers = [];
    private readonly ThreadAffinity _affinity = ThreadAffinity.Capture();
    private int _dispatching;
    private bool _compact;

    /// <summary>
    /// Gets the number of slots, including the <see langword="null"/> slots of observers removed during a dispatch.
    /// </summary>
    public int Count => _observers.Count;

    /// <summary>
    /// Gets the observer in a slot. It is <see langword="null"/> when the observer was removed during the current dispatch.
    /// </summary>
    /// <param name="index">The slot index, from 0 to the count that <see cref="BeginDispatch"/> returned.</param>
    /// <returns>The observer, or <see langword="null"/> for a removed one.</returns>
    public T? this[int index] => _observers[index];

    /// <summary>
    /// Adds an observer at the end of the list. An observer added during a dispatch is not called by that dispatch.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="observer"/> is <see langword="null"/>.</exception>
    public void Add(T observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _affinity.Assert();
        _observers.Add(observer);
    }

    /// <summary>
    /// Removes an observer. During a dispatch, the slot becomes <see langword="null"/> so the walk does not skip an entry.
    /// An observer that is not in the list is ignored.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="observer"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Starts a walk over the list. Each call must have a matching <see cref="EndDispatch"/>, and walks can nest.
    /// </summary>
    /// <returns>The number of slots to walk, which ignores observers added after this call.</returns>
    public int BeginDispatch()
    {
        _dispatching++;
        return _observers.Count;
    }

    /// <summary>
    /// Ends a walk that <see cref="BeginDispatch"/> started. The outermost call removes the slots that removals left empty.
    /// </summary>
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
