using System.Runtime.InteropServices;
using Basin.Cli;
using BlurClient.Protocol;
using Microsoft.Extensions.Logging;
using Wayland;

namespace BlurClient;

internal sealed unsafe class ShmBuffer : IDisposable
{
    private const int ProtReadWrite = 3;
    private const int MapShared = 1;

    private readonly int _fd;
    private readonly int _size;
    private void* _map;

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

    public ShmBuffer(WlShm shm, int width, int height)
    {
        Width = width;
        Height = height;
        Stride = width * 4;
        _size = Stride * height;

        _fd = memfd_create("blur-client-shm", 1 );
        if (_fd < 0 || ftruncate(_fd, _size) != 0)
        {
            throw new InvalidOperationException("memfd_create/ftruncate failed");
        }

        _map = mmap(null, (nuint)_size, ProtReadWrite, MapShared, _fd, 0);
        if ((nint)_map == -1)
        {
            throw new InvalidOperationException("mmap failed");
        }

        var pool = shm.CreatePool(_fd, _size);
        Proxy = pool.CreateBuffer(0, width, height, Stride, WlShm.Format.Argb8888);
        pool.Dispose();
    }

    public WlBuffer Proxy { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public nint Data => (nint)_map;

    public void Dispose()
    {
        if (_map != null)
        {
            munmap(_map, (nuint)_size);
            _map = null;
            close(_fd);
            if (!Proxy.IsDestroyed)
            {
                Proxy.Dispose();
            }
        }
    }
}
