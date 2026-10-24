namespace Basin;

/// <summary>
/// The X11 display that a child process connects to, and the environment that goes with it.
/// </summary>
/// <param name="Display">The value for <c>DISPLAY</c>, for example <c>:1</c>.</param>
/// <param name="Environment">More variables to set in the child, for example scale hints.</param>
public sealed record X11Route(string Display, IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>
    /// The variable that names the X11 display that runs at scale 1, when a second Xwayland exists for that purpose.
    /// </summary>
    public const string LowDensityDisplayVariable = "BASIN_X11_LOW_DENSITY_DISPLAY";
}
