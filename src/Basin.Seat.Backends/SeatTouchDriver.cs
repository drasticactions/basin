namespace Basin.Seat.Backends;

/// <summary>
/// Builds the standard touch assembly over a <see cref="SeatBinder"/>: a <see cref="TouchRouter"/>, a <see cref="TouchMoveResize"/>, and the binder's five touch events wired to the router.
/// </summary>
public sealed class SeatTouchDriver : ITouchInteractionObserver
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SeatTouchDriver"/> class. The binder's touch events stay wired for the life of the binder.
    /// </summary>
    /// <param name="binder">The binder whose touch events feed the router.</param>
    /// <param name="seat">The seat whose touch the router delivers to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="binder"/> or <paramref name="seat"/> is <see langword="null"/>.</exception>
    public SeatTouchDriver(SeatBinder binder, Seat seat)
    {
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(seat);

        Touch = seat.Touch;
        Router = new TouchRouter(seat.Touch) { Interaction = this };
        MoveResize = new TouchMoveResize(Router, seat.Touch);
        binder.TouchDown += Router.Down;
        binder.TouchMotion += Router.Motion;
        binder.TouchUp += Router.Up;
        binder.TouchFrame += Router.Frame;
        binder.TouchCancelled += Router.Cancel;
    }

    /// <summary>
    /// Gets the seat touch that receives the routed contacts.
    /// </summary>
    public SeatTouch Touch { get; }

    /// <summary>
    /// Gets the router that decides where each contact goes. The consumer sets its hit tester, chrome and gestures.
    /// </summary>
    public TouchRouter Router { get; }

    /// <summary>
    /// Gets the touch move and resize helper over <see cref="Router"/>.
    /// </summary>
    public TouchMoveResize MoveResize { get; }

    /// <summary>
    /// Occurs after the router routes a touch down. The arguments are the contact id, the target kind, and the surface or <see langword="null"/>.
    /// </summary>
    public event Action<int, TouchTargetKind, Surface?>? Routed;

    /// <summary>
    /// Creates a touch pointer emulation driver and makes it the router's pointer target. It replaces any earlier driver.
    /// </summary>
    /// <param name="target">The target that receives the emulated pointer events.</param>
    /// <returns>The new driver.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public TouchPointerDriver AttachPointer(ITouchPointerTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var driver = new TouchPointerDriver(Touch, target);
        Router.Pointer = driver;
        return driver;
    }

    void ITouchInteractionObserver.OnTouchInteraction(int id, TouchTargetKind kind, Surface? surface) =>
        Routed?.Invoke(id, kind, surface);
}
