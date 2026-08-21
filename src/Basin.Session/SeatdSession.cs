using Seatd;

namespace Basin.Session;

public sealed class SeatdSession : ISession
{
    private readonly Seat _seat;
    private readonly IEventSource? _source;
    private bool _active;

    private SeatdSession(Seat seat, ICompositorEventLoop? loop)
    {
        _seat = seat;
        if (loop is not null)
        {
            _source = loop.AddFd(_seat.PollFd, FdReadiness.Readable, (_, _) => _seat.Dispatch(0));
        }
    }

    public static SeatdSession Open(ICompositorEventLoop? loop = null)
    {
        SeatdSession session = null!;
        Seat seat;
        try
        {
            seat = Seat.Open(_ => session?.OnEnabled(), _ => session?.OnDisabled());
        }
        catch (SeatdException e)
        {
            throw new InvalidOperationException(
                "No seat manager is available: {e.Message}", e);
        }

        for (var i = 0; i < 100 && !seat.IsActive; i++)
        {
            seat.Dispatch(50);
        }

        session = new SeatdSession(seat, loop) { _active = seat.IsActive };
        return session;
    }

    public string SeatName => _seat.Name;

    public bool IsActive => _active;

    public event Action? Enabled;

    public event Action? Disabled;

    public ISessionDevice OpenDevice(string path) => new SeatdDevice(_seat.OpenDevice(path));

    public void SwitchSession(int session) => _seat.SwitchSession(session);

    public void Dispose()
    {
        _source?.Remove();
        _seat.Dispose();
    }

    private void OnEnabled()
    {
        if (!_active)
        {
            _active = true;
            Enabled?.Invoke();
        }
        else
        {
            _active = true;
        }
    }

    private void OnDisabled()
    {
        _active = false;
        Disabled?.Invoke();
    }

    private sealed class SeatdDevice(SeatDevice device) : ISessionDevice
    {
        public int FileDescriptor => device.FileDescriptor;

        public string Path => device.Path;

        public void Dispose() => device.Dispose();
    }
}
