using System.Runtime.InteropServices;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ImplicitFenceTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern int close(int fd);

    private static WlBuffer DmabufBuffer(CompositorTestHost host)
    {
        const int width = 8, height = 8, stride = width * 4;
        var fd = memfd_create("basin-fence-plane", 1);
        Assert.True(fd >= 0);
        Assert.Equal(0, ftruncate(fd, stride * height));
        var bufferParams = host.Client.Dmabuf!.CreateParams();
        bufferParams.Add(fd, 0, 0, stride, 0, 0);
        close(fd);
        WlBuffer? created = null;
        var failed = false;
        bufferParams.Created += (_, e) => created = e.Buffer;
        bufferParams.Failed += (_, _) => failed = true;
        bufferParams.Create(width, height, (uint)DrmFormat.Argb8888, 0);
        host.PumpUntil(() => created is not null || failed);
        Assert.False(failed);
        return created!;
    }

    private static unsafe (int Read, int Write) Pipe()
    {
        var fds = stackalloc int[2];
        Assert.Equal(0, pipe2(fds, 0x80000));
        return (fds[0], fds[1]);
    }

    [Fact]
    public void A_commit_waits_for_an_unsignaled_fence_and_applies_when_it_signals()
    {
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node, so no linux-dmabuf global");
        using var host = new CompositorTestHost();
        var buffer = DmabufBuffer(host);
        var surface = host.Client.Compositor.CreateSurface();
        surface.Commit();
        host.PumpToServer();
        var server = host.SurfaceScenes[0].Surface;

        var (read, writeEnd) = Pipe();
        ImplicitFence.Export = _ => (read, 0);
        try
        {
            surface.Attach(buffer, 0, 0);
            surface.Damage(0, 0, 8, 8);
            surface.Commit();
            host.PumpToServer();
            Assert.Null(server.Current.Buffer);
            Assert.True(server.HasParkedCommits);
            Assert.Equal(1, server.ParkedForFences);

            unsafe
            {
                byte signal = 1;
                Assert.Equal(1, write(writeEnd, &signal, 1));
            }

            host.PumpUntil(() => server.Current.Buffer is not null);
            Assert.False(server.HasParkedCommits);
            Assert.Equal(0, server.ParkedForFences);
        }
        finally
        {
            ImplicitFence.Export = null;
            close(writeEnd);
            buffer.Dispose();
            surface.Dispose();
            host.PumpToServer();
        }
    }

    [Fact]
    public void A_buffer_whose_fence_cannot_be_exported_applies_at_once()
    {
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node, so no linux-dmabuf global");
        using var host = new CompositorTestHost();
        var buffer = DmabufBuffer(host);
        var surface = host.Client.Compositor.CreateSurface();
        ImplicitFence.Export = _ => (-1, 25);
        try
        {
            surface.Attach(buffer, 0, 0);
            surface.Commit();
            host.PumpToServer();
            var server = host.SurfaceScenes[0].Surface;
            Assert.NotNull(server.Current.Buffer);
            Assert.False(server.HasParkedCommits);
        }
        finally
        {
            ImplicitFence.Export = null;
            buffer.Dispose();
            surface.Dispose();
            host.PumpToServer();
        }
    }

    [Fact]
    public void A_commit_with_an_explicit_acquire_point_does_not_wait_for_an_implicit_fence()
    {
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node, so no linux-dmabuf global");
        using var host = new CompositorTestHost();
        var buffer = DmabufBuffer(host);
        var surface = host.Client.Compositor.CreateSurface();
        surface.Commit();
        host.PumpToServer();
        var server = host.SurfaceScenes[0].Surface;
        var (read, writeEnd) = Pipe();
        ImplicitFence.Export = _ => (read, 0);
        server.CommitRequested += server.MarkExplicitAcquire;
        try
        {
            surface.Attach(buffer, 0, 0);
            surface.Commit();
            host.PumpToServer();
            Assert.NotNull(server.Current.Buffer);
            Assert.False(server.HasParkedCommits);
        }
        finally
        {
            server.CommitRequested -= server.MarkExplicitAcquire;
            ImplicitFence.Export = null;
            close(read);
            close(writeEnd);
            buffer.Dispose();
            surface.Dispose();
            host.PumpToServer();
        }
    }
}
