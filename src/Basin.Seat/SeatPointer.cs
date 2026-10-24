using Basin.Diagnostics;
using Wayland;

namespace Basin.Seat;

/// <summary>
/// The seat's pointer. It delivers enter, leave, motion, button and axis events to the focused client and runs pointer grabs. Coordinates are surface-local logical pixels.
/// </summary>
public sealed class SeatPointer
{
    /// <summary>
    /// The surface role name that a cursor surface gets from <c>wl_pointer.set_cursor</c>.
    /// </summary>
    public const string CursorRole = "cursor";

    private readonly Seat _seat;
    private readonly List<IPointerGrab> _grabs = [];
    private readonly DefaultGrab _defaultGrab;
    private uint _lastEnterSerial;
    private Surface? _motionFocus;
    private double _motionX;
    private double _motionY;
    private int _pressedButtons;
    private double _focusLayoutOffsetX;
    private double _focusLayoutOffsetY;

    internal SeatPointer(Seat seat)
    {
        _seat = seat;
        _defaultGrab = new DefaultGrab(this);
    }

    /// <summary>
    /// Gets the surface that has pointer focus, or <see langword="null"/>.
    /// </summary>
    public Surface? Focus { get; private set; }

    /// <summary>
    /// Gets or sets the map from layout to surface coordinates that the implicit grab pin uses. When it is null or fails, the pin uses the last translation offset.
    /// </summary>
    public SurfaceMapper? MapToSurface { get; set; }

    /// <summary>
    /// Gets the last horizontal position sent to the focused surface, in surface-local logical pixels.
    /// </summary>
    public double X { get; private set; }

    /// <summary>
    /// Gets the last vertical position sent to the focused surface, in surface-local logical pixels.
    /// </summary>
    public double Y { get; private set; }

    /// <summary>
    /// Gets the horizontal pointer position in layout coordinates. Only <see cref="NotifyMotionAt"/> updates the offset that it adds to <see cref="X"/>.
    /// </summary>
    public double LayoutX => X + _focusLayoutOffsetX;

    /// <summary>
    /// Gets the vertical pointer position in layout coordinates. Only <see cref="NotifyMotionAt"/> updates the offset that it adds to <see cref="Y"/>.
    /// </summary>
    public double LayoutY => Y + _focusLayoutOffsetY;

    /// <summary>
    /// Gets the grab that receives the notify calls: the newest started grab, or the default grab that delivers to the focus.
    /// </summary>
    public IPointerGrab Grab => _grabs.Count > 0 ? _grabs[^1] : _defaultGrab;

    /// <summary>
    /// Gets a value indicating whether a grab other than the default one is active.
    /// </summary>
    public bool HasGrab => _grabs.Count > 0;

    /// <summary>
    /// Gets a value indicating whether at least one button is held, so that focus is pinned to the pressed surface.
    /// </summary>
    public bool HasImplicitGrab => _pressedButtons > 0;

    /// <summary>
    /// Gets the serial of the press that started the implicit grab. It is only meaningful while <see cref="HasImplicitGrab"/> is true.
    /// </summary>
    public uint GrabSerial { get; private set; }

    /// <summary>
    /// Occurs when the focused client sets its cursor with the serial of its latest enter. A request with a null surface hides the cursor.
    /// </summary>
    public event Action<CursorRequest>? CursorRequested;

    /// <summary>
    /// Occurs after the pointer focus changes, with the new focus or <see langword="null"/>.
    /// </summary>
    public event Action<Surface?>? FocusChanged;

    /// <summary>
    /// Occurs on each delivered motion with the time in milliseconds and the surface-local move since the last position.
    /// </summary>
    public event Action<uint, double, double>? Moved;

    /// <summary>
    /// Occurs after each notified button with the button code and <see langword="true"/> for a press.
    /// </summary>
    public event Action<uint, bool>? Buttoned;

    /// <summary>
    /// Occurs on each delivered axis event with the time in milliseconds and the axis, also when no surface has focus.
    /// </summary>
    public event Action<uint, PointerAxis>? Axised;

