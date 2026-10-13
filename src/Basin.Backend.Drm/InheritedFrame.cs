using System.Runtime.InteropServices;
using Basin.Diagnostics;

namespace Basin.Backend.Drm;

public sealed class InheritedFrame : IDisposable
{
    private const ulong DmaBufIoctlSync = 0x40086200;
    private const ulong SyncRead = 1;
    private const ulong SyncEnd = 4;
    private const int ProtRead = 1;
    private const int MapShared = 1;
    private const int SeekEnd = 2;

    private DmabufBuffer? _dmabuf;
    private readonly DmabufAttributes _attributes;

    internal InheritedFrame(string output, in DmabufAttributes attributes, int sourceWidth, int sourceHeight)
    {
        Output = output;
        _attributes = attributes;
        _dmabuf = new DmabufBuffer(attributes);
        SourceWidth = sourceWidth > 0 ? Math.Min(sourceWidth, attributes.Width) : attributes.Width;
        SourceHeight = sourceHeight > 0 ? Math.Min(sourceHeight, attributes.Height) : attributes.Height;
        BasinCounters.Track();
    }

    public string Output { get; }

    public int Width => _attributes.Width;

    public int Height => _attributes.Height;

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    public DrmFormat Format => _attributes.Format;

    public ulong Modifier => _attributes.Modifier;

    public DmabufBuffer Dmabuf => _dmabuf ?? throw new ObjectDisposedException(nameof(InheritedFrame));

    public bool CpuReadable =>
        _attributes.PlaneCount == 1 &&
        _attributes.Modifier is DrmFormatSet.ModifierLinear or DrmFormatSet.ModifierInvalid;

    public MemoryBuffer? CopyToMemory()
    {
        ObjectDisposedException.ThrowIf(_dmabuf is null, this);
        if (!CpuReadable || Format.BytesPerPixel() is not (> 0 and var bytesPerPixel))
        {
            return null;
        }

        var fd = _attributes.Fds[0];
        var size = lseek(fd, 0, SeekEnd);
        _ = lseek(fd, 0, 0);
        var rowBytes = (long)SourceWidth * bytesPerPixel;
        var stride = (long)_attributes.Strides[0];
        var offset = (long)_attributes.Offsets[0];
        if (size <= 0 || offset + (stride * (SourceHeight - 1)) + rowBytes > size)
        {
            return null;
        }

        var mapping = mmap(0, (nuint)size, ProtRead, MapShared, fd, 0);
        if (mapping == -1)
        {
            return null;
        }

        var copy = new MemoryBuffer(SourceWidth, SourceHeight, Format);
        var start = SyncRead;
        _ = ioctl(fd, DmaBufIoctlSync, ref start);
        try
        {
            if (!copy.BeginDataAccess(BufferDataAccess.Write, out var view))
            {
                copy.Destroy();
                return null;
            }

            unsafe
            {
                for (var y = 0; y < SourceHeight; y++)
                {
                    var from = new ReadOnlySpan<byte>((byte*)mapping + offset + (stride * y), (int)rowBytes);
                    from.CopyTo(new Span<byte>((byte*)view.Data + ((long)view.Stride * y), (int)rowBytes));
                }
            }

            copy.EndDataAccess();
            return copy;
        }
        finally
        {
            var end = SyncRead | SyncEnd;
            _ = ioctl(fd, DmaBufIoctlSync, ref end);
            _ = munmap(mapping, (nuint)size);
        }
    }

    public void Dispose()
    {
        if (_dmabuf is null)
        {
            return;
        }

        _dmabuf.Destroy();
        _dmabuf = null;
        BasinCounters.Untrack();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern nint mmap(nint address, nuint length, int protection, int flags, int fd, long offset);

    [DllImport("libc")]
    private static extern int munmap(nint address, nuint length);

    [DllImport("libc", SetLastError = true)]
    private static extern long lseek(int fd, long offset, int whence);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref ulong argument);
}
