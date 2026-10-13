using System.Collections.Concurrent;
using System.Diagnostics;
using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class XWaylandFocusTests
{
    private sealed class FocusClient : IDisposable
    {
        private readonly Process _process;

        public FocusClient(string display, string mode)
        {
            var info = new ProcessStartInfo(XWaylandClassTests.X11Focus, [mode, "20"])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            info.Environment["DISPLAY"] = display;
            info.Environment.Remove("WAYLAND_DISPLAY");
            _process = Process.Start(info)!;
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    Lines.Enqueue(line);
                }
            };
            _process.BeginOutputReadLine();
        }

        public ConcurrentQueue<string> Lines { get; } = new();

        public int Count(string prefix) => Lines.Count(line => line.StartsWith(prefix, StringComparison.Ordinal));

        public void Dispose()
        {
            try
            {
                _process.Kill();
            }
            catch (InvalidOperationException)
            {
            }

            _process.WaitForExit(2000);
            _process.Dispose();
        }
    }

    private sealed class Rig : IDisposable
    {
        public Rig()
        {
            Host = new CompositorTestHost();
            Shell = new XwaylandShellGlobal(Host.Display, Host.Compositor);
            Server = new XWaylandServer(Host.Display, Host.Loop);
            Server.Ready += fd =>
            {
                Wm = new XWaylandWm(fd, Host.Loop, Shell, Host.Seat);
                Wm.WindowMapped += Mapped.Add;
            };
        }

        public CompositorTestHost Host { get; }

        public XwaylandShellGlobal Shell { get; }

        public XWaylandServer Server { get; }

        public XWaylandWm? Wm { get; private set; }

        public List<XWaylandWindow> Mapped { get; } = [];

        public XWaylandWindow WaitMapped(int count)
        {
            XWaylandTestSupport.Pump(Host, () => Mapped.Count >= count);
            return Mapped[count - 1];
        }

        public void PumpFor(int millis)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < millis)
            {
                Host.Loop.Dispatch(10);
                Host.Display.FlushClients();
            }
        }

        public void Dispose()
        {
            Wm?.Dispose();
            Server.Dispose();
            Shell.Dispose();
            Host.Dispose();
        }
    }

    [Theory]
    [InlineData("tiny")]
    [InlineData("noinput")]
    [InlineData("disabled")]
    [InlineData("noactivate")]
    public void A_window_that_declines_focus_reports_it_and_activation_leaves_the_focus_alone(string mode)
    {
        XWaylandClassTests.SkipWithoutX11Focus();
        using var rig = new Rig();
        using var client = new FocusClient(rig.Server.DisplayName, mode);
        var window = rig.WaitMapped(1);
        XWaylandTestSupport.Pump(rig.Host, () => !window.WantsFocus);

        window.Activate();
        rig.PumpFor(300);
        Assert.Equal(0, client.Count("FOCUS in"));
        switch (mode)
        {
            case "tiny":
                Assert.True(window.IsUseless);
                break;
            case "noinput":
                Assert.False(window.AcceptsInput);
                Assert.False(window.TakesFocus);
                break;
            case "disabled":
                Assert.Equal(0x08000000u, window.HwndStyle & 0x08000000u);
                break;
            case "noactivate":
                Assert.True(window.SkipTaskbar && window.SkipPager);
                break;
        }
    }

    [Fact]
    public void An_ordinary_window_takes_focus_on_activation()
    {
        XWaylandClassTests.SkipWithoutX11Focus();
        using var rig = new Rig();
        using var client = new FocusClient(rig.Server.DisplayName, "normal");
        var window = rig.WaitMapped(1);
        Assert.True(window.WantsFocus);

        window.Activate();
        XWaylandTestSupport.Pump(rig.Host, () => client.Count("FOCUS in") > 0);
    }

    [Fact]
    public void Focus_taken_by_another_client_is_given_back()
    {
        XWaylandClassTests.SkipWithoutX11Focus();
        using var rig = new Rig();
        using var owner = new FocusClient(rig.Server.DisplayName, "normal");
        var focused = rig.WaitMapped(1);
        focused.Activate();
        XWaylandTestSupport.Pump(rig.Host, () => owner.Count("FOCUS in") == 1);

        using var thief = new FocusClient(rig.Server.DisplayName, "steal");
        _ = rig.WaitMapped(2);
        XWaylandTestSupport.Pump(rig.Host, () => thief.Count("STOLE") == 1);
        XWaylandTestSupport.Pump(rig.Host, () => owner.Count("FOCUS in") == 2);
        XWaylandTestSupport.Pump(rig.Host, () => thief.Count("FOCUS out") >= 1);
    }

    [Fact]
    public void A_move_request_from_the_client_reaches_the_window_as_an_event()
    {
        XWaylandClassTests.SkipWithoutX11Focus();
        using var rig = new Rig();
        var moves = 0;
        rig.Server.Ready += _ => rig.Wm!.WindowMapped += window => window.MoveRequested += _ => moves++;
        using var client = new FocusClient(rig.Server.DisplayName, "moveresizenow");
        _ = rig.WaitMapped(1);
        XWaylandTestSupport.Pump(rig.Host, () => moves == 1);
    }

    [Fact]
    public void An_override_redirect_window_parked_off_screen_at_one_pixel_is_a_stub()
    {
        XWaylandClassTests.SkipWithoutX11Focus();
        using var rig = new Rig();
        XWaylandWindow? stub = null;
        rig.Server.Ready += _ => rig.Wm!.OverrideRedirectMapped += window => stub ??= window;
        using var client = new FocusClient(rig.Server.DisplayName, "stub");
        XWaylandTestSupport.Pump(rig.Host, () => stub is not null);
        Assert.True(stub!.IsOffscreenStub);
    }

    [Fact]
    public void Static_gravity_with_a_program_position_is_a_fixed_popup()
    {
        XWaylandClassTests.SkipWithoutX11Focus();
        using var rig = new Rig();
        using var client = new FocusClient(rig.Server.DisplayName, "fixedpopup");
        var window = rig.WaitMapped(1);
        Assert.True(window.ProgramPosition);
        Assert.Equal(10u, window.Gravity);
        Assert.True(window.IsFixedPopup);
    }
}
