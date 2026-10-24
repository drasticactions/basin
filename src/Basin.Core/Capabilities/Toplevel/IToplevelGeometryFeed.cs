namespace Basin.Capabilities;

/// <summary>
/// Receives the boxes in which the scene drew each surface, so that a toplevel source can report window geometry.
/// </summary>
public interface IToplevelGeometryFeed
{
    /// <summary>
    /// Updates the geometry of each known toplevel from the box of its root surface.
    /// The host calls it before a repaint after the scene changed.
    /// </summary>
    /// <param name="boxes">The layout box of each surface that the scene drew. The span is valid only during the call.</param>
    void ReportFrom(ReadOnlySpan<SurfaceBox> boxes);
}
