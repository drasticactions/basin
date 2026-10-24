using Basin.Seat;

namespace Basin.Shell.Xdg;

/// <summary>
/// Finds the start point of a move or a resize that a client asks for, from a touch point or from the pointer.
/// </summary>
public sealed class GrabOrigin
{
    private readonly Basin.Seat.Seat _seat;
    private readonly Func<(double X, double Y)> _pointer;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrabOrigin"/> class.
    /// </summary>
    /// <param name="seat">The seat whose pointer focus is cleared for a pointer grab.</param>
    /// <param name="pointer">The function that returns the pointer position in layout coordinates.</param>
    public GrabOrigin(Basin.Seat.Seat seat, Func<(double X, double Y)> pointer)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(pointer);
        _seat = seat;
        _pointer = pointer;
    }

    /// <summary>
    /// Gets or sets the touch move-resize capture. With <see langword="null"/>, every grab starts from the pointer.
    /// </summary>
    public TouchMoveResize? Touch { get; set; }

    /// <summary>
    /// Gets or sets the touch point that holds a frame that the compositor draws, or <see langword="null"/> when no touch point does. A grab starts from it first.
    /// </summary>
    public int? FrameTouchSlot { get; set; }

    /// <summary>
    /// Finds the start point of a grab. It tries <see cref="FrameTouchSlot"/>, then the touch point of <paramref name="serial"/>, and captures the touch point that it uses. Otherwise it clears the pointer focus and uses the pointer position.
    /// </summary>
    /// <param name="serial">The serial of the client request, or <see langword="null"/> when there is none.</param>
    /// <returns>The start point in layout coordinates.</returns>
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
