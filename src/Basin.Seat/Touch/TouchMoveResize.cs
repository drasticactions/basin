using Basin.Diagnostics;

namespace Basin.Seat;

/// <summary>
/// Captures one touch contact to drive an interactive window move or resize. It must be used on the thread that created it.
/// </summary>
public sealed class TouchMoveResize : ITouchCapture
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly TouchRouter _router;
    private readonly SeatTouch _touch;
    private int _slot = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="TouchMoveResize"/> class.
    /// </summary>
    /// <param name="router">The router that the contact is captured from.</param>
    /// <param name="touch">The touch device that resolves a client's down serial to a contact.</param>
    public TouchMoveResize(TouchRouter router, SeatTouch touch)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(touch);

        _router = router;
        _touch = touch;
    }

    /// <summary>
    /// Gets or sets the handler that receives the drag.
    /// </summary>
    public ITouchDragHandler? Handler { get; set; }

    /// <summary>
    /// Gets a value indicating whether a contact is captured.
    /// </summary>
    public bool Dragging => _slot >= 0;

    /// <summary>
    /// Checks whether a contact is the captured one.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <returns><see langword="true"/> when the contact drives the drag.</returns>
    public bool Owns(int id) => _slot >= 0 && _slot == id;

    /// <summary>
    /// Starts a drag from the touch down that a client names by serial, for example in <c>xdg_toplevel.move</c>.
    /// </summary>
    /// <param name="serial">The touch down serial, or <see langword="null"/>.</param>
    /// <param name="x">The horizontal layout position of the contact.</param>
    /// <param name="y">The vertical layout position of the contact.</param>
    /// <returns><see langword="false"/> when a drag runs, the serial is not a live touch down, or the capture fails.</returns>
    public bool TryBegin(uint? serial, out double x, out double y)
    {
        _thread.Assert();
        if (_slot < 0 && serial is { } value && _touch.TryGetPointBySerial(value, out var id))
        {
            return TryBeginContact(id, out x, out y);
        }

        x = 0;
        y = 0;
        return false;
    }

    /// <summary>
    /// Starts a drag from a contact id that the compositor picked.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal layout position of the contact.</param>
    /// <param name="y">The vertical layout position of the contact.</param>
    /// <returns><see langword="false"/> when a drag runs or the contact is not down.</returns>
    public bool TryBeginContact(int id, out double x, out double y)
    {
        _thread.Assert();
        if (_slot < 0 && _router.TryGetPosition(id, out x, out y) && _router.Capture(id, this))
        {
            _slot = id;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    /// <summary>
    /// Forgets the captured contact without a call to <see cref="ITouchDragHandler.DragEnd"/>. Later events of the contact are dropped.
    /// </summary>
    public void End()
    {
        _thread.Assert();
        _slot = -1;
    }

    void ITouchCapture.Motion(int id, uint timeMs, double x, double y)
    {
        if (_slot == id)
        {
            Handler?.DragTo(timeMs, x, y);
        }
    }

    void ITouchCapture.Up(int id, uint timeMs)
    {
        if (_slot == id)
        {
            _slot = -1;
            Handler?.DragEnd(timeMs, cancelled: false);
        }
    }

    void ITouchCapture.Cancel()
    {
        if (_slot >= 0)
        {
            _slot = -1;
            Handler?.DragEnd(0, cancelled: true);
        }
    }
}