    /// <summary>
    /// Occurs when a grab starts, with <see langword="true"/>, or ends, with <see langword="false"/>.
    /// </summary>
    public event Action<IPointerGrab, bool>? GrabChanged;

    /// <summary>
    /// Checks that a serial is the serial of the latest pointer enter.
    /// </summary>
    /// <param name="serial">The serial from the client request.</param>
    /// <returns><see langword="true"/> when the serial is the latest enter serial.</returns>
    public bool ValidateEnterSerial(uint serial) => serial != 0 && serial == _lastEnterSerial;

    /// <summary>
    /// Passes a focus change to the active grab. The default grab sends leave and enter, or updates the position when the focus is the same.
    /// </summary>
    /// <param name="surface">The new focus, or <see langword="null"/> to clear it.</param>
    /// <param name="x">The horizontal surface-local position.</param>
    /// <param name="y">The vertical surface-local position.</param>
    public void NotifyEnter(Surface? surface, double x, double y) => Grab.Enter(surface, x, y);

    /// <summary>
    /// Passes a focus clear to the active grab. It also breaks the implicit grab pin of <see cref="NotifyMotionAt"/>.
    /// </summary>
    public void NotifyClearFocus() => Grab.Enter(null, 0, 0);

    private int _scopedNotifies;

    private const int NotifyScopeWarmup = 10;

    /// <summary>
    /// Passes a motion on the current focus to the active grab.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal surface-local position.</param>
    /// <param name="y">The vertical surface-local position.</param>
    public void NotifyMotion(uint timeMs, double x, double y)
    {
        if (HasGrab || _scopedNotifies < NotifyScopeWarmup)
        {
            _scopedNotifies += HasGrab ? 0 : 1;
            Grab.Motion(timeMs, x, y);
            return;
        }

        AllocationScope.Begin(region: "PointerNotify", forgiving: true);
        try
        {
            _defaultGrab.Motion(timeMs, x, y);
        }
        finally
        {
            AllocationScope.End();
        }
    }

    /// <summary>
    /// Moves the pointer on the focused surface without a user motion. It bypasses grabs and raises no <see cref="Moved"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds, used when the client gets a motion instead of a warp.</param>
    /// <param name="x">The horizontal surface-local position.</param>
    /// <param name="y">The vertical surface-local position.</param>
    public void NotifyWarp(uint timeMs, double x, double y) => SendWarp(timeMs, x, y);

    /// <summary>
    /// Delivers a motion from the result of a hit test: enter and motion on a surface, or a focus clear on nothing. While a button is held, motion stays on the pressed surface.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="surface">The surface under the pointer, or <see langword="null"/>.</param>
    /// <param name="surfaceX">The horizontal position local to <paramref name="surface"/>.</param>
    /// <param name="surfaceY">The vertical position local to <paramref name="surface"/>.</param>
    /// <param name="layoutX">The horizontal position in layout coordinates.</param>
    /// <param name="layoutY">The vertical position in layout coordinates.</param>
    public void NotifyMotionAt(uint timeMs, Surface? surface, double surfaceX, double surfaceY, double layoutX, double layoutY)
    {
        if (!HasGrab && HasImplicitGrab && Focus is { IsDestroyed: false } pinned && !ReferenceEquals(surface, pinned))
        {
            if (MapToSurface is { } map && map(pinned, layoutX, layoutY, out var pinnedX, out var pinnedY))
            {
                NotifyMotion(timeMs, pinnedX, pinnedY);
                return;
            }

            NotifyMotion(timeMs, layoutX - _focusLayoutOffsetX, layoutY - _focusLayoutOffsetY);
            return;
        }

        if (surface is null)
        {
            NotifyClearFocus();
            return;
        }

        _focusLayoutOffsetX = layoutX - surfaceX;
        _focusLayoutOffsetY = layoutY - surfaceY;
        NotifyEnter(surface, surfaceX, surfaceY);
        NotifyMotion(timeMs, surfaceX, surfaceY);
    }

