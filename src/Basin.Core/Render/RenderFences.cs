using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Drm.Native;

namespace Basin;

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

    public static int DuplicateFence(int fd) => fd < 0 ? -1 : dup(fd);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int close_(int fd);

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

    public static void PublishFenceTo(in DmabufAttributes attributes, bool forWrite, int fenceFd)
    {
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            _ = ImportDmabufSyncFile(attributes.Fds[plane], forWrite, fenceFd);
        }
    }

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
