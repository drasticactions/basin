using System.Runtime.InteropServices;
using Wayland;

namespace Basin.Portal.Client;

internal sealed unsafe class ClientShm : IDisposable
{
    private const int ProtReadWrite = 3;
    private const int MapShared = 1;

    private readonly WlShm _shm;
    private int _fd = -1;
    private int _capacity;
    private void* _map;
    private WlShmPool? _pool;

    public ClientShm(WlShm shm) => _shm = shm;

    public WlBuffer? Buffer { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Stride { get; private set; }

    public nint Data => (nint)_map;

    public bool Resize(int width, int height)
    {
        var stride = width * 4;
        var size = stride * height;
        if (size <= 0)
        {
            return false;
        }

        if (size > _capacity)
        {
            Release();
            _fd = memfd_create("basin-portal-capture", 1);
            if (_fd < 0 || ftruncate(_fd, size) != 0)
            {
                return false;
            }

            _map = mmap(null, (nuint)size, ProtReadWrite, MapShared, _fd, 0);
            if ((nint)_map == -1)
            {
                _map = null;
                return false;
            }

            _capacity = size;
            _pool = _shm.CreatePool(_fd, size);
        }

        if (Buffer is { IsDestroyed: false } && Width == width && Height == height)
        {
            return true;
        }

        if (Buffer is { IsDestroyed: false } old)
        {
            old.Dispose();
        }

        Buffer = _pool!.CreateBuffer(0, width, height, stride, WlShm.Format.Xrgb8888);
        Width = width;
        Height = height;
        Stride = stride;
        return true;
    }

    public void Dispose() => Release();

    private void Release()
    {
        if (Buffer is { IsDestroyed: false } buffer)
        {
            buffer.Dispose();
        }

        Buffer = null;
        if (_pool is { IsDestroyed: false } pool)
        {
            pool.Destroy();
        }

        _pool = null;
        if (_map != null)
        {
            munmap(_map, (nuint)_capacity);
            _map = null;
        }

        if (_fd >= 0)
        {
            close(_fd);
            _fd = -1;
        }

        _capacity = 0;
    }

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
}
