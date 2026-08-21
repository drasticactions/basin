using Wayland;

namespace Basin.Seat;

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

    public bool HasPoints => _points.Count > 0;

    public ITouchGrab Grab => _grabs.Count > 0 ? _grabs[^1] : _defaultGrab;

    public bool HasGrab => _grabs.Count > 0;

    public bool IsActiveDownSerial(uint serial) => TryGetPointBySerial(serial, out _);

    public bool Accepts(Surface? surface) => _seat.ClientOf(surface) is { Touches.Count: > 0 };

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

    public uint NotifyDown(Surface surface, uint timeMs, int id, double x, double y) =>
        Grab.Down(surface, timeMs, id, x, y);

    public void NotifyUp(uint timeMs, int id) => Grab.Up(timeMs, id);

    public void NotifyMotion(uint timeMs, int id, double x, double y) => Grab.Motion(timeMs, id, x, y);

    public void NotifyFrame() => Grab.Frame();

    public void NotifyCancel() => Grab.Cancel();

    public void StartGrab(ITouchGrab grab) => _grabs.Add(grab);

    public void EndGrab(ITouchGrab grab) => _grabs.Remove(grab);

    public uint SendDown(Surface surface, uint timeMs, int id, double x, double y)
    {
        var serial = _seat.NextSerial(SerialKind.TouchDown);
        _points[id] = (surface, serial);
        if (_seat.ClientOf(surface) is { } client)
        {
            foreach (var touch in client.Touches)
            {
                touch.SendDown(serial, timeMs, surface.Resource, id, WlFixed.FromDouble(x), WlFixed.FromDouble(y));
            }

            _frameClients.Add(client);
        }

        return serial;
    }

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

    public void SendMotion(uint timeMs, int id, double x, double y)
    {
        if (!_points.TryGetValue(id, out var point) || _seat.ClientOf(point.Surface) is not { } client)
        {
            return;
        }

        foreach (var touch in client.Touches)
        {
            touch.SendMotion(timeMs, id, WlFixed.FromDouble(x), WlFixed.FromDouble(y));
        }

        _frameClients.Add(client);
    }

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
