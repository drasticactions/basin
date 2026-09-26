namespace Basin.Ipc;

public sealed class IpcClientState : IDisposable
{
    private static long _nextId;

    private readonly Dictionary<object, IDisposable> _owned = [];

    public long Id { get; } = Interlocked.Increment(ref _nextId);

    public int Pid { get; internal set; }

    public int Count => _owned.Count;

    public T? Get<T>(object key)
        where T : class, IDisposable =>
        _owned.TryGetValue(key, out var value) ? value as T : null;

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

    public bool Remove(object key)
    {
        if (!_owned.Remove(key, out var value))
        {
            return false;
        }

        value.Dispose();
        return true;
    }

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
