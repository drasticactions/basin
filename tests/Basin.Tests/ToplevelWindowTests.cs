using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class ToplevelWindowTests
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

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            CompositorTestHost.SkipWithoutWaylandClient();
            BasinCounters.Reset();
            Host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "waylonia-tests" });
            Manager = new ToplevelWindows(Host, action => action());

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

        public ToplevelWindows Manager { get; }

        public ShmTestClient Client { get; }

        public void Pump()
        {
            Client.Display.Flush();
            Host.Session.BeginFrame();
            Host.Session.EndFrame();
            while (Readable())
            {
                Client.Display.Dispatch();
            }

            Client.Display.DispatchPending();
            Dispatcher.UIThread.RunJobs();
        }

        public void PumpUntil(Func<bool> condition, int rounds = 50)
        {
            for (var i = 0; i < rounds && !condition(); i++)
            {
                Pump();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(condition(), "condition not reached while pumping");
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
            Dispatcher.UIThread.RunJobs();
            Manager.Dispose();
            Host.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void A_toplevel_lives_as_a_host_window_from_map_to_close()
    {
        var harness = new Harness();
        var client = harness.Client;
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevelProxy = xdgSurface.GetToplevel();
        toplevelProxy.SetTitle("first title");
        toplevelProxy.SetAppId("org.example.app");
        var decoration = client.DecorationManager!.GetToplevelDecoration(toplevelProxy);
        decoration.SetMode(Basin.Shell.Xdg.Protocol.ZxdgToplevelDecorationV1.Mode.ServerSide);

        var configuredWidth = 0;
        var configuredHeight = 0;
        var states = new List<uint>();
        var closed = false;
        toplevelProxy.Configure += (_, e) =>
        {
            configuredWidth = e.Width;
            configuredHeight = e.Height;
            states.Clear();
            for (var offset = 0; offset + 4 <= e.States.Length; offset += 4)
            {
                states.Add(BitConverter.ToUInt32(e.States, offset));
            }
        };
        toplevelProxy.Close += (_, _) => closed = true;
        uint pendingSerial = 0;
        xdgSurface.Configure += (_, e) => pendingSerial = e.Serial;

        surface.Commit();
        harness.PumpUntil(() => pendingSerial != 0);
        xdgSurface.AckConfigure(pendingSerial);

        var buffer = client.CreateBuffer(200, 150, Fill.Solid(200, 150, 0xFF336699));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 200, 150);
        surface.Commit();
        harness.PumpUntil(() => harness.Manager.Windows.Count == 1);

        var window = harness.Manager.Windows.First();
        Assert.Equal("first title", window.Title);
        Assert.Equal(200, (int)window.Width);
        Assert.Equal(150, (int)window.Height);

        toplevelProxy.SetTitle("second title");
        harness.PumpUntil(() => window.Title == "second title");

        toplevelProxy.SetMaximized();
        harness.PumpUntil(() => states.Contains(1u));
        Assert.Equal(WindowState.Maximized, window.WindowState);

        toplevelProxy.UnsetMaximized();
        harness.PumpUntil(() => !states.Contains(1u));
        Assert.Equal(WindowState.Normal, window.WindowState);

        window.Width = 320;
        window.Height = 240;
        harness.PumpUntil(() => configuredWidth == 320 && configuredHeight == 240);

        xdgSurface.AckConfigure(pendingSerial);
        var resized = client.CreateBuffer(320, 240, Fill.Solid(320, 240, 0xFF669933));
        surface.Attach(resized.Proxy, 0, 0);
        surface.Damage(0, 0, 320, 240);
        surface.Commit();
        harness.Pump();

        window.Close();
        harness.PumpUntil(() => closed);
        Assert.Single(harness.Manager.Windows);

        decoration.Destroy();
        toplevelProxy.Destroy();
        xdgSurface.Destroy();
        surface.Destroy();
        harness.PumpUntil(() => harness.Manager.Windows.Count == 0);

        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [AvaloniaFact]
    public void A_client_move_or_resize_request_becomes_a_host_gesture()
    {
        var harness = new Harness();
        var client = harness.Client;
        Basin.Shell.Xdg.XdgToplevelWindow? serverToplevel = null;
        harness.Host.Shell.NewToplevel += t => serverToplevel = t;
        var moveSeen = false;
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevelProxy = xdgSurface.GetToplevel();
        var configuredWidth = 0;
        var configuredHeight = 0;
        var states = new List<uint>();
        toplevelProxy.Configure += (_, e) =>
        {
            configuredWidth = e.Width;
            configuredHeight = e.Height;
            states.Clear();
            for (var offset = 0; offset + 4 <= e.States.Length; offset += 4)
            {
                states.Add(BitConverter.ToUInt32(e.States, offset));
            }
        };
        uint pendingSerial = 0;
        xdgSurface.Configure += (_, e) => pendingSerial = e.Serial;
        surface.Commit();
        harness.PumpUntil(() => pendingSerial != 0);
        xdgSurface.AckConfigure(pendingSerial);
        var buffer = client.CreateBuffer(200, 150, Fill.Solid(200, 150, 0xFF336699));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 200, 150);
        surface.Commit();
        harness.PumpUntil(() => harness.Manager.Windows.Count == 1);
        var window = harness.Manager.Windows.First();

        var pointerProxy = client.Seat!.GetPointer();
        uint pressSerial = 0;
        pointerProxy.Button += (_, e) =>
        {
            if (e.State == Wayland.WlPointer.ButtonState.Pressed)
            {
                pressSerial = e.Serial;
            }
        };
        harness.Pump();

        var releaseCount = 0;
        var leaveAfterRelease = false;
        pointerProxy.Button += (_, e) =>
        {
            if (e.State == Wayland.WlPointer.ButtonState.Released)
            {
                releaseCount++;
            }
        };
        pointerProxy.Leave += (_, _) => leaveAfterRelease = releaseCount > 0;

        window.MouseMove(new global::Avalonia.Point(50, 40));
        window.MouseDown(new global::Avalonia.Point(50, 40), global::Avalonia.Input.MouseButton.Left);
        harness.PumpUntil(() => pressSerial != 0);
        serverToplevel!.MoveRequested += _ => moveSeen = true;
        toplevelProxy.Move(client.Seat, pressSerial);
        client.Display.Flush();
        harness.PumpUntil(() => moveSeen && releaseCount > 0 && leaveAfterRelease);
        window.MouseUp(new global::Avalonia.Point(80, 70), global::Avalonia.Input.MouseButton.Left);
        harness.Pump();
        Assert.Equal(1, releaseCount);

        pressSerial = 0;
        window.MouseMove(new global::Avalonia.Point(60, 50));
        window.MouseDown(new global::Avalonia.Point(60, 50), global::Avalonia.Input.MouseButton.Left);
        harness.PumpUntil(() => pressSerial != 0);
        toplevelProxy.Resize(client.Seat, pressSerial, Basin.Shell.Xdg.Protocol.XdgToplevel.ResizeEdge.BottomRight);
        client.Display.Flush();
        harness.PumpUntil(() => states.Contains(3u) && releaseCount == 2);
        window.Width = 240;
        window.Height = 180;
        harness.PumpUntil(() => configuredWidth == 240 && configuredHeight == 180);
        Assert.Contains(3u, states);
        window.MouseUp(new global::Avalonia.Point(100, 80), global::Avalonia.Input.MouseButton.Left);
        harness.PumpUntil(() => !states.Contains(3u));
        Assert.Equal(2, releaseCount);

        pointerProxy.Release();
        toplevelProxy.Destroy();
        xdgSurface.Destroy();
        surface.Destroy();
        harness.PumpUntil(() => harness.Manager.Windows.Count == 0);
        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [AvaloniaFact]
    public void Two_toplevels_never_share_scene_space()
    {
        var harness = new Harness();
        var client = harness.Client;

        (Wayland.WlSurface Surface, Basin.Shell.Xdg.Protocol.XdgSurface Xdg, Basin.Shell.Xdg.Protocol.XdgToplevel Toplevel) Map(uint color)
        {
            var surface = client.Compositor.CreateSurface();
            var xdgSurface = client.WmBase!.GetXdgSurface(surface);
            var toplevelProxy = xdgSurface.GetToplevel();
            uint serial = 0;
            xdgSurface.Configure += (_, e) => serial = e.Serial;
            surface.Commit();
            harness.PumpUntil(() => serial != 0);
            xdgSurface.AckConfigure(serial);
            var buffer = client.CreateBuffer(64, 48, Fill.Solid(64, 48, color));
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, 64, 48);
            surface.Commit();
            harness.Pump();
            return (surface, xdgSurface, toplevelProxy);
        }

        var first = Map(0xFF112233);
        var second = Map(0xFF445566);
        harness.PumpUntil(() => harness.Manager.Windows.Count == 2);

        var boxes = new List<Basin.Scene.SceneSurfaceBox>();
        harness.Host.Scene.CollectSurfaces(boxes);
        Assert.Equal(2, boxes.Count);
        var a = boxes[0].Box;
        var b = boxes[1].Box;
        Assert.True(a.Intersect(b).IsEmpty, $"cells overlap: {a} and {b}");

        harness.Dispose();
    }
}
