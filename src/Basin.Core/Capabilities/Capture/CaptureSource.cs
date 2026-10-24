using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// Identifies what a capture reads: an output, a toplevel, the cursor or a layout region. Use the factory methods to create one.
/// </summary>
public readonly record struct CaptureSource
{
    private CaptureSource(CaptureSourceKind kind, IOutput? output, ulong toplevelId, bool overlayCursor,
        Box layoutBox = default, double scale = 0, bool clientOnly = false)
    {
        Kind = kind;
        OutputTarget = output;
        ToplevelId = toplevelId;
        OverlayCursor = overlayCursor;
        LayoutBox = layoutBox;
        Scale = scale;
        ClientOnly = clientOnly;
    }

    /// <summary>
    /// Gets the kind of the source. It is <see cref="CaptureSourceKind.None"/> for the default value.
    /// </summary>
    public CaptureSourceKind Kind { get; }

    /// <summary>
    /// Gets the output of an output or cursor source, otherwise <see langword="null"/>.
    /// </summary>
    public IOutput? OutputTarget { get; }

    /// <summary>
    /// Gets the toplevel id of a toplevel source, otherwise 0.
    /// </summary>
    public ulong ToplevelId { get; }

    /// <summary>
    /// Gets a value indicating whether the capture draws the cursor into the pixels.
    /// </summary>
    public bool OverlayCursor { get; }

    /// <summary>
    /// Gets the area in layout coordinates of a region source, otherwise an empty box.
    /// </summary>
    public Box LayoutBox { get; }

    /// <summary>
    /// Gets the scale of a region source in buffer pixels per layout unit. It is 0 when the implementation chooses the scale.
    /// </summary>
    public double Scale { get; }

    /// <summary>
    /// Gets a value indicating whether a toplevel capture leaves out the frame and the decorations that the compositor draws.
    /// </summary>
    public bool ClientOnly { get; }

    /// <summary>
    /// Creates a source that captures a whole output.
    /// </summary>
    /// <param name="output">The output to capture.</param>
    /// <param name="overlayCursor">A value indicating whether to draw the cursor into the pixels.</param>
    /// <returns>A source of kind <see cref="CaptureSourceKind.Output"/>.</returns>
    public static CaptureSource Output(IOutput output, bool overlayCursor = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new CaptureSource(CaptureSourceKind.Output, output, 0, overlayCursor);
    }

    /// <summary>
    /// Creates a source that captures one window.
    /// </summary>
    /// <param name="toplevelId">The toplevel id from <see cref="IToplevelModel"/>.</param>
    /// <param name="clientOnly">A value indicating whether to leave out the frame that the compositor draws.</param>
    /// <param name="overlayCursor">A value indicating whether to draw the cursor into the pixels.</param>
    /// <returns>A source of kind <see cref="CaptureSourceKind.Toplevel"/>.</returns>
    public static CaptureSource Toplevel(ulong toplevelId, bool clientOnly = false, bool overlayCursor = false) =>
        new(CaptureSourceKind.Toplevel, null, toplevelId, overlayCursor, clientOnly: clientOnly);

    /// <summary>
    /// Creates a source that captures the cursor image while it is on an output.
    /// </summary>
    /// <param name="output">The output that the cursor is on.</param>
    /// <returns>A source of kind <see cref="CaptureSourceKind.Cursor"/>.</returns>
    public static CaptureSource Cursor(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new CaptureSource(CaptureSourceKind.Cursor, output, 0, false);
    }

    /// <summary>
    /// Creates a source that captures an area of the layout, across outputs.
    /// </summary>
    /// <param name="layoutBox">The area in layout coordinates.</param>
    /// <param name="scale">The buffer pixels per layout unit, or 0 to use the largest scale of the outputs under the area.</param>
    /// <param name="overlayCursor">A value indicating whether to draw the cursor into the pixels.</param>
    /// <returns>A source of kind <see cref="CaptureSourceKind.Region"/>.</returns>
    public static CaptureSource Region(in Box layoutBox, double scale = 0, bool overlayCursor = false) =>
        new(CaptureSourceKind.Region, null, 0, overlayCursor, layoutBox, scale);
}
