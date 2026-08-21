using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Basin.Protocol;
using Wayland;

namespace Basin.Backend.Wayland;

internal sealed unsafe class ParentCursorSurface : IDisposable
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
    private readonly WlPointer _pointer;
    private readonly WlSurface _surface;
    private readonly WpViewport? _viewport;

    private WlShmPool? _pool;
    private WlBuffer? _proxy;
    private nint _mapping;
    private int _mappingSize;
    private int _mappingFd = -1;
    private int _width;
    private int _height;

    private uint _enterSerial;
    private bool _entered;
    private bool _hasImage;
    private bool _hidden;
    private int _hotspotX;
    private int _hotspotY;
    private double _scale = 1;

    internal ParentCursorSurface(WaylandBackend backend, WlPointer pointer)
    {
        _backend = backend;
        _pointer = pointer;
        _surface = backend.ParentCompositor.CreateSurface();
        _viewport = backend.ParentViewporter?.GetViewport(_surface);
    }

    internal void NotifyEnter(uint serial)
    {
        _enterSerial = serial;
        _entered = true;
        if (_hidden)
        {
            Apply(null);
        }
        else if (_hasImage)
        {
            Apply(_surface);
        }
    }

    internal void NotifyLeave() => _entered = false;

    internal bool Show(IBuffer image, int hotspotX, int hotspotY, double scale)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            return false;
        }

        if (!Upload(image))
        {
            return false;
        }

        _hotspotX = hotspotX;
        _hotspotY = hotspotY;
        _scale = scale <= 0 ? 1 : scale;
        _hasImage = true;
        _hidden = false;

        if (_viewport is not null)
        {
            _viewport.SetDestination(
                Math.Max(1, (int)Math.Round(_width / _scale)),
                Math.Max(1, (int)Math.Round(_height / _scale)));
        }
        else if (_scale == Math.Floor(_scale))
        {
            _surface.SetBufferScale((int)_scale);
        }

        _surface.Attach(_proxy!, 0, 0);
        _surface.DamageBuffer(0, 0, _width, _height);
        _surface.Commit();
        Apply(_surface);
        return true;
    }

    internal void Hide()
    {
        _hidden = true;
        Apply(null);
    }

    public void Dispose()
    {
        _proxy?.Dispose();
        _viewport?.Dispose();
        _surface.Dispose();
        DestroyPool();
    }

    private void Apply(WlSurface? surface)
    {
        if (!_entered)
        {
            return;
        }

        _pointer.SetCursor(
            _enterSerial,
            surface!,
            (int)Math.Round(_hotspotX / _scale),
            (int)Math.Round(_hotspotY / _scale));
    }

    private bool Upload(IBuffer image)
    {
        if (image.Width != _width || image.Height != _height)
        {
            RebuildPool(image.Width, image.Height);
        }

        if (_mapping == 0 || !image.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return false;
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

        return true;
    }

    private void RebuildPool(int width, int height)
    {
        DestroyPool();
        _width = width;
        _height = height;
        _mappingSize = width * height * 4;
        _mappingFd = memfd_create("basin-wl-cursor", 1 );
        if (_mappingFd < 0 || ftruncate(_mappingFd, _mappingSize) != 0)
        {
            throw new InvalidOperationException("cursor shm creation failed");
        }

        var map = mmap(null, (nuint)_mappingSize, ProtReadWrite, MapShared, _mappingFd, 0);
        if ((nint)map == -1)
        {
            throw new InvalidOperationException("cursor shm mmap failed");
        }

        _mapping = (nint)map;
        _pool = _backend.ParentShm.CreatePool(_mappingFd, _mappingSize);
        _proxy = _pool.CreateBuffer(0, width, height, width * 4, WlShm.Format.Argb8888);
    }

    private void DestroyPool()
    {
        _proxy?.Dispose();
        _proxy = null;
        _pool?.Dispose();
        _pool = null;
        if (_mapping != 0)
        {
            munmap((void*)_mapping, (nuint)_mappingSize);
            _mapping = 0;
            close(_mappingFd);
            _mappingFd = -1;
        }
    }
}
