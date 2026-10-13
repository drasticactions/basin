namespace Basin;

public sealed record X11Route(string Display, IReadOnlyDictionary<string, string> Environment)
{
    public const string LowDensityDisplayVariable = "BASIN_X11_LOW_DENSITY_DISPLAY";
}