    /// <summary>
    /// Passes a button event to the active grab and tracks the implicit grab.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The Linux evdev button code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    /// <returns>The serial that the grab issued, or zero when the grab issued none.</returns>
    public uint NotifyButton(uint timeMs, uint button, bool pressed) =>
        NotifyButton(timeMs, button, pressed ? WlPointer.ButtonState.Pressed : WlPointer.ButtonState.Released);

    /// <summary>
    /// Passes a button event to the active grab and tracks the implicit grab. The first press records <see cref="GrabSerial"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The Linux evdev button code.</param>
    /// <param name="state">The button state.</param>
    /// <returns>The serial that the grab issued, or zero when the grab issued none.</returns>
    public uint NotifyButton(uint timeMs, uint button, WlPointer.ButtonState state)
    {
        uint serial;
        if (HasGrab || _scopedNotifies < NotifyScopeWarmup)
        {
            _scopedNotifies += HasGrab ? 0 : 1;
            serial = Grab.Button(timeMs, button, state);
        }
        else
        {
            AllocationScope.Begin(region: "PointerNotify", forgiving: true);
            try
            {
                serial = _defaultGrab.Button(timeMs, button, state);
            }
            finally
            {
                AllocationScope.End();
            }
        }

        Buttoned?.Invoke(button, state == WlPointer.ButtonState.Pressed);
        if (state == WlPointer.ButtonState.Pressed)
        {
            if (_pressedButtons++ == 0)
            {
                GrabSerial = serial;
            }
        }
        else if (_pressedButtons > 0)
        {
            _pressedButtons--;
        }

        return serial;
    }

    /// <summary>
    /// Passes a scroll event to the active grab. A value and a value120 of zero is a stop.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The scroll event.</param>
    public void NotifyAxis(uint timeMs, in PointerAxis axis)
    {
        if (HasGrab || _scopedNotifies < NotifyScopeWarmup)
        {
            _scopedNotifies += HasGrab ? 0 : 1;
            Grab.Axis(timeMs, axis);
            return;
        }

        AllocationScope.Begin(region: "PointerNotify", forgiving: true);
        try
        {
            _defaultGrab.Axis(timeMs, axis);
        }
        finally
        {
            AllocationScope.End();
        }
    }

    /// <summary>
    /// Forgets the held buttons, so that focus is no longer pinned. Use it when the compositor takes over a press.
    /// </summary>
    public void ClearImplicitGrab() => _pressedButtons = 0;

    /// <summary>
    /// Pushes a grab. It receives every notify call until it ends or a newer grab starts.
    /// </summary>
    /// <param name="grab">The grab.</param>
    public void StartGrab(IPointerGrab grab)
    {
        _grabs.Add(grab);
        GrabChanged?.Invoke(grab, true);
    }

    /// <summary>
    /// Removes a grab. When it was the active grab, its <see cref="IPointerGrab.Cancel"/> runs.
    /// </summary>
    /// <param name="grab">The grab. A grab that is not started is ignored.</param>
    public void EndGrab(IPointerGrab grab)
    {
        var wasActive = HasGrab && Grab == grab;
        var removed = _grabs.Remove(grab);
        if (wasActive)
        {
            grab.Cancel();
        }

        if (removed)
        {
            GrabChanged?.Invoke(grab, false);
        }
    }

    internal void InitializeResource(SeatClient client, Wayland.WlPointerResource pointer)
    {
        if (Focus is { IsDestroyed: false } focus && _seat.ClientOf(focus) == client && _lastEnterSerial != 0)
        {
            var scale = focus.ServerScale;
            pointer.SendEnter(_lastEnterSerial, focus.Resource, WlFixed.FromDouble(X * scale), WlFixed.FromDouble(Y * scale));
            SendFrame(pointer);
        }
    }

