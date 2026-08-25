using Basin.Seat;

namespace Basin.Shell.Xdg;

public sealed class GrabOrigin
{
    private readonly Basin.Seat.Seat _seat;
    private readonly Func<(double X, double Y)> _pointer;

    public GrabOrigin(Basin.Seat.Seat seat, Func<(double X, double Y)> pointer)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(pointer);
        _seat = seat;
        _pointer = pointer;
    }

    public TouchMoveResize? Touch { get; set; }

    public int? FrameTouchSlot { get; set; }

    public (double X, double Y) For(uint? serial)
    {
        if (Touch is { } touch)
        {
            if (FrameTouchSlot is { } slot && touch.TryBeginContact(slot, out var frameX, out var frameY))
            {
                return (frameX, frameY);
            }

            if (touch.TryBegin(serial, out var pointX, out var pointY))
            {
                return (pointX, pointY);
            }
        }

        _seat.Pointer.NotifyClearFocus();
        return _pointer();
    }
}
