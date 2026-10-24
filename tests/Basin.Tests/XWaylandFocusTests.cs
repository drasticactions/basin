using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Basin.XWayland;
using Wayland;
using Xcb.Native;
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
        public Rig(bool knowsClient = false)
        {
            Host = new CompositorTestHost();
            Shell = new XwaylandShellGlobal(Host.Display, Host.Compositor);
            Server = new XWaylandServer(Host.Display, Host.Loop);
            Server.Ready += fd =>
            {
                Wm = knowsClient
                    ? new XWaylandWm(fd, Host.Loop, Shell, Host.Seat, new XWaylandWmOptions(), Server.Client)
                    : new XWaylandWm(fd, Host.Loop, Shell, Host.Seat);
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

    private sealed unsafe class FocusProbe : IDisposable
    {
        private readonly xcb_connection_t* _conn;
        private readonly uint _root;
        private readonly uint _activeAtom;

        public FocusProbe(string display)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(display + "\0");
            fixed (byte* ptr = bytes)
            {
                _conn = Libxcb.xcb_connect((sbyte*)ptr, null);
            }

            Assert.Equal(0, Libxcb.xcb_connection_has_error(_conn));
            _root = Libxcb.xcb_setup_roots_iterator(Libxcb.xcb_get_setup(_conn)).data->root;
            var name = "_NET_ACTIVE_WINDOW"u8;
            fixed (byte* ptr = name)
            {
                var reply = Libxcb.xcb_intern_atom_reply(_conn, Libxcb.xcb_intern_atom(_conn, 0, (ushort)name.Length, (sbyte*)ptr), null);
                _activeAtom = reply->atom;
                NativeMemory.Free(reply);
            }
        }

        public uint InputFocus
        {
            get
            {
                var reply = Libxcb.xcb_get_input_focus_reply(_conn, Libxcb.xcb_get_input_focus(_conn), null);
                Assert.True(reply != null);
                var focus = reply->focus;
                NativeMemory.Free(reply);
                return focus;
            }
        }

        public uint ActiveWindow
        {
            get
            {
                var reply = Libxcb.xcb_get_property_reply(_conn, Libxcb.xcb_get_property(_conn, 0, _root, _activeAtom, 33, 0, 1), null);
                Assert.True(reply != null);
                var value = Libxcb.xcb_get_property_value_length(reply) >= 4 ? *(uint*)Libxcb.xcb_get_property_value(reply) : 0u;
                NativeMemory.Free(reply);
                return value;
            }
        }

        public void Dispose() => Libxcb.xcb_disconnect(_conn);
    }

    private const uint KeyA = 30;

    private static void Focus(Rig rig, XWaylandWindow window)
    {
        window.Activate();
        rig.Host.Seat.Keyboard.NotifyEnter(window.Surface);
    }

    private static void AssertXFocus(Rig rig, FocusProbe probe, uint window)
    {
        XWaylandTestSupport.Pump(rig.Host, () => probe.InputFocus == window && probe.ActiveWindow == window);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_native_window_taking_the_keyboard_moves_the_X_focus_to_none_after_its_enter(bool knowsClient)
    {
        XWaylandClassTests.SkipWithoutX11Focus();
        using var rig = new Rig(knowsClient);
        using var client = new FocusClient(rig.Server.DisplayName, "normal");
        var x = rig.WaitMapped(1);
        Assert.Equal(knowsClient, rig.Wm!.Client is not null);
        using var probe = new FocusProbe(rig.Server.DisplayName);
        var native = MappedToplevel.Map(rig.Host, rig.Host.Client);
        var keyboard = rig.Host.Client.Seat!.GetKeyboard();
        var enters = 0;
        keyboard.Enter += (_, e) =>
        {
            if (e.Surface == native.Surface)
            {
                enters++;
            }
        };
        rig.Host.PumpToClient();

        Focus(rig, x);
        AssertXFocus(rig, probe, x.WindowId);

        rig.Host.Seat.Keyboard.NotifyEnter(native.ServerSurface);
        Assert.Equal(0, rig.Wm!.FocusReleaseRuns);
        Assert.Equal(x.WindowId, probe.InputFocus);
        rig.Host.Display.FlushClients();
        rig.Host.Client.Display.Dispatch();
        Assert.Equal(1, enters);
        Assert.Equal(0, rig.Wm.FocusReleaseRuns);
        Assert.Equal(x.WindowId, probe.InputFocus);

        XWaylandTestSupport.Pump(rig.Host, () => rig.Wm.FocusReleaseRuns == 1);
        Assert.Equal(0u, probe.InputFocus);
        Assert.Equal(0u, probe.ActiveWindow);

        Focus(rig, x);
        AssertXFocus(rig, probe, x.WindowId);
        rig.Host.Seat.Keyboard.NotifyKey(1, KeyA, true);
        rig.Host.Seat.Keyboard.NotifyKey(2, KeyA, false);
        XWaylandTestSupport.Pump(rig.Host, () => client.Count($"KEY {KeyA + 8}") == 1);
        rig.PumpFor(100);
        Assert.Equal(1, rig.Wm.FocusReleaseRuns);
    }

    [Fact]
    public void Focus_that_returns_to_X_before_the_release_runs_stays_on_the_X_window()
    {
        XWaylandClassTests.SkipWithoutX11Focus();
        using var rig = new Rig();
        using var client = new FocusClient(rig.Server.DisplayName, "normal");
        var x = rig.WaitMapped(1);
        using var probe = new FocusProbe(rig.Server.DisplayName);
        var native = MappedToplevel.Map(rig.Host, rig.Host.Client);

        Focus(rig, x);
        AssertXFocus(rig, probe, x.WindowId);

        rig.Host.Seat.Keyboard.NotifyEnter(native.ServerSurface);
        Focus(rig, x);
        rig.PumpFor(200);
        Assert.Equal(0, rig.Wm!.FocusReleaseRuns);
        Assert.Equal(x.WindowId, probe.InputFocus);
        Assert.Equal(x.WindowId, probe.ActiveWindow);
    }

    [Fact]
    public void Focus_moving_between_two_X_windows_never_arms_the_release()
    {
        XWaylandClassTests.SkipWithoutX11Focus();
        using var rig = new Rig();
        using var first = new FocusClient(rig.Server.DisplayName, "normal");
        var a = rig.WaitMapped(1);
        using var second = new FocusClient(rig.Server.DisplayName, "normal");
        var b = rig.WaitMapped(2);
        using var probe = new FocusProbe(rig.Server.DisplayName);

        Focus(rig, a);
        AssertXFocus(rig, probe, a.WindowId);
        Focus(rig, b);
        AssertXFocus(rig, probe, b.WindowId);
        Focus(rig, a);
        AssertXFocus(rig, probe, a.WindowId);
        rig.PumpFor(200);
        Assert.Equal(0, rig.Wm!.FocusReleaseRuns);
        Assert.Equal(a.WindowId, probe.InputFocus);
    }

    [Fact]
    public void Clearing_the_keyboard_focus_moves_the_X_focus_to_none()
    {
        XWaylandClassTests.SkipWithoutX11Focus();
        using var rig = new Rig();
        using var client = new FocusClient(rig.Server.DisplayName, "normal");
        var x = rig.WaitMapped(1);
        using var probe = new FocusProbe(rig.Server.DisplayName);

        Focus(rig, x);
        AssertXFocus(rig, probe, x.WindowId);

        rig.Host.Seat.Keyboard.NotifyClearFocus();
        XWaylandTestSupport.Pump(rig.Host, () => rig.Wm!.FocusReleaseRuns == 1);
        Assert.Equal(0u, probe.InputFocus);
        Assert.Equal(0u, probe.ActiveWindow);
        rig.PumpFor(200);
        Assert.Equal(0u, probe.InputFocus);
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
