namespace Basin.Ipc;

/// <summary>
/// Holds what one client owns, such as an idle inhibitor. Disposal of the state disposes each owned resource, so the resources go when the connection closes.
/// </summary>
public sealed class IpcClientState : IDisposable
{
    private static long _nextId;

    private readonly Dictionary<object, IDisposable> _owned = [];

    /// <summary>
    /// Gets an id that is unique among the client states of this process.
    /// </summary>
    public long Id { get; } = Interlocked.Increment(ref _nextId);

    /// <summary>
    /// Gets the peer process id from <c>SO_PEERCRED</c>. It is 0 for the line front and for an adopted descriptor.
    /// </summary>
    public int Pid { get; internal set; }

    /// <summary>
    /// Gets the number of owned resources.
    /// </summary>
    public int Count => _owned.Count;

    /// <summary>
    /// Finds an owned resource by its key.
    /// </summary>
    /// <typeparam name="T">The type of the resource.</typeparam>
    /// <param name="key">The key that <see cref="Set"/> used.</param>
    /// <returns>The resource, or <see langword="null"/> when the key is absent or the resource is not a <typeparamref name="T"/>.</returns>
    public T? Get<T>(object key)
        where T : class, IDisposable =>
        _owned.TryGetValue(key, out var value) ? value as T : null;

    /// <summary>
    /// Stores a resource under a key. A resource that the key held before is disposed.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The resource. The state disposes it when the key is removed or replaced, or when the state is disposed.</param>
    public void Set(object key, IDisposable value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (_owned.Remove(key, out var previous))
        {
            previous.Dispose();
        }

        _owned[key] = value;
    }

    /// <summary>
    /// Removes the resource under a key and disposes it.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="false"/> when the key holds no resource.</returns>
    public bool Remove(object key)
    {
        if (!_owned.Remove(key, out var value))
        {
            return false;
        }

        value.Dispose();
        return true;
    }

    /// <summary>
    /// Disposes every owned resource and forgets them.
    /// </summary>
    public void Dispose()
    {
        var values = _owned.Values.ToArray();
        _owned.Clear();
        foreach (var value in values)
        {
            value.Dispose();
        }
    }
}
