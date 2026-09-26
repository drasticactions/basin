namespace TinyComp;

internal sealed record SettingKey(string Section, string Table, string Key, string Label, SettingKind Kind)
{
    public string Path => Table.Length == 0 ? Key : Table + "." + Key;

    public string? Default { get; init; }

    public IReadOnlyList<string>? Choices { get; init; }

    public IReadOnlyList<string>? Presets { get; init; }

    public string? ChoicesFrom { get; init; }

    public double Min { get; init; } = double.NegativeInfinity;

    public double Max { get; init; } = double.PositiveInfinity;

    public double Step { get; init; }

    public bool Restart { get; init; }

    public string? FlagKey { get; init; }

    public string? Flag { get; init; }

    public string? Group { get; init; }

    public string? Note { get; init; }

    public bool Optional { get; init; }

    public double Suggested { get; init; }

    public Func<SettingsDraft, bool>? Visible { get; init; }
}
