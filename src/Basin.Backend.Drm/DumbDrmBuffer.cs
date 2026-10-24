using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Drm;
using Drm.Native;

namespace Basin.Backend.Drm;

/// <summary>
/// A dumb buffer that <see cref="DumbAllocator"/> created. The CPU can map it, the backend can scan it out, and it exports as a linear dmabuf.
/// </summary>
public sealed class DumbDrmBuffer : BufferBase
{
    private const uint DrmCloexec = 0x80000;
    private const uint DrmRdwr = 2;

    private readonly DrmDumbBuffer _dumb;
    private readonly int _deviceFd;
    private nint _data;
    private int _primeFd = -1;

    internal DumbDrmBuffer(DrmDumbBuffer dumb, DrmFormat format, int deviceFd)
        : base((int)dumb.Width, (int)dumb.Height)
    {
        _dumb = dumb;
        Format = format;
        _deviceFd = deviceFd;
    }

    /// <inheritdoc/>
    public override DrmFormat Format { get; }

    internal uint GemHandle => _dumb.Handle;

    internal uint Stride => _dumb.Pitch;

    /// <summary>
    /// Maps the dumb buffer's memory for every kind of access. The mapping stays until the storage is freed.
    /// </summary>
    /// <param name="access">The requested access. The buffer allows all access.</param>
    /// <param name="view">The mapped pixels with the kernel's pitch.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    protected override unsafe bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        if (_data == 0)
        {
            _data = (nint)Unsafe.AsPointer(ref MemoryMarshal.GetReference(_dumb.AsSpan()));
        }

        view = new BufferDataView(_data, (int)_dumb.Pitch, Format);
        return true;
    }

    /// <summary>
    /// Exports the dumb buffer through PRIME on the first call and describes it as one linear plane. The buffer keeps the file descriptor until its storage is freed.
    /// </summary>
    /// <param name="attributes">The plane, format and linear modifier, or the default value on failure.</param>
    /// <returns><see langword="false"/> when the device cannot export the buffer.</returns>
    public override unsafe bool TryGetDmabuf(out DmabufAttributes attributes)
    {
        attributes = default;
        if (IsDestroyed)
        {
            return false;
        }

        if (_primeFd < 0)
        {
            int fd;
            if (Libdrm.drmPrimeHandleToFD(_deviceFd, _dumb.Handle, DrmCloexec | DrmRdwr, &fd) != 0 &&
                Libdrm.drmPrimeHandleToFD(_deviceFd, _dumb.Handle, DrmCloexec, &fd) != 0)
            {
                return false;
            }

            _primeFd = fd;
        }

        attributes = new DmabufAttributes
        {
            Width = Width,
            Height = Height,
            Format = Format,
            Modifier = DrmFormatSet.ModifierLinear,
            PlaneCount = 1,
        };
        attributes.Fds[0] = _primeFd;
        attributes.Offsets[0] = 0;
        attributes.Strides[0] = _dumb.Pitch;
        return true;
    }

    /// <summary>
    /// Closes the exported file descriptor and destroys the dumb buffer in the kernel.
    /// </summary>
    protected override void OnFreeStorage()
    {
        _data = 0;
        if (_primeFd >= 0)
        {
            _ = CloseFd(_primeFd);
            _primeFd = -1;
        }

        _dumb.Dispose();
    }

    [DllImport("libc", EntryPoint = "close")]
    private static extern int CloseFd(int fd);
}
