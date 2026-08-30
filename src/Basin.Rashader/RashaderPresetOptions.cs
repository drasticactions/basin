namespace Basin.Rashader;

public readonly record struct RashaderPresetOptions
{
    public RashaderRuntime Runtime { get; init; }

    public bool VerticalScreen { get; init; }
}
