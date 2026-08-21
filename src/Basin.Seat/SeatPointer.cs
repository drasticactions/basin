using Wayland;

namespace Basin.Seat;

public sealed class SeatPointer
{
    public const string CursorRole = "cursor";

    private readonly Seat _seat;
    private readonly List<IPointerGrab> _grabs = [];
    private readonly DefaultGrab _defaultGrab;
    private uint _lastEnterSerial;
    private int _pressedButtons;
    private double _focusLayoutOffsetX;
    private double _focusLayoutOffsetY;

    internal SeatPointer(Seat seat)
    {
        _seat = seat;
        _defaultGrab = new DefaultGrab(this);
    }

    public Surface? Focus { get; private set; }

    public double X { get; private set; }

    public double Y { get; private set; }

    public IPointerGrab Grab => _grabs.Count > 0 ? _grabs[^1] : _defaultGrab;

    public bool HasGrab => _grabs.Count > 0;

    public bool HasImplicitGrab => _pressedButtons > 0;

    public uint GrabSerial { get; private set; }

    public event Action<CursorRequest>? CursorRequested;

    public event Action<Surface?>? FocusChanged;

    public event Action<uint, double, double>? Moved;

    public bool ValidateEnterSerial(uint serial) => serial != 0 && serial == _lastEnterSerial;

    public void NotifyEnter(Surface? surface, double x, double y) => Grab.Enter(surface, x, y);

    public void NotifyClearFocus() => Grab.Enter(null, 0, 0);

    public void NotifyMotion(uint timeMs, double x, double y) => Grab.Motion(timeMs, x, y);

    public void NotifyWarp(uint timeMs, double x, double y) => SendWarp(timeMs, x, y);

    public void NotifyMotionAt(uint timeMs, Surface? surface, double surfaceX, double surfaceY, double layoutX, double layoutY)
    {
        if (!HasGrab && HasImplicitGrab && Focus is { IsDestroyed: false } pinned && !ReferenceEquals(surface, pinned))
        {
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

    public uint NotifyButton(uint timeMs, uint button, bool pressed) =>
        NotifyButton(timeMs, button, pressed ? WlPointer.ButtonState.Pressed : WlPointer.ButtonState.Released);

    public uint NotifyButton(uint timeMs, uint button, WlPointer.ButtonState state)
    {
        var serial = Grab.Button(timeMs, button, state);
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

    public void NotifyAxis(uint timeMs, in PointerAxis axis) => Grab.Axis(timeMs, axis);

    public void ClearImplicitGrab() => _pressedButtons = 0;

    public void StartGrab(IPointerGrab grab) => _grabs.Add(grab);

    public void EndGrab(IPointerGrab grab)
    {
        var wasActive = HasGrab && Grab == grab;
        _grabs.Remove(grab);
        if (wasActive)
        {
            grab.Cancel();
        }
    }

    internal void InitializeResource(SeatClient client, Wayland.WlPointerResource pointer)
    {
        if (Focus is { IsDestroyed: false } focus && _seat.ClientOf(focus) == client && _lastEnterSerial != 0)
        {
            pointer.SendEnter(_lastEnterSerial, focus.Resource, WlFixed.FromDouble(X), WlFixed.FromDouble(Y));
            SendFrame(pointer);
        }
    }

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
        if (surface is not null && _seat.ClientOf(surface) is { } client)
        {
            var serial = _seat.NextSerial(SerialKind.PointerEnter);
            _lastEnterSerial = serial;
            foreach (var pointer in client.Pointers)
            {
                pointer.SendEnter(serial, surface.Resource, WlFixed.FromDouble(x), WlFixed.FromDouble(y));
                SendFrame(pointer);
            }
        }

        FocusChanged?.Invoke(surface);
    }

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

    public void SendMotion(uint timeMs, double x, double y)
    {
        var dx = x - X;
        var dy = y - Y;
        X = x;
        Y = y;
        Moved?.Invoke(timeMs, dx, dy);
        if (_seat.ClientOf(Focus) is { } client)
        {
            foreach (var pointer in client.Pointers)
            {
                pointer.SendMotion(timeMs, WlFixed.FromDouble(x), WlFixed.FromDouble(y));
                SendFrame(pointer);
            }
        }
    }

    public void SendWarp(uint timeMs, double x, double y)
    {
        X = x;
        Y = y;
        if (_seat.ClientOf(Focus) is { } client)
        {
            foreach (var pointer in client.Pointers)
            {
                if (pointer.SupportsSendWarp)
                {
                    pointer.SendWarp(WlFixed.FromDouble(x), WlFixed.FromDouble(y));
                }
                else
                {
                    pointer.SendMotion(timeMs, WlFixed.FromDouble(x), WlFixed.FromDouble(y));
                }

                SendFrame(pointer);
            }
        }
    }

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

    public void SendAxis(uint timeMs, in PointerAxis axis)
    {
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

        CursorRequested?.Invoke(new CursorRequest(cursorSurface, e.HotspotX, e.HotspotY));
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
