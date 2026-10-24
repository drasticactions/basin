namespace Basin.Capabilities;

/// <summary>
/// Tells whether content that is excluded from capture is at a position or has keyboard focus. Synthetic input uses it to refuse input into such content.
/// </summary>
public interface ICaptureExclusion
{
    /// <summary>
    /// Gets a value that tells whether excluded content is at a layout position.
    /// </summary>
    /// <param name="x">The X position in layout coordinates.</param>
    /// <param name="y">The Y position in layout coordinates.</param>
    /// <returns><see langword="true"/> when the topmost content at the position is excluded from capture, otherwise <see langword="false"/>.</returns>
    bool IsExcludedAt(double x, double y);

    /// <summary>
    /// Gets a value indicating whether the surface with keyboard focus is excluded from capture.
    /// </summary>
    bool IsKeyboardFocusExcluded { get; }
}
