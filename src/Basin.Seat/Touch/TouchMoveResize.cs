using Basin.Diagnostics;

namespace Basin.Seat;

public sealed class TouchMoveResize : ITouchCapture
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly TouchRouter _router;
    private readonly SeatTouch _touch;
    private int _slot = -1;

    public TouchMoveResize(TouchRouter router, SeatTouch touch)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(touch);

        _router = router;
        _touch = touch;
    }

    public ITouchDragHandler? Handler { get; set; }

    public bool Dragging => _slot >= 0;

    public bool Owns(int id) => _slot >= 0 && _slot == id;

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

    public void End()
    {
        _thread.Assert();
        _slot = -1;
    }

    void ITouchCapture.Motion(int id, uint timeMs, double x, double y)
    {
        if (_slot == id)
        {
            Handler?.DragTo(x, y);
        }
    }

    void ITouchCapture.Up(int id, uint timeMs)
    {
        if (_slot == id)
        {
            _slot = -1;
            Handler?.DragEnd(cancelled: false);
        }
    }

    void ITouchCapture.Cancel()
    {
        if (_slot >= 0)
        {
            _slot = -1;
            Handler?.DragEnd(cancelled: true);
        }
    }
}
