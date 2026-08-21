using System.Diagnostics;
using Basin.XWayland;
using Xcb.Native;
using Xunit;

namespace Basin.Tests;

public sealed class X11HotkeyGrabberTests
{
    private const uint KeysymF8 = 0xFFC5;

    private static bool HasXwayland { get; } =
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, "Xwayland")));

    [Fact]
    public void A_missing_display_degrades_to_null()
    {
        using var host = new CompositorTestHost();
        Assert.Null(X11HotkeyGrabber.TryConnect(host.Loop, ":63"));
    }

    [Fact]
    public void A_grabbed_chord_fires_on_a_synthetic_key()
    {
        Assert.SkipWhen(!HasXwayland, "Xwayland is not installed");

        using var host = new CompositorTestHost();
        using var server = new XWaylandServer(host.Display, host.Loop);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        XWaylandWm? wm = null;
        server.Ready += fd => wm = new XWaylandWm(fd, host.Loop, shell);
        var anchorConn = IntPtr.Zero;
        _ = Task.Run(() => Volatile.Write(ref anchorConn, ConnectRaw(server.DisplayName)), TestContext.Current.CancellationToken);
        PumpFor(host, () => Volatile.Read(ref anchorConn) != IntPtr.Zero, "Xwayland did not accept an X client");

        using var grabber = X11HotkeyGrabber.TryConnect(host.Loop, server.DisplayName);
        Assert.NotNull(grabber);

        var fired = 0;
        Assert.True(grabber.TryGrab(X11HotkeyModifiers.None, "f8", () => fired++));
        Assert.False(grabber.TryGrab(X11HotkeyModifiers.None, "no-such-key", () => { }));

        var clock = Stopwatch.StartNew();
        while (fired == 0 && clock.ElapsedMilliseconds < 10000)
        {
            PressF8(anchorConn);
            for (var i = 0; i < 30 && fired == 0; i++)
            {
                host.PumpToServer();
                Thread.Sleep(10);
            }
        }

        Assert.True(fired > 0, "the grabbed chord never fired");
        Disconnect(anchorConn);
        wm?.Dispose();
    }

    [Fact]
    public void An_owned_chord_is_refused_to_a_second_client()
    {
        Assert.SkipWhen(!HasXwayland, "Xwayland is not installed");

        using var host = new CompositorTestHost();
        using var server = new XWaylandServer(host.Display, host.Loop);
        using var shell = new XwaylandShellGlobal(host.Display, host.Compositor);
        XWaylandWm? wm = null;
        server.Ready += fd => wm = new XWaylandWm(fd, host.Loop, shell);
        var anchorConn = IntPtr.Zero;
        _ = Task.Run(() => Volatile.Write(ref anchorConn, ConnectRaw(server.DisplayName)), TestContext.Current.CancellationToken);
        PumpFor(host, () => Volatile.Read(ref anchorConn) != IntPtr.Zero, "Xwayland did not accept an X client");

        using var first = X11HotkeyGrabber.TryConnect(host.Loop, server.DisplayName);
        Assert.NotNull(first);
        Assert.True(first.TryGrab(X11HotkeyModifiers.Ctrl | X11HotkeyModifiers.Alt, "t", () => { }));

        using var second = X11HotkeyGrabber.TryConnect(host.Loop, server.DisplayName);
        Assert.NotNull(second);
        Assert.False(second.TryGrab(X11HotkeyModifiers.Ctrl | X11HotkeyModifiers.Alt, "t", () => { }));
        Disconnect(anchorConn);
        wm?.Dispose();
    }

    private static void PumpFor(CompositorTestHost host, Func<bool> condition, string complaint)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 10000)
        {
            host.PumpToServer();
            Thread.Sleep(10);
        }

        Assert.True(condition(), complaint);
    }

    private static unsafe IntPtr ConnectRaw(string display)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(display + "\0");
        fixed (byte* ptr = bytes)
        {
            var conn = Libxcb.xcb_connect((sbyte*)ptr, null);
            Assert.Equal(0, Libxcb.xcb_connection_has_error(conn));
            return (IntPtr)conn;
        }
    }

    private static unsafe void Disconnect(IntPtr conn) => Libxcb.xcb_disconnect((xcb_connection_t*)conn);

    private static unsafe void PressF8(IntPtr anchorConn)
    {
        var conn = (xcb_connection_t*)anchorConn;
        var keycode = KeycodeFor(conn, KeysymF8);
        Assert.NotEqual(0, keycode);
        _ = LibxcbXtest.xcb_test_fake_input(conn, 2, keycode, 0, 0, 0, 0, 0);
        _ = LibxcbXtest.xcb_test_fake_input(conn, 3, keycode, 0, 0, 0, 0, 0);
        _ = Libxcb.xcb_flush(conn);
    }

    private static unsafe byte KeycodeFor(xcb_connection_t* conn, uint keysym)
    {
        var setup = Libxcb.xcb_get_setup(conn);
        var min = setup->min_keycode;
        var count = (byte)(setup->max_keycode - setup->min_keycode + 1);
        var reply = Libxcb.xcb_get_keyboard_mapping_reply(
            conn, Libxcb.xcb_get_keyboard_mapping(conn, min, count), null);
        Assert.True(reply != null, "the keyboard mapping was not readable");
        var per = reply->keysyms_per_keycode;
        var length = Libxcb.xcb_get_keyboard_mapping_keysyms_length(reply);
        var keysyms = Libxcb.xcb_get_keyboard_mapping_keysyms(reply);
        byte found = 0;
        for (var i = 0; i < length && found == 0; i++)
        {
            if (keysyms[i] == keysym)
            {
                found = (byte)(min + i / per);
            }
        }

        Libc.Free(reply);
        return found;
    }
}
