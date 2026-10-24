namespace Basin.Capabilities;

/// <summary>
/// What a window is and what its frame must show. The consumer fills it, and an <see cref="IFrameRenderer"/> reads it.
/// </summary>
public readonly record struct FrameState
{
    /// <summary>
    /// Gets the window title, or <see langword="null"/> when the window has none.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the app id of the window, or <see langword="null"/> when it has none.
    /// </summary>
    public string? AppId { get; init; }

    /// <summary>
    /// Gets the icon to draw.
    /// </summary>
    public FrameIcon Icon { get; init; }

    /// <summary>
    /// Gets a value indicating whether the window has keyboard focus.
    /// </summary>
    public bool Active { get; init; }

    /// <summary>
    /// Gets a value indicating whether the window is maximized.
    /// </summary>
    public bool Maximized { get; init; }

    /// <summary>
    /// Gets a value indicating whether the window is fullscreen.
    /// </summary>
    public bool Fullscreen { get; init; }

    /// <summary>
    /// Gets a value indicating whether an interactive resize is in progress.
    /// </summary>
    public bool Resizing { get; init; }

    /// <summary>
    /// Gets the operations that the frame can offer. A renderer draws no control for an operation that is not set.
    /// </summary>
    public FrameCapabilities Capabilities { get; init; }

    /// <summary>
    /// Gets the window type.
    /// </summary>
    public FrameKind Kind { get; init; }

    /// <summary>
    /// Gets the screen edge that the window is tiled against.
    /// </summary>
    public FrameTiling Tiled { get; init; }

    /// <summary>
    /// Gets a value indicating whether the window is shaded to its title bar.
    /// </summary>
    public bool Shaded { get; init; }

    /// <summary>
    /// Gets a value indicating whether the window is kept above others.
    /// </summary>
    public bool Above { get; init; }

    /// <summary>
    /// Gets a value indicating whether the window shows on all workspaces.
    /// </summary>
    public bool Sticky { get; init; }

    /// <summary>
    /// Gets a value indicating whether an X11 window is on the low-density X server. A menu then offers full density.
    /// </summary>
    public bool LowDensity { get; init; }

    /// <summary>
    /// Gets where the client area sits on its output. An empty value means that the position is not known.
    /// </summary>
    public FramePlacement Placement { get; init; }

    /// <summary>
    /// Gets the color scheme that the window asks for.
    /// </summary>
    public FrameColorScheme ColorScheme { get; init; }
}
