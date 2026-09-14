using Tmds.DBus.Protocol;

namespace Basin.Portal;

public static class RestoreData
{
    public const uint Version = 1;

    public static VariantValue Encode(string vendor, IReadOnlyList<ScreenCastStream> streams)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        ArgumentNullException.ThrowIfNull(streams);
        var sources = new List<(uint, Dictionary<string, VariantValue>)>(streams.Count);
        foreach (var stream in streams)
        {
            var entry = new Dictionary<string, VariantValue>();
            var source = stream.Source;
            switch (source.Kind)
            {
                case ScreenCastSourceKind.Monitor:
                    entry["output"] = VariantValue.String(source.OutputName);
                    entry["output-size"] = Results.IntPair(stream.LayoutBox.Width, stream.LayoutBox.Height);
                    break;
                case ScreenCastSourceKind.Window:
                    entry["app-id"] = VariantValue.String(source.AppId);
                    entry["title"] = VariantValue.String(source.Title);
                    break;
            }

            sources.Add(((uint)source.Kind, entry));
        }

        var data = new Dictionary<string, VariantValue> { ["sources"] = Results.Streams(sources) };
        return Results.Restore(vendor, Version, Results.Dict(data));
    }

    public static IReadOnlyList<ScreenCastSource>? Decode(string vendor, string expectedVendor, uint version, VariantValue data)
    {
        if (!string.Equals(vendor, expectedVendor, StringComparison.Ordinal) || version != Version ||
            data.Type != VariantValueType.Dictionary)
        {
            return null;
        }

        var dictionary = data.GetDictionary<string, VariantValue>();
        if (!dictionary.TryGetValue("sources", out var sources) || sources.Type != VariantValueType.Array)
        {
            return null;
        }

        var list = new List<ScreenCastSource>(sources.Count);
        for (var i = 0; i < sources.Count; i++)
        {
            var item = sources.GetItem(i);
            if (item.Type != VariantValueType.Struct || item.Count != 2)
            {
                return null;
            }

            var kind = (ScreenCastSourceKind)item.GetItem(0).GetUInt32();
            var entry = item.GetItem(1);
            if (entry.Type != VariantValueType.Dictionary)
            {
                return null;
            }

            var properties = entry.GetDictionary<string, VariantValue>();
            switch (kind)
            {
                case ScreenCastSourceKind.Monitor:
                    list.Add(new ScreenCastSource(kind, null, 0, OutputName: String(properties, "output")));
                    break;
                case ScreenCastSourceKind.Window:
                    list.Add(new ScreenCastSource(kind, null, 0, AppId: String(properties, "app-id"), Title: String(properties, "title")));
                    break;
                default:
                    return null;
            }
        }

        return list.Count == 0 ? null : list;
    }

    private static string String(Dictionary<string, VariantValue> properties, string key) =>
        properties.TryGetValue(key, out var value) && value.Type == VariantValueType.String ? value.GetString() : "";
}
