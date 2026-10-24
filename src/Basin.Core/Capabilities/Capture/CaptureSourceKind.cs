namespace Basin.Capabilities;

/// <summary>
/// Lists the kinds of <see cref="CaptureSource"/>.
/// </summary>
public enum CaptureSourceKind
{
    /// <summary>
    /// No source. No implementation supports it.
    /// </summary>
    None = 0,

    /// <summary>
    /// A whole output.
    /// </summary>
    Output,

    /// <summary>
    /// One window.
    /// </summary>
    Toplevel,

    /// <summary>
    /// The cursor image.
    /// </summary>
    Cursor,

    /// <summary>
    /// An area of the layout.
    /// </summary>
    Region,
}