    /// <summary>
    /// Sends leave to the old focus and enter to the new one, then raises <see cref="FocusChanged"/>. A grab calls it to deliver. It bypasses grabs.
    /// </summary>
    /// <param name="surface">The new focus, or <see langword="null"/>.</param>
    /// <param name="x">The horizontal surface-local position.</param>
    /// <param name="y">The vertical surface-local position.</param>
    public void SendEnter(Surface? surface, double x, double y)
    {
        if (Focus == surface)
        {
            X = x;
            Y = y;
            return;
        }

        if (Focus is { } old && !old.IsDestroyed && _seat.ClientOf(old) is { } oldClient)
        {
            var leaveSerial = _seat.NextSerial(SerialKind.Other);
            foreach (var pointer in oldClient.Pointers)
            {
                pointer.SendLeave(leaveSerial, old.Resource);
                SendFrame(pointer);
            }
        }

        Focus = surface;
        X = x;
        Y = y;
        _motionFocus = null;
        if (surface is not null && _seat.ClientOf(surface) is { } client)
        {
            var serial = _seat.NextSerial(SerialKind.PointerEnter);
            _lastEnterSerial = serial;
            var scale = surface.ServerScale;
            foreach (var pointer in client.Pointers)
            {
                pointer.SendEnter(serial, surface.Resource, WlFixed.FromDouble(x * scale), WlFixed.FromDouble(y * scale));
                SendFrame(pointer);
            }
        }

        FocusChanged?.Invoke(surface);
    }

    /// <summary>
    /// Sends <c>wl_pointer.frame</c> to the focused client at version 5 and later. Each send method already ends its own frame.
    /// </summary>
    public void NotifyFrame()
    {
        if (_seat.ClientOf(Focus) is { } client)
        {
            foreach (var pointer in client.Pointers)
            {
                SendFrame(pointer);
            }
        }
    }

    /// <summary>
    /// Sends a motion to the focused client and raises <see cref="Moved"/>. A repeat of the last position on the same focus is not sent. It bypasses grabs.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal surface-local position.</param>
    /// <param name="y">The vertical surface-local position.</param>
    public void SendMotion(uint timeMs, double x, double y)
    {
        var dx = x - X;
        var dy = y - Y;
        X = x;
        Y = y;
        AllocationScope.Pause();
        try
        {
            Moved?.Invoke(timeMs, dx, dy);
        }
        finally
        {
            AllocationScope.Resume();
        }

        if (ReferenceEquals(Focus, _motionFocus) && x == _motionX && y == _motionY)
        {
            return;
        }

        _motionFocus = Focus;
        _motionX = x;
        _motionY = y;
        if (_seat.ClientOf(Focus) is { } client)
        {
            var scale = Focus!.ServerScale;
            foreach (var pointer in client.Pointers)
            {
                pointer.SendMotion(timeMs, WlFixed.FromDouble(x * scale), WlFixed.FromDouble(y * scale));
                SendFrame(pointer);
            }
        }
    }

    /// <summary>
    /// Sends <c>wl_pointer.warp</c> to the focused client, or a motion to a client too old for it. It bypasses grabs.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds for the motion fallback.</param>
    /// <param name="x">The horizontal surface-local position.</param>
    /// <param name="y">The vertical surface-local position.</param>
    public void SendWarp(uint timeMs, double x, double y)
    {
        X = x;
        Y = y;
        _motionFocus = Focus;
        _motionX = x;
        _motionY = y;
        if (_seat.ClientOf(Focus) is { } client)
        {
            var scale = Focus!.ServerScale;
            foreach (var pointer in client.Pointers)
            {
                if (pointer.SupportsSendWarp)
                {
                    pointer.SendWarp(WlFixed.FromDouble(x * scale), WlFixed.FromDouble(y * scale));
                }
                else
                {
                    pointer.SendMotion(timeMs, WlFixed.FromDouble(x * scale), WlFixed.FromDouble(y * scale));
                }

                SendFrame(pointer);
            }
        }
    }

