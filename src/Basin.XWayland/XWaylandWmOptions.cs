namespace Basin.XWayland;

/// <summary>
/// The display settings that an <see cref="XWaylandWm"/> publishes to X11 clients.
/// </summary>
public sealed record XWaylandWmOptions
{
    /// <summary>
    /// Gets the X11 scale. X11 coordinates are logical coordinates times this value.
    /// </summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>
    /// Gets the allowed DPI values. An empty list publishes the exact DPI. <see cref="XWaylandDpi.For"/> explains the choice.
    /// </summary>
    public IReadOnlyList<int> DpiBuckets { get; init; } = [];

    /// <summary>
    /// Gets the cursor theme name. An empty string leaves the theme to the X defaults.
    /// </summary>
    public string CursorTheme { get; init; } = string.Empty;

    /// <summary>
    /// Gets the cursor size in logical pixels. The published size is this value times <see cref="Scale"/>.
    /// </summary>
    public int CursorSize { get; init; } = 24;

    /// <summary>
    /// Gets a value indicating whether the window manager takes the XSETTINGS selection.
    /// </summary>
    public bool PublishXSettings { get; init; } = true;
}
