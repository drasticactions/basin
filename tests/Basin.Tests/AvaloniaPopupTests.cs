using System.Runtime.InteropServices;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.Shell.Xdg.Protocol;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class AvaloniaPopupTests
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

        public (WlSurface Surface, XdgSurface Xdg, XdgToplevel Toplevel) MapToplevel(int width, int height)
        {
            var surface = Client.Compositor.CreateSurface();
            var xdgSurface = Client.WmBase!.GetXdgSurface(surface);
            var toplevelProxy = xdgSurface.GetToplevel();
            uint serial = 0;
            xdgSurface.Configure += (_, e) => serial = e.Serial;
            surface.Commit();
            PumpUntil(() => serial != 0);
            xdgSurface.AckConfigure(serial);
            var buffer = Client.CreateBuffer(width, height, Fill.Solid(width, height, 0xFF336699));
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, width, height);
            surface.Commit();
            PumpUntil(() => Manager.Windows.Count > 0);
            return (surface, xdgSurface, toplevelProxy);
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
    public void A_popup_at_the_edge_slides_on_screen_and_an_outside_click_dismisses_it()
    {
        var harness = new Harness();
        var client = harness.Client;
        var (surface, xdgSurface, _) = harness.MapToplevel(400, 300);
        var window = harness.Manager.Windows.First();

        window.MouseMove(new global::Avalonia.Point(50, 40));
        window.MouseDown(new global::Avalonia.Point(50, 40), MouseButton.Left);
        window.MouseUp(new global::Avalonia.Point(50, 40), MouseButton.Left);
        var pointerProxy = client.Seat!.GetPointer();
        uint pressSerial = 0;
        pointerProxy.Button += (_, e) =>
        {
            if (e.State == WlPointer.ButtonState.Pressed)
            {
                pressSerial = e.Serial;
            }
        };
        window.MouseDown(new global::Avalonia.Point(50, 40), MouseButton.Left);
        window.MouseUp(new global::Avalonia.Point(50, 40), MouseButton.Left);
        harness.PumpUntil(() => pressSerial != 0);

        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(1800, 100);
        positioner.SetAnchorRect(350, 50, 10, 10);
        positioner.SetAnchor(XdgPositioner.Anchor.BottomRight);
        positioner.SetGravity(XdgPositioner.Gravity.BottomRight);
        positioner.SetConstraintAdjustment(
            XdgPositioner.ConstraintAdjustment.SlideX | XdgPositioner.ConstraintAdjustment.SlideY);

        var popupSurface = client.Compositor.CreateSurface();
        var popupXdg = client.WmBase.GetXdgSurface(popupSurface);
        var popupProxy = popupXdg.GetPopup(xdgSurface, positioner);
        var configured = default((int X, int Y, int Width, int Height));
        var done = false;
        uint popupSerial = 0;
        popupProxy.Configure += (_, e) => configured = (e.X, e.Y, e.Width, e.Height);
        popupProxy.PopupDone += (_, _) => done = true;
        popupXdg.Configure += (_, e) => popupSerial = e.Serial;
        popupProxy.Grab(client.Seat, pressSerial);
        popupSurface.Commit();
        harness.PumpUntil(() => popupSerial != 0);
        popupXdg.AckConfigure(popupSerial);
        var popupBuffer = client.CreateBuffer(1800, 100, Fill.Solid(1800, 100, 0xFF884422));
        popupSurface.Attach(popupBuffer.Proxy, 0, 0);
        popupSurface.Damage(0, 0, 1800, 100);
        popupSurface.Commit();
        harness.PumpUntil(() => harness.Manager.PopupWindows.Count == 1);

        harness.PumpUntil(() => configured.X + configured.Width <= 1920 && configured.Width == 1800);
        Assert.True(configured.X >= 0, $"popup slid off the left edge: {configured}");

        var popupWindow = harness.Manager.PopupWindows.First();
        harness.PumpUntil(() => popupWindow.IsOpen);

        harness.Manager.DeactivateNow(window.Id);
        harness.PumpUntil(() => done);

        popupProxy.Destroy();
        popupXdg.Destroy();
        popupSurface.Destroy();
        pointerProxy.Release();
        harness.PumpUntil(() => harness.Manager.PopupWindows.Count == 0);
        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [AvaloniaFact]
    public void A_subsurface_draws_in_the_parent_cell_with_no_extra_window()
    {
        var harness = new Harness();
        var client = harness.Client;
        var (surface, _, _) = harness.MapToplevel(200, 150);
        Assert.Single(harness.Manager.Windows);

        var child = client.Compositor.CreateSurface();
        var subsurface = client.Subcompositor.GetSubsurface(child, surface);
        subsurface.SetPosition(20, 20);
        var buffer = client.CreateBuffer(50, 40, Fill.Solid(50, 40, 0xFFAA3311));
        child.Attach(buffer.Proxy, 0, 0);
        child.Damage(0, 0, 50, 40);
        child.Commit();
        surface.Commit();
        harness.Pump();

        Assert.Single(harness.Manager.Windows);
        Assert.Empty(harness.Manager.PopupWindows);

        var boxes = new List<Basin.SurfaceBox>();
        harness.Host.Scene.CollectSurfaces(boxes);
        Assert.Equal(2, boxes.Count);

        subsurface.Destroy();
        child.Destroy();
        harness.Pump();
        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }
}
