using System.Runtime.InteropServices;
using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// Waits for one descriptor at a time to become readable, for example a render fence. It watches a private epoll instance that the loop watches once, so a new wait adds no loop source. Linux only.
/// </summary>
public sealed unsafe class DescriptorWait : IDisposable
{
    private const int EpollCloexec = 0x80000;
    private const int EpollCtlAdd = 1;
    private const int EpollCtlDel = 2;
    private const uint EpollIn = 0x001;

    private readonly IEventSource _source;
    private readonly Action _ready;
    private int _epoll;
    private int _fd = -1;

    private DescriptorWait(ICompositorEventLoop loop, int epoll, Action ready)
    {
        _epoll = epoll;
        _ready = ready;
        _source = loop.AddFd(epoll, FdReadiness.Readable, OnReadable);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the descriptor that the wait watches, or -1 when it is not armed.
    /// </summary>
    public int Fd => _fd;

    /// <summary>
    /// Gets a value indicating whether a descriptor is armed.
    /// </summary>
    public bool IsArmed => _fd >= 0;

    /// <summary>
    /// Creates a wait whose callback runs on the loop thread when the armed descriptor becomes readable.
    /// </summary>
    /// <param name="loop">The loop that runs the callback.</param>
    /// <param name="ready">The callback. The wait is disarmed before it runs.</param>
    /// <returns>The wait, or <see langword="null"/> when the system has no epoll.</returns>
    public static DescriptorWait? TryCreate(ICompositorEventLoop loop, Action ready)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(ready);
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var epoll = epoll_create1(EpollCloexec);
        return epoll < 0 ? null : new DescriptorWait(loop, epoll, ready);
    }

    /// <summary>
    /// Watches a descriptor until it becomes readable. It replaces the descriptor that was armed before. The caller keeps the descriptor and must disarm before it closes it.
    /// </summary>
    /// <param name="fd">The descriptor to watch.</param>
    /// <returns><see langword="false"/> when the descriptor cannot be watched or the wait is disposed.</returns>
    public bool Arm(int fd)
    {
        Disarm();
        if (_epoll < 0 || fd < 0)
        {
            return false;
        }

        var entry = stackalloc byte[16];
        *(uint*)entry = EpollIn;
        if (epoll_ctl(_epoll, EpollCtlAdd, fd, entry) != 0)
        {
            return false;
        }

        _fd = fd;
        return true;
    }

    /// <summary>
    /// Stops watching the armed descriptor. The descriptor stays open. A wait that is not armed does nothing.
    /// </summary>
    public void Disarm()
    {
        if (_fd < 0)
        {
            return;
        }

        var entry = stackalloc byte[16];
        _ = epoll_ctl(_epoll, EpollCtlDel, _fd, entry);
        _fd = -1;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_epoll < 0)
        {
            return;
        }

        Disarm();
        _source.Remove();
        _ = close(_epoll);
        _epoll = -1;
        BasinCounters.Untrack();
    }

    private void OnReadable(int fd, FdReadiness readiness)
    {
        if (_fd < 0 || _epoll < 0)
        {
            return;
        }

        var events = stackalloc byte[16];
        if (epoll_wait(_epoll, events, 1, 0) <= 0)
        {
            return;
        }

        Disarm();
        _ready();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int epoll_create1(int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int epoll_ctl(int epoll, int operation, int fd, byte* entry);

    [DllImport("libc", SetLastError = true)]
    private static extern int epoll_wait(int epoll, byte* events, int maxEvents, int timeoutMs);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
