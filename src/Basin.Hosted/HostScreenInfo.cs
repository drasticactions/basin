namespace Basin.Hosted;

/// <summary>
/// A description of one host monitor that <see cref="HostScreens"/> publishes as a <c>wl_output</c>.
/// Positions and sizes are in host device pixels. The layout divides them by <paramref name="Scaling"/>.
/// </summary>
/// <param name="Key">The stable identity of the monitor. Rows with the same key are the same output.</param>
/// <param name="Name">The output name that clients see.</param>
/// <param name="X">The left edge in host device pixels.</param>
/// <param name="Y">The top edge in host device pixels.</param>
/// <param name="Width">The width in host device pixels.</param>
/// <param name="Height">The height in host device pixels.</param>
/// <param name="Scaling">The host's scale factor for the monitor.</param>
/// <param name="Primary">A value indicating whether the host names this monitor as its primary one.</param>
public sealed record HostScreenInfo(
    string Key, string Name, int X, int Y, int Width, int Height, double Scaling, bool Primary)
{
    /// <summary>
    /// Gets the rotation and flip of the monitor.
    /// </summary>
    public OutputTransform Transform { get; init; }
}
