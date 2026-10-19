using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Basin.Hosted;
using Basin.Tests;
using Xunit;

namespace SdlComp.Tests;

internal sealed class SdlCompHarness : IDisposable
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

    public SdlCompHarness(string renderer, double scale = 1.0, int width = 320, int height = 240, int outputs = 1, bool toplevels = false)
    {
        Environment.SetEnvironmentVariable("BASIN_MUTE", "1");
        BasinCounters.Reset();
        Comp = Create(renderer, scale, width, height, outputs, toplevels);
        Comp.Start();
        Comp.FrameLoop.FrameInterval = TimeSpan.Zero;
        Step();

        int serverFd, clientFd;
        unsafe
        {
            var fds = stackalloc int[2];
            Assert.Equal(0, socketpair(1, 1 | 0x80000, 0, fds));
            serverFd = fds[0];
            clientFd = fds[1];
        }

        Host.Display.CreateClient(serverFd);
        Client = new ShmTestClient(clientFd);
        Client.BindGlobals(Pump);
    }

    public SdlComp Comp { get; }

    private static SdlComp Create(string renderer, double scale, int width, int height, int outputs, bool toplevels)
    {
        try
        {
            return new SdlComp(
                new SdlCompOptions
                {
                    Renderer = renderer,
                    Width = width,
                    Height = height,
                    Scale = scale,
                    VideoDriver = "offscreen",
                    Outputs = outputs,
                    Toplevels = toplevels,
                },
                BasinLog.For("SdlComp.Tests"));
        }
        catch (Exception error) when (renderer == Basin.Sdl.SdlRenderDevices.Vulkan &&
            error is InvalidOperationException or DllNotFoundException)
        {
            Assert.Skip($"no Vulkan lease on this box: {error.Message}");
            throw;
        }
    }

    public BasinCompositorHost Host => Comp.Host;

    public ShmTestClient Client { get; }

    public void Step(int maxWaitMs = 5) => Comp.FrameLoop.RunOnce(maxWaitMs);

    public void Pump()
    {
        Client.Display.Flush();
        Step();
        Step(0);
        while (Readable())
        {
            Client.Display.Dispatch();
        }

        Client.Display.DispatchPending();
    }

    public void PumpUntil(Func<bool> settled, string what)
    {
        for (var i = 0; i < 400 && !settled(); i++)
        {
            Pump();
        }

        Assert.True(settled(), what);
    }

    public void MapToplevel(int width, int height, Action<nint, int> fill)
    {
        var existing = Comp.Shell.Windows.Count;
        var surface = Client.Compositor.CreateSurface();
        var xdgSurface = Client.WmBase!.GetXdgSurface(surface);
        var toplevel = xdgSurface.GetToplevel();
        toplevel.SetTitle("sdl");
        toplevel.SetAppId("org.basin.sdl");
        var configured = false;
        xdgSurface.Configure += (_, e) =>
        {
            xdgSurface.AckConfigure(e.Serial);
            configured = true;
        };
        surface.Commit();
        PumpUntil(() => configured, "the compositor never configured the toplevel");
        var buffer = Client.CreateBuffer(width, height, fill);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, width, height);
        surface.Commit();
        PumpUntil(() => Comp.Shell.Windows.Count > existing, "the toplevel never became a managed window");
    }

    public byte[] Capture()
    {
        var window = Comp.Window;
        var before = window.Presented;
        window.RequestCapture();
        window.View.RequestRender?.Invoke();
        PumpUntil(() => window.Presented > before && window.LastCapture is not null, "no frame was captured");
        for (var i = 0; i < 4; i++)
        {
            Pump();
        }

        before = window.Presented;
        window.RequestCapture();
        window.View.RequestRender?.Invoke();
        PumpUntil(() => window.Presented > before, "no settled frame was captured");
        return window.LastCapture!;
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
        Step(0);
        Comp.Dispose();
        if (BasinCounters.Enabled)
        {
            Assert.Equal(0, BasinCounters.LiveObjects);
            Assert.Equal(0, BasinCounters.PendingFrees);
        }
    }
}
