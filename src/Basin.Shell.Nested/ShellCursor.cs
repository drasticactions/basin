using Basin.Capabilities;

namespace Basin.Shell.Nested;

/// <summary>
/// The cursor that a client asks for while the pointer is over it. <see cref="NestedShell.ClientCursorChanged"/> carries it to the host.
/// </summary>
/// <param name="Shape">The named cursor shape, or <see langword="null"/> when the client gives no shape.</param>
/// <param name="Surface">The cursor image surface, or <see langword="null"/> when the client gives no image.</param>
/// <param name="HotspotX">The horizontal hotspot in surface-local logical pixels.</param>
/// <param name="HotspotY">The vertical hotspot in surface-local logical pixels.</param>
/// <param name="Hidden">A value indicating whether the client hides the cursor.</param>
public readonly record struct ShellCursor(CursorShape? Shape, Surface? Surface, int HotspotX, int HotspotY, bool Hidden)
{
    /// <summary>
    /// No shape, no image and not hidden. The host shows its default cursor.
    /// </summary>
    public static readonly ShellCursor Default = default;

    /// <summary>
    /// A hidden cursor.
    /// </summary>
    public static readonly ShellCursor None = new(null, null, 0, 0, true);

    /// <summary>
    /// Makes a cursor from a named shape.
    /// </summary>
    /// <param name="shape">The shape that the client asked for.</param>
    /// <returns>A visible cursor with the shape and no image.</returns>
    public static ShellCursor Of(CursorShape shape) => new(shape, null, 0, 0, false);

    /// <summary>
    /// Makes a cursor from a client surface. The host reads the surface content on each change.
    /// </summary>
    /// <param name="surface">The cursor surface that the client set.</param>
    /// <param name="hotspotX">The horizontal hotspot in surface-local logical pixels.</param>
    /// <param name="hotspotY">The vertical hotspot in surface-local logical pixels.</param>
    /// <returns>A visible cursor with the image and no shape.</returns>
    public static ShellCursor Image(Surface surface, int hotspotX, int hotspotY) => new(null, surface, hotspotX, hotspotY, false);
}
