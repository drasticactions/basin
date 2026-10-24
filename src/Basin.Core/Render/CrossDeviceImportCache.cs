namespace Basin;

/// <summary>
/// Caches one cross-device conversion per buffer. A conversion is disposed when its buffer is destroyed or when the cache is disposed.
/// </summary>
/// <param name="convert">The factory that makes a conversion for a buffer, or returns <see langword="null"/> when it cannot.</param>
public sealed class CrossDeviceImportCache(Func<IBuffer, ICrossDeviceConversion?> convert) : IDisposable
{
    private readonly Dictionary<IBuffer, ICrossDeviceConversion?> _conversions = [];

    /// <summary>
    /// Gets the conversion for a buffer and makes it on the first call. A <see langword="null"/> result is cached too, so the factory runs once per buffer.
    /// </summary>
    /// <param name="buffer">The buffer from the other device.</param>
    /// <returns>The conversion, or <see langword="null"/> when the factory did not make one. The cache owns it.</returns>
    public ICrossDeviceConversion? Get(IBuffer buffer)
    {
        if (_conversions.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        var conversion = convert(buffer);
        _conversions[buffer] = conversion;
        buffer.Destroyed += () =>
        {
            if (_conversions.Remove(buffer, out var dead))
            {
                dead?.Dispose();
            }
        };

        return conversion;
    }

    /// <summary>
    /// Disposes every cached conversion and empties the cache.
    /// </summary>
    public void Dispose()
    {
        foreach (var conversion in _conversions.Values)
        {
            conversion?.Dispose();
        }

        _conversions.Clear();
    }
}
