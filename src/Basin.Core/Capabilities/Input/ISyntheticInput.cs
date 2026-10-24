namespace Basin.Capabilities;

/// <summary>
/// Injects input into the compositor as if a seat device sent it, for example for automation.
/// Positions are in layout coordinates. Each method returns <see langword="false"/> when the input goes nowhere.
/// </summary>
public interface ISyntheticInput
{
    /// <summary>Moves the pointer to a position.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    bool PointerMotionAbsolute(uint timeMs, double x, double y);

    /// <summary>Presses or releases a pointer button.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The evdev button code, for example <c>BTN_LEFT</c>.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    bool PointerButton(uint timeMs, uint button, bool pressed);

    /// <summary>Scrolls on one axis. The default implementation returns <see langword="false"/>.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The <c>wl_pointer.axis</c> value: 0 is vertical, 1 is horizontal.</param>
    /// <param name="value">The scroll distance in the units of <c>wl_pointer.axis</c>.</param>
    /// <param name="source">The <c>wl_pointer.axis_source</c> value.</param>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    bool PointerAxis(uint timeMs, uint axis, double value, uint source) => false;

    /// <summary>Presses or releases a key.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    bool Key(uint timeMs, uint keycode, bool pressed);

    /// <summary>Starts a touch point. The default implementation returns <see langword="false"/>.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The identifier of the touch point.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    bool TouchDown(uint timeMs, int id, double x, double y) => false;

    /// <summary>Moves a touch point. The default implementation returns <see langword="false"/>.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The identifier of the touch point.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    bool TouchMotion(uint timeMs, int id, double x, double y) => false;

    /// <summary>Ends a touch point. The default implementation returns <see langword="false"/>.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The identifier of the touch point.</param>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    bool TouchUp(uint timeMs, int id) => false;

    /// <summary>Ends a group of touch events that belong together. The default implementation returns <see langword="false"/>.</summary>
    /// <returns><see langword="true"/> when the frame was delivered.</returns>
    bool TouchFrame() => false;

    /// <summary>Cancels all active touch points. The default implementation returns <see langword="false"/>.</summary>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    bool TouchCancel() => false;

    /// <summary>Reads the pointer position that the injected input sees. The default implementation gives 0, 0 and returns <see langword="false"/>.</summary>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns><see langword="true"/> when the position comes from a live injection target.</returns>
    bool TryPointerPosition(out double x, out double y)
    {
        x = 0;
        y = 0;
        return false;
    }
}
