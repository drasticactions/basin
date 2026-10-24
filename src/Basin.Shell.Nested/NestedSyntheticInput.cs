using Basin.Capabilities;
using Basin.Hosted;

namespace Basin.Shell.Nested;

/// <summary>
/// An <see cref="ISyntheticInput"/> that sends input through <see cref="NestedShell.HandleInput"/>.
/// Grabs, frames, consumer UI surfaces and the key table see the input before a client does.
/// Each method returns <see langword="false"/> after the shell is disposed.
/// </summary>
public sealed class NestedSyntheticInput : ISyntheticInput
{
    private const double AxisStep = 10;

    private readonly NestedShell _shell;

    /// <summary>
    /// Initializes a new instance of the <see cref="NestedSyntheticInput"/> class.
    /// </summary>
    /// <param name="shell">The shell that receives the input.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shell"/> is <see langword="null"/>.</exception>
    public NestedSyntheticInput(NestedShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    /// <inheritdoc/>
    public bool PointerMotionAbsolute(uint timeMs, double x, double y) =>
        Send(new BasinViewInput(BasinViewInputKind.PointerMotion, timeMs, x, y, 0, false, 0, 0, 0));

    /// <inheritdoc/>
    public bool PointerButton(uint timeMs, uint button, bool pressed) =>
        Send(new BasinViewInput(BasinViewInputKind.PointerButton, timeMs, 0, 0, button, pressed, 0, 0, 0));

    /// <summary>
    /// Scrolls on one axis. The shell gets one discrete step for each 10 units of <paramref name="value"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The <c>wl_pointer.axis</c> value: 0 is vertical, 1 is horizontal.</param>
    /// <param name="value">The scroll distance in the units of <c>wl_pointer.axis</c>.</param>
    /// <param name="source">The <c>wl_pointer.axis_source</c> value. The shell does not read it.</param>
    /// <returns><see langword="true"/> when the input was delivered. It is <see langword="false"/> for an unknown axis.</returns>
    public bool PointerAxis(uint timeMs, uint axis, double value, uint source) =>
        axis switch
        {
            0 => Send(new BasinViewInput(BasinViewInputKind.PointerAxis, timeMs, 0, 0, 0, false, 0, -value / AxisStep, 0)),
            1 => Send(new BasinViewInput(BasinViewInputKind.PointerAxis, timeMs, 0, 0, 0, false, -value / AxisStep, 0, 0)),
            _ => false,
        };

    /// <inheritdoc/>
    public bool Key(uint timeMs, uint keycode, bool pressed) =>
        Send(new BasinViewInput(BasinViewInputKind.Key, timeMs, 0, 0, keycode, pressed, 0, 0, 0));

    /// <summary>Starts a touch point.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The identifier of the touch point.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    public bool TouchDown(uint timeMs, int id, double x, double y) =>
        Send(new BasinViewInput(BasinViewInputKind.TouchDown, timeMs, x, y, 0, false, 0, 0, id));

    /// <summary>Moves a touch point.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The identifier of the touch point.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    public bool TouchMotion(uint timeMs, int id, double x, double y) =>
        Send(new BasinViewInput(BasinViewInputKind.TouchMotion, timeMs, x, y, 0, false, 0, 0, id));

    /// <summary>Ends a touch point.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The identifier of the touch point.</param>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    public bool TouchUp(uint timeMs, int id) =>
        Send(new BasinViewInput(BasinViewInputKind.TouchUp, timeMs, 0, 0, 0, false, 0, 0, id));

    /// <summary>Ends a group of touch events. The shell needs no frame, so the call sends nothing.</summary>
    /// <returns><see langword="true"/> while the shell is not disposed.</returns>
    public bool TouchFrame() => !_shell.IsDisposed;

    /// <summary>Cancels all active touch points.</summary>
    /// <returns><see langword="true"/> when the input was delivered.</returns>
    public bool TouchCancel() =>
        Send(new BasinViewInput(BasinViewInputKind.TouchCancel, 0, 0, 0, 0, false, 0, 0, 0));

    /// <summary>Reads the pointer position of the shell.</summary>
    /// <param name="x">The horizontal position in logical layout coordinates.</param>
    /// <param name="y">The vertical position in logical layout coordinates.</param>
    /// <returns><see langword="true"/> while the shell is not disposed.</returns>
    public bool TryPointerPosition(out double x, out double y)
    {
        (x, y) = _shell.PointerPosition;
        return !_shell.IsDisposed;
    }

    private bool Send(in BasinViewInput input)
    {
        if (_shell.IsDisposed)
        {
            return false;
        }

        _shell.HandleInput(input);
        return true;
    }
}
