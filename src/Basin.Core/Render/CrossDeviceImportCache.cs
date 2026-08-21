namespace Basin;

public sealed class CrossDeviceImportCache(Func<IBuffer, ICrossDeviceConversion?> convert) : IDisposable
{
    private readonly Dictionary<IBuffer, ICrossDeviceConversion?> _conversions = [];

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

    public void Dispose()
    {
        foreach (var conversion in _conversions.Values)
        {
            conversion?.Dispose();
        }

        _conversions.Clear();
    }
}
