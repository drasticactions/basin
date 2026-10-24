using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;

namespace Basin;

/// <summary>
/// An <see cref="IBuffer"/> over dmabuf planes. It owns the plane file descriptors and closes them when the storage is freed.
/// </summary>
public sealed class DmabufBuffer : BufferBase, IBuffer
{
    [DllImport("libc")]
    private static extern int close(int fd);

    private DmabufAttributes _attributes;
    private readonly FdLedger? _ledger;
    private readonly WlClient? _fdOwner;

    /// <summary>
    /// Initializes a new instance of the <see cref="DmabufBuffer"/> class. The buffer takes ownership of each plane file descriptor.
    /// </summary>
    /// <param name="attributes">The dmabuf description.</param>
    /// <param name="ledger">The ledger that records each plane file descriptor, or <see langword="null"/>.</param>
    /// <param name="fdOwner">The client whose fd table the descriptors came from, or <see langword="null"/> when they belong to this process.</param>
    public DmabufBuffer(in DmabufAttributes attributes, FdLedger? ledger = null, WlClient? fdOwner = null)
        : base(attributes.Width, attributes.Height)
    {
        _attributes = attributes;
        _ledger = ledger;
        _fdOwner = fdOwner;
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            if (fdOwner is null)
            {
                _ledger?.Acquired(_attributes.Fds[plane], $"dmabuf plane {plane}");
            }
            else
            {
                _ledger?.AcquiredFromClient(_attributes.Fds[plane], $"dmabuf plane {plane}");
            }
        }
    }

    /// <inheritdoc/>
    public override DrmFormat Format => _attributes.Format;

    /// <summary>Gets the format modifier.</summary>
    public ulong Modifier => _attributes.Modifier;

    /// <summary>Gets the <c>dev_t</c> that the client asked to sample on, or zero when it named none.</summary>
    public ulong SamplingDevice => _attributes.SamplingDevice;

    /// <summary>
    /// Gets the dmabuf planes. The buffer keeps ownership of the file descriptors.
    /// </summary>
    /// <param name="attributes">The dmabuf description.</param>
    /// <returns><see langword="true"/> until the storage is freed.</returns>
    public bool TryGetDmabuf(out DmabufAttributes attributes)
    {
        attributes = _attributes;
        return !IsStorageFreed;
    }

    private const ulong DmaBufIoctlSync = 0x40086200;
    private const ulong SyncRead = 1;
    private const ulong SyncWrite = 2;
    private const ulong SyncEnd = 4;
    private const int ProtRead = 1;
    private const int ProtWrite = 2;
    private const int MapShared = 1;
    private const int SeekEnd = 2;

    [DllImport("libc", SetLastError = true)]
    private static extern nint mmap(nint address, nuint length, int protection, int flags, int fd, long offset);

    [DllImport("libc")]
    private static extern int munmap(nint address, nuint length);

    [DllImport("libc", SetLastError = true)]
    private static extern long lseek(int fd, long offset, int whence);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref ulong argument);

    [DllImport("libc", SetLastError = true)]
    private static extern int fstat(int fd, out FileStat stat);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStat
    {
        public ulong Device;
        public ulong Inode;
        public ulong Padding0;
        public ulong Padding1;
        public ulong Padding2;
        public ulong Padding3;
        public ulong Padding4;
        public ulong Padding5;
        public ulong Padding6;
        public ulong Padding7;
        public ulong Padding8;
        public ulong Padding9;
        public ulong Padding10;
        public ulong Padding11;
        public ulong Padding12;
        public ulong Padding13;
        public ulong Padding14;
        public ulong Padding15;
    }

    private nint _mapping;
    private nuint _mappingLength;
    private bool _mappingWritable;
    private bool _mapRefused;
    private ulong _syncFlags;
    private int _mapDepth;

    /// <summary>
    /// Gets a value indicating whether the CPU can map the buffer. That requires a linear modifier and all planes in one allocation.
    /// </summary>
    public bool IsCpuMappable => _attributes.Modifier == DrmFormatSet.ModifierLinear && OneAllocation();

    /// <inheritdoc/>
    protected override bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        view = default;
        var write = (access & BufferDataAccess.Write) != 0;
        if (_mapRefused || !IsCpuMappable || !EnsureMapping(write))
        {
            return false;
        }

        if (_mapDepth++ == 0)
        {
            _syncFlags = (write ? SyncWrite : 0) | SyncRead;
            var start = _syncFlags;
            _ = ioctl(_attributes.Fds[0], DmaBufIoctlSync, ref start);
        }

        view = new BufferDataView(_mapping + (nint)_attributes.Offsets[0], (int)_attributes.Strides[0], _attributes.Format);
        return true;
    }

    /// <inheritdoc/>
    protected override void Unmap()
    {
        if (_mapDepth > 0 && --_mapDepth == 0)
        {
            var end = _syncFlags | SyncEnd;
            _ = ioctl(_attributes.Fds[0], DmaBufIoctlSync, ref end);
        }
    }

    private bool OneAllocation()
    {
        if (_attributes.PlaneCount <= 1)
        {
            return _attributes.PlaneCount == 1;
        }

        if (fstat(_attributes.Fds[0], out var first) != 0)
        {
            return false;
        }

        for (var plane = 1; plane < _attributes.PlaneCount; plane++)
        {
            if (fstat(_attributes.Fds[plane], out var other) != 0 || other.Inode != first.Inode || other.Device != first.Device)
            {
                return false;
            }
        }

        return true;
    }

    private bool EnsureMapping(bool write)
    {
        if (_mapping != 0 && (_mappingWritable || !write))
        {
            return true;
        }

        if (_mapping != 0)
        {
            _ = munmap(_mapping, _mappingLength);
            _mapping = 0;
        }

        var size = lseek(_attributes.Fds[0], 0, SeekEnd);
        _ = lseek(_attributes.Fds[0], 0, 0);
        if (size <= 0)
        {
            _mapRefused = true;
            return false;
        }

        var mapped = mmap(0, (nuint)size, ProtRead | (write ? ProtWrite : 0), MapShared, _attributes.Fds[0], 0);
        if (mapped == -1)
        {
            if (!write)
            {
                _mapRefused = true;
            }

            return false;
        }

        _mapping = mapped;
        _mappingLength = (nuint)size;
        _mappingWritable = write;
        return true;
    }

    /// <inheritdoc/>
    protected override void OnFreeStorage()
    {
        if (_mapping != 0)
        {
            _ = munmap(_mapping, _mappingLength);
            _mapping = 0;
        }

        for (var plane = 0; plane < _attributes.PlaneCount; plane++)
        {
            if (_fdOwner is null)
            {
                FdLedger.AssertNotClientOwned(_attributes.Fds[plane]);
                close(_attributes.Fds[plane]);
            }
            else
            {
                _fdOwner.CloseFd(_attributes.Fds[plane]);
            }

            _ledger?.Closed(_attributes.Fds[plane]);
            _attributes.Fds[plane] = -1;
        }
    }
}
