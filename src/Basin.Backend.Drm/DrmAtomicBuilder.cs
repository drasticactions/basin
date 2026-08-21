using Drm;

namespace Basin.Backend.Drm;

internal sealed class DrmAtomicBuilder : IDisposable
{
    public readonly DrmAtomicRequest Request = new();
    private readonly List<(string Label, uint ObjectId, string Prop, ulong Value)> _entries = [];

    public void Reset()
    {
        Request.Cursor = 0;
        _entries.Clear();
    }

    public void Add(DrmPropertyMap map, string objectLabel, string property, ulong value)
    {
        Request.AddProperty(map.ObjectId, map.IdOf(property), value);
        _entries.Add((objectLabel, map.ObjectId, property, value));
    }

    public void Dump(TextWriter writer)
    {
        foreach (var (label, objectId, prop, value) in _entries)
        {
            writer.WriteLine($"    {label}#{objectId} {prop} = {value} (0x{value:X})");
        }
    }

    public override string ToString()
    {
        var writer = new StringWriter();
        Dump(writer);
        return writer.ToString();
    }

    public void Dispose() => Request.Dispose();
}
