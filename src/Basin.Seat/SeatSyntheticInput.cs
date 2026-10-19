using Basin.Capabilities;

namespace Basin.Seat;

public sealed class SeatSyntheticInput : ISyntheticInput
{
    private readonly Seat _seat;
    private readonly BasinServices _services;

    public SeatSyntheticInput(Seat seat, BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(services);
        _seat = seat;
        _services = services;
    }

    public bool AllowWhileLocked { get; set; }

    public bool IsAvailable => Target is not null;

    private ISyntheticInput? Target =>
        _seat.SyntheticInput is { } target && (AllowWhileLocked || _services.Find<ILockState>() is not { IsLocked: true }) ? target : null;

    public bool PointerMotionAbsolute(uint timeMs, double x, double y) => Target?.PointerMotionAbsolute(timeMs, x, y) ?? false;

    public bool PointerButton(uint timeMs, uint button, bool pressed) => Target?.PointerButton(timeMs, button, pressed) ?? false;

    public bool PointerAxis(uint timeMs, uint axis, double value, uint source) => Target?.PointerAxis(timeMs, axis, value, source) ?? false;

    public bool Key(uint timeMs, uint keycode, bool pressed) => Target?.Key(timeMs, keycode, pressed) ?? false;

    public bool TouchDown(uint timeMs, int id, double x, double y) => Target?.TouchDown(timeMs, id, x, y) ?? false;

    public bool TouchMotion(uint timeMs, int id, double x, double y) => Target?.TouchMotion(timeMs, id, x, y) ?? false;

    public bool TouchUp(uint timeMs, int id) => Target?.TouchUp(timeMs, id) ?? false;

    public bool TouchFrame() => Target?.TouchFrame() ?? false;

    public bool TouchCancel() => Target?.TouchCancel() ?? false;

    public bool TryPointerPosition(out double x, out double y)
    {
        if (Target is { } target)
        {
            return target.TryPointerPosition(out x, out y);
        }

        (x, y) = (_seat.Pointer.X, _seat.Pointer.Y);
        return false;
    }
}
