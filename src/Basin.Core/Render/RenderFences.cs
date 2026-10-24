using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Drm.Native;

namespace Basin;

/// <summary>
/// Holds the fence operations on sync_file, dmabuf and syncobj fds that the renderers and backends share.
/// </summary>
public static class RenderFences
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int poll(PollFd* fds, nuint nfds, int timeoutMs);

    private const short PollIn = 1;

    /// <summary>
    /// Signals a binary syncobj from the CPU. A failure is ignored.
    /// </summary>
    /// <param name="drmFd">The DRM fd to import the syncobj on.</param>
    /// <param name="syncobjFd">The syncobj fd. The caller keeps ownership of it.</param>
    [SupportedOSPlatform("linux")]
    public static unsafe void SignalSyncobjFd(int drmFd, int syncobjFd)
    {
        uint handle;
        if (Libdrm.drmSyncobjFDToHandle(drmFd, syncobjFd, &handle) != 0)
        {
            return;
        }

        _ = Libdrm.drmSyncobjSignal(drmFd, &handle, 1);
        _ = Libdrm.drmSyncobjDestroy(drmFd, handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct SyncMergeData
    {
        public fixed byte Name[32];
        public int Fd2;
        public int Fence;
        public uint Flags;
        public uint Pad;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int ioctl(int fd, nuint request, void* argument);

    private const nuint SyncIocMerge = 0xC0303E03;

    /// <summary>
    /// Makes one fence that signals when both inputs signal. When one input is -1, the other is duplicated.
    /// </summary>
    /// <param name="a">The first sync_file fd, or -1.</param>
    /// <param name="b">The second sync_file fd, or -1.</param>
    /// <returns>A new fd that the caller owns, or -1 when both inputs are -1 or the merge fails.</returns>
    public static unsafe int MergeSyncFiles(int a, int b)
    {
        if (a < 0 || b < 0)
        {
            var single = a < 0 ? b : a;
            return single < 0 ? -1 : dup(single);
        }

        var merge = default(SyncMergeData);
        merge.Fd2 = b;
        merge.Fence = -1;
        return ioctl(a, SyncIocMerge, &merge) == 0 ? merge.Fence : -1;
    }

    [DllImport("libc")]
    private static extern int dup(int fd);

    /// <summary>
    /// Duplicates a fence fd.
    /// </summary>
    /// <param name="fd">The fd to duplicate, or -1.</param>
    /// <returns>A new fd that the caller owns, or -1.</returns>
    public static int DuplicateFence(int fd) => fd < 0 ? -1 : dup(fd);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int close_(int fd);

    /// <summary>
    /// Closes a fence fd. A negative fd is ignored.
    /// </summary>
    /// <param name="fd">The fd to close.</param>
    public static void CloseFence(int fd)
    {
        if (fd >= 0)
        {
            _ = close_(fd);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DmabufExportSyncFile
    {
        public uint Flags;
        public int Fd;
    }

    private const nuint DmabufIocExportSyncFile = 0xC0086202;

    private const uint DmabufSyncRead = 1 << 0;
    private const uint DmabufSyncWrite = 2 << 0;

    /// <summary>
    /// Reads the implicit fences of a dmabuf as a sync_file and reports the error on failure.
    /// </summary>
    /// <param name="dmabufFd">The dmabuf fd.</param>
    /// <param name="forWrite"><see langword="true"/> to get the fences that a write must wait for, which includes the readers.</param>
    /// <param name="errno">The errno of the ioctl, or zero on success.</param>
    /// <returns>A new sync_file fd that the caller owns, or -1 on failure.</returns>
    public static unsafe int ExportDmabufSyncFile(int dmabufFd, bool forWrite, out int errno)
    {
        var export = new DmabufExportSyncFile
        {
            Flags = forWrite ? DmabufSyncWrite : DmabufSyncRead,
            Fd = -1,
        };
        if (ioctl(dmabufFd, DmabufIocExportSyncFile, &export) == 0)
        {
            errno = 0;
            return export.Fd;
        }

        errno = Marshal.GetLastPInvokeError();
        return -1;
    }

    /// <summary>
    /// Reads the time at which a signaled fence signaled, from the latest fence inside the sync_file.
    /// </summary>
    /// <param name="syncFileFd">The sync_file fd.</param>
    /// <returns>The signal time on the monotonic clock in nanoseconds, or zero when the fence has not signaled or the kernel cannot tell.</returns>
    public static unsafe long SignalTimeNanos(int syncFileFd)
    {
        if (syncFileFd < 0)
        {
            return 0;
        }

        var info = stackalloc byte[SyncFileInfoSize];
        new Span<byte>(info, SyncFileInfoSize).Clear();
        if (ioctl(syncFileFd, SyncIocFileInfo, info) != 0 || *(int*)(info + 32) != 1)
        {
            return 0;
        }

        var count = *(uint*)(info + 40);
        if (count == 0 || count > MaxFenceInfos)
        {
            return 0;
        }

        var fences = stackalloc byte[SyncFenceInfoSize * MaxFenceInfos];
        *(uint*)(info + 40) = count;
        *(ulong*)(info + 48) = (ulong)fences;
        if (ioctl(syncFileFd, SyncIocFileInfo, info) != 0)
        {
            return 0;
        }

        var latest = 0L;
        for (var i = 0; i < count; i++)
        {
            var fence = fences + (i * SyncFenceInfoSize);
            latest = Math.Max(latest, *(long*)(fence + 72));
        }

        return latest;
    }

    private const int SyncFileInfoSize = 56;

    private const int SyncFenceInfoSize = 80;

    private const int MaxFenceInfos = 8;

    private const nuint SyncIocFileInfo = 0xC0383E04;

    /// <summary>
    /// Tells whether a fence is signaled, without a wait.
    /// </summary>
    /// <param name="syncFileFd">The sync_file fd, or -1 for no fence.</param>
    /// <returns><see langword="true"/> when the fence is signaled or the fd is -1.</returns>
    public static unsafe bool IsSignaled(int syncFileFd)
    {
        if (syncFileFd < 0)
        {
            return true;
        }

        var pfd = new PollFd { Fd = syncFileFd, Events = PollIn };
        return poll(&pfd, 1, 0) > 0;
    }

    /// <summary>
    /// Reads the implicit fences of a dmabuf as a sync_file.
    /// </summary>
    /// <param name="dmabufFd">The dmabuf fd.</param>
    /// <param name="forWrite"><see langword="true"/> to get the fences that a write must wait for, which includes the readers.</param>
    /// <returns>A new sync_file fd that the caller owns, or -1 when the kernel cannot export.</returns>
    public static unsafe int ExportDmabufSyncFile(int dmabufFd, bool forWrite)
    {
        var export = new DmabufExportSyncFile
        {
            Flags = forWrite ? DmabufSyncWrite : DmabufSyncRead,
            Fd = -1,
        };
        return ioctl(dmabufFd, DmabufIocExportSyncFile, &export) == 0 ? export.Fd : -1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DmabufImportSyncFile
    {
        public uint Flags;
        public int Fd;
    }

    private const nuint DmabufIocImportSyncFile = 0x40086203;

    /// <summary>
    /// Attaches a fence to a dmabuf, so implicit-sync users of the buffer wait for it.
    /// </summary>
    /// <param name="dmabufFd">The dmabuf fd.</param>
    /// <param name="forWrite"><see langword="true"/> to attach the fence as a write, <see langword="false"/> as a read.</param>
    /// <param name="syncFileFd">The sync_file fd. The caller keeps ownership of it.</param>
    /// <returns><see langword="false"/> when the fd is negative or the kernel refused.</returns>
    public static unsafe bool ImportDmabufSyncFile(int dmabufFd, bool forWrite, int syncFileFd)
    {
        if (syncFileFd < 0)
        {
            return false;
        }

        var import = new DmabufImportSyncFile
        {
            Flags = forWrite ? DmabufSyncWrite : DmabufSyncRead,
            Fd = syncFileFd,
        };
        return ioctl(dmabufFd, DmabufIocImportSyncFile, &import) == 0;
    }

    /// <summary>
    /// Attaches a fence to every plane of a dmabuf. Failures are ignored.
    /// </summary>
    /// <param name="attributes">The buffer whose planes get the fence.</param>
    /// <param name="forWrite"><see langword="true"/> to attach the fence as a write, <see langword="false"/> as a read.</param>
    /// <param name="fenceFd">The sync_file fd. The caller keeps ownership of it.</param>
    public static void PublishFenceTo(in DmabufAttributes attributes, bool forWrite, int fenceFd)
    {
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            _ = ImportDmabufSyncFile(attributes.Fds[plane], forWrite, fenceFd);
        }
    }

    /// <summary>
    /// Blocks the calling thread until a fence signals or the timeout ends.
    /// </summary>
    /// <param name="syncFileFd">The sync_file fd, or -1 for no fence.</param>
    /// <param name="timeoutMs">The longest wait in milliseconds.</param>
    /// <returns><see langword="true"/> when the fence signaled or the fd is -1, and <see langword="false"/> on a timeout or an error.</returns>
    public static unsafe bool WaitSyncFile(int syncFileFd, int timeoutMs = 1000)
    {
        if (syncFileFd < 0)
        {
            return true;
        }

        var pfd = new PollFd { Fd = syncFileFd, Events = PollIn };
        while (true)
        {
            var rc = poll(&pfd, 1, timeoutMs);
            if (rc > 0)
            {
                return true;
            }

            if (rc == 0)
            {
                return false;
            }

            if (Marshal.GetLastPInvokeError() != 4 )
            {
                return false;
            }
        }
    }
}
