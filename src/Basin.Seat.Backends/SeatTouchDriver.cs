namespace Basin.Seat.Backends;

public sealed class SeatTouchDriver : ITouchInteractionObserver
{
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

    public SeatTouch Touch { get; }

    public TouchRouter Router { get; }

    public TouchMoveResize MoveResize { get; }

    public event Action<int, TouchTargetKind, Surface?>? Routed;

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
