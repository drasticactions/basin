namespace Basin.UI.Skia;

/// <summary>
/// Receives the pointer input of an <see cref="ISkiaUISurface"/>. Coordinates are surface-local logical pixels.
/// </summary>
public interface ISkiaPointerHandler
{
    /// <summary>
    /// Tells the handler that the pointer entered the surface.
    /// </summary>
    /// <param name="x">The x coordinate in logical pixels.</param>
    /// <param name="y">The y coordinate in logical pixels.</param>
    void PointerEnter(double x, double y);

    /// <summary>
    /// Tells the handler that the pointer moved.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The x coordinate in logical pixels.</param>
    /// <param name="y">The y coordinate in logical pixels.</param>
    void PointerMotion(uint timeMs, double x, double y);

    /// <summary>
    /// Tells the handler that a pointer button changed.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The evdev button code, for example BTN_LEFT.</param>
    /// <param name="pressed"><see langword="true"/> when the button went down.</param>
    void PointerButton(uint timeMs, uint button, bool pressed);

    /// <summary>
    /// Tells the handler that the pointer scrolled. The default implementation ignores it.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The horizontal scroll distance, in wl_pointer axis units.</param>
    /// <param name="dy">The vertical scroll distance, in wl_pointer axis units.</param>
    void PointerAxis(uint timeMs, double dx, double dy)
    {
    }

    /// <summary>
    /// Tells the handler that the pointer left the surface.
    /// </summary>
    void PointerLeave();

    /// <summary>
    /// Gets the cursor shape that the handler wants at a point. The default is <see langword="null"/>.
    /// </summary>
    /// <param name="x">The x coordinate in logical pixels.</param>
    /// <param name="y">The y coordinate in logical pixels.</param>
    /// <returns>An xcursor name, or <see langword="null"/> to let the compositor choose.</returns>
    string? CursorAt(double x, double y) => null;
}
