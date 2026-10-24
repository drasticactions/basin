using Basin.Diagnostics;
using Wayland;

namespace Basin.Seat;

/// <summary>
/// The seat's touch device. It delivers down, motion, up, frame and cancel to the clients under each contact and runs touch grabs.
/// </summary>
public sealed class SeatTouch
{
    private readonly Seat _seat;
    private readonly Dictionary<int, (Surface Surface, uint DownSerial)> _points = [];
    private readonly HashSet<SeatClient> _frameClients = [];
    private readonly List<ITouchGrab> _grabs = [];
    private readonly HashSet<SeatClient> _canceled = [];
    private readonly DefaultGrab _defaultGrab;

    internal SeatTouch(Seat seat)
    {
        _seat = seat;
        _defaultGrab = new DefaultGrab(this);
    }

    /// <summary>
    /// Gets a value indicating whether a contact is down on a client surface.
    /// </summary>
    public bool HasPoints => _points.Count > 0;

    /// <summary>
    /// Gets the router that drives this device, or <see langword="null"/>. A new <see cref="TouchRouter"/> sets it.
    /// </summary>
    public TouchRouter? Router { get; internal set; }

    /// <summary>
    /// Gets the grab that receives the notify calls: the newest started grab, or the default grab that delivers to clients.
    /// </summary>
    public ITouchGrab Grab => _grabs.Count > 0 ? _grabs[^1] : _defaultGrab;

    /// <summary>
    /// Gets a value indicating whether a grab other than the default one is active.
    /// </summary>
    public bool HasGrab => _grabs.Count > 0;

    /// <summary>
    /// Checks whether a serial belongs to a touch down that is still on its surface.
    /// </summary>
    /// <param name="serial">The serial.</param>
    /// <returns><see langword="true"/> when the contact of that down is still live.</returns>
    public bool IsActiveDownSerial(uint serial) => TryGetPointBySerial(serial, out _);

    /// <summary>
    /// Checks whether the client of a surface has bound <c>wl_touch</c>.
    /// </summary>
    /// <param name="surface">The surface, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> for a null or destroyed surface, or a client with no touch resource.</returns>
    public bool Accepts(Surface? surface) => _seat.ClientOf(surface) is { Touches.Count: > 0 };

    /// <summary>
    /// Finds the live contact of a touch down serial.
    /// </summary>
    /// <param name="serial">The down serial.</param>
    /// <param name="id">The contact id, or zero when it is not found.</param>
    /// <returns><see langword="false"/> when no live contact has that serial.</returns>
    public bool TryGetPointBySerial(uint serial, out int id)
    {
        foreach (var (pointId, point) in _points)
        {
            if (point.DownSerial == serial)
            {
                id = pointId;
                return true;
            }
        }

        id = 0;
        return false;
    }

    private int _scopedNotifies;

    private const int NotifyScopeWarmup = 10;

    /// <summary>
    /// Passes a touch down to the active grab.
    /// </summary>
    /// <param name="surface">The surface under the contact.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal surface-local position in logical pixels.</param>
    /// <param name="y">The vertical surface-local position in logical pixels.</param>
    /// <returns>The serial that the grab issued, or zero.</returns>
    public uint NotifyDown(Surface surface, uint timeMs, int id, double x, double y)
    {
        if (HasGrab || _scopedNotifies < NotifyScopeWarmup)
        {
            _scopedNotifies += HasGrab ? 0 : 1;
            return Grab.Down(surface, timeMs, id, x, y);
        }

        AllocationScope.Begin(region: "TouchNotify", forgiving: true);
        try
        {
            return _defaultGrab.Down(surface, timeMs, id, x, y);
        }
        finally
        {
            AllocationScope.End();
        }
    }

    /// <summary>
    /// Passes the lift of a contact to the active grab.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    public void NotifyUp(uint timeMs, int id)
    {
        if (HasGrab || _scopedNotifies < NotifyScopeWarmup)
        {
            _scopedNotifies += HasGrab ? 0 : 1;
            Grab.Up(timeMs, id);
            return;
        }

        AllocationScope.Begin(region: "TouchNotify", forgiving: true);
        try
        {
            _defaultGrab.Up(timeMs, id);
        }
        finally
        {
            AllocationScope.End();
        }
    }

    /// <summary>
    /// Passes a contact motion to the active grab.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position local to the surface of the down.</param>
    /// <param name="y">The vertical position local to the surface of the down.</param>
    public void NotifyMotion(uint timeMs, int id, double x, double y)
    {
        if (HasGrab || _scopedNotifies < NotifyScopeWarmup)
        {
            _scopedNotifies += HasGrab ? 0 : 1;
            Grab.Motion(timeMs, id, x, y);
            return;
        }

        AllocationScope.Begin(region: "TouchNotify", forgiving: true);
        try
        {
            _defaultGrab.Motion(timeMs, id, x, y);
        }
        finally
        {
            AllocationScope.End();
        }
    }

