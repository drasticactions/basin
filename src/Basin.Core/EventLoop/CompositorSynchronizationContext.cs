using System.Runtime.InteropServices;
using Basin.Diagnostics;

namespace Basin;

public sealed class CompositorSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly int _loopThreadId = Environment.CurrentManagedThreadId;
    private readonly object _lock = new();
    private readonly List<(SendOrPostCallback Callback, object? State)> _queue = [];
    private readonly List<(SendOrPostCallback Callback, object? State)> _running = [];
    private readonly int[] _pipe = [-1, -1];
    private readonly IEventSource _wake;
    private volatile bool _disposed;

    public CompositorSynchronizationContext(ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        unsafe
        {
            fixed (int* fds = _pipe)
            {
                if (pipe2(fds, OCloexec | ONonblock) != 0)
                {
                    throw new InvalidOperationException("the compositor wake pipe could not be created");
                }
            }
        }

        _wake = loop.AddFd(_pipe[0], FdReadiness.Readable, OnWake);
        BasinCounters.Track();
    }

    public bool IsOnLoopThread => Environment.CurrentManagedThreadId == _loopThreadId;

    public int Pending
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _queue.Add((d, state));
        }

        unsafe
        {
            byte one = 1;
            _ = write(_pipe[1], &one, 1);
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (!IsOnLoopThread)
        {
            throw new InvalidOperationException(
                "Send from a foreign thread would block it on the compositor; Post instead");
        }

        Run(d, state);
    }

    public override SynchronizationContext CreateCopy() => this;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _thread.Assert();
        lock (_lock)
        {
            _disposed = true;
            _queue.Clear();
        }

        _wake.Remove();
        for (var i = 0; i < _pipe.Length; i++)
        {
            if (_pipe[i] >= 0)
            {
                _ = close(_pipe[i]);
                _pipe[i] = -1;
            }
        }

        BasinCounters.Untrack();
    }

    private void OnWake(int fd, FdReadiness readiness)
    {
        unsafe
        {
            var drain = stackalloc byte[64];
            while (read(fd, drain, 64) == 64)
            {
            }
        }

        lock (_lock)
        {
            _running.AddRange(_queue);
            _queue.Clear();
        }

        try
        {
            foreach (var (callback, state) in _running)
            {
                Run(callback, state);
            }
        }
        finally
        {
            _running.Clear();
        }
    }

    private void Run(SendOrPostCallback callback, object? state)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            callback(state);
        }
        catch (Exception e)
        {
            BasinLog.Error($"a callback posted to the compositor loop threw: {e}");
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    private const int OCloexec = 0x80000;
    private const int ONonblock = 0x800;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
