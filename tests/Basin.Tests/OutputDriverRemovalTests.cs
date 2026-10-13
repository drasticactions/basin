using System.Runtime.InteropServices;
using Basin.Host;
using Xunit;

namespace Basin.Tests;

public sealed class OutputDriverRemovalTests
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

    private static bool Readable(Wayland.WlDisplay display)
    {
        unsafe
        {
            var pollFd = new PollFd { Fd = display.Fd, Events = 1 };
            return poll(&pollFd, 1, 0) > 0 && (pollFd.REvents & 1) != 0;
        }
    }

    private static void Pump(BasinHost host, Wayland.WlDisplay display, Func<bool> until)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!until() && Environment.TickCount64 < deadline)
        {
            display.Flush();
            host.Loop.Dispatch(5);
            host.Display.FlushClients();
            while (Readable(display))
            {
                display.Dispatch();
            }

            display.DispatchPending();
        }
    }

    [Fact]
    public void A_removed_output_stays_until_the_client_acknowledges_it()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server, and the driver hosts a real display");
        CompositorTestHost.SkipWithoutWaylandClient();
        using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        var scene = new Scene.Scene();
        using var renderer = new Basin.Render.Pixman.PixmanRenderer();
        using var driver = new OutputDriver(host, scene, new OutputLayout(), renderer, null)
        {
            HeadlessMode = new OutputMode(320, 240, 60_000),
        };
        driver.CreateInitialOutputs();
        using var fixes = new FixesGlobal(host.Display);
        Assert.SkipWhen(!host.Display.SupportsFixes, "this transport does not service wl_fixes.ack_global_remove");
        var second = driver.AddView(host.Headless!.CreateOutput(new OutputMode(320, 240, 60_000)));

        int serverFd, clientFd;
        unsafe
        {
            var fds = stackalloc int[2];
            Assert.Equal(0, socketpair(AF_UNIX, SOCK_STREAM, 0, fds));
            serverFd = fds[0];
            clientFd = fds[1];
        }

        host.Display.CreateClient(serverFd);
        using var display = Wayland.WlDisplay.ConnectToFd(clientFd);
        var registry = display.GetRegistry();
        Wayland.WlFixes? proxy = null;
        var outputs = 0;
        var removed = new List<uint>();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wl_fixes")
            {
                proxy = registry.Bind<Wayland.WlFixes>(e.Name, FixesGlobal.Version);
            }
            else if (e.Interface == "wl_output")
            {
                outputs++;
            }
        };
        registry.GlobalRemove += (_, e) => removed.Add(e.Name);
        Pump(host, display, () => proxy is not null && outputs == 2);
        Assert.NotNull(proxy);

        var client = Assert.Single(host.Display.Clients);
        var name = second.Global.NameFor(client);
        driver.RemoveView(second);
        Pump(host, display, () => removed.Contains(name));
        Assert.Contains(name, removed);
        Assert.Equal(name, second.Global.NameFor(client));

        proxy!.AckGlobalRemove(registry, name);
        Pump(host, display, () => false);
        Assert.Throws<ObjectDisposedException>(() => second.Global.NameFor(client));

        proxy.Dispose();
        registry.Dispose();
        display.Flush();
        host.Loop.Dispatch(0);
    }
}
