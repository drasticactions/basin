using System.Runtime.InteropServices;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.Scene;
using SkiaSharp;
using Xunit;

namespace Basin.Tests;

public sealed class BasinAvaloniaHostTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int socketpair(int domain, int type, int protocol, int* fds);

    [DllImport("libc")]
    private static extern unsafe int poll(PollFd* fds, nuint count, int timeoutMs);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short REvents;
    }

    private const int AF_UNIX = 1;
    private const int SOCK_STREAM = 1;

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            CompositorTestHost.SkipWithoutWaylandClient();
            BasinCounters.Reset();
            Host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "waylonia-tests" });
            Host.Services.Find<CompositorGlobal>();
            var compositor = Host.Services.Require<CompositorGlobal>();
            compositor.SurfaceCreated += surface =>
            {
                var sceneSurface = new SceneSurface(Host.Scene.Root, surface);
                Surfaces.Add(sceneSurface);
                sceneSurface.Destroyed += () => Surfaces.Remove(sceneSurface);
            };

            View = Host.CreateViewOutput(160, 120);

            int serverFd, clientFd;
            unsafe
            {
                var fds = stackalloc int[2];
                Assert.Equal(0, socketpair(AF_UNIX, SOCK_STREAM, 0, fds));
                serverFd = fds[0];
                clientFd = fds[1];
            }

            Host.Display.CreateClient(serverFd);
            Client = new ShmTestClient(clientFd);
            Client.BindGlobals(PumpToClient);
        }

        public BasinCompositorHost Host { get; }

        public BasinViewOutput View { get; }

        public ShmTestClient Client { get; }

        public List<SceneSurface> Surfaces { get; } = [];

        public void PumpToServer()
        {
            Client.Display.Flush();
            Host.Loop.Dispatch(0);
        }

        public void PumpToClient()
        {
            PumpToServer();
            Host.Display.FlushClients();
            while (Readable())
            {
                Client.Display.Dispatch();
            }

            Client.Display.DispatchPending();
        }

        private bool Readable()
        {
            unsafe
            {
                var pollFd = new PollFd { Fd = Client.Display.Fd, Events = 1 };
                return poll(&pollFd, 1, 0) > 0 && (pollFd.REvents & 1) != 0;
            }
        }

        public void Dispose()
        {
            Client.Dispose();
            Host.Loop.Dispatch(0);
            Host.Loop.Dispatch(0);
            View.Dispose();
            Host.Dispose();
        }
    }

    [Fact]
    public void A_client_buffer_reaches_the_leased_canvas()
    {
        using var harness = new Harness();
        var surface = harness.Client.Compositor.CreateSurface();
        var buffer = harness.Client.CreateBuffer(64, 48, Fill.Solid(64, 48, 0xFF2060A0));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        harness.PumpToServer();
        Assert.Single(harness.Surfaces);

        using var canvas = SKSurface.Create(new SKImageInfo(160, 120, SKColorType.Bgra8888, SKAlphaType.Premul));
        Assert.True(harness.Host.Renderer.BindFrame(canvas.Canvas, context: null));
        try
        {
            harness.Host.Session.BeginFrame();
            try
            {
                Assert.True(harness.Host.Session.CommitOutput(
                    harness.View.SceneOutput, harness.Host.Renderer, harness.View.Target, 0));
                harness.Host.Scene.SendFrameDone(1);
            }
            finally
            {
                harness.Host.Session.EndFrame();
            }
        }
        finally
        {
            harness.Host.Renderer.UnbindFrame();
        }

        using var snapshot = canvas.Snapshot();
        using var pixels = new SKBitmap(160, 120, SKColorType.Bgra8888, SKAlphaType.Premul);
        Assert.True(snapshot.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0));
        Assert.Equal(0xFF2060A0, (uint)pixels.GetPixel(10, 10));
    }

    [Fact]
    public void Without_an_egl_lease_the_dmabuf_global_is_withheld()
    {
        using var harness = new Harness();
        Assert.Null(harness.Host.Dmabuf);
        Assert.Equal(0, harness.Host.Renderer.DmabufTextureFormats.Count);
        Assert.DoesNotContain(harness.Client.Globals, g => g.Interface == "zwp_linux_dmabuf_v1");
    }

    [Fact]
    public void Teardown_in_the_documented_order_leaves_nothing_live()
    {
        LeakTracking.Require();
        var harness = new Harness();
        var surface = harness.Client.Compositor.CreateSurface();
        var buffer = harness.Client.CreateBuffer(64, 48, Fill.Solid(64, 48, 0xFF2060A0));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        harness.PumpToServer();

        harness.Dispose();
        Assert.Equal(0, BasinCounters.LiveObjects);
        Assert.Equal(0, BasinCounters.PendingFrees);
    }
}
