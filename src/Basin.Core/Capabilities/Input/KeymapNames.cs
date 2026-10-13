namespace Basin.Capabilities;

public readonly record struct KeymapNames(
    string? Rules = null,
    string? Model = null,
    string? Layout = null,
    string? Variant = null,
    string? Options = null)
{
    public static KeymapNames Parse(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var open = id.IndexOf('(', StringComparison.Ordinal);
        if (open > 0 && id.EndsWith(')'))
        {
            return new KeymapNames(Layout: id[..open].Trim(), Variant: id[(open + 1)..^1].Trim());
        }

        return new KeymapNames(Layout: id.Trim());
    }

    public IEnumerable<string> EnvironmentLines()
    {
        if (Rules is { Length: > 0 } rules)
        {
            yield return "XKB_DEFAULT_RULES=" + rules;
        }

        if (Model is { Length: > 0 } model)
        {
            yield return "XKB_DEFAULT_MODEL=" + model;
        }

        if (Layout is { Length: > 0 } layout)
        {
            yield return "XKB_DEFAULT_LAYOUT=" + layout;
        }

        if (Variant is { Length: > 0 } variant)
        {
            yield return "XKB_DEFAULT_VARIANT=" + variant;
        }

        if (Options is { Length: > 0 } options)
        {
            yield return "XKB_DEFAULT_OPTIONS=" + options;
        }
    }
}
