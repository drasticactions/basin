using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Drm.Native;

namespace Basin;

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

    public static DrmSyncobjTimeline Create(int drmFd)
    {
        uint handle;
        var rc = Libdrm.drmSyncobjCreate(drmFd, 0, &handle);
        return rc != 0
            ? throw new InvalidOperationException($"drmSyncobjCreate failed ({rc}).")
            : new DrmSyncobjTimeline(drmFd, handle);
    }

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

    public static DrmSyncobjTimeline ImportFd(int drmFd, int syncobjFd)
    {
        uint handle;
        var rc = Libdrm.drmSyncobjFDToHandle(drmFd, syncobjFd, &handle);
        return rc != 0
            ? throw new InvalidOperationException($"drmSyncobjFDToHandle failed ({rc}).")
            : new DrmSyncobjTimeline(drmFd, handle);
    }

    public int ExportFd()
    {
        int fd;
        var rc = Libdrm.drmSyncobjHandleToFD(_drmFd, _handle, &fd);
        return rc != 0 ? throw new InvalidOperationException($"drmSyncobjHandleToFD failed ({rc}).") : fd;
    }

    public int TryExportFd()
    {
        int fd;
        return Libdrm.drmSyncobjHandleToFD(_drmFd, _handle, &fd) == 0 ? fd : -1;
    }

    public void Signal(ulong point)
    {
        var handle = _handle;
        _ = Libdrm.drmSyncobjTimelineSignal(_drmFd, &handle, &point, 1);
    }

    public ulong QueryLastSignaled()
    {
        var handle = _handle;
        ulong point;
        return Libdrm.drmSyncobjQuery(_drmFd, &handle, &point, 1) == 0 ? point : 0;
    }

    public bool Wait(ulong point, long timeoutNs)
    {
        var handle = _handle;
        var deadline = timeoutNs <= 0 ? 0 : MonotonicNow() + timeoutNs;
        uint firstSignaled;
        var rc = Libdrm.drmSyncobjTimelineWait(
            _drmFd, &handle, &point, 1, deadline, WaitFlagWaitAll | WaitFlagWaitForSubmit, &firstSignaled);
        return rc == 0;
    }

    public bool IsSignaled(ulong point) => Wait(point, 0);

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

    public void Retain() => _refs++;

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

        private void OnReadable(int fd, FdReadiness readiness)
        {
            ulong value;
            _ = ReadFd(fd, &value, sizeof(ulong));

            Cancel();
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

        public void Dispose() => Cancel();
    }
}
