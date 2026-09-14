using System.Runtime.InteropServices;
using Basin;
using Basin.Diagnostics;
using Basin.Render.Gbm;
using Mesa.Gbm;

namespace Basin.Portal.Client;

internal sealed class DmabufSupport : IDisposable
{
    private const int ORdwr = 2;
    private const int OCloexec = 0x80000;

    private readonly int _fd;
    private readonly GbmDevice _gbm;

    private DmabufSupport(int fd, GbmDevice gbm, GbmAllocator allocator)
    {
        _fd = fd;
        _gbm = gbm;
        Allocator = allocator;
    }

    public GbmAllocator Allocator { get; }

    public static DmabufSupport? TryOpen(string renderNode, DrmFormatSet importable, BasinLogger log)
    {
        if (importable.Count == 0)
        {
            return null;
        }

        var fd = open(renderNode, ORdwr | OCloexec);
        if (fd < 0)
        {
            log.Info($"dmabuf capture off: {renderNode} did not open (errno {Marshal.GetLastPInvokeError()})");
            return null;
        }

        GbmDevice? gbm = null;
        try
        {
            gbm = GbmDevice.Create(fd);
        }
        catch (Exception e) when (e is GbmException or DllNotFoundException or EntryPointNotFoundException)
        {
            log.Info($"dmabuf capture off: no GBM device on {renderNode}: {e.Message}");
        }

        if (gbm is null)
        {
            close(fd);
            return null;
        }

        var allocator = new GbmAllocator(gbm, importable);
        log.Info($"dmabuf capture on: {renderNode}, {importable.Count} format(s)");
        return new DmabufSupport(fd, gbm, allocator);
    }

    public void Dispose()
    {
        Allocator.Dispose();
        _gbm.Dispose();
        close(_fd);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc")]
    private static extern int close(int fd);
}
