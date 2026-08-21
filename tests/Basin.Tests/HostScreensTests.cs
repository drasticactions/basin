using System.Runtime.InteropServices;
using Basin.Avalonia;
using Basin.Diagnostics;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class HostScreensTests
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

    private static readonly HostScreenInfo Left = new("DP-1", "DP-1", 0, 0, 1920, 1080, 1.0, true);
    private static readonly HostScreenInfo Right = new("HDMI-1", "HDMI-1", 1920, 0, 3840, 2160, 1.5, false);

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            CompositorTestHost.SkipWithoutWaylandClient();
            BasinCounters.Reset();
            Host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "waylonia-tests" });
            Host.Screens.Apply([Left, Right]);

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
            Host.Dispose();
        }
    }

    [Fact]
    public void Two_screens_are_two_outputs_and_a_window_enters_and_leaves()
    {
        var harness = new Harness();
        Assert.Equal(2, harness.Client.Outputs.Count);

        var surface = harness.Client.Compositor.CreateSurface();
        var entered = new List<WlOutput>();
        var left = new List<WlOutput>();
        surface.Enter += (_, e) => entered.Add(e.Output!);
        surface.Leave += (_, e) => left.Add(e.Output!);

        var scaleEvents = new List<uint>();
        var fractional = harness.Client.FractionalScale!.GetFractionalScale(surface);
        fractional.PreferredScale += (_, e) => scaleEvents.Add(e.Scale);
        harness.Pump();

        harness.Host.Screens.EnterScreen(SurfaceOf(harness), "DP-1");
        harness.Pump();
        Assert.Single(entered);
        Assert.Empty(left);
        Assert.Contains(120u, scaleEvents);

        harness.Host.Screens.EnterScreen(SurfaceOf(harness), "HDMI-1");
        harness.Pump();
        Assert.Equal(2, entered.Count);
        Assert.Single(left);
        Assert.Contains(180u, scaleEvents);

        fractional.Destroy();
        surface.Destroy();
        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [Fact]
    public void A_removed_screen_retires_its_output_and_the_surface_leaves_first()
    {
        var harness = new Harness();
        var surface = harness.Client.Compositor.CreateSurface();
        var left = new List<WlOutput>();
        surface.Leave += (_, e) => left.Add(e.Output!);
        harness.Pump();

        harness.Host.Screens.EnterScreen(SurfaceOf(harness), "HDMI-1");
        harness.Pump();

        var removed = 0;
        harness.Client.Registry.GlobalRemove += (_, _) => removed++;
        harness.Host.Screens.Apply([Left]);
        harness.Pump();

        Assert.Single(left);
        Assert.Equal(1, removed);
        Assert.Single(harness.Host.Screens.Current);

        surface.Destroy();
        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [Fact]
    public void A_scale_change_is_a_reannounce_not_a_restart()
    {
        var harness = new Harness();
        var geometryEvents = 0;
        var done = 0;
        foreach (var output in harness.Client.Outputs)
        {
            output.Geometry += (_, _) => geometryEvents++;
            output.Done += (_, _) => done++;
        }

        harness.Pump();
        var removed = 0;
        harness.Client.Registry.GlobalRemove += (_, _) => removed++;

        harness.Host.Screens.Apply([Left, Right with { Scaling = 2.0 } ]);
        harness.Pump();
        Assert.Equal(0, removed);
        Assert.True(done > 0, "no wl_output.done after the scale change");

        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [Fact]
    public void A_window_observed_scale_readvertises_the_output_and_survives_apply()
    {
        var harness = new Harness();
        var scales = new List<int>();
        var modes = new List<(int Width, int Height)>();
        foreach (var output in harness.Client.Outputs)
        {
            output.Scale += (_, e) => scales.Add(e.Factor);
            output.ModeEvent += (_, e) => modes.Add((e.Width, e.Height));
        }

        harness.Pump();
        scales.Clear();
        modes.Clear();

        harness.Host.Screens.NoteWindowScale("DP-1", 1.5);
        harness.Pump();
        Assert.Contains(2, scales);
        Assert.Contains((2880, 1620), modes);
        Assert.Equal(1.5, harness.Host.Screens.ScalingOf("DP-1"));
        Assert.Equal(1.5, harness.Host.Screens.DefaultScaling);

        harness.Host.Screens.Apply([Left with { Primary = false }, Right]);
        harness.Pump();
        Assert.Equal(1.5, harness.Host.Screens.ScalingOf("DP-1"));

        var surface = harness.Client.Compositor.CreateSurface();
        var received = new List<uint>();
        var fractional = harness.Client.FractionalScale!.GetFractionalScale(surface);
        fractional.PreferredScale += (_, e) => received.Add(e.Scale);
        harness.Pump();

        harness.Host.Screens.EnterScreen(SurfaceOf(harness), "DP-1");
        harness.Pump();
        Assert.Contains(180u, received);

        fractional.Destroy();
        surface.Destroy();
        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    private static Surface SurfaceOf(Harness harness) =>
        System.Linq.Enumerable.First(harness.Host.Services.Require<CompositorGlobal>().Surfaces);
}
