namespace Basin.Seat.Backends;

/// <summary>
/// Injects synthetic pointer, key and touch input into a seat. Absolute and relative motion goes through the binder's constraint gate, and a warp does not.
/// </summary>
public sealed class SeatInjector : Basin.Capabilities.ISyntheticInput
{
    private readonly SeatBinder _binder;
    private readonly Seat _seat;
    private readonly OutputLayout _layout;
    private readonly LayoutPointer _pointer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeatInjector"/> class.
    /// </summary>
    /// <param name="binder">The binder whose gate, capabilities and touch events the injector uses.</param>
    /// <param name="seat">The seat that gets the keyboard capability on a key.</param>
    /// <param name="layout">The output layout that bounds absolute motion.</param>
    /// <param name="pointer">The layout pointer that the injector moves.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets or sets the callback that runs after the injector moves the pointer. The argument is the event time in milliseconds.
    /// </summary>
    public Action<uint>? Moved { get; set; }

    /// <summary>
    /// Gets or sets the callback that runs after a gated motion, in place of <see cref="Moved"/>. The arguments are the time and the distance moved.
    /// </summary>
    public Action<uint, double, double>? MovedBy { get; set; }

    /// <summary>
    /// Gets or sets the callback that delivers an injected button. The arguments are the time, the button and the press state.
    /// </summary>
    public Action<uint, uint, bool>? DeliverButton { get; set; }

    /// <summary>
    /// Gets or sets the callback that delivers an injected key. The arguments are the time, the evdev key code and the press state.
    /// </summary>
    public Action<uint, uint, bool>? DeliverKey { get; set; }

    /// <summary>
    /// Gets or sets the callback that delivers an injected scroll. The arguments are the time, the axis, the value and the axis source.
    /// </summary>
    public Action<uint, uint, double, uint>? DeliverAxis { get; set; }

    /// <summary>
    /// Moves the pointer to a layout position without the constraint gate, then calls <see cref="Moved"/>.
    /// </summary>
    /// <param name="x">The layout x.</param>
    /// <param name="y">The layout y.</param>
    public void Warp(double x, double y)
    {
        _binder.EnsurePointerCapability();
        _pointer.Warp(x, y);
        Moved?.Invoke((uint)Environment.TickCount);
    }

    /// <summary>
    /// Sets the pointer capability and gives a button to <see cref="DeliverButton"/> with the current tick count.
    /// </summary>
    /// <param name="button">The evdev button code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    public void Button(uint button, bool pressed)
    {
        _binder.EnsurePointerCapability();
        DeliverButton?.Invoke((uint)Environment.TickCount, button, pressed);
    }

    /// <summary>
    /// Sets the keyboard capability and gives a key to <see cref="DeliverKey"/> with the current tick count.
    /// </summary>
    /// <param name="key">The evdev key code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    public void Key(uint key, bool pressed)
    {
        _seat.SetCapability(SeatCapability.Keyboard, true);
        var timeMs = (uint)Environment.TickCount;
        if (!_seat.Keyboard.IsKeyBlocked(timeMs, key, pressed))
        {
            DeliverKey?.Invoke(timeMs, key, pressed);
        }
    }

    /// <summary>
    /// Warps the pointer to the center of the layout bounds. It does nothing when the layout is empty.
    /// </summary>
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

    /// <summary>
    /// Maps a position in an extent onto the layout bounds and moves the pointer there through the constraint gate.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The x position in the extent.</param>
    /// <param name="y">The y position in the extent.</param>
    /// <param name="extentWidth">The extent width.</param>
    /// <param name="extentHeight">The extent height.</param>
    /// <returns>
    /// <see langword="false"/> when the layout or the extent is empty, or when nothing observes the move, as <see cref="PointerMotionAbsolute"/> tells.
    /// It is <see langword="true"/> when a lock holds the pointer, because the locked client receives the relative motion.
    /// </returns>
    public bool MotionAbsolute(uint timeMs, double x, double y, double extentWidth, double extentHeight)
    {
        var bounds = _layout.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 || !(extentWidth > 0) || !(extentHeight > 0))
        {
            return false;
        }

        return PointerMotionAbsolute(timeMs, bounds.X + (x / extentWidth * bounds.Width), bounds.Y + (y / extentHeight * bounds.Height));
    }

    /// <summary>
    /// Moves the pointer to a layout position through the constraint gate, then calls <see cref="MovedBy"/> or <see cref="Moved"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The layout x.</param>
    /// <param name="y">The layout y.</param>
    /// <returns>
    /// <see langword="false"/> when nothing observes the move: no callback is set, the injector does not deliver through the binder, and no handler watches the layout pointer.
    /// It is <see langword="true"/> when a lock holds the pointer, because the locked client receives the relative motion.
    /// </returns>
    public bool PointerMotionAbsolute(uint timeMs, double x, double y)
    {
        _binder.EnsurePointerCapability();
        var previousX = _pointer.X;
        var previousY = _pointer.Y;
        if (!_binder.Gate(timeMs, previousX, previousY, ref x, ref y, null, null))
        {
            return true;
        }

        var observed = _pointer.HasMovedHandlers;
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
        else
        {
            return observed;
        }

        return true;
    }

    /// <summary>
    /// Moves the pointer by a relative distance through the constraint gate.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The relative x motion.</param>
    /// <param name="dy">The relative y motion.</param>
    /// <returns>The result of <see cref="PointerMotionAbsolute"/> for the target position.</returns>
    public bool PointerMotion(uint timeMs, double dx, double dy) =>
        PointerMotionAbsolute(timeMs, _pointer.X + dx, _pointer.Y + dy);

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public bool Key(uint timeMs, uint keycode, bool pressed)
    {
        if (DeliverKey is { } deliver)
        {
            _seat.SetCapability(SeatCapability.Keyboard, true);
            if (!_seat.Keyboard.IsKeyBlocked(timeMs, keycode, pressed))
            {
                deliver(timeMs, keycode, pressed);
            }

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

    /// <inheritdoc/>
    public bool TouchDown(uint timeMs, int id, double x, double y) => _binder.InjectTouchDown(timeMs, id, x, y);

    /// <inheritdoc/>
    public bool TouchMotion(uint timeMs, int id, double x, double y) => _binder.InjectTouchMotion(timeMs, id, x, y);

    /// <inheritdoc/>
    public bool TouchUp(uint timeMs, int id) => _binder.InjectTouchUp(timeMs, id);

    /// <inheritdoc/>
    public bool TouchFrame() => _binder.InjectTouchFrame();

    /// <inheritdoc/>
    public bool TouchCancel() => _binder.InjectTouchCancel();

    /// <summary>
    /// Reads the layout pointer position.
    /// </summary>
    /// <param name="x">The layout x.</param>
    /// <param name="y">The layout y.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool TryPointerPosition(out double x, out double y)
    {
        x = _pointer.X;
        y = _pointer.Y;
        return true;
    }
}
