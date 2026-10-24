namespace Basin.Scene;

/// <summary>
/// An output that <see cref="OutputZoom"/> can magnify. <see cref="SceneOutput"/> is one, and <see cref="ZoomView"/> serves an output that is drawn without one.
/// </summary>
public interface IZoomable
{
    /// <summary>
    /// Gets the output.
    /// </summary>
    IOutput Output { get; }

    /// <summary>
    /// Gets the zoom factor. It is 1 when the output shows its whole area.
    /// </summary>
    double ZoomFactor { get; }

    /// <summary>
    /// Gets the whole area of the output in logical layout coordinates, whatever the zoom factor is.
    /// </summary>
    (double X, double Y, double Width, double Height) LogicalArea { get; }

    /// <summary>
    /// Gets the area of the layout that the output shows, in logical layout coordinates.
    /// </summary>
    (double X, double Y, double Width, double Height) ZoomArea { get; }

    /// <summary>
    /// Magnifies the output about a point. The visible area stays inside the output's own area.
    /// </summary>
    /// <param name="factor">The zoom factor, clamped to the range from 1 to <see cref="SceneOutput.MaxZoom"/>.</param>
    /// <param name="centerX">The horizontal center in logical layout coordinates.</param>
    /// <param name="centerY">The vertical center in logical layout coordinates.</param>
    void SetZoom(double factor, double centerX, double centerY);
}
