using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Basin.Render.Skia;
using Xunit;

namespace Basin.Tests;

public sealed class GlRenderTargetTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    public static TheoryData<string> GlRows => new("gl", "skia-gl");

    [Theory]
    [MemberData(nameof(GlRows))]
    public void A_dmabuf_that_EGL_rejects_leaves_nothing_alive(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        LeakTracking.Require();
        using var host = new CompositorTestHost(renderer: renderer);
        var device = host.Renderer switch
        {
            GlRenderer gl => gl.Device,
            SkiaGlRenderer skia => skia.Device,
            _ => throw new InvalidOperationException(renderer),
        };

        var fd = memfd_create("basin-not-a-dmabuf", 1);
        Assert.True(fd >= 0);
        Assert.Equal(0, ftruncate(fd, 4096));
        var attributes = new DmabufAttributes
        {
            Width = 16,
            Height = 16,
            Format = DrmFormat.Argb8888,
            Modifier = 0,
            PlaneCount = 1,
        };
        attributes.Fds[0] = fd;
        attributes.Offsets[0] = 1u << 20;
        attributes.Strides[0] = 64;
        var buffer = new DmabufBuffer(attributes);
        var live = BasinCounters.LiveObjects;
        for (var i = 0; i < 5; i++)
        {
            Assert.Throws<InvalidOperationException>(() => GlRenderTarget.Create(device, buffer));
        }

        Assert.Equal(live, BasinCounters.LiveObjects);
        buffer.Destroy();
    }
}
