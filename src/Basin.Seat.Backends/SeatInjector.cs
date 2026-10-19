namespace Basin.Seat.Backends;

public sealed class SeatInjector : Basin.Capabilities.ISyntheticInput
{
    private readonly SeatBinder _binder;
    private readonly Seat _seat;
    private readonly OutputLayout _layout;
    private readonly LayoutPointer _pointer;

    public SeatInjector(SeatBinder binder, Seat seat, OutputLayout layout, LayoutPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(pointer);
        _binder = binder;
        _seat = seat;
        _layout = layout;
        _pointer = pointer;
    }

    internal bool ThroughBinder { get; init; }

    public Action<uint>? Moved { get; set; }

    public Action<uint, double, double>? MovedBy { get; set; }

    public Action<uint, uint, bool>? DeliverButton { get; set; }

    public Action<uint, uint, bool>? DeliverKey { get; set; }

    public Action<uint, uint, double, uint>? DeliverAxis { get; set; }

    public void Warp(double x, double y)
    {
        _binder.EnsurePointerCapability();
        _pointer.Warp(x, y);
        Moved?.Invoke((uint)Environment.TickCount);
    }

    public void Button(uint button, bool pressed)
    {
        _binder.EnsurePointerCapability();
        DeliverButton?.Invoke((uint)Environment.TickCount, button, pressed);
    }

    public void Key(uint key, bool pressed)
    {
        _seat.SetCapability(SeatCapability.Keyboard, true);
        DeliverKey?.Invoke((uint)Environment.TickCount, key, pressed);
    }

    public void Center()
    {
        var bounds = _layout.Bounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        _pointer.Warp(bounds.X + (bounds.Width / 2.0), bounds.Y + (bounds.Height / 2.0));
        Moved?.Invoke((uint)Environment.TickCount);
    }

    public bool MotionAbsolute(uint timeMs, double x, double y, double extentWidth, double extentHeight)
    {
        _binder.EnsurePointerCapability();
        var bounds = _layout.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 || !(extentWidth > 0) || !(extentHeight > 0))
        {
            return true;
        }

        var previousX = _pointer.X;
        var previousY = _pointer.Y;
        var targetX = bounds.X + (x / extentWidth * bounds.Width);
        var targetY = bounds.Y + (y / extentHeight * bounds.Height);
        if (!_binder.Gate(timeMs, previousX, previousY, ref targetX, ref targetY, null, null))
        {
            return true;
        }

        _pointer.Warp(targetX, targetY);
        if (MovedBy is { } movedBy)
        {
            movedBy(timeMs, _pointer.X - previousX, _pointer.Y - previousY);
        }
        else
        {
            Moved?.Invoke(timeMs);
        }

        return true;
    }

    public bool PointerMotionAbsolute(uint timeMs, double x, double y)
    {
        _binder.EnsurePointerCapability();
        var previousX = _pointer.X;
        var previousY = _pointer.Y;
        if (!_binder.Gate(timeMs, previousX, previousY, ref x, ref y, null, null))
        {
            return true;
        }

        _pointer.Warp(x, y);
        if (MovedBy is { } movedBy)
        {
            movedBy(timeMs, _pointer.X - previousX, _pointer.Y - previousY);
        }
        else if (Moved is { } moved)
        {
            moved(timeMs);
        }
        else if (ThroughBinder)
        {
            _binder.RaiseMotion(timeMs, _pointer.X - previousX, _pointer.Y - previousY);
        }

        return true;
    }

    public bool PointerMotion(uint timeMs, double dx, double dy) =>
        PointerMotionAbsolute(timeMs, _pointer.X + dx, _pointer.Y + dy);

    public bool PointerButton(uint timeMs, uint button, bool pressed)
    {
        if (DeliverButton is { } deliver)
        {
            _binder.EnsurePointerCapability();
            deliver(timeMs, button, pressed);
            return true;
        }

        if (!ThroughBinder)
        {
            return false;
        }

        _binder.EnsurePointerCapability();
        _binder.RaiseButton(timeMs, button, pressed);
        return true;
    }

    public bool PointerAxis(uint timeMs, uint axis, double value, uint source)
    {
        if (DeliverAxis is { } deliver)
        {
            _binder.EnsurePointerCapability();
            deliver(timeMs, axis, value, source);
            return true;
        }

        if (!ThroughBinder)
        {
            return false;
        }

        _binder.EnsurePointerCapability();
        _binder.DeliverAxis(
            timeMs, new PointerAxis((Wayland.WlPointer.Axis)axis, value, Source: (Wayland.WlPointer.AxisSource)source), 1.0);
        return true;
    }

    public bool Key(uint timeMs, uint keycode, bool pressed)
    {
        if (DeliverKey is { } deliver)
        {
            _seat.SetCapability(SeatCapability.Keyboard, true);
            deliver(timeMs, keycode, pressed);
            return true;
        }

        if (!ThroughBinder)
        {
            return false;
        }

        _seat.SetCapability(SeatCapability.Keyboard, true);
        _binder.RaiseKey(timeMs, keycode, pressed);
        return true;
    }

    public bool TouchDown(uint timeMs, int id, double x, double y) => _binder.InjectTouchDown(timeMs, id, x, y);

    public bool TouchMotion(uint timeMs, int id, double x, double y) => _binder.InjectTouchMotion(timeMs, id, x, y);

    public bool TouchUp(uint timeMs, int id) => _binder.InjectTouchUp(timeMs, id);

    public bool TouchFrame() => _binder.InjectTouchFrame();

    public bool TouchCancel() => _binder.InjectTouchCancel();

    public bool TryPointerPosition(out double x, out double y)
    {
        x = _pointer.X;
        y = _pointer.Y;
        return true;
    }
}
