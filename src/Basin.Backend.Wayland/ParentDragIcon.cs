using System.Runtime.InteropServices;
using Wayland;

namespace Basin.Backend.Wayland;

internal sealed unsafe class ParentDragIcon : IDisposable
{
    private const int ProtReadWrite = 3;
    private const int MapShared = 1;

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc", SetLastError = true)]
    private static extern void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(void* addr, nuint length);

    [DllImport("libc")]
    private static extern int close(int fd);

    private readonly WaylandBackend _backend;
    private readonly Surface _guest;
    private readonly Action _onCommitted;
    private WlShmPool? _pool;
    private WlBuffer? _proxy;
    private nint _mapping;
    private int _mappingSize;
    private int _mappingFd = -1;
    private int _width;
    private int _height;
    private bool _disposed;

    internal ParentDragIcon(WaylandBackend backend, Surface guest)
    {
        _backend = backend;
        _guest = guest;
        Surface = backend.ParentCompositor.CreateSurface();
        _onCommitted = Mirror;
        _guest.Committed += _onCommitted;
    }

    internal WlSurface Surface { get; }

    internal void Mirror()
    {
        if (_disposed || _guest.Current.Buffer is not { } image || image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        if (image.Width != _width || image.Height != _height)
        {
            RebuildPool(image.Width, image.Height);
        }

        if (_mapping == 0 || !image.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return;
        }

        try
        {
            var rowBytes = _width * 4;
            for (var y = 0; y < _height; y++)
            {
                Buffer.MemoryCopy(
                    (void*)(view.Data + (y * view.Stride)),
                    (void*)(_mapping + (y * rowBytes)),
                    rowBytes,
                    Math.Min(rowBytes, view.Stride));
            }
        }
        finally
        {
            image.EndDataAccess();
        }

        Surface.Attach(_proxy!, 0, 0);
        Surface.DamageBuffer(0, 0, _width, _height);
        Surface.Commit();
        _backend.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _guest.Committed -= _onCommitted;
        WaylandBackend.DisposeParent(Surface);
        DestroyPool();
        _backend.Flush();
    }

    private void RebuildPool(int width, int height)
    {
        DestroyPool();
        _width = width;
        _height = height;
        _mappingSize = width * height * 4;
        _mappingFd = memfd_create("basin-wl-drag-icon", 1);
        if (_mappingFd < 0 || ftruncate(_mappingFd, _mappingSize) != 0)
        {
            _mappingFd = -1;
            return;
        }

        var map = mmap(null, (nuint)_mappingSize, ProtReadWrite, MapShared, _mappingFd, 0);
        if ((nint)map == -1)
        {
            close(_mappingFd);
            _mappingFd = -1;
            return;
        }

        _mapping = (nint)map;
        _pool = _backend.ParentShm.CreatePool(_mappingFd, _mappingSize);
        _proxy = _pool.CreateBuffer(0, width, height, width * 4, WlShm.Format.Argb8888);
    }

    private void DestroyPool()
    {
        WaylandBackend.DisposeParent(_proxy);
        _proxy = null;
        WaylandBackend.DisposeParent(_pool);
        _pool = null;
        if (_mapping != 0)
        {
            munmap((void*)_mapping, (nuint)_mappingSize);
            _mapping = 0;
        }

        if (_mappingFd >= 0)
        {
            close(_mappingFd);
            _mappingFd = -1;
        }
    }
}
