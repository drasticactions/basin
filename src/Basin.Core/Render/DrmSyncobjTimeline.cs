using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Drm.Native;

namespace Basin;

/// <summary>
/// Wraps a DRM timeline syncobj, a sequence of fence points that processes share as an fd. It is reference-counted, and the handle is destroyed when the count reaches zero. The DRM fd stays owned by the caller.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed unsafe class DrmSyncobjTimeline
{
    private const uint WaitFlagWaitAll = 1;
    private const uint WaitFlagWaitForSubmit = 2;

    [DllImport("libc", EntryPoint = "eventfd", SetLastError = true)]
    private static extern int EventFd(uint initial, int flags);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int CloseFd(int fd);

    [DllImport("libc", EntryPoint = "read")]
    private static extern nint ReadFd(int fd, void* buffer, nuint count);

    private const int EfdCloexec = 0x80000;

    private readonly int _drmFd;
    private uint _handle;
    private int _refs = 1;

    private DrmSyncobjTimeline(int drmFd, uint handle)
    {
        _drmFd = drmFd;
        _handle = handle;
    }

    /// <summary>
    /// Creates a new timeline on a DRM device. It throws <see cref="InvalidOperationException"/> when the kernel refuses.
    /// </summary>
    /// <param name="drmFd">A card or render node fd.</param>
    /// <returns>The timeline with a reference count of one.</returns>
    public static DrmSyncobjTimeline Create(int drmFd)
    {
        uint handle;
        var rc = Libdrm.drmSyncobjCreate(drmFd, 0, &handle);
        return rc != 0
            ? throw new InvalidOperationException($"drmSyncobjCreate failed ({rc}).")
            : new DrmSyncobjTimeline(drmFd, handle);
    }

    /// <summary>
    /// Creates a new timeline on a DRM device, without exceptions.
    /// </summary>
    /// <param name="drmFd">A card or render node fd.</param>
    /// <returns>The timeline, or <see langword="null"/> when the fd is negative, libdrm is missing or the kernel refuses.</returns>
    public static DrmSyncobjTimeline? TryCreate(int drmFd)
    {
        if (drmFd < 0)
        {
            return null;
        }

        uint handle;
        try
        {
            return Libdrm.drmSyncobjCreate(drmFd, 0, &handle) == 0 ? new DrmSyncobjTimeline(drmFd, handle) : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Imports a timeline that another process exported. It throws <see cref="InvalidOperationException"/> when the kernel refuses.
    /// </summary>
    /// <param name="drmFd">The DRM fd to import on.</param>
    /// <param name="syncobjFd">The syncobj fd. The caller keeps ownership of it.</param>
    /// <returns>The timeline with a reference count of one.</returns>
    public static DrmSyncobjTimeline ImportFd(int drmFd, int syncobjFd)
    {
        uint handle;
        var rc = Libdrm.drmSyncobjFDToHandle(drmFd, syncobjFd, &handle);
        return rc != 0
            ? throw new InvalidOperationException($"drmSyncobjFDToHandle failed ({rc}).")
            : new DrmSyncobjTimeline(drmFd, handle);
    }

    /// <summary>
    /// Exports the timeline as a syncobj fd. It throws <see cref="InvalidOperationException"/> when the kernel refuses.
    /// </summary>
    /// <returns>A new fd that the caller owns.</returns>
    public int ExportFd()
    {
        int fd;
        var rc = Libdrm.drmSyncobjHandleToFD(_drmFd, _handle, &fd);
        return rc != 0 ? throw new InvalidOperationException($"drmSyncobjHandleToFD failed ({rc}).") : fd;
    }

    /// <summary>
    /// Exports the timeline as a syncobj fd, without exceptions.
    /// </summary>
    /// <returns>A new fd that the caller owns, or -1 on failure.</returns>
    public int TryExportFd()
    {
        int fd;
        return Libdrm.drmSyncobjHandleToFD(_drmFd, _handle, &fd) == 0 ? fd : -1;
    }

    /// <summary>
    /// Signals a point from the CPU. A failure is ignored.
    /// </summary>
    /// <param name="point">The timeline point to signal.</param>
    public void Signal(ulong point)
    {
        var handle = _handle;
        _ = Libdrm.drmSyncobjTimelineSignal(_drmFd, &handle, &point, 1);
    }

    /// <summary>
    /// Reads the highest point that is signaled.
    /// </summary>
    /// <returns>The point, or zero when the query fails.</returns>
    public ulong QueryLastSignaled()
    {
        var handle = _handle;
        ulong point;
        return Libdrm.drmSyncobjQuery(_drmFd, &handle, &point, 1) == 0 ? point : 0;
    }

    /// <summary>
    /// Blocks until a point is submitted and signaled, or until the timeout ends.
    /// </summary>
    /// <param name="point">The timeline point to wait for.</param>
    /// <param name="timeoutNs">The longest wait in nanoseconds. Zero or less only checks the state.</param>
    /// <returns><see langword="true"/> when the point is signaled.</returns>
    public bool Wait(ulong point, long timeoutNs)
    {
        var handle = _handle;
        var deadline = timeoutNs <= 0 ? 0 : MonotonicNow() + timeoutNs;
        uint firstSignaled;
        var rc = Libdrm.drmSyncobjTimelineWait(
            _drmFd, &handle, &point, 1, deadline, WaitFlagWaitAll | WaitFlagWaitForSubmit, &firstSignaled);
        return rc == 0;
    }

    /// <summary>
    /// Tells whether a point is signaled, without a wait.
    /// </summary>
    /// <param name="point">The timeline point to check.</param>
    /// <returns><see langword="true"/> when the point is signaled.</returns>
    public bool IsSignaled(ulong point) => Wait(point, 0);

    /// <summary>
    /// Waits for a point on the event loop, through an eventfd. The callback runs on the loop thread when the point signals.
    /// </summary>
    /// <param name="loop">The loop that watches the eventfd.</param>
    /// <param name="point">The timeline point to wait for.</param>
    /// <param name="ready">The callback to run when the point signals.</param>
    /// <returns>A waiter that the caller must dispose, or <see langword="null"/> when the kernel has no syncobj eventfd support.</returns>
    public DrmSyncobjWaiter? TryWait(ICompositorEventLoop loop, ulong point, Action ready)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(ready);
        var eventFd = EventFd(0, EfdCloexec);
        if (eventFd < 0)
        {
            return null;
        }

        try
        {
            if (Libdrm.drmSyncobjEventfd(_drmFd, _handle, point, eventFd, 0) != 0)
            {
                _ = CloseFd(eventFd);
                return null;
            }
        }
        catch (EntryPointNotFoundException)
        {
            _ = CloseFd(eventFd);
            return null;
        }

        return new DrmSyncobjWaiter(loop, eventFd, ready);
    }

    /// <summary>
    /// Arms an existing waiter for another point, so the waiter and its eventfd can be used again.
    /// </summary>
    /// <param name="waiter">A waiter from <see cref="TryWait"/> on this timeline.</param>
    /// <param name="point">The next timeline point to wait for.</param>
    /// <returns><see langword="false"/> when the kernel refused.</returns>
    public bool Rearm(DrmSyncobjWaiter waiter, ulong point)
    {
        ArgumentNullException.ThrowIfNull(waiter);
        try
        {
            return Libdrm.drmSyncobjEventfd(_drmFd, _handle, point, waiter.EventFd, 0) == 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Exports the fence at a point as a sync_file.
    /// </summary>
    /// <param name="point">The timeline point to export.</param>
    /// <returns>A new sync_file fd that the caller owns, or -1 on failure.</returns>
    public int ExportSyncFileAt(ulong point)
    {
        uint binary;
        if (Libdrm.drmSyncobjCreate(_drmFd, 0, &binary) != 0)
        {
            return -1;
        }

        try
        {
            if (Libdrm.drmSyncobjTransfer(_drmFd, binary, 0, _handle, point, 0) != 0)
            {
                return -1;
            }

            int fd;
            return Libdrm.drmSyncobjExportSyncFile(_drmFd, binary, &fd) == 0 ? fd : -1;
        }
        finally
        {
            _ = Libdrm.drmSyncobjDestroy(_drmFd, binary);
        }
    }

    /// <summary>
    /// Attaches the fence of a sync_file to a point.
    /// </summary>
    /// <param name="point">The timeline point that gets the fence.</param>
    /// <param name="syncFileFd">The sync_file fd. The caller keeps ownership of it.</param>
    /// <returns><see langword="false"/> when the fd is negative or the kernel refused.</returns>
    public bool ImportSyncFileAt(ulong point, int syncFileFd)
    {
        if (syncFileFd < 0)
        {
            return false;
        }

        uint binary;
        if (Libdrm.drmSyncobjCreate(_drmFd, 0, &binary) != 0)
        {
            return false;
        }

        try
        {
            return Libdrm.drmSyncobjImportSyncFile(_drmFd, binary, syncFileFd) == 0 &&
                   Libdrm.drmSyncobjTransfer(_drmFd, _handle, point, binary, 0, 0) == 0;
        }
        finally
        {
            _ = Libdrm.drmSyncobjDestroy(_drmFd, binary);
        }
    }

    /// <summary>
    /// Adds one reference.
    /// </summary>
    public void Retain() => _refs++;

    /// <summary>
    /// Removes one reference. The last release destroys the syncobj handle.
    /// </summary>
    public void Release()
    {
        if (--_refs == 0 && _handle != 0)
        {
            _ = Libdrm.drmSyncobjDestroy(_drmFd, _handle);
            _handle = 0;
        }
    }

    private static long MonotonicNow()
    {
        var ticks = System.Diagnostics.Stopwatch.GetTimestamp();
        return (long)((double)ticks / System.Diagnostics.Stopwatch.Frequency * 1_000_000_000);
    }

    /// <summary>
    /// Watches the eventfd of one asynchronous wait on a timeline.
    /// </summary>
    public sealed class DrmSyncobjWaiter : IDisposable
    {
        private readonly int _eventFd;
        private readonly Action _ready;
        private IEventSource? _source;

        internal DrmSyncobjWaiter(ICompositorEventLoop loop, int eventFd, Action ready)
        {
            _eventFd = eventFd;
            _ready = ready;
            _source = loop.AddFd(eventFd, FdReadiness.Readable, OnReadable);
        }

        internal int EventFd => _eventFd;

        private void OnReadable(int fd, FdReadiness readiness)
        {
            ulong value;
            _ = ReadFd(fd, &value, sizeof(ulong));

            _ready();
        }

        private void Cancel()
        {
            if (_source is { } source)
            {
                _source = null;
                source.Remove();
                _ = CloseFd(_eventFd);
            }
        }

        /// <summary>
        /// Stops the watch and closes the eventfd. The callback does not run after it.
        /// </summary>
        public void Dispose() => Cancel();
    }
}
