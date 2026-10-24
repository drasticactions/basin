using System.Runtime.InteropServices;

namespace Basin.Backend.Hosted;

/// <summary>
/// Watches the poll descriptor of the compositor loop on a background thread and raises <see cref="Ready"/> when clients have data.
/// A host uses it to schedule a tick while the compositor is idle.
/// </summary>
public sealed class HostedWakeSource : IDisposable
{
    private readonly int _fd = -1;
    private readonly Thread? _thread;
    private readonly int[] _wake = [-1, -1];
    private volatile bool _stopping;
    private volatile bool _suspended;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedWakeSource"/> class.
    /// It starts the watch thread only when the platform has descriptors and threads and the loop has a poll descriptor.
    /// </summary>
    /// <param name="loop">The compositor loop to watch.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The system did not create the wake pipe.</exception>
    public HostedWakeSource(ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        if (!PlatformFacts.HasDescriptors || !PlatformFacts.HasThreads)
        {
            return;
        }

        try
        {
            _fd = loop.Fd;
        }
        catch (NotSupportedException)
        {
            return;
        }

        unsafe
        {
            fixed (int* fds = _wake)
            {
                if (pipe(fds) != 0)
                {
                    throw new InvalidOperationException($"The wake pipe could not be created (errno {Marshal.GetLastPInvokeError()}).");
                }
            }
        }

        IsWatching = true;
        _thread = new Thread(Watch) { IsBackground = true, Name = "basin-hosted-wake" };
        _thread.Start();
    }

    /// <summary>
    /// Gets a value indicating whether a watch thread runs. When it is <see langword="false"/>, <see cref="Ready"/> never occurs.
    /// </summary>
    public bool IsWatching { get; }

    /// <summary>
    /// Occurs on the watch thread, not on the compositor thread, when the loop descriptor is readable.
    /// It can occur again each millisecond until the loop is dispatched. A handler must not touch compositor state.
    /// </summary>
    public event Action? Ready;

    /// <summary>
    /// Gets a value indicating whether <see cref="Ready"/> is stopped by <see cref="Suspend"/>.
    /// </summary>
    public bool IsSuspended => _suspended;

    /// <summary>
    /// Stops <see cref="Ready"/> until <see cref="Resume"/>. The watch thread keeps running. Calling it twice has no other effect.
    /// </summary>
    public void Suspend()
    {
        if (_suspended)
        {
            return;
        }

        _suspended = true;
        Poke();
    }

    /// <summary>
    /// Starts <see cref="Ready"/> again after <see cref="Suspend"/>. Calling it twice has no other effect.
    /// </summary>
    public void Resume()
    {
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        Poke();
    }

    private void Poke()
    {
        if (_wake[1] >= 0)
        {
            unsafe
            {
                byte one = 0;
                _ = write(_wake[1], &one, 1);
            }
        }
    }

    /// <summary>
    /// Stops the watch thread, waits up to one second for it and closes the wake pipe.
    /// </summary>
    public void Dispose()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        Poke();
        _thread?.Join(1000);

        for (var i = 0; i < _wake.Length; i++)
        {
            if (_wake[i] >= 0)
            {
                _ = close(_wake[i]);
                _wake[i] = -1;
            }
        }
    }

    private unsafe void Watch()
    {
        var fds = stackalloc PollFd[2];
        while (!_stopping)
        {
            fds[0].Fd = _wake[0];
            fds[0].Events = PollIn;
            fds[0].REvents = 0;
            fds[1].Fd = _fd;
            fds[1].Events = PollIn;
            fds[1].REvents = 0;

            var suspended = _suspended;
            var ready = poll(fds, suspended ? 1u : 2u, -1);
            if (ready < 0)
            {
                if (Marshal.GetLastPInvokeError() == Eintr)
                {
                    continue;
                }

                return;
            }

            if (_stopping)
            {
                return;
            }

            if ((fds[0].REvents & PollIn) != 0)
            {
                byte token = 0;
                _ = read(_wake[0], &token, 1);
                continue;
            }

            if (!suspended && (fds[1].REvents & PollIn) != 0)
            {
                Ready?.Invoke();
                Thread.Sleep(PollBackoffMillis);
            }
        }
    }

    private const short PollIn = 1;
    private const int Eintr = 4;
    private const int PollBackoffMillis = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short REvents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int poll(PollFd* fds, nuint count, int timeoutMs);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe(int* fds);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
