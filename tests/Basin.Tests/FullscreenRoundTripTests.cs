using Basin.Desktop;
using Basin.Shell.Xdg.Protocol;
using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class FullscreenRoundTripTests
{
    private const int OutputWidth = 640;
    private const int OutputHeight = 480;

    private static readonly Box Screen = new(0, 0, OutputWidth, OutputHeight);

    [Fact]
    public void Floating_a_fullscreen_xdg_window_keeps_the_fullscreen_size()
    {
        using var host = new CompositorTestHost(OutputWidth, OutputHeight);
        var client = XdgConfigures.Fullscreen(host);
        var roundTrip = new FullscreenRoundTrip(client.Window.ServerToplevel);

        Assert.Equal(FloatSize.Keep, roundTrip.Float(fullscreen: true, Screen));
        client.PumpUntilNot(host, fullscreen: true);

        Assert.True(roundTrip.RefullscreenOnTile);
        Assert.NotEmpty(client.Seen);
        Assert.All(client.Seen, seen => Assert.Equal((OutputWidth, OutputHeight), (seen.Width, seen.Height)));
    }

    [Fact]
    public void Tiling_a_floated_xdg_window_again_restores_fullscreen_with_no_windowed_size()
    {
        using var host = new CompositorTestHost(OutputWidth, OutputHeight);
        var client = XdgConfigures.Fullscreen(host);
        var roundTrip = new FullscreenRoundTrip(client.Window.ServerToplevel);
        roundTrip.Float(fullscreen: true, Screen);
        client.PumpUntilNot(host, fullscreen: true);

        Assert.True(roundTrip.Tile(fullscreen: false));
        client.Window.ServerToplevel.SetFullscreen(true, OutputWidth, OutputHeight);
        client.PumpUntilNot(host, fullscreen: false);

        Assert.False(roundTrip.RefullscreenOnTile);
        Assert.True(client.Seen[^1].Fullscreen);
        Assert.NotEmpty(client.Seen);
        Assert.All(client.Seen, seen => Assert.Equal((OutputWidth, OutputHeight), (seen.Width, seen.Height)));
    }

    [Fact]
    public void An_xdg_window_that_left_fullscreen_itself_tiles_as_a_normal_window()
    {
        using var host = new CompositorTestHost(OutputWidth, OutputHeight);
        var client = XdgConfigures.Fullscreen(host);
        var toplevel = client.Window.ServerToplevel;
        var roundTrip = new FullscreenRoundTrip(toplevel);
        toplevel.FullscreenRequested += fullscreen =>
        {
            if (!fullscreen)
            {
                roundTrip.ClientLeftFullscreen();
            }
        };
        roundTrip.Float(fullscreen: true, Screen);
        client.PumpUntilNot(host, fullscreen: true);

        client.Window.Toplevel.UnsetFullscreen();
        host.PumpUntil(() => toplevel.RequestedFullscreen == false);

        Assert.False(roundTrip.RefullscreenOnTile);
        Assert.False(roundTrip.Tile(fullscreen: false));
        Assert.Equal(FloatSize.Policy, roundTrip.Float(fullscreen: false, Screen));
    }

    [Fact]
    public void An_xdg_fullscreen_exit_carries_the_restored_size_in_its_first_configure()
    {
        using var host = new CompositorTestHost(OutputWidth, OutputHeight);
        var client = XdgConfigures.Fullscreen(host);
        var roundTrip = new FullscreenRoundTrip(client.Window.ServerToplevel);

        roundTrip.ExitFullscreen(new Box(20, 30, 300, 200));
        client.PumpUntilNot(host, fullscreen: true);

        var first = client.Seen.Find(seen => !seen.Fullscreen);
        Assert.Equal((300, 200), (first.Width, first.Height));
        Assert.NotEmpty(client.Seen);
        Assert.All(client.Seen, seen => Assert.Equal((300, 200), (seen.Width, seen.Height)));
    }

    [Fact]
    public void Floating_a_fullscreen_x_window_sends_no_intermediate_size()
    {
        using var rig = XRig.Start();
        var roundTrip = new FullscreenRoundTrip(rig.Window);

        Assert.Equal(FloatSize.Keep, roundTrip.Float(fullscreen: true, Screen));
        rig.Settle();

        Assert.False(rig.Window.IsFullscreen);
        Assert.NotEmpty(rig.Probe.Sizes);
        Assert.All(rig.Probe.Sizes, size => Assert.Equal((OutputWidth, OutputHeight), size));
    }

    [Fact]
    public void Tiling_a_floated_x_window_again_restores_fullscreen_with_no_intermediate_size()
    {
        using var rig = XRig.Start();
        var roundTrip = new FullscreenRoundTrip(rig.Window);
        roundTrip.Float(fullscreen: true, Screen);
        rig.Settle();

        Assert.True(roundTrip.Tile(rig.Window.IsFullscreen));
        rig.Window.SetFullscreen(true, 0, 0, OutputWidth, OutputHeight);
        rig.Settle();

        Assert.True(rig.Window.IsFullscreen);
        Assert.NotEmpty(rig.Probe.Sizes);
        Assert.All(rig.Probe.Sizes, size => Assert.Equal((OutputWidth, OutputHeight), size));
    }

    [Fact]
    public void An_x_fullscreen_exit_sends_only_the_restored_size()
    {
        using var rig = XRig.Start();
        var roundTrip = new FullscreenRoundTrip(rig.Window);

        roundTrip.ExitFullscreen(new Box(20, 30, 300, 200));
        rig.Settle();
        XWaylandTestSupport.Pump(rig.Host, () =>
        {
            rig.Probe.Drain();
            return rig.Probe.Sizes.Contains((300, 200));
        });

        Assert.False(rig.Window.IsFullscreen);
        Assert.All(rig.Probe.Sizes, size => Assert.Equal((300, 200), size));
    }

    private sealed class XdgConfigures
    {
        private XdgConfigures(MappedToplevel window) => Window = window;

        public MappedToplevel Window { get; }

        public List<(int Width, int Height, bool Fullscreen)> Seen { get; } = [];

        public static XdgConfigures Fullscreen(CompositorTestHost host)
        {
            var window = MappedToplevel.Map(host, host.Client);
            var result = new XdgConfigures(window);
            window.Toplevel.Configure += (_, e) =>
            {
                var fullscreen = false;
                for (var offset = 0; offset + 4 <= e.States.Length; offset += 4)
                {
                    fullscreen |= BitConverter.ToUInt32(e.States, offset) == (uint)XdgToplevel.State.Fullscreen;
                }

                result.Seen.Add((e.Width, e.Height, fullscreen));
            };

            window.ServerToplevel.SetFullscreen(true, OutputWidth, OutputHeight);
            result.PumpUntilNot(host, fullscreen: false);
            result.Seen.Clear();
            return result;
        }

        public void PumpUntilNot(CompositorTestHost host, bool fullscreen) =>
            host.PumpUntil(() => Seen.Count > 0 && Seen[^1].Fullscreen != fullscreen);
    }

    private sealed class XRig : IDisposable
    {
        private readonly XwaylandShellGlobal _shell;
        private readonly XWaylandServer _server;
        private readonly System.Diagnostics.Process _eyes;
        private XWaylandWm? _wm;

        private XRig(CompositorTestHost host)
        {
            Host = host;
            _shell = new XwaylandShellGlobal(host.Display, host.Compositor);
            _server = new XWaylandServer(host.Display, host.Loop);
            XWaylandWindow? mapped = null;
            _server.Ready += fd =>
            {
                _wm = new XWaylandWm(fd, host.Loop, _shell, host.Seat);
                _wm.WindowMapped += window => mapped ??= window;
            };

            _eyes = XWaylandTestSupport.Start("xeyes", _server.DisplayName, "-geometry", "150x100+0+0");
            XWaylandTestSupport.Pump(host, () => mapped?.Surface?.Current.Buffer is not null);
            Window = mapped!;
            Window.SetFullscreen(true, 0, 0, OutputWidth, OutputHeight);
            XWaylandTestSupport.Pump(host, () => Window.Surface!.Current.Width == OutputWidth);
            Probe = new X11ConfigureProbe(_server.DisplayName, Window.WindowId);
        }

        public CompositorTestHost Host { get; }

        public XWaylandWindow Window { get; }

        public X11ConfigureProbe Probe { get; }

        public static XRig Start()
        {
            Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes"), "Xwayland and xeyes are needed");
            return new XRig(new CompositorTestHost(OutputWidth, OutputHeight));
        }

        public void Settle()
        {
            for (var i = 0; i < 20; i++)
            {
                Host.Loop.Dispatch(10);
                Host.Display.FlushClients();
            }

            Probe.Drain();
        }

        public void Dispose()
        {
            Probe.Dispose();
            _eyes.Kill();
            _eyes.WaitForExit(2000);
            _eyes.Dispose();
            _wm?.Dispose();
            _server.Dispose();
            _shell.Dispose();
            Host.Dispose();
        }
    }
}
