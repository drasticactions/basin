namespace Basin.Sdl;

/// <summary>
/// The settings of an <see cref="SdlOutputWindow"/> at creation.
/// </summary>
public sealed record SdlOutputWindowOptions
{
    /// <summary>
    /// Gets the window title. The default is "basin".
    /// </summary>
    public string Title { get; init; } = "basin";

    /// <summary>
    /// Gets the window width in points. The default is 1280.
    /// </summary>
    public int Width { get; init; } = 1280;

    /// <summary>
    /// Gets the window height in points. The default is 800.
    /// </summary>
    public int Height { get; init; } = 800;

    /// <summary>
    /// Gets the name of the output that the window shows. It is <see langword="null"/> to let the host choose one.
    /// </summary>
    public string? OutputName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user can resize a top-level window. The default is <see langword="true"/>.
    /// </summary>
    public bool Resizable { get; init; } = true;

    /// <summary>
    /// Gets the output scale to use in place of the display scale. It is <see langword="null"/> to follow the display.
    /// </summary>
    public double? Scale { get; init; }

    /// <summary>
    /// Gets a value indicating whether the window grabs the keyboard while it has focus, so host shortcuts reach the guest.
    /// </summary>
    public bool GrabKeyboard { get; init; }

    /// <summary>
    /// Gets the parent window of a popup. It is <see langword="null"/> for a top-level window.
    /// </summary>
    public SdlWindow? PopupParent { get; init; }

    /// <summary>
    /// Gets the horizontal offset of a popup from its parent in points.
    /// </summary>
    public int PopupX { get; init; }

    /// <summary>
    /// Gets the vertical offset of a popup from its parent in points.
    /// </summary>
    public int PopupY { get; init; }

    /// <summary>
    /// Gets a value indicating whether a popup is a tooltip. A popup that is not a tooltip is a popup menu.
    /// </summary>
    public bool Tooltip { get; init; }

    /// <summary>
    /// Gets a value indicating whether the window is transparent and composited on a clear background.
    /// </summary>
    public bool Transparent { get; init; }

    /// <summary>
    /// Gets a value indicating whether a top-level window has no host decorations.
    /// </summary>
    public bool Borderless { get; init; }
}
