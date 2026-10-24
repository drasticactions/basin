using Seatd;

namespace Basin.Session;

/// <summary>
/// An <see cref="ISession"/> through libseat, which uses seatd or logind. Seat events dispatch on the compositor loop.
/// </summary>
public sealed class SeatdSession : ISession
{
    private readonly Seat _seat;
    private readonly IEventSource _source;
    private readonly CompositorSynchronizationContext _context;
    private readonly object _lock = new();
    private bool _active;
    private bool _disposed;

    private SeatdSession(Seat seat, ICompositorEventLoop loop)
    {
        _seat = seat;
        _context = new CompositorSynchronizationContext(loop);
        _source = loop.AddFd(_seat.PollFd, FdReadiness.Readable, (_, _) => TryDispatch());
    }

    /// <summary>
    /// Opens the seat and waits up to five seconds for it to become active.
    /// </summary>
    /// <param name="loop">The loop that watches the seat and dispatches its events, so that <see cref="Enabled"/> and <see cref="Disabled"/> occur.</param>
    /// <returns>The open session, which the caller owns. It can still be inactive when the wait expired.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No seat manager is available.</exception>
    public static SeatdSession Open(ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        SeatdSession session = null!;
        Seat seat;
        try
        {
            seat = Seat.Open(_ => session?.OnEnabled(), _ => session?.OnDisabled());
        }
        catch (SeatdException e)
        {
            throw new InvalidOperationException(
                "No seat manager is available. Start seatd, or run the compositor in a logind session.", e);
        }

        for (var i = 0; i < 100 && !seat.IsActive; i++)
        {
            seat.Dispatch(50);
        }

        session = new SeatdSession(seat, loop) { _active = seat.IsActive };
        return session;
    }

    /// <inheritdoc/>
    public string SeatName => _seat.Name;

    /// <inheritdoc/>
    public bool IsActive => _active;

    /// <summary>
    /// Occurs when the session changes from inactive to active. It occurs on the loop thread.
    /// </summary>
    public event Action? Enabled;

    /// <summary>
    /// Occurs when the seat manager disables the session. It occurs on the loop thread.
    /// </summary>
    public event Action? Disabled;

    /// <summary>
    /// Opens a device node through the seat manager. The caller owns the result.
    /// </summary>
    /// <param name="path">The absolute path of the device node.</param>
    /// <returns>The open device. Disposal closes it through the seat manager.</returns>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
    public ISessionDevice OpenDevice(string path)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new SeatdDevice(this, _seat.OpenDevice(path));
        }
    }

    /// <summary>
    /// Opens a device node on a thread-pool thread, then posts a seat dispatch to the loop for events that arrived while the seat was locked.
    /// </summary>
    /// <param name="path">The absolute path of the device node.</param>
    /// <returns>A task that completes with the open device.</returns>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
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
                _context.Post(_ => TryDispatch(), null);
            }
        });
    }

    /// <summary>
    /// Disposes a device on a thread-pool thread.
    /// </summary>
    /// <param name="device">The device to close.</param>
    /// <returns>A task that completes when the device is closed.</returns>
    public Task CloseDeviceAsync(ISessionDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return Task.Run(device.Dispose);
    }

    /// <inheritdoc/>
    public void SwitchSession(int session)
    {
        lock (_lock)
        {
            _seat.SwitchSession(session);
        }
    }

    /// <summary>
    /// Removes the loop watch and closes the seat. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _source.Remove();
            _context.Dispose();
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
