using Drm;

namespace Basin.Backend.Drm;

internal sealed class DrmPropertyMap
{
    private readonly DrmDevice _device;
    private readonly DrmObjectType _type;
    private readonly Dictionary<string, (uint Id, ulong Value)> _byName = new(StringComparer.Ordinal);

    public DrmPropertyMap(DrmDevice device, uint objectId, DrmObjectType type)
    {
        _device = device;
        _type = type;
        ObjectId = objectId;
        Refresh();
    }

    public uint ObjectId { get; }

    public bool Has(string name) => _byName.ContainsKey(name);

    public uint IdOf(string name) =>
        _byName.TryGetValue(name, out var entry)
            ? entry.Id
            : throw new InvalidOperationException($"{_type} {ObjectId} has no property '{name}'");

    public ulong ValueOf(string name) => _byName[name].Value;

    public bool TryGetValue(string name, out ulong value)
    {
        if (_byName.TryGetValue(name, out var entry))
        {
            value = entry.Value;
            return true;
        }

        value = 0;
        return false;
    }

    public void Refresh()
    {
        foreach (var value in _device.GetObjectProperties(ObjectId, _type))
        {
            var property = _device.GetProperty(value.PropertyId);
            _byName[property.Name] = (property.PropertyId, value.Value);
        }
    }
}
