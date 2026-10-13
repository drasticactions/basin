using Seatd;

namespace Basin.Session;

public sealed class SeatdSession : ISession
{
    private readonly Seat _seat;
    private readonly IEventSource? _source;
    private readonly CompositorSynchronizationContext? _context;
    private readonly object _lock = new();
    private bool _active;
    private bool _disposed;

    private SeatdSession(Seat seat, ICompositorEventLoop? loop)
    {
        _seat = seat;
        if (loop is not null)
        {
            _context = new CompositorSynchronizationContext(loop);
            _source = loop.AddFd(_seat.PollFd, FdReadiness.Readable, (_, _) => TryDispatch());
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
                $"No seat manager is available: {e.Message}", e);
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

    public ISessionDevice OpenDevice(string path)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new SeatdDevice(this, _seat.OpenDevice(path));
        }
    }

    public Task<ISessionDevice> OpenDeviceAsync(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(() =>
        {
            try
            {
                return OpenDevice(path);
            }
            finally
            {
                _context?.Post(_ => TryDispatch(), null);
            }
        });
    }

    public Task CloseDeviceAsync(ISessionDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return Task.Run(device.Dispose);
    }

    public void SwitchSession(int session)
    {
        lock (_lock)
        {
            _seat.SwitchSession(session);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _source?.Remove();
            _context?.Dispose();
            _seat.Dispose();
        }
    }

    private void TryDispatch()
    {
        if (!Monitor.TryEnter(_lock))
        {
            return;
        }

        try
        {
            if (!_disposed)
            {
                _seat.Dispatch(0);
            }
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    private void CloseDevice(SeatDevice device)
    {
        lock (_lock)
        {
            device.Dispose();
        }
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

    private sealed class SeatdDevice(SeatdSession session, SeatDevice device) : ISessionDevice
    {
        public int FileDescriptor => device.FileDescriptor;

        public string Path => device.Path;

        public void Dispose() => session.CloseDevice(device);
    }
}