    /// <summary>
    /// Passes the end of a group of touch events to the active grab.
    /// </summary>
    public void NotifyFrame()
    {
        if (HasGrab || _scopedNotifies < NotifyScopeWarmup)
        {
            _scopedNotifies += HasGrab ? 0 : 1;
            Grab.Frame();
            return;
        }

        AllocationScope.Begin(region: "TouchNotify", forgiving: true);
        try
        {
            _defaultGrab.Frame();
        }
        finally
        {
            AllocationScope.End();
        }
    }

    /// <summary>
    /// Passes a cancel of the whole touch sequence to the active grab.
    /// </summary>
    public void NotifyCancel() => Grab.Cancel();

    /// <summary>
    /// Pushes a grab. It receives every notify call until it ends or a newer grab starts.
    /// </summary>
    /// <param name="grab">The grab.</param>
    public void StartGrab(ITouchGrab grab) => _grabs.Add(grab);

    /// <summary>
    /// Removes a grab without a cancel call.
    /// </summary>
    /// <param name="grab">The grab. A grab that is not started is ignored.</param>
    public void EndGrab(ITouchGrab grab) => _grabs.Remove(grab);

    /// <summary>
    /// Sends <c>wl_touch.down</c> to the client of a surface and records the contact. It bypasses grabs.
    /// </summary>
    /// <param name="surface">The surface under the contact.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal surface-local position in logical pixels.</param>
    /// <param name="y">The vertical surface-local position in logical pixels.</param>
    /// <returns>The down serial, issued also when the client has no touch resource.</returns>
    public uint SendDown(Surface surface, uint timeMs, int id, double x, double y)
    {
        var serial = _seat.NextSerial(SerialKind.TouchDown);
        _points[id] = (surface, serial);
        if (_seat.ClientOf(surface) is { } client)
        {
            var scale = surface.ServerScale;
            foreach (var touch in client.Touches)
            {
                touch.SendDown(serial, timeMs, surface.Resource, id, WlFixed.FromDouble(x * scale), WlFixed.FromDouble(y * scale));
            }

            _frameClients.Add(client);
        }

        return serial;
    }

    /// <summary>
    /// Sends <c>wl_touch.up</c> for a recorded contact and forgets it. An unknown contact is ignored. It bypasses grabs.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    public void SendUp(uint timeMs, int id)
    {
        if (!_points.Remove(id, out var point))
        {
            return;
        }

        var serial = _seat.NextSerial(SerialKind.Other);
        if (_seat.ClientOf(point.Surface) is { } client)
        {
            foreach (var touch in client.Touches)
            {
                touch.SendUp(serial, timeMs, id);
            }

            _frameClients.Add(client);
        }
    }

    /// <summary>
    /// Sends <c>wl_touch.motion</c> for a recorded contact. An unknown contact is ignored. It bypasses grabs.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position local to the surface of the down.</param>
    /// <param name="y">The vertical position local to the surface of the down.</param>
    public void SendMotion(uint timeMs, int id, double x, double y)
    {
        if (!_points.TryGetValue(id, out var point) || _seat.ClientOf(point.Surface) is not { } client)
        {
            return;
        }

        var scale = point.Surface.ServerScale;
        foreach (var touch in client.Touches)
        {
            touch.SendMotion(timeMs, id, WlFixed.FromDouble(x * scale), WlFixed.FromDouble(y * scale));
        }

        _frameClients.Add(client);
    }

    /// <summary>
    /// Sends <c>wl_touch.frame</c> to every client that received a touch event since the last frame. It bypasses grabs.
    /// </summary>
    public void SendFrame()
    {
        foreach (var client in _frameClients)
        {
            foreach (var touch in client.Touches)
            {
                touch.SendFrame();
            }
        }

        _frameClients.Clear();
    }

    /// <summary>
    /// Sends <c>wl_touch.cancel</c> to every client with a live contact and forgets all contacts. It bypasses grabs.
    /// </summary>
    public void SendCancel()
    {
        foreach (var point in _points.Values)
        {
            if (_seat.ClientOf(point.Surface) is { } client)
            {
                _canceled.Add(client);
            }
        }

        foreach (var client in _canceled)
        {
            foreach (var touch in client.Touches)
            {
                touch.SendCancel();
            }
        }

        _frameClients.ExceptWith(_canceled);
        _canceled.Clear();
        _points.Clear();
    }

    private sealed class DefaultGrab(SeatTouch touch) : ITouchGrab
    {
        public uint Down(Surface surface, uint timeMs, int id, double x, double y) =>
            touch.SendDown(surface, timeMs, id, x, y);

        public void Up(uint timeMs, int id) => touch.SendUp(timeMs, id);

        public void Motion(uint timeMs, int id, double x, double y) => touch.SendMotion(timeMs, id, x, y);

        public void Frame() => touch.SendFrame();

        public void Cancel() => touch.SendCancel();
    }
}
