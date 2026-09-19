using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Basin.Hosted;
using Basin.Shell.Nested;
using Basin.Shell.Xdg.Protocol;
using Xunit;

namespace Basin.Tests.Nested;

internal sealed class NestedShellHarness : IDisposable
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

    public NestedShellHarness(
        int width = 800,
        int height = 600,
        double scale = 1.0,
        ShellSettings? settings = null,
        PanelLayout? panel = null)
    {
        CompositorTestHost.SkipWithoutWaylandClient();
        BasinCounters.Reset();
        settings ??= new ShellSettings();
        Host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "basin-tests" });
        View = Host.CreateViewOutput(width, height, scale, NestedShell.OutputKey);
        Shell = new NestedShell(
            Host,
            View,
            settings,
            panel ?? new PanelLayout(24, 1, 2),
            KeyTable.Build(settings.Keys, []),
            action => action(),
            [TestTheme.Load]);

        int serverFd, clientFd;
        unsafe
        {
            var fds = stackalloc int[2];
            Assert.Equal(0, socketpair(1, 1, 0, fds));
            serverFd = fds[0];
            clientFd = fds[1];
        }

        Host.Display.CreateClient(serverFd);
        Client = new ShmTestClient(clientFd);
        Client.BindGlobals(Pump);
    }

    public BasinCompositorHost Host { get; }

    public BasinViewOutput View { get; }

    public NestedShell Shell { get; }

    public ShmTestClient Client { get; }

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
        string title = "basin",
        string appId = "org.basin.test",
        bool serverDecorated = false)
    {
        var existing = Shell.Windows.Count;
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
        if (serverDecorated && Client.DecorationManager is { } decorations)
        {
            var decoration = decorations.GetToplevelDecoration(toplevel);
            mapped.Decoration = decoration;
            var configuredMode = false;
            decoration.Configure += (_, _) => configuredMode = true;
            decoration.SetMode(ZxdgToplevelDecorationV1.Mode.ServerSide);
            PumpUntil(() => configuredMode, "the compositor never answered the decoration mode");
        }

        var buffer = Client.CreateBuffer(width, height, Fill(width, height, 0xFF3366AA));
        mapped.Buffer = buffer;
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, width, height);
        surface.Commit();
        PumpUntil(() => Shell.Windows.Count > existing, "the mapped toplevel never became a managed window");
        return mapped;
    }

    public void Commit(HarnessToplevel toplevel, int width, int height)
    {
        var buffer = Client.CreateBuffer(width, height, Fill(width, height, 0xFF3366AA));
        toplevel.Surface.Attach(buffer.Proxy, 0, 0);
        toplevel.Surface.Damage(0, 0, width, height);
        toplevel.Surface.Commit();
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

    public static void Move(NestedShell shell, double x, double y) =>
        shell.HandleInput(new BasinViewInput(BasinViewInputKind.PointerMotion, 0, x, y, 0, false, 0, 0, 0));

    public static void Button(NestedShell shell, uint code, bool pressed) =>
        shell.HandleInput(new BasinViewInput(BasinViewInputKind.PointerButton, 0, 0, 0, code, pressed, 0, 0, 0));

    public static void Key(NestedShell shell, uint code, bool pressed) =>
        shell.HandleInput(new BasinViewInput(BasinViewInputKind.Key, 0, 0, 0, code, pressed, 0, 0, 0));

    public static void Touch(NestedShell shell, BasinViewInputKind kind, int id, double x, double y, uint time = 0) =>
        shell.HandleInput(new BasinViewInput(kind, time, x, y, 0, false, 0, 0, id));

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
        Shell.Dispose();
        View.Dispose();
        Host.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }
}
