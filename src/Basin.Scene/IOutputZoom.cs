namespace Basin.Scene;

/// <summary>
/// Magnifies the outputs of a scene. Zoom changes only what the outputs present. Captures, screencasts and input stay in layout coordinates.
/// </summary>
public interface IOutputZoom
{
    /// <summary>
    /// Gets the zoom factor that every output shows. It is 1 when zoom is off.
    /// </summary>
    double Factor { get; }

    /// <summary>
    /// Gets or sets what moves the visible area.
    /// </summary>
    ZoomFollow Follow { get; set; }

    /// <summary>
    /// Occurs after the factor changed.
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// Turns zoom on at the last factor that was on, or off.
    /// </summary>
    void Toggle();

    /// <summary>
    /// Multiplies the factor by one step.
    /// </summary>
    void ZoomIn();

    /// <summary>
    /// Divides the factor by one step. A result below one step turns zoom off.
    /// </summary>
    void ZoomOut();

    /// <summary>
    /// Sets the factor of every output. The output under the pointer centers on the pointer.
    /// </summary>
    /// <param name="factor">The factor, clamped to the range from 1 to <see cref="SceneOutput.MaxZoom"/>.</param>
    void SetFactor(double factor);

    /// <summary>
    /// Moves the visible area of the output that holds a box so the box shows. It does nothing when zoom is off or <see cref="Follow"/> does not include the source.
    /// </summary>
    /// <param name="box">The box in logical layout coordinates.</param>
    /// <param name="source">What the box is.</param>
    void Show(in Box box, ZoomFollow source);
}
