using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Diagnostics;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class AvaloniaClipboardTests
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
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

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
            Clipboard = new HostClipboard(
                Host,
                () => Manager.Windows.Count > 0
                    ? global::Avalonia.Controls.TopLevel.GetTopLevel(Manager.Windows.First())?.Clipboard
                    : null,
                action => action());

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

        public HostClipboard Clipboard { get; }

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
                Thread.Sleep(10);
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
            Clipboard.Dispose();
            Client.Dispose();
            Host.Loop.Dispatch(0);
            Host.Loop.Dispatch(0);
            Dispatcher.UIThread.RunJobs();
            Manager.Dispose();
            Host.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static string? HostText(Harness harness)
    {
        var window = harness.Manager.Windows.FirstOrDefault();
        if (window is null)
        {
            return null;
        }

        var clipboard = global::Avalonia.Controls.TopLevel.GetTopLevel(window)?.Clipboard;
        return clipboard?.TryGetTextAsync().GetAwaiter().GetResult();
    }

    [AvaloniaFact]
    public void A_client_selection_lands_on_the_host_clipboard()
    {
        var harness = new Harness();
        var client = harness.Client;
        _ = MappedToplevelFor(harness);

        var device = client.DataDeviceManager!.GetDataDevice(client.Seat!);
        var source = client.DataDeviceManager.CreateDataSource();
        source.Offer("text/plain;charset=utf-8");
        source.Send += (_, e) =>
        {
            var payload = Encoding.UTF8.GetBytes("copied in the client");
            unsafe
            {
                fixed (byte* data = payload)
                {
                    _ = write(e.Fd, data, (nuint)payload.Length);
                }
            }

            close(e.Fd);
        };
        device.SetSelection(source, 0);
        harness.PumpUntil(() => HostText(harness) == "copied in the client");
        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [AvaloniaFact]
    public async Task The_host_clipboard_reaches_a_client_on_the_focus_push()
    {
        var harness = new Harness();
        var client = harness.Client;
        var window = MappedToplevelFor(harness);

        var hostWindow = harness.Manager.Windows.First();
        var clipboard = global::Avalonia.Controls.TopLevel.GetTopLevel(hostWindow)!.Clipboard!;
        await clipboard.SetTextAsync("typed on the host");

        var device = client.DataDeviceManager!.GetDataDevice(client.Seat!);
        WlDataOffer? offer = null;
        var mimes = new List<string>();
        device.DataOffer += (_, e) =>
        {
            offer = e.Id;
            offer.Offer += (_, o) => mimes.Add(o.MimeType);
        };
        var selectionSeen = false;
        device.Selection += (_, e) => selectionSeen = e.Id is not null;

        await harness.Clipboard.PushFromHostAsync();
        harness.Host.Seat.Keyboard.NotifyEnter(window);
        harness.PumpUntil(() => selectionSeen && offer is not null && mimes.Count > 0);

        int readFd, writeFd;
        unsafe
        {
            var fds = stackalloc int[2];
            Assert.Equal(0, pipe2(fds, 0));
            readFd = fds[0];
            writeFd = fds[1];
        }

        offer!.Receive("text/plain;charset=utf-8", writeFd);
        client.Display.Flush();
        close(writeFd);
        harness.Pump();

        var received = new MemoryStream();
        var buffer = new byte[256];
        for (var i = 0; i < 200; i++)
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
            if (Encoding.UTF8.GetString(received.GetBuffer(), 0, (int)received.Length) == "typed on the host")
            {
                break;
            }
        }

        close(readFd);
        Assert.Equal("typed on the host", Encoding.UTF8.GetString(received.GetBuffer(), 0, (int)received.Length));

        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [AvaloniaFact]
    public void A_source_that_closes_early_or_never_writes_stalls_nothing()
    {
        var harness = new Harness();
        harness.Clipboard.ReadTimeout = TimeSpan.FromMilliseconds(300);
        var client = harness.Client;
        _ = MappedToplevelFor(harness);

        var device = client.DataDeviceManager!.GetDataDevice(client.Seat!);
        var early = client.DataDeviceManager.CreateDataSource();
        early.Offer("text/plain;charset=utf-8");
        var earlyAsked = false;
        early.Send += (_, e) =>
        {
            earlyAsked = true;
            close(e.Fd);
        };
        device.SetSelection(early, 0);
        harness.PumpUntil(() => earlyAsked);

        var silent = client.DataDeviceManager.CreateDataSource();
        silent.Offer("text/plain;charset=utf-8");
        var silentAsked = false;
        var silentFd = -1;
        silent.Send += (_, e) =>
        {
            silentAsked = true;
            silentFd = e.Fd;
        };
        device.SetSelection(silent, 0);
        harness.PumpUntil(() => silentAsked);

        for (var i = 0; i < 10; i++)
        {
            harness.Pump();
        }

        if (silentFd >= 0)
        {
            close(silentFd);
        }

        Thread.Sleep(400);
        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    private static Surface MappedToplevelFor(Harness harness)
    {
        var client = harness.Client;
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        _ = xdgSurface.GetToplevel();
        uint serial = 0;
        xdgSurface.Configure += (_, e) => serial = e.Serial;
        surface.Commit();
        harness.PumpUntil(() => serial != 0);
        xdgSurface.AckConfigure(serial);
        var buffer = client.CreateBuffer(80, 60, Fill.Solid(80, 60, 0xFF336699));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 80, 60);
        surface.Commit();
        harness.PumpUntil(() => harness.Manager.Windows.Count == 1);
        return System.Linq.Enumerable.First(harness.Host.Services.Require<CompositorGlobal>().Surfaces);
    }
}
