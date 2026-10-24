using System.Runtime.InteropServices;
using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// A synchronization context that runs posted work on the compositor loop thread. Any thread can post, and a wake pipe on the loop starts the work.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositorSynchronizationContext"/> class. It must be made on the loop thread, which becomes the target thread. It throws <see cref="InvalidOperationException"/> when the wake pipe cannot be made.
    /// </summary>
    /// <param name="loop">The loop that watches the wake pipe.</param>
    public CompositorSynchronizationContext(ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        unsafe
        {
            fixed (int* fds = _pipe)
            {
                var created = PlatformFacts.HasLinuxSyscalls ? pipe2(fds, OCloexec | ONonblock) : pipe(fds);
                if (created != 0)
                {
                    throw new InvalidOperationException("the compositor wake pipe could not be created");
                }

                if (!PlatformFacts.HasLinuxSyscalls)
                {
                    for (var i = 0; i < 2; i++)
                    {
                        _ = fcntl(fds[i], FSetFd, FdCloexec);
                        _ = fcntl(fds[i], FSetFl, fcntl(fds[i], FGetFl, 0) | ONonblockDarwin);
                    }
                }
            }
        }

        _wake = loop.AddFd(_pipe[0], FdReadiness.Readable, OnWake);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets a value indicating whether the caller runs on the loop thread.
    /// </summary>
    public bool IsOnLoopThread => Environment.CurrentManagedThreadId == _loopThreadId;

    /// <summary>
    /// Gets the number of posted callbacks that did not run yet.
    /// </summary>
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

    /// <summary>
    /// Queues a callback for the loop thread. It can be called from any thread. The wake byte is written under the queue lock, so a post that races <see cref="Dispose"/> either runs or is ignored and never writes to a closed fd. An exception from the callback is logged.
    /// </summary>
    /// <param name="d">The callback.</param>
    /// <param name="state">The argument for the callback.</param>
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
            unsafe
            {
                byte one = 1;
                _ = write(_pipe[1], &one, 1);
            }
        }
    }

    /// <summary>
    /// Runs a callback now on the loop thread. It throws <see cref="InvalidOperationException"/> from another thread, because a block on the compositor can deadlock.
    /// </summary>
    /// <param name="d">The callback.</param>
    /// <param name="state">The argument for the callback.</param>
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

    /// <summary>
    /// Gets this context, because all copies share one queue.
    /// </summary>
    /// <returns>This instance.</returns>
    public override SynchronizationContext CreateCopy() => this;

    /// <summary>
    /// Drops the queued callbacks, stops the wake watch and closes the pipe. It must be called on the loop thread.
    /// </summary>
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
            if (_pipe[1] >= 0)
            {
                _ = close(_pipe[1]);
                _pipe[1] = -1;
            }
        }

        _wake.Remove();
        if (_pipe[0] >= 0)
        {
            _ = close(_pipe[0]);
            _pipe[0] = -1;
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
    private const int ONonblockDarwin = 0x0004;
    private const int FGetFl = 3;
    private const int FSetFl = 4;
    private const int FSetFd = 2;
    private const int FdCloexec = 1;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe(int* fds);

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int command, int argument);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
