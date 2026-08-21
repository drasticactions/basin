using System.Runtime.InteropServices;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ManagedShmTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc")]
    private static extern int close(int fd);

    private static CompositorTestHost CreateManagedHost(int width = 64, int height = 64, string policy = "1")
    {
        Environment.SetEnvironmentVariable("BASIN_MANAGED_SHM", policy);
        try
        {
            return new CompositorTestHost(width, height);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BASIN_MANAGED_SHM", null);
        }
    }

    private static int CreateMemfd(int size)
    {
        var fd = memfd_create("basin-managed-shm-test", 1);
        if (fd < 0 || ftruncate(fd, size) != 0)
        {
            throw new InvalidOperationException("memfd_create/ftruncate failed.");
        }

        return fd;
    }

    [Fact]
    public void Managed_shm_serves_client_buffers()
    {
        using var host = CreateManagedHost();
        Assert.Contains(0u, host.Client.ShmFormats);
        Assert.Contains(1u, host.Client.ShmFormats);

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xFF204060));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 16, 16);
        surface.Commit();
        host.PumpToServer();

        var serverBuffer = host.SurfaceScenes[0].Surface.Current.Buffer;
        Assert.NotNull(serverBuffer);
        Assert.Equal("ManagedShmBuffer", serverBuffer!.GetType().Name);

        Assert.True(serverBuffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        unsafe
        {
            Assert.Equal(0xFF204060u, *(uint*)view.Data);
        }

        serverBuffer.EndDataAccess();
        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_buffer_out_of_pool_bounds_is_a_protocol_error()
    {
        using var host = CreateManagedHost();
        var fd = CreateMemfd(4096);
        var pool = host.Client.Shm.CreatePool(fd, 4096);
        close(fd);
        _ = pool.CreateBuffer(0, 100, 100, 400, WlShm.Format.Xrgb8888);

        Assert.ThrowsAny<WaylandException>(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                host.PumpToServer();
                host.PumpToClient();
            }
        });
    }

    [Fact]
    public void An_unadvertised_format_is_a_protocol_error()
    {
        using var host = CreateManagedHost();
        var fd = CreateMemfd(4096);
        var pool = host.Client.Shm.CreatePool(fd, 4096);
        close(fd);
        _ = pool.CreateBuffer(0, 4, 4, 16, (WlShm.Format)0x56595559);

        Assert.ThrowsAny<WaylandException>(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                host.PumpToServer();
                host.PumpToClient();
            }
        });
    }

    [Fact]
    public void A_nonpositive_pool_size_is_a_protocol_error()
    {
        using var host = CreateManagedHost();
        var fd = CreateMemfd(4096);
        _ = host.Client.Shm.CreatePool(fd, 0);
        close(fd);

        Assert.ThrowsAny<WaylandException>(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                host.PumpToServer();
                host.PumpToClient();
            }
        });
    }

    [Fact]
    public void The_pool_cap_is_a_protocol_error()
    {
        using var host = CreateManagedHost();
        for (var i = 0; i < 65; i++)
        {
            var fd = CreateMemfd(4096);
            _ = host.Client.Shm.CreatePool(fd, 4096);
            close(fd);
        }

        Assert.ThrowsAny<WaylandException>(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                host.PumpToServer();
                host.PumpToClient();
            }
        });
    }

    [Fact]
    public void A_sparse_pool_the_size_a_terminal_ships_fits_the_default_cap()
    {
        using var host = CreateManagedHost();
        var first = CreateMemfd(512 * 1024 * 1024);
        _ = host.Client.Shm.CreatePool(first, 512 * 1024 * 1024);
        close(first);

        var second = CreateMemfd(512 * 1024 * 1024);
        var pool = host.Client.Shm.CreatePool(second, 512 * 1024 * 1024);
        close(second);

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = pool.CreateBuffer(0, 16, 16, 64, WlShm.Format.Xrgb8888);
        surface.Attach(buffer, 0, 0);
        surface.Damage(0, 0, 16, 16);
        surface.Commit();
        host.PumpToServer();

        Assert.NotNull(host.SurfaceScenes[0].Surface.Current.Buffer);
        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void The_compositor_outlives_an_erroring_client()
    {
        using var host = CreateManagedHost();
        var fd = CreateMemfd(4096);
        var pool = host.Client.Shm.CreatePool(fd, 4096);
        close(fd);
        _ = pool.CreateBuffer(0, 100, 100, 400, WlShm.Format.Xrgb8888);
        for (var i = 0; i < 5; i++)
        {
            host.PumpToServer();
        }

        var second = host.ConnectClient();
        var surface = second.Compositor.CreateSurface();
        var buffer = second.CreateBuffer(8, 8, Fill.Solid(8, 8, 0xFF008040));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var serverBuffer = host.SurfaceScenes[0].Surface.Current.Buffer;
        Assert.NotNull(serverBuffer);
        surface.Dispose();
        host.PumpToServer();
        host.DisconnectClient(second);
        host.PumpToServer();
    }

    [Fact]
    public void A_resize_keeps_a_locked_buffer_on_the_old_mapping()
    {
        using var host = CreateManagedHost();
        const int size = 16 * 16 * 4;
        var fd = CreateMemfd(2 * size);
        var pool = host.Client.Shm.CreatePool(fd, size);
        close(fd);
        var proxy = pool.CreateBuffer(0, 16, 16, 64, WlShm.Format.Xrgb8888);

        var surface = host.Client.Compositor.CreateSurface();
        surface.Attach(proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var serverBuffer = host.SurfaceScenes[0].Surface.Current.Buffer!;
        using var bufferLock = serverBuffer.Lock();
        Assert.True(serverBuffer.BeginDataAccess(BufferDataAccess.Read, out var before));

        pool.Resize(2 * size);
        host.PumpToServer();

        Assert.True(serverBuffer.BeginDataAccess(BufferDataAccess.Read, out var after));
        Assert.Equal(before.Data, after.Data);
        serverBuffer.EndDataAccess();
        serverBuffer.EndDataAccess();

        proxy.Dispose();
        pool.Dispose();
        surface.Dispose();
        host.PumpToServer();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int munmap(void* addr, nuint length);

    private sealed unsafe class GuardedClientPool : IDisposable
    {
        public readonly int Fd;
        public readonly int Size;
        public readonly WlShmPool Pool;
        public readonly WlBuffer Proxy;
        public readonly int Width;
        public readonly int Stride;
        private void* _map;

        public GuardedClientPool(WlShm shm, int width, int height)
        {
            Width = width;
            Stride = width * 4;
            Size = Stride * height;
            Fd = CreateMemfd(Size);
            _map = mmap(null, (nuint)Size, 3, 1, Fd, 0);
            if ((nint)_map == -1)
            {
                throw new InvalidOperationException("mmap failed.");
            }

            Pool = shm.CreatePool(Fd, Size);
            Proxy = Pool.CreateBuffer(0, width, height, Stride, WlShm.Format.Xrgb8888);
        }

        public void WritePixel(int x, int y, uint value) => *(uint*)((byte*)_map + y * Stride + x * 4) = value;

        public void FillAll(uint value)
        {
            for (var i = 0; i < Size / 4; i++)
            {
                ((uint*)_map)[i] = value;
            }
        }

        public void Truncate(int size) => ftruncate(Fd, size);

        public void Dispose()
        {
            if (_map != null)
            {
                munmap(_map, (nuint)Size);
                _map = null;
            }

            close(Fd);
            if (!Proxy.IsDestroyed)
            {
                Proxy.Dispose();
            }

            if (!Pool.IsDestroyed)
            {
                Pool.Dispose();
            }
        }
    }

    private static uint ServerPixel(IBuffer buffer, int x, int y)
    {
        if (!buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            throw new InvalidOperationException("BeginDataAccess failed.");
        }

        try
        {
            unsafe
            {
                return *(uint*)((byte*)view.Data + y * view.Stride + x * 4);
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    [Fact]
    public void Guarded_reads_come_from_the_shadow()
    {
        using var host = CreateManagedHost(policy: "guarded");
        using var pool = new GuardedClientPool(host.Client.Shm, 16, 16);
        pool.FillAll(0xFF111111);

        var surface = host.Client.Compositor.CreateSurface();
        surface.Attach(pool.Proxy, 0, 0);
        surface.Damage(0, 0, 16, 16);
        surface.Commit();
        host.PumpToServer();

        var serverBuffer = host.SurfaceScenes[0].Surface.Current.Buffer!;
        Assert.Equal(0xFF111111u, ServerPixel(serverBuffer, 3, 3));

        pool.WritePixel(3, 3, 0xFF222222);
        Assert.Equal(0xFF111111u, ServerPixel(serverBuffer, 3, 3));

        surface.Attach(pool.Proxy, 0, 0);
        surface.Damage(0, 0, 16, 16);
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(0xFF222222u, ServerPixel(serverBuffer, 3, 3));

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Guarded_copies_only_the_damaged_region()
    {
        using var host = CreateManagedHost(policy: "guarded");
        using var pool = new GuardedClientPool(host.Client.Shm, 16, 16);
        pool.FillAll(0xFF111111);

        var surface = host.Client.Compositor.CreateSurface();
        surface.Attach(pool.Proxy, 0, 0);
        surface.Damage(0, 0, 16, 16);
        surface.Commit();
        host.PumpToServer();

        pool.WritePixel(2, 2, 0xFF222222);
        pool.WritePixel(12, 12, 0xFF333333);
        surface.Attach(pool.Proxy, 0, 0);
        surface.DamageBuffer(2, 2, 1, 1);
        surface.Commit();
        host.PumpToServer();

        var serverBuffer = host.SurfaceScenes[0].Surface.Current.Buffer!;
        Assert.Equal(0xFF222222u, ServerPixel(serverBuffer, 2, 2));
        Assert.Equal(0xFF111111u, ServerPixel(serverBuffer, 12, 12));

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_truncated_pool_drops_the_frame_and_the_compositor_survives()
    {
        using var host = CreateManagedHost(policy: "guarded");
        using var pool = new GuardedClientPool(host.Client.Shm, 64, 64);
        pool.FillAll(0xFF111111);

        var surface = host.Client.Compositor.CreateSurface();
        surface.Attach(pool.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 64);
        surface.Commit();
        host.PumpToServer();

        var serverBuffer = host.SurfaceScenes[0].Surface.Current.Buffer!;
        Assert.Equal(0xFF111111u, ServerPixel(serverBuffer, 8, 60));

        // The EOF-containing page stays readable (zero-filled), so the pool
        // must shrink by whole pages for the copy to fault; the kernel copy
        // stops at the fault, so rows past it keep the previous frame.
        pool.Truncate(4096);
        surface.Attach(pool.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 64);
        surface.Commit();
        host.PumpToServer();

        Assert.Equal(0xFF111111u, ServerPixel(serverBuffer, 8, 60));

        var probe = host.Client.Compositor.CreateSurface();
        probe.Commit();
        host.PumpToClient();
        probe.Dispose();
        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void An_abrupt_disconnect_frees_pools_and_buffers()
    {
        using var host = CreateManagedHost();
        var second = host.ConnectClient();
        var surface = second.Compositor.CreateSurface();
        var buffer = second.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xFF804020));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var fd = CreateMemfd(4096);
        _ = second.Shm.CreatePool(fd, 4096);
        close(fd);
        host.PumpToServer();

        host.DisconnectClient(second);
        host.PumpToServer();
    }
}
