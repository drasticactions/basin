using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Diagnostics;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class AvaloniaDragTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int socketpair(int domain, int type, int protocol, int* fds);

    [DllImport("libc")]
    private static extern unsafe int poll(PollFd* fds, nuint count, int timeoutMs);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc")]
    private static extern unsafe nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern int close(int fd);

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
            Drag = new HostDrag(Host);
            Manager.AttachDrag(Drag);

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

        public HostDrag Drag { get; }

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

        public void PumpUntil(Func<bool> condition, int rounds = 100)
        {
            for (var i = 0; i < rounds && !condition(); i++)
            {
                Pump();
                Thread.Sleep(5);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(condition(), "condition not reached while pumping");
        }

        public (WlSurface Proxy, Surface Server) MapToplevel()
        {
            var surface = Client.Compositor.CreateSurface();
            var xdgSurface = Client.WmBase!.GetXdgSurface(surface);
            _ = xdgSurface.GetToplevel();
            uint serial = 0;
            xdgSurface.Configure += (_, e) => serial = e.Serial;
            surface.Commit();
            PumpUntil(() => serial != 0);
            xdgSurface.AckConfigure(serial);
            var buffer = Client.CreateBuffer(200, 150, Fill.Solid(200, 150, 0xFF336699));
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, 200, 150);
            surface.Commit();
            PumpUntil(() => Manager.Windows.Count == 1);
            return (surface, System.Linq.Enumerable.First(Host.Services.Require<CompositorGlobal>().Surfaces));
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
            Drag.Dispose();
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
    public void A_host_drag_enters_moves_and_drops_into_the_client()
    {
        var harness = new Harness();
        var client = harness.Client;
        _ = harness.MapToplevel();
        var window = harness.Manager.Windows.First();

        var device = client.DataDeviceManager!.GetDataDevice(client.Seat!);
        WlDataOffer? offer = null;
        var mimes = new List<string>();
        var entered = false;
        var dropped = false;
        device.DataOffer += (_, e) =>
        {
            offer = e.Id;
            offer.Offer += (_, o) => mimes.Add(o.MimeType);
        };
        device.Enter += (_, _) => entered = true;
        device.Drop += (_, _) => dropped = true;
        harness.Pump();

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText("dragged from the host"));
        window.DragDrop(new global::Avalonia.Point(40, 30), RawDragEventType.DragEnter, transfer, DragDropEffects.Copy);
        harness.PumpUntil(() => entered && offer is not null && mimes.Count > 0);

        offer!.Accept(0, mimes[0]);
        offer.SetActions(WlDataDeviceManager.DndAction.Copy, WlDataDeviceManager.DndAction.Copy);
        client.Display.Flush();
        harness.Pump();

        window.DragDrop(new global::Avalonia.Point(60, 40), RawDragEventType.DragOver, transfer, DragDropEffects.Copy);
        window.DragDrop(new global::Avalonia.Point(60, 40), RawDragEventType.Drop, transfer, DragDropEffects.Copy);
        harness.PumpUntil(() => dropped);

        int readFd, writeFd;
        unsafe
        {
            var fds = stackalloc int[2];
            Assert.Equal(0, pipe2(fds, 0));
            readFd = fds[0];
            writeFd = fds[1];
        }

        offer.Receive(mimes[0], writeFd);
        client.Display.Flush();
        close(writeFd);
        harness.Pump();

        var received = new MemoryStream();
        var buffer = new byte[256];
        for (var i = 0; i < 100; i++)
        {
            nint got;
            unsafe
            {
                fixed (byte* data = buffer)
                {
                    got = read(readFd, data, (nuint)buffer.Length);
                }
            }

            if (got <= 0)
            {
                break;
            }

            received.Write(buffer, 0, (int)got);
            if (received.Length >= 21)
            {
                break;
            }
        }

        close(readFd);
        Assert.Equal("dragged from the host", Encoding.UTF8.GetString(received.GetBuffer(), 0, (int)received.Length));

        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [AvaloniaFact]
    public void A_host_drag_that_leaves_cancels_cleanly()
    {
        var harness = new Harness();
        var client = harness.Client;
        _ = harness.MapToplevel();
        var window = harness.Manager.Windows.First();

        var device = client.DataDeviceManager!.GetDataDevice(client.Seat!);
        var entered = false;
        var left = false;
        var dropped = false;
        device.DataOffer += (_, e) => e.Id.Offer += (_, _) => { };
        device.Enter += (_, _) => entered = true;
        device.Leave += (_, _) => left = true;
        device.Drop += (_, _) => dropped = true;
        harness.Pump();

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText("never dropped"));
        window.DragDrop(new global::Avalonia.Point(40, 30), RawDragEventType.DragEnter, transfer, DragDropEffects.Copy);
        harness.PumpUntil(() => entered);

        window.DragDrop(new global::Avalonia.Point(40, 30), RawDragEventType.DragLeave, transfer, DragDropEffects.Copy);
        harness.PumpUntil(() => left);
        Assert.False(dropped);

        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [AvaloniaFact]
    public void A_client_source_destroyed_mid_drag_ends_the_drag()
    {
        var harness = new Harness();
        var client = harness.Client;
        var (surfaceProxy, _) = harness.MapToplevel();
        var window = harness.Manager.Windows.First();

        var pointerProxy = client.Seat!.GetPointer();
        uint pressSerial = 0;
        pointerProxy.Button += (_, e) =>
        {
            if (e.State == WlPointer.ButtonState.Pressed)
            {
                pressSerial = e.Serial;
            }
        };
        harness.Pump();
        window.MouseMove(new global::Avalonia.Point(50, 40));
        window.MouseDown(new global::Avalonia.Point(50, 40), MouseButton.Left);
        harness.PumpUntil(() => pressSerial != 0);

        var device = client.DataDeviceManager!.GetDataDevice(client.Seat);
        var source = client.DataDeviceManager.CreateDataSource();
        source.Offer("text/plain");
        source.SetActions(WlDataDeviceManager.DndAction.Copy);
        device.StartDrag(source, surfaceProxy, null, pressSerial);
        client.Display.Flush();
        harness.PumpUntil(() => harness.Drag.ClientDragActive);

        source.Destroy();
        client.Display.Flush();
        harness.PumpUntil(() => !harness.Drag.ClientDragActive);

        window.MouseUp(new global::Avalonia.Point(50, 40), MouseButton.Left);
        harness.Pump();
        pointerProxy.Release();
        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

}
