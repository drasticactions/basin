using Basin.Diagnostics;

namespace Basin;

internal sealed class ImplicitFence : ISurfaceCommitFence
{
    private const int Enotty = 25;

    private static bool s_reported;

    private int _fd;
    private IEventSource? _source;

    private ImplicitFence(ICompositorEventLoop loop, int fd)
    {
        _fd = fd;
        _source = loop.AddFd(fd, FdReadiness.Readable, (_, _) => Signal());
        CoreLog.Log.Debug($"a dma-buf commit waits for its implicit fence");
    }

    internal static Func<int, (int Fd, int Errno)>? Export { get; set; }

    public bool Signaled { get; private set; }

    public event Action<ISurfaceCommitFence>? SignaledChanged;

    public static ImplicitFence? For(ICompositorEventLoop loop, IBuffer buffer)
    {
        if (!buffer.TryGetDmabuf(out var attributes))
        {
            return null;
        }

        var merged = -1;
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            var planeFd = attributes.Fds[plane];
            var seen = false;
            for (var earlier = 0; earlier < plane; earlier++)
            {
                seen |= attributes.Fds[earlier] == planeFd;
            }

            if (seen)
            {
                continue;
            }

            var (fd, errno) = Export is { } export ? export(planeFd) : (RenderFences.ExportDmabufSyncFile(planeFd, forWrite: false, out var error), error);
            if (fd < 0)
            {
                if (errno == Enotty && !s_reported)
                {
                    s_reported = true;
                    CoreLog.Log.Debug($"DMA_BUF_IOCTL_EXPORT_SYNC_FILE failed with ENOTTY; such a buffer applies without waiting for an implicit fence");
                }

                continue;
            }

            if (RenderFences.IsSignaled(fd))
            {
                RenderFences.CloseFence(fd);
                continue;
            }

            if (merged < 0)
            {
                merged = fd;
            }
            else
            {
                var both = RenderFences.MergeSyncFiles(merged, fd);
                RenderFences.CloseFence(merged);
                RenderFences.CloseFence(fd);
                merged = both;
            }
        }

        return merged < 0 ? null : new ImplicitFence(loop, merged);
    }

    public void Dispose()
    {
        _source?.Remove();
        _source = null;
        if (_fd >= 0)
        {
            RenderFences.CloseFence(_fd);
            _fd = -1;
        }
    }

    private void Signal()
    {
        Dispose();
        Signaled = true;
        SignaledChanged?.Invoke(this);
    }
}
