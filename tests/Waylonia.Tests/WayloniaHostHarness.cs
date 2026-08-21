using System.Runtime.InteropServices;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.Shell.Xdg.Protocol;
using Basin.Tests;
using Wayland;
using Xunit;

namespace Waylonia.Tests;

internal sealed class WayloniaHostHarness : IDisposable
{
    private const int AfUnix = 1;
    private const int SockStream = 1;

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

    public static bool HasWaylandClient { get; } =
        OperatingSystem.IsLinux() &&
        (NativeLibrary.TryLoad("wayland-client", out _) ||
            NativeLibrary.TryLoad("libwayland-client.so.0", out _));

    public static void SkipWithoutWaylandClient() =>
        Assert.SkipWhen(
            !HasWaylandClient,
            "this host has no libwayland client, and the host-window tests drive the compositor with one");

    public WayloniaHostHarness()
    {
        SkipWithoutWaylandClient();
        BasinCounters.Reset();
        Host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "waylonia-tests" });
        Windows = new ToplevelWindows(Host, action => action(), requestFrame: () => FrameRequests++);

        int serverFd, clientFd;
        unsafe
        {
            var fds = stackalloc int[2];
            Assert.Equal(0, socketpair(AfUnix, SockStream, 0, fds));
            serverFd = fds[0];
            clientFd = fds[1];
        }

        Host.Display.CreateClient(serverFd);
        Client = new ShmTestClient(clientFd);
        Client.BindGlobals(Pump);
    }

    private ZwlrLayerShellV1? _layerShell;

    public BasinCompositorHost Host { get; }

    public ToplevelWindows Windows { get; }

    public ShmTestClient Client { get; }

    public int FrameRequests { get; private set; }

    public void Pump()
    {
        Client.Display.Flush();
        Host.Loop.Dispatch(0);
        Host.Display.FlushClients();
        while (Readable())
        {
            Client.Display.Dispatch();
        }

        Client.Display.DispatchPending();
        Dispatcher.UIThread.RunJobs();
    }

    public void PumpInput()
    {
        Pump();
        Host.Session.BeginFrame();
        Host.Session.EndFrame();
        Pump();
    }

    public void PumpUntil(Func<bool> settled, string what)
    {
        for (var i = 0; i < 200 && !settled(); i++)
        {
            Pump();
        }

        Assert.True(settled(), what);
    }

    public HarnessToplevel MapToplevel(
        int width = 120,
        int height = 90,
        string title = "waylonia",
        string appId = "waylonia.test")
    {
        var existing = Windows.Windows.Count;
        var surface = Client.Compositor.CreateSurface();
        var xdgSurface = Client.WmBase!.GetXdgSurface(surface);
        var toplevel = xdgSurface.GetToplevel();
        toplevel.SetTitle(title);
        toplevel.SetAppId(appId);

        var mapped = new HarnessToplevel(surface, xdgSurface, toplevel);
        xdgSurface.Configure += (_, e) =>
        {
            xdgSurface.AckConfigure(e.Serial);
            mapped.Configured = true;
        };
        toplevel.Configure += (_, e) =>
        {
            mapped.ConfiguredWidth = e.Width;
            mapped.ConfiguredHeight = e.Height;
        };
        toplevel.Close += (_, _) => mapped.CloseReceived = true;

        surface.Commit();
        PumpUntil(() => mapped.Configured, "the compositor never configured the toplevel");

        var buffer = Client.CreateBuffer(width, height, Fill(width, height, 0xFF3366AA));
        mapped.Buffer = buffer;
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, width, height);
        surface.Commit();
        PumpUntil(() => Windows.Windows.Count > existing, "the mapped toplevel never became a host window");
        return mapped;
    }

    public HarnessLayer MapLayer(
        int width = 200,
        int height = 40,
        ZwlrLayerShellV1.Layer layer = ZwlrLayerShellV1.Layer.Top,
        string scope = "panel",
        ZwlrLayerSurfaceV1.Anchor anchor = 0,
        ZwlrLayerSurfaceV1.KeyboardInteractivity keyboard = ZwlrLayerSurfaceV1.KeyboardInteractivity.None)
    {
        var existing = Windows.LayerWindows.Count;
        _layerShell ??= Client.BindAt<ZwlrLayerShellV1>("zwlr_layer_shell_v1", 4);
        var surface = Client.Compositor.CreateSurface();
        var layerSurface = _layerShell.GetLayerSurface(surface, null, layer, scope);
        layerSurface.SetSize((uint)width, (uint)height);
        layerSurface.SetAnchor(anchor);
        layerSurface.SetKeyboardInteractivity(keyboard);

        var mapped = new HarnessLayer(surface, layerSurface);
        layerSurface.Configure += (_, e) =>
        {
            layerSurface.AckConfigure(e.Serial);
            mapped.Configured = true;
        };
        layerSurface.Closed += (_, _) => mapped.CloseReceived = true;

        mapped.Buffer = Client.CreateBuffer(width, height, Fill(width, height, 0xFF22AA55));
        ShowLayer(mapped);
        PumpUntil(
            () => Windows.LayerWindows.Count > existing,
            "the mapped layer surface never became a host window");
        return mapped;
    }

    public void ShowLayer(HarnessLayer mapped)
    {
        mapped.Configured = false;
        mapped.Surface.Commit();
        PumpUntil(() => mapped.Configured, "the compositor never configured the layer surface");

        var buffer = mapped.Buffer!;
        mapped.Surface.Attach(buffer.Proxy, 0, 0);
        mapped.Surface.Damage(0, 0, buffer.Width, buffer.Height);
        mapped.Surface.Commit();
    }

    public void SetInputRegion(HarnessLayer mapped, params (int X, int Y, int Width, int Height)[] rects)
    {
        var region = Client.Compositor.CreateRegion();
        foreach (var rect in rects)
        {
            region.Add(rect.X, rect.Y, rect.Width, rect.Height);
        }

        mapped.Surface.SetInputRegion(region);
        mapped.Surface.Commit();
        region.Destroy();
        Pump();
    }

    public void HideLayer(HarnessLayer mapped)
    {
        mapped.Surface.Attach(null, 0, 0);
        mapped.Surface.Commit();
    }

    public static Action<nint, int> Fill(int width, int height, uint color) => (data, stride) =>
    {
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)((byte*)data + (y * stride));
                for (var x = 0; x < width; x++)
                {
                    row[x] = color;
                }
            }
        }
    };

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
        Dispatcher.UIThread.RunJobs();
        Windows.Dispose();
        Host.Dispose();
        Dispatcher.UIThread.RunJobs();
    }
}

internal sealed class HarnessToplevel(WlSurface surface, XdgSurface xdgSurface, XdgToplevel toplevel)
{
    public WlSurface Surface { get; } = surface;

    public XdgSurface XdgSurface { get; } = xdgSurface;

    public XdgToplevel Toplevel { get; } = toplevel;

    public ClientShmBuffer? Buffer { get; set; }

    public bool Configured { get; set; }

    public bool CloseReceived { get; set; }

    public int ConfiguredWidth { get; set; }

    public int ConfiguredHeight { get; set; }

    public void Destroy()
    {
        Toplevel.Dispose();
        XdgSurface.Dispose();
        Surface.Dispose();
    }
}

internal sealed class HarnessLayer(WlSurface surface, ZwlrLayerSurfaceV1 layerSurface)
{
    public WlSurface Surface { get; } = surface;

    public ZwlrLayerSurfaceV1 LayerSurface { get; } = layerSurface;

    public ClientShmBuffer? Buffer { get; set; }

    public bool Configured { get; set; }

    public bool CloseReceived { get; set; }

    public void Destroy()
    {
        LayerSurface.Dispose();
        Surface.Dispose();
    }
}
