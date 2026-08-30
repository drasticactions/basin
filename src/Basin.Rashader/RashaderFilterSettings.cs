namespace Basin.Rashader;

public readonly record struct RashaderFilterSettings
{
    public bool ContinuousRepaint { get; init; }

    public bool DisableCache { get; init; }

    public bool VerticalScreen { get; init; }
}
