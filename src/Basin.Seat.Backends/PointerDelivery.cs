using Basin.Desktop;

namespace Basin.Seat.Backends;

/// <summary>
/// Gives pointer motion to the seat and sets the cursor's hover state, with one call for a hit and one for empty space.
/// </summary>
public sealed class PointerDelivery
{
    private readonly Basin.Seat.Seat _seat;
    private readonly CursorController _cursor;

    /// <summary>
    /// Initializes a new instance of the <see cref="PointerDelivery"/> class.
    /// </summary>
    /// <param name="seat">The seat whose pointer receives the motion.</param>
    /// <param name="cursor">The cursor controller that follows the hovered surface.</param>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> or <paramref name="cursor"/> is <see langword="null"/>.</exception>
    public PointerDelivery(Basin.Seat.Seat seat, CursorController cursor)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(cursor);
        _seat = seat;
        _cursor = cursor;
    }

    /// <summary>
    /// Gets or sets the cursor name that shows when no surface has the pointer. The default is "left_ptr".
    /// </summary>
    public string DefaultCursorName { get; set; } = "left_ptr";

    /// <summary>
    /// Sends a motion to the seat on the surface under the pointer. With no surface, it clears the focus and shows the default cursor.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="surface">The surface under the pointer, or <see langword="null"/>.</param>
    /// <param name="localX">The surface-local x.</param>
    /// <param name="localY">The surface-local y.</param>
    /// <param name="x">The layout x.</param>
    /// <param name="y">The layout y.</param>
    /// <returns><see langword="true"/> when a surface took the motion.</returns>
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

    /// <summary>
    /// Clears the seat's pointer focus and the cursor's hover surface.
    /// </summary>
    /// <param name="showDefaultCursor"><see langword="true"/> to show <see cref="DefaultCursorName"/>.</param>
    public void ClearFocus(bool showDefaultCursor = true)
    {
        _seat.Pointer.NotifyClearFocus();
        _cursor.SetHover(null, overClient: false);
        if (showDefaultCursor)
        {
            _cursor.ShowNamed(DefaultCursorName);
        }
    }
}
