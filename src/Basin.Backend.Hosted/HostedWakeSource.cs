using System.Runtime.InteropServices;

namespace Basin.Backend.Hosted;

public sealed class HostedWakeSource : IDisposable
{
    private readonly int _fd = -1;
    private readonly Thread? _thread;
    private readonly int[] _wake = [-1, -1];
    private volatile bool _stopping;
    private volatile bool _suspended;

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
                    throw new InvalidOperationException("the wake pipe could not be created");
                }
            }
        }

        IsWatching = true;
        _thread = new Thread(Watch) { IsBackground = true, Name = "basin-hosted-wake" };
        _thread.Start();
    }

    public bool IsWatching { get; }

    public event Action? Ready;

    public bool IsSuspended => _suspended;

    public void Suspend()
    {
        if (_suspended)
        {
            return;
        }

        _suspended = true;
        Poke();
    }

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
