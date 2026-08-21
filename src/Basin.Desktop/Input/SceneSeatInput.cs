using Basin.Capabilities;
using Basin.Seat;

namespace Basin.Desktop;

public class SceneSeatInput : SeatInputSink
{
    private readonly Basin.Scene.Scene _scene;

    public SceneSeatInput(Basin.Seat.Seat seat, Basin.Scene.Scene scene, OutputLayout layout)
        : base(seat)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(layout);
        _scene = scene;
        Pointer = new LayoutPointer(layout);
        Pointer.Reposition();
    }

    public LayoutPointer Pointer { get; }

    public IOutput? Output { get; set; }

    public bool FocusOnButton { get; set; } = true;

    public override bool PointerMotion(uint timeMs, double dx, double dy)
    {
        Pointer.Motion(dx, dy);
        Route(timeMs);
        return true;
    }

    public override bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        Pointer.MotionAbsolute(Output, x / width, y / height);
        Route(timeMs);
        return true;
    }

    public override bool PointerButton(uint timeMs, uint button, bool pressed)
    {
        if (pressed && FocusOnButton && !Seat.Pointer.HasImplicitGrab)
        {
            Seat.Keyboard.NotifyEnter(_scene.SurfaceAt(Pointer.X, Pointer.Y)?.Surface);
        }

        return base.PointerButton(timeMs, button, pressed);
    }

    protected void Route(uint timeMs)
    {
        var hit = _scene.SurfaceAt(Pointer.X, Pointer.Y);
        Seat.Pointer.NotifyMotionAt(timeMs, hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0, Pointer.X, Pointer.Y);
    }
}
