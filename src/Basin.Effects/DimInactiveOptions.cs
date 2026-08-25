namespace Basin.Effects;

public readonly record struct DimInactiveOptions
{
    public DimInactiveOptions()
    {
    }

    public int Strength { get; init; } = 25;

    public bool DimPanels { get; init; } = false;

    public bool DimDesktop { get; init; } = false;

    public bool DimKeepAbove { get; init; } = false;

    public bool DimByGroup { get; init; } = true;

    public bool DimFullScreen { get; init; } = true;

    public double ActivateMillis { get; init; } = 160;

    public double FullScreenMillis { get; init; } = 250;
}
