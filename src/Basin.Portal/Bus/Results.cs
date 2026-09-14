using Tmds.DBus.Protocol;

namespace Basin.Portal;

public static class Results
{
    public static Dictionary<string, VariantValue> Empty => [];

    public static Dictionary<string, VariantValue> With(string key, VariantValue value) =>
        new() { [key] = value };

    public static VariantValue IntPair(int x, int y) => VariantValue.Struct(VariantValue.Int32(x), VariantValue.Int32(y));

    public static VariantValue Restore(string vendor, uint version, VariantValue data) =>
        VariantValue.Struct(VariantValue.String(vendor), VariantValue.UInt32(version), VariantValue.Variant(data));

    public static VariantValue Streams(IReadOnlyList<(uint NodeId, Dictionary<string, VariantValue> Properties)> streams)
    {
        var array = new Array<Struct<uint, Dict<string, VariantValue>>>(streams.Count);
        foreach (var (nodeId, properties) in streams)
        {
            array.Add(Struct.Create(nodeId, ToDict(properties)));
        }

        return array.AsVariantValue();
    }

    public static VariantValue Shortcuts((string, Dictionary<string, VariantValue>)[] shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        var array = new Array<Struct<string, Dict<string, VariantValue>>>(shortcuts.Length);
        foreach (var (id, properties) in shortcuts)
        {
            array.Add(Struct.Create(id, ToDict(properties)));
        }

        return array.AsVariantValue();
    }

    public static VariantValue Shortcuts(IReadOnlyList<(string Id, Dictionary<string, VariantValue> Properties)> shortcuts)
    {
        var array = new Array<Struct<string, Dict<string, VariantValue>>>(shortcuts.Count);
        foreach (var (id, properties) in shortcuts)
        {
            array.Add(Struct.Create(id, ToDict(properties)));
        }

        return array.AsVariantValue();
    }

    public static VariantValue Dict(Dictionary<string, VariantValue> properties) => ToDict(properties).AsVariantValue();

    private static Dict<string, VariantValue> ToDict(Dictionary<string, VariantValue> properties)
    {
        var dict = new Dict<string, VariantValue>();
        foreach (var (key, value) in properties)
        {
            dict.Add(key, value);
        }

        return dict;
    }
}
