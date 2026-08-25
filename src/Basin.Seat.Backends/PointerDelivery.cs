using Basin.Desktop;

namespace Basin.Seat.Backends;

public sealed class PointerDelivery
{
    private readonly Basin.Seat.Seat _seat;
    private readonly CursorController _cursor;

    public PointerDelivery(Basin.Seat.Seat seat, CursorController cursor)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(cursor);
        _seat = seat;
        _cursor = cursor;
    }

    public RelativePointerManager? RelativePointer { get; set; }

    public string DefaultCursorName { get; set; } = "left_ptr";

    public bool Motion(uint timeMs, Surface? surface, double localX, double localY, double x, double y)
    {
        if (surface is null)
        {
            ClearFocus();
            return false;
        }

        _seat.Pointer.NotifyMotionAt(timeMs, surface, localX, localY, x, y);
        _cursor.SetHover(surface, overClient: true);
        return true;
    }

    public void ClearFocus(bool showDefaultCursor = true)
    {
        _seat.Pointer.NotifyClearFocus();
        _cursor.SetHover(null, overClient: false);
        if (showDefaultCursor)
        {
            _cursor.ShowNamed(DefaultCursorName);
        }
    }

    public void Relative(uint timeMs, double dx, double dy, double? unacceleratedDx, double? unacceleratedDy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        RelativePointer?.NotifyMotion(
            (ulong)timeMs * 1000, dx, dy, unacceleratedDx ?? dx, unacceleratedDy ?? dy);
    }
}