    /// <summary>
    /// Sends a button event to the focused client with a new serial. It bypasses grabs and does not track the implicit grab.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The Linux evdev button code.</param>
    /// <param name="state">The button state.</param>
    /// <returns>The serial, issued also when no client has focus.</returns>
    public uint SendButton(uint timeMs, uint button, WlPointer.ButtonState state)
    {
        var serial = _seat.NextSerial(state == WlPointer.ButtonState.Pressed
            ? SerialKind.PointerButtonPress
            : SerialKind.PointerButtonRelease);
        if (_seat.ClientOf(Focus) is { } client)
        {
            foreach (var pointer in client.Pointers)
            {
                pointer.SendButton(serial, timeMs, button, state);
                SendFrame(pointer);
            }
        }

        return serial;
    }

    /// <summary>
    /// Sends a scroll event to the focused client in the form that each bound version understands, and raises <see cref="Axised"/>. It bypasses grabs.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The scroll event. A value and a value120 of zero sends <c>axis_stop</c>.</param>
    public void SendAxis(uint timeMs, in PointerAxis axis)
    {
        Axised?.Invoke(timeMs, axis);
        if (_seat.ClientOf(Focus) is not { } client)
        {
            return;
        }

        var stop = axis.IsStop;
        client.AccumulateValue120(axis, out var lowResValue, out var lowResSteps);
        foreach (var pointer in client.Pointers)
        {
            if (stop && pointer.Version < 5)
            {
                continue;
            }

            if (pointer.Version < 8 && axis.Value120 != 0 && lowResSteps == 0)
            {
                continue;
            }

            if (pointer.Version >= 5)
            {
                pointer.SendAxisSource(axis.Source);
            }

            if (stop)
            {
                pointer.SendAxisStop(timeMs, axis.Axis);
                SendFrame(pointer);
                continue;
            }

            if (pointer.Version >= 9)
            {
                pointer.SendAxisRelativeDirection(axis.Axis, axis.RelativeDirection);
            }

            if (axis.Value120 == 0)
            {
                pointer.SendAxis(timeMs, axis.Axis, WlFixed.FromDouble(axis.Value));
            }
            else if (pointer.Version >= 8)
            {
                pointer.SendAxisValue120(axis.Axis, axis.Value120);
                pointer.SendAxis(timeMs, axis.Axis, WlFixed.FromDouble(axis.Value));
            }
            else
            {
                if (pointer.Version >= 5)
                {
#pragma warning disable CS0618
                    pointer.SendAxisDiscrete(axis.Axis, lowResSteps);
#pragma warning restore CS0618
                }

                pointer.SendAxis(timeMs, axis.Axis, WlFixed.FromDouble(lowResValue));
            }

            SendFrame(pointer);
        }
    }

    internal void HandleSetCursor(SeatClient client, WlPointerResource.SetCursorEventArgs e)
    {
        if (_seat.ClientOf(Focus) != client || e.Serial != _lastEnterSerial)
        {
            return;
        }

        Surface? cursorSurface = null;
        if (e.Surface is { } resource)
        {
            cursorSurface = _seat.ResolveSurface(resource);
            if (cursorSurface is null)
            {
                return;
            }

            if (!cursorSurface.TrySetRole(CursorRole, this) && cursorSurface.RoleObject != this)
            {
                resource.PostError(0 , "surface already has another role");
                return;
            }
        }

        var hotspotScale = cursorSurface?.ServerScale ?? 1.0;
        CursorRequested?.Invoke(new CursorRequest(
            cursorSurface, ServerScaling.ToLogical(e.HotspotX, hotspotScale), ServerScaling.ToLogical(e.HotspotY, hotspotScale)));
    }

    private static void SendFrame(WlPointerResource pointer)
    {
        if (pointer.Version >= 5)
        {
            pointer.SendFrame();
        }
    }

    private sealed class DefaultGrab(SeatPointer pointer) : IPointerGrab
    {
        public void Enter(Surface? surface, double x, double y) => pointer.SendEnter(surface, x, y);

        public void Motion(uint timeMs, double x, double y) => pointer.SendMotion(timeMs, x, y);

        public uint Button(uint timeMs, uint button, WlPointer.ButtonState state) =>
            pointer.SendButton(timeMs, button, state);

        public void Axis(uint timeMs, in PointerAxis axis) => pointer.SendAxis(timeMs, axis);

        public void Cancel()
        {
        }
    }
}
